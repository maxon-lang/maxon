---
name: fix-spec-tests
description: Run spec tests and fix any failures in the compiler
---

Run the spec tests and fix any failures by modifying the compiler code. Use `filter` when working on a specific failing test.

Prefer the `maxon-dev` MCP tools for all build/test commands (see CLAUDE.md for the full mapping). They are faster and return structured output. Only fall back to raw Bash when no MCP tool covers the case.

**Which compiler?** Two suites are driveable: the **C# bootstrap** (`compiler: "csharp"`, suite `specs`) and **shv2** (`compiler: "shv2"`, suite `specs-shv2`) — shv2 is the active development line. Pick the one whose suite is failing and pass that token to every tool below. *(The v1 `maxon-selfhosted` compiler is DEPRECATED and unreachable from the MCP — do not target it.)* In a worktree, pass `repoRoot` to every tool call (see the box in CLAUDE.md) or you will drive the main checkout instead of your tree.

## Steps
0. Run the `maxon-coder` skill to load Maxon syntax rules before writing any Maxon code.
1. Run the suite with `mcp__maxon-dev__run_spec_test` (set `compiler` to `"csharp"` or `"shv2"`). Use `filter` to narrow to a specific test, and `mcp__maxon-dev__spec_test_outcome` (requires `filter`) for verbose per-test PASS/FAIL when investigating a single failure.
2. Analyze the output to identify which tests are failing and why. Use `mcp__maxon-dev__lookup_error_code` for any 4-digit error codes you see, and `mcp__maxon-dev__dump_ir` to inspect IR (`dumpStages: true` for per-stage artifacts — **csharp only**; shv2 rejects it).
3. Fix the compiler code to make the failing tests pass — `maxon-shv2/` for the shv2 suite, `maxon-sharp/` for the C# suite.
4. Rebuild with `mcp__maxon-dev__build` (`target: "csharp"` or `target: "shv2"` — one compiler per call; shv2 is built BY the bootstrap, so build `csharp` first if it is stale) and re-run the suite via the MCP tools to verify the fixes.
5. Repeat until all tests pass.
6. Apply the standard code quality checklist from CLAUDE.md to all changed files. Format modified Maxon files with `mcp__maxon-dev__fmt` (csharp `maxon fmt` — shv2 has no formatter).
7. **Stage every golden the runs touched** — `git status --short specs-shv2/fragments/ specs/`, then
   `git add -A` those paths: `??` (minted), ` M` (**modified**) and ` D` (deleted) are the same
   obligation. See the guideline below before you write the message.
8. Write a git commit message.

## Guidelines
- **Red before green.** If the bug is already covered by a failing spec, that failing spec IS your red
  baseline — do NOT stash your fix afterward to "reconfirm" it. If the bug is not yet covered,
  write/enable the spec that captures it and watch it fail against the current compiler *before* you
  change any code (a spec is data — no rebuild needed to see it go red); then the fix is proven the
  moment it goes green. Never stash → rebuild → test → unstash → rebuild to confirm a fix: that is two
  extra full builds to re-derive a red you can get for free up front.
- Read the relevant spec file (`specs/` for the C# suite, `specs-shv2/` for shv2) to understand what the expected behavior is.
- Fix root causes, not symptoms. No workarounds.
- ⛔ **A MODIFIED GOLDEN IS COMMITTED WITH THE CHANGE THAT MOVED IT — never reverted.** `git checkout -- specs-shv2/fragments/ specs/` to "clean up churn" throws away the only record of what your fix did to the emitted code, and buries a real regression in the noise it creates. **A moved golden IS a codegen change: review the diff as one, and if you cannot say why it moved, that is a finding to explain BEFORE committing, not noise to drop.**
- If a RequiredIR block fails, regenerate it with `updateRequired: true` **paired with a `filter`** — unfiltered, it rewrites every golden in the suite. shv2's `--update-required` regenerates RequiredIR but NOT `maxoncstderr` blocks; an error-code renumber moves those by hand.
- A bug surfaced by the shv2 suite may actually live in the C# bootstrap, since the bootstrap builds shv2 — fix it in `maxon-sharp/` when that is the real cause.
