---
title: Maxon 0.2.0
description: Release notes for Maxon 0.2.0 — what changed in the compiler and standard library.
date: 2026-09-14
authors: maxon
tags:
  - release
excerpt: Maxon 0.2.0 is out. Here's what changed.
---

**Maxon 0.2.0 is released.** Archives for every supported target are on the
[GitHub releases page](https://github.com/maxon-lang/maxon/releases/tag/v0.2.0).

## What changed

### Added

- One-line installers for macOS, Linux and Windows, which install the latest release into
  `~/.maxon`.
- `maxon upgrade`, which updates the install the running compiler belongs to.
- Container images at `ghcr.io/maxon-lang/maxon`, in `debian` and `distroless` variants for amd64
  and arm64.
- The Homebrew formula serves x64 and arm64 Linux as well as macOS.
- `maxon mcp-server`, a Model Context Protocol server exposing build, run, test, format, check, IR
  dumps and error-code lookup to AI tools.

### Changed

- A green thread that runs for 10 ms without waiting is preempted, including inside a loop that
  calls nothing, so one busy thread no longer holds up the others.
- Socket reads, accepting a connection, subprocess pipes and waiting for a child process park only
  the green thread that waits; none of them occupies an OS thread any more.
- `main` runs as a green thread whose stack grows as needed, and idle green threads give back stack
  they no longer use.
- Mutating a value that a live `let` can still observe is refused, however the value was reached —
  through a call's result, a closure, an interface method or a union payload as well as a direct
  field. A write through a method or a field that could reach such a record is a new error, E3159.
- Generated code is substantially faster: every function called from one place is inlined, and the
  optimizer gains value-range analysis, bounds-check elimination against loop limits, loop
  unswitching, jump threading and hot/cold code layout. `fannkuch-redux` runs in about 1.2× the time
  of the C reference, down from 5.4×.
- A stack trace names inlined functions as their own frames, and prints up to 100 frames, saying
  when it stops short.
- The compiler runs its per-function optimization passes and instruction selection in parallel.
- On Windows, a compiled program's file description is its own name; the "Built with Maxon" credit
  is in its Comments field.

### Fixed

- `maxon build` intermittently hung on macOS when a build manifest ran subprocesses.
- Deep recursion in `main` overflowed its stack at 1 MB on Windows.
- A function returning a tuple of two records could hand back a freed record.
- Passing a struct that holds a file or socket to an `async` call crashed the compiler.
- A fault on arm64 macOS with a corrupted stack hung instead of printing a trace, and a fault on a
  Windows green thread printed its report twice.
- `maxon run` failed in deeply nested directories on Windows.

## Download

```
curl -fsSL https://maxon.dev/install.sh | sh    # macOS and Linux
powershell -c "irm maxon.dev/install.ps1|iex"   # Windows
```

Elsewhere, take the archive for your platform from the
[releases page](https://github.com/maxon-lang/maxon/releases/tag/v0.2.0) and follow the `INSTALL.md` inside it.

⚠ Each archive holds the `maxon` compiler, `stdlib/` and `runtime/` **as siblings**, and that layout
is the contract: the compiler finds its standard library and its runtime by walking up from its own
executable, so moving the binary out on its own leaves it unable to compile.
