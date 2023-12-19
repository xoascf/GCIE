using GCIE;

static void TrimPath(ref string s) {
	s = s.Trim();
	if (s.StartsWith('\"') && s.EndsWith('\"'))
		s = s[1..^1];
}

static bool FileBeingInvalid(ref string? filePath) {
	if (string.IsNullOrEmpty(filePath))
		return true;

	TrimPath(ref filePath);

	return !File.Exists(filePath);
}

static Dictionary<string, string> ParseArguments(string[] args) {
	Dictionary<string, string> argDictionary = new(StringComparer.OrdinalIgnoreCase);

	for (int i = 0; i < args.Length; i++) {
		if (args[i].StartsWith("--") || args[i].StartsWith('-')) {
			string key = args[i].TrimStart('-');
			string? value = i + 1 < args.Length && !args[i + 1].StartsWith('-') ? (string?)args[i + 1] : null;
			if (value != null) {
				argDictionary[key] = value;
				i++; // Skip the next element as it is the value associated with the key.
			}
		}
		else
			// Treat a single argument without a key as the ISO file path.
			argDictionary["fileIn"] = args[i];
	}

	return argDictionary;
}

Dictionary<string, string> argDictionary = ParseArguments(args);

argDictionary.TryGetValue("fileIn", out string? isoPath);
argDictionary.TryGetValue("Extract", out string? fileToExtract);
argDictionary.TryGetValue("ListFiles", out string? listFormat);

if (FileBeingInvalid(ref isoPath)) {
	do {
		Console.WriteLine("Please enter the file path of your GC ISO:");
		isoPath = Console.ReadLine();
	} while (FileBeingInvalid(ref isoPath));
}

using FileStream isoStream = File.OpenRead(isoPath!);

Extract.Main(isoStream, fileToExtract, listFormat);