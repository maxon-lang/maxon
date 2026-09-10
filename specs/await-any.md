---
feature: await-any
status: stable
keywords: [async, promise, awaitAny, select, __Builtins, intrinsics, scheduler]
category: concurrency
---

# `__Builtins.awaitAny` — ONE WAITING PRIMITIVE FOR REPLIES, FILE IO AND SUBPROCESS DRAINS

## Documentation

`await p` parks the caller until **one named** promise completes. A dispatcher holding N of them
cannot use it: awaiting slot 0 while slot 3 has already answered is head-of-line blocking, and the whole
point of running N children is that whichever finishes first is served first.

`__Builtins.awaitAny(promises)` is the way out. It takes an array whose element type is a
`Promise with …`, parks the caller until **some** element has completed, and returns that element's
**index**.

```maxon
let ready = __Builtins.awaitAny(drains)
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
| **drops the array with losers still in it** | **exit 75** — a reported green-thread leak |

✅ **THE SECOND ROW WAS A PRE-EXISTING DEFECT (`W217`), NOT SOMETHING `awaitAny` INTRODUCED, AND IT IS
FIXED.** An `Array with Promise` used to emit no `__gt_promise_drop` per element when it died:
`__gt_live_count` stayed up and the exit gate reported 75, on a compiler built from `main` with or
without this primitive. A container is now an OWNER of its promise elements and drops each un-awaited
one, so the case that pins the composition (`the-losers-are-dropped-when-the-array-dies`) runs — it was
`disabled-test` against `W217` until the container's element walk existed. See
`async-promise-drop.a-container-drops-its-un-awaited-elements` for the isolated statement of it.

The motivating consumer is unaffected, and that is the point of choosing a primitive that does not
consume: a worker pool selecting over drains **awaits every drain eventually**.

### A service REPLY is an ordinary promise, so it selects the same way

There is no separate "channel select" in this design. A handler reply is a `Promise`, so it goes into the
same array and this same primitive picks the first one to answer — `over-service-replies` is that case, and
it is why `SERVICES_DESIGN.md` chose a waiting primitive rather than a mailbox-specific one.

The storage must be `Promise with (T, ServiceError)`: a reply ALWAYS carries `ServiceError`, because the
service can be gone whatever the message declares. A message that itself THROWS has a two-member reply error
type no `throws` clause can name, and its reply may not be stored at all — both rules are pinned in
`specs/services.md`, whose `awaitany-returns-the-completed-index` and
`a-stored-reply-decodes-serviceerror-through-the-storage-road` carry them.

### A wait registers on EVERY slot, and the first completer readies it once

`__gt_await_any` scans the array first (`__gt_any_completed`) and returns at once if some element has
already completed, having parked on nothing. Otherwise it parks the caller like every other wait, and the
registration (`__gt_register_park`'s awaitAny arm) runs on the far side of the switch, under
`__sched_lock`: it scans again, readies the caller at once if a slot completed in between, and otherwise
stores the caller as the awaiter of every slot — tagged (`AwaitAnyTag`), with its `selectDone` word
cleared. That is Go's `selectgo`, one registration per case.

Whichever slot completes first takes the tagged awaiter in `__gt_runner_done`, under the same lock, sets
`selectDone` and readies the caller through `__gt_ready_locked`; a second slot completing before the caller
runs finds `selectDone` already set and readies nothing. On waking, the caller clears every slot's awaiter
word that still names it — under the lock again — and scans. So no slot keeps a registration once
`awaitAny` has returned, and a later plain `await` of any element finds the word clear.

⚠ **A SLOT HOLDS ONE AWAITER.** The registration refuses a slot whose awaiter word is already taken
(`schedulerPromiseAwaitedTwice`) rather than overwrite it, because the overwritten waiter would never be
woken. The front end cannot produce that shape.

### K registrations, and still ONE lock

Go orders channel locks by address so a K-way wait can hold them all. That is wrong here: both platform
locks are **recursive on the wrong identity** (a Win32 `CRITICAL_SECTION` is recursive per OS THREAD) while
green threads multiplex over one OS thread — so a green thread parking while holding one would let a
different green thread on the same M take the recursive path straight into the critical section. Not a
deadlock; silent FIFO corruption.

So the K registrations take no per-promise lock: every awaiter word is guarded by the ONE scheduler lock.
The registration, the completer's hand-over and the withdrawal each take it and release it with no switch
in between — the registration on a machine's scheduler context after the park's switch, the other two in
straight-line code — so no green thread ever holds it across a park.

### What the scan reads, and the one thing the caller owes it

A slot is skipped when it holds `0` — the zero a `resize` leaves in a slot nobody filled. Every other slot
is read as a green-thread handle and its status word compared against `completed`.

⚠ **A promise that has already been AWAITED leaves a stale handle in its slot**, because `await` recycles
the green-thread struct and nothing writes the slot back. That is the same contract
`__Builtins.gtIsComplete` has had since G17 — the intrinsic asks nothing of its handle beyond it being one
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

### `Runtime.awaitAny` — the nicer spelling, and why it is not here

The surface a program would rather write is `awaitAny(drains)`, an ordinary stdlib declaration whose body
is this intrinsic, exactly as `sleep(ms)` is `stdlib/Sleep.maxon`'s declaration over `__Builtins.sleep`.
It cannot be written yet: the declaration's parameter is `Array with Promise with T` for a type parameter
`T`, and the intrinsic's argument rule is *"the element type is a `Promise with …`"*, which no type
parameter can be proven to be. That is a stdlib-generics question and not this primitive's, and adding a
BARE-name `awaitAny` builtin instead would be the wart `print`/`sleep`/`runProcess` were moved into the
reserved `__Builtins.` space to remove.

### Targets — the lanes with a green-thread substrate, refused by name everywhere else

`__gt_await_any` is the green-thread scheduler. It is named on the `greenThreads` facility's roster beside
`__gt_sleep` and `__gt_resched`, so a program that calls it on a target whose row denies that facility reads
**E3104 at the call's own span**, naming the runtime entry — never a panic from inside a backend.
`error.rejected-on-wasm` pins that attribution. All four native lanes provide it; wasm32-wasi does not.

The two front-end cases (`arity-checked`, `error.operand-type`) reach no substrate at all and carry no
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
	Runtime.yield()
	return 2
end 'quick'

function main() returns ExitCode
	var arr = IntPromiseArray.create()
	arr.push(async slow(120))
	arr.push(async quick())
	arr.push(async slow(240))
	let ready = __Builtins.awaitAny(arr)
	var score = 0
	if ready == 1 'themiddleonefinishedfirst'
		score = score + 3
	end 'themiddleonefinishedfirst'
	var sum = 0
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
under `main`'s own `Runtime.yield()` loop before `awaitAny` is called, and it is the only one in the
program — so at the moment of the call nothing else is runnable, no timer is pending and no child is
parked. The scan at the top of `__gt_await_any` finds slot 0 `completed` and returns `0` without parking.

⚠ The exit code cannot tell that road from the one behind it: were the scan skipped, the registration's own
scan under `__sched_lock` would find the same slot and ready the caller at once. What this case pins is the
answer — the completed slot is named, and its promise is still awaitable afterwards.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function makeValue() returns Integer
	Runtime.yield()
	return 42
end 'makeValue'

function main() returns ExitCode
	let maxSpins = 100
	var arr = IntPromiseArray.create()
	arr.push(async makeValue())
	let p = try arr.get(0) otherwise panic("the promise was just pushed")
	var spins = 0
	var done = 0
	while spins < maxSpins and done == 0 'drive'
		Runtime.yield()
		done = __Builtins.gtIsComplete(p.inner)
		spins = spins + 1
	end 'drive'
	var score = 0
	if done == 1 'itwasalreadycompletebeforethecall'
		score = score + 1
	end 'itwasalreadycompletebeforethecall'
	if __Builtins.awaitAny(arr) == 0 'thecompletedslotisnamed'
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
	Runtime.yield()
	return v
end 'value'

function main() returns ExitCode
	var arr = IntPromiseArray.create()
	arr.push(async value(10))
	arr.push(async value(20))
	arr.push(async value(30))
	let first = __Builtins.awaitAny(arr)
	let second = __Builtins.awaitAny(arr)
	var score = 0
	if first == second 'aselectiswithoutsideeffect'
		score = score + 1
	end 'aselectiswithoutsideeffect'
	var sum = 0
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
	let ready = __Builtins.awaitAny(arr)
	var score = 0
	if ready == 1 'theearliestdeadlinewon'
		score = score + 3
	end 'theearliestdeadlinewon'
	var sum = 0
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

⚠ The storage names `ServiceError` because a reply always carries it (`services.md`'s
`error.a-reply-stored-without-its-error-type-is-refused`), and both replies are awaited afterwards because
`awaitAny` retires nothing.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReplyPromise = Promise with (Integer, ServiceError)
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
	ps.push(slow.value())
	ps.push(quick.value())
	let ready = __Builtins.awaitAny(ps)
	var score = 0
	if ready == 1 'thequickreplywonthoughitwassentsecond'
		score = score + 3
	end 'thequickreplywonthoughitwassentsecond'
	var sum = 0
	for p in ps 'drainbothreplies'
		sum = sum + (try await p otherwise 0)
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
	Runtime.yield()
	return v
end 'inner'

function selectOver() returns Integer
	var ps = IntPromiseArray.create()
	ps.push(async inner(3))
	ps.push(async inner(4))
	let ready = __Builtins.awaitAny(ps)
	var sum = 0
	for p in ps 'all'
		sum = sum + await p
	end 'all'
	return sum + ready
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
	Runtime.yield()
	return 42
end 'makeValue'

function main() returns ExitCode
	var arr = IntPromiseArray.create()
	arr.resize(2)
	arr.push(async makeValue())
	let ready = __Builtins.awaitAny(arr)
	let p = try arr.get(ready) otherwise panic("awaitAny named a slot that is in range")
	return (ready * 10 + await p) as ExitCode
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
	let ready = __Builtins.awaitAny(arr)
	return ready as ExitCode
end 'main'
```
```exitcode
92
```

<!-- test: await-any.the-losers-are-dropped-when-the-array-dies -->
⭐ The composition: select, serve the winner, and let the array die with the losers still in it. Exit 0
is what a container that drops its promise elements gives. This case was `disabled-test` until the
container owned its elements: the missing element walk on an `Array with Promise` (`W217`) reported
**exit 75** here, and on `main` with or without this primitive.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function value(v Integer) returns Integer
	Runtime.yield()
	return v
end 'value'

function main() returns ExitCode
	var arr = IntPromiseArray.create()
	arr.push(async value(1))
	arr.push(async value(2))
	let ready = __Builtins.awaitAny(arr)
	let winner = try arr.get(ready) otherwise panic("awaitAny named a slot that is in range")
	_ = await winner
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: await-any.arity-checked -->
`awaitAny` takes exactly one argument — the array. An intrinsic has no signature for the ordinary arity
check to read, so it is refused by the same `builtinArity` check every other `__Builtins` member uses.
Front-end only and target-neutral, so no marker.
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

⚠ **THE THUNK YIELDS THROUGH `__Builtins.parallelBoundary()` AND NOT `Runtime.yield()`, WHICH IS THE
DIFFERENCE BETWEEN PINNING THIS RULE AND PINNING A NEIGHBOUR'S.** A legal `async` needs a callee that can
suspend (E3073), and `Runtime.yield` lowers to `__gt_resched`, which is on the SAME target roster — so it
raises its own E3104 four lines earlier and the case would pass against a compiler that had never heard of
`awaitAny`. The CPU-parallel checkpoint satisfies E3073 and is deliberately NOT on that roster (its body is
a void return, which lowers everywhere), so the only refusal left is this one's.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function value(v Integer) returns Integer
	__Builtins.parallelBoundary()
	return v
end 'value'

function main() returns ExitCode
	var arr = IntPromiseArray.create()
	arr.push(async value(10))
	let ready = __Builtins.awaitAny(arr)
	return ready as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:14:25: this construct lowers to the runtime entry '__gt_await_any', which has no wasm32-wasi implementation
```
