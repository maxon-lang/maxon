---
feature: sched-processor
status: stable
keywords: [scheduler, green-threads, processor, worker, multi-M, GMP, async, MAXON_MAX_PROCS]
category: system
---

# The scheduler's PROCESSOR (P) and its worker M

## Documentation

shv2's green-thread runtime is split across two files, and the split is the subject of this spec.
`Compiler/Runtime/GtRuntime.maxon` owns the GREEN THREAD (G) — its struct, its stack, the global FIFO
run queue, the timer and process stores. `Compiler/Runtime/SchedRuntime.maxon` owns the two things a
green thread alone cannot express:

| | |
|---|---|
| **M** | the OS thread that is running a green thread right now |
| **P** | the per-processor state that OS thread owns while it does |

Go's model, and both reference compilers': the bootstrap's layout is `GtLayout.cs:360-402` and v1's
Win32 mechanics are `X64Backend.maxon:8990-9340`.

### The process is single-M BY DEFAULT, not by construction

`SchedRuntime.DefaultMaxProcs` is **1**, so an ordinary program builds exactly one P, never finds a
free one to claim, and **never creates a worker OS thread**. `MAXON_MAX_PROCS=N` raises the ceiling
to `min(N, cpuCount)` for a deliberate multi-M run.

⚠ **THE STRUCTURE IS REAL EITHER WAY; WHAT THE SUITE EXERCISES IS NOT ALL OF IT.** Exactly two of the
four pieces run at the default and are therefore covered by the cases below: the **P indirection**
(every green thread reaches `currentGt` through TLS, which is every async case in the corpus) and the
**per-P syscall stack** (`a-green-thread-kernel-call-round-trips-its-processor-stack`). The **CAS
claim** and the **Dekker park** live in code no thread enters at `MAXON_MAX_PROCS=1`, and the
**waiter** half of the deferred pair has no producer yet at all — its yielder half does
(`__gt_resched`'s handoff) and runs on every `Runtime.yield()`. Those three were proved by hand, and
by sabotage; the numbers are in the rung's report, not in this suite.

⚠⚠ **THE KNOB IS NOT A SUPPORTED CONFIGURATION AND THIS SPEC DOES NOT PIN IT.** At `N>1` the
SCHEDULER is correct, but the allocator is one unsharded unlocked shard and the refcounts are a plain
load/add/store; `SchedRuntime.maxon`'s header enumerates every such item. A green thread that only
COMPUTES is safe there and one that ALLOCATES is not — measured, and the runtime says which:
`maxon-shv2/track0/alloc-torture.maxon` dies at `MAXON_MAX_PROCS=2` with **exit 86**,
`slabSpanExhaustedPastItsEnd`, the slab's own INV-1 trap.

⛔ **AND THE SUITE STRUCTURALLY CANNOT REACH IT.** A spec case has no way to set an environment
variable for the program it runs — the harness has an `Args:` marker and no `Env:` one — so every
case below runs at `MAXON_MAX_PROCS=1`. What they pin is that the P INDIRECTION is correct, which is
the part that every program pays for; the `N>1` behaviour is proved by hand and recorded here rather
than gated by the suite.

### What the P replaced, and why a `.data` word could not stay

Two `.data` globals were retired outright when the P landed, and neither could have been kept:

- **`__gt_current_gt`** — the running green thread. Two Ms run two different green threads at the
  same instant, so a single word answers whichever wrote last. It is `P->currentGt`, read through
  two loads and no call: `mov reg, [__sched_tls_teb_offset]` then `mov reg, gs:[reg]`, which IS
  `TlsGetValue` for a slot in the TEB's inline 64.
- **`__gt_system_stack_top`** — the 64 KB scratch stack a green thread's Win32 calls run on. Its
  safety invariant was written down as prose in one comment (*"single-M runs one thing at a time"*),
  which multi-M falsifies by definition: two Ms inside kernel calls would set RSP to the same top and
  write the same region, as silent interleaved corruption rather than a fault. It is
  `P->systemStackSP`, so an M can only reach its own.

### A spawned green thread is PUBLISHED LAST, and that ordering is load-bearing

`__gt_spawn` creates a green thread and returns it UNQUEUED; the lowering fills its inline argument
slots and its releaser, and only then calls `__gt_ready`, which appends it to the run queue and wakes
an idle M.

⚠ **THAT SPLIT EXISTS BECAUSE THE ONE-CALL SHAPE WAS A MEASURED WRONG ANSWER.** `__gt_spawn` used to
enqueue the thread itself, under a comment reading *"SAFE to fill AFTER the spawn: a spawned GT is
only ENQUEUED, not run, until awaited (single-M)"*. With a worker M that premise is false: at
`MAXON_MAX_PROCS=12`, 600 green threads each reading one integer argument summed to `370613000`,
`390241000` and `381169000` on three consecutive runs against the single-M answer of `722397000`,
because a worker dequeued and ran each thread while the spawning thread was still storing its
arguments and the callee read the allocator's zeros. Publishing last removes the premise instead of
restating it; the same three runs are now `722397000` every time.

The bootstrap reaches the same ordering by a different route — its `__gt_spawn` takes
`(func, argc, argbuf)` and copies the arguments in before its own enqueue — so its publish is last
too.

## Tests

<!-- test: sched-processor.spawn-carries-every-argument -->
<!-- targets: x64-windows -->
**THE FULL INLINE ARGUMENT REGION, AND THE SUBJECT OF THE PUBLISH-LAST ORDERING.** `MaxAsyncArgs` is
six, so this is the widest spawn the language admits: every slot of the GT struct's inline region is
written by the lowering and read back by the trampoline. A slot missed, mis-strided or read before it
was written shows up as a wrong sum rather than as a crash — which is exactly how the multi-M
ordering bug presented.
```maxon
function widest(a int, b int, c int, d int, e int, f int) returns int
	__Builtins.parallelBoundary()
	return a + b * 2 + c * 4 + d * 8 + e * 16 + f * 32
end 'widest'

function main() returns ExitCode
	let p = async widest(1, b: 1, c: 1, d: 1, e: 1, f: 1)
	return await p as ExitCode
end 'main'
```
```exitcode
63
```

<!-- test: sched-processor.many-green-threads-through-one-processor -->
<!-- targets: x64-windows -->
Thirty-two green threads spawned before any is awaited, so all thirty-two are on the run queue at
once and one P drives every one of them: the scheduler's `currentGt` is written and restored around
each switch, and a P that lost track of which thread it was running would return one thread's result
for another. The sum is index-derived, so any mis-pairing lands on a different number.
```maxon
function step(n Counted) returns Counted
	__Builtins.parallelBoundary()
	return n * 3
end 'step'

typealias Counted = int(i64.min to i64.max)
typealias CountedPromise = Promise with Counted
typealias CountedPromiseArray = Array with CountedPromise

function main() returns ExitCode
	var promises = CountedPromiseArray.create()

	for i in 0 upto 32 'spawn'
		promises.push(async step(i))
	end 'spawn'

	var total = 0

	for p in promises 'await'
		total = total + await p
	end 'await'

	if total == 1488 'expected'
		return 7
	end 'expected'
	return 1
end 'main'
```
```exitcode
7
```

<!-- test: sched-processor.a-green-thread-kernel-call-round-trips-its-processor-stack -->
<!-- targets: x64-windows -->
**THE PER-P SYSCALL STACK, END TO END.** A green thread's `sleep` is a Win32 call, which the syscall
shim runs on a scratch stack instead of the thread's own 2 KB one — and since the P landed, that
scratch stack is reached through `P->systemStackSP` rather than a global. The shim must find this
thread's P, switch RSP to its stack, repoint the TIB, call, restore the TIB from the green thread and
switch back; a wrong offset anywhere in that chain corrupts the return path rather than producing a
wrong number, so the assertion is that the thread returns AT ALL with its own value intact.
```maxon
function napper(n int) returns int
	__Builtins.sleep(2)
	return n + 5
end 'napper'

function main() returns ExitCode
	let first = async napper(1)
	let second = async napper(2)
	let a = await first
	let b = await second
	return (a + b) as ExitCode
end 'main'
```
```exitcode
13
```

<!-- test: sched-processor.a-scalar-program-still-runs-with-no-processor-at-all -->
The negative control, and it is target-neutral on purpose: a program with no green thread installs no
scheduler, so it has no P, no TLS slot and no `__sched_*` `.data` word — and the prologue stack guard
every function in a green-thread image carries is not emitted for it either.

⚠ **ITS FAILURE MODE IS A COMPILE-TIME PANIC, NOT A RUNTIME FAULT**, and that is worth stating because
it changes what the case is for: a `globalAddr __sched_tls_teb_offset` in an image whose `.data` never
laid that slot out is a BACKEND RELOCATION PANIC (the trap `DebugStreamRuntime` records for `__ds_base`
in an untraced build). So this asserts that the P indirection stayed behind `usesGt` — a property every
other scalar spec in the suite also happens to assert, which is why this one is a control rather than a
discovery.
```maxon
function twice(n int) returns int
	return n * 2
end 'twice'

function main() returns ExitCode
	return twice(21) as ExitCode
end 'main'
```
```exitcode
42
```
