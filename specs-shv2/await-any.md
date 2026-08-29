---
feature: await-any
status: stable
keywords: [async, promise, awaitAny, select, __Builtins, intrinsics, scheduler]
category: concurrency
---

# `__Builtins.awaitAny` — ONE WAITING PRIMITIVE FOR REPLIES, FILE IO AND SUBPROCESS DRAINS

## Documentation

`await p` drives the scheduler until **one named** promise completes. A dispatcher holding N of them
cannot use it: awaiting slot 0 while slot 3 has already answered is head-of-line blocking, and the whole
point of running N children is that whichever finishes first is served first.

`__Builtins.awaitAny(promises)` is the way out. It takes an array whose element type is a
`Promise with …`, drives the scheduler until **some** element has completed, and returns that element's
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

⛔ **THE SECOND ROW IS A PRE-EXISTING DEFECT (`W217`), NOT SOMETHING `awaitAny` INTRODUCES.** An
`Array with Promise` emits no `__gt_promise_drop` per element when it dies: `__gt_live_count` stays up and
the exit gate reports 75. That is true on a compiler built from `main` with or without this primitive —
`var s = IntPromiseArray.create()` plus one `s.push(async f())` and nothing else is already exit 75. The
case that pins the composition (`the-losers-are-dropped-when-the-array-dies`) is therefore `disabled-test`
against `W217`, and a reader who meets that 75 has met the container's missing element walk rather than a
bug in this primitive.

The motivating consumer is unaffected, and that is the point of choosing a primitive that does not
consume: a worker pool selecting over drains **awaits every drain eventually**.

### A service REPLY is an ordinary promise, so it selects the same way

There is no separate "channel select" in this design. A handler reply is a `Promise`, so it goes into the
same array and this same primitive picks the first one to answer — `over-service-replies` is that case, and
it is why `SERVICES_DESIGN.md` chose a waiting primitive rather than a mailbox-specific one.

The storage must be `Promise with (T, ServiceError)`: a reply ALWAYS carries `ServiceError`, because the
service can be gone whatever the message declares. A message that itself THROWS has a two-member reply error
type no `throws` clause can name, and its reply may not be stored at all — both rules are pinned in
`specs-shv2/services.md`, whose `awaitany-returns-the-completed-index` and
`a-stored-reply-decodes-serviceerror-through-the-storage-road` carry them.

### The exit test is at the TOP of the drive loop, so an already-complete promise never parks

`__gt_await_any` is the **shared** cooperative drive loop — the same body `await` and the exit teardown run
— with a third exit predicate. The loop tests its exit condition BEFORE it drives anything, so an array
that already holds a completed promise returns at once, having switched into nothing.
`no-park-when-one-is-already-complete` is the case that pins it: with the single promise in that array
already complete and **nothing else runnable anywhere**, a compiler that tested only after driving would
find no work, no timer and no child, and abort as a scheduler deadlock.

Everything else in that loop is shared and must stay shared: the coroutine drain, the
`__sched_find_runnable` fallback (this P's ring, the global queue, four rounds of stealing), the netpoll's
timer and child waits, the `awaitOtherM` poll, and the deadlock abort. `over-a-mixed-array-of-sleeps` is
the case that reaches the netpoll — three sleepers, no busy loop, and the **earliest deadline** wins.

### No K-way registration, and above all NO K LOCKS

`awaitAny` registers on nothing. The exit test SCANS the array's status words from the driver, which needs
no lock at all and leaves nothing for `__gt_promise_drop` to deregister.

Go orders channel locks by address so a K-way wait can hold them all. That is wrong here: both platform
locks are **recursive on the wrong identity** (a Win32 `CRITICAL_SECTION` is recursive per OS THREAD) while
green threads multiplex over one OS thread — so a green thread parking while holding one lets a different
green thread on the same M take the recursive path straight into the critical section. Not a deadlock;
silent FIFO corruption.

### What the scan reads, and the one thing the caller owes it

A slot is skipped when it holds `0` — the zero a `resize` leaves in a slot nobody filled. Every other slot
is read as a green-thread handle and its status word compared against `completed`.

⚠ **A promise that has already been AWAITED leaves a stale handle in its slot**, because `await` recycles
the green-thread struct and nothing writes the slot back. That is the same contract
`__Builtins.gtIsComplete` has had since G17 — the intrinsic asks nothing of its handle beyond it being one
— and the same slot-level linearity gap named above. A caller that re-selects over an array must
overwrite a consumed slot, exactly as `Testing/SpecWorkerPool.sendAndDrain` re-arms a drain with `set`.

### An EMPTY array is a scheduler deadlock, and that is Go's answer too

Awaiting any of zero promises can never complete. The scan finds nothing, the drive loop finds nothing
runnable, no timer and no child, and the shared body's `nothingLeft` arm aborts — the same abort a plain
`await` of a thread nobody can run reaches, **exit 92**. Go's `select {}` reaches its own deadlock detector
for exactly this reason. `an-empty-array-is-a-scheduler-deadlock` pins it.

⚠ That case is also the only shape in which `awaitAny` can be a program's **first** scheduler call — an
array with a promise in it has already spawned one — so it is what makes the lazy `__gt_init` at the top of
the drive loop reachable. Without it the same program **segfaults** on a TLS slot nobody allocated.

### `Runtime.awaitAny` — the nicer spelling, and why it is not here

The surface a program would rather write is `awaitAny(drains)`, an ordinary stdlib declaration whose body
is this intrinsic, exactly as `sleep(ms)` is `stdlib/Sleep.maxon`'s declaration over `__Builtins.sleep`.
It cannot be written yet: the declaration's parameter is `Array with Promise with T` for a type parameter
`T`, and the intrinsic's argument rule is *"the element type is a `Promise with …`"*, which no type
parameter can be proven to be. That is a stdlib-generics question and not this primitive's, and adding a
BARE-name `awaitAny` builtin instead would be the wart `print`/`sleep`/`runProcess` were moved into the
reserved `__Builtins.` space to remove.

### Targets — x64-windows only at this rung, refused by name everywhere else

`__gt_await_any` is the green-thread scheduler, whose substrate is Win32-only at this rung. It is named in
`SemanticCheck.calleeNeedsWin32Substrate` beside `__gt_sleep` and `__gt_resched`, so a program that calls
it on another target reads **E3104 at the call's own span**, naming the runtime entry — never a panic from
inside a backend. `error.rejected-on-wasm` pins that attribution.

The two front-end cases (`arity-checked`, `error.operand-type`) reach no substrate at all and carry no
marker.

## Tests

<!-- test: await-any.returns-the-first-completed-index -->
<!-- targets: x64-windows -->
⭐ **THE DISCRIMINATING CASE — the index returned is the one that FINISHED, not the one that is first in
the array.** Slot 0 and slot 2 park on timers; slot 1 only yields, so it is the one that reaches
`completed`. A scan that looked at slot 0 alone, or that returned the first slot it could read rather than
the first slot that had completed, answers `0` here.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function slow(ms Integer) returns Integer
	sleep(ms)
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
<!-- targets: x64-windows -->
⭐ **THE EXIT TEST IS AT THE TOP OF THE LOOP, AND THIS IS WHAT SAYS SO.** The promise is driven to
completion by `Runtime.yield()` before `awaitAny` is called, and it is the ONLY green thread in the
program — so at the moment of the call there is nothing runnable, no timer pending and no child parked.
A drive loop that tested its exit condition only after driving would find all three empty and abort as a
scheduler deadlock; this returns `0` without switching into anything.
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
<!-- targets: x64-windows -->
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
<!-- targets: x64-windows -->
⭐ **THE NETPOLL CASE — nothing is runnable at all, so the shared body BLOCKS on the earliest timer.**
Three sleepers and no other work: the drive loop finds no coroutine, nothing on the ring, nothing to
steal, no parked child — and sleeps on the nearest deadline rather than spinning. The index that comes
back is the shortest sleeper, which is deadline order and not array order.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function nap(ms Integer, tag Integer) returns Integer
	sleep(ms)
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
<!-- targets: x64-windows -->
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
	var n as int

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function value() returns Integer
		sleep(80)
		return 1
	end 'value'
end 'Slow'

type Quick
	var n as int

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

<!-- test: await-any.a-slot-nobody-filled-is-skipped -->
<!-- targets: x64-windows -->
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
<!-- targets: x64-windows -->
⭐ **THE ONE SHAPE IN WHICH `awaitAny` IS A PROGRAM'S FIRST SCHEDULER CALL**, because an array with a
promise in it has already spawned one. Two things are pinned at once: the drive loop's lazy `__gt_init`
runs (without it this program reads `currentP` through a TLS slot nobody allocated and **segfaults**), and
an empty select is answered by the shared body's deadlock abort — exit **92**, promptly, rather than a
hang or an index naming a promise that never completed.
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

<!-- disabled-test: await-any.the-losers-are-dropped-when-the-array-dies -->
<!-- W217 -->
⭐ The composition: select, serve the winner, and let the array die with the losers still in it. Exit 0 is
what a container that dropped its promise elements would give. Today it is **exit 75** — `W217`, the
missing element walk on an `Array with Promise`, which is present on `main` with or without this
primitive.
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
<!-- targets: wasm32-wasi -->
The green-thread scheduler is x64-windows only at this rung, so a program that selects over promises is
refused at its own source span with `E3104` naming `__gt_await_any` — never a panic from inside a backend.

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
error E3104: <fragment>:14:25: this construct is x64-windows only at this rung: it lowers to the runtime entry '__gt_await_any', which has no wasm32-wasi implementation
```
