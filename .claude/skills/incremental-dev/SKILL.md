---
name: incremental-dev
description: Implement spec tests one at a time
---

We are implementing new features by enabling tests one by one until they pass.
Run the spec tests and fix any failures by modifying the compiler code.

## Steps

1. Find the next disabled test in this spec file (labeled with 'disabled-test') and enable it by removing 'disabled-'.
2. Run the spec tests: `maxon.exe spec-test --update-requiredmlir`
3. Analyze the output to identify which tests are failing and why.
4. Fix the compiler code in `maxon-sharp/` to make the failing tests pass.
5. Rebuild and re-run spec tests to verify the fixes.
6. Repeat until all tests pass.
7. Fix any problems reported by the IDE
8. review the X86 MLIR for that test and ensure registers are handled correctly.
9. If any changes occured to the required MLIR of other tests in register-allocator.md then those changes need to be review to ensure they are ok.
10. Review all code changes:
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
11. Write a git commit message for these changes.

## Guidelines

- Read the relevant spec file in `specs/` to understand what the expected behavior is.
- Use `--log=CATEGORY:LEVEL` to get more detail when debugging (e.g., `--log=mlir:debug`).
- Fix root causes, not symptoms. No workarounds.
- Any old 3 digit error codes (ie E022) in the spec files need to updated to the new 4 digit error codes.
