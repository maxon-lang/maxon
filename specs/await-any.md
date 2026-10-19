---
feature: await-any
status: stable
keywords: [async, promise, awaitAny, select, scheduler]
category: concurrency
---

# `promises.awaitAny()` — ONE WAITING PRIMITIVE FOR REPLIES, FILE IO AND SUBPROCESS DRAINS

## Documentation

`await p` parks the caller until **one named** promise completes. A dispatcher holding N of them
cannot use it: awaiting slot 0 while slot 3 has already answered is head-of-line blocking, and the whole
point of running N children is that whichever finishes first is served first.

`promises.awaitAny()` is the way out. It is a member of every array whose element type is a
`Promise with …`: it parks the caller until **some** element has completed, and returns that element's
**index** as an unnamed `int`, which indexes the array (`get(ready)`) and adds to an `Integer` with no cast.

```maxon
let ready = drains.awaitAny()
```

### It returns an INDEX, and it does NOT consume

This is the whole design, and it is why `awaitAny` is a function rather than a `select` statement with an
`any` arm. An `any` arm would consume an array **slot**, and slot-level linearity is precisely the
documented gap in `await`'s ownership rule (*"awaiting the same array slot twice is not statically
caught"*) — so the statement form would replace a hand-rolled poll with a construct whose misuse is a
runtime double free. `awaitAny` returns a number. The caller then awaits exactly one promise, by the
ordinary `await`, under the ordinary rules; the primitive is neutral on the existing hole rather than
widening it. It also needs zero grammar.

### ⚠ THE LOSERS ARE STILL IN THE ARRAY, AND WHAT YOU DO WITH THEM DECIDES THE EXIT CODE

`awaitAny` names one index. The other promises are **un-awaited and still in the array**, and they are the
caller's to finish. Two outcomes, both live in this file:

| What the program does next | Result |
|---|---|
| **awaits the rest** (this file's `leaves-the-others-awaitable`) | correct and **leak-free**, exit 0 |
| **drops the array with losers still in it** | **leak-free**, exit 0 — the array drops each un-awaited loser |

✅ **THE SECOND ROW IS NOT A LEAK.** A container is an OWNER of its promise elements and drops each
un-awaited one when it dies, so the green-thread live count returns to zero — the case that pins the composition is
`the-losers-are-dropped-when-the-array-dies`. See
`async-promise-drop.a-container-drops-its-un-awaited-elements` for the isolated statement of it.

The motivating consumer is unaffected, and that is the point of choosing a primitive that does not
consume: a worker pool selecting over drains **awaits every drain eventually**.

### A service REPLY is an ordinary promise, so it selects the same way

There is no separate "channel select" in this design. A handler reply is a `Promise`, so it goes into the
same array and this same primitive picks the first one to answer — `over-service-replies` is that case, and
it is why the design carries a waiting primitive rather than a mailbox-specific select: a reply is an
ordinary promise (`specs/services.md`).

The storage names the reply's own error type: `Promise with T` for a message whose handler cannot throw,
`Promise with (T, E)` with the handler's error type `E` for one that can. A send that cannot be queued fails
at the send, with the message's own `sendError`, so a reply carries no service error of its own
(`specs/services.md`).

### A wait registers on EVERY slot, and the first completer readies it once

`__gt_await_any` scans the array first (`__gt_any_completed`) and returns at once if some element has
already completed, having parked on nothing. Otherwise it parks the caller like every other wait, and the
registration (`__gt_register_park`'s awaitAny arm) runs on the far side of the switch, holding every
slot's own promise lock: it scans again, readies the caller at once if a slot completed in between, and
otherwise stores the caller as the awaiter of every slot — tagged (`AwaitAnyTag`), with its `selectDone`
word cleared. That is Go's `selectgo`, one registration per case, made under every case's lock at once.

A completing slot takes the awaiter word in `__gt_runner_done` under that slot's own lock. Finding the
tagged awaiter, it claims the select with a compare-and-swap of `selectDone` from pending to readied, and
the one that wins readies the caller through `__gt_ready_locked` after leaving the lock. A second slot
completing before the caller runs loses that compare-and-swap and readies nothing, so two slots completing
at once on two processors, each holding only its own lock, ready the caller exactly once. On waking, the
caller takes every slot's lock again, clears each awaiter word that still names it, and scans. So no slot
keeps a registration once `awaitAny` has returned, and a later plain `await` of any element finds the word
clear.

⚠ **A SLOT HOLDS ONE AWAITER.** The registration refuses a slot whose awaiter word is already taken
(`schedulerPromiseAwaitedTwice`) rather than overwrite it, because the overwritten waiter would never be
woken. The front end cannot produce that shape.

### K registrations, K locks, taken in address order

Each promise's awaiter word is guarded by a lock word in the record it names, the lock a plain `await` and
the completer meet under (Go's `hchan.lock`). A K-way wait must hold all K at once, so the registration and
the withdrawal gather the array's non-zero slots, sort them by address, and lock them in ascending order,
releasing them in the reverse order (`__gt_any_lock`, `__gt_any_unlock`) — Go's `sellock`. Every multi-lock
holder takes the locks in that one order, and a completer only ever holds its own one, so no two holders can
each wait on a lock the other has: no deadlock, whatever the array's order and however many selects share a
promise. Sorting by address rather than by slot index is what makes the order one order across arrays.

⚠ **NO GREEN THREAD HOLDS A PROMISE LOCK ACROSS A PARK.** A promise lock is **recursive on the wrong
identity** (its owner token is the OS THREAD's own block) while several green threads share one —
so a green thread parking while holding one would let a different green thread on the same M find its
own token in the owner slot, step the depth, and enter the hold the parked thread is still inside. Not a
deadlock; silent corruption. So the registration, the completer's hand-over and the withdrawal each take their locks and release them with no
switch in between — the registration on a machine's scheduler context after the park's switch, the other
two in straight-line code.

### What the scan reads, and the one thing the caller owes it

A slot is skipped when it holds `0` — the zero a `resize` leaves in a slot nobody filled. Every other slot
is read as a green-thread handle and its status word compared against `completed`.

⚠ **A promise that has already been AWAITED leaves a stale handle in its slot**, because `await` recycles
the green-thread struct and nothing writes the slot back. That is the same contract a promise's
`isComplete()` has — the read asks nothing of its handle beyond it being one
— and the same slot-level linearity gap named above. A caller that re-selects over an array must
overwrite a consumed slot, exactly as `Testing/SpecWorkerPool.sendAndDrain` re-arms a drain with `set`.
The registration writes the awaiter word of every slot it parks on, so a stale handle there is a write
into a recycled struct, not only a wrong read.

### An EMPTY array is a scheduler deadlock, and that is Go's answer too

Awaiting any of zero promises can never complete. The scan finds nothing, the registration has no slot to
register on, and the caller parks with nothing that can ever ready it. Once every machine is idle, the last
one to join the idle list runs the deadlock check (`__sched_checkdead`, Go's `checkdead` at `mput`): no
machine running, no timer, child or read pending, and `main` not finished — **exit 92**, the same answer a
plain `await` of a thread nobody can run gets. Go's `select {}` reaches its own deadlock detector for
exactly this reason. `an-empty-array-is-a-scheduler-deadlock` pins it.

### Why it is a member the compiler serves

`awaitAny` is not an ordinary stdlib declaration, as `sleep(ms)` is `stdlib/Sleep.maxon`'s declaration over
`__Builtins.sleep`: such a declaration's parameter would be `Array with Promise with T` for a type parameter
`T`, and the primitive's rule is *"the element type is a `Promise with …`"*, which no type parameter can be
proven to be. So the compiler serves `awaitAny()` as a member of a promise array, the way it serves
`isComplete()` on a promise, and lowers it to the runtime intrinsic `__Builtins.awaitAny`. The intrinsic is
library-tier like every `__Builtins` member (E3185 outside `stdlib/` and `runtime/`); `arity-checked` and
`error.operand-type` are about the intrinsic's own argument rules, so they compile in the library tier.

### Targets — the lanes with a green-thread substrate, refused by name everywhere else

`__gt_await_any` is the green-thread scheduler. It is named on the `greenThreads` facility's roster beside
`__gt_sleep` and `__gt_resched`, so a program that calls it on a target whose row denies that facility reads
**E3104 at the call's own span**, naming the runtime entry — never a panic from inside a backend.
`error.rejected-on-wasm` pins that attribution. All four native lanes provide it; wasm32-wasi does not.

The two front-end cases (`arity-checked`, `error.operand-type`) reach no substrate at all and carry no target
marker. Neither does `over-service-replies`: a service runs on every lane with a green-thread substrate,
and wasm32-wasi refuses `__svc_spawn` with the same E3104, which the harness counts as a SKIP.

## Tests

<!-- test: await-any.returns-the-first-completed-index -->
⭐ **THE DISCRIMINATING CASE — the index returned is the one that FINISHED, not the one that is first in
the array.** Slot 0 and slot 2 park on timers; slot 1 only yields, so it is the one that reaches
`completed`. A scan that looked at slot 0 alone, or that returned the first slot it could read rather than
the first slot that had completed, answers `0` here.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function slow(ms Integer) returns Integer
	sleep(ms as Milliseconds)
	return 1
end 'slow'

function quick() returns Integer
	Scheduler.yield()
	return 2
end 'quick'

function main() returns ExitCode
	var arr = IntPromiseArray.create()
	arr.push(async slow(120))
	arr.push(async quick())
	arr.push(async slow(240))
	let ready = arr.awaitAny()
	var score = 0 as Integer
	if ready == 1 'themiddleonefinishedfirst'
		score = score + 3
	end 'themiddleonefinishedfirst'
	var sum = 0 as Integer
	for p in arr 'drainthemall'
		sum = sum + await p
	end 'drainthemall'
	if sum == 4 'everypromisewasstillawaitable'
		score = score + 4
	end 'everypromisewasstillawaitable'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: await-any.no-park-when-one-is-already-complete -->
⭐ **AN ALREADY-COMPLETE SLOT IS ANSWERED BY THE SCAN, BEFORE ANY PARK.** The promise runs to completion
under `main`'s own `Scheduler.yield()` loop before `awaitAny` is called, and it is the only one in the
program — so at the moment of the call nothing else is runnable, no timer is pending and no child is
parked. The scan at the top of `__gt_await_any` finds slot 0 `completed` and returns `0` without parking.

⚠ The exit code cannot tell that road from the one behind it: were the scan skipped, the registration's own
scan under the slots' promise locks would find the same slot and ready the caller at once. What this case pins is the
answer — the completed slot is named, and its promise is still awaitable afterwards.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function makeValue() returns Integer
	Scheduler.yield()
	return 42
end 'makeValue'

function main() returns ExitCode
	let maxSpins = 100
	var arr = IntPromiseArray.create()
	arr.push(async makeValue())
	let p = try arr.get(0) otherwise panic("the promise was just pushed")
	var spins = 0 as Integer
	var done = false
	while spins < maxSpins and not done 'drive'
		Scheduler.yield()
		done = p.isComplete()
		spins = spins + 1
	end 'drive'
	var score = 0 as Integer
	if done 'itwasalreadycompletebeforethecall'
		score = score + 1
	end 'itwasalreadycompletebeforethecall'
	if arr.awaitAny() == 0 'thecompletedslotisnamed'
		score = score + 2
	end 'thecompletedslotisnamed'
	if await p == 42 'stillawaitable'
		score = score + 4
	end 'stillawaitable'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: await-any.leaves-the-others-awaitable -->
⭐ **THE CENTRAL CASE.** `awaitAny` names one index and RETIRES NOTHING: every promise in the array,
winner included, is still awaitable afterwards, and a program that awaits them all is leak-free. This is
the shape the motivating consumer has — a pool selects, serves the ready drain, and eventually awaits
every drain it dispatched.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function value(v Integer) returns Integer
	Scheduler.yield()
	return v
end 'value'

function main() returns ExitCode
	var arr = IntPromiseArray.create()
	arr.push(async value(10))
	arr.push(async value(20))
	arr.push(async value(30))
	let first = arr.awaitAny()
	let second = arr.awaitAny()
	var score = 0 as Integer
	if first == second 'aselectiswithoutsideeffect'
		score = score + 1
	end 'aselectiswithoutsideeffect'
	var sum = 0 as Integer
	for p in arr 'awaiteveryone'
		sum = sum + await p
	end 'awaiteveryone'
	if sum == 60 'allthreewerestillthere'
		score = score + 6
	end 'allthreewerestillthere'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: await-any.over-a-mixed-array-of-sleeps -->
⭐ **THE TIMER CASE — nothing is runnable at all, so the machine BLOCKS on the earliest deadline.** Three
sleepers and no other work: once all three coroutines are parked on their timers and `main` is parked in
`awaitAny`, the machine has nothing to run and parks until the nearest deadline rather than spinning
(`SchedRuntime.emitSchedParkTimeout`). Firing it readies the 20 ms sleeper, whose completion readies
`main`. The index that comes back is the shortest sleeper, which is deadline order and not array order.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function nap(ms Integer, tag Integer) returns Integer
	sleep(ms as Milliseconds)
	return tag
end 'nap'

function main() returns ExitCode
	var arr = IntPromiseArray.create()
	arr.push(async nap(200, tag: 1))
	arr.push(async nap(20, tag: 2))
	arr.push(async nap(400, tag: 3))
	let ready = arr.awaitAny()
	var score = 0 as Integer
	if ready == 1 'theearliestdeadlinewon'
		score = score + 3
	end 'theearliestdeadlinewon'
	var sum = 0 as Integer
	for p in arr 'drainthemall'
		sum = sum + await p
	end 'drainthemall'
	if sum == 6 'everysleeperwasstillawaitable'
		score = score + 4
	end 'everysleeperwasstillawaitable'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: await-any.over-service-replies -->
⭐⭐ **THE COMPOSITION THAT MAKES THE PRIMITIVE WORTH HAVING.** A handler reply is an ordinary `Promise`, so
ONE waiting primitive covers service replies, file IO and subprocess drains — there is no separate "channel
select" anywhere in the design. Two services are sent to; the first handler sleeps and the second answers at
once, so the index that comes back is `1`: **reply order, not send order**, which is the whole point of
selecting rather than awaiting slot 0.

⚠ The storage is a plain `Promise with Integer` because neither handler throws (`specs/services.md`), so
each reply is awaited with a plain `await`, and both are awaited afterwards because `awaitAny` retires
nothing.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReplyPromise = Promise with Integer
typealias ReplyPromiseArray = Array with ReplyPromise

type Slow
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function value() returns Integer
		sleep(80)
		return 1
	end 'value'
end 'Slow'

type Quick
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function value() returns Integer
		return 2
	end 'value'
end 'Quick'

function main() returns ExitCode
	let slow = spawn Slow.create()
	let quick = spawn Quick.create()
	var ps = ReplyPromiseArray.create()
	ps.push(try slow.value() otherwise panic("send slow"))
	ps.push(try quick.value() otherwise panic("send quick"))
	let ready = ps.awaitAny()
	var score = 0 as Integer
	if ready == 1 'thequickreplywonthoughitwassentsecond'
		score = score + 3
	end 'thequickreplywonthoughitwassentsecond'
	var sum = 0 as Integer
	for p in ps 'drainbothreplies'
		sum = sum + (await p)
	end 'drainbothreplies'
	if sum == 3 'bothrepliessurvivedtheselect'
		score = score + 4
	end 'bothrepliessurvivedtheselect'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: await-any.selects-from-inside-an-async-body -->
⭐ **A SELECT INSIDE A COROUTINE — the waiter is not its strand's owner.** `selectOver` is a coroutine of
`main`'s green thread, and `main` is itself parked on `await outer` while it selects. So the thread the
registration stores on each slot, the one a completer readies, and the one that withdraws on waking are all
the coroutine rather than `main`: it is readied to the BACK of `main`'s strand queue, where a readied owner
goes to the front (`__gt_ready_locked`), and the withdrawal names itself by the running thread
(`M->currentGt`), which must agree with the thread the registration stored.

A select that only ever ran on a strand's owner would leave that whole road untested for this entry point.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function inner(v Integer) returns Integer
	Scheduler.yield()
	return v
end 'inner'

function selectOver() returns Integer
	var ps = IntPromiseArray.create()
	ps.push(async inner(3))
	ps.push(async inner(4))
	let ready = ps.awaitAny()
	var sum = 0 as Integer
	for p in ps 'all'
		sum = sum + await p
	end 'all'
	return sum + (ready as Integer)
end 'selectOver'

function main() returns ExitCode
	let outer = async selectOver()
	return await outer as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: await-any.a-slot-nobody-filled-is-skipped -->
⭐ **THE NULL-SLOT GUARD, AND IT IS REACHABLE RATHER THAN DEFENSIVE.** `Array.resize` refuses a MANAGED
element type at compile time (E3106) — but a `Promise with …` is not managed (its value IS the green-thread
pointer, `PromiseType.maxon`'s first fact), so `resize` is legal here and publishes length over slots nobody
filled. Those slots are zero, and a zero in a promise column is an ABSENCE, not a handle: reading a status
word through it faults on the null page.

The scan therefore skips a `0` slot and keeps going, so the answer is `2` — the only slot with a promise in
it — and `2 * 10 + 42` says both halves in one number.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function makeValue() returns Integer
	Scheduler.yield()
	return 42
end 'makeValue'

function main() returns ExitCode
	var arr = IntPromiseArray.create()
	arr.resize(2)
	arr.push(async makeValue())
	let ready = arr.awaitAny()
	let p = try arr.get(ready) otherwise panic("awaitAny named a slot that is in range")
	return ((ready as Integer) * 10 + await p) as ExitCode
end 'main'
```
```exitcode
62
```

<!-- test: await-any.an-empty-array-is-a-scheduler-deadlock -->
⭐ **A SELECT OVER NOTHING PARKS ON NOTHING.** The registration has no slot to store `main` on, so `main`
parks with nothing that can ready it and the machine running it goes idle. As the last machine joins the
idle list, `__sched_checkdead` finds no machine running, no timer, child or read pending, and `main`
unfinished — exit **92**, promptly, rather than a hang or an index naming a promise that never completed.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function main() returns ExitCode
	var arr = IntPromiseArray.create()
	let ready = arr.awaitAny()
	return ready as ExitCode
end 'main'
```
```exitcode
92
```
```stderr
fatal error: runtime abort 92 (schedulerDeadlock)
```

<!-- test: await-any.the-losers-are-dropped-when-the-array-dies -->
⭐ The composition: select, serve the winner, and let the array die with the losers still in it. Exit 0
is what a container that drops its promise elements gives; a container that did not would leave the losers
live and exit **75**.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function value(v Integer) returns Integer
	Scheduler.yield()
	return v
end 'value'

function main() returns ExitCode
	var arr = IntPromiseArray.create()
	arr.push(async value(1))
	arr.push(async value(2))
	let ready = arr.awaitAny()
	let winner = try arr.get(ready) otherwise panic("awaitAny named a slot that is in range")
	_ = await winner
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: await-any.replies-racing-the-registration-across-many-rounds -->
<!-- procs: 4 -->
⭐⭐ **A SELECT WHOSE SLOTS COMPLETE ON OTHER PROCESSORS WHILE IT REGISTERS.** Each round sends one message to
each of four services and selects over the four replies. At four processors the services answer on their
own machines, so a reply can complete before `awaitAny`'s scan, between the scan and the registration, or
after the registration, and several can complete at once. Every one of those orders must give the same
answers. The slot `awaitAny` names has completed. Exactly one completer readies the selector, and a second
one finds the select already claimed and readies nothing. When the selector wakes it withdraws from every
slot, so each reply is then awaited once, by the plain `await`, with no registration left on it. A reply that
readied the selector twice, or a registration left behind on a slot, aborts the program
(`schedulerPromiseAwaitedTwice`) or loses a reply, and the sum comes out short.

✅ **IT IS A CONTROL: ANY SOUND AWAITER PROTOCOL PASSES IT, AND IT HOLDS THIS ONE TO ITS ANSWERS.** Each
promise has a lock of its own, and the registrant and the completer meet under it. The selector's
`selectDone` is claimed by a compare-and-swap, and a select takes its slots' locks in address order, so no
lock any two slots share is taken at all. `replies-racing-the-registration-at-one-processor` is its twin at
one processor, where every reply completes after the registration.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReplyPromise = Promise with Integer
typealias ReplyPromiseArray = Array with ReplyPromise
typealias EchoHandleArray = Array with Echo.handle

let rounds = 300
let services = 4

type Echo
	var served as Integer

	static function create() returns Self
		return Self{served: 0}
	end 'create'

	export function value(v Integer) returns Integer
		self.served = self.served + 1
		return v
	end 'value'
end 'Echo'

function main() returns ExitCode
	var echoes = EchoHandleArray.create()
	for _ in 0 upto services 'spawnEach'
		echoes.push(spawn Echo.create())
	end 'spawnEach'

	var sum = 0 as Integer
	var completedPicks = 0 as Integer
	for r in 0 upto rounds 'eachRound'
		var replies = ReplyPromiseArray.create()
		for (slot, echo) in echoes.withIterator() 'sendEach'
			replies.push(try echo.value((r as Integer) * services + (slot.index() as Integer)) otherwise panic("send value"))
		end 'sendEach'

		let ready = replies.awaitAny()

		for (slot, p) in replies.withIterator() 'drain'
			if slot.index() == ready and p.isComplete() 'theNamedSlotHadCompleted'
				completedPicks = completedPicks + 1
			end 'theNamedSlotHadCompleted'

			sum = sum + (await p)
		end 'drain'
	end 'eachRound'

	print("picks={completedPicks} sum={sum}\n")
	return 0
end 'main'
```
```stdout
picks=300 sum=719400
```
```exitcode
0
```

<!-- test: await-any.replies-racing-the-registration-at-one-processor -->
<!-- procs: 1 -->
<!-- preempt: off -->
**THE ONE-PROCESSOR TWIN OF `replies-racing-the-registration-across-many-rounds`.** With one processor the
services run only once `main` has parked in `awaitAny`. So every reply completes after the registration, and
the completer always finds the tagged awaiter. The `preempt: off` marker is what keeps that order: without it
the system monitor could take the processor from `main` between two sends and run a service before the
select. That road must give the same answers as the racing one. Like its twin, it is a control.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReplyPromise = Promise with Integer
typealias ReplyPromiseArray = Array with ReplyPromise
typealias EchoHandleArray = Array with Echo.handle

let rounds = 300
let services = 4

type Echo
	var served as Integer

	static function create() returns Self
		return Self{served: 0}
	end 'create'

	export function value(v Integer) returns Integer
		self.served = self.served + 1
		return v
	end 'value'
end 'Echo'

function main() returns ExitCode
	var echoes = EchoHandleArray.create()
	for _ in 0 upto services 'spawnEach'
		echoes.push(spawn Echo.create())
	end 'spawnEach'

	var sum = 0 as Integer
	var completedPicks = 0 as Integer
	for r in 0 upto rounds 'eachRound'
		var replies = ReplyPromiseArray.create()
		for (slot, echo) in echoes.withIterator() 'sendEach'
			replies.push(try echo.value((r as Integer) * services + (slot.index() as Integer)) otherwise panic("send value"))
		end 'sendEach'

		let ready = replies.awaitAny()

		for (slot, p) in replies.withIterator() 'drain'
			if slot.index() == ready and p.isComplete() 'theNamedSlotHadCompleted'
				completedPicks = completedPicks + 1
			end 'theNamedSlotHadCompleted'

			sum = sum + (await p)
		end 'drain'
	end 'eachRound'

	print("picks={completedPicks} sum={sum}\n")
	return 0
end 'main'
```
```stdout
picks=300 sum=719400
```
```exitcode
0
```

<!-- test: await-any.sixteen-replies-completing-out-of-order-across-many-rounds -->
<!-- procs: 4 -->
⭐⭐ **A SELECT OVER SIXTEEN SLOTS LOCKS ALL SIXTEEN IN ONE ADDRESS ORDER, WHATEVER ORDER THE ARRAY HOLDS
THEM IN.** Each round sends one message to each of sixteen services and selects over the sixteen replies.
Every handler yields a different number of times before it answers, and the numbers rotate from round to
round, so the replies complete in a different scrambled order each round, and at four processors several
complete at once while the selector registers or withdraws. The reply records come from the green-thread
free lists, so after the first round the array's order need not be the records' address order. The registration
and the withdrawal each sort the sixteen records by address and lock them in that order, and every
completer holds its own record's lock alone, so no two parties can each hold a lock the other waits on. A
lock order taken from the array, or a sort that loses or repeats a slot past the first few, deadlocks the
program (the case times out), leaves a registration on a slot (`schedulerPromiseAwaitedTwice`), or loses a
reply, and the line comes out wrong.

✅ **IT IS A CONTROL: ANY SOUND MULTI-LOCK SELECT PASSES IT, AND IT HOLDS THIS ONE TO ITS ANSWERS.** The
four-slot cases above sort four records; this one sorts sixteen, past every small-array shortcut a sort
could take. The slot `awaitAny` names has completed every round, and every reply is awaited once, so the
answer is fixed: 400 completed picks and the sum of every value sent, `6400 * 6399 / 2`.
`sixteen-replies-completing-out-of-order-at-one-processor` is its twin at one processor.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReplyPromise = Promise with Integer
typealias ReplyPromiseArray = Array with ReplyPromise
typealias EchoHandleArray = Array with Echo.handle

let rounds = 400
let services = 16
let slotStride = 7
let roundStride = 5

type Echo
	var served as Integer

	static function create() returns Self
		return Self{served: 0}
	end 'create'

	export function value(v Integer, spins Integer) returns Integer
		for _ in 0 upto spins 'dawdle'
			Scheduler.yield()
		end 'dawdle'

		self.served = self.served + 1
		return v
	end 'value'
end 'Echo'

function main() returns ExitCode
	var echoes = EchoHandleArray.create()
	for _ in 0 upto services 'spawnEach'
		echoes.push(spawn Echo.create())
	end 'spawnEach'

	var sum = 0 as Integer
	var completedPicks = 0 as Integer
	for r in 0 upto rounds 'eachRound'
		var replies = ReplyPromiseArray.create()
		for (slot, echo) in echoes.withIterator() 'sendEach'
			let s = slot.index() as Integer
			let spins = (s * slotStride + (r as Integer) * roundStride) mod services
			replies.push(try echo.value((r as Integer) * services + s, spins: spins) otherwise panic("send value"))
		end 'sendEach'

		let ready = replies.awaitAny()

		for (slot, p) in replies.withIterator() 'drain'
			if slot.index() == ready and p.isComplete() 'theNamedSlotHadCompleted'
				completedPicks = completedPicks + 1
			end 'theNamedSlotHadCompleted'

			sum = sum + (await p)
		end 'drain'
	end 'eachRound'

	print("picks={completedPicks} sum={sum}\n")
	return 0
end 'main'
```
```stdout
picks=400 sum=20476800
```
```exitcode
0
```

<!-- test: await-any.sixteen-replies-completing-out-of-order-at-one-processor -->
<!-- procs: 1 -->
<!-- preempt: off -->
**THE ONE-PROCESSOR TWIN OF `sixteen-replies-completing-out-of-order-across-many-rounds`.** With one
processor and `preempt: off` the services run only once `main` has parked in `awaitAny`, and they take
turns, so the replies complete one at a time in the order their yield counts give: the round's scrambled
order, every completion after the registration. The sixteen-way registration and withdrawal, and the lock
order they share, must give the same answers as on the racing road. Like its twin, it is a control.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReplyPromise = Promise with Integer
typealias ReplyPromiseArray = Array with ReplyPromise
typealias EchoHandleArray = Array with Echo.handle

let rounds = 400
let services = 16
let slotStride = 7
let roundStride = 5

type Echo
	var served as Integer

	static function create() returns Self
		return Self{served: 0}
	end 'create'

	export function value(v Integer, spins Integer) returns Integer
		for _ in 0 upto spins 'dawdle'
			Scheduler.yield()
		end 'dawdle'

		self.served = self.served + 1
		return v
	end 'value'
end 'Echo'

function main() returns ExitCode
	var echoes = EchoHandleArray.create()
	for _ in 0 upto services 'spawnEach'
		echoes.push(spawn Echo.create())
	end 'spawnEach'

	var sum = 0 as Integer
	var completedPicks = 0 as Integer
	for r in 0 upto rounds 'eachRound'
		var replies = ReplyPromiseArray.create()
		for (slot, echo) in echoes.withIterator() 'sendEach'
			let s = slot.index() as Integer
			let spins = (s * slotStride + (r as Integer) * roundStride) mod services
			replies.push(try echo.value((r as Integer) * services + s, spins: spins) otherwise panic("send value"))
		end 'sendEach'

		let ready = replies.awaitAny()

		for (slot, p) in replies.withIterator() 'drain'
			if slot.index() == ready and p.isComplete() 'theNamedSlotHadCompleted'
				completedPicks = completedPicks + 1
			end 'theNamedSlotHadCompleted'

			sum = sum + (await p)
		end 'drain'
	end 'eachRound'

	print("picks={completedPicks} sum={sum}\n")
	return 0
end 'main'
```
```stdout
picks=400 sum=20476800
```
```exitcode
0
```

<!-- test: await-any.arity-checked -->
<!-- tier: stdlib -->
The intrinsic `__Builtins.awaitAny` takes exactly one argument — the array. An intrinsic has no signature
for the ordinary arity check to read, so it is refused by the same `builtinArity` check every other
`__Builtins` member uses. Front-end only and target-neutral, so no target marker.
```maxon
function main() returns ExitCode
	let ready = __Builtins.awaitAny()
	return ready as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:25: '__Builtins.awaitAny' takes exactly 1 argument, but 0 were given
```

<!-- test: await-any.error.operand-type -->
<!-- tier: stdlib -->
The argument must be an array whose element type is a `Promise with …`. An `Array with Integer` carries
plain numbers, and reading one as a green-thread handle would dereference an integer — so it is refused at
the call, where the element type is still known.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	let ready = __Builtins.awaitAny(arr)
	return ready as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:8:25: '__Builtins.awaitAny' requires a promise array — an `Array` whose element type is a `Promise with …`, but its argument is IntArray
```

<!-- test: await-any.error.rejected-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
A WASI component has no addressable call stack for a context switch to move, so it has no green-thread
scheduler and a program that selects over promises is refused at its own source span with `E3104` naming
`__gt_await_any` — never a panic from inside a backend.

⚠ **THE THUNK YIELDS THROUGH `Scheduler.parallelBoundary()` AND NOT `Scheduler.yield()`, WHICH IS THE
DIFFERENCE BETWEEN PINNING THIS RULE AND PINNING A NEIGHBOUR'S.** A legal `async` needs a callee that can
suspend (E3073), and `Scheduler.yield` lowers to `__gt_resched`, which is on the SAME target roster — so it
raises its own E3104 four lines earlier and the case would pass against a compiler that had never heard of
`awaitAny`. The CPU-parallel checkpoint satisfies E3073 and is deliberately NOT on that roster (its body is
a void return, which lowers everywhere), so the only refusal left is this one's.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function value(v Integer) returns Integer
	Scheduler.parallelBoundary()
	return v
end 'value'

function main() returns ExitCode
	var arr = IntPromiseArray.create()
	arr.push(async value(10))
	let ready = arr.awaitAny()
	return ready as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:14:18: this construct lowers to the runtime entry '__gt_await_any', which has no wasm32-wasi implementation
```
