# Widening the WAVE vs SLICING the rung

*Referenced from `SKILL.md` §2. Read it when the rung looks too big for one wave.*

**A rung may be too big for one wave. There are TWO ways to answer that, and this skill used to name
only one — which is why rungs have been sliced far harder than they need to be** (P1.7 ran **ten**
slices; P1.8 was cut A/B/C/D before a line was written).

| | |
|---|---|
| **Widening the WAVE** — more agents, in parallel, in ONE loop | Costs one more brief. The loop runs **once**: one survey, one contract, one optimizer, one reviewer, one gate battery, one cross-target run, one merge, one PLAN.md update. Bounded by integration at ~4–5 agents |
| **SLICING the rung** — sequential slices, each its own loop | **Multiplies the entire loop.** Every slice pays a fresh survey, a fresh optimizer agent, a fresh reviewer agent, a full gate battery, a cross-target run, a rebase/merge/push and a PLAN.md edit — plus a coordinator plan-and-integrate cycle, which is the serial bottleneck |

**A slice is the most expensive thing this process can buy, and its price used to be written down
nowhere while two separate lines argued for more of them.** Both of those lines are monotone — a survey
always finds more, and a spec port list only ever gets longer — so an agent applying them faithfully
slices every time. That is the bug.

**⇒ Default to ONE rung with a WIDER WAVE. Slice only when the wave cannot absorb the work**, which
means one of exactly three things:

1. **A hard dependency** — part B codes against a contract part A defines (new IR ops, a new dialect
   op, a layout descriptor). B cannot start until A's contract is real, so parallelism is unavailable.
2. **A risk split** — one part lands unattended, the other needs a **design ruling** or is likely to
   HALT. Slicing keeps the cheap, certain unlock from being held hostage. *(This is P1.0d's precedent,
   and note what actually justified it: the front-end part needed **no new IR ops and no codegen** —
   a mechanism boundary, not a size boundary. It unlocked 1080 corpus cases on its own.)*
3. **The exclusive file lists genuinely collide** — two parts must both own the same file, so they
   cannot be concurrent agents at all (rule 1: one file, one owner, per wave).

**⛔ NOT reasons to slice:**

- **A long spec port list.** Spec count is not risk. Two hundred cases over ONE mechanism is one rung
  with one wave — the list is the acceptance criteria, not a workload estimate.
- **"The rung is N mechanisms."** N mechanisms with disjoint file lists is an N-agent wave.
- **Wanting a green checkpoint sooner.** That is what the wave's per-agent `--filter` runs are for.
- **The survey came back big.** The survey's job is to find everything; it has no opinion on batching.

**If you do slice, say which of the three reasons applies, per slice, in the plan.** A slice without
one of those three named is a slice that should have been another agent in the same wave.

**Each slice runs the full loop — that is exactly why there should be few of them.** What a slice does
NOT re-run is the survey (`SKILL.md` §2).

---

## The BATCH — why the board stopped being worked one row at a time

*From `maxon-shv2/PLAN.md`'s board, rewritten 2026-07-31.*

Measured over nine consecutive rungs: the per-rung *fixed* cost — claim, survey, plan, worktree, three
suite lanes, the ladder read, an independent review, rebase, merge, row, memory — **dominated the actual
fix**, and **integration friction (two landing races, three conflicted rebases, a stale-binary false red)
consumed more wall-clock than every review combined while finding nothing.** Twenty-two rows were filed
against thirteen closed the same day. **A board of singletons *manufactures* that overhead.**

**A BATCH is the unit of claiming, branching, reviewing and merging.** One worktree, one branch, one
review, one merge, one row-closure pass for the whole batch.

| Rule | |
|---|---|
| **Claim the BATCH row, not its members** | Members inherit the batch's status. `rung-start.sh` flips both. |
| **A batch claims EVERY lane it lists** | Stricter than one-row-one-lane, and it is what makes a batch safe. Two batches sharing a lane cannot be live at once — `rung-start.sh` refuses. |
| **One gate, paid once** | BATCH1 was eight bootstrap defects behind ONE C# suite run plus ONE codegen-neutrality A/B. Paying that per row is seven wasted runs. |
| **One thesis, so a review has one thing to attack** | Members share a mechanism or a file, so the reviewer forms a single argument instead of eight. |
| **A member may be dropped mid-batch** | If a member turns out not to share the thesis, release it back to `⬜ FREE` in the closure and say why. Do NOT stretch the batch to keep it. |
| ⛔ **Do not file a new singleton row for hygiene** | Fix it in the batch that found it, or add it to an existing batch. A row is a commitment someone must read and dispose of; the board grew by nine net because that bar was too low. |
