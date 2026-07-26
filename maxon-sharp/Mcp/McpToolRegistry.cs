using MaxonSharp.Mcp.Tools;

namespace MaxonSharp.Mcp;

/// <summary>
/// Every tool this server hosts, in ONE list, contributed BY FAMILY.
///
/// Debug was the first family and coverage is the second; refactoring and building follow. A family adds a ROW to
/// <see cref="Families"/> and its own rows to its own file — not a case to a dispatch ladder here, and
/// not a second registration site. `tools/list` and `tools/call` both read this one list, so a tool
/// cannot be half-registered: advertised but unroutable, or routable but invisible.
/// </summary>
internal static class McpToolRegistry {

  /// <summary>
  /// The families, each owning its own tools. A refactoring family has a ready reuse path already in the
  /// tree — `Lsp/RenameHandler`, `PrepareRenameHandler` and `CodeActionHandler` are front-ends over the
  /// same compiler — so when it lands it is another surface over one brain, appended here.
  /// </summary>
  private static IReadOnlyList<IReadOnlyList<McpTool>> Families =>
    [DebugTools.Tools, CoverageTools.Tools, ProfileTools.Tools];

  public static IReadOnlyList<McpTool> All { get; } = [.. Families.SelectMany(family => family)];

  /// The tool a `tools/call` named, or null. One scan of the one list.
  public static McpTool? Find(string name) {
    foreach (var tool in All)
      if (tool.Name == name) return tool;
    return null;
  }
}
