---
name: implement-phase
description: Implement a phase of the ROADMAP.md plan
---

Implement the specific phase of the ROADMAP.md plan.

## Steps

1. Add the spec files specified in the ROADMAP.md for this phase to the whitelist in runAllSpecTests.
2. Run the spec tests: `main.exe spec-test`
3. Analyze failures and implement the necessary compiler changes
4. Rebuild and re-run spec tests to verify the fix.
5. Repeat steps 2-4 until all spec tests pass.
6. Review all code changes:
    - Eliminate duplicated code — refactor shared logic into helper methods.
    - Ensure no `match` statements use default cases — all cases must be handled explicitly.
    - Ensure no `else` clauses silently catch unhandled conditions — throw errors for unexpected inputs.
    - Ensure functions that handle multiple cases, for example a series of 'if' statements, but return 
      a default value for unhandled cases, should be refactored to throw an error instead. This ensures that all cases are handled explicitly and prevents silent failures.
    - Ensure comments explain "why" not "what".
    - Fix any problems reported by the IDE
    - Ensure you have not duplicated any helpers
    - typealias should describe its purpose, not its type
    - typed ranges should be as specific as possible, e.g. `int(0 to 100)` instead of `int(0 to u64.max)`. Carefully consider the valid range for each type and use the narrowest possible range to catch errors. Max range is fine if there is no clear limit.
8. Write a git commit message for these changes.

## Guidelines

- Read existing spec files and compiler code for similar features to follow established patterns.
- Use `--log=CATEGORY:LEVEL` for debugging (e.g., `--log=mlir:debug`, `--log=codegen:trace`).
- Fix root causes, not symptoms. No workarounds.
- When adding new operations or types to MLIR dialects, follow the existing naming conventions.
- Test error cases too — the compiler should produce clear error messages for invalid usage.
- Any old 3-digit error codes (e.g., E022) in spec files need to be updated to the new 4-digit error codes.
- If the feature requires new runtime functions, add them in `X86CodeEmitter.Runtime.cs`.
- If you find an issue, fix it properly. It doesn't matter if it is pre-existing.
- If any tests that use RequiredMLIR fail you can regenerate the required MLIR by using `--update-requiredmlir`
