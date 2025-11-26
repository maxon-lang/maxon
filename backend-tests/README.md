# Backend Tests

Unit tests for the Maxon compiler backend implementation. Tests are ordered by complexity,
starting with the simplest features and building up incrementally.

## Test File Format

Each `.maxon` file contains a single test with expected outputs in header comments:

```maxon
// ExitCode: 42
// Stdout:
//   Line 1
//   Line 2
function main() int
    return 42
end 'main'
```

### Header Comments

- `// ExitCode: N` - Expected exit code (required)
- `// Stdout:` - Expected stdout output (optional, omit for empty)
  - Multi-line output uses indented continuation lines: `//   Line N`

### File Naming

Files are numbered with 3-digit prefixes for ordering:
- `001-return-zero.maxon` - Test 1
- `002-return-nonzero.maxon` - Test 2
- etc.

## Running Tests

```bash
# From project root
make backend-test
```

## Test Phases

| Phase | Tests | Description |
|-------|-------|-------------|
| 1 | 001-003 | Integer Constants & Return |
| 2 | 004-011 | Arithmetic Operators |
| 3 | 012-017 | Variables |
| 4 | 018-028 | Comparisons & Booleans |
| 5 | 029-037 | Control Flow |
| 6 | 038-044 | Functions & Calls |
| 7 | 045-053 | Floats |
| 8 | 054-062 | Arrays |
| 9 | 063-068 | Structs |
| 10 | 069-075 | Pointers & Advanced |

## Test Runner Behavior

1. Cleans all non-`.maxon` and non-`.md` files from this directory
2. Discovers and sorts `.maxon` files numerically
3. For each test:
   - Compiles with optimization (`-O`)
   - Compiles without optimization (debug)
   - Runs both executables
   - Verifies exit codes and stdout match expected
4. On success: deletes temp executables, continues to next test
5. On failure: generates diagnostic artifacts and stops
   - Recompiles with `-vvv --emit-ir`
   - Runs `llvm-objdump` on both executables
   - Saves all outputs for debugging

## Failure Artifacts

When a test fails, these files are created:
- `NNN-test-name.opt.exe` - Optimized executable
- `NNN-test-name.debug.exe` - Debug executable
- `NNN-test-name.opt.ll` - Optimized IR
- `NNN-test-name.debug.ll` - Debug IR
- `NNN-test-name.opt.objdump` - Optimized disassembly
- `NNN-test-name.debug.objdump` - Debug disassembly
- `NNN-test-name.compile-error.txt` - Verbose compile output (if compilation failed)
