---
name: rung
description: Implement one rung of maxon-shv2/PLAN.md end to end — contract, worktree-isolated implementer, independent review, optimization pass, gate battery, rebase, fast-forward merge, push. Use whenever asked to implement a milestone, phase, or rung of the shv2 plan.
---

# Run one rung of the plan

You are the **coordinator**. You do not write the rung; you own the contract, the integration, and the
verification. Agents own layers.

> **The two rules that make parallel agents net-positive**
> 1. **One file, one owner, per wave.** Never let two agents hold the same file.
> 2. **The coordinator writes the CONTRACT before the wave launches** — the dialect ops
>    (`MaxonDialect` / `StdDialect` / `TargetDialect` + the `*OpMeta` backing) and a concrete golden-IR
>    example. **Agents coding against a contract that is still moving is the failure mode that makes
>    parallel agents net-negative.**

Integration is inherently serial and is the real limit on wave size: **beyond ~4–5 agents, integration
dominates and adding agents makes it slower.**

## 0. Orient

Read `maxon-shv2/PLAN.md` (the ladder, and which rung is next) and `maxon-shv2/ARCHITECTURE.md`.
`git fetch origin` and rebase — **optimization work runs in a different repo in parallel** and lands
upstream.

## 1. Establish the baseline YOURSELF

Never start from a claimed-green tree. Build and run:

```
./bin/maxon.exe build maxon-shv2
./maxon-shv2/.maxon/maxon-shv2.exe spec-test --workers=12     # expect all green
```

## 2. Write the contract (if the rung needs new IR ops)

Land the dialect ops **before** launching the wave. Hand agents a golden-IR example for a sample
program. If the rung is purely front-end, say so and skip.

## 3. Set up isolation

```
git worktree add ../maxon-<rung> -b <rung>-<slug>
cp -r bin ../maxon-<rung>/bin        # bin/ is GITIGNORED — a worktree has no compiler without this
```

## 4. Implement — `maxon-rung-implementer`

Every brief MUST carry:
- **(a) the specific v1 file to PORT**, with line numbers, and the **shv2 divergences to adapt to** —
  *except* where the design deliberately departs (the register allocator ports lessons, not code);
- **(b) the exclusive file list**, and the files it must NOT touch;
- **(c) the traps** for that area;
- **(d) the `/specs` files to port as its acceptance tests** (on demand — the corpus is **not**
  bulk-ported);
- **(e) reproduced evidence** for every bug it is asked to fix. Never hand an agent a symptom you have
  not seen yourself.

## 5. Optimize — `maxon-rung-optimizer`

Hunts **unscalable (superlinear) algorithms**. Gated objectively by `scale-test`, which fits a growth
exponent to every phase's time **and** allocations. Commits separately on the same branch.

## 6. Review — `maxon-rung-reviewer`

Hunts **duplication** first, then latent bugs. Commits separately on the same branch.

> **⚠ Optimize BEFORE you review, and never the other way round.** An optimizer *rewrites code*, so it
> can introduce exactly the duplication the review exists to catch — a fast path forked from a slow one,
> a helper inlined at three call sites. **The duplication-focused review must be the LAST quality gate
> before the merge**, and it reviews the optimizer's diff as well as the implementer's.

**Both are mandatory, and both must be agents that did not write the code** (user directive). The
independence is the point: the P1.0a review found two resource leaks and a cross-process duplicated
selection rule that the author, re-reading their own work, had not seen.

## 7. VERIFY THE AGENT'S CLAIMS YOURSELF

**Do not trust the report.** Re-run the gates in the worktree, and read the crux files. An agent in this
project once left work uncommitted in a worktree based on a stale parent; another claimed a green build
by grepping for a success string.

**Check exit codes. Never grep for success.** Exit **101** = memory leak.

## 8. The gate battery

| Gate | |
|---|---|
| Build | exit 0, zero warnings |
| shv2 suite | all green, **including every pre-existing test** |
| Worker-count invariance | `--workers=1` and `--workers=12` stdout **byte-identical** |
| Fragments | `git status --short specs-shv2/fragments/` — **additions only**. An **`M`** is a codegen change: justify or fix. Empty diff after a spec run **proves byte-identical codegen** |
| `scale-test` | **PASS** — mandatory after any change to a pass, the IR, or a data structure the compiler indexes by. VOID/NOISY are not passes |
| If `maxon-sharp/` was touched | C# suite green (**2883+**) **AND codegen neutrality**: `git status --short specs/ specs-shv2/` EMPTY |
| Leak gate | no run exits **101** |

## 9. Land it — linear history, then push

```
git fetch origin
git rebase --onto main <old-base> <branch>     # rebase the branch, do NOT merge-commit
git checkout main && git merge --ff-only <branch>
```
`merge.ff=only` is configured, so a non-fast-forward merge **errors** rather than making a merge commit.

Re-run the suites on the merged tree, then **`git push origin main`** — the parallel repo consumes it.
Remove the worktree and delete the branch.

## 10. Close the loop

Update `maxon-shv2/PLAN.md` (a rung's deliverable is the set of `disabled-test:` markers it flipped to
`test:`) and record anything durable in memory.

---

## The thing this process exists to catch

shv2's 126 spec tests were written **by shv2, for shv2**. Run against `/specs` — the accumulated
definition of the language, written by people who were not trying to make shv2 look good — the "finished"
scalar core scored **48 of 2,746**. *Not one of the 126 had ever used a parenthesis.*

**So: port real specs, not invented ones. Expect bugs — that is the point. And never let an agent
disable a case it should pass.**
