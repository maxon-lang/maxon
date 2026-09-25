---
title: Debugging & Profiling
description: Debug info, panics and backtraces, the leak check, and the debug, monitor, profile and coverage commands.
sidebar:
  order: 3
---

Maxon has an **interactive debugger on x64-windows** — `maxon debug <exe>`: breakpoints, stepping,
backtraces with inlined frames, and locals read out of the stopped thread. It launches the program
rather than attaching to one already running, and it needs the in-process debug agent, which is emitted
by default on that target only. Everything else on this page works on every target: debug information
beside every binary, panic backtraces, a leak check, a trace monitor, a sampling profiler and code
coverage.

The examples below use output from real runs; addresses, counts and timings vary from build to build.

## The `.mxdbg` debug-info sidecar

Every `maxon build` writes a sidecar named after the full output file, beside it, unless you pass
`--no-debug-info` or the described build sets `debugInfo: false`:

```text
[CMP] INFO: Wrote 35598 bytes of debug info to app.exe.mxdbg
[CMP] INFO: Wrote 103992 bytes of code to app.exe (compiled in 0.3 s)
Compiled -> app.exe
```

On Linux and macOS the executable is `app` and the sidecar `app.mxdbg`. No sidecar is written for
`wasm32-wasi`. The executable is **byte-identical** with or without the sidecar, so the binary you debug is
the binary you ship.

The sidecar maps machine code back to the source. It records the target, a **build id** (a hash of
the executable's code section) and the directory the build ran in, then the source files, functions with
their frame size, local variables and origin, types, the line table and inlining records. `maxon debug`
prints it; `maxon profile` and `maxon coverage` read it, and a `--coverage` build requires it.

The sidecar format is versioned (version 7), and a reader refuses a sidecar of any other version: after
upgrading the compiler, rebuild before debugging, profiling or reporting coverage.

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
`panic: nil pointer or invalid memory access` or `panic: stack overflow`, followed by the same stack trace.

## The leak check

A program that allocates checks, after `main` returns, that every heap allocation was released. If one
was not, the program prints nothing and exits with code **101** instead of the code `main` returned. Maxon
releases memory itself, so there is no call you forgot to make: exit 101 points at the memory management
the compiler emitted. `maxon test` reports such a test as `LEAKED`, and
[`maxon monitor --filter=mm`](#maxon-monitor) shows every allocation and free.

## `maxon debug`

Debugs a program interactively, and reads the `.mxdbg` sidecar back. The two sidecar forms are read-only
and accept either the executable or the sidecar itself; the debugger forms launch the executable.

```bash
maxon debug <exe> [--trace] [--stop-timeout=<seconds>] [--target-env=NAME=VALUE]... [-- <program args>...]
maxon debug --batch --commands=<cmd;cmd;...|@file> <exe> [same options]
maxon debug --complete=<partial line> <exe>
maxon debug --classify=<hex>[,<hex>...]
maxon debug --dump-info <exe|.mxdbg> [header|files|functions|types|lines|statements|inline]
maxon debug --symbolize <exe|.mxdbg> <codeOffset...>
```

| Option | Description |
|--------|-------------|
| `--batch` | Run `--commands=` instead of prompting, and write one JSON object per line on stdout |
| `--commands=` | The commands `--batch` runs, separated by `;`, or `@<path>` to read them from a file |
| `--complete=` | Print the completions for a partially typed debugger line, one per line, and run nothing |
| `--classify=` | Classify raw x64 instruction bytes the way arming a breakpoint does, and run nothing |
| `--trace` | Create the DebugStream ring the `trace` command reads, and name it to the debugged program |
| `--stop-timeout=` | Seconds to wait for a stop, and the budget for one driver-walked step (default 10). A fraction is honoured to the millisecond; a value outside 0.001 to 922337203685 is refused, naming the option. |
| `--target-env=` | Set a variable in the DEBUGGED program's environment; repeatable |
| `--dump-info` | Print the sidecar, or only the sections named after the path |
| `--symbolize` | Resolve offsets into the executable's code section to `file:line:col` |

`maxon debug` with no arguments prints the usage above and exits 1.

**The session.** The driver creates a two-page shared control segment, names it in the `MAXON_DEBUG`
environment variable of the program it launches, and the agent inside that program parks before `main`
while the driver arms whatever was asked for. Only the agent writes into the program's own code, and no
OS debug API is used. A debugged program that finds its driver gone stops where it is and exits 97,
rather than staying parked forever waiting for a command nobody will send.

A program the debugger cannot drive is refused by name, with exit 1: a wasm module, a binary built for
another target, one built `--no-debug-agent` (reported as no agent having attached), and a binary whose
sidecar describes a different build.

**Commands**, with their aliases: `break` (`b`) · `clear` · `run` (`r`) · `continue` (`c`) · `step` (`s`) ·
`next` (`n`) · `finish` · `until` (`u`) · `backtrace` (`bt`, `where`) · `print` (`p`) · `locals` · `pause` ·
`threads` · `gt-backtrace` · `gt` · `gt-park` · `gt-resume` · `trace` · `help` (`?`, `commands`) ·
`quit` (`q`, `exit`).

`break <target> if <local> <op> <literal>` arms a conditional breakpoint the agent evaluates itself, so a
hit that does not satisfy it is resumed without a stop; a condition over a float, a `String`, a field path,
a constant or a local the breakpoint's pc has no record for is refused `condition-unsupported` and the
breakpoint is NOT armed, and a literal that does not parse is `condition-invalid`.

`pause` interrupts a program that stops on nothing and publishes a `pause` stop. `threads` lists the
program's green threads, `gt-backtrace <id>` walks one, `gt <id>` selects the thread that `print`,
`locals` and `backtrace` answer about (`gt` alone clears it), and `gt-park <id>` / `gt-resume <id>` hold a
thread off every machine and let it go again. Wherever an id is taken, the selectors `running`, `ready`,
`waiting` and `held` name the lowest-numbered thread of that status instead, and `<status>:<function>`
narrows to the threads whose entry function has that name — a batch script cannot know an id in advance.
`trace [N]` prints the last N DebugStream events committed before the stop (20 by default), and needs
`--trace` on the session and `--debugstream` on the build.

Stepping is refused while a `gt` selection is on, a register-located local of a selected thread reads
`unavailable: "register-of-parked-thread"`, and an id names a thread only for as long as that thread
lives: every word that names one re-lists the roster first, and an id whose thread has ended is
`no-such-thread`. The other refusals are:

- `hold-table-full` — 16 threads may be held at once.
- `thread-is-running` — for a thread on a machine.
- `not-pausable` — for a program the agent could not interrupt.
- `not-running` — for a word that needs a stop while the program is running.
- `fault-is-terminal` — for a step from a `fault` stop.
- `table-full` — for a `break` when 64 instructions are already armed, counting the temporary
  breakpoints a step plants, or when every out-of-line slot the agent runs a displaced instruction in is
  still in use. A step that meets a full table walks one instruction at a time.
- `return-hold-taken` — for a conditional `break` on the instruction of a running step's temporary
  breakpoint, when another of that step's temporary breakpoints already shares its instruction with a
  condition. One such instruction at a time may carry a condition.

A `stop` carries a `reason`: `entry` before `main`, `breakpoint`, `step`, `pause`, `trap` and `fault`.
`trap` is an `int3` the agent did not plant — a stop like any other, with a register file and a stack to
walk, which `continue` resumes past. `fault` is a hardware fault the program would otherwise panic on (an
access violation, a stack overflow, an integer divide by zero or an integer overflow), stopped at the
faulting instruction with a `fault` field naming it in the words its panic line uses; a fault inside
library code is positioned at the program's own call into it. The faulting instruction would fault
again, so a step from a fault stop is refused `fault-is-terminal`, and `continue` hands the fault to the
runtime, which ends the program with the same panic line, backtrace and exit code it has undebugged,
reported as a `crash`. A stop also carries the `machine` (the OS thread id that took it), its `thread`
id when a green thread was running, and its `function` when its position lies in one.

**Every stop stops the whole program.** The agent suspends every other machine before it publishes a word
of the stop and resumes them all on `continue` or a step, so the roster, a parked thread's stack and the
memory a `print` reads are one consistent picture. A stop of a program whose threads are all idle — a
`pause` with nothing running user code — reports no pc: `threads` and `gt-backtrace` answer as usual,
while a walk of the stopping thread has nothing to describe.

Two costs are worth knowing. A held thread is refused each time a machine reaches it, and that machine
pauses and looks again, so a hold is a polite spin rather than a parked thread. And a `pause` asks the
running threads to yield at their next safe point rather than stopping them where they stand, so a
program that reaches none within the attempt budget answers `not-pausable`.

**The running state.** A `run`, `continue` or step that reaches `--stop-timeout=` answers `timeout` and
LEAVES THE PROGRAM RUNNING. In that state `break`, `clear`, `pause`, `threads`, `gt-park` and `gt-resume`
are serviced live by the agent's own service thread and `continue` waits again; `backtrace`, `print`,
`locals` and the steps answer an error until something stops. The session's exit code is still 1 once
anything has timed out, and the program is reaped when the session closes.

A stop that lands while the program runs is reported ahead of the next command's answer: a stop that
came before the command, or one the agent was already parked in when it answered, precedes that answer
in the transcript, so each event sits where it happened.

A break target is `file.maxon:LINE`, a bare `LINE` in the file that declares `main`, `*0x<offset>`, or a
function name resolved exact → `Type.method` → leaf name → word prefix. More than one match answers
`ambiguous` with the candidates; a name nothing answers to names the nearest function.

- **A file** is named by the most specific spelling given: a full path names that one file; a relative
  path with a directory names the file it reaches from the directory the program was built in, or else
  every source file whose path ends in it; and a bare file name matches every source file of that name.
  A spelling that matches more than one file answers `ambiguous` with the candidates.
- **`*0x<offset>`** arms the instruction at that offset into the code section, the offsets
  `--dump-info` and `--symbolize` print. An offset past the code, or in no function, is refused
  `out-of-text`; one that falls inside an instruction, past its first byte, is refused
  `not-an-instruction-start`.
- **A line** arms wherever control enters it: the `line-entry` rows of the line table. A line the inliner
  copied into several places is armed at every copy. A line whose only code is an inlined call arms at
  the call: the stop is at the call line in the caller's frame, `next` runs over the inlined body and
  `step` enters it. As in gdb, the inlined body joins that stop's backtrace once it is entered. A line
  whose only code is a narrowing cast's range check is a line with code. A line with none answers
  `no-code`.
- **The runtime's own code** — the debug agent, the entry stub and the rest of the compiler's scaffolding
  — is refused `inside-runtime-symbol`. Which code is the runtime's is recorded by the build. A `test`
  body is the program's own, and is named `test '<prose>'` in stops, backtraces and break answers, as
  `maxon test` names it.

`step` enters a callee, `next` stays in the frame it was issued from, `finish` runs to the return of its
frame and `until` runs forward past the current line. Each walks the program's own code one instruction
at a time, and runs other code at full speed to a temporary breakpoint:

- `next` and `until` run a call they reach, direct or through a value, to its return address.
- `finish` from a function's own frame runs to that frame's return address. A recursive call that
  reaches the address in a deeper frame runs on.
- `step` into a library function runs it to the return into the program's code.
- Library code the compiler inlined into the program's frame runs to the one place control leaves it.
  Under `step` it does so when the inlined code, and every function it calls directly, is library
  code, and a temporary breakpoint also stands on each call it makes through a value, so a closure of
  the program's that the library calls is still stepped into. Inlined code with more than one way out
  is walked.

Each honours any breakpoint it passes — except the one it started on — and each is bounded by
`--stop-timeout=`. A `finish` from the outermost frame is refused.

```text
$ maxon debug --batch --commands="break app.maxon:12;run;locals;next;backtrace;continue" app.exe
{"event":"breakpoint","action":"set","function":"work","file":"app.maxon","line":12,"offset":"0x7a"}
{"event":"stop","reason":"breakpoint","function":"work","file":"app.maxon","line":12,"col":9,"offset":"0x7a","machine":5312,"thread":1,"source":[…],"backtrace":[…]}
{"event":"locals","function":"work","locals":[{"name":"total","type":"Amount","kind":"int","display":"42"}]}
{"event":"stop","reason":"step","function":"work","file":"app.maxon","line":13,"col":3,"offset":"0x7e","machine":5312,"thread":1,"source":[…],"backtrace":[…]}
{"event":"backtrace","frames":[{"frame":0,"function":"work","file":"app.maxon","line":13,"col":3,"offset":"0x7e"}]}
{"event":"exit","code":0}
```

**In `--batch`** stdout is pure JSON, one object per line, and the debugged program's own stdout and
stderr both go to this driver's stderr. The events are `breakpoint`, `stop`, `backtrace`, `locals`,
`value`, `threads`, `gt-backtrace`, `gt-select`, `gt-park`, `gt-resume`, `trace`, `exit`, `crash`,
`timeout` and `error`. A list that cannot be produced is `null` beside a `<name>Unavailable` reason, and a
value that cannot be read carries `unavailable` with one of `optimized-out`, `not-live-here`,
`read-failed` or `layout-not-described`. A `stop`, `breakpoint`, `locals` or thread row carries
`function` when its position lies in a function, and a backtrace frame carries its `col` beside its
`line`. A `trace` event carries `since`, the previous stop's position in the ring, when there was a
previous stop.
The driver exits 0 when the session completed — the program's own exit code is DATA in the `exit` event —
and 1 on a timeout, an unacknowledged command, a refused session or a crash. A command issued after the
program has ended is an `error`, and the verdict stands.

**Without `--batch`** the same commands are read from stdin, one per line, and answered as text for a
person; end of input quits. The debugged program's streams pass through to this driver's own.

**`--classify=`** is the door onto the instruction classifier a breakpoint's out-of-line resume depends
on. It reads no program:

```text
$ maxon debug --classify=488d0dce6f0000,c21000,ff5008,62
len=7 class=2 cond=0 disp=3 target=0x7fd5
len=3 class=6 cond=0 disp=0 target=0x0 rel=1
len=3 class=7 cond=0 disp=2 target=0x0 operand=mem:base=0,index=none,scale=1,disp=+0x8
unclassified
```

The classes are 1 plain, 2 pc-relative data, 3 direct call, 4 direct jump, 5 conditional jump, 6 return,
7 indirect call and 8 indirect jump. The agent places a breakpoint on classes 1 to 7 and refuses an
indirect jump `unclassified`. Every address is computed against a fixed probe address, `0x1000`:

- `target` is the absolute branch target for 3, 4 and 5 and the absolute referent for 2.
- `disp` is the byte position of the displacement of a memory operand, and `cond` the condition code of
  a conditional jump.
- `rel=` on a return is the byte position of the immediate a `ret imm16` releases, and `0` for a `ret`
  that releases nothing.
- `operand=` on an indirect call names what it calls through: `reg:<n>` a register; `word:0x<address>` a
  pc-relative word at that absolute address; `mem:base=<n>,index=<n>,scale=<s>,disp=<±0x…>` a memory
  word, with `none` for an absent base or index; and `overridden:form=<n>` an operand behind a segment
  or address-size prefix, whose word the classifier leaves unresolved (`form` 1 register, 2 memory,
  3 pc-relative).

**Sections** of `--dump-info` (with none named, all are printed):

| Section | Contents |
|---------|----------|
| `header` | The file, target, build id and `root`, the directory the build ran in, from which the recorded relative source paths are read |
| `files` | The source files, including the standard-library and runtime files the program uses |
| `functions` | Each function's code range, frame size, parameter, line and local counts and `origin`, then `as <name>` when the debugger shows it under another name (a test body is shown as `test '<prose>'`); and, per local, the code range `[start, end)` it is live over, its location, `in [n]` when it belongs to inline site `n`, and its type. A location is a frame slot, a register, `<optimized out>`, `= v` for a value folded to a constant or `= {…}` for a whole record folded to one; a `*` after it means the location holds a pointer to the value rather than the value. A name with several live runs has a row per run. |
| `types` | Each type's kind, size, alignment and fields, with `(signed)` on a type whose values are signed and an `element` row on an array's |
| `lines` | The line table: code offset, source position and flags — `statement`, `coverage`, and `line-entry` on a row where control enters its source line: falling in from a different line, at the function's entry, or through a branch from another line |
| `statements` | The same table, without the code offsets |
| `inline` | Inlined call sites, each with the site it nests under, the position of its call and its `origin`, then the code ranges they occupy |

An `origin` says where a function or an inlined body came from: `authored` (the program's own source),
`test` (a `test` body), `library` (the standard library or the runtime), `generated` (source the compiler
wrote for this build) or `synthesized` (code with no source, such as the entry stub). The debugger treats
`authored` and `test` code as the program's own.

```text
$ maxon debug --dump-info app.exe header
Debug info: app.exe
  target:   x64-windows
  build-id: 0xb1c1a21a3d5126c2
  root:     C:\work\app

$ maxon debug --dump-info app.exe functions
  functions (109):
    mrt_start                        [0x0000, 0x002e)  frame=0x20  params=0  lines=0  locals=0  origin=synthesized
    main                             [0x0040, 0x021c)  frame=0x48  params=0  lines=21  locals=9  origin=authored
        limit                [0x0040, 0x021c)  = 64  : Amount
        points               [0x0065, 0x021c)  [rbp-0x50]*  : Array_Amount
        total                [0x0085, 0x0089)  reg2  in [0]  : Amount

$ maxon debug --dump-info app.exe inline
  inline sites (385):
    [0] worker  called at app.maxon:17:15  origin=authored
    [1] print  called at app.maxon:20:2  origin=library

$ maxon debug --dump-info app.exe lines
  line table (2484):
    0x0051  app.maxon:14:15  [statement]
    0x0078  app.maxon:6:11  [statement, line-entry]
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
| `--filter=sched` | Green-thread events only: `sched_spawn #N`, `sched_await #N` (the thread awaited), `sched_yield #N` and `sched_resume #N` (around `sleep` and `Scheduler.yield()`), `io_yield #N` and `io_resume #N` (around a blocking I/O operation). `N` is the thread's number, the same one `--async-trace` prints. |
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
