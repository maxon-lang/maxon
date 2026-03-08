---
name: fix-spec-tests
description: Run spec tests and fix any failures in the compiler
---

Run the spec tests and fix any failures by modifying the compiler code.

## Steps
1. Run the spec tests: `./maxon-selfhosted/main.exe spec-test`
2. Analyze the output to identify which tests are failing and why.
3. Fix the compiler code in `maxon-selfhosted/` to make the failing tests pass. If new features need to be implemented you
can use the maxon-sharp compiler for reference.
4. Rebuild and re-run spec tests to verify the fixes.
5. Repeat until all tests pass.
6. Review all code changes:
    - Eliminate duplicated code — refactor shared logic into helper methods.
    - Ensure no `switch` or 'match' silently ignore unhandled cases — add a default case that throws an error for unexpected inputs.
    - Ensure no `else` clauses silently catch unhandled conditions — throw errors for unexpected inputs.
    - Ensure functions that handle multiple cases, for example a series of 'if' statements, but return 
      a default value for unhandled cases, should be refactored to throw an error instead. This ensures that all cases are handled explicitly and prevents silent failures.
    - Ensure comments explain "why" not "what".
    - Fix any problems reported by the IDE
    - Ensure you have not duplicated any helpers
    - typealias should describe its purpose, not its type
    - typed ranges should be as specific as possible, e.g. `int(0 to 100)` instead of `int(0 to u64.max)`. Carefully consider the valid range for each type and use the narrowest possible range to catch errors. Max range is fine if there is no clear limit.
    - look for any comments that imply that something was skippped or not fully implemented or should be done later
    - Fix any compiler warnings
7. Write a git commit message for these changes

## Guidelines
- Read the relevant spec file in `specs/` to understand what the expected behavior is.
- Use `--log=CATEGORY:LEVEL` to get more detail when debugging (e.g., `--log=mlir:debug`).
- For memory issues try compiling with "--mm-trace"
- Fix root causes, not symptoms. No workarounds.
- Any old 3 digit error codes (ie E022) in the spec files need to updated to the new 4 digit error codes.
- It is possible that any bugs encountered could be in the c# bootrap compiler. If this is the case then you will need to fix the c# compiler in `maxon-sharp/`
- exit code 101 means memory leak detected
