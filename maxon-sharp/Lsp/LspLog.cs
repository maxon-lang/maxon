namespace MaxonSharp.Lsp;

/// <summary>
/// Writes log messages to stderr so the OmniSharp LSP client surfaces them in
/// the "Maxon Language Server" output channel. stdout is reserved for the LSP
/// protocol stream and must not be written to.
/// </summary>
internal static class LspLog {
  public static void Info(string message) {
    Console.Error.WriteLine($"[maxon-lsp] {message}");
  }
}
