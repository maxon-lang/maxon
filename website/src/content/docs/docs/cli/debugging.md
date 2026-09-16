---
title: Debugging & Profiling
description: Debug info, panics and backtraces, the leak check, and the debug, monitor, profile and coverage commands.
sidebar:
  order: 3
---

Maxon has **no interactive debugger**: there are no breakpoints, no stepping and no attaching to a
running process. The tools on this page are what exists: debug information beside every binary, panic
backtraces, a leak check, a trace monitor, a sampling profiler and code coverage.

The examples below use output from real runs; addresses, counts and timings vary from build to build.

## The `.mxdbg` debug-info sidecar

Every `maxon build` writes a sidecar named after the full output file, beside it, unless you pass
`--no-debug-info` or the manifest sets `debugInfo: false`:

```text
[CMP] INFO: Wrote 35598 bytes of debug info to app.exe.mxdbg
[CMP] INFO: Wrote 103992 bytes of code to app.exe (compiled in 0.3 s)
Compiled -> app.exe
```

On Linux and macOS the executable is `app` and the sidecar `app.mxdbg`. No sidecar is written for
`wasm32-wasi`. The executable is **byte-identical** with or without the sidecar, so the binary you debug is
the binary you ship.

The sidecar maps machine code back to the source. It records the target and a **build id** (a hash of
the executable's code section), then the source files, functions with their frame size and local
variables, types, the line table and inlining records. `maxon debug` prints it; `maxon profile` and
`maxon coverage` read it, and a `--coverage` build requires it.

## Panics and backtraces

A runtime panic (a failed range check, an explicit `panic`, an out-of-bounds access) prints the message
with its source position, then the call chain, to stderr, and the program exits with code **1**:

```text
panic at app.maxon:21: Range check failed: value outside typealias 'Byte'
Stack trace:
  in narrow
  in main
  in mrt_start
```

Frames are listed innermost first, by function name only, down to the program's start (`mrt_start`).
At most 100 frames are printed; a deeper chain
ends with an `...additional frames elided...` line. A processor fault has no source position, for example
`panic: integer divide by zero`, followed by the same stack trace.

## The leak check

A program that allocates checks, after `main` returns, that every heap allocation was released. If one
was not, the program prints nothing and exits with code **101** instead of the code `main` returned. Maxon
releases memory itself, so there is no call you forgot to make: exit 101 points at the memory management
the compiler emitted. `maxon test` reports such a test as `LEAKED`, and
[`maxon monitor --filter=mm`](#maxon-monitor) shows every allocation and free.

## `maxon debug`

Reads the `.mxdbg` sidecar back. Both forms are read-only and accept either the executable or the sidecar
itself.

```bash
maxon debug --dump-info <exe|.mxdbg> [header|files|functions|types|lines|statements|inline]
maxon debug --symbolize <exe|.mxdbg> <codeOffset...>
```

| Option | Description |
|--------|-------------|
| `--dump-info` | Print the sidecar, or only the sections named after the path |
| `--symbolize` | Resolve offsets into the executable's code section to `file:line:col` |

`maxon debug` with no arguments prints the usage above and exits 1.

**Sections** of `--dump-info` (with none named, all are printed):

| Section | Contents |
|---------|----------|
| `header` | The file, target and build id |
| `files` | The source files, including the standard-library and runtime files the program uses |
| `functions` | Each function's code range, frame size, parameter, line and local counts, and each local's location (a frame slot, a register or `<optimized out>`) and type |
| `types` | Each type's kind, size, alignment and fields |
| `lines` | The line table: code offset and source position |
| `statements` | The same table, source positions only |
| `inline` | Inlined call sites and the code ranges they occupy |

```text
$ maxon debug --dump-info app.exe header
Debug info: app.exe
  target:   x64-windows
  build-id: 0xc9c1ee8ee7dd71e0

$ maxon debug --dump-info app.exe functions
  functions (136):
    mrt_start                        [0x0000, 0x003b)  frame=0x20  params=0  lines=0  locals=0
    worker                           [0x0060, 0x00de)  frame=0x28  params=1  lines=4  locals=1
        base                 reg3  : Integer
    main                             [0x00e0, 0x053d)  frame=0x88  params=0  lines=50  locals=3
        points               [rbp-0x60]  : <generic instance>

$ maxon debug --dump-info app.exe lines
  line table (390):
    0x00c9  app.maxon:16:8  [statement]
    0x0151  app.maxon:25:15  [statement]
```

A word that is not a section is refused before the file is read:

```text
maxon debug --dump-info: 'bogus' is not a section (header|files|functions|types|lines|statements|inline).
```

**`--symbolize`** takes offsets in decimal or `0x`-prefixed hex and prints one line each. An offset
before the function's first statement prints `<no line>`. An unreadable offset is reported and the
command exits 1 after printing the others.

```text
$ maxon debug --symbolize app.exe 0x00e0 0x0151 zz
0x00e0  <no line>  (in main)
0x0151  app.maxon:25:15  (in main)
Not a code offset: 'zz' (use decimal or 0x-prefixed hex).
```

**The build id.** Given an executable, `maxon debug` checks that the sidecar beside it was written by the
same build, and refuses a stale one with exit 1:

```text
maxon debug: the .mxdbg sidecar describes a different build of this binary — rebuild it
```

Given the `.mxdbg` path directly, it prints the sidecar without that check. A missing sidecar is refused
with exit 1, naming the path it looked for and `--no-debug-info` as the likely cause. `maxon debug` reads
PE, ELF and Mach-O executables whatever the host, so it works on a cross-compiled binary.

## `maxon monitor`

Launches an executable built with `--debugstream` and prints the trace events it writes into a
shared-memory ring. Each event line is prefixed with the time since start as `[+SSSS.mmm]`. The monitor
creates the ring and passes its name to the program in the `MAXON_DEBUGSTREAM` environment variable.
The program's own stdout and stderr pass through unchanged.

```bash
maxon monitor [--filter=mm|sched|log] <exe> [args...]
```

Everything after the executable is the program's command line and reaches it untouched.

| Option | Description |
|--------|-------------|
| `--filter=mm` | Memory-manager events only: `mm_alloc`, `mm_free`, `mm_incref`, `mm_decref` and related |
| `--filter=sched` | Scheduler events only. The current runtime emits none, so this filter prints no event lines. |
| `--filter=log` | Only the events the program emitted through the `__DebugStream` builtin |

With no `--filter`, every family is printed. An unrecognized value is refused with the usage line.

```text
$ maxon build app.maxon --debugstream
$ maxon monitor --filter=mm app.exe
[+0000.015] mm_alloc ArrayRecord #1 size=48
[+0000.015] mm_alloc Point #2 size=16
[+0000.015] mm_alloc ElementBuffer #3 size=32
...
points=1000 worker=42
[debugstream] 3063 events, 0 dropped, peak buffer: 0.0 MB / 2.0 MB (2%)
```

Events go to stdout; the closing `[debugstream]` summary goes to stderr.

A binary built without `--debugstream` is refused before it starts:

```text
maxon monitor: could not read the interned name tables out of the target (app.exe has no .symtab section)
```

**Exit codes:** the program's own exit code (a panicking program's `1` included), except `1` for a
command line `monitor` cannot act on (no executable, no such file, an unknown filter, a binary built
without `--debugstream`) and `3` for a trace this compiler cannot decode, which asks you to rebuild the
program.

**Emitting your own events.** A `--debugstream` build enables the `__DebugStream` builtin, which puts
events from your code into the same ring. Without `--debugstream`, every call compiles to nothing; with no
monitor attached, `__DebugStream.enabled()` is `false`.

| Call | Event | Notes |
|------|-------|-------|
| `__DebugStream.enabled()` | — | `true` when a monitor is attached, so a caller can skip building a message nobody reads |
| `__DebugStream.nameId("phase")` | — | Interns a name at compile time and returns its id. The argument must be a string literal. |
| `__DebugStream.phaseBegin(nameId, unitId)` | `log_phase_begin` | Opens a nested span |
| `__DebugStream.phaseEnd(nameId, unitId)` | `log_phase_end` | Closes it |
| `__DebugStream.event(nameId, cat, lvl, unitId, arg0, arg1)` | `log_event` | A name and two numbers; allocates nothing, so it is safe on a hot path |
| `__DebugStream.text(cat, lvl, unitId, message)` | `log_text` | A UTF-8 message, truncated at 64 KiB |

Each event line names the green thread and processor that emitted it (`gt=0x0 P0`).

## `maxon profile`

Launches a program and samples where its CPU time goes. The program needs no instrumentation and no
rebuild: only its `.mxdbg` sidecar is read, so you measure the binary you ship. The program's own output
goes to stderr, so stdout is only the report.

**x64-windows only.** Sampling suspends the program's threads and reads their x64 register state. A
compiler for any other host refuses the command.

```bash
maxon profile run <exe> [--json|--folded] [--rate=<hz>] [--min-percent=<share>] [--timeout=<seconds>] [--target-env=<NAME>=<VALUE>]... [args...]
```

| Option | Description |
|--------|-------------|
| `--json` | Emit the full report as JSON instead of a summary |
| `--folded` | Emit collapsed stacks (`root;child;leaf <count>`), the format flamegraph.pl, inferno and speedscope read. `--json` and `--folded` together are refused. |
| `--rate=N` | Samples per CPU-second a thread consumes (default 1000, from 1 to 10000). A thread is charged by the CPU time it used, so an idle thread costs almost nothing. The report shows the rate achieved beside the one requested. |
| `--min-percent=N` | Hide rows below this share of the run from the printed tables (default 1). The number hidden is always reported. |
| `--timeout=S` | Stop the program after `S` seconds (default 600) and report what was sampled, marked `PARTIAL` |
| `--target-env=N=V` | Set a variable in the profiled program's environment (repeatable). `--target-env=MAXON_MAX_PROCS=1` pins the scheduler's processor count, which makes a green-thread profile reproducible. |

```text
$ maxon profile run busy.exe
profile: busy.exe
target exit code: 0
samples: 2204 from 2060 captures at 1000 Hz over 2.3066124s (requested 1000 Hz)
unsymbolized: 2 samples (0.1%)
stacks: 0 truncated, 1 unreadable

=== hot functions (self time) ===
 99.5%     2193  mix
  (4 functions below 1% not shown)

=== call tree ===
 99.9%     2202  mrt_start
 99.9%     2202    main
 99.9%     2202      spin
 99.5%     2193        mix
  (1 nodes below 1% not shown)

=== stacks ===
  one row per stack that ran — an OS thread's or a green thread's — named by its outermost frame
 99.9%     2202  mrt_start
  (1 stacks below 1% not shown)
```

```text
$ maxon profile run busy.exe --folded
[ntdll.dll] 1
mrt_start;main;spin 10
mrt_start;main;spin;mix 2194
```

`--json` prints one object with `exe`, `targetExitCode`, `runCompleted`, `achievedRateHz`,
`requestedRateHz`, `samples`, `captures` and related counts, and the arrays `functions`
(`name`, `selfSamples`, `totalSamples`), `tree` and `stacks`.

**Exit codes:** `0` when the measurement completed. `1` for a refusal (no such executable, no sidecar, a
sidecar from a different build, a program that ended before sampling began) and for a run stopped by
`--timeout`, whose partial report is still printed. The program's own status is the `target exit code`
line.

## `maxon coverage`

Reads back what a `--coverage` build counted, as line and branch coverage. The instrumented program
writes its counters to `<exe>.mxcov` as it exits.

```bash
maxon coverage <run|report> <exe> [--json] [--data=<file>] [--timeout=<seconds>] [args...]
```

- **`run`** deletes the previous counter file, launches the program, then reports on the counters it
  wrote. The program's output goes to stderr, so stdout is only the report. Arguments after the
  executable are the program's.
- **`report`** reports from counters an earlier run wrote, and launches nothing.

| Option | Description |
|--------|-------------|
| `--json` | Emit the report as JSON instead of an annotated source listing |
| `--data=F` | Read counters from `F` instead of `<exe>.mxcov`. For `report` only; `run` refuses it. |
| `--timeout=S` | Kill the program after `S` seconds (default 600) and report nothing. For `run` only. |

```text
$ maxon build cov.maxon --coverage
$ maxon coverage run cov.exe
coverage: cov.exe
target exit code: 0
lines: 5/6 covered  branches: 1/2 arms taken  eliminated: 0 lines
legend: a count = executed, ##### = never executed, ----- = no code emitted (optimized away), blank = no coverage point

=== cov.maxon ===
        |   3| function classify(n Count) returns String
      3 |   4| 	if n > 5 'big'
  ##### |   5| 		return "big"
        |   6| 	end 'big'
        |   7| 
      3 |   8| 	return "small"
...
=== branches ===
  cov.maxon:4:2  then@4=0  else(implicit)@4=3
```

`maxon coverage report cov.exe` prints the same report without the `target exit code` line. With
`--json` the report is one object with `exe`, `runCompleted`, `linesCovered`, `linesInstrumented`,
`linesEliminated`, `armsTaken`, `armsInstrumented`, and `files` (each line's `state` and `count`) and
`branches` arrays.

**Exit codes:** `0` when a report is produced, `1` for a refusal, such as a binary built without
`--coverage`:

```text
coverage: app.exe was not built with coverage instrumentation — it has no coverage points to report on
```

The program's own status is the `target exit code` line.

## Tracing green threads

A build with `--async-trace` writes one line to stderr each time a green thread is spawned, sleeps,
waits on I/O, resumes, or is awaited. Without the flag, no trace code is emitted.

```text
$ maxon build app.maxon --async-trace
$ ./app.exe
spawn #1
sleep_yield #1
sleep_resume #1
await #1 [yield]
points=1000 worker=42
```

| Line | Meaning |
|------|---------|
| `spawn #N` | Green thread `N` was started |
| `sleep_yield #N`, `sleep_resume #N` | It gave up its processor to sleep, and resumed |
| `io_yield #N [kind]`, `io_resume #N [kind]` | It waited on I/O (for example `[file_exists]`, `[net_connect]`, `[net_recv]`), and resumed |
| `await #N [yield]` | It was awaited and the caller had to wait |
| `await #N [immediate]` | It was awaited after it had already finished |
| `try_await #N` | The same, for a `try await` |

## Investigating a running program

1. **A panic:** read the backtrace, then map positions with `maxon debug --dump-info <exe> lines` or
   `maxon debug --symbolize`.
2. **Green threads that stall or run out of order:** build with `--async-trace`.
3. **Memory:** build with `--debugstream` and run `maxon monitor --filter=mm`. Exit code 101 is the leak
   check.
4. **Slow:** `maxon profile run` (x64-windows), or `--folded` into a flame graph.
5. **Untested code:** build with `--coverage` and run `maxon coverage run`.
6. **What the compiler emitted:** `maxon build --emit-ir`.
