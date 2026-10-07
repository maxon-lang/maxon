---
feature: promise-typing
status: stable
keywords: [async, await, promise, green-threads, type, ownership, E3005, E2004, E2015]
category: concurrency
---

# Promises are TYPED at birth

## Documentation

`async f()` mints a promise. That promise is **typed** — `Promise with T`, or `Promise with (T, E)` when
`f` throws — from the moment it is minted, exactly as `stdlib/Builtins.maxon` declares it: an *opaque
handle*, typed by both the value its thunk returns and the error its thunk throws.

The handle IS a machine word, but that is a representation, not a type, and a value carrying its
representation as its type would be accepted everywhere that word is accepted: `return async work()`
would satisfy a `returns Integer` and print a raw green-thread pointer; `p + 1` would be pointer
arithmetic; `p.clone()` would hand out a second copy of a handle exactly one owner may reclaim; and an
`Integer` parameter would take a promise.

Typing the promise at birth is what refuses all of them, and it does so through the checks that already
exist rather than a new roster: a promise is not an `Integer`, so every position that wants an `Integer`
already knows how to say no. **The roster of refusals is DERIVED, not enumerated** — which is the point.
There is no list to keep in step with the language.

⚠ **THESE REFUSALS ARE PINNED TO `x64-windows`, AND THE REASON IS THE THUNK RATHER THAN THE RULE.** The
rules themselves are target-neutral — nothing about "a promise is not an `Integer`" depends on a backend.
But an `async` thunk must have a yield point or **E3073** refuses the spawn outright (*"function never
yields"*), and the yield points available lower to runtime entries `wasm32-wasi` does not
have: `File.exists` reaches `__mf_exists` and raises **E3104** there. Either way the case's own subject is
MASKED by an error about the thunk. Dropping the marker to win the lane simply trades E3104 for E3073,
so the case is pinned instead of quietly testing something else.

⚠ **ON THE SPELLINGS IN THESE DIAGNOSTICS.** Two are the compiler's existing renderings rather than
anything this rule chose, and both are worth knowing. A refusal taken at the TAG arm prints the tag word
(`struct`), because at that point the check has compared classes and not names. And an instance no
`typealias` spells renders by its type ARGUMENT's range — `Promise with int(…)` and not `Promise with
Integer` — which is what `rangeRenderedInstanceName` does for every alias-less instance, on the ground that
the alias is exactly the thing that was ambiguous. Declare `typealias IntPromise = Promise with Integer` and
the diagnostics say `IntPromise`.

The one sanctioned promise → `int` conversion is **`p.inner`** (see `promise-peek.md`): a non-blocking
peek at the handle word, which reads the promise without consuming it.

## Tests

<!-- test: promise-typing.error.return-a-promise-as-its-result-type -->
A promise is not its result. `grab` is declared `returns Integer` and returns `async plain()`, which is a
`Promise with Integer` — the value that will eventually produce an `Integer`, not an `Integer`. Before
promises were typed this compiled and printed the green thread's raw address (a different number on every
run), which is the wrong answer this whole slice exists to stop.
```maxon
typealias Integer = int(i64.min to i64.max)

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function grab() returns Integer
		return async plain()
end 'grab'

function main() returns ExitCode
		print("grabbed {grab()}")
		return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:3: Cannot return 'struct' from function declared to return 'Integer'
```

<!-- test: promise-typing.error.arithmetic-on-a-promise -->
A promise is not a number, so it has no arithmetic.
```maxon
typealias Integer = int(i64.min to i64.max)

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function main() returns ExitCode
		let p = async plain()
		let bumped = p + 1
		print("bumped {bumped}")
		return (await p) as ExitCode
end 'main'
```
```maxoncstderr
error E2004: <fragment>:11:18: Cannot operate on struct and int
```

<!-- test: promise-typing.error.a-promise-in-an-integer-parameter -->
<!-- unsupported-targets: wasm32-wasi -->
An `Integer` parameter does not take a promise. The cure is to `await` it and pass the RESULT — which is
also the only spelling that keeps the thread's one owner intact.
```maxon
typealias Integer = int(i64.min to i64.max)

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function takesInt(n Integer) returns Integer
		return n
end 'takesInt'

function main() returns ExitCode
		let p = async plain()
		return takesInt(p) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:15:10: argument type mismatch for 'n': expected 'Integer', got 'Promise with int(-9223372036854775808 to 9223372036854775807)'
```

<!-- test: promise-typing.error.clone-a-promise -->
⭐ The one that would be a latent double-reclaim rather than merely a wrong type. A promise owns a green
thread that exactly one owner may reclaim; a `p.clone()` would hand back a second copy of the handle,
with nothing to say which of the two owned the thread. `Promise` declares no `clone`, and synthesizing
one is refused at the receiver.
```maxon
typealias Integer = int(i64.min to i64.max)

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function main() returns ExitCode
		let p = async plain()
		let copy = p.clone()
		print("copied {copy.inner > 0}")
		return (await p) as ExitCode
end 'main'
```
```maxoncstderr
error E3141: <fragment>:11:16: a promise cannot be borrowed through 'clone': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. `Promise with int(-9223372036854775808 to 9223372036854775807)` is or holds a promise, and a promise owns a green thread that exactly one owner may reclaim — so a copy would give two of them one thread. `await` the promise and copy a value holding its RESULT
```

<!-- test: promise-typing.error.clone-a-map-of-promises -->
The same refusal one container deep: a `Map` whose values are promises would hand each thread to two maps.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias Pending = Map with (Integer, IntPromise)

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function main() returns ExitCode
		var pending = Pending.create()
		pending.upsert(1, value: async plain())
		let copy = pending.clone()
		return copy.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3141: <fragment>:14:22: a promise cannot be borrowed through 'clone': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. `Pending` is or holds a promise, and a promise owns a green thread that exactly one owner may reclaim — so a copy would give two of them one thread. `await` the promise and copy a value holding its RESULT
```

<!-- test: promise-typing.error.clone-a-record-holding-an-array-of-promises -->
And through a record whose field is an array of promises.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

type Batch
	export let pending as IntPromiseArray

	static function create(pending IntPromiseArray) returns Self
		return Self{pending: pending}
	end 'create'
end 'Batch'

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function main() returns ExitCode
		var pending = IntPromiseArray.create()
		pending.push(async plain())
		let batch = Batch.create(pending)
		let copy = batch.clone()
		return copy.pending.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3141: <fragment>:23:20: a promise cannot be borrowed through 'clone': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. `Batch` is or holds a promise, and a promise owns a green thread that exactly one owner may reclaim — so a copy would give two of them one thread. `await` the promise and copy a value holding its RESULT
```

<!-- test: promise-typing.error.append-an-array-of-promises -->
`append` copies every element of its argument, so an array of promises appended to another would leave each
thread with two arrays to reclaim it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function main() returns ExitCode
		var first = IntPromiseArray.create()
		var second = IntPromiseArray.create()
		second.push(async plain())
		first.append(second)
		return first.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3141: <fragment>:15:9: a promise cannot be borrowed through 'append': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. an element of this array is or holds a promise, and a promise owns a green thread that exactly one owner may reclaim — so a copy would give two of them one thread. `await` the promise and copy a value holding its RESULT
```

<!-- test: promise-typing.error.clone-an-array-of-records-holding-a-promise -->
And through an array whose element is a record holding a promise: the array's copy reaches the promise
only through the element type.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias JobArray = Array with Job

type Job
	export var pending as IntPromise

	static function create(pending IntPromise) returns Self
		return Self{pending: pending}
	end 'create'
end 'Job'

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function main() returns ExitCode
		var jobs = JobArray.create()
		jobs.push(Job.create(async plain()))
		let copy = jobs.clone()
		return copy.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3141: <fragment>:22:19: a promise cannot be borrowed through 'clone': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. `JobArray` is or holds a promise, and a promise owns a green thread that exactly one owner may reclaim — so a copy would give two of them one thread. `await` the promise and copy a value holding its RESULT
```

<!-- test: promise-typing.inner-is-the-one-unwrap -->
The sanctioned promise → `int` conversion. `.inner` peeks at the handle word without consuming the
promise, so the `await` that follows still reclaims the thread and the program still balances to zero.
```maxon
typealias Integer = int(i64.min to i64.max)

function plain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
end 'plain'

function main() returns ExitCode
		let p = async plain()
		let named = p.inner > 0
		print("names a thread {named}")
		return (await p) as ExitCode
end 'main'
```
```stdout
names a thread true
```
```exitcode
7
```

<!-- test: promise-typing.a-promise-array-element-may-be-spelled-inline -->
⛔⛔ **`Array with Promise with (T, E)` AND THE TWO-STEP ALIAS ARE ONE TYPE.**
`typealias Ps = Promise with (Integer, ServiceError)` followed by `Array with Ps` and the element written
inline must mean the same thing; an inline element sized through the primitive helper raises

```
panic at LayoutDescriptor.maxon:563: primitiveTypeByteSize: a `genericInstance` is an aggregate —
size it through its base StructLayout.sizeBytes, not this helper
  in ProgramSignatures.trivialElementSlot → arrayElementSize → Parser.emitArrayCreateOp
```

⇒ **one meaning, two answers — and a panic is never one of the two.** The case asserts the ANSWER rather
than the absence of a crash: the array is built, a reply promise is pushed into it, popped and awaited, and
the value arrives.

⭐ **"IS IT AN `__mm` BOX?" IS NOT "IS THIS ELEMENT STORED AS A POINTER?".** A promise is a pointer into
the scheduler's own slab that `__gt_promise_drop` reclaims and no refcount owns, so `typeIsManaged`
answers **false** for a promise by design (*"an `Array with Promise` answers `false` there and `true`
here"*, in `containerElementOwesDrop`'s words). Sizing the element by `containerElementIsManaged` would
send it to the INLINE-STORAGE path, where `trivialElementSlot` would size an aggregate through the
primitive helper; the two-step spelling would escape only because its element is the alias NAME, which
the undeclared-`named` fallback sizes at a machine word. `arrayElementSize` asks
`containerElementOccupiesAPointerSlot`, which is the question actually being asked, and both populations
answer it.

⚠ **THE TWO SPELLINGS AGREE TO THE BYTE.** A drop-the-promises
program — an `Array with Promise` filled and abandoned — emits the same bytes and
exits 0 under both spellings, so the inline form gets the same `element_size@24` stride and the same
`element_destroy@40` stamp rather than merely stopping short of the panic.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromiseArray = Array with Promise with (Integer, ServiceError)

type Calc
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function double(by Integer) returns Integer
		return by * 2
	end 'double'
end 'Calc'

function main() returns ExitCode
	let h = spawn Calc.create()
	var ps = IntPromiseArray.create()
	ps.push(h.double(21))

	let p = try ps.pop() otherwise panic("the push above filled it")
	return (try await p otherwise 0) as ExitCode
end 'main'
```
```exitcode
42
```
