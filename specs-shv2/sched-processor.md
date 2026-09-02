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
| **P** | the per-processor state that OS thread HOLDS while it does |

Go's model, and both reference compilers': the bootstrap's layout is `GtLayout.cs:360-402` and v1's
Win32 mechanics are `X64Backend.maxon:8990-9340`.

### The M and the P are TWO STRUCTS, and TLS points at the M

⭐⭐ **UNTIL W213-C1 THEY WERE ONE.** A single struct carried both halves and the thread's TLS slot pointed
at it, because an M took a P at birth and held it for life — so "the processor" and "the thread of
execution" named the same object and neither reference compiler distinguishes them either. The rule that
separates them is one question asked of every field: ***if this thread's P were taken away while it is
blocked in a kernel call, would this field still be true of it?***

| on the **M** | on the **P** |
|---|---|
| `currentP` — the processor it holds, **or 0** | `id`, the shard index and the run-queue ring |
| `currentGt` — the green thread it is executing | the steal count |
| `systemStackSP` — its own 64 KB syscall stack | the deferred re-enqueue slots and the remote-free queue |
| its inline scheduler green thread (Go's `g0`) | `status`, and its link on the idle-P list |
| **its park event, and its place on the idle-M list** | |
| **the spinning bit and the three deadlock words** | |
| **its OS thread handle, and its link on the roster** | |

⚠ **`M->currentP` IS THE FIELD THE SPLIT EXISTS TO CREATE.** *"This thread holds no processor"* is one load
and one compare; before the split the runtime had no way to write it down at all, because TLS pointed at the
processor.

⛔⛔ **AND W213-C2 GAVE THAT ZERO A PRODUCER — THE THREE ROWS IN BOLD ARE ITS DOING, AND THIS PARAGRAPH USED
TO SAY THE OPPOSITE.** It read *"NO SCHEDULING CHANGED: an M still takes one P at birth and holds it until it
dies, so a `currentP` of 0 is a state the code can EXPRESS and that nothing today PRODUCES"*. That is no
longer true and the correction is behavioural, not cosmetic: **a worker M that finds nothing to run RELEASES
its processor onto the idle-P list and parks on its OWN event with `currentP == 0`**, and a waker takes a
processor and a machine off the two lists and binds them. So an idle machine no longer occupies a processor,
a processor can sit idle with no machine, and the two need not be the pair they were before.

⚠ **WHAT IS STILL AHEAD IS THE OTHER HALF.** Nothing yet takes a processor away from a machine that is BUSY,
so a green thread inside a blocking kernel call still holds its processor for the duration. That is
`entersyscall`/`handoffp`/`sysmon`, and `handoffp`'s last step — *park the P, not the M* — is precisely the
idle-P list this rung built.

⚠ **THE HOT READS DID NOT GET LONGER.** The syscall shim and the prologue stack guard want `currentGt` and
the syscall stack, and both are the M's, so they are the same two loads through the same TLS slot they were
before. Only a reader that wants the PROCESSOR pays the extra field load — the allocator's shard read, and
the scheduler's own per-schedule walks.

### The process is single-M BY CONSTRUCTION for everything `async` creates — and multi-M for what `spawn` creates

⚖ **AN `async f(…)` CALL DOES NOT CREATE A GREEN THREAD** (user, 2026-08-27). It creates a COROUTINE
of the green thread that called it, published only to that green thread's coroutine queue and driven
only by its chain of drivers — `sched-runqueue.md` is where that is stated in full. **Nothing an
`async` program does publishes a GT to a P**, so nothing calls `__sched_wake_or_spawn` and **no worker
OS thread is created for it, at any `MAXON_MAX_PROCS`.**

⛔ **THAT IS NOW A STATEMENT ABOUT `async`, NOT ABOUT THE PROCESS, AND THE SENTENCE HERE USED TO CONFLATE
THE TWO.** It said `DefaultMaxProcs` was 1 so an ordinary program built exactly one P and the rest sat
unclaimed. Both halves have expired: **`spawn` creates real green threads**, which is exactly what a ring, a
steal and a worker loop schedule, and **the default is now the machine's processor count**
(`sched-default-procs.md`, which is that flip's own spec). An ordinary build therefore has one P per
processor, and whether a second M starts is decided by the WORK — a service program runs on several, an
`async`-only program still runs on one.

⭐ **MEASURED, and it is the difference the pin makes rather than a claim about it.**
`maxon-shv2/track0/pin-matrix.sh` drives the `track0` programs across `MAXON_MAX_PROCS ∈ {1, 2, 7, 12}` and
at the default. `steal-torture` — which is `async` — reads `workers=1 steals=0` at every one; on the commit
before the pin, on the same box, it read `workers=8 steals=3996` at N=12. The two SPAWN-driven programs read
the other way, which is the same script's other family.

⚠ **WHAT AN `async`-ONLY PROGRAM EXERCISES IS STILL LESS OF THE STRUCTURE.** Two of the four pieces run in
one and are covered by the cases below: the **TLS indirection** (every green thread reaches `currentGt`
through the M its TLS slot names, which is every async case in the corpus) and the **per-M syscall stack**
(`a-green-thread-kernel-call-round-trips-its-processor-stack`). The **CAS claim**, the **Dekker park** and
the run-queue hierarchy behind them are code no thread in such a program enters — dead-code elimination
takes `__sched_runq_put`, `__sched_find_runnable`, `__sched_steal`, `__sched_worker_loop`,
`__sched_wake_or_spawn`, `__gt_enqueue` and `__gt_dequeue` out of an async program's emitted binary
entirely. A program that spawns keeps all of them and runs all of them. The **waiter** half of the deferred
pair still has no producer in either kind; its yielder half does (`__gt_resched`'s handoff) and runs on
every `Runtime.yield()`. ⚠⚠ **NOTHING UNREACHED IS DELETED, AND EVERY UNREACHED PIECE SAYS SO AT ITS OWN
DECLARATION.**

⛔ **WHAT THE ALLOCATOR NOTE HERE USED TO SAY IS STALE TWICE OVER, and both corrections are
measured.** It said *"at N>1 the allocator is one unsharded unlocked shard and the refcounts are a
plain load/add/store"*, and that `alloc-torture` *"dies at `MAXON_MAX_PROCS=2` with exit 86"*. S5
sharded the allocator per P and G2 made the refcount read-modify-write atomic, both long before this
rung. ⚠ **AND THE REFCOUNT HALF MOVED AGAIN INSIDE EC10 ITSELF: slice 2 made the read-modify-write
PLAIN, unconditionally**, because pinning `async` removed the second party G2's atomic was serialising
against (`MmRuntime.emitAdjustRefcount` carries the argument and the committed control). **MEASURED on
EC10's PARENT** — which is the tree that can still reach those paths —
`pin-matrix.sh` runs `alloc-torture` at 1, 2, 7 and 12 with `workers` of 1, 2, 7 and 12, an identical
`aggregate=205500` and exit 42 throughout. So the old sentence is false in both halves: the paths are
CORRECT.

⚠ **AND THOSE TWO PROGRAMS NO LONGER REACH THEM.** With no worker M, `alloc-torture` and
`remote-free-torture` run entirely on one M and stop touching the per-P mcache handoff, the remote-free
MPSC queue and the span ownership gate. A green run of either does not cover them. ⭐ **A DIFFERENT
PROGRAM DOES, NOW THAT `spawn` HAS LANDED:** `service-torture` and `service-fanin-torture` move 4,800 heap
`String`s each ACROSS Ms, so a record allocated on one M is released on another — which is the remote-free
push. `maxon-shv2/track0/README.md` states which rows cover what, once, where the programs are.

⛔ **THE SUITE USED TO BE STRUCTURALLY UNABLE TO SET THE KNOB, AND THAT IS THE OTHER THING THAT HAS
CHANGED.** This paragraph said a spec case had no way to set an environment variable for the program it
runs, so every case below ran at `MAXON_MAX_PROCS=1`. `<!-- procs: N -->` lifted that
(`sched-default-procs.md` owns the marker), and the default flip means a case that carries NO marker now
runs at the machine's count rather than at one. ⇒ **every case below runs multi-M**, and **none of them
carries a `procs:` marker**, which is a claim about their SUBJECT rather than an oversight: every case here
measures a coroutine, and an `async` coroutine is published to its owner green thread's own queue and is
never stolen, so its answer cannot depend on how many processors exist. The marker lives where a case's
subject really is a count — `sched-default-procs.md` owns it, and the five ring/global-queue ordering cases
in `sched-runqueue.md` carry `procs: 1`.
⚠ An earlier version of this paragraph said the single-processor cases here "carry `procs: 1` and say why at
the case". **No case in this file has ever carried one**, and a reader who trusted that sentence would have
gone looking for a marker that was not there. `pin-matrix.sh` remains the instrument for the counts a spec
case has no reason to name.

### What the M and the P replaced, and why a `.data` word could not stay

Two `.data` globals were retired outright when the P landed, and neither could have been kept. **Both
belong to the M rather than to the P**, which is the correction W213-C1 made — they are properties of the
thread that is EXECUTING, and the argument for each is the same argument one level up:

- **`__gt_current_gt`** — the running green thread. Two Ms run two different green threads at the
  same instant, so a single word answers whichever wrote last. It is `M->currentGt`, read through
  two loads and no call: `mov reg, [__sched_tls_teb_offset]` then `mov reg, gs:[reg]`, which IS
  `TlsGetValue` for a slot in the TEB's inline 64, and then one field load. The thread executing inside a
  kernel call belongs to the M blocked there, not to the processor a handoff can take away — and the
  syscall shim reads this word again AFTER the call, to restore the TIB.
- **`__gt_system_stack_top`** — the 64 KB scratch stack a green thread's Win32 calls run on. Its
  safety invariant was written down as prose in one comment (*"single-M runs one thing at a time"*),
  which multi-M falsifies by definition: two Ms inside kernel calls would set RSP to the same top and
  write the same region, as silent interleaved corruption rather than a fault. It is
  `M->systemStackSP` — one region per OS thread that runs Maxon code, committed as that thread becomes
  an M. ⛔ **The per-P spelling it had first made the invariant *"an M can only reach its own P"*, and that
  is exactly the sentence P migration falsifies**: a P handed to a second M while the first is still
  inside a kernel call puts two Ms back on one region, with the `.data` global's failure shape restored
  in full. *"One OS thread runs one thing at a time"* is true unconditionally, which is why the region is
  the M's.

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
<!-- targets: x64-windows, arm64-macos -->
**THE FULL INLINE ARGUMENT REGION, AND THE SUBJECT OF THE PUBLISH-LAST ORDERING.** `MaxAsyncArgs` is
six, so this is the widest spawn the language admits: every slot of the GT struct's inline region is
written by the lowering and read back by the trampoline. A slot missed, mis-strided or read before it
was written shows up as a wrong sum rather than as a crash — which is exactly how the multi-M
ordering bug presented.
```maxon
function widest(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer) returns Integer
	__Builtins.parallelBoundary()
	return a + b * 2 + c * 4 + d * 8 + e * 16 + f * 32
end 'widest'

function main() returns ExitCode
	let p = async widest(1, b: 1, c: 1, d: 1, e: 1, f: 1)
	return await p as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
63
```

<!-- test: sched-processor.many-green-threads-through-one-processor -->
<!-- targets: x64-windows, arm64-macos -->
Thirty-two green threads spawned before any is awaited, so all thirty-two are on the run queue at
once and one P drives every one of them: the scheduler's `currentGt` is written and restored around
each switch, and an M that lost track of which thread it was running would return one thread's result
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
<!-- targets: x64-windows, arm64-macos -->
**THE PER-M SYSCALL STACK, END TO END.** A green thread's `sleep` is a Win32 call, which the syscall
shim runs on a scratch stack instead of the thread's own 2 KB one — and since the P landed, that
scratch stack is reached through a struct field rather than a global: `P->systemStackSP` at first, and
`M->systemStackSP` since W213-C1. ⚠ The case's NAME still says *processor*, and it is left alone
deliberately: renaming it churns a golden and buys nothing, and what it exercises — the shim's whole
chain — has not changed. The shim must find this thread's M, switch RSP to its stack, repoint the TIB,
call, restore the TIB from the green thread and switch back; a wrong offset anywhere in that chain
corrupts the return path rather than producing a wrong number, so the assertion is that the thread
returns AT ALL with its own value intact.
```maxon
function napper(n Integer) returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
13
```

<!-- test: sched-processor.a-scalar-program-still-runs-with-no-processor-at-all -->
The negative control, and it is target-neutral on purpose: a program with no green thread installs no
scheduler, so it has no M, no P, no TLS slot and no `__sched_*` `.data` word — and the prologue stack
guard every function in a green-thread image carries is not emitted for it either.

⚠ **ITS FAILURE MODE IS A COMPILE-TIME PANIC, NOT A RUNTIME FAULT**, and that is worth stating because
it changes what the case is for: a `globalAddr __sched_tls_teb_offset` in an image whose `.data` never
laid that slot out is a BACKEND RELOCATION PANIC (the trap `DebugStreamRuntime` records for `__ds_base`
in an untraced build). So this asserts that the M/P indirection stayed behind `usesGt` — a property every
other scalar spec in the suite also happens to assert, which is why this one is a control rather than a
discovery.
```maxon
function twice(n Integer) returns Integer
	return n * 2
end 'twice'

function main() returns ExitCode
	return twice(21) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```
