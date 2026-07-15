---
name: rung
description: Implement one rung of maxon-shv2/PLAN.md end to end — plan, contract, worktree-isolated implementer, independent review, optimization pass, gate battery, rebase, fast-forward merge, push. Use whenever asked to implement a milestone, phase, or rung of the shv2 plan.
---

# Run one rung of the plan

You are the **coordinator**. You do not write the rung; you own the plan, the contract, the integration,
and the verification. Agents own layers.

> **The two rules that make parallel agents net-positive**
> 1. **One file, one owner, per wave.** Never let two agents hold the same file.
> 2. **The coordinator writes the PLAN and the CONTRACT before the wave launches** — the dialect ops
>    (`MaxonDialect` / `StdDialect` / `TargetDialect` + the `*OpMeta` backing) and a concrete golden-IR
>    example. **Agents coding against a contract that is still moving is the failure mode that makes
>    parallel agents net-negative.**

Integration is inherently serial and is the real limit on wave size: **beyond ~4–5 agents, integration
dominates and adding agents makes it slower.**

## 0. Which rung, and orient

**If an argument was given** (`/rung P1.2`, `/rung structs`, `/rung fix the divide-by-zero trap`), that
is the rung. **If NOT, pick the next one from `maxon-shv2/PLAN.md`'s ladder** — it is the source of
truth for what is next, and it is kept current precisely so that the no-argument form works. State which
rung you picked and why **before** doing anything, so the user can redirect you cheaply.

Then read `maxon-shv2/ARCHITECTURE.md` (design pillars, core invariants) and the relevant PLAN.md
sections.

`git fetch origin` and rebase — **optimization work runs in a different repo in parallel** and lands
upstream, so local `main` goes stale between rungs.

## 1. Establish the baseline YOURSELF

Never start from a claimed-green tree. Build and run:

```
./bin/maxon.exe build maxon-shv2
./maxon-shv2/.maxon/maxon-shv2.exe spec-test --workers=12     # expect all green
```

## 2. PLAN IT — read BOTH reference compilers before you design anything

**Write a detailed implementation plan BEFORE the contract and before any agent launches, and state it
to the user.** A wrong approach caught here costs a paragraph; caught in the wave it costs the wave.

**Delegate the READING; keep the JUDGMENT.** The survey is big — 191k lines of v1, the bootstrap, ~2,700
spec cases — and doing it inline eats the context you will need for **integration, which is the serial
bottleneck.** So fan out **read-only** survey agents (one per reference, one over `/specs`) and have them
return **FACTS**: file + line ranges, how it works, what it costs, which specs exist and what they cover.
**The decisions stay yours** — take vs leave, the IR ops, the spec port list, the slicing call — because
an agent deep in v1 is the *worst* placed to judge whether shv2 should copy it, and because **you** are
the one who must state this plan to the user and integrate against it. **You own what it says.**

> **Two reference compilers already implement what you are about to build. They answer DIFFERENT
> questions, and the plan must consult BOTH.**
>
> | | |
> |---|---|
> | **`maxon-selfhosted` (v1) — the closest CODE** | **191,487 lines of working, debugged Maxon**, same language, same `stdlib/`, closest to shv2's shape. Its bugs are already paid for. ⚠ It does **NOT build** — you can read it, never run it |
> | **`maxon-sharp` (bootstrap) — the RUNNABLE ORACLE** | Different language (C#), but it **builds, runs, and is canonical for `/specs`**. It is the one you can execute on a sample program, `dump_ir` (`dumpStages: true`, csharp-only), and diff behaviour against. **When the question is "what should this actually DO?", the bootstrap answers by RUNNING — v1 can only answer by reading** |
>
> **Where the two disagree, that IS the design question** — resolve it in the plan, not in the wave.
>
> ### ⚠ READ them for the knowledge. Do not BLINDLY COPY them.
>
> Their knowledge — the mechanism's real shape, the edge cases, the traps — is expensive and already
> paid for, and **none of it is worth re-deriving.** But **shv2 is a deliberate rewrite, and a number of
> things it does are BETTER.** Where shv2 departs, **the departure IS the thesis**, and the reference is
> merely how the old one happened to do it. **So the plan must justify BOTH directions:** a divergence
> needs a reason, and **a copy needs one too** — *"it works in v1"* is not one. Two concrete traps:
> **v1 is debugged, not FAST** (its regalloc was ~74% of self-compile; port an algorithm and you port its
> cost curve), and **the bootstrap's code cannot be transliterated** (it borrows and retains-on-store
> where the self-hosted tier consumes — same stdlib, different obligations).

The plan must name, per layer:

- **the v1 file + line ranges** that already implement this, and **the `maxon-sharp` file(s)** — plus,
  for each, **what to TAKE and what to LEAVE, with the reason.** Both are decisions.
  *(⚠ The clearest "leave it": the register allocator ports **LESSONS, not code** — shv2's is a
  deliberately different, linear, SSA-chordal design. Keep v1's correctness traps, not its reactive
  spill loop.)*
- **the shv2 differences — the rewrite's THESIS, which the reference will not have** — block args, not
  phi nodes; parser-minted `ValueId`s, not name strings; **Maxon → Std → Target** (3 tiers, no MIR);
  static ownership from commit 1; the flat `StdOp`; `project.diagnostics` first-class;
  `FileParseArtifact` staging. **A port that reintroduces one of these is a regression, not a port.**
- **the new IR ops needed** → these ARE the contract (step 3).
- **the exclusive file list per agent** → steps 4–5. One file, one owner.
- **the RED baseline for every BUG the rung fixes** — reproduced by you, captured as a failing spec → 5(e).

### ⭐ The SPEC PORT LIST — name the `/specs` files, and what each one unlocks

**The plan MUST list the exact `/specs` files this rung ports into `specs-shv2/`**, and per file, **which
cases the rung UNLOCKS versus which stay `disabled-test:`, and on which later rung.** That list is the
rung's acceptance criteria *and* its deliverable (step 11).

**It is the COORDINATOR's call, not the agent's.** An agent left to choose its own coverage tests what it
remembered — which is exactly how a "finished" scalar core scored **48 of 2,746** (see the closing
section). Survey `/specs` yourself and hand the list down.

- **Port REAL specs, never invented ones.** The corpus is **not** bulk-ported: the rung copies exactly the
  files it needs, **byte-identical**, and the agent's only sanctioned edit is the marker flip → 5(d).
- **The list must go RED before the wave.** Run the candidates against today's compiler and watch them
  fail — that IS the rung's red baseline, and **the rung is DONE when they go green.**
- **Never plan to disable a case the rung should pass.** For each one that stays disabled, name the
  **missing mechanism** and the rung that supplies it.

**A rung may be too big for one wave, and the survey — the length of that spec list included — is what
tells you.** If it is, **SLICE it and say so**: land the cheap, high-unlock, low-risk part first (P1.0d's
front-end slice unlocked 1080 corpus cases with no new IR ops and no codegen), and keep the deep part (a
new register bank for floats) as its own slice. **Each slice runs the full loop below.**

## 3. Write the contract (if the rung needs new IR ops)

Land the dialect ops **before** launching the wave. Hand agents a golden-IR example for a sample
program. If the rung is purely front-end, say so and skip.

## 4. Set up isolation

```
git worktree add ../maxon-<rung> -b <rung>-<slug>
cp -r bin ../maxon-<rung>/bin        # bin/ is GITIGNORED — a worktree has no compiler without this
```

## 5. Implement — `maxon-rung-implementer`

**The brief is the PLAN, sliced per agent.** The reference survey is already done (step 2) — hand each
agent its share of it rather than making it re-derive one.

Every brief MUST carry:
- **(a) the reference targets from the plan** — the specific **v1 file + line numbers** to READ, the
  **`maxon-sharp` file** that shows the behaviour running, **what to TAKE and what to LEAVE**, and the
  **shv2 differences to design to**. Say plainly where the reference is *wrong for shv2*, so the agent
  does not "fix" its own correct code to match it;
- **(b) the exclusive file list**, and the files it must NOT touch;
- **(c) the traps** for that area;
- **(d) its share of the plan's SPEC PORT LIST** — the exact `/specs` files to copy in, and **which cases
  it must unlock vs which stay `disabled-test:`** (on demand — the corpus is **not** bulk-ported). The
  agent executes this list; it does not get to choose its own coverage, and **a case it should pass is
  never disabled**;
- **(e) reproduced evidence** for every bug it is asked to fix, **captured as a failing spec wherever
  one can be** — hand the agent the RED, so its contract is "make this spec green," not "fix, then stash
  to prove you fixed it." Never hand an agent a symptom you have not seen yourself.

**If an agent finds the plan is wrong when it reaches the code, it STOPS and reports** — it does not
silently redesign. The plan is a contract too, and a plan that survives contact only because nobody said
otherwise is worth nothing.

## 6. Optimize — `maxon-rung-optimizer`

Hunts **unscalable (superlinear) algorithms**. Gated objectively by `scale-test`, which fits a growth
exponent to every phase's time **and** allocations. Commits separately on the same branch.

## 7. Review — `maxon-rung-reviewer`

Hunts **duplication** first, then latent bugs. Commits separately on the same branch.

> **⚠ Optimize BEFORE you review, and never the other way round.** An optimizer *rewrites code*, so it
> can introduce exactly the duplication the review exists to catch — a fast path forked from a slow one,
> a helper inlined at three call sites. **The duplication-focused review must be the LAST quality gate
> before the merge**, and it reviews the optimizer's diff as well as the implementer's.

**Both are mandatory, and both must be agents that did not write the code** (user directive). The
independence is the point: the P1.0a review found two resource leaks and a cross-process duplicated
selection rule that the author, re-reading their own work, had not seen.

## 8. VERIFY THE AGENT'S CLAIMS YOURSELF

**Do not trust the report.** Re-run the gates in the worktree, and read the crux files. An agent in this
project once left work uncommitted in a worktree based on a stale parent; another claimed a green build
by grepping for a success string.

**Check exit codes. Never grep for success.** Exit **101** = memory leak.

## 9. The gate battery

| Gate | |
|---|---|
| Build | exit 0, zero warnings |
| shv2 suite | all green, **including every pre-existing test** |
| Worker-count invariance | `--workers=1` and `--workers=12` stdout **byte-identical** |
| Fragments | `git status --short specs-shv2/fragments/` — **additions only**. An **`M`** is a codegen change: justify or fix. Empty diff after a spec run **proves byte-identical codegen** |
| `scale-test` | ⚠ **NOT A GATE — it is an INSTRUMENT with no verdict.** Run it after any change to a pass, the IR, or a data structure the compiler indexes by, and **read it**: the per-rung memory numbers are exact and bit-for-bit reproducible, so any movement is real. **Explain and attribute what moved**, and record the reason in `docs/optimization-log.md` — the trend table is the deliverable. There is nothing to "pass"; do not chase one, and never touch the instrument to make a number look better |
| If `maxon-sharp/` was touched | C# suite green (**2883+**) **AND codegen neutrality**: `git status --short specs/ specs-shv2/` EMPTY |
| Leak gate | no run exits **101** |

## 10. Land it — linear history, then push

```
git fetch origin
git rebase --onto main <old-base> <branch>     # rebase the branch, do NOT merge-commit
git checkout main && git merge --ff-only <branch>
```
`merge.ff=only` is configured, so a non-fast-forward merge **errors** rather than making a merge commit.

Re-run the suites on the merged tree, then **`git push origin main`** — the parallel repo consumes it.
Remove the worktree and delete the branch.

## 11. Close the loop

Update `maxon-shv2/PLAN.md` (a rung's deliverable is the set of `disabled-test:` markers it flipped to
`test:`) and record anything durable in memory.

---

## The thing this process exists to catch

shv2's 126 spec tests were written **by shv2, for shv2**. Run against `/specs` — the accumulated
definition of the language, written by people who were not trying to make shv2 look good — the "finished"
scalar core scored **48 of 2,746**. *Not one of the 126 had ever used a parenthesis.*

**So: port real specs, not invented ones. Expect bugs — that is the point. And never let an agent
disable a case it should pass.**
