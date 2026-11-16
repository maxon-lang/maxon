# The halves the benchmark programs share

`bench/aes` and `bench/hash` each hold one Maxon program and one Go program that run the same workloads and
print the same lines. What is not specific to a workload lives here: the argument parsing, the timing loop, the
clock, the digest helper and the printing of a row.

```
bench/common/
  BenchProgram.maxon   the Maxon half
  go/                  the Go half: package benchcommon, module maxon.dev/bench/common (Go 1.24 or newer)
```

## The Maxon half

`BenchProgram.maxon` is compiled in one program with the benchmark's own file: the harness hands both to one
`maxon build` (`bench_common.compile_in_arm` stages them side by side in the arm's directory, and the examples
harness's `companions` argument passes the shared file's checkout path). It declares `Settings`, `filled`,
`digest`, `iterationsOf`, `clockResolution`, `measure` and `main`, and expects the benchmark's file to declare:

| Name | What it is |
|---|---|
| `benchmarkName` | the program's name, for its usage line |
| `Workload` | an enum with one case per row, in print order |
| `nameOf`, `baseIterationsOf`, `bytesOf` | a workload's row name, base iteration count and bytes per op |
| `Fixtures` | the inputs, built once by `Fixtures.create(settings)` before the first row |
| `runOnce(workload, f, iterations)` | one timed repeat, which answers an `Outcome` |
| `Outcome` | what a repeat produced; its `answer()` is the row's digest |

Every one of these is `public` and so are the shared declarations the benchmarks call, because the two files sit in
different directories in the repository (the examples tests build them where they are) and in one directory when
the harness stages them.

`measure` reads the clock, calls `runOnce`, reads the clock, and only then asks the outcome for its answer. The
outcome is a `let` of the repeat's loop body, so whatever it holds, a 1000000-entry table included, is released
after the second clock read and not inside the timed interval.

## The Go half

`benchcommon` gives a program `ParseSettings(name)`, `Iterations`, `Filled`, `Digest`, `Settings.PrintClock` and
`Settings.Measure(name, bytesPerOp, baseIterations, run)`, where `run` answers a `Result` whose `Answer()` is the
row's digest. `Measure` calls `runtime.GC()` before each repeat's clock read, so the garbage the previous repeat
left (the tables, mostly) is collected outside the timed interval, and keeps the result alive until after the
second clock read. The clock is `QueryPerformanceCounter` on Windows and `time.Since` elsewhere.

A program's `go.mod` requires `maxon.dev/bench/common` and replaces it with `../../common/go`. The harnesses
hash the sources of both directories into the build stamp (`bench_common.build_go_variants`), so an edit here
rebuilds every Go program that uses it.
