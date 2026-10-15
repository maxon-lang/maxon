# Concurrency benchmark log

Wall time, CPU time and peak working set of the Maxon programs under `bench/concurrency/` against their
Go twins (`bench/concurrency/<workload>/main.go`), both built and run on this machine by
`scripts/bench-concurrency.py`. **Read the tables downwards**: each row is one measurement of one
workload at one processor count, dated, and the ratio columns are the numbers the optimization loop drives
at 1.0.

- **Rows are appended by the tool, never by hand.** A run without `--note` records nothing; a run
  with one records unconditionally. The note says WHY the number moved - the instrument cannot.
- **This is an instrument, not a gate.** There is nothing to pass; a row that did not move is a
  datapoint, and a row that moved the wrong way is a reading to explain.
- **Rows rot.** Re-measure before planning from one; an A/B is two arms in ONE session, interleaved,
  never a before/after across a source change.
- **Only rows from one host compare.** `host` is `windows` (Maxon x64-windows against Go for Windows) or
  `wsl` (Maxon built `--target=x64-linux`, Go built in WSL, both run inside WSL); the machine is the
  same, the schedulers and allocators are not.
- `docs/optimization-log.md` is the compile-time axis and is never touched by this tool.

Columns: `P` is the processor count, `MAXON_MAX_PROCS=P` against `GOMAXPROCS=P`, and a trailing `*`
marks a workload whose Go arm was held at `GOMAXPROCS=1` (coroutine-churn: Maxon coroutines share one
strand); `args` are the sizes passed to both programs; `go wall ms` and `maxon wall ms` are medians of
`runs` interleaved timed runs after one warm-up each; each `ratio` is Maxon median / Go median for that
metric (wall, CPU = user + kernel, peak working set); `arm` names the compiler (`slot`, `ref@<sha>`, or a
`--label`); `tree` is whether the harness's checkout was clean.

| date | head | tree | host | workload | args | P | runs | go wall ms | maxon wall ms | wall ratio | cpu ratio | mem ratio | arm | note |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
