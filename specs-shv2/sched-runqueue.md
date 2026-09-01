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
| **a green thread** | a unit the P/M scheduler may hand to any OS thread. GT0 — a processor's inline scheduler context — is one, and **`spawn` is the producer of every other** (SV1: a spawned SERVICE is a green thread). A green thread's owner is ITSELF, which is what closes the chain above. |

⇒ **only one green thread can hold references to a box at a time, and that is a language guarantee
rather than a thread count.** Every refcount read-modify-write on a box therefore happens on the OS
thread running that box's one owning green thread.

**The coroutine queue** is an intrusive FIFO through `GtOffNext`, with its two ends on the owning green
thread's own struct. `__gt_coro_enqueue` appends and `__gt_coro_next` is the one place a driver asks what
runs next; `__sched_lock` covers every access to it, because the IOCP completion thread appends to it too
when a coroutine parked on a pipe read becomes runnable again. That completion thread is the ONLY other OS
thread that touches the queue, and it is the reason the lock is there.

⚠ **THE TWO DOORS DIFFER IN WHO TAKES THAT LOCK, AND A READER WHO GETS IT BACKWARDS WRITES A RACE.**
`__gt_coro_next` takes it around its own dequeue. `__gt_coro_enqueue` does **not** — it REQUIRES the
caller to hold it, because its callers already do: the completion thread holds it across its whole
abandoned-vs-normal decision, and `__gt_timer_check` / `__gt_proc_check` hold it across the store walk
they publish from. `osLockEnter` is a Win32 CRITICAL_SECTION and therefore RECURSIVE, so calling it
without the lock produces no diagnostic anywhere — only a queue two OS threads can be inside at once.

**A yield goes to the TAIL**, which is the whole content of *"let someone else have a turn"*. The
bootstrap measured what the other choice costs — *"a thousand yields from one green thread left a
sibling that had never run still unrun"* — and the tail is what refuses it.

### The green-thread run-queue hierarchy — and `spawn` is what reaches it

W212 built Go's three tiers. Nothing an `async` program does enters any of them, because what they
schedule is GREEN THREADS — and since SV1 a `spawn` produces those, so a program that spawns a service
reaches all three:

| Tier | What it is | Who writes it |
|---|---|---|
| **the per-P ring** | 256 fixed slots on the P struct, addressed by two MONOTONIC counters (`runqhead`, `runqtail`); the slot is `counter mod 256` and the length is `tail - head` | the owning P's M pushes at the tail with no lock; the owner AND any thief take from the head by CAS |
| **the global queue** | the intrusive FIFO through `GtOffNext` that used to be the whole scheduler | every mutation under `__sched_lock` |
| **stealing** | four rounds, each visiting every other ACTIVE P once from a random start, grabbing HALF a victim's ring into the thief's own | the thief, by CAS on the victim's head |

⛔ **WHICH TIER A PROGRAM REACHES IS OBSERVABLE IN THE EMITTED BINARY, NOT MERELY ASSERTED, AND IT IS
DECIDED BY WHETHER THE PROGRAM SPAWNS.** For an `async`-only program `__gt_ready` publishes to the owner's
coroutine queue, so nothing reaches `__sched_runq_put`; nothing reaches it, so nothing reaches
`__sched_wake_or_spawn`, so **no worker OS thread is created at any `MAXON_MAX_PROCS`** — and dead-code
elimination then takes the whole tier out. MEASURED on the first case below, the emitted program contains
`__sched_runq_put`, `__sched_runq_get`, `__sched_steal`, `__sched_find_runnable`, `__sched_worker_loop`,
`__sched_wake_or_spawn`, `__gt_enqueue` and `__gt_dequeue` before the EC10 pin and **none of the eight**
after it.

⭐ **A `spawn` PUTS ALL EIGHT BACK** (`SERVICES_DESIGN.md`, *"Send is a MOVE"*). `__svc_spawn` calls
`__gt_spawn_green` and publishes to a P RING, which is exactly what a ring, a steal and a worker loop are
for — so the five cases at the END of this file, which drive services, are the tier's first spec-level
readers. They are also why the tier is no longer describable as "unreached".

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

⛔⛔ **THIS PARAGRAPH USED TO READ *"A SPEC CASE CANNOT SET `MAXON_MAX_PROCS`, SO NOTHING BELOW EXERCISES
TWO PROCESSORS"*, AND BOTH HALVES OF THAT HAVE SINCE STOPPED BEING TRUE.** A case CAN name its own
processor count — `<!-- procs: N -->`, whose parser and whose gate are `specs-shv2/sched-default-procs.md`'s
— and the five SERVICE cases below now carry `procs: 1` explicitly rather than inheriting it from a default
that is about to become the machine's processor count. Each of them asserts something that IS a property of
one P — a ring's own monotonic counters, the every-61st-schedule fairness tick, the ring-versus-global
preference a yielder is routed against, a steal count of zero — so the marker is what preserves the
assertion rather than what narrows it. And the argument that used to follow, *"one P means one M,
which is what makes a global counter a legal channel"*, has been **withdrawn at the language level**: a
message may no longer write a module-level `var` at all (`specs-shv2/green-thread-globals.md`, E3143), and
every service case below now tallies in `self` and reports through an awaited reply. What remains true is
the COROUTINE half — a coroutine cannot reach a second processor at any processor count, so the `async`
answers below are the answers everywhere. **Work stealing, the head CAS under contention and the Dekker
fence on the ring publish are still out of reach from a case pinned to one P**; the multi-processor gate is
`maxon-shv2/track0/pin-matrix.sh`, which drives the `track0` programs across
`MAXON_MAX_PROCS ∈ {1, 2, 7, 12}` and asserts `workers=1 steals=0` of every COROUTINE-only program and
`workers >= 2, steals > 0` at N ≥ 2 of every SPAWN-driven one.

⛔ **AND THE DROPPED-WHILE-EXECUTING SHAPE IS UNREACHABLE FOR A COROUTINE ALTOGETHER, WHATEVER ELSE THE
PROGRAM SPAWNS.** It needs a second M popping the thread out of the dropper's queue while the dropper is
still spawning; a coroutine enters no queue a second M reads, so no processor count exposes it.
`maxon-shv2/track0/drop-running-torture.maxon` measures the shape a `spawn` DOES expose.

⚠ **EVERY CASE HERE CARRIES A `targets:` MARKER, and that is a property of the subject.** They are all
green-thread programs, and the green-thread substrate exists on exactly the lanes that have written it —
x64-windows and, since the arm64-macOS scheduler landed, that one too. `async-scheduler.md`'s *Targets*
section is the one statement of that gate.

⚠ **FIVE CASES HERE NAME x64-windows ALONE, AND THE REASON IS NOT THE RUN QUEUE.** Each additionally starts
a SERVICE, which reaches `__svc_spawn`/`__mbox_send` — a band whose own target gate
(`SemanticCheck.requireTargetSupportsServiceEntry`) refuses every lane but the first regardless of what the
scheduler beneath it provides, and which `services.md`'s `error.a-service-is-rejected-on-arm64` pins. They
widen when that band does.

## Tests

<!-- test: sched-runqueue.a-coroutine-spawned-by-a-coroutine-joins-its-owners-queue -->
<!-- targets: x64-windows, arm64-macos -->
**THE TRANSITIVITY THE WHOLE PIN RESTS ON.** A coroutine's owner is its SPAWNER'S owner, not its spawner
— so `inner`, spawned by the coroutine `outer`, belongs to the same green thread `main` does, and lands
in the SAME queue as `sibling`, which `main` spawned. It is the case that states the transitivity
DIRECTLY, in positions rather than in an exit code: `outer` runs first, spawns `inner` behind `sibling`,
and then awaits `inner` — which drives the ONE queue, so `sibling` runs before `inner` even though
`inner` is what is being awaited and `sibling` is nobody's business.

⚠ **IT IS NOT THE ONLY CASE THAT CAN SEE THE STAMP BE *WRONG* RATHER THAN MISSING — a first cut of this
paragraph said it was, and the review measured otherwise.** MEASURED against `__gt_spawn` stamping
`gt.owner = currentGt` — the SPAWNER — instead of `currentGt.owner`: this case reads
`ra=0 rb=1 posA=1 posB=2 posC=0` (`inner` lands in `outer`'s own queue, which nothing drives, so `await c`
bails and answers the unset result slot), **every other case in THIS FILE still passes** because none of
them nests an `async` inside an `async` — and **EIGHT cases elsewhere go red for the same sabotage**:
`async-await.nested`, `.nested-two-levels`, `.nested-in-expression`, `.nested-spawn-then-await-late`,
`.nested-void`, `.nested-try-await`, `.nested-sleep`, and `async-scheduler.nested`. So the transitivity is
gated whether or not this case exists; what this case adds is a reading that says WHICH queue rather than
a wrong answer. Removing the stamp altogether is the coarser sabotage and does not need this case: an
`owner` of 0 makes `__gt_coro_enqueue` write its queue ends through a null pointer, so **every**
green-thread program takes an access violation (exit `0xC0000005`, measured across all eight cases here).
A zero here is not a benign default and a plausible-looking non-zero is not enough either.
```maxon
var order = 0
var posA = 0
var posB = 0
var posC = 0

function inner() returns Integer
	__Builtins.parallelBoundary()
	order = order + 1
	posC = order
	return 1
end 'inner'

function sibling() returns Integer
	__Builtins.parallelBoundary()
	order = order + 1
	posB = order
	return 1
end 'sibling'

function outer() returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```stdout
ra=1 rb=1 posA=1 posB=2 posC=3
```
```exitcode
0
```

<!-- test: sched-runqueue.spawn-order-is-fifo-within-one-green-thread -->
<!-- targets: x64-windows, arm64-macos -->
**FIFO WITHIN ONE GREEN THREAD.** A coroutine queue is a queue and not a stack: three coroutines spawned
in order run in that order, whatever order their promises are awaited in. The awaits here run BACKWARDS
on purpose — `p3` first — so a queue that handed back the newest entry would be visible as `a3=1`.
```maxon
var order = 0
var a1 = 0
var a2 = 0
var a3 = 0

function first() returns Integer
	__Builtins.parallelBoundary()
	order = order + 1
	a1 = order
	return 1
end 'first'

function second() returns Integer
	__Builtins.parallelBoundary()
	order = order + 1
	a2 = order
	return 1
end 'second'

function third() returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```stdout
s=3 a1=1 a2=2 a3=3
```
```exitcode
0
```

<!-- test: sched-runqueue.a-yield-hands-the-processor-to-a-never-run-sibling -->
<!-- targets: x64-windows, arm64-macos -->
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

function yielder() returns Integer
	var i = 0
	while i < 1000 'spin'
		Runtime.yield()
		i = i + 1
	end 'spin'

	return siblingRan
end 'yielder'

function sibling() returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```stdout
seen=1 done=1
```
```exitcode
0
```

<!-- test: sched-runqueue.a-dropped-coroutine-in-the-queue-is-skipped-not-run -->
<!-- targets: x64-windows, arm64-macos -->
**THE POPPER'S HALF OF THE DROPPED-THREAD PROTOCOL.** `marker`'s promise is dropped while two live
coroutines sit around it in the queue — one ahead of it, one behind — so the drop's own front-of-queue
sweep cannot reach it and `__gt_coro_next` is what has to refuse it. `await p2` drives past `marker`; it
must reclaim that coroutine instead of running it, so `ran` stays 0 and the two live results still
arrive. A scheduler that ran it would both set `ran` and leave a thread nobody awaits, which is the
`__gt_live_count` abort (75) rather than 7.
```maxon
var ran = 0

function marker() returns Integer
	__Builtins.parallelBoundary()
	ran = ran + 1
	return 1
end 'marker'

function ok(v Integer) returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```stdout
ran=0
```
```exitcode
7
```

<!-- test: sched-runqueue.a-spawn-drop-loop-stays-bounded -->
<!-- targets: x64-windows, arm64-macos -->
**THE SWEEP'S HALF.** Five thousand coroutines are spawned and dropped without anything ever driving the
scheduler, so nothing would pop them and the mark alone would leave five thousand structs alive. Each
drop instead sweeps the tombstones off the FRONT of the dropped coroutine's own owner's queue, where the
one it just marked is sitting, so the live raw-allocation count stays at its steady state rather than
growing with the loop. `__Builtins.mmRawAllocLive()` counts exactly the population a GT struct belongs
to.
```maxon
var ran = 0

function neverRuns() returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```stdout
ran=0 live=bounded
```
```exitcode
0
```

<!-- test: sched-runqueue.a-drop-that-arrives-after-completion-reclaims -->
<!-- targets: x64-windows, arm64-macos -->
**THE RUNNER-FIRST ARRIVAL ORDER.** `p` is never awaited, but `await q` drives the scheduler past it, so
`p` runs to completion and the driver's hand-off adds the RUNNER ticket while the promise is still live —
the struct survives, because it still holds a result an un-awaited promise owns. The loop body's scope exit
then drops `p`, and that dropper is the SECOND arrival: it finds the runner's ticket and reclaims. Three
iterations, and `mmRawAllocLive()` is identical before and after, so every struct came back.
`gtIsComplete` is what proves the case reached the order it names — a `p` that had NOT completed would be
the tombstone order the two cases above cover instead.
```maxon
function done() returns Integer
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
		finished = finished + __Builtins.gtIsComplete(p.inner)
		i = i + 1
	end 'warm'

	let liveBefore = __Builtins.mmRawAllocLive()
	var j = 0

	while j < 3 'completedThenDropped'
		let p = async done()
		let q = async done()
		total = total + await q
		finished = finished + __Builtins.gtIsComplete(p.inner)
		j = j + 1
	end 'completedThenDropped'

	let liveGrew = __Builtins.mmRawAllocLive() - liveBefore
	print("total={total} finished={finished} liveGrew={liveGrew}")
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
total=4 finished=4 liveGrew=0
```
```exitcode
0
```

<!-- test: sched-runqueue.a-drop-of-a-parked-thread-is-both-halves -->
<!-- targets: x64-windows, arm64-macos -->
**THE ONE ARM WHERE ONE CALL IS BOTH PARTIES.** `s` parks on a 200 ms timer and `await f` returns long
before it fires, so the loop body's scope exit drops a thread that is registered in the timer store and
that no runner will ever come back for. The drop takes it out of the store under `__sched_lock` — and,
having done so, is the only holder left, so it performs the RUNNER's half (the stack the parked thread is
suspended on) and then the consumer's, which reclaims. Three iterations with no hang, because nothing ever
waits on the 200 ms deadline, and `mmRawAllocLive()` returns to its baseline.
```maxon
function done() returns Integer
	Runtime.yield()
	return 1
end 'done'

function sleeper() returns Integer
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
		stillParked = stillParked + (1 - __Builtins.gtIsComplete(s.inner))
		i = i + 1
	end 'warm'

	let liveBefore = __Builtins.mmRawAllocLive()
	var j = 0

	while j < 3 'parkedThenDropped'
		let s = async sleeper()
		let f = async done()
		total = total + await f
		stillParked = stillParked + (1 - __Builtins.gtIsComplete(s.inner))
		j = j + 1
	end 'parkedThenDropped'

	let liveGrew = __Builtins.mmRawAllocLive() - liveBefore
	print("total={total} stillParked={stillParked} liveGrew={liveGrew}")
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
total=4 stillParked=4 liveGrew=0
```
```exitcode
0
```

<!-- test: sched-runqueue.no-coroutine-is-ever-stolen -->
<!-- targets: x64-windows, arm64-macos -->
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
function work(v Integer) returns Integer
	__Builtins.parallelBoundary()
	return v
end 'work'

function main() returns ExitCode
	let p = async work(5)
	let r = await p
	print("r={r} steals={__Builtins.schedStealCount()}")
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
r=5 steals=0
```
```exitcode
0
```

<!-- test: sched-runqueue.ring-overflow-runs-every-spawned-service -->
<!-- targets: x64-windows -->
<!-- procs: 1 -->
**THE OVERFLOW, AND THE PROOF THAT NOTHING IS LOST IN IT.** Three hundred `spawn`s run back to back with
nothing between them that yields, so all 300 green threads are published before the first one runs. The ring
fills to its 256 slots and the 257th push moves the OLDEST HALF plus the new thread to the global queue. The
drain then unwinds both tiers, and every one of the 300 handles its one message exactly once whichever tier
it ended up in.

⛔⛔ **A MODULE-LEVEL `var ran` USED TO BE THE ONLY CHANNEL A SERVICE HAD, AND THIS CASE'S OWN NOTE ARGUED IT
WAS SOUND *"because a spec case gets no environment, `DefaultMaxProcs` is 1, and one P means one M"*.** That
argument expired with the default, and `specs-shv2/green-thread-globals.md` now refuses the write outright —
so each `Sink` tallies into its own field and `main` sums 300 awaited replies on the one green thread that
awaited them. The old note's escape hatch, `track0/service-torture.maxon`, is no longer the only place the
shape can be read at more than one processor: this program's answer is now processor-independent by
construction.

⭐ **AND `beforeAnyRan` IS NOW AN AWAITED REPLY FROM SINK #1, WHICH IS STRICTLY MORE THAN THE GLOBAL COULD
SAY.** Read off a global, the number could only ever be 0 — nothing had been *sent*, so no handler could have
run whatever the scheduler did, and the line asserted nothing. Read as a reply from the FIRST-spawned sink —
the one the 257th push moved out of the ring and onto the global queue — it says that thread is alive,
scheduled and answering, at the point in the program where the overflow has just happened and before a single
`bump` exists to confuse the reading.

⚠ **A LOST THREAD IS NOW A DIAGNOSIS RATHER THAN A SHORT NUMBER, AND THE BOUNDED SPIN IS GONE WITH THE
GLOBAL.** A thread the overflow dropped never runs, so the reply `main` awaits from it never resolves, every
green thread in the program is parked and none can become ready — which is `__sched_find_runnable`'s
`nothingLeft` arm, **exit 92**, in 35 ms at one processor (`services.a-blocking-cycle-through-an-indirect-call-aborts`
measures both that code and its timing). `<!-- procs: 1 -->` is what keeps that unconditional: the same case
measures the detector BIMODAL above one M.

⭐ **SEEN RED.** With `__sched_runq_put`'s overflow no longer publishing the thread that overflowed it —
one line, the `emitSchedEnqueueLocked` at `moveDone` — exactly one thread is lost, which is the arithmetic:
pushes 1-256 fill the ring, push 257 moves 128 out and drops the new one, and pushes 258-300 fit in the room
that made. Under the old global tally that read `ran=299` and exit 101; it now stops at the reply that never
comes.
```maxon
typealias SinkHandleArray = Array with Sink.handle

type Sink
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function bump()
		self.n = self.n + 1
	end 'bump'

	export function total() returns Integer
		return self.n
	end 'total'
end 'Sink'

function main() returns ExitCode
	var sinks = SinkHandleArray.create()

	var i = 0
	while i < 300 'spawnEach'
		sinks.push(spawn Sink.create())
		i = i + 1
	end 'spawnEach'

	// Sink #1 is the thread the 257th push moved to the global queue. Nothing has been sent yet, so this
	// asks the overflow's own victim to answer 0 — see the note above.
	let first = try sinks.get(0) otherwise panic("sinks.get OOB at 0 — the loop above pushed 300")
	let beforeAnyRan = try await first.total() otherwise 0
	print("beforeAnyRan={beforeAnyRan}\n")

	var k = 0
	while k < 300 'sendEach'
		let sink = try sinks.get(k) otherwise panic("sinks.get OOB at {k} — the loop is bounded by the count the pushes above filled")
		sink.bump()
		k = k + 1
	end 'sendEach'

	var ran = 0
	var n = 0
	while n < 300 'collect'
		let sink = try sinks.get(n) otherwise panic("sinks.get OOB at {n} — the loop is bounded by the count the pushes above filled")
		ran = ran + (try await sink.total() otherwise 0)
		n = n + 1
	end 'collect'

	print("ran={ran}")
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
beforeAnyRan=0
ran=300
```
```exitcode
0
```

<!-- test: sched-runqueue.the-ring-index-wraps-past-its-capacity -->
<!-- targets: x64-windows -->
<!-- procs: 1 -->
**THE WRAP.** `runqhead`/`runqtail` are monotonic counters, not indices. One service takes six thousand
messages one at a time, each drained before the next is sent, so the service is woken and re-published
six thousand times while the ring never holds more than one thread — which drives both counters far past
256 without ever filling a single slot. A counter used directly as a slot index addresses further and
further past the end of the P struct.

⚠⚠ **THE COUNT IS SIX THOUSAND FOR A MEASURED REASON, AND IT WAS RE-MEASURED FOR THIS SHAPE.** An
unmasked index writes and reads through the SAME wrong address, so the program's own answer stays correct
while it scribbles on whatever follows the P struct; the only observable is when it walks out of the
mapped region entirely. **MEASURED against exactly that sabotage** (`emitRunqSlotAddr` addressing the raw
monotonic counter with no `and RunqIndexMask`), one round count per build of this very program:

| rounds | reading under the sabotage |
|---|---|
| 400 | `sum=400`, exit 0 — **GREEN under the sabotage it exists to catch** |
| 1,000 | `sum=1000`, exit 0 |
| 2,000 | `sum=2000`, exit 0 |
| 3,000 | `sum=3000`, exit 0 |
| 4,000 | **SEGFAULT** (exit 139; through the harness, `0xC0000005`) |
| 5,000 | SEGFAULT |
| 6,000 | SEGFAULT |

⇒ six thousand is the smallest round number with a comfortable margin past the point where the mask stops
being invisible. **DO NOT "simplify" it back**: the first cut of this case used 400 rounds, and the table
above is what that would buy. (The pre-EC10 `async` shape of this case measured 2,000 green and 5,000
segfaulting — the same threshold within a factor of two, reached through a different producer.)

⛔ **THE TALLY MOVED OUT OF A MODULE-LEVEL `var sum` AND INTO `self`, WHICH `green-thread-globals.md` NOW
REQUIRES — AND THE SPIN LOOP WENT WITH IT.** The old round waited for the tick to be handled by watching a
shared word; the round now ASKS, and a reply is queued behind the tick that came before it, so the await is
the settle. **That only widens the margin the table above measures:** a round used to publish the service
once and now publishes it twice — the tick and the reply — so the monotonic counters reach any given
distance past 256 in FEWER rounds than when the table was taken, and 6,000 remains a floor rather than a
number to re-derive.

⚠ **`<!-- procs: 1 -->` IS WHAT KEEPS THAT TABLE APPLICABLE.** The distance a counter travels is per-P; a
service free to migrate between Ps splits its wakes across several rings and each one advances more slowly,
which is the one change that could put 6,000 rounds back under the threshold.
```maxon
type Step
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function tick()
		self.n = self.n + 1
	end 'tick'

	export function total() returns Integer
		return self.n
	end 'total'
end 'Step'

function main() returns ExitCode
	let h = spawn Step.create()

	var sum = 0
	var i = 0
	while i < 6000 'rounds'
		h.tick()
		// Queued behind the tick, so this IS the settle: the service is drained and re-published once more
		// per round, which is what drives both monotonic counters far past 256 slots.
		sum = try await h.total() otherwise 0
		i = i + 1
	end 'rounds'

	print("sum={sum}")
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
sum=6000
```
```exitcode
0
```

<!-- test: sched-runqueue.the-global-queue-is-consulted-within-sixty-one-schedules -->
<!-- targets: x64-windows -->
<!-- procs: 1 -->
**THE FAIRNESS CHECK, AND THE ONE SHAPE THAT CAN SEE IT.** The overflow above moves the OLDEST half of
the ring to the global queue, so service #1 — the first ever spawned — ends up at the global head while
~170 services remain in the ring. The scheduler prefers its ring, so without the every-61st-schedule
global check service #1 would run only after all ~170 of them; with it, it runs within the first 61
schedules. The case records the position at which #1 ran and asserts it is early.

⚠ It reports EARLY/LATE rather than the exact position, because the position depends on how many
schedules the drain loop has already spent — a number this spec has no business pinning. The two outcomes
are ~61 and ~171, so the boundary at 130 has a wide margin either way.

⭐ **SEEN RED, AND BOTH POSITIONS MEASURED.** Healthy, an instrumented copy of this program prints
`firstPos=61` — the fairness check firing on its first opportunity, since shv2 tests the tick AFTER the
increment. With the check disabled (`atFairness` compared against `GtFairnessInterval`, a value
`tick mod 61` can never take) it prints `firstPos=172` and this case reads `oldest=late`. 61 against 172
is why the boundary is a wide one rather than a pinned number.

⛔⛔ **THE SEQUENCE IS A SERVICE'S MAILBOX AND NO LONGER A MODULE-LEVEL `var runCount`, WHICH
`green-thread-globals.md` REFUSES — AND A MAILBOX IS THE RIGHT CHANNEL RATHER THAN A SUBSTITUTE FOR ONE.**
This case is the only one in the file whose subject is an ORDER ACROSS SERVICES, so its tally genuinely
cannot live in any one leaf's `self`. What it can live in is a 301st service: each leaf sends `note(id)`
fire-and-forget as it runs, and a mailbox is FIFO, so the order the notes are *enqueued* — which is the order
the leaves *ran* — is the order `Tally` counts them in. That is exactly the sequence the shared word used to
approximate, and it is a sequence rather than a read-modify-write, so no interleaving can lose a step at any
processor count.

⚠ **`Tally` IS SPAWNED LAST, AFTER ALL 300 LEAVES, AND THAT IS NOT TIDINESS.** The whole measurement rests on
leaf #1 being the FIRST thread ever published, so that the 257th push is what moves it to the global head.
Spawning the collector ahead of the leaves would put it in that slot and shift every position the table above
was measured at.

⚠ **THE BOUNDED DRAIN SURVIVES, AND SO DOES ITS REASON.** `main` now asks `Tally` for the count instead of
reading a word, but the loop is still bounded, so a leaf the overflow dropped still leaves `runCount` short
and still prints a wrong answer rather than wedging.
```maxon
typealias LeafHandleArray = Array with Leaf.handle

let drainSpinLimit = 200000

// The 301st service, and the only shared sequence in the program. A send is an ENQUEUE, so the order these
// arrive in is the order the leaves ran — with no word two green threads both step.
type Tally
	var n as Integer
	var firstPos as Integer

	static function create() returns Self
		return Self{n: 0, firstPos: 0}
	end 'create'

	export function note(id Integer)
		self.n = self.n + 1
		if id == 1 'oldest'
			self.firstPos = self.n
		end 'oldest'
	end 'note'

	export function count() returns Integer
		return self.n
	end 'count'

	export function oldestPosition() returns Integer
		return self.firstPos
	end 'oldestPosition'
end 'Tally'

type Leaf
	var id as Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'

	export function go(sink Tally.handle)
		sink.note(self.id)
	end 'go'
end 'Leaf'

function main() returns ExitCode
	var leaves = LeafHandleArray.create()

	var i = 0
	while i < 300 'spawnEach'
		leaves.push(spawn Leaf.create(i + 1))
		i = i + 1
	end 'spawnEach'

	// Last, so leaf #1 keeps the first-published slot the overflow arithmetic depends on.
	let tally = spawn Tally.create()

	var k = 0
	while k < 300 'sendEach'
		let leaf = try leaves.get(k) otherwise panic("leaves.get OOB at {k} — the loop is bounded by the count the pushes above filled")
		// A send MOVES its argument, so each leaf gets its own reference to the collector.
		leaf.go(tally.clone())
		k = k + 1
	end 'sendEach'

	var runCount = 0
	var spins = 0
	while runCount < 300 and spins < drainSpinLimit 'drain'
		runCount = try await tally.count() otherwise 0
		spins = spins + 1
	end 'drain'

	let firstPos = try await tally.oldestPosition() otherwise 0
	if firstPos < 130 'early'
		print("runCount={runCount} oldest=early")
	end 'early' else 'late'
		print("runCount={runCount} oldest=late")
	end 'late'

	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
runCount=300 oldest=early
```
```exitcode
0
```

<!-- test: sched-runqueue.a-yield-goes-behind-the-global-queue -->
<!-- targets: x64-windows -->
<!-- procs: 1 -->
**THE BACK OF THE QUEUE, AND THE ONLY SHAPE AT ONE PROCESSOR THAT CAN SEE IT.** A drained yielder goes to
the GLOBAL queue's tail; the scheduler consults its RING first, so anything pushed to the ring AFTER the
yielder was drained still runs BEFORE the yielder resumes. That is Go's split exactly (`Gosched` →
`globrunqput`, `goready` → `runqput`).

⚠ **AND IT TAKES THREE GREEN THREADS, BECAUSE TWO CANNOT TELL THE TWO ENDS APART.** Both ends are FIFO,
so a yielder routed to either one lands behind everything already queued — the difference only shows
against a thread queued AFTERWARDS. `yielder` yields (and is drained while `spawner` is still waiting in
the ring); `spawner` then runs and `spawn`s `spawnee` into the ring; the ring is preferred, so `spawnee`
runs FIRST and the yielder resumes second. Routed to the ring instead, the yielder would sit ahead of
`spawnee` and the two positions would swap.

⭐⭐ **SEEN RED TWICE, FOR TWO DIFFERENT CAUSES, AND IT IS THE ONLY CASE IN THIS FILE THAT CATCHES EITHER.**
Against the tree this case was restored into it read `sPos=2 yPos=1` — not a routing bug but a yield that
handed off to nobody at all, `__gt_resched` asking only about its owner's COROUTINE queue (see
`GtRuntime.GtReschedName`, where the three shapes of that defect are written down). Against the fixed tree
with `__sched_publish_yield` re-pointed at the local ring — one argument, `SchedGreenEnd.localRing` — it
reads `sPos=2 yPos=1` again, now for the reason its own prose names. **MEASURED under that second
sabotage, every other case in this file stays GREEN, `a-yield-hands-the-processor-to-a-never-run-sibling`
included**: that one discriminates the FRONT of a queue from its TAIL, which a single FIFO can express,
and is blind to RING-versus-GLOBAL, which only a green thread has two tiers to have.

⚠ **`spawnee`'s HANDLE IS DROPPED BEFORE IT HAS RUN, AND THAT IS THE POINT AT WHICH IT IS STILL QUEUED.**
A drop closes the mailbox, and a closed mailbox DRAINS what is already in it — so the message survives
its handle and the service still runs it once.

⛔⛔ **THE POSITIONS COME OFF A COLLECTOR'S MAILBOX AND NO LONGER OFF A MODULE-LEVEL `var order`, WHICH
`green-thread-globals.md` REFUSES.** Like the fairness case above, this one's subject is an ORDER ACROSS
services, so the sequence cannot live in any one of them; it lives in a `Tally` service each of the three
sends `note(id)` to as it runs. A send is an ENQUEUE and a mailbox is FIFO, so the arrival order IS the run
order — the same discrimination the shared word made, through a channel with no read-modify-write in it.

⚠ **`Tally` IS SPAWNED THIRD, AFTER `y` AND `s`.** Both of those keep the publication slots the argument
above turns on; a collector spawned ahead of them would take `y`'s.
```maxon
let drainSpinLimit = 200000

let spawneeId = 1
let yielderId = 2

// The only shared sequence in the program — see the note above.
type Tally
	var n as Integer
	var sPos as Integer
	var yPos as Integer

	static function create() returns Self
		return Self{n: 0, sPos: 0, yPos: 0}
	end 'create'

	export function note(id Integer)
		self.n = self.n + 1
		if id == spawneeId 'theSpawnee'
			self.sPos = self.n
		end 'theSpawnee' else 'theYielder'
			self.yPos = self.n
		end 'theYielder'
	end 'note'

	export function count() returns Integer
		return self.n
	end 'count'

	export function spawneePosition() returns Integer
		return self.sPos
	end 'spawneePosition'

	export function yielderPosition() returns Integer
		return self.yPos
	end 'yielderPosition'
end 'Tally'

type Spawnee
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function go(sink Tally.handle)
		sink.note(spawneeId)
	end 'go'
end 'Spawnee'

type Spawner
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function go(sink Tally.handle)
		let child = spawn Spawnee.create()
		// A send MOVES, and `sink` arrived BORROWED, so the child gets its own reference.
		child.go(sink.clone())
	end 'go'
end 'Spawner'

type Yielder
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function go(sink Tally.handle)
		Runtime.yield()
		sink.note(yielderId)
	end 'go'
end 'Yielder'

function main() returns ExitCode
	let y = spawn Yielder.create()
	let s = spawn Spawner.create()
	let tally = spawn Tally.create()
	y.go(tally.clone())
	s.go(tally.clone())

	var order = 0
	var spins = 0
	while order < 2 and spins < drainSpinLimit 'drain'
		order = try await tally.count() otherwise 0
		spins = spins + 1
	end 'drain'

	let sPos = try await tally.spawneePosition() otherwise 0
	let yPos = try await tally.yielderPosition() otherwise 0
	print("sPos={sPos} yPos={yPos}")
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
sPos=1 yPos=2
```
```exitcode
0
```

<!-- test: sched-runqueue.nothing-is-stolen-on-one-processor -->
<!-- targets: x64-windows -->
<!-- procs: 1 -->
**THE STEAL COUNTER, AND THE ONE ANSWER A SPEC CAN PIN — the SERVICE twin of
`no-coroutine-is-ever-stolen` above, and the reason the two are different cases.** That one reads 0
because a coroutine never enters a P ring at all, so its zero is an arm nothing executes. This one reads
0 for the opposite reason: twelve services and twelve hundred messages DO fill a ring, `__sched_steal` IS
laid out and its rounds ARE reached — there is simply nobody to steal from at one processor. That is what
makes the `steals > 0` reading in `track0/pin-matrix.sh` a measurement rather than a number that is
always there.

⭐ **SEEN RED, AND THE TWIN STAYED GREEN — which is the whole reason these are two cases.** With
`DefaultMaxProcs` raised from 1 to 12, so that a spec case finally HAS somebody to steal from, this case
reads `done=1200 steals=1062` and `steals=1197` on two builds — while `no-coroutine-is-ever-stolen` above
**passes unchanged**, because a coroutine enters no queue a thief can reach at any processor count. One
sabotage, two opposite answers, each the one its case claims.

⛔⛔ **THE TALLY WAS A MODULE-LEVEL `var done` UNTIL `green-thread-globals.md` REFUSED IT, AND THIS CASE'S
OWN NUMBERS ARE THAT SPEC'S OPENING MEASUREMENT.** `done = done + 1` inside `Work.go` is a load, an add and
a store on a word twelve green threads share; re-run at `MAXON_MAX_PROCS=16`, ten times, it read 1200, 1200,
**1199**, **1199**, **1198**, **1199**, **1199**, 1200, **1199**, 1200 — five runs short, all ten exit 0.
Each `Work` now tallies into its own field and `main` sums twelve awaited replies on the one green thread
that awaited them, so the 1200 is arithmetic rather than a coincidence of scheduling. **The drain spin loop
is gone with it and that is a strengthening, not a simplification:** a reply arrives only after the 100 `go`s
queued ahead of it, so the awaits ARE the drain — where the old bounded spin could give up and print a short
number that looked like a lost message, a lost message now shows as a short sum and nothing else can produce
one.

⚠ **AND IT NOW CARRIES `<!-- procs: 1 -->`, WHICH ITS NAME ALWAYS IMPLIED AND THE DEFAULT USED TO SUPPLY.**
`steals=0` is a claim about ONE processor and is false at any other count — the sabotage above measures
`steals=1062` at twelve. The `done=1200` half is now processor-independent; the `steals=0` half is not, and
pinning the count is what keeps it the assertion its name makes.
```maxon
typealias WorkHandleArray = Array with Work.handle

type Work
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function go()
		self.n = self.n + 1
	end 'go'

	export function total() returns Integer
		return self.n
	end 'total'
end 'Work'

function main() returns ExitCode
	var workers = WorkHandleArray.create()

	var i = 0
	while i < 12 'spawnEach'
		workers.push(spawn Work.create())
		i = i + 1
	end 'spawnEach'

	var r = 0
	while r < 100 'eachRound'
		var k = 0
		while k < 12 'eachWorker'
			let worker = try workers.get(k) otherwise panic("workers.get OOB at {k} — the loop is bounded by the count the pushes above filled")
			worker.go()
			k = k + 1
		end 'eachWorker'
		r = r + 1
	end 'eachRound'

	// A reply is queued behind the 100 sends that came before it, so awaiting all twelve is the drain.
	var done = 0
	var n = 0
	while n < 12 'collect'
		let worker = try workers.get(n) otherwise panic("workers.get OOB at {n} — the loop is bounded by the count the pushes above filled")
		done = done + (try await worker.total() otherwise 0)
		n = n + 1
	end 'collect'

	print("done={done} steals={__Builtins.schedStealCount()}")
	return 0 as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
done=1200 steals=0
```
```exitcode
0
```
