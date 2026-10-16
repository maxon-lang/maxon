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
