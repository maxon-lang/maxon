---
title: Debugger
description: The parts of maxon debug — the .mxdbg debug-info sidecar, the host debug backend and the debugger itself.
sidebar:
  order: 6
---

`maxon debug` debugs a native program interactively: breakpoints, stepping, backtraces and locals. What it
offers a user is under [Debugging and Profiling](../CLI_REFERENCE.md#debugging-and-profiling); this page is an
outline of its parts and is not yet a full account of them.

## The parts

| Part | Where | Role |
| --- | --- | --- |
| Debug-info emission | `maxon-bin/Compiler/Debug/` | `MxdbgEmit`, `MxdbgWriter`, `MxdbgTypeTable` and `MxdbgFormat` write the `.mxdbg` sidecar beside the executable; `MxdbgReader` reads it back. |
| The runtime contract | `maxon-bin/Compiler/Debug/DebugContract.maxon` | The roster of `.data` words and record offsets the sidecar publishes, so a debugger outside the process can find the scheduler's own state and see whether the runtime has started (`__mrt_program_started`). Each word is published with the width of its slot. |
| The host backend | `maxon-bin/Debug/Backend/` | The one funnel onto the host's debug interface, the register file, the stop events, and the scratch pages a stopped thread's registers travel through. `BackendChoice` picks a backend off the host triple and `DebugBackendUnion` is the one face the debugger asks through: `WindowsBackend` over a debug port, `PtraceBackend` over `ptrace`, `GdbRemoteBackend` over the GDB remote serial protocol to Apple's `debugserver`. |
| The debugger | `maxon-bin/Debug/Debugger/` | The session, the command REPL and batch mode, breakpoint conditions, symbol lookup, value rendering, threads, and the `X64Decode` and `Arm64Decode` instruction decoders it steps and unwinds with. |
| The child process | `maxon-bin/Debug/TargetChild.maxon` | Launches and controls the program being debugged. |
| The command | `maxon-bin/Debug/DebugCommand.maxon` | The `maxon debug` entry point, including `--dump-info` and `--symbolize`. |

The debugger drives the program from outside it, through the host's own debug interface, so a debuggee
carries its `.mxdbg` sidecar and nothing else of the debugger. With `--trace`, the debugger also reads the
[debug stream](debug-stream.md) and shows the events that led to each stop.

## The session

**The program is the driver's own child on every host.** On Windows and Linux the driver spawns it as its
debugger. On macOS the driver spawns it with `POSIX_SPAWN_START_SUSPENDED` and starts
`debugserver --attach=<pid> -R 127.0.0.1:<port>` (with `--unmask-signals` where the host's `debugserver`
takes it), which connects back to a port the driver is already listening on. The program's output reaches
the driver on its own pipes on every host, and a program left running after a detach reports its own exit
code. While `debugserver` is attached, macOS makes it the program's parent, so closing a session kills the
program through the stub, closes the stub, and then reaps the program. The stub's own output is drained
while the session runs, and a session whose stub exits or times out before connecting is refused with
what the stub printed.

**Registers.** A stop reply carries the general registers of the thread that stopped; another thread's
come from its `qThreadStopInfo` reply, and `p` reads any register those replies leave out. Vector registers are read
when the debugger first asks for one, on every host.

**Resuming off a breakpoint.** The debugger restores the original instruction, single-steps the thread
over it and plants the trap again. Every other thread is held for that one step (suspended on Windows,
left stopped under `ptrace` and `debugserver`), so the trap is out only while the stepping thread runs. A
thread that exits during the step has its trap planted again before the others are released.

**Pausing.** Each backend reports the true reason for a stop. While a pause is pending, a breakpoint whose
condition holds is reported as the breakpoint and answers the pause; a stop the debugger would resume on
its own is reported as the pause. On macOS the entry stop and a pause both arrive as a halt of the stub's:
the first halt is the entry, and a halt with no pause pending is resumed unseen. A program's own `SIGINT`
is passed on to it. Any stop the debugger reports ends a step still outstanding on another thread. A
pause that lands while the program is still being started (in the loader, or ahead of
`mrt_runtime_init`) lets the program run and asks again, until the pause's deadline. The runtime's
`__mrt_program_started` byte, written first by `mrt_runtime_init`, tells the two apart; a binary whose
sidecar lacks the byte is paused where it lands.

**Detaching** restores every byte the debugger wrote and lets the program run on. Under `ptrace`, a stop
signal still owed by a thread is taken first, and every signal owed to the program is delivered with the
detach. On Windows, every stepping thread's trap flag is cleared and every queued debug event is continued
before the process is released.
