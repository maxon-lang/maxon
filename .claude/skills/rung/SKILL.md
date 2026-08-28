---
name: rung
description: Implement one rung of maxon-shv2/PLAN.md end to end — claim, plan, contract, worktree-isolated implementer, scale-test ladder read (optimizer agent on trigger), independent review, gate battery, cross-target gate, merge, push, board closure. The mechanical halves are two scripts (rung-start.sh / rung-finish.sh); what is left is the judgement. Prefer a WIDER WAVE over slicing. Use whenever asked to implement a milestone, phase, or rung of the shv2 plan. Also runs in SLICE mode for parallel agents with no outer coordinator — claim a row on PLAN.md's 🧭 SLICE BOARD (the push is the lock). Invoke as `/rung <row-id>` (e.g. `/rung G1`) to take a specific row.
---

# Run one rung of the plan

You are the **coordinator**. You do not write the rung; you own the plan, the contract, the integration
and the verification. Agents own layers. **Two scripts own the bookkeeping.**

> **The two rules that make parallel agents net-positive**
> 1. **One file, one owner, per wave.** Never let two agents hold the same file.
> 2. **The coordinator writes the PLAN and the CONTRACT before the wave launches.** Agents coding
>    against a contract that is still moving is the failure mode that makes parallel agents
>    net-negative.

Integration is inherently serial and is the real limit on wave size: **beyond ~4–5 agents, integration
dominates and adding agents makes it slower.**

## ⚙ THE MECHANICS ARE TWO SCRIPTS. Everything else in this file is judgement.

**Steps 1 and 8 are one command each.** They are order-sensitive bookkeeping with a known failure at
every step, so they are scripted rather than re-derived each rung — and each one **measures its numbers
and refuses to invent one.**

| | |
|---|---|
| **`scripts/rung-start.sh`** | claim the row · **refuse a duplicate id** · **refuse a LANE COLLISION** · push (the lock) · worktree · `cp -r bin` · build |
| **`scripts/rung-finish.sh`** | rebase FIRST · build · suite · leak · ladder row · C# lane · cross-target · `--ff-only` merge · flip the board · push · re-run the suite on a rejected push · tear down · **REAP leaked rung worktrees** |

Both take `--dry-run`. Both refuse to run outside a checkout that owns `main`. **Read what they refuse —
the refusal names the fix.**

## ⚙ EACH GATE RUNS ONCE, AND THIS TABLE SAYS WHERE

**Every gate below used to run in two to five places**, because each agent's brief independently told it
to be thorough. Nobody was wrong locally and the rung paid for all of it. *(Counted over a 3-agent wave:
the full suite ran **five** times — three implementers, the reviewer, the coordinator — and `scale-test`
up to **four**. Exactly one of each gated anything.)*

| Work | Runs | Where | Everywhere else |
|---|---|---|---|
| **filtered specs** | many | **each agent's fast loop** | this is the loop, not a gate |
| **full shv2 suite** | **once** | `rung-finish.sh` | ⛔ the reviewer must NOT — it finishes minutes before the battery, on the identical tree. The implementer keeps ONE at its finish: a different, pre-integration tree, where breakage is cheapest to fix |
| **`scale-test` ladder** | **once** | **you, §5a** — plus the optimizer's before/after, which is its instrument, not a check | ⛔ the implementer must NOT — a reading on a pre-review, pre-optimizer tree attributes nothing |
| **C# suite + neutrality** | twice, deliberately | implementer (fast feedback), `rung-finish.sh` (authoritative, merged tree) | — |
| **cross-target** | **once** | `rung-finish.sh` | — |

**When an agent has a suspicion it cannot cheaply confirm, it says so in its report.** A sentence from
the agent that saw it, feeding a gate that is going to run anyway, beats a run nobody can attribute.

⚠ **And the operating rules — redirect-to-a-file, never `--workers=1`, never `fmt` with arguments —
live in `.claude/CLAUDE.md`, once.** They were copied into three agent briefs apiece; every agent
already loads CLAUDE.md, so those copies were pure context cost that drifted.

## ⛔ HALT AND ASK — the things that are NOT yours to decide

**This skill is built to run unattended, rung after rung.** That stays safe only if it stops at the
right things. **The danger is never one rung failing — it is a rung landing WRONG and the next rung
building on top of it.** *(The scalar core was **claimed** done at 126/0. Measured against the real
corpus it was **48 of 2,746**. A loop would have marched straight past that and built P1.1 on it.)*

**STOP, report, and ask when:**

- **Any gate is red** — a non-zero build, a non-green suite, exit **101**, or an unjustified **`M`** on a
  pre-existing fragment. **Never turn a gate green by narrowing what it tests.**
  ⚠ **A lane that did not RUN is not a red gate.** Remote **arm64** is outside the rung gate entirely —
  an unreachable Mac or a skipped target is a **SKIP you report**, never a HALT and never a reason to
  hold a rung open. Red means *ran and failed*.
- **A reachable DEFECT in this rung's own mechanism — a WRONG ANSWER as much as a leak — even one the
  SUITE is GREEN over.** It is **FIXED**, or the causing construct is **cleanly REJECTED**, before merge
  — **NEVER deferred.** See `reference/deferral.md`.
- **A DESIGN RULING is needed** — the corpus contradicts itself, the two references disagree and the
  plan cannot settle it, or the spec is genuinely ambiguous. **You must not guess.** *(`/specs` said both
  "lossy conversions are not allowed" **and** `takeInt(3.7)` ⇒ silently `3`, and the bootstrap passed
  BOTH. Either reading was defensible. It took a user ruling.)*
- **An agent reports the plan was wrong at the code** — that invalidates the wave, not just that agent.
- **A case would have to be disabled that the rung should pass** — the failure mode of this entire
  process. **A green suite that tests nothing is the most expensive lie a test runner can tell.**
- **The rung needs SLICING and the boundary is not obvious.**
- **`PLAN.md`'s next rung is ambiguous** — the no-argument form depends on that ladder being current.
  Say so; do not pick for yourself.

**Everything else runs unattended.** Landing a clean rung — the plan, the wave, the gates, the merge,
the push and the PLAN.md update — needs no permission. Report what you did; ask only when the list
above fires.

⭐ **FIX, don't FILE.** A finding that genuinely cannot ride this rung becomes a numbered entry in
`PLAN.md`, and that is **your** call in the plan, never an agent's mid-rung escape. **A step with
residuals is not complete — it is ◑, not ✅.** Read `reference/deferral.md` before deferring anything.

---

## 0. Which rung, and orient

**If an argument was given** (`/rung BATCH14`, `/rung P1.2`, `/rung fix the divide-by-zero trap`), that
is the rung. **If NOT, pick the next one from `maxon-shv2/PLAN.md`** — the ladder for a sequential rung,
the **🧭 SLICE BOARD** for a claimable row. State which you picked and why **before** doing anything, so
the user can redirect you cheaply.

> ### TWO MODES — and the difference is whether an OUTER coordinator exists
>
> - **WAVE mode.** You are the only coordinator. You slice the rung yourself, hand each sub-agent an
>   exclusive file list, and integrate. Nobody else is touching `main`. Pass `--wave` to both scripts.
> - **SLICE mode.** The board exists, several instances of this skill run at once with no outer
>   coordinator, **each in its OWN CLONE**. You own **exactly one board row**. Inside your slice you are
>   still the coordinator and every step below still applies — but you no longer own `main`, you share
>   it. **Read `reference/slice-mode.md`.**
>
> **You are in SLICE mode if the board has a claimable row**, or if you were invoked with a row id.

Then read `maxon-shv2/ARCHITECTURE.md` (design pillars, core invariants) and the relevant PLAN.md
sections.

## 1. Claim it and isolate it — `scripts/rung-start.sh`

```bash
scripts/rung-start.sh --batch BATCH14 --slug <kebab-slug> --message "<one line>"
#   add --wave for WAVE mode (no board, no claim — worktree and build only)
#   --dry-run runs every check and writes nothing
```

It refuses a duplicate row id, a non-free row, a **lane collision**, a dirty tree, and a claim attempted
from a worktree. On a rejected push it **re-reads the board** rather than forcing. Then it creates the
worktree, copies the gitignored `bin/` in, and builds.

**Announce the claim SHA it prints.** That is the claim's evidence.

⚠ **In the worktree, every `maxon-dev` MCP call needs `repoRoot`** — the script prints the exact line.
Omit it and you are told `success: true` about a tree containing none of your work.

> ### ⚠ ALWAYS BUILD. The SUITE is the part you may skip — never the BUILD.
> Both binaries are gitignored and nothing rebuilds them. *(Measured 2026-07-27 on a clean `main`: the
> tree's `maxon-shv2.exe` read **71 FAILED**; a 13 s rebuild read **1922/0**.)* The build is 13 s.
>
> **There is NO baseline suite run**, and that is deliberate. The gate at the end is **`failed: 0`**,
> not an arithmetic delta from a remembered total — a green suite is green whatever it started at, and
> other agents land between and during rungs. ⇒ **When `rung-finish.sh` comes back RED you may not
> assume the red is yours.** Attribute it: do the failures touch what you changed? The script measures

## 2. PLAN IT — read BOTH reference compilers before you design anything

**Write a detailed implementation plan BEFORE the contract and before any agent launches, and state it
to the user.** A wrong approach caught here costs a paragraph; caught in the wave it costs the wave.

**Delegate the READING; keep the JUDGMENT.** The survey is big — 191k lines of v1, the bootstrap, ~2,700
spec cases — and doing it inline eats the context you need for **integration, the serial bottleneck.**
Fan out **read-only** survey agents (one per reference, one over `/specs`) and have them return
**FACTS**: file + line ranges, how it works, what it costs, which specs exist. **The decisions stay
yours** — an agent deep in v1 is the *worst* placed to judge whether shv2 should copy it.

⇒ **`reference/agents.md`** — which model each role runs on and why, what the two references are for,
the one-survey-per-rung-family rule, and what every brief must carry.

The plan must name, per layer:

- **the v1 file + line ranges** and **the `maxon-sharp` file(s)** — plus **what to TAKE and what to
  LEAVE, with the reason.** Both are decisions.
- **the shv2 differences — the rewrite's THESIS**, which the reference will not have.
- **the new IR ops needed** → these ARE the contract (§3).
- **the exclusive file list per agent** → §4. One file, one owner.
- **the RED baseline for every BUG the rung fixes** — reproduced by you, captured as a failing spec.

### ⭐ The SPEC PORT LIST — name the `/specs` files, and what each one unlocks

**The plan MUST list the exact `/specs` files this rung ports into `specs-shv2/`**, and per file, **which
cases the rung UNLOCKS versus which stay `disabled-test:`, and on which later rung.** That list is the
rung's acceptance criteria *and* its deliverable.

**It is the COORDINATOR's call, not the agent's.** An agent left to choose its own coverage tests what it
remembered — which is exactly how a "finished" scalar core scored **48 of 2,746**.

- **Port REAL specs, never invented ones**, byte-identical; the agent's only sanctioned edit is the
  marker flip.
- **The list must go RED before the wave.** Run the candidates against today's compiler and watch them
  fail — that IS the rung's red baseline, and **the rung is DONE when they go green.**
- **Never plan to disable a case the rung should pass.** For each one that stays disabled, name the
  **missing mechanism** and the rung that supplies it.

### ⭐⭐ WIDEN THE WAVE BEFORE YOU SLICE

**A slice is the most expensive thing this process can buy** — it multiplies the entire loop, while one
more agent in the same wave costs one more brief. **Default to ONE rung with a WIDER WAVE.** There are
exactly three reasons to slice and four common non-reasons: **`reference/slicing.md`**, which also holds
the BATCH rules.

## 3. Write the contract (if the rung needs new IR ops)

Land the dialect ops **before** launching the wave. Hand agents a golden-IR example for a sample
program. If the rung is purely front-end, say so and skip.

## 4. Implement — `maxon-rung-implementer`

**The brief is the PLAN, sliced per agent.** The survey is already done — hand each agent its share
rather than making it re-derive one. **The five things every brief must carry, and the ⛔ on briefing
coverage instead of diagnosis, are in `reference/agents.md`.**

## 5. The ladder read (ALWAYS) and the optimizer agent (ON A TRIGGER)

**Two different things, and only one belongs on every rung.**

**5a. The `scale-test` read — ALWAYS, and it is YOURS (≈17 s).** Run it, read the doubling ladder, and
record what moved and WHY in `docs/optimization-log.md`. This never batches, and the reason is not
thoroughness — **attribution is only available now.** The instrument sees exactly WHAT moved and can
never see why; ten rungs later, neither can you. `rung-finish.sh` refuses to close a rung that wrote no
row. ⇒ **`reference/gates.md`** for how to read it (and why there is no green one).

**5b. The `maxon-rung-optimizer` AGENT — when a trigger fires.** A full superlinear hunt over a rung
that added no algorithm has nothing to find. Spend the agent when:

- the rung adds **a pass**, **an IR op**, or **a collection the compiler indexes by**;
- **5a's ALLOCATION ladder shows a ratio ≳ 2.4 per doubling that you cannot explain.** ⚠ Read the
  trigger off the **ALLOCATION** column, not CPU: allocations are exact and bit-for-bit reproducible, so
  a bend there is a fact, while a single CPU sample's per-phase exponents wobble up to **±0.5** run to
  run (`pruneDeadBlockArgs` read 1.91 then 2.41 on the same unchanged compiler). **If the bend is
  CPU-only, re-run with `--repeat=3` and confirm it reproduces before spending the agent**;
- the rung touches something PLAN.md's **"Measured debt"** list names a re-measure trigger for.

Otherwise: **say in the rung report that no trigger fired**, and carry the hunt to the phase-boundary
sweep (§10).

⚠ **That is a batching decision, not a lower bar. It does not touch the FIX rule:** a superlinearity you
can *trigger on a realistic input* is still **fixed, not filed**, whoever finds it and whenever. The two
biggest finds in this repo's history — the `regalloc:splitting` quadratic and the cascade fixpoint duals
— were both **read off the ladder**, which is the part that stays per rung.

When the agent runs, it commits separately on the same branch.

## 6. Review — `maxon-rung-reviewer`

Hunts **duplication** first, then latent bugs. Commits separately on the same branch.

**THE REVIEW IS MANDATORY ON EVERY RUNG — it does not batch, it is not triggered, and it is never
skipped**, and it must be an agent that did **not** write the code. **Optimize BEFORE you review, never
the other way round.** The list of what this gate has actually caught is in `reference/agents.md`.

## 7. VERIFY THE AGENT'S CLAIMS YOURSELF

**Do not trust the report.** Read the crux files and re-run the crux filters. An agent in this project
once left work uncommitted in a worktree based on a stale parent; another claimed a green build by
grepping for a success string. **Check exit codes. Never grep for success.** Exit **101** = leak.

**The full battery is `rung-finish.sh`'s, and it runs ONCE** — your independent verification and the
pre-merge gate at the same time, not a second run stacked on the agents'. The agents iterate on
`--filter` and prove their own slice; the full suite and the cross-target matrix are the
script's, run once on the final tree. If it comes back red, an agent goes back.

### ⭐⭐ THE SPEC SUITE IS THE TESTING MECHANISM. A hand-run snippet is DISCOVERY, never EVIDENCE.

**Probing is how the leak gate and the reachable-defect rule find things** — a `let m = f()` no
committed test runs is exactly what to go looking for, and `run_program` is the right tool for the
looking. **But the probe is where the work STARTS, not where it ends:**

> **A probe that finds something becomes a SPEC. A probe that finds nothing becomes a spec, or it never
> happened.**

- **A defect found by hand is reproduced as a failing spec case FIRST** — that is the RED — and the fix
  is proven when that case goes GREEN. A snippet proves the bug is gone from your terminal; a spec
  proves it is gone from every future rung.
- **"I verified it manually" is not a gate result and does not appear in a rung report.** It is
  unreviewable, unrepeatable, and invisible to the cross-target lanes — a hand-run x64 snippet says
  nothing about wasm or the Linux ELF, whereas a spec case runs on all three for free.
- **The same holds for an agent's claims.** "I confirmed X by running a test program" is an anecdote.
  **Ask where the case is.** If the behaviour is worth checking twice it is worth a spec; if it is not
  worth a spec it was not worth checking.
- ⚠ **The exception is genuinely un-spec-able signal, and it is narrow:** a scaling measurement (§5a), a
  `dump_ir` read while forming a hypothesis, a debugger session. Those inform the work; they never
  *stand in* for a case.

**Why this is a speed issue and not just a rigor one:** hand-testing is re-run by hand on every
iteration, by every agent, forever, and decays to nothing when the session ends. A spec case is written
once and then runs 1,900-strong in 17 seconds, on three targets, unattended, for the rest of the
project. **Manual testing is the slowest possible way to check the same thing twice.**

## 8. Land it — `scripts/rung-finish.sh`

**Write the PLAN.md DETAIL ROW first**, uncommitted, in the main checkout. The script flips the board's
status cells on top of it and lands both in one commit — and **refuses if PLAN.md carries no
uncommitted change**, because a rung that wrote no detail is not closed.

```bash
scripts/rung-finish.sh --batch BATCH14 --message-file temp/closure.txt \
    [--no-ladder-row "<reason>"] [--branch <name> for --wave] [--dry-run]
```

It rebases **before** the suite, runs the full battery and the cross-target gate, merges `--ff-only`,
flips the batch row **and its members**, commits, pushes — and on a rejected push **re-runs the whole
suite** rather than retrying, because the tree changed under you.

**What each gate means when it goes red — and the arm64 trade — is `reference/gates.md`.**

## 9. Close the loop

The script wrote the status cells; **the prose is yours.** A rung's deliverable is the set of
`disabled-test:` markers it flipped to `test:`.

- **Mark the rung done ONLY if it has no open residuals.** If the plan sanctioned any deferral in §2,
  the rung is **not** complete (**◑**, not ✅), and each residual must be written into the appropriate
  PLAN.md section — see `reference/deferral.md`.
- ⭐ **An unverified arm64 lane is NOT a residual and never holds a rung at ◑.** Mark it ✅ on the local
  targets and write the skip into the detail row as the gate reported it: `arm64 SKIP — remote,
  UNVERIFIED`.
- **If the rung moved codegen**, say so, and say that the arm64 goldens are therefore owed a mint at the
  periodic sync.
- Record anything durable in memory.

**Write the rung's DETAIL row. Do NOT hand-update the "Status at a glance" index — that is a
PHASE-BOUNDARY job.** The index is a second copy of facts the detail rows already hold, and maintaining
it per rung buys nothing but drift: as of 2026-07-27 its caption read *"snapshot 2026-07-22, suite head
**1162/0**"* while the detail rows said **→1906** and the tree measured **1922/0** — three spellings of
one number, which is this project's signature bug in its own plan.

## 10. At the PHASE boundary — the batched work

**Four things are deliberately NOT per-rung, because doing them per rung buys re-derivation rather than
coverage.**

| | |
|---|---|
| **The optimizer SWEEP** | One `maxon-rung-optimizer` over everything the phase landed — it sees cross-rung interactions a per-rung pass structurally cannot |
| **The PLAN.md index table** | Regenerated in one pass from the detail rows (§9) |
| **The REMOTE arm64/Mac sync** | `scripts/cross-target-gate.sh --mac --require-mac`. **A red lane here is a real defect, fixed, not filed as "the sync was red"** |
| **The stale-golden sweep** | The measured rot: 288 stale + ~489 *absent* x64-linux goldens, and 317 stale arm64 C#-suite goldens. ⚠ **A MISSING golden never fails** — absence is invisible to every gate, so it can only be found by going to look. Half of that is now machine-checked (a golden the suite MINTED is caught by the run that minted it) and half is not: a golden absent because **nobody ran that lane** has nothing to be untracked. **This sweep is what covers those two** |

⚠ **Nothing that catches a DEFECT batches.** The reviewer, the leak/probe gate, the RED spec baseline,
the host suite and the ladder read all stay per rung — a rung that lands wrong is the one failure this
process exists to prevent, and every one of those has actually fired.

---

## The thing this process exists to catch

shv2's 126 spec tests were written **by shv2, for shv2**. Run against `/specs` — the accumulated
definition of the language, written by people who were not trying to make shv2 look good — the
"finished" scalar core scored **48 of 2,746**. *Not one of the 126 had ever used a parenthesis.*

**So: port real specs, not invented ones. Expect bugs — that is the point. And never let an agent
disable a case it should pass.**
