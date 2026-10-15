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
| 2026-10-15 | f561e2d8c | dirty | windows | thread-ring | 3000000 | 1 | 3 | 339 | 1,272 | 3.76 | 3.90 | 0.86 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | thread-ring | 3000000 | 1 | 3 | 339 | 864 | 2.55 | 2.62 | 0.86 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | thread-ring | 3000000 | 12 | 3 | 1,599 | 1,729 | 1.08 | 0.21 | 0.80 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | thread-ring | 3000000 | 12 | 3 | 1,599 | 1,394 | 0.87 | 0.15 | 0.80 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | ping-pong | 1000000 | 1 | 3 | 360 | 717 | 1.99 | 2.09 | 0.71 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | ping-pong | 1000000 | 1 | 3 | 360 | 464 | 1.29 | 1.32 | 0.70 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | ping-pong | 1000000 | 12 | 3 | 490 | 984 | 2.01 | 2.07 | 0.69 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | ping-pong | 1000000 | 12 | 3 | 490 | 787 | 1.61 | 1.69 | 0.69 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | fan-in | 100000 | 1 | 3 | 274 | 1,152 | 4.21 | 4.35 | 66.79 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | fan-in | 100000 | 1 | 3 | 274 | 762 | 2.78 | 2.88 | 50.06 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | fan-in | 100000 | 12 | 3 | 993 | 3,053 | 3.07 | 9.35 | 54.24 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | fan-in | 100000 | 12 | 3 | 993 | 2,254 | 2.27 | 7.21 | 41.59 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | worker-pool | 6000 250000 | 1 | 3 | 2,123 | 2,123 | 1.00 | 1.00 | 1.29 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | worker-pool | 6000 250000 | 1 | 3 | 2,123 | 2,122 | 1.00 | 0.99 | 1.27 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | worker-pool | 6000 250000 | 12 | 3 | 197 | 211 | 1.07 | 1.03 | 1.13 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | worker-pool | 6000 250000 | 12 | 3 | 197 | 211 | 1.07 | 1.04 | 1.12 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | spawn-many | 200000 | 1 | 3 | 243 | 1,127 | 4.63 | 4.50 | 6.61 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | spawn-many | 200000 | 1 | 3 | 243 | 1,083 | 4.46 | 4.25 | 6.60 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | spawn-many | 200000 | 12 | 3 | 54 | 418 | 7.66 | 8.45 | 6.14 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | spawn-many | 200000 | 12 | 3 | 54 | 391 | 7.18 | 7.82 | 6.13 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | coroutine-churn | 200000 | 1 | 3 | 353 | 899 | 2.55 | 2.59 | 4.48 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | coroutine-churn | 200000 | 1 | 3 | 353 | 904 | 2.56 | 2.64 | 4.48 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | coroutine-churn | 200000 | 12* | 3 | 350 | 940 | 2.69 | 2.68 | 4.55 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | coroutine-churn | 200000 | 12* | 3 | 350 | 924 | 2.64 | 2.59 | 4.55 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | shared-counter | 200000 | 1 | 3 | 160 | 2,289 | 14.34 | 13.18 | 130.89 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | shared-counter | 200000 | 1 | 3 | 160 | 1,511 | 9.47 | 8.82 | 98.48 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | shared-counter | 200000 | 12 | 3 | 558 | 6,055 | 10.85 | 19.56 | 106.89 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | windows | shared-counter | 200000 | 12 | 3 | 558 | 4,620 | 8.28 | 15.31 | 81.89 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 356 | 979 | 2.75 | 2.76 | 1.28 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 356 | 692 | 1.94 | 1.95 | 1.28 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,033 | 937 | 0.91 | 0.33 | 1.04 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,033 | 771 | 0.75 | 0.28 | 1.04 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 365 | 569 | 1.56 | 1.56 | 0.21 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 365 | 371 | 1.01 | 1.02 | 0.21 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 355 | 530 | 1.49 | 1.47 | 0.25 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 355 | 422 | 1.19 | 1.19 | 0.19 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | fan-in | 100000 | 1 | 3 | 274 | 1,061 | 3.88 | 3.87 | 198.25 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | fan-in | 100000 | 1 | 3 | 274 | 653 | 2.39 | 2.39 | 148.94 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | fan-in | 100000 | 12 | 3 | 627 | 2,393 | 3.82 | 7.59 | 155.26 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | fan-in | 100000 | 12 | 3 | 627 | 1,441 | 2.30 | 6.09 | 117.05 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,135 | 2,126 | 1.00 | 1.00 | 2.00 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,135 | 2,125 | 1.00 | 1.00 | 1.93 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 195 | 208 | 1.06 | 1.07 | 1.76 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 195 | 206 | 1.06 | 1.04 | 1.76 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | spawn-many | 200000 | 1 | 3 | 165 | 517 | 3.14 | 3.18 | 5.51 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | spawn-many | 200000 | 1 | 3 | 165 | 465 | 2.82 | 2.87 | 5.48 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | spawn-many | 200000 | 12 | 3 | 50 | 436 | 8.80 | 14.11 | 9.62 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | spawn-many | 200000 | 12 | 3 | 50 | 476 | 9.62 | 16.69 | 9.60 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 255 | 374 | 1.46 | 1.47 | 2.52 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 255 | 317 | 1.24 | 1.25 | 2.52 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 256 | 334 | 1.30 | 1.29 | 2.53 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 256 | 316 | 1.23 | 1.23 | 2.53 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | shared-counter | 200000 | 1 | 3 | 154 | 2,062 | 13.35 | 13.40 | 388.19 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | shared-counter | 200000 | 1 | 3 | 154 | 1,330 | 8.61 | 8.65 | 296.50 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | shared-counter | 200000 | 12 | 3 | 709 | 4,810 | 6.79 | 7.08 | 327.06 | ref@f561e2d8c | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
| 2026-10-15 | f561e2d8c | dirty | wsl | shared-counter | 200000 | 12 | 3 | 709 | 2,871 | 4.05 | 5.50 | 245.94 | slot | s2r2: plain lock-depth step, envelope-free mailbox, Go park order + lock-free wake early-out, one lock per strand turn |
