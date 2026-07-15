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
to the user.** A wrong approach caught here costs a paragraph; caught in the wave it costs the wave. The
plan is yours — fan out read-only survey agents to do the reading if it is large, but you own what it
says.

> **Two reference compilers already implement what you are about to build. They answer DIFFERENT
> questions, and the plan must consult BOTH.**
>
> | | |
> |---|---|
> | **`maxon-selfhosted` (v1) — the CODE** | **191,487 lines of working, debugged Maxon**, same language, same `stdlib/`, closest to shv2's shape. **This is what you PORT**; its bugs are already paid for. ⚠ It does **NOT build** — you can read it, never run it |
> | **`maxon-sharp` (bootstrap) — the RUNNABLE ORACLE** | Different language (C#), but it **builds, runs, and is canonical for `/specs`**. It is the one you can execute on a sample program, `dump_ir` (`dumpStages: true`, csharp-only), and diff behaviour against. **When the question is "what should this actually DO?", the bootstrap answers by RUNNING — v1 can only answer by reading** |
>
> **Where the two disagree, that IS the design question** — resolve it in the plan, not in the wave.

The plan must name, per layer:

- **the v1 file + line ranges** that already implement this, and **the `maxon-sharp` file(s)** — plus
  **every divergence from them, with its reason.** A divergence is a decision.
  *(⚠ Exception: the register allocator ports **LESSONS, not code** — shv2's is a deliberately different,
  linear, SSA-chordal design. Do not drag v1's in.)*
- **the shv2 differences to adapt to** — no MIR tier (**Maxon → Std → Target**, 3 not 4);
  `project.diagnostics` is first-class; `FileParseArtifact` staging; the flat `StdOp`.
- **the new IR ops needed** → these ARE the contract (step 3).
- **the exclusive file list per agent** → steps 4–5. One file, one owner.
- **the `/specs` files to port as acceptance tests** → 5(d).
- **the RED baseline** — which specs fail today, and how → 5(e).

**A rung may be too big for one wave, and the survey is what tells you.** If it is, **SLICE it and say
so**: land the cheap, high-unlock, low-risk part first (P1.0d's front-end slice unlocked 1080 corpus
cases with no new IR ops and no codegen), and keep the deep part (a new register bank for floats) as its
own slice. **Each slice runs the full loop below.**

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
- **(a) the port targets from the plan** — the specific **v1 file + line numbers** to PORT, the
  **`maxon-sharp` file** that shows the behaviour running, and the **shv2 divergences to adapt to** —
  *except* where the design deliberately departs (the register allocator ports lessons, not code);
- **(b) the exclusive file list**, and the files it must NOT touch;
- **(c) the traps** for that area;
- **(d) the `/specs` files to port as its acceptance tests** (on demand — the corpus is **not**
  bulk-ported);
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
