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
| 2026-10-15 | 9cb941b80 | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 361 | 12,242 | 33.93 | 34.05 | 1.28 | ref@9cb941b80 | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 361 | 978 | 2.71 | 2.72 | 1.28 | slot | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,028 | 11,201 | 10.90 | 4.04 | 1.00 | ref@9cb941b80 | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,028 | 935 | 0.91 | 0.34 | 0.96 | slot | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 364 | 6,983 | 19.17 | 19.23 | 0.21 | ref@9cb941b80 | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 364 | 569 | 1.56 | 1.56 | 0.21 | slot | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 358 | 6,619 | 18.48 | 18.67 | 0.19 | ref@9cb941b80 | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 358 | 525 | 1.46 | 1.44 | 0.19 | slot | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | fan-in | 100000 | 1 | 3 | 273 | 8,108 | 29.69 | 29.82 | 187.06 | ref@9cb941b80 | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | fan-in | 100000 | 1 | 3 | 273 | 971 | 3.56 | 3.56 | 198.12 | slot | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | fan-in | 100000 | 12 | 3 | 626 | 7,430 | 11.87 | 18.65 | 160.67 | ref@9cb941b80 | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | fan-in | 100000 | 12 | 3 | 626 | 2,354 | 3.76 | 7.90 | 161.83 | slot | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,133 | 2,145 | 1.01 | 1.01 | 2.00 | ref@9cb941b80 | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,133 | 2,128 | 1.00 | 1.00 | 2.00 | slot | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 195 | 220 | 1.13 | 1.07 | 1.76 | ref@9cb941b80 | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 195 | 208 | 1.06 | 1.07 | 1.76 | slot | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | spawn-many | 200000 | 1 | 3 | 165 | 2,776 | 16.80 | 17.13 | 5.45 | ref@9cb941b80 | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | spawn-many | 200000 | 1 | 3 | 165 | 514 | 3.11 | 3.16 | 5.45 | slot | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 925 | 19.03 | 24.58 | 10.16 | ref@9cb941b80 | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 437 | 8.98 | 14.57 | 10.26 | slot | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 256 | 1,396 | 5.46 | 5.56 | 2.71 | ref@9cb941b80 | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 256 | 375 | 1.47 | 1.49 | 2.71 | slot | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 241 | 1,404 | 5.83 | 5.91 | 2.90 | ref@9cb941b80 | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 241 | 357 | 1.48 | 1.50 | 2.90 | slot | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 156 | 16,077 | 103.31 | 104.17 | 349.19 | ref@9cb941b80 | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 156 | 1,978 | 12.71 | 12.77 | 387.62 | slot | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 709 | 15,022 | 21.17 | 17.04 | 320.61 | ref@9cb941b80 | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
| 2026-10-15 | 9cb941b80 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 709 | 4,819 | 6.79 | 7.07 | 323.94 | slot | s2r1: Linux host lock reads its owner token from the thread block, no gettid |
