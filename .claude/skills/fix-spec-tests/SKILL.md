---
name: fix-spec-tests
description: Run spec tests and fix any failures in the compiler
disable-model-invocation: true
---

Run the spec tests and fix any failures by modifying the compiler code.

## Steps

1. Run the spec tests: `./maxon-sharp/bin/Debug/net8.0/win-x64/maxonsharp.exe spec-test`
2. Analyze the output to identify which tests are failing and why.
3. Fix the compiler code in `maxon-sharp/` to make the failing tests pass.
4. Rebuild and re-run spec tests to verify the fixes.
5. Repeat until all tests pass.
6. If any compiler code was changed then review any code changes to see if you can refactor to eliminate duplicated code.
7. If any compiler code was changed then review any code changes to check that the code does not use default cases. When handling multiple cases if there is not a specific match it should throw an error. Check 'switch' and also the use of 'else'. 

## Guidelines

- Read the relevant spec file in `specs/` to understand what the expected behavior is.
- Use `--log=CATEGORY:LEVEL` to get more detail when debugging (e.g., `--log=mlir:debug`).
- Fix root causes, not symptoms. No workarounds.
- Any old 3 digit error codes (ie E022) in the spec files need to updated to the new 4 digit error codes.
