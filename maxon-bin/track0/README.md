# Track 0 — multi-core validation harness

The gate that says the multi-core runtime **truly works** before the compiler depends on
it. Scope: **x64-windows** (PLAN.md §108, §118-124, item 1a.3).

The runtime the compiler emits into every compiled binary
has a per-P sharded, lock-free slab allocator with an ownership gate + a cross-P
remote-free MPSC queue, plus a green-thread scheduler that spawns worker OS
threads (Ms) on demand. Those cross-P paths had **no runtime coverage** — nothing
in the repo had ever raced them. This harness builds a torture program that
forces the cross-P paths and asserts the runtime stays correct as the core count
varies.

## ⛔ FOUR DRIVERS, AND WHAT EACH ONE ANSWERS

This directory holds programs whose readings a spec case has no reason to assert:
they sweep the processor count across four values plus the default, and a case
that named one of those counts would be pinning the harness rather than the
runtime. (A spec case CAN set the count now — `<!-- procs: N -->`, which
`specs/sched-default-procs.md` owns — so the old reason given here, that it
could not set an environment variable at all, has expired.) It holds **four**
drivers, and every one of them drives `maxon-bin/.maxon/maxon[.exe]`.

| Driver | What it answers |
|---|---|
| `validate.sh` | is the emitted per-P sharded allocator + multi-M scheduler correct above one P? |
| `pin-matrix.sh` | is an `async` frame pinned to its green thread — `workers=1`, `steals=0` at every `MAXON_MAX_PROCS` and at the default — while a SPAWNED one reaches a worker M? |
| `refcount-race.sh` | does a contended refcount word survive, and can the pin be removed to break it? |
| `awaitany-index-race.sh` | does a driver with nothing runnable OBSERVE a promise another M answered, or sleep through it — read as the SELECT LATENCY, in the exit code (W219) |

⭐⭐ **AND ONE PROGRAM HERE HAS NO DRIVER ON PURPOSE: `runnext-starvation-probe.maxon`
(MC1).** Every program the four drivers run asserts something about the SHIPPED
compiler; that one goes red only against a compiler with `runnext` BUILT, which no
tree here produces — so a driver would assert nothing, for ever. It is committed
because it is the measurement `SchedRuntime.POffRunnext` cites for keeping the slot
reserved, and a reason nobody can re-run is a reason that rots. Its own header
carries both readings and how to reproduce them.

⛔⛔ **TWO OF `validate.sh`'s FOUR CHECKS NO LONGER HAVE A SUBJECT, AND THE SCRIPT SAYS
SO RATHER THAN GOING RED OVER IT.** Check 4 reads a `[slab-stats]` stderr dump gated on
`MAXON_SLAB_STATS` / `MAXON_SLAB_GLOBAL_LOCK`, and the emitted runtime implements
NEITHER — the cross-P count is a language surface instead
(`__Builtins.slabRemoteFreeCount()`, see the knobs table), and no knob serialises the
allocator to A/B against. So the script SKIPS check 4 on a run that prints no stats
line: a threshold compared against a counter nobody printed is a verdict about nothing.
Check 2 asserts a second worker M ran, which `alloc-torture` can no longer produce —
see the pin section below. Checks 1 and 3 (determinism, leak-freedom, a balanced
mm-trace under `maxon monitor`) are the ones that still measure something.

`pin-matrix.sh` and `refcount-race.sh` were added by EC10 because W212 drove these
programs BY HAND ("240 runs at 1/2/7/12") and left no script, so its readings could
not be reproduced; `awaitany-index-race.sh` joined them at W219.

```
bash maxon-bin/track0/validate.sh              # REPS=N, default 15
bash maxon-bin/track0/pin-matrix.sh            # PROCS_LIST=, PROGRAMS=, MAXON=
bash maxon-bin/track0/refcount-race.sh 12      # reps as argv[1], MAXON=
bash maxon-bin/track0/awaitany-index-race.sh   # the exit code IS the reading (W219)
```

⚠ **THE COUNT IN THIS SECTION SAID "THREE" IN FOUR PLACES WHILE THE TABLE LISTED FOUR, AND THE BLOCK
ABOVE LISTED THREE.** `awaitany-index-race.sh` had been on disk since W219 without ever being added to
either. A roster kept by hand in three places is a roster that goes stale in at least one of them — if a
fifth driver arrives, the table, this block and the counts all move together or none of them do.

Each exits `0` iff every check passes (`refcount-race.sh` records rather than
asserts — see its header for why). Override the compiler with `MAXON=<path>`,
which is how a PARENT-commit reading is taken; the binary must sit inside a
checkout, because it locates `stdlib/` relative to itself.

### ⭐⭐ WHAT EC10's PIN COST THIS HARNESS, STATED ONCE

`alloc-torture` and `remote-free-torture` reach the allocator's CROSS-P paths by
getting worker Ms to run their tasks. Since EC10 an `async f(...)` call creates a
COROUTINE of the calling green thread — published only to that green thread's
queue, never to a P ring — so **those two programs create no worker M at any
`MAXON_MAX_PROCS`, nor at the default**, and run entirely on one M. They still prove
determinism, leak-freedom and single-shard churn; they do not reach the per-P
mcache handoff, the remote-free MPSC queue or the span ownership gate. Those three
are `validate.sh`'s own subject, so its cross-P checks are out of reach with them
and its second-worker check reads `1`.

⭐ **AND THE COMPILER PRODUCER THIS PARAGRAPH SAID WOULD ARRIVE HAS ARRIVED.** `spawn`
(`SERVICES_DESIGN.md §"Ownership — the spine"`) creates real green threads, and
`service-torture` / `service-fanin-torture` move 4,800 heap `String`s each across
Ms — a record allocated on one M and released on another, which is the remote-free
push. So a green run of the two SERVICE rows is a cross-P allocation reading; a
green run of `alloc-torture`'s rows still is not, and converting those two to drive
it themselves is separate work nobody has done.

## Pieces

- **`alloc-torture.maxon`** — spawns hundreds of `async` tasks (promise-array
  pattern), each calling `__Builtins.parallelBoundary()`. Main (P0) builds a
  managed `StringArray` per task and hands it to the task as an `async` argument
  **without keeping a reference**: the spawn site increfs the managed arg, main's
  scope-end decrefs it, and the async trampoline performs the **final decref on
  the worker P**. So a P0-allocated array (plus its element Strings and backing
  store) is freed on a worker P → a cross-P **remote-free push** onto P0's queue.
  Each task also churns local allocations for volume. The aggregate is an
  order-independent sum of index-derived per-task results, so it is identical no
  matter how many cores ran the work. Passing **any CLI argument** selects a small
  workload used only by the mm-trace check (so the debugstream ring captures a
  complete, drop-free trace).
- **`steal-torture.maxon`** — all of its work is created by ONE green thread, so
  before EC10 the only way a second P could run any of it was the stealing rounds.
  `steals=` is a direct reading of that mechanism. Since the pin it reads 0 at
  every processor count, which is the pin.
- **`drop-running-torture.maxon`** — a promise dropped while its thread EXECUTES
  on another M, the one shape the teardown rendezvous was built for and the one no
  spec case can reach. Since the pin it is unreachable for a coroutine too; the
  program stays because `spawn` re-creates the shape.
- **`refcount-torture.maxon`** — twelve `async` tasks all handed the SAME heap
  `String`, each pushing it into a local container in a loop: `push` emits
  `__str_retain` and the container's scope end decrefs every element, so one round
  is N increfs and N decrefs of ONE word. It justified the `lock` prefix on
  `emitAdjustRefcount` at G2 and justifies its REMOVAL at EC10 — rebuilt after the
  original was lost to a `temp/` path in a comment, which is why it is committed
  here. **The exit code is the only discriminator** — the aggregate is
  byte-identical in passing and crashing runs. The three measured builds, and the
  one-line sabotage that reddens it (48 of 96 runs), are tabulated in its header.
- **`syscall-stack-torture.maxon`** (W213-C1) — the only program here that puts
  more than one M inside the **syscall shim** at once. Twelve spawned services
  make ~24,000 real kernel calls between them: `File.exists`
  (`GetFileAttributesA`, no stack arguments — the pure stack switch, at the
  highest frequency reachable) and, every fortieth round, a
  write/read/delete cycle whose `CreateFileA` is the **widest stack-argument copy
  in the shim's table** (seven arguments, three copied words). The shim parks the
  green thread's own RSP in the first word of the 64 KB scratch region it
  switches to, so two Ms on ONE region overwrite each other's parked RSP and the
  first one out returns onto the other's stack — silent interleaved corruption,
  not a fault at the point of the bug. Sabotaged to share one region it
  **segfaults 9 of 9 at `MAXON_MAX_PROCS` 2/7/12 and is clean 3 of 3 at 1**;
  `pin-matrix.sh`'s header carries that reading, what the same sabotage does to
  the spec suite (3–4 red of 7,097, intermittently), and why the sabotage is
  *"share the region"* rather than *"put it back on the P"*.
- **`awaitany-index-torture.maxon`** (W219) — the only program here that measures
  a **LATENCY** rather than an aggregate, a leak or a crash, and the only one
  whose subject is what a driver does when it has nothing to run. It is
  `specs/await-any.md`'s `over-service-replies` in a loop: `Slow` sleeps
  (`slowSleepMs`, 25) and `Quick` answers at once, so `awaitAny` must come back
  with index 1 within a millisecond. Before W219 the netpoll's blind waits were
  plain `osSleepMs` calls — objects nobody can signal — so a driver parked on
  `Slow`'s deadline slept through `Quick`'s reply and then answered the WRONG
  INDEX, both promises having completed by the time it looked. See **W219's
  readings** below, which are the only copy of those numbers.
  ⭐ **THE READING IS THE EXIT CODE AND THAT IS MEASURED, NOT STYLISTIC**: a
  `print`-instrumented build of the same tree read 150/150 clean while the exit
  code read 13/160. The probe was the bug's hiding place.
  ⚠ **AND THREE CONSECUTIVE FULL SUITES SAMPLED NONE OF IT** — `over-service-replies`
  passes either way, because it asserts the index and not the time it took. That
  is why this is committed as a standing instrument rather than left as a spec
  case's footnote.

### ⭐⭐ W219's READINGS — THE ONE COPY, AND EVERYTHING ELSE CITES IT

⛔ **THIS SECTION EXISTS BECAUSE THE FIRST CUT HAD FIVE COPIES AND TWO OF THEM
DISAGREED** — one comment said the deadline was 25 ms and another said 80, both
citing this program, whose `slowSleepMs` is 25; the rate read "9 of 10" in the
driver script and "9 of 12" in three other places. The runtime comments now say
*"`track0/README.md` owns the measurement"* and stop.

All rows `MAXON_MAX_PROCS=16` unless stated, on the 16-processor box:

| what | before W219 | after |
|---|---|---|
| `awaitany-index-torture` (`slowSleepMs = 25`), 12 runs | **10 red** — 6 the wrong index, 4 late at 15-32 ms | — |
| the same, 180 runs across procs 1 / 4 / 16 | — | **180 clean, worst latency 0 ms** |
| the same at procs 1 | 12 clean of 12 | 12 clean of 12 |
| the same built with `slowSleepMs = 80`, 10 runs | **9 red**, the late ones reading **92-94 ms** | **10 clean, 0 ms** |
| a send-and-await loop, 1,200 awaits, WALL ms at procs 1 / 4 / 16 | 44 / 542 / 896 | **18 / 14 / 15** (×39 at 4, ×60 at 16) |

⭐ **THE WAKE IS THE LOAD-BEARING HALF, AND THE CONTROL SAYS SO.** With the
completion wake removed and the interruptible wait KEPT — one line — the
reproducer reads **17 of 30 red at procs 16** (12 late, 5 wrong index). The
re-test before blocking narrows the window; only the signal closes it.

⚠ **THE PROCESSOR COUNT IS THE LEVER**, which is why `procs=16` is the gate and
`procs=1` is only a control: the window needs a worker M to have taken both
handlers off `main`, so at one processor `main` runs them itself and the arm is
blameless. A driver pointed at one processor measures nothing.

⚠ **AND THE WALL ROW IS NOT A THROUGHPUT BONUS — IT IS THE SAME DEFECT.**
`Sleep(1)` returns on Windows' scheduler tick, so every await in a send-and-await
loop paid one. Process CPU reads 0-16 ms in BOTH arms of that row at every count:
the before arm's wall time was spent NOT RUNNING.
- **`validate.sh`** — compiles `alloc-torture` once (a plain build + a
  `--debugstream` build), then runs the four checks below.

## Knobs (all read once at scheduler init)

| Env var | Effect |
|---|---|
| `MAXON_MAX_PROCS=N` | The processor count is `min(N, cpuCount)`, floor 1; a value that is not a count at all (unset, malformed, `< 1`, over 32 bytes) is `cpuCount`. `=1` forces single-threaded (no worker Ms). |
| `MAXON_SLAB_STATS=1` | **UNIMPLEMENTED — `validate.sh`'s check 4 skips over it.** It would dump `[slab-stats] lock_wait=<n> ownership_gate_miss=<n> remote_free=<n>` to stderr at exit; nothing reads the variable, so setting it gets silence and no error. |
| `MAXON_SLAB_GLOBAL_LOCK=1` | **UNIMPLEMENTED — same.** It would bracket alloc/free in one global spinlock, the A/B bisection safety net. |

⛔⛔ **THE FIRST ROW BRIEFLY SAID THE VARIABLE "lowers *and* raises", AND THAT IS BACKWARDS — IT IS THE ONE
DIRECTION THE FLIP TOOK AWAY.** `SchedRuntime.emitResolveMaxProcs` settles the count to `osCpuCount`, or to
`min(requested, osCpuCount)` when the variable names one, and its own ceiling note says it in as many words:
*"a request at or below the machine is taken EXACTLY … that direction is what the variable gained with the
flip: it used to be able only to raise a default of 1."* ⇒ with the default already `cpuCount`, `N` can only
LOWER or leave it alone; `MAXON_MAX_PROCS=32` on a 16-way box is 16, not 32. ⚠ **The one source of truth is
`emitResolveMaxProcs`** — re-derive this row from it, not from this prose. `pin-matrix.sh` agrees:
`effective_count()` is `min(N, HOST_CPUS)` and its `default` row is `HOST_CPUS` itself.

`remote_free` is the counter added for this harness (item 1a.3): it increments on
every cross-P remote-free push in `__slab_free`, giving direct observability of
the otherwise-invisible MPSC path.

⚠ **THE EVENT IS COUNTED AND IS NOT EXPOSED THROUGH THAT VARIABLE — it is a LANGUAGE SURFACE.**
`__Builtins.slabRemoteFreeCount()` sums a per-P word (`SchedRuntime.POffRemoteFreeCount`) credited at
`SlabRuntime.emitSlabRemotePush`, so a program reads its own number instead of a stderr dump at exit,
and `specs/slab-sharding.md`'s `a-box-freed-on-another-machine-takes-the-remote-free-road` asserts on
it. That is why the `MAXON_SLAB_STATS` row above says UNIMPLEMENTED rather than "this event is invisible".

## The four checks

1. **Determinism / byte-identity across core counts.** Runs the program under
   `MAXON_MAX_PROCS ∈ {1, 2, 7, ncpu}` (7 clamped to ncpu if smaller), `REPS`
   times each, and asserts the printed `aggregate=` line and the exit code are
   identical across every run and equal to the serial (`=1`) run. Byte-identical
   output regardless of core count is the core correctness property; the
   repetitions also re-exercise the multi-M spawn path to catch intermittent
   crashes.
2. **A second worker actually ran.** Asserts the unclamped run's
   `schedMaxActiveWorkers` print is `>= 2` and the `MAXON_MAX_PROCS=1` run's is
   exactly `1`. Byte-identity alone can pass on single-M cooperative execution —
   this proves real cross-core parallelism.
3. **Leak-clean + balanced mm-trace.** Asserts no run exits `101` (the runtime's
   exact leak-check gate) across all core counts. Additionally runs the small
   workload under `maxon monitor --filter=mm` at `MAXON_MAX_PROCS=1` and asserts
   the captured MM trace has equal `mm_alloc`/`mm_free` counts with **zero dropped
   events** (a complete, balanced trace).
4. **Remote-free exercised + global-lock A/B parity — SKIPPED.** Both halves read
   counters that only a `[slab-stats]` dump carries and neither environment variable
   is implemented, so the check reports SKIP and asserts nothing. What it would
   assert: unclamped `remote_free` large (worker cross-P traffic) and dwarfing the
   single-P value, `MAXON_MAX_PROCS=1` `remote_free` at/under a tiny floor (see
   below), and a `MAXON_SLAB_GLOBAL_LOCK=1` run agreeing with the lock-free one on
   aggregate and exit code — the bisection that validates the lock-free path.
   `specs/slab-sharding.md` asserts the remote-free half from inside a program.

## Notes / findings

- **Single-P `remote_free` is a small non-zero floor, not 0.** Raw OS threads with
  no Maxon P (the IOCP completion loop / sync worker) route their frees through
  the same remote-free branch (`LoadCurrentP`==NULL). This floor is a constant
  (empirically `1`) that appears even in a no-async, single-threaded control
  program, so it is **not** worker cross-P traffic and **not** a bug. Check 4
  asserts the single-P value stays at/under that floor while the unclamped value
  is orders of magnitude larger.
- **A real intermittent multi-core crash was found and fixed here.** The torture
  program surfaced a ~2.5% (worker-count-correlated) `NULL`-pointer crash in
  `__gt_enqueue` (`gt->next`, offset `0x38`), always on the path
  `main → __gt_spawn → __gt_enqueue`. Root cause: x86 `__gt_spawn` passed `gt` to
  `__gt_enqueue` in R10 **without reloading it** after a `LeaveCriticalSection`
  call. R10 is caller-saved on Win64 and `LeaveCriticalSection` clobbers it on its
  contended wake-a-waiter path; the import-call-on-system-stack path preserves R10 only
  on the GT-stack path, not the main-thread path that main's own spawns take. Fix:
  reload `gt` from its stack slot before the enqueue, matching what the ARM64
  emitter already did. After the fix, 340+ high-concurrency runs are clean.
