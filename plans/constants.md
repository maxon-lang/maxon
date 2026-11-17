# A constant is declared once; a copy across a boundary no name crosses is sealed, read or pinned, and a gate refuses any other

Class issue: #53 · Priority: P2

## Stage order

One stage is one local commit, which ticks its box. A stage starts when every stage it waits on is ticked
here and every issue it waits on is closed. The plan lands once, after its last stage.

| Done | Stage | Rule it establishes | Waits on | Not beside | Needs a ruling |
|---|---|---|---|---|---|
| [ ] | 1 | The compiler declares each constant once: synonyms and same-name repeats fold, same-name different-fact pairs are renamed, and a gate enforces it |  | 2, 3 | Open question 1: a name two directories give two different facts (#32 ruled it 'is not a duplicate and stays'): (a) rename so each name says its fact, (b) allow list beside the seal roster; recommended (a) |
| [ ] | 2 | stdlib declares each constant once, and a constant both stdlib and runtime declare is checked equal per target by check-stdlib |  | 1, 3 | Open question 3: stdlib and runtime mirrors, check now or fold when packages land (#36): (a) the check-stdlib comparison now, replaced by one declaration at #36's visibility stage, (b) wait; recommended (a) |
| [ ] | 3 | Every constant an emitter shares with the runtime tier is read from the tier file or pinned to it by a TierGeometryPin row |  | 1, 2 |  |

## Problem

A constant is one fact, and the tree lets it be declared wherever a reader needs it. Three boundaries
produce the copies, and only one of them has a check, which is applied by hand:

- **Inside one program** (`maxon-bin/`, and `stdlib/` on its own). A top-level name is scoped by its
  directory (`specs/top-level-name-scope.md`), so a directory that cannot see a declaration restates it,
  and a subdirectory that can see it may shadow it. Nothing reports either. Today `maxon-bin/` still has
  about thirty constant names declared in more than one directory. Most are sealed copies (below); the
  rest are repeats (`FileAttributeNormal`, `FrameRecordBytes`) or one name for two different values
  (`Utf8ContinuationMask` is `0x3F` exported from `GraphemeRuntime` and `0xC0` in `X64Runtime`;
  `MaxBacktraceFrames` is 100 and 64; `TargetSeparator` is `":"` and `","`). The same fact also survives
  under two names (`HexRadix`/`HexBase`, `NewlineByte`/`LineFeedByte`, `AsciiCeiling`/`AsciiHighBit`,
  `WordSignBitShift`/`WordSignShift`/`SignBitPosition`, `LastSingleHexDigit`/`SingleDigitLimit`,
  `NanosPerMillisecond`/`NanosPerMilli`). `stdlib/String.maxon` and `stdlib/helpers/string/grapheme.maxon`
  both declare `AsciiLimit` and `CarriageReturnByte`.
- **Between the compiler's emitters and the `runtime/` tier.** No name crosses the tier boundary
  (`runtime/README.md`, "Rules a tier file obeys"; E3154), so a tier file restates what it shares with an
  emitter. `TierGeometryPins.requireTierGeometryAgrees` compares the emitter's value with the tier's own,
  read out of the tier file by `ProgramSignatures.integerConstantIn` on every compile. That check exists
  only where someone wrote a `TierGeometryPin` row. About seventy constant names are declared on both
  sides, and about fifteen have no row: `FileTimeTicksPerSecond` and `FileTimeTicksFrom1601To1970`
  (`ClockRuntime` / `runtime/Clock.maxon`), `WaitTimeout` (`GtRuntime` / `runtime/Process.maxon`),
  `MinimumProcessorCount`, `LowSurrogateFloor`, `SurrogatePayloadBits`, `BitPosToByteShift`,
  `BitPosWithinByteMask`, `BitsPerByte`, `MachineWordBytes`, `ByteMask`, `AllBitsSet`, `AsciiLowerA`,
  `AsciiLowerZ`, `NoWaitMs`. `ManagedMemoryRuntime.checkManagedGeometry` compares three of them against
  `TierBitsPerByte`, `TierMachineWordBytes` and `TierBitPosToByteShift`, which are literals declared in the
  compiler: a third copy, and a pin against itself.
- **Between `stdlib/` and `runtime/`.** Both are source in every program and neither can name the other's
  unmarked declarations. `AsciiCaseBit`, `AsciiLimit`, `BitsPerByte` and `NanosPerFileTimeTick` are
  declared in both, and nothing compares them. `stdlib/Sampler.maxon` decodes the FILETIME answer
  `runtime/Clock.maxon` produces, so the last one is a decode that can drift.

The shared mechanism: whether two declarations of one fact are compared is decided per constant, by
whoever remembered, and no place enumerates the copies.

## Design

**One fact, one declaration.** Two declarations are one constant when they state the same fact: if one
changed, the other would have to change with it. Equal digits are not enough, and a different name is not a
different fact. A second declaration is allowed only where a boundary forbids reading the first, and then
it is one of three checked forms. A machine, not a reviewer, finds a copy that is none of them.

1. **Inside one program, a constant has one declaration that every reader can name.** A name declared in
   two directories for two different facts is renamed so that each name says its fact.
2. **A sealed copy** is declared by a file sealed for a stated, checkable reason, and is read only by the
   sealed file or files. The seals are:
   - a wire vocabulary's keys (DAP, the debugger's event stream, LSP, MCP): each vocabulary declares its
     own, once;
   - a test oracle (`MachoSectionReader`, the PE reader's escape table, `BinaryImageReader`): it shares
     nothing with the code it judges;
   - the library-cache schema files (`ContentHash.maxon`, `LibraryCacheCodecs.maxon`,
     `LibraryCacheFormat.maxon`): a wire-deciding value is declared inside them, they read no
     wire-deciding constant from outside, and their copy of a general fact (`HeaderWordByteMask`,
     `ByteBitWidth`) is read only by them;
   - `Run/ScratchName.maxon` and the files `ScratchNameSourcePaths` names, which compile as a separate
     program.
   A role name (`ExtensionDotByte`, `DriveColonByte`, the `WinDesc*` offsets, `PosixNanosPerSecond`) states
   a position in a grammar or an API and may be declared in terms of the character's name. A file or lane
   prefix on a character or a unit factor is a namespace, not a role, and folds.
3. **Across the emitter and tier boundary, the tier file holds the declaration.** The compiler reads the
   tier as input on every compile, so the value in the file being compiled is the only one that is true of
   the program being built. An emitter takes one of two forms:
   - **read**: it declares nothing and reads the tier's value through `integerConstantIn` into a layout
     record built once per compile, as `HostContextLayout` reads `stdlib/Debuggee.maxon` and
     `stdlib/Sampler.maxon`. This is the form for a new shared constant.
   - **pin**: where the compiler needs the value with no `ProgramSignatures` in hand (a layout owner such
     as `BitsPerByte` or `MachineWordBytes`, a static range, a generated table), it keeps its declaration
     and a `TierGeometryPin` row compares it with the tier's on every compile of a target that declares
     it.
   A comparison against a value declared on the compiler's own side is ruled out.
4. **Between `stdlib/` and `runtime/`, a name declared in both folds to one value.** `maxon check-stdlib`
   compares every top-level integer constant the two trees both declare, per target, and fails naming both
   files. No roster: a new shared name is covered when it is written.

Go solves the emitter and runtime half with shared source: `internal/abi` and `internal/goarch` are
imported by both `cmd/compile` and `runtime`, and `typecheck/builtin.go` is generated from the runtime's
declarations by `mkbuiltin.go`. Maxon keeps the single declaration and departs on where it is read: Go
links the shared package into the compiler, while Maxon's compiler reads the tier as input, so a value
compiled into the compiler can disagree with the tier file on disk. Reading the file at compile time
cannot.

**Gates.** Three cases in `tests/docs`, in the idiom of `each-shared-harness-helper-is-defined-once`:

- a top-level constant name is declared in one directory of `maxon-bin/`, or every further declaring file
  is on the seal roster the case holds;
- a top-level constant name is declared once in `stdlib/` (two arms of one file's `#if` are one
  declaration);
- a top-level constant name declared in `runtime/` and under `maxon-bin/` is named by a `TierGeometryPin`
  row.

The gates match names. A fact declared under two names is still found by reading; no tool decides "the
same fact".

### Open design questions

1. **A name two directories give two different facts.** #32 ruled that it "is not a duplicate and stays".
   The first gate cannot tell it from a repeat. Options: (a) rename so each name says its fact, and the
   gate needs no exception; (b) an allow list of names beside the seal roster. Recommended: (a).
   `Utf8ContinuationMask` shows the cost of (b): an exported `0x3F` and a directory-local `0xC0` under one
   name, where the local one silently wins in its directory. This narrows #32's clause.
2. **Existing pins: keep, or convert to reads.** About fifty-five slab, process and shared-memory
   constants are pinned today. A pinned figure takes a staged build to change, because the building
   compiler's value is compared with the tier file on disk (`runtime/README.md`, "Restated geometry and its
   pins"); a read would follow the file. Options: (a) leave them pinned, since they are checked;
   (b) convert the slab geometry to reads as a further stage. Recommended: (a) here, and (b) as its own
   issue if the staged build keeps costing, because the slab emitters are soundness-sensitive and no
   defect rests on the pins.
3. **`stdlib/` and `runtime/` mirrors: check now, or fold when packages land.** Under #36 the standard
   library with the runtime tier beneath it is one package and `export` is visible to every file of a
   package, so a tier `export let` could then be the one declaration. Options: (a) the `check-stdlib`
   comparison now, replaced by one declaration at #36's visibility stage; (b) wait. Recommended: (a).

## Stages

Each stage lands as one commit. The three are independent in code; each adds a case under `tests/docs`
and a line to `tests/README.md`'s layout tree, so they land one after another.

1. **The compiler declares each constant once.**
   - Changes: fold the synonyms listed under Problem to one declaration each, the hex radix declared
     beside the other shared constants where `Compiler/` and `Debug/` can both name it, with no `export`
     on the parser's declaration. Fold the same-name repeats (`FileAttributeNormal`; `FrameRecordBytes`
     reads `BacktraceFormat`'s). Rename the same-name different-fact pairs (`Utf8ContinuationMask`,
     `MaxBacktraceFrames`, `TargetSeparator`, `CountColumnWidth`, `BoolTypeName`, `TrueSpelling`,
     `FalseSpelling`). Add the first gate with its seal roster.
   - Area: `maxon-bin/Compiler/ParserStateTypes.maxon`, `Parser.maxon`, `Lsp/LspDocumentProject.maxon`,
     `Lsp/LspHover.maxon`, `Runtime/SchedRuntime.maxon`, `Runtime/StringRuntime.maxon`,
     `Targets/X64/X64Runtime.maxon`, `Targets/X64/StdToX64Conversion.maxon`, `ConditionalCompilation.maxon`,
     `maxon-bin/Debug/**`, `maxon-bin/Testing/SpecParser.maxon`, `Testing/ScaleTestRunner.maxon`,
     `maxon-bin/TimeFactors.maxon`, `AsciiBytes.maxon`, a new `tests/docs` case, `tests/README.md`.
   - Depends on: open question 1.
   - Discharges: #32.
   - Shown by: the gate case red on the names above before the folds; byte-identical executables and
     sidecars across the lanes after them.
2. **`stdlib/` declares each constant once, and agrees with `runtime/`.**
   - Changes: `AsciiLimit` and `CarriageReturnByte` keep their declaration in `stdlib/String.maxon`, which
     `helpers/string/` can name, and leave `grapheme.maxon`. `maxon check-stdlib` compares same-named
     integer constants of `stdlib/` and `runtime/` per target. Add the second gate.
   - Area: `stdlib/String.maxon`, `stdlib/helpers/string/grapheme.maxon`, the `check-stdlib` driver,
     `docs/CLI_REFERENCE.md`, a `tests/cli` case over a staged library tier, a new `tests/docs` case,
     `tests/README.md`.
   - Depends on: open question 3. Confirm first that `grapheme.maxon` is compiled by nothing outside
     `stdlib/`.
   - Discharges: #53.
   - Shown by: a `tests/cli` case that stages a tier whose `AsciiCaseBit` differs from the library's and
     expects `check-stdlib` to fail naming both files; the gate case red on the two repeats.
3. **Every constant an emitter shares with `runtime/` is read or pinned.**
   - Changes: a `TierGeometryPin` row, or a read, for each unpinned name listed under Problem; the ones
     under `#if os(Windows)` in `runtime/WideText.maxon` and `runtime/Process.maxon` are compared on the
     targets that declare them. `checkManagedGeometry` reads the tier file and `TierBitsPerByte`,
     `TierMachineWordBytes` and `TierBitPosToByteShift` go. A compiler name that only coincides with a tier
     name (`NoWaitMs` in `Debug/Backend/PtraceBackend.maxon`) is renamed. Add the third gate.
     `runtime/README.md`'s "Restated geometry and its pins" states the two forms.
   - Area: `maxon-bin/Compiler/Runtime/ManagedMemoryRuntime.maxon`, `ClockRuntime.maxon`,
     `HostTextRuntime.maxon`, `SchedRuntime.maxon`, `GtRuntime.maxon`, `TierGeometryPins.maxon`,
     `maxon-bin/Compiler/IR/LayoutDescriptor.maxon`, `maxon-bin/AsciiBytes.maxon`,
     `maxon-bin/Debug/Backend/PtraceBackend.maxon`, `runtime/README.md`, a new `tests/docs` case,
     `tests/README.md`.
   - Depends on: none. It shares `SchedRuntime.maxon` and `AsciiBytes.maxon` with stage 1, so it follows
     it.
   - Discharges: the tier half of #53, and the runtime-tier mirrors #37's body counts among the
     compiler's names.
   - Shown by: the gate case red on the unpinned names; a compile against a staged tier with one of the
     newly pinned figures changed panics naming both files.

## Members

- #53 — the owner. `AsciiLimit` and `CarriageReturnByte` repeated inside `stdlib/` (repro:
  `grep -n "^\s*let CarriageReturnByte\|^\s*let AsciiLimit" -r stdlib` prints two of each);
  `AsciiCaseBit`, `AsciiLimit` and `BitsPerByte` mirrored between `stdlib/` and `runtime/` with nothing
  comparing them. Stage 2, and stage 3 for the compiler's side of the same names.
- #32 — the compiler's remaining synonyms and same-name repeats. Stage 1. Its rulings (2026-11-15, and the
  two of 2026-11-16 in its comments) are carried above as the same-fact test, the seals and the role-name
  rule, and stay citable there by number. Its proof carries over: byte-identical executables and sidecars
  across the lanes, and `python scripts/library-cache/generate-codecs.py` when `LibraryCacheFormat.maxon`'s
  text changes.

## Related, not folded

- #37 — a design issue: which names decline the library baseline. Its ruling that the compiler's mirrors
  are not renamed to get the baseline back is unaffected; stage 3 pins those mirrors.
- #36 — packages. Open question 3 names the stage at which the `stdlib/` and `runtime/` mirrors fold.
- #294 — which hosts provide a debug-host entry is a decision table, not a constant: the stdlib's
  `#if os(...)` gates against `TargetFacilities`. It is planned with #220 and #153, which edit the same
  table.
- #60 — the CPU-feature probes are a closed set enumerated by hand inside the compiler. Its fix is one
  enum in `CpuFeaturesRuntime`; no boundary is involved and no gate here sees it.
- #197 — two call pairs of `emitHashSoftwareState` and `emitHashFinish` in one file
  (`buildHashGenerate`, `buildHashWord`'s software arm). A local helper; the hash mix has no second tier.
- #13 — two signature hash marks hold one value. The defect is that distinctness is checked by hand, the
  opposite of a repeat; its fix is a distinctness check over the mark set.
- #301 — `tests/debug` hard-codes Linux's SIGSTOP. The names differ, so no gate here finds it; the fix is
  to call the stdlib's `stopProcess`.
- #135 — test corpora hand-spell Target IR vocabulary. A test-side vocabulary owned by
  `tests/support/TargetIr.maxon`, not a declared constant in two tiers.
- #160 — one harness function repeated in `tests/emittedRuntime`. A one-file change.
- #39 — its layout-tree half is gated by `the-readme-layout-tree-lists-every-test-case`; what remains is
  prose that counts cases by ordinal, which is #105's class.
- #105 — the owner of stale citations; a class of its own.
