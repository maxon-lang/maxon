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
| The runtime contract | `maxon-bin/Compiler/Debug/DebugContract.maxon` | The roster of `.data` words and record offsets the sidecar publishes, so a debugger outside the process can find the scheduler's own state. |
| The host backend | `maxon-bin/Debug/Backend/` | The one funnel onto the host's debug interface, the register file, the stop events, and the Windows backend that drives them. |
| The debugger | `maxon-bin/Debug/Debugger/` | The session, the command REPL and batch mode, breakpoint conditions, symbol lookup, value rendering, threads, and the x64 instruction decoder it steps with. |
| The child process | `maxon-bin/Debug/TargetChild.maxon` | Launches and controls the program being debugged. |
| The command | `maxon-bin/Debug/DebugCommand.maxon` | The `maxon debug` entry point, including `--dump-info` and `--symbolize`. |

The debugger drives the program from outside it, through the host's own debug interface, so a debuggee
carries its `.mxdbg` sidecar and nothing else of the debugger. With `--trace`, the debugger also reads the
[debug stream](debug-stream.md) and shows the events that led to each stop.
