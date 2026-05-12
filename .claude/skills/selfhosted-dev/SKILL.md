---
name: selfhosted-dev
description: Implement the self hosted compiler
---

We are developing the self hosted maxon compiler by enabling spec tests one by one and fixing any failures until all tests pass. This ensures that we are building the compiler incrementally and have a clear understanding of what features are implemented at each step. Run the spec tests and fix any failures by implementing missing functionality in the self hosted compiler code.

By default spec tests will only show the name of failing tests, but you can use `--verbose` to show detailed failure messages for failing tests which can help with debugging. Use --filter when working on a specific failing test.

## Steps

0. Read `docs/WRITING_MAXON_CODE.md`
1. Run the spec tests: `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test`
2. Analyze the output to identify which tests are failing and why.
3. Fix the compiler code in `maxon-selfhosted/` to make the failing tests pass.
4. Rebuild and re-run spec tests to verify the fixes (see CLAUDE.md for build commands and flags):
   - **Build self-hosted compiler:** `./bin/maxon.exe build maxon-selfhosted` (requires C# compiler already built)
   - **Run self-hosted spec tests:** `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test`
   - **Run wasm spec tests:** `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test --target=wasm32-wasi`
   - **Build C# compiler (if needed):** `dotnet build` from `maxon-sharp/`
   - **Run C# spec tests (if needed):** `./bin/maxon.exe spec-test`
5. Repeat until all tests pass.
6. If any changes occurred to the RequiredIR of other tests in register-allocator.md, review them to ensure they are correct.
7. Apply the standard code quality checklist from CLAUDE.md to all changed files.
8. Refactor all modified files to eliminate duplicated code, regardless if it was pre-existing or introduced by you.
9. Update the ROADMAP.md file to reflect the current status of the self-hosted compiler and any remaining work.
10. Write a git commit message

## Guidelines
- Read the relevant spec file in `specs/` to understand what the expected behavior is.
- Fix root causes, not symptoms. No workarounds.
- It's possible that bugs encountered could be in the C# bootstrap compiler. If so, fix the C# compiler in `maxon-sharp/`.
- The valid ExitCode range is `int(0 to 125)` (due to the wasm target). If any tests return values outside this range, fix them.
