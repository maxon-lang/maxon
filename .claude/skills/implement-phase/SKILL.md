---
name: implement-phase
description: Implement a phase of the ROADMAP.md plan
---

Implement the specific phase of the ROADMAP.md plan.

Run the spec tests and fix any failures by modifying the compiler code. Use --filter when working on a specific failing test.

Prefer the `maxon-dev` MCP tools for build/test/IR-dump operations (see CLAUDE.md for the full mapping). They are faster and return structured output.

## Steps

0. Run the `maxon-coder` skill to load Maxon syntax rules before writing any Maxon code.
1. Add the spec files specified in the ROADMAP.md for this phase to the whitelist in runAllSpecTests.
2. Run the spec tests with `mcp__maxon-dev__run_self_hosted_test`. Use `filter` to narrow when iterating on a single failure; use `mcp__maxon-dev__spec_test_outcome` (compiler: "selfhosted") for verbose per-test detail.
3. Read maxon-selfhosted\ARCHITECTURE.md for an overview of the compiler architecture and how to navigate the codebase. This will help you understand where to find the relevant code for the phase you are implementing.
4. Analyze failures and implement the necessary compiler changes. Use `mcp__maxon-dev__lookup_error_code` for any 4-digit error codes, and `mcp__maxon-dev__dump_ir` (with `dumpStages: true` when useful) to inspect IR.
5. Rebuild with `mcp__maxon-dev__build` (`target: "selfhosted"` or `"both"`) and re-run spec tests via the MCP tools to verify the fix.
6. Repeat steps 2-4 until all spec tests pass.
7. Apply the standard code quality checklist from CLAUDE.md to all changed files. Format modified Maxon files with `mcp__maxon-dev__fmt`.
8. Write a git commit message

## Guidelines
- Read existing spec files and compiler code for similar features to follow established patterns.
- Fix root causes, not symptoms. No workarounds.
