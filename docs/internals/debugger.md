---
title: Debugger
description: The parts of maxon debug — the .mxdbg debug-info sidecar, the in-process debug agent and the debugger itself.
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
| Debug agent layout | `maxon-bin/Compiler/Debug/DebugControlLayout.maxon` | The layout of the control segment the in-process debug agent shares with the debugger. |
| The debugger | `maxon-bin/Debug/Debugger/` | The session, the command REPL and batch mode, breakpoint conditions, symbol lookup, value rendering, threads, and the x64 instruction decoder it steps with. |
| The child process | `maxon-bin/Debug/TargetChild.maxon` | Launches and controls the program being debugged. |
| The command | `maxon-bin/Debug/DebugCommand.maxon` | The `maxon debug` entry point, including `--dump-info` and `--symbolize`. |

The debug agent is emitted into x64-windows executables by default and stays dark unless `MAXON_DEBUG` names
a control segment; `--no-debug-agent` leaves it out of the image. With `--trace`, the debugger also reads the
[debug stream](debug-stream.md) and shows the events that led to each stop.
