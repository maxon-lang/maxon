# Build-cost log — what a compiler change costs to BUILD, to SHIP, and to TEST

A dated table you read downwards, recorded on **every commit that changes compiler source**. Three
numbers, and they are three different kinds of number — read each accordingly.

This is a **trend, not a gate.** Nothing here passes or fails. A row that moves is a reading to
*explain*, exactly as in `docs/optimization-log.md`, and the moment to explain it is when you write it —
the numbers can show WHAT moved and can never show WHY.

## ⚠ The three columns do NOT read alike

| column | kind | how to read it |
|---|---|---|
| **exe bytes** / **code bytes** | **EXACT and bit-reproducible** | The same sources produce the same binary. **Any movement is real**, down to the byte. This is the trustworthy column. |
| **build s** | **WALL CLOCK** | Counts every other process on the box. Noisy, and not comparable across machines. |
| **suite s** | **WALL CLOCK**, and it is a *parallel* run | Same caveat, worse: the runner uses a worker pool, so it also moves with core count and with whatever else is competing for cores. |

**The wall-clock noise band is MEASURED, not assumed.** Three consecutive full-suite runs of the
**identical binary over the identical suite** (2026-08-01, 3262 tests, an otherwise-idle box) read
**48.0 s / 48.9 s / 50.4 s** — a spread of **~5%**. So:

> **⇒ A time movement under ~5% is NOT a datapoint.** Do not write a sentence explaining it. A build
> that goes 19.7 s → 20.4 s has told you nothing. One that goes 19.7 s → 31 s has.

The size columns have no such band. A 4-byte move in `exe bytes` is four real bytes.

*(This is the same argument `.claude/CLAUDE.md` makes about `scale-test`, which is why that instrument
deliberately collects **no** wall time at all. These columns are kept anyway because they answer a
different question — not "does this pass scale" but "is the compiler still cheap to build and test?" —
and because a 10× regression in either is worth catching whatever the noise floor is. Do not mistake
them for the scaling instrument: for per-phase memory and CPU-time trends, `run_scale_test` and
`docs/optimization-log.md` remain the only honest answer.)*

## Where the numbers come from — no stopwatch, no extra runs

All three fall out of work the tick is doing anyway:

```
build     target=shv2   ->  durationMs, and the rawTail's "Wrote <N> bytes code, ..." line
run_spec_test compiler=shv2  ->  durationMs, passed, total
```
plus the on-disk size:
```bash
stat -c '%s' maxon-shv2/.maxon/maxon-shv2.exe
```

⚠ **Record the build of the tree you are COMMITTING**, not an earlier one. A stale binary's numbers
belong to a different commit, and the `.mxdbg` debug sidecar is a separate file — `exe bytes` is the
executable alone.

## The table

`code bytes` is the emitted machine code alone (from the build's own report); `exe bytes` is the whole
PE/ELF on disk, which also carries rdata, data, ucddata, symdata and headers.

⚠ **A row cannot carry the hash of the commit that creates it** — the row is *in* that commit. So the
first column is the commit's SUBJECT, not its hash: `git log --oneline --grep=` finds it, and nothing
has to be filled in afterwards by a second commit that would then need its own row. (The first row is
the exception: it measures a tree that already existed.)

| date | commit | change | build s | exe bytes | code bytes | suite s | tests |
|------|--------|--------|---------|-----------|------------|---------|-------|
| 2026-08-01 | `f3a208b5b` | baseline — the tree `/spec-port` was built against (no compiler change of its own) | 19.7 | 10,076,672 | 9,002,998 | 48.0 / 48.9 / 50.4 | 3262 |
| 2026-08-01 | `spec-port: tick 2 — panic` | `panic("…")` routed to the `mrt_panic` runtime (`StdOp.osPanic`), message baked at parse time. Exe +4,096 B (one page); emitted panic BLOCKS shrank 4 instructions → 2 everywhere, but the compiler gained the parse-time baking, so `code bytes` is +3,093. Both times inside the ~5% band ⇒ no reading. ⚠ **ITS TWO SIZE NUMBERS DO NOT DESCRIBE ITS OWN TREE — see the tick-4 row and do not diff against them.** | 19.0 | 10,080,768 | 9,006,091 | 49.6 | 3266 |
| 2026-08-01 | `spec-port: tick 4 — function-declaration` | A function SIGNATURE now has to end at its line (`requireSignatureLineEnd`), so `function foo() int` — the `returns` keyword missing — is E2001 at the `int` instead of falling into the body as `E2015 Unsupported: int statement`. Deduplicating the "token's text, or its kind's spelling" fact into one `tokenDisplayText` (3 open-coded readers → 1) paid for the new check and more: **exe −4,608 B, code −4,827 B.** ⚠ **Those deltas are against a MEASURED CONTROL, not against the row above** — see the note under the table. Build and suite times are not comparable to earlier rows either (different bootstrap); both are unremarkable on their own. | 18.9 | 10,212,352 | 9,118,910 | 43.4 | 3272 |

## ⛔ 2026-08-01 — the first TWO rows' SIZE columns are not comparable to later ones

**Measured, not inferred.** Tick 4 read `exe 10,212,352 / code 9,118,910` where the tick-2 row above
records `10,080,768 / 9,006,091` — a step of **+131,584 / +112,819** that no change could account for,
so it was treated as a reading to explain before anything was written down.

**The control settles it.** `git stash` → build the immediate predecessor tree (`ddefd957e`) → measure:

```
CONTROL (ddefd957e, none of tick 4's work)   exe 10,216,960   code 9,123,737
tick 4  (the tree being committed)           exe 10,212,352   code 9,118,910
                                                  -4,608          -4,827
```

**No commit between the tick-2 row and that control touched `stdlib/` or `maxon-shv2/Compiler/`** (all
five are skill/doc commits), and the bootstrap binary that compiles shv2 was untouched across them. So
the control tree and tick 2's tree must produce the *same* shv2 binary — and the control says that
binary is `9,123,737`, not the `9,006,091` the row claims. ⇒ **The tick-2 row's size columns describe
some other build.** The baseline row above it is low by the same ~117 KB, so both predate whatever
changed.

**Most likely cause, stated as the hypothesis it is:** `bin/maxon.exe` — the bootstrap that *emits*
shv2 — has an mtime of **16:08**, while the rows were written in the evening. A bootstrap refresh
changes shv2's emitted size without a line of shv2 changing, which is the trap already recorded for
`docs/optimization-log.md`. Unverified, because reproducing it means rebuilding the old bootstrap; the
*consequence* needs no verification and is what matters here.

⇒ **Two rules this buys, both cheap:**
1. **A size step you cannot account for gets a CONTROL before it gets a sentence** — stash, build the
   predecessor, measure. It costs one build (~19 s) and it is the only thing that separates "my change
   did this" from "this row is measuring a different compiler."
2. **`exe bytes` / `code bytes` are only comparable across rows built by the SAME bootstrap.** When the
   bootstrap moves, the size columns restart; say so in the row rather than reading the step as a
   regression.

⚠ **Separately, and worth someone's attention:** that bootstrap binary is **older than its own source**
— `bin/maxon.exe` is 16:08, while the last commit touching `maxon-sharp/` (`e4c654236`) is 19:55. Every
shv2 build in this log since then was emitted by a stale bootstrap. That is not this tick's to fix, and
it does not threaten any correctness gate (the suite is green and the goldens are the compiler's own
output either way), but the next `maxon-sharp` change should rebuild it before recording a row.
