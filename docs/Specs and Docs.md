# Maxon Specification Files

Spec files in `specs/` are the single source of truth for language features. Each spec combines developer notes, user-facing documentation, and executable tests in one Markdown file.

## Spec File Structure

```markdown
---
feature: feature-name
status: stable|draft|experimental
keywords: [keyword1, keyword2]
category: control-flow|declaration|etc
---

# Feature Name

## Developer Notes
Implementation details, architecture decisions, file locations, data structures.
For developers working on the compiler.

## Documentation
User-facing documentation with examples.
Generated into HTML in `maxon-docs/Output/` via `make docs`.

## Tests
Executable test cases that verify the feature works correctly.
Extracted to `language-tests/fragments/` and run via `make test`.
```

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

## Workflow

### Adding a New Feature

1. **Create spec**: `specs/feature-name.md` with YAML frontmatter, notes, docs, tests
2. **Extract tests**: `maxon extract-specs` - Creates `.test` files in `language-tests/fragments/`
3. **Generate IR**: `maxon regen-fragments` - Compiles code and captures optimized/unoptimized LLVM IR
4. **Implement**: Update lexer/parser/codegen until `make test` passes
5. **Generate docs**: `make docs` - Creates HTML from Documentation section

### Modifying a Feature

1. **Edit spec**: Update `specs/feature-name.md` (code and/or expected results)
2. **Run tests**: `make test` - Automatically extracts specs, regenerates IR, runs tests
3. **Update implementation**: Fix compiler if tests fail

### Complete Workflow

```bash
make fragments  # Extract specs + regen IR + run tests
make docs       # Generate HTML documentation
```

## Test Fragment Format

Extracted `.test` files contain:

```
// Test: feature-name.test-name.1
<maxon source code>
---
<optimized LLVM IR>
---
<unoptimized LLVM IR>
---
ExitCode: <number>
Stdout: <output>
Stderr: <errors>
MaxoncStderr: <compiler errors>
OptimizedInstructionCount: <count>
UnoptimizedInstructionCount: <count>
```

## Key Commands

- `maxon extract-specs` - Extract test fragments from specs (preserves metadata)
- `maxon regen-fragments` - Regenerate LLVM IR sections only (preserves metadata)
- `maxon test-fragments` - Run all fragment tests
- `make fragments` - Full workflow: extract + regen + test
- `make validate-specs` - Check for orphaned fragments
- `make docs` - Generate HTML documentation

## Validation

`make validate-specs` ensures:
- All fragments in `language-tests/fragments/` come from spec files
- No orphaned test files exist

## Important Notes

- **Specs are authoritative**: Test expected values are defined in spec files
- **IR is generated**: Never manually edit LLVM IR in `.test` files
- **Metadata preserved**: `regen-fragments` keeps ExitCode, Stdout, etc. from specs
- **No temp files**: Use `maxon <file>` or pipe code to compile and run without artifacts
- **Optimizations enabled by default**: Both `maxon <file>` and piped input compile with optimizations enabled
