---
name: code-review
description: Conduct a code review of the recent changes in the project, ensuring code quality and consistency.
---

Review the changes that have been made in the project to the c# and self hosted compilers. If you find any issues that 
are outside the current changes fix them as well. The goal is to ensure that the codebase maintains high quality and consistency, and that any issues are addressed before merging the changes.

Create a task list to perform these steps.

Prefer the `maxon-dev` MCP tools for build/test/format operations (see CLAUDE.md for the full mapping). They are faster and return structured output.

## Steps

0. Read `docs/WRITING_MAXON_CODE.md`
1. Use `mcp__maxon-dev__fmt` to format all modified maxon files, ensuring consistent code style.
2. Apply the standard code quality checklist from CLAUDE.md to all changed files.
3. Update documentation, including `LANGUAGE_REFERENCE.md` and `STDLIB_REFERENCE.md` and `QUICK_REFERENCE.md` and `BNF_SYNTAX.md` if necessary.
4. Rebuild and run spec tests if you made any changes to the codebase, ensuring all tests pass. Run the C# tests first, then the self-hosted tests, then the self-hosted wasm target:
    - **Build:** `mcp__maxon-dev__build` with `target: "both"`.
    - **C# spec tests:** `mcp__maxon-dev__run_spec_test` (default compiler is `csharp`).
    - **Self-hosted spec tests:** `mcp__maxon-dev__run_self_hosted_test`.
    - **Self-hosted wasm target:** `mcp__maxon-dev__run_self_hosted_test` with `target: "wasm32-wasi"`.

5. Refactor all modified files to eliminate duplicated code, regardless if it was pre-existing or introduced by you. Our goal is to continuously improve the code quality.

6. Give me a git commit message that summaries the changes in this commit, not what happened in the code review.
