---
feature: promise-peek
status: stable
keywords: [async, promise, isComplete, non-blocking, peek, poll]
category: concurrency
---

# The NON-BLOCKING PEEK — `promise.isComplete()`

## Documentation

`await` blocks: it parks the caller until one named promise completes. A dispatcher holding N
concurrent promises cannot afford that — awaiting slot 0 while slot 3 has already answered is
head-of-line blocking, and the whole point of running N children is that whichever finishes first is
served first. The peek is the way out:

| Member | Meaning |
|---|---|
| `p.isComplete() returns bool` | `true` once the green thread behind promise `p` has reached `completed`, `false` before |

It is a compiler-known member of every `Promise with T`. `maxon-bin/Testing/SpecWorkerPool.maxon`'s
`drainHasAnswered` is such a poller. A dispatcher that only needs the FIRST of N promises to finish uses
`promises.awaitAny()` instead, which parks until one has (`await-any.md`).

### The peek is a QUESTION, never a step

`isComplete` loads the green thread's status word and compares it. It does not park, does not advance the
thread and does not retire the promise — so peeking any number of times leaves the promise exactly as
awaitable as it was, and `await`'s linearity (E3100) counts none of them.

That is not a nicety. `SpecWorkerPool` asks the same predicate for two opposite reasons — `findReadyDrain`
SERVES on it and `abortOnWedgedPool` ACCUSES on it — and a peek with a side effect would make the two
disagree about a slot.

### ⛔ A promise's value IS its green-thread handle, so the peek must not dereference it

**The compiler does not box a promise** — a promise handle owns no `__mm_alloc` record, so a
`Promise with T` value IS the green-thread pointer. A lowering that loaded a word at some offset from the
promise value would read the green thread's own STACK (its saved stack pointer sits at offset 0). It would
never fault and never crash — it would answer a plausible value, which is the worst shape a defect can
have. The peek reads the status word of the thread the handle names, and the cases below pin the
`false -> true` TRANSITION, which no read of a stack word reproduces.

The handle itself (`Promise.inner`) is library-tier surface: a read of `.inner` outside a source file under
`stdlib/` or `runtime/` is `E3185`. The cases that pin the handle and the `__Builtins.gtIsComplete`
intrinsic under `isComplete` compile as library-tier sources (`<!-- tier: stdlib -->`).

### Targets — the peek itself is target-NEUTRAL; what gates these cases is the yield point

`__gt_is_complete` is a load, a compare and a return. It names no Win32 import, allocates nothing and
lowers on every backend, so it names no `HostFacility` in `TargetFacilities.calleeHostFacility` — giving it
one would refuse it on a lane that can serve it perfectly well.

What restricts the cases below is what restricts every async case in this suite: a legal `async` spawn
needs a callee that YIELDS, and a target without the yield primitives cannot have one. So the
E3104 a peeking program earns on such a target names `__gt_resched` — the thunk's yield — and never
the peek. `rejected-on-wasm` pins that attribution.

## Tests

<!-- test: promise-peek.completes-under-the-drive -->
⭐ **THE DISCRIMINATING CASE.** A freshly spawned thread has not run, so `isComplete()` answers `false`;
the program's yield loop lets it run to completion, so the same peek answers `true`; and the promise is
still awaitable afterwards. It is the `false -> true` TRANSITION that pins the mechanism rather than
either reading alone — a peek that read a word off the green thread's zero-filled stack would also answer
`false` first. Nothing but the real status word turns `true` at exactly the moment the thread finishes.
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
	let before = p.isComplete()
	var spins = 0 as Count
	var after = false
	while spins < maxSpins and not after 'drive'
		Scheduler.yield()
		after = p.isComplete()
		spins = spins + 1
	end 'drive'
	var score = 0 as Count
	if not before 'notCompleteOnArrival'
		score = score + 1
	end 'notCompleteOnArrival'
	if after 'completeAfterTheDrive'
		score = score + 2
	end 'completeAfterTheDrive'
	if await p == 42 'stillAwaitable'
		score = score + 4
	end 'stillAwaitable'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: promise-peek.does-not-consume-the-promise -->
Three peeks and the promise is untouched: `await` still hands over the value, `await`'s linearity
(E3100) counts none of them, and the run is leak-clean.
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
	arr.push(async makeValue())
	let p = try arr.get(0) otherwise panic("the promise was just pushed")
	let a = p.isComplete()
	let b = p.isComplete()
	let c = p.isComplete()
	var score = 0 as Count
	if a == b and b == c 'threePeeksAgree'
		score = score + 3
	end 'threePeeksAgree'
	if await p == 42 'theResultSurvived'
		score = score + 4
	end 'theResultSurvived'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: promise-peek.a-peek-through-a-function-leaves-the-promise-alone -->
⭐⭐ **THE PEEK BEHIND A FUNCTION, WHICH IS THE SHAPE A DISPATCHER WRITES.** Every case above peeks inline
in `main`; `SpecWorkerPool.drainHasAnswered` takes the promise as a BY-VALUE PARAMETER and peeks it there.

⛔⛔ **A PROMISE PARAMETER IS NOT OWNED BY THE CALLEE BY TYPE, OR EVERY SUCH PEEK WOULD CANCEL THE
CALLER'S THREAD.** The callee's scope exit would run `__gt_promise_drop`, the reclaim would take the green
thread back while the caller went on naming it, and the promise would never complete — so `before` and
`after` would both read `false` and the poll would spin to its bound. A callee OWNS a promise exactly when it CONSUMES it, which is what
`the-handle-survives-a-field-chain` below pins from the other side.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function makeValue() returns Integer
	Scheduler.yield()
	return 42
end 'makeValue'

function peek(p IntPromise) returns bool
	return p.isComplete()
end 'peek'

function main() returns ExitCode
	let maxSpins = 100
	let p = async makeValue()
	let before = peek(p)
	var spins = 0 as Count
	var after = false
	while spins < maxSpins and not after 'drive'
		Scheduler.yield()
		after = peek(p)
		spins = spins + 1
	end 'drive'
	var score = 0 as Count
	if not before 'notCompleteOnArrival'
		score = score + 1
	end 'notCompleteOnArrival'
	if after 'completeAfterTheDrive'
		score = score + 2
	end 'completeAfterTheDrive'
	if await p == 42 'stillAwaitable'
		score = score + 4
	end 'stillAwaitable'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: promise-peek.two-promises-name-two-threads -->
<!-- tier: stdlib -->
The handle under a promise (`.inner`, library-tier) distinguishes green threads: two spawns are two
handles, and neither is null. This is the property a dispatcher holding N slots leans on — it peeks each slot in turn, and a handle that named
the same thread for every slot would serve the first answer to every job.

⚠ **IT IS A COMPANION AND NOT THE DISCRIMINATOR.** Two green
threads have two stacks, so a dereferencing read would answer two distinct non-null numbers as well — this
case cannot tell the handle from `gt->sp`. It is kept because the property is real and nothing else pins
it (a `for` over the array still awaits both, so it is also the leak gate on that shape). What separates
the two readings is `completes-under-the-drive`.
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
	arr.push(async makeValue())
	arr.push(async makeValue())
	let first = try arr.get(0) otherwise panic("both promises were just pushed")
	let second = try arr.get(1) otherwise panic("both promises were just pushed")
	var score = 0 as Count
	if first.inner != 0 and second.inner != 0 'bothAreRealHandles'
		score = score + 1
	end 'bothAreRealHandles'
	if first.inner != second.inner 'twoDistinctThreads'
		score = score + 2
	end 'twoDistinctThreads'
	var sum = 0 as Integer
	for p in arr 'each'
		sum = sum + await p
	end 'each'
	if sum == 84 'bothRan'
		score = score + 4
	end 'bothRan'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: promise-peek.the-handle-survives-a-field-chain -->
⭐ **A SECOND DOOR, PINNED ON ITS OWN.** `h.p.isComplete()` is a member call at the end of a TWO-HOP chain,
and the parser resolves a chain through its own walk rather than through the single-hop member dispatch —
so "the peek works on `p`" is no evidence at all about `h.p`. The peek taken THROUGH THE CHAIN must go
`false -> true` across the drive, which only a real status word does.

⚠ Passing a promise MOVES it — a green thread has one owner — so after `Holder.of(p)` the thread belongs
to the struct and the await is `await h.p`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

type Holder
	public var p as IntPromise

	static function of(p IntPromise) returns Holder
		return Holder{p: p}
	end 'of'
end 'Holder'

function makeValue() returns Integer
	Scheduler.yield()
	return 42
end 'makeValue'

function main() returns ExitCode
	let maxSpins = 100
	var arr = IntPromiseArray.create()
	arr.push(async makeValue())
	let p = try arr.get(0) otherwise panic("the promise was just pushed")
	view h = Holder.of(p)
	let before = h.p.isComplete()
	var spins = 0 as Count
	var after = false
	while spins < maxSpins and not after 'drive'
		Scheduler.yield()
		after = h.p.isComplete()
		spins = spins + 1
	end 'drive'
	var score = 0 as Count
	if not before 'notcompleteonarrival'
		score = score + 1
	end 'notcompleteonarrival'
	if after 'thechainreadsarealstatus'
		score = score + 2
	end 'thechainreadsarealstatus'
	if await h.p == 42 'stillawaitable'
		score = score + 4
	end 'stillawaitable'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: promise-peek.the-chain-names-the-thread-the-promise-named -->
<!-- tier: stdlib -->
The handle read through the two-hop chain is the handle the promise carried before it moved into the
`Holder`: `h.p.inner` resolves through the chain walk, `p.inner` through the single-hop member dispatch,
and the two must name one green thread. The single-hop handle is read BEFORE `Holder.of(p)`, which moves
the promise; `.inner` is not a consume, so the await is `await h.p`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

type Holder
	public var p as IntPromise

	static function of(p IntPromise) returns Holder
		return Holder{p: p}
	end 'of'
end 'Holder'

function makeValue() returns Integer
	Scheduler.yield()
	return 42
end 'makeValue'

function main() returns ExitCode
	let p = async makeValue()
	let handle = p.inner
	let h = Holder.of(p)
	var score = 0 as Count
	if h.p.inner == handle 'theTwoDoorsAgree'
		score = score + 1
	end 'theTwoDoorsAgree'
	if handle != 0 'aRealHandle'
		score = score + 2
	end 'aRealHandle'
	if await h.p == 42 'stillAwaitable'
		score = score + 4
	end 'stillAwaitable'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: promise-peek.arity-checked -->
<!-- tier: stdlib -->
The intrinsic under `isComplete`, `__Builtins.gtIsComplete`, takes exactly one argument — the handle. An
intrinsic has no signature for the ordinary arity check to read, so it is refused by the same
`builtinArity` check every other `__Builtins` member uses.
```maxon
function main() returns ExitCode
	let done = __Builtins.gtIsComplete()
	return done as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:24: '__Builtins.gtIsComplete' takes exactly 1 argument, but 0 were given
```

<!-- test: promise-peek.error.operand-type -->
<!-- tier: stdlib -->
The handle is a machine word. A `String` is refused at the call, with the same `builtinOperandType`
sentence the subprocess handle entries use — this is one intrinsic reading one raw pointer, not a
member lookup on a promise, so the operand rule is the shared one and not a bespoke test.
```maxon
function main() returns ExitCode
	let done = __Builtins.gtIsComplete("x")
	return done as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:24: '__Builtins.gtIsComplete' requires a int, but its argument is String
```

<!-- test: promise-peek.rejected-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
⭐ **THE REFUSAL NAMES THE YIELD POINT, NOT THE PEEK.** `__gt_is_complete` is a load and a compare and
lowers on every backend, so it is not in the Win32-substrate set; what a peeking program cannot have on
another target is a THUNK, because the only yield primitive is `__gt_resched`. This case
exists to pin that attribution — an E3104 quoting `__gt_is_complete` here would mean the peek had been
gated by reflex rather than by need.
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
	arr.push(async makeValue())
	let p = try arr.get(0) otherwise panic("the promise was just pushed")
	let done = 1 if p.isComplete() else 0
	return (await p + done) as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:7:12: 'Scheduler.yield' lowers to the runtime entry '__gt_resched', which has no wasm32-wasi implementation
```
