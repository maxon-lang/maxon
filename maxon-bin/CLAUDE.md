## The compiler

One compiler builds this tree and it is written in Maxon: source `maxon-bin/`, binary
`maxon-bin/.maxon/maxon`, suite `specs/`. On Windows the binary is `maxon.exe`; commands below show
the Windows form.

⭐ **TWO SOURCE TIERS ARE READ ON EVERY COMPILE, AND BOTH LIVE AT THE CHECKOUT ROOT.** `stdlib/` is the
standard library; `runtime/` beside it is the language runtime — the code every program needs before
any of its own runs. The compiler locates `stdlib/` by walking UP from its own executable and reaches
`runtime/` as its sibling, so the two travel together everywhere: a release archive, an install tree, a
Docker image, a Homebrew prefix. A tree holding one without the other compiles nothing.

⛔ **A `runtime/` FILE IS NOT COMPILER SOURCE AND NEEDS ONE SELF-COMPILE, NOT TWO.** It is input the
compiler READS, so the first build already compiles against the edited file and carries it. That is the
opposite of `Compiler/Runtime/` below, which the compiler WRITES into every program including itself.
`scripts/self-compiles-needed.sh` answers for both; it does not watch `runtime/`, deliberately.

⭐ **THREE FAMILIES ARE IN, AND THE ROOT THAT REACHES ANY OF THEM IS A CALL THE COMPILER EMITS.**
`runtime/Clock.maxon` holds `__clock_now_unix_s` and `__uptime_ms`, `runtime/ParallelBoundary.maxon` holds
`__parallel_boundary` and `runtime/FaultProbe.maxon` holds `maxon_force_segfault`;
`__Builtins.currentUnixTimeSeconds()`, `__Builtins.tickCountMs()`, `__Builtins.parallelBoundary()` and
`__Builtins.forceSegfault()` lower to calls naming them, from whatever file wrote the construct.

⛔⛔ **THE TIER'S PROTECTION IS OVER BOTH DOORS A NAME CAN BE REACHED THROUGH, AND IT TAKES TWO REFUSALS.**
`Parser.requireCalleeIsNotReservedName` admits a reserved CALLEE, and
`Parser.requireFunctionValueNameIsNotReserved` a reserved name in VALUE position, only where
`Parser.fileMayUseReservedNames` holds — `runtime/` itself, `stdlib/Builtins.maxon` and
`stdlib/Testing.maxon`, **and any file this compile WROTE part of, which is a staged `*.test.maxon` under
`maxon test`** — and in both doors conjoined with `declaresCallee`. An ordinary stdlib or user file calling
one earns E3004 and naming one as a value earns E3155, which is what keeps the visibility admit-list below
sound. The value door is cured at `requireNameIsUsableAsFunctionValue`, the one site both producers of an
author-named function value ask — a bare name READ and a function-backed enum case — so the rule needs no
list of syntactic positions. ⇒ The property is *"no source file outside the tier may CALL **or NAME** a
runtime entry"*, and the staged-file provenance is the standing exception to it on BOTH doors alike: under
`maxon test` an author's own file may do either.

⛔⛔ **ONE TIER ENTRY WEARS NO PREFIX AND SO CARRIES ITS OWN RESERVATION — `maxon_force_segfault`.** Its name
is the FRAME a backtrace prints (`specs/safety.md` asserts three of them), so it cannot be moved into the
`__` band a shape test recognises. `Parser.requireUnreservedName` refuses the DECLARATION of it by name, under
E2051 and beside the `self` rule, and `MmRuntime.ReservedCalleeReason.faultProbeEntry` refuses the CALL. The
declaration half is not tidiness: a second declaration of the bare name makes it CONTESTED across directories,
`Parser.declaredMethodName` then files the tier's own declaration as `runtime.maxon_force_segfault`, and the
bare call the compiler emits binds to the user's function — which does not fault. E4015 cannot report it,
because it tells the two sides apart by one having no source file and both are now parsed.
`specs/safety.md`'s `error.force-segfault-name-is-reserved` is the case. ⇒ **THE NEXT UNRESERVED TIER ENTRY
OWES THE SAME TWO DOORS.**

A runtime name is therefore never
classified `LibraryFacts.unreachable`, and the exemption is by PROVENANCE rather than by reachability: a
walk over source call edges holds no evidence about the tier at all
(`StdlibSource.unreachableLibraryNames`). Its bodies therefore lower, its range guards are inserted and
its runtime floor is counted, while dead-function elimination still sweeps an entry nothing calls, so a
program that reaches no runtime family carries none of it.

⚠ **THE TWO arm64-macos force-segfault GOLDENS OWE A RE-MINT ON A MAC, AND THE DRIFT IS EXPECTED.** A
Mach-O runs only on macOS, so an x64 host reports `force-segfault-macos` and that lane's
`force-segfault-on-a-green-thread` NOT RUN and the harness correctly mints nothing for them; their committed
text still renders `func @maxon_force_segfault`, which the printer withholds now that the name is in
`libraryFunctions`. The drift is exactly that block and nothing else. It is the FIRST tier migration whose
entry was previously rendered in goldens — the clock's and the checkpoint's never were — so neither earlier
commit set a precedent for reading it.

⚠ **A `RuntimeUsage` BIT NO LONGER GATES A SOURCED BODY'S INSTALLATION, AND STILL GATES EVERYTHING ELSE.**
DFE decides whether the body survives, so `usesWallClock` and `usesUptimeClock` install nothing — but the
per-target hand-assembled `osReadWallClock` chunk, the POSIX clock floor and the Windows optional import
band are all still theirs. Retire a bit when its LAST consumer is gone, not when the body moves.
`usesParallelBoundary` and `usesFaultProbe` are the two that qualified and both are GONE: neither body
declares any dependency — no heap, no scheduler, no import, no `.data` word — so the install guard was the
only reader of each. The fault probe's family predicate `isFaultProbeRuntimeCallee` outlived its bit, because
`MmRuntime.reservedCalleeReasonOf` still routes the call refusal through it; it moved there with the two
constants and `Compiler/Runtime/FaultProbeRuntime.maxon` is deleted, having nothing left to build.

⛔⛔⛔ **THREE PREDICATES ASKED "IS THIS THE RUNTIME'S?" THROUGH A PROXY, AND THE TIER FALSIFIED ALL THREE.
`LibraryFacts.runtimeTier` IS THE PROVENANCE FACT, AND A FOURTH SITE MUST READ IT RATHER THAN INVENT A
PROXY OF ITS OWN.** Where a family's body is WRITTEN is not something a program can observe, so moving one
must move no verdict. Two proxies said *"the module does not declare it"* and one said *"it was not parsed
from source"*; a migrated entry point falsifies each:

- `SemanticCheck.calleeYields` — answers "yields" for a tier callee and records NO edge. E3073's
  unknown-callee fallback is what admits a CPU-bound `async` target; with the edge recorded the closure
  hands the caller the tier body's own non-yielding answer and refuses a legal spawn.
- `SemanticCheck.calleeShowsAnEffect` — answers "an effect", and records no edge. Its roster is the whole
  positive side, so an unlisted runtime entry MUST read as effectful; otherwise a pure `module function`
  helper in a tier file propagates purity into a user caller and a discarded call earns E3064 on a legal
  program.
- `TargetPrinter.isRuntimeFunction` — tier membership is a DISJUNCT that stands on its own, because a name
  band exists to identify a body nothing declared and a tier member has a path AND membership to argue from.
  Keyed on the band as well, `maxon_force_segfault` — which can never take a prefix — would carry a
  green-thread stack guard and be marked `FunctionCodeChunk.asyncPreemptible` (which is `mightGuard`, NOT
  `emitGuard`, so a 0-byte frame does not escape it; arm64 has no frame-size exemption at all). It also drives
  `InlineLeaves.splicingWouldWidenTheSafePoint`.
  ⚠ **THE STANDING LIMIT THIS BUYS: EVERY DECLARATION IN A `runtime/` FILE IS EXEMPT FROM THE GREEN-THREAD
  STACK GUARD, an unreserved `module function` helper included.** That is intended — they are compiler-authored
  frames inside the margin `GtRuntime.gtStackGuardMargin` is sized for — but a tier helper that RECURSES on
  program-sized data would be the `__probeDeep` SIGSEGV with a compiler author instead of a user. A tier body
  that recurses needs a guard this predicate will not give it.

⚠ **EACH ARM SITS AFTER ITS OWN WALK'S ROSTER**, which still decides a name it lists. ⛔ And widening
`isRuntimeFunction` does NOT reopen the `__probeDeep` SIGSEGV its header records: that was a USER function
in `stdlib/Builtins.maxon` reaching the scheduler's exemption, and `runtime/` is a compiler-loaded cone no
user file can declare or even name into. The set is closed; the name test is untouched.

⛔ **AND NOTHING IN THE SUITE CAN SEE THE LAST OF THE THREE.** The guard is emitted at byte level rather
than as a TargetOp (`X64Backend`), and `asyncPreemptible` reaches only `__symtable`, whose sole reader is
`__gt_preempt_safe` — a scheduler-internal decision with no program-visible result. `schedPreemptCount()`
counts preemptions HONOURED, so a zero is indistinguishable from a monitor that never asked, and no program
can arrange to be stopped inside a four-instruction runtime body. ⇒ **a golden CANNOT catch a regression
here; read the predicate.** Closing this needs a channel that renders per-symbol safe-point provenance.

⚠ **A COMPILER-EMITTED CALL INTO THE TIER IS EXEMPT FROM MODULE VISIBILITY, AND THE EXEMPTION IS AN
ADMIT-LIST.** `SemanticCheck.calleeVisibleFrom` admits a callee that is RESERVED and is declared under
`runtime/`. Reserved is `MmRuntime.isCompilerInternalCallee`, which is the `__` band OR a name the
declaration door reserves outright — today `maxon_force_segfault`, and it belongs in the admit-list for the
band's own reason: no author can have written it either. ⇒ **WHAT KEEPS THIS SOUND FOR AN UNPREFIXED ENTRY
IS THE DECLARATION DOOR, so a name admitted here owes a refusal in
`Parser.requireFreeFunctionNameIsNotTheFaultProbe` or its successor.** A runtime file's OTHER unreserved
declarations stay module-scoped, which
`specs/runtime-source-tier.md`'s `runtime-file-unreserved-declaration-is-still-module-scoped` measures.

⚠ **`scanRuntimeUsage` WALKS EVERY RUNTIME BODY, IN EVERY PROGRAM.** A runtime name is never
`unreachable`, so the scan never skips one — and a CALL inside a runtime body would therefore set that
family's bit for a program DFE sweeps the body out of, which is rule 1 ("vocabulary does not ship ahead of
its consumer") failing open. Vacuous while every tier body calls only `__Raw`, which names no callee
(`MaxonDialect.maxonOpCalleeKind` answers `noCallee` for `rawIntrinsic`) — true of all three families in
the tier. The first family that CALLS something is the one that has to answer it.

Five doors are still standing open rather than shut:

- **E3153 is complete over SPELLED types and incomplete over INFERRED values.** `parseTypeReference`
  catches every type a runtime file writes; the value-side check at `declareInitializedBinding` and
  `bindParameters` does not see a `for` binding, a closure cell, a caught error, a `match` payload, or
  an unbound temporary. `for c in "abc"` in a runtime file is admitted. The complete site is the built
  `IrFunction`, and it is now reached, so the hole can be closed on its own.
- **`osThreadCpuTicks` is the one `__Raw` row nothing exercises**, as are `reserveRawScratchSlot`'s refusals.
  `osTickCountMs`, `osReadWallClock`, `scratch` and `loadWord` are the clock family's, and
  `builtins-clock.md`'s `wall-clock-body-is-runtime-source` renders the emitted body the last three lower to;
  `storeWord` is the fault probe's, and no golden renders that body — what measures it is a LIVE fault, in
  `specs/safety.md`'s three backtrace cases. `osThreadCpuTicks` waits on `__thread_cpu_ticks`, which cannot
  move until a `__Raw` row names the current-GT read its green-thread arm makes.
- **`ownFrame` is the one row that is a DIRECTIVE rather than an operation.** It appends no Std op and
  instead sets `IrFunction.keepsItsOwnFrame`, which `InlineLeaves.functionShape` refuses to splice. Without
  it a tier body small enough to inline is spliced into every call site and then swept, and a runtime entry
  whose whole product is a FRAME ceases to exist. It is the checkpoint's, and
  `builtins-parallel-boundary.md`'s `checkpoint-body-is-runtime-source` renders the body it protects —
  though a ```RequiredRuntime block pins a body never-inline for its own compile, so what actually guards
  the rule is the unchanged `call __parallel_boundary` in every other golden.
- **`__Raw.scratch` is the one door that materializes a frame ADDRESS in a register in a GT program, and
  the gate that protects the other such door does not cover it.** `PromoteStackRecords` promotes NOTHING in
  a program running green threads, because `__gt_stack_relocate` frees the old pages and a promoted address
  would dangle; `__Raw.scratch` reaches `TargetOp.leaRegSlot` by a different route and no gate asks. It
  cannot fire today — the guard precedes the `lea`, an async stop cannot relocate (`GtRuntime`), and the
  shim window is not a safe point — but a tier body holding a `scratch` address across a guarded call is
  the bug it becomes.
- **A `__Raw` row's host facility reaches no refusable site.** `maxonOpCalleeKind` answers `noCallee`,
  so `LibraryFacts.substrateEntries` never sees one, and a lane without the op reaches instruction
  selection instead of E3104. A substrate-entry row per op is what closes it.

- **Build it:** `./maxon-bin/.maxon/maxon build maxon-bin` at the repo root. `build.maxon` there
  declares the one target, so a bare `maxon build` builds it; name it anyway, because the seed rule
  below turns a bare invocation into a path build.
- **Get a compiler to build it WITH:** put a released `maxon` binary at `.bootstrap/maxon.exe`, which
  you run directly when the slot is empty. Maxon compiles Maxon, so there is no second
  implementation here — a previous build of this compiler is the only thing that can build it.
  ⛔ **NAME THE OUTPUT WHEN YOU BUILD WITH THE SEED, ALWAYS:**
  ```
  ./.bootstrap/maxon.exe build maxon-bin -o maxon-bin/.maxon/maxon
  ```
  **A SEED OLDER THAN NAMED MANIFEST TARGETS READS `maxon-bin` AS A PATH, NOT A TARGET**, and a path
  build picks its own output name. MEASURED with the v0.1.0 release as the seed: it wrote
  `maxon-bin/Compiler/BorrowCheck.exe`, left the slot EMPTY, and **exited 0** — so the next command is
  a bare `127` about a compiler that was never written. `scripts/build-from-seed.sh` passes `-o` for this reason;
  `CONTRIBUTING.md` spells it too.
- **Run the suite:** `./maxon-bin/.maxon/maxon.exe spec-test`.
- Exit code **101** means a memory leak was detected.
- There is **no `maxon clean`**.

> ### ⭐ THE BUILD WRITES TO `.next` AND RENAMES INTO PLACE
>
> A compiler cannot overwrite its own running image (**E6002**), and a half-written slot is a
> compiler that answers as though it were whole. So a compiler rebuilding its own slot RENAMES its
> running image to `maxon-bin/.maxon/maxon.previous` first — an OS will not let a running executable be
> deleted, but will let one be renamed — and its `.mxdbg` travels with it. A FAILED build leaves the
> slot **EMPTY** rather than reinstating anything, because a stale compiler reporting as current is the
> failure every staleness refusal in this repo exists to prevent.
>
>
> ⛔ **A CHANGE UNDER `Compiler/Runtime/` NEEDS *TWO* SELF-COMPILES BEFORE THE COMPILER ITSELF BEHAVES
> THAT WAY.** The compiler EMITS the runtime into every program it builds — including into itself — so
> with `C0` the old compiler and `S` the fixed sources:
>
> - `C0` builds `S` → `C1`. `C1`'s emitter logic is fixed, so **programs C1 builds get the new
>   runtime** — but `C1`'s OWN embedded runtime was emitted by `C0`, and is old.
> - `C1` builds `S` → `C2`. Now the compiler's own runtime is new too.
>
> ⇒ **It bites hardest where the compiler is the program under test**: `spec-test`'s worker IS the
> compiler, so a runtime fix to subprocess, the scheduler or memory management does not change what the
> HARNESS does until the second build. MEASURED: a delayed-stdin fix looked like a Windows-only lane bug
> for exactly this reason — the case failed 3/3 against `C1` and passed 3/3 against `C2`.
>
> ⚠ **`fixpoint.sh` DOES NOT CATCH THIS.** It builds `stage2` and `stage3` under `temp/` and compares
> them — both are past the convergence point, so they agree while the SLOT still holds `C1`.
>
> ⭐⭐ **DO NOT GUESS WHICH CASE YOU ARE IN — ASK, BEFORE YOU BUILD:**
> ```
> scripts/self-compiles-needed.sh     # prints `once` or `twice`, and why
> ```
> The compiler stamps the commit it was built from, so *"has any runtime file changed since the slot
> binary was built"* is a `git diff` rather than a judgement — and it counts uncommitted changes too.
> ⛔ **THIS RULE IS ONLY ABOUT `Compiler/Runtime/`. EVERY OTHER CHANGE NEEDS ONE BUILD.** The old
> wording asked whether "the seed you built with predates a runtime change", which nobody can evaluate
> in their head, so the safe answer was always twice — and a needless self-compile is ninety seconds
> off every task that touches the compiler.
> ⛔ **THE COMPILER THAT BUILDS THIS TREE MUST LIVE INSIDE IT.** `stdlib/` and its sibling `runtime/` are
> found by walking up from the EXECUTABLE, so an installed `maxon` on PATH compiles this repository
> against the RELEASE's sources — MEASURED: it succeeds and exits 0, having built a compiler from a
> library that is not this tree's. Run `.bootstrap/maxon` or the slot binary, never a PATH one.
>
> ⛔ **`.bootstrap/` HOLDS THE BINARY AND NOTHING ELSE.** A release archive ships its own `stdlib/` and
> `runtime/`, and the compiler resolves both by walking UP from its own executable — so an archive
> unpacked whole would leave a RELEASED stdlib and runtime one directory above the compiler and the
> tree's own would never be reached. The build would succeed and compile the wrong sources, silently.

## maxon MCP tools (PREFER THESE — **IN A WORKTREE, PASS `repoRoot`**)

**The server IS the compiler**: `maxon mcp-server --dev`, implemented under `maxon-bin/Compiler/Mcp/`.
There is no separate project and nothing to build but the compiler itself, so a rebuild of the slot is
a rebuild of the server. Prefer these tools over raw Bash invocations: faster (no shell startup),
structured results. Use Bash only where no tool covers the case.

⚠ **A RUNNING SERVER IS THE COMPILER YOU BUILT IT FROM.** Rebuilding the slot renames the running
image to `maxon.previous` and writes a new one; the live process keeps serving from the vacated image
until the host restarts it. So after a build, a tool answer still comes from the PREVIOUS compiler —
restart the MCP server when you need the new one to answer.

> ## 🟡 IN A WORKTREE, EVERY MCP TOOL NEEDS `repoRoot` — OR IT DRIVES THE **MAIN REPO**
>
> ONE stdio server process is shared by every agent in every worktree, and its default root is the
> main checkout (derived from the SERVER's own binary path). **Say nothing and you are told
> `success: true` about a tree containing none of your work.**
>
> ⇒ **In a worktree, pass `repoRoot` — the ABSOLUTE path of your worktree root — to EVERY tool call
> that acts in a tree**: `build`, `run_spec_test`, `run_scale_test` and `spec_test_outcome`. The
> user-facing tools (`run`, `test`, `fmt`, `check`, `dump_ir`, `lookup_error_code`, `info`) take none —
> they act in the host's working directory and name no tree.
>
> ```
> build(repoRoot: "C:/Users/Eric/dev/maxon/.claude/worktrees/agent-xyz")
> ```
>
> - **Every result echoes the `repoRoot` it actually used**, in the payload's `repoRoot` field —
>   answers and refusals alike. **READ IT BACK.**
> - ⭐ **THE TREE'S OWN COMPILER RUNS, NOT THE SERVER'S.** A tool acting on `repoRoot` spawns
>   `<repoRoot>/maxon-bin/.maxon/maxon`, because `stdlib/` and `runtime/` are resolved by walking UP
>   from the EXECUTABLE — the server's binary would compile your worktree against the MAIN repo's
>   sources. A tree whose slot is empty is REFUSED, naming the `build` tool.
> - **A `repoRoot` that is not a Maxon checkout is REFUSED** (`invalidParams`), never quietly swapped
>   for the main repo. Relative paths are refused too — they would resolve against the *server's* cwd.
>   A checkout is any tree holding `stdlib/`, `runtime/` and `maxon-bin/`, so a brand-new worktree qualifies
>   before anything is built in it.
>
> ⚠ These tools **EDIT** the tree they are pointed at: `run_spec_test` with `updateRequired: true`
> rewrites that tree's committed goldens, `run_scale_test` with `note:` writes a row into its
> `docs/optimization-log.md`, and `fmt` rewrites files in place.

| Task | Tool |
|------|------|
| Build the compiler | `build` — `path: "maxon-bin"`; `from:` names the compiler to build WITH |
| Run the spec suite | `run_spec_test` |
| Per-test PASS/FAIL detail | `spec_test_outcome` (requires `filter`) |
| MEASURE per-phase memory + CPU scaling — an instrument, **no verdict** | `run_scale_test` |
| Run an inline snippet or a file | `run` — `source:` for a snippet, `path:` for a file |
| Dump IR | `dump_ir` |
| Format a file or snippet | `fmt` — `source:` returns the formatted text; `path:` rewrites in place |
| Look up a 4-digit error code | `lookup_error_code` — number, `"E3014"`, or the case name |

⭐ **AN ARGUMENT NO TOOL DECLARES IS REFUSED** (`invalidParams`), never dropped: a `mmTrace: true` run
that never traced, reported back as a clean success, is indistinguishable from a leak-free run. The
refusal is by ARRIVAL against the tool roster, so it covers `mmTrace` and `dumpStages` and every other
argument nobody thought to reject. A contributor argument sent to a server started WITHOUT `--dev` is
refused the same way.

Always pair `updateRequired` with a `filter` — unfiltered, it rewrites every golden in the suite.

⛔ **`build`'s `target:` TELLS A TRIPLE FROM A MANIFEST TARGET BY THE DASH.** `wasm32-wasi` becomes
`--target=wasm32-wasi`; a bare word becomes a positional naming a target in `build.maxon`. `maxon
build` reads a bare word as another SOURCE PATH, so the two cannot be passed the same way.

**A `spec-test` filter is ONE CASE-SENSITIVE substring** of the `<spec>/<test>` label (`maxon test`
lowercases its own, and takes a comma-separated union). Neither is a list here —
`--filter=static-methods,enums` selects NOTHING. Run one filter per file and read every one.

The runner has no `--verbose` (it always prints a line per test), no `--no-batch` (its batching is
`RunStrategy`, chosen by target and host) and no `--debug-info`; it does have `--network`.

### Common flags

- `--filter=PATTERN`, `--update-required`, `--log=CATEGORY:LEVEL` (e.g. `--log=ir:debug`),
  `--mm-trace`, `--workers=<n>`, `--target=ARCH-OS`.
- **`--workers=1` is a DEBUGGING TOOL, not a gate.** It is the same pool with one worker in it, and
  the parent buffers results and reports in fixed order — **ordering cannot vary with pool size**.
  The default pool is 12 and that is the only count these processes run the suite at.

### Targets

The compiler emits `x64-windows`, `x64-linux`, `arm64-macos`, `arm64-linux` and **`wasm32-wasi`** (a
WASI Preview2 component).

> #### ⛔⛔ `--target=` CROSS-COMPILES THE PROGRAMS. IT NEVER HOSTS THE COMPILER.
>
> `spec-test --target=x64-linux` on a Windows host builds and runs the TEST binaries for Linux while
> the compiler stays a Windows process. So that lane never runs the compiler's own `main` **as** a
> Linux program — on a Linux green thread, through the Linux runtime, with that lane's frame layout —
> and it is not evidence about anything that only happens there. **CI hosts every target on its own
> architecture**: seed → `C1` → `C2` → the suite under `C2`, which is a whole class of defect a cross
> lane cannot reach.
>
> MEASURED: the x64 large-frame page walk touched-then-compared, so its last store landed below the
> frame base (`ca4abf52b8`). Harmless on an OS-grown stack; a SIGSEGV on a green thread's exact mmap'd
> one — and reachable only once the called-once inliner carried the compiler's own `main` past one
> page. **Three consecutive pushes to `main` died at exit 139 in `build-from-seed.sh` before the suite
> could start, while the cross lane read 7706/0 on the same tree.**
>
> ⇒ **TO HOST x64-linux LOCALLY, CROSS-BUILD ONCE AND THEN STAY INSIDE WSL** — the recipe is in the
> `compiler-workflow` skill.

`run_spec_test` takes `target: "wasm32-wasi"` and runs the output under the
vendored wasmtime. By hand, for ONE program:

```
./maxon-bin/.maxon/maxon build f.maxon -o out --target=wasm32-wasi
./vendor/wasmtime/wasmtime run -S cli-exit-with-code=y out.wasm
./vendor/wasm-tools/wasm-tools print out.wasm      # attribute a wrong answer to an instruction
```

⛔ **`vendor/` IS GITIGNORED AND A CLONE HAS NONE OF IT** — `scripts/fetch-vendor.sh` stages it; see the
`compiler-workflow` skill.

⚠ **THE wasm LANE IS NOT "SCALAR ONLY".** Heap, `String`, `print`, structs, arrays, closures,
interfaces and **floats** (arithmetic AND shortest-round-trip printing) all work there; the two lanes
run within a few hundred cases of each other. The families it does not run carry an explicit
`<!-- unsupported-targets: … -->` exclusion: **async / green threads, the clock builtins, argv**, plus
the x64-only CODEGEN cases (register pressure, `.rdata`, emitted symbol names), which are about x64's
output rather than about wasm. ⇒ **a float or String case failing on wasm is a BUG on that lane, not
an out-of-slice case to refuse.**

⭐ **THE MARKER NAMES THE LANES THAT CANNOT SERVE A CASE, NEVER THE ONES THAT CAN**, so a backend that
lands inherits every unmarked case instead of being excluded from all of them at once. The harness
REFUSES a key naming no supported target, refuses a marker that excludes every one of them (that is a
suspension — spell it `<!-- disabled-test: -->`), and refuses the retired `<!-- targets: -->` spelling.

⛔⛔ **DO NOT MARK A CASE THE COMPILER ALREADY REFUSES.** A lane with no substrate answers **E3104**, and
the harness reports that as a counted **SKIP** naming the case; a marker removes the case from selection
with nothing said anywhere. The two are not two spellings of one fact — one is the fact and the other is
the fact made invisible. So async, the clock, argv, file and directory IO and console stdin carry NO
marker on wasm: every one of those skips is a case a reader can count. A marker is for a case that would
otherwise go RED — an ISA-specific codegen reading, a POSIX/Windows shell spelling, a diagnostic
displaced by E3104 — and it states its reason.

⚠ **SUBPROCESS IS THE EXCEPTION, AND NEEDS THE MARKER.** A target that can never host a child process
answers **E3074** ahead of E3104 (`requireTargetSupportsCallee`), and the harness counts only E3104 as a
SKIP — so an unmarked subprocess case goes RED on wasm. Mark it `wasm32-wasi`.

## Measuring and self-checking — the `compiler-workflow` skill

Two instruments live in that skill, with their reading guides: `run_scale_test`, the per-phase memory/CPU
doubling ladder to run after any change to a pass, the IR, or a data structure the compiler indexes by;
and `scripts/fixpoint.sh`, which answers whether the compiler reproduces itself byte for byte — a
difference there is a MISCOMPILE, and a green suite cannot see it. Load the skill before acting on either.

## `tests/` — fixture corpora for the DRIVER COMMANDS

**`spec-test` is for the LANGUAGE — compiler syntax and emitted code. A DRIVER COMMAND is not that**
(user ruling), and could not be gated there anyway: a spec case is a Maxon PROGRAM the harness
compiles and runs, so it can reach `stdlib/` and `runtime/` and nothing else. Driver commands are
gated by spawning the compiler at a fixture project and asserting what it reports.

**`tests/README.md` is the authority** — it lists every corpus, the constant each is reached through,
and the rules that keep the corpora honest. Read it before touching anything under `tests/`. The three
facts worth knowing before you get there:

- ⭐ **RUN EACH CORPUS AND READ ITS PASS/FAIL COUNT.** A runner broken badly enough to report green
  having run nothing cannot detect itself, so the count is what closes the circularity.
  ```
  ./maxon-bin/.maxon/maxon.exe test tests/test-command
  ```
  ⚠ **Nothing runs these automatically** — `/land`'s battery is where they belong, beside the suite
  and the self-compile.
- ⛔ **EXPECTATIONS ARE GENERATED, NEVER HAND-WRITTEN** — e.g. `python
  tests/fmt/generate-expectations.py` runs the compiler and records its real answers, so a corpus
  pins what the tool DOES rather than what its author expected. Re-run the generator after changing
  any input, and read the diff: a generated expectation cannot tell you an answer is wrong.
  **`tests/examples/` is the one exception**: it checks the example programs against answers that
  exist outside this compiler (the Benchmarks Game's published output, an example's documented
  result), because there a generated expectation would only record whatever the compiler said.
- ⛔ **NOTHING STORED THERE IS A LIVE `.maxon` OR A REAL `.git`** unless its corpus's row says so.
  Names are `<x>.fixture` and `dot-git/`, mapped back at staging time — git refuses to commit a path
  with a `.git` component, and a real `.maxon` under `tests/` is walked by `maxon fmt`, which is the
  tool under test rewriting its own oracle.

One test per file is structural, not tidiness: a file is what ONE process runs and that process has a
5 s default deadline, so twelve compiler-spawning tests in one file report a spurious `TIMED OUT`.

## `maxon fmt`

`maxon fmt [<file|directory>]` — gated byte-for-byte by `tests/fmt/`.
⚠ **With NO PATH it formats the whole current directory** — that is its documented
default. `fmt <file>` formats only that file, `fmt <dir>` that directory; `fmt --check` and `fmt a b`
are REJECTED, exit 1, nothing written. The walk prunes any directory holding `.git`, so it cannot
descend into a nested checkout or an agent worktree.

⛔ **A SUBTREE THAT IS NOT A CHECKOUT NEEDS A `.maxonignore`, AND `website/` IS THE ONE THAT DOES.**
The `.git` rule protects a sibling repository, not a directory of this one — so without the marker a
root `fmt` rewrites `website/src/examples/*.maxon` in place and walks every directory under
`node_modules/`. MEASURED: remove `website/.maxonignore`, mis-format one of those files, run `fmt`,
and it is silently reformatted. The marker is a FLAG whose contents are never read, and both walks
honour it — `fmt`'s and the compiler's own `collectMaxonSources`.

⚠ **THE FORMATTER SELF-TEST RIDES `spec-test`** (`requireFormatterPreservesItsCorpus`, called from
`SpecWorkerPool`) and REDDENS THE SUITE if formatting loses a comment, duplicates one, writes a
lexer-error sentinel into a file, or stops being idempotent. There is no `fmt-selftest` command.
It carries 8 comment shapes + 4 unlexable sources, with `UrlInPlainString` and `NoMultilineLiteral`
as controls that must stay GREEN. Three separate silent
source-corrupting defects reached the tree before it existed; a preservation check phrased as
*presence* passes duplication, so it asserts **multiplicity**.

## ⚠ Running a suite by hand: REDIRECT IT TO A FILE. Never pipe through `head`/`tail`/`grep`.

```
mkdir -p temp
./maxon-bin/.maxon/maxon.exe spec-test > temp/spec.log 2>&1; echo "exit=$?"
grep -n '^FAIL' temp/spec.log
```

Then **read the file** at each hit for the full reason. **A pipe decides what to keep before you know
what failed**, so when the run goes red the detail is already gone and the only way back is running
the whole suite again. Grep alone is not enough either: a failed compile embeds the compiler's entire
stderr, so the marker line is a headline, not the evidence.

Do not assume the console is small: **the runner prints one line per test (~1,500) and only then
the summary**, with failures wherever those tests fall in declaration order — `tail` shows PASS lines
while the reason sits thousands of lines above. `temp/` is gitignored. The MCP tools need none of this.

⚠ **THE SUITE RUNS EVERY TEST BINARY WITH CWD `temp/`**, so that directory is shared with anything
else you put there. Stage a binary you must keep somewhere else.

## Error codes — ONE registry, and it is the enum itself

**`maxon-bin/Compiler/ErrorCodeRegistry.maxon` IS the registry.** It carries the number, the canonical
name and the doc text for every code, and it is AUTHORED — edit it directly.

**To add a diagnostic:** take the next free number in the right band, add a case, write the code that
emits it.

- **A duplicate NAME does not compile**, and `ErrorCode.Foo` does not compile unless the enum declares
  it — both are structural, so neither needs a checker.
- **A duplicate NUMBER is neither**: two cases may carry one `"E3099"` and the program is well-formed.
  `Testing/ErrorCodeSelfTest.maxon` walks `allCases`/`allCaseNames` on every `spec-test` and refuses
  one, naming BOTH claimants.
- **The stage is derived from the leading digit** (1xxx lexer … 9xxx internal) and is never written
  down, so it cannot disagree.
- **NEVER REFERENCE A CODE BY ITS NUMBER OUTSIDE THE REGISTRY.** Use the generated member
  (`ErrorCode.semanticUnneededCast`, plus `.rawValue` for the `"E3010"` spelling). A literal `"E3010"`
  in a source file is a second copy of the number space: renumber the code and every gate stays green
  while the code that matched it silently stops matching anything.

## Spec files

- **Golden drift needs no attention.** The fragments under `specs/fragments*/` are regenerated by every
  run; whatever a run mints, rewrites or deletes is committed with the change (`git add -A specs/`) —
  never measured, investigated, explained or reverted. (A `RequiredIR` block in a spec file is not
  drift: it is a test input, and a mismatch is a failing test.)
- Old 3-digit error codes (e.g. `E022`) in spec files must be updated to the new 4-digit codes.
- If tests using RequiredIR fail, regenerate with `--update-required` **plus a `filter`**.
- `--update-required` regenerates RequiredIR but **not** `maxoncstderr` blocks — an error-code
  renumber moves those by hand.

## ⚠ A running MCP server keeps answering from the compiler it was started as

The server is the compiler, so editing `maxon-bin/Compiler/Mcp/` and rebuilding the slot leaves the
LIVE process running the old code: a build renames the running image to `maxon.previous` rather than
overwriting it (an OS will not let a running executable be deleted, but will let one be renamed), and
the process serves on from the vacated image. **RESTART THE MCP SERVER after a build whose result you
want the tools to reflect.**

`tests/mcp/rebuild.test.maxon` pins the surviving half of that: the process keeps answering across the
replacement rather than dying mid-session.
