# Maxon Language Specification Format

This document describes the format for Maxon language specification files.

## Overview

Each language feature must have a spec file in the `specs/` directory that serves as the single source of truth. Spec files contain:

1. **YAML Frontmatter** - Metadata about the feature
2. **Documentation** - User-facing documentation (extracted to HTML)
3. **Tests** - Test cases (extracted to language-tests/fragments/)

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

Runtime stderr (e.g., panic messages with stack traces) can be verified with a `stderr` block:

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)

function dangerous(value Integer) returns Byte
	return Byte{value}
end

function main() returns ExitCode
	let result = dangerous(Integer{300})
	return 0 as ExitCode
end
```
```exitcode
1
```
```stderr
panic at example.test:6: Range check failed for type 'Byte': value outside int(0 to 255)
Stack trace:
  in example.dangerous
  in example.main
  in _start
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
export function helper() returns int
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
function privateHelper() returns int
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
   - `` `maxoncstderr `` (for compile/parse errors)

3. **In Documentation section:**
   - Examples WITHOUT `function main()` don't need output blocks
   - Examples WITH `function main()` MUST have output blocks
   - These are auto-named as `doc-example-1`, `doc-example-2`, etc.

4. **In Tests section:**
   - Use `<!-- test: test-name -->` comment before each test
   - Test name is prefixed with spec filename (e.g., `abs.float`)
   - ALL `maxon` blocks in Tests MUST have output blocks

## Documentation Examples

Code examples in the **Documentation** section can be:

1. **Illustrative only** - No `output` block, not extracted as tests
   ```markdown
   ```maxon
	 for i in numbers 'loop'
			 print(i)
	 end 'loop'
   ```
   ```
   
   **Note**: Small code snippets or pseudo-code that should not be executed (like desugared examples or intermediate representations) should be marked as ` ```text ` instead of ` ```maxon ` to prevent extraction.

2. **Executable examples** - With `output` block, extracted as tests
   ```markdown
   ```maxon
	 function main() returns ExitCode
			 return 42
	 end 'main'
   ```
   ```output
   ExitCode: 42
   ```
   ```

Examples with `output` blocks are automatically numbered as `feature-name.doc-example-N.1` tests.

## Test Section

Tests in the **Tests** section are always extracted. Each test needs:

- **Test marker**: `<!-- test: test-name -->`
- **Maxon code block**: The source code
- **Output block**: Expected results

Optional per-test directives go between the test marker and the maxon block:

| Directive | Effect |
|-----------|--------|
| `<!-- Args: ... -->` | Pass the listed arguments to the test executable |
| `<!-- MmTrace -->` | Enable memory-manager trace output |
| `<!-- AsyncTrace -->` | Enable async-runtime trace output |
| `<!-- IncludeStdlibIr -->` | Include reachable stdlib functions in the captured CompiledIR snapshot |

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

**Example (executable):**

```maxon
function main() returns ExitCode
		var x = abs(-5.0)
		return trunc(x)
end 'main'
```
```exitcode
5
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
		return abs(0.0) as int
end 'main'
```
```exitcode
0
```
```

## Workflow

### Test Fragment Files

Test fragment files are auto-generated from spec files by the test runner and stored under `specs/fragments-<target>/<spec-name>/<test-name>.test` (e.g. `specs/fragments-x64-windows/arithmetic/addition.test`). They are machine-maintained — edit the spec file, not the fragment — but agents reading them should understand the format so they don't confuse expected IR with captured IR.

#### Fragment Format

A fragment has four parts separated by `---` lines:

```
// Test: <test-name>
<maxon source>
---
<expectations>
---
// CompiledIR
<raw IR captured from the compiler at fragment-generation time>
---
```

**Section 1 — source.** The test's Maxon source, with a `// Test: <name>` header.

**Section 2 — expectations.** Any combination of these keys, in any order:

| Key | Meaning |
|-----|---------|
| `ExitCode: N` | Expected process exit code |
| `Args: ...` | Command-line arguments to pass to the test executable |
| `MmTrace: true` / `AsyncTrace: true` | Enable runtime trace output |
| `IncludeStdlibIr: true` | Include reachable stdlib functions in the captured CompiledIR snapshot |
| `` Stdout: ``` `` / `` Stderr: ``` `` | Expected runtime stdout/stderr (fenced multiline block) |
| `` RequiredIR: ``` `` | Expected compiler IR to verify, pinned (fenced multiline block) |
| `` RequiredRdata: ``` `` / `` RequiredData: ``` `` | Expected .rdata / .data section contents |
| `` MaxoncStderr: ``` `` | Expected compiler error output — used only for compiler-error tests |

**Section 3 — compiled IR snapshot.** When non-empty, section 3 **must** begin with the literal header line `// CompiledIR` on its own, followed by the raw IR captured from the compiler when the fragment was generated. This is a snapshot of the compiler's actual output at generation time, **not** an expectation — the test runner does not verify section 3 during runs. Its job is purely to make the compiler's output diff-reviewable in git.

Section 3 can legitimately be empty (no `// CompiledIR` header, no content) in two cases:
1. The expectation section sets `RequiredIR:` — the expected IR is already pinned in section 2, so the snapshot is redundant and skipped.
2. The test is a compiler-error test (has `MaxoncStderr:`) — there is no IR to capture.

The `// CompiledIR` header exists to make the asymmetry with `RequiredIR` visually obvious at a glance: `RequiredIR` is the test's **input** (a pinned assertion in section 2), while `CompiledIR` is the compiler's **output** (a captured snapshot in section 3). If you see unlabeled IR in a fragment, the fragment is in a stale format and must be regenerated — the parser will reject it.

#### Example (success test with IR capture)

```
// Test: addition
function main() returns ExitCode
	return 10 + 5
end 'main'
---
ExitCode: 15
---
// CompiledIR
module {
  func @main() -> u8 {
  entry:
    x64.mov r8d, 15
    x64.ret
  }
}
---
```

#### Example (compiler-error test)

```
// Test: invalid-syntax
function main() returns ExitCode
	return @@@
end 'main'
---
MaxoncStderr: ```
error E1234: unexpected token '@'
```
---
---
```

Section 3 is empty — no header, no content — because there is no captured IR for a test that fails to compile.

#### Commands

```bash
# Run all C# spec tests (auto-regenerates stale fragments)
./bin/maxon.exe spec-test

# Run only tests matching a pattern
./bin/maxon.exe spec-test --filter=arithmetic

# Force regeneration of fragments and RequiredIR blocks in spec files
./bin/maxon.exe spec-test --update-required

# Run the self-hosted runner (whitelisted specs only)
./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test
```

#### Adding Tests

1. Create or edit `specs/<feature-name>.md`.
2. Run `spec-test` — fragments are auto-generated on the next run.
3. Implement until tests pass. Never edit fragments directly; edit the spec file and let the runner regenerate.
