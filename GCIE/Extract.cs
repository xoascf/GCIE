/* This is a C# port of Spodi's Extract_OoT.ps1, licensed under the MIT License. */

using System.Text;

namespace GCIE;

public enum FSTType {
	File = 0,
	Directory = 1
}

public class FSTEntryBase {
	public uint FileOffset { get; set; }
	public uint Size { get; set; }
	public string? Name { get; set; }
	public string? FullName { get; set; }

	public FSTEntryBase() {
	}

	public FSTEntryBase(FSTEntryBase source) {
		FileOffset = source.FileOffset;
		Size = source.Size;
		Name = source.Name;
		FullName = source.FullName;
	}
}

public class FSTEntry : FSTEntryBase {
	public int Pos { get; set; }
	public string? ParentFile { get; set; }
	public FSTType Type { get; set; }
	public string? Path { get; set; }
	public uint NameOffset { get; set; }
	public int ParentDirPos { get; set; }
	//public uint NextDirPos { get; set; }	// FIXME: UNUSED?
}

public class Extract {

	/// <summary>
	/// Makes a new file out of another by copying only a specific area.
	/// </summary>
	/// <param name="stream">The source <see cref="Stream" /> to get the data from.</param>
	/// <param name="fileOut">Name and path of the new file. Won't overwrite existing files.</param>
	/// <param name="start">Starting position of the area to copy in bytes (from start of the file).</param>
	/// <param name="size">Size of the area to copy in bytes (bytes from starting position).</param>
	private static void SplitFile(Stream stream, string fileOut, int start, int size) {
		if (File.Exists(fileOut))
			throw new IOException($"{fileOut} already exists.");

		using FileStream writeStream = new(fileOut, FileMode.CreateNew, FileAccess.Write);
		byte[] buffer = new byte[131072];
		int bytesToRead = size;

		stream.Seek(start, SeekOrigin.Begin);

		while (bytesToRead > 0) {
			int bytesRead = stream.Read(buffer, 0, Math.Min(bytesToRead, buffer.Length));
			writeStream.Write(buffer, 0, bytesRead);

			if (bytesRead == 0)
				break;

			bytesToRead -= bytesRead;
		}
	}

	private static uint ConvertByteArrayToUInt32(byte[] value) {
		if (BitConverter.IsLittleEndian)
			Array.Reverse(value);

		Array.Resize(ref value, 4);

		return BitConverter.ToUInt32(value, 0);
	}

	public static List<FSTEntry> ReadGCFST(Stream stream, uint fstStart, int offsetShift = 0, string parentFile = null!) {
		List<FSTEntry> entries = [];
		byte[] fstEntry = new byte[0x0C];

		stream.Seek(fstStart, SeekOrigin.Begin);
		stream.Read(fstEntry, 0, 0x0C);

		uint EntryCount = ConvertByteArrayToUInt32(fstEntry.Skip(8).Take(4).ToArray());

		int lastFolder = 0;

		for (int i = 1; i < EntryCount; i++) {
			stream.Read(fstEntry, 0, 0x0C);
			if ((FSTType)fstEntry[0] == FSTType.Directory) {
				lastFolder = i;
				FSTEntry directoryEntry = new() {
					Pos = i,
					ParentFile = parentFile,
					Type = (FSTType)fstEntry[0],
					Path = null,
					Name = null,
					NameOffset = ConvertByteArrayToUInt32(fstEntry.Skip(1).Take(3).ToArray()),
					ParentDirPos = (int)ConvertByteArrayToUInt32(fstEntry.Skip(4).Take(4).ToArray()),
					//NextDirPos = ConvertByteArrayToUInt32(fstEntry.Skip(8).Take(4).ToArray()),
					FullName = null
				};
				entries.Add(directoryEntry);
			}
			else {
				FSTEntry fileEntry = new() {
					Pos = i,
					ParentFile = parentFile,
					Type = (FSTType)fstEntry[0],
					Path = null,
					Name = null,
					NameOffset = ConvertByteArrayToUInt32(fstEntry.Skip(1).Take(3).ToArray()),
					FileOffset = (uint)(ConvertByteArrayToUInt32(fstEntry.Skip(4).Take(4).ToArray()) + offsetShift),
					Size = ConvertByteArrayToUInt32(fstEntry.Skip(8).Take(4).ToArray()),
					ParentDirPos = lastFolder,
					FullName = null
				};
				entries.Add(fileEntry);
			}
		}

		long stringTablePos = stream.Position;

		// 1st Pass: Getting the names from the String-Table
		foreach (FSTEntry entry in entries) {
			List<byte> name = [];
			byte b;
			stream.Seek(stringTablePos + entry.NameOffset, SeekOrigin.Begin);
			while ((b = (byte)stream.ReadByte()) != 0)
				name.Add(b);

			entry.Name = Encoding.ASCII.GetString(name.ToArray());
		}

		// 2nd Pass: Add Paths
		foreach (IGrouping<int, FSTEntry> group in entries.GroupBy(x => x.ParentDirPos)) {
			List<string> path = ["/"];
			FSTEntry parent = group.First();
			while (parent.ParentDirPos != 0) {
				parent = entries[parent.ParentDirPos - 1];
				path.Insert(0, parent.Name!);
				path.Insert(0, "/");
			}
			foreach (FSTEntry? entry in group)
				entry.Path = string.Join("", path);
		}

		// 3rd Pass: Add FullName
		foreach (FSTEntry entry in entries) {
			entry.FullName = entry.ParentFile + entry.Path + entry.Name;
			if (entry.Type == FSTType.Directory)
				entry.FullName += "/";
		}

		return entries;
	}

	private static List<FSTEntry> ReadTGC(Stream stream, uint fileOffset, string fullName) {
		List<FSTEntry> tgcEntries = [];
		byte[] buffer = new byte[4];

		stream.Seek(fileOffset, SeekOrigin.Begin);
		stream.Read(buffer, 0, 4);

		byte[] tgcMagic = [0xae, 0x0f, 0x38, 0xa2];

		if (buffer.SequenceEqual(tgcMagic)) {
			stream.Seek(fileOffset + 0x0010, SeekOrigin.Begin);
			stream.Read(buffer, 0, 4);
			uint fstStart = ConvertByteArrayToUInt32(buffer) + fileOffset;

			stream.Seek(0x4 * 4, SeekOrigin.Current);
			stream.Read(buffer, 0, 4);
			uint fileArea = ConvertByteArrayToUInt32(buffer);

			stream.Seek(0x4 * 3, SeekOrigin.Current);
			stream.Read(buffer, 0, 4);
			uint virtualFileArea = ConvertByteArrayToUInt32(buffer);

			int offsetShift = (int)((fileArea - virtualFileArea) + fileOffset);

			tgcEntries = ReadGCFST(stream, fstStart, offsetShift, fullName);
		}

		return tgcEntries;
	}

	public static void Main(Stream stream, string? extract, string? listFiles) {
		byte[] buffer = new byte[0x04];

		stream.Seek(0x0424, SeekOrigin.Begin);
		stream.Read(buffer, 0, 0x4);
		uint FSTStart = ConvertByteArrayToUInt32(buffer);

		List<FSTEntry> fstEntries = ReadGCFST(stream, FSTStart);

		List<FSTEntry> fileList = [.. fstEntries.Where(entry => entry.Type == FSTType.File).SelectMany(
		entry => {
			if (entry.Name!.EndsWith(".tgc"))
				return ReadTGC(stream, entry.FileOffset, entry.FullName!).Where(tgcEntry => tgcEntry.Type == FSTType.File).Append(entry);
			else
				return (IEnumerable<FSTEntry>)(new[] { entry });
		}),
		];

		if (!string.IsNullOrEmpty(listFiles)) {
			List<FSTEntryBase> listBase = [.. fileList.Select(list => new FSTEntryBase(list)).OrderBy(entry => entry.FileOffset)];
			switch (listFiles.ToLower()) {
				case "json":
					PrintJsonList(listBase);
					break;

				case "text":
					PrintTextList(listBase);
					break;
			}
		}
		else if (!string.IsNullOrEmpty(extract)) {
			List<FSTEntry> filteredList = fileList.Where(entry => entry.FullName!.Contains(extract)).ToList();

			if (filteredList.Count > 0)
				foreach (FSTEntry entry in filteredList)
					SplitFile(stream, Path.Combine(Environment.CurrentDirectory, entry.Name!), (int)entry.FileOffset, (int)entry.Size);
			else
				Console.WriteLine($"Couldn't find any file or path matching the regular expression '{extract}'.");
		}
		else {
			List<FSTEntry> filteredList = fileList.Where(entry => entry.Name == "zlp_f.n64" || entry.Name == "urazlp_f.n64").ToList();

			if (filteredList.Count > 0)
				foreach (FSTEntry entry in filteredList) {
					string outputPath = (entry.Name == "zlp_f.n64") ? "TLoZ-OoT-GC.z64" : "TLoZ-OoT-MQ-GC.z64";
					SplitFile(stream, Path.Combine(Environment.CurrentDirectory, outputPath), (int)entry.FileOffset, (int)entry.Size);
				}
			else
				Console.WriteLine("Couldn't find any PAL OoT or MQ ROM.");
		}
	}

	private static void PrintJsonList(List<FSTEntryBase> fileList) {
		string jsonOutput = Newtonsoft.Json.JsonConvert.SerializeObject(fileList, Newtonsoft.Json.Formatting.Indented);
		File.WriteAllText("FileList.json", jsonOutput);
	}

	private static void PrintTextList(List<FSTEntryBase> fileList) {
		int maxSizeLength = fileList.Max(entry => entry.Size.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
		int maxNameLength = fileList.Max(entry => entry.Name!.Length);

		using StreamWriter writer = new("FileList.txt");
		writer.WriteLine("{0,10} {1," + maxSizeLength + "} {2,-" + maxNameLength + "} {3}", "FileOffset", "Size", "Name", "FullName");
		writer.WriteLine("{0,-10} {1," + maxSizeLength + "} {2,-" + maxNameLength + "} {3}", new string('-', "FileOffset".Length), new string('-', "Size".Length), new string('-', "Name".Length), new string('-', "FullName".Length));
		foreach (FSTEntryBase entry in fileList)
			writer.WriteLine("{0,10} {1," + maxSizeLength + "} {2,-" + maxNameLength + "} {3}", entry.FileOffset, entry.Size, entry.Name, entry.FullName);
	}
}