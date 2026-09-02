# Maxon CLI Reference

This document covers the Maxon command-line interface and project system.

---

## Quick Reference

| Command | Description |
|---------|-------------|
| `maxon build [file\|directory]` | Compile a file, directory, or project (default: current directory) |
| `maxon run [function]` | Run an exported function from `build.maxon`; lists commands if omitted |
| `maxon fmt [file\|directory]` | Format `.maxon` source files in-place (default: current directory) |
| `maxon test [directory]` | Run a project's `*.test.maxon` unit tests |
| `maxon spec-test [options]` | Run the COMPILER's own spec suite (not a project's tests) |
| `maxon monitor [--filter=…] <exe> [args...]` | Launch executable with shared-memory debug stream monitor |
| `maxon lsp-server` | Start the language server (LSP) |

---

## Commands

### `maxon build`

Compiles a single Maxon source file, a directory of source files, or a project with `build.maxon`.

**Usage:**
```bash
maxon build [file|directory] [options]
```

**Arguments:**
- `[file|directory]` - Path to a source file or directory (default: current directory). When given a directory, discovers all `.maxon` files recursively and compiles them together.

**Options:**

| Option | Description |
|--------|-------------|
| `--target=ARCH-OS` | Set compilation target (default: the host triple). Supported: `x64-windows`, `arm64-macos` — anything else is refused by name (E5004). `maxon --help` prints the authoritative list, derived from `CompileTarget.SupportedTargets`; this table is a copy and defers to it |
| `--emit-ir` | Write `.ir` file |
| `--dump-stages` | Write IR at each pipeline stage (`.1-maxon.ir`, etc.), user program only |
| `--dump-stages-stdlib` | Like `--dump-stages` but include the full stdlib IR in each stage file (implies `--dump-stages`) |
| `--mm-trace` | Enable runtime memory manager trace output (stderr). Bypasses the stdlib native cache so stdlib re-lowers through the user pipeline with tracing materialized |
| `--mm-debug` | Enable runtime memory debug checks (magic, canary, poison) |
| `--leak-check` | Wire the process-exit leak gate (`mrt_leak_check`) into the binary so it exits `101` if any allocation is still live at exit. Unlike `--mm-trace`, does NOT bypass the stdlib cache, so it reproduces the cached-build path |
| `--async-trace` | Enable async/await runtime trace output (stderr) |
| `--debugstream` | Enable the shared-memory debug stream (use with `maxon monitor`). Also enables the `__DebugStream` builtin: without it, every `__DebugStream` call emits zero instructions |
| `--timing` | Print per-stage compile timings to stderr |
| `--timing-functions=N` | Print top-N hottest functions per heavy pass (implies `--timing`) |

**Behavior:**
- **Single file:** Compiles the file directly. Output name comes from the source filename (`foo.maxon` → `foo.exe`).
- **Directory with `build.maxon`:** Runs the `build()` function from `build.maxon` to get the build config (output path, name, etc.), then compiles all `.maxon` files in the directory.
- **Directory without `build.maxon`:** Compiles all `.maxon` files and names the output after the file containing `main()`.

**Examples:**
```bash
# Compile a single file
maxon build hello.maxon

# Compile with IR output
maxon build app.maxon --emit-ir

# Compile for a different target
maxon build app.maxon --target=arm64-macos

# Build a project directory (uses build.maxon if present)
maxon build myproject/

# Build current directory
maxon build
```

---

### `maxon run`

Compiles `build.maxon` in the current directory and runs the specified exported function as the entry point. If no function name is given, lists available commands.

**Usage:**
```bash
maxon run [function] [options]
```

**Arguments:**
- `[function]` - Name of an exported function in `build.maxon` (optional). If omitted, lists all available exported functions.

Accepts the same build options as `maxon build`.

**Behavior:**
1. Finds `build.maxon` in the current directory
2. Compiles `build.maxon`
3. Runs the specified exported function as the entry point

**Dash-to-underscore translation:** Since Maxon does not allow dashes in identifiers, the CLI automatically translates dashes to underscores. You can type `maxon run spec-test-selfhosted` and it will run the function `spec_test_selfhosted`. When listing available commands (`maxon run` with no arguments), function names are displayed with underscores replaced by dashes.

**Requirements for runnable functions:**
- Must be declared with `export function`
- Must return `ExitCode`
- Must not throw

Private helper functions (without `export`) are not listed or runnable.

**Examples:**
```bash
# List available commands
maxon run

# Run a specific function (dashes are translated to underscores)
maxon run spec-test-selfhosted

# maxon build is equivalent to:
maxon run build
```

**Doc comments:** Each exported function may be preceded by one or more `///` doc-comment lines. Those lines are concatenated (joined with single spaces) and shown next to the command name when listing available commands via `maxon run`. Plain `//` comments are treated as in-source authoring notes and are NOT surfaced.

**Example `build.maxon`:**
```maxon
/// Compile the self-hosted compiler and run its spec tests.
export function spec_test_selfhosted() returns ExitCode
	print("Compiling...\n")
	let exe = try FilePath.from("bin/maxon.exe") otherwise return 2
	var argv = StringArray.create()
	argv.push("build")
	argv.push("maxon-selfhosted")
	let result = try Subprocess.run(.path(exe), arguments: argv, workingDirectory: Directory.currentPath(), timeoutMs: 120000) otherwise return 1
	if not result.succeeded() 'failed'
		return 1
	end 'failed'
	return 0
end 'spec_test_selfhosted'
```

---

### `maxon fmt`

Formats `.maxon` source files in-place.

**Usage:**
```bash
maxon fmt [file|directory]
```

**Arguments:**
- `[file|directory]` - Path to a source file or directory to format (default: current directory). When given a directory, formats all `.maxon` files recursively, skipping directories with `.maxonignore`.

**Examples:**
```bash
# Format all files in current directory
maxon fmt

# Format a single file
maxon fmt main.maxon

# Format a specific directory
maxon fmt src/
```

---

### `maxon test`

Runs a project's unit tests — every `test` declaration in its `*.test.maxon` files.

All discovered tests are compiled into ONE binary with a generated entry point, and which of them
run is a runtime argument. That is why changing `--filter` between runs does not recompile
anything, and why every re-run the harness needs (isolating a crash, attributing a leak) costs a
process rather than a build.

The project's own `main` needs no change: the test binary is compiled with a generated entry
instead, so `main` becomes unreachable and is dropped. The same directory still builds and runs
normally with `maxon build`.

**Usage:**
```bash
maxon test [directory] [options]
```

**Arguments:**
- `[directory]` - The project to test (default: current directory).

**Options:**

| Option | Description |
|--------|-------------|
| `-t PATTERN`, `-t=PATTERN`, `--filter=PATTERN` | Run only tests whose NAME or FILE path contains PATTERN (case-insensitive). Comma-separated patterns run a union. |
| `--list` | Print the tests that would run, and compile nothing |
| `--json` | Emit the report as JSON instead of text |
| `--isolate` | Run every test in its own process |
| `--bail[=N]` | Stop claiming new work after N failures (default 1) |
| `--workers=N` | Run N test processes at once (default `max(1, ProcessorCount - 2)`) |
| `--timeout=MS` | Kill a test process after MS milliseconds (default 5000). The deadline is per PROCESS, and one process runs one FILE's tests — so it bounds a file's whole run, not each test, unless `--isolate` is given |
| `--no-timing` | Omit durations, making stdout byte-reproducible |
| `--color=auto\|always\|never` | Colour; `auto` colours only when stdout is a terminal — not redirected, `NO_COLOR` unset, and `TERM` not `dumb` |
| `--target=ARCH-OS` | Compile the test binary for a specific target |
| `--log=CATEGORY:LEVEL` | Enable compiler logging (e.g. `codegen:trace`) |

**Working directory.** A test binary runs in the directory `maxon test` itself was invoked from — not
the project directory, and not the staging tree the tests were compiled out of. So a relative path in a
test body means the same thing it means in the shell you typed the command in.

**`--color=auto` degrades to `never` on targets with no terminal detection.** Asking the OS what kind of
object a handle is needs a host call, and only **x64-windows** makes it today (`GetFileType`). On every
other target — **arm64-macos**, **arm64-linux**, **x64-linux** and **wasm32-wasi** — the question is
answered "not a terminal", so `auto` prints no colour there however the report is being viewed.
`--color=always` is unaffected and is the way to get colour on those lanes.

The build is never refused for it: the answer degrades rather than the compile failing, because "not a
terminal" is a sound conservative answer where a missing file surface or a missing stdin would leave a
program with no answer at all. What each lane owes to answer it properly is one call, and the gap is
recorded per lane in `TargetFacilities.targetProvidesFacility`'s `terminalDetection` row — `isatty(1)`
on Darwin, `ioctl(1, TCGETS, ...)` on the libc-less Linux lanes, translated into the `FILE_TYPE_CHAR`
vocabulary `StdOp.osHandleFileType` speaks. wasm32-wasi is the one lane where it is not merely unwritten:
a WASI component cannot ask what is on the other end of its `output-stream` at all.

**Outcomes.** A test that does not pass is reported as one of five distinct states, because they
are found by different evidence and call for different action:

| State | Meaning |
|-------|---------|
| `FAIL` | The body threw — a failed assertion, or a foreign error the compiler reported |
| `CRASHED` | The test began and never ended: it took the process down (`panic` is uncatchable) |
| `TIMED OUT` | Still running when its process hit `--timeout`, and was killed |
| `DID NOT RUN` | Selected for a process that died before reaching it, and re-running made no progress. Never reported as a pass. |
| `LEAKED` | Held an allocation at exit (exit code 101), attributed by re-running the test alone |

`--json` reports the same outcomes under its own `state` vocabulary, which a machine reader
branches on. The two spellings are one map in the renderer, so they cannot describe different
states:

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

A zero-test run exits 1 on purpose: a silently-green suite that stopped containing tests is the
failure this command exists to prevent.

**A worked example — write a test, run it, read the failure.**

Two files in a directory. `pricing.maxon` is ordinary source; the tests go in a sibling whose name
ends `.test.maxon`, which is the only place a `test` declaration is allowed:

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

The `try` is not optional: a `test` implicitly declares `throws TestFailure`, so an assertion
without it is a compile error rather than an assertion whose failure nothing observes. There is no
`main` here and none is needed — `maxon test` compiles a generated entry point instead.

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
`Expect`'s `file` and `line` parameters default to `__file__` / `__line__`, which expand at the
call site. Correct the expectation to `2250` and the same command exits 0:

```text
$ maxon test pricing
pricing/pricing.test.maxon:
  ✓ a small order pays full price                    0.00ms
  ✓ ten items take the bulk discount                 0.00ms

 2 pass
 0 fail

 2 tests across 1 file.   compile 966ms, run 138ms
```

The second run still says `compile` rather than `cached` because the source changed. Editing only
the `--filter` between runs recompiles nothing: every discovered test is built into the binary and
which ones run is an argument.

**Examples:**
```bash
# Run every test in the current directory
maxon test

# Run one project's tests
maxon test src/parser

# Only tests whose name or file mentions "json"
maxon test -t json

# Two patterns, as a union
maxon test --filter=parser,lexer

# What would run, without compiling
maxon test --list

# Machine-readable, and reproducible byte for byte
maxon test --json --no-timing
```

**shv2 has this command too, and its report is byte-identical.** `maxon-shv2 test` accepts the
same flags and prints the same bytes — pinned by nine fixture projects under
`maxon-shv2/Testing/test-fixtures/`, whose `expected.txt` files are generated by THIS compiler's
`maxon test` and compared against shv2's to the byte, and by the tests in
`maxon-shv2/Testing/test-command/`, which are run under BOTH compilers so neither can be the only
witness to its own correctness.

⚠ **Two bounds are shv2's alone.** Subprocess spawning is `x64-windows` only
(`SubprocessRuntime.maxon`), so `maxon-shv2 test` runs there and `--target=` can compile a test
binary for another lane but not execute it. And `--color=auto` needs terminal detection, which not
every lane provides: where the host cannot answer, `auto` degrades to `never` rather than guessing.
`always` and `never` are exact everywhere.

---

### `maxon spec-test`

Runs the spec tests — the COMPILER's own suite. For a project's unit tests see `maxon test`.

**Usage:**
```bash
maxon spec-test [options]
```

**Options:**

| Option | Description |
|--------|-------------|
| `--filter=PATTERN` | Run only tests matching the pattern. Comma-separated terms run a union (any-of) — e.g. `--filter=basics,arrays,map`. Whitespace around each term is trimmed. |
| `--workers=N` | Use N worker threads (default: `max(1, ProcessorCount - 2)`) |
| `--update-required` | Force regeneration and update `RequiredIR` + `MmTrace` stderr blocks |
| `--verbose` | Show per-test PASS/FAIL timing logs |
| `--no-batch` | Disable per-spec compile batching (each test compiled individually) |

**Examples:**
```bash
# Run all tests
maxon spec-test

# Run tests matching a pattern
maxon spec-test --filter=array

# Run tests matching any of several patterns
maxon spec-test --filter=basics,arrays,map

# Run with verbose output
maxon spec-test --verbose

# Regenerate RequiredIR blocks
maxon spec-test --update-required

# Combine options
maxon spec-test --filter=string --verbose
```

---

### `maxon monitor`

Launches an executable with the shared-memory debug stream monitor. Reads binary trace events written via `--debugstream` and prints them to the terminal, each prefixed with `[+SSSS.mmm]`. The child's own stdout is forwarded unchanged, so trace lines are told apart by that prefix.

**Usage:**
```bash
maxon monitor [--filter=mm|sched|log] <exe> [args...]
```

**Options:**

| Option | Description |
|--------|-------------|
| `--filter=mm` | Memory-manager events only (`mm_alloc` / `mm_free` / `mm_incref` / …) |
| `--filter=sched` | Scheduler and green-thread events only |
| `--filter=log` | Only the events the program itself emitted via the `__DebugStream` builtin |

**Examples:**
```bash
# Build with debugstream enabled, then monitor
maxon build app.maxon --debugstream
maxon monitor app.exe

# Just the program's own trace events
maxon monitor --filter=log app.exe
```

**The `__DebugStream` builtin.** Every event family above except `log` is emitted by the runtime. `__DebugStream` is what lets *user Maxon source* put an event into the same ring — which is how a compiler written in Maxon stays debuggable once its work is spread over several workers and one stderr stops being readable.

| Call | Event | Notes |
|------|-------|-------|
| `__DebugStream.enabled()` | — | `true` when the ring is attached. Lets a caller skip building a message nothing would read. |
| `__DebugStream.nameId("phase")` | — | Interns a name **at compile time** into the executable's `MXDS_STRS` blob and yields its `u16`. The name never exists at runtime; the monitor prints it anyway. **The argument must be a string literal.** |
| `__DebugStream.phaseBegin(nameId, unitId)` | `LOG_PHASE_BEGIN` | Opens a nested, per-worker, per-unit span. |
| `__DebugStream.phaseEnd(nameId, unitId)` | `LOG_PHASE_END` | Closes it. |
| `__DebugStream.event(nameId, cat, lvl, unitId, arg0, arg1)` | `LOG_EVENT` | **Structured, zero-alloc.** An interned name plus two numbers — safe on a hot path, where a formatted message would allocate into the very `mm` stream a trace is being read to investigate. |
| `__DebugStream.text(cat, lvl, unitId, message)` | `LOG_TEXT` | A UTF-8 message, for the rare human line. Allocating (the caller built the string); truncated, never torn, at 64 KiB. |

Every Log event also carries the emitting green thread and its owning processor, so a run with several workers in flight can be demuxed back into one timeline per worker.

Two gates keep this free enough to leave in place:

- **Compile time** — without `--debugstream`, every call above emits **zero instructions**. Not a branch that is never taken: nothing at all.
- **Runtime** — with the ring detached, each call is a load of `__ds_base`, a test, and a not-taken branch. The bail is inline, before any `call`.

---

### `maxon lsp-server`

Starts the language server for IDE integration. Communicates over stdin/stdout using the Language Server Protocol. Normally launched automatically by the VS Code extension.

---

## Logging

All commands accept logging options to control diagnostic output:

| Option | Description |
|--------|-------------|
| `--log=LEVEL` | Set all log categories to the given level |
| `--log=CATEGORY:LEVEL` | Set a specific category to the given level |

**Log levels:** `none`, `error`, `info`, `debug`, `trace`

**Log categories:** `compiler`, `lexer`, `parser`, `semantic`, `hir`, `lir`, `optimizer`, `codegen`, `pe`, `testing`

**Testing log levels:**
- `info` — Show failures and summary only
- `debug` — Also show each passing test

**Examples:**
```bash
maxon spec-test --log=ir:debug
maxon build app.maxon --log=codegen:trace
```

---

## Project Structure

A Maxon project is a directory containing `.maxon` files. The `build.maxon` file serves as a script file with exported functions that can be run via `maxon run`.

### Basic Project

```
myproject/
├── build.maxon      # Script file with exported build/run functions
├── main.maxon       # Entry point (contains main function)
├── utils.maxon      # Utility functions
└── types.maxon      # Type definitions
```

### Project with Subdirectories

```
myproject/
├── build.maxon
├── main.maxon
├── lib/
│   ├── math.maxon
│   └── io.maxon
└── utils/
    └── helpers.maxon
```

All `.maxon` files in subdirectories are automatically included when compiling a directory with `maxon build`.

### Ignoring Directories

Place a `.maxonignore` file in any directory to exclude it and all its subdirectories from compilation, formatting, and LSP processing. The file is a flag — its contents are ignored.

```
myproject/
├── main.maxon
├── tests/
│   ├── .maxonignore     # This directory is skipped
│   └── test_data.maxon
└── src/
    └── app.maxon
```

### Rules

1. **`build.maxon` as script** - Contains exported functions runnable via `maxon run`
2. **Automatic discovery** - All `.maxon` files are found recursively when compiling a directory
3. **Standard library** - The stdlib is automatically included
4. **Export visibility** - Only `export function` declarations in `build.maxon` are listed and runnable

---

## Standard Library

The standard library is automatically loaded for all compilations. It includes:

- **Core functions**: `print`, `abs`, `sqrt`, `pow`, math functions
- **String operations**: `format_int`, `format_float`, string methods
- **Collections**: `Array`, `Map`, `Set`
- **Iteration**: `range`, iterator protocol

The stdlib is located in the `stdlib/` directory relative to the compiler.

---

## Namespace Resolution

When building multi-file projects, namespaces are derived from file paths:

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
var result = format(value)  // Finds utils.format if unique
```

### Export Visibility

Functions must be exported to be visible from other files:

```maxon
// utils.maxon
export function helper(x int) returns int
		return x * 2
end 'helper'

function internal(x int) returns int  // Not visible from other files
		return x + 1
end 'internal'
```

---

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Error (compilation failed, invalid arguments, etc.) |

`maxon test` splits that 1 in two, because CI has to tell a broken program from a harness that
never ran: 1 means a test failed (or none was found), 2 means the run could not happen. See
[`maxon test`](#maxon-test).

---

## Environment

### Standard Library Location

The compiler looks for the standard library in these locations (in order):
1. `stdlib/` relative to the compiler executable
2. `../stdlib/` relative to the compiler executable

### Working Directory

- `maxon build` - Output is relative to the source file/directory location
- `maxon run` - Runs from the current working directory (requires `build.maxon`)
- `maxon spec-test` - Runs from the current directory

---

## Common Workflows

### Developing a Single File

```bash
# Edit and build
maxon build program.maxon

# Run the result
./program.exe
```

### Developing a Project

```bash
# Navigate to project
cd myproject

# List available commands from build.maxon
maxon run

# Build the project
maxon build

# Run a specific task (dashes translate to underscores)
maxon run spec-test-selfhosted
```

### Running Tests During Development

```bash
# Run all tests
maxon spec-test

# Run specific tests
maxon spec-test --filter=optional

# Verbose output for debugging
maxon spec-test --filter=map --verbose
```

### Debugging Compilation Issues

```bash
# Emit IR for inspection
maxon build problem.maxon --emit-ir

# Emit IR at each pipeline stage
maxon build problem.maxon --dump-stages

```
