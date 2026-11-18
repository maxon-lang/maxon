# maxon lint reports a method nothing uses, and its spelling census counts only what can name a declaration

Class issue: #69 · Priority: P2

## Goal

`maxon lint` reports a method nothing uses, as it reports an unused top-level declaration, and its census counts only tokens that can name a declaration.

- **Done when:** the compiler and the stdlib lint clean under the new rule, with every finding either removed or used.
- **Not the goal:** a whole-program reachability analysis, or a finding a conformance or the compiler itself accounts for.
- **Between two designs that both satisfy a stage, take** a use decided from a resolved reference over one decided from a spelling.

## Stage order

One stage is one local commit, which ticks its box. A stage starts when every stage it waits on is ticked
here and every issue it waits on is closed. A `deferred` stage is skipped. The plan lands once, after its
last stage.

| Done | Stage | Rule it establishes | Waits on | Not beside | Needs a ruling |
|---|---|---|---|---|---|
| [ ] | 1 | A directive line's identifiers are not spellings: the raw and conditional spelling censuses share one walk that skips them |  |  |  |
| [ ] | 2 | The methods nothing uses are removed from the compiler, stdlib, tests and examples, so the method audit lands green |  |  | Open question 1: does lint report an unused method at all? (a) audit methods as designed, a breaking change to lint owing a RELEASE_NOTES.md entry; (b) keep the type as the unit and close #69 as working as specified; recommended (a) |
| [ ] | 3 | maxon lint reports a method nothing references: E3092 for an export method, E3219 for an unmarked one, audited by reference and never by spelling | 1, 2 |  | Open questions 1 to 3: (1) does lint report an unused method at all, (a) audit methods or (b) close #69 as working as specified; (2) the codes, (a) widen E3092 and E3219 or (b) mint two; (3) an unmarked method of a public type in a library, (a) audited like any unmarked declaration or (b) exempt with its type; recommended (a) for all three |

## Problem

Dead and repeated declarations in the compiler are found by a reviewer reading, because the two
unused-declaration audits stop at the top level and one of them over-counts.

- **A method is never a finding.** `UnusedExportCheck` (E3092) skips every function
  `namesAMethodOnATrackedType` answers for: "the type owns the diagnostic". `UnusedUnexportedCheck` (E3219)
  seeds only what `declaresAFreeFunction` answers for, and counts a method as a root that keeps other
  declarations live. `specs/unused-export.md` pins the first ("an internal helper method of a public type
  is not reported") and `specs/unused-file-private-declaration.md` the second. So a method no body calls,
  exported or not, compiles and lints clean for as long as its type is used.
  `ProgramSignatures.aggregateNameIsBuiltOnATypeParameter` was one: exported, no caller, reported by
  nothing, removed by hand.
- **A directive's own condition counts as a spelling.** E3219 credits a candidate as live when its name is
  spelled in a conditional arm the target excludes: `creditNamesSpelledOutsideTheActiveArms` marks it live
  when the raw stream's count of the name exceeds the active stream's. `QueryDatabase.conditionalSpellingsIn`
  leaves the identifiers on a `#if` line out of the spellings it collects, but `rawSpellingCountsIn`, which
  produces the count compared, walks every name token and includes them. A name spelled on a directive
  line therefore reads as "spelled in an excluded arm" and its declaration is marked live.

The shared mechanism: "is this declaration used" is answered only for top-level declarations, and by two
audits that seed and count differently, so everything below the top level is left to review.

## Design

**`maxon lint` reports a method nothing uses, as it reports a top-level declaration nothing uses.**

1. **The census counts what can name a declaration.** `rawSpellingCountsIn` skips the tokens of a
   directive line, the same tokens `conditionalSpellingsIn` already skips, through one shared walk so the
   two cannot differ again.
2. **A method is audited by reference, never by spelling.** Method names repeat across types (`create`,
   `count`), so the spelling census E3219 uses for top-level names would credit every `create` whenever
   one is written. A method is used when a built body's resolved callee names it, when it is taken as a
   function value, when a conformance the type declares requires it (its witness), or when the compiler
   calls it by name (the hooks a roster already lists: construction, iteration, interpolation, a service's
   messages). Rust's `dead_code` and staticcheck's `unused` draw the same line: a method that satisfies an
   interface its type implements is live with the conformance, and every other method needs a reference.
3. **Its own marker sets the question, as for a top-level declaration.** A method carries its own
   visibility. A `public` method is API surface and is not audited. An `export` method that nothing
   outside its declaring directory's subtree references is E3092, repaired by dropping `export`. An
   unmarked method nothing references is E3219, repaired by deleting it. A method stops being a liveness
   root for E3219: it keeps other declarations live only when it is live itself.
4. **The pass stays linear.** `UnusedExportCheck` already visits every resolved callee of every built
   body; crediting a method is one set lookup per reference.

**Why no wider tool.** The review findings filed as duplication (#41, #96, #103, #140, #166, #215, #279,
#284) are each a second implementation of one decision, in different files with different shapes: six
three-line path parses, a token set written out, a hand-built list, literals in a usage string, a second
block-nesting walk, a second capped buffer, a wrapper, a second roster. Whether two pieces of code decide
the same thing is the same-fact judgement #32 ruled cannot be read off equal text, and none of them is a
dead or a repeated declaration. No audit discharges them, and they stay their own issues.

### Open design questions

1. **Does lint report an unused method at all?** The two specs above pin today's answer, "no, the type
   owns the diagnostic", as a deliberate choice. Options: (a) audit methods as designed here, a breaking
   change to `lint` that owes a `RELEASE_NOTES.md` entry; (b) keep the type as the unit, and close #69 as
   working as specified. Recommended: (a). The reason the spec gives is that a `public` type's helper
   would be reported as dead, and point 3 already exempts by the method's own marker.
2. **The codes.** Options: (a) widen E3092 and E3219 to methods, since the question and the repair are the
   same; (b) mint two codes. Recommended: (a).
3. **An unmarked method of a `public` type in a library.** Options: (a) audited like any unmarked
   declaration, by `check-stdlib` and the library findings E3219 already produces; (b) exempt with its
   type. Recommended: (a).
4. **Union and enum cases never constructed, and fields never read.** Rust reports both. Options:
   (a) stop at methods; (b) add them as further stages. Recommended: (a). No open issue needs them: the one
   never-constructed case on file (#152) is constructed by the generated cache codec, so an audit would
   not report it.

## Stages

1. **A directive line's identifiers are not spellings.**
   - Changes: `rawSpellingCountsIn` and `conditionalSpellingsIn` share the walk that knows a directive
     line. If the conditional-spellings fact is stored in the library cache, the stored form is
     invalidated in the same commit.
   - Area: `maxon-bin/Compiler/QueryDatabase.maxon`, `specs/unused-file-private-declaration.md`.
   - Depends on: none.
   - Discharges: #16.
   - Shown by: a new spec case, red first. The filed repro was read off the code and not run: one file
     with `#if debug` … `#end`, another in the same directory declaring an unused unmarked
     `let debug = 1`, expecting E3219. For the count to be compared the name must also be spelled inside
     a conditional region of a file that can name the candidate, with its only active use in a
     declaration that is itself dead; the case is built to that shape.
2. **Remove the methods nothing uses.**
   - Changes: deletions only, of the methods a build of stage 3's audit reports over `maxon-bin/`,
     `stdlib/`, `tests/` and `examples/`; an `export` that only the audit's first rule condemns is dropped.
   - Area: wherever the audit points; no behaviour changes.
   - Depends on: open question 1, and stage 3's audit built outside the tree's slot to produce the list.
   - Discharges: nothing by itself; it makes stage 3 green alone.
   - Shown by: byte-identical executables across the lanes, since only unreferenced bodies leave.
3. **The audits report a method nothing uses.**
   - Changes: the design above, in both audits; `lint --fix` repairs as it does for a top-level
     declaration. This stage is a `/plan-and-land`: the survey settles which compiler-called hooks credit
     a method and how a reference from a generic or an unbuilt library body is seen
     (`noteSpelledUnbuiltReferences`).
   - Area: `maxon-bin/Compiler/UnusedExportCheck.maxon`, `UnusedUnexportedCheck.maxon`,
     `DiagnosticFixes.maxon`, the E3092 and E3219 entries of `ErrorCodeRegistry.maxon`,
     `specs/unused-export.md`, `specs/unused-file-private-declaration.md`, `docs/CLI_REFERENCE.md`,
     `docs/LANGUAGE_REFERENCE.md`, `RELEASE_NOTES.md`.
   - Depends on: stages 1 and 2; open questions 1 to 3.
   - Discharges: #69.
   - Shown by: new spec cases red first: an exported method with no caller outside its directory
     (E3092), an unmarked method with no caller (E3219), and controls that stay clean — a `public` method,
     a method that is a conformance's witness, a method reached only as a function value, a service's
     `export` method reached only through a handle. `specs/unused-export.md`'s
     `error.unused-exported-type` is re-pinned. The scale ladder reads linear over a program of many
     methods.

## Members

- #69 — the owner. `aggregateNameIsBuiltOnATypeParameter` was exported with no caller and no audit
  reported it. The function is gone from the tree; what remains is its second half, "find why the audit
  misses an exported method with no callers", answered under Problem and fixed by stage 3.
- #16 — identifiers on `#if` directive lines count as spellings in the unused-declaration audits
  (`rawSpellingCountsIn` counts them; `conditionalSpellingsIn` excludes them). Stage 1, which carries its
  repro and the shape a red case needs.

## Related, not folded

- #152 — `RowSetQuery.dropDiverges` has no caller outside the cache reader. A union case, constructed by
  the generated codec, so no audit here reports it; its fix also regenerates the codec and follows #66.
- #41 — six driver commands each parse one path and print `error: invalid path:`. A helper beside
  `sourcePathsNamedBy`; no audit sees three repeated lines.
- #96 — "is this token `export` or `public`" written out at six sites beside `isVisibilityModifier`. A
  closed set spelled by hand; each site is rewritten to ask the table.
- #140 — the build usage string spells flags as literals beside the flag constants. A literal inside a
  longer string; no declaration is repeated.
- #103 — `IrFunctionSelfTest` builds its broken-row list by hand. One file, routed through
  `requireEveryRowHeld` with a trailer argument.
- #166 — go-to-definition pairs block labels with its own walk instead of `lspDocumentBlocks`. Two
  algorithms for one decision; planned with #165, which fixes the shared block model.
- #215 — the LSP relay and the spec pool each keep a capped prefix of a child's stderr. One shared
  retention type; the two already differ, so merging them is a design choice, not a deletion.
- #279 — a wrapper, a panic text and a type name left after #218. Parser ownership cleanup; its nearest
  class is #277's retain and drop decisions.
- #284 — the arms of a value merge recorded in `joinedVariables` and in the value-fact record. A stage of
  the value-fact record's plan (#30, R-c), after #243, #235's first task and #232.
