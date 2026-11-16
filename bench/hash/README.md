# Hash: Maxon against Go

An instrument for one question: how fast are the stdlib's seeded hash (`Hasher`, `KeyedHasher`, `String.hash()`,
`Array.hash()`, and the `Map` and `Set` built on them) and `sha256` against Go's, on the same work, on the same
machine, on each of Maxon's hash tiers? One Maxon program (`hash.maxon`) and one Go program (`go/`) run the same
fourteen workloads and print the same lines. `scripts/bench-hash.py` builds both, verifies that every arm prints
the same iteration counts and digests, runs the arms interleaved and reports each workload's time with the
Maxon/Go ratio for every tier. The ratios are the numbers the hash work drives to 1.0 or below; rows are logged in
`docs/hash-benchmark-log.md`.

It is an INSTRUMENT: it exits 0 whatever the ratio is. Non-zero means the measurement itself is invalid.

## Layout

```
bench/hash/
  hash.maxon           the Maxon program: its pools, tables and workloads
  go/main.go           the Go program (hash/maphash, map, crypto/sha256)
  go/go.mod            the Go module (maxon.dev/bench/hash), which needs Go 1.24 or newer, and replaces
                       maxon.dev/bench/common with ../../common/go
  out/                 builds and ref compilers (gitignored)
bench/common/          the Maxon and Go halves this program shares with bench/aes; see its README.md
tests/examples/bench-hash.maxtest
                       builds hash.maxon and checks every line but the time against what the Go program
                       prints at a small size, on the host's best tier and on `software` (correctness only)
```

The Maxon program is guarded by `./maxon-bin/.maxon/maxon test tests/examples --filter=bench-hash`.

## The workloads

Every input is a fixed pattern, identical in both languages. A hashing workload takes its inputs from a pool of 8
members, and operation `i` hashes member `i mod 8`, so no operation repeats the one before it and none can be hoisted
out of the loop (the pool size, a power of two so that a mask picks the member, sets the 7 sites and the gaps between them in both programs). Member 0 is a base input, and member `k` is the base with the first `k` of the 7 sites changed, so
consecutive members differ at exactly one site, and the last member and the first differ at all seven. The sites
are spread over the whole input, the first and last byte (or element) among them: a string's byte `site * (length - 1) / 6`,
an array's element at the same position, and for a record its id, its score and five bytes of its name, the last
byte included. A hash that skips any site gives two members one hash, and the row's digest drops.

| Workload | Bytes per op | Base iterations | One op |
|---|---|---|---|
| `string-8` | 8 | 2000000 | hash an 8-byte string: Maxon `String.hash()`, Go `maphash.String` |
| `string-16` | 16 | 2000000 | the same at 16 bytes |
| `string-64` | 64 | 2000000 | the same at 64 bytes |
| `string-1k` | 1024 | 200000 | the same at 1 KiB |
| `string-64k` | 65536 | 2000 | the same at 64 KiB |
| `map-string-insert` | 0 | 1000000 | insert one key into a map that starts empty each repeat: `Map with String, Count` `upsert`, Go `map[string]int` |
| `map-string-lookup` | 0 | 1000000 | look up one key of a map that holds all of them, summing its values |
| `set-int-insert` | 0 | 1000000 | insert one scrambled int into a set that starts empty each repeat: `Set with Count`, Go `map[int]struct{}` |
| `set-int-contains` | 0 | 1000000 | test one int for membership in a set that holds all of them |
| `array-hash-16` | 128 | 1000000 | `Array.hash()` of 16 ints: Maxon `Array with Word`, Go `maphash.Bytes` over the ints' 128 bytes |
| `array-hash-1024` | 8192 | 50000 | the same with 1024 ints |
| `hasher-fields` | 0 | 2000000 | hash a record of an int, a name of 24 bytes and an int: Maxon `KeyedHasher.empty`, `combined`, `combinedBytes`, `combined` (the static form) under one fixed bench seed, Go one `maphash.Hash` reused, `SetSeed`, `Write` of the int's 8 bytes, `WriteString`, `Write`, `Sum64` |
| `hasher-object` | 0 | 2000000 | the same record, folded with Maxon's object form: `KeyedHasher.create` under the same seed, `combine`, `combineBytes`, `combine`, `finalize`; Go is the same code as `hasher-fields` |
| `sha256-1m` | 1048576 | 100 | `sha256` of 1 MiB; each op first copies the previous digest over the buffer's first 32 bytes, so no op repeats another |

The table size, 1000000 (`tableSize`, in each program), is the base iteration count of the four table rows and the
size of the key and int fixtures, so a row's `iterations` is its entry count. Each table row checks that its fixture holds
`iterations` entries before it runs and stops the program when it does not. A workload that has no bytes per op
reports nanoseconds per operation, and the rest report MB/s. A key is `key` followed by its index in decimal, and the
keys and ints, the pools, the lookup map and the lookup set are built before timing starts. Both languages iterate
the keys in the order they were inserted.

The digest on every row is the same in both languages and on every tier, so verification compares every arm:

- A hashing row (`string-*`, `array-hash-*`, `hasher-*`) prints how many ops hashed to a value other than the
  op before. The hashes themselves are not comparable: they are different hashes, from different seeds, on
  different tiers. The count is `iterations` when the hash reads every site of its input, and it depends on every
  hash, so a compiler cannot delete the work.
- `map-string-insert` prints the map's count plus the value of the last key, `map-string-lookup` the sum of every value
  looked up, `set-int-insert` the set's count and `set-int-contains` the number of hits.
- `sha256-1m` prints the weighted sum of the last digest's bytes, modulo 2^24, so it is compared with Go's
  `crypto/sha256` exactly.

## The programs

```
hash [repeats [divisor]]                      the Maxon program; both default to 5 and 1
hash [repeats [divisor]]                      the Go program
```

Each workload runs `base iterations / divisor` ops (at least 1), `repeats` times, and prints one line:

```
workload bytesPerOp iterations repeats bestNanos digest
```

`bestNanos` is the fastest of the repeats. Before the workload lines each program prints one line,
`clock resolutionNanos`, the smallest step its timing clock makes; `bench/common/README.md` says how each reads it.

## The arms

One Maxon binary runs once per hash tier, with `MAXON_HASH_TIER` set to `aes` or `software` in the process's
environment, so a Maxon arm is a compiler and a tier (`slot-aes`, `slot-software`).

| Tier | The Maxon hash it runs |
|---|---|
| `aes` | `AESENC` on x64, `AESE` and `AESMC` on arm64 |
| `software` | portable code |

A tier the host lacks ends the program before `main`, so a run on such a host names the tiers it has with `--tiers`.

**Each tier is compared with the Go arm that runs the same kind of hash.** The Go program is built once, with the
default flags, and runs as two arms:

| Go arm | How it runs | Paired with |
|---|---|---|
| `go` | as it is: its runtime hashes with AES where the CPU has it | `slot-aes` |
| `go-software` | with `GODEBUG=cpu.aes=off` | `slot-software` |

The tables show the pairs, so a ratio is `slot-aes` over `go` or `slot-software` over `go-software`, and a log row
names the Go arm it divides by. `go-software` is the knob that makes Go's own hash its non-AES code:

- Go's runtime hash (`memhash`, `strhash`, and so `map[string]int`, `map[int]struct{}` and, through the runtime,
  `hash/maphash`) is the AES one when `runtime.alginit` finds `cpu.X86.HasAES`, `HasSSSE3` and `HasSSE41` on x86, or
  `cpu.ARM64.HasAES` on arm64, and the portable one otherwise.
- `internal/cpu` lists `aes` among its options on both architectures, and `GODEBUG=cpu.aes=off` clears that feature
  at startup, so the runtime takes the portable hash. The harness sets `GODEBUG` for both Go arms (empty for `go`),
  and refuses `--tiers software` on a host that is neither x64 nor arm64, where the knob is not known.
- `-tags purego` would not do: it replaces only `hash/maphash`'s calls into the runtime and leaves the maps on AES.
- The knob changes the rows that hash short inputs (`string-8` to `string-64`, `array-hash-16`, the map and set rows)
  and not much the 1 KiB rows and above, where Go's portable hash streams about as fast as its AES one.
- It does not change SHA-256: `crypto/sha256` selects SHA-NI by `cpu.X86.HasSHA`, which `cpu.sha` clears and `cpu.aes`
  does not.

`MAXON_HASH_TIER` changes Maxon's seeded hash only, so two rows are not Maxon tier rows:

- `sha256-1m` runs on SHA-NI where the CPU has it on both sides, whatever the tier. Each tier's row divides by that
  tier's Go arm, and since neither knob touches SHA-256 the two Maxon cells and the two Go cells each differ by noise.
- `set-int-insert` and `set-int-contains` hash an `int` with Maxon's own multiplicative `hash()`, which is not seeded and
  has no tier, so their two Maxon cells differ by noise. Go's `map[int]struct{}` is different: it hashes with the
  runtime's `memhash64`, which checks the same AES flag `cpu.aes=off` clears, so its two Go cells, and with them the
  two ratios, are not the same measurement.

## What each row prices

- **Allocation.**
  - `hasher-fields` keeps the name as a `ByteArray` in the record, because `combineBytes` takes one and
    `String.toByteArray()` copies: the row prices the hash, not that copy. Go's `WriteString` takes the string as it is.
  - `KeyedHasher.create` heap-allocates once per op: a `HeapStats.allocationsMade()` census of 100000 object-form folds
    counted 100000 allocations, and 100000 static-form folds counted none. So `hasher-fields` prices the hash alone
    (the static form, `KeyedHasher.empty`, `combined`, `combinedBytes`), and `hasher-object` prices the hasher object
    with its allocation. Go reuses one `maphash.Hash` for both rows.
  - Maxon runs three mix steps per record, one `hashWord`, one `hashBytes` and one `hashWord`. Go buffers the same
    record's 40 bytes and hashes them once at `Sum64`.
  - `array-hash-*` hashes the ints' bytes in place on both sides: Go passes the array's memory to `maphash.Bytes`,
    with no copy.
  - The table rows allocate in both languages, from empty with no size hint. The Maxon table is kept alive past the
    clock read and freed after it, so its release is not timed. Go's `Measure` collects garbage before each repeat,
    outside the timer, and keeps the table alive past the clock read; Go's collector still runs on other cores
    during the timed insert, which is its cost of allocating.
- **Calls.** Each side counts hash changes in one loop shared by its hashing rows, and neither pays an indirect call per
  op. Maxon's `countChanges` is generic over the pool's type, so the compiler emits one copy per pool type (`StringSource`,
  `WordSource` and the two account sources) that calls its `hashAt` directly; the IR of the program has no indirect call
  in any of them. Go's `countChanges` takes the hash as a function, and `go build -gcflags=-m=2` reports `countChanges` and
  each caller's function literal as inlined into `hashStrings`, `hashWords` and `hashAccounts`. The loop is shared in Go
  all the same, because writing it out in each caller would copy it three times; the inlining budget it fits in is tight
  (`hashStrings` costs 79 of 80), so rerun that command after editing either function.
  Each source reads its pool member in its own `hashAt`, with the same `pool.get(op and poolMask)`, four times. No shared
  spelling keeps the loop as fast: a generic over `Array with T` is E3179, an `extension Array` method shares one body
  that retains the element through its type parameter, and a plain helper function per element type is not inlined into
  the loop. Either made `string-8` more than twice as slow.
- **The hash width.** `String.hash()` and `Array.hash()` answer the low 32 bits of the 64-bit hash that
  `maphash.String` answers whole.
- **Cache.** The pool of 8 members of 64 KiB is 512 KiB, which stays in L2 on current CPUs, so `string-64k` and
  `array-hash-1024` measure the hash's throughput from cache and not from memory. The table rows hold 1000000
  entries, which do not fit in cache in either language.
- **The seed.** Maxon draws its process seed at startup and Go draws its seed once per run, so a table's collisions
  differ from run to run in both.

## The harness

```
python scripts/bench-hash.py                                 # the slot compiler on every tier, 5 runs
python scripts/bench-hash.py --tiers aes                     # only these tiers
python scripts/bench-hash.py --ref <pre-change-sha> --note "..."   # A/B: the control commit's compiler vs slot
python scripts/bench-hash.py --check-go                      # locate Go and print its version
```

The script needs Python 3 and only the standard library. It shares its Go location, arm planning, ref compilers,
output-directory lock, output parsing, verification, tables, session header and log-row prefix with
`scripts/bench-aes.py` through `scripts/bench_common.py`, which holds their description; this section covers what
differs.

- **Go is located** by `scripts/bench_common.py`: `MAXON_GO`, then `go` on `PATH`, then
  `C:\Program Files\Go\bin\go.exe` on Windows or `~/.local/go/bin/go` elsewhere. It builds with `GOTOOLCHAIN=local`
  into `out/go/`, cached by a hash of the sources of `go/` and `bench/common/go`, the `go version` line, the target
  settings and the build flags.
- **Maxon is built once per arm** into `out/<arm>/`: `slot`, `--ref <sha>` (a compiler built from that commit), or
  `--compiler <path> --label <name>`. Each arm compiles its own checkout's `bench/hash/hash.maxon` together with
  `bench/common/BenchProgram.maxon`. A `--ref` arm compiles what its commit holds: its copy of `hash.maxon`, and the shared
  source only if the commit has it, so a commit from before the extraction builds its program alone. A commit with no
  `bench/hash/hash.maxon` is refused.
- **Verification.** Every run of every arm must exit 0; a Maxon arm's exit 101 is reported as a LEAK. For each
  workload, every arm, tiers included, must print the same bytes per op, iterations, repeats and digest, and every
  arm must print every workload. A control compiler built before a workload existed may omit it on all of its
  tiers; one tier of a compiler that prints the row on another is refused. Every arm must print exactly one `clock`
  line, unless it is a Maxon control built before the line existed. A workload whose best repetition is under 1000
  times its arm's clock resolution is refused, naming the row and the arm: raise that workload's base iterations in
  `hash.maxon` and `go/main.go` together. Any failure is a verification failure (exit 1).
- **Timing.** `--warmup` untimed runs per arm (default 0: each process already takes the best of its repeats), then
  `--runs` timed runs interleaved, every tier of every compiler and both Go arms in turn, in the opposite order on
  alternate rounds. A cell is the best `bestNanos` over the runs.
- **`--note "..."`** appends one row per workload for each Maxon arm and tier to `docs/hash-benchmark-log.md`, with
  the Go arm that tier is paired with and its time; the file is only ever appended to, by the tool. The `host`
  cell carries the CPU model and the Go version.
- **Exit codes:** 0 a completed measurement; 1 a verification failure; 2 a build failure or no Go toolchain; 3 usage.

Measure on an otherwise idle machine: the interleaving cancels drift, and contention stays in the numbers.
