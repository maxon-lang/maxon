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
- **Only rows from one host compare.** `host` is one of:
  - `windows`: Maxon x64-windows against Go for Windows;
  - `wsl`: Maxon built `--target=x64-linux`, Go built in WSL, both run inside WSL;
  - `macos`: arm64-macos natively on the Mac;
  - `orb`: Maxon built `--target=arm64-linux` on the Mac, run with Go inside an OrbStack arm64 Linux machine;
  - `linux`: a native Linux host.

  Rows from two hosts on the same machine still differ in scheduler and allocator.
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
| 2026-10-15 | eb401fad2 | clean | macos | thread-ring | 3000000 | 1 | 3 | 157 | 515 | 3.28 | 3.30 | 0.77 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | thread-ring | 3000000 | 1 | 3 | 157 | 464 | 2.95 | 2.97 | 0.77 | slot | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | thread-ring | 3000000 | 8 | 3 | 714 | 848 | 1.19 | 0.47 | 0.72 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | thread-ring | 3000000 | 8 | 3 | 714 | 832 | 1.17 | 0.44 | 0.70 | slot | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | ping-pong | 1000000 | 1 | 3 | 182 | 321 | 1.77 | 1.77 | 0.59 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | ping-pong | 1000000 | 1 | 3 | 182 | 282 | 1.55 | 1.55 | 0.59 | slot | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | ping-pong | 1000000 | 8 | 3 | 207 | 540 | 2.61 | 2.53 | 0.59 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | ping-pong | 1000000 | 8 | 3 | 207 | 541 | 2.61 | 2.51 | 0.60 | slot | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | fan-in | 100000 | 1 | 3 | 126 | 348 | 2.76 | 2.78 | 107.25 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | fan-in | 100000 | 1 | 3 | 126 | 288 | 2.28 | 2.27 | 80.66 | slot | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | fan-in | 100000 | 8 | 3 | 470 | 924 | 1.97 | 3.41 | 78.26 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | fan-in | 100000 | 8 | 3 | 470 | 702 | 1.50 | 2.67 | 58.66 | slot | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | worker-pool | 6000 250000 | 1 | 3 | 2,399 | 2,405 | 1.00 | 1.00 | 1.51 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | worker-pool | 6000 250000 | 1 | 3 | 2,399 | 2,398 | 1.00 | 1.00 | 1.48 | slot | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | worker-pool | 6000 250000 | 8 | 3 | 449 | 461 | 1.03 | 1.00 | 1.32 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | worker-pool | 6000 250000 | 8 | 3 | 449 | 473 | 1.05 | 1.00 | 1.29 | slot | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | spawn-many | 200000 | 1 | 3 | 110 | 174 | 1.59 | 1.62 | 2.71 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | spawn-many | 200000 | 1 | 3 | 110 | 162 | 1.48 | 1.50 | 2.70 | slot | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | spawn-many | 200000 | 8 | 3 | 29 | 409 | 14.12 | 19.33 | 9.01 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | spawn-many | 200000 | 8 | 3 | 29 | 419 | 14.44 | 20.43 | 9.01 | slot | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | coroutine-churn | 200000 | 1 | 3 | 189 | 131 | 0.70 | 0.68 | 1.46 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | coroutine-churn | 200000 | 1 | 3 | 189 | 133 | 0.70 | 0.69 | 1.46 | slot | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | coroutine-churn | 200000 | 8* | 3 | 170 | 131 | 0.77 | 0.76 | 1.46 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | coroutine-churn | 200000 | 8* | 3 | 170 | 132 | 0.78 | 0.76 | 1.46 | slot | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | shared-counter | 200000 | 1 | 3 | 36 | 687 | 18.89 | 19.32 | 215.74 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | shared-counter | 200000 | 1 | 3 | 36 | 572 | 15.71 | 15.93 | 162.11 | slot | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | shared-counter | 200000 | 8 | 3 | 1,084 | 1,849 | 1.71 | 1.90 | 158.32 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | clean | macos | shared-counter | 200000 | 8 | 3 | 1,084 | 1,441 | 1.33 | 1.42 | 118.25 | slot | arm64 A/B of R2-R4 (ref f561e2d8c, pre plain depth step / intrusive mailbox / Go park order) |
| 2026-10-15 | eb401fad2 | dirty | orb | thread-ring | 3000000 | 1 | 3 | 156 | 509 | 3.27 | 3.28 | 0.69 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | thread-ring | 3000000 | 1 | 3 | 156 | 368 | 2.36 | 2.36 | 0.70 | slot | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | thread-ring | 3000000 | 7 | 3 | 249 | 466 | 1.87 | 1.06 | 0.64 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | thread-ring | 3000000 | 7 | 3 | 249 | 378 | 1.52 | 0.85 | 0.71 | slot | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | ping-pong | 1000000 | 1 | 3 | 179 | 316 | 1.76 | 1.76 | 0.30 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | ping-pong | 1000000 | 1 | 3 | 179 | 229 | 1.28 | 1.28 | 0.30 | slot | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | ping-pong | 1000000 | 7 | 3 | 158 | 285 | 1.80 | 1.80 | 0.23 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | ping-pong | 1000000 | 7 | 3 | 158 | 232 | 1.47 | 1.46 | 0.23 | slot | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | fan-in | 100000 | 1 | 3 | 124 | 357 | 2.88 | 2.88 | 200.36 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | fan-in | 100000 | 1 | 3 | 124 | 179 | 1.44 | 1.45 | 150.53 | slot | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | fan-in | 100000 | 7 | 3 | 153 | 480 | 3.13 | 4.48 | 161.46 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | fan-in | 100000 | 7 | 3 | 153 | 350 | 2.28 | 4.79 | 115.77 | slot | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,414 | 2,406 | 1.00 | 1.00 | 1.81 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,414 | 2,415 | 1.00 | 1.00 | 1.76 | slot | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 412 | 516 | 1.25 | 0.95 | 1.66 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 412 | 618 | 1.50 | 0.95 | 1.62 | slot | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | spawn-many | 200000 | 1 | 3 | 105 | 158 | 1.50 | 1.50 | 3.01 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | spawn-many | 200000 | 1 | 3 | 105 | 141 | 1.34 | 1.33 | 3.00 | slot | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | spawn-many | 200000 | 7 | 3 | 54 | 148 | 2.74 | 3.33 | 2.50 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | spawn-many | 200000 | 7 | 3 | 54 | 110 | 2.03 | 2.48 | 2.42 | slot | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 168 | 107 | 0.64 | 0.64 | 1.61 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 168 | 111 | 0.66 | 0.66 | 1.61 | slot | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 188 | 106 | 0.56 | 0.57 | 1.58 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 188 | 109 | 0.58 | 0.58 | 1.58 | slot | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | shared-counter | 200000 | 1 | 3 | 33 | 714 | 21.85 | 21.89 | 372.09 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | shared-counter | 200000 | 1 | 3 | 33 | 354 | 10.82 | 10.84 | 279.43 | slot | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | shared-counter | 200000 | 7 | 3 | 417 | 1,025 | 2.46 | 2.81 | 304.45 | ref@f561e2d8c | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | eb401fad2 | dirty | orb | shared-counter | 200000 | 7 | 3 | 417 | 675 | 1.62 | 2.45 | 218.79 | slot | arm64 A/B of R2-R4 (ref f561e2d8c) |
| 2026-10-15 | bb5d6fd54 | clean | windows | thread-ring | 3000000 | 1 | 3 | 354 | 902 | 2.55 | 2.48 | 0.86 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | thread-ring | 3000000 | 1 | 3 | 354 | 990 | 2.80 | 2.83 | 0.86 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,318 | 1,448 | 1.10 | 0.20 | 0.81 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,318 | 1,643 | 1.25 | 0.23 | 0.81 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | ping-pong | 1000000 | 1 | 3 | 372 | 474 | 1.27 | 1.29 | 0.70 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | ping-pong | 1000000 | 1 | 3 | 372 | 508 | 1.37 | 1.33 | 0.70 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | ping-pong | 1000000 | 12 | 3 | 502 | 814 | 1.62 | 1.71 | 0.68 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | ping-pong | 1000000 | 12 | 3 | 502 | 882 | 1.76 | 1.86 | 0.68 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | fan-in | 100000 | 1 | 3 | 286 | 746 | 2.61 | 2.61 | 49.86 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | fan-in | 100000 | 1 | 3 | 286 | 705 | 2.47 | 2.50 | 50.06 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | fan-in | 100000 | 12 | 3 | 1,024 | 2,298 | 2.24 | 6.27 | 41.86 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | fan-in | 100000 | 12 | 3 | 1,024 | 2,041 | 1.99 | 5.58 | 39.95 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,128 | 2,131 | 1.00 | 1.00 | 1.27 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,128 | 2,129 | 1.00 | 1.00 | 1.27 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 226 | 235 | 1.04 | 1.08 | 1.14 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 226 | 265 | 1.18 | 1.09 | 1.14 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | spawn-many | 200000 | 1 | 3 | 245 | 1,104 | 4.50 | 4.38 | 6.67 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | spawn-many | 200000 | 1 | 3 | 245 | 1,115 | 4.55 | 4.50 | 6.72 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | spawn-many | 200000 | 12 | 3 | 58 | 399 | 6.93 | 9.33 | 6.21 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | spawn-many | 200000 | 12 | 3 | 58 | 256 | 4.44 | 6.11 | 6.20 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | coroutine-churn | 200000 | 1 | 3 | 376 | 914 | 2.43 | 2.52 | 4.30 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | coroutine-churn | 200000 | 1 | 3 | 376 | 928 | 2.47 | 2.57 | 4.30 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | coroutine-churn | 200000 | 12* | 3 | 365 | 925 | 2.53 | 2.57 | 4.36 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | coroutine-churn | 200000 | 12* | 3 | 365 | 923 | 2.53 | 2.57 | 4.36 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | shared-counter | 200000 | 1 | 3 | 160 | 1,473 | 9.20 | 9.30 | 98.23 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | shared-counter | 200000 | 1 | 3 | 160 | 1,372 | 8.56 | 8.80 | 98.58 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | shared-counter | 200000 | 12 | 3 | 570 | 4,500 | 7.90 | 13.28 | 81.33 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | windows | shared-counter | 200000 | 12 | 3 | 570 | 4,095 | 7.18 | 12.50 | 78.93 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 372 | 747 | 2.01 | 2.02 | 1.28 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 372 | 830 | 2.23 | 2.24 | 1.28 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,092 | 820 | 0.75 | 0.28 | 0.96 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,092 | 952 | 0.87 | 0.32 | 1.11 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 367 | 395 | 1.08 | 1.08 | 0.21 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 367 | 414 | 1.13 | 1.13 | 0.21 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 366 | 438 | 1.20 | 1.20 | 0.25 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 366 | 449 | 1.23 | 1.22 | 0.19 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | fan-in | 100000 | 1 | 3 | 276 | 699 | 2.53 | 2.52 | 148.81 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | fan-in | 100000 | 1 | 3 | 276 | 623 | 2.26 | 2.25 | 148.56 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | fan-in | 100000 | 12 | 3 | 685 | 1,396 | 2.04 | 4.81 | 122.89 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | fan-in | 100000 | 12 | 3 | 685 | 1,139 | 1.66 | 4.29 | 124.22 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,140 | 2,156 | 1.01 | 1.01 | 1.93 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,140 | 2,164 | 1.01 | 1.01 | 1.93 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 216 | 247 | 1.14 | 1.05 | 1.71 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 216 | 249 | 1.15 | 1.04 | 1.71 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | spawn-many | 200000 | 1 | 3 | 174 | 486 | 2.79 | 2.83 | 5.79 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | spawn-many | 200000 | 1 | 3 | 174 | 508 | 2.92 | 2.95 | 5.91 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | spawn-many | 200000 | 12 | 3 | 53 | 535 | 10.09 | 16.56 | 9.70 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | spawn-many | 200000 | 12 | 3 | 53 | 286 | 5.39 | 7.58 | 9.69 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 267 | 326 | 1.22 | 1.23 | 2.64 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 267 | 358 | 1.34 | 1.35 | 2.64 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 264 | 334 | 1.27 | 1.27 | 2.68 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 264 | 338 | 1.28 | 1.29 | 2.68 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 157 | 1,302 | 8.29 | 8.31 | 297.38 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 157 | 1,213 | 7.72 | 7.75 | 297.62 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 721 | 2,767 | 3.84 | 5.01 | 245.94 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 721 | 2,260 | 3.14 | 4.31 | 245.78 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | thread-ring | 3000000 | 1 | 3 | 154 | 457 | 2.98 | 2.99 | 0.77 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | thread-ring | 3000000 | 1 | 3 | 154 | 524 | 3.41 | 3.42 | 0.78 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | thread-ring | 3000000 | 8 | 3 | 699 | 799 | 1.14 | 0.43 | 0.71 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | thread-ring | 3000000 | 8 | 3 | 699 | 849 | 1.22 | 0.45 | 0.72 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | ping-pong | 1000000 | 1 | 3 | 180 | 277 | 1.54 | 1.54 | 0.59 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | ping-pong | 1000000 | 1 | 3 | 180 | 301 | 1.67 | 1.68 | 0.59 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | ping-pong | 1000000 | 8 | 3 | 202 | 518 | 2.56 | 2.49 | 0.58 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | ping-pong | 1000000 | 8 | 3 | 202 | 526 | 2.61 | 2.48 | 0.58 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | fan-in | 100000 | 1 | 3 | 126 | 287 | 2.28 | 2.26 | 80.66 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | fan-in | 100000 | 1 | 3 | 126 | 285 | 2.27 | 2.26 | 80.66 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | fan-in | 100000 | 8 | 3 | 429 | 696 | 1.62 | 3.08 | 59.51 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | fan-in | 100000 | 8 | 3 | 429 | 685 | 1.60 | 3.07 | 58.07 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | worker-pool | 6000 250000 | 1 | 3 | 2,227 | 2,345 | 1.05 | 1.05 | 1.48 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | worker-pool | 6000 250000 | 1 | 3 | 2,227 | 2,388 | 1.07 | 1.07 | 1.48 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | worker-pool | 6000 250000 | 8 | 3 | 556 | 570 | 1.03 | 1.00 | 1.28 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | worker-pool | 6000 250000 | 8 | 3 | 556 | 599 | 1.08 | 1.01 | 1.29 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | spawn-many | 200000 | 1 | 3 | 112 | 182 | 1.63 | 1.66 | 2.41 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | spawn-many | 200000 | 1 | 3 | 112 | 187 | 1.67 | 1.64 | 2.46 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | spawn-many | 200000 | 8 | 3 | 29 | 353 | 12.20 | 15.70 | 9.02 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | spawn-many | 200000 | 8 | 3 | 29 | 268 | 9.26 | 11.73 | 9.02 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | coroutine-churn | 200000 | 1 | 3 | 251 | 163 | 0.65 | 0.63 | 1.58 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | coroutine-churn | 200000 | 1 | 3 | 251 | 147 | 0.59 | 0.60 | 1.58 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | coroutine-churn | 200000 | 8* | 3 | 194 | 140 | 0.72 | 0.71 | 1.44 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | coroutine-churn | 200000 | 8* | 3 | 194 | 141 | 0.73 | 0.71 | 1.44 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | shared-counter | 200000 | 1 | 3 | 36 | 576 | 15.83 | 16.16 | 160.73 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | shared-counter | 200000 | 1 | 3 | 36 | 572 | 15.71 | 15.91 | 160.73 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | shared-counter | 200000 | 8 | 3 | 1,068 | 1,412 | 1.32 | 1.49 | 118.03 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | clean | macos | shared-counter | 200000 | 8 | 3 | 1,068 | 1,382 | 1.29 | 1.49 | 115.28 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | thread-ring | 3000000 | 1 | 3 | 155 | 365 | 2.35 | 2.35 | 0.69 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | thread-ring | 3000000 | 1 | 3 | 155 | 392 | 2.53 | 2.53 | 0.70 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | thread-ring | 3000000 | 7 | 3 | 241 | 374 | 1.55 | 0.88 | 0.59 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | thread-ring | 3000000 | 7 | 3 | 241 | 397 | 1.65 | 0.94 | 0.61 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | ping-pong | 1000000 | 1 | 3 | 180 | 229 | 1.28 | 1.28 | 0.20 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | ping-pong | 1000000 | 1 | 3 | 180 | 233 | 1.30 | 1.30 | 0.26 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | ping-pong | 1000000 | 7 | 3 | 159 | 234 | 1.47 | 1.46 | 0.24 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | ping-pong | 1000000 | 7 | 3 | 159 | 236 | 1.49 | 1.48 | 0.24 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | fan-in | 100000 | 1 | 3 | 124 | 179 | 1.45 | 1.45 | 140.13 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | fan-in | 100000 | 1 | 3 | 124 | 174 | 1.41 | 1.40 | 140.14 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | fan-in | 100000 | 7 | 3 | 144 | 357 | 2.47 | 5.46 | 114.17 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | fan-in | 100000 | 7 | 3 | 144 | 318 | 2.20 | 4.48 | 110.50 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,408 | 2,410 | 1.00 | 1.00 | 1.85 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,408 | 2,404 | 1.00 | 1.00 | 1.83 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 422 | 482 | 1.14 | 0.98 | 1.79 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 422 | 482 | 1.14 | 1.01 | 1.77 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | spawn-many | 200000 | 1 | 3 | 108 | 139 | 1.29 | 1.29 | 3.49 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | spawn-many | 200000 | 1 | 3 | 108 | 143 | 1.33 | 1.33 | 3.56 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | spawn-many | 200000 | 7 | 3 | 64 | 106 | 1.68 | 1.91 | 1.82 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | spawn-many | 200000 | 7 | 3 | 64 | 115 | 1.81 | 1.75 | 1.86 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 167 | 109 | 0.65 | 0.65 | 1.61 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 167 | 110 | 0.66 | 0.66 | 1.61 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 168 | 108 | 0.65 | 0.65 | 1.61 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 168 | 111 | 0.66 | 0.66 | 1.61 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | shared-counter | 200000 | 1 | 3 | 33 | 352 | 10.80 | 10.86 | 293.65 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | shared-counter | 200000 | 1 | 3 | 33 | 346 | 10.61 | 10.65 | 293.65 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | shared-counter | 200000 | 7 | 3 | 436 | 666 | 1.53 | 2.32 | 226.23 | ref@7346b44c7 | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | bb5d6fd54 | dirty | orb | shared-counter | 200000 | 7 | 3 | 436 | 618 | 1.42 | 1.97 | 216.92 | slot | R5: per-mailbox lock, spinning host locks, reply cell writes only its words |
| 2026-10-15 | 03577b04c | clean | windows | thread-ring | 3000000 | 1 | 3 | 345 | 878 | 2.54 | 2.55 | 0.86 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | thread-ring | 3000000 | 1 | 3 | 345 | 868 | 2.51 | 2.50 | 0.86 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,564 | 1,411 | 0.90 | 0.16 | 0.80 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,564 | 1,442 | 0.92 | 0.16 | 0.80 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | ping-pong | 1000000 | 1 | 3 | 363 | 471 | 1.30 | 1.25 | 0.70 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | ping-pong | 1000000 | 1 | 3 | 363 | 468 | 1.29 | 1.25 | 0.70 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | ping-pong | 1000000 | 12 | 3 | 500 | 810 | 1.62 | 1.69 | 0.70 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | ping-pong | 1000000 | 12 | 3 | 500 | 822 | 1.64 | 1.67 | 0.70 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | fan-in | 100000 | 1 | 3 | 277 | 738 | 2.67 | 2.61 | 49.86 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | fan-in | 100000 | 1 | 3 | 277 | 687 | 2.48 | 2.39 | 50.02 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | fan-in | 100000 | 12 | 3 | 1,014 | 2,199 | 2.17 | 6.46 | 41.78 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | fan-in | 100000 | 12 | 3 | 1,014 | 2,040 | 2.01 | 6.16 | 40.28 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,128 | 2,129 | 1.00 | 0.99 | 1.27 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,128 | 2,127 | 1.00 | 0.99 | 1.27 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 201 | 210 | 1.04 | 1.11 | 1.14 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 201 | 212 | 1.05 | 1.14 | 1.14 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | spawn-many | 200000 | 1 | 3 | 252 | 1,062 | 4.22 | 4.19 | 6.71 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | spawn-many | 200000 | 1 | 3 | 252 | 1,074 | 4.27 | 4.25 | 6.76 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | spawn-many | 200000 | 12 | 3 | 61 | 412 | 6.77 | 8.30 | 6.09 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | spawn-many | 200000 | 12 | 3 | 61 | 257 | 4.21 | 5.80 | 6.05 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | coroutine-churn | 200000 | 1 | 3 | 353 | 890 | 2.52 | 2.59 | 4.50 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | coroutine-churn | 200000 | 1 | 3 | 353 | 921 | 2.61 | 2.68 | 4.50 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | coroutine-churn | 200000 | 12* | 3 | 346 | 874 | 2.53 | 2.50 | 4.27 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | coroutine-churn | 200000 | 12* | 3 | 346 | 888 | 2.56 | 2.59 | 4.27 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | shared-counter | 200000 | 1 | 3 | 159 | 1,445 | 9.06 | 9.20 | 100.68 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | shared-counter | 200000 | 1 | 3 | 159 | 1,338 | 8.39 | 8.50 | 99.88 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | shared-counter | 200000 | 12 | 3 | 553 | 4,569 | 8.27 | 15.92 | 81.12 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | windows | shared-counter | 200000 | 12 | 3 | 553 | 3,912 | 7.08 | 13.67 | 80.98 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 355 | 688 | 1.94 | 1.94 | 1.28 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 355 | 725 | 2.04 | 2.05 | 1.28 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,031 | 767 | 0.74 | 0.27 | 1.00 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,031 | 796 | 0.77 | 0.28 | 1.11 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 363 | 372 | 1.03 | 1.03 | 0.21 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 363 | 392 | 1.08 | 1.08 | 0.21 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 357 | 422 | 1.18 | 1.19 | 0.19 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 357 | 431 | 1.21 | 1.21 | 0.19 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | fan-in | 100000 | 1 | 3 | 273 | 741 | 2.71 | 2.72 | 148.81 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | fan-in | 100000 | 1 | 3 | 273 | 601 | 2.20 | 2.20 | 148.75 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | fan-in | 100000 | 12 | 3 | 633 | 1,379 | 2.18 | 5.51 | 123.39 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | fan-in | 100000 | 12 | 3 | 633 | 1,176 | 1.86 | 5.29 | 122.33 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,128 | 2,132 | 1.00 | 1.00 | 1.93 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,128 | 2,131 | 1.00 | 1.00 | 1.93 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 198 | 208 | 1.05 | 1.04 | 1.71 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 198 | 208 | 1.05 | 1.04 | 1.71 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | spawn-many | 200000 | 1 | 3 | 166 | 467 | 2.82 | 2.84 | 5.50 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | spawn-many | 200000 | 1 | 3 | 166 | 484 | 2.92 | 2.97 | 5.61 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 472 | 9.57 | 16.82 | 10.21 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 268 | 5.43 | 7.73 | 10.21 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 257 | 319 | 1.24 | 1.25 | 2.62 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 257 | 340 | 1.32 | 1.33 | 2.62 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 255 | 315 | 1.23 | 1.24 | 2.52 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 255 | 325 | 1.27 | 1.29 | 2.52 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | shared-counter | 200000 | 1 | 3 | 155 | 1,251 | 8.05 | 8.08 | 297.56 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | shared-counter | 200000 | 1 | 3 | 155 | 1,229 | 7.91 | 7.93 | 297.50 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | shared-counter | 200000 | 12 | 3 | 709 | 2,772 | 3.91 | 5.32 | 245.72 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | wsl | shared-counter | 200000 | 12 | 3 | 709 | 2,363 | 3.33 | 4.92 | 244.78 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | thread-ring | 3000000 | 1 | 3 | 154 | 458 | 2.96 | 2.98 | 0.77 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | thread-ring | 3000000 | 1 | 3 | 154 | 483 | 3.13 | 3.14 | 0.79 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | thread-ring | 3000000 | 8 | 3 | 682 | 801 | 1.18 | 0.45 | 0.70 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | thread-ring | 3000000 | 8 | 3 | 682 | 736 | 1.08 | 0.40 | 0.73 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | ping-pong | 1000000 | 1 | 3 | 180 | 278 | 1.54 | 1.55 | 0.59 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | ping-pong | 1000000 | 1 | 3 | 180 | 290 | 1.61 | 1.62 | 0.59 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | ping-pong | 1000000 | 8 | 3 | 203 | 521 | 2.57 | 2.48 | 0.59 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | ping-pong | 1000000 | 8 | 3 | 203 | 516 | 2.55 | 2.48 | 0.60 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | fan-in | 100000 | 1 | 3 | 126 | 289 | 2.29 | 2.29 | 80.66 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | fan-in | 100000 | 1 | 3 | 126 | 288 | 2.29 | 2.29 | 80.66 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | fan-in | 100000 | 8 | 3 | 433 | 693 | 1.60 | 2.72 | 60.67 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | fan-in | 100000 | 8 | 3 | 433 | 672 | 1.55 | 2.58 | 58.96 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | worker-pool | 6000 250000 | 1 | 3 | 2,229 | 2,340 | 1.05 | 1.05 | 1.45 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | worker-pool | 6000 250000 | 1 | 3 | 2,229 | 2,388 | 1.07 | 1.07 | 1.45 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | worker-pool | 6000 250000 | 8 | 3 | 411 | 429 | 1.04 | 1.00 | 1.32 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | worker-pool | 6000 250000 | 8 | 3 | 411 | 479 | 1.17 | 1.00 | 1.32 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | spawn-many | 200000 | 1 | 3 | 109 | 168 | 1.54 | 1.52 | 2.45 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | spawn-many | 200000 | 1 | 3 | 109 | 174 | 1.60 | 1.58 | 2.50 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | spawn-many | 200000 | 8 | 3 | 29 | 416 | 14.42 | 20.52 | 8.83 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | spawn-many | 200000 | 8 | 3 | 29 | 304 | 10.56 | 14.37 | 8.83 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | coroutine-churn | 200000 | 1 | 3 | 190 | 135 | 0.71 | 0.71 | 1.41 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | coroutine-churn | 200000 | 1 | 3 | 190 | 137 | 0.72 | 0.71 | 1.41 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | coroutine-churn | 200000 | 8* | 3 | 190 | 134 | 0.70 | 0.69 | 1.41 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | coroutine-churn | 200000 | 8* | 3 | 190 | 137 | 0.72 | 0.70 | 1.41 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | shared-counter | 200000 | 1 | 3 | 36 | 575 | 15.77 | 16.12 | 162.12 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | shared-counter | 200000 | 1 | 3 | 36 | 569 | 15.59 | 15.87 | 162.11 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | shared-counter | 200000 | 8 | 3 | 1,078 | 1,406 | 1.30 | 1.52 | 118.35 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | clean | macos | shared-counter | 200000 | 8 | 3 | 1,078 | 1,369 | 1.27 | 1.48 | 117.57 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | thread-ring | 3000000 | 1 | 3 | 155 | 369 | 2.37 | 2.37 | 0.67 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | thread-ring | 3000000 | 1 | 3 | 155 | 386 | 2.48 | 2.49 | 0.68 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | thread-ring | 3000000 | 7 | 3 | 225 | 375 | 1.67 | 0.99 | 0.64 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | thread-ring | 3000000 | 7 | 3 | 225 | 386 | 1.72 | 1.03 | 0.64 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | ping-pong | 1000000 | 1 | 3 | 179 | 229 | 1.28 | 1.28 | 0.26 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | ping-pong | 1000000 | 1 | 3 | 179 | 229 | 1.27 | 1.28 | 0.26 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | ping-pong | 1000000 | 7 | 3 | 159 | 233 | 1.47 | 1.45 | 0.26 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | ping-pong | 1000000 | 7 | 3 | 159 | 234 | 1.47 | 1.46 | 0.26 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | fan-in | 100000 | 1 | 3 | 124 | 179 | 1.45 | 1.45 | 140.14 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | fan-in | 100000 | 1 | 3 | 124 | 176 | 1.42 | 1.42 | 140.13 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | fan-in | 100000 | 7 | 3 | 151 | 349 | 2.31 | 4.90 | 114.86 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | fan-in | 100000 | 7 | 3 | 151 | 307 | 2.03 | 3.67 | 112.99 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,259 | 2,352 | 1.04 | 1.04 | 1.81 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,259 | 2,408 | 1.07 | 1.07 | 1.84 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 410 | 530 | 1.29 | 0.97 | 1.77 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 410 | 516 | 1.26 | 0.97 | 1.72 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | spawn-many | 200000 | 1 | 3 | 108 | 137 | 1.27 | 1.27 | 3.51 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | spawn-many | 200000 | 1 | 3 | 108 | 144 | 1.33 | 1.34 | 3.58 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | spawn-many | 200000 | 7 | 3 | 53 | 104 | 1.98 | 2.17 | 2.53 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | spawn-many | 200000 | 7 | 3 | 53 | 110 | 2.09 | 2.13 | 2.73 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 167 | 109 | 0.65 | 0.65 | 1.67 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 167 | 108 | 0.65 | 0.65 | 1.67 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 165 | 106 | 0.64 | 0.64 | 1.58 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 165 | 109 | 0.66 | 0.66 | 1.58 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | shared-counter | 200000 | 1 | 3 | 32 | 352 | 10.85 | 10.86 | 291.37 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | shared-counter | 200000 | 1 | 3 | 32 | 349 | 10.74 | 10.74 | 291.37 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | shared-counter | 200000 | 7 | 3 | 439 | 687 | 1.57 | 2.26 | 226.23 | ref@7346b44c7 | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | 03577b04c | dirty | orb | shared-counter | 200000 | 7 | 3 | 439 | 642 | 1.46 | 1.94 | 216.77 | slot | R5+R5b vs pre-R5 (7346b44c7): per-mailbox lock, spinning locks, lean reply cell, lock-free parked-service ready |
| 2026-10-15 | a69e38248 | clean | windows | thread-ring | 3000000 | 1 | 3 | 339 | 873 | 2.57 | 2.67 | 0.86 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | thread-ring | 3000000 | 1 | 3 | 339 | 718 | 2.12 | 2.19 | 0.86 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,584 | 1,373 | 0.87 | 0.15 | 0.80 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,584 | 1,200 | 0.76 | 0.13 | 0.81 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | ping-pong | 1000000 | 1 | 3 | 362 | 470 | 1.30 | 1.30 | 0.70 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | ping-pong | 1000000 | 1 | 3 | 362 | 409 | 1.13 | 1.13 | 0.71 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | ping-pong | 1000000 | 12 | 3 | 498 | 789 | 1.59 | 1.57 | 0.70 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | ping-pong | 1000000 | 12 | 3 | 498 | 712 | 1.43 | 1.47 | 0.70 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | fan-in | 100000 | 1 | 3 | 274 | 730 | 2.66 | 2.76 | 49.90 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | fan-in | 100000 | 1 | 3 | 274 | 679 | 2.47 | 2.59 | 49.90 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | fan-in | 100000 | 12 | 3 | 1,007 | 2,287 | 2.27 | 7.07 | 40.98 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | fan-in | 100000 | 12 | 3 | 1,007 | 2,017 | 2.00 | 6.27 | 40.26 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,121 | 2,122 | 1.00 | 1.00 | 1.27 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,121 | 2,121 | 1.00 | 1.00 | 1.27 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 197 | 210 | 1.07 | 1.07 | 1.13 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 197 | 211 | 1.07 | 1.04 | 1.13 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | spawn-many | 200000 | 1 | 3 | 241 | 1,031 | 4.28 | 4.47 | 6.40 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | spawn-many | 200000 | 1 | 3 | 241 | 1,052 | 4.37 | 4.47 | 6.44 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | spawn-many | 200000 | 12 | 3 | 56 | 393 | 7.02 | 8.00 | 6.10 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | spawn-many | 200000 | 12 | 3 | 56 | 253 | 4.52 | 5.09 | 6.10 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | coroutine-churn | 200000 | 1 | 3 | 337 | 871 | 2.58 | 2.62 | 4.35 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | coroutine-churn | 200000 | 1 | 3 | 337 | 891 | 2.64 | 2.67 | 4.35 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | coroutine-churn | 200000 | 12* | 3 | 350 | 869 | 2.48 | 2.62 | 4.35 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | coroutine-churn | 200000 | 12* | 3 | 350 | 885 | 2.53 | 2.71 | 4.35 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | shared-counter | 200000 | 1 | 3 | 159 | 1,446 | 9.08 | 9.40 | 100.13 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | shared-counter | 200000 | 1 | 3 | 159 | 1,347 | 8.46 | 8.60 | 100.06 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | shared-counter | 200000 | 12 | 3 | 553 | 4,465 | 8.07 | 15.64 | 81.30 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | windows | shared-counter | 200000 | 12 | 3 | 553 | 4,030 | 7.28 | 14.26 | 79.81 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 356 | 696 | 1.96 | 1.96 | 1.28 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 356 | 630 | 1.77 | 1.77 | 1.28 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,031 | 760 | 0.74 | 0.27 | 1.04 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,031 | 688 | 0.67 | 0.25 | 1.11 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 367 | 372 | 1.01 | 1.01 | 0.21 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 367 | 357 | 0.97 | 0.97 | 0.21 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 358 | 423 | 1.18 | 1.19 | 0.19 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 358 | 396 | 1.11 | 1.11 | 0.25 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | fan-in | 100000 | 1 | 3 | 273 | 620 | 2.27 | 2.27 | 148.69 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | fan-in | 100000 | 1 | 3 | 273 | 600 | 2.20 | 2.20 | 148.75 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | fan-in | 100000 | 12 | 3 | 622 | 1,400 | 2.25 | 5.70 | 123.17 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | fan-in | 100000 | 12 | 3 | 622 | 1,164 | 1.87 | 5.28 | 122.50 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,128 | 2,137 | 1.00 | 1.00 | 1.93 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,128 | 2,130 | 1.00 | 1.00 | 1.93 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 193 | 229 | 1.18 | 1.05 | 1.71 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 193 | 213 | 1.10 | 1.07 | 1.71 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | spawn-many | 200000 | 1 | 3 | 166 | 475 | 2.86 | 2.87 | 5.55 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | spawn-many | 200000 | 1 | 3 | 166 | 483 | 2.90 | 2.93 | 5.66 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 474 | 9.62 | 16.91 | 9.94 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 276 | 5.60 | 8.02 | 9.95 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 242 | 314 | 1.30 | 1.30 | 2.87 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 242 | 323 | 1.34 | 1.35 | 2.87 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 240 | 337 | 1.40 | 1.42 | 2.89 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 240 | 325 | 1.36 | 1.37 | 2.89 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 155 | 1,267 | 8.17 | 8.19 | 297.31 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 155 | 1,247 | 8.03 | 8.07 | 296.62 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 695 | 2,748 | 3.96 | 5.23 | 248.89 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 695 | 2,265 | 3.26 | 4.33 | 247.00 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | thread-ring | 3000000 | 1 | 3 | 154 | 456 | 2.97 | 2.98 | 0.78 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | thread-ring | 3000000 | 1 | 3 | 154 | 416 | 2.71 | 2.70 | 0.80 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | thread-ring | 3000000 | 8 | 3 | 711 | 834 | 1.17 | 0.45 | 0.71 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | thread-ring | 3000000 | 8 | 3 | 711 | 669 | 0.94 | 0.35 | 0.73 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | ping-pong | 1000000 | 1 | 3 | 182 | 279 | 1.54 | 1.54 | 0.59 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | ping-pong | 1000000 | 1 | 3 | 182 | 265 | 1.46 | 1.46 | 0.59 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | ping-pong | 1000000 | 8 | 3 | 208 | 538 | 2.59 | 2.49 | 0.59 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | ping-pong | 1000000 | 8 | 3 | 208 | 495 | 2.38 | 2.31 | 0.58 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | fan-in | 100000 | 1 | 3 | 127 | 290 | 2.28 | 2.25 | 80.66 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | fan-in | 100000 | 1 | 3 | 127 | 288 | 2.27 | 2.26 | 80.66 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | fan-in | 100000 | 8 | 3 | 469 | 702 | 1.50 | 2.60 | 60.68 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | fan-in | 100000 | 8 | 3 | 469 | 687 | 1.47 | 2.59 | 59.19 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | worker-pool | 6000 250000 | 1 | 3 | 2,410 | 2,403 | 1.00 | 1.00 | 1.49 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | worker-pool | 6000 250000 | 1 | 3 | 2,410 | 2,405 | 1.00 | 1.00 | 1.49 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | worker-pool | 6000 250000 | 8 | 3 | 475 | 479 | 1.01 | 1.00 | 1.29 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | worker-pool | 6000 250000 | 8 | 3 | 475 | 529 | 1.11 | 1.00 | 1.29 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | spawn-many | 200000 | 1 | 3 | 116 | 171 | 1.47 | 1.48 | 2.86 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | spawn-many | 200000 | 1 | 3 | 116 | 174 | 1.50 | 1.52 | 2.92 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | spawn-many | 200000 | 8 | 3 | 44 | 444 | 10.07 | 15.53 | 8.06 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | spawn-many | 200000 | 8 | 3 | 44 | 321 | 7.28 | 11.68 | 7.59 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | coroutine-churn | 200000 | 1 | 3 | 226 | 180 | 0.80 | 0.76 | 1.59 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | coroutine-churn | 200000 | 1 | 3 | 226 | 171 | 0.76 | 0.75 | 1.59 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | coroutine-churn | 200000 | 8* | 3 | 208 | 148 | 0.71 | 0.69 | 1.45 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | coroutine-churn | 200000 | 8* | 3 | 208 | 150 | 0.72 | 0.71 | 1.45 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | shared-counter | 200000 | 1 | 3 | 37 | 583 | 15.97 | 16.42 | 162.12 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | shared-counter | 200000 | 1 | 3 | 37 | 576 | 15.77 | 16.00 | 162.11 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | shared-counter | 200000 | 8 | 3 | 1,064 | 1,423 | 1.34 | 1.46 | 116.96 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | clean | macos | shared-counter | 200000 | 8 | 3 | 1,064 | 1,376 | 1.29 | 1.41 | 113.83 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | thread-ring | 3000000 | 1 | 3 | 162 | 413 | 2.55 | 2.55 | 0.67 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | thread-ring | 3000000 | 1 | 3 | 162 | 366 | 2.26 | 2.27 | 0.67 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | thread-ring | 3000000 | 7 | 3 | 245 | 376 | 1.54 | 0.95 | 0.63 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | thread-ring | 3000000 | 7 | 3 | 245 | 364 | 1.48 | 0.91 | 0.62 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | ping-pong | 1000000 | 1 | 3 | 189 | 241 | 1.27 | 1.27 | 0.30 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | ping-pong | 1000000 | 1 | 3 | 189 | 230 | 1.21 | 1.21 | 0.31 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | ping-pong | 1000000 | 7 | 3 | 166 | 240 | 1.45 | 1.44 | 0.25 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | ping-pong | 1000000 | 7 | 3 | 166 | 227 | 1.37 | 1.35 | 0.24 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | fan-in | 100000 | 1 | 3 | 129 | 186 | 1.44 | 1.44 | 139.87 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | fan-in | 100000 | 1 | 3 | 129 | 179 | 1.38 | 1.38 | 139.88 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | fan-in | 100000 | 7 | 3 | 145 | 354 | 2.44 | 5.32 | 114.69 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | fan-in | 100000 | 7 | 3 | 145 | 332 | 2.29 | 4.78 | 112.45 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,409 | 2,414 | 1.00 | 1.00 | 1.96 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,409 | 2,411 | 1.00 | 1.00 | 1.96 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 450 | 496 | 1.10 | 1.00 | 1.77 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 450 | 521 | 1.16 | 1.01 | 1.78 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | spawn-many | 200000 | 1 | 3 | 114 | 149 | 1.30 | 1.30 | 3.39 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | spawn-many | 200000 | 1 | 3 | 114 | 151 | 1.32 | 1.32 | 3.45 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | spawn-many | 200000 | 7 | 3 | 74 | 133 | 1.79 | 1.92 | 2.18 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | spawn-many | 200000 | 7 | 3 | 74 | 138 | 1.86 | 1.89 | 2.34 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 183 | 116 | 0.64 | 0.64 | 1.82 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 183 | 116 | 0.63 | 0.63 | 1.82 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 215 | 118 | 0.55 | 0.55 | 1.70 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 215 | 118 | 0.55 | 0.55 | 1.70 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | shared-counter | 200000 | 1 | 3 | 33 | 358 | 10.91 | 10.96 | 292.50 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | shared-counter | 200000 | 1 | 3 | 33 | 348 | 10.60 | 10.64 | 292.51 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | shared-counter | 200000 | 7 | 3 | 454 | 694 | 1.53 | 2.30 | 224.72 | ref@7346b44c7 | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | a69e38248 | dirty | orb | shared-counter | 200000 | 7 | 3 | 454 | 685 | 1.51 | 2.07 | 213.78 | slot | R5+R5b+R5c vs pre-R5 (7346b44c7): mailbox lock held across the park, direct handoff |
| 2026-10-15 | e53136b60 | clean | windows | thread-ring | 3000000 | 1 | 3 | 343 | 726 | 2.12 | 2.09 | 0.86 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | thread-ring | 3000000 | 1 | 3 | 343 | 642 | 1.87 | 1.86 | 0.87 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,565 | 1,221 | 0.78 | 0.15 | 0.81 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,565 | 1,003 | 0.64 | 0.12 | 0.81 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | ping-pong | 1000000 | 1 | 3 | 364 | 412 | 1.13 | 1.13 | 0.71 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | ping-pong | 1000000 | 1 | 3 | 364 | 357 | 0.98 | 1.00 | 0.71 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | ping-pong | 1000000 | 12 | 3 | 492 | 721 | 1.47 | 1.60 | 0.70 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | ping-pong | 1000000 | 12 | 3 | 492 | 590 | 1.20 | 1.32 | 0.70 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | fan-in | 100000 | 1 | 3 | 287 | 695 | 2.42 | 2.32 | 49.86 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | fan-in | 100000 | 1 | 3 | 287 | 692 | 2.41 | 2.32 | 49.86 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | fan-in | 100000 | 12 | 3 | 1,004 | 2,029 | 2.02 | 6.73 | 40.65 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | fan-in | 100000 | 12 | 3 | 1,004 | 1,996 | 1.99 | 6.76 | 40.24 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,129 | 2,128 | 1.00 | 1.00 | 1.27 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,129 | 2,128 | 1.00 | 0.99 | 1.34 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 218 | 257 | 1.18 | 1.04 | 1.14 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 218 | 231 | 1.06 | 1.06 | 1.19 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | spawn-many | 200000 | 1 | 3 | 250 | 1,093 | 4.37 | 4.67 | 6.68 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | spawn-many | 200000 | 1 | 3 | 250 | 1,100 | 4.40 | 4.60 | 6.77 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | spawn-many | 200000 | 12 | 3 | 57 | 256 | 4.46 | 5.67 | 6.25 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | spawn-many | 200000 | 12 | 3 | 57 | 229 | 3.99 | 5.11 | 6.99 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | coroutine-churn | 200000 | 1 | 3 | 354 | 917 | 2.59 | 2.64 | 4.26 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | coroutine-churn | 200000 | 1 | 3 | 354 | 934 | 2.64 | 2.73 | 4.29 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | coroutine-churn | 200000 | 12* | 3 | 354 | 934 | 2.64 | 2.73 | 4.41 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | coroutine-churn | 200000 | 12* | 3 | 354 | 930 | 2.63 | 2.73 | 4.44 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | shared-counter | 200000 | 1 | 3 | 160 | 1,571 | 9.81 | 10.20 | 100.56 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | shared-counter | 200000 | 1 | 3 | 160 | 1,365 | 8.53 | 8.80 | 99.26 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | shared-counter | 200000 | 12 | 3 | 554 | 4,156 | 7.50 | 14.33 | 80.03 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | windows | shared-counter | 200000 | 12 | 3 | 554 | 4,019 | 7.25 | 14.11 | 81.25 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 364 | 630 | 1.73 | 1.74 | 1.28 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 364 | 571 | 1.57 | 1.57 | 1.28 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,045 | 700 | 0.67 | 0.25 | 1.07 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,045 | 631 | 0.60 | 0.22 | 1.07 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 368 | 361 | 0.98 | 0.98 | 0.21 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 368 | 322 | 0.88 | 0.88 | 0.21 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 357 | 398 | 1.11 | 1.11 | 0.27 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 357 | 359 | 1.01 | 1.01 | 0.20 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | fan-in | 100000 | 1 | 3 | 283 | 619 | 2.19 | 2.18 | 148.94 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | fan-in | 100000 | 1 | 3 | 283 | 607 | 2.14 | 2.14 | 148.81 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | fan-in | 100000 | 12 | 3 | 647 | 1,139 | 1.76 | 4.36 | 130.47 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | fan-in | 100000 | 12 | 3 | 647 | 1,159 | 1.79 | 5.04 | 129.65 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,136 | 2,139 | 1.00 | 1.00 | 1.93 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,136 | 2,142 | 1.00 | 1.00 | 2.13 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 200 | 222 | 1.11 | 1.05 | 1.71 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 200 | 226 | 1.13 | 1.06 | 1.94 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | spawn-many | 200000 | 1 | 3 | 172 | 491 | 2.86 | 2.91 | 5.86 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | spawn-many | 200000 | 1 | 3 | 172 | 508 | 2.96 | 3.01 | 6.08 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | spawn-many | 200000 | 12 | 3 | 52 | 286 | 5.53 | 7.90 | 10.08 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | spawn-many | 200000 | 12 | 3 | 52 | 247 | 4.77 | 5.94 | 11.32 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 283 | 333 | 1.18 | 1.18 | 2.71 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 283 | 349 | 1.24 | 1.23 | 2.77 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 260 | 337 | 1.30 | 1.30 | 2.62 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 260 | 351 | 1.35 | 1.35 | 2.68 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 156 | 1,250 | 8.01 | 8.00 | 297.38 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 156 | 1,201 | 7.69 | 7.72 | 297.50 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 717 | 2,257 | 3.15 | 4.29 | 246.33 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 717 | 2,319 | 3.24 | 4.68 | 245.06 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | thread-ring | 3000000 | 1 | 3 | 153 | 408 | 2.67 | 2.68 | 0.78 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | thread-ring | 3000000 | 1 | 3 | 153 | 345 | 2.26 | 2.26 | 0.80 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | thread-ring | 3000000 | 8 | 3 | 691 | 628 | 0.91 | 0.33 | 0.72 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | thread-ring | 3000000 | 8 | 3 | 691 | 441 | 0.64 | 0.24 | 0.74 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | ping-pong | 1000000 | 1 | 3 | 179 | 261 | 1.45 | 1.46 | 0.59 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | ping-pong | 1000000 | 1 | 3 | 179 | 235 | 1.31 | 1.31 | 0.59 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | ping-pong | 1000000 | 8 | 3 | 203 | 475 | 2.34 | 2.29 | 0.58 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | ping-pong | 1000000 | 8 | 3 | 203 | 330 | 1.63 | 1.61 | 0.60 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | fan-in | 100000 | 1 | 3 | 126 | 290 | 2.30 | 2.27 | 80.68 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | fan-in | 100000 | 1 | 3 | 126 | 288 | 2.28 | 2.25 | 80.68 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | fan-in | 100000 | 8 | 3 | 430 | 690 | 1.60 | 3.03 | 58.42 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | fan-in | 100000 | 8 | 3 | 430 | 683 | 1.59 | 3.05 | 58.23 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | worker-pool | 6000 250000 | 1 | 3 | 2,227 | 2,227 | 1.00 | 1.00 | 1.49 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | worker-pool | 6000 250000 | 1 | 3 | 2,227 | 2,228 | 1.00 | 1.00 | 1.59 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | worker-pool | 6000 250000 | 8 | 3 | 412 | 423 | 1.03 | 1.00 | 1.30 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | worker-pool | 6000 250000 | 8 | 3 | 412 | 426 | 1.03 | 1.00 | 1.39 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | spawn-many | 200000 | 1 | 3 | 113 | 178 | 1.58 | 1.58 | 2.46 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | spawn-many | 200000 | 1 | 3 | 113 | 185 | 1.64 | 1.66 | 2.56 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | spawn-many | 200000 | 8 | 3 | 29 | 305 | 10.57 | 14.47 | 8.82 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | spawn-many | 200000 | 8 | 3 | 29 | 181 | 6.27 | 7.18 | 9.88 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | coroutine-churn | 200000 | 1 | 3 | 194 | 139 | 0.72 | 0.70 | 1.41 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | coroutine-churn | 200000 | 1 | 3 | 194 | 152 | 0.78 | 0.76 | 1.44 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | coroutine-churn | 200000 | 8* | 3 | 193 | 139 | 0.72 | 0.70 | 1.36 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | coroutine-churn | 200000 | 8* | 3 | 193 | 151 | 0.78 | 0.77 | 1.39 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | shared-counter | 200000 | 1 | 3 | 36 | 570 | 15.65 | 15.90 | 162.13 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | shared-counter | 200000 | 1 | 3 | 36 | 574 | 15.76 | 16.19 | 162.12 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | shared-counter | 200000 | 8 | 3 | 1,078 | 1,374 | 1.27 | 1.47 | 113.73 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | clean | macos | shared-counter | 200000 | 8 | 3 | 1,078 | 1,370 | 1.27 | 1.48 | 112.89 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | thread-ring | 3000000 | 1 | 3 | 156 | 355 | 2.28 | 2.28 | 0.71 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | thread-ring | 3000000 | 1 | 3 | 156 | 319 | 2.05 | 2.06 | 0.74 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | thread-ring | 3000000 | 7 | 3 | 237 | 360 | 1.52 | 0.90 | 0.61 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | thread-ring | 3000000 | 7 | 3 | 237 | 323 | 1.37 | 0.80 | 0.60 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | ping-pong | 1000000 | 1 | 3 | 179 | 221 | 1.23 | 1.23 | 0.26 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | ping-pong | 1000000 | 1 | 3 | 179 | 207 | 1.16 | 1.16 | 0.26 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | ping-pong | 1000000 | 7 | 3 | 159 | 225 | 1.42 | 1.41 | 0.26 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | ping-pong | 1000000 | 7 | 3 | 159 | 211 | 1.32 | 1.32 | 0.26 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | fan-in | 100000 | 1 | 3 | 124 | 175 | 1.41 | 1.42 | 139.61 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | fan-in | 100000 | 1 | 3 | 124 | 173 | 1.40 | 1.40 | 139.61 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | fan-in | 100000 | 7 | 3 | 153 | 326 | 2.14 | 4.23 | 106.71 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | fan-in | 100000 | 7 | 3 | 153 | 338 | 2.22 | 4.32 | 105.83 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,249 | 2,406 | 1.07 | 1.07 | 1.78 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,249 | 2,403 | 1.07 | 1.07 | 1.96 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 415 | 461 | 1.11 | 0.98 | 1.63 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 415 | 556 | 1.34 | 0.95 | 1.74 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | spawn-many | 200000 | 1 | 3 | 111 | 143 | 1.30 | 1.29 | 3.75 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | spawn-many | 200000 | 1 | 3 | 111 | 146 | 1.32 | 1.32 | 3.89 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | spawn-many | 200000 | 7 | 3 | 52 | 111 | 2.16 | 2.45 | 2.25 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | spawn-many | 200000 | 7 | 3 | 52 | 91 | 1.77 | 2.17 | 2.38 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 194 | 111 | 0.57 | 0.57 | 1.55 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 194 | 119 | 0.61 | 0.61 | 1.58 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 193 | 112 | 0.58 | 0.58 | 1.55 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 193 | 119 | 0.62 | 0.62 | 1.59 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | shared-counter | 200000 | 1 | 3 | 32 | 343 | 10.57 | 10.59 | 291.94 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | shared-counter | 200000 | 1 | 3 | 32 | 343 | 10.57 | 10.61 | 291.93 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | shared-counter | 200000 | 7 | 3 | 458 | 647 | 1.41 | 1.95 | 221.35 | ref@5ad514a16 | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-15 | e53136b60 | dirty | orb | shared-counter | 200000 | 7 | 3 | 458 | 683 | 1.49 | 2.12 | 215.11 | slot | R6 stage 1: Go lock structure on the message/spawn/await path |
| 2026-10-16 | 971e5932d | clean | windows | thread-ring | 3000000 | 1 | 3 | 338 | 638 | 1.89 | 1.86 | 0.87 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | thread-ring | 3000000 | 1 | 3 | 338 | 567 | 1.68 | 1.64 | 0.86 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,530 | 967 | 0.63 | 0.13 | 0.81 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,530 | 858 | 0.56 | 0.10 | 0.81 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | ping-pong | 1000000 | 1 | 3 | 361 | 352 | 0.97 | 1.00 | 0.70 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | ping-pong | 1000000 | 1 | 3 | 361 | 302 | 0.84 | 0.83 | 0.70 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | ping-pong | 1000000 | 12 | 3 | 488 | 588 | 1.20 | 1.18 | 0.69 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | ping-pong | 1000000 | 12 | 3 | 488 | 525 | 1.08 | 1.04 | 0.70 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | fan-in | 100000 | 1 | 3 | 276 | 683 | 2.47 | 2.59 | 50.05 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | fan-in | 100000 | 1 | 3 | 276 | 452 | 1.64 | 1.71 | 49.83 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | fan-in | 100000 | 12 | 3 | 1,027 | 2,013 | 1.96 | 6.13 | 41.42 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | fan-in | 100000 | 12 | 3 | 1,027 | 1,310 | 1.27 | 2.17 | 41.40 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,122 | 2,123 | 1.00 | 1.01 | 1.33 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,122 | 2,123 | 1.00 | 1.01 | 1.29 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 198 | 206 | 1.04 | 1.06 | 1.17 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 198 | 205 | 1.03 | 1.02 | 1.13 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | spawn-many | 200000 | 1 | 3 | 248 | 1,061 | 4.27 | 4.19 | 6.80 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | spawn-many | 200000 | 1 | 3 | 248 | 1,002 | 4.04 | 4.00 | 6.70 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | spawn-many | 200000 | 12 | 3 | 56 | 224 | 3.99 | 4.90 | 6.93 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | spawn-many | 200000 | 12 | 3 | 56 | 212 | 3.76 | 4.00 | 6.45 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | coroutine-churn | 200000 | 1 | 3 | 339 | 902 | 2.66 | 2.59 | 4.41 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | coroutine-churn | 200000 | 1 | 3 | 339 | 868 | 2.56 | 2.50 | 4.39 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | coroutine-churn | 200000 | 12* | 3 | 337 | 908 | 2.69 | 2.76 | 4.42 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | coroutine-churn | 200000 | 12* | 3 | 337 | 879 | 2.61 | 2.67 | 4.40 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | shared-counter | 200000 | 1 | 3 | 160 | 1,263 | 7.87 | 8.10 | 100.17 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | shared-counter | 200000 | 1 | 3 | 160 | 900 | 5.61 | 5.80 | 99.13 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | shared-counter | 200000 | 12 | 3 | 551 | 3,958 | 7.19 | 13.38 | 80.12 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | windows | shared-counter | 200000 | 12 | 3 | 551 | 2,559 | 4.65 | 4.76 | 82.34 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 355 | 555 | 1.56 | 1.57 | 1.28 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 355 | 547 | 1.54 | 1.54 | 1.32 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,036 | 613 | 0.59 | 0.22 | 1.11 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,036 | 624 | 0.60 | 0.22 | 1.11 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 366 | 322 | 0.88 | 0.88 | 0.21 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 366 | 306 | 0.83 | 0.83 | 0.21 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 359 | 361 | 1.01 | 1.01 | 0.19 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 359 | 343 | 0.96 | 0.96 | 0.19 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | fan-in | 100000 | 1 | 3 | 272 | 594 | 2.18 | 2.17 | 148.81 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | fan-in | 100000 | 1 | 3 | 272 | 575 | 2.11 | 2.11 | 148.75 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | fan-in | 100000 | 12 | 3 | 637 | 1,140 | 1.79 | 4.41 | 123.61 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | fan-in | 100000 | 12 | 3 | 637 | 1,140 | 1.79 | 3.66 | 121.22 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,123 | 2,136 | 1.01 | 1.01 | 2.13 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,123 | 2,136 | 1.01 | 1.01 | 2.00 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 194 | 224 | 1.15 | 1.05 | 1.88 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 194 | 212 | 1.09 | 1.08 | 1.82 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | spawn-many | 200000 | 1 | 3 | 172 | 487 | 2.84 | 2.88 | 5.87 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | spawn-many | 200000 | 1 | 3 | 172 | 471 | 2.74 | 2.77 | 5.64 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 235 | 4.76 | 6.03 | 11.09 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 247 | 5.01 | 6.22 | 10.30 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 256 | 341 | 1.33 | 1.35 | 2.86 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 256 | 339 | 1.32 | 1.34 | 2.82 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 256 | 375 | 1.46 | 1.47 | 2.88 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 256 | 330 | 1.29 | 1.30 | 2.83 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | shared-counter | 200000 | 1 | 3 | 156 | 1,181 | 7.60 | 7.63 | 297.31 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | shared-counter | 200000 | 1 | 3 | 156 | 1,077 | 6.93 | 6.94 | 297.38 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | shared-counter | 200000 | 12 | 3 | 705 | 2,309 | 3.27 | 4.61 | 244.50 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | wsl | shared-counter | 200000 | 12 | 3 | 705 | 2,288 | 3.24 | 3.45 | 241.72 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | thread-ring | 3000000 | 1 | 3 | 156 | 349 | 2.24 | 2.25 | 0.80 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | thread-ring | 3000000 | 1 | 3 | 156 | 313 | 2.01 | 2.02 | 0.79 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | thread-ring | 3000000 | 8 | 3 | 696 | 435 | 0.63 | 0.23 | 0.73 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | thread-ring | 3000000 | 8 | 3 | 696 | 399 | 0.57 | 0.21 | 1.04 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | ping-pong | 1000000 | 1 | 3 | 178 | 234 | 1.31 | 1.31 | 0.59 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | ping-pong | 1000000 | 1 | 3 | 178 | 198 | 1.11 | 1.11 | 0.60 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | ping-pong | 1000000 | 8 | 3 | 204 | 331 | 1.62 | 1.60 | 0.58 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | ping-pong | 1000000 | 8 | 3 | 204 | 269 | 1.32 | 1.32 | 0.59 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | fan-in | 100000 | 1 | 3 | 126 | 284 | 2.26 | 2.26 | 80.67 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | fan-in | 100000 | 1 | 3 | 126 | 180 | 1.43 | 1.39 | 80.66 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | fan-in | 100000 | 8 | 3 | 434 | 682 | 1.57 | 2.99 | 58.21 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | fan-in | 100000 | 8 | 3 | 434 | 535 | 1.23 | 1.81 | 60.32 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | worker-pool | 6000 250000 | 1 | 3 | 2,226 | 2,233 | 1.00 | 1.00 | 1.58 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | worker-pool | 6000 250000 | 1 | 3 | 2,226 | 2,235 | 1.00 | 1.00 | 1.52 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | worker-pool | 6000 250000 | 8 | 3 | 410 | 424 | 1.03 | 1.00 | 1.37 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | worker-pool | 6000 250000 | 8 | 3 | 410 | 428 | 1.04 | 1.00 | 1.31 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | spawn-many | 200000 | 1 | 3 | 110 | 178 | 1.62 | 1.61 | 2.45 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | spawn-many | 200000 | 1 | 3 | 110 | 142 | 1.29 | 1.25 | 2.36 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | spawn-many | 200000 | 8 | 3 | 29 | 181 | 6.16 | 7.06 | 9.85 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | spawn-many | 200000 | 8 | 3 | 29 | 148 | 5.05 | 4.99 | 9.17 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | coroutine-churn | 200000 | 1 | 3 | 189 | 143 | 0.76 | 0.77 | 1.43 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | coroutine-churn | 200000 | 1 | 3 | 189 | 112 | 0.60 | 0.58 | 1.41 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | coroutine-churn | 200000 | 8* | 3 | 189 | 141 | 0.75 | 0.73 | 1.43 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | coroutine-churn | 200000 | 8* | 3 | 189 | 113 | 0.60 | 0.60 | 1.40 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | shared-counter | 200000 | 1 | 3 | 36 | 566 | 15.58 | 15.93 | 162.83 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | shared-counter | 200000 | 1 | 3 | 36 | 357 | 9.82 | 9.98 | 162.81 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | shared-counter | 200000 | 8 | 3 | 1,049 | 1,368 | 1.30 | 1.49 | 115.63 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | clean | macos | shared-counter | 200000 | 8 | 3 | 1,049 | 1,063 | 1.01 | 0.93 | 119.71 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | thread-ring | 3000000 | 1 | 3 | 156 | 315 | 2.02 | 2.03 | 0.71 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | thread-ring | 3000000 | 1 | 3 | 156 | 311 | 2.00 | 2.01 | 0.67 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | thread-ring | 3000000 | 7 | 3 | 233 | 322 | 1.38 | 0.81 | 0.62 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | thread-ring | 3000000 | 7 | 3 | 233 | 324 | 1.39 | 0.82 | 0.62 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | ping-pong | 1000000 | 1 | 3 | 178 | 208 | 1.17 | 1.17 | 0.27 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | ping-pong | 1000000 | 1 | 3 | 178 | 200 | 1.12 | 1.12 | 0.28 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | ping-pong | 1000000 | 7 | 3 | 160 | 213 | 1.33 | 1.31 | 0.26 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | ping-pong | 1000000 | 7 | 3 | 160 | 205 | 1.28 | 1.27 | 0.25 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | fan-in | 100000 | 1 | 3 | 124 | 174 | 1.41 | 1.41 | 146.13 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | fan-in | 100000 | 1 | 3 | 124 | 171 | 1.38 | 1.38 | 146.13 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | fan-in | 100000 | 7 | 3 | 155 | 335 | 2.16 | 4.39 | 116.34 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | fan-in | 100000 | 7 | 3 | 155 | 390 | 2.51 | 4.65 | 117.13 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,254 | 2,333 | 1.03 | 1.04 | 2.16 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,254 | 2,409 | 1.07 | 1.07 | 2.05 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 506 | 527 | 1.04 | 0.90 | 1.83 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 506 | 644 | 1.27 | 0.91 | 1.73 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | spawn-many | 200000 | 1 | 3 | 109 | 149 | 1.37 | 1.38 | 3.78 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | spawn-many | 200000 | 1 | 3 | 109 | 140 | 1.29 | 1.29 | 3.64 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | spawn-many | 200000 | 7 | 3 | 59 | 88 | 1.49 | 1.82 | 2.01 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | spawn-many | 200000 | 7 | 3 | 59 | 94 | 1.60 | 1.84 | 1.91 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 173 | 119 | 0.69 | 0.69 | 1.62 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 173 | 111 | 0.64 | 0.64 | 1.60 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 168 | 118 | 0.70 | 0.70 | 1.65 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 168 | 110 | 0.65 | 0.65 | 1.62 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | shared-counter | 200000 | 1 | 3 | 33 | 344 | 10.58 | 10.60 | 293.08 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | shared-counter | 200000 | 1 | 3 | 33 | 340 | 10.47 | 10.47 | 293.07 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | shared-counter | 200000 | 7 | 3 | 447 | 647 | 1.45 | 1.96 | 217.93 | ref@daf7f1172 | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 971e5932d | dirty | orb | shared-counter | 200000 | 7 | 3 | 447 | 758 | 1.70 | 2.32 | 222.57 | slot | R7 change 1: Go lock_spinbit runtime locks |
| 2026-10-16 | 355305c2c | dirty | macos | thread-ring | 3000000 | 1 | 3 | 153 | 309 | 2.02 | 2.02 | 0.82 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | thread-ring | 3000000 | 1 | 3 | 153 | 298 | 1.95 | 1.95 | 0.79 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | thread-ring | 3000000 | 8 | 3 | 690 | 398 | 0.58 | 0.21 | 1.03 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | thread-ring | 3000000 | 8 | 3 | 690 | 373 | 0.54 | 0.20 | 1.02 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | ping-pong | 1000000 | 1 | 3 | 181 | 200 | 1.10 | 1.10 | 0.59 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | ping-pong | 1000000 | 1 | 3 | 181 | 193 | 1.07 | 1.07 | 0.58 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | ping-pong | 1000000 | 8 | 3 | 203 | 271 | 1.34 | 1.33 | 0.59 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | ping-pong | 1000000 | 8 | 3 | 203 | 256 | 1.27 | 1.24 | 0.59 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | fan-in | 100000 | 1 | 3 | 126 | 183 | 1.45 | 1.44 | 80.66 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | fan-in | 100000 | 1 | 3 | 126 | 161 | 1.28 | 1.25 | 80.66 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | fan-in | 100000 | 8 | 3 | 438 | 513 | 1.17 | 1.67 | 59.97 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | fan-in | 100000 | 8 | 3 | 438 | 228 | 0.52 | 0.38 | 56.01 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | worker-pool | 6000 250000 | 1 | 3 | 2,233 | 2,230 | 1.00 | 1.00 | 1.52 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | worker-pool | 6000 250000 | 1 | 3 | 2,233 | 2,230 | 1.00 | 1.00 | 1.52 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | worker-pool | 6000 250000 | 8 | 3 | 482 | 490 | 1.02 | 1.01 | 1.34 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | worker-pool | 6000 250000 | 8 | 3 | 482 | 503 | 1.04 | 1.00 | 1.33 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | spawn-many | 200000 | 1 | 3 | 113 | 152 | 1.34 | 1.30 | 2.43 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | spawn-many | 200000 | 1 | 3 | 113 | 142 | 1.25 | 1.28 | 2.43 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | spawn-many | 200000 | 8 | 3 | 29 | 148 | 5.09 | 5.05 | 9.64 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | spawn-many | 200000 | 8 | 3 | 29 | 130 | 4.47 | 4.50 | 9.65 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | coroutine-churn | 200000 | 1 | 3 | 191 | 121 | 0.63 | 0.62 | 1.44 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | coroutine-churn | 200000 | 1 | 3 | 191 | 130 | 0.68 | 0.66 | 1.44 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | coroutine-churn | 200000 | 8* | 3 | 191 | 122 | 0.64 | 0.62 | 1.45 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | coroutine-churn | 200000 | 8* | 3 | 191 | 130 | 0.68 | 0.66 | 1.45 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | shared-counter | 200000 | 1 | 3 | 37 | 363 | 9.94 | 10.21 | 162.12 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | shared-counter | 200000 | 1 | 3 | 37 | 319 | 8.74 | 8.81 | 162.13 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | shared-counter | 200000 | 8 | 3 | 1,067 | 1,061 | 0.99 | 0.88 | 120.97 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | macos | shared-counter | 200000 | 8 | 3 | 1,067 | 458 | 0.43 | 0.20 | 113.71 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | thread-ring | 3000000 | 1 | 3 | 157 | 317 | 2.01 | 2.02 | 0.67 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | thread-ring | 3000000 | 1 | 3 | 157 | 313 | 1.99 | 1.99 | 0.71 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | thread-ring | 3000000 | 7 | 3 | 226 | 326 | 1.44 | 0.87 | 0.67 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | thread-ring | 3000000 | 7 | 3 | 226 | 319 | 1.41 | 0.86 | 0.69 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | ping-pong | 1000000 | 1 | 3 | 181 | 203 | 1.12 | 1.12 | 0.26 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | ping-pong | 1000000 | 1 | 3 | 181 | 200 | 1.11 | 1.11 | 0.26 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | ping-pong | 1000000 | 7 | 3 | 160 | 205 | 1.28 | 1.27 | 0.25 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | ping-pong | 1000000 | 7 | 3 | 160 | 206 | 1.29 | 1.27 | 0.24 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | fan-in | 100000 | 1 | 3 | 124 | 171 | 1.38 | 1.38 | 146.99 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | fan-in | 100000 | 1 | 3 | 124 | 155 | 1.26 | 1.26 | 147.05 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | fan-in | 100000 | 7 | 3 | 142 | 389 | 2.73 | 5.38 | 117.19 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | fan-in | 100000 | 7 | 3 | 142 | 157 | 1.10 | 0.99 | 105.55 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,306 | 2,272 | 0.99 | 0.99 | 1.91 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,306 | 2,358 | 1.02 | 1.02 | 1.95 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 413 | 572 | 1.38 | 0.94 | 1.70 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 413 | 421 | 1.02 | 0.99 | 1.80 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | spawn-many | 200000 | 1 | 3 | 108 | 140 | 1.29 | 1.29 | 3.60 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | spawn-many | 200000 | 1 | 3 | 108 | 133 | 1.23 | 1.23 | 3.60 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | spawn-many | 200000 | 7 | 3 | 57 | 90 | 1.59 | 1.97 | 2.59 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | spawn-many | 200000 | 7 | 3 | 57 | 103 | 1.81 | 1.70 | 2.99 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 196 | 111 | 0.57 | 0.57 | 1.57 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 196 | 117 | 0.60 | 0.60 | 1.57 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 168 | 110 | 0.66 | 0.66 | 1.64 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 168 | 120 | 0.72 | 0.72 | 1.64 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | shared-counter | 200000 | 1 | 3 | 33 | 340 | 10.37 | 10.40 | 292.50 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | shared-counter | 200000 | 1 | 3 | 33 | 309 | 9.42 | 9.48 | 292.57 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | shared-counter | 200000 | 7 | 3 | 453 | 782 | 1.73 | 2.34 | 222.36 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 355305c2c | dirty | orb | shared-counter | 200000 | 7 | 3 | 453 | 312 | 0.69 | 0.41 | 203.64 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | thread-ring | 3000000 | 1 | 3 | 340 | 565 | 1.66 | 1.64 | 0.86 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | thread-ring | 3000000 | 1 | 3 | 340 | 556 | 1.63 | 1.59 | 0.86 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | thread-ring | 3000000 | 12 | 3 | 1,570 | 853 | 0.54 | 0.10 | 0.80 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | thread-ring | 3000000 | 12 | 3 | 1,570 | 855 | 0.54 | 0.09 | 0.80 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | ping-pong | 1000000 | 1 | 3 | 360 | 302 | 0.84 | 0.83 | 0.70 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | ping-pong | 1000000 | 1 | 3 | 360 | 300 | 0.83 | 0.87 | 0.70 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | ping-pong | 1000000 | 12 | 3 | 483 | 529 | 1.10 | 1.12 | 0.70 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | ping-pong | 1000000 | 12 | 3 | 483 | 528 | 1.09 | 1.10 | 0.69 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | fan-in | 100000 | 1 | 3 | 275 | 453 | 1.65 | 1.61 | 49.86 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | fan-in | 100000 | 1 | 3 | 275 | 468 | 1.70 | 1.67 | 50.23 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | fan-in | 100000 | 12 | 3 | 967 | 1,301 | 1.35 | 2.44 | 42.10 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | fan-in | 100000 | 12 | 3 | 967 | 764 | 0.79 | 0.99 | 42.02 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | worker-pool | 6000 250000 | 1 | 3 | 2,121 | 2,121 | 1.00 | 0.99 | 1.29 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | worker-pool | 6000 250000 | 1 | 3 | 2,121 | 2,118 | 1.00 | 1.00 | 1.29 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | worker-pool | 6000 250000 | 12 | 3 | 198 | 212 | 1.07 | 1.04 | 1.13 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | worker-pool | 6000 250000 | 12 | 3 | 198 | 209 | 1.05 | 1.06 | 1.13 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | spawn-many | 200000 | 1 | 3 | 249 | 1,032 | 4.14 | 3.94 | 6.59 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | spawn-many | 200000 | 1 | 3 | 249 | 1,018 | 4.09 | 4.06 | 6.59 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | spawn-many | 200000 | 12 | 3 | 57 | 211 | 3.74 | 3.58 | 6.39 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | spawn-many | 200000 | 12 | 3 | 57 | 210 | 3.71 | 3.08 | 6.38 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | coroutine-churn | 200000 | 1 | 3 | 344 | 899 | 2.61 | 2.64 | 4.47 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | coroutine-churn | 200000 | 1 | 3 | 344 | 902 | 2.62 | 2.64 | 4.47 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | coroutine-churn | 200000 | 12* | 3 | 344 | 914 | 2.66 | 2.55 | 4.38 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | coroutine-churn | 200000 | 12* | 3 | 344 | 898 | 2.61 | 2.64 | 4.38 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | shared-counter | 200000 | 1 | 3 | 162 | 920 | 5.67 | 5.80 | 99.19 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | shared-counter | 200000 | 1 | 3 | 162 | 931 | 5.73 | 5.80 | 100.10 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | shared-counter | 200000 | 12 | 3 | 573 | 2,653 | 4.63 | 4.84 | 82.23 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | windows | shared-counter | 200000 | 12 | 3 | 573 | 1,551 | 2.71 | 2.06 | 82.37 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 356 | 557 | 1.57 | 1.57 | 1.32 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 356 | 551 | 1.55 | 1.55 | 1.32 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,037 | 605 | 0.58 | 0.21 | 1.07 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,037 | 606 | 0.58 | 0.21 | 1.11 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 364 | 303 | 0.83 | 0.83 | 0.21 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 364 | 299 | 0.82 | 0.82 | 0.21 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 359 | 343 | 0.95 | 0.96 | 0.19 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 359 | 342 | 0.95 | 0.95 | 0.25 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | fan-in | 100000 | 1 | 3 | 273 | 541 | 1.98 | 1.97 | 148.81 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | fan-in | 100000 | 1 | 3 | 273 | 559 | 2.05 | 2.04 | 148.62 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | fan-in | 100000 | 12 | 3 | 669 | 1,136 | 1.70 | 3.49 | 130.06 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | fan-in | 100000 | 12 | 3 | 669 | 837 | 1.25 | 2.29 | 130.41 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,131 | 2,130 | 1.00 | 1.00 | 2.00 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,131 | 2,139 | 1.00 | 1.00 | 2.00 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 192 | 210 | 1.10 | 1.05 | 1.82 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 192 | 221 | 1.15 | 1.06 | 1.82 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | spawn-many | 200000 | 1 | 3 | 171 | 490 | 2.87 | 2.90 | 5.69 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | spawn-many | 200000 | 1 | 3 | 171 | 472 | 2.76 | 2.78 | 5.69 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | spawn-many | 200000 | 12 | 3 | 50 | 252 | 5.00 | 5.97 | 10.42 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | spawn-many | 200000 | 12 | 3 | 50 | 236 | 4.70 | 5.39 | 10.43 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 251 | 331 | 1.32 | 1.32 | 2.75 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 251 | 348 | 1.38 | 1.38 | 2.75 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 250 | 339 | 1.36 | 1.36 | 2.73 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 250 | 333 | 1.33 | 1.34 | 2.73 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | shared-counter | 200000 | 1 | 3 | 155 | 1,073 | 6.94 | 6.97 | 297.50 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | shared-counter | 200000 | 1 | 3 | 155 | 1,113 | 7.20 | 7.22 | 297.56 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | shared-counter | 200000 | 12 | 3 | 725 | 2,258 | 3.11 | 3.19 | 243.17 | ref@971e5932d | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | e574e973a | dirty | wsl | shared-counter | 200000 | 12 | 3 | 725 | 1,656 | 2.28 | 2.09 | 247.56 | slot | R7b: Go-exact lock (procyield, Xchg8, LSE, event word) |
| 2026-10-16 | 8ae57740b | dirty | macos | thread-ring | 3000000 | 1 | 3 | 154 | 296 | 1.93 | 1.93 | 0.78 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | thread-ring | 3000000 | 1 | 3 | 154 | 295 | 1.92 | 1.92 | 1.11 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | thread-ring | 3000000 | 8 | 3 | 692 | 371 | 0.54 | 0.20 | 1.04 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | thread-ring | 3000000 | 8 | 3 | 692 | 371 | 0.54 | 0.20 | 1.02 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | ping-pong | 1000000 | 1 | 3 | 180 | 191 | 1.06 | 1.06 | 0.60 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | ping-pong | 1000000 | 1 | 3 | 180 | 194 | 1.08 | 1.08 | 0.60 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | ping-pong | 1000000 | 8 | 3 | 204 | 253 | 1.24 | 1.22 | 0.59 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | ping-pong | 1000000 | 8 | 3 | 204 | 254 | 1.25 | 1.22 | 0.60 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | fan-in | 100000 | 1 | 3 | 126 | 161 | 1.28 | 1.25 | 81.01 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | fan-in | 100000 | 1 | 3 | 126 | 159 | 1.27 | 1.24 | 81.01 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | fan-in | 100000 | 8 | 3 | 442 | 228 | 0.51 | 0.39 | 56.13 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | fan-in | 100000 | 8 | 3 | 442 | 229 | 0.52 | 0.40 | 56.76 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | worker-pool | 6000 250000 | 1 | 3 | 2,227 | 2,280 | 1.02 | 1.02 | 1.54 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | worker-pool | 6000 250000 | 1 | 3 | 2,227 | 2,229 | 1.00 | 1.00 | 1.54 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | worker-pool | 6000 250000 | 8 | 3 | 411 | 422 | 1.03 | 1.00 | 1.34 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | worker-pool | 6000 250000 | 8 | 3 | 411 | 434 | 1.06 | 1.00 | 1.36 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | spawn-many | 200000 | 1 | 3 | 110 | 144 | 1.30 | 1.32 | 2.46 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | spawn-many | 200000 | 1 | 3 | 110 | 136 | 1.23 | 1.23 | 2.46 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | spawn-many | 200000 | 8 | 3 | 29 | 126 | 4.35 | 4.49 | 9.39 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | spawn-many | 200000 | 8 | 3 | 29 | 105 | 3.64 | 3.73 | 9.47 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | coroutine-churn | 200000 | 1 | 3 | 193 | 131 | 0.68 | 0.66 | 1.44 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | coroutine-churn | 200000 | 1 | 3 | 193 | 133 | 0.69 | 0.70 | 1.44 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | coroutine-churn | 200000 | 8* | 3 | 192 | 127 | 0.66 | 0.64 | 1.44 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | coroutine-churn | 200000 | 8* | 3 | 192 | 133 | 0.69 | 0.68 | 1.44 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | shared-counter | 200000 | 1 | 3 | 36 | 316 | 8.71 | 8.84 | 163.42 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | shared-counter | 200000 | 1 | 3 | 36 | 317 | 8.73 | 8.84 | 162.12 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | shared-counter | 200000 | 8 | 3 | 1,079 | 451 | 0.42 | 0.19 | 111.73 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | macos | shared-counter | 200000 | 8 | 3 | 1,079 | 453 | 0.42 | 0.20 | 112.18 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | thread-ring | 3000000 | 1 | 3 | 160 | 313 | 1.95 | 1.96 | 0.70 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | thread-ring | 3000000 | 1 | 3 | 160 | 313 | 1.95 | 1.95 | 1.21 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | thread-ring | 3000000 | 7 | 3 | 234 | 318 | 1.36 | 0.80 | 0.66 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | thread-ring | 3000000 | 7 | 3 | 234 | 321 | 1.37 | 0.81 | 1.07 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | ping-pong | 1000000 | 1 | 3 | 179 | 199 | 1.11 | 1.12 | 0.26 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | ping-pong | 1000000 | 1 | 3 | 179 | 202 | 1.13 | 1.13 | 0.33 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | ping-pong | 1000000 | 7 | 3 | 158 | 204 | 1.29 | 1.27 | 0.25 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | ping-pong | 1000000 | 7 | 3 | 158 | 202 | 1.28 | 1.28 | 0.31 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | fan-in | 100000 | 1 | 3 | 123 | 156 | 1.26 | 1.26 | 150.59 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | fan-in | 100000 | 1 | 3 | 123 | 155 | 1.26 | 1.26 | 150.59 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | fan-in | 100000 | 7 | 3 | 145 | 157 | 1.09 | 0.97 | 112.54 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | fan-in | 100000 | 7 | 3 | 145 | 159 | 1.10 | 1.00 | 111.64 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,248 | 2,263 | 1.01 | 1.01 | 2.03 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,248 | 2,249 | 1.00 | 1.00 | 2.10 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 472 | 515 | 1.09 | 0.99 | 1.89 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 472 | 576 | 1.22 | 0.98 | 1.89 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | spawn-many | 200000 | 1 | 3 | 108 | 133 | 1.23 | 1.23 | 3.73 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | spawn-many | 200000 | 1 | 3 | 108 | 136 | 1.26 | 1.27 | 3.73 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | spawn-many | 200000 | 7 | 3 | 52 | 91 | 1.74 | 1.70 | 2.44 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | spawn-many | 200000 | 7 | 3 | 52 | 78 | 1.50 | 1.74 | 2.30 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 167 | 118 | 0.71 | 0.71 | 1.62 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 167 | 123 | 0.73 | 0.73 | 1.62 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 169 | 119 | 0.71 | 0.71 | 1.66 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 169 | 123 | 0.73 | 0.73 | 1.66 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | shared-counter | 200000 | 1 | 3 | 33 | 310 | 9.46 | 9.49 | 293.13 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | shared-counter | 200000 | 1 | 3 | 33 | 311 | 9.47 | 9.50 | 293.14 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | shared-counter | 200000 | 7 | 3 | 446 | 317 | 0.71 | 0.44 | 227.19 | ref@144b354df | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | 8ae57740b | dirty | orb | shared-counter | 200000 | 7 | 3 | 446 | 316 | 0.71 | 0.44 | 227.13 | slot | R7 change 2: Go coroutine path (kept stacks, per-P stack cache and seed stats, lazy trace id) |
| 2026-10-16 | beea4edf0 | clean | macos | thread-ring | 3000000 | 1 | 3 | 156 | 295 | 1.89 | 1.90 | 0.80 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | thread-ring | 3000000 | 1 | 3 | 156 | 297 | 1.90 | 1.91 | 1.14 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | thread-ring | 3000000 | 8 | 3 | 717 | 370 | 0.52 | 0.19 | 1.04 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | thread-ring | 3000000 | 8 | 3 | 717 | 365 | 0.51 | 0.19 | 1.03 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | ping-pong | 1000000 | 1 | 3 | 181 | 191 | 1.05 | 1.05 | 0.61 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | ping-pong | 1000000 | 1 | 3 | 181 | 193 | 1.07 | 1.07 | 0.60 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | ping-pong | 1000000 | 8 | 3 | 207 | 257 | 1.24 | 1.22 | 0.59 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | ping-pong | 1000000 | 8 | 3 | 207 | 259 | 1.25 | 1.21 | 0.59 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | fan-in | 100000 | 1 | 3 | 130 | 170 | 1.31 | 1.30 | 81.36 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | fan-in | 100000 | 1 | 3 | 130 | 169 | 1.31 | 1.27 | 81.38 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | fan-in | 100000 | 8 | 3 | 464 | 239 | 0.52 | 0.37 | 56.49 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | fan-in | 100000 | 8 | 3 | 464 | 240 | 0.52 | 0.39 | 57.47 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | worker-pool | 6000 250000 | 1 | 3 | 2,389 | 2,389 | 1.00 | 1.00 | 1.54 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | worker-pool | 6000 250000 | 1 | 3 | 2,389 | 2,389 | 1.00 | 1.00 | 1.54 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | worker-pool | 6000 250000 | 8 | 3 | 443 | 454 | 1.03 | 1.00 | 1.32 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | worker-pool | 6000 250000 | 8 | 3 | 443 | 463 | 1.04 | 1.00 | 1.34 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | spawn-many | 200000 | 1 | 3 | 108 | 144 | 1.33 | 1.35 | 2.82 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | spawn-many | 200000 | 1 | 3 | 108 | 140 | 1.29 | 1.26 | 2.82 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | spawn-many | 200000 | 8 | 3 | 30 | 134 | 4.46 | 4.72 | 8.96 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | spawn-many | 200000 | 8 | 3 | 30 | 114 | 3.81 | 3.87 | 9.04 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | coroutine-churn | 200000 | 1 | 3 | 198 | 131 | 0.66 | 0.64 | 1.47 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | coroutine-churn | 200000 | 1 | 3 | 198 | 134 | 0.68 | 0.68 | 1.47 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | coroutine-churn | 200000 | 8* | 3 | 175 | 131 | 0.75 | 0.72 | 1.47 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | coroutine-churn | 200000 | 8* | 3 | 175 | 134 | 0.77 | 0.74 | 1.47 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | shared-counter | 200000 | 1 | 3 | 36 | 319 | 8.80 | 8.84 | 161.42 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | shared-counter | 200000 | 1 | 3 | 36 | 321 | 8.86 | 8.86 | 161.42 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | shared-counter | 200000 | 8 | 3 | 1,081 | 453 | 0.42 | 0.19 | 116.40 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | clean | macos | shared-counter | 200000 | 8 | 3 | 1,081 | 453 | 0.42 | 0.20 | 116.20 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | thread-ring | 3000000 | 1 | 3 | 155 | 313 | 2.01 | 2.01 | 0.71 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | thread-ring | 3000000 | 1 | 3 | 155 | 309 | 1.99 | 1.99 | 1.21 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | thread-ring | 3000000 | 7 | 3 | 208 | 321 | 1.55 | 1.03 | 0.68 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | thread-ring | 3000000 | 7 | 3 | 208 | 321 | 1.55 | 1.03 | 1.02 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | ping-pong | 1000000 | 1 | 3 | 180 | 200 | 1.11 | 1.11 | 0.26 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | ping-pong | 1000000 | 1 | 3 | 180 | 201 | 1.11 | 1.11 | 0.33 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | ping-pong | 1000000 | 7 | 3 | 159 | 202 | 1.27 | 1.27 | 0.25 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | ping-pong | 1000000 | 7 | 3 | 159 | 202 | 1.27 | 1.26 | 0.31 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | fan-in | 100000 | 1 | 3 | 123 | 156 | 1.26 | 1.26 | 140.19 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | fan-in | 100000 | 1 | 3 | 123 | 156 | 1.27 | 1.26 | 140.19 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | fan-in | 100000 | 7 | 3 | 138 | 158 | 1.14 | 1.07 | 116.48 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | fan-in | 100000 | 7 | 3 | 138 | 158 | 1.14 | 1.06 | 121.89 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,268 | 2,401 | 1.06 | 1.06 | 2.01 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,268 | 2,406 | 1.06 | 1.06 | 2.02 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 452 | 509 | 1.13 | 0.95 | 1.70 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 452 | 515 | 1.14 | 0.90 | 1.74 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | spawn-many | 200000 | 1 | 3 | 111 | 140 | 1.26 | 1.26 | 3.63 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | spawn-many | 200000 | 1 | 3 | 111 | 139 | 1.25 | 1.25 | 3.63 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | spawn-many | 200000 | 7 | 3 | 54 | 92 | 1.70 | 1.42 | 2.42 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | spawn-many | 200000 | 7 | 3 | 54 | 81 | 1.49 | 1.51 | 2.40 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 177 | 124 | 0.70 | 0.70 | 1.68 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 177 | 128 | 0.72 | 0.72 | 1.68 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 170 | 121 | 0.71 | 0.71 | 1.63 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 170 | 126 | 0.74 | 0.74 | 1.63 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | shared-counter | 200000 | 1 | 3 | 33 | 311 | 9.48 | 9.53 | 291.43 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | shared-counter | 200000 | 1 | 3 | 33 | 310 | 9.43 | 9.49 | 291.43 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | shared-counter | 200000 | 7 | 3 | 375 | 318 | 0.85 | 0.60 | 213.25 | ref@144b354df | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | beea4edf0 | dirty | orb | shared-counter | 200000 | 7 | 3 | 375 | 320 | 0.85 | 0.62 | 207.66 | slot | R7 change 2: Go coroutine path (beea4edf0) |
| 2026-10-16 | ac3867b23 | dirty | macos | thread-ring | 3000000 | 1 | 3 | 154 | 296 | 1.92 | 1.93 | 1.14 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | thread-ring | 3000000 | 1 | 3 | 154 | 298 | 1.94 | 1.95 | 0.85 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | thread-ring | 3000000 | 8 | 3 | 699 | 372 | 0.53 | 0.20 | 1.03 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | thread-ring | 3000000 | 8 | 3 | 699 | 366 | 0.52 | 0.19 | 1.03 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | ping-pong | 1000000 | 1 | 3 | 181 | 192 | 1.06 | 1.07 | 0.61 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | ping-pong | 1000000 | 1 | 3 | 181 | 194 | 1.07 | 1.08 | 0.60 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | ping-pong | 1000000 | 8 | 3 | 204 | 253 | 1.24 | 1.21 | 0.61 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | ping-pong | 1000000 | 8 | 3 | 204 | 254 | 1.25 | 1.21 | 0.60 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | fan-in | 100000 | 1 | 3 | 127 | 159 | 1.25 | 1.22 | 81.03 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | fan-in | 100000 | 1 | 3 | 127 | 160 | 1.26 | 1.23 | 81.03 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | fan-in | 100000 | 8 | 3 | 447 | 228 | 0.51 | 0.39 | 57.02 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | fan-in | 100000 | 8 | 3 | 447 | 231 | 0.52 | 0.40 | 55.79 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | worker-pool | 6000 250000 | 1 | 3 | 2,226 | 2,229 | 1.00 | 1.00 | 1.53 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | worker-pool | 6000 250000 | 1 | 3 | 2,226 | 2,232 | 1.00 | 1.00 | 1.53 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | worker-pool | 6000 250000 | 8 | 3 | 411 | 425 | 1.03 | 1.00 | 1.35 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | worker-pool | 6000 250000 | 8 | 3 | 411 | 443 | 1.08 | 1.00 | 1.35 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | spawn-many | 200000 | 1 | 3 | 109 | 136 | 1.25 | 1.24 | 2.47 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | spawn-many | 200000 | 1 | 3 | 109 | 147 | 1.35 | 1.33 | 2.48 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | spawn-many | 200000 | 8 | 3 | 29 | 106 | 3.66 | 3.79 | 9.28 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | spawn-many | 200000 | 8 | 3 | 29 | 107 | 3.68 | 3.79 | 9.27 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | coroutine-churn | 200000 | 1 | 3 | 192 | 131 | 0.68 | 0.67 | 1.44 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | coroutine-churn | 200000 | 1 | 3 | 192 | 120 | 0.63 | 0.61 | 1.44 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | coroutine-churn | 200000 | 8* | 3 | 193 | 133 | 0.69 | 0.68 | 1.45 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | coroutine-churn | 200000 | 8* | 3 | 193 | 122 | 0.63 | 0.61 | 1.45 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | shared-counter | 200000 | 1 | 3 | 36 | 318 | 8.77 | 8.86 | 162.14 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | shared-counter | 200000 | 1 | 3 | 36 | 323 | 8.91 | 9.15 | 162.13 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | shared-counter | 200000 | 8 | 3 | 1,068 | 451 | 0.42 | 0.20 | 113.15 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | macos | shared-counter | 200000 | 8 | 3 | 1,068 | 464 | 0.43 | 0.20 | 111.23 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | thread-ring | 3000000 | 1 | 3 | 156 | 315 | 2.02 | 2.03 | 1.24 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | thread-ring | 3000000 | 1 | 3 | 156 | 312 | 2.00 | 2.01 | 0.79 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | thread-ring | 3000000 | 7 | 3 | 227 | 322 | 1.42 | 0.88 | 1.03 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | thread-ring | 3000000 | 7 | 3 | 227 | 320 | 1.41 | 0.87 | 0.81 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | ping-pong | 1000000 | 1 | 3 | 179 | 199 | 1.11 | 1.12 | 0.38 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | ping-pong | 1000000 | 1 | 3 | 179 | 201 | 1.12 | 1.13 | 0.38 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | ping-pong | 1000000 | 7 | 3 | 159 | 201 | 1.27 | 1.26 | 0.33 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | ping-pong | 1000000 | 7 | 3 | 159 | 200 | 1.26 | 1.24 | 0.33 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | fan-in | 100000 | 1 | 3 | 124 | 157 | 1.27 | 1.27 | 139.94 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | fan-in | 100000 | 1 | 3 | 124 | 155 | 1.26 | 1.26 | 139.93 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | fan-in | 100000 | 7 | 3 | 140 | 159 | 1.14 | 1.06 | 112.07 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | fan-in | 100000 | 7 | 3 | 140 | 157 | 1.12 | 1.03 | 114.57 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,268 | 2,257 | 1.00 | 0.99 | 1.97 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,268 | 2,408 | 1.06 | 1.06 | 1.97 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 412 | 579 | 1.41 | 0.95 | 1.91 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 412 | 532 | 1.29 | 0.98 | 1.91 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | spawn-many | 200000 | 1 | 3 | 110 | 135 | 1.23 | 1.23 | 3.72 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | spawn-many | 200000 | 1 | 3 | 110 | 142 | 1.29 | 1.30 | 3.72 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | spawn-many | 200000 | 7 | 3 | 55 | 82 | 1.49 | 1.66 | 2.26 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | spawn-many | 200000 | 7 | 3 | 55 | 74 | 1.35 | 1.51 | 2.28 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 169 | 124 | 0.73 | 0.73 | 1.62 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 169 | 113 | 0.67 | 0.67 | 1.63 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 166 | 125 | 0.75 | 0.75 | 1.58 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 166 | 115 | 0.69 | 0.69 | 1.58 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | shared-counter | 200000 | 1 | 3 | 33 | 309 | 9.50 | 9.52 | 292.00 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | shared-counter | 200000 | 1 | 3 | 33 | 309 | 9.47 | 9.50 | 292.00 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | shared-counter | 200000 | 7 | 3 | 457 | 315 | 0.69 | 0.43 | 208.46 | ref@beea4edf0 | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | ac3867b23 | dirty | orb | shared-counter | 200000 | 7 | 3 | 457 | 314 | 0.69 | 0.43 | 203.95 | slot | R7e: ascending stack cache, gfget gives the stack, window close frees global stacks |
| 2026-10-16 | 651dbc48f | clean | windows | thread-ring | 3000000 | 1 | 3 | 342 | 557 | 1.63 | 1.59 | 0.86 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | thread-ring | 3000000 | 1 | 3 | 342 | 559 | 1.64 | 1.64 | 0.87 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,441 | 864 | 0.60 | 0.10 | 0.80 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,441 | 880 | 0.61 | 0.10 | 0.79 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | ping-pong | 1000000 | 1 | 3 | 362 | 303 | 0.84 | 0.86 | 0.70 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | ping-pong | 1000000 | 1 | 3 | 362 | 308 | 0.85 | 0.86 | 0.70 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | ping-pong | 1000000 | 12 | 3 | 494 | 521 | 1.06 | 1.11 | 0.69 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | ping-pong | 1000000 | 12 | 3 | 494 | 534 | 1.08 | 1.11 | 0.68 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | fan-in | 100000 | 1 | 3 | 276 | 468 | 1.69 | 1.76 | 49.44 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | fan-in | 100000 | 1 | 3 | 276 | 469 | 1.70 | 1.71 | 49.44 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | fan-in | 100000 | 12 | 3 | 980 | 774 | 0.79 | 1.01 | 41.64 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | fan-in | 100000 | 12 | 3 | 980 | 770 | 0.79 | 1.07 | 41.57 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,126 | 2,126 | 1.00 | 1.01 | 1.29 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,126 | 2,126 | 1.00 | 1.01 | 1.29 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 210 | 228 | 1.08 | 0.98 | 1.15 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 210 | 220 | 1.05 | 0.99 | 1.16 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | spawn-many | 200000 | 1 | 3 | 253 | 1,022 | 4.04 | 4.06 | 6.65 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | spawn-many | 200000 | 1 | 3 | 253 | 578 | 2.28 | 2.31 | 6.65 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | spawn-many | 200000 | 12 | 3 | 56 | 210 | 3.74 | 4.56 | 6.51 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | spawn-many | 200000 | 12 | 3 | 56 | 193 | 3.45 | 3.56 | 6.69 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | coroutine-churn | 200000 | 1 | 3 | 349 | 914 | 2.62 | 2.64 | 4.48 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | coroutine-churn | 200000 | 1 | 3 | 349 | 467 | 1.34 | 1.32 | 4.48 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | coroutine-churn | 200000 | 12* | 3 | 352 | 911 | 2.59 | 2.52 | 4.32 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | coroutine-churn | 200000 | 12* | 3 | 352 | 470 | 1.34 | 1.30 | 4.32 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | shared-counter | 200000 | 1 | 3 | 161 | 929 | 5.78 | 5.90 | 99.33 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | shared-counter | 200000 | 1 | 3 | 161 | 931 | 5.79 | 6.00 | 100.31 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | shared-counter | 200000 | 12 | 3 | 559 | 1,535 | 2.74 | 2.12 | 81.63 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | clean | windows | shared-counter | 200000 | 12 | 3 | 559 | 1,532 | 2.74 | 2.19 | 81.50 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 360 | 547 | 1.52 | 1.52 | 1.32 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 360 | 557 | 1.55 | 1.55 | 1.32 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,068 | 602 | 0.56 | 0.21 | 1.15 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,068 | 612 | 0.57 | 0.21 | 1.19 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 367 | 303 | 0.82 | 0.82 | 0.21 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 367 | 305 | 0.83 | 0.83 | 0.21 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 358 | 342 | 0.95 | 0.94 | 0.19 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 358 | 339 | 0.95 | 0.94 | 0.25 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | fan-in | 100000 | 1 | 3 | 274 | 559 | 2.04 | 2.04 | 148.88 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | fan-in | 100000 | 1 | 3 | 274 | 556 | 2.03 | 2.03 | 148.75 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | fan-in | 100000 | 12 | 3 | 634 | 837 | 1.32 | 2.41 | 123.56 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | fan-in | 100000 | 12 | 3 | 634 | 835 | 1.32 | 2.38 | 123.67 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,135 | 2,130 | 1.00 | 1.00 | 2.00 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,135 | 2,134 | 1.00 | 1.00 | 2.07 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 211 | 225 | 1.06 | 1.05 | 1.82 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 211 | 228 | 1.08 | 1.02 | 1.88 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | spawn-many | 200000 | 1 | 3 | 177 | 475 | 2.68 | 2.73 | 5.70 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | spawn-many | 200000 | 1 | 3 | 177 | 478 | 2.70 | 2.74 | 5.70 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | spawn-many | 200000 | 12 | 3 | 50 | 242 | 4.87 | 5.60 | 10.52 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | spawn-many | 200000 | 12 | 3 | 50 | 215 | 4.32 | 4.65 | 10.68 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 260 | 348 | 1.34 | 1.34 | 2.73 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 260 | 330 | 1.27 | 1.27 | 2.73 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 282 | 376 | 1.33 | 1.33 | 2.70 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 282 | 331 | 1.18 | 1.18 | 2.70 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | shared-counter | 200000 | 1 | 3 | 155 | 1,127 | 7.28 | 7.27 | 297.44 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | shared-counter | 200000 | 1 | 3 | 155 | 1,111 | 7.18 | 7.20 | 297.50 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | shared-counter | 200000 | 12 | 3 | 717 | 1,660 | 2.31 | 2.02 | 248.44 | ref@144b354df | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | wsl | shared-counter | 200000 | 12 | 3 | 717 | 1,674 | 2.33 | 2.16 | 246.83 | slot | R7 change 2 + R7e: Go coroutine path (c550796db) |
| 2026-10-16 | 651dbc48f | dirty | windows | spawn-many | 200000 | 1 | 5 | 247 | 572 | 2.31 | 2.25 | 6.68 | ref@beea4edf0 | R7e spawn-many check: c550796db against beea4edf0 |
| 2026-10-16 | 651dbc48f | dirty | windows | spawn-many | 200000 | 1 | 5 | 247 | 579 | 2.34 | 2.31 | 6.68 | slot | R7e spawn-many check: c550796db against beea4edf0 |
| 2026-10-16 | 651dbc48f | dirty | windows | spawn-many | 200000 | 12 | 5 | 57 | 193 | 3.36 | 3.67 | 6.44 | ref@beea4edf0 | R7e spawn-many check: c550796db against beea4edf0 |
| 2026-10-16 | 651dbc48f | dirty | windows | spawn-many | 200000 | 12 | 5 | 57 | 194 | 3.38 | 4.00 | 6.44 | slot | R7e spawn-many check: c550796db against beea4edf0 |
| 2026-10-16 | 651dbc48f | dirty | wsl | spawn-many | 200000 | 1 | 5 | 169 | 918 | 5.43 | 5.51 | 13.09 | ref@beea4edf0 | R7e spawn-many check: c550796db against beea4edf0 |
| 2026-10-16 | 651dbc48f | dirty | wsl | spawn-many | 200000 | 1 | 5 | 169 | 467 | 2.76 | 2.79 | 5.80 | slot | R7e spawn-many check: c550796db against beea4edf0 |
| 2026-10-16 | 651dbc48f | dirty | wsl | spawn-many | 200000 | 12 | 5 | 51 | 235 | 4.63 | 4.85 | 10.54 | ref@beea4edf0 | R7e spawn-many check: c550796db against beea4edf0 |
| 2026-10-16 | 651dbc48f | dirty | wsl | spawn-many | 200000 | 12 | 5 | 51 | 231 | 4.56 | 4.79 | 10.53 | slot | R7e spawn-many check: c550796db against beea4edf0 |
| 2026-10-16 | d914f3bc4 | dirty | windows | shared-counter | 200000 | 1 | 3 | 557 | 982 | 1.76 | 1.72 | 98.34 | slot | shared-counter oracle is the channel-actor equivalent (cap 1024) |
| 2026-10-16 | d914f3bc4 | dirty | windows | shared-counter | 200000 | 12 | 3 | 2,032 | 1,583 | 0.78 | 0.89 | 82.75 | slot | shared-counter oracle is the channel-actor equivalent (cap 1024) |
| 2026-10-16 | d914f3bc4 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 568 | 1,169 | 2.06 | 2.05 | 297.38 | slot | shared-counter oracle is the channel-actor equivalent (cap 1024) |
| 2026-10-16 | d914f3bc4 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,514 | 1,718 | 1.14 | 1.65 | 252.56 | slot | shared-counter oracle is the channel-actor equivalent (cap 1024) |
| 2026-10-16 | 41a928111 | dirty | macos | thread-ring | 3000000 | 1 | 3 | 154 | 300 | 1.95 | 1.96 | 0.86 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | thread-ring | 3000000 | 1 | 3 | 154 | 254 | 1.65 | 1.66 | 0.87 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | thread-ring | 3000000 | 8 | 3 | 713 | 367 | 0.51 | 0.19 | 1.04 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | thread-ring | 3000000 | 8 | 3 | 713 | 317 | 0.44 | 0.16 | 1.02 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | ping-pong | 1000000 | 1 | 3 | 178 | 193 | 1.08 | 1.08 | 0.61 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | ping-pong | 1000000 | 1 | 3 | 178 | 178 | 1.00 | 1.00 | 0.61 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | ping-pong | 1000000 | 8 | 3 | 206 | 258 | 1.25 | 1.23 | 0.60 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | ping-pong | 1000000 | 8 | 3 | 206 | 241 | 1.17 | 1.14 | 0.60 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | fan-in | 100000 | 1 | 3 | 126 | 160 | 1.27 | 1.24 | 80.81 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | fan-in | 100000 | 1 | 3 | 126 | 90 | 0.72 | 0.72 | 35.68 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | fan-in | 100000 | 8 | 3 | 480 | 225 | 0.47 | 0.35 | 57.68 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | fan-in | 100000 | 8 | 3 | 480 | 131 | 0.27 | 0.15 | 29.08 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | worker-pool | 6000 250000 | 1 | 3 | 2,228 | 2,235 | 1.00 | 1.00 | 1.53 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | worker-pool | 6000 250000 | 1 | 3 | 2,228 | 2,231 | 1.00 | 1.00 | 1.51 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | worker-pool | 6000 250000 | 8 | 3 | 413 | 424 | 1.03 | 1.00 | 1.36 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | worker-pool | 6000 250000 | 8 | 3 | 413 | 429 | 1.04 | 1.00 | 1.42 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | spawn-many | 200000 | 1 | 3 | 107 | 141 | 1.32 | 1.28 | 2.73 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | spawn-many | 200000 | 1 | 3 | 107 | 130 | 1.21 | 1.17 | 2.94 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | spawn-many | 200000 | 8 | 3 | 29 | 102 | 3.49 | 3.85 | 9.09 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | spawn-many | 200000 | 8 | 3 | 29 | 121 | 4.16 | 4.19 | 9.21 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | coroutine-churn | 200000 | 1 | 3 | 189 | 112 | 0.59 | 0.57 | 1.46 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | coroutine-churn | 200000 | 1 | 3 | 189 | 113 | 0.59 | 0.60 | 1.47 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | coroutine-churn | 200000 | 8* | 3 | 189 | 113 | 0.60 | 0.58 | 1.45 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | coroutine-churn | 200000 | 8* | 3 | 189 | 113 | 0.60 | 0.58 | 1.45 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | shared-counter | 200000 | 1 | 3 | 251 | 315 | 1.26 | 1.25 | 161.42 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | shared-counter | 200000 | 1 | 3 | 251 | 175 | 0.70 | 0.70 | 70.08 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | shared-counter | 200000 | 8 | 3 | 989 | 447 | 0.45 | 0.33 | 112.49 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | macos | shared-counter | 200000 | 8 | 3 | 989 | 255 | 0.26 | 0.14 | 56.75 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | thread-ring | 3000000 | 1 | 3 | 156 | 315 | 2.02 | 2.03 | 0.82 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | thread-ring | 3000000 | 1 | 3 | 156 | 257 | 1.66 | 1.66 | 0.86 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | thread-ring | 3000000 | 7 | 3 | 213 | 322 | 1.51 | 0.97 | 0.88 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | thread-ring | 3000000 | 7 | 3 | 213 | 263 | 1.23 | 0.79 | 0.89 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | ping-pong | 1000000 | 1 | 3 | 178 | 201 | 1.13 | 1.13 | 0.33 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | ping-pong | 1000000 | 1 | 3 | 178 | 188 | 1.06 | 1.06 | 0.32 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | ping-pong | 1000000 | 7 | 3 | 159 | 199 | 1.26 | 1.25 | 0.31 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | ping-pong | 1000000 | 7 | 3 | 159 | 191 | 1.20 | 1.19 | 0.31 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | fan-in | 100000 | 1 | 3 | 124 | 156 | 1.26 | 1.26 | 150.59 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | fan-in | 100000 | 1 | 3 | 124 | 93 | 0.75 | 0.75 | 66.19 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | fan-in | 100000 | 7 | 3 | 145 | 158 | 1.09 | 0.97 | 105.50 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | fan-in | 100000 | 7 | 3 | 145 | 94 | 0.65 | 0.57 | 54.87 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,243 | 2,252 | 1.00 | 1.00 | 2.05 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | worker-pool | 6000 250000 | 1 | 3 | 2,243 | 2,256 | 1.01 | 1.00 | 2.12 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 458 | 426 | 0.93 | 1.04 | 1.91 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | worker-pool | 6000 250000 | 7 | 3 | 458 | 572 | 1.25 | 0.97 | 1.97 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | spawn-many | 200000 | 1 | 3 | 108 | 139 | 1.29 | 1.29 | 3.56 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | spawn-many | 200000 | 1 | 3 | 108 | 128 | 1.19 | 1.18 | 3.84 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | spawn-many | 200000 | 7 | 3 | 56 | 74 | 1.33 | 1.37 | 2.36 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | spawn-many | 200000 | 7 | 3 | 56 | 78 | 1.40 | 1.43 | 2.36 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 188 | 113 | 0.60 | 0.60 | 1.64 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | coroutine-churn | 200000 | 1 | 3 | 188 | 113 | 0.60 | 0.60 | 1.65 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 168 | 114 | 0.68 | 0.68 | 1.59 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | coroutine-churn | 200000 | 7* | 3 | 168 | 109 | 0.65 | 0.65 | 1.59 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | shared-counter | 200000 | 1 | 3 | 249 | 310 | 1.24 | 1.24 | 274.88 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | shared-counter | 200000 | 1 | 3 | 249 | 186 | 0.74 | 0.75 | 120.46 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | shared-counter | 200000 | 7 | 3 | 285 | 315 | 1.10 | 1.01 | 209.98 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | 41a928111 | dirty | orb | shared-counter | 200000 | 7 | 3 | 285 | 187 | 0.65 | 0.59 | 113.86 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | thread-ring | 3000000 | 1 | 3 | 342 | 558 | 1.63 | 1.67 | 0.87 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | thread-ring | 3000000 | 1 | 3 | 342 | 473 | 1.38 | 1.43 | 0.89 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,438 | 873 | 0.61 | 0.11 | 0.78 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,438 | 756 | 0.53 | 0.09 | 0.78 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | ping-pong | 1000000 | 1 | 3 | 363 | 307 | 0.85 | 0.83 | 0.70 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | ping-pong | 1000000 | 1 | 3 | 363 | 273 | 0.75 | 0.74 | 0.70 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | ping-pong | 1000000 | 12 | 3 | 482 | 529 | 1.10 | 1.20 | 0.70 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | ping-pong | 1000000 | 12 | 3 | 482 | 479 | 0.99 | 1.06 | 0.69 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | fan-in | 100000 | 1 | 3 | 276 | 468 | 1.70 | 1.76 | 49.90 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | fan-in | 100000 | 1 | 3 | 276 | 309 | 1.12 | 1.18 | 22.26 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | fan-in | 100000 | 12 | 3 | 987 | 776 | 0.79 | 1.04 | 42.14 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | fan-in | 100000 | 12 | 3 | 987 | 405 | 0.41 | 0.27 | 19.61 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,124 | 2,124 | 1.00 | 1.00 | 1.29 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,124 | 2,123 | 1.00 | 0.99 | 1.27 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 203 | 222 | 1.09 | 1.05 | 1.16 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 203 | 214 | 1.05 | 1.00 | 1.16 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | spawn-many | 200000 | 1 | 3 | 253 | 575 | 2.27 | 2.25 | 6.56 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | spawn-many | 200000 | 1 | 3 | 253 | 609 | 2.41 | 2.44 | 6.74 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | spawn-many | 200000 | 12 | 3 | 57 | 194 | 3.37 | 4.00 | 6.68 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | spawn-many | 200000 | 12 | 3 | 57 | 207 | 3.60 | 3.80 | 6.75 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | coroutine-churn | 200000 | 1 | 3 | 348 | 465 | 1.34 | 1.26 | 4.40 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | coroutine-churn | 200000 | 1 | 3 | 348 | 471 | 1.35 | 1.30 | 4.40 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | coroutine-churn | 200000 | 12* | 3 | 348 | 467 | 1.34 | 1.36 | 4.44 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | coroutine-churn | 200000 | 12* | 3 | 348 | 465 | 1.34 | 1.32 | 4.44 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | shared-counter | 200000 | 1 | 3 | 548 | 927 | 1.69 | 1.69 | 99.83 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | shared-counter | 200000 | 1 | 3 | 548 | 598 | 1.09 | 1.09 | 43.65 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | shared-counter | 200000 | 12 | 3 | 1,811 | 1,529 | 0.84 | 1.11 | 82.93 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | clean | windows | shared-counter | 200000 | 12 | 3 | 1,811 | 784 | 0.43 | 0.26 | 38.54 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 354 | 555 | 1.57 | 1.57 | 1.32 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 354 | 443 | 1.25 | 1.25 | 1.36 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,040 | 599 | 0.58 | 0.21 | 1.11 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,040 | 520 | 0.50 | 0.18 | 1.15 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 364 | 305 | 0.84 | 0.84 | 0.21 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 364 | 269 | 0.74 | 0.74 | 0.29 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 357 | 340 | 0.95 | 0.95 | 0.27 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 357 | 315 | 0.88 | 0.88 | 0.33 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | fan-in | 100000 | 1 | 3 | 274 | 554 | 2.03 | 2.02 | 148.94 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | fan-in | 100000 | 1 | 3 | 274 | 373 | 1.36 | 1.37 | 64.45 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | fan-in | 100000 | 12 | 3 | 645 | 834 | 1.29 | 2.36 | 123.28 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | fan-in | 100000 | 12 | 3 | 645 | 655 | 1.01 | 1.52 | 57.12 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,127 | 2,139 | 1.01 | 1.01 | 2.07 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,127 | 2,145 | 1.01 | 1.01 | 2.00 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 201 | 226 | 1.12 | 1.06 | 1.88 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 201 | 229 | 1.14 | 1.03 | 1.88 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | spawn-many | 200000 | 1 | 3 | 169 | 467 | 2.77 | 2.79 | 5.74 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | spawn-many | 200000 | 1 | 3 | 169 | 512 | 3.03 | 3.06 | 6.19 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 222 | 4.52 | 4.82 | 10.38 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 243 | 4.94 | 5.07 | 10.54 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 258 | 329 | 1.27 | 1.27 | 2.58 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 258 | 326 | 1.26 | 1.27 | 2.59 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 262 | 371 | 1.41 | 1.43 | 2.62 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 262 | 325 | 1.24 | 1.25 | 2.63 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | shared-counter | 200000 | 1 | 3 | 554 | 1,241 | 2.24 | 2.24 | 297.38 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | shared-counter | 200000 | 1 | 3 | 554 | 726 | 1.31 | 1.31 | 128.45 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,375 | 1,634 | 1.19 | 1.71 | 253.67 | ref@41a928111 | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,375 | 1,286 | 0.94 | 1.28 | 114.11 | slot | R8: mailbox ring, no per-message allocation |
| 2026-10-16 | f16b0678d | dirty | windows | spawn-many | 200000 | 1 | 5 | 249 | 571 | 2.29 | 2.31 | 6.72 | ref@41a928111 | R8 spawn-many check: 386e5e1e2 against 41a928111 |
| 2026-10-16 | f16b0678d | dirty | windows | spawn-many | 200000 | 1 | 5 | 249 | 622 | 2.49 | 2.50 | 6.90 | slot | R8 spawn-many check: 386e5e1e2 against 41a928111 |
| 2026-10-16 | f16b0678d | dirty | windows | spawn-many | 200000 | 12 | 5 | 56 | 194 | 3.48 | 3.80 | 6.42 | ref@41a928111 | R8 spawn-many check: 386e5e1e2 against 41a928111 |
| 2026-10-16 | f16b0678d | dirty | windows | spawn-many | 200000 | 12 | 5 | 56 | 207 | 3.71 | 3.80 | 6.50 | slot | R8 spawn-many check: 386e5e1e2 against 41a928111 |
| 2026-10-16 | f16b0678d | dirty | wsl | spawn-many | 200000 | 1 | 5 | 172 | 465 | 2.71 | 2.75 | 5.85 | ref@41a928111 | R8 spawn-many check: 386e5e1e2 against 41a928111 |
| 2026-10-16 | f16b0678d | dirty | wsl | spawn-many | 200000 | 1 | 5 | 172 | 497 | 2.89 | 2.93 | 6.32 | slot | R8 spawn-many check: 386e5e1e2 against 41a928111 |
| 2026-10-16 | f16b0678d | dirty | wsl | spawn-many | 200000 | 12 | 5 | 51 | 216 | 4.19 | 4.57 | 10.66 | ref@41a928111 | R8 spawn-many check: 386e5e1e2 against 41a928111 |
| 2026-10-16 | f16b0678d | dirty | wsl | spawn-many | 200000 | 12 | 5 | 51 | 225 | 4.37 | 4.63 | 10.82 | slot | R8 spawn-many check: 386e5e1e2 against 41a928111 |
| 2026-10-17 | 990357859 | dirty | windows | thread-ring | 3000000 | 1 | 3 | 345 | 484 | 1.41 | 1.48 | 0.88 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | thread-ring | 3000000 | 1 | 3 | 345 | 492 | 1.43 | 1.48 | 0.86 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | thread-ring | 3000000 | 12 | 3 | 1,591 | 755 | 0.47 | 0.08 | 0.79 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | thread-ring | 3000000 | 12 | 3 | 1,591 | 761 | 0.48 | 0.08 | 0.78 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | ping-pong | 1000000 | 1 | 3 | 360 | 268 | 0.75 | 0.74 | 0.70 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | ping-pong | 1000000 | 1 | 3 | 360 | 268 | 0.74 | 0.74 | 0.71 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | ping-pong | 1000000 | 12 | 3 | 491 | 492 | 1.00 | 0.98 | 0.69 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | ping-pong | 1000000 | 12 | 3 | 491 | 503 | 1.02 | 1.02 | 0.69 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | fan-in | 100000 | 1 | 3 | 273 | 308 | 1.12 | 1.12 | 22.28 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | fan-in | 100000 | 1 | 3 | 273 | 413 | 1.51 | 1.53 | 0.75 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | fan-in | 100000 | 12 | 3 | 1,008 | 399 | 0.40 | 0.21 | 19.69 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | fan-in | 100000 | 12 | 3 | 1,008 | 2,756 | 2.73 | 5.70 | 0.77 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | worker-pool | 6000 250000 | 1 | 3 | 2,111 | 2,113 | 1.00 | 0.99 | 1.27 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | worker-pool | 6000 250000 | 1 | 3 | 2,111 | 2,109 | 1.00 | 1.00 | 1.27 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | worker-pool | 6000 250000 | 12 | 3 | 193 | 200 | 1.03 | 1.03 | 1.17 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | worker-pool | 6000 250000 | 12 | 3 | 193 | 218 | 1.13 | 1.08 | 1.17 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | spawn-many | 200000 | 1 | 3 | 244 | 597 | 2.45 | 2.53 | 6.80 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | spawn-many | 200000 | 1 | 3 | 244 | 585 | 2.40 | 2.47 | 6.80 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | spawn-many | 200000 | 12 | 3 | 56 | 203 | 3.62 | 3.90 | 6.71 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | spawn-many | 200000 | 12 | 3 | 56 | 201 | 3.58 | 3.50 | 6.71 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | coroutine-churn | 200000 | 1 | 3 | 338 | 459 | 1.36 | 1.32 | 4.42 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | coroutine-churn | 200000 | 1 | 3 | 338 | 452 | 1.34 | 1.27 | 4.42 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | coroutine-churn | 200000 | 12* | 3 | 344 | 458 | 1.33 | 1.38 | 4.31 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | coroutine-churn | 200000 | 12* | 3 | 344 | 460 | 1.34 | 1.38 | 4.31 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | shared-counter | 200000 | 1 | 3 | 545 | 588 | 1.08 | 1.06 | 43.68 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | shared-counter | 200000 | 1 | 3 | 545 | 802 | 1.47 | 1.46 | 0.75 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | shared-counter | 200000 | 12 | 3 | 1,899 | 773 | 0.41 | 0.23 | 38.79 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | shared-counter | 200000 | 12 | 3 | 1,899 | 5,727 | 3.02 | 6.44 | 0.77 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 360 | 444 | 1.23 | 1.24 | 1.36 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 360 | 460 | 1.28 | 1.28 | 1.28 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,033 | 510 | 0.49 | 0.18 | 1.19 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,033 | 526 | 0.51 | 0.19 | 1.19 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 361 | 266 | 0.74 | 0.74 | 0.29 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 361 | 267 | 0.74 | 0.74 | 0.29 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 355 | 314 | 0.88 | 0.88 | 0.31 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 355 | 309 | 0.87 | 0.86 | 0.31 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | fan-in | 100000 | 1 | 3 | 272 | 365 | 1.34 | 1.35 | 64.45 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | fan-in | 100000 | 1 | 3 | 272 | 413 | 1.52 | 1.52 | 0.50 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | fan-in | 100000 | 12 | 3 | 621 | 684 | 1.10 | 1.56 | 57.19 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | fan-in | 100000 | 12 | 3 | 621 | 5,000 | 8.05 | 22.93 | 0.56 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,115 | 2,130 | 1.01 | 1.01 | 2.00 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,115 | 2,127 | 1.01 | 1.01 | 2.00 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 198 | 213 | 1.08 | 0.98 | 1.88 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 198 | 228 | 1.15 | 1.02 | 1.88 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | spawn-many | 200000 | 1 | 3 | 169 | 495 | 2.93 | 2.96 | 6.35 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | spawn-many | 200000 | 1 | 3 | 169 | 484 | 2.86 | 2.88 | 6.35 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | spawn-many | 200000 | 12 | 3 | 51 | 223 | 4.38 | 4.68 | 10.69 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | spawn-many | 200000 | 12 | 3 | 51 | 220 | 4.34 | 4.49 | 10.68 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 243 | 375 | 1.54 | 1.54 | 2.92 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 243 | 320 | 1.32 | 1.32 | 2.92 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 238 | 319 | 1.34 | 1.35 | 2.97 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 238 | 322 | 1.35 | 1.35 | 2.97 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 549 | 738 | 1.35 | 1.35 | 128.45 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 549 | 809 | 1.47 | 1.48 | 0.50 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,240 | 1,342 | 1.08 | 1.52 | 114.11 | ref@6a8d2fe6e | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,240 | 10,407 | 8.39 | 24.20 | 0.50 | slot | R9 + 29931a815: bounded mailboxes (1024), failable sends |
| 2026-10-17 | 990357859 | dirty | windows | fan-in | 100000 | 1 | 3 | 274 | 303 | 1.10 | 1.06 | 22.26 | ref@29931a815 | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | windows | fan-in | 100000 | 1 | 3 | 274 | 412 | 1.50 | 1.44 | 0.75 | slot | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | windows | fan-in | 100000 | 12 | 3 | 1,001 | 397 | 0.40 | 0.24 | 19.67 | ref@29931a815 | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | windows | fan-in | 100000 | 12 | 3 | 1,001 | 2,825 | 2.82 | 6.10 | 0.77 | slot | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | windows | worker-pool | 6000 250000 | 1 | 3 | 2,112 | 2,110 | 1.00 | 1.00 | 1.27 | ref@29931a815 | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | windows | worker-pool | 6000 250000 | 1 | 3 | 2,112 | 2,109 | 1.00 | 1.00 | 1.27 | slot | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | windows | worker-pool | 6000 250000 | 12 | 3 | 192 | 196 | 1.02 | 1.01 | 1.17 | ref@29931a815 | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | windows | worker-pool | 6000 250000 | 12 | 3 | 192 | 207 | 1.08 | 1.02 | 1.17 | slot | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | windows | spawn-many | 200000 | 1 | 3 | 237 | 591 | 2.50 | 2.60 | 6.63 | ref@29931a815 | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | windows | spawn-many | 200000 | 1 | 3 | 237 | 583 | 2.46 | 2.47 | 6.63 | slot | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | windows | spawn-many | 200000 | 12 | 3 | 56 | 202 | 3.59 | 4.00 | 6.64 | ref@29931a815 | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | windows | spawn-many | 200000 | 12 | 3 | 56 | 204 | 3.61 | 4.11 | 6.64 | slot | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | windows | shared-counter | 200000 | 1 | 3 | 544 | 586 | 1.08 | 1.06 | 43.65 | ref@29931a815 | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | windows | shared-counter | 200000 | 1 | 3 | 544 | 801 | 1.47 | 1.46 | 0.75 | slot | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | windows | shared-counter | 200000 | 12 | 3 | 1,840 | 772 | 0.42 | 0.25 | 38.88 | ref@29931a815 | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | windows | shared-counter | 200000 | 12 | 3 | 1,840 | 5,764 | 3.13 | 7.10 | 0.76 | slot | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | wsl | fan-in | 100000 | 1 | 3 | 272 | 420 | 1.54 | 1.55 | 64.45 | ref@29931a815 | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | wsl | fan-in | 100000 | 1 | 3 | 272 | 417 | 1.53 | 1.54 | 0.50 | slot | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | wsl | fan-in | 100000 | 12 | 3 | 616 | 666 | 1.08 | 1.47 | 57.23 | ref@29931a815 | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | wsl | fan-in | 100000 | 12 | 3 | 616 | 4,875 | 7.91 | 22.65 | 0.56 | slot | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,120 | 2,123 | 1.00 | 1.00 | 2.00 | ref@29931a815 | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,120 | 2,124 | 1.00 | 1.00 | 2.00 | slot | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 196 | 201 | 1.03 | 1.01 | 1.88 | ref@29931a815 | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 196 | 216 | 1.11 | 1.05 | 1.88 | slot | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | wsl | spawn-many | 200000 | 1 | 3 | 165 | 490 | 2.97 | 3.03 | 5.97 | ref@29931a815 | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | wsl | spawn-many | 200000 | 1 | 3 | 165 | 485 | 2.94 | 2.99 | 5.97 | slot | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 222 | 4.57 | 4.71 | 10.84 | ref@29931a815 | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 224 | 4.62 | 4.60 | 10.81 | slot | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 551 | 777 | 1.41 | 1.41 | 128.45 | ref@29931a815 | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 551 | 807 | 1.47 | 1.47 | 0.50 | slot | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,290 | 1,341 | 1.04 | 1.35 | 114.12 | ref@29931a815 | R9 alone against 29931a815 |
| 2026-10-17 | 990357859 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,290 | 10,381 | 8.05 | 21.79 | 0.56 | slot | R9 alone against 29931a815 |
| 2026-10-17 | 5f06d0adb | clean | windows | fan-in | 100000 | 1 | 3 | 274 | 413 | 1.51 | 1.53 | 0.75 | ref@29f3a07ea | R10 sendTimeout (5f06d0adb) against R9 alone (29f3a07ea): sends park on the sender queue instead of spin-retrying |
| 2026-10-17 | 5f06d0adb | clean | windows | fan-in | 100000 | 1 | 3 | 274 | 421 | 1.54 | 1.59 | 0.75 | slot | R10 sendTimeout (5f06d0adb) against R9 alone (29f3a07ea): sends park on the sender queue instead of spin-retrying |
| 2026-10-17 | 5f06d0adb | clean | windows | fan-in | 100000 | 12 | 3 | 926 | 2,420 | 2.61 | 5.49 | 0.76 | ref@29f3a07ea | R10 sendTimeout (5f06d0adb) against R9 alone (29f3a07ea): sends park on the sender queue instead of spin-retrying |
| 2026-10-17 | 5f06d0adb | clean | windows | fan-in | 100000 | 12 | 3 | 926 | 1,034 | 1.12 | 1.16 | 0.75 | slot | R10 sendTimeout (5f06d0adb) against R9 alone (29f3a07ea): sends park on the sender queue instead of spin-retrying |
| 2026-10-17 | 5f06d0adb | clean | windows | shared-counter | 200000 | 1 | 3 | 546 | 803 | 1.47 | 1.49 | 0.75 | ref@29f3a07ea | R10 sendTimeout (5f06d0adb) against R9 alone (29f3a07ea): sends park on the sender queue instead of spin-retrying |
| 2026-10-17 | 5f06d0adb | clean | windows | shared-counter | 200000 | 1 | 3 | 546 | 811 | 1.49 | 1.49 | 0.75 | slot | R10 sendTimeout (5f06d0adb) against R9 alone (29f3a07ea): sends park on the sender queue instead of spin-retrying |
| 2026-10-17 | 5f06d0adb | clean | windows | shared-counter | 200000 | 12 | 3 | 1,789 | 5,451 | 3.05 | 6.49 | 0.77 | ref@29f3a07ea | R10 sendTimeout (5f06d0adb) against R9 alone (29f3a07ea): sends park on the sender queue instead of spin-retrying |
| 2026-10-17 | 5f06d0adb | clean | windows | shared-counter | 200000 | 12 | 3 | 1,789 | 2,056 | 1.15 | 1.14 | 0.76 | slot | R10 sendTimeout (5f06d0adb) against R9 alone (29f3a07ea): sends park on the sender queue instead of spin-retrying |
| 2026-10-17 | 5f06d0adb | dirty | wsl | fan-in | 100000 | 1 | 3 | 272 | 417 | 1.53 | 1.54 | 0.50 | ref@29f3a07ea | R10 sendTimeout (5f06d0adb) against R9 alone (29f3a07ea): sends park on the sender queue instead of spin-retrying |
| 2026-10-17 | 5f06d0adb | dirty | wsl | fan-in | 100000 | 1 | 3 | 272 | 429 | 1.58 | 1.58 | 0.50 | slot | R10 sendTimeout (5f06d0adb) against R9 alone (29f3a07ea): sends park on the sender queue instead of spin-retrying |
| 2026-10-17 | 5f06d0adb | dirty | wsl | fan-in | 100000 | 12 | 3 | 628 | 4,619 | 7.36 | 20.78 | 0.61 | ref@29f3a07ea | R10 sendTimeout (5f06d0adb) against R9 alone (29f3a07ea): sends park on the sender queue instead of spin-retrying |
| 2026-10-17 | 5f06d0adb | dirty | wsl | fan-in | 100000 | 12 | 3 | 628 | 2,534 | 4.04 | 4.32 | 0.56 | slot | R10 sendTimeout (5f06d0adb) against R9 alone (29f3a07ea): sends park on the sender queue instead of spin-retrying |
| 2026-10-17 | 5f06d0adb | dirty | wsl | shared-counter | 200000 | 1 | 3 | 569 | 816 | 1.43 | 1.44 | 0.50 | ref@29f3a07ea | R10 sendTimeout (5f06d0adb) against R9 alone (29f3a07ea): sends park on the sender queue instead of spin-retrying |
| 2026-10-17 | 5f06d0adb | dirty | wsl | shared-counter | 200000 | 1 | 3 | 569 | 835 | 1.47 | 1.47 | 0.50 | slot | R10 sendTimeout (5f06d0adb) against R9 alone (29f3a07ea): sends park on the sender queue instead of spin-retrying |
| 2026-10-17 | 5f06d0adb | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,260 | 9,841 | 7.81 | 22.23 | 0.56 | ref@29f3a07ea | R10 sendTimeout (5f06d0adb) against R9 alone (29f3a07ea): sends park on the sender queue instead of spin-retrying |
| 2026-10-17 | 5f06d0adb | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,260 | 5,020 | 3.99 | 4.28 | 0.56 | slot | R10 sendTimeout (5f06d0adb) against R9 alone (29f3a07ea): sends park on the sender queue instead of spin-retrying |
| 2026-10-18 | 0b7c85b13 | clean | windows | fan-in | 100000 | 1 | 3 | 275 | 431 | 1.56 | 1.50 | 0.75 | ref@86ea26ca3 | R11 (0b7c85b13) against 86ea26ca3: no clock read without a deadline; the outcome word claims a waiting sender, readied after the mailbox lock is released |
| 2026-10-18 | 0b7c85b13 | clean | windows | fan-in | 100000 | 1 | 3 | 275 | 421 | 1.53 | 1.50 | 0.75 | slot | R11 (0b7c85b13) against 86ea26ca3: no clock read without a deadline; the outcome word claims a waiting sender, readied after the mailbox lock is released |
| 2026-10-18 | 0b7c85b13 | clean | windows | fan-in | 100000 | 12 | 3 | 921 | 1,080 | 1.17 | 1.26 | 0.75 | ref@86ea26ca3 | R11 (0b7c85b13) against 86ea26ca3: no clock read without a deadline; the outcome word claims a waiting sender, readied after the mailbox lock is released |
| 2026-10-18 | 0b7c85b13 | clean | windows | fan-in | 100000 | 12 | 3 | 921 | 1,059 | 1.15 | 1.36 | 0.76 | slot | R11 (0b7c85b13) against 86ea26ca3: no clock read without a deadline; the outcome word claims a waiting sender, readied after the mailbox lock is released |
| 2026-10-18 | 0b7c85b13 | clean | windows | shared-counter | 200000 | 1 | 3 | 542 | 812 | 1.50 | 1.53 | 0.75 | ref@86ea26ca3 | R11 (0b7c85b13) against 86ea26ca3: no clock read without a deadline; the outcome word claims a waiting sender, readied after the mailbox lock is released |
| 2026-10-18 | 0b7c85b13 | clean | windows | shared-counter | 200000 | 1 | 3 | 542 | 804 | 1.48 | 1.50 | 0.75 | slot | R11 (0b7c85b13) against 86ea26ca3: no clock read without a deadline; the outcome word claims a waiting sender, readied after the mailbox lock is released |
| 2026-10-18 | 0b7c85b13 | clean | windows | shared-counter | 200000 | 12 | 3 | 1,795 | 2,389 | 1.33 | 1.40 | 0.76 | ref@86ea26ca3 | R11 (0b7c85b13) against 86ea26ca3: no clock read without a deadline; the outcome word claims a waiting sender, readied after the mailbox lock is released |
| 2026-10-18 | 0b7c85b13 | clean | windows | shared-counter | 200000 | 12 | 3 | 1,795 | 2,238 | 1.25 | 1.44 | 0.76 | slot | R11 (0b7c85b13) against 86ea26ca3: no clock read without a deadline; the outcome word claims a waiting sender, readied after the mailbox lock is released |
| 2026-10-18 | 0b7c85b13 | dirty | wsl | fan-in | 100000 | 1 | 3 | 277 | 432 | 1.56 | 1.56 | 0.50 | ref@86ea26ca3 | R11 (0b7c85b13) against 86ea26ca3: no clock read without a deadline; the outcome word claims a waiting sender, readied after the mailbox lock is released |
| 2026-10-18 | 0b7c85b13 | dirty | wsl | fan-in | 100000 | 1 | 3 | 277 | 428 | 1.55 | 1.55 | 0.50 | slot | R11 (0b7c85b13) against 86ea26ca3: no clock read without a deadline; the outcome word claims a waiting sender, readied after the mailbox lock is released |
| 2026-10-18 | 0b7c85b13 | dirty | wsl | fan-in | 100000 | 12 | 3 | 614 | 2,489 | 4.05 | 4.42 | 0.56 | ref@86ea26ca3 | R11 (0b7c85b13) against 86ea26ca3: no clock read without a deadline; the outcome word claims a waiting sender, readied after the mailbox lock is released |
| 2026-10-18 | 0b7c85b13 | dirty | wsl | fan-in | 100000 | 12 | 3 | 614 | 1,215 | 1.98 | 3.06 | 0.56 | slot | R11 (0b7c85b13) against 86ea26ca3: no clock read without a deadline; the outcome word claims a waiting sender, readied after the mailbox lock is released |
| 2026-10-18 | 0b7c85b13 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 545 | 824 | 1.51 | 1.51 | 0.50 | ref@86ea26ca3 | R11 (0b7c85b13) against 86ea26ca3: no clock read without a deadline; the outcome word claims a waiting sender, readied after the mailbox lock is released |
| 2026-10-18 | 0b7c85b13 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 545 | 824 | 1.51 | 1.51 | 0.50 | slot | R11 (0b7c85b13) against 86ea26ca3: no clock read without a deadline; the outcome word claims a waiting sender, readied after the mailbox lock is released |
| 2026-10-18 | 0b7c85b13 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,224 | 4,969 | 4.06 | 4.39 | 0.56 | ref@86ea26ca3 | R11 (0b7c85b13) against 86ea26ca3: no clock read without a deadline; the outcome word claims a waiting sender, readied after the mailbox lock is released |
| 2026-10-18 | 0b7c85b13 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,224 | 2,467 | 2.02 | 3.06 | 0.56 | slot | R11 (0b7c85b13) against 86ea26ca3: no clock read without a deadline; the outcome word claims a waiting sender, readied after the mailbox lock is released |
| 2026-10-18 | 3f88cdd00 | clean | windows | thread-ring | 3000000 | 1 | 3 | 341 | 495 | 1.45 | 1.48 | 0.86 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | thread-ring | 3000000 | 1 | 3 | 341 | 429 | 1.26 | 1.29 | 0.86 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,349 | 799 | 0.59 | 0.11 | 0.79 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,349 | 718 | 0.53 | 0.10 | 0.79 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | ping-pong | 1000000 | 1 | 3 | 371 | 272 | 0.73 | 0.74 | 0.71 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | ping-pong | 1000000 | 1 | 3 | 371 | 273 | 0.74 | 0.74 | 0.71 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | ping-pong | 1000000 | 12 | 3 | 475 | 468 | 0.99 | 1.00 | 0.70 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | ping-pong | 1000000 | 12 | 3 | 475 | 473 | 0.99 | 0.98 | 0.70 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | fan-in | 100000 | 1 | 3 | 276 | 424 | 1.54 | 1.59 | 0.75 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | fan-in | 100000 | 1 | 3 | 276 | 291 | 1.05 | 1.12 | 0.75 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | fan-in | 100000 | 12 | 3 | 913 | 1,069 | 1.17 | 1.28 | 0.76 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | fan-in | 100000 | 12 | 3 | 913 | 1,086 | 1.19 | 1.20 | 0.74 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,117 | 2,117 | 1.00 | 1.01 | 1.30 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,117 | 2,118 | 1.00 | 1.01 | 1.30 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 211 | 257 | 1.22 | 1.01 | 1.20 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 211 | 245 | 1.16 | 0.99 | 1.20 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | spawn-many | 200000 | 1 | 3 | 250 | 599 | 2.39 | 2.53 | 6.79 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | spawn-many | 200000 | 1 | 3 | 250 | 595 | 2.38 | 2.53 | 6.79 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | spawn-many | 200000 | 12 | 3 | 57 | 208 | 3.64 | 3.25 | 7.16 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | spawn-many | 200000 | 12 | 3 | 57 | 206 | 3.62 | 3.25 | 7.19 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | coroutine-churn | 200000 | 1 | 3 | 335 | 478 | 1.43 | 1.36 | 4.36 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | coroutine-churn | 200000 | 1 | 3 | 335 | 464 | 1.39 | 1.32 | 4.36 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | coroutine-churn | 200000 | 12* | 3 | 334 | 463 | 1.39 | 1.38 | 4.37 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | coroutine-churn | 200000 | 12* | 3 | 334 | 468 | 1.40 | 1.43 | 4.37 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | shared-counter | 200000 | 1 | 3 | 545 | 814 | 1.49 | 1.49 | 0.75 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | shared-counter | 200000 | 1 | 3 | 545 | 571 | 1.05 | 1.06 | 0.75 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | shared-counter | 200000 | 12 | 3 | 1,786 | 2,078 | 1.16 | 1.35 | 0.75 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | clean | windows | shared-counter | 200000 | 12 | 3 | 1,786 | 2,087 | 1.17 | 1.21 | 0.73 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 360 | 461 | 1.28 | 1.28 | 1.28 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 360 | 401 | 1.11 | 1.12 | 1.28 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,027 | 526 | 0.51 | 0.19 | 1.15 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,027 | 461 | 0.45 | 0.17 | 1.11 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 360 | 268 | 0.74 | 0.74 | 0.29 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 360 | 270 | 0.75 | 0.75 | 0.29 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 359 | 309 | 0.86 | 0.85 | 0.31 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 359 | 317 | 0.88 | 0.87 | 0.31 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | fan-in | 100000 | 1 | 3 | 279 | 423 | 1.52 | 1.52 | 0.50 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | fan-in | 100000 | 1 | 3 | 279 | 289 | 1.04 | 1.04 | 0.50 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | fan-in | 100000 | 12 | 3 | 614 | 1,225 | 1.99 | 3.04 | 0.56 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | fan-in | 100000 | 12 | 3 | 614 | 1,048 | 1.71 | 2.17 | 0.56 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,122 | 2,123 | 1.00 | 1.00 | 2.13 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,122 | 2,127 | 1.00 | 1.00 | 2.13 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 197 | 232 | 1.18 | 1.06 | 2.00 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 197 | 248 | 1.26 | 1.15 | 2.00 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | spawn-many | 200000 | 1 | 3 | 164 | 507 | 3.08 | 3.10 | 6.16 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | spawn-many | 200000 | 1 | 3 | 164 | 493 | 3.00 | 3.01 | 6.16 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | spawn-many | 200000 | 12 | 3 | 50 | 224 | 4.51 | 4.79 | 11.59 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | spawn-many | 200000 | 12 | 3 | 50 | 228 | 4.59 | 4.65 | 11.64 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 255 | 325 | 1.27 | 1.29 | 2.74 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 255 | 324 | 1.27 | 1.29 | 2.74 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 253 | 357 | 1.41 | 1.42 | 2.66 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 253 | 326 | 1.29 | 1.29 | 2.66 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 547 | 816 | 1.49 | 1.49 | 0.50 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 547 | 572 | 1.05 | 1.05 | 0.50 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,222 | 2,424 | 1.98 | 3.07 | 0.56 | ref@3855fe76b | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | 3f88cdd00 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,222 | 2,060 | 1.69 | 2.16 | 0.56 | slot | R12 (3f88cdd00) against 3855fe76b: a handle reference counts only its box; the box owns the mailbox share |
| 2026-10-18 | f6f828188 | clean | windows | thread-ring | 3000000 | 1 | 3 | 341 | 432 | 1.27 | 1.23 | 0.86 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | thread-ring | 3000000 | 1 | 3 | 341 | 430 | 1.26 | 1.23 | 0.86 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,602 | 691 | 0.43 | 0.07 | 0.78 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,602 | 710 | 0.44 | 0.07 | 0.78 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | ping-pong | 1000000 | 1 | 3 | 359 | 271 | 0.75 | 0.74 | 0.70 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | ping-pong | 1000000 | 1 | 3 | 359 | 275 | 0.77 | 0.74 | 0.70 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | ping-pong | 1000000 | 12 | 3 | 498 | 500 | 1.01 | 0.89 | 0.69 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | ping-pong | 1000000 | 12 | 3 | 498 | 507 | 1.02 | 1.04 | 0.69 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | fan-in | 100000 | 1 | 3 | 274 | 288 | 1.05 | 1.06 | 0.75 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | fan-in | 100000 | 1 | 3 | 274 | 288 | 1.05 | 1.06 | 0.75 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | fan-in | 100000 | 12 | 3 | 951 | 1,098 | 1.15 | 1.39 | 0.73 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | fan-in | 100000 | 12 | 3 | 951 | 1,098 | 1.15 | 1.30 | 0.73 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,111 | 2,113 | 1.00 | 0.99 | 1.30 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,111 | 2,111 | 1.00 | 0.99 | 0.88 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 195 | 211 | 1.08 | 1.07 | 1.20 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 195 | 205 | 1.05 | 1.07 | 0.83 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | spawn-many | 200000 | 1 | 3 | 240 | 586 | 2.44 | 2.47 | 6.69 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | spawn-many | 200000 | 1 | 3 | 240 | 255 | 1.07 | 1.07 | 1.17 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | spawn-many | 200000 | 12 | 3 | 56 | 207 | 3.69 | 4.88 | 6.97 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | spawn-many | 200000 | 12 | 3 | 56 | 165 | 2.94 | 3.75 | 2.27 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | coroutine-churn | 200000 | 1 | 3 | 338 | 463 | 1.37 | 1.38 | 4.38 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | coroutine-churn | 200000 | 1 | 3 | 338 | 466 | 1.38 | 1.43 | 4.38 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | coroutine-churn | 200000 | 12* | 3 | 345 | 462 | 1.34 | 1.36 | 4.18 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | coroutine-churn | 200000 | 12* | 3 | 345 | 472 | 1.37 | 1.36 | 4.18 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | shared-counter | 200000 | 1 | 3 | 544 | 573 | 1.05 | 1.06 | 0.75 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | shared-counter | 200000 | 1 | 3 | 544 | 569 | 1.04 | 1.03 | 0.75 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | shared-counter | 200000 | 12 | 3 | 1,788 | 2,197 | 1.23 | 1.24 | 0.74 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | clean | windows | shared-counter | 200000 | 12 | 3 | 1,788 | 2,141 | 1.20 | 1.25 | 0.74 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 353 | 407 | 1.15 | 1.15 | 1.28 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 353 | 417 | 1.18 | 1.18 | 1.28 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,025 | 451 | 0.44 | 0.16 | 1.15 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,025 | 464 | 0.45 | 0.17 | 1.19 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 364 | 266 | 0.73 | 0.73 | 0.29 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 364 | 263 | 0.72 | 0.72 | 0.29 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 354 | 310 | 0.87 | 0.87 | 0.31 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 354 | 305 | 0.86 | 0.86 | 0.31 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | fan-in | 100000 | 1 | 3 | 271 | 288 | 1.06 | 1.06 | 0.50 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | fan-in | 100000 | 1 | 3 | 271 | 290 | 1.07 | 1.07 | 0.50 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | fan-in | 100000 | 12 | 3 | 619 | 1,044 | 1.69 | 2.16 | 0.61 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | fan-in | 100000 | 12 | 3 | 619 | 1,034 | 1.67 | 2.12 | 0.61 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,128 | 2,117 | 0.99 | 0.99 | 2.13 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,128 | 2,122 | 1.00 | 1.00 | 0.87 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 195 | 225 | 1.16 | 1.07 | 2.00 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 195 | 215 | 1.10 | 1.05 | 0.88 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | spawn-many | 200000 | 1 | 3 | 165 | 496 | 3.01 | 3.05 | 6.15 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | spawn-many | 200000 | 1 | 3 | 165 | 271 | 1.64 | 1.66 | 1.98 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 224 | 4.55 | 4.63 | 11.51 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 177 | 3.59 | 4.21 | 3.38 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 251 | 321 | 1.28 | 1.28 | 2.62 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 251 | 326 | 1.30 | 1.29 | 2.62 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 256 | 322 | 1.26 | 1.26 | 2.60 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 256 | 319 | 1.25 | 1.25 | 2.60 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 548 | 572 | 1.04 | 1.04 | 0.50 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | shared-counter | 200000 | 1 | 3 | 548 | 567 | 1.04 | 1.04 | 0.50 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,222 | 2,051 | 1.68 | 2.14 | 0.56 | ref@beb2e3015 | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-18 | f6f828188 | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,222 | 2,047 | 1.68 | 2.15 | 0.56 | slot | R13 (a4f9437b0) against beb2e3015: a spawn is a preemption point; a reply cell is a 144-byte prefix record |
| 2026-10-19 | be7bd857f | clean | windows | thread-ring | 3000000 | 1 | 3 | 339 | 430 | 1.27 | 1.33 | 0.86 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | thread-ring | 3000000 | 1 | 3 | 339 | 370 | 1.09 | 1.14 | 0.86 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,604 | 705 | 0.44 | 0.08 | 0.79 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,604 | 604 | 0.38 | 0.07 | 0.79 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | ping-pong | 1000000 | 1 | 3 | 359 | 273 | 0.76 | 0.74 | 0.70 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | ping-pong | 1000000 | 1 | 3 | 359 | 257 | 0.71 | 0.65 | 0.70 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | ping-pong | 1000000 | 12 | 3 | 492 | 503 | 1.02 | 1.10 | 0.68 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | ping-pong | 1000000 | 12 | 3 | 492 | 462 | 0.94 | 1.00 | 0.68 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | fan-in | 100000 | 1 | 3 | 273 | 289 | 1.06 | 1.00 | 0.75 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | fan-in | 100000 | 1 | 3 | 273 | 285 | 1.04 | 1.00 | 0.75 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | fan-in | 100000 | 12 | 3 | 955 | 1,068 | 1.12 | 1.20 | 0.73 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | fan-in | 100000 | 12 | 3 | 955 | 1,047 | 1.10 | 1.24 | 0.72 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,123 | 2,116 | 1.00 | 0.99 | 0.88 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,123 | 2,122 | 1.00 | 0.99 | 0.88 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 210 | 265 | 1.26 | 1.02 | 0.83 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 210 | 254 | 1.21 | 1.02 | 0.83 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | spawn-many | 200000 | 1 | 3 | 240 | 276 | 1.15 | 1.20 | 1.36 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | spawn-many | 200000 | 1 | 3 | 240 | 256 | 1.07 | 1.07 | 1.18 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | spawn-many | 200000 | 12 | 3 | 56 | 166 | 2.97 | 4.00 | 2.34 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | spawn-many | 200000 | 12 | 3 | 56 | 167 | 2.98 | 3.67 | 2.36 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | coroutine-churn | 200000 | 1 | 3 | 346 | 469 | 1.36 | 1.32 | 4.46 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | coroutine-churn | 200000 | 1 | 3 | 346 | 449 | 1.30 | 1.32 | 4.46 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | coroutine-churn | 200000 | 12* | 3 | 346 | 467 | 1.35 | 1.36 | 4.40 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | coroutine-churn | 200000 | 12* | 3 | 346 | 449 | 1.30 | 1.27 | 4.40 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | shared-counter | 200000 | 1 | 3 | 549 | 572 | 1.04 | 1.06 | 0.75 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | shared-counter | 200000 | 1 | 3 | 549 | 560 | 1.02 | 1.06 | 0.75 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | shared-counter | 200000 | 12 | 3 | 1,892 | 2,156 | 1.14 | 1.13 | 0.74 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | clean | windows | shared-counter | 200000 | 12 | 3 | 1,892 | 2,047 | 1.08 | 1.10 | 0.75 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 355 | 411 | 1.16 | 1.16 | 1.28 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 355 | 350 | 0.99 | 0.99 | 1.28 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,044 | 467 | 0.45 | 0.16 | 1.19 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,044 | 388 | 0.37 | 0.14 | 1.15 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 369 | 264 | 0.72 | 0.72 | 0.29 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 369 | 252 | 0.68 | 0.68 | 0.29 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 356 | 304 | 0.85 | 0.85 | 0.31 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 356 | 278 | 0.78 | 0.77 | 0.31 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | fan-in | 100000 | 1 | 3 | 272 | 286 | 1.05 | 1.05 | 0.50 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | fan-in | 100000 | 1 | 3 | 272 | 286 | 1.05 | 1.05 | 0.50 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | fan-in | 100000 | 12 | 3 | 616 | 1,051 | 1.71 | 2.19 | 0.56 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | fan-in | 100000 | 12 | 3 | 616 | 1,032 | 1.68 | 2.11 | 0.56 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,129 | 2,124 | 1.00 | 1.00 | 0.86 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,129 | 2,135 | 1.00 | 1.00 | 0.87 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 192 | 204 | 1.06 | 1.04 | 0.88 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 192 | 225 | 1.17 | 1.02 | 0.88 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | spawn-many | 200000 | 1 | 3 | 166 | 269 | 1.62 | 1.64 | 1.76 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | spawn-many | 200000 | 1 | 3 | 166 | 282 | 1.69 | 1.71 | 2.02 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 175 | 3.57 | 4.14 | 3.32 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 174 | 3.55 | 4.11 | 3.30 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 242 | 343 | 1.42 | 1.42 | 2.91 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 242 | 313 | 1.29 | 1.29 | 2.91 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 240 | 329 | 1.37 | 1.40 | 2.97 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 240 | 310 | 1.30 | 1.32 | 2.97 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | shared-counter | 200000 | 1 | 3 | 550 | 578 | 1.05 | 1.05 | 0.50 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | shared-counter | 200000 | 1 | 3 | 550 | 569 | 1.04 | 1.04 | 0.50 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,227 | 2,044 | 1.67 | 2.14 | 0.56 | ref@f76ee1893 | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | be7bd857f | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,227 | 2,047 | 1.67 | 2.14 | 0.50 | slot | R14 A (be7bd857f) against f76ee1893: a return that can follow a green-thread switch is an indirect jump |
| 2026-10-19 | dc6036eff | clean | windows | thread-ring | 3000000 | 1 | 3 | 341 | 369 | 1.08 | 1.05 | 0.86 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | thread-ring | 3000000 | 1 | 3 | 341 | 366 | 1.07 | 1.05 | 0.86 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,587 | 598 | 0.38 | 0.07 | 0.78 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,587 | 589 | 0.37 | 0.07 | 0.78 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | ping-pong | 1000000 | 1 | 3 | 360 | 256 | 0.71 | 0.70 | 0.70 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | ping-pong | 1000000 | 1 | 3 | 360 | 239 | 0.66 | 0.65 | 0.70 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | ping-pong | 1000000 | 12 | 3 | 484 | 456 | 0.94 | 0.87 | 0.68 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | ping-pong | 1000000 | 12 | 3 | 484 | 428 | 0.88 | 0.91 | 0.69 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | fan-in | 100000 | 1 | 3 | 273 | 285 | 1.04 | 1.06 | 0.75 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | fan-in | 100000 | 1 | 3 | 273 | 287 | 1.05 | 1.06 | 0.75 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | fan-in | 100000 | 12 | 3 | 936 | 1,029 | 1.10 | 1.16 | 0.73 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | fan-in | 100000 | 12 | 3 | 936 | 1,015 | 1.08 | 1.16 | 0.74 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,108 | 2,113 | 1.00 | 1.01 | 0.88 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,108 | 2,110 | 1.00 | 1.01 | 0.88 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 193 | 199 | 1.03 | 1.01 | 0.83 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 193 | 200 | 1.04 | 0.98 | 0.83 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | spawn-many | 200000 | 1 | 3 | 237 | 264 | 1.11 | 1.13 | 1.28 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | spawn-many | 200000 | 1 | 3 | 237 | 271 | 1.14 | 1.13 | 1.59 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | spawn-many | 200000 | 12 | 3 | 55 | 166 | 2.99 | 3.56 | 2.29 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | spawn-many | 200000 | 12 | 3 | 55 | 156 | 2.81 | 3.67 | 2.29 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | coroutine-churn | 200000 | 1 | 3 | 334 | 439 | 1.31 | 1.33 | 4.37 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | coroutine-churn | 200000 | 1 | 3 | 334 | 435 | 1.30 | 1.33 | 4.37 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | coroutine-churn | 200000 | 12* | 3 | 340 | 445 | 1.31 | 1.27 | 4.30 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | coroutine-churn | 200000 | 12* | 3 | 340 | 435 | 1.28 | 1.27 | 4.30 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | shared-counter | 200000 | 1 | 3 | 544 | 562 | 1.03 | 1.03 | 0.75 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | shared-counter | 200000 | 1 | 3 | 544 | 563 | 1.03 | 1.03 | 0.75 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | shared-counter | 200000 | 12 | 3 | 1,836 | 2,069 | 1.13 | 1.10 | 0.73 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | clean | windows | shared-counter | 200000 | 12 | 3 | 1,836 | 2,063 | 1.12 | 1.12 | 0.73 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 351 | 354 | 1.01 | 1.01 | 1.28 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 351 | 353 | 1.01 | 1.01 | 1.28 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,021 | 396 | 0.39 | 0.14 | 1.11 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,021 | 388 | 0.38 | 0.14 | 1.14 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 366 | 250 | 0.68 | 0.68 | 0.29 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 366 | 238 | 0.65 | 0.65 | 0.29 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 354 | 276 | 0.78 | 0.77 | 0.31 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 354 | 261 | 0.74 | 0.73 | 0.31 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | fan-in | 100000 | 1 | 3 | 271 | 292 | 1.08 | 1.08 | 0.50 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | fan-in | 100000 | 1 | 3 | 271 | 287 | 1.06 | 1.06 | 0.50 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | fan-in | 100000 | 12 | 3 | 611 | 1,032 | 1.69 | 2.15 | 0.50 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | fan-in | 100000 | 12 | 3 | 611 | 1,035 | 1.69 | 2.16 | 0.50 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,113 | 2,117 | 1.00 | 1.00 | 0.86 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,113 | 2,113 | 1.00 | 1.00 | 0.87 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 197 | 218 | 1.11 | 0.97 | 0.88 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 197 | 216 | 1.10 | 0.97 | 0.88 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | spawn-many | 200000 | 1 | 3 | 166 | 261 | 1.58 | 1.58 | 1.92 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | spawn-many | 200000 | 1 | 3 | 166 | 262 | 1.58 | 1.59 | 2.05 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 173 | 3.50 | 3.96 | 3.39 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 164 | 3.32 | 3.57 | 3.40 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 245 | 309 | 1.26 | 1.27 | 2.66 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 245 | 302 | 1.23 | 1.24 | 2.66 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 252 | 316 | 1.25 | 1.26 | 2.78 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 252 | 310 | 1.23 | 1.23 | 2.78 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | shared-counter | 200000 | 1 | 3 | 545 | 569 | 1.04 | 1.04 | 0.50 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | shared-counter | 200000 | 1 | 3 | 545 | 568 | 1.04 | 1.04 | 0.50 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,229 | 2,046 | 1.67 | 2.12 | 0.56 | ref@8b45db473 | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | dc6036eff | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,229 | 2,037 | 1.66 | 2.08 | 0.56 | slot | R14 B (31240514a + dc6036eff) against 8b45db473: spawn zeroes only what a reader needs; per-machine live count, quiesce at the all-idle moment |
| 2026-10-19 | 8473d60ac | clean | windows | thread-ring | 3000000 | 1 | 3 | 340 | 372 | 1.09 | 1.09 | 0.86 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | thread-ring | 3000000 | 1 | 3 | 340 | 381 | 1.12 | 1.09 | 0.85 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,598 | 595 | 0.37 | 0.06 | 0.78 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | thread-ring | 3000000 | 12 | 3 | 1,598 | 620 | 0.39 | 0.07 | 0.78 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | ping-pong | 1000000 | 1 | 3 | 358 | 239 | 0.67 | 0.65 | 0.70 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | ping-pong | 1000000 | 1 | 3 | 358 | 240 | 0.67 | 0.65 | 0.70 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | ping-pong | 1000000 | 12 | 3 | 484 | 436 | 0.90 | 0.94 | 0.69 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | ping-pong | 1000000 | 12 | 3 | 484 | 435 | 0.90 | 0.89 | 0.68 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | fan-in | 100000 | 1 | 3 | 273 | 287 | 1.05 | 1.06 | 0.75 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | fan-in | 100000 | 1 | 3 | 273 | 280 | 1.03 | 1.06 | 0.74 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | fan-in | 100000 | 12 | 3 | 951 | 1,069 | 1.12 | 1.08 | 0.72 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | fan-in | 100000 | 12 | 3 | 951 | 1,002 | 1.05 | 1.05 | 0.72 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,116 | 2,117 | 1.00 | 1.00 | 0.88 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | worker-pool | 6000 250000 | 1 | 3 | 2,116 | 2,115 | 1.00 | 1.00 | 0.88 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 193 | 194 | 1.01 | 1.00 | 0.82 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | worker-pool | 6000 250000 | 12 | 3 | 193 | 250 | 1.29 | 1.02 | 0.82 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | spawn-many | 200000 | 1 | 3 | 239 | 253 | 1.06 | 1.00 | 1.32 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | spawn-many | 200000 | 1 | 3 | 239 | 217 | 0.91 | 0.88 | 1.46 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | spawn-many | 200000 | 12 | 3 | 55 | 155 | 2.81 | 2.82 | 2.25 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | spawn-many | 200000 | 12 | 3 | 55 | 116 | 2.10 | 2.18 | 2.31 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | coroutine-churn | 200000 | 1 | 3 | 335 | 436 | 1.30 | 1.33 | 4.33 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | coroutine-churn | 200000 | 1 | 3 | 335 | 437 | 1.30 | 1.33 | 4.33 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | coroutine-churn | 200000 | 12* | 3 | 339 | 434 | 1.28 | 1.23 | 4.37 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | coroutine-churn | 200000 | 12* | 3 | 339 | 443 | 1.31 | 1.27 | 4.37 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | shared-counter | 200000 | 1 | 3 | 547 | 563 | 1.03 | 1.03 | 0.75 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | shared-counter | 200000 | 1 | 3 | 547 | 544 | 1.00 | 1.00 | 0.74 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | shared-counter | 200000 | 12 | 3 | 1,825 | 2,031 | 1.11 | 1.22 | 0.75 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | clean | windows | shared-counter | 200000 | 12 | 3 | 1,825 | 1,976 | 1.08 | 1.19 | 0.74 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 353 | 355 | 1.00 | 1.00 | 1.28 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | thread-ring | 3000000 | 1 | 3 | 353 | 367 | 1.04 | 1.04 | 1.28 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,026 | 397 | 0.39 | 0.14 | 1.15 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | thread-ring | 3000000 | 12 | 3 | 1,026 | 411 | 0.40 | 0.15 | 1.19 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 361 | 239 | 0.66 | 0.66 | 0.29 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | ping-pong | 1000000 | 1 | 3 | 361 | 241 | 0.67 | 0.67 | 0.29 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 356 | 263 | 0.74 | 0.73 | 0.31 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | ping-pong | 1000000 | 12 | 3 | 356 | 270 | 0.76 | 0.75 | 0.31 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | fan-in | 100000 | 1 | 3 | 273 | 287 | 1.05 | 1.05 | 0.50 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | fan-in | 100000 | 1 | 3 | 273 | 282 | 1.03 | 1.03 | 0.50 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | fan-in | 100000 | 12 | 3 | 615 | 1,026 | 1.67 | 2.12 | 0.59 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | fan-in | 100000 | 12 | 3 | 615 | 1,009 | 1.64 | 2.09 | 0.53 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,124 | 2,130 | 1.00 | 1.00 | 0.87 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | worker-pool | 6000 250000 | 1 | 3 | 2,124 | 2,110 | 0.99 | 0.99 | 0.87 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 194 | 216 | 1.12 | 0.98 | 0.88 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | worker-pool | 6000 250000 | 12 | 3 | 194 | 202 | 1.04 | 0.97 | 0.88 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | spawn-many | 200000 | 1 | 3 | 165 | 253 | 1.53 | 1.55 | 1.78 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | spawn-many | 200000 | 1 | 3 | 165 | 216 | 1.31 | 1.32 | 1.73 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 166 | 3.37 | 3.81 | 3.36 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | spawn-many | 200000 | 12 | 3 | 49 | 132 | 2.68 | 3.16 | 3.45 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 240 | 302 | 1.26 | 1.26 | 2.98 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | coroutine-churn | 200000 | 1 | 3 | 240 | 302 | 1.26 | 1.25 | 2.98 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 251 | 309 | 1.23 | 1.25 | 2.60 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | coroutine-churn | 200000 | 12* | 3 | 251 | 303 | 1.21 | 1.21 | 2.60 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | shared-counter | 200000 | 1 | 3 | 551 | 568 | 1.03 | 1.03 | 0.50 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | shared-counter | 200000 | 1 | 3 | 551 | 545 | 0.99 | 0.99 | 0.50 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,253 | 2,078 | 1.66 | 2.12 | 0.56 | ref@4b2f7a699 | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-19 | 8473d60ac | dirty | wsl | shared-counter | 200000 | 12 | 3 | 1,253 | 2,026 | 1.62 | 2.07 | 0.56 | slot | R15 (16a959747 + f42e3815b + 8473d60ac) against 4b2f7a699: inbox-first send, a stable stack seed, batched cells / one-step collect / packed handle counts |
| 2026-10-22 | 947cd1414 | clean | macos | thread-ring | 3000000 | 1 | 5 | 160 | 215 | 1.34 | 1.34 | 0.78 | slot | arm64 catch-up: first macos measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | clean | macos | thread-ring | 3000000 | 8 | 5 | 794 | 253 | 0.32 | 0.14 | 1.01 | slot | arm64 catch-up: first macos measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | clean | macos | ping-pong | 1000000 | 1 | 5 | 186 | 138 | 0.74 | 0.74 | 0.55 | slot | arm64 catch-up: first macos measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | clean | macos | ping-pong | 1000000 | 8 | 5 | 195 | 156 | 0.80 | 0.77 | 0.53 | slot | arm64 catch-up: first macos measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | clean | macos | fan-in | 100000 | 1 | 5 | 135 | 86 | 0.64 | 0.63 | 0.59 | slot | arm64 catch-up: first macos measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | clean | macos | fan-in | 100000 | 8 | 5 | 427 | 405 | 0.95 | 0.85 | 0.69 | slot | arm64 catch-up: first macos measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | clean | macos | worker-pool | 6000 250000 | 1 | 5 | 2,430 | 2,432 | 1.00 | 1.00 | 0.85 | slot | arm64 catch-up: first macos measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | clean | macos | worker-pool | 6000 250000 | 8 | 5 | 414 | 422 | 1.02 | 1.00 | 0.89 | slot | arm64 catch-up: first macos measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | clean | macos | spawn-many | 200000 | 1 | 5 | 105 | 119 | 1.14 | 1.10 | 2.06 | slot | arm64 catch-up: first macos measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | clean | macos | spawn-many | 200000 | 8 | 5 | 31 | 66 | 2.11 | 2.30 | 3.16 | slot | arm64 catch-up: first macos measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | clean | macos | coroutine-churn | 200000 | 1 | 5 | 181 | 106 | 0.58 | 0.57 | 1.45 | slot | arm64 catch-up: first macos measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | clean | macos | coroutine-churn | 200000 | 8* | 5 | 179 | 107 | 0.60 | 0.58 | 1.43 | slot | arm64 catch-up: first macos measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | clean | macos | shared-counter | 200000 | 1 | 5 | 256 | 165 | 0.64 | 0.64 | 0.58 | slot | arm64 catch-up: first macos measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | clean | macos | shared-counter | 200000 | 8 | 5 | 885 | 800 | 0.90 | 0.79 | 0.68 | slot | arm64 catch-up: first macos measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | dirty | orb | thread-ring | 3000000 | 1 | 5 | 162 | 216 | 1.34 | 1.34 | 0.79 | slot | arm64 catch-up: first orb measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | dirty | orb | thread-ring | 3000000 | 7 | 5 | 266 | 205 | 0.77 | 0.46 | 0.95 | slot | arm64 catch-up: first orb measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | dirty | orb | ping-pong | 1000000 | 1 | 5 | 182 | 148 | 0.81 | 0.81 | 0.33 | slot | arm64 catch-up: first orb measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | dirty | orb | ping-pong | 1000000 | 7 | 5 | 161 | 139 | 0.87 | 0.86 | 0.33 | slot | arm64 catch-up: first orb measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | dirty | orb | fan-in | 100000 | 1 | 5 | 125 | 92 | 0.73 | 0.73 | 0.43 | slot | arm64 catch-up: first orb measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | dirty | orb | fan-in | 100000 | 7 | 5 | 153 | 112 | 0.73 | 0.73 | 0.57 | slot | arm64 catch-up: first orb measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | dirty | orb | worker-pool | 6000 250000 | 1 | 5 | 2,436 | 2,435 | 1.00 | 1.00 | 0.86 | slot | arm64 catch-up: first orb measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | dirty | orb | worker-pool | 6000 250000 | 7 | 5 | 418 | 437 | 1.04 | 1.00 | 0.87 | slot | arm64 catch-up: first orb measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | dirty | orb | spawn-many | 200000 | 1 | 5 | 97 | 107 | 1.10 | 1.10 | 1.71 | slot | arm64 catch-up: first orb measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | dirty | orb | spawn-many | 200000 | 7 | 5 | 54 | 58 | 1.07 | 1.17 | 1.97 | slot | arm64 catch-up: first orb measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | dirty | orb | coroutine-churn | 200000 | 1 | 5 | 159 | 106 | 0.66 | 0.66 | 1.73 | slot | arm64 catch-up: first orb measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | dirty | orb | coroutine-churn | 200000 | 7* | 5 | 160 | 105 | 0.66 | 0.66 | 1.63 | slot | arm64 catch-up: first orb measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | dirty | orb | shared-counter | 200000 | 1 | 5 | 255 | 173 | 0.68 | 0.68 | 0.40 | slot | arm64 catch-up: first orb measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
| 2026-10-22 | 947cd1414 | dirty | orb | shared-counter | 200000 | 7 | 5 | 300 | 214 | 0.71 | 0.74 | 0.58 | slot | arm64 catch-up: first orb measurement since R8 (R9-R17 landed unmeasured on arm64), go1.27.3 |
