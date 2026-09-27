---
feature: pass-fan-out-order
status: experimental
keywords: [passes, diagnostics, ordering, parallel, function-index, per-function]
category: codegen
---

## Documentation

The Std passes that work one function at a time — `pruneDeadBlockArgs`, `elimTrivialBlockArgs`,
`foldConstants`, `strengthReduceDivision`, `foldConstOperands`, `threadConstantBranches`,
`refineValueRanges`, `commonSubexpressionElimination`, `loopInvariantCodeMotion`,
`unswitchInvariantGuards`, `foldEstablishedGuards`, `refineVersionedRanges`, and `insertRangeChecks`
before them — run one function at a time on workers and answer in **function-index order** however the
functions are scheduled. Each pass's `--log=ir` banner and summary is printed once per step. The
contract has two lines:

1. A compile-time diagnostic a per-function pass raises is reported in the order of the functions
   that raise it, and a pass reports **every** function's diagnostics before the pipeline refuses the
   program — the error gate runs after the pass, not inside it.
2. The emitted binary does not depend on how many workers ran the passes.

The first line is pinned here. The second is a property of a spawned compiler's output file, which a spec
case cannot observe, so it is gated by the driver corpus `tests/parallel-compile/`
(`byte-identical.maxtest`).

**Which pass the case exercises, and why.** None of the passes from `pruneDeadBlockArgs` through
`refineVersionedRanges` has a diagnostic site: they rewrite, they never refuse. The per-function pass
nearest that group that can
refuse a program is `insertRangeChecks`, which reports **E3005** for a written literal outside the range
of the alias it reaches, one report per site, so it is the gate for the first line of the contract.

## Tests

<!-- test: diagnostics-from-two-functions-report-in-declaration-order -->
Two functions each return a literal outside `Small`. Both are reported — the refusal does not stop at
the first function — and `first`'s line precedes `second`'s because `first` is declared first.
```maxon
typealias Small = int(0 to 10)

function first() returns Small
	return 11
end 'first'

function second() returns Small
	return 12
end 'second'

function main() returns ExitCode
	print("{first()} {second()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:5:2: Value 11 is outside the range of 'Small' (int(0 to 10))
error E3005: <fragment>:9:2: Value 12 is outside the range of 'Small' (int(0 to 10))
```
