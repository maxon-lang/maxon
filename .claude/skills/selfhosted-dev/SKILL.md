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
4. Rebuild and re-run spec tests to verify the fixes:
   - **Build C# compiler (if needed):** `dotnet build` from `maxon-sharp/`
   - **Run C# spec tests (if needed):** `./bin/maxon.exe spec-test`
   - **Build self-hosted compiler:** `./bin/maxon.exe build maxon-selfhosted` (requires C# compiler already built)
   - **Run self-hosted spec tests:** `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test`
   - **Run wasm spec tests:** `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test --target=wasm`
5. Repeat until all tests pass.
6. Fix any problems reported by the IDE
7. If any changes occured to the required IR of other tests in register-allocator.md then those changes need to be reviewed to ensure they are ok.
8. Review all code changes:
    - Ensure we have maintained equivilant functionality in all the targets (x64, arm64, etc)
    - Eliminate duplicated code — refactor shared logic into helper methods.
    - Ensure no `switch` or 'match' statements use `default` cases — all cases must be handled explicitly.
    - Ensure no `else` clauses silently catch unhandled conditions — throw errors for unexpected inputs.
    - Ensure functions that handle multiple cases, for example a series of 'if' statements, but return 
      a default value for unhandled cases, should be refactored to throw an error instead. This ensures that all cases are handled explicitly and prevents silent failures.
    - Ensure comments explain "why" not "what".
    - if a `try` should never fail then its `otherwise` should be a panic
    - Fix any problems reported by the IDE
    - typealias should describe its purpose, not its type
    - typed ranges should be as specific as possible, e.g. `int(0 to 100)` instead of `int(0 to u64.max)`. Carefully consider the valid range for each type and use the narrowest possible range to catch errors. Max range is fine if there is no clear limit.
    - look for any comments that imply that something was skippped or not fully implemented or should be done later
    - Fix any compiler warnings
9. Refactor all modified files to eliminate duplicated code, regardless if it was pre-existing or introduced by you. Our goal is to continuously improve the code quality.
10. Update the roadmap.md file to reflect the current status of the self hosted compiler and any remaining work that needs to be done.
11. Write a git commit message

## Guidelines
- Read the relevant spec file in `specs/` to understand what the expected behavior is.
- For memory issues try compiling with "--mm-trace"
- Use `--log=CATEGORY:LEVEL` to get more detail when debugging (e.g., `--log=ir:debug`).
- For assembly-level debugging of a compiled Maxon executable, use `./scripts/lldb.sh <program.exe>` — it wraps `llvm-project/bin/lldb.exe` with the env vars its embedded Python 3.10 needs. Maxon emits COFF symbols, so Maxon functions are addressable by name (e.g. `b test_leak.main`, `b stdlib.Print.print`).
- Fix root causes, not symptoms. No workarounds.
- If any tests that use RequiredIR fail you can regenerate the required IR and MmTrace stderr by using `--update-required`
- its possible that any bugs encountered could be in the c# bootrap compiler. If this is the case then you will need to fix the c# compiler in `maxon-sharp/`
- The valid ExitCode range was recently changed to int(0 to 125) (due to the wasm target) so if you encounter any tests that return values outside of this range then you will need to fix the test to return a valid ExitCode.
