# Compiler output, caches, allocation counts and reports are a function of the program: one environment gate, one source order key, and a table walk that names its order

Class issue: #311 · Priority: P1

## Stage order

One stage is one local commit, which ticks its box. A stage starts when every stage it waits on is ticked
here and every issue it waits on is closed. A `deferred` stage is skipped. The plan lands once, after its
last stage.

| Done | Stage | Rule it establishes | Waits on | Not beside | Needs a ruling |
|---|---|---|---|---|---|
| [ ] | 1 | One environment gate varies the process hash seed over fixed fixtures, and `keysOfCrossKindContests` returns its names in byte order |  |  |  |
| [ ] | 2 | A file's source order key is (layer, root-relative path), computed once per file at load, and no comparison reads the working directory | 1 |  |  |
| [ ] | 3 | A lint-only analysis writes nothing lowering reads: `EffectSummary` asks the generic-instance registry through a non-minting lookup | 1 |  |  |
| [ ] | 4 | The spec runner orders its suite and report by spec name in byte order, whatever the filesystem's listing order |  |  |  |
| deferred | 5 | `Map` and `Set` walk in a named order through `inKeyOrder()` or `inTableOrder()` |  |  | How is the walk rule enforced? (a) a `maxon lint` rule for every program; (b) only the compiler's own build; (c) no rule, only the gate. Recommended: (a). |
| deferred | 6 | Every table walk in the compiler, stdlib, tests and examples names its order, minting walks taking `inKeyOrder()` | 5 |  |  |
| deferred | 7 | A bare `for` over a `Map` or `Set` is a lint finding that names both walk doors | 6 |  |  |

## Problem

What the compiler emits, caches, allocates and reports must be a function of the program (its sources, target, defines and command). Four inputs that are not the program reach those results, each through one decision that no single place owns:

1. **The process hash seed, through a table walk.** `for (key, value) in table` over a `Map` or `Set` compiles anywhere, and the compiler's source holds about 300 such walks over about 650 table aliases. #78 ruling 10a requires a walk that can mint (register a first-wins entry, intern a name, number an id, fill a written array) to sort its keys first, but the rule is a comment on `sortNamesInByteOrder` (`ByteOrdering.maxon`) and a convention at each site. `keysOfCrossKindContests` (`SignatureIndex.maxon`) returns its map's keys in table order to `crossKindScopedAliasNames` and `settleRangeContests`, and nothing refused it.
2. **Where the files sit, through the source order key.** The compiler's canonical file order is `pathIdentity` (`ByteOrdering.maxon`): the absolute path, resolved against the working directory. `SourceSite.compare`, `comparePathIdentity`, `precedesInPathOrder` and `ParseStaging.sourceFilesInPathOrder` all order by it. So whether a library file sorts before or after a program file depends on the two directories' spellings, and every roster in "first source site" order inherits that: `sortServiceSpawnCandidates` orders the spawn candidates by `SourceSite`, `serviceRosterOf` keeps first-spawn order, and `CownInterface.installInterfaceHandleConversions` emits `__cown_iface_*` functions in that order. `comparePathIdentity` also reads `Directory.currentPath()` and re-resolves and re-spells both paths on every comparison, so the cost of ordering grows with the working directory's depth, per file.
3. **The filesystem's listing order, through a report.** `specFilePaths` (`SpecTestRunner.maxon`) builds the suite from an unsorted `Directory.list`, and that order is the report order.
4. **Whether lint ran, through a write from a read-only analysis.** `checkDiscardedResults` runs only under `LintRules.enforced` (`lint`, `spec-test`); its `EffectSummary.instanceFromArguments` calls `internGenericInstanceOf`, which can append a generic-instance row that `build`, `run` and `test` (`LintRules.skipped`) never make.

Two gates exist and both vary only the seed: `tests/cli/the-output-is-independent-of-the-hash-seed` (one fixture, executable and debug sidecar) and `tests/cli/the-library-cache-is-independent-of-the-process-hash-seed` (four fixtures, five pack kinds). They see only what their fixtures reach. No gate varies the stdlib location, the working directory, the listing order or the lint setting, and no gate compares allocation counts across seeds.

## Design

**The rule.** For one program, target, defines and command, the executable, the debug sidecar, every library cache pack (given one stored digest seed and tier), the allocation counts `--metrics` reports and the tool's report are equal under every process hash seed, every directory the compiler and the program sit in, every working directory, every filesystem listing order, and with lint enforced or skipped. This extends #78 ruling 10 and 10a from the seed to the whole environment; #78 and its wiki page `Design-hashing` must say so.

**1. One environment gate.** A `tests/` corpus case family built on one harness: it compiles a fixed set of small fixture programs once in a baseline environment and once per varied axis (not the cross product), and compares the artifacts byte for byte and the allocation counts for equality. Axes: process seed (two further pins, one tier, one planted stored seed, fresh cache roots); stdlib location (the compiler staged in two directories that sort on opposite sides of the program's path); working directory depth (the same build started three components deeper); lint (the same program compiled with `LintRules.enforced` and `skipped`, comparing the image). Every roster the gate claims to hold is asserted to be reached by a fixture, and a fixture that stops reaching it fails the gate as "could not run" (the shape `requireEveryLookupHelperIn` already has). The two existing seed gates become cells of this harness.

*How it differs from the two-rebuild fixpoint check, which stays out of CI and the landing gates:* the fixpoint check holds the environment fixed and asks whether the compiler reproduces itself, at two extra self-compiles. This gate holds the program fixed and varies the environment; it never compiles the compiler. Its cost is one cold library compile per varied cache root (five: baseline, two seeds, one location, one depth) plus one warm compile per fixture per cell; the first stage records the measured wall time per lane in `tests/README.md`.

**2. One source order key that is a function of the program.** A file's order key is (layer, path relative to the root it was loaded under, portable separators), with layers ordered stdlib, scriptlib, program. It is computed once per file at load, by one owner that also resolves each root directory's physical identity once and derives each file's identity from it; a file that is itself a link keeps its own physical lookup. `SourceSite.compare` and every `pathIdentity` caller compare these keys; no comparison reads the working directory. This is not sorting the loader's input: the loader's order stays whatever the walk gives (and `MAXON_SOURCE_ORDER=reversed` keeps proving that); it changes the key the decision points already compare.

**3. A table walk names its order.** `Map` and `Set` gain two walk doors: `inKeyOrder()` (keys `Comparable`; byte order for `ByteArray` and `String`, numeric for ids, as the generated cache writers already order) and `inTableOrder()`. A bare `for` over a `Map` or `Set` is a lint finding that names both doors. Order-insensitive folds (`count`, `any`, `all`, `min`, `max`) need neither. Every `inTableOrder()` in the compiler is then a greppable claim that the body touches only its own key's entry; ruling 10a's test decides each site, and a site that mints takes `inKeyOrder()`. The measured cost of ordering the settle-tail walks is tens of allocations per warm compile (#178).

**4. A lint-only analysis writes nothing lowering reads.** `EffectSummary` asks through a non-minting lookup (an exported door over `findRow`, `SignatureIndexGenericTypes.maxon`); an absent row is `AbsentError`, which `calleeInstance` already propagates.

**5. A report's order is decided at the report.** The spec runner orders its suite by spec name in byte order (`compareBytesOrdinally`); `Directory.list` stays in listing order.

**Sources read.** Go's `cmd/compile/internal/test/reproduciblebuilds_test.go` compiles fixed inputs repeatedly under `-c 2` and compares object bytes, and `reflectdata` sorts its type and symbol lists by name before writing: Maxon adopts both, and varies more axes because its seed, cache and path identity are process state Go's compiler does not have. Go identifies a file by import path, not disk path (`-trimpath`): the source order key follows it. rustc's `UnordMap`/`UnordItems` (iteration only through order-insensitive operations or `into_sorted`) and its internal lint `potential_query_instability` are the model for design 3; Maxon departs by making it a lint rule for every program, because lint rules are not scoped per project and #78 ruling 2 already tells every program that needs an order to sort. Go's `os.ReadDir` sorts by filename; Maxon does not adopt that for `Directory.list` (open question 3).

### Open questions

1. **How is the walk rule enforced?** (a) A `maxon lint` rule for every program, as designed above: one mechanism, a breaking lint finding for user code that walks a table bare. (b) A rule only the compiler's own build enforces: needs a project-scoped lint mechanism that does not exist. (c) No rule, only the gate: the next unordered mint is again found by a fixture or by accident. Recommendation: (a).
2. **Does the source order key replace `pathIdentity` everywhere?** It changes which of two duplicate declarations is blamed first and moves emitted order once (ir-spec pins re-recorded). Recommendation: yes; a second order beside it would be the same defect again.
3. **Is `Directory.list` sorted, as Go's `os.ReadDir` is?** Sorting it fixes every listing consumer at one point, but sorts the loader's input, which the standing ruling refuses because it hides order dependences that `MAXON_SOURCE_ORDER=reversed` and unsorted hosts expose. Recommendation: no; fix at the report (design 5).
4. **Must library cache packs be equal across stdlib locations?** The gate's location axis compares the executable and sidecar only; pack content across locations is #18, #205 and #36's sealing rule. Recommendation: out of this class.
5. **Which lanes run the gate?** Listing order and path spelling differ per host. Recommendation: every native lane's `corpora` job.

## Stages

1. **The environment gate, seed axis; `keysOfCrossKindContests` ordered.** One harness and fixture set; the two existing seed gates move onto it; a fixture with a cross-kind contest is added, and allocation counts under three seeds are compared. `keysOfCrossKindContests` returns its names through `sortNamesInByteOrder`. The false comment on `stringBytesPrecede` (`Project.maxon`) is corrected. `tests/ladders/README.md` says a hand-run ladder pins `MAXON_HASH_SEED` and `MAXON_HASH_TIER` (#78 ruling 6). Area: `tests/cli` (the two seed cases, their fixtures, `CliHarness.maxon`), `SignatureIndex.maxon` (`keysOfCrossKindContests`), `tests/README.md`, `tests/ladders/README.md`. Depends on: none. Discharges #237. Shown by: the cross-kind fixture's packs under three seeds against one stored seed; if the fixture cannot be made to differ, the fix still lands under ruling 10a and the stage says so.
2. **One source order key and identity owner** (`/plan-and-land`: the owner's shape is open). Area: `ByteOrdering.maxon` (`pathIdentity`, `comparePathIdentity`, `precedesInPathOrder`), `SourceSite` (`SignatureIndex.maxon`), `ParseStaging.sourceFilesInPathOrder`, `Queries.requireFileProvenanceOf`, `Project.identityOfPath`, the loader's root table; ir-spec pins that move. Depends on: stage 1. Discharges #158, #142. Shown by: the gate's location axis (a program with service interface handles, one of them on a stdlib type, built by one compiler staged in two directories sorting on opposite sides of the program; `cmp` of the executables) and its depth axis (allocation counts equal from two working directories of different depth, at every rung of the scale ladder fixture used).
3. **Lint parity.** `EffectSummary.instanceFromArguments` asks the non-minting door; the other `EffectSummary` paths and the `LintRules.auditPasses` passes are audited for any write to `ProgramSignatures`. Area: `EffectSummary.maxon`, one exported lookup beside `internGenericInstanceOf` in `SignatureIndexInstances.maxon`; the gate's lint axis. Depends on: stage 1. Discharges #181. Shown by: a mutation that panics on a registry append during `checkDiscardedResults` passes the filtered E3064 specs; E3064 specs unchanged (a verdict that moves is a design question, not a re-record); the lint axis compares the image built with lint enforced and skipped.
4. **Spec report order.** `specFilePaths` orders by spec name in byte order. Area: `SpecTestRunner.maxon`, one `tests/cli` case. Depends on: none. Discharges #76. Shown by: a case that runs `spec-test` over a fixture directory in natural and reversed listing order and compares the per-spec report lines.
5. **The walk doors.** `inKeyOrder()` and `inTableOrder()` on `Map` and `Set`, with specs and `STDLIB_REFERENCE`. Area: `stdlib/Map.maxon`, `stdlib/Set.maxon`, their specs and docs. Depends on: open question 1.
6. **Every table walk names its order.** One `/land` per area, parallel: `SignatureIndex*` and `Queries*`; `Parser*`; `IR/` and `Targets/`; `Runtime/`; `Testing/`, `Run/`, `Lsp/`, `Mcp/`; `stdlib/`, `tests/`, `examples/`. A walk whose body can mint takes `inKeyOrder()`; each landing reports the warm-compile allocation delta against its parent (bound: #178's, at most 0.05% bytes). Depends on: stage 5.
7. **The lint rule.** A bare `for` over a `Map` or `Set` is a finding naming both doors; error-code registry, `docs/`, release notes entry with its `Fix:`. Area: `SemanticCheck.maxon` lint rules, `ErrorCodeRegistry.maxon`, `specs/`. Depends on: stage 6.

## Members

- #237 — `keysOfCrossKindContests` walks its table unordered into a path that can mint; stage 1 (its first step, the cross-kind fixture under three seeds, is the stage's red test; the `stringBytesPrecede` comment is carried there).
- #158 — emitted `__cown_iface_stub.*` / `__cown_iface_from.*` order follows whether the stdlib's path sorts before the program's (2,167 differing bytes over the compiler's own source; the same binary from one place is identical and the seed does not change it); stage 2, by the repro in that stage.
- #142 — allocation count grows with working-directory depth per source file (+11,927 at rung 0, +109,267 at rung 5, three components deeper); stage 2. Done when the same binary's count at every rung is equal from two working directories of different depth, without weakening physical identity for a file that is itself a link.
- #181 — a lint-only check appends generic-instance rows, so `spec-test` can lower against a different registry than `build`; stage 3, with its three acceptance points carried there.
- #76 — `spec-test` reports in filesystem listing order; stage 4.

## Related, not folded

- #189 — the declaration-identity class holds it: its fix is one keyed answer per (name, reader file) in alias resolution, and the clash costs about 40 allocations per reference under every seed. Stage 1's seed axis on allocation counts and the ladder README line are its determinism half; its acceptance (equal parse allocations under eight seeds for `genmemberlookup.sh 16 fields`; no per-reference allocation for a clash) stays with it.
- #214 — the declaration-identity class holds it: an id that does not carry its interner. The verdict it moved was a function of the program and target, not of the environment.
- #143 — a value id reused within a function; deterministic, and a debug-roster soundness defect with its own fix (one per-function counter).
- #56 — eviction by last write; a cache retention policy with a decided fix, deterministic for a given sequence.
- #163, #186 — which pack holds a row and what names a pack; #163 owns that class and #186 is its instance.
- #205 — a library cache entry records a program file's path; #18's class (a declaration's origin). Open question 4 points here.
- #195 — a spec batch changes a case's verdict through a whole-program property; the batcher's partition rule, not the environment.
- #153, #159 — a runtime installed by mention, and `usesGt` decided after its snapshots; one-decision-point defects in runtime installation.
- #198 — a documentation line on `MAXON_HASH_SEED`.
- #37, #18, #36 — designs this class meets: the clash rule, declaration origin, and the sealing rule that depends on #78 ruling 10.
