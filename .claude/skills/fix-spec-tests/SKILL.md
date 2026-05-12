---
name: fix-spec-tests
description: Run spec tests and fix any failures in the compiler
---

Run the spec tests and fix any failures by modifying the compiler code.
By default spec tests will only show the name of failing tests, but you can use `--verbose` to show detailed failure messages for failing tests which can help with debugging. Use --filter when working on a specific failing test.

## Steps
0. Run the `maxon-coder` skill to load Maxon syntax rules before writing any Maxon code.
1. Run the spec tests: `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test`
2. Analyze the output to identify which tests are failing and why.
3. Fix the compiler code in `maxon-selfhosted/` to make the failing tests pass. If new features need to be implemented you
can use the maxon-sharp compiler for reference.
4. Rebuild and re-run spec tests to verify the fixes (see CLAUDE.md for build commands and flags).
5. Repeat until all tests pass.
6. Apply the standard code quality checklist from CLAUDE.md to all changed files.
7. Write a git commit message

## Guidelines
- Read the relevant spec file in `specs/` to understand what the expected behavior is.
- Fix root causes, not symptoms. No workarounds.
- It is possible that any bugs encountered could be in the C# bootstrap compiler. If this is the case then fix the C# compiler in `maxon-sharp/`.
