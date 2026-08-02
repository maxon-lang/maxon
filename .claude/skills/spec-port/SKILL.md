---
name: spec-port
description: Port ONE spec file from /specs into specs-shv2 and make it pass — the rapid-iteration loop. Takes the next spec off the v1 whitelist order (maxon-selfhosted/Testing/SpecTestRunner.maxon), copies it byte-identical, runs it, implements the gaps, and lands it. A passing spec is good enough — the minted goldens are committed, not audited. One spec per invocation, designed for `/loop /spec-port`. Invoke as `/spec-port <name>` to take a specific spec instead of the next one.
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

## 0. Baseline — measure it, never assume it

```
build target=shv2 repoRoot=C:/Users/Eric/dev/maxon      # bootstrap first if it is stale
run_spec_test compiler=shv2 repoRoot=C:/Users/Eric/dev/maxon
```

Write the number down. **You need it to attribute every later number**, and a tick that starts red is
not a tick — a red you did not cause still gets fixed first, in its own commit, before the port
(precedent: `d45cb0007`, a stale golden + eight unminted ones left by another rung's arm64-only sweep).

⚠ **`git status` must be clean before you copy anything.** The suite MINTS goldens as a side effect
(§5); if the tree is already dirty you cannot tell yours from the leftovers.

## 1. Copy it BYTE-IDENTICAL

```bash
cp specs/<name>.md specs-shv2/<name>.md
```

**No edits. Not the frontmatter, not the prose, not one case.** The copy is the *claim* — "shv2 should
do what this says" — and every subsequent edit you make to it is a *retraction* you now have to justify
in the commit message. Starting from a pre-trimmed file makes the retractions invisible, which is the
whole failure mode this step exists to prevent. `git diff` against `/specs` is the review of your
retractions; keep it readable.

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
> **So every tick, mechanically:**
> ```bash
> grep -c '<!-- test:' specs-shv2/<name>.md      # markers in the file
> grep -c '<!-- disabled-test:' specs-shv2/<name>.md
> ```
> **markers − disabled MUST equal the runner's `total` for that filter.** A shortfall is a defect in
> the port, never a pass. Fix it by moving the `## ` heading below the cases (and say so in the commit),
> not by accepting the smaller number.

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
(v1) first** — the closest code, in the same language against the same `stdlib/`, and *it passes this
spec, because the whitelist is v1's* — then **`maxon-sharp` (the bootstrap)** as the RUNNABLE oracle
when the question is *what is the right answer* rather than *how is it built*. **Neither binds it.**
shv2 is a deliberate rewrite; where it departs, the departure is the thesis, and an implementation that
does not fit gets designed rather than copied.

**Inline is the exception, and a narrow one:** a one-line fix you have already located and can prove
with a single filtered run. The moment it needs a second hypothesis, hand it over.

⛔ **A defect this spec reaches is FIXED, not filed** — a wrong answer as much as a leak, and whether or
not the suite is green over it. That is the CLAUDE.md rule and neither you nor the agent softens it.
What may be shelved is a case needing a *feature that does not exist yet* (§4) — never a case that is
broken.

**Then verify its claims yourself.** Its report is a lead: re-run the filtered suite and the full one,
and read its diff. This project's history is full of agent findings that measurement refuted.

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

## 6. The other two agents — a judgement call, and here is the call

*(The implementer of §3 is not a judgement call — it is the default. These two are.)*

**REVIEWER — run `maxon-rung-reviewer` whenever compiler source changed. Every time.** The loop is
unattended, and delegating §3 makes this *structurally* right rather than merely advisable: the reviewer
is now never the agent that wrote the code, which is the rule `/rung` states and could not enforce when
the coordinator implemented inline. *Duplication is its top priority* — precisely what a long tail of
small similar gaps generates, one near-miss helper at a time. Skip it only for a **zero-code-change
port** (the spec copied in and passed as-is), where there is nothing to review. **Verify its claims
yourself before acting on them** — an agent's finding is a lead, and this project's own history is full
of refuted ones.

**OPTIMIZER — the LADDER READ is yours and routine; the AGENT is TRIGGERED.**
- **Always, if compiler source changed:** `run_scale_test repoRoot=...` (~17 s) and *read* it. It is an
  instrument with no verdict — you are looking for a ratio that bends across a DOUBLING ladder (×2
  linear, ×4 quadratic), in allocations/bytes (exact — any movement is real) and CPU (noise band of a
  few percent). Do **not** pass `note:` for a routine tick; a log of every tick is a log nobody reads.
  Pass it only when the row is a datapoint, and then say WHY it moved.
- **Spawn `maxon-rung-optimizer` only on a trigger:** a bent curve you cannot explain; or a change that
  added a pass, a whole-program walk, a new collection the compiler indexes by, or a per-site scan. A
  leaf parser/typecheck fix trips none of these and does not get an agent.

Both agents work in this same checkout — tell them so, and tell the reviewer to report, not edit.

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
| **full** `run_spec_test compiler=shv2` (unfiltered) ≥ baseline + this spec's cases | stop |
| `memoryLeak: false` / no exit **101** | stop |
| test-count check (§2) | stop |
| every new `.test` **committed** (§5 — not read, committed) | stop |

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

Append one row to `docs/spec-port-log.md` (create it on the first tick, mirroring
`docs/optimization-log.md`'s read-downwards shape):

```markdown
| spec | date | outcome | cases | note |
|------|------|---------|-------|------|
| print-function | 2026-08-01 | PORTED | 4/4 | no compiler change; interpolation already handled every case |
| <name> | <date> | PORTED | 9/12 | 3 shelved: 2 need Map (rung X), 1 needs the value-tuple ABI |
| <name> | <date> | DEFERRED | 0/31 | needs the whole ownership model — filed as PLAN.md future rung |
```

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
