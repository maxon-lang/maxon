using System.Text.Json;

namespace MaxonSharp.Mcp.Tools;

/// <summary>
/// The `source` argument shape every family that BUILDS a program shares: a `.maxon` file that
/// already exists, or a snippet written into the session's own scratch directory.
///
/// It lives here rather than in one family because two families now take it (debug and coverage) and
/// the schema, the vocabulary of kinds, the snippet-name rule and the write-only-on-change trick are
/// one fact each. A second copy would not fail to compile; it would drift — and the first thing to
/// drift would be the cache behaviour below, which is invisible until someone measures it.
/// </summary>
internal static class McpSource {
  private const string KindFile = "file";
  private const string KindSnippet = "snippet";
  private static readonly string[] Kinds = [KindFile, KindSnippet];

  /// The file name a snippet is written under before it is built. It shows up in every stop event's
  /// `file` field and in every coverage listing's header, so it is a name that reads as what it is.
  private const string DefaultSnippetName = "snippet.maxon";

  /// The `source` object's schema, so both families advertise exactly the argument this class parses.
  public static McpToolArg Arg(string purpose) =>
    new("source", McpSchema.Object(
      $"The program to {purpose}: either a .maxon file that already exists, or a snippet to write and "
      + "build in a scratch directory that is deleted when the session ends.", [
        new("kind", McpSchema.StringEnum($"'{KindFile}' to build `path`; '{KindSnippet}' to build `text`.", Kinds),
          Required: true),
        new("path", McpSchema.String(
          "Path to a single .maxon file, relative to the server's working directory or absolute. "
          + "A DIRECTORY is refused: build a project with `maxon build <dir>` first."), Required: false),
        new("text", McpSchema.String("The complete Maxon source of a program with a main()."), Required: false),
        new("name", McpSchema.String(
          $"File name to give a snippet (default {DefaultSnippetName})."), Required: false),
      ]), Required: true);

  /// <summary>
  /// The `source` argument as a path on disk: the caller's own file, or a snippet written into the
  /// session's scratch directory — which the SESSION owns and reaps, so a snippet that fails to
  /// compile (and therefore never produces a session) still leaves nothing behind.
  /// </summary>
  public static string Resolve(McpSession session, JsonElement args) {
    var source = McpArgs.RequireObject(args, "source");
    if (McpArgs.RequireChoice(source, "kind", Kinds) == KindFile)
      return McpArgs.RequireString(source, "path");

    var text = McpArgs.RequireString(source, "text");
    var name = McpArgs.OptionalString(source, "name") ?? DefaultSnippetName;
    if (name.Length == 0 || Path.GetFileName(name) != name)
      throw new McpInvalidParamsException(
        $"`source.name` must be a bare file name, got '{name}' — a snippet is written into a scratch "
        + "directory this server owns, not to a path of the caller's choosing.");

    var path = Path.Combine(session.Workspace(), name);
    WriteSnippetIfChanged(path, text);
    return path;
  }

  /// <summary>
  /// Write a snippet only when its text actually DIFFERS from what is already on disk.
  ///
  /// An unconditional write is what stops a snippet from ever hitting the build cache: the cache keys
  /// a source on its last-write time, so rewriting identical bytes moves the timestamp and forces a
  /// full recompile of a program that did not change (measured ~320 ms against ~27 ms).
  /// </summary>
  private static void WriteSnippetIfChanged(string path, string text) {
    if (File.Exists(path) && File.ReadAllText(path) == text) return;
    File.WriteAllText(path, text);
  }
}
