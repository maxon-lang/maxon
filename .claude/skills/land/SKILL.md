---
name: land
description: Implement one self-contained change end to end and push it — decide the MINIMAL spec set and watch it go RED first, write the code, green the set on a filter, optimize, independent review, document it, rebase on origin/main, then ONE gate battery (full suite + wasm lane + self-compile), commit and push. Main checkout, no worktree, no branch. Use for a bug fix, a missing diagnostic, a small feature, a defect found by hand, or any compiler change — HOWEVER LARGE it turns out to be: the task is done AS ONE CHUNK, in ONE commit, with no slicing and no escalation out of this process. Invoke as `/land <what to do>`.
---

# Land one change

**One invocation = ONE change, done WHOLE: specified, implemented, reviewed and pushed as a single
commit.** You work directly in the main checkout, alone on `main`, and you run the expensive gates
**exactly once, at the end**.

**This is the process for compiler work:** the bug you just found, the diagnostic nobody wrote, the pass
that answers wrong, a feature however large. There is no heavier sibling to escalate into.

> ## ⛔⛔ THERE IS NO ESCALATION. YOU FINISH THE CHANGE IN THIS PROCESS.
>
> **The process was CHOSEN, deliberately, by the person who invoked it. It is not a guess for you to
> second-guess once you see how big the work is.** You may not open a worktree, you may not invent a
> contract or a board, you may not stop and recommend that someone run a heavier process instead, and
> you may not land half the change and file the rest. **You finish it here.**
>
> ⛔ **NONE of these is a reason to leave, and each has been offered as one:** it turned out large; it
> touches several passes; it needs new IR ops; it wants a design decision you are able to make; it
> "deserves" a contract or a plan; it will take hours. `.claude/CLAUDE.md` settles all of them at once —
> ***"There are no time constraints. Complexity doesn't matter. If you are fixing an issue then fix it
> properly."***
>
> **What ceremony buys is COORDINATION ACROSS TREES — a contract so agents in separate worktrees can
> code against an interface that is still moving, and a board so nobody takes the same row twice.** You
> delegate too, heavily (see *Coordinating*, below) — but every agent you send works in the **SAME tree,
> under you, integrated as it arrives**. There is no second tree and no second owner, so the contract has
> nobody to inform and the board has nobody to tell. It buys you nothing and costs you the red set, the
> diagnosis and the context you have already paid for.
>
> **When the change turns out to be big, the loop is what scales — not the process:** widen the spec set
> (§1), write new IR ops as ordinary code (§2 — a "contract" is a signature you land *with* its first
> consumer, and with one author that is just writing it), delegate to another implementer agent when the
> diagnosis runs long — one mechanism at a time **into your own tree, integrated as it arrives**, which
> is delegated labour and never a slice of the deliverable — and land the whole thing in one commit.
> **A big change is a longer §2 and §3, not more commits.**
>
> **The ONLY thing that stops you is the HALT list at the end of this file, and none of its entries is a
> reroute** — each is a question for the user, answered in place, after which you carry on here.

> ## ⛔⛔ ONE CHANGE, ONE CHUNK, ONE COMMIT. **DO NOT SLICE THE TASK.**
>
> **The task you were given is the unit.** It goes through §1–§9 **once**, whole, and reaches `main` as
> **one commit**. ⛔ **No phases, no "part 1 of 3", no landing the reachable half and filing the rest,
> no per-piece commit-and-push, no separate red/green cycle per piece.** A task cut into four is not
> four small `/land`s; it is one `/land` done four times, badly.
>
> **A slice is only safe with the two things this process deliberately lacks: a separate worktree per
> slice and a board saying who owns which.** Here there is ONE tree and ONE commit, and **you are the
> integrator** — so nothing waits at the far end to put slices back together. A slice is not a smaller
> unit of work; it is an unintegrated one that lands half-finished on `main`.
>
> **Three things go wrong, and the first is the one that made this process worth writing:**
> - **The battery multiplies.** §8 is the expensive part and it runs ONCE precisely because the change
>   is one chunk. Four slices is four rebases, four full suites, four wasm lanes, four ~3-minute
>   self-compiles and four reviews — **four times the gate cost**, for the same code.
> - **`main` carries half a mechanism between slices**, and the next agent builds on it. An `export`
>   without its consumer is an E3092 that breaks the self-compile; a diagnostic no case reaches is dead
>   code; a lowering with no emitter is a wrong answer waiting for a caller.
> - **The acceptance set dissolves.** §1's red set is the acceptance for the WHOLE change. Slice it and
>   each piece invents its own smaller one — which is how "done" gets declared against cases nobody
>   chose.
>
> ✅ **What IS allowed — and it is most of the work — is delegating LABOUR.** Every step of this process
> is handed to an agent (that is the next section), and several implementer agents may run at once on
> **disjoint files**, or sequentially when the files overlap. **That is not slicing**: they share your
> one tree, you integrate as their work arrives, and the whole thing goes through §3–§9 as a unit and
> lands as one commit. **Cutting up the WORK is the method; cutting up the DELIVERABLE is the failure.**
> Also fine: fixing a pre-existing red **you did not cause** in its own commit first (§8) — somebody
> else's defect, not a slice of your task.

## The shape — who does what

**You are the COORDINATOR. You do not write the change; you own the spec set, the integration, the
gates and the commit.** Agents do the reading and the typing.

| | | who | red here means |
|---|---|---|---|
| **0** | Orient · clean tree · **build** | **you** | |
| **1** | Decide the **MINIMAL** spec set, write it, **watch every case FAIL** | **scout** agent surveys · **you** decide · **spec-author** agent writes · **you** read the red | a case that is green *now* tests nothing about your change |
| **2** | Write **all** the code | **implementer** agent(s) — one or more | |
| **3** | Green the set — **on the FILTER, never the full suite** | same agent(s) · **you** integrate and confirm | keep going |
| **4** | Optimization pass | the **`optimize`** skill, in a dispatched agent, on a trigger · **the ladder read is YOURS** | a ladder you cannot explain |
| **5** | Independent review | the **`code-review`** skill, in a dispatched agent — never one that wrote the code | fix before the commit |
| **6** | **Document it** — every comment the change gets, plus its `docs/` source | the **`documenter`** skill, in a dispatched agent | a build that no longer compiles |
| **7** | **Rebase** on `origin/main` | **you** | resolve by hand |
| **8** | THE BATTERY, once: full suite · wasm lane · **self-compile** | **you** | **stop** |
| **9** | Commit and push | **you** | a rejected push re-runs §8 |

**Everything before §8 runs on one `--filter`.** That is what makes this lightweight — and it is only
honest because §1's red was real. The filter is a *proxy* for the suite, and a proxy you never saw fail
is a proxy for nothing.

⚠ **Steps 4–8 do not reorder.** Optimize *before* review (an optimizer rewrites code and can introduce
the very duplication the review exists to catch). Review *before* commit (a review after the commit is a
bug report; before it, it is a gate). **Document AFTER the review** — comments are written once, against
code that has stopped moving, and a documenter running earlier would comment code the review then
rewrites. **Rebase before the battery** — a battery run on a tree you then rebase has tested something
you are not pushing.

## Coordinating: what you delegate, what you keep, and what you never hand over

> ### ⭐ THE LINE: **an agent may produce any amount of TEXT or CODE. You keep every NUMBER THAT GATES,
> and every decision about what "done" means.**

**Delegate by default — every step above with an agent named in it, every time.** Not because you could
not do it, but because **your context is the scarce resource**: an implementation is greps, dead ends,
IR dumps and refuted hypotheses, and none of that is anything the *gates* need. The agent absorbs it and
returns a report. You stay small enough to integrate, judge and land.

⚠ **A coordinator who starts reading a third file to form a hypothesis has stopped coordinating.** Hand
it over. **The one exception is narrow:** a single edit you have ALREADY located and can prove with one
filtered run. The moment it needs a second hypothesis or a second file, hand it over — and **§5's review
still runs**, because independence is not a size question.

**What is NEVER delegated**, because delegating it would move the gate:

- **The choice of spec set (§1).** An agent left to choose its own coverage tests what it remembered —
  which is exactly how a "finished" scalar core once scored 48 of 2,746. The scout returns FACTS; the
  set is yours.
- **Reading the RED (§1) and confirming the GREEN (§3)** with your own filtered run.
- **The `scale-test` ladder read and its `optimization-log.md` row (§4)** — attribution is only
  available now, and only to whoever knows what changed.
- **The rebase (§7), the battery (§8), the commit and the push (§9).**

### ⛔ EACH GATE RUNS ONCE, AND THIS TABLE SAYS WHERE

Every agent's brief independently tells it to be thorough, so without this table the suite runs three to
five times per change and exactly one of those runs gates anything.

| Work | Runs | Where | Everywhere else |
|---|---|---|---|
| **build** | **only when the binary is stale** | whoever last edited a compiled-in source — usually **§6's documenter**, which is the last editor | ⛔ **nobody builds a current binary** — see the box below |
| **filtered specs** | many | **each agent's own loop** | that is the loop, not a gate |
| **full suite · wasm lane · self-compile** | **once** | **you, §8** | ⛔ **no agent runs any of these** — §6's documenter finishes minutes before the battery, on the identical tree, and runs no tests at all |
| **`scale-test` ladder** | **once** | **you, §4** — plus the optimizer's own before/after, which is its instrument | ⛔ not the implementer's: a reading on a pre-review tree attributes nothing |
| **`git commit` / `git push` / `git rebase`** | **once** | **you, §7 and §9** | ⛔ **no agent commits, pushes, rebases, stashes or opens a worktree** |

**When an agent suspects something it cannot cheaply confirm, it says so in its report** — a sentence
feeding a gate that is going to run anyway beats a run nobody can attribute.

> ### ⭐⭐ NEVER REPEAT A BUILD OR A RUN WHOSE INPUTS HAVE NOT CHANGED (user directive)
> A build's inputs are the sources compiled into the compiler; a run's are the binary, the tree and the
> filter. Same inputs, same answer — so a repeat buys nothing and costs minutes.
> - **Build only when the binary is STALE** — a compiled-in source (`maxon-bin/**.maxon`) is newer than
>   it: `find maxon-bin -name '*.maxon' -newer maxon-bin/.maxon/maxon.exe | head -1` (`maxon` off
>   Windows) prints nothing when it is current, and a missing binary is stale. ⛔ **Never build "to be
>   safe"**: `spec-test` and `scale-test` refuse a stale binary by themselves
>   (`Testing/CompilerFreshness.maxon`), and a `stdlib/` edit needs no build at all — the suite
>   compiles the stdlib from source.
> - **A run already done on the same binary and tree is READ, not repeated.** Agents write every build
>   and run to a file under `temp/` and report its path and exit code; you read that file. Re-run only
>   when something it depended on has changed since.
> - **Every edit comes before the expensive runs, never beside them.** An edit to a compiled-in source
>   after a run started invalidates that run. So the battery starts only once no agent can still edit:
>   **§6's documenter is the last editor**, and its verification is a build — so the battery's
>   build-if-stale row is already satisfied when it arrives, and you do not pay for it twice.
> - **A red you did not cause is re-run by FILTER** — the failing cases alone — never by re-running the
>   whole suite.

### Every brief carries these eight things

1. **The change in one line**, and **the cases that must go green** — the acceptance, not a topic.
2. **"This is a `/land` change"**: main checkout, no worktree, no branch, **one commit at the end, which
   is the coordinator's**. ⛔ Do not commit, push, rebase or stash.
3. **Its EXCLUSIVE file list**, when more than one agent is live. One file, one owner — and agents run in
   parallel only on disjoint files, sequentially otherwise.
4. **The DIAGNOSIS you already have** — symptoms, exit codes, stderr, the call sites you read, the
   hypotheses you already refuted. Making an agent re-derive this is the one waste delegation exists to
   prevent.
5. **Its STOP RULE**: stop when your filter is green; the full suite is the coordinator's.
6. **The gate table above**, in one line: which runs are yours, which are not.
7. **"No manual tests"** — no sabotage, no hand-run probe or scratch program to confirm anything. A
   check worth running goes into the set as a case; anything else is not run (the box below).
8. **"Write every build and run to a file under `temp/`, and report each one's path and exit code"** —
   including your LAST build, and whether you edited a compiled-in source after it. That report is what
   lets the coordinator read your work instead of repeating it.

⛔ **Brief for DIAGNOSIS, never for COVERAGE.** "Enumerate every construct that could false-reject and
prove each parses", "confirm no other spec regressed", "check nothing was left disabled" — the §8
battery and §1's count check already do all three, better, one step later. ⚠ **A specific instruction in
your brief OUTRANKS the agent's own stop rule**: you cannot brief thoroughness in without briefing the
stop out.

> ### ⛔ NO MANUAL TESTS. A CHECK WORTH RUNNING IS A CASE; ONE THAT IS NOT IS NOT RUN (user directive)
> Verification lives in cases and nowhere else — a case in `specs/`, or for a driver command its
> `tests/` corpus. **No agent sabotages code, a test, a fixture or a file to watch a check fail, and
> nobody verifies by a hand-run probe, a scratch program or a one-off script.** A check worth running
> is worth keeping: write it as a case, in the set, where the battery and the wasm lane run it every
> time and a reader can see it. A check not worth a case is not worth running at all.
> - **§1's red IS the sabotage every case needs**: it watched the case fail before the fix existed.
> - **Diagnosis is not verification.** Running a reproduction to find a cause is part of §2; the
>   moment it shows a wrong answer, it becomes a case in the set — never evidence in a report.

### Verify the claims, do not re-derive the work

**Do not trust a report — read its evidence.** Read the crux run's log file yourself, check the binary
was current when it ran (the staleness check above), read the crux lines of the diff, **check exit
codes, and never grep for a success string** — a past session reported a
green build by grepping for `^error` while the real failure printed `[CMP] ERROR:`. Exit **101** is a
leak. What you must NOT do is re-derive the agent's diagnosis or re-run what it already ran; auditing
its diff for quality is **§5's** job, not yours.

---

## 0. Start — yours

*(Two tool calls and a sentence. Delegating this would cost more than doing it.)*

**Say in one or two sentences what the change is and what will prove it**, before touching anything, so
the user can redirect you cheaply. If the ask is ambiguous *in a way that changes the code*, this is the
cheapest moment to ask.

- ⚠ **`git status` must be CLEAN**, so the one commit holds this change and nothing left over from
  another. (Goldens the suite touches along the way are not a concern — §8.)
- **BUILD — IF, AND ONLY IF, THE BINARY IS STALE** (the check in the box under the gate table). The
  compiler binary is gitignored and nothing rebuilds it, so a stale one lies in *both* directions.
  `build` compiles `maxon-bin/` with the compiler already in the slot and renames the result into
  place, falling back to a released binary at `.bootstrap/maxon` when the slot is empty. A current
  binary is already what every red in §1 is read off: rebuilding it is minutes spent to learn nothing.
- **No baseline suite run.** The §8 gate is `failed: 0`, not a delta from a remembered total, so there is
  nothing to measure yet. (When §8 comes back red you therefore may not assume the red is yours — §8 says
  how to attribute it.)

## 1. The MINIMAL spec set — and it goes RED before you write any code

**Minimal means the smallest set that FAILS today for the reason you are about to fix, and would fail
again if the fix were reverted.** Not "few": one case per distinct wrong answer, plus the error path if
the change adds or moves a diagnostic. A case that cannot fail for your reason is padding; a wrong
answer with no case is the thing this skill exists to prevent.

⚠ **"Minimal" describes the SPEC SET, never the change.** The set is the smallest thing that proves the
**whole** task — not a smaller task. If a case you would need is inconvenient because the change is
large, that is the set telling you the size of the work, and the answer is §2, not a narrower set.

### The scout finds the material; YOU pick the set; an author writes it

**Send a read-only survey agent first** (`Explore`). It reads; it does not run probes — what the
compiler does today is what §1's red run of the chosen cases shows.
Ask it for **FACTS, not a recommendation**: which `specs` files own this behaviour, every existing
case that touches it with file + line, every `disabled-test:` in range, whether `/specs` pins it and the
**verbatim text** of the case that does, and what the neighbouring cases in that file look like.

**Then YOU pick the set** — from its facts, against the criterion above. That decision never moves.

**Then hand the writing out**: give an agent the exact list — file, case name, program, expected
`exitcode`/`stdout`/`maxoncstderr`, and the marker to flip for anything already present — and let it
write them into `specs/`, and **tell it to invoke the `maxon-coder` skill before writing a case**. *(For
one or two cases, writing them yourself is cheaper than the brief — invoke `maxon-coder` yourself first.
That is a cost call, not a rule.)*

**Where the cases come from, in this order:**

1. **An existing `specs/` case.** Grep first — the behaviour may already be pinned, and then your
   set is "these three, which are red". A `<!-- disabled-test:` marker your change unblocks is a case you
   **enable**: those markers are DEBT, not precedent.
2. **A new case in the `specs/*.md` file that already owns the behaviour.** Format:
   `docs/SPECS.md`. Follow the neighbouring cases' shape.
3. **A case lifted from an unported `/specs` file** — copy it VERBATIM (program, `exitcode`, `stdout`,
   `maxoncstderr`, name), never paraphrased and never renamed. A real compiler passed those cases
   unedited, so a copied case is a claim someone already satisfied; a case you reworded is a claim you
   made up. ⚠ **Do not port the rest of that file here** — take the cases your change needs and leave
   the others where they are. That is a queue position, not a decision, and it needs no note anywhere.

⛔ **Cases go in `specs/`.**

**A user-visible change also names its documentation gate — but not as a red-first case.** When it adds a
command, option, MCP tool or `public` stdlib API, the doc-coverage gate that owns the surface
(`maxon test tests/cli --filter=reference-documents`, `tests/mcp --filter=reference-documents`, `tests/docs`) reads the
built compiler or the tree's `stdlib/`, so it is green today and turns red only once the new surface exists.
It is not in this set; it is acceptance, read in §8's battery after §6 has written the documentation.
Every other user-visible change still owes its `docs/` source an edit (§6), which the gates cannot see.

**Then SEE IT RED.** A spec is data — no rebuild needed to run one.

```
run_spec_test filter=<pattern>
```

- **Read the failure of EVERY case in the set** and record the exact symptom — exit code, stderr, diff.
  That is your acceptance criterion *and* the most valuable input to the diagnosis.
  (`spec_test_outcome` with the same `filter` gives per-case PASS/FAIL detail when the summary is not
  enough.)
- **A case that is already GREEN is not in the set.** Either it does not test your change, or the change
  is already done. Find out which before writing a line.
- ⚠ **`--filter` is ONE substring**, matched against the `<spec>/<case>` label (`selectedByFilter`) — no
  lists. Name new cases so one distinctive substring selects the whole set; otherwise run one filter per
  file and read every one of them.

> ### ⛔ COUNT WHAT RAN. A spec can pass by running NOTHING — three ways, all of which read as green.
> ```bash
> grep -c '<!-- test:'          specs/<file>.md   # must EQUAL the runner's `total` for your filter
> grep -c '<!-- disabled-test:' specs/<file>.md   # cases you did not enable — you may write none
> grep -o '<!-- \(disabled-\)\?test: [^ ]*' specs/<file>.md | sed 's/.*test: //' | sort | uniq -d
> ```
> 1. **A shortfall against the runner's total is a defect in the spec, never a pass.** The two causes are
>    `status: draft` in the frontmatter (returns ZERO tests for the whole file) and a `## ` heading, which
>    **ENDS the active-test region** — every case below a stray `## Notes` silently disappears.
> 2. **The `uniq -d` must print nothing** — a name spelled both `test:` and `disabled-test:` reads as
>    disabled while it still runs.
> 3. ⛔ **You may not write a `disabled-test:`.** A case you cannot make pass is a HALT, not a marker.

## 2. Write the code — a `general-purpose` implementer agent

**Hand the implementation to a `general-purpose` agent.** The brief is §1's red set, the eight things
every brief carries, and above all **the diagnosis you already did while reading the red** — that is
the most valuable thing in it.

**ALL of the code lands in the main checkout before anything is committed.** *(More than one agent is
fine on **disjoint** files, sequentially when they overlap: never two agents in one checkout on
overlapping files. That is a scheduling rule about LABOUR — you integrate as their work arrives, and the
deliverable stays one chunk and one commit.)*

**What the agents must be told, beyond the standard eight:**

- **Invoke the `maxon-coder` skill before writing any Maxon**, and hand back only code its compile
  loop has passed.
- ⛔ **WRITE NO COMMENTS** — not a `//`, not a `///`, not the doc block above a registry case. The code
  is still moving, so a comment written now is written and rewritten and mostly thrown away. **§6
  authors every comment the change gets**, once, after the review. A reason worth keeping goes in the
  agent's REPORT, which is what §6's brief is built from.
- **They work in the MAIN checkout, so the `maxon` MCP tools need no `repoRoot`** — and they do not
  commit, do not `git add`, do not push, and leave every golden the runs touch exactly as it lies —
  golden drift is not theirs to measure, investigate or worry about. You commit everything, once, at §9.
- ⛔ **`/specs/**` is READ-ONLY — not one byte.** It is the canonical definition of the language; an edit
  there does not adjust a test, it redefines Maxon. Cases go in `specs/`,
  and a fix that would falsify another spec's committed expectation is a STOP-and-report, never an edit
  to that spec.
- **Root causes, no workarounds** — and a defect is fixed whether or not it predates you (CLAUDE.md).
- **Cross-target consistency**: an x64 change needs its arm64 equivalent. The wasm lane runs in §8 and
  is not scalar-only — a float or `String` case failing there is a bug on that lane.
- **A new diagnostic goes through the registry, which is HAND-AUTHORED and has NO generator**: add a
  `<caseName> = "EXXXX"` entry to `maxon-bin/Compiler/ErrorCodeRegistry.maxon`, then write the code that
  emits it. ⛔ **Not its doc comment — that block IS a documentation surface**
  (`Mcp/McpErrorCodes.maxon` re-parses the registry SOURCE at runtime to serve `lookup_error_code`, and
  the generated Error Codes page is built from it), **so §6 writes it.** ⚠ `sync-docs.mjs` fails closed
  on a case with no doc comment, so it is RED between here and §6 for a change that adds a diagnostic —
  that is expected, and it is how §6 finds them. Take the next free number in the band by reading the enum
  (1xxx lexer, 2xxx parser, 3xxx semantic, 4xxx IR, 5xxx emitter, 6xxx PE writer, 9xxx internal): a
  duplicate NAME does not compile, and a duplicate NUMBER is structural in neither direction and is
  caught only by `website/scripts/sync-docs.mjs`, which §8's documentation row runs. Never write a bare
  `"E3010"` in source — a diagnostic names `ErrorCode.<case>`.
  ⛔ **`docs/error-codes.txt` and `maxon error-codes generate` DO NOT EXIST** — no such file was ever
  tracked in git and `MaxonCommand` has no such case. Stale citations of both survive in
  `GlobalInitOrder.maxon`, `Lsp/LspDiagnostics.maxon`, `Parser.maxon`, `Queries.maxon`,
  `Runtime/RuntimeAbort.maxon` and six files under `specs/`. Follow the enum, not them.
- **A mechanism that does not exist yet gets BUILT** — a builtin, a runtime slice, an opcode on every
  target. Size is never a reason to stop; see the two boxes at the top of this file.
- **Documentation is §6's, not theirs** — the `docs/` source, the site regeneration and the doc-coverage
  gates all move there, in the same pass that writes the comments. What the implementer owes is the
  report: **what the change made visible to a user**, in the terms `.claude/CLAUDE.md`'s Documentation
  section uses, since that is the judgement it has the context to make.

⚠ **Whoever writes the code, §5's reviewer must not be them.**

## 3. Green the set — the agent's loop, your confirmation

**The agent iterates: edit → build → filtered run**, until its cases are green. It rebuilds after every
edit to a compiled-in source and at no other time — a stale binary reads green or red for the wrong
reason, and a current one rebuilt reads exactly what it read before. **Then YOU read its last filtered
run** — the log file it reported, with the binary current when it ran — and that is the only thing
standing between an agent's report and §8. Re-run the filter yourself only when that evidence is
missing or something changed after it.

- ⛔ **NOBODY runs the full suite in this loop — not you, not the agent.** It is §8's, once. A suite
  run per iteration is the single largest piece of duplicated work these processes have ever paid for,
  and every agent's brief must say so.
- ⛔ **Never turn a case green by narrowing what it tests** — not the spec text, not the filter, not a
  marker. A green suite that tests nothing is the most expensive lie a test runner can tell.
- **A defect ANYONE finds on the way is FIXED, not filed** — a wrong answer as much as a leak, whether or
  not the suite is green over it. **And the reproduction that found it becomes a case in the set.** A
  hand-run check is never evidence — it is unrepeatable, unreviewable, and invisible to the wasm lane a
  spec case reaches for free — so none is run to verify anything (the NO MANUAL TESTS box).
- **When the set is green, re-run §1's count check yourself**, then stop editing for correctness. An
  agent reporting "all green" and a filtered run you read are not the same evidence.

## 4. The optimization pass — the AGENT on a trigger, the LADDER always yours

**Three parts, and only the first is unconditional.**

- **(a) A scan of the diff for unscalable structure — always.** When (c) fires, the optimizer does this
  and more; when it does not, hand the diff to a **read-only scan agent** (`Explore`) with this list. An
  O(n) lookup inside an O(n) walk; a `findFirst` over a compiler-sized array in a loop; a fixpoint,
  liveness or dominator tree recomputed per element; a walk over a dense index space where the set bits
  were the point; an allocation in a hot path. The compiler must stay LINEAR in program size.
- **(b) The ladder — `run_scale_test` (~17 s)** when the change touched a pass, the IR, or a
  collection the compiler indexes by. ⚠ **It is an INSTRUMENT with no verdict — there is no green one, and
  you never touch it to make a number look better.** Read the doubling ladder straight: **×2 is linear,
  ×4 is quadratic.** Read it off the **ALLOCATION** columns, which are exact and bit-for-bit
  reproducible; the CPU column carries a few-percent noise band and a platform-defined unit. A Δ0 from a
  ladder whose corpus cannot express your feature is a blind spot, not a result.
- **(c) The `optimize` SKILL, in a dispatched agent — on a trigger:** the change adds a pass, an IR op,
  or an indexed collection; or (b)'s allocation ladder bends ≳2.4 per doubling and you cannot explain it.
  Send a `general-purpose` agent and tell it to invoke `optimize`; **it must not be an agent that wrote
  the code.** Say in the brief that this is a `/land` change — the diff is uncommitted in the main
  checkout and the battery is YOURS, minutes later — so it neither runs the suite nor commits. **If no
  trigger fired, say so in the report** — a superlinear hunt over a change that added no algorithm has
  nothing to find.

**If you ran the ladder, write the row in `docs/optimization-log.md` now** — attribution is only
available now; the instrument sees exactly WHAT moved and can never see WHY, and ten changes later
neither can you. ⚠ **Write no row you did not measure.**

## 5. The review

**Send a `general-purpose` agent and tell it to invoke the `code-review` skill**, on the working diff —
uncommitted, main checkout — after §4 and before the commit. **It must not be an agent that wrote the
code**; that independence is the whole point. The skill carries the criteria; your brief carries the
situation:

- **Say this is a `/land` change** — the diff is uncommitted in the main checkout, there is no worktree
  and no branch, and the battery is YOURS, minutes later. The skill's steps 6–7 (gates, commit) are
  standalone-only and it must skip them.
- **There is no backlog file**, so anything it leaves for triage arrives in its REPORT and you decide it
  here.
- ⛔ **COMMENTS ARE NOT ITS CONCERN** — the tree it reads has none by design and §6 writes them all,
  minutes later. Do not brief one in: this file's own warning applies, and a specific instruction in
  your brief outranks the skill's rules.
- ⛔ **Do NOT ask it to run the full suite, prove coverage, or establish correctness.** Your battery runs
  minutes later on the identical tree, and the suite is a far better false-reject detector than anything
  it can probe by hand — and it runs no sabotage and no hand probes at all: a finding that needs a run
  to show it is a case it adds to the set, or a reading-only finding in its report. Its `--filter`ed
  runs are its own. ⚠ A specific instruction in your brief
  OUTRANKS its standing stop rule — you cannot brief thoroughness in without briefing the stop out.
- **The bounds questions are YOURS**, and take seconds off `git status --short` and `git diff --stat`:
  did anything land outside this change? Is the diff as small as the fix warranted? **Did any spec
  expectation get weakened to pass?**
- **Findings are FIXED BEFORE the commit**, then the filtered set re-run. ⚠ Its report is a lead, not a
  verdict — this project's history is full of agent findings that measurement refuted. Confirm before
  acting, and say so if you disagree.

## 6. Document the change — the `documenter` skill, in a dispatched agent

**Send a `general-purpose` agent and tell it to invoke the `documenter` skill.** It writes **every
comment the change gets** — nobody before it wrote one — and updates the `docs/` source its surface
owns, then regenerates the site pages.

⭐ **This is the LAST step that edits the tree.** It runs after the review for one reason: **comments are
written once, against code that has stopped moving.** A documenter before §5 would comment code the
review then rewrites, which is the write-it-twice waste the no-comments rule exists to remove.

**What the brief carries, beyond the standard eight:**

- ⭐ **THE DIAGNOSIS — the change's *why*.** This is the most valuable thing in the brief and it exists
  nowhere else: a why-only comment cannot be composed from a diff. Also the implementer's and
  optimizer's reports, which hold the reasons behind an unobvious shape, a deliberate asymmetry and any
  forked fast path.
- **What the change made user-visible**, in `.claude/CLAUDE.md`'s terms — or that nothing is, which is
  an answer it must state rather than skip.
- ⛔ **It runs NO tests** — not the suite, not the wasm lane, not the doc-coverage gates. Its
  verification is `mcp__maxon__build`, and §8's battery runs everything else minutes later on the same
  tree. It regenerates the site pages but does not run `sync-docs.mjs --check`; that is §8's.
- **It does not commit, `git add`, push, rebase or stash**, and it changes not one byte of code.

⚠ **Read its report, do not re-derive it.** The line that matters is the count: comment lines added,
rewritten, deleted, net. **A change that came back with a paragraph per declaration has failed the
skill's own first rule** — the default is no comment, and most declarations end with none. Spot-check
its list of facts it did NOT write, which is where a wrongly-dropped constraint would be.

**Its build is the battery's build.** §8 rebuilds only if the binary is stale; §6 is the last editor and
its verification is a build, so that row is already satisfied unless the rebase moves a source.

## 7. Rebase on `origin/main` — yours

**No agent rebases, stashes, commits or pushes.** Git state is the coordinator's, start to finish.

```bash
git stash push -u -m land && git pull --rebase && git stash pop
```

**Rebase FIRST, so §8 runs on the tree you will actually push.** Other agents land on `main` between and
during changes; a battery run before the rebase measured a tree that no longer exists.

- **A conflict is resolved BY HAND, hunk by hunk.** ⛔ Never `git checkout --ours/--theirs` a doc file to
  clear one: a whole-file resolution in a doc has silently reverted somebody else's landed section, and
  it looks clean in `git diff`.
- **A stash must never outlive this step.** If the pop conflicts, resolve it now — work sitting in
  `git stash list` looks exactly like work that vanished.

## 8. THE BATTERY — once, in this order, on the rebased tree — yours

⛔ **No agent runs any part of this.** Every brief says so; this is where that promise is kept.

| Gate | |
|---|---|
| **Build** exit 0 | **Only if stale** — §6 built last, so this is stale only if the rebase moved a compiled-in source. A current binary is not rebuilt: the SELF-COMPILE below is the one build of it the battery runs |
| **Full `run_spec_test`** | **`failed: 0`**, and no exit **101**. The gate is zero failures *including every pre-existing test*, never a total |
| **`run_spec_test target=wasm32-wasi`** | `failed: 0`. Default battery, not an extra (user ruling, 2026-08-29) |
| **SELF-COMPILE** — `maxon-bin/.maxon/maxon.exe build maxon-bin --output=temp/land-selfcompile` | exit 0, ~5 min. Output discarded; only the exit code matters. The tree binary is stage-2, so this is its stage-3 build and it is slower than the seed's |
| **The tree corpora** — `maxon test tests/fmt`, `maxon test tests/spec-harness --timeout=15000`, `maxon test tests/ladders` | each `0 fail`, and read each count. These are the formatter's engine corpus, the spec harness's own refusals and gates, and the ladder index — tree-level gates `spec-test` does not run. CI runs the same three on every lane |
| **Documentation** — `node website/scripts/sync-docs.mjs --check` | exit 0 — §6 regenerated the pages but ran no check, so this is where they are gated. And for a user-visible change, each doc-coverage gate its surface owns (`maxon test tests/cli --filter=reference-documents`, `tests/mcp --filter=reference-documents`, `tests/docs`): `0 fail`. A change with nothing user-visible states that instead. ⚠ A red here after a new diagnostic means §6 did not write the registry doc comment |
| **Golden drift staged, as it is** | `git add -A specs/` — whatever the runs minted, modified or deleted, with no further thought. See the box below |
| **§1's count check** on the final tree | markers == ran, none disabled, no name spelled twice |

> ### ⭐ THE SELF-COMPILE IS THE GATE THE SUITE CANNOT SUBSTITUTE FOR
> `checkUnusedExports` (**E3092/E3093/E3094**) runs only when the compiler compiles a *program*, and the
> only program that names every `export` in `maxon-bin/Compiler/` is the compiler itself. **A three-line
> commit once broke stage-2 with the suite at 6673/0.**
> Run it after any change that adds or removes an `export` or a `module`, or adds a file declaring
> TYPES; run it anyway, it is three minutes. An E3092 here is a declaration more visible than its uses:
> narrow it, or land it *with* its first consumer.

> ### ⭐ GOLDEN DRIFT IS NOT A CONCERN. COMMIT IT AND MOVE ON.
> A run MINTS a golden a case has none of, on the host where the case passed, and never rewrites one that
> exists: a golden whose bytes differ is REPORTED as drift, not rewritten, and only `--update-required
> --filter=<spec>` rewrites it. Re-mint the specs whose runtime bodies your change moved, on every lane
> you can run, and mint the arm64 lanes on the Mac (CI fails a lane that leaves untracked goldens and
> uploads them as an artifact to unpack at the repository root). Whatever the runs minted or rewrote — by
> your change or anyone else's — goes into the commit with `git add -A specs/`, and that is the whole of
> it. **Do not measure it, investigate it, attribute it, explain it or review it**, and do not mention it
> in the message. Never revert it: a `git checkout --` to tidy `git status` is the one wrong thing to do
> with drift.

**A red gate STOPS the change** — and a red you did not cause still gets fixed, in its own commit,
before yours (CLAUDE.md). To attribute it: do the failures touch what you changed? Re-run **the failing
cases alone, by filter** — never the whole suite for them. If that still does not settle it, **measure
the control** on that same filter — `git stash -u`, re-run, `git stash pop`. ⚠ **A lane that
did not RUN is not a red gate** (remote arm64 is outside this battery): that is a SKIP you report, never
folded into the green — and §9's CI watch is where every lane you skipped is answered.

## 9. Commit and push

**ONE commit, on `main`** — this repo develops there; do not branch. The whole change lands together:
the compiler source, the spec cases, **the documentation and the site pages it regenerates**, **every
golden the runs touched — minted, modified or deleted** — and any `optimization-log.md` row.
⛔ **Not a commit per piece, and never a partial landing with the rest "to follow"** — the battery you just ran was run on the whole tree, so the whole tree is what it
licensed you to push.

**The message carries what the diff cannot:** the wrong answer that motivated the change, the mechanism,
the cases that went red → green, and the gate figures you measured. ⚠ **Never a figure you did not
measure** — a number nobody took looks exactly like one that was.

```bash
git push origin main
```

⛔ **A REJECTED PUSH IS NOT A RETRY.** Someone landed while you were running, so the tree you tested is
not the tree you would push: **rebase and RE-RUN §8** before pushing again.

> ### ⭐ THE PUSH IS NOT THE END OF THE GATE — WATCH CI SETTLE
> ```bash
> gh run watch "$(gh run list --limit 1 --json databaseId --jq '.[0].databaseId')"
> ```
> **CI hosts every target on its own architecture and runs the two-stage seed build, and your battery
> runs neither.** `--target=` cross-compiles the PROGRAMS while the compiler stays a host process, so a
> defect that only appears when the compiler itself runs on that platform is invisible to you and
> certain on CI (CLAUDE.md's Targets box carries the measurement). ⛔ **Do not report the rung done
> until all four lanes are green.** A red `main` is inherited by whoever lands next, and by then the
> break is no longer attributable to the commit that caused it.
>
> A red lane is yours to fix whether or not you caused it (CLAUDE.md), in its own commit — and reproduce
> it on that platform's own host, never on the cross lane that missed it.

Then report in a few lines: the change, the cases that went red → green, each gate's number, and
anything skipped with the reason.

## ⛔ HALT AND ASK

Everything above runs unattended. Stop and report, without landing, when:

- **A gate is red and the fix is not yours to make.**
- **A DESIGN RULING is needed** — the specs and the compiler disagree about what is *correct*, or `/specs`
  contradicts itself. **Do not guess, and never edit a spec to match the compiler**: that is how a
  compiler bug becomes a specification.
- **A case would have to be disabled**, or **an existing `specs` expectation rewritten**. The first
  is the failure mode this whole process exists to prevent; the second has a blast radius beyond your
  change and belongs to whoever owns that behaviour.

**Each of those is a QUESTION, answered in place — never a reroute.** You stop, report, and wait; you do
not start a rung, open a worktree, or hand the work to another process. When the answer comes you carry
on from where you stopped, in this file.

⚠ **NOT on that list, ever:** "this is big"; "this deserves a rung"; "I could not find the gap" (that is
diagnosis you have not finished); "a contract would be cleaner"; **"I will land this part now and the
rest after"** — there is no partial landing, and no slice. **The only honest stop is a question the user
has to answer.**

## The thing this process exists to catch

**A change that is green because nothing tested it.** Everything expensive here runs once, at the end,
and every cheap thing before it is a proxy — so the whole loop rests on §1's red being real. **Watch
every case fail before you write the fix, count what actually ran, and never make a case go away.**

**And the reason the coordination exists at all:** that red set, the integration and the gates are
judgement work, and they are the first thing to go when a coordinator spends its context on greps and
IR dumps. **Send the agent. Keep the judgement.**
