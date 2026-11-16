# Hash benchmark log

Best-of time of the stdlib's seeded hash and SHA-256 (`bench/hash/hash.maxon`) against Go
(`hash/maphash`, `map`, `crypto/sha256`), per workload and hash tier, by `scripts/bench-hash.py`.
**Read the tables downwards**: each row is one measurement of one workload on one tier, dated, and
`vs Go` is the number the hash work drives to 1.0 or below.

- **Rows are appended by the tool, never by hand.** A run without `--note` records nothing; a run with
  one records unconditionally. The note says WHY the number moved - the instrument cannot.
- **This is an instrument, not a gate.** There is nothing to pass; a row that did not move is a
  datapoint, and a row that moved the wrong way is a reading to explain.
- **Rows rot.** Re-measure before planning from one; an A/B is two arms in ONE session, interleaved,
  never a before/after across a source change.
- **Only rows from one host compare.** Each row names it: the OS, the machine, the CPU model and the Go
  version it ran. The hash tiers a host has depend on its CPU.

Columns: `Maxon` and `Go` are the best time of `runs` interleaved process runs, each of which already
took the best of 5 repetitions: MB/s for a workload that hashes bytes, or nanoseconds per operation for
one that does not. `vs Go` is the Maxon time over the time of the Go arm named in `Go arm`, so below 1.0
Maxon is faster. `tier` is the value `MAXON_HASH_TIER` forced for the row's Maxon run, and each tier is
compared with the Go arm that runs the same kind of hash: `aes` with `go`, Go as it runs, and `software`
with `go-software`, the same Go binary run with `GODEBUG=cpu.aes=off`, so Go's runtime hash (maps,
`hash/maphash`) is its portable code. `MAXON_HASH_TIER` changes Maxon's seeded hash only, and `cpu.aes=off`
does not touch SHA-256, so the two Maxon cells of `sha256-1m` and its two Go cells each differ by noise
alone. Maxon's `int` hash is not seeded and has no tier, so the two Maxon cells of `set-int-*` differ by
noise too, but Go's `map[int]` hashes with the runtime's `memhash64`, which follows `cpu.aes`: its two Go
cells, and so the two ratios, differ. `arm` names the Maxon compiler (`slot`, `ref@<sha>`, or a
`--label`); `tree` is whether the harness's checkout was clean.

| date | head | tree | host | runs | arm | tier | workload | Maxon | Go arm | Go | vs Go | note |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | aes | string-8 | 1225.1 MB/s | go | 1909.6 MB/s | 1.56 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | aes | string-16 | 2482.2 MB/s | go | 4077.3 MB/s | 1.64 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | aes | string-64 | 9672.7 MB/s | go | 12248.1 MB/s | 1.27 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | aes | string-1k | 39562.4 MB/s | go | 13740.4 MB/s | 0.35 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | aes | string-64k | 57752.9 MB/s | go | 12692.6 MB/s | 0.22 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | aes | map-string-insert | 660.98 ns | go | 166.10 ns | 3.98 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | aes | map-string-lookup | 384.58 ns | go | 32.77 ns | 11.74 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | aes | set-int-insert | 98.27 ns | go | 90.49 ns | 1.09 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | aes | set-int-contains | 30.31 ns | go | 26.76 ns | 1.13 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | aes | array-hash-16 | 14702.3 MB/s | go | 19541.2 MB/s | 1.33 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | aes | array-hash-1024 | 53914.0 MB/s | go | 13087.5 MB/s | 0.24 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | aes | hasher-fields | 18.98 ns | go | 15.95 ns | 1.19 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | aes | hasher-object | 22.61 ns | go | 16.10 ns | 1.40 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | aes | sha256-1m | 1936.9 MB/s | go | 1959.2 MB/s | 1.01 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | software | string-8 | 950.9 MB/s | go-software | 1775.5 MB/s | 1.87 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | software | string-16 | 1856.0 MB/s | go-software | 3643.0 MB/s | 1.96 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | software | string-64 | 4556.6 MB/s | go-software | 8839.9 MB/s | 1.94 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | software | string-1k | 7846.4 MB/s | go-software | 14826.4 MB/s | 1.89 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | software | string-64k | 8582.3 MB/s | go-software | 13922.2 MB/s | 1.62 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | software | map-string-insert | 747.08 ns | go-software | 192.41 ns | 3.88 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | software | map-string-lookup | 391.59 ns | go-software | 36.76 ns | 10.65 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | software | set-int-insert | 97.17 ns | go-software | 86.58 ns | 1.12 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | software | set-int-contains | 29.41 ns | go-software | 25.45 ns | 1.16 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | software | array-hash-16 | 5631.1 MB/s | go-software | 13069.5 MB/s | 2.32 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | software | array-hash-1024 | 8495.6 MB/s | go-software | 14156.5 MB/s | 1.67 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | software | hasher-fields | 22.73 ns | go-software | 15.14 ns | 1.50 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | software | hasher-object | 29.10 ns | go-software | 15.35 ns | 1.90 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
| 2026-11-16 | 2ef2aee | dirty | Linux x86_64, Intel(R) Xeon(R) Processor @ 2.30GHz, go1.24.7 | 5 | slot | software | sha256-1m | 1929.1 MB/s | go-software | 1903.8 MB/s | 0.99 | First reading: seeded hash (aes and software tiers) against Go 1.24 maphash, Go default and GODEBUG=cpu.aes=off |
