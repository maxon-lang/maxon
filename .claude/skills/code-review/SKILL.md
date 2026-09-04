---
name: code-review
description: Independent code review of a completed change in maxon-sharp and/or maxon-shv2. DUPLICATION IS THE TOP PRIORITY, then latent bugs, then the CLAUDE.md quality checklist. Never run by the agent that wrote the code. Runs after the optimization pass and before the commit.
---

# Review a change

You review a completed change **you did not write**. That independence is the point — if you wrote the
code, you are the wrong reviewer for it.

**You run AFTER the optimization pass and you are the LAST quality gate before the commit.** That
ordering is deliberate: an optimizer rewrites code, so it can introduce exactly the duplication you
exist to catch — a fast path forked from a slow one, a helper inlined at three call sites, a specialized
copy of a general routine. **Review the optimizer's diff as carefully as the implementer's**, and treat
a performance-motivated fork as duplication until proven otherwise (if it must exist, it needs a comment
saying why the two cannot be one).

**Fix what you find even if it is pre-existing** — the goal is that quality improves continuously, and
CLAUDE.md is explicit that you do not care whether an issue predates the change.

**Do not chase green.** Your job is **QUALITY and LATENT BUGS**, not re-running the suite — iterate on
`--filter` while you probe.

## First: who is running you

> ### ⛔ IF ANOTHER PROCESS DISPATCHED YOU, DO NOT RUN THE FULL SUITE AND DO NOT COMMIT.
>
> `/land` §5 and any caller that owns its own gates will run the full unfiltered suite **on the
> identical tree, minutes later**, and commit everything as one commit. A full run here is not extra
> assurance — it is the same run twice, and it is the single largest piece of duplicated work these
> processes have ever paid for. **Your `--filter`ed runs are yours; the battery is the caller's, once,
> after you.** If a refactor you make spans files, **say so in your report** — the caller's battery is
> where a broken caller three files away surfaces, and it is already going to run.

**Invoked standalone**, steps 6–8 below are yours: the gates and the commit. Skip them when you were
dispatched.

Prefer the `maxon-dev` MCP tools for build/test/format (see CLAUDE.md for the mapping). ⚠ **In a
worktree, pass `repoRoot`** — the absolute path of your worktree root — to every tool call, or you drive
the main checkout and get a green about a tree containing none of the work.

Create a task list to perform these steps.

## 1. Read `docs/WRITING_MAXON_CODE.md`

## 2. Format modified `.maxon` files

`mcp__maxon-dev__fmt`, the **file** form. ⚠ **`fmt` with NO PATH formats the whole current directory** —
that is its documented default, so name the file you mean. Check `git status` after formatting.

## 3. ⭐ ELIMINATE DUPLICATED CODE — the top priority, by user directive

It applies to pre-existing duplication in the files touched, not just new code.

**Hunt the dangerous kind: logic duplicated across a boundary, where nothing MAKES the copies agree.**
They agree today; a clause added to one and not the other does not read as a bug at either site. This is
the class that has actually bitten this project:

- The spec pool's parent and worker each independently reimplemented *which tests a spec selects*. They
  agreed. Nothing made them agree — and the failure mode was the parent waiting forever for records the
  worker was never going to send.
- Two constants named `DraftStatus` in two files.
- Hand-rolled "find separator, slice before, slice after" written once per record type.

Ask of every helper: **could a future edit to one copy silently diverge from the other, and what would
that look like at runtime?** If the answer is "a wrong answer, not a compile error," fix it.

## 4. Latent bugs — not just style

- **Resource release on EVERY path**, including throw/panic/abort. (Two `StreamingSubprocess` handle
  leaks on error paths were found exactly this way.)
- **Ordering hazards**: a flag published before the data it guards is readable. "Safe because nothing
  yields in between" is a property of today's code, not an invariant.
- Concurrency: dropped/double-dispatched work, starvation, races between a producer and a consumer.
- Off-by-one, unhandled boundary, silent fallthrough.

## 5. The CLAUDE.md quality checklist

**`.claude/CLAUDE.md`'s "Code Quality" section is the checklist and is already in your context — apply
it to every changed file, do not restate it.** The three that get missed:

- ⭐ **COMMENTS ARE A FIRST-CLASS TARGET, and excess commentary is a finding you DELETE.** Concise and
  minimal (the default is none), **why** not **how**, **present state only** (⛔ no "used to", "changed
  from", no old names — git holds that), and a comment you touch is rewritten to conform rather than
  patched. This binds PRE-EXISTING comments in the files under review exactly as duplication does: a
  restated signature, a banner over every section, a line-by-line narration, or a comment describing a
  shape the code no longer has all come out.
- **Typed ranges as narrow as PROVABLY correct** — a wrong narrow bound is a runtime panic; wide is fine
  where there is no real bound.
- **Cross-target consistency:** an x64 change needs its arm64 equivalent — **the CODE, which you review
  by READING. Never ask for it to be RUN**: the arm64 lanes are remote and are not in the battery, so
  "unverified on arm64" is never a review finding and never blocks the change.

Update documentation (`LANGUAGE_REFERENCE.md`, `STDLIB_REFERENCE.md`, `QUICK_REFERENCE.md`,
`BNF_SYNTAX.md`) if the change warrants it.

## 6. Rebuild and re-run the gates — STANDALONE ONLY

Run the **C# suite before the shv2 suite**, so regenerated fragments land in the right order.

- **Build:** `mcp__maxon-dev__build` with `target: "csharp"`, then `target: "shv2"` (one compiler per
  call — shv2 is built BY the bootstrap).
- **C# suite:** `mcp__maxon-dev__run_spec_test` (default `csharp`) — all green.
- **shv2 suite:** `mcp__maxon-dev__run_spec_test` with `compiler: "shv2"` — all green.
- **Scaling:** `mcp__maxon-dev__run_scale_test` if the change touched a pass, the IR, or a data
  structure the compiler indexes by. ⚠ **It is an INSTRUMENT with no verdict — there is no green one,
  and you never touch it to make a number look better.** Read the doubling ladder straight off the
  ALLOCATION columns: **×2 is linear, ×4 is quadratic.** A curve that bends is a reading to explain.
- **If you touched `maxon-sharp/`:** also assert **codegen neutrality** — `git status --short specs/
  specs-shv2/` EMPTY where the change should not have moved any emitted code.

**Check EXIT CODES. Never grep for a success string** — a past session reported a green build by
grepping for `^error` while the real failure printed `[CMP] ERROR:`. Exit **101** = memory leak.

Ignore fragment churn until all runs complete; then review it — **a moved fragment IS a codegen
change**, and the diff is the review.

## 7. Commit — STANDALONE ONLY

Commit to the current branch, **including every golden the runs touched**. Give a message that
summarizes **the change**, not what happened during the review.

⛔ **MODIFIED goldens are committed, never reverted.** `git status --short specs-shv2/fragments/ specs/`
→ `git add -A` those paths; `??` (minted), ` M` (**modified**) and ` D` (deleted) are one obligation.
A `git checkout --` over them to leave a tidy `git status` destroys the record of what the change did to
the emitted code — which is the very diff step 6 just told you to review. A fragment that moved for a
reason you cannot state is a **finding**: explain it in the message, or fix it; do not drop it.

## Rules of engagement

- **A DEFECT you find by PROBING is still a blocker — a WRONG ANSWER as much as a leak —
  fix-or-cleanly-reject, NEVER "defer".** *Leaks are not ok, and neither is a wrong answer in code this
  change owns*, even latent ones the committed suite is green over: a reachable leak, or a construct
  that miscompiles, is fixed, or turned into a clean compile error, before this lands. (This exists
  because a boxed-union return leak was once recommended for deferral here; the right call was to reject
  it — and the same call binds a wrong answer.)
- **Make the call on anything the author flagged for a decision, and justify it.**
- If you find something real but genuinely OUTSIDE this change — a **`maxon-sharp` bug** (needs the full
  C# suite as its gate), a **distinct feature**, or a **measured-linear perf debt** — **say so and leave
  it for your caller to triage**, rather than smuggling it in OR deferring it on your own authority.
  There is no backlog file, so it goes in your REPORT and the caller decides. ⚠ **"Too big to ride
  along" is NOT the same as "a wrong answer in the files I just reviewed"** — that one you fix, exactly
  like a leak. Do not let this bullet become an escape hatch for a defect the change owns.

## Report

Every issue with `file:line`; what you fixed; what you deliberately left, and why; the real output of
your re-verification. **Never claim a check passed unless you ran it.**
