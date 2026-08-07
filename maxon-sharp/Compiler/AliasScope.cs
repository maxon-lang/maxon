using MaxonSharp.Compiler.Ir.Core;

namespace MaxonSharp.Compiler;

/// <summary>
/// How far a typealias declaration reaches — the one fact three separate questions about a
/// declaration are answers to.
///
/// ⚠ THE MEMBER ORDER IS LOAD-BEARING AND IS COMPARED WITH <c>&gt;</c>, at the record guard in
/// <c>Parser.CopyTypeAliasesToModule</c>: the WIDEST declaration owns the module's single
/// record for a name. Declared narrowest-first for that reason. Reordering or inserting a member
/// changes which declaration wins that record, and nothing would fail to compile — it was a private
/// enum beside its own comparison until BATCH34 moved it here, so the dependency is now across files
/// and this note is the only thing carrying it.
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
/// it reaches, whether it belongs to the library layer, and which file wrote it. Carried as a record
/// so a caller cannot transpose two <c>bool</c>s and a path and be told nothing.
/// </summary>
/// <param name="File">The declaring file's path, from which its directory — and therefore whether it
/// is nearer than the other declaration — is derived.</param>
public readonly record struct AliasSite(AliasReach Reach, bool IsStdlib, string File) {
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
  public static AliasSite Of(TypeAliasInfo info) =>
    info.SourceFilePath == null
      ? throw new InvalidOperationException(
          $"typealias record for '{info.SourceTypeName}' has no declaring file, so its scope cannot be placed")
      : new(AliasScope.ReachOf(info), info.IsStdlib, info.SourceFilePath);
}

/// <summary>
/// ⭐ WHERE A TYPEALIAS NAME MEANS SOMETHING, ASKED ONCE.
///
/// Three questions the compiler used to answer separately, each with its own reading of visibility,
/// are all this one:
/// <list type="bullet">
/// <item><b>May this declaration be RENAMED under a contest?</b> Only if nothing beyond the contest
///   resolves it by name — <see cref="AliasReach.Program"/> is the one reach that says no.</item>
/// <item><b>Does it PUBLISH its bare name whole-program?</b> Everything except
///   <see cref="AliasReach.File"/>, which has no reader to serve.</item>
/// <item><b>Are two declarations of one name AMBIGUOUS?</b> <see cref="AreAmbiguous"/>.</item>
/// </list>
/// They were three readings of one fact, and this rung is about exactly that shape.
///
/// ⚠ THE THIRD ONE IS WHY THE OTHER TWO ARE NOT ENOUGH. A declaration this compiler cannot rename is
/// one whose storage the FLAT name-keyed type tables decide, and those tables hold one entry per bare
/// name — so where two such declarations of one name exist, the one that merged last silently takes
/// the other's storage. MEASURED over 3 visibilities × 3 visibilities × 2 source orders, with two
/// ONE-BYTE ranges that disagree on signedness so neither value survives the other's storage
/// (<c>int(0 to 255)</c> holding 200, <c>int(-100 to 100)</c> holding −50): four cells answered
/// <c>a=-56</c> or <c>b=206</c>, silently, exit 0 — every one of them a pair where at least one side
/// is <c>export</c> and the other reaches past its own file.
/// </summary>
public static class AliasScope {
  /// <summary>
  /// ⚠ <c>stdlib</c> IS DELIBERATELY NOT AN INPUT. A stdlib alias's reach is what it wrote down, the
  /// same as anyone's. That a plain stdlib <c>typealias</c> is nevertheless SEEDED into every parser
  /// is an implementation fact about seeding, not about the language — and it is the reason a stdlib
  /// declaration may not be RENAMED, which is a separate question asked at
  /// <c>Parser.IsFileScopedAliasDeclaration</c>. Folded in here it said that <c>Json.maxon</c>'s
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
  /// ⚠ ONE FACT, AND IT WAS SPELLED TWICE — as <c>&amp;&amp; !_isStdlib</c> in
  /// <c>Parser.IsFileScopedAliasDeclaration</c> (a stdlib alias may not be RENAMED under a contest)
  /// and as <c>isStdlib ? Program</c> in <c>Parser.RecordOwnershipReach</c> (a stdlib alias OWNS the
  /// module record as widely as an export). Both are the same sentence about seeding, and each
  /// carried its own prose saying so, which is how one of them gets edited alone.
  ///
  /// It is deliberately NOT <see cref="ReachOf"/>: that is the reach the DECLARATION wrote down, and
  /// folding seeding into it said <c>Json.maxon</c>'s file-private <c>BytePos</c> reaches the whole
  /// program and is ambiguous against <c>String.maxon</c>'s exported one — which refused the entire
  /// standard library, on a rule whose own first exemption is that a file-private declaration never
  /// participates.
  /// </summary>
  public static AliasReach ReachOfSeeded(bool isExported, bool isModuleVisible, bool isStdlib) =>
    isStdlib ? AliasReach.Program : ReachOf(isExported, isModuleVisible);

  public static AliasReach ReachOfSeeded(TypeAliasInfo info) =>
    ReachOfSeeded(info.IsExported, info.IsModuleVisible, info.IsStdlib);

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
  ///   ⚠ THAT IS CANONICAL'S MODEL, AND THIS COMPILER DOES NOT IMPLEMENT IT — the exemption is
  ///   inherited, not earned. The alias tables are keyed by bare name and hold ONE entry, so the
  ///   DEEPER declaration takes the name everywhere, including inside the ENCLOSING declarer's own
  ///   file. MEASURED at this tip, and identically at the rung's merge base (369e8c812b), so the
  ///   rung neither caused it nor cures it: <c>Compiler/types.maxon</c>'s
  ///   <c>export typealias Tally = int(0 to 100000)</c> with <c>Tallies = Array with Tally</c> read
  ///   its own 70000 back as <b>112</b> through <c>Compiler/Coverage/</c>'s
  ///   <c>int(0 to 255)</c> — exit 0, silent. The committed case cannot see it: it casts 42, a value
  ///   BOTH ranges hold. Refusing the pair is not the cure, because canonical explicitly blesses it;
  ///   the cure is giving those tables a scope, which is the same cure the disjoint-<c>module</c>
  ///   reader case in <c>Parser.IsFileScopedAliasDeclaration</c> is waiting on.</item>
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

  private static string? NormalizeDir(string path) {
    string fullPath;
    try { fullPath = Path.GetFullPath(path); } catch { fullPath = path; }
    var dir = Path.GetDirectoryName(fullPath);
    if (dir == null) return null;

    return dir.Replace('\\', '/').TrimEnd('/');
  }
}
