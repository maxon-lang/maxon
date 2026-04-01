# Maxon CLI Reference

This document covers the Maxon command-line interface and project system.

---

## Quick Reference

| Command | Description |
|---------|-------------|
| `maxon build [file\|directory]` | Compile a file, directory, or project (default: current directory) |
| `maxon run <function>` | Run an exported function from `build.maxon` |
| `maxon test` | Run spec fragment tests |

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
| `--emit-ir` | Emit IR output to `<source>.ir` |
| `--dump-stages` | Write IR at each pipeline stage |

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
maxon run [function]
```

**Arguments:**
- `[function]` - Name of an exported function in `build.maxon` (optional). If omitted, lists all available exported functions.

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

**Example `build.maxon`:**
```maxon
// Compile the self-hosted compiler and run its spec tests
export function spec_test_selfhosted() returns ExitCode
	print("Compiling...\n")
	let result = Process.execute("bin/maxon.exe build maxon-selfhosted", timeoutMs: 120000)
	if result != 0 'failed'
		return 1
	end 'failed'
	return 0
end 'spec_test_selfhosted'
```

---

### `maxon test`

Runs the spec fragment tests from `maxon-bin/specs/fragments/`.

**Usage:**
```bash
maxon test [options]
```

**Options:**

| Option | Description |
|--------|-------------|
| `--filter <pattern>` | Run only tests matching the pattern |
| `--verbose` | Show detailed output for each test |

**Examples:**
```bash
# Run all tests
maxon test

# Run tests matching a pattern
maxon test --filter "array"

# Run with verbose output
maxon test --verbose

# Combine options
maxon test --filter "string" --verbose
```

**Output:**
- Shows pass/fail status for each test
- Displays summary with total passed, failed, and skipped
- Shows elapsed time

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

---

## Environment

### Standard Library Location

The compiler looks for the standard library in these locations (in order):
1. `stdlib/` relative to the compiler executable
2. `../stdlib/` relative to the compiler executable

### Working Directory

- `maxon build` - Output is relative to the source file/directory location
- `maxon run` - Runs from the current working directory (requires `build.maxon`)
- `maxon test` - Runs from the `maxon-bin/` directory

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
# From maxon-bin directory
cd maxon-bin

# Run all tests
./bin/maxon test

# Run specific tests
./bin/maxon test --filter "optional"

# Verbose output for debugging
./bin/maxon test --filter "map" --verbose
```

### Debugging Compilation Issues

```bash
# Emit IR for inspection
maxon build problem.maxon --emit-ir

# Emit IR at each pipeline stage
maxon build problem.maxon --dump-stages

```
