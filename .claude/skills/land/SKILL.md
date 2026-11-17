---
name: land
description: Implement one self-contained compiler change end to end and land it as one commit on main through land.yml — a spec set watched red first, the code, optimization, independent review, documentation, the landing gates once (build, filtered specs, codecs check, plus lint, self-compile and the seed chain when the diff calls for them; CI runs the rest), then the door's verdict. Use for a bug fix, a missing diagnostic, a defect found by hand, or a feature of any size. Invoke as `/land <what to do>`.
---

# Land one change

**One invocation = ONE change, done WHOLE: specified, implemented, reviewed and landed as a single
commit** that ends at `land.yml`'s verdict, in the main checkout, on `main`, with no branch. The landing
gates run **exactly once, at the end**, and there is **no full local battery**: you submit on a build,
filtered specs covering what changed and the codecs check; CI runs the rest. **This is the process for all compiler work** — a bug just found,
a missing diagnostic, a pass that answers wrong, a feature however large; there is no heavier sibling.

**Beside this file, read on demand:** `brief.md` before any brief; `situations.md` when a situation it
names arises; `known-issues.md` for §10 outside the fleet. Seed-chain landing: `compiler-workflow`.

> **Whoever invoked `/land` chose it deliberately: finish it here however large**: no worktree, contract or board, no heavier process, no landing half
> and filing the rest. Size, several passes, new IR ops, a design decision you can make, or hours of work
> are no reason to leave (`AGENTS.md`); a big change is a longer §2 and §3, not more commits. The only
> stops are the HALT list, each answered in place, then you carry on. **Do not slice**: §1–§9 run once,
> to one commit, since §8 runs once (slices multiply bounces, gates, self-compiles, reviews), a slice
> leaves `main` half a mechanism (an `export` without its consumer is an E3092 failing CI's lint; an
> unreached diagnostic is dead; a lowering with no emitter is a wrong answer waiting), and §1's red set
> is the whole change's acceptance. Delegating labour is the method: several implementers at once on
> disjoint files (sequentially when they overlap), in your one tree, integrated as work arrives. Fixing a
> pre-existing red you did not cause, in its own commit first (§8), is fine.

## The shape

**You are the COORDINATOR: you do not write the change; you own the spec set, the integration, the
gates and the commit.** Agents do the reading and the typing.

| § | Step | Who | Red here means |
|---|---|---|---|
| 0 | Orient, clean tree, build | you | |
| 1 | MINIMAL spec set, **every case watched FAILING** | scout, then **you** pick, an author writes (located fix: you); **you** read the red | a case green now tests nothing |
| 2 | All the code | implementer(s) | |
| 3 | Green the set **on the FILTER** | same; **you** confirm | keep going |
| 4 | Optimization | `optimize` on a trigger; **the ladder read and its note are yours**; the door mints the row | a ladder you cannot explain |
| 5 | Review | `code-review`, never a writer of the code | fix before the commit |
| 6 | Every comment, the `docs/` source | `documenter` | a non-empty code-identity proof |
| 7 | You do not rebase for the door | you | |
| 8 | THE LANDING GATES, once | you | **stop** |
| 9 | Commit, land through the door; do not wait for CI | you | a bounce: rebase by hand, re-take gates whose inputs changed |

**Every spec run, in §8 as before it, is `--filter`ed**: the whole set in ONE run, one `--filter=` per
pattern (MCP: a `filter` array), so its files run as parallel jobs. That is honest only because §1's red
was real: a proxy you never saw fail is a proxy for nothing. CI runs the suite.

⚠ **Steps 4–8 do not reorder.** Optimize before review (an optimizer can add the duplication review
catches); review before commit (after it, a review is a bug report); document after review (comments are
written once, on code that stopped moving).

## Coordinating

> ⭐ **THE LINE: an agent may produce any amount of TEXT or CODE. You keep every NUMBER THAT GATES, and
> every decision about what "done" means.**

**Delegate by default — every step with an agent named, every time**: your context is the scarce
resource. ⚠ **Reading a third file to form a hypothesis is no longer coordinating**: hand it over. **Each
stage is its own fresh agent, however small**: inline at your context it costs about 2.6 times as much,
since every call you make re-reads all you hold. **The one exception:** a single edit you ALREADY located
and can prove with one filtered run, and §1's located-fix cases; at a second hypothesis or file, hand it
over, and **§5's review still runs**: independence is not a size question.

**Never delegated**, since that would move the gate: **the choice of spec set** (the scout returns facts);
**reading the red** (§1) and **confirming the green** (§3) with your own filtered run; **the ladder read**
and its note (§4); **the gates, the commit, the submit** (§8–§9).

### ⛔ Each gate runs once, where this table says

| Work | Runs | Where | Everywhere else |
|---|---|---|---|
| **build** | only when stale | **you, §8** (the documenter, last editor, does not build) | ⛔ nobody builds a current binary |
| **filtered specs** | many | each agent's own loop | the loop, not a gate |
| **§8 gates** (final filtered specs, codecs, lint, self-compile, seed chain, docs check) | once | **you, §8** | ⛔ no agent runs any; the documenter runs no tests. Full suite, wasm lane, tree corpora: CI |
| **`scale-test` ladder** | once to read | **you**: read §4, the note in §9; the door mints the row; plus the optimizer's before/after | ⛔ not the implementer's: a pre-review reading attributes nothing |
| **`git commit`/`push`/`rebase`, `land.sh submit`** | once | **you, §9** | ⛔ no agent commits, pushes, rebases, stashes, submits or opens a worktree |

Without this table each brief's thoroughness runs the suite 3–5 times per change, once gating. **An agent
reports what it suspects but cannot cheaply confirm**: it feeds a gate that runs anyway.

> ### ⭐⭐ NEVER REPEAT A BUILD OR A RUN WHOSE INPUTS HAVE NOT CHANGED (user directive)
> A build's inputs are the compiled-in sources; a run's, the binary, the tree and the filter. Same inputs,
> same answer: a repeat costs minutes and buys nothing.
> - **Build only when STALE**: `find maxon-bin -type f -name '*.maxon' -newer maxon-bin/.maxon/maxon.exe
>   | head -1` (`maxon` off Windows) prints nothing when current; a missing binary is stale. ⛔ **Never
>   "to be safe"**: `spec-test` and `scale-test` refuse a stale binary. A `stdlib/`, `runtime/` or
>   emitted-runtime edit: `situations.md`, "Deciding to build".
> - **A run done on the same binary and tree is READ**: agents log every build and run to a scratchpad
>   file and report its path and exit code. **Every edit precedes the expensive runs** (a later edit invalidates them): the documenter is
>   the last editor, so §8's build is the one after review. **A red you did not cause is re-run by FILTER**,
>   the failing cases alone.
> - **A result is re-taken only when an input changed.** A compiler-dependent gate's inputs (build,
>   filtered specs, corpora, IR pins, lint, self-compile, seed chain) are the sources under `maxon-bin/`,
>   `stdlib/`, `runtime/` plus its own data files (spec files, corpus, pins, lint subjects); any other
>   gate's, the files it reads. After a fix round or manual rebase **list the changed paths**
>   (`git diff --name-only <tree then> <tree now>`, trees named per `situations.md`): **a gate with no
>   input listed stands**, its log quoted. **Others' changes in files you did not touch are not your
>   inputs**: drop them; CI is where disjoint changes meet. **A measurement over fixtures** re-takes only
>   fixtures a round could move (`situations.md`).

### Agents

- **Briefs follow `brief.md`**: the nine things plus the stage's lines.
- **Every running agent is watched by `watchdog.mjs`** (`situations.md`, "Running the watchdog"): no
  model calls, it exits only on a trip, which wakes you. Run it with `run_in_background` at the first
  dispatch; whenever the set of running agents changes, stop it and run it again over those still running.
- **Reports are files** (`brief.md` item 8): read each summary; read further by offset only when a
  summary line needs it; hand the next agent the **path**, never the text.
- **When an agent reports done, kill any shell it left running** before the next step: a leftover loop
  can kill another build or swap the compiler in the slot.

- **Each stage runs on the cheapest model that can do it**: `brief.md`'s model table.

> ⛔ **NO MANUAL TESTS (user directive).** A check worth running is a case — in `specs/`, or a driver
> command's `tests/` corpus — where CI runs it on every lane; one not worth a case is not run. **Nobody
> sabotages code, a test, a fixture or a file to watch a check fail, or verifies by a hand-run probe,
> scratch program or one-off script**: §1's red IS every case's sabotage. **Diagnosis is not
> verification**: a reproduction showing a wrong answer becomes a case, never evidence in a report. A
> diagnosis reading counts only on `situations.md`'s terms.

**Verify claims; do not re-derive work.** Read the crux run's log, check the binary was current then,
read the crux diff lines, **check exit codes, never grep for a success string** (failures share no
prefix: `[CMP] ERROR:` misses `^error`). Exit **101** is a leak. Do not redo an agent's diagnosis or
runs; auditing its diff is §5's.

## 0. Start — yours

*(Two tool calls and a sentence: delegating costs more.)* **Say in one or two sentences what the change
is and what will prove it**, before touching anything, so the user can redirect you cheaply; ask now
about any ambiguity that changes the code.

- ⚠ **`git status` must be CLEAN**, so the commit holds this change only.
- **BUILD IF, AND ONLY IF, STALE** — the binary is gitignored and nothing rebuilds it, so a stale one lies
  both ways: `./maxon-bin/.maxon/maxon run build` at the repo root. An EMPTY slot is refused: fetch the
  seed (`scripts/fetch-seed.sh`) and build as `maxon-bin/AGENTS.md` says. A current binary is what §1's
  reds are read off: rebuilding it learns nothing.
- **No baseline suite run**: §8's gate is `failed: 0`, not a delta, so a §8 red is not assumed yours.

## 1. The MINIMAL spec set — RED before any code

**The smallest set that FAILS today for the reason you are fixing, and would fail again if the fix were
reverted**: one case per distinct wrong answer, plus the error path when a diagnostic is added or moved.
A case that cannot fail for your reason is padding; a wrong answer with no case is what this skill
prevents. ⚠ **"Minimal" describes the SET, never the change**: it proves the **whole** task; a case
inconvenient because the change is large is the size of the work (§2).

**A LOCATED fix** (an issue with `Repro` and `Where`, at most two cases that go red at once:
`situations.md`) skips the scout and the spec author, and nothing else. **Otherwise the scout surveys** (read-only, no probes; the facts it returns are in `brief.md`), **YOU pick
the set** from its facts — a decision that never moves — and **a spec author writes** your exact list.
⚠ **Cases or a failure count the ask names, run, are part of the acceptance**; a narrower hand-built red
supplements them, never replaces them. **Cases come from**, in order: an existing `specs/` case (grep
first); a new case in the `specs/*.md` file that owns the behaviour, in its neighbours' shape
(`docs/SPECS.md`).

- **A user-visible change's doc gate, a deletion and a pure optimization** have no red-first case:
  `situations.md`.

**Then SEE IT RED** — a spec is data, no rebuild: `run_spec_test filter=["<spec-a>/", …]`.

- **Read the failure of EVERY case** and record the exact symptom (exit code, stderr, diff): the
  acceptance and the diagnosis's best input (`failures`; `cases: true` gives every verdict).
- **A case already GREEN is not in the set**: learn whether it misses the change or the change is done,
  before writing a line.
- ⚠ **A feature-named filter cannot see specs not named for the feature.** A change that refuses
  programs or changes a construct has every committed program as its blast radius (scaffolding that used
  it incidentally too), so it greps `specs/` and `ir-specs/` for the construct itself; any derived
  list driving an edit has its scope as a premise.
- ⚠ **One run for the whole set**, one pattern per spec file (`situations.md`, "Filter patterns"). A
  case that can race is a rate `n of m` (`situations.md`).

> ### ⛔ COUNT WHAT RAN — a spec can pass by running nothing
> ```bash
> grep -c '<!-- test:' specs/<file>.md   # summed over the set's files, must EQUAL the runner's `total`
> grep -o '<!-- test: [^ ]*' specs/<file>.md | sed 's/.*test: //' | sort | uniq -d   # must print nothing
> ```
> A shortfall — after subtracting cases an `<!-- unsupported-targets: -->` marker excludes on your lane,
> and adding other files a pattern selects — is a defect, never a pass. The harness refuses a marker
> outside `## Tests` and a program or directive no marker opened, so it is a harness defect to fix, not a
> spec to adjust. A duplicate name reports two cases as one.
> ⛔ **A case you cannot make pass is a HALT**: never removed, narrowed or excluded.

## 2. Write the code — `implementer` agents

Every implementer — first, further, every later round (a review fix, a gating defect) — is
`subagent_type: "implementer"` (its definition sets effort and default model; `model` per piece by the
table), briefed with the red set, `brief.md`, and above all **the diagnosis you made reading the red**.

**ALL the code lands in the main checkout before anything is committed**: several agents only on
**disjoint** files, sequentially when they overlap, integrated as work arrives. **Split a large change**
into small dispatches, one mechanism and a few builds each, each leaving a tree that builds (an
implementer degrades as its context fills); defer an enforcement that breaks the compiler's sources to
the piece that migrates them; hand each piece the previous report's path. ⚠ **§5's reviewer is never
whoever wrote the code.**

## 3. Green the set — the agent's loop, your confirmation

The agent iterates **edit → build → filtered run**, rebuilding after every compiled-in edit and at no
other time. **Then YOU read its last filtered run's log** (binary current when it ran): the only thing
between its report and §8. Re-run the filter yourself only when that evidence is missing or stale.

- **Nobody runs the full suite** — not this loop, not §8, not you, not an agent; every brief says so.
- ⛔ **Never turn a case green by narrowing what it tests**: spec text, filter or marker.
- **A defect ANYONE finds is FIXED**, a wrong answer as much as a leak, green suite or not: RELATED (§10),
  now, its reproduction a case in the set; unrelated, filed and drained after the push. A hand-run check
  is never evidence, and none is run to verify anything.
- **When the set is green, re-run §1's count check yourself** (an agent's "all green" is not a run you
  read), then stop editing for correctness.

## 4. Optimization — the agent on a trigger, the ladder always yours

- **(a) Scan the diff for unscalable structure — always** (`brief.md`'s list): the optimizer does it
  when (c) fires; else a scan agent, or for a located fix the reviewer. The compiler stays LINEAR in
  program size.
- **(b) The ladder, `run_scale_test` (~17 s)**, when the change touched a pass, the IR, or a collection
  the compiler indexes by: an INSTRUMENT with no verdict, read as `compiler-workflow` says (×2 linear, ×4
  quadratic, off the ALLOCATION columns). A Δ0 from a corpus that cannot express your feature is a blind
  spot.
- **(c) The `optimize` skill, in an agent that wrote none of the code, on a trigger:** the change adds a
  pass, an IR op or an indexed collection, or (b)'s allocation ladder bends ≳2.4 per doubling
  unexplained. **No trigger: the report says so** (a hunt over no new algorithm finds nothing).

**If you ran the ladder, settle the row's note now**: attribution exists only now. §9 carries it as the
commit's `Scale-Note:` trailer, and `land.yml` mints the row on the merged tree with the compiler it
built: you mint nothing, and a commit that adds a row by hand is refused. The note holds the A/B figure
`compiler-workflow` describes. ⚠ **Write no note you did not measure.**

## 5. Review

**A `code-review` agent on the uncommitted diff, after §4, before the commit** (model by the table),
**never one that wrote the code**. The skill carries the criteria, `brief.md` the situation.

- **Its leftovers for triage are yours**, by §10's test; never a `todo.md` bullet (nothing drains it).
- **The bounds questions are yours**, off `git status --short` and `git diff --stat`: anything outside
  this change? The diff as small as the fix warranted? **A spec expectation weakened?**
- **Findings are FIXED BEFORE the commit**, then filtered runs whose inputs the fix changed are re-taken.
  ⚠ A report is a lead, not a verdict (measurement has refuted many): confirm before acting; say so if
  you disagree.

**A soundness checker implementing an approved design gets a second reviewer** (`brief.md`, §5).

⛔ **§6 runs only on REVIEWED code.** Code written after the review (its fixes, a later round, a gating
defect) is reviewed again on that delta by its own fresh agent before §6, looping until a round has
nothing to fix. **The documenter is not a reviewer**: it reports defects it notices and fixes none; code
defects in its report mean §5 missed them — fix, review that delta, re-run §6 on the files they touched.

## 6. Document — the `documenter` skill

**A `documenter` agent writes every comment the change gets**, updates the `docs/` source its surface
owns, and regenerates the site pages. ⭐ **It is the LAST step that edits the tree**, after review, so
comments are written once, on code that stopped moving.

⚠ **Read its report, do not re-derive it.** It carries no code defects (§5); its comment-line count
(added, rewritten, deleted, net) is zero or close — more than a few, or a comment over two lines, failed
the skill. **§8 builds**: §6 edits compiled-in sources without building.

## 7. The door needs no rebase — yours

**You do not rebase for the door**: it merges your commit at main's tip (§9), and §8 gates the tree you
wrote.

## 8. THE LANDING GATES — once, on your tree — yours

⭐ **The MINIMUM targeted run.** A row whose condition the diff does not meet is not run; a filter is never
widened "to be safe": breadth is CI's, and a CI red costs one fix commit where a local battery costs
every change. ⛔ **No agent runs any part of this.** `$M` is `./maxon-bin/.maxon/maxon`.

| Gate | |
|---|---|
| **Build** | **Only if stale** (after §6 touched a compiled-in source, it is). Exit 0. Emitted runtime built with `MAXON_SECOND_STAGE=1` since the change: `maxon run build` again without it, before the spec rows |
| **Filtered `run_spec_test`**: §1's set plus every spec file the change touched, ONE run | **`failed: 0`**, no exit **101**; never a total |
| **Filtered tree corpora and IR specs**: `$M test <tests/dir> --filter=…`, `spec-test ir-specs --filter=<spec>/`, per touched corpus case or `ir-specs/` spec | `0 fail` / `failed: 0`. A moved pin is re-minted (`--update-required --filter=<spec>/`) and staged (`git add -A ir-specs/ specs/`): `situations.md`. One target's emitted code changed: also `--target=<lane>` (compiled, not run: any host) |
| **LINT**: `$M lint maxon-bin` · `$M lint --tests tests/<corpus>` · `bash scripts/lint-tree.sh <group>` | `lint maxon-bin` when the diff touches `maxon-bin/` (`--target=<lane>` for a lane's `#if` branch); `lint --tests` per touched corpus; `lint-tree.sh` per other touched group (`script-programs`, `multicore-stress`, `manifests`). After the build, with its compiler. Exit 0, one `Linted …` line per `lint`, no unclean subject |
| **SELF-COMPILE**: `$M build maxon-bin --output=temp/land/selfcompile --rebuild` | **Where the change adds a file declaring types, or adds or tightens a non-lint diagnostic**: its rules' first meeting with the compiler's source. Exit 0, last stdout line `Compiled -> <path>` (`--rebuild` forces the compile, ~1 min); output discarded |
| **Seed chain**: `build-from-seed.sh` on a throwaway clone (`compiler-workflow`) | **Where the tree's sources start using a declaration, intrinsic, form, rule or runtime behaviour the change made legal or required, or the change alters a rule the seed applies to the compiler's source.** Exit 0, or a refusal (any failure but E6002, a locked slot), which lands via `land.yml` (§9) |
| **Codecs**: `python3 scripts/library-cache/generate-codecs.py --check` | exit 0; on 1, run without `--check` and stage the rewrite |
| **Documentation**: `node website/scripts/sync-docs.mjs --check` | exit 0. A user-visible change: each doc-coverage gate its surface owns, `0 fail` — `maxon test tests/cli --filter=reference-documents`, `tests/mcp --filter=reference-documents` (also gates the MCP `instructions` against `docs/WRITING_MAXON_CODE.md`), `tests/docs`; else state nothing is user-visible. ⚠ Red after a new diagnostic: §6 missed its registry doc comment |
| **No leftovers**: `python3 scripts/check-ignored-files.py` | exit 0, every change: no untracked path, no ignored path off `scripts/allowed-ignored-paths.txt`. Exit 1: `situations.md` |
| **§1's count check**, final tree | markers == ran, no name twice |

**The filter stays narrow** (`situations.md`, "Choosing the §8 filter"). **CI alone runs** the full suite, wasm32-wasi,
full tree corpora, multicore stress compile-all, `check-stdlib`, every `lint-tree.sh` group, the full IR
suite on every lane, the WSL lane. ⭐ **`lint maxon-bin` is the one local E3092/E3219 gate**
(`situations.md`, "A lint finding").

**A red gate STOPS the change**, and a red you did not cause is fixed in its own commit before yours:
attribute it by the failing cases alone, then a control (`situations.md`). ⚠ **A lane or gate that did not
RUN is a SKIP you report**, never folded into the green. **A CI red after the push is fixed next**, in its
own commit.

## 9. Commit and land through the door

**ONE commit, on `main`**, holding the whole change: compiler source, spec cases, the documentation and
regenerated site pages, every re-minted `TargetIr` pin. ⛔ Never a commit
per piece or a partial landing "to follow": the gates licensed the whole tree. **The message carries what
the diff cannot**: the wrong answer that motivated it, the mechanism, the cases red → green, the gate
figures measured. ⚠ **Never a figure you did not measure**: it looks exactly like one taken. **When §4
read the ladder, the final paragraph carries `Scale-Note: <text>`** as a trailer, beside any
`Co-Authored-By:`; a `Land:` or `Scale-Note:` line outside the final paragraph fails the run.

- **Stage and commit as `situations.md`'s "Staging and committing" says**: explicit paths, stderr in
  view, the cached stat read.
- **Glance at the last COMPLETED CI run as its own step** (`gh run list --branch main --workflow ci
  --limit 3`), then land in a separate command. A completed red goes through §10's CI-red check (claimed
  by someone else: theirs; else recorded, its culprit asked); land anyway, and that red (it hides later failures) is first in your
  drain if it falls to you. A run in progress never blocks a land.
- **Then land, in the background** (`run_in_background`; about 4 minutes for a compiler change, and its
  exit wakes you). **From `main`: `scripts/land.sh submit --name=<name>`**, which pushes `land/<name>`,
  dispatches `land.yml` and waits for the run. **A session that pushes only its own branch** ends the
  commit's final paragraph with `Land: main`, pushes the branch, and runs `scripts/land.sh await <branch>
  <head>`. **No agent runs `git push origin main`.** The door merges the commit at main's tip and mints the §4 row.

⛔ **A commit the seed chain refused lands through the same door, on a bootstrap chain**
(`compiler-workflow`). ⭐ **Land it WITHOUT ASKING (user ruling)**: publishing the `seed-*` pre-release
and its pin is authorized like a push to `main`; never hand the user a seed to publish.
⛔ **A BOUNCE IS NOT A RETRY** (a file both sides changed, the seed refusing the merged tree, a refused
note, a misplaced trailer): the run names what to fix and deletes its branch when the branch still
points at the run's head; nobody deletes it by hand, and a name that still exists is a run going or a
run that kept it (read the run). Rebase by hand (`situations.md`), re-take each gate whose inputs
changed, and submit again under the same name.

> ⛔ **DO NOT WAIT FOR CI (user ruling).** The door's verdict ends the change: `land.sh` prints the sha
> main received, the door's commit and not yours. Then `git fetch origin main && git reset --keep
> origin/main`, delete `temp/land/` and every other `temp/` directory this change created, and do not
> watch, poll or schedule a check. CI hosts the four native targets on their own architecture, the full suite, the tree corpora,
> the two-stage seed build and wasm32-wasi (on its x64-linux lane), so a lane can go red after you report. A red lane reported
> to you goes through §10's CI-red check; one that falls to you is yours whether or not you caused it,
> in its own commit, reproduced on that platform's own host, never on the cross lane that missed it.

**Report in a few lines**: the change, the cases red → green, each gate's number, the sha main received,
and every gate or lane not run, with its reason (CI-only lanes included). **Then drain the known issues (§10)**: the task is not
over while one is claimable.

## 10. Known issues

**Every defect found gets fixed; those the change does not need wait** as issues on `maxon-lang/maxon`,
for whoever finishes first.

> ### ⭐ RELATED — fixed now, in this change
> - the acceptance set or a §8 gate **cannot go green** without it (a red you did not cause included, in
>   its own commit first);
> - it is in code **this change wrote or changed**: a defect, duplication or superlinearity the new code
>   takes part in;
> - or it is another instance of **the class this change closes**, even in an untouched file. State the
>   scope as a class and test each sibling: a reason for leaving B that applies equally to the A you fixed
>   is not a reason.
>
> **Everything else is UNRELATED**: a pre-existing defect in a touched file but not in what you changed, a
> reviewer's finding elsewhere, pre-existing duplication the new code does not join, a diagnostic missed in
> passing. "It is in a file I am editing anyway" does not make it related; "it blocks a case" does.

### Recording

**An unrelated defect is an issue labelled `bug` and `accepted`** plus its area (`compiler`, `codegen`,
`runtime`, `stdlib`, `tooling`, `performance`), so it is recorded and fixed once. Title: the one-line
symptom. Body: **Where** (`file:line`), **Seen** (how, by which change), **Repro** (a program that
reproduces it: the fixing land's red set).

**In the maintainer's fleet the orchestrator files, claims and assigns every known issue** (`AGENTS.md`,
"Orchestration"): recording is a `FOUND` to it, asking a CI red's culprit is its job, and draining is a
`DONE` after which it assigns the next. **Outside the fleet** the filing, the CI-red check and the
draining are yours: `known-issues.md`. Either way, relatedness and how an issue lands bind you.

⛔ **Before filing an issue and before landing one, ask whether it repeats a landed fix**: if so it is a
design question, a HALT (`situations.md`).

## ⛔ HALT AND ASK

Everything above runs unattended. Stop and report, without landing, only when:

- **A gate is red and the fix is not yours to make.**
- **A DESIGN RULING is needed**: specs and compiler disagree about what is correct, or `specs/`
  contradicts itself. Do not guess; **never edit a spec to match the compiler**: that makes a compiler bug the specification.
- **A case would have to be removed or excluded, or an existing `specs` expectation rewritten**: the
  first is what this process prevents; the second belongs to whoever owns that behaviour.
- **A finding or known issue repeats a landed fix** at another consumer of the same fact (§10).
- **A change would make the compiler accept a program an approved design refuses** (`situations.md`).

**In the fleet each is a `QUESTION` to the orchestrator** (`AGENTS.md`). **Each is answered in place,
never a reroute**: stop, report, wait — no worktree, no hand-off — then carry on here. ⚠ **Never a
stop:** "this is big"; "I could not find the gap" (unfinished diagnosis); "a contract would be cleaner";
"I will land this part now and the rest after". The only honest stop is a question the user must answer.

## The thing this process exists to catch

**A change that is green because nothing tested it.** The gates run once, at the end, on filters, and
every spec run before them is a proxy, so the loop rests on §1's red being real: **watch every case fail
before the fix, count what ran, never make a case go away.** That red set, the integration and the gates
are judgement, the first thing lost when a coordinator spends its context on greps and IR dumps. **Send
the agent. Keep the judgement.**
