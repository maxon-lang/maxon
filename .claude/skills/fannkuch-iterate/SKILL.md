---
name: fannkuch-iterate
description: One round of the fannkuch-redux Maxon-vs-C loop — build the tree's compiler, measure and profile examples/fannkuch-redux.maxon against the C reference, choose ONE emitted-code optimization, /land it, then a real interleaved A/B against a control compiler built from the pre-change commit, and a log row. Invoke as `/fannkuch-iterate [candidate]`; repeat until the n=12 ratio is at or below 1.0.
---

# One round of the fannkuch loop

**One invocation = one measured, landed, A/B'd compiler change.** The instrument is
`scripts/bench-fannkuch.py`; the ranking is `bench/fannkuch/README.md`; the history is
`docs/fannkuch-benchmark-log.md`. Read the README's traps before the first round.

> ## ⭐ ONE ROUND IS ONE SESSION, ON `main`, LANDED BY `/land`'s OWN TAIL
>
> Rounds 1–6 were built on a local branch and merged on 2026-09-10; from round 7 the loop runs on
> `main` like any other change: `/land`'s rebase on `origin/main`, its battery ONCE, one commit, one
> push. Start each round in a fresh session — a round's context is the README ranking, the log and
> this file, not the previous session's transcript.

## 1. Orient

- `git branch --show-current` is `main`; `git status` is clean; `git fetch` shows nothing behind
  `origin/main`. `PRE=$(git rev-parse --short HEAD)`.
- Build the compiler from the tree: `./maxon-bin/.maxon/maxon.exe build maxon-bin` (from
  `.bootstrap/maxon.exe build maxon-bin` if the slot is empty). One self-compile is enough for the
  PROGRAMS it emits; the two-build rule applies only when the compiler itself is the program under
  test.
- Nothing else may be building or running the suite on the box while a measurement runs.

## 2. Measure

```
python scripts/bench-fannkuch.py --n 11 --runs 5 --profile
```

Read, in this order: the ratio; the census columns; the profile's hot functions; then the IR of the
hot functions in `bench/fannkuch/out/slot/fannkuch-redux.ir`. Rebuild with `--emit-ir-runtime=<name>`
when a library body (`__managed_*`) is where the time went. Under ~2 s at n=11, profile with
`--profile-n 12` so the sampler has enough samples.

## 3. Pick ONE candidate

From the README ranking or from what the profile shows. Before touching code, write down:

- the SHAPE it targets in this program: which loop, which instruction sequence, how many times per
  permutation;
- the predicted census movement (which column, by roughly how much);
- the soundness argument, in one paragraph.

A candidate that names no shape is not a candidate.

## 4. Land it

`/land <the change>`, with these substitutions for an optimization:

- The red set is the SOUNDNESS half: wrong-answer controls that print a wrong number or exit 101 when
  the rule is sabotaged. Run each sabotage and record the observed failure in the case text. Goldens
  are reference, not a gate: review the minted diff under `--update-required` with a filter, line by
  line, and keep every moved fragment.
- Every target the change touches gets the equivalent change (x64, arm64, wasm) or a stated reason.
- ⛔ **During the round, never run the entire spec suite** (user ruling). Every spec run — yours, an
  implementer's, a reviewer's — is `spec-test --filter=<spec>` over the spec files the change
  touches, one filter per file, on x64 and again with `--target=wasm32-wasi`. The full suite runs
  exactly once, in `/land`'s battery (§7 there), after the rebase; the self-compile is the E3092 gate
  and is not a spec run.
- ⛔ **A filtered-green compiler is not a working compiler.** After EVERY rebuild the implementer
  builds `examples/fannkuch-redux.maxon` into a scratch directory (`--emit-ir --log=ir:debug`) and
  runs it at n=10 (73196 / 38, exit 38); a panic or a wrong answer there is a red gate, and the
  function it died in becomes a spec case. Round 5 shipped a filtered-green compiler that could not
  compile the example.
- ⛔ **A sabotaged compiler must never build the revert.** After a sabotage measurement, rebuild from
  `.bootstrap/maxon.exe` (`build maxon-bin -o maxon-bin/.maxon/maxon`), then self-compile, then the
  example gate. Round 5 let a sabotaged compiler build the reverted source and got a spec-green
  compiler that panicked in its own CSE.
- Run §5 and §6 below between `/land`'s battery and its commit, so the round is ONE commit carrying
  the compiler change, its spec, the README row, the log rows and the roadmap row. A push rejected
  by a newer `origin/main` re-runs the battery only if the new commits touch `maxon-bin/`, `specs/`
  or `stdlib/` (`/land`'s rule: never repeat a run whose inputs have not changed).

## 5. A/B

```
python scripts/bench-fannkuch.py --n 11 --runs 5 --ref $PRE --self-compile --note "<candidate>: <mechanism>; census <col> a->b; predicted <shape>"
```

Control and experiment are two compilers in ONE session, runs interleaved. A before/after across a
source change is not an A/B (roadmap, EC19). The note carries the WHY; the tool cannot.

> ## ⛔ HALT ON A SLOWER SELF-COMPILE (user ruling 2026-09-08)
>
> `--self-compile` times each arm's compiler compiling `maxon-bin` (the control arm — `--ref $PRE`
> — is listed first, so put it first). **A `HALT` line means the change made the compiler compile
> itself more than 5% slower than the control does.** Do not land it; do not narrow the pass to make
> the number pass. Stop, report the two times and the mechanism, and wait for the user's ruling —
> the compiler's own build time is a budget the user owns, and a benchmark win does not spend it.
> Between +2% and +5%, say so in the note and carry on.

> ## ⛔ THE BENEFIT GATE — a round lands only if it measured a real win
>
> The change lands when EITHER holds:
>
> - **fannkuch**: the experiment's n=11 median beats the control's by at least 2% AND by more than
>   the wider of the two arms' min-to-max spread; or
> - **the compiler itself**: `--self-compile` shows the experiment at least 2% faster than the
>   control (the compiler is a large real program, and a general pass that helps it has proven
>   itself even where this loop's inner loops did not move).
>
> Below both bars, READ the census and the profile from the same run before deciding, because the
> three ways to miss have three different answers:
>
> | What the run shows | Meaning | Do |
> |---|---|---|
> | the census column predicted in §3 did not move; the IR still holds the shape | the pass missed its shape — an implementation miss, not a wrong idea | ONE bounded fix, then ONE more A/B; a second miss declines |
> | the census moved as predicted, the medians did not | the premise "this shape costs time" was wrong (EC14) | decline — no improvement to the pass can repair a wrong premise |
> | the census and the time moved, but under the bar | the shape is not hot enough; the ranking was wrong | decline; re-rank from the profile |
>
> **Declining** means: the compiler change and its spec do NOT land. Revert them, and commit only the
> README row marked DECLINED with the two medians, the census movement and the mechanism, so the
> next session does not rebuild it. A decline is a measurement, not a failure; the roadmap's
> DECLINED rows are its most-cited ones.

## 6. Headline

Whenever n=11 moved by more than the arms' spread. One run is enough while the ratio is far
from 1.0 (a 112 s run does not need averaging to read 5×); take 5 only when the arms are within a few
percent of each other or of C:

```
python scripts/bench-fannkuch.py --n 12 --runs 1 --warmup 0 --ref $PRE --note "<candidate> headline"
```

## 7. Record

- `bench/fannkuch/README.md`: mark the candidate closed with its measured delta (or DECLINED under
  §5's gate, with the measurement and which of the three misses it was), and re-rank the rest from
  the NEW profile.
- `docs/emitted-code-roadmap.md`: a row in the workstream's format when the change is a codegen change.
- README + log + roadmap go into the round's one commit (§4), which `/land` pushes.

## 8. Stop condition

Maxon median ≤ C median at n=12 over 5 runs, both arms verified, C built by the README's command.
The final row's note begins `HEADLINE`.

## Anti-patterns, each already paid for once

- **Instruction count is not time.** `EC14` deleted two loads from the anchor loop and measured zero.
- **Attribute the shape before predicting the win.** One change was ×1.90 on fannkuch and −5.8% on nbody.
- **Rows rot.** Re-measure before planning from a row.
- **Fixed-order runs manufacture a sign.** The harness interleaves; do not time by hand.
- **A green suite cannot see a premature free.** `A3`'s maximal elision passed 6,954 cases and printed
  a silent wrong answer.
- **Never touch the instrument to make a number look better.** Write no row you did not measure.
- **A change under `Compiler/Runtime/` shows in the programs the compiler builds after ONE build**, and
  in the compiler's own behaviour only after two.
- **Goldens drift for reasons that are not yours.** The full battery reports thousands of drifted
  fragments (main's console-probe data block); drift is committed as it lies and is not a gate. Read
  only the drift in the specs the round touches, under `--update-required` with a filter.
