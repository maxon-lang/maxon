# Maxon Language Specification Format

This document describes the format for Maxon language specification files.

## Overview

Each language feature must have a spec file in the `specs/` directory that serves as the single source of truth. Spec files contain:

1. **YAML Frontmatter** - Metadata about the feature
2. **Documentation** - User-facing prose and examples, read by people; nothing extracts it
3. **Tests** - Test cases, each a program the harness compiles and checks. The cases whose subject is the
   emitted code live in the sibling directory `ir-specs/`, which `maxon spec-test ir-specs` runs.

## Spec File Structure

```markdown
---
feature: feature-name
status: stable|experimental|deprecated
keywords: [keyword1, keyword2, ...]
category: category-name
---


## Documentation

User-facing documentation with examples.

## Tests

Executable test cases.
```

## Code Block Format

### Non-Executable Sample Code

For code snippets that demonstrate syntax but are NOT meant to be executed as tests:

```text
var x = abs(-5.5)  // Shows syntax only
var y = 10 + 20    // Not extracted as a test
```

Use `` `text `` blocks for examples that don't need to compile or run.

### Executable Examples (Success)

For code that should compile and run successfully:

```maxon
function main() returns ExitCode
		var x = 10
		return x
end 'main'
```
```exitcode
10
```

Optionally include stdout and/or stderr output:

```maxon
function main() returns ExitCode
		print("42")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```

#### Per-target stdout

When a program's stdout differs by target (e.g. `FilePath` prints `\` on
x64-windows but `/` on wasm32-wasi), use target-qualified `Stdout:<target>`
blocks instead of the bare `stdout` block. The runner picks the block matching
the target under test; a bare `stdout` block, if also present, is the fallback
for targets without a qualified block. `Maxoncstderr:<target>` and
`Stderr:<target>` are the other two fences that take this qualifier.

```Stdout:x64-windows
C:\Users\test
```
```Stdout:wasm32-wasi
C:/Users/test
```

Runtime stderr (e.g., panic messages with stack traces) can be verified with a `stderr` block:

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)

function dangerous(value Integer) returns Byte
	return value as Byte
end

function main() returns ExitCode
	return dangerous(300)
end
```
```exitcode
1
```
```stderr
panic at example.test:6: Range check failed: value outside typealias 'Byte'
Stack trace:
  in dangerous
  in main
  in mrt_start
```

### Pinning emitted code

The fences the harness reads are the `*Fence` constants in `maxon-bin/Testing/SpecParser.maxon`. A case
that opens any other tagged fence, such as ` ```RequiredIR `, is refused, naming the case and the fence.

Three things pin what the compiler emits:

- **A `TargetIr:<target>` block** holds the Target IR the compiler renders for the case on that lane: every
  function the case's own source declares, plus the bodies a `RequiredRuntime` block names. It is a gate on
  the lane it names. When the run's lane has a block, a compile that renders different text fails the case
  and the failure shows the first differing line; only the block for the run's own lane is compared. The
  fence is always qualified, and the target is one of `x64-windows`, `x64-linux`, `arm64-macos` or `arm64-linux`: an
  unqualified fence, or `wasm32-wasi`, which renders no Target IR, is refused, naming the case. A pin is
  compared against the COMPILE, so a lane this host cannot run (`--target=arm64-macos` on Windows) still
  checks it. A case carrying any `TargetIr` block always compiles and runs on its own. The cases that pin
  emitted code live in `ir-specs/`.
- **A `RequiredRuntime` block**, which opts a body the Target IR would otherwise withhold — an emitted
  runtime function, or a `stdlib/` body the program reaches — into the rendering, one name per line.
  `ir-specs/emitted-runtime-body.md` is the subject and the canonical example. It says nothing about the
  run, so it does not satisfy `pinsAnyResult` — a case carrying it still owes an exit code, a stdout or a
  stderr block.
- **A `tests/` case over `--emit-ir` or `--emit-ir-runtime=<name>` output**, which spawns the compiler at
  a fixture and reads the printed IR itself. This is where an emitted-code property that must go RED
  belongs; `tests/emitted-runtime/` is the corpus, and `tests/README.md` states what each one costs.

`--update-required --filter=<spec>/` re-mints the `TargetIr:<lane>` block of this run's lane in the spec
file, in place: it replaces a case's block, or adds one after the case's last block. A spec that holds any
`TargetIr` block is an IR spec, and every selected case of it that passed (or compiled, on a lane this
host cannot run) gets a block; a spec gains blocks only once it holds one.

### Rdata Verification

To verify the exact contents of the `.rdata` section in the compiled PE executable, include a `RequiredRdata` block. This performs an exact match — the concatenated bytes from the typed values must equal the full `.rdata` section contents (trailing zero-padding from PE alignment is trimmed before comparison).

Each line is a typed value:

- `f64 3.14` — 8 bytes, IEEE 754 little-endian double
- `i64 42` — 8 bytes, little-endian int64
- `i64[] 10, 20, 30` — N×8 bytes, consecutive little-endian int64s
- `utf8 "hello world\0"` — variable length, UTF-8 encoded (supports `\0`, `\n`, `\t`, `\\`)

Example:

```maxon
function main() returns ExitCode
		var x = 3.14
		if x == 3.14 'check'
				return 1
		end 'check' else 'other'
				return 0
		end 'other'
end 'main'
```
```exitcode
1
```
```RequiredRdata
f64 3.14
```

The `RequiredRdata` block is optional. When present, the test compiles the source to an executable, reads the `.rdata` PE section, and compares it byte-for-byte against the expected values.

### Data Section Verification

To verify the `.data` section (mutable globals), include a `RequiredData` block. Same format as `RequiredRdata`, with additional types:

- `i8 1` — 1 byte, signed int8
- `i16 256` — 2 bytes, little-endian int16
- `i32 42` — 4 bytes, little-endian int32
- `f32 1.5` — 4 bytes, IEEE 754 little-endian float
- `pad 7` — N zero bytes (alignment padding)

Example:

```maxon
var flag = true
var counter = 42

function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```
```RequiredData
i64 42
i8 1
```

Note: Globals are sorted largest-first in the data section to minimize alignment padding, so the i64 appears before the i8 regardless of source order.

### Executable Examples (Compile Errors)

For code that demonstrates compile/parse errors:

```maxon
function main() returns ExitCode
		var x = "not a number"
		return x + 5
end 'main'
```
```maxoncstderr
In file 'temp\temp_fragment.maxon':
Type mismatch: cannot perform arithmetic on string
  Location: line 3, column 12
```

### Multi-File Tests

To test cross-file behavior (e.g., export visibility, multi-file builds), use `// --- file: name.maxon` markers inside a single `maxon` code block:

```maxon
// --- file: helper.maxon
export function helper() returns ExitCode
		return 42
end 'helper'

// --- file: main.maxon
function main() returns ExitCode
		return helper()
end 'main'
```
```exitcode
42
```

When `// --- file:` markers are present, each section is written to a separate temporary file during compilation. The files are compiled together as a multi-file project. Error messages in `maxoncstderr` blocks name the location as `<fragment>:line:column`, whichever file it is in:

```maxon
// --- file: helper.maxon
function privateHelper() returns ExitCode
		return 99
end 'privateHelper'

// --- file: main.maxon
function main() returns ExitCode
		return privateHelper()
end 'main'
```
```maxoncstderr
error E3008: <fragment>:2:10: function 'privateHelper' is not exported
```

When no `// --- file:` markers are present, behavior is unchanged (single-file test).

## Code Block Rules

1. **`text` blocks** are for non-executable sample code
   - NOT extracted as test fragments
   - Used for syntax examples and snippets
   - No output block needed

2. **`maxon` blocks** must be followed by EITHER:
   - `` `exitcode `` + optional `` `stdout `` and/or `` `stderr `` (for successful execution)
   - `` `mm-trace `` or `` `log-trace `` (debug-stream trace assertion; see below)
   - `` `maxoncstderr `` (for compile/parse errors)

3. **In the Documentation section:** nothing is extracted and nothing is compiled. The runnable region
   begins at the `## Tests` heading, so a `maxon` block above it needs no output block and is never a gate.

4. **In Tests section:**
   - Use `<!-- test: test-name -->` comment before each test
   - Test name is prefixed with spec filename (e.g., `abs.float`)
   - ALL `maxon` blocks in Tests MUST have output blocks

## Documentation Examples

Every code example in the **Documentation** section is illustrative: the parser's runnable region starts at
the `## Tests` heading, so a ` ```maxon ` block above it is never extracted, compiled or run, and it needs
no expected-output block.

⚠ **A DOCUMENTATION EXAMPLE IS UNVERIFIED PROSE.** It rots exactly as a comment does, and no gate reports
it. An example a reader is meant to be able to trust belongs under `## Tests`, where the harness runs it.

Prefer ` ```text ` over ` ```maxon ` for pseudo-code, desugarings and intermediate representations, so a
reader can tell at a glance what is not a program.

## Test Section

The **Tests** region runs from the `## Tests` heading to the end of the file, and every case in it runs. A
case runs from its marker to the next test marker or `## ` heading. Each test needs:

- **Test marker**: `<!-- test: test-name -->`
- **Maxon code block**: The source code
- **Output block**: Expected results

Optional per-test directives go between the test marker and the case's first fence, one directive per
line:

| Directive | Effect |
|-----------|--------|
| `<!-- Args: ... -->` | The argv the compiled program is spawned with, space-separated; a double-quoted run is one argument and may be empty (`""`). Capital `A`, matched exactly |
| `<!-- unsupported-targets: t1, t2 -->` | Exclude the case from the named targets (`x64-windows`, `wasm32-wasi`, …; comma-separated); it runs on every other target. A missing or blank marker excludes nothing; a key naming no supported target, or a list naming every one, is a parse failure |
| `<!-- targets: ... -->` | Retired. The parser refuses it: nothing reads it, so a case carrying it would run everywhere. Spell the lanes that cannot serve the case with `unsupported-targets:` instead |
| `<!-- MmTrace -->` | Enable mm-trace capture mode (see below): the program is built with `--debugstream`, run under `maxon monitor --filter=mm`, and its normalized trace compared to the ` ```mm-trace ` block. Equivalent to adding the block |
| `<!-- LogTrace -->` | The same capture mode for the other family of the debug stream — the events the program writes through `__DebugStream` — compared to a ` ```log-trace ` block. A case selects one family, never both |
| `<!-- AsyncTrace -->` | Compile with `--async-trace` and compare stderr after both sides are normalized for the green-thread trace |
| `<!-- DebugInfo -->` | Compile this case with debug info on, the way `maxon build` does; every other case compiles with it off |
| `<!-- network: live -->` | The case opens a socket to a real external host. It is left out of a default run and runs only under `--network`; `live` is the only value |
| `<!-- procs: N -->` | Run the program with `MAXON_MAX_PROCS=N` in its environment, pinning the scheduler's processor count; `N` is decimal digits with an optional leading `+`, from 1 to 2147483647, the same values the scheduler honours |
| `<!-- preempt: off -->` | Run the program with `MAXON_PREEMPT=off` in its environment, so the monitor takes no processor from the thread holding it; `off` is the only value |
| `<!-- stdin: hold -->` / `<!-- stdin: delayed -->` | Give the program a stdin that blocks. `hold` is a pipe nobody ever writes to, so a read blocks for the program's whole life; `delayed` writes one line about a second after the program's first stdout byte and closes, so the read blocks and then completes. Without the marker stdin is the null device and every read answers at once with EOF |
| `<!-- runs: alone -->` | Run the case's program with no other case's program running beside it: the harness runs a spec's `alone` cases as a job of their own, after every other job has finished, and starts nothing else while it runs. It is for a program whose peak memory is set by a runtime limit, such as a green thread's stack grown to its 1 GiB maximum; `alone` is the only value |
| `<!-- process: own -->` | Keep the case out of every batch program: it compiles and runs in a program of its own. It is for a case that observes state the whole process shares, which no check of its source can see (a named resource an earlier case still holds, a per-process counter). `runs: alone` also changes when the case is scheduled; `own` changes only which process it runs in, and is the only value |

A case holds one block of each kind: one ` ```maxon ` program, one ` ```exitcode `, one each of the portable
` ```stdout `, ` ```stderr ` and ` ```maxoncstderr `, one target-qualified block per target, and one
` ```mm-trace ` or ` ```log-trace ` block. ` ```RequiredData `, ` ```RequiredRdata ` and
` ```RequiredRuntime ` blocks accumulate. A block closes on a line holding only ` ``` `.

Prose blocks, a bare ` ``` ` fence or ` ```text `, are skipped whole wherever they sit in the region, so
a marker or fence quoted inside one reads as prose.

The harness reads a spec strictly. A claim a spec file makes is honoured or refused, and each refusal is a
parse failure naming the file and line. It refuses:

- a test marker above `## Tests`, or one with no `-->` of its own;
- a `<!-- … -->` comment line in the region that no directive claims, inside a case or outside any (a
  misspelling, or a marker newer than the parser); the failure lists the directives above;
- two directives on one line, a directive repeated in one case, or a directive written after the case's
  first fence;
- a valued directive with an unrecognized value. The four valueless flags match by their whole body, so
  `<!-- DebugInfos -->` is an unread comment line;
- a tagged fence outside any case other than ` ```text `, a case with no ` ```maxon ` block, and a case
  with no result block;
- a second block of a kind the case holds one of;
- a target-qualified fence naming no supported target;
- a block the file ends inside, and a tagged fence opened inside a block;
- `<!-- MmTrace -->` and `<!-- LogTrace -->` on one case, or a trace marker and a trace block naming
  different families.

### mm-trace blocks

An ` ```mm-trace ` block asserts a program's memory-management behavior — heap
allocations, reference-count transitions, and frees. A test enters **mm-trace
capture mode** when it carries either a `<!-- MmTrace -->` directive or an
` ```mm-trace ` block; in that mode the harness compiles the program with the
binary debug-event stream enabled, runs it under `maxon monitor --filter=mm`,
and compares the decoded, normalized trace against the block:

````markdown
<!-- test: heap-alloc-free -->
```maxon
function main() returns ExitCode
	let s = "value {n}"
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```mm-trace
mm_alloc String #1 size=16
mm_incref String #1 rc=1
mm_decref String #1 rc=0
mm_free String #1
```
````

The trace is normalized so the blocks are stable across runs and machines:
timestamps and depth indentation are stripped, and allocation ids (`#<id>`) are
densely renumbered `1, 2, 3, …` by first appearance. Regenerate the block with
`--update-required`, which writes it over the case's existing block, or after the case's last line when
it has none, in the file's own line ending. The run refuses to write into a spec file that changed after
it was parsed.

**Event order is not normalized, and is not stable once a second green thread
produces events.** The debug stream is one shared ring carrying no sequence
number and no thread id, so with two producers the decoded order is the order
they took the lock rather than the order the program ran — and the block is
compared by exact string equality. A case that traces concurrent work must
therefore pin `<!-- procs: 1 -->`: one processor is one producer, which makes
ring order program order and the committed block the only order the program
can produce. An ` ```exitcode ` block may accompany it (checked against
the monitor's returned child exit code); an ` ```stdout ` block, if present, is
checked via a separate untraced run since the monitor interleaves trace lines
with the child's own stdout. mm-trace assertions are enforced by the spec
runner.

## Example: Complete Spec File

```markdown
---
feature: abs
status: stable
keywords: [abs, absolute value, math]
category: math-intrinsic
---

## Documentation

# abs

Calculate the absolute value of a number.

**Signature:** `abs(x float) float`

**Example (non-executable syntax):**

```text
var x = -5.5
var y = abs(x)  // Returns 5.5
```

**Example (illustrative — the Documentation section runs nothing):**

```maxon
function main() returns ExitCode
		var x = abs(-5.0)
		return trunc(x)
end 'main'
```

## Tests

<!-- test: abs.float -->
```maxon
function main() returns ExitCode
		var x = abs(-5.5)
		return trunc(x)
end 'main'
```
```exitcode
5
```

<!-- test: abs.zero -->
```maxon
function main() returns ExitCode
		return trunc(abs(0.0))
end 'main'
```
```exitcode
0
```
```

## Workflow

### Staged files and diagnostic paths

A case compiles from a scratch directory under `<spec dir>/.spec-tmp/`, which `.gitignore` covers. A
```maxoncstderr block names the case's file as `<spec directory>/<spec>/<test>.maxon:<line>:<column>` — for
a case in `specs/arrays.md` named `push`, `specs/arrays/push.maxon:3:5` — or by the placeholder
`<fragment>:3:5`. Whichever spelling an expectation uses, the harness rewrites it and the compiler's
output to one form before comparing, so the path decides nothing. A multi-file case keeps the directory
prefix of each file ahead of the name (`app/specs/arrays/push.maxon:10:13`).

### Batched runs

A default run compiles a spec's plain run cases into the fewest programs whose declared names do not
overlap and runs each program once; `--batch=off` compiles and runs every case on its own. A nominal type
name (`type`, `enum`, `union`, `interface`) is whole-program, and so is an extension method of one
signature on one type, so two cases declaring the same name land in different programs, and so do a case
declaring a nominal type and another case that names that word (a `typealias` of it, or the library's own type
of that name), and a case declaring a `typealias` and another that names the word without declaring it. The
declared and mentioned names come from the compiler's own front end, one pass per case
(`Compiler.claimsOfSources`). Each batched case
sits in its own directory, so its file-private names stay its own, with its `main` renamed to an exported
`__spec_case_<n>`, and a generated entry runs the cases selected by `--select=`
between begin and end markers carrying each case's exit code. After each case's `main` returns and before
its end marker, the entry calls `__Builtins.gtQuiesce()`, which waits for every service and coroutine the
case left running, so their output lands between the markers, and the end marker carries the count that is
still outstanding. On wasm32-wasi the entry skips that call and the end marker carries 0. A case's stdout and
stderr are the bytes between its markers. A case whose end marker is missing or reports work still
outstanding (a service the runtime owns for the whole program, such as the default log sink, counts), and
every case of a program that exited non-zero (a panic, a crash, a leak at exit 101 or 75), runs
again ALONE from the same binary with `--select=<n>`, and its verdict comes from that run. A case a program's
compile refuses is dropped from the program so the rest still run, and compiled alone: when it builds, the
case FAILS with the batch compile's diagnostic and a message saying the batching checks missed it, which is
a batching gap the runner must close; when it does not build alone either (a target refusal such as E3104,
or a real error), its own compile decides it as for any case. A program that does not compile at all is
treated the same way for every case it held. Output outside the markers belongs to no single case: a
`static` or module-level initializer runs before `main` for every case of a program, so what it prints
lands ahead of the first marker, and a `--select=` rerun of the same binary prints it too. A program whose
run left such output FAILS every one of its cases, and so does a case whose alone rerun died before the
entry began, each with a message naming the cause: a case ran code before `main` that the batching checks
did not catch, and the runner must keep it out of batches. A batched case
that FAILS is compiled and run alone to diagnose the failure: when it passes alone the case FAILS with a
message saying it passes alone but fails batched, which is a batching gap (a missing check, or a case
that needs `<!-- process: own -->`); when it fails alone too, the solo failure is the verdict. The case
counts as run alone either way, and a batched PASS stands. A target with no command line (wasm32-wasi)
cannot read `--select=`, so its entry runs every case, and a case whose output cannot be read there is
diagnosed the same way.

These cases always compile and run on their own: a compile-error case; a case pinning `stderr`; an `Args`
marker; a `stdin`, `procs`, `preempt`, `network`, `process: own` or `runs: alone` marker; an `MmTrace`, `LogTrace`,
`AsyncTrace` or `DebugInfo` marker; a stdlib-overlay or runtime-file section; a multi-file case;
`__file__` or `__line__` in the source; a `TargetIr`, `RequiredRuntime`, `RequiredData` or
`RequiredRdata` block; a top-level `export` or `module` declaration, which a multi-file program refuses
when no other file uses it (E3092, E3094); and a `main` that is not exactly
`function main() returns ExitCode` or `function main()` with one `end 'main'`. So does a case whose source
names one of these words anywhere, a `typealias` included: `CommandLine` or `executablePath`, which read
the batch program's own argv and path; `__Builtins`, whose runtime counters earlier cases move;
`__DebugStream`, whose name-id table is numbered across the whole program; and `Log`, `Logger`, `LogSink`
or `TraceCapture`, which set state the whole process shares. A case declaring any name the standard library
also declares (an enum such as `StringError`, an alias such as `ByteArray`, an interface such as
`InitableFromStringLiteral`) runs alone too, because every other case's use of that name would change meaning.

So does a case whose own module-level `var`, `let` or `static` initializers run program code, which the
compiler's front end answers before any batch is built (`ModuleInitNeed.runsProgramCode`): such an initializer
runs before `main` for every case of a program, so its effects would belong to no case. A case the front end
refuses is placed alone, and its own compile reports why.

The line under `N passed, M failed` counts them: `N run case(s) ran batched in M program(s); K ran alone`.

#### Commands

```bash
# Run only tests matching a pattern
./maxon-bin/.maxon/maxon.exe spec-test --filter=arithmetic

# Run several specs in one run: every case any pattern selects
./maxon-bin/.maxon/maxon.exe spec-test --filter=arithmetic/ --filter=tuples/

# Run every case on its own
./maxon-bin/.maxon/maxon.exe spec-test --batch=off --filter=arithmetic/

# The Target IR suite, on this host's lane and on another lane's compile
./maxon-bin/.maxon/maxon.exe spec-test ir-specs
./maxon-bin/.maxon/maxon.exe spec-test ir-specs --target=arm64-macos

# Re-mint the inline blocks (trace captures, TargetIr pins) of the matching cases
./maxon-bin/.maxon/maxon.exe spec-test ir-specs --update-required --filter=static-variables/
```

#### Adding Tests

1. Create or edit `specs/<feature-name>.md`.
2. Run `spec-test --filter=<feature-name>`.
3. Implement until tests pass. A case that pins emitted code belongs in `ir-specs/<feature-name>.md`: write
   its program and result blocks, add a `TargetIr:<lane>` block for the lane you run (any content), and
   run `spec-test ir-specs --update-required --filter=<feature-name>/` on each lane.
