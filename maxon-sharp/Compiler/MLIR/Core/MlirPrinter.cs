using System.Text;
using System.Text.RegularExpressions;

namespace MaxonSharp.Compiler.Ir.Core;

public static class IrPrinter {
  public static string Print<TOp>(IrModule<TOp> module, Func<IrFunction<TOp>, bool>? filter = null) where TOp : IPrintableOp {
    var sb = new StringBuilder();
    sb.AppendLine("module {");
    foreach (var func in module.Functions) {
      if (filter != null && !filter(func)) continue;
      PrintFunction(sb, func, "  ");
    }
    sb.AppendLine("}");
    return StabilizeLabels(sb.ToString());
  }

  private static void PrintFunction<TOp>(StringBuilder sb, IrFunction<TOp> func, string indent) where TOp : IPrintableOp {
    sb.Append($"{indent}func @{func.Name}(");
    for (int i = 0; i < func.ParamTypes.Count; i++) {
      if (i > 0) sb.Append(", ");
      if (i < func.ParamNames.Count)
        sb.Append($"{func.ParamNames[i]}: ");
      sb.Append(IrType.Resolve(func.ParamTypes[i]));
    }
    sb.Append(')');
    if (func.ReturnType != null && func.ReturnType != IrType.Void) {
      sb.Append($" -> {IrType.Resolve(func.ReturnType)}");
    }
    sb.AppendLine(" {");

    foreach (var block in func.Body.Blocks) {
      sb.AppendLine($"{indent}{block.Name}:");
      foreach (var op in block.Operations) {
        sb.Append($"{indent}  ");
        PrintOp(sb, op);
        sb.AppendLine();
      }
    }

    sb.AppendLine($"{indent}}}");
  }

  private static void PrintOp(StringBuilder sb, IPrintableOp op) {
    if (op.PrintableResults.Count > 0) {
      sb.Append(string.Join(", ", op.PrintableResults));
      sb.Append(" = ");
    }
    sb.Append(op.Mnemonic);
    if (op.PrintableOperands.Count > 0) {
      sb.Append(' ');
      sb.Append(string.Join(", ", op.PrintableOperands));
    }
    foreach (var (key, attr) in op.PrintableAttributes) {
      sb.Append($" {{{key} = {attr}}}");
    }
  }

  // --- Stable label renumbering ---
  //
  // Counter-based identifier names in the lowering pipeline (block labels,
  // synthetic variables, rdata/symdata symbols) carry trailing `_<digits>`
  // suffixes minted from various counters. Most of those counters bump across
  // the whole compile unit — stdlib functions that are filtered out of fragment
  // output still drive the counters, and batched compiles share counters
  // across every test in the batch. A small unrelated change can therefore
  // shift every label number in every printed fragment.
  //
  // StabilizeLabels rewrites recognized counter-suffixed identifiers in the
  // printed IR text so they restart at _0 in order of first appearance within
  // that text. This is purely cosmetic — only the printed text is rewritten;
  // the underlying IR, emitted assembly, symdata symbols, and debug info keep
  // the original counter-based names. Safe to call multiple times; a text
  // that is already dense stays dense. Called both at the end of every IR
  // print and after batched-IR splitting in FragmentGenerator, so each test's
  // slice is stable both on its own and against unrelated tests in the batch.
  //
  // Detection is context-based rather than prefix-list-based: we collect
  // every identifier that appears in a position where the printer is known
  // to emit a label or label-like identifier — block headers (`<name>:`),
  // explicit label-def ops (`x64.label NAME`), the `var = NAME` MLIR
  // attribute, branch targets in `j*`/`br`/`cond_br` ops, and symdata/rdata
  // operands in `[NAME]` brackets — then renumber any of those identifiers
  // that end in `_<digits>`. This captures both compiler-internal names
  // (`__nonnull_skip_N`, `__try_result_N`, `__cstr_done_N`, ...) and
  // user-written block labels minted via `UniqueLabel("foo")` →
  // `foo_<counter>`, without needing to enumerate every prefix.

  // Identifier shape: alphanumeric/underscore start (function names may
  // begin with a digit when synthesized — e.g. `1-Logic.storeAndCheck` for
  // a renamed-during-batch test main), with `.` and `-` allowed inside for
  // qualified forms (`enum-match-only.main.foo_3`).
  private const string IdentPattern = @"[A-Za-z0-9_][A-Za-z0-9_.\-]*";

  // SYSLIB1045 suggests source-generated regexes, but these patterns interpolate
  // `IdentPattern` at construction time — incompatible with the
  // GeneratedRegexAttribute model. Suppress for the regex block.
#pragma warning disable SYSLIB1045

  // Function header: `^  func @<name>(`. Two-space indent matches every printer.
  private static readonly Regex FuncHeaderRegex = new(
    $@"^  func @(?<name>{IdentPattern})\(",
    RegexOptions.Compiled | RegexOptions.Multiline);

  // Block header: `^  <name>:` (2-space indent, identifier, colon).
  private static readonly Regex BlockHeaderRegex = new(
    $@"^  (?<name>{IdentPattern}):\s*$",
    RegexOptions.Compiled | RegexOptions.Multiline);

  // `x64.label <name>` — explicit label-def op.
  private static readonly Regex LabelDefRegex = new(
    $@"\bx64\.label\s+(?<name>{IdentPattern})\b",
    RegexOptions.Compiled);

  // `{var = NAME}` attribute — synthetic variable name (decl site in Maxon
  // dialect).
  private static readonly Regex VarAttrRegex = new(
    $@"\{{var\s*=\s*(?<name>{IdentPattern})\}}",
    RegexOptions.Compiled);

  // `memref.store %X, NAME` and `memref.load NAME` and `maxon.struct_var_ref
  // NAME` — variable references in the Standard / Maxon dialects. The
  // Standard dialect has no explicit `{var = ...}` decl site (vars are
  // implicitly introduced by the first store), so we detect them via every
  // load/store/var-ref occurrence and treat each unique name as a candidate.
  private static readonly Regex MemRefVarRegex = new(
    $@"\b(?:memref\.store\s+%\w+,\s*|memref\.load\s+|maxon\.struct_var_ref\s+)(?<name>{IdentPattern})\b",
    RegexOptions.Compiled);

  // `maxon.scope_end [a, b, c]` — variable cleanup list. The names here are
  // bare identifiers separated by `, `. Same scope as the enclosing function.
  private static readonly Regex ScopeEndRegex = new(
    $@"\bmaxon\.scope_end\s+\[(?<names>[^\]]*)\]",
    RegexOptions.Compiled);

  // Branch targets: `x64.jmp NAME`, `x64.j<cond> NAME`, `cf.br NAME`,
  // `maxon.br NAME`, plus then/else inside `cond_br ... [then: A, else: B]`.
  private static readonly Regex BranchTargetRegex = new(
    $@"\b(?:x64\.j[a-z]*|cf\.br|maxon\.br)\s+(?<name>{IdentPattern})\b",
    RegexOptions.Compiled);
  private static readonly Regex CondBrTargetRegex = new(
    $@"\b(?:then|else):\s*(?<name>{IdentPattern})\b",
    RegexOptions.Compiled);

  // Data label references: `lea_symdata rax, [NAME]`, `lea_rdata rax, [NAME]`.
  // Symdata and rdata labels are module-scoped real symbols.
  private static readonly Regex DataLabelRegex = new(
    $@"\b(?:lea_symdata|lea_rdata)\s+\w+,\s*\[(?<name>{IdentPattern})\]",
    RegexOptions.Compiled);

  // Trailing `_<digits>` suffix at end of identifier. The base (everything
  // before the underscore that starts the digit run) becomes the prefix in
  // the renumbering scheme.
  private static readonly Regex CounterSuffixRegex = new(
    @"^(?<base>.+)_(?<num>\d+)$",
    RegexOptions.Compiled);

  // Substitution regex for Pass 2. Captures any `<funcQual>.<ident>` or bare
  // `<ident>` whose ident ends in `_<digits>`. We then look up the rename
  // map and substitute on hit. The optional `.<fieldTail>` suffix is captured
  // separately and preserved verbatim, so a renumber of `__arr_0` correctly
  // rewrites `__arr_0.field` → `__arr_<stable>.field`.
  private static readonly Regex SubstitutionRegex = new(
    @"(?<![A-Za-z0-9_.])(?:(?<func>[A-Za-z0-9_][A-Za-z0-9_.\-]*)\.)?(?<base>[A-Za-z_][A-Za-z0-9_]*)_(?<num>\d+)(?<tail>(?:\.[A-Za-z0-9_]+)*)(?![A-Za-z0-9_])",
    RegexOptions.Compiled);

#pragma warning restore SYSLIB1045

  public static string StabilizeLabels(string text) {
    // Pass 1: walk the text, collect every (scope, originalName) where a label
    // or label-like identifier is introduced. Track the enclosing function.
    var renames = new Dictionary<string, string>();   // "scope|original" → "rewritten-base_num"
    var nextNum = new Dictionary<string, int>();      // "scope|base" → next stable number

    var lines = text.Replace("\r\n", "\n").Split('\n');
    string currentFunc = "";
    foreach (var line in lines) {
      var headerMatch = FuncHeaderRegex.Match(line);
      if (headerMatch.Success) {
        currentFunc = headerMatch.Groups["name"].Value;
        continue;
      }
      foreach (Match m in BlockHeaderRegex.Matches(line))
        RegisterCandidate(m.Groups["name"].Value, currentFunc, renames, nextNum);
      foreach (Match m in LabelDefRegex.Matches(line))
        RegisterCandidate(m.Groups["name"].Value, currentFunc, renames, nextNum);
      foreach (Match m in VarAttrRegex.Matches(line))
        RegisterCandidate(m.Groups["name"].Value, currentFunc, renames, nextNum);
      foreach (Match m in MemRefVarRegex.Matches(line))
        RegisterCandidate(m.Groups["name"].Value, currentFunc, renames, nextNum);
      foreach (Match m in BranchTargetRegex.Matches(line))
        RegisterBranchTarget(m.Groups["name"].Value, currentFunc, renames, nextNum);
      foreach (Match m in CondBrTargetRegex.Matches(line))
        RegisterBranchTarget(m.Groups["name"].Value, currentFunc, renames, nextNum);
      foreach (Match m in DataLabelRegex.Matches(line))
        RegisterDataLabel(m.Groups["name"].Value, renames, nextNum);
      foreach (Match m in ScopeEndRegex.Matches(line)) {
        var names = m.Groups["names"].Value.Split(',');
        foreach (var rawName in names) {
          var trimmed = rawName.Trim();
          // Skip non-identifiers (e.g., `%5` SSA values) and field accesses
          // (`__arr_0.field` — the base `__arr_0` will be registered elsewhere).
          if (trimmed.Length == 0 || !char.IsLetter(trimmed[0]) && trimmed[0] != '_') continue;
          RegisterCandidate(trimmed, currentFunc, renames, nextNum);
        }
      }
    }

    if (renames.Count == 0) return text;

    // Pass 2: rewrite. For each matched token, determine its scope and look
    // up the rename map. The substitution callback resolves the enclosing
    // function via a binary search over recorded `func @<name>(` offsets.
    var funcHeaders = new List<(int Start, string Name)>();
    foreach (Match fh in FuncHeaderRegex.Matches(text)) {
      funcHeaders.Add((fh.Index, fh.Groups["name"].Value));
    }
    string FuncAt(int offset) {
      string name = "";
      foreach (var (start, fn) in funcHeaders) {
        if (start <= offset) name = fn; else break;
      }
      return name;
    }

    return SubstitutionRegex.Replace(text, m => {
      var funcQual = m.Groups["func"].Success ? m.Groups["func"].Value : null;
      var baseName = m.Groups["base"].Value;
      var num = m.Groups["num"].Value;
      var tail = m.Groups["tail"].Value;
      var original = funcQual != null ? $"{funcQual}.{baseName}_{num}" : $"{baseName}_{num}";

      // Decide scope. We use the same priority order as Pass 1:
      // 1) Module-scoped: if seen in a data-label context (Pass 1 recorded it under "<module>").
      // 2) Function-qualified: scope is funcQual.
      // 3) Bare: scope is enclosing function at this position.
      // We try module first via the special "<module>|<base>_<num>" key, then
      // by funcQual or enclosing func.
      var moduleKey = $"<module>|{baseName}_{num}";
      if (renames.TryGetValue(moduleKey, out var modRewrite)) {
        return funcQual != null ? $"{funcQual}.{modRewrite}{tail}" : $"{modRewrite}{tail}";
      }
      var scope = funcQual ?? FuncAt(m.Index);
      if (string.IsNullOrEmpty(scope)) return m.Value;
      var key = $"{scope}|{baseName}_{num}";
      if (!renames.TryGetValue(key, out var rewritten)) return m.Value;
      return funcQual != null ? $"{funcQual}.{rewritten}{tail}" : $"{rewritten}{tail}";
    });
  }

  // Register a label discovered in a function-scoped definition context
  // (block header, x64.label, var attribute). If the name ends in `_<digits>`,
  // it becomes a renumbering candidate. If the name has further `.subname`
  // structure (e.g. `k_15.cmp0`, where `k_15` is the user-label-counter base
  // and `.cmp0` is a stable sub-block suffix), extract the counter-suffixed
  // prefix and register that.
  private static void RegisterCandidate(string name, string currentFunc, Dictionary<string, string> renames, Dictionary<string, int> nextNum) {
    if (string.IsNullOrEmpty(currentFunc)) return;
    // Try the whole name first (covers `foo_3`).
    var m = CounterSuffixRegex.Match(name);
    if (m.Success) {
      AssignRename(currentFunc, name, m.Groups["base"].Value, renames, nextNum);
      return;
    }
    // Try the prefix before the first `.` (covers `k_15.cmp0` →
    // register `k_15`).
    var dotIdx = name.IndexOf('.');
    if (dotIdx > 0) {
      var prefix = name[..dotIdx];
      var pm = CounterSuffixRegex.Match(prefix);
      if (pm.Success) {
        AssignRename(currentFunc, prefix, pm.Groups["base"].Value, renames, nextNum);
      }
    }
  }

  // Register a branch target. Targets can be bare (function-local) or
  // function-qualified (`<func>.<name>`), and may have sub-label suffixes
  // (`<func>.<name>_<N>.<sub>`). We use SubstitutionRegex's structure to
  // pull out the `<funcQual>?.<base>_<num>(<tail>)?` shape, register the
  // counter-suffixed `<base>_<num>` portion (without `tail`), and let the
  // substitution pass rewrite `tail` verbatim.
  private static void RegisterBranchTarget(string target, string currentFunc, Dictionary<string, string> renames, Dictionary<string, int> nextNum) {
    var m = SubstitutionRegex.Match(target);
    if (!m.Success) return;
    var funcQual = m.Groups["func"].Success ? m.Groups["func"].Value : null;
    var baseName = m.Groups["base"].Value;
    var num = m.Groups["num"].Value;
    var name = $"{baseName}_{num}";
    var scope = funcQual ?? currentFunc;
    if (string.IsNullOrEmpty(scope)) return;
    AssignRename(scope, name, baseName, renames, nextNum);
  }

  // Register a module-scoped data label (symdata or rdata).
  private static void RegisterDataLabel(string name, Dictionary<string, string> renames, Dictionary<string, int> nextNum) {
    var m = CounterSuffixRegex.Match(name);
    if (!m.Success) return;
    AssignRename("<module>", name, m.Groups["base"].Value, renames, nextNum);
  }

  // Assign a stable rename for (scope, name) if we haven't seen it before.
  private static void AssignRename(string scope, string name, string baseName, Dictionary<string, string> renames, Dictionary<string, int> nextNum) {
    var key = $"{scope}|{name}";
    if (renames.ContainsKey(key)) return;
    var nextKey = $"{scope}|{baseName}";
    var stable = nextNum.TryGetValue(nextKey, out var n) ? n : 0;
    nextNum[nextKey] = stable + 1;
    renames[key] = $"{baseName}_{stable}";
  }
}
