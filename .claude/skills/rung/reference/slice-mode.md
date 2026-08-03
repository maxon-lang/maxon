# SLICE mode — the board, the lock, and the landing race

*Referenced from `SKILL.md` §1. `scripts/rung-start.sh` and `scripts/rung-finish.sh` now perform every
mechanical step described here. This file is the WHY, and the parts that are still yours to decide.*

## What the scripts do for you, and what they cannot

| | |
|---|---|
| **`rung-start.sh` does** | fetch + rebase · refuse a duplicate row id · refuse a non-free row · **refuse a LANE COLLISION** · edit the batch row AND its members · commit PLAN.md alone · push · re-read the board on rejection · worktree · `cp -r bin` · build |
| **`rung-finish.sh` does** | derive the branch FROM the board row · rebase · gate · merge `--ff-only` · flip 🔶 → ✅ on the batch and its members · commit · push · re-run the suite on a rejected push · tear down |
| **Neither can do** | pick the row · judge whether a stale claim is really stale · write the detail-row prose · decide that a residual holds the rung at ◑ |

## `git push` IS THE LOCK

There is no lockfile, no registry and no coordinator to ask. **A claim exists when — and only when — it
is on `origin/main`.** A claim in your working tree is not a claim; it is a private intention, and the
next agent's `fetch` will never see it.

> ### ⛔ A WORKTREE CANNOT CLAIM. IT CANNOT CHECK OUT `main`.
> ```
> $ git checkout main
> fatal: 'main' is already used by worktree at 'C:/Users/Eric/Dev/maxon'
> ```
> **Verified 2026-07-29** — git allows one checkout of a branch per clone, so the worktree (on
> `slice/<id>-<slug>`) structurally cannot run the claim, nor the closure. That splits the process by
> WHERE it runs, not by what it does:
>
> | | Where | Why |
> |---|---|---|
> | **claim · land · release** (`rung-start.sh`, `rung-finish.sh`) | **a checkout that OWNS `main`** | all three commit or merge onto `main` |
> | **plan → implement → gate** | **your worktree** | never touches `main` |
>
> ⇒ **Two ways to be a parallel agent, and only one of them is a worktree:**
> - **Your OWN CLONE** — you have your own `main`, the scripts work verbatim, you are genuinely
>   uncoordinated. This is what the board is for.
> - **A WORKTREE in a shared clone** — you have no `main` and **cannot claim**. A launcher must claim
>   your row in the primary checkout BEFORE spawning you, and land/release there too. The push-lock
>   still guards against other clones; it cannot guard two agents inside one clone, so the launcher is
>   what serializes them. **If you are in a worktree and were not handed a pre-claimed row, STOP and
>   say so** — do not improvise a claim, and do not start work unclaimed.
>
> Both scripts refuse outright when run from a checkout that is not on `main`, and say this.

**Pick a row that is `⬜ FREE` AND whose LANE holds no `🔶`.** Both conditions, every time — and
`rung-start.sh` enforces both. The lane table is the real exclusion unit, because most of the remaining
rows live inside one 28k-line file (`Compiler/Parser.maxon`) and *"different mechanism"* does not mean
*"different code"*.

> ### ⛔ NEVER `git push --force` ON `main`. NOT ONCE, NOT "JUST THIS TIME".
> A rejected push is not an obstacle — **it is the lock working.** Forcing past it deletes another
> agent's claim commit while that agent is already building against it, and you both then implement the
> same row against a board that agrees with neither of you. The rejection is the ONLY signal this
> protocol has; overriding it removes the protocol.

**Announce the claim before working**: the row id, the lane, and the pushed commit SHA that
`rung-start.sh` prints. That SHA is the claim's evidence — what another agent can verify, and what you
point at if the board and reality ever disagree.

## Releasing, and the claim that outlives its agent

- **On success**, `rung-finish.sh` flips the row `🔶 → ✅ DONE` and pushes it with the closure.
- **If you abandon** — a HALT-AND-ASK, a blocker you cannot clear — **push the row back to `⬜ FREE`
  yourself**, with a one-line note saying what stopped you. A silent abandon is the worst outcome the
  board can produce: it looks exactly like work in progress, forever.
- **Reclaiming a STALE row** (`🔶` older than ~24 h with no branch on the remote — check with
  `git ls-remote --heads origin 'slice/<id>-*'`) is allowed, but it is **an edit that gets pushed like
  any other**: move it to `⬜ FREE`, name the claim you released and why, push, and only then claim it.
  Never just take it — the previous agent may be mid-rebase, and **two live branches for one row is the
  one state this board cannot represent.**

⚠ **A status cell may carry PROSE.** BATCH10's holds a 300-character release note explaining why it was
given back and what to reclaim it after. `rung-start.sh` REFUSES to claim such a row rather than
silently deleting the note: move the note into the row's own description cell first. **The status
column is a status**; a finding written into it is one fact in two columns.

## The landing race — why "did `main` move?" flips from exception to default

Another agent lands while you work. **That is the normal case here, not the surprise**, so:

- **Your branch's base will almost always be behind `main`** ⇒ the re-gate is **owed, not optional**.
  You gated a tree that no longer exists. `rung-finish.sh` rebases FIRST, before the suite, for exactly
  this reason.
- **Your `git push origin main` can be REJECTED.** Same rule as the claim: **rebase and re-gate, never
  force.** A forced landing overwrites a merge whose author already gated it, and the suite that proves
  your work green never saw their code. `rung-finish.sh` re-runs the whole suite on a rejected push
  rather than retrying the push.
- **If the agent who landed first touched YOUR lane's file** (three lanes share `Parser.maxon`), **read
  their diff before you re-gate.** A clean textual rebase is not evidence the two changes compose — it
  is only evidence they were far apart in the file.

## Why a board at all — a near-miss on the day it was opened

While the board section was being written, `main` advanced **5 commits** (`886f238c3`..`89a964f85`, one
of them a PLAN.md edit) under an agent holding **107 uncommitted lines of PLAN.md**. It survived only
because the two edits happened to touch different rows. **Nothing detected the overlap; nothing would
have.** A claim that is not PUSHED is not a claim — it is a private intention that the next `git pull`
silently arbitrates.

## And the collision the board reproduced inside itself

**2026-08-01: two agents filed different rows both called `A2o`** (a gitignored scratch dir reddening
`dotnet build`; and `Array.resize` putting a zero into a range excluding zero). Both noticed
independently, **both renamed to `A2r`**, and the collision simply MOVED — a third rename settled it.
**Three pushes spent on a name.** This is the project's signature bug — ONE NAME, TWO DECLARATIONS,
resolved by whoever pushed last — reproduced inside the ledger that tracks it.

`rung-start.sh` now refuses a duplicate row id. ⚠ But note what PLAN.md's `A2n` row measured before
anyone built that check: **a file-wide "a bolded first table cell is a row id" uniqueness test finds 19
duplicates that are all LEGITIMATE**, because every ladder rung appears once in the *Status at a glance*
index and once in its detail row. The check has to be scoped to the board's own tables — see the header
of `scripts/lib/plan-board.sh`, which anchors on the table header rather than on the section or the file.
