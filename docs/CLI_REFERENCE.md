# Maxon CLI Reference

This document covers the Maxon command-line interface and project system.

---

## Quick Reference

| Command | Description |
|---------|-------------|
| `maxon build [file\|directory]` | Compile a file, directory, or project (default: current directory) |
| `maxon run [function]` | Run an exported function from `build.maxon`; lists commands if omitted |
| `maxon fmt [file\|directory]` | Format `.maxon` source files in-place (default: current directory) |
| `maxon spec-test [options]` | Run spec tests |
| `maxon monitor <exe> [args...]` | Launch executable with shared-memory debug stream monitor |
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
| `--target=ARCH-OS` | Set compilation target (default: `x64-windows`). Examples: `x64-windows`, `arm64-macos`, `x64-linux` |
| `--emit-ir` | Write `.ir` file |
| `--dump-stages` | Write IR at each pipeline stage (`.1-maxon.ir`, etc.) |
| `--mm-trace` | Enable runtime memory manager trace output (stderr) |
| `--mm-debug` | Enable runtime memory debug checks (magic, canary, poison) |
| `--async-trace` | Enable async/await runtime trace output (stderr) |
| `--debugstream` | Enable shared-memory debug stream (use with `maxon monitor`) |
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

### `maxon spec-test`

Runs the spec tests.

**Usage:**
```bash
maxon spec-test [options]
```

**Options:**

| Option | Description |
|--------|-------------|
| `--filter=PATTERN` | Run only tests matching the pattern. Comma-separated terms run a union (any-of) — e.g. `--filter=basics,arrays,map`. Whitespace around each term is trimmed. |
| `--workers=N` | Use N worker threads (default: `ProcessorCount - 2`) |
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

Launches an executable with the shared-memory debug stream monitor. Reads debug output written via `--debugstream` and prints it to the terminal.

**Usage:**
```bash
maxon monitor <exe> [args...]
```

**Examples:**
```bash
# Build with debugstream enabled, then monitor
maxon build app.maxon --debugstream
maxon monitor app.exe
```

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
