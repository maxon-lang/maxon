using System.Text;

namespace MaxonSharp.Compiler;

/// <summary>
/// THE parser of <c>docs/error-codes.txt</c> — the single source of truth for the
/// 4-digit error-code space shared by all three compilers. The grammar it implements is
/// specified in that file's own header; this class is the only code in the tree that
/// reads it, and the header is the only prose that describes it.
///
/// The number space used to be written down three times (ErrorCode.cs,
/// maxon-selfhosted/Compiler/ErrorCode.maxon, maxon-shv2/Compiler/Diagnostics.maxon),
/// which is how two agents took E3099 on the same day: each had correctly grepped a
/// different copy. The registry fixed that by GENERATING the three enums — and then
/// promptly grew a second reader of its own format, in the MCP's `lookup_error_code`,
/// which is the same disease one level up. The two hand-written parsers disagreed on
/// five inputs; one of them answered `emittedBy: {}` for a code the bootstrap declares.
///
/// So the format has ONE reader, and tools get a GENERATED artifact instead:
/// <see cref="InterchangeRelativePath"/> is a plain JSON projection of exactly what
/// this parser saw, carrying an FNV-1a hash of the registry bytes it was made from.
/// A tool reads that with an off-the-shelf JSON parser and checks the hash — so it
/// either reports what the gate parsed, or refuses. It cannot invent a third answer.
/// </summary>
public static class ErrorCodeRegistry {
  public const string RegistryRelativePath = "docs/error-codes.txt";

  /// <summary>The machine-readable projection tools read instead of parsing the registry.</summary>
  public const string InterchangeRelativePath = "docs/error-codes.json";

  public enum Compiler { Csharp, Selfhosted, Shv2 }

  /// <summary>
  /// One compiler: the registry key that declares its spelling of a code, the enum
  /// artifact generated for it, and the source tree that must actually EMIT what it
  /// claims. All three facts in one row, because all three are the same fact — "this
  /// compiler" — and splitting them is how a claim came to name a member no source
  /// mentions.
  /// </summary>
  public sealed record CompilerRow(
    Compiler Compiler,
    string Key,
    string GeneratedRelativePath,
    string SourceDir,
    string SourceExtension);

  /// <summary>Registry order: the order every generated list and every answer uses.</summary>
  public static readonly CompilerRow[] Compilers = [
    new(Compiler.Csharp, "csharp", "maxon-sharp/Compiler/ErrorCode.g.cs", "maxon-sharp", ".cs"),
    new(Compiler.Selfhosted, "selfhosted", "maxon-selfhosted/Compiler/ErrorCodeRegistry.maxon", "maxon-selfhosted", ".maxon"),
    new(Compiler.Shv2, "shv2", "maxon-shv2/Compiler/ErrorCodeRegistry.maxon", "maxon-shv2", ".maxon"),
  ];

  public static CompilerRow RowFor(Compiler c) =>
    Compilers.FirstOrDefault(r => r.Compiler == c)
    ?? throw new ErrorCodeRegistryException($"no compiler row for {c}");

  public static string KeyFor(Compiler c) => RowFor(c).Key;

  /// <summary>Directories a source scan never descends into: build output, not source.</summary>
  static readonly string[] SkippedSourceDirs = [".maxon", "bin", "obj", ".git", ".vs"];

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
    public string Tag => $"E{Number:D4}";

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
        $"{Tag} ({CanonicalName}): {Number / 1000}xxx is not a known stage band"),
    };
  }

  /// <summary>
  /// The registry as parsed, plus the hash of the exact bytes it was parsed from. The
  /// hash travels with the entries because it is what lets a downstream reader prove
  /// its copy is the one THIS parse produced.
  /// </summary>
  public sealed record Registry(IReadOnlyList<Entry> Entries, string SourceHash);

  // ===========================================================================
  // The registry hash — the handshake between this parser and every tool
  // ===========================================================================

  /// <summary>
  /// FNV-1a, 64-bit, over the registry's raw bytes, as 16 lowercase hex digits.
  ///
  /// Reimplemented — identically — in `maxon-dev-mcp/mcp/ErrorCodeTool.maxon`, and that
  /// is the ONE fact those two files share. It is safe to share precisely because it is
  /// not a grammar: hashing a byte string has a single right answer, a mismatch is
  /// self-announcing, and the failure mode of a divergence is a refusal rather than a
  /// wrong answer. (Maxon's `int(0 to u64.max)` multiply wraps, which is what FNV needs;
  /// see maxon-shv2/Compiler/ContentHash.maxon, which uses the same constants.)
  /// </summary>
  public static string HashBytes(byte[] bytes) {
    const ulong offsetBasis = 14695981039346656037UL;
    const ulong prime = 1099511628211UL;
    var h = offsetBasis;
    foreach (var b in bytes) {
      h ^= b;
      h *= prime;
    }
    return h.ToString("x16");
  }

  // ===========================================================================
  // Parsing — the ONE reader of the authored format
  // ===========================================================================

  /// <summary>Key/value and code/name are separated by a run of spaces or tabs.</summary>
  static readonly char[] FieldSeparators = [' ', '\t'];

  /// <summary>
  /// Parse and validate the registry. Throws <see cref="ErrorCodeRegistryException"/> —
  /// naming both claimants — on any duplicate. Callers let it escape: a bad registry is
  /// a build failure, not a diagnostic to collect.
  /// </summary>
  public static Registry Load(string registryPath) {
    if (!File.Exists(registryPath)) {
      throw new ErrorCodeRegistryException($"registry not found: {registryPath}");
    }

    var bytes = File.ReadAllBytes(registryPath);
    var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes);
    var entries = new List<Entry>();
    Pending? pending = null;

    var lines = text.Split('\n');
    for (var i = 0; i < lines.Length; i++) {
      var raw = lines[i].TrimEnd('\r');
      var lineNo = i + 1;

      // A blank line and a `#` comment are ignored ANYWHERE, including between an
      // entry's key lines — an entry is delimited by the next column-0 header, never
      // by a gap.
      if (raw.Trim().Length == 0 || raw.TrimStart().StartsWith('#')) continue;

      if (!char.IsWhiteSpace(raw[0])) {
        // `E<nnnn> <CanonicalName>` — a new entry.
        if (pending is not null) entries.Add(pending.Build());
        var parts = raw.Split(FieldSeparators, 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts[1].Length == 0 || parts[0].Length != 5 || parts[0][0] != 'E'
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

      var kv = raw.TrimStart().Split(FieldSeparators, 2, StringSplitOptions.TrimEntries);
      var key = kv[0];
      var value = kv.Length > 1 ? kv[1] : "";
      if (value.Length == 0) {
        throw new ErrorCodeRegistryException($"{registryPath}:{lineNo}: key `{key}` has no value");
      }

      var claimant = Compilers.FirstOrDefault(r => r.Key == key);
      if (claimant is not null) {
        if (pending.Names.ContainsKey(claimant.Compiler)) {
          throw new ErrorCodeRegistryException(
            $"{registryPath}:{lineNo}: E{pending.Number:D4} declares `{key}` twice");
        }
        pending.Names[claimant.Compiler] = value;
        continue;
      }

      switch (key) {
        case "reserved":
          if (pending.Reserved is not null) {
            throw new ErrorCodeRegistryException(
              $"{registryPath}:{lineNo}: E{pending.Number:D4} declares `reserved` twice");
          }
          pending.Reserved = value;
          break;
        case "doc":
          pending.Doc.Add(value);
          break;
        default:
          // Never silently skip: an unknown key is a typo, and a typo that parses is a
          // fact quietly dropped.
          throw new ErrorCodeRegistryException(
            $"{registryPath}:{lineNo}: unknown key `{key}` (want "
            + $"{string.Join("/", Compilers.Select(r => r.Key))}/reserved/doc)");
      }
    }
    if (pending is not null) entries.Add(pending.Build());

    Validate(entries, registryPath);
    return new Registry(entries, HashBytes(bytes));
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
          $"{registryPath}: DUPLICATE ERROR CODE {e.Tag} - claimed twice:\n"
          + $"  line {prior.LineNumber}: {prior.CanonicalName}\n"
          + $"  line {e.LineNumber}: {e.CanonicalName}\n"
          + "  One number, one meaning. Give one of them the next free number in its band.");
      }
      byNumber[e.Number] = e;

      if (byCanonical.TryGetValue(e.CanonicalName, out var priorName)) {
        throw new ErrorCodeRegistryException(
          $"{registryPath}: DUPLICATE CANONICAL NAME `{e.CanonicalName}` - used twice:\n"
          + $"  line {priorName.LineNumber}: {priorName.Tag}\n"
          + $"  line {e.LineNumber}: {e.Tag}");
      }
      byCanonical[e.CanonicalName] = e;

      foreach (var (compiler, member) in e.Names) {
        var slot = (compiler, member);
        if (byMember.TryGetValue(slot, out var priorMember)) {
          throw new ErrorCodeRegistryException(
            $"{registryPath}: {KeyFor(compiler)} spells two codes `{member}`:\n"
            + $"  line {priorMember.LineNumber}: {priorMember.Tag}\n"
            + $"  line {e.LineNumber}: {e.Tag}");
        }
        byMember[slot] = e;
      }

      if (e.Reserved is not null && e.Names.Count > 0) {
        throw new ErrorCodeRegistryException(
          $"{registryPath}:{e.LineNumber}: {e.Tag} is `reserved` but "
          + $"{string.Join("/", e.Names.Keys.Select(KeyFor))} declares it. "
          + "Drop the `reserved` line - it is a live code now.");
      }
      if (e.Reserved is null && e.Names.Count == 0) {
        throw new ErrorCodeRegistryException(
          $"{registryPath}:{e.LineNumber}: {e.Tag} ({e.CanonicalName}) is declared by no "
          + "compiler and is not marked `reserved`. Add a `reserved <why>` line, or a compiler claim.");
      }
      if (e.Doc.Count == 0) {
        throw new ErrorCodeRegistryException(
          $"{registryPath}:{e.LineNumber}: {e.Tag} ({e.CanonicalName}) has no `doc` line. "
          + "`lookup_error_code` answers from these; a code with no doc is a code that cannot be looked up.");
      }
      _ = e.Stage; // rejects a number outside the known stage bands
    }
  }

  // ===========================================================================
  // Claim liveness — a claim that names a member nobody emits is a lie
  // ===========================================================================

  /// <summary>
  /// Every claim must name a member the claiming compiler's source ACTUALLY MENTIONS.
  ///
  /// The converse — emitted but not in the registry — is structurally impossible, because
  /// the enum is generated: `ErrorCode.Whatever` does not compile unless the registry put
  /// it there. This is the other half, and nothing enforced it: 22 rows claimed members no
  /// source referenced, including `selfhosted semanticCapturingClosureEscapes` for E3099 —
  /// the very code the collision story is about. A registry that lies about itself is worse
  /// than no registry, because it is the thing everyone consults to find out what is true.
  ///
  /// Run by `check` (and therefore by every build), not by `generate` — the workflow is
  /// "add the entry, generate the member, then WRITE the code that throws it", and a
  /// liveness rule inside `generate` would forbid step two.
  /// </summary>
  static List<string> FindDeadClaims(IReadOnlyList<Entry> entries, string root) {
    var dead = new List<string>();

    foreach (var row in Compilers) {
      var claimed = entries.Where(e => e.Names.ContainsKey(row.Compiler)).ToList();
      if (claimed.Count == 0) continue;

      var mentioned = CollectIdentifiers(root, row);
      foreach (var e in claimed.Where(e => !mentioned.Contains(e.Names[row.Compiler]))) {
        dead.Add(
          $"  {RegistryRelativePath}:{e.LineNumber}: {e.Tag} claims `{row.Key} {e.Names[row.Compiler]}`, "
          + $"but no file under {row.SourceDir}/ mentions that member.");
      }
    }

    return dead;
  }

  /// <summary>
  /// Every identifier token appearing in a compiler's source, excluding its own generated
  /// enum — which declares every member and would therefore vouch for all of them,
  /// including the dead ones. Tokenised rather than substring-searched so that a member
  /// name is not "found" inside a longer one.
  /// </summary>
  static HashSet<string> CollectIdentifiers(string root, CompilerRow row) {
    var identifiers = new HashSet<string>(StringComparer.Ordinal);
    var treeDir = Path.Combine(root, row.SourceDir.Replace('/', Path.DirectorySeparatorChar));
    if (!Directory.Exists(treeDir)) return identifiers;

    var generated = Path.GetFullPath(
      Path.Combine(root, row.GeneratedRelativePath.Replace('/', Path.DirectorySeparatorChar)));

    foreach (var file in EnumerateSourceFiles(treeDir, row.SourceExtension)) {
      if (Path.GetFullPath(file) == generated) continue;
      AddIdentifiers(File.ReadAllText(file), identifiers);
    }

    return identifiers;
  }

  static IEnumerable<string> EnumerateSourceFiles(string dir, string extension) {
    foreach (var file in Directory.EnumerateFiles(dir)) {
      if (Path.GetExtension(file).Equals(extension, StringComparison.OrdinalIgnoreCase)) yield return file;
    }

    foreach (var sub in Directory.EnumerateDirectories(dir)) {
      if (SkippedSourceDirs.Contains(Path.GetFileName(sub))) continue;
      foreach (var file in EnumerateSourceFiles(sub, extension)) yield return file;
    }
  }

  static void AddIdentifiers(string text, HashSet<string> into) {
    var i = 0;
    while (i < text.Length) {
      if (!IsIdentifierStart(text[i])) {
        i++;
        continue;
      }
      var start = i;
      while (i < text.Length && IsIdentifierPart(text[i])) i++;
      into.Add(text[start..i]);
    }
  }

  static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
  static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

  // ===========================================================================
  // Generation
  // ===========================================================================

  const string Banner =
    "GENERATED FROM docs/error-codes.txt. DO NOT EDIT.\n"
    + "\n"
    + "Add or change a code in that file, then run `maxon error-codes generate`.\n"
    + "`maxon error-codes check` (run by every build that consumes this file) fails if it drifts.";

  /// <summary>Render every generated artifact: path -> exact bytes it must contain.</summary>
  static List<(string RelativePath, string Content)> RenderAll(Registry registry) {
    var rendered = Compilers
      .Select(row => (row.GeneratedRelativePath, RenderEnum(registry.Entries, row)))
      .ToList();
    rendered.Add((InterchangeRelativePath, RenderInterchange(registry)));
    return rendered;
  }

  static string RenderEnum(IReadOnlyList<Entry> entries, CompilerRow row) {
    var claimed = entries.Where(e => e.Names.ContainsKey(row.Compiler)).OrderBy(e => e.Number).ToList();
    return row.Compiler == Compiler.Csharp ? RenderCsharp(claimed) : RenderMaxon(claimed, row);
  }

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
  static string RenderMaxon(List<Entry> claimed, CompilerRow row) {
    var stringBacked = row.Compiler == Compiler.Shv2;
    var b = new StringBuilder();
    foreach (var line in Banner.Split('\n')) b.Append(line.Length == 0 ? "//\n" : $"// {line}\n");
    b.Append('\n');
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
      var value = stringBacked ? $"\"{e.Tag}\"" : e.Number.ToString();
      b.Append($"\t{e.Names[row.Compiler]} = {value}\n");
    }
    b.Append("end 'ErrorCode'\n");
    return b.ToString();
  }

  /// <summary>
  /// The interchange artifact: what THIS parser saw, in a format anything can read with
  /// an off-the-shelf JSON parser. Every field a tool could want is present and none is
  /// left to be derived — `stage` and `notEmittedBy` are written out rather than
  /// recomputed downstream, because a derivation performed twice is a fact written twice,
  /// and this file exists to stop exactly that.
  ///
  /// `sourceHash` is the handshake: a reader hashes docs/error-codes.txt itself and
  /// refuses to answer if it does not match. That is what makes a stale copy of this file
  /// SILENT instead of WRONG.
  /// </summary>
  static string RenderInterchange(Registry registry) {
    var b = new StringBuilder();
    b.Append("{\n");
    b.Append($"  \"generatedFrom\": \"{RegistryRelativePath}\",\n");
    b.Append($"  \"sourceHash\": \"{registry.SourceHash}\",\n");
    b.Append("  \"note\": \"GENERATED. Do not edit. Run `maxon error-codes generate`. "
      + "A reader MUST hash generatedFrom's bytes (FNV-1a 64, hex) and refuse to answer unless it equals sourceHash.\",\n");
    b.Append("  \"compilers\": [");
    b.Append(string.Join(", ", Compilers.Select(r => $"\"{r.Key}\"")));
    b.Append("],\n");
    b.Append("  \"codes\": [\n");

    var ordered = registry.Entries.OrderBy(e => e.Number).ToList();
    for (var i = 0; i < ordered.Count; i++) {
      var e = ordered[i];
      b.Append("    {\n");
      b.Append($"      \"code\": {e.Number},\n");
      b.Append($"      \"tag\": \"{e.Tag}\",\n");
      b.Append($"      \"name\": {JsonString(e.CanonicalName)},\n");
      b.Append($"      \"stage\": \"{e.Stage}\",\n");
      b.Append($"      \"doc\": {JsonString(string.Join("\n", e.Doc))},\n");

      var emitted = Compilers.Where(r => e.Names.ContainsKey(r.Compiler)).ToList();
      b.Append("      \"emittedBy\": {");
      b.Append(string.Join(", ", emitted.Select(r => $"{JsonString(r.Key)}: {JsonString(e.Names[r.Compiler])}")));
      b.Append("},\n");

      var absent = Compilers.Where(r => !e.Names.ContainsKey(r.Compiler)).ToList();
      b.Append("      \"notEmittedBy\": [");
      b.Append(string.Join(", ", absent.Select(r => JsonString(r.Key))));
      b.Append(']');

      // Present only when the number is TAKEN but not yet DECLARED — the field's very
      // presence is the answer to "may I take this number?".
      if (e.Reserved is not null) {
        b.Append(",\n      \"reserved\": ");
        b.Append(JsonString(e.Reserved));
      }
      b.Append('\n');
      b.Append(i == ordered.Count - 1 ? "    }\n" : "    },\n");
    }

    b.Append("  ]\n");
    b.Append("}\n");
    return b.ToString();
  }

  static string JsonString(string s) {
    var b = new StringBuilder("\"");
    foreach (var c in s) {
      switch (c) {
        case '"': b.Append("\\\""); break;
        case '\\': b.Append("\\\\"); break;
        case '\n': b.Append("\\n"); break;
        case '\r': b.Append("\\r"); break;
        case '\t': b.Append("\\t"); break;
        default:
          if (c < 0x20) b.Append($"\\u{(int)c:x4}");
          else b.Append(c);
          break;
      }
    }
    return b.Append('"').ToString();
  }

  // ===========================================================================
  // The `maxon error-codes` command, and the gate every consumer runs
  // ===========================================================================

  /// <summary>Entry point for `maxon error-codes &lt;check|generate&gt; [root]`.</summary>
  public static int Run(string[] args) {
    if (args.Length == 0 || (args[0] != "check" && args[0] != "generate")) {
      Console.Error.WriteLine("Usage: maxon error-codes <check|generate> [repo-root]");
      Console.Error.WriteLine();
      Console.Error.WriteLine("  check     verify docs/error-codes.txt, that every generated file matches it,");
      Console.Error.WriteLine("            and that every compiler claim names a member that compiler emits");
      Console.Error.WriteLine("  generate  rewrite the generated files from docs/error-codes.txt");
      return 1;
    }

    string root;
    try {
      root = args.Length > 1 ? Path.GetFullPath(args[1]) : FindRoot(Directory.GetCurrentDirectory());
    } catch (ErrorCodeRegistryException ex) {
      Console.Error.WriteLine($"error-codes: {ex.Message}");
      return 1;
    }

    return args[0] == "generate" ? Generate(root) : Check(root);
  }

  /// <summary>
  /// THE GATE. Run by `dotnet build` (which generates the C# enum) AND by any `maxon
  /// build` of a project that consumes a generated registry — because a generated file
  /// must be verified WHERE IT IS USED, not only where it happens to be produced. Without
  /// the second half, an shv2 agent could hand-edit maxon-shv2/Compiler/ErrorCodeRegistry.maxon,
  /// get a green suite, and never trip the check that lives in someone else's build.
  /// </summary>
  public static int Check(string root) {
    Registry registry;
    try {
      registry = Load(RegistryPath(root));
    } catch (ErrorCodeRegistryException ex) {
      // The duplicate-number failure lands here. Loud, and it names both claimants.
      Console.Error.WriteLine($"error-codes: {ex.Message}");
      return 1;
    }

    var stale = RenderAll(registry)
      .Where(a => ReadOrNull(Path.Combine(root, a.RelativePath.Replace('/', Path.DirectorySeparatorChar))) != a.Content)
      .Select(a => a.RelativePath)
      .ToList();

    if (stale.Count > 0) {
      Console.Error.WriteLine("error-codes: these generated files do not match docs/error-codes.txt:");
      foreach (var s in stale) Console.Error.WriteLine($"  {s}");
      Console.Error.WriteLine("Run `maxon error-codes generate` and commit the result.");
      return 1;
    }

    var dead = FindDeadClaims(registry.Entries, root);
    if (dead.Count > 0) {
      Console.Error.WriteLine("error-codes: DEAD CLAIMS - the registry names members no compiler emits:");
      foreach (var d in dead) Console.Error.WriteLine(d);
      Console.Error.WriteLine(
        "Either emit the code from that compiler, or delete the claim line. If deleting it leaves the");
      Console.Error.WriteLine(
        "entry with no claimant, add `reserved <why>` - the number stays TAKEN either way.");
      return 1;
    }

    Console.WriteLine($"error-codes: OK - {Summary(registry)}, {Compilers.Length + 1} generated files up to date");
    return 0;
  }

  static int Generate(string root) {
    Registry registry;
    try {
      registry = Load(RegistryPath(root));
    } catch (ErrorCodeRegistryException ex) {
      Console.Error.WriteLine($"error-codes: {ex.Message}");
      return 1;
    }

    foreach (var (relative, content) in RenderAll(registry)) {
      var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
      if (ReadOrNull(path) == content) {
        Console.WriteLine($"  unchanged  {relative}");
        continue;
      }
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
      Console.WriteLine($"  wrote      {relative}");
    }

    Console.WriteLine($"error-codes: {Summary(registry)} from {RegistryRelativePath}");
    return 0;
  }

  static string Summary(Registry registry) =>
    $"{registry.Entries.Count} codes ({registry.Entries.Count(e => e.Reserved is not null)} reserved), "
    + $"registry hash {registry.SourceHash}";

  static string RegistryPath(string root) =>
    Path.Combine(root, RegistryRelativePath.Replace('/', Path.DirectorySeparatorChar));

  static string? ReadOrNull(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

  /// <summary>
  /// Is `projectDir` a tree that CONSUMES a generated registry? Asked by `maxon build`,
  /// which then gates on <see cref="Check"/>. Derived from <see cref="Compilers"/>, so
  /// adding a fourth compiler cannot forget to gate its build.
  /// </summary>
  public static bool ConsumesGeneratedRegistry(string root, string projectDir) {
    var project = Path.GetFullPath(projectDir);
    return Compilers.Any(row => {
      var generated = Path.GetFullPath(
        Path.Combine(root, row.GeneratedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
      return generated.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    });
  }

  /// <summary>Walk up from `startDir` looking for the registry. Throws if there is none above it.</summary>
  public static string FindRoot(string startDir) {
    var dir = new DirectoryInfo(startDir);
    while (dir is not null) {
      if (File.Exists(Path.Combine(dir.FullName, "docs", "error-codes.txt"))) return dir.FullName;
      dir = dir.Parent;
    }
    throw new ErrorCodeRegistryException(
      $"no Maxon checkout above {startDir} (looked for {RegistryRelativePath}). "
      + "Pass the repo root explicitly: maxon error-codes check <root>");
  }

  /// <summary>Like <see cref="FindRoot"/>, but absence is an answer rather than an error.</summary>
  public static string? FindRootOrNull(string startDir) {
    try {
      return FindRoot(startDir);
    } catch (ErrorCodeRegistryException) {
      return null;
    }
  }
}

public sealed class ErrorCodeRegistryException(string message) : Exception(message);
