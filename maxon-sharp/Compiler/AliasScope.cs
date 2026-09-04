using MaxonSharp.Compiler.Ir.Core;

namespace MaxonSharp.Compiler;

/// <summary>
/// How far a typealias declaration reaches — the one fact three separate questions about a
/// declaration are answers to.
///
/// ⚠ THE MEMBER ORDER IS LOAD-BEARING AND IS COMPARED WITH <c>&gt;</c> BY TWO READERS. Declared
/// narrowest-first for that reason; reordering or inserting a member changes what both decide, and
/// nothing would fail to compile — it was a private enum beside its own comparison until BATCH34
/// moved it here, so the dependency is now across files and this note is the only thing carrying it.
/// <list type="bullet">
/// <item>the record guard in <c>Parser.CopyTypeAliasesToModule</c>: the WIDEST declaration owns the
///   module's single record for a name;</item>
/// <item>the last rank in <see cref="AliasScope.CompareNearness"/>: of two declarations nothing else
///   separates, the one that PUBLISHES its name governs the reader.</item>
/// </list>
/// </summary>
public enum AliasReach {
  /// <summary>A plain <c>typealias</c>: its own file and nowhere else.</summary>
  File,

  /// <summary>A <c>module typealias</c>: its declaring directory and every directory below it.</summary>
  Subtree,

  /// <summary>An <c>export typealias</c>, or any stdlib alias: the whole program.</summary>
  Program,
}

/// <summary>
/// One declaration of a typealias name, reduced to exactly what deciding a collision needs: how far
/// it reaches, whether it belongs to the library layer, which file wrote it — and WHAT IT MEANS.
/// Carried as a record so a caller cannot transpose two <c>bool</c>s and a path and be told nothing.
///
/// ⭐ <see cref="DeclaredType"/> IS HERE RATHER THAN IN A TABLE BESIDE THIS ONE, and that is the
/// whole of W48's shape. Choosing between two declarations and then fetching what the winner MEANS
/// are one question; asked of two maps they can answer about different declarations, which is
/// precisely the defect — <c>IrModule.TypeDefs</c> holds ONE type per bare name, so an enclosing
/// directory's file had its storage decided by a declaration in a directory BENEATH it, silently
/// (<c>outer=112</c> for its own 70000) or as a range-check panic, depending only on whether a check
/// caught the truncation.
/// </summary>
/// <param name="File">The declaring file's path, from which its directory — and therefore whether it
/// is nearer than the other declaration — is derived.</param>
/// <param name="DeclaredType">The type THIS declaration binds the name to. Null only where the
/// recording path does not hold one (a bare name a file-private contested mint deliberately withheld
/// from the module's type table); a reader that finds none falls back to the flat entry, which is
/// exactly the pre-W48 answer and never worse than it.</param>
public readonly record struct AliasSite(AliasReach Reach, bool IsStdlib, string File, IrType? DeclaredType) {
  /// <summary>
  /// The site a module's alias RECORD describes. The path is taken from the record rather than
  /// accepted beside it: both callers passed <c>info.SourceFilePath</c> and nothing made them, so a
  /// third caller could hand this a reach and a stdlib flag from one declaration and a path from
  /// another — one fact from two hands, in the file whose subject is exactly that.
  ///
  /// Throws rather than returning a site with an empty path: a record with no declaring file cannot
  /// be positioned against another declaration at all, and every caller already has to decide what
  /// to do about that before it asks.
  /// </summary>
  public static AliasSite Of(TypeAliasInfo info, IrType? declaredType) =>
    info.SourceFilePath == null
      ? throw new InvalidOperationException(
          $"typealias record for '{info.SourceTypeName}' has no declaring file, so its scope cannot be placed")
      : new(AliasScope.ReachOf(info), info.IsStdlib, info.SourceFilePath, declaredType);
}

/// <summary>
/// ⭐ WHERE A TYPEALIAS NAME MEANS SOMETHING, ASKED ONCE.
///
/// Questions the compiler used to answer separately, each with its own reading of visibility, are all
/// this one:
/// <list type="bullet">
/// <item><b>Does a declaration PUBLISH its bare name whole-program?</b> Everything except
///   <see cref="AliasReach.File"/>, which has no reader to serve.</item>
/// <item><b>Are two declarations of one name AMBIGUOUS?</b> <see cref="AreAmbiguous"/>.</item>
/// <item><b>WHICH declaration governs a given READING FILE?</b>
///   <see cref="NearestDeclarationFor"/> — the question the flat, bare-name-keyed type tables
///   structurally cannot answer, and the one W48 added.</item>
/// </list>
///
/// ⭐ A FOURTH ONE USED TO BE HERE AND IS GONE: "may this declaration be RENAMED under a contest?",
/// answered as <c>reach != Program</c>. Its premise was that a declaration a foreign file may NAME is
/// one that file resolves BY NAME, so renaming it would break a reader doing nothing wrong. The
/// question above retired that premise — a reader no longer resolves by bare name — and with it the
/// one contest that had no cure at all: two <c>export</c> declarations of one name, a shape
/// <c>/specs/typealias-collision.md</c>'s strict-enclosure rule BLESSES, so refusing it was never
/// available. Renameability is now about who OWNS the name (see
/// <c>Parser.IsRenameableAliasDeclaration</c>), not about how far it reaches.
///
/// ⚠ WHY THE FLAT TABLES ARE NOT ENOUGH, MEASURED. Those tables hold one entry per bare name, so of
/// two declarations of one name the one that merged last silently takes the other's storage. Over 3
/// visibilities × 3 visibilities × 2 source orders, with two ONE-BYTE ranges that disagree on
/// signedness so neither value survives the other's storage (<c>int(0 to 255)</c> holding 200,
/// <c>int(-100 to 100)</c> holding −50): four cells answered <c>a=-56</c> or <c>b=206</c>, silently,
/// exit 0. And an <c>outer/</c> file's own <c>export typealias Cell = int(0 to 100000)</c> read its
/// own 70000 back as <b>112</b> through an <c>outer/inner/</c> declaration.
/// </summary>
public static class AliasScope {
  /// <summary>
  /// ⚠ <c>stdlib</c> IS DELIBERATELY NOT AN INPUT. A stdlib alias's reach is what it wrote down, the
  /// same as anyone's. That a plain stdlib <c>typealias</c> is nevertheless SEEDED into every parser
  /// is an implementation fact about seeding, not about the language — and it is one half of the
  /// reason a stdlib declaration may not be RENAMED, which is a separate question asked at
  /// <c>Parser.IsRenameableAliasDeclaration</c>. Folded in here it said that <c>Json.maxon</c>'s
  /// file-private <c>BytePos</c> reaches the whole program and is therefore ambiguous against
  /// <c>String.maxon</c>'s exported one — which refused the ENTIRE STANDARD LIBRARY, on a rule whose
  /// own first exemption is that a file-private declaration never participates.
  /// </summary>
  public static AliasReach ReachOf(bool isExported, bool isModuleVisible) =>
    isExported ? AliasReach.Program
    : isModuleVisible ? AliasReach.Subtree
    : AliasReach.File;

  public static AliasReach ReachOf(TypeAliasInfo info) =>
    ReachOf(info.IsExported, info.IsModuleVisible);

  /// <summary>
  /// How far a declaration reaches ONCE SEEDING IS COUNTED — the language's reach for anyone but the
  /// stdlib, and <see cref="AliasReach.Program"/> for a stdlib alias however it is written, because
  /// <c>SeedFromModule</c> hands every stdlib alias to every parser regardless of <c>export</c>.
  ///
  /// ⚠ ONE FACT, AND IT WAS SPELLED TWICE — as <c>&amp;&amp; !_isStdlib</c> in the mint's own guard
  /// (a stdlib alias may not be RENAMED under a contest) and as <c>isStdlib ? Program</c> in the
  /// record-ownership guard (a stdlib alias OWNS the module record as widely as an export). Both are
  /// the same sentence about seeding, and each carried its own prose saying so, which is how one of
  /// them gets edited alone. W48 left only the second reader here: renameability no longer asks about
  /// reach at all, so the mint's guard is now a bare <c>!_isStdlib</c> with its own reason.
  ///
  /// It is deliberately NOT <see cref="ReachOf"/>: that is the reach the DECLARATION wrote down, and
  /// folding seeding into it said <c>Json.maxon</c>'s file-private <c>BytePos</c> reaches the whole
  /// program and is ambiguous against <c>String.maxon</c>'s exported one — which refused the entire
  /// standard library, on a rule whose own first exemption is that a file-private declaration never
  /// participates.
  /// </summary>
  public static AliasReach ReachOfSeeded(AliasReach declared, bool isStdlib) =>
    isStdlib ? AliasReach.Program : declared;

  public static AliasReach ReachOfSeeded(bool isExported, bool isModuleVisible, bool isStdlib) =>
    ReachOfSeeded(ReachOf(isExported, isModuleVisible), isStdlib);

  public static AliasReach ReachOfSeeded(TypeAliasInfo info) =>
    ReachOfSeeded(info.IsExported, info.IsModuleVisible, info.IsStdlib);

  public static AliasReach ReachOfSeeded(AliasSite site) =>
    ReachOfSeeded(site.Reach, site.IsStdlib);

  /// <summary>
  /// ⭐ WHICH DECLARATION OF A TYPEALIAS NAME GOVERNS <paramref name="readerPath"/> — the one fact
  /// every reading file needs and the flat, bare-name-keyed type tables structurally cannot state.
  ///
  /// The tables hold ONE record per name, so of two declarations the one that merged last answered
  /// for every reader in the program. MEASURED at this rung's merge base: an <c>outer/</c> file's
  /// <c>export typealias Cell = int(0 to 100000)</c> read its own 70000 back as <b>112</b> through an
  /// <c>outer/inner/</c> declaration of the same name, silently, exit 0 — and the same shape with two
  /// one-byte ranges that disagree on signedness PANICKED instead, one defect wearing two faces. A
  /// <c>module</c> alias's own sibling was told <c>E2003 Unknown type</c> because the single
  /// <c>TypeDefSourceFiles</c> entry named the OTHER subtree's declarer.
  ///
  /// The rule is <c>/specs/typealias-collision.md</c>'s, read from the READER's side rather than as a
  /// relation between two declarations:
  /// <list type="number">
  /// <item>A declaration the reader cannot SEE is not a candidate — that is the reach, counted with
  ///   seeding (<see cref="ReachOfSeeded"/>), because a stdlib alias is handed to every parser
  ///   however it is written.</item>
  /// <item>THE READER'S OWN FILE WINS. It is the innermost scope there is, and it is what
  ///   <c>file-private-alias-still-governs-its-own-file</c> pins.</item>
  /// <item>A PROJECT declaration beats a STDLIB one — "seeded as a lower-precedence library layer"
  ///   (<c>project-export-shadows-stdlib-export</c>). Asked BEFORE enclosure on purpose: the stdlib
  ///   contains no project file and no project file contains it, so enclosure cannot separate them
  ///   and would fall through to a depth comparison between unrelated trees.</item>
  /// <item>STRICT ENCLOSURE. A declaration whose directory CONTAINS the reader outranks one that does
  ///   not, and among those that do, the DEEPEST governs — that is the nested-export rule stated from
  ///   the reader's side. Among those that do NOT contain the reader, canonical gives the reader the
  ///   ENCLOSING declaration ("the nearer declaration wins inside its own subtree and the enclosing
  ///   one everywhere else"), so there the SHALLOWEST governs.</item>
  /// <item>DECLARED REACH. Only the stdlib layer reaches this: <see cref="ReachOfSeeded"/> promotes a
  ///   stdlib file-private declaration to whole-program, so it arrives beside a stdlib
  ///   <c>export</c> of the same name with nothing above to separate them. The one that PUBLISHES
  ///   its name governs — see <see cref="CompareNearness"/> for what was measured without it.</item>
  /// </list>
  ///
  /// ⚠ THE ORDER IS TOTAL, DELIBERATELY. Two declarations no rule separates are the AMBIGUITY
  /// question — <see cref="AreAmbiguous"/> marks them and E3063 refuses every bare use — but this
  /// still has to answer, because a parser seeds its registry before it knows whether the name will
  /// be written. Left partial it would answer from dictionary enumeration order, which is file order,
  /// which is the defect this rung exists to close, re-entered one door along.
  /// </summary>
  public static AliasSite? NearestDeclarationFor(
      IReadOnlyDictionary<string, AliasSite> sites, string readerPath) {
    AliasSite? nearest = null;
    foreach (var site in sites.Values) {
      if (!IsVisibleTo(site, readerPath)) continue;
      if (nearest is { } incumbent && CompareNearness(site, incumbent, readerPath) < 0) continue;

      nearest = site;
    }

    return nearest;
  }

  /// <summary>
  /// Whether a file at <paramref name="readerPath"/> may write this declaration's bare name at all —
  /// the reach, counted with seeding, applied to one reader.
  /// </summary>
  private static bool IsVisibleTo(AliasSite site, string readerPath) => ReachOfSeeded(site) switch {
    AliasReach.Program => true,
    AliasReach.Subtree => IsInDirectoryScopeOf(site.File, readerPath),
    AliasReach.File => IsSameFile(site.File, readerPath),
    _ => throw new ArgumentOutOfRangeException(nameof(site), site.Reach,
      "Unhandled alias reach; every reach must state which readers it serves."),
  };

  /// Positive when <paramref name="candidate"/> governs the reader in preference to
  /// <paramref name="incumbent"/>. Never zero for two distinct declaring files: the ordinal path
  /// break makes the order total (see <see cref="NearestDeclarationFor"/>).
  private static int CompareNearness(AliasSite candidate, AliasSite incumbent, string readerPath) {
    int byOwnFile = IsSameFile(candidate.File, readerPath).CompareTo(IsSameFile(incumbent.File, readerPath));
    if (byOwnFile != 0) return byOwnFile;

    int byLayer = (!candidate.IsStdlib).CompareTo(!incumbent.IsStdlib);
    if (byLayer != 0) return byLayer;

    bool candidateEncloses = IsInDirectoryScopeOf(candidate.File, readerPath);
    if (candidateEncloses != IsInDirectoryScopeOf(incumbent.File, readerPath))
      return candidateEncloses ? 1 : -1;

    int candidateDepth = DirectoryDepth(candidate.File);
    int incumbentDepth = DirectoryDepth(incumbent.File);
    if (candidateDepth != incumbentDepth) {
      bool candidateIsDeeper = candidateDepth > incumbentDepth;
      return (candidateEncloses ? candidateIsDeeper : !candidateIsDeeper) ? 1 : -1;
    }

    // ⭐ A DECLARATION THAT PUBLISHES ITS NAME OUTRANKS ONE THAT DOES NOT, and only the STDLIB layer
    // ever gets here needing it. Two PROJECT declarations cannot reach this line as peers: a
    // file-private one is visible to its own file alone, and the reader's-own-file test above has
    // already settled that file. But ReachOfSeeded hands every stdlib alias to every parser however
    // it is written, so a stdlib file-private declaration and a stdlib `export` of one name arrive
    // here indistinguishable, at equal depth, and the ordinal break below gave the name to whichever
    // PATH sorted first. MEASURED, with `stdlib/Json.maxon`'s file-private `BytePos` narrowed to
    // `int(0 to 9)`: a project file's `BytePos` resolved to THAT one rather than to
    // `stdlib/String.maxon`'s `export typealias BytePos = int(0 to i64.max)`, and 5000 was refused
    // E3005. The four such names in today's stdlib — `Byte`, `ByteArray`, `BytePos`,
    // `StringArray` — are each written over identical ranges wherever they are declared, so the
    // wrong winner is invisible until one of them moves.
    //
    // ⚠ IT IS THE DECLARED reach, not the seeded one: seeding is what made the pair peers, so
    // reading it here would compare Program against Program and say nothing.
    if (candidate.Reach != incumbent.Reach) return candidate.Reach > incumbent.Reach ? 1 : -1;

    return string.CompareOrdinal(incumbent.File, candidate.File);
  }

  /// <summary>
  /// ⭐ WHETHER TWO DECLARATIONS OF ONE TYPEALIAS NAME LEAVE ANY FILE UNABLE TO SAY WHICH IT MEANS.
  ///
  /// Two declarations are ambiguous when their reaches INTERSECT and neither is strictly NEARER.
  /// Every exemption below is a rule <c>/specs/typealias-collision.md</c> already states, and each
  /// has a committed case behind it:
  /// <list type="bullet">
  /// <item><b>A file-private declaration never participates.</b> It is the innermost thing at its own
  ///   file and invisible everywhere else, so no file is ever left choosing
  ///   (<c>exported-typealias-file-private-doesnt-collide</c>, and the spec's line 28).</item>
  /// <item><b>stdlib is the outer layer.</b> A project declaration is strictly nearer than any stdlib
  ///   one, which is why a project may export a name stdlib exports
  ///   (<c>project-export-shadows-stdlib-export</c>: "seeded as a lower-precedence library layer, so
  ///   they never participate in cross-file ambiguity").</item>
  /// <item><b>Strict directory enclosure disambiguates.</b> The nearer declaration wins inside its own
  ///   subtree and the enclosing one everywhere else, so no file is left without a rule to apply
  ///   (<c>nested-export-shadowed-by-enclosing-dir</c>).
  ///   ⚠ THE EXEMPTION WAS INHERITED FOR MONTHS BEFORE IT WAS EARNED, and W48 is where it was earned.
  ///   The alias tables are keyed by bare name and held ONE entry, so the declaration that merged last
  ///   took the name everywhere — including inside the ENCLOSING declarer's own file: measured,
  ///   <c>outer/</c>'s <c>export typealias Cell = int(0 to 100000)</c> read its own 70000 back as
  ///   <b>112</b> through <c>outer/inner/</c>'s <c>int(0 to 255)</c>, exit 0, silent. The committed
  ///   case could not see it — it cast 42, a value BOTH ranges hold — and now carries a two-way
  ///   discriminator instead. Refusing the pair was never available, because canonical explicitly
  ///   blesses it; the cure is <see cref="NearestDeclarationFor"/>, which gives those tables a
  ///   scope.</item>
  /// <item><b>Two subtrees that do not contain one another never meet</b>, so two <c>module</c>
  ///   declarations in sibling directories are not ambiguous — no file can see both
  ///   (<c>module-alias-does-not-govern-another-directory</c>).</item>
  /// </list>
  ///
  /// ⚠ IT IS SYMMETRIC, DELIBERATELY, and that is the whole point. The pair is ill-formed or it is
  /// not; making the answer depend on which declaration the compiler happened to read first would be
  /// the defect this rung exists to close, wearing the fix's clothes. Two of the six cells it refuses
  /// answered CORRECTLY before it, purely by the luck of merge order.
  /// </summary>
  public static bool AreAmbiguous(AliasSite a, AliasSite b) {
    if (a.IsStdlib != b.IsStdlib) return false;
    if (a.Reach == AliasReach.File || b.Reach == AliasReach.File) return false;

    if (IsStrictlyInsideDirectoryOf(b.File, a.File) || IsStrictlyInsideDirectoryOf(a.File, b.File))
      return false;

    // Left last because it is the only test that can say NO for two same-reach declarations, and
    // saying no here means "no file can see both" rather than "one of them wins".
    return a.Reach == AliasReach.Program || b.Reach == AliasReach.Program
      || IsInDirectoryScopeOf(a.File, b.File) || IsInDirectoryScopeOf(b.File, a.File);
  }

  /// <summary>
  /// True if <paramref name="accessorPath"/> is in the same directory as <paramref name="declarerPath"/>
  /// or in any subdirectory of it. This is the heart of <c>module</c>-keyword visibility, and the
  /// reason <see cref="FlatNamespaceCheck"/> and the parser both call it rather than restating it:
  /// two <c>module</c> declarations of one name collide only where their subtrees overlap, and a
  /// second copy of that rule could say otherwise.
  /// </summary>
  public static bool IsInDirectoryScopeOf(string declarerPath, string accessorPath) {
    var declarerDir = NormalizeDir(declarerPath);
    var accessorDir = NormalizeDir(accessorPath);
    if (declarerDir == null || accessorDir == null) return false;
    if (string.Equals(declarerDir, accessorDir, StringComparison.OrdinalIgnoreCase)) return true;

    return accessorDir.StartsWith(declarerDir + "/", StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Whether <paramref name="innerPath"/> sits in a directory strictly BELOW
  /// <paramref name="outerPath"/>'s — which is what makes one declaration nearer than the other.
  /// Two files in the SAME directory are deliberately not in this relation: neither is nearer, so
  /// nothing disambiguates them.
  /// </summary>
  public static bool IsStrictlyInsideDirectoryOf(string innerPath, string outerPath) {
    var innerDir = NormalizeDir(innerPath);
    var outerDir = NormalizeDir(outerPath);
    if (innerDir == null || outerDir == null) return false;
    if (string.Equals(innerDir, outerDir, StringComparison.OrdinalIgnoreCase)) return false;

    return innerDir.StartsWith(outerDir + "/", StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Whether two paths name one file. Normalized through the same <see cref="Path.GetFullPath"/> and
  /// case rules as the directory predicates above, so "the reader's own declaration" and "a
  /// declaration in the reader's directory" cannot disagree about one pair of paths.
  /// </summary>
  private static bool IsSameFile(string a, string b) =>
    string.Equals(NormalizePath(a), NormalizePath(b), StringComparison.OrdinalIgnoreCase);

  /// How deeply nested a declaring file's DIRECTORY is. Only ever compared between two directories
  /// on one enclosure chain (see <see cref="CompareNearness"/>), where it is that chain's order.
  private static int DirectoryDepth(string path) {
    var dir = NormalizeDir(path);
    if (dir == null) return 0;

    return dir.Count(c => c == '/');
  }

  private static string NormalizePath(string path) {
    string fullPath;
    try { fullPath = Path.GetFullPath(path); } catch { fullPath = path; }

    return fullPath.Replace('\\', '/').TrimEnd('/');
  }

  private static string? NormalizeDir(string path) {
    var dir = Path.GetDirectoryName(NormalizePath(path));
    if (dir == null) return null;

    return dir.Replace('\\', '/').TrimEnd('/');
  }
}
