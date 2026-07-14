using System.Text;

namespace MaxonSharp.Compiler;

/// <summary>
/// Reader, validator and code generator for <c>docs/error-codes.txt</c> — the single
/// source of truth for the 4-digit error-code space shared by all three compilers.
///
/// The number space used to be written down three times (ErrorCode.cs,
/// maxon-selfhosted/Compiler/ErrorCode.maxon, maxon-shv2/Compiler/Diagnostics.maxon),
/// which is how two agents took E3099 on the same day: each had correctly grepped a
/// different copy. Now the registry is authored once and the three enums are
/// generated from it, so a duplicate number cannot be written — <see cref="Load"/>
/// refuses the file, naming both claimants, and that refusal fails the build.
/// </summary>
public static class ErrorCodeRegistry {
  public const string RegistryRelativePath = "docs/error-codes.txt";

  /// <summary>The three generated artifacts, each keyed by the registry column that feeds it.</summary>
  public static readonly (Compiler Compiler, string RelativePath)[] GeneratedFiles = [
    (Compiler.Csharp, "maxon-sharp/Compiler/ErrorCode.g.cs"),
    (Compiler.Selfhosted, "maxon-selfhosted/Compiler/ErrorCodeRegistry.maxon"),
    (Compiler.Shv2, "maxon-shv2/Compiler/ErrorCodeRegistry.maxon"),
  ];

  public enum Compiler { Csharp, Selfhosted, Shv2 }

  /// <summary>The registry key that declares each compiler's spelling of a code.</summary>
  public static string KeyFor(Compiler c) => c switch {
    Compiler.Csharp => "csharp",
    Compiler.Selfhosted => "selfhosted",
    Compiler.Shv2 => "shv2",
    _ => throw new ArgumentOutOfRangeException(nameof(c)),
  };

  /// <summary>
  /// One code. <see cref="Reserved"/> is non-null exactly when no compiler declares the
  /// number yet — an in-flight claim from an unmerged branch, or a retired meaning. A
  /// reserved code still OCCUPIES the number space, which is the whole point: a
  /// reservation that lives in a comment is not a reservation, it is a rumour.
  /// </summary>
  public sealed record Entry(
    int Number,
    string CanonicalName,
    IReadOnlyDictionary<Compiler, string> Names,
    string? Reserved,
    IReadOnlyList<string> Doc,
    int LineNumber) {
    /// <summary>Derived from the leading digit — never written down, so it cannot disagree.</summary>
    public string Stage => (Number / 1000) switch {
      1 => "lexer",
      2 => "parser",
      3 => "semantic",
      4 => "ir",
      5 => "codegen",
      6 => "pewriter",
      9 => "internal",
      _ => throw new ErrorCodeRegistryException(
        $"E{Number:D4} ({CanonicalName}): {Number / 1000}xxx is not a known stage band"),
    };
  }

  // ===========================================================================
  // Parsing
  // ===========================================================================

  /// <summary>
  /// Parse and validate the registry. Throws <see cref="ErrorCodeRegistryException"/> —
  /// naming both claimants — on any duplicate. Callers let it escape: a bad registry is
  /// a build failure, not a diagnostic to collect.
  /// </summary>
  public static List<Entry> Load(string registryPath) {
    if (!File.Exists(registryPath)) {
      throw new ErrorCodeRegistryException($"registry not found: {registryPath}");
    }

    var text = File.ReadAllText(registryPath);
    var entries = new List<Entry>();
    Pending? pending = null;

    var lines = text.Split('\n');
    for (var i = 0; i < lines.Length; i++) {
      var raw = lines[i].TrimEnd('\r');
      var lineNo = i + 1;

      if (raw.Length == 0 || raw.TrimStart().StartsWith('#')) continue;

      if (!char.IsWhiteSpace(raw[0])) {
        // `E<nnnn> <CanonicalName>` — a new entry.
        if (pending is not null) entries.Add(pending.Build());
        var parts = raw.Split(' ', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts[0].Length != 5 || parts[0][0] != 'E'
            || !int.TryParse(parts[0][1..], out var number) || number < 1000 || number > 9999) {
          throw new ErrorCodeRegistryException(
            $"{registryPath}:{lineNo}: expected `E<nnnn> <CanonicalName>`, got: {raw}");
        }
        pending = new Pending(number, parts[1], lineNo);
        continue;
      }

      if (pending is null) {
        throw new ErrorCodeRegistryException(
          $"{registryPath}:{lineNo}: key line before any `E<nnnn>` entry: {raw}");
      }

      var kv = raw.TrimStart().Split(' ', 2, StringSplitOptions.TrimEntries);
      var key = kv[0];
      var value = kv.Length > 1 ? kv[1] : "";
      if (value.Length == 0) {
        throw new ErrorCodeRegistryException($"{registryPath}:{lineNo}: key `{key}` has no value");
      }

      switch (key) {
        case "csharp":
        case "selfhosted":
        case "shv2":
          var compiler = key switch {
            "csharp" => Compiler.Csharp,
            "selfhosted" => Compiler.Selfhosted,
            _ => Compiler.Shv2,
          };
          if (pending.Names.ContainsKey(compiler)) {
            throw new ErrorCodeRegistryException(
              $"{registryPath}:{lineNo}: E{pending.Number:D4} declares `{key}` twice");
          }
          pending.Names[compiler] = value;
          break;
        case "reserved":
          pending.Reserved = value;
          break;
        case "doc":
          pending.Doc.Add(value);
          break;
        default:
          // Never silently skip: an unknown key is a typo, and a typo that parses is a
          // fact quietly dropped.
          throw new ErrorCodeRegistryException(
            $"{registryPath}:{lineNo}: unknown key `{key}` (want csharp/selfhosted/shv2/reserved/doc)");
      }
    }
    if (pending is not null) entries.Add(pending.Build());

    Validate(entries, registryPath);
    return entries;
  }

  /// <summary>The entry being accumulated as the parser walks that entry's key lines.</summary>
  sealed class Pending(int number, string canonicalName, int lineNumber) {
    public int Number { get; } = number;
    public Dictionary<Compiler, string> Names { get; } = [];
    public string? Reserved { get; set; }
    public List<string> Doc { get; } = [];

    public Entry Build() => new(Number, canonicalName, Names, Reserved, Doc, lineNumber);
  }

  /// <summary>
  /// The guard. Every rule here is one that a past collision walked straight through.
  /// </summary>
  static void Validate(List<Entry> entries, string registryPath) {
    var byNumber = new Dictionary<int, Entry>();
    var byCanonical = new Dictionary<string, Entry>();
    var byMember = new Dictionary<(Compiler, string), Entry>();

    foreach (var e in entries) {
      // THE rule. This is what would have caught E3099.
      if (byNumber.TryGetValue(e.Number, out var prior)) {
        throw new ErrorCodeRegistryException(
          $"{registryPath}: DUPLICATE ERROR CODE E{e.Number:D4} - claimed twice:\n"
          + $"  line {prior.LineNumber}: {prior.CanonicalName}\n"
          + $"  line {e.LineNumber}: {e.CanonicalName}\n"
          + "  One number, one meaning. Give one of them the next free number in its band.");
      }
      byNumber[e.Number] = e;

      if (byCanonical.TryGetValue(e.CanonicalName, out var priorName)) {
        throw new ErrorCodeRegistryException(
          $"{registryPath}: DUPLICATE CANONICAL NAME `{e.CanonicalName}` - used twice:\n"
          + $"  line {priorName.LineNumber}: E{priorName.Number:D4}\n"
          + $"  line {e.LineNumber}: E{e.Number:D4}");
      }
      byCanonical[e.CanonicalName] = e;

      foreach (var (compiler, member) in e.Names) {
        var slot = (compiler, member);
        if (byMember.TryGetValue(slot, out var priorMember)) {
          throw new ErrorCodeRegistryException(
            $"{registryPath}: {KeyFor(compiler)} spells two codes `{member}`:\n"
            + $"  line {priorMember.LineNumber}: E{priorMember.Number:D4}\n"
            + $"  line {e.LineNumber}: E{e.Number:D4}");
        }
        byMember[slot] = e;
      }

      if (e.Reserved is not null && e.Names.Count > 0) {
        throw new ErrorCodeRegistryException(
          $"{registryPath}:{e.LineNumber}: E{e.Number:D4} is `reserved` but "
          + $"{string.Join("/", e.Names.Keys.Select(KeyFor))} declares it. "
          + "Drop the `reserved` line - it is a live code now.");
      }
      if (e.Reserved is null && e.Names.Count == 0) {
        throw new ErrorCodeRegistryException(
          $"{registryPath}:{e.LineNumber}: E{e.Number:D4} ({e.CanonicalName}) is declared by no "
          + "compiler and is not marked `reserved`. Add a `reserved <why>` line, or a compiler claim.");
      }
      if (e.Doc.Count == 0) {
        throw new ErrorCodeRegistryException(
          $"{registryPath}:{e.LineNumber}: E{e.Number:D4} ({e.CanonicalName}) has no `doc` line. "
          + "`lookup_error_code` answers from these; a code with no doc is a code that cannot be looked up.");
      }
      _ = e.Stage; // rejects a number outside the known stage bands
    }
  }

  // ===========================================================================
  // Generation
  // ===========================================================================

  /// <summary>Render the artifact a given compiler consumes. LF endings — .gitattributes pins eol=lf.</summary>
  public static string Render(IReadOnlyList<Entry> entries, Compiler compiler) {
    var claimed = entries.Where(e => e.Names.ContainsKey(compiler)).OrderBy(e => e.Number).ToList();
    return compiler == Compiler.Csharp ? RenderCsharp(claimed) : RenderMaxon(claimed, compiler);
  }

  const string Banner =
    "GENERATED FROM docs/error-codes.txt. DO NOT EDIT.\n"
    + "\n"
    + "Add or change a code in that file, then run `maxon error-codes generate`.\n"
    + "`maxon error-codes check` (run by every `dotnet build`) fails if this file drifts.";

  static string RenderCsharp(List<Entry> claimed) {
    var b = new StringBuilder();
    b.Append("// <auto-generated/>\n");
    foreach (var line in Banner.Split('\n')) b.Append(line.Length == 0 ? "//\n" : $"// {line}\n");
    b.Append("\nnamespace MaxonSharp.Compiler;\n\n");
    b.Append("/// <summary>\n");
    b.Append("/// Structured error codes for the compiler, grouped by compilation stage:\n");
    b.Append("/// 1xxx lexer, 2xxx parser, 3xxx semantic, 4xxx IR, 5xxx code emitter,\n");
    b.Append("/// 6xxx PE writer, 9xxx internal.\n");
    b.Append("/// </summary>\n");
    b.Append("public enum ErrorCode {\n");
    string? band = null;
    foreach (var e in claimed) {
      if (e.Stage != band) {
        if (band is not null) b.Append('\n');
        band = e.Stage;
      }
      // One <summary> holding every doc line — repeating the tag is CS1710.
      b.Append("  /// <summary>\n");
      foreach (var line in e.Doc) b.Append($"  /// {XmlEscape(line)}\n");
      b.Append("  /// </summary>\n");
      b.Append($"  {e.Names[Compiler.Csharp]} = {e.Number},\n");
    }
    b.Append("}\n");
    return b.ToString();
  }

  static string XmlEscape(string s) =>
    s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

  /// <summary>
  /// Both Maxon compilers get the same shape; they differ only in the raw-value type.
  /// shv2's ErrorCode is String-backed (its `Diagnostic.render` prints `code.rawValue`
  /// straight into `error <code>: ...`), v1's is int-backed.
  /// </summary>
  static string RenderMaxon(List<Entry> claimed, Compiler compiler) {
    var stringBacked = compiler == Compiler.Shv2;
    var b = new StringBuilder();
    foreach (var line in Banner.Split('\n')) b.Append(line.Length == 0 ? "//\n" : $"// {line}\n");
    b.Append("\n");
    b.Append("// Error codes grouped by compilation stage:\n");
    b.Append("// 1xxx lexer, 2xxx parser, 3xxx semantic, 4xxx IR, 5xxx code emitter,\n");
    b.Append("// 6xxx PE writer, 9xxx internal.\n");
    b.Append("export enum ErrorCode\n");
    string? band = null;
    foreach (var e in claimed) {
      if (e.Stage != band) {
        if (band is not null) b.Append('\n');
        band = e.Stage;
      }
      foreach (var line in e.Doc) b.Append($"\t// {line}\n");
      var value = stringBacked ? $"\"E{e.Number:D4}\"" : e.Number.ToString();
      b.Append($"\t{e.Names[compiler]} = {value}\n");
    }
    b.Append("end 'ErrorCode'\n");
    return b.ToString();
  }

  // ===========================================================================
  // The `maxon error-codes` command
  // ===========================================================================

  /// <summary>Entry point for `maxon error-codes &lt;check|generate&gt; [root]`.</summary>
  public static int Run(string[] args) {
    if (args.Length == 0 || (args[0] != "check" && args[0] != "generate")) {
      Console.Error.WriteLine("Usage: maxon error-codes <check|generate> [repo-root]");
      Console.Error.WriteLine();
      Console.Error.WriteLine("  check     verify docs/error-codes.txt and that every generated file matches it");
      Console.Error.WriteLine("  generate  rewrite the generated files from docs/error-codes.txt");
      return 1;
    }

    string root;
    try {
      root = args.Length > 1 ? Path.GetFullPath(args[1]) : FindRoot();
    } catch (ErrorCodeRegistryException ex) {
      Console.Error.WriteLine($"error-codes: {ex.Message}");
      return 1;
    }

    var registryPath = Path.Combine(root, RegistryRelativePath.Replace('/', Path.DirectorySeparatorChar));

    List<Entry> entries;
    try {
      entries = Load(registryPath);
    } catch (ErrorCodeRegistryException ex) {
      // The duplicate-number failure lands here. Loud, and it names both claimants.
      Console.Error.WriteLine($"error-codes: {ex.Message}");
      return 1;
    }

    return args[0] == "generate" ? Generate(root, entries) : Check(root, entries);
  }

  static int Generate(string root, List<Entry> entries) {
    foreach (var (compiler, relative) in GeneratedFiles) {
      var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
      var rendered = Render(entries, compiler);
      var existing = File.Exists(path) ? File.ReadAllText(path) : null;
      if (existing == rendered) {
        Console.WriteLine($"  unchanged  {relative}");
        continue;
      }
      File.WriteAllText(path, rendered, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
      Console.WriteLine($"  wrote      {relative}");
    }
    Console.WriteLine($"error-codes: {entries.Count} codes "
      + $"({entries.Count(e => e.Reserved is not null)} reserved) from {RegistryRelativePath}");
    return 0;
  }

  static int Check(string root, List<Entry> entries) {
    var stale = new List<string>();
    foreach (var (compiler, relative) in GeneratedFiles) {
      var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
      var rendered = Render(entries, compiler);
      var existing = File.Exists(path) ? File.ReadAllText(path) : null;
      if (existing != rendered) stale.Add(relative);
    }

    if (stale.Count > 0) {
      Console.Error.WriteLine(
        "error-codes: these generated files do not match docs/error-codes.txt:");
      foreach (var s in stale) Console.Error.WriteLine($"  {s}");
      Console.Error.WriteLine("Run `maxon error-codes generate` and commit the result.");
      return 1;
    }

    Console.WriteLine($"error-codes: OK - {entries.Count} codes "
      + $"({entries.Count(e => e.Reserved is not null)} reserved), 3 generated files up to date");
    return 0;
  }

  /// <summary>Walk up from the working directory looking for the registry.</summary>
  static string FindRoot() {
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null) {
      if (File.Exists(Path.Combine(dir.FullName, "docs", "error-codes.txt"))) return dir.FullName;
      dir = dir.Parent;
    }
    throw new ErrorCodeRegistryException(
      $"no Maxon checkout above {Directory.GetCurrentDirectory()} (looked for {RegistryRelativePath}). "
      + "Pass the repo root explicitly: maxon error-codes check <root>");
  }
}

public sealed class ErrorCodeRegistryException(string message) : Exception(message);
