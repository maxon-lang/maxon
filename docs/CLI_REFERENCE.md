# Maxon CLI Reference

This document covers the Maxon command-line interface and project system.

---

## Quick Reference

| Command | Description |
|---------|-------------|
| `maxon compile <file>` | Compile a single source file |
| `maxon build` | Build a project from the current directory |
| `maxon test` | Run spec fragment tests |

---

## Commands

### `maxon compile`

Compiles a single Maxon source file to an executable.

**Usage:**
```bash
maxon compile <source.maxon> [options]
```

**Arguments:**
- `<source.maxon>` - Path to the source file (required)

**Options:**

| Option | Description |
|--------|-------------|
| `--debug` | Enable debug output (default) |
| `--no-debug` | Disable debug output |
| `--track-allocs` | Enable runtime allocation tracking |

**Output:**
- Creates `<source>.exe` in the same directory as the source file
- For `.maxon` files: `foo.maxon` → `foo.exe`
- For `.test` files: `foo.test` → `foo.exe`

**Examples:**
```bash
# Basic compilation
maxon compile hello.maxon

# Compile with allocation tracking
maxon compile app.maxon --track-allocs

# Compile without debug info
maxon compile app.maxon --no-debug
```

---

### `maxon build`

Builds a multi-file project from the current directory. Automatically discovers all `.maxon` files and compiles them together.

**Usage:**
```bash
maxon build [options]
```

**Options:**

| Option | Description |
|--------|-------------|
| `--debug` | Enable debug output (default) |
| `--no-debug` | Disable debug output |
| `--track-allocs` | Enable runtime allocation tracking |

**Behavior:**
1. Scans the current directory recursively for `.maxon` files
2. Finds the file containing the `main()` function
3. Compiles all files together with the standard library
4. Creates `<directory-name>.exe` in the current directory

**Output:**
- Executable named after the current directory
- Example: Running `maxon build` in `myproject/` creates `myproject.exe`

**Examples:**
```bash
# Build project in current directory
cd myproject
maxon build

# Build with allocation tracking
maxon build --track-allocs
```

**Error Conditions:**
- `No .maxon files found` - No source files in current directory
- `No main() function found` - None of the files contain a main function
- `Multiple main() functions found` - More than one file has a main function

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
| `-j <N>`, `--jobs <N>` | Run tests with N parallel jobs |

**Examples:**
```bash
# Run all tests
maxon test

# Run tests matching a pattern
maxon test --filter "array"

# Run with verbose output
maxon test --verbose

# Run with 4 parallel jobs
maxon test -j 4
maxon test --jobs 4

# Combine options
maxon test --filter "string" --verbose -j 8
```

**Output:**
- Shows pass/fail status for each test
- Displays summary with total passed, failed, and skipped
- Shows elapsed time

---

## Project Structure

A Maxon project is simply a directory containing `.maxon` files. The `maxon build` command automatically discovers and compiles all source files.

### Basic Project

```
myproject/
├── main.maxon       # Entry point (contains main function)
├── utils.maxon      # Utility functions
└── types.maxon      # Type definitions
```

### Project with Subdirectories

```
myproject/
├── main.maxon
├── lib/
│   ├── math.maxon
│   └── io.maxon
└── utils/
    └── helpers.maxon
```

All `.maxon` files in subdirectories are automatically included when running `maxon build`.

### Rules

1. **One main function** - Exactly one file must contain a `main()` function
2. **Automatic discovery** - All `.maxon` files are found recursively
3. **Standard library** - The stdlib is automatically included
4. **Output naming** - The executable is named after the directory

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

- `maxon compile` - Output is relative to the source file location
- `maxon build` - Output is in the current working directory
- `maxon test` - Runs from the `maxon-bin/` directory

---

## Common Workflows

### Developing a Single File

```bash
# Edit and compile
maxon compile program.maxon

# Run the result
./program.exe
```

### Developing a Project

```bash
# Navigate to project
cd myproject

# Build the project
maxon build

# Run the result
./myproject.exe
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
# Enable debug output (shows IR, codegen details)
maxon compile problem.maxon --debug

# Check for memory issues
maxon compile app.maxon --track-allocs
```
