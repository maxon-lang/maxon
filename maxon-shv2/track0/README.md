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

## Run it

```
bash maxon-shv2/track0/validate.sh
```

Exits `0` iff every check passes; prints `PASS`/`FAIL` per assertion and exits
non-zero on any failure. Override the compiler with `MAXON=/path/to/maxon.exe`
and the sweep repetition count with `REPS=N` (default 15).

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
- **`validate.sh`** — compiles the program once (a plain build + a `--debugstream`
  build), then runs the four checks below.

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
