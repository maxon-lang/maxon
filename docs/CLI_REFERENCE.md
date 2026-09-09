# Maxon CLI Reference

Everything the `maxon` compiler driver accepts: the commands, their options, and the project layout
they read.

`maxon --help` prints the same command and option list from the driver itself. Where this document and
that listing disagree, the listing is the compiler and this is a copy.

---

## Quick Reference

| Command | Description |
|---------|-------------|
| `maxon build <file\|directory>...` | Compile a Maxon program to an executable |
| `maxon fmt [file\|directory]` | Re-print `.maxon` sources in canonical layout, in place |
| `maxon test [directory]` | Run a project's own `test` declarations |
| `maxon spec-test [directory]` | Run the COMPILER's own spec suite |
| `maxon scale-test` | Compile a doubling ladder and report per-phase memory and CPU |
| `maxon lsp-server` | Speak the Language Server Protocol over stdio |
| `maxon monitor [--filter=…] <exe> [args...]` | Run a `--debugstream` binary and print its trace events |
| `maxon debug --dump-info <exe>` | Print the `.mxdbg` debug-info sidecar beside a binary |
| `maxon debug --symbolize <exe> <off...>` | Resolve `.text` code offsets to `file:line:col` |
| `maxon coverage <run\|report> <exe>` | Run a `--coverage` binary and report line + branch coverage |
| `maxon profile run <exe>` | Sample a running program and report where its CPU time went |
| `maxon verify-warm-rebuild <file>` | Assert the query spine is deterministic and incremental |
| `maxon verify-recheck <file\|dir>` | Assert one project survives being re-checked |

Only the **first** command word on a line is the command. A later one is an ordinary positional
argument, so `maxon fmt fmt` formats the directory `fmt/` rather than selecting `fmt` twice.

---

## Commands

### `maxon build`

Compiles Maxon source to a standalone executable.

**Usage:**
```bash
maxon build <file|directory>... [options]
```

**Arguments:**
- `<file|directory>...` — one or more paths. **Several paths are compiled as ONE program, in the order
  given**, and that order is load-bearing: sources are registered in exactly the order named. A
  directory contributes every `.maxon` file beneath it, except `build.maxon` and except the subtrees a
  `.maxonignore` excludes. A path named explicitly is compiled whatever a `.maxonignore` above it says
  — the marker governs the *walk*, never an explicit name.

Naming no path runs the **build manifest** in the current directory — see
[The build manifest](#the-build-manifest) below. A directory with no `build.maxon` prints the usage
line and exits 1.

**Options:**

| Option | Description |
|--------|-------------|
| `-o <path>`, `--output=<path>` | Output executable path. Without it the name is derived: a single-file build writes beside the source (`foo.maxon` → `foo.exe`); every other shape takes the first registered source file's base name. An override that already carries the target's executable extension is used as it is. |
| `--target=<cpu>-<os>` | Compile for this target instead of the host. See [Targets](#targets). |
| `--emit-ir` | Also write the lowered Target IR beside the executable, as `<output>.ir`. It renders the functions parsed from the program's own source. |
| `--emit-ir-runtime=<a>,<b>` | Also render these compiler-emitted or `stdlib/` functions in that IR (implies `--emit-ir`). A value naming no function is refused. |
| `--no-debug-info` | Do NOT write the `<output>.mxdbg` debug-info sidecar. It is written **by default**, and the executable is byte-identical either way — the sidecar never decides which instructions are emitted, so the binary you debug is the binary you shipped. |
| `--coverage` | Instrument for code coverage: the binary counts each statement and branch arm it executes and writes them to `<output>.mxcov` as it exits. It **changes the emitted code**, so it is a separate build from the one you ship; read it back with `maxon coverage`. It requires the debug-info sidecar (which carries what each counter counts), so `--no-debug-info` beside it is refused. |
| `--debugstream` | Emit the shared-memory debug-stream producer, plus the memory-manager events written into it. Use with `maxon monitor`. It also enables the `__DebugStream` builtin: without it, every `__DebugStream` call emits zero instructions. Refused on a target with no shared-memory or uptime-clock facility. |
| `--async-trace` | Emit the green-thread trace (`spawn #`, `io_yield #`, `io_resume #`, `await #`) to stderr. |
| `--metrics=<path>` | Write this compile's per-phase time and memory attribution to `<path>` as TSV. `--log=compiler:debug` prints the same numbers as a human-readable table. |

`--debugstream`, `--async-trace` and `--coverage` are opt-in **per build**: with the flag off, not one
instruction of the machinery is emitted — not a branch that is never taken, nothing at all.

**Tree lock.** A build that does *not* pass `-o` and whose project root is a **directory** takes a lock
on the checkout it writes into, so two such builds cannot race on `<project>/.maxon/`. Everything else
takes nothing: a `-o` build writes where the caller said, a single-file build writes beside the file it
was handed, and a path with no `stdlib/` anywhere above it is not in a checkout to lock.

**Examples:**
```bash
maxon build hello.maxon                       # → hello.exe
maxon build src/ -o build/app                 # a whole directory, named output
maxon build maxon-bin                         # a project
maxon build a.maxon b.maxon                   # one program out of two files, in that order
maxon build app.maxon --emit-ir
maxon build app.maxon --target=wasm32-wasi
```

On success it prints `Compiled -> <path>` and exits 0. A compile error prints its diagnostics and
exits 1.

---

### `maxon fmt`

Re-prints `.maxon` sources in canonical layout, **in place**.

**Usage:**
```bash
maxon fmt [<file|directory>]
```

**Arguments:**
- `[file|directory]` — default: the working directory. A named **file** is formatted whatever it is
  called and wherever it sits. A **directory** is walked for `.maxon` files, skipping `build.maxon`,
  skipping anything under a `.maxonignore`, and **pruning any subdirectory that holds a `.git`** (a
  nested clone or a worktree), so a run cannot rewrite files outside the tree you named. The traversal
  root itself is exempt, or `fmt` inside a checkout would format nothing.

⚠ **With no path it formats the whole current directory.** That is the documented default.

**It takes NO options.** Any `-`-leading argument is refused on stderr with exit 1 and nothing written
— including a flag the driver implements for other commands, and wherever on the line it sits. A
swallowed flag would leave `fmt` with no path and send it at the current directory. More than one path
is refused the same way.

**A file it cannot lex is left byte-identical** and counted as `unchanged`, which is the same word it
uses for a file that was already canonical. A file that is already canonical is not rewritten at all,
so a run over a clean tree leaves every mtime alone.

The run prints one `formatted: <path>` line per rewritten file and a
`fmt: N file(s) changed, M unchanged.` summary. `maxon fmt` is gated byte-for-byte by `tests/fmt/`.

**Examples:**
```bash
maxon fmt              # the current directory
maxon fmt main.maxon   # one file
maxon fmt src/         # one directory
```

---

### `maxon test`

Runs a project's unit tests — every `test` declaration in its `*.test.maxon` files.

All discovered tests are compiled into ONE binary with a generated entry point, and which of them run
is a runtime argument. That is why changing `--filter` between runs does not recompile anything, and
why every re-run the harness needs (isolating a crash, attributing a leak) costs a process rather than
a build.

The project's own `main` needs no change: the test binary is compiled with a generated entry instead,
so `main` becomes unreachable and is dropped. The same directory still builds and runs normally with
`maxon build`.

**Usage:**
```bash
maxon test [directory] [options]
```

**Arguments:**
- `[directory]` — the project to test (default: the working directory). A second positional is refused.

**Options:**

| Option | Description |
|--------|-------------|
| `-t PATTERN`, `-t=PATTERN`, `--filter=PATTERN` | Run only tests whose NAME or FILE path contains PATTERN (case-insensitive). Comma-separated patterns run a union. A bare `-t` with nothing after it is refused rather than treated as "everything". |
| `--list` | Print the tests that would run, and compile nothing |
| `--json` | Emit the report as JSON instead of text |
| `--isolate` | Run every test in its own process, rather than one process per test FILE |
| `--bail[=N]` | Stop claiming new work after N failures (default 1). The shard already running finishes, so the total may exceed N. |
| `--timeout=MS` | Kill a test process after MS milliseconds (default 5000). The deadline is per PROCESS, and one process runs one FILE's tests — so it bounds a file's whole run, not each test, unless `--isolate` is given. |
| `--no-timing` | Omit durations, making stdout byte-reproducible |
| `--color=auto\|always\|never` | Colour; `auto` colours only when stdout is a terminal — not redirected, `NO_COLOR` unset, and `TERM` not `dumb` |
| `--target=<cpu>-<os>` | Build the test binary for that target and then SPAWN it, so a target this host cannot execute reports every test as not run |
| `--log=CATEGORY:LEVEL` | Enable compiler logging (e.g. `codegen:trace`). Without it, `maxon test` silences the compiler's INFO chatter so its stdout is a report rather than a build log. |

**The run is serial**, by design, so there is no `--workers` — and one is not accepted as a no-op
either.

**Working directory.** A test binary runs in the directory `maxon test` itself was invoked from — not
the project directory, and not the staging tree the tests were compiled out of. So a relative path in a
test body means the same thing it means in the shell you typed the command in.

**Where the build lives.** `<project>/.maxon/test/`, with a `.maxonignore` written at its root before
anything is staged, so a generated test source can never be swept into the project's next ordinary
build or into the next run's own discovery.

**`--color=auto` degrades to `never` on wasm32-wasi.** Asking the OS what kind of object a handle is
needs a host call: **x64-windows** makes it with `GetFileType`, **arm64-macos** with `isatty`, and both
Linux lanes with the `ioctl(1, TCGETS, ...)` `isatty` is made of — a libc-less static image has no
`isatty` to call, and reads `NO_COLOR` and `TERM` out of the environment vector its entry stub
captured. On **wasm32-wasi** the question is answered "not a terminal", so `auto` prints no colour
there however the report is being viewed. `--color=always` is unaffected and is the way to get colour
on that lane.

The build is never refused for it: the answer degrades rather than the compile failing, because "not a
terminal" is a sound conservative answer where a missing file surface or a missing stdin would leave a
program with no answer at all. Which lanes can ask is recorded in
`TargetFacilities.targetProvidesFacility`'s `terminalDetection` row. wasm32-wasi is not merely
unwritten: a WASI component cannot ask what is on the other end of its `output-stream` at all.

**Outcomes.** A test that does not pass is reported as one of five distinct states, because they are
found by different evidence and call for different action:

| State | Meaning |
|-------|---------|
| `FAIL` | The body threw — a failed assertion, or a foreign error the compiler reported |
| `CRASHED` | The test began and never ended: it took the process down (`panic` is uncatchable) |
| `TIMED OUT` | Still running when its process hit `--timeout`, and was killed |
| `DID NOT RUN` | Selected for a process that died before reaching it, and re-running made no progress. Never reported as a pass. |
| `LEAKED` | Held an allocation at exit (exit code 101), attributed by re-running the test alone |

`--json` reports the same outcomes under its own `state` vocabulary, which a machine reader branches
on. The two spellings are one map in the renderer, so they cannot describe different states:

| `state` | Text label |
|---------|------------|
| `passed` | (a `✓` line) |
| `failed` | `FAIL` |
| `crashed` | `CRASHED` |
| `timedOut` | `TIMED OUT` |
| `didNotRun` | `DID NOT RUN` |
| `leaked` | `LEAKED` |

**Exit codes:**

| Code | Meaning |
|------|---------|
| `0` | Every test passed |
| `1` | A test failed, crashed, timed out, leaked, or did not run — **or no tests were found** |
| `2` | The run could not happen: a bad flag, a compile error, no such project |

A zero-test run exits 1 on purpose: a silently-green suite that stopped containing tests is the failure
this command exists to prevent.

**A worked example — write a test, run it, read the failure.**

Two files in a directory. `pricing.maxon` is ordinary source; the tests go in a sibling whose name ends
`.test.maxon`, which is the only place a `test` declaration is allowed:

```maxon
// pricing/pricing.maxon
export typealias Cents = int(0 to i64.max)

/// What `quantity` items cost at `unitPrice`, with a tenth off from 10 items up.
export function totalCost(unitPrice Cents, quantity Cents) returns Cents
	let gross = unitPrice * quantity
	if quantity < 10 'noDiscount'
		return gross
	end 'noDiscount'

	return gross - gross / 10
end 'totalCost'
```

```maxon
// pricing/pricing.test.maxon
test 'a small order pays full price'
	try Expect.equal(totalCost(250, quantity: 4), expected: 1000)
end 'a small order pays full price'

test 'ten items take the bulk discount'
	try Expect.equal(totalCost(250, quantity: 10), expected: 2500)
end 'ten items take the bulk discount'
```

The `try` is not optional: a `test` implicitly declares `throws TestFailure`, so an assertion without
it is a compile error rather than an assertion whose failure nothing observes. There is no `main` here
and none is needed — `maxon test` compiles a generated entry point instead.

The second expectation is wrong: it forgot the discount. Run it:

```text
$ maxon test pricing
pricing/pricing.test.maxon:
  ✓ a small order pays full price                    0.00ms
  ✗ ten items take the bulk discount                 0.00ms

FAIL  pricing/pricing.test.maxon > ten items take the bulk discount
  FAIL pricing.test.maxon:6: Expect.equal
    expected: 2500
    received: 2250

 1 pass
 1 fail

 2 tests across 1 file.   compile 947ms, run 38ms
```

The `file:line` on the `FAIL` line is the **assertion's own**, not a line inside `Testing.maxon` —
`Expect`'s `file` and `line` parameters default to `__file__` / `__line__`, which expand at the call
site. Correct the expectation to `2250` and the same command exits 0:

```text
$ maxon test pricing
pricing/pricing.test.maxon:
  ✓ a small order pays full price                    0.00ms
  ✓ ten items take the bulk discount                 0.00ms

 2 pass
 0 fail

 2 tests across 1 file.   compile 966ms, run 138ms
```

The second run still says `compile` rather than `cached` because the source changed. Editing only the
`--filter` between runs recompiles nothing: every discovered test is built into the binary and which
ones run is an argument.

**Examples:**
```bash
maxon test                        # every test in the current directory
maxon test src/parser             # one project's tests
maxon test -t json                # only tests whose name or file mentions "json"
maxon test --filter=parser,lexer  # two patterns, as a union
maxon test --list                 # what would run, without compiling
maxon test --json --no-timing     # machine-readable, and reproducible byte for byte
```

---

### `maxon spec-test`

Runs the spec tests — the COMPILER's own suite, under `specs/`. For a project's unit tests see
[`maxon test`](#maxon-test).

**Usage:**
```bash
maxon spec-test [directory] [options]
```

**Arguments:**
- `[directory]` — the spec directory (default: `specs`, resolved against the working directory). A
  second positional is refused.

**Options:**

| Option | Description |
|--------|-------------|
| `--filter=PATTERN` | Run only tests whose `<spec>/<test>` label **contains** PATTERN. One substring, matched case-sensitively — not a list. A bare spec name selects that whole spec, a bare test name selects that test wherever it lives, and `spec/test` selects exactly one. |
| `--workers=N` | Run the suite on N persistent worker subprocesses (default: this machine's CPU count). `1` is the same pool with one worker in it, NOT a serial path — every worker count must print byte-identical output, and that invariance is the pool's sharpest gate. |
| `--target=<cpu>-<os>` | Cross-compile each selected test for that target and run it under the vendored runtime |
| `--network` | Also run the cases that open a socket to a REAL EXTERNAL HOST. They are out of a default run because their verdict measures somebody else's uptime rather than this compiler; a default run NAMES every one it left out. |
| `--update-required` | Rewrite the committed `.test` goldens instead of checking the emitted IR against them. **Review the resulting diff**, and pair it with `--filter`: an unfiltered run rewrites EVERY golden in the suite. |

Running a test proves its allocation is *correct*; only the pinned IR proves the code did not get
*worse*, which is why regenerating a golden is a deliberate act and the diff is the review.

The run refuses to start — exit 2, nothing measured — if the compiler binary is older than the sources
it was built from, or if another command already holds this checkout's tree lock. A stale binary
reports a verdict about code that is not here.

**Examples:**
```bash
maxon spec-test
maxon spec-test --filter=arrays
maxon spec-test --filter=arrays/a-pushed-element-survives-the-push
maxon spec-test --workers=1
maxon spec-test --target=wasm32-wasi
maxon spec-test --filter=strings --update-required
```

---

### `maxon scale-test`

Compiles a ladder of generated programs, each rung double the last, and reports the **per-phase
memory** — allocations, frees and bytes — plus the CPU **ticks** the compiling thread spent.

It does NOT collect wall time: wall time counts every other process on the box, so a dated table of it
would compare a loaded machine in July against an idle one in August. The ladder doubles, so the ratio
between rungs IS the growth (×2.00 linear, ×4.00 quadratic) and nothing is fitted.

⚠ **An INSTRUMENT, not a gate.** It renders no verdict and exits 0 whatever the numbers say; a non-zero
exit means the RUN broke. The trend it feeds is `docs/optimization-log.md`.

**Usage:**
```bash
maxon scale-test [options]
```

**Options:**

| Option | Description |
|--------|-------------|
| `--rungs=N` | Climb N rungs (default 6, range 1–8). Each rung DOUBLES the program, so this is exponential in cost. |
| `--repeat=N` | Compile every rung N times (default 1, range 1–25) and report the LEAST CPU ticks any of them spent in each phase — the least-disturbed sample, since noise can only ADD to a CPU reading and a mean would be dragged by one stall. The MEMORY columns take nothing from a repeat: they are exact, so they come from the first compile and are cross-checked for equality against the rest. A memory number that differs between two compiles of one rung is a broken run. |
| `--note=TEXT` | Record this run in `docs/optimization-log.md` — one dated row of the per-rung allocations and bytes, with TEXT as the reason. Without it a run reports and records nothing; **with** it the row is written, moved or not, because it is an instruction. The instrument can see exactly WHAT moved and can never see why. |
| `--emit-corpus=DIR` | Dump the generated corpus to DIR for inspection (it is otherwise built into a temp directory and thrown away) |
| `--result-json=PATH` | Write the whole run — per-rung and per-phase memory, and the deltas from the last recorded run — to PATH as JSON, so a tool never has to scrape the human report |

A bad value for `--rungs`, `--repeat` or `--workers` is refused as a bad option, never clamped to the
default: quietly running a different ladder would answer a question the caller never asked.

---

### `maxon lsp-server`

Speaks the Language Server Protocol over stdio: JSON-RPC bodies under `Content-Length` framing, the
`initialize` / `shutdown` / `exit` lifecycle, and full text-document synchronization. Normally launched
by the VS Code extension rather than by hand.

---

### `maxon monitor`

Launches an executable with the shared-memory debug-stream monitor attached. Reads the binary trace
events a `--debugstream` build writes and prints them to the terminal, each prefixed with
`[+SSSS.mmm]`. The child's own stdout and stderr are forwarded unchanged, so trace lines are told apart
by that prefix.

**Usage:**
```bash
maxon monitor [--filter=mm|sched|log] <exe> [args...]
```

Everything after the executable is the **child's** command line and reaches it verbatim — the driver
stops parsing at the word `monitor`, so a target argument spelled like one of these flags is not
consumed on the way past.

**Options:**

| Option | Description |
|--------|-------------|
| `--filter=mm` | Memory-manager events only (`mm_alloc` / `mm_free` / `mm_incref` / …) |
| `--filter=sched` | Scheduler and green-thread events only |
| `--filter=log` | Only the events the program itself emitted via the `__DebugStream` builtin |

Given no `--filter`, every family is printed. An unrecognized value is refused by name.

**Exit codes:** the child's own status, except 1 for a command line this could not act on (no
executable, no such file, an unknown filter, no ring) and 3 for a trace schema this build cannot decode.

**Examples:**
```bash
maxon build app.maxon --debugstream
maxon monitor app.exe
maxon monitor --filter=log app.exe
```

**The `__DebugStream` builtin.** Every event family above except `log` is emitted by the runtime.
`__DebugStream` is what lets *user Maxon source* put an event into the same ring — which is how a
compiler written in Maxon stays debuggable once its work is spread over several workers and one stderr
stops being readable.

| Call | Event | Notes |
|------|-------|-------|
| `__DebugStream.enabled()` | — | `true` when the ring is attached. Lets a caller skip building a message nothing would read. |
| `__DebugStream.nameId("phase")` | — | Interns a name **at compile time** into the executable's `MXDS_STRS` blob and yields its `u16`. The name never exists at runtime; the monitor prints it anyway. **The argument must be a string literal.** |
| `__DebugStream.phaseBegin(nameId, unitId)` | `LOG_PHASE_BEGIN` | Opens a nested, per-worker, per-unit span. |
| `__DebugStream.phaseEnd(nameId, unitId)` | `LOG_PHASE_END` | Closes it. |
| `__DebugStream.event(nameId, cat, lvl, unitId, arg0, arg1)` | `LOG_EVENT` | **Structured, zero-alloc.** An interned name plus two numbers — safe on a hot path, where a formatted message would allocate into the very `mm` stream a trace is being read to investigate. |
| `__DebugStream.text(cat, lvl, unitId, message)` | `LOG_TEXT` | A UTF-8 message, for the rare human line. Allocating (the caller built the string); truncated, never torn, at 64 KiB. |

Every Log event also carries the emitting green thread and its owning processor, so a run with several
workers in flight can be demuxed back into one timeline per worker.

Two gates keep this free enough to leave in place:

- **Compile time** — without `--debugstream`, every call above emits **zero instructions**. Not a
  branch that is never taken: nothing at all.
- **Runtime** — with the ring detached, each call is a load of `__ds_base`, a test, and a not-taken
  branch. The bail is inline, before any `call`.

---

### `maxon debug`

Reads the `.mxdbg` debug-info sidecar back. Both faces are read-only, and both accept either the
executable or the sidecar itself.

**Usage:**
```bash
maxon debug --dump-info <exe|.mxdbg> [header|files|functions|types|lines|statements]
maxon debug --symbolize <exe|.mxdbg> <codeOffset...>
```

`--dump-info` with no section names prints the whole sidecar. A name that is not a section is refused
by name, before the file is even read, so a misspelling is answered as a misspelling rather than by
whichever failure the path happens to produce first.

`--symbolize` resolves `.text` code offsets to `file:line:col`.

Like `monitor`, the parse stops at the word `debug` — its own subcommands are `-`-leading and every
other arm of the driver would otherwise read them as unknown options and abort.

---

### `maxon coverage`

Reads back what a `--coverage` binary counted, as line and branch coverage.

**Usage:**
```bash
maxon coverage <run|report> <exe> [--json] [--data=<file>] [--timeout=<seconds>] [args...]
```

- **`run`** launches the instrumented program (deleting the previous data file first, so a run whose
  dump never happened cannot be reported from stale numbers) and then reports on the counters it wrote.
  The program's own output goes to **stderr**, so stdout is only the report. Any trailing `args...` are
  the program's.
- **`report`** reads counters already on disk and launches nothing.

| Option | Description |
|--------|-------------|
| `--json` | Emit the report as JSON |
| `--data=<file>` | Where to READ counters from. Belongs to `report`; refused on `run`, which reports on the file the program itself just wrote. |
| `--timeout=<seconds>` | Bound the run (default 600). Belongs to `run`; refused on `report`, which launches nothing to wait for. |

Target arguments are refused on `report` for the same reason: it launches no program to give them to.
Everything after the verb is filed verbatim, so a target's own flag is neither consumed on the way past
nor able to abort the run before the child is spawned.

---

### `maxon profile`

Launches a program and samples where its CPU time goes. It needs no instrumentation and no rebuild —
only the `.mxdbg` sidecar is read, so the program measured is the one that ships. The program's own
output goes to stderr, so stdout is only the report.

⚠ **x64-windows only.** Sampling suspends the target's threads and reads an x64 `CONTEXT` record.
Elsewhere the command is refused by name rather than being absent.

**Usage:**
```bash
maxon profile run <exe> [--json|--folded] [--rate=<hz>] [--min-percent=<share>] [--timeout=<seconds>] [--target-env=<NAME>=<VALUE>] [args...]
```

| Option | Description |
|--------|-------------|
| `--json` | Emit the full report as JSON instead of a summary |
| `--folded` | Emit collapsed stacks (`root;child;leaf <count>`) — the format flamegraph.pl, inferno and speedscope all read. `--json` and `--folded` are two different machine formats; asking for both is refused. |
| `--rate=N` | Samples per second per running thread (default 1000, max 10000). A rate is **refused rather than clamped**; the report also states the rate it ACHIEVED beside the one requested, since the real ceiling is the OS timer's granularity. |
| `--min-percent=N` | Hide rows below this share of the run from the printed tables (default 1). How many were hidden is always reported. |
| `--timeout=S` | Stop the program and report a PARTIAL profile after S seconds (default 600). Unlike coverage's deadline, reaching it is not fatal to the measurement — the samples taken so far are still a profile. |
| `--target-env=N=V` | Set a variable in the PROFILED program's environment (repeatable). `MAXON_MAX_PROCS=N` pins the scheduler's worker count, which is what makes a green-thread profile reproducible rather than machine-dependent. |

This tool's own exit code says whether the MEASUREMENT completed; the program's status is reported as
`target exit code`.

---

### `maxon verify-warm-rebuild` and `maxon verify-recheck`

Two gates on the incremental machinery. Each takes exactly one path and exits 0 only if its property
holds; the failures print their own reasons.

- **`maxon verify-warm-rebuild <file>`** asserts the query spine is deterministic **and** incremental,
  without ever running the pipeline.
- **`maxon verify-recheck <file|dir>`** asserts one project can be re-checked: two checks over
  unchanged input agree, and a diagnostic introduced by an edit CLEARS when the edit is undone. That is
  what an editor holding one project open for hours actually does.

---

## Targets

`--target=<cpu>-<os>` is accepted by `build`, `spec-test` and `test`. Without it, the host target is
used.

| Spelling | Output |
|----------|--------|
| `x64-windows` | PE executable |
| `x64-linux` | ELF executable |
| `arm64-linux` | ELF executable |
| `arm64-macos` | Mach-O executable |
| `wasm32-wasi` | WASI Preview 2 component |

A spelling that names none of them is refused (`error: unknown --target '<spec>'`) and the command
exits 1. `maxon --help` prints the authoritative list; this table is a copy of it.

Not every target provides every host facility. `--debugstream` and `--coverage` are refused by name on
a target that cannot serve them, before anything is compiled, rather than compiling to an instrument
that silently reports nothing.

---

## Logging

Every command accepts logging options.

| Option | Description |
|--------|-------------|
| `--log=LEVEL` | Set all log categories to the given level |
| `--log=CATEGORY:LEVEL` | Set one category to the given level |

**Levels:** `none`, `error`, `info`, `debug`, `trace`

**Categories:** `compiler`, `lexer`, `parser`, `semantic`, `ir`, `codegen`, `binary`, `testing`

An unrecognized spec is reported on stderr and otherwise ignored.

```bash
maxon build app.maxon --log=codegen:trace
maxon build app.maxon --log=compiler:debug   # per-phase timing + memory as a table
maxon spec-test --log=ir:debug
```

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success |
| `1` | The command ran and failed — a compile error, a failing gate, an unusable command line |
| `2` | **Nothing ran**: the tree is not in a state to be measured (a compiler binary older than its sources, a checkout another command is already writing), or a `maxon test` run that could not happen |
| `101` | A leak-gated program still held an allocation at exit. Reported by the program, not by the driver; `maxon test` turns it into a `LEAKED` outcome. |

`2` is deliberately distinct from `1`: "nothing ran" and "something failed" are different facts, and a
caller reading only the exit code has to be able to tell them apart.

---

## Project Structure

A Maxon project is a directory of `.maxon` files. There is no manifest to write — the directory *is*
the project.

```
myproject/
├── main.maxon          # entry point (contains main)
├── utils.maxon
├── lib/
│   ├── math.maxon
│   └── io.maxon
└── pricing.test.maxon  # tests: not part of an ordinary build
```

All `.maxon` files in subdirectories are included when compiling a directory, with three exclusions:

1. **`build.maxon`** — the build manifest is a program in its own right, not part of the one being
   built, so it is never swept in. Naming it explicitly still compiles it, and `maxon build` with no
   path runs it — see [The build manifest](#the-build-manifest).
2. **`*.test.maxon`** — a `test` declaration's file is a source *category*, not a flag inside the file,
   so test sources are not part of an ordinary build. `maxon test` names them explicitly. The match is
   case-insensitive, so `Suite.TEST.maxon` is a test file too.
3. **Anything under a `.maxonignore`** — see below.

### The build manifest

`maxon build` with no path looks for **`build.maxon`** in the current directory, compiles it, runs it,
and performs the build it describes.

⭐ **A manifest is a PROGRAM, not a configuration file.** It is ordinary Maxon with the whole standard
library available, so a build can *compute* what it compiles — read a directory, choose by host,
stamp a version — rather than only spell it out. The compiler does not parse the manifest; it runs it
and reads what it prints.

**Its entry point is `build`, not `main`.** A manifest holds tasks, and `build` is the one this
command asks for by name. It returns `ExitCode`; a non-zero return, or a crash, fails the build and
nothing is compiled.

```maxon
export function build() returns ExitCode
	Build.buildOne("maxon-bin", output: "maxon-bin/.maxon/maxon")
	return 0
end 'build'
```

**What it prints is the contract.** `stdlib/Build.maxon` writes the build description as JSON on
stdout, and the compiler reads it back — so both ends share one description of what a build is:

| Call | Meaning |
|---|---|
| `Build.buildOne(source, output:)` | Compile one file or directory to one output. The common shape. |
| `Build.buildWithConfig(config)` | Full control via a `BuildConfig`: several sources, in order. |
| `Build.build(name)` | The executable name only; leaves `sources` empty, which is **refused** — see below. |

The output path omits the extension: the compiler appends `.exe` on Windows and nothing elsewhere.

⛔ **An empty `sources` is refused rather than read as "this directory".** Accepting it would compile
every file beneath the manifest — every spec, every test — under a command that named nothing at all.

⚠ **`-o` and `--target` on the command line outrank the manifest**, for the reason every flag outranks
a file: the person typing the command is answering a narrower question than the file was. The manifest
itself is always built for the host, whatever `--target` says, because it is a program this machine has
to run in a moment.

#### Rebuilding the compiler that is running

The manifest at the root of the Maxon repository builds the compiler into the slot the running
compiler occupies, so `maxon build` there replaces the binary executing the command.

That works. No operating system permits *deleting* a running executable, but they permit *renaming*
one, so the compiler moves its running image to `maxon.previous.exe` first and the slot is then empty
for the ordinary write path.

⛔ A **failed** build therefore leaves the slot **empty** and the last good compiler at
`maxon.previous.exe`. That is deliberate: the alternative — compiling to a temporary name and swapping
it in afterwards — leaves the old binary in place when the build fails, and a stale compiler answering
as though it were current is the failure every staleness check here exists to prevent. If a build
fails, move `maxon.previous.exe` back deliberately.

### Ignoring Directories

Place a `.maxonignore` file in any directory to exclude it, and every directory beneath it, from
compilation, formatting and LSP processing. The file is a flag — its contents are ignored, and a marker
anywhere *above* a directory excludes it too.

```
myproject/
├── main.maxon
├── fixtures/
│   ├── .maxonignore     # this directory is skipped by the walk
│   └── sample.maxon
└── src/
    └── app.maxon
```

The marker says *"do not sweep me into somebody else's program"*, never *"this file may not be
compiled"*. **Naming a path outright overrides it**: `maxon build fixtures/sample.maxon` compiles that
file, and `maxon fmt` formats a file you name inside a marked directory.

---

## Namespace Resolution

Namespaces are derived from file paths, relative to the project root (the directory you named, or the
file's own directory for a single-file build):

| File Path | Namespace |
|-----------|-----------|
| `main.maxon` | (global) |
| `utils/helpers.maxon` | `utils` |
| `lib/math/vectors.maxon` | `lib.math` |

### Calling Functions Across Files

**Full qualification:**
```maxon
var result = utils.format(value)
```

**Suffix matching (if unambiguous):**
```maxon
var result = format(value)  // finds utils.format if unique
```

### Export Visibility

Functions must be exported to be visible from other files:

```maxon
// utils.maxon
export function helper(x int) returns int
	return x * 2
end 'helper'

function internal(x int) returns int  // not visible from other files
	return x + 1
end 'internal'
```

---

## Standard Library

The standard library is loaded for every compilation. The compiler finds it by walking **up from its
own executable's directory** looking for a `stdlib/` — not up from the working directory, so a program
compiled in a throwaway temp directory still gets the right library. Failing to find one is a hard
error, not a silent skip.

⚠ That walk is why a bootstrap compiler is kept **alone** in `.bootstrap/`: a release archive unpacked
whole would leave a released `stdlib/` one directory above the compiler, and the tree's own would never
be reached.

See [STDLIB_REFERENCE.md](STDLIB_REFERENCE.md) for what the library contains.

---

## Common Workflows

### A single file

```bash
maxon build program.maxon
./program.exe
```

### A project

```bash
maxon build myproject
maxon test myproject
maxon fmt myproject
```

### Working on the compiler

```bash
./maxon-bin/.maxon/maxon build                  # rebuild it with itself
maxon-bin/.maxon/maxon spec-test                # the whole suite
maxon-bin/.maxon/maxon spec-test --filter=arrays
scripts/fixpoint.sh                             # does it reproduce itself byte for byte?
```

### Investigating emitted code

```bash
maxon build problem.maxon --emit-ir                                # writes problem.ir
maxon build problem.maxon --emit-ir-runtime=__managed_get          # plus a runtime body
maxon build problem.maxon --metrics=temp/phases.tsv                # where the compile went
```

### Investigating a running program

```bash
maxon build app.maxon --debugstream && maxon monitor --filter=mm app.exe
maxon build app.maxon --coverage    && maxon coverage run app.exe
maxon profile run app.exe --folded > app.folded
maxon debug --dump-info app.exe functions lines
```
