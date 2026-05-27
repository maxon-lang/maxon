---
name: fix-spec-tests
description: Run spec tests and fix any failures in the compiler
---

Run the spec tests and fix any failures by modifying the compiler code. Use --filter when working on a specific failing test.

Prefer the `maxon-dev` MCP tools for all build/test commands (see CLAUDE.md for the full mapping). They are faster and return structured output. Only fall back to raw Bash when no MCP tool covers the case.

## Steps
0. Run the `maxon-coder` skill to load Maxon syntax rules before writing any Maxon code.
1. Run the spec tests with `mcp__maxon-dev__run_self_hosted_test` (or `run_spec_test` with `compiler: "selfhosted"`). Use `filter` to narrow to a specific test, and `spec_test_outcome` for verbose per-test PASS/FAIL when investigating a single failure.
2. Analyze the output to identify which tests are failing and why. Use `mcp__maxon-dev__lookup_error_code` for any 4-digit error codes you see, and `mcp__maxon-dev__dump_ir` when you need to inspect IR (pass `dumpStages: true` for per-stage artifacts).
3. Fix the compiler code in `maxon-selfhosted/` to make the failing tests pass. If new features need to be implemented you can use the maxon-sharp compiler for reference.
4. Rebuild with `mcp__maxon-dev__build` (`target: "selfhosted"`, or `"both"` if you also touched maxon-sharp) and re-run spec tests via the MCP tools to verify the fixes.
5. Repeat until all tests pass.
6. Apply the standard code quality checklist from CLAUDE.md to all changed files. Format modified Maxon files with `mcp__maxon-dev__fmt`.
7. Write a git commit message

## Guidelines
- Read the relevant spec file in `specs/` to understand what the expected behavior is.
- Fix root causes, not symptoms. No workarounds.
- It is possible that any bugs encountered could be in the C# bootstrap compiler. If this is the case then fix the C# compiler in `maxon-sharp/`.
