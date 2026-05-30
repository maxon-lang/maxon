---
name: incremental-dev
description: Implement the self hosted compiler
---

We are developing the self hosted maxon compiler by enabling spec tests one by one and fixing any failures until all tests pass. This ensures that we are building the compiler incrementally and have a clear understanding of what features are implemented at each step. Run the spec tests and fix any failures by implementing missing functionality in the self hosted compiler code.

Run the spec tests and fix any failures by modifying the compiler code. Use --filter when working on a specific failing test.

Prefer the `maxon-dev` MCP tools for all build/test commands (see CLAUDE.md for the full mapping). They are faster and return structured output. Only fall back to raw Bash when no MCP tool covers the case.

## Steps

0. Read `docs/WRITING_MAXON_CODE.md`
1. Find the next commented out spec file in the whitelist and uncomment it. You have to recompile the self hosted compiler with the updated whitelist using `mcp__maxon-dev__build` with `target: "selfhosted"` 
2. Run the spec tests with `mcp__maxon-dev__run_self_hosted_test`. Use `filter` to narrow to a specific test, and `spec_test_outcome` (with `compiler: "selfhosted"`) for verbose per-test PASS/FAIL.
3. If everything passes then repeat from step 1. 
4. Analyze the output to identify which tests are failing and why. Use `mcp__maxon-dev__lookup_error_code` for any 4-digit error codes, and `mcp__maxon-dev__dump_ir` when you need to inspect lowered IR.
5. Fix the compiler code in `maxon-selfhosted/` to make the failing tests pass.
6. Rebuild and re-run spec tests to verify the fixes:
   - **Build self-hosted compiler:** `mcp__maxon-dev__build` with `target: "selfhosted"` (requires C# compiler already built; use `target: "both"` to chain).
   - **Run self-hosted spec tests:** `mcp__maxon-dev__run_self_hosted_test`.
   - **Run wasm spec tests:** `mcp__maxon-dev__run_self_hosted_test` with `target: "wasm32-wasi"`.
   - **Build C# compiler (if needed):** `mcp__maxon-dev__build` with `target: "csharp"`.
   - **Run C# spec tests (if needed):** `mcp__maxon-dev__run_spec_test` (default compiler is `csharp`).
7. Repeat until all tests pass.

## Guidelines
- Read the relevant spec file in `specs/` to understand what the expected behavior is.
- Fix root causes, not symptoms. No workarounds.
- It's possible that bugs encountered could be in the C# bootstrap compiler. If so, fix the C# compiler in `maxon-sharp/`.
- The valid ExitCode range is `int(0 to 125)` (due to the wasm target). If any tests return values outside this range, fix them.
