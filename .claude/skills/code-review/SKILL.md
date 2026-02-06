---
name: code-review
description: Implement a new language feature in the Maxon compiler using spec-driven development
disable-model-invocation: true
---

Review the changes that have been made in the project.

## Steps

1. Review all code changes:
    - Eliminate duplicated code — refactor shared logic into helper methods.
    - Ensure no `switch` statements use `default` cases — all cases must be handled explicitly.
    - Ensure no `else` clauses silently catch unhandled conditions — throw errors for unexpected inputs.
    - Ensure comments explain "why" not "what".
2. Write a git commit message for these changes.

## Guidelines

- Any old 3-digit error codes (e.g., E022) in spec files need to be updated to the new 4-digit error codes.
- Keep the x86 code generation correct — watch for short jump overflow (max +/-127 bytes) and 32-bit register truncation (image base is above 4GB).
