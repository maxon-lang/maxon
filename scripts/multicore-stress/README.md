# multicore-stress — the multi-core validation harness

The gate that says the multi-core runtime **truly works** before the compiler depends on it. Scope:
**x64-windows**.

The runtime the compiler emits into every compiled binary has a per-P sharded, lock-free slab
allocator with an ownership gate and a cross-P remote-free MPSC queue, plus a green-thread scheduler
that spawns worker OS threads (Ms) on demand. Those cross-P paths have no coverage anywhere else:
a spec case is a Maxon program the harness compiles and runs once, and everything here is a **sweep**
across processor counts, a repetition count, or a latency — readings a single case cannot take.

## The drivers

Every driver runs `maxon-bin/.maxon/maxon[.exe]`, and every one of them takes `MAXON=<path>` to drive
a compiler staged elsewhere, which is how a PARENT-commit reading is taken. The binary must sit
inside a checkout: it locates `stdlib/` relative to itself.

| Driver | What it answers |
|---|---|
| `validate.sh` | is the emitted per-P sharded allocator + multi-M scheduler correct above one P? |
| `pin-matrix.sh` | is an `async` frame pinned to its green thread — `workers=1`, `steals=0` at every `MAXON_MAX_PROCS` and at the default — while a SPAWNED one reaches a worker M? |
| `refcount-race.sh` | does a contended refcount word survive, and can the pin be removed to break it? |
| `awaitany-index-race.sh` | does a driver with nothing runnable OBSERVE a promise another M answered, or sleep through it — read as the SELECT LATENCY, in the exit code |

```
bash scripts/multicore-stress/validate.sh              # REPS=N, default 15
bash scripts/multicore-stress/pin-matrix.sh            # PROCS_LIST=, PROGRAMS=, MAXON=
bash scripts/multicore-stress/refcount-race.sh 12      # reps as argv[1], MAXON=
bash scripts/multicore-stress/awaitany-index-race.sh   # the exit code IS the reading
```

Each exits `0` iff every check passes (`refcount-race.sh` records rather than asserts — see its
header for why).

## The shared halves

- **`lib.sh`** — what every driver needs, written once: where this host's compiler is (through
  `scripts/lib/host-binaries.sh`, so **no script here spells `.exe`**), the field readers, and
  `SPAWNING_PROGRAMS`.
  ⭐ **THAT ROSTER IS ONE FACT WITH TWO READERS** — the prelude a program is compiled with, and the
  family `pin-matrix.sh` asserts for it. Kept in one place so a program appended to the wrong list
  cannot inherit the other's expectation.
- **`worker-arrival.maxon`** — `awaitWorkerArrival()`, compiled into the `SPAWNING_PROGRAMS` and
  nothing else. An uncalled prelude is not free: the compiler's runtime-usage scan filters only
  unreachable *stdlib* functions, so linking it everywhere would install the scheduler queries into
  binaries whose readings are dated.

### ⭐⭐ "A WORKER M RAN" IS BLOCKED ON, NOT SAMPLED

A program can publish every message and read `schedMaxActiveWorkers()` before the OS has run the
thread `__sched_wake_or_spawn` created for it. Win that race and the row reads `workers=1 steals=0`
— which is indistinguishable from the coroutine pin, so a starved box and a broken scheduler produce
the same output.

`awaitWorkerArrival()` closes that: it waits for `schedMaxActiveWorkers() >= 2` on a bounded budget,
short-circuits when `__Builtins.schedProcessorCount()` resolves to one (the configuration where
`workers=1` is the correct answer and waiting would spend the budget to learn nothing), and reports
expiry as **`workerwait=timeout`**. Both drivers fail such a row rather than reading it as a pin.

⚠ **IT SLEEPS RATHER THAN YIELDING, AND THAT IS THE LOAD-BEARING CHOICE.** `Runtime.yield()` is one
turn on the *same* M and returns as soon as nothing else is runnable — under the saturation this
exists for, a yield-spin competes with the very machine it waits for and drains the queue that
machine would steal from. A sleep parks on a timer and the scheduler netpolls, so one turn is one
real millisecond and the turn budget IS the timeout.

## The programs

An `async f(...)` call creates a COROUTINE of the calling green thread, published only to that
thread's own queue and never to a P ring, so an `async` program runs on one M whatever
`MAXON_MAX_PROCS` says. A `spawn` creates a real green thread that a worker M can take. **Which
family a program is in decides what its rows mean**, so it is the first column here.

### `spawn` — reaches a worker M

- **`service-torture.maxon`** — twelve sink services, 4,000 rounds each. The first program here whose
  concurrency is real, and the one that flips `pin-matrix.sh`'s pin assertion. Also the co-own
  sabotage's subject: a send is a MOVE, so a lost decref is 101 at the leak gate and a lost incref
  frees a record under a live holder, which is 139. ⚠ `aggregate=` is byte-identical in passing and
  failing runs — **the exit code is the discriminator**.
  Prints `remoteFrees=`; that reading is a LOWER BOUND, since sinks are still queued when `main`
  reads it.
- **`service-fanin-torture.maxon`** — the mailbox's MULTI-PRODUCER side: twelve SENDER services and
  one sink, where `service-torture` is one sender and twelve mailboxes. 48,000 heap `String`s are
  built by twelve green threads and dropped by a thirteenth, so **leak-freedom here is the exit code
  and nothing else**. Its aggregate is accumulated by the sink, whose mailbox serialises its own
  handlers, which is what makes the reading sound above one P without a cross-thread flag.
  Its `remoteFrees=` is the stronger of the two: every payload but the last has already crossed by
  the time it reports.
- **`syscall-stack-torture.maxon`** — the only program that puts more than one M inside the **syscall
  shim** at once. Twelve services make ~24,000 real kernel calls: `File.exists`
  (`GetFileAttributesA`, no stack arguments — the pure stack switch at the highest frequency
  reachable) and, every fortieth round, a write/read/delete cycle whose `CreateFileA` is the widest
  stack-argument copy in the shim's table. The shim parks the green thread's own RSP in the first
  word of the 64 KB scratch region it switches to, so two Ms sharing one region overwrite each
  other's parked RSP and the first one out returns onto the other's stack. Its header carries the
  sabotage reading. ⚠ It writes scratch files into the cwd.
- **`awaitany-index-torture.maxon`** — the only program here measuring a **LATENCY**, and the only one
  whose subject is what a driver does with nothing to run. `Slow` sleeps 25 ms and `Quick` answers at
  once, so `awaitAny` must come back with index 1 within a millisecond.
  ⛔ **IT PRINTS NOTHING, AND THAT IS MEASURED, NOT STYLISTIC**: a `print`-instrumented build of the
  same tree read 150/150 clean while the exit code read 13/160. A print is a syscall on the driver's
  own M and perturbs exactly the window the program is about. The reading is the exit code —
  `150 + min(worstMs, 99)`, or `250` for a wrong index.
- **`scavenge-race-torture.maxon`** — the allocator's DECOMMIT running concurrently with the
  scheduler's queue walks. ⚠ **NO DRIVER RUNS IT.** The one offending site it was written for has
  since been deleted, so it cannot go red here — which is itself the finding. What it still proves is
  that the global sweep, the pop-then-test in `__sched_find_runnable` and the ring/steal machinery
  survive concurrent scavenging.
  ⛔ **ITS HEADER FILES TWO LIVE COMPILER DEFECTS** it works around rather than fixes: binding a
  `spawn` to a local before pushing it double-frees, and `.withIterator()` over
  `Array with <T>.handle` panics the compiler.
- **`service-async-strand-torture.maxon`** — services shut down while a coroutine their handler
  started is PARKED: the handler yields after its `async` so the coroutine runs to its `sleep`, and
  keeps the promise in the service's state so the shutdown is what drops it. `parked=` witnesses that
  shape and must equal `rounds=` — a promise discarded at its own statement is renounced before it
  runs and builds nothing. A strand is invisible to the exit gate, because the drop already debited
  the live count, so the scheduler's record-carve count is the instrument: `leaked=` is how far it runs
  past the most a runtime that reclaims every record can carve, and the rounds grow with the processor
  count until one record lost per round would reach twice that bound. The coroutine's `Probe` argument
  makes the heap gate a second witness: a lost reclaim is also a 101. The control is built in: one arm
  awaits its coroutine. ⚠ **NO DRIVER RUNS IT.**
- **`runnext-starvation-probe.maxon`** — ⭐⭐ **UNDRIVEN ON PURPOSE.** Every program a driver runs
  asserts something about the SHIPPED compiler; this one goes red only against a compiler with
  `runnext` BUILT, which no tree here produces, so a driver would assert nothing for ever. It is
  committed because it is the measurement `SchedRuntime.POffRunnext` cites for keeping the slot
  reserved, and a reason nobody can re-run is a reason that rots. Its header carries both readings
  and how to reproduce them.
- **`refcount-service-refused.maxon`** — a **MUST-NOT-COMPILE** entry: `refcount-torture` written
  against services. A send is a MOVE, so the first send takes the shared `String` away from `main`
  and the second asks `main` to give up a reference it no longer holds. `pin-matrix.sh` asserts the
  build fails **with E3102 specifically** — a must-not-compile program that fails for the wrong
  reason asserts nothing, and the first cut of this file was refused by E2015 instead.

### `async` — one M, whatever the processor count

These still prove determinism, leak-freedom and single-shard allocator churn. ⛔ **NO ROW OF THEIRS
COVERS A CROSS-P FREE**, and none should be read as doing so.

- **`alloc-torture.maxon`** — hundreds of tasks, each handed a managed `StringArray` the spawn site
  increfs and the async trampoline finally decrefs. Written to drive the remote-free push from the
  allocate side. ⭐ **THE ONLY PROGRAM HERE THAT TAKES AN ARGUMENT**: any argument selects a small
  workload, which is what `validate.sh`'s mm-trace check needs to fit the debugstream ring drop-free.
- **`remote-free-torture.maxon`** — the same subject from the allocate-then-sleep side: a green
  thread allocates, parks, and any idle M resumes it, so scope-end release lands on a different P.
- **`steal-torture.maxon`** — all work created by ONE green thread, so `steals=` is a direct reading
  of the stealing rounds. It reads 0 at every processor count, and that IS the pin.
- **`drop-running-torture.maxon`** — a promise dropped while its thread executes on another M, the
  one shape the teardown rendezvous was built for and the one no spec case can reach. `leaked=` is
  its assertion: how far the scheduler's record-carve count runs past the most a runtime that
  reclaims every dropped record can carve, over enough rounds that one record stranded per round
  would reach twice that bound. A row that prints no `leaked=` is a failure, not a pass.
- **`refcount-torture.maxon`** — twelve tasks all handed the SAME heap `String`, each pushing it into
  a local container in a loop: one round is N increfs and N decrefs of ONE word. ⚠ **THE EXIT CODE IS
  THE ONLY DISCRIMINATOR** — the aggregate is byte-identical in passing and crashing runs, and
  `live=` varies with the processor count in both. Its header tabulates the measured builds and the
  one-line sabotage that reddens it (48 of 96 runs).
- **`park-torture.maxon`** — the only program here that PARKS: 3,200 suspensions per run through the
  deferred-park window. Every other program spins and returns. ⚠ Inert at its committed knobs; its
  header carries the two instruments the decisive A/B needs.

### Neither

- **`bootstrap-otherwise-binding-hang.maxon`** — an **INVALID-MAXON REPRODUCER**, marked *do not add
  it to any suite*. No `async`, no `spawn`, no concurrency: it pins error RECOVERY inside a method
  body, where `otherwise (e)` in the single-expression form must answer `E2010` in under a second.
  ⚠ It is parser work sitting in a scheduler harness and has no business here.

## The knob

| Env var | Effect |
|---|---|
| `MAXON_MAX_PROCS=N` | The processor count is `min(N, cpuCount)`, floor 1; a value that is not a count at all (unset, malformed, `< 1`, over 32 bytes) is `cpuCount`. `=1` forces single-threaded (no worker Ms). |

⛔⛔ **`N` CAN ONLY LOWER THE COUNT OR LEAVE IT ALONE.** `SchedRuntime.emitResolveMaxProcs` settles to
`osCpuCount`, or to `min(requested, osCpuCount)` when the variable names one — so `MAXON_MAX_PROCS=32`
on a 16-way box is 16, not 32. ⚠ **The one source of truth is `emitResolveMaxProcs`** — re-derive this
row from it, not from this prose. `pin-matrix.sh` agrees by construction: its `effective_count()` is
`min(N, HOST_CPUS)` and its `default` row is `HOST_CPUS` itself.

⭐ **THE CROSS-P FREE COUNT IS A LANGUAGE SURFACE, NOT AN ENVIRONMENT DUMP.**
`__Builtins.slabRemoteFreeCount()` sums a per-P word credited at every remote push, so a program
reads its own number; `specs/slab-sharding.md` asserts on it from inside a case, and check 4 below
asserts it across processor counts.

## The four checks

1. **Determinism / byte-identity across core counts.** Runs `alloc-torture` and `service-torture`
   under `MAXON_MAX_PROCS ∈ {1, 2, 7, ncpu}`, `REPS` times each, and asserts the `aggregate=` line and
   the exit code are identical across every run and equal to the serial one. Byte-identical output
   regardless of core count is the core correctness property; the repetitions re-exercise the multi-M
   spawn path to catch intermittent crashes.
   ⭐ **BOTH FAMILIES ARE SWEPT ON PURPOSE.** Sweeping only the `async` program proves determinism of
   SINGLE-THREADED execution across processor counts, which is far weaker than it reads as.
2. **A spawned green thread reaches a worker M.** `service-torture` BLOCKS on
   `schedMaxActiveWorkers() >= 2` and reports `workerwait`. The check fails a `timeout` by name, fails
   `workers < 2` when the wait claims arrival (a contradiction, not a race), and at
   `MAXON_MAX_PROCS=1` requires `workers == 1` **and** `workerwait=ok` — the `ok` proving the
   one-processor short-circuit was taken rather than the budget having been spent.
3. **Leak-clean + balanced mm-trace.** No run of any of the three programs exits `101` (the runtime's
   exact leak-check gate) at any core count. Additionally runs the small `alloc-torture` workload
   under `maxon monitor --filter=mm` at `MAXON_MAX_PROCS=1` and asserts the captured trace has equal
   `mm_alloc`/`mm_free` counts with **zero dropped events**.
   ⛔ **THE MM-TRACE HALF CANNOT MOVE TO A SERVICE PROGRAM**: it asserts `dropped == 0`, which needs a
   workload small enough to fit the ring, and `alloc-torture` is the only program that takes one.
4. **The cross-P remote-free road is TAKEN, not merely built.** Both service programs' `remoteFrees=`
   must clear `REMOTE_MIN` unclamped and exceed the `MAXON_MAX_PROCS=1` reading, and the `=1` reading
   must stay at or under `FLOOR_MAX`. On a host that resolves to one processor the readings are
   reported without a verdict — a count that cannot be produced cannot witness its own absence.

## Notes / findings

- **The single-P `remoteFrees` floor is a per-program fact and must be measured, never inherited.**
  Both service programs read exactly **0** at `MAXON_MAX_PROCS=1`, 10 runs each (2026-09-09,
  16-processor box), because neither does any IO and so neither starts a P-less OS thread whose frees
  would take the remote arm. At 2 and above the smallest reading was **72,016**
  (`service-fanin-torture` at 2), with `service-torture` at ~240,000 throughout.
  ⚠ `alloc-torture` reads a floor of **1**: raw OS threads with no Maxon P (the IOCP completion loop /
  sync worker) route their frees through the same branch. That is a constant of THAT program's shape,
  not of the counter, and a program doing IO would read a small non-zero floor honestly.
- **A real intermittent multi-core crash was found and fixed here.** The torture program surfaced a
  ~2.5% (worker-count-correlated) `NULL`-pointer crash in `__gt_enqueue` (`gt->next`, offset `0x38`),
  always on the path `main → __gt_spawn → __gt_enqueue`. Root cause: x86 `__gt_spawn` passed `gt` to
  `__gt_enqueue` in R10 **without reloading it** after a `LeaveCriticalSection` call. R10 is
  caller-saved on Win64 and `LeaveCriticalSection` clobbers it on its contended wake-a-waiter path;
  the import-call-on-system-stack path preserves R10 only on the GT-stack path, not the main-thread
  path that main's own spawns take. Fix: reload `gt` from its stack slot before the enqueue, matching
  what the ARM64 emitter already did. After the fix, 340+ high-concurrency runs are clean.

### ⭐⭐ W219's READINGS — THE ONE COPY, AND EVERYTHING ELSE CITES IT

⛔ **THIS SECTION EXISTS BECAUSE THE FIRST CUT HAD FIVE COPIES AND TWO OF THEM DISAGREED** — one
comment said the deadline was 25 ms and another said 80, both citing this program, whose `slowSleepMs`
is 25; the rate read "9 of 10" in the driver script and "9 of 12" in three other places. The runtime
comments now say *"`multicore-stress/README.md` owns the measurement"* and stop.

All rows `MAXON_MAX_PROCS=16` unless stated, on the 16-processor box:

| what | before W219 | after |
|---|---|---|
| `awaitany-index-torture` (`slowSleepMs = 25`), 12 runs | **10 red** — 6 the wrong index, 4 late at 15-32 ms | — |
| the same, 180 runs across procs 1 / 4 / 16 | — | **180 clean, worst latency 0 ms** |
| the same at procs 1 | 12 clean of 12 | 12 clean of 12 |
| the same built with `slowSleepMs = 80`, 10 runs | **9 red**, the late ones reading **92-94 ms** | **10 clean, 0 ms** |
| a send-and-await loop, 1,200 awaits, WALL ms at procs 1 / 4 / 16 | 44 / 542 / 896 | **18 / 14 / 15** (×39 at 4, ×60 at 16) |

⭐ **THE WAKE IS THE LOAD-BEARING HALF, AND THE CONTROL SAYS SO.** With the completion wake removed
and the interruptible wait KEPT — one line — the reproducer reads **17 of 30 red at procs 16** (12
late, 5 wrong index). The re-test before blocking narrows the window; only the signal closes it.

⚠ **THE PROCESSOR COUNT IS THE LEVER**, which is why `procs=16` is the gate and `procs=1` is only a
control: the window needs a worker M to have taken both handlers off `main`, so at one processor
`main` runs them itself and the arm is blameless. A driver pointed at one processor measures nothing.

⚠ **AND THE WALL ROW IS NOT A THROUGHPUT BONUS — IT IS THE SAME DEFECT.** `Sleep(1)` returns on
Windows' scheduler tick, so every await in a send-and-await loop paid one. Process CPU reads 0-16 ms
in BOTH arms of that row at every count: the before arm's wall time was spent NOT RUNNING.
