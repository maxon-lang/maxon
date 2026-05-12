---
name: code-review
description: Conduct a code review of the recent changes in the project, ensuring code quality and consistency.
---

Review the changes that have been made in the project to the c# and self hosted compilers. If you find any issues that 
are outside the current changes fix them as well. The goal is to ensure that the codebase maintains high quality and consistency, and that any issues are addressed before merging the changes.

Create a task list to perform these steps.

## Steps

0. Read `docs/WRITING_MAXON_CODE.md`
1. Review all code changes:
    - Any changes to target specific code (e.g., x64) should have an equivalent change in all
      target specific code (e.g., arm64) if applicable.
    - Eliminate duplicated code — refactor shared logic into helper methods.
    - Ensure no `else` clauses silently catch unhandled conditions — throw errors for unexpected inputs.
    - Ensure functions that handle multiple cases, for example a series of 'if' statements, but return 
      a default value for unhandled cases, should be refactored to throw an error instead. This ensures that all cases are handled explicitly and prevents silent failures.
    - Ensure comments explain "why" not "what".
    - Fix any problems reported by the IDE
    - typealias should describe its purpose, not its type
    - typed ranges should be as specific as possible, e.g. `int(0 to 100)` instead of `int(0 to u64.max)`. Carefully consider the valid range for each type and use the narrowest possible range to catch errors. Max range is fine if there is no clear limit.
    - look for any comments that imply that something was skippped or not fully implemented or should be done later
    - Fix any compiler warnings
    - If a `match` has multiple cases with the same result try to consolidate them into a single case with a range
    - Add blank lines to improve code readability, especially around control flow statements and between logical sections of code.
    - Remove functions that are just a thin wrapper around another function call
    - the "try/otherwise panic" structure should be used to handle errors that should never happen
    - function should not return empty strings or -1 or other "empty" values if they cannot return a valid value — they should throw an error instead. This prevents silent failures and makes it easier to identify and fix issues.
2. Update documentation, including `LANGUAGE_REFERENCE.md` and `STDLIB_REFERENCE.md` and `QUICK_REFERENCE.md` and `BNF_SYNTAX.md` if necessary.
3. Rebuild and run spec tests if you made any changes to the codebase, and ensure all tests pass. Do the C# tests first, then the self-hosted tests.

    ### Building the compilers

    **C# compiler:**
    ```
    dotnet build
    ```
    Run from `maxon-sharp/`. Output binary: `./bin/maxon.exe`.

    **Self-hosted compiler:**
    First ensure the C# compiler is built, then:
    ```
    ./bin/maxon.exe build maxon-selfhosted
    ```
    Output binary: `./maxon-selfhosted/.maxon/maxon-selfhosted.exe`.

    ### Running spec tests

    **C# compiler:** `./bin/maxon.exe spec-test`

    **Self-hosted compiler:** `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test`

    Do NOT use `dotnet run` — it recompiles every time. Use the pre-built binaries directly.

    Useful flags:
    - `--filter=PATTERN` — run only tests matching a pattern
    - `--verbose` — show detailed failure messages
    - `--target=ARCH-OS` — test a specific target (e.g., `x64-windows`, `arm64-macos`); self-hosted compiler only

    If any changes touch both compilers, run spec tests for both. Also run the self-hosted wasm target spec tests:
    ```
    ./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test --target=wasm32-wasi
    ```

4. Refactor all modified files to eliminate duplicated code, regardless if it was pre-existing or introduced by you. Our goal is to continuously improve the code quality.

5. Measure how long each of the following steps take and record it in specs/code-review-timings.md. This will allow
us to track performance regressions.
- compiling the C# compiler
- running the C# compiler spec tests
- compiling the self-hosted compiler
- running the self-hosted compiler spec tests
- running the self-hosted wasm target spec tests

6. Write a git commit message that summaries the changes in this commit, not what happened in the code review.
