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

| date | commit | change | build s | exe bytes | code bytes | suite s | tests |
|------|--------|--------|---------|-----------|------------|---------|-------|
| 2026-08-01 | `f3a208b5b` | baseline — the tree `/spec-port` was built against (no compiler change of its own) | 19.7 | 10,076,672 | 9,002,998 | 48.0 / 48.9 / 50.4 | 3262 |
