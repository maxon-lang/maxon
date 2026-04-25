using System.Text;
using System.Text.RegularExpressions;

namespace MaxonSharp.Testing;

/// <summary>
/// Rewrites a fragment's source so it can be compiled alongside other fragments
/// in a single batch without name collisions on top-level declarations
/// (typealias, type, top-level let/var, main).
///
/// The rewriter renames each top-level declaration to a per-test mangled name
/// (e.g. `typealias Integer` in test `div-live-values` becomes
/// `typealias __b_div_live_values_Integer`) and rewrites every reference to that
/// name within the same fragment. Other fragments in the batch get their own
/// per-test prefix, so what was a duplicate `typealias Integer` collision
/// becomes two distinct typealiases.
///
/// String-literal handling is conservative: if a string in the fragment textually
/// contains any of the renamed names, the fragment is marked unbatchable and
/// falls back to the per-fragment path. This avoids miscompiling
/// `"my Integer is 5"` into `"my __b_<test>_Integer is 5"` (or worse, leaving
/// the string content untouched while renaming the declaration, which would
/// break interpolation lookups inside `"{Integer.max}"`).
/// </summary>
public static partial class BatchRewriter {

  /// <summary>
  /// The mangled-name scheme is shared between the rewriter (for in-fragment
  /// renames) and the dispatcher generator (for resolving each test's main).
  /// Centralized here so the two cannot drift.
  ///
  /// Single-underscore prefix only — double-underscore is reserved for
  /// compiler internals.
  /// </summary>
  public static string MangleSymbol(string testName, string originalName) {
    var sanitized = SanitizeTestName(testName);
    return $"_b_{sanitized}_{originalName}";
  }

  private static string SanitizeTestName(string testName) {
    var sb = new StringBuilder(testName.Length);
    foreach (var c in testName) {
      if (char.IsLetterOrDigit(c) || c == '_') {
        sb.Append(c);
      } else {
        sb.Append('_');
      }
    }
    return sb.ToString();
  }

  /// <summary>
  /// Result of attempting to rewrite a fragment for batching.
  /// </summary>
  public record RewriteResult(
    /// <summary>The rewritten source if Batchable, otherwise null.</summary>
    string? RewrittenSource,
    /// <summary>The mangled name of `main` for this test, used by the dispatcher. Null if not Batchable.</summary>
    string? MangledMainName,
    /// <summary>True if the fragment was rewritten successfully and can be batched.</summary>
    bool Batchable,
    /// <summary>Reason the fragment was rejected (string-literal collision, etc.). Empty when Batchable.</summary>
    string Reason
  );

  /// <summary>
  /// Attempt to rewrite `source` for batching under test name `testName`.
  /// Returns Batchable=false with a reason if the fragment cannot be safely
  /// renamed (string-literal collision); the caller should fall back to the
  /// per-fragment path in that case.
  /// </summary>
  public static RewriteResult Rewrite(string testName, string source) {
    // Step 1: Find top-level declarations.
    var renames = FindTopLevelRenames(testName, source);
    if (renames.Count == 0) {
      // No top-level declarations means nothing to rewrite — but every fragment
      // has `function main()`, so finding zero suggests a malformed fragment.
      return new RewriteResult(null, null, false, "no top-level declarations found (missing main?)");
    }

    if (!renames.TryGetValue("main", out string? value)) {
      return new RewriteResult(null, null, false, "no top-level `function main` found");
    }

    // Step 2: Conservative string-literal safety check. If any quoted string
    // body in the fragment textually contains any of the renamed names as a
    // word, bail out.
    var collision = FindStringLiteralCollision(source, renames.Keys);
    if (collision != null) {
      return new RewriteResult(null, null, false, $"renamed name '{collision}' appears inside a string literal");
    }

    // Step 3: Apply the rename map across the whole source with a single
    // alternation regex over the keys, anchored to word boundaries.
    var rewritten = ApplyRenames(source, renames);

    // Step 4: Add a debug header.
    var prefixed = $"// --- batched test: {testName} ---\n{rewritten}";

    return new RewriteResult(prefixed, value, true, "");
  }

  // --- internals ---

  private static Dictionary<string, string> FindTopLevelRenames(string testName, string source) {
    var renames = new Dictionary<string, string>();

    if (MainRegex().IsMatch(source)) {
      renames["main"] = MangleSymbol(testName, "main");
    }
    foreach (Match m in TypealiasRegex().Matches(source)) {
      var name = m.Groups[1].Value;
      renames[name] = MangleSymbol(testName, name);
    }
    foreach (Match m in TypeRegex().Matches(source)) {
      var name = m.Groups[1].Value;
      renames[name] = MangleSymbol(testName, name);
    }
    foreach (Match m in EnumRegex().Matches(source)) {
      var name = m.Groups[1].Value;
      renames[name] = MangleSymbol(testName, name);
    }
    foreach (Match m in UnionRegex().Matches(source)) {
      var name = m.Groups[1].Value;
      renames[name] = MangleSymbol(testName, name);
    }
    foreach (Match m in InterfaceRegex().Matches(source)) {
      var name = m.Groups[1].Value;
      renames[name] = MangleSymbol(testName, name);
    }
    foreach (Match m in TopLevelLetRegex().Matches(source)) {
      var name = m.Groups[1].Value;
      renames[name] = MangleSymbol(testName, name);
    }
    foreach (Match m in TopLevelVarRegex().Matches(source)) {
      var name = m.Groups[1].Value;
      renames[name] = MangleSymbol(testName, name);
    }
    // Free top-level functions. Skip `main` (handled above) and any qualified
    // method declaration of the form `function <Type>.<method>(...)` — the
    // type-qualified part already gets prefixed via the type rename.
    foreach (Match m in FreeFunctionRegex().Matches(source)) {
      var name = m.Groups[1].Value;
      if (name == "main") continue;
      renames[name] = MangleSymbol(testName, name);
    }

    return renames;
  }

  /// <summary>
  /// Finds the first renamed name that appears as a word inside any string
  /// literal in the source. Returns null if no collision.
  /// </summary>
  private static string? FindStringLiteralCollision(string source, IEnumerable<string> names) {
    var nameSet = new HashSet<string>(names);
    if (nameSet.Count == 0) return null;

    foreach (Match m in StringLiteralRegex().Matches(source)) {
      var body = m.Groups[1].Value;
      // Split the string body on non-word boundaries; any word that matches a
      // renamed name is a potential collision (could be inside an interpolation
      // expression, where it would refer to the renamed declaration; or just
      // text, where leaving it unrewritten is silently lossy).
      foreach (var word in WordSplitRegex().Split(body)) {
        if (word.Length > 0 && nameSet.Contains(word)) {
          return word;
        }
      }
    }
    return null;
  }

  private static string ApplyRenames(string source, Dictionary<string, string> renames) {
    if (renames.Count == 0) return source;
    // Build a single alternation regex over the keys. Sort by length descending
    // so longer names match first (defensive; word boundaries make this rarely
    // matter in practice).
    var sortedKeys = renames.Keys.OrderByDescending(k => k.Length);
    var pattern = @"\b(" + string.Join("|", sortedKeys.Select(Regex.Escape)) + @")\b";
    var combined = new Regex(pattern, RegexOptions.Compiled);
    return combined.Replace(source, m => renames[m.Groups[1].Value]);
  }

  // Regex patterns. All are anchored at start-of-line via Multiline so they
  // match top-level declarations only (Maxon indents non-top-level code with
  // tabs, so any line beginning at column 0 is by definition top-level).
  // `(?!//)` is unnecessary because `//` doesn't start with a keyword; the
  // patterns are keyword-anchored already.

  [GeneratedRegex(@"^function\s+main\s*\(", RegexOptions.Multiline | RegexOptions.Compiled)]
  private static partial Regex MainRegex();

  [GeneratedRegex(@"^(?:export\s+)?typealias\s+(\w+)\s*=", RegexOptions.Multiline | RegexOptions.Compiled)]
  private static partial Regex TypealiasRegex();

  [GeneratedRegex(@"^(?:export\s+)?type\s+(\w+)", RegexOptions.Multiline | RegexOptions.Compiled)]
  private static partial Regex TypeRegex();

  [GeneratedRegex(@"^(?:export\s+)?enum\s+(\w+)", RegexOptions.Multiline | RegexOptions.Compiled)]
  private static partial Regex EnumRegex();

  [GeneratedRegex(@"^(?:export\s+)?union\s+(\w+)", RegexOptions.Multiline | RegexOptions.Compiled)]
  private static partial Regex UnionRegex();

  [GeneratedRegex(@"^(?:export\s+)?interface\s+(\w+)", RegexOptions.Multiline | RegexOptions.Compiled)]
  private static partial Regex InterfaceRegex();

  [GeneratedRegex(@"^(?:export\s+)?let\s+(\w+)\b", RegexOptions.Multiline | RegexOptions.Compiled)]
  private static partial Regex TopLevelLetRegex();

  [GeneratedRegex(@"^(?:export\s+)?var\s+(\w+)\b", RegexOptions.Multiline | RegexOptions.Compiled)]
  private static partial Regex TopLevelVarRegex();

  // Free top-level function: `function <name>(...)` at column 0. Excludes
  // method declarations like `function Type.method(...)` which the parser
  // separates into a different namespace (Type-qualified) and which our type
  // renames already cover at the type-name component.
  [GeneratedRegex(@"^(?:export\s+)?function\s+(\w+)\s*\(", RegexOptions.Multiline | RegexOptions.Compiled)]
  private static partial Regex FreeFunctionRegex();

  // Single-line string literals. Matches a double-quoted body that may contain
  // escaped characters via `\.`. Maxon does not have triple-quoted strings.
  [GeneratedRegex("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled)]
  private static partial Regex StringLiteralRegex();

  [GeneratedRegex(@"\W+", RegexOptions.Compiled)]
  private static partial Regex WordSplitRegex();
}
