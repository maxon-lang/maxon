---
name: code-review
description: Conduct a code review of the recent changes in the project, ensuring code quality and consistency. Duplication is the top priority.
---

Review the changes made to the **C# bootstrap (`maxon-sharp`)** and/or **`maxon-shv2`**. Fix issues you
find even if they are pre-existing — the goal is that quality improves continuously, and CLAUDE.md is
explicit that you do not care whether an issue predates your change.

> ⚠ **The v1 self-hosted compiler (`maxon-selfhosted`) is DEPRECATED.** There is no
> `run_self_hosted_test` tool, no `target: "both"` build, and the wasm target is unreachable from the
> MCP. **Two compilers are driveable: `csharp` and `shv2`.** (This skill previously told you otherwise.)

Prefer the `maxon-dev` MCP tools for build/test/format (see CLAUDE.md for the mapping) — but note they
drive the **MAIN tree**. If you are reviewing inside a git worktree, drive the binaries by hand instead.

Create a task list to perform these steps.

## Steps

1. Read `docs/WRITING_MAXON_CODE.md`.

2. Format modified `.maxon` files with `mcp__maxon-dev__fmt` (the **file** form).
   ⚠ **`fmt` with NO PATH formats the whole current directory** — that is its documented default,
   so name the file you mean. `fmt <file>` formats only that file, and an unrecognized flag or a
   second path is REJECTED (exit 1, nothing written) — MEASURED 2026-08-20, correcting this step,
   which used to claim `fmt` "ignores unknown args and reformats the entire tree". It did once;
   both holes are guarded now (`Program.RunFmt`). Check `git status` after formatting anyway.

3. **⭐ ELIMINATE DUPLICATED CODE — this is the top priority** (user directive), and it applies to
   pre-existing duplication in the files you touched, not just new code.

   Hunt the dangerous kind especially: **logic duplicated across a boundary, where nothing MAKES the
   copies agree.** They agree today; a clause added to one and not the other reads as correct at both
   sites. Real examples from this project: the spec pool's parent and worker each independently
   reimplemented *which tests a spec selects* (failure mode: the parent waits forever for records the
   worker was never going to send); the same constant declared in two files; "find separator, slice
   before, slice after" hand-rolled once per record type.

   Ask of each: **could a future edit to one copy silently diverge, and would that show up as a wrong
   answer rather than a compile error?** If so, fix it.

4. Apply the CLAUDE.md quality checklist to all changed files — no bare `default` in a `match`, no
   silent `else` fallthrough, `otherwise panic("reason")` where failure is impossible, no magic values,
   no sentinel returns, no thin wrappers, purpose-named typealiases, narrow-but-provable ranges,
   comments that explain **why**. **Cross-target consistency:** an x64 change needs its arm64
   equivalent.

5. Look for **latent bugs**, not just style: resources released on **every** path (including
   throw/panic), flags published before the data they guard, dropped or double-dispatched work.

6. Update documentation (`LANGUAGE_REFERENCE.md`, `STDLIB_REFERENCE.md`, `QUICK_REFERENCE.md`,
   `BNF_SYNTAX.md`) if the change warrants it.

7. **Rebuild and re-run the gates.** Run the **C# suite before the shv2 suite**, so regenerated
   fragments land in the right order.
   - **Build:** `mcp__maxon-dev__build` with `target: "csharp"`, then `target: "shv2"` (one compiler per
     call — shv2 is built BY the bootstrap).
   - **C# suite:** `mcp__maxon-dev__run_spec_test` (default `csharp`) — expect **2883+ passed, 0 failed**.
   - **shv2 suite:** `mcp__maxon-dev__run_spec_test` with `compiler: "shv2"` — expect **all green**.
   - **Scaling gate:** `mcp__maxon-dev__run_scale_test` — **mandatory** if you touched a pass, the IR, or
     a data structure the compiler indexes by. **PASS** required; `VOID` and `NOISY` are not passes.
   - **If you touched `maxon-sharp/`:** also assert **codegen neutrality** — `git status --short specs/
     specs-shv2/` EMPTY where the change should not have moved any emitted code.

   **Check EXIT CODES. Never grep for a success string** — a past session reported a green build by
   grepping for `^error` while the real failure printed `[CMP] ERROR:`. Exit **101** = memory leak.

   Ignore fragment churn until all test runs complete; then review it — **a moved fragment IS a codegen
   change**, and the diff is the review.

8. Commit to the current branch. Give a commit message that summarizes **the change**, not what happened
   during the review.
