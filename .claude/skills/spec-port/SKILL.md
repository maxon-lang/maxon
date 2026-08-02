---
name: spec-port
description: Port ONE spec file from /specs into specs-shv2 and make it pass — the rapid-iteration loop. Takes the next spec off the v1 whitelist order (maxon-selfhosted/Testing/SpecTestRunner.maxon), copies it byte-identical, runs it, implements the gaps, and lands it, with an independent review as the last step before the commit. The minted goldens are committed, not audited. One spec per invocation, designed for `/loop /spec-port`. Invoke as `/spec-port <name>` to take a specific spec instead of the next one.
---

# Port one spec, make it pass, land it

**One invocation = ONE spec, ported and landed.** This is the cheap sibling of `/rung`: no worktree, no
wave, no slice board. You work directly in the main checkout, you are the only agent on `main`, and the
whole tick should be minutes, not hours.

**`/rung` is for a rung of `maxon-shv2/PLAN.md` — a named feature with a contract.** This skill is for
the long tail: the specs `/specs` already has and `specs-shv2` does not, most of which are a small
front-end gap or nothing at all. **When a spec turns out to need a whole feature, you do NOT start a
rung here** — you land what is reachable, shelve the rest with named reasons, and say so (§7).

## The ordering is v1's whitelist, and it is the only backlog

`maxon-selfhosted/Testing/SpecTestRunner.maxon` opens with `let whitelist = [...]` — ~265 spec names,
**deliberately ordered easiest-first**, tiered by comment (`Tier 1: leaf language features` … `Tier 4:
async / IO / network`). That order was earned by the v1 loop walking it, and it is reused verbatim here.
**Do not re-order it, do not curate it, and do not skip a name because it looks boring.** The next spec
is a mechanical fact, not a choice:

```bash
cd C:/Users/Eric/dev/maxon
sed -n '5,425p' maxon-selfhosted/Testing/SpecTestRunner.maxon \
  | sed -n 's/^[[:space:]]*"\([a-z0-9-]*\)".*/\1/p' > temp/wl.txt
while read s; do
  [ -f "specs-shv2/$s.md" ] && continue          # already ported
  grep -q "^| $s " docs/spec-port-log.md 2>/dev/null && continue   # DEFERRED on a past tick
  [ -f "specs/$s.md" ] || { echo "SKIP(no source): $s"; continue; }
  echo "NEXT: $s"; break
done < temp/wl.txt
```

Three exclusions, and each is load-bearing:
- **already in `specs-shv2/`** — done, on some earlier tick.
- **a `DEFERRED` row in `docs/spec-port-log.md`** — a past tick measured this one as needing a rung
  (§7). Without this the loop stalls on it forever, retrying the same halt every tick.
- **no `specs/<name>.md`** — the whitelist names a few specs that only ever existed in v1's head. Report
  the skip and move to the next; do not invent the file.

`/spec-port <name>` overrides all three and takes that spec.

## 0. Start — no baseline run. There is nothing to measure yet.

**Do NOT open the tick with a full suite run.** It is ~43 s spent on a number the tick does not need:
**the §8 gate is `failed: 0`, not an arithmetic total** (see the box there). A suite that ends green is
green whatever it started at, and the check that catches this loop's real failure mode — a spec landing
green having tested NOTHING — is §2's, which compares this spec's markers against this spec's own filter
total and needs no history at all.

```
build target=shv2 repoRoot=C:/Users/Eric/dev/maxon      # bootstrap first if it is stale
```

**Build anyway** — that is not a baseline, it is making the binary current. Everything downstream (§2's
filtered run, §3's diagnosis, the goldens §5 mints) is read off that binary, and a stale one has already
cost this repo a ladder read off the wrong compiler and an oracle that made two compilers look
byte-identical on every probe. It costs ~19 s and it is not optional.

⚠ **`git status` must be clean before you copy anything.** The suite MINTS goldens as a side effect
(§5); if the tree is already dirty you cannot tell yours from the leftovers.

### The one thing a baseline bought, and where you now pay for it

It told you, the instant §8 came back red, whether the red was yours. Without it that question moves to
§8 — the right trade, because **the suite is green on almost every tick, and paying 43 s every time to
insure against the rare red is the expensive way round.**

⇒ **When §8 is RED you may no longer assume the red is yours.** Attribute it: do the failures touch what
you changed? If that is not obvious in a minute, **measure the control THEN** — `git stash -u`, rerun,
`git stash pop`. One run in the rare case instead of one run every tick. A red you did not cause still
gets fixed first, in its own commit, before the port (precedent: `d45cb0007`, a stale golden + eight
unminted ones left by another rung's arm64-only sweep).

⛔ **Do not reach for `docs/spec-port-log.md` to reconstruct a baseline.** It is INFORMATIONAL — a trend
someone reads, exactly like `docs/optimization-log.md`. Nothing in this loop may depend on it, and a tick
that needs a number it can only get from a log has invented a dependency the log never agreed to carry.
If you genuinely need a before-number, MEASURE it (the control above); do not read one.

## 1. Copy it BYTE-IDENTICAL

```bash
cp specs/<name>.md specs-shv2/<name>.md
```

**No edits. Not the frontmatter, not the prose, not one case.** The copy is the *claim* — "shv2 should
do what this says" — and every subsequent edit you make to it is a *retraction* you now have to justify
in the commit message. Starting from a pre-trimmed file makes the retractions invisible, which is the
whole failure mode this step exists to prevent. `git diff` against `/specs` is the review of your
retractions; keep it readable.

### ⭐ THE FILE YOU JUST COPIED IS KNOWN TO PASS. **A REAL COMPILER PASSED IT, UNEDITED.**

**Every spec on the whitelist was RUN and PASSED by `maxon-selfhosted` (v1) — against `/specs` itself,
byte-for-byte, with no shv2-style retractions.** v1's runner walks up from cwd to find the `specs/`
directory (`SpecTestRunner.maxon`, `findSpecsRoot`) and reads those exact files; the whitelist *is* the
set it passed, and everything off it is explicitly marked `DEFERRED SPECS`. That is what earned the
easiest-first ordering this loop walks.

⇒ **So never wonder whether a case is right, and never edit one because you suspect the spec is wrong.**
Its program, its `exitcode`, its `stdout` and its `maxoncstderr` blocks are all a configuration a real
Maxon compiler has actually satisfied. **The only open question is whether shv2 LEGITIMATELY DIFFERS** —
and that is a much narrower question than "does this spec work".

**A retraction is legitimate only when you can point at the thing that ratifies it**, in one of these
three forms. Nothing else counts:

| legitimate | the evidence you must be able to point at |
|---|---|
| shv2 has its own **registered** code for this rule | a `shv2` line in `docs/error-codes.txt` — the ONE registry shared by all three compilers. E.g. **E2053** `callArgMissingLabel` is claimed for **shv2 alone**, where the bootstrap reports the same rule through its general E3005. |
| shv2 **structurally cannot** emit the expected code | the registry claims it for other compilers only. E.g. **E4006** `IrInvalidFieldAccess` is an IR-stage code claimed by `csharp`/`selfhosted`; shv2 refuses the same program one stage earlier under E3004. |
| an **already-ported** `specs-shv2` file pins the other spelling | name the file. ⚠ And note this cuts the other way too: if making your spec pass would force a change to that ported file, **you are in HALT AND ASK**, not in a retraction. |

⛔ **What is NEVER a reason: "shv2 prints something else, so I wrote down what shv2 prints."** That is
editing the claim to match the implementation, which is §3b's whole prohibition — a compiler bug
becoming a specification. If you cannot fill in the right-hand column, the spec is right and shv2 is
wrong: **fix shv2.**

⚠ **You cannot re-run v1 to re-check any of this** — it no longer builds (`.claude/CLAUDE.md` records
why, and that is accepted). The passes are historical and were real when the whitelist was earned; v1
remains a SOURCE to read, not a runnable oracle. **The runnable oracle is the C# bootstrap** — use it for
*what the right answer is*, exactly as §3 says.

The runner discovers specs by listing `specs-shv2/*.md` (`specFilePaths`) — **copying the file is the
whole registration.** There is no list to add it to.

## 2. Run it, and COUNT WHAT RAN

```
run_spec_test compiler=shv2 repoRoot=C:/Users/Eric/dev/maxon filter=<name>/
```

> ### ⛔ THE COUNT IS THE GATE, NOT THE COLOUR. A spec can pass by running NOTHING.
>
> shv2's spec parser drops cases silently, in two live ways, and both read as a clean green:
> - **`status: draft` in the frontmatter returns ZERO tests for the whole file** (`selectedTests`).
>   `/specs` has no draft files today; the day it gets one, this is how it disappears.
> - **A `## ` heading ENDS the active-test region** (`SpecParser.extractTests`: *"A new `## ` section
>   (e.g. `## Deferred`) ends the active-test region"*). A `/specs` file that puts `## Notes` or
>   `## See also` after `## Tests` silently shelves every case below it. **This has already cost this
>   project a green that tested nothing.**
>
> **So every tick, mechanically — TWO checks, and neither substitutes for the other:**
> ```bash
> grep -c '<!-- test:'          specs-shv2/<name>.md   # ACTIVE cases. This is the number.
> grep -c '<!-- disabled-test:' specs-shv2/<name>.md   # shelved — informational, do NOT subtract
> grep -o '<!-- \(disabled-\)\?test: [^ ]*' specs-shv2/<name>.md \
>   | sed 's/.*test: //' | sort | uniq -d                # must print NOTHING
> ```
> 1. **`grep -c '<!-- test:'` MUST equal the runner's `total` for that filter.** A shortfall is a defect
>    in the port, never a pass. Fix it by moving the `## ` heading below the cases (and say so in the
>    commit), not by accepting the smaller number.
> 2. **No name may appear as BOTH `test:` and `disabled-test:`** — that is a shelve that did not take.
>
> ⚠ **DO NOT SUBTRACT.** `<!-- disabled-test:` does **not** match the `<!-- test:` grep (the `<!-- `
> prefix breaks it — verified: `echo '<!-- disabled-test: x -->' | grep -c '<!-- test:'` is `0`), so the
> first count **already excludes** the shelved cases. Subtracting under-counts by exactly the number you
> shelved, which makes a CORRECT port look like a defect on every tick that shelves anything. *(This
> skill said "markers − disabled" until 2026-08-01, when tick 5 shelved one case of fourteen and the
> formula demanded 12 against a correct 13.)*
>
> ⚠ **Shelving REPLACES the marker, it does not accompany it.** Write `<!-- disabled-test: <name> -->`
> **in place of** `<!-- test: <name> -->` — leave the `test:` line underneath and the case still runs,
> silently, while the file reads as though it were shelved. Check 2 is what catches that; it is how the
> same tick found its own botched shelve.

Then read the failures. The MCP result is structured; when you want per-case detail use
`spec_test_outcome filter=<name>/`. If you run the binary by hand instead, **redirect to a file** — never
pipe through `head`/`grep` (CLAUDE.md).

## 3. Implement the gaps — DELEGATE to `maxon-spec-implementer`

**Hand the fix to the `maxon-spec-implementer` agent. Do not do it inline.** Not because you could not,
but because **the loop's context is the scarce resource**: an implementation is greps, dead ends, IR
dumps and refuted hypotheses, and none of that is anything the *next tick* needs. The agent absorbs it
and returns a report. You stay small enough to run tick after tick.

**Brief it with what you already know** — the spec name, the failing case names, the exact symptoms
(exit codes, stderr, diffs), and any diagnosis you did while reading the failures. **Diagnosis you have
already done is the most valuable thing in the brief**; making the agent re-derive it is the one waste
this delegation is supposed to prevent.

Its brief (`.claude/agents/maxon-spec-implementer.md`) fixes the reference ORDER: **`maxon-selfhosted`
(v1) first** — the closest code, in the same language against the same `stdlib/`, and the compiler that
**passed this exact spec unedited** (§1) — then **`maxon-sharp` (the bootstrap)** as the RUNNABLE oracle
when the question is *what is the right answer* rather than *how is it built*. **Neither binds it.**
shv2 is a deliberate rewrite; where it departs, the departure is the thesis, and an implementation that
does not fit gets designed rather than copied.

**Inline is the exception, and a narrow one:** a one-line fix you have already located and can prove
with a single filtered run. The moment it needs a second hypothesis, hand it over.

⛔ **A defect this spec reaches is FIXED, not filed** — a wrong answer as much as a leak, and whether or
not the suite is green over it. That is the CLAUDE.md rule and neither you nor the agent softens it.
What may be shelved is a case needing a *feature that does not exist yet* (§4) — never a case that is
broken.

**The agent runs ONLY its own spec's filter — the full suite is YOURS** (§8), and one tick runs it
exactly once. **You** do not re-derive its diagnosis or re-run what it ran; the suite is your check.
**Auditing its diff is the REVIEWER's job (§6), not yours** — that is the division, and it is why you
can stay cheap without the diff going unread.

### ⛔ NEVER ASK A SUBAGENT TO HAND-APPROXIMATE A GATE THE LOOP ALREADY RUNS

**Brief it for DIAGNOSIS, never for COVERAGE.** What only you have is the symptom, the position, the
call sites you already read, the hypothesis you already refuted — hand over all of it. What you must
NOT hand over is a list of things to go and prove, because **the loop's own gates already prove them,
better, one step later**:

| you might be tempted to ask for | the gate that already does it |
|---|---|
| "enumerate every construct that could false-reject and prove each parses" | the **full unfiltered suite** (§8) — thousands of cases across closures, interfaces, generics, operators |
| "confirm no other spec regressed" | the same run, which is the whole point of it being unfiltered |
| "check you did not shelve anything" | the **§2 count check** |
| "confirm you stayed in bounds / the diff is minimal" | **you**, reading `git diff` and `git status` — seconds of work |

⚠ **A specific instruction in your brief OUTRANKS the agent's own standing rules.** Its definition
already says *"YOUR SCOPE IS THE FAILING CASES. STOP WHEN THEY ARE GREEN"* — and a brief that ends
*"…and prove these twelve constructs still parse"* silently repeals it, because a concrete task always
beats a general rule. **You cannot brief thoroughness into an agent without briefing the stop rule out
of it.**

*(Precedent, 2026-08-01, tick 4: the brief for a one-token parser check asked the implementer to
enumerate ~12 header shapes and prove each. It ran ~20 compiled probes and a five-tree corpus scan
after its filter was already 6/6 — all of it re-deriving what the 3272-case suite established in 45
seconds immediately afterwards. The same brief-written-too-thoroughly then happened AGAIN to the
reviewer in the same tick. Both were the loop's error, not the agents'.)*

**A few targeted probes at the exact position changed are fine and worth asking for.** A sweep is not.
The rule of thumb: if the full suite would catch it, do not ask anyone to catch it by hand.

## ⛔⛔ 3b. THERE IS NO SUCH THING AS AN ACCEPTED DIVERGENCE. That is what the spec files are FOR.

**Never file a "known divergence from `/specs`". Never write one into `PLAN.md`. Never record one in the
log.** A `/specs` file is not a description of what the compiler happens to do — it is **the definition
of the language**, and shv2 differing from it is a BUG, full stop. The moment you catch yourself writing
*"measured divergence, filed as accepted"*, you are converting a defect into documentation.

**Every difference you find is in exactly one of two states, and there is no third:**

1. **A spec that IS ported says otherwise** ⇒ it is a **live bug and you fix it now.** It is on your
   acceptance path by definition — the case is red.
2. **No ported spec covers it** ⇒ it is simply **NOT DONE YET**, and the spec file that will catch it is
   sitting on the whitelist waiting for its tick. **That is a queue position, not a decision, and it
   needs no note anywhere** — the whitelist already tracks it, which is the whole reason the whitelist
   is the backlog.

⚠ **State 2 is the one that gets mis-filed**, because a gap you can *describe* feels like a finding
worth recording. It is not. Writing it into `PLAN.md`'s "Future rungs" claims it needs its own numbered
rung with a contract, when all it needs is for the loop to reach its spec — and a note that says
"accepted" about something nobody accepted is worse than silence.

*(Precedent, 2026-08-01: tick 2 left an interpolated `panic("{n}")` rendering the hole as `{}` and filed
it as an accepted divergence. It was neither accepted nor a divergence — `/specs/panic.md` tests only
literals, so **all 4 ported cases passed and nothing diverged**; the behaviour is pinned by
`/specs/panic-interpolation.md`, a SEPARATE unported spec, 6 cases, whitelist position 229. The entry
was deleted and that spec ported instead.)*

⇒ **So when you find one: write NOTHING, and take the next spec in whitelist order.**

⛔ **And do NOT jump the queue to go close it.** The gap's spec has a position, and that position is the
answer — the whitelist is ordered easiest-first for a reason, and a spec 200 places down is 200 places
down because everything above it is cheaper and more foundational. *(Precedent, same day: on finding the
`{}` gap the loop queued `panic-interpolation` — **whitelist position 229** — as the very next tick,
while `function-declaration` at position 3 sat unported. Cancelled. **Too soon is a real failure mode:
it spends a tick's budget on a hard spec while the cheap ones that would have exposed simpler bugs go
unrun.**)* The one exception remains an explicit `/spec-port <name>` from the user.

## 4. Shelving a case — the only legal way, and it has a floor

A case that needs a feature shv2 has not built yet becomes:

```markdown
<!-- disabled-test: <name> -->
<!-- needs <THE MECHANISM>, which is <WHERE IT LIVES>: <why it cannot ride this port>. -->
```

**You may not shelve a case whose gap you have not NAMED and LOCATED.** *"Fails"* is not a reason.
*"Needs Map, which is a follow-on rung — `MapIterator.current()` returns a genuine tuple, so Map is
sequenced after tuples"* is. If you cannot write that sentence, you have not finished diagnosing, and
the honest move is to keep debugging.

> **⛔ THE FLOOR: if more than half the file's cases end up disabled, STOP.** Do not commit a mostly-shelved
> spec. That file is not a port, it is a rung wearing a port's clothes — take the §7 exit.

## 5. COMMIT the goldens the run minted — you do NOT have to read them

**A passing case is good enough** (user ruling, 2026-08-01). The spec's own ```exitcode / ```stdout /
```stderr blocks are the CORRECTNESS gate and they are checked on every run; the `.test` fragment is a
CODEGEN-QUALITY gate — it pins register allocation, spills, coalescing — and holding a rapid-iteration
loop open to hand-audit register choices is not what this loop is for. **Do not read the minted
fragments. If the spec passes, land them.**

What that trades away, so it is a decision and not an accident: a freshly ported spec is the one moment
the IR gate is off (`checkTestFragment` mints a missing golden and passes — *"a first run that failed
for want of a golden it cannot have would be a rite, not a gate"*), so whatever it records becomes the
baseline every later change is measured against. A suboptimal allocation ported in this way is pinned,
not caught. That is accepted: it costs code quality, never correctness, and the moment anyone *changes*
that codegen the fragment goes red and gets looked at then.

⛔ **What you MUST still do is `git add` them.** A minted golden left untracked is invisible to the
summary line and to `git status` noise alike — that is precisely how the baseline of this loop's first
tick was found at 3257/1 with eight unminted fragments and one stale one sitting in the tree.

```bash
git status --short specs-shv2/fragments/     # every one of these gets committed with the spec
```

## 6. REVIEW — the last step before you commit. Everything ELSE stays cut.

**Run `maxon-rung-reviewer` on the working diff, after the suite is green and BEFORE `git commit`.**
It is the one piece of thoroughness this loop keeps, and the order matters: a review that lands after
the commit is a bug report, while a review that lands before it is a gate.

**Still cut, and still deliberate** (the process is expedited on purpose): **no `run_scale_test` ladder
read, no other targets, no cross-target gate, no re-auditing the implementer's diagnosis yourself, no
reading the minted goldens.** `/rung`, `/code-review` and the multi-agent cloud review are the deep
passes and they are somebody else's; this is one focused look by someone who did not write the code.

### It is a CODE-QUALITY pass. Correctness is the SUITE's, and bounds are YOURS.

**Ask it for the "Code Quality" checklist in `.claude/CLAUDE.md`, and nothing else** — duplication
first (including pre-existing duplication the diff touches), then silent unhandled cases, bare
`default` arms, comments that restate WHAT instead of WHY, thin wrappers, sentinel returns, magic
literals, over-wide typed ranges, redundant `match` arms.

**Do NOT ask it to establish correctness.** The full suite ran one step earlier and is a far better
false-reject detector than any probe it can write by hand — §3's table applies here unchanged. Asking
anyway is how a 67-line review becomes a compile-and-probe expedition.

**Do NOT delegate the bounds questions either — answer them yourself, from the diff, in seconds:**

```bash
diff specs/<name>.md specs-shv2/<name>.md   # byte-identical ⇒ no retractions to justify
git status --short                          # anything outside this spec + the compiler?
git diff --stat                             # as small as the fix warranted?
```

Those three answer *did it edit another spec file* (`/specs/**` is never written to and other
`specs-shv2/*.md` are read-only — tick 2 rewrote `character-ownership.md`'s prose), *did it change
behaviour no red case required* (tick 2 rebuilt interpolated panics, which no case tested), *is the
diff minimal*, and *did it weaken an expectation to pass*. *(Tick 4 read all four off `git diff` in
under a minute — after having asked a reviewer for them.)* They are cheap to see and expensive to
explain, which is precisely the wrong shape to delegate.

⚠ **Prefer a reviewer whose TOOLS cannot exceed the scope.** One holding the build and spec-test tools
has been handed the means to re-do the suite's job, and a brief asking it not to is weaker than simply
not giving it them — Read + Grep + Glob + Bash is the whole kit a quality pass needs. A 60-line diff
does not need the heaviest model in the repo either.

**Findings are FIXED BEFORE THE COMMIT**, then the suite re-run. Do not commit and follow up. ⚠ But its
report is a lead, not a verdict — this project's history is full of agent findings that measurement
refuted, so confirm anything it claims before acting on it, and say so if you disagree.

## 7. The exit for a spec that is really a rung

Some whitelist entries are whole features (`map`, `ownership`, `advent`). When §4's floor fires, or the
first read makes it obvious:

1. **`git checkout -- specs-shv2/` and delete the copied file.** Land nothing half-done.
2. **Record it in `docs/spec-port-log.md` as `DEFERRED`**, naming the mechanism and the rung that must
   land first. That row is what stops the next tick retrying it.
3. **Put the rung itself on `maxon-shv2/PLAN.md`** — the "Future rungs" list — per the project's
   one-backlog rule. `docs/spec-port-log.md` records *what this loop did*; it is a trend, not a backlog,
   and it never holds work.
4. Report and let the loop take the next spec.

## 8. The gate battery, then land it

Nothing here is optional, and none of it needs permission:

| Gate | What red means |
|---|---|
| `build target=shv2` exit 0 | stop |
| **full** `run_spec_test compiler=shv2` (unfiltered) reports **`failed: 0`** | stop — and §0 says how to attribute it, since no baseline was measured |
| `memoryLeak: false` / no exit **101** | stop |
| test-count check (§2) | stop |
| every new `.test` **committed** (§5 — not read, committed) | stop |
| **`maxon-rung-reviewer` run and its findings resolved (§6)** — the LAST thing before `git commit` | stop |

> ### ⛔ THE SUITE GATE IS `failed: 0`. IT IS NOT A TOTAL, AND IT NEVER NEEDED A BEFORE-NUMBER.
>
> **`0 failed` is the whole condition.** A green suite is green whatever it started at, so there is
> nothing to subtract and nothing to remember from last time — which is why §0 no longer opens with a
> measuring run.
>
> A `baseline + this spec's cases` total was the old spelling, and it was a **worse** test of the same
> thing in both directions: it goes wrong when nothing is wrong (other agents commit to `main` between
> ticks and during them — measured 2026-08-02, seven commits mid-tick, two of them spec files), and it
> can come out RIGHT while something is: a spec that silently dropped two cases and a suite that gained
> two elsewhere sum to the number you expected.
>
> **The test-count check on the row below is the one that catches that**, and it is deliberately local —
> this spec's `<!-- test:` markers against this spec's own filter total (§2). It asks a question the
> whole-suite total structurally cannot, and it needs no history to ask it.
>
> ⇒ **Read `failed`, read `memoryLeak`, and read §2's count. The `total` is context for your report, not
> a gate.**

**The order is fixed: build → full suite → REVIEW → fix any findings → re-run the suite → commit →
push.** A review after the commit is a bug report; a review before it is a gate. That is the whole
reason it sits here and not one step later.

### ⇒ RUN `scripts/spec-port-finish.sh`. It is this battery and §9's row, in one pass.

Everything from here down is mechanical and order-sensitive, so it is a script rather than a checklist
you re-derive each tick. **You still do §6's review yourself, first** — the script starts after it.

```bash
scripts/spec-port-finish.sh --spec <name> --outcome PORTED --cases 30/30 \
    --note-file temp/note.md --message-file temp/commit-msg.txt \
    [--build-cost-note-file temp/bc-note.md]      # required iff compiler source changed
```

You write the PROSE (the log note, the commit message, the cost-log note); it measures every NUMBER and
refuses to invent one. What it does, in order: **rebase → rebuild (bootstrap too, if stale) → full
suite → gates → write the log rows → commit → push.**

- **The rebase comes FIRST**, so the suite runs on the tree that will actually be pushed. A rebase after
  testing means pushing something nobody tested.
- **A REJECTED PUSH IS NOT A RETRY.** Another agent landed while you were running, so the script
  rebases and **re-runs the whole suite** before pushing again — it will not push a tree the suite never
  saw. *(Two pushes were rejected on 2026-08-02 alone.)*
- **Nothing is written or committed until every gate is green**, so a failed run leaves the tree as it
  found it. **A non-zero exit means the tick is NOT done** — read the message.
- `--dry-run` runs every gate and writes nothing. `--no-push` stops after the commit.

It re-execs itself from a gitignored copy in `temp/` before touching anything, because it stashes the
working tree and bash reads a script incrementally — stashing its own bytes mid-run fails weirdly.

**If compiler source changed, add a row to `maxon-shv2/build-cost-log.md`** — build seconds, exe bytes,
suite seconds + test count. Its three numbers all fall out of the build and the full-suite run this
battery just did, so it costs nothing but the row. It is a trend, not a gate: **size is exact, the two
times carry a measured ~5% noise band, and a movement inside it is not a datapoint.** See
`.claude/CLAUDE.md`, which makes this binding for *any* compiler change, not just a tick of this loop.

⚠ **The full suite is the gate, not the filtered run.** A front-end change that fixes your spec and
breaks four others reads green under `--filter`.

⚠ **The HOST lane (x64-windows) is what you gate on.** The other lanes — `x64-linux` (WSL),
`arm64-macos` / `arm64-linux` (remote Mac, synced by hand) — are **outside this loop**, exactly as they
are outside a rung's completion. A spec ported here mints host goldens only; the other lanes get theirs
when their sweep next runs. **Do not hold a tick open for a lane that did not run** — a lane that did not
run is not a red gate. *(Do notice the inverse, though: `d45cb0007` was a rung that committed the arm64
lane and forgot the host's.)*

Then: commit, and **push**. Directly on `main` — this repo develops there; do not branch.

**The commit message carries what the diff cannot:** which cases were shelved and the located reason for
each; the before/after suite numbers.

## 9. Close the tick

**`scripts/spec-port-finish.sh` appends this row for you** (§8) — you supply the prose via `--note-file`
and it fills in the measured suite figure. Write it by hand only if you landed without the script.

The row's shape (`docs/spec-port-log.md` mirrors `docs/optimization-log.md`'s read-downwards form):

```markdown
| spec | date | outcome | cases | note |
|------|------|---------|-------|------|
| print-function | 2026-08-01 | PORTED | 4/4 | no compiler change; interpolation already handled every case. Suite 3258 → 3262. |
| <name> | <date> | PORTED | 9/12 | 3 shelved: 2 need Map (rung X), 1 needs the value-tuple ABI. Suite 3262 → 3271. |
| <name> | <date> | DEFERRED | 0/31 | needs the whole ownership model — filed as PLAN.md future rung. Suite unchanged at 3271. |
```

⚠ **This log is INFORMATIONAL and nothing may depend on it.** It is a trend a person reads, exactly like
`docs/optimization-log.md` — write the suite figure in the row because it is useful to a reader, not
because anything downstream consumes it. **The one exception is the `DEFERRED` first column**, which the
§"ordering" selector reads to stop the loop retrying a spec forever; that is a name, not a measurement.
No tick may reconstruct a number from here — if you need one, MEASURE it (§0).

Then report to the user in a few lines: which spec, the gap you closed, what you shelved and why, the
suite delta, and what the next tick will take. **The loop's next tick re-reads the whitelist and picks
up from the file system — it carries nothing in its head, which is what makes it restartable.**

## ⛔ HALT AND ASK

Everything above runs unattended. Stop and report, without landing, when:

- **A gate is red and you did not cause it** — fix a pre-existing red in its own commit first (that is
  the CLAUDE.md rule: you do not care that it is pre-existing). Halt only if the fix is not yours to
  make.
- **The floor fires (§4/§7)** — take the DEFERRED exit and say so; that is a report, not a question.
- **A DESIGN RULING is needed** — `/specs` and shv2 disagree about what is *correct*, and the spec is
  genuinely ambiguous rather than merely unimplemented. **Do not guess, and do not "fix" the spec file
  to match the compiler.** *(A ported spec is a claim about the language. Editing the claim to match
  the implementation is how a compiler bug becomes a specification.)*
- **The port would require changing an EXISTING `specs-shv2` expectation** — that is a behaviour change
  with a blast radius beyond this spec, and it belongs to whoever owns that behaviour.
- **A case cannot be made to pass AND cannot be honestly shelved** — you could not name and locate the
  gap. Report the case and what you found; do not disable it to move on.

## The thing this process exists to catch

**A spec file that lands green having tested nothing.** Three mechanisms produce that: `status: draft`,
a stray `## ` heading, and a `disabled-test:` written to make a case go away. §2's count catches the
first two mechanically and §4's name-and-locate rule is the whole defence against the third. Both are
cheap. **Do them every tick, including the ticks where the spec passed on the first run — especially
those.**

*(A fourth candidate — freshly minted goldens nobody read — was ruled OUT of this loop's scope on
2026-08-01: the goldens gate codegen quality, not correctness, and the spec's own run assertions gate
correctness on every run. See §5.)*
