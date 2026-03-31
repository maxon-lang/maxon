namespace MaxonSharp.Compiler;

public static class MaxonIgnore {
	/// <summary>
	/// Walks up parent directories looking for a .maxonignore flag file.
	/// Directories containing this file are excluded from compilation and LSP processing.
	/// </summary>
	public static bool IsIgnored(string filePath) {
		var dir = Path.GetDirectoryName(filePath);
		while (dir != null) {
			if (File.Exists(Path.Combine(dir, ".maxonignore")))
				return true;
			dir = Path.GetDirectoryName(dir);
		}
		return false;
	}
}
