---
name: implement-phase
description: Implement a phase of the ROADMAP.md plan
---

Implement the specific phase of the ROADMAP.md plan.

Run the spec tests and fix any failures by modifying the compiler code. Use --filter when working on a specific failing test.

## Steps

0. Run the `maxon-coder` skill to load Maxon syntax rules before writing any Maxon code.
1. Add the spec files specified in the ROADMAP.md for this phase to the whitelist in runAllSpecTests.
2. Run the spec tests: `./maxon-selfhosted/.maxon/maxon-selfhosted.exe spec-test`
3. Read maxon-selfhosted\ARCHITECTURE.md for an overview of the compiler architecture and how to navigate the codebase. This will help you understand where to find the relevant code for the phase you are implementing.
4. Analyze failures and implement the necessary compiler changes
5. Rebuild and re-run spec tests to verify the fix (see CLAUDE.md for build commands and flags).
6. Repeat steps 2-4 until all spec tests pass.
7. Apply the standard code quality checklist from CLAUDE.md to all changed files.
8. Write a git commit message

## Guidelines
- Read existing spec files and compiler code for similar features to follow established patterns.
- Fix root causes, not symptoms. No workarounds.
