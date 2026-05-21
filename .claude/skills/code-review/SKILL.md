---
name: code-review
description: Conduct a code review of the recent changes in the project, ensuring code quality and consistency.
---

Review the changes that have been made in the project to the c# and self hosted compilers. If you find any issues that 
are outside the current changes fix them as well. The goal is to ensure that the codebase maintains high quality and consistency, and that any issues are addressed before merging the changes.

Create a task list to perform these steps.

## Steps

0. Read `docs/WRITING_MAXON_CODE.md`
1. Apply the standard code quality checklist from CLAUDE.md to all changed files.
2. Update documentation, including `LANGUAGE_REFERENCE.md` and `STDLIB_REFERENCE.md` and `QUICK_REFERENCE.md` and `BNF_SYNTAX.md` if necessary.
3. Rebuild and run spec tests if you made any changes to the codebase, ensuring all tests pass. Run the C# tests first, then the self-hosted tests. Also run the self-hosted wasm target:
    ```
    ./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test --target=wasm32-wasi
    ```

4. Refactor all modified files to eliminate duplicated code, regardless if it was pre-existing or introduced by you. Our goal is to continuously improve the code quality.

5. Use "maxon fmt" to format all modified maxon files, ensuring consistent code style.

6. Measure how long each of the following steps take and record it in specs/code-review-timings.md. This will allow
us to track performance regressions.
- compiling the C# compiler
- running the C# compiler spec tests
- compiling the self-hosted compiler
- running the self-hosted compiler spec tests
- running the self-hosted wasm target spec tests

7. Write a git commit message that summaries the changes in this commit, not what happened in the code review.
