You do not care if an issue is pre-existing. Just debug and fix it.

Do not use "cmd /c" to run commands

There are no time constraints. Complexity doesn't matter. If you are fixing an issue then fix it properly. No workarounds.

## maxon-dev MCP tools (PREFER THESE — **IN A WORKTREE, PASS `repoRoot`. SEE THE BOX.**)

> ## 🟡 IN A WORKTREE, EVERY MCP TOOL NEEDS `repoRoot` — OR IT DRIVES THE **MAIN REPO**
>
> **The tools default to the main checkout, and they cannot do otherwise: ONE stdio server process is
> shared by every agent in every worktree, and its working directory is the MCP host's, not yours.**
> The default root is derived from `Process.executablePath()` — the SERVER's own binary — which lives
> in the main repo. **So if you are in a worktree and you do not say which tree you mean, you will be
> told `success: true` about a tree containing none of your work.**
>
> ⇒ **In a worktree, pass `repoRoot` — the ABSOLUTE path of your worktree's root — to EVERY tool call.**
> All nine take it. That is the whole fix; there is nothing else to remember.
>
> ```
> build(target: "csharp", repoRoot: "C:/Users/Eric/dev/maxon/.claude/worktrees/agent-xyz")
> ```
>
> **Two things make a mistake here VISIBLE rather than silent, which is what it was before:**
>
> - **Every result echoes the `repoRoot` it actually used** — successes in `repoRoot`, failures in
>   `error.data.repoRoot`. **READ IT BACK.** A `build` that answers
>   `"repoRoot": "C:\Users\Eric\dev\maxon"` when you are in `.claude/worktrees/agent-xyz/` has just
>   built the wrong tree, and now says so. An instrument states what it measured.
> - **A `repoRoot` that is not a Maxon checkout is REFUSED** (`invalidParams`, naming what was missing).
>   It is never quietly swapped for the main repo — that fallback would be the original bug wearing
>   your intention as a mask. Relative paths are refused for the same reason: they would resolve
>   against the *server's* working directory, not yours. A checkout is any tree holding `stdlib/` and
>   `maxon-sharp/` — **a brand-new worktree qualifies, before anything has been built in it**, so
>   `build target=csharp repoRoot=<your worktree>` is the correct first call.
>
> ⚠ Still worth knowing what these do to the tree they are pointed at: **`run_spec_test` with
> `updateRequired: true` REWRITES that tree's committed goldens**; **`run_scale_test` with `note:`
> writes a row into that tree's `docs/optimization-log.md`**; and **`fmt` rewrites files in place,
> relative to that tree**. Point them at the wrong one and they do not merely report the wrong answer —
> they *edit*. Which is exactly why the root is now yours to state and theirs to echo.
>
> *(Found 2026-07-14, when an agent's `build` "succeeded" on a tree with none of its work in it. It
> burned five agents at once: this file said "PREFER THE MCP TOOLS" while the rung workflow said "work
> in an isolated worktree", and the two had been silently contradicting each other for some time. It is
> the project's own signature bug — ONE FACT WRITTEN DOWN TWICE — at the tooling level. Fixed the same
> day: `repoRoot` is that fact, written down once, by the only party who knows it.)*

Prefer the `maxon-dev` MCP tools over raw Bash invocations of the compiler binaries. They are faster (no shell startup), return structured results, and are the canonical way to drive builds, tests, and one-off snippets in this project. Bash invocation of `./bin/maxon.exe` should only be used when no MCP tool covers the case. **In a worktree, pass `repoRoot` (see the box).**

Two compilers are driveable, and every tool that drives one takes a `compiler`
(or `target`) naming which: `"csharp"` (the C# bootstrap) or `"shv2"` (the
ground-up rewrite, whose suite is `specs-shv2`). **The v1 self-hosted compiler
(`maxon-selfhosted`) is DEPRECATED and no longer reachable from the MCP** — no
`selfhosted` token, no build target, no spec-test runner. Drive it by hand if you
must (see Building and Testing below).

| Task | Use this tool |
|------|---------------|
| Build the C# compiler | `mcp__maxon-dev__build` with `target: "csharp"` |
| Build the shv2 compiler | `mcp__maxon-dev__build` with `target: "shv2"` (built BY the bootstrap — build `csharp` first if it is stale) |
| Run a spec-test suite | `mcp__maxon-dev__run_spec_test` (set `compiler` to pick the suite; `"shv2"` runs `specs-shv2`) |
| MEASURE per-phase MEMORY + CPU-TIME SCALING (shv2) — an instrument, **no verdict** | `mcp__maxon-dev__run_scale_test` (no `compiler` arg — shv2 only) |
| Get per-test PASS/FAIL detail for a filter | `mcp__maxon-dev__spec_test_outcome` (requires `filter`; either compiler) |
| Run an inline Maxon snippet or a file | `mcp__maxon-dev__run_program` (requires `compiler`) |
| Dump IR (optionally per-stage) | `mcp__maxon-dev__dump_ir` (requires `compiler`; `dumpStages: true` for stage-by-stage artifacts — csharp only) |
| Format a Maxon file or snippet | `mcp__maxon-dev__fmt` (csharp `maxon fmt`) |
| Look up a 4-digit error code | `mcp__maxon-dev__lookup_error_code` |
| Debug memory-management issues | `mcp__maxon-dev__mm_trace_analyze` |

`build` takes ONE compiler per call — the old `both` / `all` chains sequenced the bootstrap ahead of the retired self-hosted compiler and are gone.

**Every tool in that table takes `repoRoot`** — the tree it acts on. Omit it and you get the main checkout (the one hosting the server binary); in a worktree that is a false green. See the box.

Flags like `--filter`, `--update-required`, `--log`, `--mm-trace`, and `--target` are exposed as parameters on the relevant tools (`filter`, `updateRequired`, `log`, `mmTrace`, `target`). When iterating on a specific failing test, pass `filter` to `run_spec_test` or use `spec_test_outcome` for per-test detail. `target` cross-compiles to what the CHOSEN compiler can emit — the bootstrap has x64 and arm64 emitters (`"x64-windows"`, `"arm64-macos"`); **`run_spec_test` DOES take `target: "wasm32-wasi"` for shv2** and runs the emitted component under the vendored wasmtime (MEASURED 2026-08-03 — this file said it was "rejected outright", which is stale). Driving it by hand is still the way to run ONE program rather than a suite: `./maxon-shv2/.maxon/maxon-shv2 build f.maxon -o out --target=wasm32-wasi`, then `./vendor/wasmtime/wasmtime run -S cli-exit-with-code=y out.wasm` (on Windows the runnable binary is `wasmtime.exe` — the extensionless `wasmtime` beside it is a Mach-O for the Mac lane). Disassemble with `./vendor/wasm-tools/wasm-tools print out.wasm`, which is how a wrong ANSWER on that target gets attributed to an instruction.

⚠ **THE wasm LANE IS NOT "SCALAR ONLY", AND THIS FILE SAID SO FOR TWO WEEKS AFTER IT STOPPED BEING TRUE.** MEASURED 2026-08-03 (rung X5), same tree, same binary: `spec-test --target=wasm32-wasi` runs **3471 cases, 0 failed**, against the host lane's **3694, 0 failed** (re-measured 2026-08-03 at X5's merge; both counts grow with every ported spec, so treat them as a DATED SNAPSHOT showing the two lanes are close, never as a target to hit) — heap, `String`, `print`, structs, arrays, closures, interfaces and **floats** (arithmetic AND shortest-round-trip printing) all work there. The families it does not run are the ones carrying an explicit `<!-- targets: … -->` restriction: **async / green threads, file and directory IO, the clock builtins, argv**, plus the x64-only CODEGEN cases (register pressure, `.rdata`, emitted symbol names) which are about x64's output rather than about wasm. ⇒ **a float or String case failing on wasm is a BUG on that lane, not an out-of-slice case to refuse** — X5's 26 red cases were one codegen defect (`emitDivMod`/`binOpImm` taking their width from an operand), not a missing feature. The `maxon-selfhosted` wasm backend remains the read-only reference for the families still absent.

### `run_scale_test` — the scaling INSTRUMENT (shv2 only). ⚠ NOT A GATE.

**`scale-test` collects data for TREND ANALYSIS. It has no verdict, and there is nothing to pass.** It compiles a ladder of generated programs — six rungs, each double the last — and measures **MEMORY and CPU TIME per phase per rung**. It fits nothing: there are no growth exponents, because a doubling ladder already *is* the growth (see the next section). **Run it after any change to a pass, the IR, or a data structure the compiler indexes by, and READ it.** A default run is **~17 s** (measured 2026-07-28).

⚠ **`DefaultRepeatCount` is 1 as of 2026-07-28 (user ruling), and it had been 3.** The repeats bought the routine read nothing — the per-phase **allocation tables are BYTE-IDENTICAL** at `--repeat=1` and `--repeat=3`, verified on one unchanged tree, while the run cost **51 s vs 17 s** (the cost is linear in the count). **Raise it back with `--repeat=3` when A/B-ing two binaries' CPU**, where the effect can be a few percent and a single sample cannot carry it. Two consequences, both deliberate: **CPU rows logged before that date are MINIMA and later ones are single samples** (measured ~+9–10% apart at rung 5 — the changeover is recorded in `docs/optimization-log.md`, do not read the step as a regression), and **the cross-repeat allocation-equality check cannot run at 1** — `--repeat=2` buys it back.

**The artifact is the trend: `docs/optimization-log.md`** — a dated table you read downwards. The question it answers is *"what has this compiler's cost actually done, change by change?"*, not *"may I merge?"*

⚠ **DO NOT CHASE A GREEN SCALE-TEST. There isn't one.** A curve that looks wrong is a **reading to explain**, not a light to turn green. And **never touch the instrument to make a number look better** — a past pass exempted `regalloc:liveness` from a noise check to stop it complaining, which was treating the symptom of a verdict that should not have existed. The right response to a curve that bends is to say WHY it bends. (`liveness` bills two call sites into one bucket — one per function, linear; one after every split, superlinear — so it is a *sum of two exponents* and bends on a perfectly idle machine.)

✅ **The gate apparatus is GONE** (2026-07-14): the committed memory goldens, the exponent budgets, `--update-required` and the PASS/FAIL/VOID/NOISY verdicts have all been deleted, along with `ScaleGates.maxon` and `ScaleBaseline.maxon`. **Do not reintroduce them.** `scale-test` exits **0** whatever the numbers say; a non-zero exit means the **RUN ITSELF BROKE** (a degenerate corpus, a rung that failed to compile, an IO failure) and produced no valid data — never that a number was surprising.

### ⚠ IT COLLECTS MEMORY **AND CPU TIME**. NOT WALL TIME, AND NO CURVES.

**`scale-test` measures per-rung, per-phase MEMORY — allocations, frees, bytes — and, since 2026-07-25, per-rung, per-phase CPU TIME.** There is still no WALL time, no exponent fits and no residuals. Each of those three has its own reason, and they are not the same reason:

- **The ladder DOUBLES, so the RATIO between consecutive rungs IS the growth.** Linear ⇒ allocations double. Quadratic ⇒ they quadruple. **You read it straight off the raw numbers.** An exponent fit adds no information the doubling ladder does not already give you — it is *interpretation dressed up as measurement*, and it is what dragged in the residual, which dragged in the NOISY verdict, which is what once led an agent to **edit the instrument to stop it complaining**.
- **WALL time cannot be trended, and that argument is UNCHANGED.** It counts every *other* process on the box, so a dated table of it would be comparing a loaded machine in July against an idle one in August. *(Measured: allocation deltas read 0.000 across every curve on an unchanged compiler while time deltas read +0.09…+0.29, purely because the machine was busy — and one run read `phase:parse` at ×5.03 then ×1.78 across a DOUBLING ladder, which is not a curve of any shape, it is preemption.)* Wall nanos are still emitted in the metrics TSV and the scale runner still **deliberately skips** them.
- **CPU time CAN be trended, because it is not a clock.** `__Builtins.threadCpuTicks()` — a bootstrap intrinsic added 2026-07-25 (`QueryThreadCycleTime` on Windows ⇒ TSC ticks; `clock_gettime(CLOCK_THREAD_CPUTIME_ID)` on macOS ⇒ nanoseconds) — advances **only while the CALLING THREAD is scheduled**. It cannot see preemption and it cannot see any other process, which is precisely the property wall time lacks. *(Measured across a 300 ms sleep: it advanced **837,520 ticks** while wall time advanced **301,000,000 ns**.)* `PhaseProbe` brackets it alongside the wall clock and the memory counters, so every `CompilePhase` and every `RegAllocPhase` reports it; `scale-test` prints a third per-phase table beside allocations and bytes, and `docs/optimization-log.md` carries a third table under **`## CPU`**.

> ### ⚠ THE CPU COLUMN DOES NOT READ LIKE THE OTHER TWO — IT HAS A NOISE BAND, AND A PLATFORM-DEFINED UNIT
>
> **Allocations and bytes are exact and bit-for-bit reproducible: ANY movement is real. CPU ticks are NOT.** They still move with turbo, thermal throttling and cache pressure from other cores — **a few percent** — so **a movement inside that band is not a datapoint.** Against the only question a doubling ladder asks (**×2 is linear, ×4 is quadratic**) that band has a **100% margin**, which is exactly why a few percent is good enough. Against a claimed 3% constant-factor win it is worth nothing — use the allocation columns for that.
>
> **The unit is platform-defined and the platforms do not agree** (TSC ticks vs nanoseconds), and there is **no honest conversion** — `QueryPerformanceFrequency` is the *performance counter's* rate, not the TSC's, so any normalization would be a guess wearing a unit's name. ⇒ **Compare RATIOS between rungs, which are unit-free; compare absolutes only within one platform.**

✅ **Why a third column had to exist: a cost that ALLOCATES NOTHING was invisible, and this project keeps measuring them.** Every one of these is a measured quadratic the memory-only instrument read as **Δ0** — the op-insertion quadratic found inside `regalloc:splitting` (fitted `16.0n + 6.59n²`, **68% of the whole compile at N=1024**, and allocation-free); `regalloc:splitting` again at **×3.9 then ×5.0 per doubling, ~98% of the compile**; `requireInterfaceForParse`, whose two arms allocate **identically to the digit, 1,417,523 both ways**, against a **+24.15 ms** parse delta; `getBlockByIdIn`'s per-guard-site linear scan; and the two cascade fixpoint duals, whose commit (`4c4524b45`) says it outright — *"'0 corpus hits' measured the instrument's blind spot, not the cost."* **Those filings are now RE-MEASURABLE rather than structurally unmeasurable.** ⚠ The list that marked WHICH — Workstream O's *"Measured debt the trend log carries"* — lived in `maxon-shv2/PLAN.md`, retired 2026-09-01; the measurements themselves survive in `docs/optimization-log.md` and in the commits named above. Recover the list with `git show 61535d55d2^:maxon-shv2/PLAN.md` if you need it.

⚠ **It cures ONE blind spot, not both. The other is the CORPUS**, and a Δ0 from a ladder that cannot express the feature is still *"the instrument's blind spot, not the cost"* — in **every** column, CPU included. *(The clearest case: `regalloc:splitting`'s float-across-calls quadratic was hidden because the corpus's `floatSpill` knob was **4** — few enough that every float fit a register and no split happened — not because the cost was allocation-free. The knob went 4 → 12; that is a corpus fix.)* **Two different blindnesses; never credit one with the other's fix.**

⚠ The compiler's own per-phase timing (`--metrics=<path>`, `--log=compiler:debug`) is still a **different thing** from `scale-test`, is useful interactively, and stays — and it gained the same column at the same time. `--metrics` appends a **7th** TSV field `cputicks` (appended, not slotted in beside `nanos`, so every existing field keeps its index), and `--log=compiler:debug`'s timing table gained a **`cpu%` beside the wall `%`**. **A phase where the two disagree spent its wall time NOT RUNNING**: `load` measures 51.2% of wall but only 25.4% of CPU because it waits on file IO, while `regalloc` is 22.2% of wall and 36.1% of CPU.

**So: read the per-rung numbers, and know which kind you are reading. In the MEMORY columns any movement for the same input is REAL, every time; in the CPU column, a movement is real once it is OUTSIDE the noise band.** Explain it, attribute it, and record the reason in the log at the one moment it is still known — the instrument can see exactly WHAT moved and can never see WHY.

`perType: true` adds an untimed `--mm-trace` pass that prints TWO ranked tables, each with its own growth exponent:

- **by TYPE** — the way to answer "the memory numbers moved, but of *what*?". Names the data structure (a `LiveIndexColumn` at exponent 2.17 is a quadratic).
- **by SCOPE** — the function that made the allocation, which the type table structurally *cannot* tell you: a `String` row can never say that 150 of them came from `emitFixedToken`. A constant-factor hog hides inside its type; it is a single named row here. **This is the column that finds things.**

It is slow (several minutes) and off by default.

There is **no `maxon clean` command** — it prints usage and exits 1. To force a from-source stdlib rebuild, delete `stdlib/.maxon/cache/*.mxc` yourself; the self-hosted compiler recompiles the stdlib whenever its cache is absent. The C# bootstrap's stdlib cache is in-memory only, so it always builds the stdlib fresh.

**The tools say what a compiler cannot do rather than pretending.** ⚠ **THIS PARAGRAPH USED TO SAY shv2's RUNNER "IMPLEMENTS ONLY `--filter`, `--update-required`, AND `--log`", AND THAT WAS STALE BY FOUR FLAGS — MEASURED 2026-09-01 off `Main.maxon`'s own parse loop.** It also accepts a positional `[dir]`, **`--network`**, **`--workers=<n>`** and **`--target=<cpu>-<os>`**; `--target` is not rejected at all, and `--network` is the BOOTSTRAP's gap (only shv2's runner knows it), which is why `checkSpecTestFlags` now refuses in both directions. What shv2's runner genuinely lacks against the bootstrap's is `--verbose` (it always prints a line per test), `--no-batch` (its batching is `RunStrategy`, chosen by target and host, not a flag) and `--debug-info`; its `--filter` is ONE substring — **and so is the bootstrap's. THIS LINE CLAIMED THE BOOTSTRAP'S "UNIONS ON COMMAS" AND THAT IS FALSE: MEASURED 2026-09-02, `--filter=static-methods,enums` selects NOTHING (`0 passed, total: 0`) while `--filter=static-methods` selects 3** — `TestRunner.cs`'s match is a single case-insensitive `Contains`. Neither runner takes a list; run one filter per file and read every one. `mmTrace` and `dumpStages` are still REJECTED with an `invalidParams` error naming the gap, never silently dropped. ⚠ **shv2 NOW HAS `fmt` (2026-09-02)** — `maxon-shv2 fmt [<file|directory>]`, the bootstrap's engine ported token for token, gated byte-for-byte against the reference by `tests/fmt/`. The MCP `fmt` tool takes an OPTIONAL `compiler` (default `csharp`) — optional rather than required because this is the one command where the two are meant to agree byte for byte, so a caller with no opinion is being given the reference, not a coin flip. Always pair `updateRequired` with a `filter` — unfiltered, it rewrites every golden in the suite.

## Building and Testing

Binary names differ by host OS: Windows produces `maxon.exe` / `maxon-shv2.exe`, Linux and macOS produce `maxon` / `maxon-shv2` (no extension). Commands below show the Windows form; drop the `.exe` on Linux/macOS.

### C# bootstrap compiler (maxon-sharp)

- **Build:** `dotnet build` (run from `maxon-sharp/`)
- **Spec tests:** `./bin/maxon.exe spec-test`

The C# compiler binary is at `./bin/maxon.exe` (Windows) or `./bin/maxon` (Linux/macOS).

### shv2 compiler (maxon-shv2)

- **Build:** `./bin/maxon.exe build maxon-shv2` (requires C# compiler already built)
- **Spec tests:** `./maxon-shv2/.maxon/maxon-shv2.exe spec-test` (the `specs-shv2` suite)
- **Unit tests (`maxon test`'s own):** `./maxon-shv2/.maxon/maxon-shv2.exe test maxon-shv2/Testing/test-command`

> ### ⚠ `maxon test`'s OWN TESTS ARE NOT IN THE SPEC SUITE, AND THEY ARE NOT SUPPOSED TO BE
>
> **`spec-test` is for the LANGUAGE — compiler syntax and emitted code. A DRIVER COMMAND is not that**
> (user ruling, 2026-09-02), and it could not be gated there anyway: a `specs-shv2` case is a Maxon
> PROGRAM the harness compiles and runs, so it can reach `stdlib/` and nothing else —
> `LspPositionSelfTest.maxon:3-6` states it, and **no shv2 command is gated by a spec case, not one**.
>
> `maxon test` is gated by **itself**: `maxon-shv2/Testing/test-command/` holds ordinary `test`
> declarations that spawn the command at the fixture projects in `maxon-shv2/Testing/test-fixtures/`
> and assert its report with `Expect`. Each fixture carries an `expected.txt` / `expected-exit.txt`
> **generated from the BOOTSTRAP's working `maxon test`**, so the contract is the reference
> compiler's real output rather than one we invented.
>
> ⭐ **RUN IT UNDER BOTH COMPILERS — that is what closes the circularity.** A runner broken badly
> enough to report green having run nothing cannot detect itself; the bootstrap's `maxon test` is an
> independent oracle that still reports honestly. Both must pass and both must report the SAME count:
> ```
> ./maxon-shv2/.maxon/maxon-shv2.exe test maxon-shv2/Testing/test-command
> ./bin/maxon.exe                    test maxon-shv2/Testing/test-command
> ```
> ⚠ **Nothing runs these automatically** — `/land`'s battery is where they belong, beside the suite and
> the self-compile. One test per file is structural, not tidiness: a file is what ONE process runs and
> that process has a 5 s default deadline, so twelve compiler-spawning tests in one file report a
> spurious `TIMED OUT`.
>
> ### ⭐ AND THE HOME FOR THE NEXT ONE IS `tests/`, NOT `maxon-shv2/Testing/`
>
> `maxon fmt`'s corpus is `tests/fmt/` (added 2026-09-02, the directory's first tenant). Run it the
> way the box above runs `test`'s — **under BOTH compilers, and they must agree**:
> `maxon-shv2 test tests/fmt` and `./bin/maxon.exe test tests/fmt`.
> `test-command`/`test-fixtures` above predate it and are the next to move; that split is in progress,
> not a second convention. **`tests/README.md` states the six rules**, each answering a hazard whose
> obvious alternative fails SILENTLY. The two worth knowing before you touch any of it:
>
> - ⛔ **NOTHING STORED THERE IS A LIVE `.maxon` OR A REAL `.git`.** Names are `<x>.fixture` and
>   `dot-git/`, mapped back at staging time. Git refuses to commit a path with a `.git` component —
>   and a real `.maxon` under `tests/` is walked by `maxon fmt` over the checkout, which is **the tool
>   under test rewriting its own oracle**. It would re-bless every expectation, `already-formatted`
>   included, which would then be green forever by construction. For the same reason there is **no
>   `.maxonignore` there** (it would hide the corpus from the command it gates) and `.gitattributes`
>   marks it `-text` (git must not normalize line endings in a file compared as raw bytes).
> - ⛔ **EXPECTATIONS ARE GENERATED, NEVER HAND-WRITTEN** — `python tests/fmt/generate-expectations.py`
>   runs the BOOTSTRAP and records its real answers. That is what makes the corpus a parity oracle for
>   a port rather than a record of what its author expected. Re-run it after changing any input.

The shv2 compiler binary is at `./maxon-shv2/.maxon/maxon-shv2.exe` (Windows) or `./maxon-shv2/.maxon/maxon-shv2` (Linux/macOS).

**⭐ THE EMITTED-CODE INSTRUMENT: `scripts/self-host-ab.sh`.** A green suite and `scale-test` both measure
the compiler's LOGIC, which every stage of the self-host chain shares byte for byte. The QUALITY OF THE
CODE shv2 EMITS is a different question, and this is the one command that answers it: it builds stage-2
(stage-1 compiling shv2) and stage-3 (stage-2 compiling shv2), `cmp`s them (the fixpoint gate — a
difference is a MISCOMPILE), times both self-compiles, and runs `scale-test` on stage-1 and stage-2
INTERLEAVED, printing the per-phase ratio table (allocations, bytes, CPU) of stage-2 over stage-1. Same
logic in both binaries ⇒ any allocation ratio above 1.00 is a construct shv2's codegen allocates for and
the bootstrap's does not. MEASURED 2026-08-25: stage-2 self-compiles in 5m01 against stage-1's 2m49
(×1.8) with ×3.24 the allocations, 67% of the CPU gap in `regalloc` — the rungs that follow from it are
the **`EC1`–`EC3`** rows of the retired slice board (`git show 61535d55d2^:maxon-shv2/PLAN.md`), each carrying its readings. `--profile` adds the
function-level attribution through `scripts/sample_profile.py`, which reads shv2-emitted binaries too
(no `.mxdbg` needed — their `__symtable` closes `.text`). ~15 min; writes only under `temp/selfhost/`.

### Self-hosted compiler (maxon-selfhosted) — DEPRECATED as a PRODUCT, not as a SOURCE

**⭐ READ IT. It is 191,487 lines of working, debugged Maxon**, written against the same language and the
**same `stdlib/`** shv2 uses. Every hard mechanism on the shv2 ladder — ownership, closures, generics +
layout descriptors, witness tables, `async`/green threads, the emitted runtime — already exists there
with its bugs paid for. **When implementing anything in shv2, find the v1 file that already does it and
read it first.** Not re-deriving that knowledge is the plan.

⚠ **But do NOT blindly copy it. shv2 is a deliberate rewrite, and a number of things it does are BETTER**
— block args not phi nodes, parser-minted `ValueId`s not name strings, 3 tiers (Maxon → Std → Target) not
4, static ownership from commit 1, the flat `StdOp`. **Where shv2 departs, the departure IS the thesis**,
and v1 is merely how the old one happened to do it. **Both directions are decisions and both need a
reason: a divergence needs one, and so does a copy** — *"it works in v1"* is not a reason, because **v1 is
debugged, not FAST** (its register allocator was ~74% of self-compile time — port an algorithm and you
port its cost curve).
*(The clearest case: the register allocator ports **lessons**, not code — shv2's is a deliberately
different linear SSA-chordal design. Keep v1's correctness traps, not its reactive spill loop.)*

⚠ **IT NO LONGER BUILDS** (verified 2026-07-13). It has bit-rotted against the current bootstrap:
`error E3005: Cannot return 'TypeNameIdArray' from function declared to return 'RegIntArray'` in
`Targets/X64/X64RegisterAlloc.maxon` and `Targets/Arm64/Arm64RegisterAlloc.maxon` — a consequence of
`e4146cf8e` ("a generic's RANGED element type is part of its type"), which made two array typealiases
over different ranges distinct types.

✅ **This is ACCEPTED, and it is NOT being repaired** (user, 2026-07-14). It costs almost nothing,
because **everything v1 is still USED for reads its source rather than runs it**:
- **Porting its code into shv2 — its whole remaining job — is unaffected.**
- **`lookup_error_code` still works**: it parses `ErrorCode.maxon`, it does not execute it.

What is genuinely lost: you cannot **run** it — so v1's own wasm backend cannot be executed. That is now
largely moot: **shv2 has its own `wasm32-wasi` scalar backend as of 2026-07-18** (WASI Preview2 component),
and v1's wasm sources are the read-only reference for the deeper slices. And v1 — the only
dictionary-passing + witness-table compiler in the tree — cannot
be used to *measure* whether shv2 needs a witness slot for a `Hashable` constraint. That question
therefore rests on an inference (under dictionary-passing there is no route to `element.hash()` on a type
parameter except a witness slot), and shv2 will answer it definitively when it reaches generics at P1.6.

Not driveable from the `maxon-dev` MCP. To attempt it by hand:

- **Build:** `./bin/maxon.exe build maxon-selfhosted` (requires C# compiler already built)
- **Spec tests:** `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test`

The self-hosted compiler binary is at `./maxon-selfhosted/.maxon/maxon-selfhosted.exe` (Windows) or `./maxon-selfhosted/.maxon/maxon-selfhosted` (Linux/macOS).

### Common flags

- `--filter=PATTERN` — run only tests matching a pattern
- `--update-required` — regenerate RequiredIR blocks
- `--log=CATEGORY:LEVEL` — enable detailed logging (e.g., `--log=ir:debug`, `--log=codegen:trace`)
- `--mm-trace` — trace memory management operations (useful for memory leak debugging)
- `--target=ARCH-OS` — test a specific target (`x64-windows`, `arm64-macos`, `wasm32-wasi`). The bootstrap emits x64/arm64; the **shv2 binary** adds `wasm32-wasi` (a WASI Preview2 component — run the output under `vendor/wasmtime/wasmtime[.exe] -S cli-exit-with-code=y`). It is **not** a scalar-only lane — see the measured scope note above.

### ⚠ NEVER `--workers=1`, and NEVER `fmt` with arguments

Both of these lived in three agent definitions apiece and in the rung skill, which is four copies of
one fact — the bug this file keeps naming. They belong here, once, because they are true of anyone
driving these binaries:

- **`--workers=1` is a DEBUGGING TOOL, not a gate.** There is no worker-count invariance step in any
  process here, and it was deleted from the rung battery for a measured reason: `--workers=1` is the
  same pool with one worker in it, not a separate serial branch, and the parent never prints a result
  as it arrives — it buffers and reports in fixed order (`maxon-shv2/Testing/SpecWorkerPool.maxon:17-34`).
  **Ordering cannot vary with pool size**, so the check re-derived a known answer at full suite cost.
  Reach for it when chasing a suspected nondeterminism or reading a failure serially; never as a gate.
  **The default pool is 12 and that is the only count these processes run the suite at.**
- ⚠ **`fmt` WITH NO PATH FORMATS THE WHOLE CURRENT DIRECTORY — that is its documented default, not a
  bug** (`maxon fmt [<file|directory>]`, default: current directory). Give it a path when you mean one
  file. ⛔ **This bullet used to say "fmt with arguments IGNORES them and reformats the ENTIRE TREE",
  and that is FALSE — MEASURED 2026-08-20, all five spellings:** `fmt <file>` formats **only that
  file**; `fmt <dir>` formats that directory; `fmt --check` and `fmt a b` are **REJECTED, exit 1,
  nothing written**. It was presumably true once — `RunFmt`'s own comment records the shape (an
  unrecognized flag matched no path, fell back to the current directory and rewrote everything) and
  `EnumerateFormattableFiles` records the incident it caused, *"one accidental whole-tree run rewrote
  92 files across two agent worktrees"*. Both are now guarded: flags are refused, and the walk prunes
  any directory holding `.git`, so it cannot descend into a nested checkout or an agent worktree.
  ⇒ The claim outlived its defect by long enough that an agent (me, 2026-08-20) repeated it into a
  commit message without measuring. **Check `RunFmt` before trusting this bullet again.**
  ✅ **The COMMENT-DELETING bug is FIXED (2026-08-20) and `fmt` is safe to run.** One run over a
  pristine `maxon-shv2/Compiler/Runtime/GtRuntime.maxon` used to destroy **338 comment lines**,
  silently, exit 0. The cause was in the LEXER, not the formatter — `Advance()` did not count a
  newline consumed inside a byte-string literal, so token line numbers drifted below true source lines
  and the formatter's line-keyed comment map stopped finding them. **The same defect made EVERY
  DIAGNOSTIC after such a literal name the wrong line** (measured: an undefined function on line 6
  reported at line 4). Counting now happens in `Advance` itself, so no scanner can forget it.
  ⚠ **`maxon fmt-selftest` runs on every `dotnet build` and FAILS IT** if formatting loses a comment
  or stops being idempotent. Sabotage-proved: with the original defect restored, its byte-string cases
  go red and the build stops — while its no-multi-line-literal control still PASSES, which is
  precisely why nothing caught this for two weeks.
  ⛔⛔ **AND IT CORRUPTED SOURCE TWO MORE WAYS UNTIL 2026-09-02 — BOTH SILENT, BOTH EXIT 0, BOTH
  REPORTED AS `formatted:`.** Found while building `tests/fmt/`, not by any gate:
  **(a) A LEXER ERROR IS A TOKEN, NOT A THROW**, so `FormatCore`'s catch never fired and the emit loop
  wrote the error's own sentinel into the file — `let s = "unterminated` became
  `let s = __unterminated_string__:Unterminated string literal`, and `/* unterminated` became
  `__unterminated_block_comment__:Unterminated block comment`. The formatter's own comment claimed the
  opposite was happening. The guard now asks the TOKEN TYPE, not the two sentinel spellings, so a third
  lexer error cannot reopen it.
  **(b) A STRING CAN CONTAIN A STRING.** `"{f("//x")}"` is ONE literal — the interpolation's expression
  holds a second string — so the comment harvester's single `inString` bool closed at the INNER quote,
  read the tail as a trailing comment, and appended a phantom copy of it to the line. Corruption by
  DUPLICATION, which a preservation check phrased as presence passes and which is perfectly idempotent;
  only MULTIPLICITY catches it. Replaced with a nesting stack.
  ⇒ **`fmt-selftest` now carries 8 comment shapes + 4 unlexable sources**, every one sabotage-proved,
  with `UrlInPlainString` and `NoMultilineLiteral` as controls that stay GREEN under the sabotage.

### ⚠ Running a suite by hand: REDIRECT IT TO A FILE. Never pipe it through `head`/`tail`/`grep`.

```
mkdir -p temp
./maxon-shv2/.maxon/maxon-shv2.exe spec-test > temp/shv2-spec.log 2>&1; echo "exit=$?"
grep -n '^FAIL' temp/shv2-spec.log          # shv2's marker; the bootstrap's is `[FAIL]`
```

Then **read the file** at each hit for the full reason. **A pipe decides what to keep before you know
what failed**, so when the run goes red the detail is already gone and the only way back is *running the
whole suite again* — which is how a red suite routinely costs two runs instead of one. Redirected, the
run is on disk the instant it ends and can be reread from any later step for free. Grep alone is not
enough either: a failed compile embeds the compiler's entire stderr, so the marker line is a headline,
not the evidence.

⚠ **A BOOTSTRAP `spec-test` RUN DELETES EVERY `*.exe`, `*.ir_exe` AND `*.mxdbg` UNDER `temp/`, RECURSIVELY**
(`TestRunner.CleanupExecutables`, `maxon-sharp/Testing/TestRunner.cs:890`, called on EVERY run — filtered or
not — against `<checkout>/temp`, `Program.cs:1374`). It is cleaning fragment binaries and it cannot tell them
from one you staged there yourself: an A/B that parks two compilers under `temp/` loses them to the next
C# suite run, silently, exit 0. MEASURED 2026-08-27 (EC6's review): three staged self-compiles gone after
one C# run in the same chain. shv2's runner deletes only its own `specs-shv2/.spec-tmp/` files but runs
every test binary with cwd `temp/`, so the directory is shared. Stage a binary you must keep somewhere
else, or copy it out before the C# suite runs.

Do not assume the console is small: **shv2's runner prints one line per test (~1,500) and only then the
summary**, with failures wherever those tests fall in declaration order — `tail` shows PASS lines while
the reason sits thousands of lines above. (The bootstrap prints only failures unless `--verbose`.)
`temp/` is gitignored. The MCP tools above need none of this — they return structured results.

Do NOT use `dotnet run` — it recompiles every time. Use the pre-built binaries directly.

Exit code 101 means a memory leak was detected.

## Code Quality

Apply these standards when writing or reviewing any code:

- **Eliminate duplicated code** — refactor shared logic into helper methods. This includes pre-existing duplication.
- **No silent unhandled cases** — `match`/`if` chains that don't cover all cases must throw on the unhandled path, not return a default value. Never use a bare `default` case in `match` — use `default throws` or `default panic("msg")`.
- **No silent `else` fallthrough** — if an `else` branch should never be reached, throw an error instead.
- **`try/otherwise` that should never fail** must use `otherwise panic("reason")`.
- **⭐ COMMENTS — CONCISE, MINIMAL, "WHY" ONLY, PRESENT TENSE.** The codebase has accumulated a lot of
  excessive commentary; these four rules are binding on every comment you write **and on every comment
  you touch**:
  - **Minimal and concise.** The default is **no comment**. Write one only where the code cannot carry
    the point by itself, and then in as few words as it takes — one line where one line does. A comment
    per line, a banner over every section, and a restated signature above a function are all noise, and
    noise is not free: it is unverified prose that rots while the code keeps working.
  - **"Why", never "how".** The code is the "how" — do not narrate it, do not restate a name, do not
    summarize the block below. Comment the thing the reader cannot recover from the code: the
    constraint, the invariant, the reason this order/bound/branch is the correct one, the cost that
    motivated an unobvious shape.
  - ⛔ **NO HISTORY. Describe the CURRENT state only.** No "used to", "previously", "changed from",
    "renamed", "now that we…", "this was a workaround for…", no dated narration of an edit and no
    reference to what a function was called before. **Git holds the history; a comment holds the
    present.** The reason a guard exists is a *why* and belongs — but state the constraint that still
    binds ("callers may hand this an unsorted list"), never the edit that introduced it ("we added this
    after the sort was removed"). *(This bans history in SOURCE COMMENTS. Docs and commit messages are
    where a measurement, an incident and a correction get recorded — that is unchanged.)*
  - **Editing a comment means REWRITING it to conform.** If you touch a line whose comment breaks any
    rule above, fix the whole comment — or delete it — rather than patching around it. Do not leave a
    conforming edit inside a non-conforming comment.
- **No skipped work** — look for comments implying something was skipped, deferred, or not fully implemented, and address them.
- **typealias names describe purpose**, not type — e.g. `BytePos` not `Offset`.
- **Typed ranges should be as specific as possible** — e.g. `int(0 to 100)` instead of `int(0 to u64.max)`. Use the narrowest range correct for the domain. Wide ranges are fine when there is no clear limit.
- **Fix all IDE-reported problems and compiler warnings.**
- **Cross-target consistency** — any change to target-specific code (e.g. x64) must have an equivalent change in all other targets (e.g. arm64) where applicable.
- **Consolidate redundant match arms** — if a `match` has multiple cases with the same result, collapse them into a single case.
- **No thin wrapper functions** — remove functions that do nothing but delegate to one other call.
- **No sentinel return values** — functions that cannot return a valid value must throw, not return `""`, `-1`, `null`, or similar.
- **Blank lines for readability** — add blank lines around control flow statements and between logical sections.
- **No magic values** — replace bare literal constants (numbers, strings) with named `static` constants that describe their meaning. When a set of related constants belongs together, group them into a `static enum` instead of scattering individual constants.

## Error codes — ONE registry, ONE parser, and neither is a compiler's

**`docs/error-codes.txt` is the single source of truth for the 4-digit error-code space.**
**FOUR** files are **GENERATED** from it and must never be hand-edited:

```
maxon-sharp/Compiler/ErrorCode.g.cs                (C# bootstrap's enum)
maxon-selfhosted/Compiler/ErrorCodeRegistry.maxon  (v1's enum)
maxon-shv2/Compiler/ErrorCodeRegistry.maxon        (shv2's enum)
docs/error-codes.json                              (the artifact TOOLS read)
```

**To add a diagnostic:** take the next free number in the right band, add an entry to
`docs/error-codes.txt` (canonical name + `doc` + a `csharp`/`selfhosted`/`shv2` line per
compiler that will emit it), run **`maxon error-codes generate`**, **write the code that
emits it**, and commit the regenerated files with your change.

- **`maxon error-codes check` FAILS THE BUILD** on a duplicate number (naming both
  claimants and their lines), a duplicate name, a generated file that has drifted, or a
  **DEAD CLAIM**. Do not grep an enum to find a free number — the enums are derived, and
  grepping one of three copies is exactly how two agents took E3099 on the same day.
- **It runs wherever a generated file is USED, not only where it is produced**: on every
  `dotnet build`, AND on `maxon build maxon-shv2` / `maxon build maxon-selfhosted`. Bolted
  only to the bootstrap's build, it let an shv2 agent hand-edit shv2's enum and take a
  green 275/0.
- **A CLAIM MUST BE LIVE.** A `csharp`/`selfhosted`/`shv2` line must name a member that
  compiler's source actually mentions; 22 rows named members nobody emitted. (The converse
  is structural — a code that is emitted cannot be missing from the registry, because
  `ErrorCode.Foo` does not compile unless the registry generated `Foo`. Keep it that way.)
- **A reserved number is a real entry** (`reserved <why>`, no compiler claims). It occupies
  the number space. A reservation written in a comment is not a reservation.
- **The stage is derived from the leading digit** (1xxx lexer … 9xxx internal) and is never
  written down, so it cannot disagree.
- **NEVER REFERENCE A CODE BY ITS NUMBER OUTSIDE THE REGISTRY.** Use the generated member
  (`ErrorCode.SemanticUnneededCast`, plus `.Format()` for the `"E3010"` spelling). A literal
  `"E3010"` in a source file is a fourth copy of the number space: renumber the code and
  every gate stays green while the code that matched it silently stops matching anything.
- **The format has exactly ONE parser** (`maxon-sharp/Compiler/ErrorCodeRegistry.cs`). It
  briefly had two, and the second one — the MCP's — reported `emittedBy: {}` for a code the
  bootstrap declares. **Do not write a second one.** Tools read `docs/error-codes.json`,
  which the parser generates and stamps with a hash of the registry's bytes.
- **`lookup_error_code` reads `docs/error-codes.json`** and re-hashes `docs/error-codes.txt`
  before trusting it, so it reports a code's one meaning plus `emittedBy` (each compiler's
  own spelling) and `notEmittedBy` — **or REFUSES.** If it says the artifact is STALE, run
  `maxon error-codes generate`. It cannot answer for the wrong compiler and it cannot answer
  from a registry nothing checked.

### ⚠ THE MCP SERVER'S BINARY IS GITIGNORED AND NOTHING REBUILDS IT

**If you edit anything under `maxon-dev-mcp/mcp/`, rebuild it — `maxon build maxon-dev-mcp/mcp`
— AND RESTART THE MCP SERVER.** A rebuild alone does not replace the running process.

You will not get away with forgetting: **every `tools/call` compares the running binary's
timestamp against its own sources and REFUSES if a source is newer**, naming the file and the
fix. (`tools/list` still answers, so the host can still tell you why.) That guard exists
because a commit once rewrote `lookup_error_code`, nothing rebuilt the binary, the tool went
on answering `not found` for all 130 codes from code that no longer existed — and this file
was edited by the same commit to say it worked. **A tool that answers confidently from stale
code is worse than one that refuses.**

## Spec Files

- Old 3-digit error codes (e.g. `E022`) in spec files must be updated to the new 4-digit codes.
- If tests that use RequiredIR fail, regenerate with `--update-required`.
- shv2's `--update-required` regenerates RequiredIR but **not** `maxoncstderr` blocks — an
  error-code renumber moves those by hand.
