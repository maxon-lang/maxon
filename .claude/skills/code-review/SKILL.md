---
name: code-review
description: Conduct a code review of the recent changes in the project, ensuring code quality and consistency.
---

Review the changes that have been made in the project.

## Steps

1. Review all code changes:
    - Eliminate duplicated code — refactor shared logic into helper methods.
    - Ensure no `switch` or 'match' statements use `default` cases — all cases must be handled explicitly.
    - Ensure no `else` clauses silently catch unhandled conditions — throw errors for unexpected inputs.
    - Ensure functions that handle multiple cases, for example a series of 'if' statements, but return 
      a default value for unhandled cases, should be refactored to throw an error instead. This ensures that all cases are handled explicitly and prevents silent failures.
    - Ensure comments explain "why" not "what".
    - Fix any problems reported by the IDE
    - typealias should describe its purpose, not its type
    - typed ranges should be as specific as possible, e.g. `int(0 to 100)` instead of `int(0 to u64.max)`. Carefully consider the valid range for each type and use the narrowest possible range to catch errors. Max range is fine if there is no clear limit.
    - look for any comments that imply that something was skippped or not fully implemented or should be done later
    - Fix any compiler warnings
2. Update documentation, including `LANGUAGE_REFERENCE.md` and `QUICK_REFERENCE.md` and `BNF_SYNTAX.md` if necessary.
3. Rebuild and run spec tests if you made any changes to the codebase, and ensure all tests pass.
4. Write a git commit message for these changes.

## Guidelines

- Keep the x86 code generation correct — watch for short jump overflow (max +/-127 bytes) and 32-bit register truncation (image base is above 4GB).
