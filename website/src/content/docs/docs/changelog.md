---
title: Changelog
description: Every released version of the Maxon compiler and standard library, and what changed in it.
---

Every released version, newest first.

Downloads for each release are on the
[GitHub releases page](https://github.com/maxon-lang/maxon/releases), and the install instructions are in
[Installation](/docs/getting-started/installation/).

## 0.2.2 — 2026-09-16

### Added

- `MAXON_PREEMPT=off`, which stops the scheduler taking a processor back from the green thread
  holding it, for a program whose result depends on a thread running uninterrupted. Unset or `on`
  preempts as before; any other value stops the program before `main` runs.

### Changed

- `maxon build`, and `maxon` with no arguments, open with the version line and a warning that Maxon
  is an early preview whose language, standard library and tools may change incompatibly between
  releases.
- A build reports how long the compile took, and no longer prints a line naming the executable
  format it wrote.
- The one-line installers make `maxon` work in the terminal that ran them rather than only in the
  next one. On macOS and Linux, an install whose `PATH` already holds a writable `~/.local/bin` or
  `~/bin` links the compiler there and edits no shell profile.

### Fixed

- A `StreamingSubprocess` handle kept after its child was released could drive the next child the
  runtime started in the same slot — waiting on it, reading its output, writing to its input. A
  copied `StreamingSubprocess` value reached this on its own, because each copy tracked release
  separately. A released handle is refused now.
- The sixty-fifth `runDetached` in a program's life failed, because a detached child never gave its
  runtime slot back.
- Releasing a `StreamingSubprocess` while another green thread collected it left the collector
  reading a slot holding no child, and could corrupt the poll records of unrelated sockets and
  pipes. Two green threads collecting one handle stopped with the error for two readers on one
  socket. Each of the three is a named stop of its own now.
- A socket closed and reopened while an operation was parked on it could take the previous socket's
  timeout, or wake a green thread that no longer owned it.
- Connecting to, or binding, an address written as a dotted quad parked the green thread for a name
  resolution that never ran.
- On Windows, a socket send or receive cancelled by its deadline after the kernel had already moved
  part of the buffer reported a timeout and no count, so a retried send went out twice and received
  bytes were consumed unseen. It reports what it moved, and the deadline is reported by the next
  call.
- On Windows, opening, connecting to and closing a socket counted as blocking kernel calls, so the
  scheduler could take a processor back during them, and Winsock was started up again on every
  connect.
- `Environment.inheritUpdating` and `Environment.custom` take an `EnvMap`, which the standard
  library did not export, so no caller could build the argument. It is public.
- A compiler rebuilding itself moved the running binary aside before compiling, so a compile error
  left the tree with no compiler to build the fix — and the move failed outright when another
  process, such as a language server, still ran the previous binary. The move happens only once the
  compile has succeeded, and a previous binary still in use is retired aside rather than deleted.
- On Windows, `maxon upgrade` ran the install script in a Windows PowerShell that inherited the
  caller's `PSModulePath`. Started from pwsh, that names another edition's modules ahead of this
  one's, and the script stopped with `The term 'Get-FileHash' is not recognized`. The child is
  handed this PowerShell's own module directory.

## 0.2.1 — 2026-09-15

### Fixed

- The compiler occasionally crashed partway through a build with `Range check failed: value outside
  typealias 'AllocCount'`.
- A green thread that started reading a socket while another was finishing a read of the same socket
  could wait forever, unreported by the deadlock detector. It now stops with the documented error for
  two readers on one socket, every time.
- On Windows, a socket receive or send that had already completed when its deadline passed or the
  socket closed reported a timeout: the bytes received were lost, and a retried send went out twice.

## 0.2.0 — 2026-09-14

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

## 0.1.1 — 2026-09-10

### Added

- `maxon run`, which compiles a program and runs it in one step. A `.maxon` file may begin with a
  `#!` line and be run directly as a script.
- `maxon help`, and `maxon help <command>` for a single command. Running `maxon` with no arguments
  now lists the commands.
- `--define`, which sets a top-level `String` constant at build time.
- Build manifests can declare several named targets, and state the version of what they build.
- Compiled executables carry version metadata: a version resource on Windows, a source version on
  macOS, and a producer string on Linux.

### Changed

- `maxon version` replaces the `--version` and `-V` flags, and reports the commit and date the
  compiler was built from.
- `maxon help` replaces the `--help` and `-h` flags.
- `Build.buildOne` in a build manifest is renamed `Build.build`.
- `maxon fmt` separates groups of declarations with one blank line at every nesting depth, not only
  at the top level.
- On Linux and macOS, starting a program that cannot be found raises
  `SubprocessError.executableNotFound` instead of `spawnFailed`.

### Fixed

- Non-ASCII text displays correctly in a Windows console.
- A deadlocked program on Linux reports the deadlock instead of occasionally hanging.
- A default parameter value is filled in a global variable's initializer, not only inside a function.
- On Linux, a subprocess started by program name finds the program on `PATH`, and a file of that
  name in the working directory is never run in its place.

## 0.1.0 — 2026-09-08

The first release. One compiler, written in Maxon, that builds itself.

### Added

- Binaries for `x64-windows`, `x64-linux`, `arm64-macos` and `arm64-linux`, each built and tested
  on its own architecture.
- A Homebrew formula for macOS.
- A VS Code extension, on the Marketplace and Open VSX, with a language server providing
  diagnostics, hover, go-to-definition, completion, rename, symbols and formatting.
- `maxon build`, `fmt`, `test`, `spec-test` and `lsp-server`, and a build manifest written as a
  Maxon program rather than a configuration file.
