---
feature: promise-peek
status: stable
keywords: [async, promise, inner, gtIsComplete, non-blocking, __Builtins, intrinsics]
category: concurrency
---

# The NON-BLOCKING PEEK — `promise.inner` and `__Builtins.gtIsComplete`

## Documentation

`await` blocks: it drives the scheduler until one named promise completes. A dispatcher holding N
concurrent promises cannot afford that — awaiting slot 0 while slot 3 has already answered is
head-of-line blocking, and the whole point of running N children is that whichever finishes first is
served first. The peek is the way out, and it is two halves that only work together:

| Half | What it is |
|---|---|
| `promise.inner` | the promise's own green-thread handle, read out of the `Promise with T` value |
| `__Builtins.gtIsComplete(gt)` | `1` if that green thread has reached `completed`, `0` otherwise |

`stdlib/Builtins.maxon:110-125` declares `Promise`'s single field and says exactly why it is public:
*"`inner` is exported (read-only via `let`) so non-blocking pollers like the spec test dispatcher can
peek at the underlying GT's status."* `maxon-shv2/Testing/SpecWorkerPool.maxon`'s `drainHasAnswered`
is that poller, and it is the only caller in the tree.

### The peek is a QUESTION, never a step

`__Builtins.gtIsComplete` loads the status word and compares it. It does not drive the scheduler, does
not advance the thread and does not retire the promise — so peeking any number of times leaves the
promise exactly as awaitable as it was, and `await`'s linearity (E3100) counts none of them.

That is not a nicety. `SpecWorkerPool` asks the same predicate for two opposite reasons — `findReadyDrain`
SERVES on it and `abortOnWedgedPool` ACCUSES on it — and a peek with a side effect would make the two
disagree about a slot, which is the exact failure `drainHasAnswered` exists as one function to prevent.

### ⛔ `promise.inner` IS NOT A FIELD LOAD IN shv2, AND READING IT AS ONE WAS A SILENT WRONG ANSWER

The bootstrap BOXES a promise: `BoxPromiseIntoStruct` allocates a real `Promise` record at the storage
site, so `inner` there is a genuine field of a genuine box. **shv2 does not box** — `PromiseType.maxon`
carries the argument in full: a promise handle owns no `__mm_alloc` record, so a `Promise with T` value
IS the green-thread pointer.

Those two facts do not compose. A promise's value being the handle means the ordinary field-read
lowering — *load the word at the field's offset* — DEREFERENCES the handle, and `inner` is at offset 0,
where the green thread keeps its saved stack pointer. MEASURED before the cure, on
`function peek(p IntPromise) returns Integer { return p.inner }`:

```text
func @peek {
  entry:
    x64.loadRegBaseDisp.word64 r8, [rcx + 0]     // gt->sp, NOT the handle
}
```

⇒ every peek asked about a word 16 bytes into the green thread's own STACK. It never faulted (the stack
is committed and readable) and it never crashed — it simply answered a plausible number, which is the
worst shape a defect can have. Reading `inner` is now the identity: a fresh value, typed as the field
declares, carrying the handle itself — the same answer `emitFieldLoad` already gives a fused wrapper's
inline `managed`, for the same reason (the record IS the field).

⚠ `PromiseType.maxon` used to state the opposite as settled — *"NOTHING OBSERVABLE DEPENDS ON THE BOX …
a reader of it gets the same number either way"*. The premise was right (the unboxed value IS the raw
GT pointer the stdlib doc-comment promises) and the conclusion did not follow, because nothing had told
the field-read door.

### Targets — the peek itself is target-NEUTRAL; what gates these cases is the yield point

`__gt_is_complete` is a load, a compare and a return. It names no Win32 import, allocates nothing and
lowers on every backend, so it is deliberately **not** in `SemanticCheck.calleeNeedsWin32Substrate` —
unlike `__proc_pid` or `__gt_resched`, adding it there would be a refusal on arm64 for a construct that
target can serve perfectly well the moment its scheduler lands.

What restricts the cases below is what restricts every async case in this suite: a legal `async` spawn
needs a callee that YIELDS, and the only yield primitives are x64-windows-only at this rung. So the
E3104 a peeking program earns on another target names `__gt_resched` — the thunk's yield — and never
the peek. `rejected-on-wasm` pins that attribution rather than assuming it, and the two front-end cases
(`arity-checked`, `error.operand-type`) reach no substrate at all and carry no marker.

## Tests

<!-- test: promise-peek.completes-under-the-drive -->
<!-- targets: x64-windows, arm64-macos, arm64-linux, x64-linux -->
⭐ **THE DISCRIMINATING CASE.** A freshly spawned thread has not run, so the peek is `0`; driving the
scheduler runs it to completion, so the same peek becomes `1`; and the promise is still awaitable
afterwards. It is the `0 -> 1` TRANSITION that pins the mechanism rather than either reading alone —
a peek that dereferenced the handle read a word off the green thread's own stack, which on a fresh
`VirtualAlloc`'d (zero-filled) stack ALSO answers `0`, so the not-yet-complete half is satisfied by the
wrong answer too. Nothing but the real status word turns into `1` at exactly the moment the thread
finishes.
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
	let before = __Builtins.gtIsComplete(p.inner)
	var spins = 0
	var after = 0
	while spins < maxSpins and after == 0 'drive'
		Runtime.yield()
		after = __Builtins.gtIsComplete(p.inner)
		spins = spins + 1
	end 'drive'
	var score = 0
	if before == 0 'notCompleteOnArrival'
		score = score + 1
	end 'notCompleteOnArrival'
	if after == 1 'completeAfterTheDrive'
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
<!-- targets: x64-windows, arm64-macos, arm64-linux, x64-linux -->
Three peeks and the promise is untouched: `await` still hands over the value, `await`'s linearity
(E3100) counts none of them, and the run is leak-clean. A peek that retired the promise would take the
whole harness with it — `findReadyDrain` serves on this predicate and `abortOnWedgedPool` reports on
it, and they must be asking about the same thread.
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
	arr.push(async makeValue())
	let p = try arr.get(0) otherwise panic("the promise was just pushed")
	let a = __Builtins.gtIsComplete(p.inner)
	let b = __Builtins.gtIsComplete(p.inner)
	let c = __Builtins.gtIsComplete(p.inner)
	var score = 0
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

<!-- test: promise-peek.two-promises-name-two-threads -->
<!-- targets: x64-windows, arm64-macos, arm64-linux, x64-linux -->
`inner` distinguishes green threads: two spawns are two handles, and neither is null. This is the
property a dispatcher holding N slots leans on — it peeks each slot in turn, and a handle that named
the same thread for every slot would serve the first answer to every job.

⚠ **IT IS A COMPANION AND NOT THE DISCRIMINATOR, and it was measured green BEFORE the cure.** Two green
threads have two stacks, so the dereferencing read answered two distinct non-null numbers as well — this
case cannot tell the handle from `gt->sp`. It is kept because the property is real and nothing else pins
it (a `for` over the array still awaits both, so it is also the leak gate on that shape). What separates
the two readings is `completes-under-the-drive`.
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
	arr.push(async makeValue())
	arr.push(async makeValue())
	let first = try arr.get(0) otherwise panic("both promises were just pushed")
	let second = try arr.get(1) otherwise panic("both promises were just pushed")
	var score = 0
	if first.inner != 0 and second.inner != 0 'bothAreRealHandles'
		score = score + 1
	end 'bothAreRealHandles'
	if first.inner != second.inner 'twoDistinctThreads'
		score = score + 2
	end 'twoDistinctThreads'
	var sum = 0
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
<!-- targets: x64-windows, arm64-macos, arm64-linux, x64-linux -->
⭐ **A SECOND DOOR, PINNED SEPARATELY AND ANCHORED ON ITS OWN.** `h.p.inner` is a TWO-HOP chain, and the
parser resolves a chain through its own walk rather than through the single-hop member dispatch — so
"the promise arm fires for `p.inner`" is no evidence at all about `h.p.inner`. Agreement between the two
readings is necessary and NOT sufficient (a door that kept the load would agree with another that kept
it), so this case carries `completes-under-the-drive`'s own anchor as well: the peek taken THROUGH THE
CHAIN must go `0 -> 1` across the drive, which only a real status word does.

⚠ **THE SINGLE-HOP HANDLE IS TAKEN BEFORE THE PROMISE IS HANDED TO THE `Holder`, AND THAT ORDER IS THE
OWNERSHIP RULE SHOWING THROUGH** (`W217`). Passing a promise MOVES it — a green thread has one owner — so
after `Holder.of(p)` the thread belongs to the struct and `p` names nothing this frame may consume; the
await is therefore `await h.p`. `.inner` is not a consume, so reading the handle a line EARLIER is legal
and is what the two doors are compared against. Written the other way (`h.p.inner == p.inner`, `await p`)
this case pinned a promise with two owners, and it only passed because nothing yet objected.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

type Holder
	public let p as IntPromise

	static function of(p IntPromise) returns Holder
		return Holder{p: p}
	end 'of'
end 'Holder'

function makeValue() returns Integer
	Runtime.yield()
	return 42
end 'makeValue'

function main() returns ExitCode
	let maxSpins = 100
	var arr = IntPromiseArray.create()
	arr.push(async makeValue())
	let p = try arr.get(0) otherwise panic("the promise was just pushed")
	let handle = p.inner
	let h = Holder.of(p)
	let before = __Builtins.gtIsComplete(h.p.inner)
	var spins = 0
	var after = 0
	while spins < maxSpins and after == 0 'drive'
		Runtime.yield()
		after = __Builtins.gtIsComplete(h.p.inner)
		spins = spins + 1
	end 'drive'
	var score = 0
	if h.p.inner == handle 'thetwodoorsagree'
		score = score + 1
	end 'thetwodoorsagree'
	if before == 0 and after == 1 'thechainreadsarealstatus'
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

<!-- test: promise-peek.arity-checked -->
`gtIsComplete` takes exactly one argument — the handle. An intrinsic has no signature for the ordinary
arity check to read, so it is refused by the same `builtinArity` check every other `__Builtins` member
uses. Front-end only and target-neutral, so no marker.
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
<!-- targets: wasm32-wasi -->
⭐ **THE REFUSAL NAMES THE YIELD POINT, NOT THE PEEK.** `__gt_is_complete` is a load and a compare and
lowers on every backend, so it is not in the Win32-substrate set; what a peeking program cannot have on
another target is a THUNK, because the only yield primitive at this rung is `__gt_resched`. This case
exists to pin that attribution — an E3104 quoting `__gt_is_complete` here would mean the peek had been
gated by reflex rather than by need.
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
	arr.push(async makeValue())
	let p = try arr.get(0) otherwise panic("the promise was just pushed")
	let done = __Builtins.gtIsComplete(p.inner)
	return (await p + done) as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:7:10: this construct is x64-windows only at this rung: 'Runtime.yield' lowers to the runtime entry '__gt_resched', which has no wasm32-wasi implementation
```
