using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler;

/// <summary>
/// THE GATE on Maxon's FLAT TOP-LEVEL NAMESPACE, over THIS REPOSITORY'S OWN Maxon projects: no two
/// files of one compilation unit may declare the same top-level name with cross-file visibility.
///
/// WHY IT EXISTS. <c>Targets/Arm64/Arm64Runtime.maxon</c> and <c>Targets/X64/X64LinuxRuntime.maxon</c>
/// each exported <c>LinuxSyscallWrite</c>/<c>LinuxSyscallMmap</c>/<c>LinuxSyscallExitGroup</c> over
/// DIFFERENT values. The top-level namespace is flat and a file's own directory grants it nothing, so
/// the two resolved by the order the filesystem handed the compiler its files, with no diagnostic —
/// and arm64-linux emitted x86-64's syscall numbers (231/1/9 for 94/64/222). Because those numbers name
/// harmless syscalls on AArch64, a violated range guard did not abort; it fell through and the program
/// continued with a zero. File order is a property of the CHECKOUT, so which numbers a binary carried
/// depended on which machine compiled the compiler. Renaming the two tables fixed that instance; this
/// closes the class.
///
/// WHY IT IS A PROJECT CHECK AND NOT A COMPILER DIAGNOSTIC. Whether a Maxon PROGRAM may declare one
/// exported name in two files is a language question <c>/specs</c> has not settled —
/// <c>specs-shv2/global-file-private-same-name.md</c> specifies the FILE-PRIVATE case as legal and
/// says nothing about the exported one — so emitting an error from the compiler would decide it by
/// implementation. That decision is its own rung (A2d), gated by the whole C# suite. Refusing it in
/// THIS repository's own sources decides nothing about the language and still closes the class, which
/// is why this runs from <c>dotnet build</c> beside <c>maxon error-codes check</c> and never from a
/// user's <c>maxon build</c>.
///
/// WHAT IT ASKS, AND WHY THAT IS THE RULE. Measured against the bootstrap by building a two-file
/// project forwards and then with <c>MAXON_SOURCE_ORDER=reverse</c> (SourceCollector's own seam), for
/// every declaration kind.
///
/// ⚠ READ THE POSITIVE AND NEGATIVE ROWS DIFFERENTLY, because the instrument was HALF BLIND when they
/// were taken: until BATCH34 the build cache did not key the source ORDER, so a reversal over
/// untouched sources hit the cache and re-ran the FIRST order's binary (see
/// <see cref="BuildCache.SourceInputs.Sources"/>). A row reporting a DIFFERENCE is unaffected — a
/// cache hit cannot manufacture one — so every bullet below that names two outcomes stands. A row
/// reporting SAMENESS ("identically both ways", "byte-identical binaries both orders") may be
/// reporting the cache, and is a claim rather than a measurement until re-taken on a cold tree.
/// BATCH34 re-took the closest one and it did NOT hold: a file-private <c>typealias</c> shadowing a
/// foreign exported one of the same name answered <c>wide=112</c> one way and <c>wide=70000</c> the
/// other (<c>specs/typealias-collision.md</c>'s <c>exported-generic-alias-keeps-its-own-element</c>).
/// <list type="bullet">
/// <item><c>export let</c> — exit 22 forwards, 11 reversed. SILENT and order-dependent.</item>
/// <item><c>export var</c> — exit 22 forwards, 11 reversed. Same.</item>
/// <item><c>export type</c> — compiled one way, E4006 the other. Same.</item>
/// <item><c>export union</c> — same exit code, DIFFERENT binary: the case ordinals follow file order.</item>
/// <item><c>export typealias</c> — accepted one way, E3005 out-of-range the other.</item>
/// <item><c>export interface</c> — E3016 naming a different missing method each way.</item>
/// <item><c>module</c> visibility behaves exactly as <c>export</c> within its subtree, so it is in.
///   ⚠ And outside its subtree it behaves like <c>export</c> for STORAGE, which is the half this
///   list did not say — see <see cref="AnyPairIsMutuallyVisible"/>.</item>
/// <item><c>export function</c> — E3006 <c>Duplicate function</c> BOTH ways. Already loud, so it is
///   OUT: this gate is for the silent class, and a second refusal of the same thing would be a second
///   place to keep the rule.</item>
/// <item>A name shared by a VALUE and a FUNCTION (<c>export let Thing</c> + <c>export function
///   Thing()</c>) resolved correctly and identically both ways — separate namespaces, not a collision.
///   Hence <see cref="DeclarationNamespace"/>: names are compared within a namespace, never across.</item>
/// <item>A file-private declaration is never in scope here, and a file's own private declaration
///   shadowing a foreign exported one of the same name resolved identically both ways.</item>
/// </list>
///
/// EQUAL VALUES ARE STILL REFUSED. All four collisions this check found on the tree it was written
/// against were byte-identical redeclarations, so nothing was wrong TODAY — which is the whole trap:
/// the failure mode is silence, and two declarations that agree now diverge on the day someone edits
/// one of them. There is no exemption for agreement.
///
/// WHAT IT DOES NOT ASK — deliberately, and measured. A project name colliding with a <c>stdlib/</c>
/// name is a different case: <c>stdlib/</c> is compiled into every program, but a project
/// <c>type</c>/<c>enum</c>/<c>union</c> SHADOWS the stdlib one deterministically (byte-identical
/// binaries both orders), which is shadowing rather than the order-dependent class above. A project
/// <c>typealias</c> against a stdlib <c>typealias</c> DOES resolve by order (E3005 one way, E3010 the
/// other) and so is real, unclosed exposure — it is left to its own rung because closing it means
/// renaming <c>maxon-shv2</c>'s <c>ParseError</c> (847 occurrences) away from <c>stdlib</c>'s.
/// </summary>
public static class FlatNamespaceCheck {
  /// <summary>
  /// Which flat namespace a top-level name occupies. Measured, not assumed: an exported value and an
  /// exported function of one name coexist and resolve correctly, so a name is only ever compared with
  /// declarations of its own kind of thing.
  /// </summary>
  public enum DeclarationNamespace {
    /// <summary><c>type</c>, <c>enum</c>, <c>union</c>, <c>interface</c>, <c>typealias</c> — one
    /// registry (<c>IrModule.TypeDefs</c>), so all five collide with each other.</summary>
    Type,

    /// <summary>Top-level <c>let</c>/<c>var</c>. A <c>let</c> and a <c>var</c> share one storage key,
    /// so mutability does not separate them.</summary>
    Value,
  }

  /// <summary>
  /// One compilation unit of this repository: a directory <c>maxon build</c> is pointed at.
  /// <see cref="NotCheckedBecause"/> is non-null exactly for a unit deliberately left out, and the
  /// reason travels with the row so it cannot become folklore.
  /// </summary>
  public sealed record Unit(string RelativeDir, string? NotCheckedBecause);

  /// <summary>
  /// The units, in the order the report walks them.
  ///
  /// <see cref="Check"/> proves this table is COMPLETE rather than trusting it: every directory in the
  /// tree holding a <c>build.maxon</c> that exports a <c>build()</c> must appear here. A new Maxon
  /// project therefore cannot join the repository and quietly escape the gate — the failure is the
  /// build's, not a future reader's.
  /// </summary>
  static readonly Unit[] Units = [
    new("maxon-shv2", null),
    new("maxon-dev-mcp/mcp", null),
    new("maxon-selfhosted", "deprecated and does not compile (see .claude/CLAUDE.md); nothing in the "
      + "repository builds it, so gating a build on its hygiene could only manufacture work"),
  ];

  /// <summary>One declaration, kept whole — including the LOSER of a collision, which every
  /// name-keyed map in the compiler has already discarded by the time it could be asked.</summary>
  sealed record Site(string Name, DeclarationNamespace Namespace, string File, int Line, int Column, bool IsExported);

  const string CheckVerb = "check";
  const string ListVerb = "list";

  /// <summary>Entry point for <c>maxon flat-namespace &lt;check|list&gt; [root]</c>.</summary>
  public static int Run(string[] args) {
    if (args.Length == 0 || (args[0] != CheckVerb && args[0] != ListVerb)) {
      Console.Error.WriteLine($"Usage: maxon flat-namespace <{CheckVerb}|{ListVerb}> [repo-root]");
      Console.Error.WriteLine();
      Console.Error.WriteLine("  check     verify that no two files of one of this repository's Maxon");
      Console.Error.WriteLine("            projects declare the same top-level name with cross-file");
      Console.Error.WriteLine("            visibility (`export` or `module`)");
      Console.Error.WriteLine("  list      print every name `check` compares, one per line, so its view");
      Console.Error.WriteLine("            can be reconciled against the tree by anything else");
      return 1;
    }

    string root;
    try {
      root = args.Length > 1
        ? Path.GetFullPath(args[1])
        : ErrorCodeRegistry.FindRoot(Directory.GetCurrentDirectory());
    } catch (ErrorCodeRegistryException ex) {
      Console.Error.WriteLine($"flat-namespace: {ex.Message}");
      return 1;
    }

    return args[0] == ListVerb ? List(root) : Check(root);
  }

  /// <summary>
  /// Print exactly what <see cref="Check"/> compares. A gate whose input cannot be enumerated cannot
  /// be shown to have SEEN anything — and a missing declaration is silent by construction, which is
  /// the same failure the gate exists to stop one level down. Reconciling this against a
  /// <c>grep '^export'</c> over the same trees is what proved the type namespace complete and found
  /// the two `let`s the value walk was missing.
  /// </summary>
  static int List(string root) {
    foreach (var unit in Units) {
      if (unit.NotCheckedBecause != null) {
        Console.WriteLine($"# {unit.RelativeDir}: not checked - {unit.NotCheckedBecause}");
        continue;
      }

      var dir = Path.Combine(root, unit.RelativeDir.Replace('/', Path.DirectorySeparatorChar));
      foreach (var site in CollectSites(SourceCollector.FromDirectory(dir))
          .OrderBy(s => s.Namespace)
          .ThenBy(s => s.Name, StringComparer.Ordinal))
        Console.WriteLine($"{site.Namespace.ToString().ToLowerInvariant()}\t{site.Name}\t"
          + $"{Relative(root, site.File)}:{site.Line}:{site.Column}");
    }

    return 0;
  }

  public static int Check(string root) {
    var untabled = UntabledProjectDirs(root);
    if (untabled.Count > 0) {
      Console.Error.WriteLine("flat-namespace: these Maxon projects are not in FlatNamespaceCheck.Units:");
      foreach (var d in untabled) Console.Error.WriteLine($"  {d}");
      Console.Error.WriteLine("Add a row for each — with a reason if it is deliberately not checked.");
      return 1;
    }

    var failed = false;
    var checkedUnits = 0;
    var scannedFiles = 0;
    // Reported per namespace, not as one total: the number is the only evidence that the check SAW
    // the declarations it claims to have compared, and a single figure cannot be reconciled against
    // the tree by anyone auditing it. Splitting them is what localized the miscount that found the
    // `SkipToMatchingEnd` defect — the type column reconciled exactly and the value column was two
    // short, which is a question about the value walk and nothing else.
    var scannedTypeNames = 0;
    var scannedValueNames = 0;

    foreach (var unit in Units) {
      if (unit.NotCheckedBecause != null) continue;

      var dir = Path.Combine(root, unit.RelativeDir.Replace('/', Path.DirectorySeparatorChar));
      if (!Directory.Exists(dir)) {
        Console.Error.WriteLine($"flat-namespace: {unit.RelativeDir} is in the unit table but does not exist");
        return 1;
      }

      var sources = SourceCollector.FromDirectory(dir);
      if (sources.Length == 0) {
        Console.Error.WriteLine($"flat-namespace: {unit.RelativeDir} has no .maxon sources to check");
        return 1;
      }

      var sites = CollectSites(sources);
      checkedUnits++;
      scannedFiles += sources.Length;
      scannedTypeNames += sites.Count(s => s.Namespace == DeclarationNamespace.Type);
      scannedValueNames += sites.Count(s => s.Namespace == DeclarationNamespace.Value);
      if (ReportCollisions(unit.RelativeDir, root, sites)) failed = true;
    }

    if (failed) {
      Console.Error.WriteLine(
        "Maxon's top-level namespace is FLAT: a file's directory grants it nothing, and two "
        + "cross-file declarations of one name");
      Console.Error.WriteLine(
        "resolve by the order the filesystem lists the files, with no diagnostic. Give one of each pair "
        + "a distinct name, or");
      Console.Error.WriteLine("delete the redeclaration and let both files read the surviving one.");
      return 1;
    }

    Console.WriteLine($"flat-namespace: OK - {scannedTypeNames} type + {scannedValueNames} value "
      + $"cross-file top-level names across {scannedFiles} files in {checkedUnits} project(s), "
      + "no name declared twice");
    return 0;
  }

  /// <summary>
  /// Every top-level name the unit's files publish beyond their own file, from the compiler's OWN two
  /// definitions of what a top-level declaration is — <see cref="Compiler.PreRegisterTypeNames"/> for
  /// types and <see cref="Parser.WalkTopLevelValueDecls"/> for values. Neither is re-implemented here:
  /// a second scanner would be free to disagree about what a declaration is, and this file would be
  /// the last place anyone looked.
  /// </summary>
  static List<Site> CollectSites(SourceFile[] sources) {
    var sites = new List<Site>();

    foreach (var source in sources) {
      var tokens = new Lexer(source.Content).Tokenize();

      Compiler.PreRegisterTypeNames(new IrModule<MaxonOp>(), source, tokens, isStdlib: false, onDeclaration: d => {
        if (!d.IsExported && !d.IsModuleVisible) return;
        if (d.SourceFilePath == null) return;

        sites.Add(new Site(d.Name, DeclarationNamespace.Type, d.SourceFilePath, d.Line, d.Column, d.IsExported));
      });

      // EVERY conditional arm, so the verdict does not depend on the host: see the parameter's note.
      // `PreRegisterTypeNames` above needs no such flag — it never selects an arm, so the type
      // namespace already sees all of them.
      new Parser(tokens, sourceFilePath: source.Path, rootPath: source.RootPath)
        .WalkTopLevelValueDecls(everyConditionalArm: true, d => {
          if (!d.IsExported && !d.IsModuleVisible) return;

          sites.Add(new Site(d.Name, DeclarationNamespace.Value, source.Path, d.Line, d.Column, d.IsExported));
        });
    }

    return sites;
  }

  /// <summary>
  /// Report every (namespace, name) declared by more than one FILE, and say whether any was found.
  /// Same-file repeats are not this check's business: the compiler already refuses them per file
  /// (E3006 <c>duplicate definition</c>), and <c>#if</c>/<c>#else</c> arms legitimately declare one
  /// name twice in one file.
  /// </summary>
  static bool ReportCollisions(string unitName, string root, List<Site> sites) {
    var found = false;

    foreach (var group in sites
        .GroupBy(s => (s.Namespace, s.Name))
        .OrderBy(g => g.Key.Name, StringComparer.Ordinal)) {
      var perFile = group
        .GroupBy(s => s.File, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())
        .OrderBy(s => s.File, StringComparer.OrdinalIgnoreCase)
        .ToList();
      if (perFile.Count < 2) continue;
      if (!AnyPairIsMutuallyVisible(perFile)) continue;

      found = true;
      Console.Error.WriteLine(
        $"flat-namespace: {unitName}: '{group.Key.Name}' is declared in {perFile.Count} files "
        + $"({group.Key.Namespace.ToString().ToLowerInvariant()} namespace):");
      foreach (var site in perFile)
        Console.Error.WriteLine($"  {Relative(root, site.File)}:{site.Line}:{site.Column}: {SourceLine(site)}");
    }

    return found;
  }

  /// <summary>
  /// Do at least two of these declarations actually see each other? An <c>export</c> is visible
  /// program-wide, so it collides with anything of its name. Two <c>module</c> declarations are
  /// mutually NAMEABLE only where their directory subtrees overlap, which is
  /// <see cref="AliasScope.IsInDirectoryScopeOf"/>'s rule and not restated here.
  ///
  /// ⚠ "SIBLING SUBTREES EACH KEEP THEIR OWN NAME" IS WHAT THIS USED TO SAY, AND IT IS FALSE. Being
  /// unable to NAME each other's declaration is not the same as each having its own; the module's
  /// type tables are keyed by bare name and hold ONE entry per name program-wide, so two
  /// <c>module</c> declarations in DISJOINT subtrees still share it. Measured (BATCH34's
  /// <c>module-alias-does-not-govern-another-directory</c>): <c>scopeB/</c>'s
  /// <c>Cell = int(0 to 255)</c> truncated <c>scopeA/</c>'s 70000 to 112, and reversing the file
  /// order gave the other answer. <c>module</c> governs who may WRITE a name down, never who decides
  /// its STORAGE.
  ///
  /// The check still does not report that pair, and the reason is the one this file opens with
  /// rather than the false premise: whether it is legal is a language question (rung A2d), and the
  /// compiler-side cure for the storage half already landed — a contested <c>module</c> alias is
  /// renamed per declarer, so each subtree's own code is correct. What remains is a READER in one of
  /// those subtrees, which is a scoping change to the alias tables and not a name-collision report.
  /// </summary>
  static bool AnyPairIsMutuallyVisible(List<Site> perFile) {
    if (perFile.Any(s => s.IsExported)) return true;

    for (var i = 0; i < perFile.Count; i++)
      for (var j = i + 1; j < perFile.Count; j++)
        if (AliasScope.IsInDirectoryScopeOf(perFile[i].File, perFile[j].File)
            || AliasScope.IsInDirectoryScopeOf(perFile[j].File, perFile[i].File))
          return true;

    return false;
  }

  /// <summary>
  /// The declaration as WRITTEN. A collision report has to name both files and both VALUES — the whole
  /// defect was two spellings of one name over different numbers — and the source line carries the
  /// value, the kind and the visibility at once, with nothing re-derived from a model of them.
  /// </summary>
  static string SourceLine(Site site) {
    var lines = File.ReadAllLines(site.File);

    // Throws rather than printing a placeholder: the line number came from a token of THIS file, so
    // being outside it means the scan and the file disagree, and a report that quietly says
    // "<line not found>" where the colliding VALUE belongs is a report that has stopped doing the
    // one thing it is for.
    if (site.Line < 1 || site.Line > lines.Length)
      throw new InvalidOperationException(
        $"flat-namespace: {site.File} has {lines.Length} lines but '{site.Name}' was scanned at line {site.Line}");

    return lines[site.Line - 1].Trim();
  }

  static string Relative(string root, string path) =>
    Path.GetRelativePath(root, path).Replace('\\', '/');

  /// <summary>
  /// Maxon projects in the tree that <see cref="Units"/> does not mention. A project is a directory
  /// holding a <c>build.maxon</c> that exports a <c>build()</c> — the same test <c>maxon build</c>
  /// applies when it decides whether a directory has a build manifest to run.
  ///
  /// THE DOMAIN IS THE REPOSITORY'S OWN CONTENT, which is why a gitignored path is not a candidate:
  /// a directory a tool in this tree WROTE is not a project this table has to name. That was not
  /// always so, and it failed <c>dotnet build</c> on a clean checkout — see
  /// <see cref="SourceCollector.RepositoryIgnoredPaths"/> for the case and for why the answer is
  /// git's rather than a fifth exclusion list here.
  /// </summary>
  static List<string> UntabledProjectDirs(string root) {
    var tabled = Units
      .Select(u => Path.GetFullPath(Path.Combine(root, u.RelativeDir.Replace('/', Path.DirectorySeparatorChar))))
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // One git query for the whole candidate list rather than one per manifest.
    var manifests = Directory
      .GetFiles(root, SourceCollector.BuildManifestFileName, SearchOption.AllDirectories)
      .Select(SourceCollector.NormalizePath)
      .ToList();
    var ignored = SourceCollector.RepositoryIgnoredPaths(root, manifests);

    var untabled = new List<string>();
    foreach (var manifest in manifests) {
      // Kept alongside the ignore query rather than folded into it: this one is the COMPILER's own
      // structural rule about its output directory, true of any tree anywhere, and it still holds
      // when git cannot be asked. The ignore query is about THIS repository's content.
      if (SourceCollector.IsInCompilerOutputDir(manifest)) continue;
      if (ignored.Contains(manifest)) continue;
      if (IsInNestedCheckout(root, manifest)) continue;
      if (!DeclaresBuildFunction(manifest)) continue;

      var dir = Path.GetFullPath(Path.GetDirectoryName(manifest)!);
      if (!tabled.Contains(dir)) untabled.Add(Relative(root, manifest));
    }

    return untabled;
  }

  /// <summary>
  /// Whether this path lies inside a checkout OTHER than <paramref name="root"/> — a git worktree, a
  /// vendored clone, any tree carrying its own <c>.git</c>.
  /// </summary>
  /// <remarks>
  /// <para>
  /// ⚠ <b>THIS EXCLUSION IS NOT COSMETIC: WITHOUT IT THE GATE FAILS THE BUILD WHENEVER AN AGENT WORKTREE
  /// EXISTS</b>, which is the rung workflow's normal state — <c>.claude/worktrees/&lt;slice&gt;/</c> holds a
  /// full checkout, so its <c>maxon-shv2/build.maxon</c> and siblings read as projects
  /// <see cref="Units"/> has never heard of. Measured 2026-07-31, integrating A1x: three "untabled
  /// projects" reported, <c>dotnet build</c> exit 1, and nothing wrong with the tree at all.
  /// </para>
  /// <para>
  /// The test is <b>a nested <c>.git</c></b> rather than the literal path <c>.claude/worktrees</c>,
  /// because what disqualifies a directory is that it belongs to a DIFFERENT checkout, not where it
  /// happens to sit. A worktree's <c>.git</c> is a FILE and a clone's is a DIRECTORY, so both are tested.
  /// Adding rows to <see cref="Units"/> for a worktree would be worse than useless: the rows would name
  /// paths that vanish when the slice merges.
  /// </para>
  /// <para>
  /// It now OVERLAPS <see cref="SourceCollector.RepositoryIgnoredPaths"/> — <c>.claude/worktrees/</c> is
  /// gitignored, so the agent-worktree case is answered twice — and it is kept because the two answers
  /// are not the same answer. A checkout vendored under a TRACKED path is a different repository while
  /// being repository content, and this is the only test that sees it; and when git cannot be asked at
  /// all, this one still holds.
  /// </para>
  /// </remarks>
  static bool IsInNestedCheckout(string root, string path) {
    var rootFull = Path.GetFullPath(root);
    var dir = Path.GetDirectoryName(Path.GetFullPath(path));

    while (dir != null && !string.Equals(dir, rootFull, StringComparison.OrdinalIgnoreCase)) {
      var marker = Path.Combine(dir, ".git");
      if (File.Exists(marker) || Directory.Exists(marker)) return true;

      dir = Path.GetDirectoryName(dir);
    }

    return false;
  }

  /// <summary>
  /// Whether this manifest exports the build function, decided at TOKEN level.
  ///
  /// This is deliberately NOT the test <c>maxon build</c> applies — that one compiles the manifest
  /// and looks for <see cref="SourceCollector.BuildFunctionName"/> among the resulting module's
  /// exported functions (<c>Program.cs</c>), which is stronger and is the right test THERE. It
  /// cannot be the test here: this check's whole job is to run from <c>dotnet build</c> over a tree
  /// whose Maxon projects may not compile at that moment, and a completeness proof that first
  /// requires every candidate project to compile would go dark exactly when someone is mid-edit.
  /// So the two tests differ on purpose, and what they SHARE — the name they look for — is spelled
  /// once, beside the manifest's file name.
  ///
  /// The consequence of the weaker test is bounded in the safe direction: it can only over-report a
  /// directory as a project, and over-reporting fails the build asking for a <see cref="Units"/>
  /// row, which is loud. It cannot silently let a project escape the table.
  /// </summary>
  static bool DeclaresBuildFunction(string manifestPath) {
    var tokens = new Lexer(File.ReadAllText(manifestPath)).Tokenize();
    for (var i = 0; i + 2 < tokens.Count; i++)
      if (Parser.IsVisibilityModifierAt(tokens, i)
          && tokens[i + 1].Type == TokenType.Function
          && tokens[i + 2].Type == TokenType.Identifier
          && tokens[i + 2].Value == SourceCollector.BuildFunctionName)
        return true;

    return false;
  }
}
