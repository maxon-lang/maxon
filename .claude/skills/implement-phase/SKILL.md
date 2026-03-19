---
name: implement-phase
description: Implement a phase of the ROADMAP.md plan
---

Implement the specific phase of the ROADMAP.md plan.

By default spec tests will only show the name of failing tests, but you can use `--verbose` to show detailed failure messages for failing tests which can help with debugging. Use --filter when working on a specific failing test.

## Steps

1. Add the spec files specified in the ROADMAP.md for this phase to the whitelist in runAllSpecTests.
2. Run the spec tests: `main.exe spec-test`
3. Read maxon-selfhosted\ARCHITECTURE.md for an overview of the compiler architecture and how to navigate the codebase. This will help you understand where to find the relevant code for the phase you are implementing.
4. Analyze failures and implement the necessary compiler changes
5. Rebuild and re-run spec tests to verify the fix.
6. Repeat steps 2-4 until all spec tests pass.
7. Review all code changes:
    - Eliminate duplicated code — refactor shared logic into helper methods.
    - Ensure you have not duplicated any helpers
    - Ensure no `else` clauses silently catch unhandled conditions — throw errors for unexpected inputs.
    - Ensure functions that handle multiple cases, for example a series of 'if' statements, but return 
      a default value for unhandled cases, should be refactored to throw an error instead. This ensures that all cases are handled explicitly and prevents silent failures.
    - Ensure comments explain "why" not "what".
    - typealias should describe its purpose, not its type
    - typed ranges should be as specific as possible, e.g. `int(0 to 100)` instead of `int(0 to u64.max)`. Carefully consider the valid range for each type and use the narrowest possible range to catch errors. Max range is fine if there is no clear limit (ie line numbers)
8. Write a git commit message for these changes.

## Guidelines
- Read existing spec files and compiler code for similar features to follow established patterns.
- Use `--log=CATEGORY:LEVEL` for debugging (e.g., `--log=mlir:debug`, `--log=codegen:trace`).
- Fix root causes, not symptoms. No workarounds.
- If you find an issue, fix it properly. It doesn't matter if it is pre-existing.
- If any tests that use RequiredMLIR fail you can regenerate the required MLIR and MmTrace stderr by using `--update-required`
