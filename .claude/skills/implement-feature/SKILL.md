---
name: implement-feature
description: Implement a new language feature in the Maxon compiler using spec-driven development
---

Implement a new language feature in the Maxon compiler following spec-driven development.

The user will describe the feature they want. You will create the spec, write the tests, and implement the compiler changes to make them pass.

By default spec tests will only show the name of failing tests, but you can use `--verbose` to show detailed failure messages for failing tests which can help with debugging. Use --filter when working on a specific failing test.

## Steps

### Phase 1: Research & Design

1. Run the `maxon-coder` skill to load Maxon syntax rules. Refer to `docs/LANGUAGE_REFERENCE.md` and `docs/QUICK_REFERENCE.md` for full details.
2. Read `docs/SPECS.md` to understand the spec file format.
3. Look at existing spec files in `specs/` for similar features to use as reference.
4. Explore the compiler code in `maxon-sharp/Compiler/` to understand how similar features are implemented across the pipeline stages (parsing, AST-to-Maxon, Maxon-to-Standard, Standard-to-X86, code emission).
5. Present a design plan to the user covering:
   - Proposed syntax and semantics
   - Which compiler pipeline stages need changes
   - Which files will be modified or created
   - Test cases to include in the spec

### Phase 2: Write the Spec

6. Create a spec file at `specs/<feature-name>.md` with:
   - YAML frontmatter (feature, status, keywords, category)
   - Documentation section with syntax explanation and examples
   - Tests section with test cases covering:
     - Basic/happy path usage
     - Edge cases
     - Error cases (compile errors with `maxoncstderr` blocks)
   - Start with all tests in the Tests section marked as `disabled-test` (e.g., `<!-- disabled-test: feature.basic -->`)
7. Run `./bin/maxon.exe spec-test --filter=<feature-name>` to verify the spec file parses correctly and fragments are extracted.

### Phase 3: Implement Incrementally

Use an agent to implement the feature incrementally, test by test:

8. Enable the first test by removing `disabled-` from its marker comment.
9. Build the C# compiler: `dotnet build` (run from `maxon-sharp/`). The output binary is at `./bin/maxon.exe`.
10. Run the spec tests: `./bin/maxon.exe spec-test --filter=<feature-name>`
11. Analyze failures and implement the necessary compiler changes across the pipeline:
    - **Lexer** (`maxon-sharp/Compiler/Lexer/`) - New tokens if needed
    - **Parser** (`maxon-sharp/Compiler/Parser/`) - New grammar rules
    - **AST** (`maxon-sharp/Compiler/AST/`) - New AST nodes
    - **AstToMaxonDialect** (`maxon-sharp/Compiler/IR/Conversion/`) - AST to Maxon dialect lowering
    - **MaxonToStandard** (`maxon-sharp/Compiler/IR/Conversion/`) - Maxon to Standard dialect lowering
    - **StandardToX86** (`maxon-sharp/Compiler/IR/Conversion/`) - Standard to X86 dialect lowering
    - **Code Emission** (`maxon-sharp/Compiler/IR/Emit/`) - X86 machine code emission
    - **Semantic Analysis** (`maxon-sharp/Compiler/Semantic/`) - Type checking, validation
12. Rebuild and re-run spec tests to verify the fix.
13. Repeat steps 8-12 for each remaining disabled test until all tests pass.

### Phase 4: Validation & Cleanup

14. Run the full spec test suite: `./bin/maxon.exe spec-test` to ensure no regressions. Don't investigate if test failures were pre-existing, just fix them.
15. If any tests from other specs broke, investigate and fix.
16. Review all code changes:
    - Eliminate duplicated code — refactor shared logic into helper methods.
    - Ensure no `switch` statements use `default` cases — all cases must be handled explicitly.
    - Ensure no `else` clauses silently catch unhandled conditions — throw errors for unexpected inputs.
    - Ensure functions that handle multiple cases, for example a series of 'if' statements, but return 
      a default value for unhandled cases, should be refactored to throw an error instead. This ensures that all cases are handled explicitly and prevents silent failures.
    - Ensure comments explain "why" not "what".
    - Fix any problems reported by the IDE
    - Ensure you have not duplicated any helpers
    - typealias should describe its purpose, not its type
    - typed ranges should be as specific as possible, e.g. `int(0 to 100)` instead of `int(0 to u64.max)`. Carefully consider the valid range for each type and use the narrowest possible range to catch errors. Max range is fine if there is no clear limit.
    - Fix any compiler warnings
17. Update documentation, including `LANGUAGE_REFERENCE.md` and `STDLIB_REFERENCE.md` and `QUICK_REFERENCE.md` and `BNF_SYNTAX.md` if necessary.
18. Write a git commit message

## Guidelines

- Read existing spec files and compiler code for similar features to follow established patterns.
- Use `--log=CATEGORY:LEVEL` for debugging (e.g., `--log=ir:debug`, `--log=codegen:trace`).
- Fix root causes, not symptoms. No workarounds.
- When adding new operations or types to IR dialects, follow the existing naming conventions.
- Test error cases too — the compiler should produce clear error messages for invalid usage.
- Any old 3-digit error codes (e.g., E022) in spec files need to be updated to the new 4-digit error codes.
- If the feature requires new runtime functions, add them in `X86CodeEmitter.Runtime.cs`.
- Keep the x86 code generation correct — watch for short jump overflow (max +/-127 bytes) and 32-bit register truncation (image base is above 4GB).
- If you find an issue, fix it properly. It doesn't matter if it is pre-existing.
- If any tests that use RequiredIR fail you can regenerate the required IR and MmTrace stderr by using `--update-required`
