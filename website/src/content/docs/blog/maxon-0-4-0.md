---
title: Maxon 0.4.0
description: Release notes for Maxon 0.4.0 — what changed in the compiler and standard library.
date: 2026-10-04
tags:
  - release
excerpt: Maxon 0.4.0 is out. Here's what changed.
---

**Maxon 0.4.0 is released.** Archives for every supported target are on the
[GitHub releases page](https://github.com/maxon-lang/maxon/releases/tag/v0.4.0).

## What changed

### Added

- A debugger, on x64-windows, x64-linux, arm64-linux and arm64-macos. `maxon debug` runs a program under
  it, interactively or from a batch of commands: breakpoints by line, function or address, with
  conditions; stepping; pausing; locals, expressions and backtraces; every green thread, which can be
  listed, inspected and held; and the program's trace events beside its stops. It attaches from outside
  the program, so nothing is built into a program to make it debuggable beyond its `.mxdbg` sidecar.
- `maxon dap-server`, a Debug Adapter Protocol server over stdio, which the VS Code extension debugs
  programs and individual tests through. `maxon test --list --build` builds a project's tests and names
  each test, so a single test can be debugged.
- Thirteen `debug_*` MCP tools that drive the same debugger, and `maxon mcp-server --http`, an HTTP
  transport on loopback with sessions.
- Generic functions: `function f(x T) uses T where T is Equatable`, with `T` inferred from the arguments.
  Integers, floats, `String` and `bool` are `Equatable` and `Comparable`.
- Subtype aliases: `typealias X = int(range) implements Parent` widens to its ancestors with no cast,
  while siblings are refused and narrowing needs one.
- Program-wide defaults: a top-level `default Key = expression` that any thread reads with
  `Key.current()` and replaces with `Key.register(x)`, for a service reached through an interface or for
  an immutable value. `I.handle` is a handle to any running service whose type implements interface `I`.
- `SharedValue`, a value every thread reads lock-free and a writer replaces whole.
- `Log`, a structured logger modelled on Go's `log/slog`: levels, typed attributes, groups, and text and
  JSON handlers. A program logs with its first call.
- `HttpServer`, an HTTP/1.1 server on the green-thread scheduler. `HttpClient` decodes chunked responses,
  answers a closed set of status codes, and takes a whole-exchange timeout and an optional body cap;
  `TcpClient.connect` takes a deadline.
- `Random`, drawing from the operating system's cryptographic source on every target. `File.createText`,
  which refuses a file that already exists; file errors tell a busy file, a denied access and an existing
  file apart. `WallClock.nowUnixNanos`, RFC 3339 formatting and `CivilDate`.
- An enum or union that declares `implements` widens into that interface wherever a `type` would.
- A record whose fields are all `let` and together fit 64 bits — narrow unsigned ranges, `bits(n)` and
  `bool` — is stored as one machine word, with no allocation.
- Closures may be returned, stored in fields and containers, and passed to `async`; captured interface
  values and nested captures work.
- A union payload may be a float.
- A record holding a value at an interface type can be cloned.
- `maxon init`, which scaffolds a project.
- `__Builtins.scavengeMemory()`, which returns freed memory to the operating system, and builtins that
  attribute the live heap and allocation churn by type.

### Changed

- Projects, task files and tests have their own extensions. A `<name>.maxproj` file marks a project and
  describes its builds, replacing `build.maxon`; `maxon build <target>` runs one of its targets. A
  `<name>.maxtasks` file holds tasks, which `maxon run <task>` runs. Test files are `<name>.maxtest`.
- `maxon run <file>` is now `maxon execute <file>`. `maxon <file>.maxon` and `#!/usr/bin/env maxon` are
  unchanged.
- Every command-line option is long-form: `-o` is `--output=` and `-t` is `--filter=`. An option given to
  a command that does not take it is refused, and so is a bad `--log=` value.
- `maxon test --filter=` may be repeated, and each pattern must match a test.
- `Runtime.yield()` and `Runtime.processorCount()` are `Scheduler.yield()` and
  `Scheduler.processorCount()`.
- The trace-key recorder that was `Log` is `TraceCapture`.
- Arguments to an `async` call or a service message, and the managed locals a closure captures, are
  moved; reading one afterwards is a compile error. The `let`-argument freeze and E3160 are gone.
- A bare type name with more than one visible declaration is an error (E3063) rather than a silent pick;
  qualify it with its directory, `stdlib.` or `export.`. A file's own declaration wins.
- A hidden type is hidden at every place a type can be named, along with its fields and methods.
- A function's parameter, return and `throws` types must be at least as visible as the function (E3167).
- A function that declares `throws` must be able to throw (E3168).
- Inside a `test` body, a throwing call or `await` needs no `try`.
- Casting a literal to the type its destination already declares is an error (E3010).
- An array index must be the array's `ElementIndex`; a `String` index is refused. Array `count()` is an
  `ElementIndex`. Several public stdlib aliases were merged into `ElementIndex`, `EntryCount`, `BytePos`,
  `Byte` and `NetworkPort`.
- An or-pattern binding is refused on any case that does not bind it (E3129).
- A typealias declared inside a type or extension is a nominal type, and a private ranged alias belongs
  to its file.
- A full-range unsigned value such as `u64.max` passes range checks; an unsigned subtraction is refused
  only when it borrows.
- The language server checks a document against its whole project, so it reports what a build would.
  Hover and go-to-definition reach other files and the standard library, a window may hold several
  projects, and the server reports when it is loading one.
- A self-compile's peak memory is roughly halved.
- On Windows, program arguments, environment variables, file and directory names, the working directory,
  the executable's path and shared-memory names are UTF-8 for any Unicode text, and a child's arguments,
  environment and working directory reach it as the characters they spell. A non-ASCII host name resolves
  through its Unicode form.
- A duplicate declaration across files is reported on the same file on every host: the one whose path
  sorts later byte by byte, and a free function rather than the method it collides with.
- A path holding a NUL names no file, a NUL in a subprocess's program, argument, working directory,
  environment or redirect path refuses the spawn with `spawnFailed`, and a host holding a NUL, or an empty
  host, is `resolveFailed`.
- A program started with its standard input, output or error closed has them opened on the null device.

### Fixed

- Bytes written to a subprocess's standard input were cut at the first NUL.
- `Subprocess.run` hung when the child wrote more than a pipe holds before reading its input, and its
  timeout did not fire.
- On Windows, concurrently started children inherited each other's pipe ends, so a child's output did
  not end until an unrelated child exited.
- A subprocess whose pipe could not be created was started without it on macOS, and on Windows the
  failure reported an unrelated error.
- A failed subprocess start could close another file's descriptor, and a released subprocess handle
  could release a process started after it.
- A failed call's error reason could be replaced by another's after the green thread moved to another
  operating-system thread.

- A variable passed to a parameter the callee reassigns — a module-level `var`, a field, or a field
  reached through a chain — was not updated.
- Programs with a `bool` global and green threads crashed at startup on arm64.
- A slot in use could be handed out twice under load on arm64, corrupting memory.
- A record of narrow immutable fields spawned as a service crashed at shutdown.
- A value moved in a match arm that ends `and fallthrough` leaked when the match entered the next arm
  directly.
- A promise read out of a temporary container, or consumed twice from a field, crashed instead of being
  kept alive or refused.
- A generic service could hand its own state to a caller, and a fresh container over `T` returned from
  one was wrongly refused.
- A generic method storing a type-parameter value taken by reference was refused, or miscompiled.
- Managed globals were freed before a green-thread program's last handlers ran.
- A dropped coroutine that went on to wait on a socket hung the program.
- On arm64-linux, signal handlers ran with an undefined mask and signal stacks could overflow; writes and
  waits interrupted by a signal failed; a child killed by a signal was not reported as signalled.
- A timed-out subprocess left its children running.
- On Windows, a subprocess started by name could resolve to a file Windows cannot run, such as the
  extensionless script beside `npm.cmd`.
- A range check on an alias declared inside a type or extension was skipped.
- Every runtime abort now prints its reason, and a `wasm32-wasi` panic prints its trace.
- An undefined function reached through `try`, `await`, a capture or `for … in` reports E3004 at the
  call instead of an unrelated error.

### Removed

- `build.maxon`, `*.test.maxon` as test files, and the `-o`, `-t`, `--compiler`, `--version`, `-V`,
  `--help` and `-h` spellings.
- E3099, E3105 and E3160.

## Install

To install Maxon or upgrade an existing install, see [Installation](/docs/getting-started/installation/), which also
shows how to install a specific release.
