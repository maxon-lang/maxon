---
name: plan-and-land
description: Plan a change, get the design approved, then orchestrate it to a pushed commit — survey the sources and the spec surface with dispatched agents, choose the approach yourself, halt for the user's approval, then run `/land` with the scout already satisfied. Use for a change whose APPROACH is not yet decided, or where the user wants to approve the design before any code is written. When the approach is already known, use `/land` directly. Invoke as `/plan-and-land <the problem>`, or with the path to an existing plan file to resume one.
---

# Plan it, approve it, then land it

**One invocation = one approved design, carried to one pushed commit.** You plan; dispatched agents
read; `/land` does everything from the red set onward. This skill adds the three things `/land` lacks —
**approach selection, an approval halt, and a way for an implementer to say the design is wrong.**

> ## ⛔ YOU PLAN. YOU DO NOT READ THE CODEBASE.
>
> The reading is dispatched (§2). You read **reports** and write the **plan**. `/land`: *"A coordinator
> who starts reading a third file to form a hypothesis has stopped coordinating."* You reach §6 still
> owing `/land` §1–§9 **including the battery** — spend what is left on judgement, not greps.
> ⚠ Planning *feels* like reading; that is why this box exists.
> ⛔ **No worktree, no branch** — `/land` §0 requires a CLEAN tree and this skill must not dirty it.

| | | who |
|---|---|---|
| **0** | Orient — `main`, clean, record `BASE` | **you** |
| **1** | State the problem and what would prove it | **you** |
| **2** | **SURVEY** — sources, and `/land` §1's scout fields | **2–3 agents** |
| **3** | **CHOOSE** — approach · blast radius · obligations · candidate acceptance · staging | **you, never delegated** |
| **4** | Write the plan file | **you** |
| **5** | ⛔ **HALT** — present it and wait | **the user** |
| **6** | `/land` per stage, with the substitution block | **you become `/land`'s coordinator** |
| **7** | Stamp `STATUS:` — next stage, or stop | **you** |

⭐ **§6 can send you back.** An agent may halt on a refuted design: a wrong FACT is amended and §6
resumes; a refuted APPROACH returns to §3, then to §5 for re-approval. That is the one loop here.

---

## 0. Orient — yours

`git branch --show-current` is `main`; `git status` is clean; `git fetch` shows nothing behind
`origin/main`. Record `BASE=$(git rev-parse HEAD)`.

⛔ **Do not build** — that is `/land` §0's, *"IF, AND ONLY IF, THE BINARY IS STALE"*, and nothing in
planning reads the binary. **Given a plan-file path instead of a problem**, this is a RESUME: read it,
then go to the resume box in §4. If the ask is ambiguous **in a way that changes the approach**, ask now.

## 1. State the problem — yours, one paragraph

What is wrong or missing, and **what would prove it fixed**. For a bug, state the reproduction **as a
program** — never as something you ran.

> ⛔ **PLANNING RUNS NO SPEC TESTS AND NO PROBES.** `/land` binds its scout the same way: *"It reads; it
> does not run probes — what the compiler does today is what §1's red run of the chosen cases shows."*
> Verification lives in cases and nowhere else (NO MANUAL TESTS, user directive). A planning-phase run is
> a claim nobody can attribute and a red nobody watched.

## 2. Dispatch the surveys — in parallel

Two briefs; three when the change spans unrelated subsystems. ⚠ **Agents run in the background by
default — pass `run_in_background: false`**: the plan cannot be written without their answers.

**A — SOURCE** (`Explore`). FACTS, not a recommendation: which passes and files own the behaviour, with
`file:line`; the call sites; what each existing mechanism already does; which targets have an arm;
whether this lands in emitted runtime (`Compiler/Runtime/`, `Targets/*/*Runtime*.maxon`), `runtime/`,
`stdlib/` or plain compiler source, **and which of
`maxon-bin/CLAUDE.md`'s obligations that triggers**.

**B — SPEC SURFACE** (`Explore`), briefed with `/land` §1's four fields **verbatim**: which `specs` files
own this behaviour; every existing case that touches it with file + line and **the verbatim text** of any
that already pins it; every `disabled-test:` in range; what the neighbouring cases look like.

Both: read-only · write nothing into the checkout · run nothing · `file:line` for every claim.

## 3. Choose the approach — yours, never delegated

Decide and write down: **the mechanism**, named — what it extends, what new vocabulary it needs (an IR
op, an `ErrorCode` case and its band, a tier entry); **the alternatives rejected**, one line each; **the
blast radius** — the file list, and for more than one implementer the **disjoint partition** (`/land`:
*"never two agents in one checkout on overlapping files"*); **the obligations** inherited from
`maxon-bin/CLAUDE.md`; **the candidate acceptance set** — file, case name, assertion, and **why it must
be RED today**; **the staging decision**.

> ⛔ **THE PLAN NAMES THE MECHANISM AND THE FILES. IT DOES NOT WRITE THE CODE.** A fenced code block that
> is not a quote of code existing today means the plan has overrun — delete it and name the mechanism in
> a sentence. Pseudocode and ordered edit lists are §2's work, and putting them here hands the
> implementer a script to follow when the **red** is the acceptance.

⚠ **A CANDIDATE SET IS NOT A SET.** The set is decided in `/land` §1, by you, against its own criterion —
the one thing `/land` never lets anyone else touch.

## 4. Write the plan file

**`C:\Users\Eric\.claude\plans\<slug>.md` — OUTSIDE the checkout, never committed.** `/land` §0 wants a
clean tree and §9 commits the whole tree, so an in-repo plan file is either dirt or gets swept into the
commit. Not deleted after landing; the `STATUS:` line is stamped instead.

```
# <the change in one line>          <- the line every /land brief carries
STATUS: AWAITING APPROVAL | APPROVED <date> | STAGE n/N LANDED <sha> | LANDED <sha> | ABANDONED - <why>
REV:    <n>                        <- bumped by a re-design; approval is against a REVISION
BASE:   <sha>, tree clean, <date>  <- the staleness anchor for the scout skip
```

| § | Section | Feeds |
|---|---|---|
| 1 | The problem, as a program with actual-vs-expected | §5 |
| 2a | **SOURCE facts**, `file:line` per claim | `/land` §2 — *"the diagnosis you already did while reading the red"* |
| **2b** | **SPEC-SURFACE facts** — §1's four fields, field for field | **`/land` §1 — this IS the scout report** |
| 3 | The approach; the alternatives and why they lost | the approval; §2's brief |
| 4 | Blast radius — file list; disjoint partition if >1 implementer | `/land`'s brief item 3, the *"EXCLUSIVE file list"* |
| 5 | Obligations — cross-target arms · `ErrorCodeRegistry` case + band · emitted runtime (`Compiler/Runtime/`, `Targets/*/*Runtime*.maxon`) ⇒ two self-compiles, per `scripts/self-compiles-needed.sh` · `runtime/` tier roster · which `docs/` source owns the surface | every brief; `/land` §8 |
| 6 | Candidate acceptance — file, case, assertion, **why RED today** | `/land` §1, as a candidate |
| 7 | Staging — `ONE /land` (default), or N stages with justification | §6 |
| 8 | Open questions — what approval is actually being asked for | §5 |
| 9 | **Amendments** — a dated line per amended fact; per re-design, what refuted the last approach | §5; a fresh session |

> ### RESUMING FROM A PLAN FILE
> `BASE` + §2a + §2b + §3 + §4 + §6 are the resume token — exactly `/land`'s inputs. ⚠ **A resume
> re-checks `BASE` first**: run §6's staleness diff, and if `specs/` or any file in §4 moved,
> **re-survey narrowly on what moved** and amend §2a/§2b. Never blanket-trust a plan across a fetch.

## 5. ⛔ HALT — present it and wait

**In chat** — the file is the record, the halt is the conversation — in this order:

1. **The change, one line.** If N > 1 stages: **`<N> STAGES — <named condition>`** as the very first thing.
2. **The mechanism**, 2–4 sentences.
3. **Alternatives rejected**, one line each. *(This is what makes the approval mean something.)*
4. **Blast radius** — file count, notable files by name.
5. **The candidate acceptance** — spec file + case names, one line on why each is red today.
6. **Obligations** — cross-target arms; a new error code and its band; is this emitted runtime (two self-compiles); which `docs/` surface it owes.
7. **The plan-file path.**
8. **The question, explicitly:** approve as planned · change the approach · change the staging · change the acceptance.

Resume in the **same session** by default; a fresh session resumes off the plan file.

## 6. Land it — `/land`, once per stage

**Run the staleness diff before you assert it** — ⚠ assert the RESULT, not the intention. Non-empty means
re-survey narrowly on what moved and amend §2b first; the failure mode of a stale skip is a **missing**
case, and a missing case reads green.

```
git diff --stat <BASE>..HEAD -- specs/
```

Then invoke `/land` with the change's one line, followed by this block. *(`/fannkuch-iterate` §4 is the
precedent for substituting into `/land`.)*

```
Substitutions for this invocation (from /plan-and-land):

S0 - the "say what the change is" sentence is DISCHARGED: the user approved
   <planfile> rev <n> on <date>. S0 is `git status` clean plus build-if-stale, nothing else.

S1 - THE SCOUT IS ALREADY RUN. Its FACTS are <planfile> section 2b, taken at <BASE> on a
   clean tree; `git diff --stat <BASE>..HEAD -- specs/` is empty. Do NOT dispatch
   the `Explore` scout. Resume at "Then YOU pick the set" - the set is still YOURS.
   <planfile> section 6 is a CANDIDATE list, not the set. The spec-author dispatch and
   the RED read are unchanged.

S2 - the DIAGNOSIS your brief must carry is <planfile> sections 2a and 3. The EXCLUSIVE
   file list is section 4. Every obligation in section 5 goes into the relevant brief.

A NINTH THING IN EVERY BRIEF, beside your eight. Quote it to each agent:
   "You are implementing an APPROVED DESIGN, named in your brief. HALT AND REPORT - do
    not work around it, do not substitute a design of your own, do not carry on and
    mention it at the end - the moment you find something in the tree that makes that
    design UNABLE to produce the acceptance cases. Report: the approved mechanism, the
    thing that refutes it with file:line, what you tried, and what you believe the tree
    actually requires. Leave your edits in place and touch nothing outside your file
    list. This is a REFUTATION, not a difficulty: 'bigger than the plan said', 'more
    files than my list', 'a cleaner design occurred to me' and 'this wants a refactor
    first' are NOT it - for those, carry on and say so in your report."

STAGE <n> of <N>. Your unit is the WHOLE of this stage. Your "ONE CHANGE, ONE CHUNK, ONE
   COMMIT" box binds absolutely inside it: do not propose and do not accept any further
   division. Staging was decided before you started and is not reopened here.
```

⛔ **Three things the skip does NOT skip**, because each would move a gate: the **set decision** (*"The
scout returns FACTS; the set is yours"*), the **spec-author dispatch**, and the **RED read**. The skip
expires at the first commit — a multi-stage plan re-scouts before the next stage.

> ### ⛔ FROM HERE YOU ARE NO LONGER A PLANNER. YOU READ EXIT CODES, NOT REPORTS.
> §2–§4 were spent believing agent reports, which is right for facts and fatal for gates. `/land`:
> *"Do not trust a report — read its evidence… check exit codes, and never grep for a success string —
> a past session reported a green build by grepping for `^error` while the real failure printed
> `[CMP] ERROR:`. **Exit 101 is a leak.**"*

## 7. Close the plan

Stamp `STATUS:` — `LANDED <sha>`, `STAGE n/N LANDED <sha>`, or `ABANDONED — <reason>`. ⛔ The plan file
is outside the repo; this is never a commit.

⛔ **Do not wait for CI** — not after the last stage, and not between stages: the next stage starts from
the pushed commit (`/land` §9's ruling).

---

## The design-flaw halt — an agent can send the design back

`/land` gives an implementer one stop rule — *"stop when your filter is green"* — enough when that agent
chose the mechanism itself. Here it is **approved before the agent exists**, which creates a failure
`/land` never had to name: an implementer that discovers the approved design cannot work. Without a halt
it grinds on against a refuted design, or silently substitutes one the user never approved — and **the
substitution is invisible in the report.** ⇒ **the ninth brief item, §6.** It binds every dispatched
agent; the reviewer counts, since `/land` §5 is often where a design flaw is first visible.

> ⭐ **THE TEST IS REFUTATION, NOT DIFFICULTY: the approved mechanism CANNOT produce the acceptance
> cases**, and something in the tree — named, with `file:line` — is why. Cost, size and elegance are not
> on this list.
>
> ⛔ **NOT a design-flaw halt, and each will be offered as one:** it is bigger than the plan said · it
> touches more files than §4 listed · a cleaner design occurred to me · the plan did not mention X · this
> wants a refactor first · a pre-existing defect is in the way (`/land`: *"a defect ANYONE finds on the
> way is FIXED, not filed"*). Each of those is **carry on and say so in the report**.

**This is not escalation.** `/land`'s *"THERE IS NO ESCALATION"* forbids the **coordinator** rerouting to
a heavier process, opening a worktree, or landing half. An agent reporting to its coordinator is the
ordinary report path, and this halt **never leaves this skill** — it returns to §3, one step up in the
same file. `/land`'s *"⚠ A specific instruction in your brief OUTRANKS the agent's own stop rule"* is the
licence for adding it.

| What the report refutes | Do | User? |
|---|---|---|
| a **FACT** in §2a/§2b | amend the plan file in place, re-brief the same agent | no |
| the **APPROACH** in §3 | re-design at §3 — re-survey first if it lands in unsurveyed code — bump `REV:`, return to the **§5 halt** | ⛔ **yes, re-approve** |
| neither (a defect, a difficulty) | it was not a halt; re-brief to carry on | no |

⛔ **A re-design is re-approved, not waved through**, and its §5 presentation leads with **what refuted
the first approach**. ⚠ Do not re-design *from the agent's proposal* — `/land`: *"Its report is a lead,
not a verdict."* The agent names what the tree requires; **you** decide what to build.

⚠ **Mind the tree.** The agent halts mid-edit, so `git status` is dirty and `/land` §0 wants it clean.
Amending a fact keeps the work; a dead approach discards it — `git checkout -- <that agent's exclusive
file list>`, **never a blanket reset**, because another implementer may be live on disjoint files.

⭐ **A halt after §1 does not throw away the red.** Those cases were chosen to fail *for the reason being
fixed*, and a re-designed mechanism usually fixes the same reason — re-examine the set against the new
approach; do not rewrite it by reflex.

---

## Staging

> ## ⛔⛔ STAGING IS DECIDED **BEFORE** `/land` IS INVOKED. A RUNNING `/land` NEVER STAGES.
> No contradiction with *"ONE CHANGE, ONE CHUNK, ONE COMMIT"*: the rules act at different times. Staging
> is decided while **no `/land` is running**; from the moment one starts, its unit is fixed and its own
> boxes bind absolutely. ⛔ A running `/land` may not be told to stage and may not propose it.

> ## ⭐ THE ONLY TEST: **stage N+1's code CANNOT BE WRITTEN until stage N's commit exists.**
> Not "easier", not "cleaner" — **cannot**. Only three things make it true, and a staging plan must NAME
> which and show the evidence: **the SEED WALL** (a declaration no release knows is refused by every seed,
> E2015 — ⚠ state why `scripts/build-from-seed.sh` and `scripts/seed-shim/`, which exist for exactly that,
> do not cover it; they usually do) · **a PRE-EXISTING RED you did not cause** (`/land` licenses it as its
> own commit first — name it **stage 0**) · **a USER RULING** that the halves are two deliverables.
> ⚠ The two-self-compiles rule is NOT on this list — both happen inside one `/land`, no commit between.
>
> ⛔ **NOT staging reasons, each already offered as one:** it turned out large · several passes · new IR
> ops · easier review · the first half is "independently useful" · a faster battery per stage ·
> **"land the refactor first, the feature after"**, exactly the failure `/land` measured: *"An `export`
> without its consumer is an E3092 that breaks the self-compile."*

⭐ **The default is ONE stage, and more than one is the user's call, not yours.**

---

## ⛔ HALT AND ASK

`/land`'s own halt list binds inside `/land`, unchanged. This skill adds three:

- **§5, always** — the approval halt.
- **A §1 red that invalidates the approved approach** — report the red, the contradiction and the
  options; do not re-plan silently.
- **A dispatched agent's design-flaw halt** — the only one that can arrive *after* approval.

⚠ **Not on that list:** "it turned out bigger than the plan said" (`/land` settles it) · "the plan was
wrong about a file" (amend §2a and carry on) · "this would be cleaner in two commits" (the staging box).

## Anti-patterns, each cheaper to name than to pay for

- **A plan with code in it.** The implementer follows the plan instead of the red.
- **An implementer that worked around the design instead of halting.** The user approved something else.
- **A survey re-run in §1.** Those facts were already bought; read them.
- **A stale skip asserted, not checked.** The failure is a missing case, and a missing case reads green.
- **A plan approved on a sentence.** If the user cannot see the mechanism, the file count and the
  acceptance, they approved nothing.
- **Staging as a schedule.** Two stages that could have been one is two batteries for one change.
