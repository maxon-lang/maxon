---
name: rung
description: Implement one rung of maxon-shv2/PLAN.md end to end — plan, contract, worktree-isolated implementer, scale-test ladder read (optimizer agent on trigger), independent review, gate battery, cross-target gate (every LOCAL target — arm64 is remote, synced separately by hand, and is NEVER required to complete a rung), rebase, fast-forward merge, push. Prefer a WIDER WAVE over slicing. Use whenever asked to implement a milestone, phase, or rung of the shv2 plan. Also runs in SLICE mode for parallel agents with no outer coordinator — claim a row on PLAN.md's 🧭 SLICE BOARD by pushing the claim (§0a; the push is the lock), then run the rung normally. Invoke as `/rung <row-id>` (e.g. `/rung G1`) to take a specific row.
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

> ### TWO MODES — and the difference is whether an OUTER coordinator exists
>
> - **WAVE mode (the default, everything below).** You are the only coordinator. You slice the rung
>   yourself, hand each sub-agent an exclusive file list, and integrate. Nobody else is touching `main`.
> - **SLICE mode.** `maxon-shv2/PLAN.md` carries a **🧭 SLICE BOARD**, and **several instances of this
>   skill are running at once, in different repos or worktrees, with no outer coordinator.** You own
>   **exactly one board row**. Inside your slice you are still the coordinator and every step below
>   still applies — but you no longer own `main`, you share it, and **§0a is what makes that safe.**
>
> **You are in SLICE mode if the board exists and has a claimable row** — or if you were invoked with a
> row id (`/rung G1`). In SLICE mode, §0a runs **before** §0's orientation and is not optional.

## ⛔ HALT AND ASK — the things that are NOT yours to decide

**This skill is built to run unattended, rung after rung (`/loop /rung` — the no-argument form in step 0
exists for exactly that).** That stays safe only if it stops at the right things. **The danger is never
one rung failing — it is a rung landing WRONG and the next rung building on top of it.** *(The scalar
core was **claimed** done at 126/0. Measured against the real corpus it was **48 of 2,746**. A loop would
have marched straight past that and built P1.1 on it.)*

**STOP, report, and ask when:**

- **Any gate is red** — a non-zero build, a non-green suite, exit **101**, or an unjustified **`M`** on a
  pre-existing fragment. **Never turn a gate green by narrowing what it tests.**
  ⚠ **A lane that did not RUN is not a red gate.** The remote **arm64** lanes are outside the rung gate
  entirely (step 10) — an unreachable Mac, an absent runner or a skipped target is a **SKIP you report**,
  never a HALT and never a reason to hold a rung open. Red means *ran and failed*.
- **A reachable DEFECT in this rung's own mechanism — a WRONG ANSWER as much as a leak — even one the
  SUITE is GREEN over.** The leak gate ("no run exits 101") checks the *committed suite*; the defects that
  matter most are the ones a suite run never reaches — a `let m = f()` that exits **101**, or a correct
  program that compiles and returns the wrong number, only when *you* probe it. **Leaks are not ok, and
  neither are wrong answers you own**: a latent/reachable leak, or a construct this rung enables that
  miscompiles, is **FIXED**, or the causing construct is **REJECTED cleanly** (turned into a compile
  error), *before merge* — **NEVER deferred.** A defect lives in "your rung's
  mechanism" if a file you changed, a construct your rung enables, or a spec on your own acceptance path
  can reach it. **Deferring a task-related defect instead of fixing it is a habit this process exists to
  break.** *(Precedent: an owned-String RETURN leaked at P1.2; the option to "defer the leak to P1.4"
  was overruled, the convention pulled forward. At P1.3 Slice 1 a boxed-union RETURN leaked; it was
  rejected (E2015) symmetric with the already-deferred param, not shipped leaking. The suite was green
  over BOTH — only adversarial probing found them.)*
- **A DESIGN RULING is needed** — the corpus contradicts itself, the two references disagree and the plan
  cannot settle it, or the spec is genuinely ambiguous. **You must not guess.** *(`/specs` said both
  "lossy conversions are not allowed" **and** `takeInt(3.7)` ⇒ silently `3` — and the bootstrap passed
  BOTH. Either reading was defensible. It took a user ruling.)*
- **An agent reports the plan was wrong at the code** (step 5) — a wrong plan invalidates the wave, not
  just that agent.
- **A case would have to be disabled that the rung should pass** — the failure mode of this entire
  process. **A green suite that tests nothing is the most expensive lie a test runner can tell.**
- **The rung needs SLICING and the boundary is not obvious.**
- **`PLAN.md`'s next rung is ambiguous** — the no-argument form depends on that ladder being current. Say
  so; do not pick for yourself.

**Everything else runs unattended.** Landing a clean rung — the plan, the wave, the gates, the
**cross-target gate (step 10)**, the merge, **the push (step 11)**, and the `PLAN.md` update (step 12) —
needs no permission. Report what you did;
ask only when the list above fires.

## Deferred work lives in PLAN.md — there is NO separate backlog file

**A finding that genuinely cannot ride this rung becomes a numbered entry on `PLAN.md` — a future rung or
an accepted-debt note — and putting it there is YOUR call, made in the plan, never an agent's mid-rung
escape.** *(There used to be an `OPEN.md` ledger. It was retired 2026-07-21: a standing backlog **invites**
deferral, and nearly every entry in it turned out to be a defect a rung had tripped over and filed instead
of fixing. Its live findings were folded into `PLAN.md` — each into the section it belongs to, so a step's
open work sits WITH the step.)* Agents (implementer, optimizer, reviewer) trip over bugs constantly; that
is the corpus doing its job. **What happens next is the whole game, and the default is FIX, not FILE.**

> **⭐ A STEP WITH RESIDUALS IS NOT COMPLETE.** If a rung generates a residual it does not close, the rung
> is **not done** — however green its suite — and its `PLAN.md` status says so (**◑**, not ✅). *"Core
> landed"* is not *"complete."* The residual lives with the rung (or its workstream), and the rung stays on
> the ladder until it closes. (Accepted debt measured linear-in-practice and a deliberate divergence are
> DECISIONS, not residuals — they do not hold a rung open.)

- **A defect in the rung's own mechanism is FIXED, or cleanly REJECTED, before merge** (see the HALT
  list). It is never deferred. *"I found it while doing something else"* / *"it wasn't what I was asked to
  do"* is **not** a reason to defer — it is the reason the bug is now yours.
- **Only four kinds of finding legitimately become a future rung**, and each has a real reason it cannot
  ride along on this rung. Each has a specific home in `PLAN.md`:
  1. a **bootstrap (`maxon-sharp`) bug** — it needs the full C# suite as its gate (unless this IS a
     bootstrap rung) ⇒ the **"Bootstrap oracle bugs"** list beside PLAN.md's "Traps that survive";
  2. a **distinct feature** that needs its own contract / IR ops / spec-port list ⇒ its own **numbered
     rung on the ladder** (the "Future rungs" list until it is sequenced);
  3. a **correctness-neutral perf debt the optimizer has MEASURED linear-in-practice** — a superlinearity
     you can still *trigger* on a realistic input is fixed, not filed ⇒ the **"Measured debt"** list in
     PLAN.md's Workstream O, WITH the measurement and the re-measure trigger;
  4. a **follow-on slice this rung's plan sanctioned UP FRONT** (e.g. the P1.5 async residuals held for
     B1c) — sanctioned by you, in the plan, before the wave ⇒ a **named residual on the rung / its
     workstream** (and the rung is **not** marked complete while it is open).
- **An agent that trips over anything else STOPS and reports it to you** — the same reflex as a wrong plan
  or an out-of-file-list edit. **You triage: fix-now is the default for anything the rung's own specs
  would exercise.** An agent never writes a deferral into PLAN.md on its own authority.

This is the leak rule (*"leaks are not ok"*) generalized: a leak may not be deferred because it is a
defect the rung owns — and **a wrong answer the rung owns is no different.** The only change from the old
regime is the DESTINATION of a legitimate deferral: a **numbered future rung in PLAN.md**, decided by the
coordinator up front — not a row in a backlog file that anyone could quietly append to.

## 0a. SLICE mode — CLAIM YOUR ROW, AND PUSH IT, BEFORE YOU DO ANYTHING ELSE

**`git push` IS THE LOCK.** There is no lockfile, no registry and no coordinator to ask. A claim exists
when — and only when — it is **on `origin/main`**. A claim in your working tree is not a claim; it is a
private intention, and the next agent's `fetch` will never see it.

**Pick a row that is `⬜ FREE` AND whose LANE holds no `🔶`.** Both conditions, every time. The lane
table is the real exclusion unit, because most of the remaining rows live inside one 28k-line file
(`Compiler/Parser.maxon`) and "different mechanism" does not mean "different code".

```bash
git fetch origin && git rebase origin/main       # NEVER claim against a stale board
# read maxon-shv2/PLAN.md §"🧭 THE SLICE BOARD"; choose a FREE row in a FREE lane
# edit ONLY that row:  ⬜ FREE → 🔶 CLAIMED   |  slice/<id>-<slug>  |  <UTC>
git add maxon-shv2/PLAN.md
git commit -m "claim(slice): <id> — <one line>"  # ⚠ PLAN.md ONLY. No code. No other file.
git push origin main
```

**The claim commit carries `maxon-shv2/PLAN.md` and NOTHING else.** It has to replay cleanly over any
other agent's claim, every time, without thought — a claim commit that can conflict is worse than no
claim at all, because it fails at the exact moment two agents are racing.

| Push result | What it means | What you do |
|---|---|---|
| **accepted** | **The row is yours.** | Proceed to §0, then §4 with `slice/<id>-<slug>` as your branch |
| **rejected** (non-fast-forward) | **You lost the race** — someone claimed between your `fetch` and your `push` | `git fetch origin && git rebase origin/main`, **re-read the board**. Row or lane now taken? `git reset --hard origin/main` to drop your claim, then pick a different row. Still free? Push again |

> ### ⛔ NEVER `git push --force` ON `main`. NOT ONCE, NOT "JUST THIS TIME".
> A rejected push here is not an obstacle — **it is the lock working.** Forcing past it deletes another
> agent's claim commit while that agent is already building against it, and you both then implement the
> same row against a board that agrees with neither of you. The rejection is the ONLY signal this
> protocol has; overriding it removes the protocol.

**Announce the claim before working**: state the row id, the lane, and the pushed commit SHA. That SHA
is the claim's evidence — it is what another agent can verify, and what you point at if the board and
reality ever disagree.

### Releasing, and the claim that outlives its agent

- **On success**, the row goes `🔶 → ✅ DONE` in **step 12's** PLAN.md update, pushed with it.
- **If you abandon** — a HALT-AND-ASK in §"⛔", a blocker you cannot clear — **push the row back to
  `⬜ FREE` yourself**, with a one-line note saying what stopped you. A silent abandon is the worst
  outcome the board can produce: it looks exactly like work in progress, forever.
- **Reclaiming a STALE row** (`🔶` older than ~24 h with no branch on the remote — check with
  `git ls-remote --heads origin 'slice/<id>-*'`) is allowed, but it is **an edit that gets pushed like
  any other**: move it to `⬜ FREE`, name the claim you released and why, push, and only then claim it.
  Never just take it — the previous agent may be mid-rebase, and two live branches for one row is the
  one state this board cannot represent.

⚠ **Your branch is not private once pushed, but `main` is shared from the first second.** Everything in
step 11 about re-running the battery when `main` moved is now the COMMON case, not the exception —
another agent will land while you work. Expect it; budget for it.

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
mkdir -p temp
./bin/maxon.exe build maxon-shv2
./maxon-shv2/.maxon/maxon-shv2.exe spec-test > temp/shv2-spec.log 2>&1; echo "exit=$?"
grep -n '^FAIL' temp/shv2-spec.log               # expect no hits
```

> ### ⚠ ALWAYS BUILD. The SUITE is the part you may skip — never the BUILD.
>
> **`bin/maxon.exe` and `maxon-shv2.exe` are BOTH gitignored, and nothing rebuilds them** — not a
> checkout, not a rebase, not a worktree. A stale binary is the single most common way this step
> starts from a lie, and it lies in *both* directions. *(Measured 2026-07-27 on a clean `main`: the
> tree's `maxon-shv2.exe` read **71 FAILED**; a 13 s rebuild read **1922/0**. The same day, `bin/maxon.exe`
> was stale against `maxon-sharp/` sources on that same clean tree.)* The build is 13 s. It is never
> the thing to save.
>
> **The SUITE (17 s) is skippable in exactly one case, and it is the same argument step 11 already
> makes:** if `origin/main` has not moved since the previous rung's step-8 battery, and the tree is
> clean, then this is byte-for-byte the tree that battery ran on and re-running it only re-derives a
> known-green answer. Check it explicitly — `git fetch origin && git status --short && git rev-parse
> HEAD origin/main` — and **say in the rung report which baseline you are standing on.** If anything
> moved, or you cannot show it did not, run the suite.

⚠ **REDIRECT EVERY SUITE RUN TO A FILE — never pipe one through `head`/`tail`/`grep`.** A pipe decides
what to keep *before* you know what failed, so the failure detail is gone by the time you want it and the
only way back is a **second full run**. Redirected, the whole run is on disk the instant it ends: grep it
for the verdicts, then **Read it** at each hit for the full reason (a golden mismatch is a multi-line
diff; a failed compile carries the compiler's whole stderr). The C# runner's marker is `[FAIL]`, shv2's
is a leading `FAIL`. `temp/` is gitignored. This holds for every suite run in every step below.

**Never pass `--workers`** — the default pool is 12 (`SpecWorkerPool.maxon:140`) and that is the only
count this process runs the suite at.

**Kick this off in the BACKGROUND and start step 2 in the same beat.** The reference survey reads source —
it does not need a built compiler — so the baseline build/suite and the survey agents overlap for free.
Just confirm the baseline came back green before you commit to the plan.

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

> ### Survey agents run on SONNET. The three rung agents do not.
>
> **Spawn the survey agents as `Explore` with `model: "sonnet"`.** They have no frontmatter file of their
> own, so this line is the only place that fact lives — say it here, not in each brief.
>
> **The reason is the paragraph above, not the price.** This role has already had its judgment removed by
> design: it returns citations, and *you* decide. It is also the one delegation whose output you can
> CHECK — a wrong `file:line` fails the moment the implementer opens the file, and a wrong spec port list
> fails when the specs do not go RED before the wave (step 2's ⭐ section). A cheaper model on a
> judgment-free, verifiable, highest-volume job is the trade this step was built for.
>
> ### ⭐ ONE SURVEY PER RUNG FAMILY — a slice does NOT re-survey
>
> **The survey belongs to the RUNG, not to the slice.** P1.7's ten slices each re-read the same v1
> register-allocator and array files to re-derive the same facts; that is nine fan-outs bought and
> thrown away. **Run the survey ONCE, when the rung is first opened**, write it into the plan (the
> per-layer table below IS the durable form), and cut each slice's brief from it — the same way step
> 5 cuts each agent's brief from the plan rather than making the agent re-derive one.
>
> **Re-survey only what actually moved:** a slice that reaches a mechanism the original survey did not
> cover gets a *targeted* survey of that mechanism, not a fresh sweep of both references. And if a
> slice discovers the survey was WRONG at the code, that is the step-5 STOP condition — it invalidates
> the plan for every remaining slice, not just that one.
>
> ⚠ **Do NOT extend this to `maxon-rung-implementer`, `maxon-rung-optimizer`, or
> `maxon-rung-reviewer`.** Their model is declared in their own frontmatter and it is not Sonnet —
> **the reviewer least of all.** It runs LAST (step 7), and it is the gate that has actually fired: a
> reachable SEGFAULT at P1.4b Wave 2c, a reachable PANIC at P1.7, a bug an 8th rung running at P1.7a,
> the owned-String leak at P1.2 — every one of them in code an Opus implementer had just called done.
> **A weaker auditor over a stronger author is the one arrangement guaranteed not to catch what the
> author missed.**

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
rung's acceptance criteria *and* its deliverable (step 12).

**It is the COORDINATOR's call, not the agent's.** An agent left to choose its own coverage tests what it
remembered — which is exactly how a "finished" scalar core scored **48 of 2,746** (see the closing
section). Survey `/specs` yourself and hand the list down.

- **Port REAL specs, never invented ones.** The corpus is **not** bulk-ported: the rung copies exactly the
  files it needs, **byte-identical**, and the agent's only sanctioned edit is the marker flip → 5(d).
- **The list must go RED before the wave.** Run the candidates against today's compiler and watch them
  fail — that IS the rung's red baseline, and **the rung is DONE when they go green.**
- **Never plan to disable a case the rung should pass.** For each one that stays disabled, name the
  **missing mechanism** and the rung that supplies it.

### ⭐⭐ WIDEN THE WAVE BEFORE YOU SLICE — they are different axes, and only one is expensive

**A rung may be too big for one wave. There are TWO ways to answer that, and this skill used to name
only one — which is why rungs have been sliced far harder than they need to be** (P1.7 ran **ten**
slices; P1.8 was cut A/B/C/D before a line was written).

| | |
|---|---|
| **Widening the WAVE** — more agents, in parallel, in ONE loop | Costs one more brief. The loop below runs **once**: one survey, one contract, one optimizer, one reviewer, one gate battery, one cross-target run, one merge, one PLAN.md update. Bounded by integration at ~4–5 agents |
| **SLICING the rung** — sequential slices, each its own loop | **Multiplies the entire loop.** Every slice pays a fresh survey, a fresh optimizer agent, a fresh reviewer agent, a full gate battery, a cross-target run, a rebase/merge/push and a PLAN.md edit — plus a coordinator plan-and-integrate cycle, which is the serial bottleneck |

**A slice is the most expensive thing this process can buy, and until now its price was written down
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

**Each slice runs the full loop below — that is exactly why there should be few of them.** What a
slice does NOT re-run is the survey: see the sharing rule in the box at the top of this step.

## 3. Write the contract (if the rung needs new IR ops)

Land the dialect ops **before** launching the wave. Hand agents a golden-IR example for a sample
program. If the rung is purely front-end, say so and skip.

## 4. Set up isolation

```
git worktree add ../maxon-<rung> -b <rung>-<slug>
cp -r bin ../maxon-<rung>/bin        # bin/ is GITIGNORED — a worktree has no compiler without this
```

**In SLICE mode the branch name is not free-form — it is `slice/<id>-<slug>`, the exact name you wrote
into the board in §0a.** The board row is how another agent checks whether a claim is alive
(`git ls-remote --heads origin 'slice/<id>-*'`), so a branch that does not match the row it claims is a
claim nobody can verify. **Push the branch early**, before it is finished: an unpushed branch is
indistinguishable from an abandoned claim.

⚠ **In a worktree, every `maxon-dev` MCP call needs `repoRoot`** — see CLAUDE.md's box. This bites
hardest in SLICE mode, where several worktrees exist at once and the default (the main checkout) is
somebody else's tree.

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

## 6. Optimize — the LADDER READ is per rung; the `maxon-rung-optimizer` AGENT is TRIGGERED

**Two different things, and only one of them belongs on every rung.**

### 6a. The `scale-test` read — ALWAYS, and it is YOURS (≈17 s)

**Run `scale-test`, read the doubling ladder, and record what moved and WHY in
`docs/optimization-log.md`.** This never batches to a phase boundary, and the reason is not thoroughness
— it is that **attribution is only available now.** The instrument sees exactly WHAT moved and can never
see why; ten rungs later, neither can you. The memory columns are exact and bit-for-bit reproducible, so
any movement is real; the CPU column moves only outside its noise band.

**≈17 s, since `DefaultRepeatCount` became 1** (user ruling 2026-07-27). It had been 3 — measured 51 s
and 48 s — and the repeats were buying this read nothing: the per-phase **allocation tables are
BYTE-IDENTICAL** at `--repeat=1` and `--repeat=3`, which is what the runner's own header already
promised. Cost is linear in the count, so 3 → 1 is 51 s → 17 s.

⚠ **Raise it back with `--repeat=3` when you are A/B-ing two binaries' CPU** — there the effect can be
a few percent and a single sample cannot carry it. ⚠ **And CPU rows in `docs/optimization-log.md` from
before 2026-07-27 are MINIMA while later ones are single samples**, so they sit ~10% apart for no
reason in the compiler; the changeover is recorded in the log at that date. **Do not read that step as
a regression.**

### 6b. The `maxon-rung-optimizer` AGENT — when a trigger fires

**A full superlinear hunt over a rung that added no algorithm has nothing to find.** Spend the agent when
at least one of these is true:

- the rung adds **a pass**, **an IR op**, or **a collection the compiler indexes by**;
- **6a's ALLOCATION ladder shows a ratio ≳ 2.4 per doubling that you cannot explain** — an unexplained
  bend is the strongest possible trigger, and it is *why* 6a is not batchable. ⚠ **Read the trigger off
  the ALLOCATION column, not the CPU one.** Allocations are exact and bit-for-bit reproducible, so a
  bend there is a fact. The CPU column at the default `--repeat=1` is a single sample: measured, its
  per-phase exponents wobble up to **±0.5** run to run (`pruneDeadBlockArgs` read 1.91 and 2.41 on the
  same unchanged compiler), so **a bare CPU ratio would spawn an optimizer agent to chase noise.** If
  the bend is CPU-only — which is the real case the column exists for, since a cost that allocates
  nothing is invisible to memory — **re-run with `--repeat=3` and confirm it reproduces before
  spending the agent**;
- the rung touches something the **"Measured debt"** list in PLAN.md's Workstream O names a re-measure
  trigger for.

Otherwise: state in the rung report that no trigger fired, and carry the hunt to **the phase-boundary
sweep** — one optimizer agent over everything the phase landed, which sees cross-rung interactions a
per-rung pass structurally cannot.

⚠ **This is a batching decision, not a lowering of the bar. It does not touch the FIX rule:** a
superlinearity you can *trigger on a realistic input* is still **fixed, not filed**, whoever finds it and
whenever. And the two biggest finds in this repo's history — the `regalloc:splitting` quadratic and the
cascade fixpoint duals — were both **read off the ladder**, which is the part that stays per-rung.

When the agent does run, it commits separately on the same branch.

## 7. Review — `maxon-rung-reviewer`

Hunts **duplication** first, then latent bugs. Commits separately on the same branch.

> **⚠ Optimize BEFORE you review, and never the other way round.** An optimizer *rewrites code*, so it
> can introduce exactly the duplication the review exists to catch — a fast path forked from a slow one,
> a helper inlined at three call sites. **The duplication-focused review must be the LAST quality gate
> before the merge**, and it reviews the optimizer's diff as well as the implementer's.

**THE REVIEW IS MANDATORY ON EVERY RUNG — it does not batch, it is not triggered, and it is never
skipped.** *(Step 6b makes the OPTIMIZER agent conditional. That is a batching decision about a hunt
with nothing to find on a rung that added no algorithm. It says nothing about this step: the reviewer
is the gate that has actually fired — a reachable SEGFAULT at P1.4b Wave 2c, a reachable PANIC at P1.7,
the owned-String leak at P1.2 — every one in code an Opus implementer had just called done.)*

**And whenever either runs, it must be an agent that did NOT write the code** (user directive). The
independence is the point: the P1.0a review found two resource leaks and a cross-process duplicated
selection rule that the author, re-reading their own work, had not seen.

## 8. VERIFY THE AGENT'S CLAIMS YOURSELF

**Do not trust the report.** Re-run the gates in the worktree, and read the crux files. An agent in this
project once left work uncommitted in a worktree based on a stale parent; another claimed a green build
by grepping for a success string.

**This is the ONE authoritative full battery, and it runs here — once.** It is your independent
verification and the pre-merge gate at the same time, not a second run stacked on top of the agents'. The
agents iterate on `--filter` and prove their own slice; the full suite, the `scale-test` read and the leak
gate are yours, run once on the final tree. If it comes back red, an agent goes back — that is the trade
for not running this battery four times.

**There is NO worker-count invariance gate.** `--workers=1` is a **debugging tool**, not a step of this
process — reach for it when you are chasing a suspected nondeterminism or want serial output while reading
a failure, and never as a routine pre-merge pass. It was costing a slow single-threaded suite run on every
rung to re-prove something the runner holds **structurally**: `--workers=1` is the same pool with one worker
in it, not a separate serial branch, and the parent never prints a result as it arrives — it buffers and
reports in fixed order (`maxon-shv2/Testing/SpecWorkerPool.maxon:17-34`). Ordering cannot vary with the pool
size, so the check was re-deriving a known answer at full suite cost. Run the suite parallel and move on.

**Check exit codes. Never grep for success.** Exit **101** = memory leak.

### ⭐⭐ THE SPEC SUITE IS THE TESTING MECHANISM. A hand-run snippet is DISCOVERY, never EVIDENCE.

**Probing is how the leak gate and the reachable-defect rule find things** — a `let m = f()` no
committed test runs is exactly what step 9 asks you to go looking for, and `run_program` is the right
tool for the looking. **But the probe is where the work STARTS, not where it ends.** The rule:

> **A probe that finds something becomes a SPEC. A probe that finds nothing becomes a spec, or it never
> happened.**

- **A defect found by hand is reproduced as a failing spec case in `specs-shv2/` FIRST** — that is the
  RED — and the fix is proven the moment that case goes GREEN. Never "fixed, then re-ran my snippet."
  A snippet proves the bug is gone from your terminal; a spec proves it is gone from every future rung.
- **"I verified it manually" is not a gate result and does not appear in a rung report.** It is
  unreviewable, unrepeatable, and invisible to the cross-target lanes — a hand-run x64 snippet says
  nothing about wasm or the Linux ELF, whereas a spec case runs on all three for free in step 10.
- **The same holds for an agent's own claims** — an implementer or reviewer reporting "I confirmed X by
  running a test program" has given you an anecdote. Ask where the case is. **If the behaviour is worth
  checking twice, it is worth a spec; if it is not worth a spec, it was not worth checking.**
- ⚠ **The exception is genuinely un-spec-able signal, and it is narrow:** a timing/scale measurement
  (that is `scale-test`, step 6a), a `dump_ir` read while forming a hypothesis, or a debugger session.
  Those inform the work; they never *stand in* for a case.

**Why this is a speed issue and not just a rigor one:** hand-testing is re-run by hand on every
iteration, by every agent, forever, and it decays to nothing the moment the session ends. A spec case is
written once and then runs 1,900-strong in 17 seconds, on three targets, unattended, for the rest of the
project. **Manual testing is the slowest possible way to check the same thing twice.**

## 9. The gate battery

| Gate | |
|---|---|
| Build | exit 0, zero warnings |
| shv2 suite | all green, **including every pre-existing test** |
| Fragments | `git status --short specs-shv2/fragments/` — **additions only**. An **`M`** is a codegen change: justify or fix. Empty diff after a spec run **proves byte-identical codegen** |
| `scale-test` | ⚠ **NOT A GATE — it is an INSTRUMENT with no verdict.** Run it after any change to a pass, the IR, or a data structure the compiler indexes by, and **read it**: the per-rung memory numbers are exact and bit-for-bit reproducible, so any movement is real. **Explain and attribute what moved**, and record the reason in `docs/optimization-log.md` — the trend table is the deliverable. There is nothing to "pass"; do not chase one, and never touch the instrument to make a number look better |
| If `maxon-sharp/` was touched | C# suite green (**2883+**) **AND codegen neutrality**: `git status --short specs/ specs-shv2/` EMPTY |
| Leak gate | no run exits **101** — **and no reachable leak, including one found only by adversarial PROBING** (a `let m = f()` no committed test runs). A probed/latent leak is FIXED, or the leak-causing construct cleanly REJECTED, before merge — **never deferred as a live leak** (see the HALT list — *"leaks are not ok"*). A green suite is not proof of no leak; it is proof no *committed test* leaks. ⇒ **and the probe that found it becomes a COMMITTED SPEC** — see step 8. That is what turns this gate's one-off discovery into coverage the next rung inherits |
| Cross-target | **step 10** — every **locally runnable** target, not just this host's. **The remote arm64 lanes are NOT in this battery and are NOT required to complete the rung** (synced by hand — see step 10). Not run ⇒ SKIP (say which); ran-and-failed ⇒ **RED** |

## 10. The CROSS-TARGET gate — every LOCAL target, once, before it lands

**Step 8's battery proved the rung on exactly ONE target: whichever one this host happens to be.**
Everything else the compiler emits stays unverified until somebody eventually runs it, and *"somebody
eventually"* is how **317 stale `specs/fragments-arm64-macos/` goldens** came to sit on `main` unnoticed,
through a run of rungs that were all green on x64-windows. **A green suite on one target is evidence
about one target.**

```
scripts/cross-target-gate.sh --skip-build --skip-host > temp/cross-target.log 2>&1; echo "exit=$?"
tail -20 temp/cross-target.log        # the matrix; the suites behind it are in the same file
```

Add `--csharp` if the rung touched `maxon-sharp/`. **Redirect it** — this one runs several suites, so a
piped run that goes red costs *minutes* to re-run just to read what a file already had. Here `tail` is
fine for the matrix precisely *because* the file is there for everything behind it.

**`--skip-build --skip-host` is the RUNG invocation, and it costs no coverage.** Run straight, this
script rebuilds both compilers and re-runs the HOST suite — all three of which step 8 just did on this
identical tree. *(Measured 2026-07-27: `dotnet build` 45 s + `maxon build maxon-shv2` 13 s + host suite
17 s, against x64-linux 52 s and wasm 27 s. Better than half the gate was re-derivation.)* Neither flag
weakens anything: `--skip-build` **refuses outright** if any source is newer than the binary it would
have built (it checks — it does not take your word for it), and `--skip-host` prints the host lane as
**`PRIOR`**, not `SKIP`, naming what covered it. Drop both flags if you are running this gate on its own
rather than after a step-8 battery.

⚠ **NEVER run two suites in one tree at once** — not two lanes, not a gate alongside a hand-run suite.
The runner shares `.spec-tmp`, and a race there produces a **FALSE RED** in a lane that is actually
green. *(Measured while building this very gate path: an overlapping second run turned x64-linux into
`FAIL exit 1`; run alone, the same tree and binary passed 1816/0.)* A red lane is a rung-halting gate,
so a false one is expensive — if a lane goes red, **re-run that lane alone before reporting it.**

It builds both compilers, then runs the shv2 suite **per target**, each behind the runner that target
needs — natively for the host, WSL for the Linux ELF, the vendored wasmtime for the wasm component. It
prints a matrix, one row per target.

**Run it ONCE, here — on the final tree, after review and optimization, before the merge.** It does not
belong on every commit: it is minutes of work whose answer only changes when the rung is finished.

### ⛔ The REMOTE (arm64/Mac) lanes are NOT part of this gate — they are synced by hand (user, 2026-07-27)

> **⭐ ARM64 VERIFICATION IS NEVER A CONDITION FOR COMPLETING A RUNG (user, 2026-07-28).** Not the
> suite, not the goldens, not "let me just try the Mac first." **A rung that is green on every
> LOCALLY runnable target is finished** — it merges, it pushes, and step 12 marks it ✅ if nothing
> else holds it open. **An unverified arm64 lane is a reported SKIP, not a residual**; see step 12.

**`arm64-macos` and `arm64-linux` run over `ssh` to a Mac, and everything expensive about them is the
REMOTE part, not the arm64 part**: a bundle transport, a second checkout's build, an OrbStack guest, and
a machine that can be asleep, wedged, or behind flaky mDNS. **They cost the rung more than they caught** —
one wedged `orb run` preflight alone burned ~95 minutes and produced *no verdict at all*. So the rung no
longer waits on another machine.

- **The gate skips them by default and SAYS SO** — two SKIP rows with the reason, so a green run can
  never be read as full cross-target coverage.
- **The sync is a separate, periodic, manual run:** `scripts/cross-target-gate.sh --mac --require-mac`
  (or `bash scripts/remote-mac.sh --host=<user@mac> --shv2` for the native macOS lane alone, which
  bypasses the OrbStack preflight). `--require-mac` makes an unreachable Mac a FAILURE there, which is
  right when reaching it *was* the point. **Not your call to schedule as part of a rung.**
- **The arm64 GOLDENS ride the same rule.** A codegen change leaves `fragments-arm64-*` stale, and those
  goldens can only be minted by the lane that emits them — **so they are minted at the periodic sync, not
  at the rung.** Do not hold a merge for them, and do not hand-edit them to look current. **Say in the
  rung report that the rung changed codegen and the arm64 goldens are therefore owed a mint.** *(This is
  why the sync's own red lanes are fixed rather than filed: the debt is real, it is just not the rung's
  to pay.)*
- ⚠ **This is a deliberate COVERAGE TRADE, not a claim arm64 is fine.** Golden rot on an unrun lane is a
  measured, recurring fact in this repo. **Do not describe a rung as cross-target verified on arm64.**
  The trade is only honest if the SKIP is stated — an unreported skip converts *"we chose not to check"*
  into *"we checked,"* which is the one failure this whole section is guarding against.

| | |
|---|---|
| **Not run ⇒ SKIP, and the gate still passes** | A missing runner — or a lane that is deliberately out of scope — must not block a rung. |
| **But a SKIP is reported, never folded into the green** | It means **UNVERIFIED**, not *proven good*. **Name the skipped targets in the rung report** — the one thing worse than not testing arm64 is believing you did. |
| **A target that RUNS and FAILS is RED** | A rung-halting gate — **HALT AND ASK**, exactly like any other red gate. No flag softens it, and you never turn it green by dropping a target. **This holds on the manual sync run too**: a red arm64 lane found by a periodic sync is still a real defect, and is fixed, not filed as *"the sync was red."* |

⚠ **Golden churn from this gate is real and belongs in the commit** — a non-native fragment that moves
is a codegen change on that target. ⚠ **But a FILTERED run's fragments are not authoritative:** the
runner batches tests into a shared module and slices the IR per test, so literal-pool indices
(`__str_N`, `__static_lit_N`) depend on *which* tests are in the batch. The gate excludes fragments on
filtered runs for exactly this reason — **regenerate goldens only from an unfiltered run.**

## 11. Land it — linear history, then push

```
git fetch origin
git rebase --onto main <old-base> <branch>     # rebase the branch, do NOT merge-commit
git checkout main && git merge --ff-only <branch>
```
`merge.ff=only` is configured, so a non-fast-forward merge **errors** rather than making a merge commit.

**Re-run the suites on the merged tree ONLY if the rebase actually replayed onto an advanced `main`** — i.e.
`origin/main` moved while you worked, so `<old-base>` is behind it, and the merged tree is now code your
step-8 battery never saw. **If `<old-base>` was already `main`** (nothing landed upstream), the fast-forward
leaves main byte-identical to the branch tip you gated in step 8, so the re-run would only re-derive a
known-green tree — skip it. Either way, then **`git push origin main`** — the parallel repo consumes it.
Remove the worktree and delete the branch.

> ### SLICE mode — the landing race, and why "did `main` move?" flips from exception to default
> Another agent lands while you work. That is the normal case here, not the surprise, so:
> - **`<old-base>` will almost always be behind `main`** ⇒ the re-run above is **owed, not optional**.
>   You gated a tree that no longer exists.
> - **Your `git push origin main` can be REJECTED** — someone landed between your `fetch` and your push.
>   Same rule as §0a: **rebase and re-gate, never force.** A forced landing overwrites a merge whose
>   author already gated it, and the suite that proves your work green never saw their code.
> - **If the agent who landed first touched YOUR lane's file** (three lanes share `Parser.maxon`), read
>   their diff before you re-gate. A clean textual rebase is not evidence the two changes compose — it
>   is only evidence they were far apart in the file.

## 12. Close the loop

**In SLICE mode, this step also RELEASES your claim: flip your board row `🔶 CLAIMED → ✅ DONE`, in the
same commit as the rest of the PLAN.md update, and push it.** Until that push lands, the board still
says you are working and your lane is still closed to everyone else — **a finished slice whose row was
never flipped blocks its whole lane exactly as effectively as an abandoned one.** If your slice moved
goldens you did not add, say so on the row (see the board's ⚠ on golden churn) so the agents rebasing
onto you know what they are inheriting.

Update `maxon-shv2/PLAN.md`: a rung's deliverable is the set of `disabled-test:` markers it flipped to
`test:`. **Mark the rung done ONLY if it has no open residuals** — if the plan sanctioned any deferral in
step 2, the rung is **not** complete (status **◑**, not ✅), and each residual must be written into the
appropriate PLAN.md section (a future rung, a workstream residual, or the bootstrap-oracle / "Measured
debt" notes), not left implicit. Record anything durable in memory.

> **⭐ AN UNVERIFIED arm64 LANE IS NOT A RESIDUAL, AND NEVER HOLDS A RUNG AT ◑.** A residual is
> **work this rung owes** — a mechanism it left unbuilt, a defect it sanctioned deferring. A skipped
> remote lane is **coverage this process deliberately batches** (step 10, step 13), owed by the
> periodic sync and not by the rung. **Mark the rung ✅ on the local targets** and write the skip into
> its detail row exactly as the gate reported it — `arm64 SKIP — remote, UNVERIFIED` — which is the
> spelling the existing rows already use. **A rung parked at ◑ waiting on another machine is the
> failure this rule exists to prevent: it makes the ladder's next rung unpickable over a laptop.**

**Write the rung's DETAIL row. Do NOT hand-update the "Status at a glance" index — that is a
PHASE-BOUNDARY job.** The index is a second copy of facts the detail rows already hold, and maintaining
it per rung buys nothing but drift: as of 2026-07-27 its caption read *"snapshot 2026-07-22, suite head
**1162/0**"* while the detail rows said **→1906** and the tree measured **1922/0** — three spellings of
one number, which is this project's signature bug in its own plan. Its caption already concedes the
detail wins. **So: per rung, update the detail. Per phase, regenerate the index from the detail rows in
one pass** — and prefer any change that makes it *derived* rather than *transcribed*.

## 13. At the PHASE boundary — the batched work

**Four things are deliberately NOT per-rung, because doing them per rung buys re-derivation rather than
coverage.** Run them once when a phase closes (or when a rung family finishes):

| | |
|---|---|
| **The optimizer SWEEP** | One `maxon-rung-optimizer` over everything the phase landed — it sees cross-rung interactions a per-rung pass structurally cannot. Per-rung, the agent runs only on a **step 6b trigger**; the 20 s ladder READ stays per rung, always |
| **The PLAN.md index table** | Regenerated in one pass from the detail rows (step 12) |
| **The REMOTE arm64/Mac sync** | `scripts/cross-target-gate.sh --mac --require-mac` — already manual and periodic (step 10). A phase boundary is the natural moment. **A red lane here is a real defect, fixed, not filed as "the sync was red"** |
| **The stale-golden sweep** | The measured rot: 288 stale + ~489 *absent* x64-linux goldens, and 317 stale arm64 C#-suite goldens. ⚠ **A MISSING golden never fails** — absence is invisible to every gate, so it can only be found by going to look |

⚠ **Nothing that catches a DEFECT batches.** The reviewer, the leak/probe gate, the RED spec baseline,
the host suite and the ladder read all stay per rung — a rung that lands wrong is the one failure this
process exists to prevent, and every one of those has actually fired.

---

## The thing this process exists to catch

shv2's 126 spec tests were written **by shv2, for shv2**. Run against `/specs` — the accumulated
definition of the language, written by people who were not trying to make shv2 look good — the "finished"
scalar core scored **48 of 2,746**. *Not one of the 126 had ever used a parenthesis.*

**So: port real specs, not invented ones. Expect bugs — that is the point. And never let an agent
disable a case it should pass.**
