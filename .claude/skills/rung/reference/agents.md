# The four agent roles — what each is for, and which model it runs on

*Referenced from `SKILL.md` §2, §4, §5, §6.*

## The survey agents run on SONNET. The three rung agents do not.

**Spawn the survey agents as `Explore` with `model: "sonnet"`.** They have no frontmatter file of their
own, so this line is the only place that fact lives.

**The reason is the role, not the price.** This role has already had its judgment removed by design: it
returns citations, and *you* decide. It is also the one delegation whose output you can **CHECK** — a
wrong `file:line` fails the moment the implementer opens the file, and a wrong spec port list fails when
the specs do not go RED before the wave. **A cheaper model on a judgment-free, verifiable,
highest-volume job is the trade this step was built for.**

⚠ **Do NOT extend this to `maxon-rung-implementer`, `maxon-rung-optimizer` or `maxon-rung-reviewer`.**
Their model is declared in their own frontmatter and it is not Sonnet — **the reviewer least of all.**

## Why the REVIEWER is mandatory and never batched

It runs LAST, and **it is the gate that has actually fired**:

- a reachable **SEGFAULT** at P1.4b Wave 2c,
- a reachable **PANIC** at P1.7,
- a bug an **8th rung running** at P1.7a,
- the owned-String **leak** at P1.2,
- two resource leaks + a cross-process duplicated selection rule at P1.0a.

**Every one of them in code an Opus implementer had just called done.** **A weaker auditor over a
stronger author is the one arrangement guaranteed not to catch what the author missed.**

**And whenever it runs, it must be an agent that did NOT write the code** (user directive). The
independence is the point.

> **⚠ Optimize BEFORE you review, and never the other way round.** An optimizer *rewrites code*, so it
> can introduce exactly the duplication the review exists to catch — a fast path forked from a slow one,
> a helper inlined at three call sites. **The duplication-focused review must be the LAST quality gate
> before the merge**, and it reviews the optimizer's diff as well as the implementer's.

## ⭐ ONE SURVEY PER RUNG FAMILY — a slice does NOT re-survey

**The survey belongs to the RUNG, not to the slice.** P1.7's ten slices each re-read the same v1
register-allocator and array files to re-derive the same facts; that is nine fan-outs bought and thrown
away. **Run the survey ONCE, when the rung is first opened**, write it into the plan (the per-layer
table IS the durable form), and cut each slice's brief from it — the same way each agent's brief is cut
from the plan rather than making the agent re-derive one.

**Re-survey only what actually moved:** a slice reaching a mechanism the original survey did not cover
gets a *targeted* survey of that mechanism, not a fresh sweep of both references. **And if a slice
discovers the survey was WRONG at the code, that invalidates the plan for every remaining slice**, not
just that one.

## The two reference compilers answer DIFFERENT questions — consult BOTH

| | |
|---|---|
| **`maxon-selfhosted` (v1) — the closest CODE** | **191,487 lines of working, debugged Maxon**, same language, same `stdlib/`, closest to shv2's shape. Its bugs are already paid for. ⚠ It does **NOT build** — you can read it, never run it |
| **`maxon-sharp` (bootstrap) — the RUNNABLE ORACLE** | Different language (C#), but it **builds, runs, and is canonical for `/specs`**. The one you can execute on a sample program, `dump_ir` (`dumpStages: true`, csharp-only), and diff behaviour against. **When the question is "what should this actually DO?", the bootstrap answers by RUNNING — v1 can only answer by reading** |

**Where the two disagree, that IS the design question** — resolve it in the plan, not in the wave.

### ⚠ READ them for the knowledge. Do not BLINDLY COPY them.

Their knowledge — the mechanism's real shape, the edge cases, the traps — is expensive and already paid
for, and **none of it is worth re-deriving.** But **shv2 is a deliberate rewrite, and a number of things
it does are BETTER.** Where shv2 departs, **the departure IS the thesis**, and the reference is merely
how the old one happened to do it.

**So the plan must justify BOTH directions: a divergence needs a reason, and a copy needs one too** —
*"it works in v1"* is not one. Two concrete traps:

- **v1 is debugged, not FAST.** Its regalloc was ~74% of self-compile time; **port an algorithm and you
  port its cost curve.** *(The clearest "leave it": the register allocator ports **LESSONS, not code** —
  shv2's is a deliberately different, linear, SSA-chordal design. Keep v1's correctness traps, not its
  reactive spill loop.)*
- **The bootstrap's code cannot be transliterated** — it borrows and retains-on-store where the
  self-hosted tier consumes. Same stdlib, different obligations.

**The shv2 differences the reference will not have** — block args, not phi nodes; parser-minted
`ValueId`s, not name strings; **Maxon → Std → Target** (3 tiers, no MIR); static ownership from commit 1;
the flat `StdOp`; `project.diagnostics` first-class; `FileParseArtifact` staging. **A port that
reintroduces one of these is a regression, not a port.**

## What every implementer brief MUST carry

- **(a) the reference targets from the plan** — the specific **v1 file + line numbers** to READ, the
  **`maxon-sharp` file** that shows the behaviour running, **what to TAKE and what to LEAVE**, and the
  **shv2 differences to design to**. Say plainly where the reference is *wrong for shv2*, so the agent
  does not "fix" its own correct code to match it;
- **(b) the exclusive file list**, and the files it must NOT touch;
- **(c) the traps** for that area;
- **(d) its share of the plan's SPEC PORT LIST** — the exact `/specs` files to copy in, and **which
  cases it must unlock vs which stay `disabled-test:`** (on demand — the corpus is **not**
  bulk-ported). The agent executes this list; it does not choose its own coverage, and **a case it
  should pass is never disabled**;
- **(e) reproduced evidence** for every bug it is asked to fix, **captured as a failing spec wherever
  one can be** — hand the agent the RED, so its contract is *"make this spec green"*, not *"fix, then
  stash to prove you fixed it."* **Never hand an agent a symptom you have not seen yourself.**

**If an agent finds the plan is wrong when it reaches the code, it STOPS and reports** — it does not
silently redesign. The plan is a contract too, and a plan that survives contact only because nobody said
otherwise is worth nothing.

## ⛔ NEVER ASK AN AGENT TO HAND-APPROXIMATE A GATE THE LOOP ALREADY RUNS

**Brief for DIAGNOSIS, never for COVERAGE.**

| you might be tempted to ask for | the gate that already does it, better |
|---|---|
| "enumerate every construct that could false-reject and prove each parses" | the **full unfiltered suite** |
| "confirm no other spec regressed" | the same run — that is why it is unfiltered |
| "check no case was left disabled" | the spec-marker count |
| "confirm you stayed in bounds / the diff is minimal" | **you**, reading `git diff` and `git status` — seconds of work |

⚠ **A specific instruction in your brief OUTRANKS the agent's own standing rules.** A brief ending
*"…and prove these twelve constructs still parse"* silently repeals its own stop rule, because a
concrete task always beats a general one. **You cannot brief thoroughness into an agent without briefing
the stop rule out of it.**

**A few targeted probes at the exact position changed are fine and worth asking for. A sweep is not.**
