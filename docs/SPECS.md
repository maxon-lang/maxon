# Maxon Language Specification Format

This document describes the format for Maxon language specification files.

## Overview

Each language feature must have a spec file in the `specs/` directory that serves as the single source of truth. Spec files contain:

1. **YAML Frontmatter** - Metadata about the feature
2. **Documentation** - User-facing prose and examples, read by people; nothing extracts it
3. **Tests** - Test cases, generated into per-target fragment trees
   (`specs/fragments/<target>/<spec>/<test>.test`)

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
for targets without a qualified block. This mirrors the `RequiredIR:<target>`
mechanism.

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

### IR Verification

To verify the compiler's IR at all pipeline stages, include a `RequiredIR` block. The block contains all stages concatenated, separated by `=== stagename` markers. The test will fail if the generated IR doesn't match exactly (after whitespace normalization).

Current pipeline stages: `maxon`, `standard`, `x86`.

```maxon
function main() returns ExitCode
		return 42
end 'main'
```
```exitcode
42
```
```RequiredIR
=== maxon
module {
  func @main() -> i64 {
  entry:
    %0 = maxon.constant {value = 42 : i64}
    maxon.return %0
  }
}
=== standard
module {
  func @main() -> i64 {
  entry:
    %1 = arith.constant {value = 42 : i64}
    func.return %1
  }
}
=== x86
module {
  func @main() -> i64 {
  entry:
    x86.push rbp
    x86.mov rbp, rsp
    x86.mov eax, 42
    x86.pop rbp
    x86.ret
  }
}
```

The `RequiredIR` block is optional. When present, the entire block is compared as one string against the generated IR from all pipeline stages.

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

When `// --- file:` markers are present, each section is written to a separate temporary file during compilation. The files are compiled together as a multi-file project. Error messages in `maxoncstderr` blocks use just the filename (not the full path):

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
error E3008: main.maxon:2:10: function 'privateHelper' is not exported
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

Tests in the **Tests** section are always extracted. Each test needs:

- **Test marker**: `<!-- test: test-name -->`
- **Maxon code block**: The source code
- **Output block**: Expected results

Optional per-test directives go between the test marker and the maxon block:

| Directive | Effect |
|-----------|--------|
| `<!-- disabled-test: test-name -->` | In place of the test marker, not beside it: a shelved case the compiler cannot yet pass. It is parsed and counted but never run; flip the marker to `test:` to revive it |
| `<!-- Args: ... -->` | The argv the compiled program is spawned with, space-separated; a double-quoted run is one argument and may be empty (`""`). Capital `A`, matched exactly |
| `<!-- unsupported-targets: t1, t2 -->` | Exclude the case from the named targets (`x64-windows`, `wasm32-wasi`, …; comma-separated); it runs on every other target. A missing or blank marker excludes nothing; a key naming no supported target, or a list naming every one, is a parse failure |
| `<!-- targets: ... -->` | Retired. The parser refuses it: nothing reads it, so a case carrying it would run everywhere. Spell the lanes that cannot serve the case with `unsupported-targets:` instead |
| `<!-- MmTrace -->` | Enable mm-trace capture mode (see below): the program is built with `--debugstream`, run under `maxon monitor --filter=mm`, and its normalized trace compared to the ` ```mm-trace ` block. Equivalent to adding the block |
| `<!-- LogTrace -->` | The same capture mode for the other family of the debug stream — the events the program writes through `__DebugStream` — compared to a ` ```log-trace ` block. A case selects one family, never both |
| `<!-- AsyncTrace -->` | Compile with `--async-trace` and compare stderr after both sides are normalized for the green-thread trace |
| `<!-- DebugInfo -->` | Compile this case with debug info on, the way `maxon build` does; every other case compiles with it off |
| `<!-- network: live -->` | The case opens a socket to a real external host. It is left out of a default run and runs only under `--network`; `live` is the only value |
| `<!-- procs: N -->` | Run the program with `MAXON_MAX_PROCS=N` in its environment, pinning the scheduler's processor count; `N` is a positive decimal |
| `<!-- preempt: off -->` | Run the program with `MAXON_PREEMPT=off` in its environment, so the monitor takes no processor from the thread holding it; `off` is the only value |
| `<!-- stdin: hold -->` / `<!-- stdin: delayed -->` | Give the program a stdin that blocks. `hold` is a pipe nobody ever writes to, so a read blocks for the program's whole life; `delayed` writes one line about a second after the program's first stdout byte and closes, so the read blocks and then completes. Without the marker stdin is the null device and every read answers at once with EOF |
| `<!-- runs: alone -->` | Run the case's program with no other case's program running beside it: the harness runs a spec's `alone` cases as a job of their own, after every other job has finished, and starts nothing else while it runs. It is for a program whose peak memory is set by a runtime limit, such as a green thread's stack grown to its 1 GiB maximum; `alone` is the only value |

A valued marker refuses an unrecognized value rather than reading it as the default, and a `<!-- … -->`
comment line inside a case that no directive above claims (a misspelling, or a marker newer than the
parser) is a parse failure naming the line and this roster — a claim a spec file makes must be honoured
or reported, never silently declined. The four valueless flags match by their whole body, so
`<!-- DebugInfos -->` is refused rather than read as `DebugInfo`.

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

The trace is normalized so goldens are stable across runs and machines:
timestamps and depth indentation are stripped, and allocation ids (`#<id>`) are
densely renumbered `1, 2, 3, …` by first appearance. Regenerate the golden with
`--update-required`. An ` ```exitcode ` block may accompany it (checked against
the monitor's returned child exit code); an ` ```stdout ` block, if present, is
checked via a separate untraced run since the monitor interleaves trace lines
with the child's own stdout. mm-trace assertions are enforced by the spec
runner.

```markdown
<!-- test: basic-example -->
```maxon
function main() returns ExitCode
		return 0
end 'main'
```
```output
ExitCode: 0
```
```

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

### Test Fragment Files

Test fragment files — the goldens — are written by the test runner and stored under `specs/fragments/<target>/<spec-name>/<test-name>.test` (e.g. `specs/fragments/x64-windows/arithmetic/addition.test`). The target is the run's effective target, never the host: a golden records the code that was generated. They are machine-maintained — edit the spec file, not the fragment — but agents reading them should understand the format so they don't confuse a golden with an expectation.

A golden is reference, not a gate. A run mints one for a case that has none, on the host where that case passed, and never rewrites one that exists: a committed golden whose bytes differ from this run's compile is reported as drift, and the case keeps the verdict its assertions earned. Only `--update-required` (with a `--filter`) rewrites a committed golden. A golden for a target only another host can run is minted there; CI fails a lane that leaves untracked goldens and uploads them as an artifact to unpack at the repository root.

#### Fragment Format

A fragment records the case's source, what the program was given, and what the compiler produced. A run case:

```
// Test: <test-name>
<maxon source>
---
Args: <argv, only when the case names any>
ExitCode: <N, or `unpinned` when the case pins only a stream>
---
<the Target IR the compiler generated>
```

A compiler-error case has no IR; the diagnostic the compiler actually produced takes its place, with every compiled file's path rewritten to the stable `<fragment>` token:

```
// Test: <test-name>
<maxon source>
---
CompilerError:
<normalized compiler stderr>
```

The `// Test:` header is exactly one line, and the source section is byte-for-byte the file the compiler was handed — so a `line:col` in the diagnostic reads directly against it. Pinned stdout/stderr and `RequiredIR` blocks are not in the fragment: they are test inputs, checked by the run, and a golden only tracks the code.

#### Example (run case)

```
// Test: addition
function main() returns ExitCode
	return 10 + 5
end 'main'
---
ExitCode: 15
---
data {
  ...
}
```

#### Example (compiler-error case)

```
// Test: error.duplicate-typealias-same-file
typealias Score = int(0 to 100)
typealias Score = int(0 to 200)

function main() returns ExitCode
	return 0
end 'main'
---
CompilerError:
error E3061: <fragment>:3:11: Duplicate typealias 'Score'
```

#### Commands

```bash
# Run only tests matching a pattern; a case with no golden mints one, a drifted golden is reported
./maxon-bin/.maxon/maxon.exe spec-test --filter=arithmetic

# Rewrite the committed goldens and RequiredIR blocks of the matching cases
./maxon-bin/.maxon/maxon.exe spec-test --update-required --filter=arithmetic
```

#### Adding Tests

1. Create or edit `specs/<feature-name>.md`.
2. Run `spec-test --filter=<feature-name>` — a passing case with no golden mints one.
3. Implement until tests pass. Never edit fragments directly; edit the spec file and let the runner mint or, under `--update-required`, rewrite.
