# Maxon Language Specification Format

This document describes the format for Maxon language specification files.

## Overview

Each language feature must have a spec file in the `specs/` directory that serves as the single source of truth. Spec files contain:

1. **YAML Frontmatter** - Metadata about the feature
2. **Developer Notes** - Implementation details for maintainers
3. **Documentation** - User-facing documentation (extracted to HTML)
4. **Tests** - Test cases (extracted to language-tests/fragments/)

## Spec File Structure

```markdown
---
feature: feature-name
status: stable|experimental|deprecated
keywords: [keyword1, keyword2, ...]
category: category-name
---

## Developer Notes

Implementation details, AST nodes, codegen notes, etc.

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
function main() int
    var x = 10
    return x
end 'main'
```
```exitcode
10
```

Optionally include stdout output:

```maxon
function main() int
    print(42)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```

### Executable Examples (Compile Errors)

For code that demonstrates compile/parse errors:

```maxon
function main() int
    var x = "not a number"
    return x + 5
end 'main'
```
```maxoncstderr
In file 'temp\temp_fragment.maxon':
Type mismatch: cannot perform arithmetic on string
  Location: line 3, column 12
```

## Code Block Rules

1. **`text` blocks** are for non-executable sample code
   - NOT extracted as test fragments
   - Used for syntax examples and snippets
   - No output block needed

2. **`maxon` blocks** must be followed by EITHER:
   - `` `exitcode `` + optional `` `stdout `` (for successful execution)
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
   for i in range(0, 5) 'loop'
       print(i)
   end 'loop'
   ```
   ```
   
   **Note**: Small code snippets or pseudo-code that should not be executed (like desugared examples or intermediate representations) should be marked as ` ```text ` instead of ` ```maxon ` to prevent extraction.

2. **Executable examples** - With `output` block, extracted as tests
   ```markdown
   ```maxon
   function main() int
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

```markdown
<!-- test: basic-example -->
```maxon
function main() int
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

## Developer Notes

The `abs` function is implemented as an LLVM intrinsic.

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
function main() int
    var x = abs(-5.0)
    return x as int
end 'main'
```
```exitcode
5
```

## Tests

<!-- test: abs.float -->
```maxon
function main() int
    var x = abs(-5.5)
    return x as int
end 'main'
```
```exitcode
5
```

<!-- test: abs.zero -->
```maxon
function main() int
    return abs(0.0) as int
end 'main'
```
```exitcode
0
```
```

## Workflow

### Adding a New Feature

1. Create `specs/feature-name.md` with frontmatter, notes, docs, and tests
2. Run `maxon extract-specs` to extract test fragments
3. Run `maxon regen-fragments` to generate IR (preserves expected results)
4. Implement the feature until tests pass
5. Run `make docs` to generate HTML documentation

### Modifying a Feature

1. Edit the spec file (update code and/or expected results)
2. Run `make test` (auto-extracts, regenerates IR, runs tests)
3. Update implementation if needed

### Important Commands

- `maxon extract-specs` - Extract test fragments from specs (includes expected results)
- `maxon regen-fragments` - Regenerate IR only (preserves metadata from specs)
- `make test` - Full test cycle (extract, regen, run)
- `make docs` - Generate HTML documentation
- `make validate-specs` - Check for orphaned fragments

## Categories

Available categories for the `category` field:

- `stdlib` - Standard Library
- `math-intrinsic` - Math Intrinsics
- `operators` - Operators
- `control-flow` - Control Flow
- `types` - Types
- `type-system` - Type System
- `diagnostics` - Diagnostics
- `declaration` - Declarations
- `statements` - Statements
- `expressions` - Expressions
- `functions` - Functions
- `namespaces` - Namespaces
- `organization` - Organization
- `compilation` - Compilation
- `optimization` - Optimization
- `interop` - Interoperability
- `literals` - Literals
- `uncategorized` - Uncategorized

## Status Values

- `stable` - Fully implemented and tested
- `experimental` - In development, API may change
- `deprecated` - Will be removed in future versions
