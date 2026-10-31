# AES: Maxon against Go

An instrument for one question: how fast are the stdlib's `Aes` and `AesGcm` against Go's, on the same
work, on the same machine, on each lane? One Maxon program (`aes.maxon`) and one Go program (`go/`) run
the same nine workloads and print the same lines. `scripts/bench-aes.py` builds both, verifies that every
arm prints the same iteration counts and digests, runs the arms interleaved and reports each workload's
throughput with the Maxon/Go ratio against every Go arm. The ratios are the numbers the AES parity work
drives to 1.0 or below; rows are logged in `docs/aes-benchmark-log.md`.

It is an INSTRUMENT: it exits 0 whatever the ratio is. Non-zero means the measurement itself is invalid.

## Layout

```
bench/aes/
  aes.maxon            the Maxon program
  go/main.go           the Go program: the `std` arm (crypto/aes, crypto/cipher) and the `bitsliced` arm
  go/bitsliced.go      the stdlib's bitsliced AES and GHASH, in Go
  go/go.mod            the Go module (maxon.dev/bench/aes), which needs Go 1.27 or newer
  out/                 builds, ref worktrees and profiles (gitignored)
tests/examples/bench-aes.maxtest
                       builds aes.maxon and checks every line but the time against Go crypto/aes's
                       output at a small size (correctness only)
```

The Maxon program is guarded by `./maxon-bin/.maxon/maxon test tests/examples --filter=bench-aes`.

## The workloads

Every input is a fixed byte pattern, identical in both languages: a 128-bit and a 256-bit key, a 12-byte
nonce, a 13-byte AAD, and 64-byte, 1 KiB and 16 KiB messages.

| Workload | Bytes per op | Base iterations | One op |
|---|---|---|---|
| `keysetup-128` | 0 | 20000 | expand a 128-bit key, then encrypt one block with it |
| `block-128` | 16 | 200000 | encrypt one block with AES-128; each op encrypts the previous op's output |
| `ctr-128-16k` | 16384 | 1000 | AES-128 CTR over 16 KiB, counter 2 |
| `ctr-256-16k` | 16384 | 1000 | AES-256 CTR over 16 KiB, counter 2 |
| `gcm-128-seal-64` | 64 | 100000 | AES-128-GCM seal of 64 bytes |
| `gcm-128-seal-1k` | 1024 | 10000 | AES-128-GCM seal of 1 KiB |
| `gcm-128-seal-16k` | 16384 | 1000 | AES-128-GCM seal of 16 KiB |
| `gcm-128-open-16k` | 16384 | 1000 | AES-128-GCM open of a sealed 16 KiB message |
| `gcm-256-seal-16k` | 16384 | 1000 | AES-256-GCM seal of 16 KiB |

## The programs

```
aes [repeats [divisor]]                       the Maxon program; both default to 5 and 1
aes std|bitsliced [repeats [divisor]]         the Go program
```

Each workload runs `base iterations / divisor` ops (at least 1), `repeats` times, and prints one line:

```
workload bytesPerOp iterations repeats bestNanos digest
```

`bestNanos` is the fastest of the repeats. `digest` sums each byte of the last op's output times its
1-based position, modulo 2^24, so two arms that computed different bytes print different lines.

On wasm the Maxon program reads no arguments and runs 5 repeats at divisor 10. ⚠ **These are coupled to
the harness:** `defaultRepeats` and `wasmDivisor` in `aes.maxon` equal `REPEATS` and `WASM_DIVISOR` in
`scripts/bench-aes.py`, which hands them to the Go arms. A mismatch fails the run's agreement check on
iterations and repeats.

## The arms

| Lane | Maxon | Go arms |
|---|---|---|
| `x64` | the native build | `go-bitsliced`, `go-purego` (`std` built with `-tags purego`), `go-aesni` (`std`, the default build) |
| `arm64` | the native build | `go-bitsliced`, `go-purego`, `go-armv8` (`std`, the default build) |
| `wasm` | `--target=wasm32-wasi` | `go-bitsliced`, `go-std` (`std`, `GOOS=wasip1 GOARCH=wasm`) |

- **`bitsliced`** runs the same algorithm as the stdlib, so its ratio isolates the compiler.
- **`portable`** (`go-purego`, and `go-std` on wasm, whose standard build is Go's portable code) is Go's
  table AES with constant-time GHASH.
- **`hardware`** (`go-aesni`, `go-armv8`) is AES-NI and PCLMULQDQ on x64 and the ARMv8 Crypto Extensions
  on arm64. It runs on the native lanes.

⛔ **`go/bitsliced.go` MIRRORS `stdlib/helpers/crypto/AesBitsliced.maxon`'s encrypt path and
`stdlib/AesGcm.maxon`, function for function. Every change to either is ported into it in the same
commit.** Both are correct AES, so a stale port still prints matching digests: the bitsliced arm would
time an algorithm the stdlib has left behind while every check stays green.

## The harness

```
python scripts/bench-aes.py                                  # this host's native lane, the slot compiler, 5 runs
python scripts/bench-aes.py --lane wasm                      # the wasm lane, under vendor/wasmtime
python scripts/bench-aes.py --lane arm64                     # on an x64 host: runs on the shared Mac
python scripts/bench-aes.py --ref <pre-change-sha> --note "..."   # A/B: the control commit's compiler vs slot
python scripts/bench-aes.py --profile                        # also profile each Maxon arm (native lanes)
python scripts/bench-aes.py --check-go                       # locate Go and print its version
```

The script needs Python 3 and only the standard library.

- **Go is located** by `scripts/bench_common.py`: `MAXON_GO`, then `go` on `PATH`, then
  `C:\Program Files\Go\bin\go.exe` on Windows or `~/.local/go/bin/go` elsewhere. It builds with
  `GOTOOLCHAIN=local`. Each Go variant lands in `out/<lane>/go/`, cached by a hash of `go/`'s sources,
  the `go version` line, the target settings `go env` reports and the build flags.
- **Maxon is built once per arm** into `out/<lane>/<arm>/`: `slot`, `--ref <sha>` (a compiler built from
  that commit in a cached worktree under `out/ref-<sha>/`, the way every bench harness builds one), or
  `--compiler <path> --label <name>`. Each arm compiles its own checkout's `bench/aes/aes.maxon`.
- **Verification.** Every run of every arm must exit 0; a Maxon arm's exit 101 is reported as a LEAK.
  For each workload, every arm must print the same bytes per op, iterations, repeats and digest, and
  every arm must print every workload. Any failure is a verification failure (exit 1).
- **Timing.** `--warmup` untimed runs per arm (default 0: each process already takes the best of its
  repeats), then `--runs` timed runs interleaved. A cell is the best `bestNanos` over the runs, shown as
  MB/s, or as microseconds per op for `keysetup-128`.
- **`--profile`** runs `maxon profile run` on each Maxon arm at repeats 1, divisor 10, saves the report as
  `out/<lane>/<arm>/profile.txt` and prints its hot-function table. It needs a host that runs the lane
  natively.
- **`--lane wasm`** needs `vendor/wasmtime` (`scripts/fetch-vendor.sh`); the log's `host` carries the
  wasmtime version.
- **`--lane arm64` on an x64 host** ships the run to the shared Mac through `scripts/mac-host.sh`. HEAD
  must be on a remote branch and the tree clean apart from the log, because the Mac clones the pushed
  commit. The harness clones a session with the seed, takes the host lock, runs a job that builds the slot
  from the seed and then this script, polls the job, fetches the rows the job wrote with
  `mac-host.sh fetch`, drops the session, and appends the rows to the local log. `--compiler` is refused
  there, because it names a binary on this host; use `--ref`.
- **`--note "..."`** appends one row per workload for each Maxon arm to `docs/aes-benchmark-log.md`; the
  file is only ever appended to, by the tool.
- **Exit codes:** 0 a completed measurement; 1 a verification failure; 2 a build failure, no Go toolchain
  or no wasmtime; 3 usage. A run on the Mac exits with the Mac job's code.

Measure on an otherwise idle machine: the interleaving cancels drift, and contention stays in the numbers.
