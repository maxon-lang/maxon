---
feature: sched-runqueue
status: stable
keywords: [scheduler, green-threads, coroutine, run-queue, ring, work-stealing, GMP, async, MAXON_MAX_PROCS]
category: system
---

# The coroutine queue, and the green-thread run-queue hierarchy behind it

## Documentation

⚖ **AN `async f(…)` CALL DOES NOT CREATE A GREEN THREAD.** *"async is not supposed to create a new
green thread. It allows a function to yield the current green thread while waiting for a blocking
operation"* (user, 2026-08-27). What it creates is a **COROUTINE** of the green thread that called it,
and the whole of the scheduler a Maxon program can reach follows from that one sentence:

| | |
|---|---|
| **a coroutine** | what `async f(…)` creates. It is OWNED by one green thread (`GtOffOwner`), is published only to that green thread's coroutine queue, and is driven only by that green thread's chain of drivers. A coroutine spawned by a coroutine belongs to the SAME green thread, so the relation is transitive and every frame `async` ever creates, at any nesting depth, lands in exactly one queue. |
| **a green thread** | a unit the P/M scheduler may hand to any OS thread. **There is exactly one** — GT0, a processor's inline scheduler context — and **no producer of a second until a `spawn` primitive lands.** A green thread's owner is ITSELF, which is what closes the chain above. |

⇒ **only one green thread can hold references to a box at a time, and that is a language guarantee
rather than a thread count.** Every refcount read-modify-write on a box therefore happens on the OS
thread running that box's one owning green thread.

**The coroutine queue** is an intrusive FIFO through `GtOffNext`, with its two ends on the owning green
thread's own struct. `__gt_coro_enqueue` appends, `__gt_coro_next` is the one place a driver asks what
runs next, and both take `__sched_lock` — because the IOCP completion thread appends to it too, when a
coroutine parked on a pipe read becomes runnable again. That completion thread is the ONLY other OS
thread that touches the queue, and it is the reason the lock is there.

**A yield goes to the TAIL**, which is the whole content of *"let someone else have a turn"*. The
bootstrap measured what the other choice costs — *"a thousand yields from one green thread left a
sibling that had never run still unrun"* — and the tail is what refuses it.

### The green-thread run-queue hierarchy — built, and UNREACHED until `spawn`

W212 built Go's three tiers, and they are all still here. Nothing an `async` program does enters any of
them, because what they schedule is GREEN THREADS:

| Tier | What it is | Who writes it |
|---|---|---|
| **the per-P ring** | 256 fixed slots on the P struct, addressed by two MONOTONIC counters (`runqhead`, `runqtail`); the slot is `counter mod 256` and the length is `tail - head` | the owning P's M pushes at the tail with no lock; the owner AND any thief take from the head by CAS |
| **the global queue** | the intrusive FIFO through `GtOffNext` that used to be the whole scheduler | every mutation under `__sched_lock` |
| **stealing** | four rounds, each visiting every other ACTIVE P once from a random start, grabbing HALF a victim's ring into the thief's own | the thief, by CAS on the victim's head |

⛔ **AND "UNREACHED" IS OBSERVABLE IN THE EMITTED BINARY, NOT MERELY ASSERTED.** `__gt_ready` publishes
to the owner's queue, so nothing reaches `__sched_runq_put`; nothing reaches it, so nothing reaches
`__sched_wake_or_spawn`, so **no worker OS thread is ever created at any `MAXON_MAX_PROCS`**. Dead-code
elimination then takes the whole tier out: MEASURED on the first case below, the emitted program
contains `__sched_runq_put`, `__sched_runq_get`, `__sched_steal`, `__sched_find_runnable`,
`__sched_worker_loop`, `__sched_wake_or_spawn`, `__gt_enqueue` and `__gt_dequeue` **before** the pin and
**none of the eight** after it.

⚠ **THE TIER STAYS BECAUSE `spawn` IS ITS PRODUCER** (`SERVICES_DESIGN.md:62-160`, *"Send is a MOVE"*).
That rung creates real green threads, which is exactly what a ring, a steal and a worker loop are for.

### The dropped-thread protocol, which is unchanged

**A ring cannot be spliced** — and neither can a queue somebody may already be walking — so a dropped
coroutine cannot be taken out of one. A green thread and a coroutine are each owned by TWO parties that
finish at moments neither can observe, so every GT carries a **teardown rendezvous word**, and each
party adds its own half-ticket to it exactly once:

| Party | Who that is | What it does first | Ticket |
|---|---|---|---|
| the **consumer** | the promise's owner: `await`, `try await`, or `__gt_promise_drop` (and the `cancel` that routes through it) | takes the result, or renounces it; gives back the thread's slot in `__gt_live_count` | 1 |
| the **runner** | whoever finishes with the thread's EXECUTION: the driver whose context switch came back from a `completed` thread, the scheduler party that takes a tombstone off a queue, or a dropper that has deregistered a PARKED thread from every store | frees the seed stack | 2 |

**The party whose add hands back the OTHER ticket is SECOND, and the second party performs the
teardown** — the releaser call and the struct free. Neither side has to know what the other is doing,
and every read of the struct that precedes a party's own add is safe by construction, which is what
makes the runner's stack-length load and the awaiter's `result` load race-free without a lock.

A coroutine renounced while it was still QUEUED reads exactly `1` in that word, and that is the whole
dropped-thread test: `__gt_coro_next` refuses to hand such a coroutine back, and
`__gt_coro_sweep_dropped` takes the ones sitting at the queue's front — which is what keeps a
spawn-and-drop loop that never schedules anything bounded. **The word is a field of its own and not a
status**, because the hand-assembled trampoline overwrites `status` with `completed` unconditionally and
a park overwrites it with `waiting`: a mark left there is erased by exactly the events the rendezvous
exists to meet.

⛔⛔ **A SPEC CASE CANNOT SET `MAXON_MAX_PROCS`, SO NOTHING BELOW EXERCISES TWO PROCESSORS** — the
harness gives a case `Args:` and no environment. Since the pin that costs less than it used to: a
coroutine cannot reach a second processor at any processor count, so the answers below are the answers
everywhere. **What still needs a harness program is the PIN ITSELF**, because only a program that can
set the variable can show that raising it changes nothing: `maxon-shv2/track0/pin-matrix.sh` drives the
five `track0` programs across `MAXON_MAX_PROCS ∈ {1, 2, 7, 12}` and asserts `workers=1` and
`steals=0` at every one. On the parent commit the same script read `workers=8 steals=3996` at N=12.

⛔ **AND THE DROPPED-WHILE-EXECUTING SHAPE IS NOW UNREACHABLE FOR A COROUTINE ALTOGETHER.** It needs a
second M popping the thread out of the dropper's queue while the dropper is still spawning, and there is
no second M. `maxon-shv2/track0/drop-running-torture.maxon` keeps measuring it, and keeps reading zero,
against the parent's `steals=51` at twelve processors; it is `spawn`'s gate, and W218's.

⚠ **EVERY CASE HERE IS `targets: x64-windows`, and that is a property of the subject.** They are all
green-thread programs, and the green-thread substrate exists on exactly one lane —
`async-scheduler.md`'s *Targets* section is the one statement of that gate.

## Tests

<!-- test: sched-runqueue.a-coroutine-spawned-by-a-coroutine-joins-its-owners-queue -->
<!-- targets: x64-windows -->
**THE TRANSITIVITY THE WHOLE PIN RESTS ON.** A coroutine's owner is its SPAWNER'S owner, not its spawner
— so `inner`, spawned by the coroutine `outer`, belongs to the same green thread `main` does, and lands
in the SAME queue as `sibling`, which `main` spawned. This case is the only one that can see that:
`outer` runs first, spawns `inner` behind `sibling`, and then awaits `inner` — which drives the ONE
queue, so `sibling` runs before `inner` even though `inner` is what is being awaited and `sibling` is
nobody's business.

⚠ **AND IT IS THE ONLY CASE THAT CAN SEE THE STAMP BE *WRONG* RATHER THAN MISSING.** MEASURED against
`__gt_spawn` stamping `gt.owner = currentGt` — the SPAWNER — instead of `currentGt.owner`: this case reads
`ra=0 posC=0` (`inner` lands in `outer`'s own queue, which nothing drives, so `await c` bails and answers
the unset result slot) while **every other case in this file still passes**, because none of them nests an
`async` inside an `async`. Removing the stamp altogether is the coarser sabotage and does not need this
case: an `owner` of 0 makes `__gt_coro_enqueue` write its queue ends through a null pointer, so **every**
green-thread program takes an access violation (exit `0xC0000005`, measured across all eight cases here).
A zero here is not a benign default and a plausible-looking non-zero is not enough either.
```maxon
var order = 0
var posA = 0
var posB = 0
var posC = 0

function inner() returns int
	__Builtins.parallelBoundary()
	order = order + 1
	posC = order
	return 1
end 'inner'

function sibling() returns int
	__Builtins.parallelBoundary()
	order = order + 1
	posB = order
	return 1
end 'sibling'

function outer() returns int
	__Builtins.parallelBoundary()
	order = order + 1
	posA = order
	let c = async inner()
	return await c
end 'outer'

function main() returns ExitCode
	let a = async outer()
	let b = async sibling()
	let ra = await a
	let rb = await b
	print("ra={ra} rb={rb} posA={posA} posB={posB} posC={posC}")
	return 0 as ExitCode
end 'main'
```
```stdout
ra=1 rb=1 posA=1 posB=2 posC=3
```
```exitcode
0
```

<!-- test: sched-runqueue.spawn-order-is-fifo-within-one-green-thread -->
<!-- targets: x64-windows -->
**FIFO WITHIN ONE GREEN THREAD.** A coroutine queue is a queue and not a stack: three coroutines spawned
in order run in that order, whatever order their promises are awaited in. The awaits here run BACKWARDS
on purpose — `p3` first — so a queue that handed back the newest entry would be visible as `a3=1`.
```maxon
var order = 0
var a1 = 0
var a2 = 0
var a3 = 0

function first() returns int
	__Builtins.parallelBoundary()
	order = order + 1
	a1 = order
	return 1
end 'first'

function second() returns int
	__Builtins.parallelBoundary()
	order = order + 1
	a2 = order
	return 1
end 'second'

function third() returns int
	__Builtins.parallelBoundary()
	order = order + 1
	a3 = order
	return 1
end 'third'

function main() returns ExitCode
	let p1 = async first()
	let p2 = async second()
	let p3 = async third()
	let s = (await p3) + (await p2) + (await p1)
	print("s={s} a1={a1} a2={a2} a3={a3}")
	return 0 as ExitCode
end 'main'
```
```stdout
s=3 a1=1 a2=2 a3=3
```
```exitcode
0
```

<!-- test: sched-runqueue.a-yield-hands-the-processor-to-a-never-run-sibling -->
<!-- targets: x64-windows -->
**THE HANDOFF ARM, THE TEST THAT SELECTS IT, AND THE END THE YIELDER IS DRAINED TO.** A yield hands the
processor over only if somebody is queued, and "queued" means *in my owner's coroutine queue* — one load
and one compare. `yielder` yields a thousand times while `sibling` has never run, and this one case pins
BOTH halves of the yield:

• **the arm choice** — a yield that read an empty queue would take the poll arm and hand the processor
  to nobody: a thousand yields, and a sibling that had never run still unrun, which is the bootstrap's
  own measured failure;
• **the TAIL** — a drained yielder that went to the queue's FRONT would be handed straight back on the
  next pop, a thousand times over, with the same result. It is the tail that lets `sibling` in.

It reads the sibling's flag as its own result, so the assertion is what the YIELDER saw.
```maxon
var siblingRan = 0

function yielder() returns int
	var i = 0
	while i < 1000 'spin'
		Runtime.yield()
		i = i + 1
	end 'spin'

	return siblingRan
end 'yielder'

function sibling() returns int
	__Builtins.parallelBoundary()
	siblingRan = 1
	return 1
end 'sibling'

function main() returns ExitCode
	let y = async yielder()
	let s = async sibling()
	let seen = await y
	let done = await s
	print("seen={seen} done={done}")
	return 0 as ExitCode
end 'main'
```
```stdout
seen=1 done=1
```
```exitcode
0
```

<!-- test: sched-runqueue.a-dropped-coroutine-in-the-queue-is-skipped-not-run -->
<!-- targets: x64-windows -->
**THE POPPER'S HALF OF THE DROPPED-THREAD PROTOCOL.** `marker`'s promise is dropped while two live
coroutines sit around it in the queue — one ahead of it, one behind — so the drop's own front-of-queue
sweep cannot reach it and `__gt_coro_next` is what has to refuse it. `await p2` drives past `marker`; it
must reclaim that coroutine instead of running it, so `ran` stays 0 and the two live results still
arrive. A scheduler that ran it would both set `ran` and leave a thread nobody awaits, which is the
`__gt_live_count` abort (75) rather than 7.
```maxon
var ran = 0

function marker() returns int
	__Builtins.parallelBoundary()
	ran = ran + 1
	return 1
end 'marker'

function ok(v int) returns int
	__Builtins.parallelBoundary()
	return v
end 'ok'

function main() returns ExitCode
	let p1 = async ok(3)
	_ = async marker()
	let p2 = async ok(4)
	let a = await p1
	let b = await p2
	print("ran={ran}")
	return (a + b) as ExitCode
end 'main'
```
```stdout
ran=0
```
```exitcode
7
```

<!-- test: sched-runqueue.a-spawn-drop-loop-stays-bounded -->
<!-- targets: x64-windows -->
**THE SWEEP'S HALF.** Five thousand coroutines are spawned and dropped without anything ever driving the
scheduler, so nothing would pop them and the mark alone would leave five thousand structs alive. Each
drop instead sweeps the tombstones off the FRONT of the dropped coroutine's own owner's queue, where the
one it just marked is sitting, so the live raw-allocation count stays at its steady state rather than
growing with the loop. `__Builtins.mmRawAllocLive()` counts exactly the population a GT struct belongs
to.
```maxon
var ran = 0

function neverRuns() returns int
	__Builtins.parallelBoundary()
	ran = ran + 1
	return 1
end 'neverRuns'

function main() returns ExitCode
	var i = 0
	while i < 5000 'loop'
		_ = async neverRuns()
		i = i + 1
	end 'loop'

	if __Builtins.mmRawAllocLive() < 100 'bounded'
		print("ran={ran} live=bounded")
	end 'bounded' else 'grew'
		print("ran={ran} live=grew")
	end 'grew'

	return 0 as ExitCode
end 'main'
```
```stdout
ran=0 live=bounded
```
```exitcode
0
```

<!-- test: sched-runqueue.a-drop-that-arrives-after-completion-reclaims -->
<!-- targets: x64-windows -->
**THE RUNNER-FIRST ARRIVAL ORDER.** `p` is never awaited, but `await q` drives the scheduler past it, so
`p` runs to completion and the driver's hand-off adds the RUNNER ticket while the promise is still live —
the struct survives, because it still holds a result an un-awaited promise owns. The loop body's scope exit
then drops `p`, and that dropper is the SECOND arrival: it finds the runner's ticket and reclaims. Three
iterations, and `mmRawAllocLive()` is identical before and after, so every struct came back.
`gtIsComplete` is what proves the case reached the order it names — a `p` that had NOT completed would be
the tombstone order the two cases above cover instead.
```maxon
function done() returns int
	Runtime.yield()
	return 1
end 'done'

function main() returns ExitCode
	var total = 0
	var finished = 0
	var i = 0

	while i < 1 'warm'
		let p = async done()
		let q = async done()
		total = total + await q
		finished = finished + __Builtins.gtIsComplete(p)
		i = i + 1
	end 'warm'

	let liveBefore = __Builtins.mmRawAllocLive()
	var j = 0

	while j < 3 'completedThenDropped'
		let p = async done()
		let q = async done()
		total = total + await q
		finished = finished + __Builtins.gtIsComplete(p)
		j = j + 1
	end 'completedThenDropped'

	let liveGrew = __Builtins.mmRawAllocLive() - liveBefore
	print("total={total} finished={finished} liveGrew={liveGrew}")
	return 0 as ExitCode
end 'main'
```
```stdout
total=4 finished=4 liveGrew=0
```
```exitcode
0
```

<!-- test: sched-runqueue.a-drop-of-a-parked-thread-is-both-halves -->
<!-- targets: x64-windows -->
**THE ONE ARM WHERE ONE CALL IS BOTH PARTIES.** `s` parks on a 200 ms timer and `await f` returns long
before it fires, so the loop body's scope exit drops a thread that is registered in the timer store and
that no runner will ever come back for. The drop takes it out of the store under `__sched_lock` — and,
having done so, is the only holder left, so it performs the RUNNER's half (the stack the parked thread is
suspended on) and then the consumer's, which reclaims. Three iterations with no hang, because nothing ever
waits on the 200 ms deadline, and `mmRawAllocLive()` returns to its baseline.
```maxon
function done() returns int
	Runtime.yield()
	return 1
end 'done'

function sleeper() returns int
	sleep(200)
	return 9
end 'sleeper'

function main() returns ExitCode
	var total = 0
	var stillParked = 0
	var i = 0

	while i < 1 'warm'
		let s = async sleeper()
		let f = async done()
		total = total + await f
		stillParked = stillParked + (1 - __Builtins.gtIsComplete(s))
		i = i + 1
	end 'warm'

	let liveBefore = __Builtins.mmRawAllocLive()
	var j = 0

	while j < 3 'parkedThenDropped'
		let s = async sleeper()
		let f = async done()
		total = total + await f
		stillParked = stillParked + (1 - __Builtins.gtIsComplete(s))
		j = j + 1
	end 'parkedThenDropped'

	let liveGrew = __Builtins.mmRawAllocLive() - liveBefore
	print("total={total} stillParked={stillParked} liveGrew={liveGrew}")
	return 0 as ExitCode
end 'main'
```
```stdout
total=4 stillParked=4 liveGrew=0
```
```exitcode
0
```

<!-- test: sched-runqueue.no-coroutine-is-ever-stolen -->
<!-- targets: x64-windows -->
**THE STEAL COUNTER, AND WHAT IT NOW READS BY CONSTRUCTION.** `__Builtins.schedStealCount()` walks
`__sched_procs` and sums every processor's steal counter, so it is the only way a Maxon program can
observe that work stealing happened at all. A coroutine is published only to its owner's queue, so
**no coroutine is ever stolen at any processor count** and this reads 0 — the answer everywhere, not
just at the one processor a spec case gets.

⚠ **AND THE ZERO IS NOW AN UNREACHED ARM RATHER THAN AN EXECUTED PATH, WHICH IS EXACTLY WHY THIS CASE
KEEPS ITS SUBJECT AND LOST ITS OLD JUSTIFICATION.** It used to say *"the scheduler still RUNS its
stealing rounds here"*; it does not — `__sched_find_runnable` is not even laid out in this program any
more. What the case still pins is that the QUERY works: the builtin, the `__sched_steal_count` walk it
roots and the per-P counter it reads are all still emitted and still answer. `track0/pin-matrix.sh`,
which can raise `MAXON_MAX_PROCS`, is where the zero becomes a measurement of the pin.
```maxon
function work(v int) returns int
	__Builtins.parallelBoundary()
	return v
end 'work'

function main() returns ExitCode
	let p = async work(5)
	let r = await p
	print("r={r} steals={__Builtins.schedStealCount()}")
	return 0 as ExitCode
end 'main'
```
```stdout
r=5 steals=0
```
```exitcode
0
```
