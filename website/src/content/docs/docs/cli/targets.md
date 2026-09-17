---
title: Targets
description: The platforms maxon compiles for, cross-compiling, running wasm32-wasi programs, and what each target supports.
sidebar:
  order: 5
---

`maxon` compiles for five targets. Without `--target`, it compiles for the **host** it runs on.

| Target | Output | Extension |
|--------|--------|-----------|
| `x64-windows` | PE executable | `.exe` |
| `x64-linux` | Static ELF executable, no libc | none |
| `arm64-linux` | Static ELF executable, no libc | none |
| `arm64-macos` | Mach-O executable, ad-hoc signed | none |
| `wasm32-wasi` | WASI Preview 2 component | `.wasm` |

`maxon version` prints the host target. A spelling that names none of them is refused and the command
exits 1:

```text
error: unknown --target 'x86-linux' (expected one of: x64-windows, x64-linux, arm64-macos, arm64-linux, wasm32-wasi)
```

## Cross-compiling

`--target=` makes the compiler **cross-compile** the program: the compiler itself still runs on the
host. Any host can build for any target, with no extra toolchain for the four native targets: the
compiler writes the executable, including the macOS code signature, itself.

Commands that take `--target=`:

| Command | What the target changes |
|---------|-------------------------|
| [`maxon build`](/docs/cli/#maxon-build) | The executable it writes |
| [`maxon test`](/docs/cli/#maxon-test) | The test binary, which it then runs; on a host that cannot execute that target, every test is reported as not run |
| [`maxon spec-test`](/docs/cli/compiler-development/#maxon-spec-test) | Each spec test, run under the checkout's vendored runtime where needed |

`maxon run` always builds for the host, and so does a `build.maxon` manifest program (the builds it
describes follow `--target`). An executable copied from Windows to a Linux or macOS machine may need
`chmod +x` before it runs.

## Running a `wasm32-wasi` program

The output is a WASI Preview 2 component. Run it under a runtime that supports components, such as
[wasmtime](https://wasmtime.dev/), enabling the `cli-exit-with-code` interface the component imports so
its exit code is delivered:

```bash
maxon build app.maxon --target=wasm32-wasi     # writes app.wasm
wasmtime run -S cli-exit-with-code=y app.wasm
```

Without `-S cli-exit-with-code=y`, wasmtime refuses to start the component.

Building a component needs two tools the compiler calls: `wasm-tools`, and the WASI WIT package. The
compiler looks for them as `vendor/wasm-tools/` and `vendor/wasi-wit/` in the working directory and the
nine directories above it. A Maxon source checkout stages them with `scripts/fetch-vendor.sh`; an
installed compiler does not include them. A build that cannot find one is refused with error E6005, naming
what is missing and where it looked, before anything is compiled; a `wasm-tools` step that fails is refused
with the same code and what the step printed.

## What each target supports

`x64-windows`, `x64-linux`, `arm64-linux` and `arm64-macos` support the whole standard library. The
differences a program can meet are:

- **`maxon profile`** runs only on x64-windows. Elsewhere it is refused.
- On the Linux targets, host-name resolution for sockets is built in and simple: `A` records only, the
  first nameserver in `/etc/resolv.conf`, no search domains and no CNAME following.

**`wasm32-wasi`** runs heap-allocated values, strings, `print`, structs, arrays, closures, interfaces and
floating point. It does not provide these, and a program that reaches one is **refused at compile
time**, at the call:

| Not available on `wasm32-wasi` | Refused with |
|--------------------------------|--------------|
| `async`/`await`, green threads, services, `sleep`, `Runtime.yield` | E3104 |
| Clocks (`Clock`, current time, CPU ticks) | E3104 |
| Command-line arguments | E3104 |
| File and directory I/O | E3104 |
| Sockets | E3104 |
| Reading console stdin | E3104 |
| Process information, priority, CPU count | E3104 |
| Subprocesses | E3074 |
| Environment variables (`Process.environmentVariable`) | E3074 |

```text
error E3104: app.maxon:3:2: 'sleep' lowers to the runtime entry '__gt_sleep', which has no wasm32-wasi implementation
error E3074: app.maxon:5:17: Subprocess is not supported on wasm32-wasi (no process-spawn primitive); guard the call with #if not os(Wasi).
```

To keep one source for several targets, guard the unsupported part with `#if not os(Wasi)`.

Build options that need a host facility are refused on `wasm32-wasi` before anything is compiled:
`--debugstream` (needs shared memory and an uptime clock) and `--coverage` (needs file output).
`maxon test --color=auto` cannot detect a terminal there and prints no colour.

On Windows, a program's output to a console is converted from UTF-8 as it is written, so non-ASCII text
displays correctly; output to a pipe or file is the program's UTF-8 bytes unchanged. The console's code
page is never modified.
