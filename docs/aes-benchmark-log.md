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
| 2026-10-30 | 8962b6bff | clean | x64 | Windows AMD64 | 5 | ref@d7b94669e | keysetup-128 | 8.24 us | 2.13 us | 0.60 us | 0.15 us | 3.87 | 13.76 | 55.60 | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | clean | x64 | Windows AMD64 | 5 | ref@d7b94669e | block-128 | 6.4 MB/s | 16.5 MB/s | 220.5 MB/s | 716.9 MB/s | 2.59 | 34.55 | 112.33 | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | clean | x64 | Windows AMD64 | 5 | ref@d7b94669e | ctr-128-16k | 8.9 MB/s | 50.0 MB/s | 247.1 MB/s | 4064.8 MB/s | 5.60 | 27.69 | 455.66 | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | clean | x64 | Windows AMD64 | 5 | ref@d7b94669e | ctr-256-16k | 8.3 MB/s | 39.8 MB/s | 182.8 MB/s | 3589.1 MB/s | 4.80 | 22.08 | 433.57 | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | clean | x64 | Windows AMD64 | 5 | ref@d7b94669e | gcm-128-seal-64 | 5.0 MB/s | 22.0 MB/s | 44.7 MB/s | 529.7 MB/s | 4.37 | 8.88 | 105.23 | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | clean | x64 | Windows AMD64 | 5 | ref@d7b94669e | gcm-128-seal-1k | 7.8 MB/s | 38.3 MB/s | 69.7 MB/s | 3415.2 MB/s | 4.91 | 8.94 | 437.84 | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | clean | x64 | Windows AMD64 | 5 | ref@d7b94669e | gcm-128-seal-16k | 8.0 MB/s | 40.4 MB/s | 72.7 MB/s | 4099.4 MB/s | 5.03 | 9.04 | 509.40 | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | clean | x64 | Windows AMD64 | 5 | ref@d7b94669e | gcm-128-open-16k | 8.1 MB/s | 40.5 MB/s | 72.5 MB/s | 4650.6 MB/s | 5.00 | 8.95 | 574.27 | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | clean | x64 | Windows AMD64 | 5 | ref@d7b94669e | gcm-256-seal-16k | 7.6 MB/s | 33.6 MB/s | 65.8 MB/s | 3607.3 MB/s | 4.43 | 8.69 | 475.88 | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | clean | x64 | Windows AMD64 | 5 | slot | keysetup-128 | 8.22 us | 2.13 us | 0.60 us | 0.15 us | 3.86 | 13.72 | 55.44 | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | clean | x64 | Windows AMD64 | 5 | slot | block-128 | 6.4 MB/s | 16.5 MB/s | 220.5 MB/s | 716.9 MB/s | 2.58 | 34.44 | 111.95 | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | clean | x64 | Windows AMD64 | 5 | slot | ctr-128-16k | 8.9 MB/s | 50.0 MB/s | 247.1 MB/s | 4064.8 MB/s | 5.62 | 27.80 | 457.34 | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | clean | x64 | Windows AMD64 | 5 | slot | ctr-256-16k | 8.3 MB/s | 39.8 MB/s | 182.8 MB/s | 3589.1 MB/s | 4.80 | 22.07 | 433.42 | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | clean | x64 | Windows AMD64 | 5 | slot | gcm-128-seal-64 | 5.0 MB/s | 22.0 MB/s | 44.7 MB/s | 529.7 MB/s | 4.40 | 8.93 | 105.79 | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | clean | x64 | Windows AMD64 | 5 | slot | gcm-128-seal-1k | 7.8 MB/s | 38.3 MB/s | 69.7 MB/s | 3415.2 MB/s | 4.91 | 8.93 | 437.72 | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | clean | x64 | Windows AMD64 | 5 | slot | gcm-128-seal-16k | 8.1 MB/s | 40.4 MB/s | 72.7 MB/s | 4099.4 MB/s | 5.00 | 8.99 | 506.59 | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | clean | x64 | Windows AMD64 | 5 | slot | gcm-128-open-16k | 8.1 MB/s | 40.5 MB/s | 72.5 MB/s | 4650.6 MB/s | 5.02 | 8.98 | 575.99 | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | clean | x64 | Windows AMD64 | 5 | slot | gcm-256-seal-16k | 7.6 MB/s | 33.6 MB/s | 65.8 MB/s | 3607.3 MB/s | 4.43 | 8.69 | 476.28 | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | dirty | wasm | Windows AMD64, wasmtime 44.0.0 (af382d7d9 2026-04-20) | 5 | ref@d7b94669e | keysetup-128 | 45.84 us | 4.67 us | 1.35 us | - | 9.82 | 34.03 | - | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | dirty | wasm | Windows AMD64, wasmtime 44.0.0 (af382d7d9 2026-04-20) | 5 | ref@d7b94669e | block-128 | 1.0 MB/s | 7.9 MB/s | 61.0 MB/s | - | 7.76 | 59.96 | - | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | dirty | wasm | Windows AMD64, wasmtime 44.0.0 (af382d7d9 2026-04-20) | 5 | ref@d7b94669e | ctr-128-16k | 1.7 MB/s | 23.0 MB/s | 78.8 MB/s | - | 13.57 | 46.41 | - | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | dirty | wasm | Windows AMD64, wasmtime 44.0.0 (af382d7d9 2026-04-20) | 5 | ref@d7b94669e | ctr-256-16k | 1.5 MB/s | 18.8 MB/s | 61.5 MB/s | - | 12.47 | 40.73 | - | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | dirty | wasm | Windows AMD64, wasmtime 44.0.0 (af382d7d9 2026-04-20) | 5 | ref@d7b94669e | gcm-128-seal-64 | 0.9 MB/s | 10.4 MB/s | 15.2 MB/s | - | 11.83 | 17.28 | - | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | dirty | wasm | Windows AMD64, wasmtime 44.0.0 (af382d7d9 2026-04-20) | 5 | ref@d7b94669e | gcm-128-seal-1k | 1.5 MB/s | 18.4 MB/s | 24.4 MB/s | - | 12.44 | 16.51 | - | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | dirty | wasm | Windows AMD64, wasmtime 44.0.0 (af382d7d9 2026-04-20) | 5 | ref@d7b94669e | gcm-128-seal-16k | 1.6 MB/s | 19.4 MB/s | 25.8 MB/s | - | 12.51 | 16.57 | - | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | dirty | wasm | Windows AMD64, wasmtime 44.0.0 (af382d7d9 2026-04-20) | 5 | ref@d7b94669e | gcm-128-open-16k | 1.6 MB/s | 19.6 MB/s | 25.5 MB/s | - | 12.62 | 16.42 | - | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | dirty | wasm | Windows AMD64, wasmtime 44.0.0 (af382d7d9 2026-04-20) | 5 | ref@d7b94669e | gcm-256-seal-16k | 1.4 MB/s | 16.2 MB/s | 23.4 MB/s | - | 11.72 | 16.89 | - | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | dirty | wasm | Windows AMD64, wasmtime 44.0.0 (af382d7d9 2026-04-20) | 5 | slot | keysetup-128 | 46.21 us | 4.67 us | 1.35 us | - | 9.90 | 34.30 | - | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | dirty | wasm | Windows AMD64, wasmtime 44.0.0 (af382d7d9 2026-04-20) | 5 | slot | block-128 | 1.0 MB/s | 7.9 MB/s | 61.0 MB/s | - | 7.83 | 60.53 | - | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | dirty | wasm | Windows AMD64, wasmtime 44.0.0 (af382d7d9 2026-04-20) | 5 | slot | ctr-128-16k | 1.7 MB/s | 23.0 MB/s | 78.8 MB/s | - | 13.53 | 46.28 | - | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | dirty | wasm | Windows AMD64, wasmtime 44.0.0 (af382d7d9 2026-04-20) | 5 | slot | ctr-256-16k | 1.5 MB/s | 18.8 MB/s | 61.5 MB/s | - | 12.44 | 40.63 | - | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | dirty | wasm | Windows AMD64, wasmtime 44.0.0 (af382d7d9 2026-04-20) | 5 | slot | gcm-128-seal-64 | 0.9 MB/s | 10.4 MB/s | 15.2 MB/s | - | 11.83 | 17.28 | - | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | dirty | wasm | Windows AMD64, wasmtime 44.0.0 (af382d7d9 2026-04-20) | 5 | slot | gcm-128-seal-1k | 1.5 MB/s | 18.4 MB/s | 24.4 MB/s | - | 12.46 | 16.53 | - | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | dirty | wasm | Windows AMD64, wasmtime 44.0.0 (af382d7d9 2026-04-20) | 5 | slot | gcm-128-seal-16k | 1.6 MB/s | 19.4 MB/s | 25.8 MB/s | - | 12.52 | 16.59 | - | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | dirty | wasm | Windows AMD64, wasmtime 44.0.0 (af382d7d9 2026-04-20) | 5 | slot | gcm-128-open-16k | 1.6 MB/s | 19.6 MB/s | 25.5 MB/s | - | 12.59 | 16.38 | - | C1 withIterator frame-local tuple elided (control: d7b94669e) |
| 2026-10-30 | 8962b6bff | dirty | wasm | Windows AMD64, wasmtime 44.0.0 (af382d7d9 2026-04-20) | 5 | slot | gcm-256-seal-16k | 1.4 MB/s | 16.2 MB/s | 23.4 MB/s | - | 11.75 | 16.93 | - | C1 withIterator frame-local tuple elided (control: d7b94669e) |
