# AES benchmark log

Best-of time of the stdlib AES and AES-GCM (`bench/aes/aes.maxon`) against Go, per workload and lane,
by `scripts/bench-aes.py`. **Read the tables downwards**: each row is one measurement of one workload,
dated, and the ratio columns are the numbers the AES parity work drives to 1.0 or below.

- **Rows are appended by the tool, never by hand.** A run without `--note` records nothing; a run with
  one records unconditionally. The note says WHY the number moved - the instrument cannot.
- **This is an instrument, not a gate.** There is nothing to pass; a row that did not move is a
  datapoint, and a row that moved the wrong way is a reading to explain.
- **Rows rot.** Re-measure before planning from one; an A/B is two arms in ONE session, interleaved,
  never a before/after across a source change.
- **Only rows from one lane and host compare.** Each row names both.

Columns: `Maxon` and the Go columns are the best time of `runs` interleaved process runs, each of which
already took the best of 5 repetitions: MB/s, or microseconds per operation for key setup. `vs ...` is the
Maxon time over that Go arm's time, so below 1.0 Maxon is faster. The Go arms are `bitsliced` (a
function-for-function Go port of the stdlib algorithm, `bench/aes/go/bitsliced.go`), `portable` (`-tags purego`
on x64 and arm64, Go's own wasm build on wasm: the table AES with constant-time GHASH) and `hardware`
(Go's default build: AES-NI and PCLMULQDQ on x64, ARMv8 Crypto on arm64; absent on wasm). `arm` names the
Maxon compiler (`slot`, `ref@<sha>`, or a `--label`); `tree` is whether the harness's checkout was clean.

| date | head | tree | lane | host | runs | arm | workload | Maxon | Go bitsliced | Go portable | Go hardware | vs bitsliced | vs portable | vs hardware | note |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
