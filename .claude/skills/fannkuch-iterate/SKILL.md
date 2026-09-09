---
name: fannkuch-iterate
description: One round of the fannkuch-redux Maxon-vs-C loop — build the tree's compiler, measure and profile examples/fannkuch-redux.maxon against the C reference, choose ONE emitted-code optimization, /land it, then a real interleaved A/B against a control compiler built from the pre-change commit, and a log row. Invoke as `/fannkuch-iterate [candidate]`; repeat until the n=12 ratio is at or below 1.0.
---

# One round of the fannkuch loop

**One invocation = one measured, landed, A/B'd compiler change.** The instrument is
`scripts/bench-fannkuch.py`; the ranking is `bench/fannkuch/README.md`; the history is
`docs/fannkuch-benchmark-log.md`. Read the README's traps before the first round.

> ## ⛔ THIS WORK LIVES ON THE LOCAL BRANCH `fannkuch-loop` AND IS NEVER PUSHED
>
> Refuse to run on `main`. Every commit goes on `fannkuch-loop`; there is no rebase and no
> `git push` — the branch is merged to `main` by the user when the loop is finished. `/land`'s
> git tail is replaced by exactly one commit on this branch.

## 1. Orient

- `git branch --show-current` is `fannkuch-loop`; `git status` is clean. `PRE=$(git rev-parse --short HEAD)`.
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
- The git tail is one commit on `fannkuch-loop`. No rebase. No push.

## 5. A/B

```
python scripts/bench-fannkuch.py --n 11 --runs 5 --ref $PRE --note "<candidate>: <mechanism>; census <col> a->b; predicted <shape>"
```

Control and experiment are two compilers in ONE session, runs interleaved. A before/after across a
source change is not an A/B (roadmap, EC19). The note carries the WHY; the tool cannot.

## 6. Headline

Whenever n=11 moved by more than the arms' spread:

```
python scripts/bench-fannkuch.py --n 12 --runs 5 --ref $PRE --note "<candidate> headline"
```

## 7. Record

- `bench/fannkuch/README.md`: mark the candidate closed with its measured delta (or DECLINED with the
  measurement), and re-rank the rest from the NEW profile.
- `docs/emitted-code-roadmap.md`: a row in the workstream's format when the change is a codegen change.
- Commit README + log + roadmap on `fannkuch-loop`.

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
