---
name: incremental-dev
description: Implement spec tests one at a time
disable-model-invocation: true
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
7. review the X86 MLIR for that test and ensure registers are handled correctly.
8. If any changes occured to the required MLIR of other tests in register-allocator.md then those changes need to be review to ensure they are ok.
9. If any compiler code was changed then review any code changes to see if you can refactor to eliminate duplicated code.
10. If any compiler code was changed then review any code changes to check that the code does not use default cases. When handling multiple cases if there is not a specific match it should throw an error. Check 'switch' and also the use of 'else'. 
11. Write a git commit message for these changes.

## Guidelines

- Read the relevant spec file in `specs/` to understand what the expected behavior is.
- Use `--log=CATEGORY:LEVEL` to get more detail when debugging (e.g., `--log=mlir:debug`).
- Fix root causes, not symptoms. No workarounds.
- Any old 3 digit error codes (ie E022) in the spec files need to updated to the new 4 digit error codes.
