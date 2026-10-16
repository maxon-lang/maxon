# Concurrency: Maxon against Go

An instrument for one question: how do Maxon's coroutines (`async` / `await`) and services (`spawn`)
compare with goroutines, channels and mutexes in Go, on the same work, on the same machine? Seven
workloads exist as one Maxon program (`<workload>.maxon`) and one Go program (`<workload>/main.go`) that
print the same line, `<workload> <value>`. `scripts/bench-concurrency.py` builds both sides, verifies that
the outputs agree, runs them interleaved at matched processor counts and reports wall time, CPU time and
peak working set with the Maxon/Go ratio of each. The ratio is the number the optimization loop drives
at 1.0; rows are logged in `docs/concurrency-benchmark-log.md`.

It is an INSTRUMENT: it exits 0 whatever the ratio is. Non-zero means the measurement itself is invalid.

## Layout

```
bench/concurrency/
  <workload>.maxon          the Maxon program
  <workload>/main.go        the Go program, the oracle for its output
  internal/argv/argv.go     the Go programs' shared argument reader
  go.mod                    the Go module (maxon.dev/bench/concurrency)
  out/                      builds and the last run (gitignored)
tests/examples/bench-concurrency-<workload>.maxtest
                            builds the Maxon program and runs it at MAXON_MAX_PROCS=1 and 4 against the
                            value the Go program prints at a small size (correctness only, never time)
```

The Maxon programs are guarded by `./maxon-bin/.maxon/maxon test tests/examples --filter=bench-concurrency`.
The harness is not a test: a timing cannot be a spec case, because `specs/sched-preempt.md` rules out
wall-clock budgets and the suite has no Go arm.

Every program reads its sizes from `argv` and falls back to the same defaults in both languages. None
depends on the processor count except worker-pool's worker count, which is the processor count by
definition.

## The workloads

| Workload | Arguments (default) | Output | Go | Maxon | Exercises |
|---|---|---|---|---|---|
| thread-ring | `N` (1000) | the 1-based label of the thread holding the token when it reaches 0, with 503 threads | 503 goroutines, each blocked on its own `sync.Mutex`; the token is handed on by unlocking the next | 503 services; `pass(remaining)` is fire-and-forget to the successor; the holder at 0 reports to a `Reporter` service | send, park, wake |
| ping-pong | `N` (1000000) | the sum of `i+1` over the rounds | two goroutines on unbuffered channels | `main` and one `Partner` service; `try await partner.ping(i)` each round | the reply path: cell, await, completion |
| fan-in | `M` (100000) | the total of `p*M+j` over 64 producers | 64 producer goroutines send to one consumer on a channel of capacity 1024 | 64 `Producer` services send fire-and-forget to one `Sink`; each producer answers a completion reply; then one awaited total | one mailbox under contention, the global lock |
| worker-pool | `T K` (4096 250000) | the sum of the xorshift kernel's results over `T` tasks | `GOMAXPROCS` workers range over a buffered job channel and write a buffered result channel | `Scheduler.processorCount()` `Worker` services; task `t` goes to worker `t mod count`; every reply is awaited and summed | parallel scaling |
| spawn-many | `S` (100000) | the sum of `i*2` over `S` threads | `S` goroutines write into a slice behind a `WaitGroup` | `S` `Doubler` services, one reply method each, all awaited and summed | spawn cost, the stack pool, peak memory |
| coroutine-churn | `N` (100000) | the sum of `i` over `N` coroutines that each yield once | `N` goroutines, each `runtime.Gosched()` once, at `GOMAXPROCS=1` | `N` `async` calls, each `Scheduler.yield()` once; all awaited | `async` spawn, park, await, with no parallelism |
| shared-counter | `M` (100000) | the number of increments, `64*M` | 64 goroutines lock a `sync.Mutex`-guarded counter `M` times each | 64 `Incrementer` services send `M` fire-and-forget `inc()` to one `Counter` service; then one awaited `read()` | the idiomatic guarded state in each language |

The worker-pool kernel, identical in both languages:

```
x := uint64(task) + 1
repeat K times: x ^= x<<13; x ^= x>>7; x ^= x<<17
result = x >> 32
```

### Fairness rulings

Go has channels and mutexes; Maxon has neither. Each Maxon program takes the nearest idiomatic shape and
is NOT bent to dodge a runtime cost, because the cost is what the instrument exists to see.

- **A service is not a goroutine, and a message is not a channel send.** Maxon's tool for a thread of
  its own is a service, and the only way to talk to it is a message handled one at a time from a FIFO
  mailbox. The mailbox is unbounded, where the Go channels in fan-in and worker-pool are bounded or
  pre-sized.
- **fan-in and shared-counter** let senders run ahead of an unbounded mailbox, where Go's senders block
  on a full channel or wait on the mutex. Maxon's peak memory includes the whole backlog of envelopes,
  and the backlog's effect on wall time can go either way, so a wall ratio there reads as a direction
  only alongside the memory ratio.
- **spawn-many** pays a message and a reply round trip per Maxon service, where each goroutine writes
  one slice slot, so its ratio is skewed against Maxon by the messaging on top of the spawn.
- **thread-ring** cannot await its successor: the ring is a cycle of handles and a node awaiting a node
  that awaits it is E3139. The holder at 0 therefore reports fire-and-forget to a `Reporter` service,
  `main` parks in `Scheduler.quiesce()` until the ring is idle and then asks the reporter who holds the
  token, and every node is shut down at the end. The Go program reports over a channel.
- **worker-pool** distributes tasks round-robin by `t mod count` at the send. Go's workers pull tasks
  from a channel as they finish; Maxon has no multi-consumer queue, so the load balance is static. The
  tasks are uniform, so the balance is equal. The Maxon/Go ratio is mostly single-thread kernel
  codegen; the scaling signal is `P = N` over `P = 1` within each language.
- **coroutine-churn** compares against Go at `GOMAXPROCS=1` at every processor count. Maxon coroutines
  share one strand and never run in parallel; a Go arm free to use every processor would not be the same
  work. In the log and the tables a trailing `*` on `P` marks the pin.
- **shared-counter** compares an actor to a mutex: each is the idiomatic way to guard shared state in its
  language.
- **The kernel uses `bits(64)`.** `or` and `xor` give a signed `int` (see `specs/bitwise-operators.md`),
  so the Maxon kernel casts each `xor` back `as Word` to keep the following `shr` a zero-filling shift.
  Without the casts the shift sign-fills and the answer is wrong from the seventh round of task 0.
- **Each program reads its sizes with `CommandLine.readOptionalIntArgument`.** `maxon build <file>` compiles that file alone,
  and the stdlib's `CommandLine.readOptionalIntArgument(position, otherwise:)` is the argument reader the programs share, as
  the Go programs share `internal/argv`.

## Sizes

Some Go defaults above are small: spawn-many and coroutine-churn finish in tens of milliseconds, and
thread-ring's `1000` is a smoke value. The harness therefore always passes explicit sizes, chosen so
each Go run takes roughly 0.2 to 2 s on the reference host (Intel Core i7-8700, 6C/12T, 16 GB) at
`P = 1` and `P = 12`. The memory ceiling of that host bounds spawn-many and coroutine-churn: a Maxon
service or coroutine costs about 9 KB of stack, so a million of either asks for 9 GB.

| Workload | Arguments the harness passes |
|---|---|
| thread-ring | 3000000 (the Benchmarks Game used 50000000) |
| ping-pong | 1000000 |
| fan-in | 100000 (64 producers, 6.4 million sends) |
| worker-pool | 6000 250000 |
| spawn-many | 200000 |
| coroutine-churn | 200000 |
| shared-counter | 200000 (64 incrementers, 12.8 million sends) |

The sizes live in `WORKLOADS` in `scripts/bench-concurrency.py`; the log row for a measurement records
the arguments it used.

## The harness

```
python scripts/bench-concurrency.py                        # every workload, the slot compiler, 5 runs
python scripts/bench-concurrency.py --runs 3 --filter ping-pong --filter fan-in
python scripts/bench-concurrency.py --ref <pre-change-sha> --note "..."   # A/B: control compiler vs slot
python scripts/bench-concurrency.py --host wsl             # the x64-linux lane, inside WSL (Windows host)
python scripts/bench-concurrency.py --host orb             # the arm64-linux lane, inside OrbStack (macOS host)
python scripts/bench-concurrency.py --json                 # also out/<host>/last-run.json
```

The script runs on Python 3.9 or newer and needs only the standard library.

| Lane | Run | Maxon target | Go | `host` in the log |
|---|---|---|---|---|
| x64-windows | `python scripts/bench-concurrency.py` on Windows | `x64-windows` | Windows `amd64` | `windows` |
| x64-linux | `--host wsl` on Windows | `x64-linux`, cross-built by the Windows compiler | built and run inside WSL | `wsl` |
| arm64-macos | `python3 scripts/bench-concurrency.py` on a Mac | `arm64-macos`, built by the Mac's compiler | macOS `arm64` | `macos` |
| arm64-linux | `python3 scripts/bench-concurrency.py --host orb` on a Mac | `arm64-linux`, cross-built by the Mac's compiler | built and run inside an OrbStack machine | `orb` |
| Linux host | `python3 scripts/bench-concurrency.py` on Linux | the host's own target, built by the host's compiler | Linux | `linux` |

- **Go is located** through `MAXON_GO`, then `go` on `PATH`, then `C:\Program Files\Go\bin\go.exe` on
  Windows or `~/.local/go/bin/go` everywhere else (inside WSL and inside an OrbStack machine included).
  A `MAXON_GO` that names no executable is refused, never skipped over. `--host wsl` hands `MAXON_GO`
  to the script it runs inside WSL, so it must name a path valid there; `--host orb` does not hand it on,
  because a Mac `MAXON_GO` names a macOS binary, and the machine finds its own Go on its `PATH` or at
  `~/.local/go/bin/go`. Go builds land in `out/<host>/go/`, cached by a hash of the program's sources,
  `internal/` and `go.mod`, the `go version` line, the target settings `go env` reports (`GOOS`,
  `GOARCH`, `GOAMD64`, `GOEXPERIMENT`, `CGO_ENABLED`) and the build flags.
- **Maxon is built once per arm** into `out/<host>/maxon/<arm>/`: `slot` (the compiler in
  `maxon-bin/.maxon`), `--ref <sha>` (a compiler built from that commit in a cached worktree,
  `out/ref-<sha>/tree/`, by `scripts/bench_common.py`, which every bench harness shares), or
  `--compiler <path> --label <name>`. A ref is built the way CI builds it: that worktree's own
  `scripts/fetch-seed.sh` places the seed its `scripts/seed-tag` pins, and its own
  `scripts/build-from-seed.sh` builds `C1` (staging its `scripts/seed-shim/` if the seed refuses the tree)
  and then `C2`, so the control carries its own runtime. It is always built on the host the harness runs
  on — Windows for `--host wsl`, the Mac for `--host orb` — with Git for Windows' `bash` on Windows; it
  needs an authenticated `gh`, and any failure is a build failure (exit 2).
  Labels compare case-insensitively, two arms with one label are refused, and `go` is reserved for the
  Go arm; each is a usage error (exit 3).
- **Processor counts.** Every workload runs at `P` in {1, the CPU count}, with `MAXON_MAX_PROCS=P`
  matched by `GOMAXPROCS=P`; coroutine-churn's Go arm stays at 1.
- **Verification.** The first Go run of a workload fixes the expected output. Every run of every arm,
  warm-ups included, must print it byte for byte and exit 0. Exit 101 is reported as a LEAK. Any other
  mismatch, crash or timeout is a verification failure (exit 1).
- **Timing.** Each arm runs `--warmup` untimed runs, then `--runs` timed runs interleaved, alternating
  order each round. It reports wall time, CPU time (user + kernel) and peak working set per arm as a
  median with its spread (max - min over the median), and the Maxon/Go ratio of the medians.
  - On Windows, CPU is `GetProcessTimes` and the peak is the process's `PeakWorkingSetSize`.
  - On Linux, CPU is the `wait4` rusage and the peak is GNU `/usr/bin/time`'s `%M`. `ru_maxrss` of the
    Python child itself would carry the parent's resident size across the exec as a floor.
  - On macOS, CPU is the `wait4` rusage and the peak is the `maximum resident set size` line of
    `/usr/bin/time -l`, which reports bytes. macOS has no `waitid`, so the harness waits for the child's
    exit with a kqueue `NOTE_EXIT` event, which leaves the child unreaped as `waitid` with `WNOWAIT` does.
    The CPU count is the logical core count, efficiency cores included (4 performance and 4 efficiency on
    an Apple M3), so `P = N` runs some threads on the slower cores in both languages.
- **`--host wsl`** (Windows only). The Windows compiler cross-builds the Maxon programs with
  `--target=x64-linux`; then the same script re-runs itself inside WSL Ubuntu, where it builds the Go
  programs with the Linux toolchain, stages every binary in `~/.cache/maxon-bench-concurrency` (a
  native filesystem, not `/mnt/c`) and measures there. Rows from the two hosts do not compare with each
  other.
- **`--host orb`** (macOS only). The Mac's compiler cross-builds the Maxon programs with
  `--target=arm64-linux`; then the same script re-runs itself inside the OrbStack Linux machine named by
  `--orb-machine` (default `ubuntu`), reaching the checkout through the machine's `/mnt/mac` mount. There
  it builds the Go programs with the machine's Go, stages every binary in
  `~/.cache/maxon-bench-concurrency` (the machine's own filesystem, not the mount) and measures with GNU
  `/usr/bin/time`. The harness refuses, before building anything, when `orb` is not on `PATH`, the machine
  does not exist, the machine is not arm64, or `/usr/bin/time` is missing in it. Setting the machine up:
  ```
  orb create ubuntu ubuntu                                        # an arm64 machine on an Apple Silicon Mac
  orb -m ubuntu sudo apt-get install -y time                      # GNU time
  # Go for linux-arm64 from go.dev/dl, unpacked to ~/.local/go inside the machine
  ```
  The machine shares OrbStack's virtual machine with the others, so its CPU count is the VM's, not the
  Mac's, and its rows compare with no other lane's.
- **`--note "..."`** appends one row per workload and `P` for each Maxon arm to
  `docs/concurrency-benchmark-log.md`; the file is only ever appended to, by the tool.
- **Exit codes:** 0 a completed measurement; 1 a verification failure; 2 a build failure (a build step
  that times out included), no Go toolchain, no `/usr/bin/time` on Linux or macOS, or an unusable
  OrbStack machine; 3 usage (`--host orb` away from macOS and `--host wsl` away from Windows included).

Nothing else heavy should run while it measures: the interleaving cancels drift, not contention.

## Provenance and licence

`thread-ring/main.go` derives from the Computer Language Benchmarks Game's Go program for the
thread-ring benchmark, contributed by KP
(https://benchmarksgame-team.pages.debian.net/benchmarksgame/description/threadring.html#threadring),
and keeps its header. Its output line carries the workload name so the harness can verify it. The
Benchmarks Game programs are published under the Revised BSD (3-Clause BSD) licence: see
https://salsa.debian.org/benchmarksgame-team/benchmarksgame/-/blob/master/LICENSE.md before
redistributing it anywhere but here. The other six Go programs and all seven Maxon programs are original
to this tree.
