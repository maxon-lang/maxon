---
feature: sched-runqueue
status: stable
keywords: [scheduler, green-threads, run-queue, ring, work-stealing, fairness, GMP, MAXON_MAX_PROCS]
category: system
---

# The run-queue hierarchy — a per-processor ring, the global queue, and the 61-schedule fairness check

## Documentation

Until W212 shv2 had **one global FIFO under one lock**, and every producer and consumer went through
it. Since W212 it has Go's hierarchy, in three tiers, and `__sched_find_runnable` is the ONE place the
scheduler decides what runs next:

| Tier | What it is | Who writes it |
|---|---|---|
| **the per-P ring** | 256 fixed slots on the P struct, addressed by two MONOTONIC counters (`runqhead`, `runqtail`); the slot is `counter mod 256` and the length is `tail - head` | the owning P's M pushes at the tail with no lock; the owner AND any thief take from the head by CAS |
| **the global queue** | the intrusive FIFO through `GtOffNext` that used to be the whole scheduler | every mutation under `__sched_lock` |
| **stealing** | four rounds, each visiting every other ACTIVE P once from a random start, grabbing HALF a victim's ring into the thief's own | the thief, by CAS on the victim's head |

**What goes where, and why the two ends are not one end.** A SPAWN and a timer/child wake go to the
current P's ring — they are work this processor just created, and it is the processor most likely to
run them next. A **YIELD** goes to the GLOBAL queue's tail, which is Go's split exactly (`goready` →
`runqput`, `Gosched` → `globrunqput`): the ring is consulted before the global queue, so a yielder
routed there is behind every runnable thread rather than in the slot it just vacated. The bootstrap
measured what the other choice costs — *"a thousand yields from one green thread left a sibling that
had never run still unrun"*.

**The 61st schedule checks the global queue first.** Without it a ring that never empties starves
everything the global queue holds, which is exactly what a ring overflow puts there.

**A ring cannot be spliced**, so a dropped thread cannot be taken out of one — and a green thread is
owned by TWO parties that finish at moments neither can observe. Every green thread therefore carries
a **teardown rendezvous word**, and each party adds its own half-ticket to it exactly once:

| Party | Who that is | What it does first | Ticket |
|---|---|---|---|
| the **consumer** | the promise's owner: `await`, `try await`, or `__gt_promise_drop` (and the `cancel` that routes through it) | takes the result, or renounces it; gives back the thread's slot in `__gt_live_count` | 1 |
| the **runner** | whoever finishes with the thread's EXECUTION: the driver whose context switch came back from a `completed` thread, the scheduler party that takes a tombstone off a queue, or a dropper that has deregistered a PARKED thread from every store | frees the seed stack | 2 |

**The party whose add hands back the OTHER ticket is SECOND, and the second party performs the
teardown** — the releaser call and the struct free. Neither side has to know what the other is doing,
and every read of the struct that precedes a party's own add is safe by construction, which is what
makes the runner's stack-length load and the awaiter's `result` load race-free without a lock.

A thread renounced while it was still QUEUED reads exactly `1` in that word, and that is the whole
dropped-thread test: `__sched_find_runnable` refuses to hand such a thread back, the ring overflow
reclaims it rather than moving it to the global queue, and `__sched_sweep_dropped` takes the ones
sitting at either queue's front — which is what keeps a spawn-and-drop loop that never schedules
anything bounded. **The word is a field of its own and not a status**, because the hand-assembled
trampoline overwrites `status` with `completed` unconditionally and a park overwrites it with
`waiting`: a mark left there is erased by exactly the events the rendezvous exists to meet.

⛔⛔ **A SPEC CASE CANNOT SET `MAXON_MAX_PROCS`, SO NOTHING BELOW EXERCISES TWO PROCESSORS.** The
harness gives a case `Args:` and no environment, and the processor count is read once from
`MAXON_MAX_PROCS` at scheduler start — so **work stealing, the head CAS under contention and the
Dekker fence on the ring publish are all out of reach from here**, and the cases below do not pretend
otherwise. Exactly one of them touches the stealing surface at all, and it pins the ONE-processor
answer: nothing is ever stolen when there is nobody to steal from. **The multi-processor gate is
`maxon-shv2/track0/steal-torture.maxon` driven across `MAXON_MAX_PROCS ∈ {1, 2, 7, 12}`**, which is a
harness program precisely because a spec cannot be one; `slab-sharding.md` says the same thing about
its own subject and for the same reason.

What a ONE-processor program CAN reach, and what each case below is for: the ring's OVERFLOW into the
global queue and back out of it, the index WRAP past 256, the 61-schedule fairness check, FIFO order
within a processor, the yield's back-of-queue rule, both halves of the dropped-thread protocol — the
popper that refuses to run one and the sweep that reclaims one — and **both ARRIVAL ORDERS of the
teardown rendezvous that a single processor admits**: the consumer arriving first (a tombstone, which
the popper or the sweep then reclaims) and the runner arriving first (a thread that completed while a
DIFFERENT promise was being awaited, whose dropper then reclaims).

⛔ **THE THIRD SHAPE — A PROMISE DROPPED WHILE ITS THREAD IS EXECUTING — IS UNREACHABLE FROM HERE, AND
IT IS THE SHAPE THE RENDEZVOUS WAS BUILT FOR.** On one processor the dropper IS the only thread
running, so nothing can be executing underneath it; reaching it needs a second M popping the thread
out of the dropper's ring while the dropper is still spawning. **That gate is
`maxon-shv2/track0/drop-running-torture.maxon`** driven across `MAXON_MAX_PROCS ∈ {1, 2, 7, 12}`,
which reads `__Builtins.mmRawAllocLive()` across a spawn-delay-drop phase and asserts it returns to
baseline. Before the rendezvous it stranded a GT struct for most of the threads a worker M had taken
out of main's ring — **0 at one processor, 39-52 at seven and 53-72 at twelve, against 54-96 steals**
— and the exit leak gate could not see one of them, because the drop had already debited the count
that gate reads.

⚠ **EVERY CASE HERE IS `targets: x64-windows`, and that is a property of the subject.** They are all
green-thread programs, and the green-thread substrate exists on exactly one lane —
`async-scheduler.md`'s *Targets* section is the one statement of that gate.

## Tests

<!-- test: sched-runqueue.ring-overflow-runs-every-spawned-thread -->
<!-- targets: x64-windows -->
**THE OVERFLOW, AND THE PROOF THAT NOTHING IS LOST IN IT.** `spawnMany` recurses with an ORDINARY call,
so all 300 threads are spawned before the first `await` drives anything — the ring fills to its 256
slots and the 257th push moves half the ring plus the new thread to the global queue. The awaits then
unwind, and every one of the 300 runs exactly once whichever tier it ended up in.
```maxon
var ran = 0

function leaf() returns int
	__Builtins.parallelBoundary()
	ran = ran + 1
	return 1
end 'leaf'

function spawnMany(n int) returns int
	if n == 0 'base'
		return 0
	end 'base'

	let p = async leaf()
	let rest = spawnMany(n - 1)
	return (await p) + rest
end 'spawnMany'

function main() returns ExitCode
	let total = spawnMany(300)
	print("total={total} ran={ran}")
	return 0 as ExitCode
end 'main'
```
```stdout
total=300 ran=300
```
```exitcode
0
```

<!-- test: sched-runqueue.the-ring-index-wraps-past-its-capacity -->
<!-- targets: x64-windows -->
**THE WRAP.** `runqhead`/`runqtail` are monotonic counters, not indices: six thousand sequential
spawn-and-await rounds drive both of them far past 256 while the ring never holds more than one thread,
so a counter used directly as a slot index addresses further and further past the end of the P struct.

⚠ **THE COUNT IS SIX THOUSAND FOR A MEASURED REASON, AND FOUR HUNDRED PROVED NOTHING.** An unmasked
index writes and reads through the SAME wrong address, so the program's own answer stays correct while
it scribbles on whatever follows the P struct; the only observable is when it walks out of the mapped
region entirely. Measured against exactly that sabotage: 2,000 rounds still printed `sum=2000` and
exited 0, 5,000 segfaulted. Six thousand is the smallest round number past the point where the mask
stops being invisible — which is what the first cut of this case (400 rounds, green under the sabotage
it was written to catch) is a record of.
```maxon
function step(v int) returns int
	__Builtins.parallelBoundary()
	return v
end 'step'

function main() returns ExitCode
	var i = 0
	var sum = 0
	while i < 6000 'rounds'
		let p = async step(1)
		sum = sum + (await p)
		i = i + 1
	end 'rounds'

	print("sum={sum}")
	return 0 as ExitCode
end 'main'
```
```stdout
sum=6000
```
```exitcode
0
```

<!-- test: sched-runqueue.the-global-queue-is-consulted-within-sixty-one-schedules -->
<!-- targets: x64-windows -->
**THE FAIRNESS CHECK, AND THE ONE SHAPE THAT CAN SEE IT.** The overflow above moves the OLDEST half of
the ring to the global queue, so thread #1 — the first ever spawned — ends up at the global head while
~170 threads remain in the ring. The scheduler prefers its ring, so without the every-61st-schedule
global check thread #1 would run only after all ~170 of them; with it, it runs within the first 61
schedules. The case records the position at which thread #1 ran and asserts it is early.

⚠ It reports EARLY/LATE rather than the exact position, because the position depends on how many
schedules `main`'s own awaits have already spent — a number this spec has no business pinning. The two
outcomes are ~61 and ~171, so the boundary has a wide margin either way.
```maxon
var runCount = 0
var firstPos = 0

function leaf(id int) returns int
	__Builtins.parallelBoundary()
	runCount = runCount + 1
	if id == 1 'oldest'
		firstPos = runCount
	end 'oldest'
	return 1
end 'leaf'

function spawnMany(n int, id int) returns int
	if n == 0 'base'
		return 0
	end 'base'

	let p = async leaf(id)
	let rest = spawnMany(n - 1, id: id + 1)
	return (await p) + rest
end 'spawnMany'

function main() returns ExitCode
	let total = spawnMany(300, id: 1)
	if firstPos < 130 'early'
		print("total={total} oldest=early")
	end 'early' else 'late'
		print("total={total} oldest=late")
	end 'late'

	return 0 as ExitCode
end 'main'
```
```stdout
total=300 oldest=early
```
```exitcode
0
```

<!-- test: sched-runqueue.spawn-order-is-fifo-within-one-processor -->
<!-- targets: x64-windows -->
**FIFO WITHIN A PROCESSOR.** The ring is a queue and not a stack: three threads spawned in order run in
that order, whatever order their promises are awaited in. The awaits here run BACKWARDS on purpose —
`p3` first — so a ring that handed back the newest entry would be visible as `a3=1`.
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
**THE HANDOFF ARM, AND THE TEST THAT SELECTS IT.** A yield hands the processor over only if somebody is
queued, and W212 re-derived what "queued" means: the running P's RING or the global queue, where before
there was one global word to read. `yielder` yields a thousand times while `sibling` has never run — and
the sibling is in the ring, which is the half that did not exist before. A yield that read only the old
global head would find it empty, take the poll arm, and hand the processor to nobody: a thousand yields,
and a sibling that had never run still unrun, which is the bootstrap's own measured failure one queue
over. It reads the sibling's flag as its own result, so the assertion is what the YIELDER saw.
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

<!-- test: sched-runqueue.a-yield-goes-behind-the-global-queue -->
<!-- targets: x64-windows -->
**THE BACK OF THE QUEUE, AND THE ONLY SHAPE AT ONE PROCESSOR THAT CAN SEE IT.** A drained yielder goes to
the GLOBAL queue's tail; the scheduler consults its RING first, so anything pushed to the ring AFTER the
yielder was drained still runs BEFORE the yielder resumes. That is Go's split exactly (`Gosched` →
`globrunqput`, `goready` → `runqput`).

⚠ **AND IT TAKES THREE THREADS, BECAUSE TWO CANNOT TELL THE TWO ENDS APART.** Both ends are FIFO, so a
yielder routed to either one lands behind everything already queued — the difference only shows against a
thread queued AFTERWARDS. `yielder` yields (and is drained while `spawner` is still waiting in the ring);
`spawner` then runs and spawns `spawnee` into the ring; the ring is preferred, so `spawnee` runs FIRST and
the yielder resumes second. Routed to the ring instead, the yielder would sit ahead of `spawnee` and the
two positions would swap — measured, `sPos=2 yPos=1`. The first cut of this file tested the yield with two
threads and was green under exactly that sabotage.
```maxon
var order = 0
var sPos = 0
var yPos = 0

function spawnee() returns int
	__Builtins.parallelBoundary()
	order = order + 1
	sPos = order
	return 1
end 'spawnee'

function spawner() returns int
	__Builtins.parallelBoundary()
	let s = async spawnee()
	return await s
end 'spawner'

function yielder() returns int
	Runtime.yield()
	order = order + 1
	yPos = order
	return 1
end 'yielder'

function main() returns ExitCode
	let y = async yielder()
	let t = async spawner()
	let a = await t
	let b = await y
	print("a={a} b={b} sPos={sPos} yPos={yPos}")
	return 0 as ExitCode
end 'main'
```
```stdout
a=1 b=1 sPos=1 yPos=2
```
```exitcode
0
```

<!-- test: sched-runqueue.a-dropped-thread-in-the-ring-is-skipped-not-run -->
<!-- targets: x64-windows -->
**THE POPPER'S HALF OF THE DROPPED-THREAD PROTOCOL.** `marker`'s promise is dropped while two live
threads sit around it in the ring — one ahead of it, one behind — so the drop's own front-of-ring sweep
cannot reach it and the scheduler is what has to refuse it. `await p2` drives past `marker`'s slot; it
must reclaim that thread instead of running it, so `ran` stays 0 and the two live results still arrive.
A scheduler that ran it would both set `ran` and leave a thread nobody awaits, which is the
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
**THE SWEEP'S HALF.** Five thousand threads are spawned and dropped without anything ever driving the
scheduler, so nothing would pop them and the mark alone would leave five thousand structs alive. Each
drop instead sweeps the dropped threads off the FRONT of its own ring, where the one it just marked is
sitting, so the live raw-allocation count stays at its steady state rather than growing with the loop.
`__Builtins.mmRawAllocLive()` counts exactly the population a green-thread struct belongs to.
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

<!-- test: sched-runqueue.nothing-is-stolen-on-one-processor -->
<!-- targets: x64-windows -->
**THE STEAL COUNTER, AND THE ONE ANSWER A SPEC CAN PIN.** `__Builtins.schedStealCount()` sums the
per-P steal counters, so it is the only way a Maxon program can observe that work stealing happened at
all. A spec case runs at one processor, where there is nobody to steal from and the answer is
therefore 0 — which is what makes the `> 0` reading in `track0/steal-torture.maxon` mean something
rather than being a number that is always there. The scheduler still RUNS its stealing rounds here (it
reaches them whenever a P finds nothing), so a zero is a real measurement and not an unreached arm.
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
