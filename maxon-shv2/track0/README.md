# Track 0 — multi-core validation harness

The gate that says the multi-core runtime **truly works** before shv2 depends on
it. Scope: **x64-windows** (PLAN.md §108, §118-124, item 1a.3).

The runtime that `maxon.exe` (the C# bootstrap) emits into every compiled binary
has a per-P sharded, lock-free slab allocator with an ownership gate + a cross-P
remote-free MPSC queue, plus a green-thread scheduler that spawns worker OS
threads (Ms) on demand. Those cross-P paths had **no runtime coverage** — nothing
in the repo had ever raced them. This harness builds a torture program that
forces the cross-P paths and asserts the runtime stays correct as the core count
varies.

## ⛔ THREE DRIVERS, AND THEY MEASURE TWO DIFFERENT COMPILERS

This directory holds programs that no spec case can run, because a spec case
cannot set an environment variable. It holds **three** drivers, and the first
question to ask of any reading from here is *which compiler produced the binary*.

| Driver | Compiler it drives | What it answers |
|---|---|---|
| `validate.sh` | **the C# BOOTSTRAP** (`$REPO/bin/maxon.exe`) | is the bootstrap's per-P sharded allocator + multi-M scheduler correct above one P? |
| `pin-matrix.sh` | **shv2** (`maxon-shv2/.maxon/maxon-shv2.exe`) | is an `async` frame pinned to its green thread — `workers=1`, `steals=0` at every `MAXON_MAX_PROCS`? |
| `refcount-race.sh` | **shv2** | does a contended refcount word survive, and can the pin be removed to break it? |

⚠ **`validate.sh` DOES NOT MEASURE SHV2 AND NEVER DID.** It defaults `MAXON` to
`$REPO/bin/maxon.exe`, and one of its checks calls `maxon monitor`, which shv2
does not have. Everything it says is about the bootstrap's emitted runtime. The
two shv2 drivers were added by EC10 because W212 drove these programs under shv2
BY HAND ("240 runs at 1/2/7/12") and left no script, so its readings could not be
reproduced.

```
bash maxon-shv2/track0/validate.sh          # the bootstrap; REPS=N, default 15
bash maxon-shv2/track0/pin-matrix.sh        # shv2; PROCS_LIST=, PROGRAMS=, MAXON=
bash maxon-shv2/track0/refcount-race.sh 12  # shv2; reps as argv[1], MAXON=
```

Each exits `0` iff every check passes (`refcount-race.sh` records rather than
asserts — see its header for why). Override the compiler with `MAXON=<path>`,
which is how a PARENT-commit reading is taken; the binary must sit inside a
checkout, because it locates `stdlib/` relative to itself.

### ⭐⭐ WHAT EC10's PIN COST THIS HARNESS, STATED ONCE

`alloc-torture` and `remote-free-torture` reach the allocator's CROSS-P paths by
getting worker Ms to run their tasks. Since EC10 an `async f(...)` call creates a
COROUTINE of the calling green thread — published only to that green thread's
queue, never to a P ring — so **no worker M is ever created at any
`MAXON_MAX_PROCS`** and both programs run entirely on one M. They still prove
determinism, leak-freedom and single-shard churn; they no longer reach the per-P
mcache handoff, the remote-free MPSC queue or the span ownership gate **in shv2**.
`validate.sh` still reaches all three **in the bootstrap**, whose scheduler is
unchanged. Those paths regain a shv2 producer when a `spawn` primitive lands
(`SERVICES_DESIGN.md §"Ownership — the spine"`); until then, do not read a green shv2 run as
covering them.

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
- **`validate.sh`** — compiles `alloc-torture` once with the BOOTSTRAP (a plain
  build + a `--debugstream` build), then runs the four checks below.

## Knobs (all read once at scheduler init)

| Env var | Effect |
|---|---|
| `MAXON_MAX_PROCS=N` | Clamp the scheduler to at most `N` live Ps. `=1` forces single-threaded (no worker Ms). |
| `MAXON_SLAB_STATS=1` | Dump `[slab-stats] lock_wait=<n> ownership_gate_miss=<n> remote_free=<n>` to stderr at exit. |
| `MAXON_SLAB_GLOBAL_LOCK=1` | Bracket alloc/free in one global spinlock — the A/B bisection safety net. |

`remote_free` is the counter added for this harness (item 1a.3): it increments on
every cross-P remote-free push in `__slab_free`, giving direct observability of
the otherwise-invisible MPSC path.

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
4. **Remote-free exercised + global-lock A/B parity.** With `MAXON_SLAB_STATS=1`,
   asserts unclamped `remote_free` is large (worker cross-P traffic) and dwarfs
   the single-P value, while `MAXON_MAX_PROCS=1` `remote_free` stays at/under a
   tiny floor (see below). Then runs once with `MAXON_SLAB_GLOBAL_LOCK=1` and
   asserts the aggregate + exit code match the lock-free run and it is leak-clean:
   because the serialised and lock-free paths agree on the deterministic result,
   the lock-free path is validated by bisection.

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
  contended wake-a-waiter path; `EmitCallImportOnSystemStack` only preserves R10
  on the GT-stack path, not the main-thread path that main's own spawns take. Fix:
  reload `gt` from its stack slot before the enqueue (`X86CodeEmitter.Runtime.cs`),
  matching what the ARM64 emitter already did. After the fix, 340+ high-concurrency
  runs are clean.
