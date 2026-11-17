# `/land` situations

Read the section for a situation when it arises. Each binds as `/land` itself does.

## Deciding to build

- A `stdlib/` or `runtime/` edit needs no build for the SUITE, which compiles both from source — but the
  compiler's own driver commands (`fmt`, `test`, the LSP) carry the stdlib they were built with, so a
  change the tree corpora exercise does need one.
- **Emitted runtime** — a change to `Compiler/Runtime/` or `Compiler/Targets/*/*Runtime*.maxon` — takes
  TWO self-compiles before the compiler itself, which is also the spec harness, runs the new runtime.
  `maxon run build` does the second build itself, and prints why; `MAXON_SECOND_STAGE=1` on that
  `run build` stops it at one. That second build is not a repeat.

## Running the watchdog

```
node .claude/skills/land/watchdog.mjs --agent <name>=<agent id or transcript path> \
  [--artifacts <name>=<path>,<path>…] [--pid <name>=<pid>] [--unbounded <name>]
```

One `--agent` per running agent (an agent id is found under `~/.claude/projects/*/*/subagents/`).
`--artifacts` names what its work moves — its build directories, its logs, its exclusive files — and
`--pid` a process it owns; both are optional and repeatable. `--unbounded` names each agent that is not an
implementer or diagnosis agent, which the handover bound does not bind. A growing transcript alone can be
a loop, so the trips read work as well as talk. A trip prints the agent and the trip, one line each:

- **`stalled`**: the transcript has not grown for 20 minutes;
- **`no-progress`**: the artifacts have not changed for 60 minutes while the transcript grew;
- **`cpu-flat`**: a process's CPU time has not moved for 20 minutes;
- **`limit`**: the agent passed the handover bound (`brief.md` item 5), read from the transcript's
  `message.usage`: context is the latest call's input, cache-read and cache-creation tokens; calls are its
  distinct assistant messages.

An agent whose transcript records it finished is no longer watched; with none left the watchdog exits
saying so. **On `limit`**, SendMessage the agent to write its handover file and stop. **On any other
trip**, SendMessage it asking what it is doing and what it expects next; a stuck or dead one is reported
to the user. `--help` lists the thresholds' overrides.

## A located fix (§1)

**A fix is LOCATED when all three hold before any dispatch:**

1. its issue carries a `Repro` program and a `Where` (`file:line`);
2. one grep names the spec file that owns the behaviour, and the repro needs at most two cases;
3. those cases, written by you into that file (invoke `maxon-coder` first), go red on their first run for
   the issue's stated symptom.

If any fails, the scout runs, and the cases you wrote are its author's to keep or replace. A located fix
dispatches no scout and no spec author; **if the implementer's measurement contradicts `Where`, the scout
is dispatched then.** Nothing else is skipped: red first, the count check, the independent review, the
documenter as its own fresh agent, a re-review of any fix delta as its own fresh agent, and every gate.
When no §4(c) trigger fired, the §4(a) scan is folded into the reviewer's brief instead of a scan agent.

## A measurement over fixtures

It is re-taken only over the fixtures a fix round could have moved. Before its first full run, record for
each fixture its result and **its key**: the compiler phase, function or diagnostic that produced that
fixture's number, which a fix can act through. After a fix round, re-run the fixtures whose key names code
the round's diff touched, plus **a fixed control sample of ten fixtures, the same ten every round**. If
any control moved, the key was wrong: re-take every fixture once and re-key. The full set runs once more,
on the final tree, as the figure the commit quotes. No key recorded means every fixture is re-run.

## A user-visible change (§1)

A change that adds a command, option, MCP tool or `public` stdlib API names its documentation gate — but
not as a red-first case. The doc-coverage gate that owns the surface (`maxon test tests/cli
--filter=reference-documents`, `tests/mcp --filter=reference-documents` (it covers the MCP
`instructions` too), `tests/docs`) reads the built compiler or the tree's `stdlib/`, so it is green today
and turns red only once the new surface exists. It is not in the set: it is acceptance, read in §8 after
§6 has written the documentation. Every other user-visible change still owes its `docs/` source an edit
(§6), which the gates cannot see.

## Choosing the §8 filter

**The filter stays narrow:** the spec files whose programs reach the changed code, the files holding the
change's own cases, and the pins it edited; `ir-specs` only for pins that moved; wasm only when wasm
lowering changed. It also covers every spec family that pins a size or a trace of the objects the change
resizes (`mm-trace/`, `builtins-mm-counters/`), and every family named after a KIND of value whose
classification changed (grep `specs/` for the kind's name, as `union-scalar-backing/`). "Every family
that mentions X" is CI's job. Briefs cap the work the same way: reuse an existing control binary, batch
edits into one build, one combined filtered run at the end.

## A deletion

**A deletion has no red.** Removing a mechanism nothing uses leaves no case that can fail, and a
manufactured one is worse than saying so. Its acceptance is that nothing was live: code bytes unchanged
at every `run_scale_test` rung, and `spec-test ir-specs` moves no pin. A drop in allocations is expected,
since the mechanism's own cost goes with it. Before deleting, grep for what still reads the thing, and ask
which guarantee goes with it: a lookup that threw on a missing entry can become a silent zero and a panic
three files away.

## A pure optimization

It changes no runtime answer, so no behaviour case can go red. Its evidence is an `ir-specs/` pin: mint
it showing the unoptimized code, read that as the recorded symptom, and let the diff of the re-mint carry
the change. ⚠ **An optimization can delete a gate's subject**: a case whose program folds away stops
gating what it gated. Move the values across a loop-header phi, which the fold reads as unknown, so the
program survives.

## Filter patterns

Select the whole set in ONE run. Each pattern is a case-sensitive substring of the `<spec>/<case>` label,
and repeated `--filter=` flags (MCP: a `filter` array) are a union: one pattern per spec file, never one
run per file, which pays the startup per file and runs each on one worker. A comma is part of a pattern,
not a separator. A pattern that selects nothing refuses the whole run and is named, so a typo'd member
cannot pass unseen.

## A case that can race

It is reported as a failure rate `n of m`, never "passes" or "fails". A 4% race comes back clean 88% of
the time at m=3, so use m ≥ 20 per cell before comparing cells. A case that fails 1 in 3 is not a red-set
gate. A racy red is finished only when it is deterministic: force the losing interleaving structurally
(put the readied work where the other contender cannot take it), never by widening a timeout.

## A red gate (§8)

**It STOPS the change**, and a red you did not cause still gets fixed, in its own commit, before yours
(`AGENTS.md`). To attribute it: do the failures touch what you changed? Re-run **the failing cases alone,
by filter** — one run, one pattern per case. If that does not settle it, **measure the control** on that
filter: `git stash -u`, re-run, `git stash pop`.

- Compare failing SETS between runs, never counts. A timeout under load lands on whatever is in flight, so
  it clusters plausibly around the change under test, and the same commit can read 11 then 0.
- Empty stdout does not prove a startup hang: a killed process loses block-buffered output.
- ⚠ `git stash pop` takes the top of the list whether or not your push stored anything: a push that
  failed or found nothing to save leaves an older stash on top, and its untracked files land in the tree
  even when the pop aborts. Read `git stash list` before popping. To test an old version of one file, copy
  it aside.

## An IR pin moved

`ir-specs/` holds the corpus's emitted-code cases, and each pins the Target IR its program compiles to on
every native lane (` ```TargetIr:<lane> `). A pin the compiler no longer renders FAILS its case, showing
the first differing line. A change that moves emitted code re-mints the pins it moves with
`--update-required --filter=<spec>/` — a pin is COMPILED, not run, so any lane re-mints on any host,
`--target=arm64-macos` and `--target=arm64-linux` included — and READS the diff, which is the review of
the emitted code. `specs/` pins no emitted code.

A change to spill priority renumbers slots across many pins. Normalise only the slot number and compare
the removed and added line multisets: identical multisets are that churn, and anything left over is a
real move.

## A lint finding (§8)

`lint maxon-bin` is the gate the suite cannot substitute for. The build, the self-compile and
`maxon test` compile past the lint rules — E3064 and E3065 (a dropped result), E3092 (an `export` no
other directory uses: adding or removing an `export`, or deleting its last cross-directory caller, which
de-duplicating usually is, orphans one) and E3219 (an unused unexported declaration) — so without §8's
lint row CI's "Every first-party program lints clean" step is the first to see them. `checkUnusedExports`
(E3092) and `checkUnusedUnexported` (E3219) judge a whole program, and the only program that names every
`export` in `maxon-bin/Compiler/` is the compiler itself: a fully green suite says nothing about them,
and `maxon build` runs neither audit, so the self-compile cannot see one. CI's lint step failing is a red
`main`. An E3092 is a declaration more visible than its uses: narrow it, or land it *with* its first
consumer.

## `check-ignored-files.py` exits 1 (§8)

Delete what your change's runs left (`git add` the change's own new files first: an untracked path fails
too). A path a tool legitimately leaves is not deleted: add its pattern to
`scripts/allowed-ignored-paths.txt` in the same commit. `maxon-bin/.maxon/` is a `closed-directory:` of
the allow-list: only the names listed under it pass (the slot's `maxon[.exe]`, `maxon.previous[.exe]`,
sidecars, compiler-minted `maxon.retired.<block>` images, `.staging-<hex>/`), so a hand-renamed compiler
image there fails: delete it and keep a compiler you need in `temp/<task>/`. CI runs the same script as
its "No file left behind" step, after the test steps of the corpora, tools, suite and cross-suite jobs on
every lane.

## A finding that repeats a landed fix (§10)

**An issue that repeats a landed fix is a design question, not another issue.** Before filing an issue,
and again before landing one, ask: **has a fix of this same kind already landed, or does another issue
name the same question at a different site?** Examples: "the async path does not consult X", after "the
spawn path did not consult X"; or a second consumer that disagrees with a third about one fact. If so, the
fixes are treating symptoms: several consumers answer one question each for themselves (`code-review` §3,
"the same DECISION made in two places"). **HALT and take it to the user**, naming every consumer with
`file:line` and the landed fixes by sha. The remedy is one source every consumer asks, through
`/plan-and-land`. ⛔ Never land the next per-site fix while that question is open.

## Reading a diagnosis

**A diagnosis reading counts only when every row ran, compiled the program you think, and differs from
its control for the reason you think.** A control that fails the same way as the experiment is a broken
harness and proves nothing in either direction; a control that never ran proves less. `rc=127` or a
suspiciously uniform column means the row is fabricated. Two binaries agreeing perfectly is suspect until
both are shown current.

## Naming a tree

Name each tree you take a result on, so the inputs rule can list what changed since:
`GIT_INDEX_FILE=<scratchpad>/land.idx git add -A`, then `GIT_INDEX_FILE=<scratchpad>/land.idx git
write-tree`, records the working tree, untracked files included, without touching the shared index.

## Staging and committing (§9)

- **The index is one file shared by every session in this checkout.** Commit with explicit paths
  (`git commit -F <msg> -- <paths>`), which commits exactly those paths whatever else is staged. A re-mint
  reads the specs on disk, so a peer's uncommitted spec edit lands in the pin.
- **Stage and commit with git's stderr in view — never suppress it.** `git add` with any pathspec that
  matches nothing stages NOTHING, so stage only paths `git status --porcelain` lists, and read
  `git diff --cached --stat` before committing: a commit whose message describes a code change must show
  that code file.
- **The CI glance and the submit are separate commands**: chained in one, the submit goes out before
  anyone reads the answer.

## A change against an approved design

A change that would make the compiler accept a program an approved design refuses — a checker rule
relaxed, a pin dropped, a refusal narrowed, against a ruling or sample of the plan the change implements
— is an amendment of a model-checked design, never an implementation choice. The agent stops and
reports; the coordinator puts the amended rule to the user, and the amended rule passes the design's
executable model (campaigns and a fresh red team, 0 counterexamples) before it lands. Every brief for
such a change quotes the rulings it implements and says this.

## A bounce (§9)

**It is not a retry.** The door refused the commit and its run printed why (`land.sh submit` and `await`
print it and exit 1). A bounce is one of:

- **a file both sides changed since your base**: named by the run. Rebase onto `origin/main` by hand
  (below) and read each named file's merged result;
- **the pinned seed refuses the merged tree**: the run names the error; fix the tree, or land it as a
  bootstrap chain (`compiler-workflow`);
- **a refused note, a hand-made row or a misplaced trailer**: carry the note as a `Scale-Note:` trailer in the commit's
  final paragraph, drop any edit to the scale tables.

**The hand rebase:**

- **Resolve each conflict BY HAND, hunk by hunk.** ⛔ Never `git checkout --ours/--theirs` a doc file: a
  whole-file resolution in a doc has silently reverted somebody else's landed section, and it looks clean
  in `git diff`.
- Resolve as the UNION of both sides, then run the OTHER side's cases as well as your own: "take my side"
  on a struct literal can silently revert the other change's fix while your acceptance stays green. When
  both sides correct one rotting fact (a count, a caller list), derive it from the merged file.
- **A clean rebase is not a coherent one.** When both sides changed a file, read the merged result for
  contradictions: git can join a new upstream rule to a new case that breaks it with no conflict marker.
- **A stash never outlives the rebase.** If the pop conflicts, resolve it now: work sitting in
  `git stash list` looks exactly like work that vanished.

List the changed paths (`git diff --name-only <tree then> <tree now>`), re-take each §8 gate whose inputs
changed (the inputs rule), amend or rewrite the commit, and submit again under the same name. The run
deletes its branch when the branch still points at the run's head, so the push needs no delete. A push
refused because the name still exists means a run of it is going or a run kept it: read the run.
