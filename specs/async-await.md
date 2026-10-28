---
feature: async-await
status: experimental
keywords: [async, await, green threads, concurrency, promise]
category: concurrency
---

# Async/Await

## Documentation

Maxon supports cooperative concurrency via `async` and `await`. ⚖ **An `async` call does NOT create a new green thread** (user ruling): it starts the callee as a **coroutine of the green thread that called it**, with a growable stack (starting at 2KB). The coroutine runs until it reaches a blocking operation, at which point it yields the green thread and that green thread's other coroutines run; `await` resumes the caller and collects the result. So "parallel work" below is overlapped **waiting**, not parallel execution, and a coroutine never leaves the green thread that made it — it changes OS thread only when that green thread does. Creating a separately scheduled green thread is `spawn`, which starts a SERVICE (`specs/services.md`).

```text
// Start a coroutine of this green thread
var promise = async someFunction(arg1, arg2)

// Wait for the result
var result = await promise

// Parallel work
var p1 = async foo(1)
var p2 = async bar(2)
var r1 = await p1
var r2 = await p2
```

**Key properties:**
- One owner — a coroutine belongs to the green thread that created it, runs only where that green
  thread's strand runs, one member at a time, and is never itself put on a run queue. A coroutine spawned
  by a coroutine belongs to the same green thread, so every frame `async` creates, at any depth, has one owner.
- Coroutines switch only where they wait — a coroutine keeps the green thread until it reaches an
  `await`, a `sleep` or an I/O point. What overlaps is the WAITING: N outstanding reads are in flight
  at once while their coroutines are parked. The green thread around them is preempted at 10 ms
  (`specs/sched-preempt.md`).
- Growable stacks — 2KB initial, doubles when needed
- Reference counting is PLAIN, not atomic — because one green thread owns every box its coroutines
  touch, a retain or release has no second party to race. That is a language guarantee rather than a
  thread count: it does not depend on `MAXON_MAX_PROCS`. State genuinely shared between OS threads —
  the runtime's own counters and queues — is a different question and is protected as such.
- Fire-and-forget safe — unawaited coroutines are drained at program exit

**Restrictions:**
- `async` can only be used on direct function calls (not closures or indirect calls)
- `async` can only be used on functions that yield (contain I/O operations or await points).
  **`E3073` is not a side condition — it is the model stated as a rule.** `async` buys overlapped
  waiting, so spawning something that can never give up the green thread buys nothing at all: it would
  simply run to completion at the spawn point. A CPU-bound function that legitimately never waits says
  so with `Scheduler.parallelBoundary()` (see `builtins-parallel-boundary.md`).
- Throwing async functions require `try await` to extract the result
- `promise.cancel()` cancels the associated green thread
- **`await` is LINEAR**: a promise is awaited exactly once, and a second await is a compile error (E3100)

**Arguments are shared.**
An `async` call's arguments are shared with the coroutine exactly as an ordinary call shares them: the caller
and its coroutines are one strand of one green thread, so the coroutine takes a plain count of its own on each
managed argument and the caller's binding stays usable. A `var` handed to `async` is read and written after
the call as before, a `let` is shared read-only, and one binding may be handed twice in one call. A coroutine
that writes its parameter is refused a read-only argument (E3159), as an ordinary call is. A parameter the
callee reassigns gets storage the coroutine owns; nothing is written back to the caller. A closure that
captured a binding owns it, so handing that binding to `async` afterwards is a use after move (E3102).

**Await is linear.**
The thunk owns its result and hands it over at the `await`. A second await of the same promise
would take a second reference to a payload the thunk only ever owned once, and the two releases
would free it twice — so the compiler refuses it rather than the runtime surviving it. This is not
about errors: a non-throwing `async` returning a `String` double-frees identically. The check is
flow-sensitive, so two awaits in mutually exclusive branches are fine (each is the only await on
its own path), while a single await sitting in a loop over a promise spawned *outside* the loop is
not (it awaits the same green thread every iteration).

Linearity is a property of the GREEN THREAD, not of the name. `let q = p` gives one thread two
names, and awaiting through both is the same double free — E3100 catches it. Conversely, assigning
a promise binding RE-ARMS it: it names a new thread, so awaiting it again is legal, which is
exactly what makes `for p in promises 'each' … await p … end` one await per promise.

**Known boundary.** This check sees awaits of BINDINGS within one function's control flow. A promise
that escapes that is tracked by a different mechanism — the SLOT it was read out of. Reading one out
of a container element or a struct field starts a move, consuming the read empties that slot, and
handing a promise to a callee that consumes it moves it there, so a second consume is refused at
compile time: **E3141** for a read whose slot another read already spent, **E3102** for a name whose
promise was moved into storage or into a callee.

What no compile-time rule reaches is two reads the compiler cannot PROVE name one slot — a run-time
index, two cursors over one array, a field read twice inline. There the second consume finds the slot
already empty and aborts with exit code **118** (`async-promise-drop.a-field-awaited-twice-aborts`), so
the double free is reported rather than performed.

**Typed promises:**
A promise is typed by BOTH what its thunk returns and what its thunk throws, because both come
back across the same `await`:

| type | meaning | await |
|---|---|---|
| `Promise with T` | the thunk does NOT throw | `await p` |
| `Promise with (T, E)` | the thunk `throws E` | `try await p otherwise (e)` — `e` is an `E` |

Declaring such a type lets promises be stored in collections: the compiler boxes the i64 handle
into a `Promise` struct at the storage site and unboxes it at the `await` site. This lets you spawn
N green threads with a `for` loop, collect the promises into an array, and await them in a second
pass.

Naming `E` is load-bearing, not decorative. A promise stored in a type that names only its result
has had its error type *erased*, and every downstream question about the error then has no answer:
an `otherwise (e)` binding has no type to give `e`, an associated-value payload has no static type
to release (so it leaks), and `try await` has nothing to check the enclosing function's `throws`
against (so one enum's ordinals get reinterpreted as another's tags). Storing a throwing promise in
a `Promise with T` is therefore refused — E3098 — and the diagnostic names the two-parameter type
that works.

```text
typealias IntPromise = Promise with Integer                  // work() does not throw
typealias IntPromiseArray = Array with IntPromise

var arr = IntPromiseArray.create()
arr.push(async work(1))
arr.push(async work(2))
for p in arr 'each'
    let result = await p   // unboxed automatically
end 'each'

typealias FetchPromise = Promise with (Integer, FetchError)  // fetch() throws FetchError
typealias FetchPromiseArray = Array with FetchPromise

var fetches = FetchPromiseArray.create()
fetches.push(async fetch(1))
for p in fetches 'each'
    let result = try await p otherwise (e) 'failed'          // e is a FetchError
        return match e 'why' ... end 'why'
    end 'failed'
end 'each'
```

## Tests

<!-- test: async-await.basic -->
```maxon
typealias Integer = int(i64.min to i64.max)

function compute() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 42
end 'compute'

function main() returns ExitCode
		let promise = async compute()
		let result = await promise
		return result
end 'main'
```
```exitcode
42
```

<!-- test: async-await.parallel -->
```maxon
typealias Integer = int(i64.min to i64.max)

function taskA() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 10
end 'taskA'

function taskB() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 20
end 'taskB'

function main() returns ExitCode
		let p1 = async taskA()
		let p2 = async taskB()
		let r1 = await p1
		let r2 = await p2
		return r1 + r2
end 'main'
```
```exitcode
30
```

<!-- test: async-await.void -->
```maxon
var flag = 0 as ExitCode

function setFlag()
		_ = File.exists(FilePath from "noyield.txt")
		flag = 1
end 'setFlag'

function main() returns ExitCode
		let p = async setFlag()
		await p
		return flag
end 'main'
```
```exitcode
1
```

<!-- test: async-await.sequence -->
```maxon
typealias Integer = int(i64.min to i64.max)

function step(n Integer) returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return n + 1
end 'step'

function main() returns ExitCode
		let p1 = async step(0)
		let r1 = await p1
		let p2 = async step(r1)
		let r2 = await p2
		let p3 = async step(r2)
		let r3 = await p3
		return r3
end 'main'
```
```exitcode
3
```

<!-- test: async-await.stack-growth -->
```maxon
typealias Integer = int(i64.min to i64.max)

function deepRecurse(n Integer) returns Integer
		if n == 0 'base'
				return 0
		end 'base'
		return deepRecurse(n - 1) + 1
end 'deepRecurse'

function yieldAndRecurse() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return deepRecurse(100)
end 'yieldAndRecurse'

function main() returns ExitCode
		let p = async yieldAndRecurse()
		let result = await p
		return result
end 'main'
```
```exitcode
100
```

<!-- test: async-await.try-await.otherwise-default -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum TestError implements Error
		failed
end 'TestError'

function mayFail(succeed bool) returns Integer throws TestError
		_ = File.exists(FilePath from "noyield.txt")
		if succeed 'ok'
				return 42
		end 'ok'
		throw TestError.failed
end 'mayFail'

function main() returns ExitCode
		let p1 = async mayFail(true)
		let r1 = try await p1 otherwise 0
		let p2 = async mayFail(false)
		let r2 = try await p2 otherwise 80
		return r1 + r2
end 'main'
```
```exitcode
122
```

<!-- test: async-await.try-await.propagate -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum TestError implements Error
		failed
end 'TestError'

function mayFail(succeed bool) returns Integer throws TestError
		_ = File.exists(FilePath from "noyield.txt")
		if succeed 'ok'
				return 10
		end 'ok'
		throw TestError.failed
end 'mayFail'

function inner() returns Integer throws TestError
		let p = async mayFail(true)
		let result = try await p
		return result
end 'inner'

function main() returns ExitCode
		let r = try inner() otherwise 0
		return r
end 'main'
```
```exitcode
10
```

<!-- test: async-await.try-await.otherwise-panic -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum TestError implements Error
		failed
end 'TestError'

function succeeds() returns Integer throws TestError
		if File.exists(FilePath from "noyield.txt") 'present'
				throw TestError.failed
		end 'present'

		return 7
end 'succeeds'

function main() returns ExitCode
		let p = async succeeds()
		let r = try await p otherwise panic("should not fail")
		return r
end 'main'
```
```exitcode
7
```

<!-- test: async-await.try-await.void -->
```maxon
var flag = 0 as ExitCode

enum TestError implements Error
		failed
end 'TestError'

function maySetFlag(succeed bool) throws TestError
		_ = File.exists(FilePath from "noyield.txt")
		if succeed 'ok'
				flag = 1
				return
		end 'ok'
		throw TestError.failed
end 'maySetFlag'

function main() returns ExitCode
		let p = async maySetFlag(true)
		try await p otherwise ignore
		return flag
end 'main'
```
```exitcode
1
```

<!-- test: async-await.try-await.otherwise-bind -->
`otherwise (e)` on a `try await` binds the error the awaited thunk THREW,
at the thunk's declared `throws` type — exactly as it does on a `try` call.
The promise carries its callee's error type, so `e` here is a `TaskError`
and can be matched.
```maxon
typealias Integer = int(i64.min to i64.max)

enum TaskError implements Error
		timedOut
		crashed
end 'TaskError'

function mayFail(mode Integer) returns Integer throws TaskError
		_ = File.exists(FilePath from "noyield.txt")
		if mode == 0 'ok'
				return 1
		end 'ok'
		if mode == 1 'slow'
				throw TaskError.timedOut
		end 'slow'
		throw TaskError.crashed
end 'mayFail'

function main() returns ExitCode
		let p = async mayFail(2)
		let r = try await p otherwise (e) 'failed'
				return match e 'why'
						timedOut gives 7
						crashed gives 9
				end 'why'
		end 'failed'
		return r
end 'main'
```
```exitcode
9
```

<!-- test: async-await.try-await.otherwise-bind-assoc-value -->
The bound error is an associated-value union, so the error flag IS a heap
pointer to the payload. The binding takes ownership and scope-end releases
it exactly once — a leak here (the payload never decref'd) or a double-free
would both surface under the runtime's allocation accounting. Before the
promise carried its error type, the `otherwise` path emitted no release at
all on a DIRECT await and the payload leaked.
```maxon
typealias Integer = int(i64.min to i64.max)

union WorkError implements Error
		failed(reason String)
		refused(code Integer)
end 'WorkError'

function work(n Integer) returns Integer throws WorkError
		_ = File.exists(FilePath from "noyield.txt")
		if n < 0 'neg'
				throw WorkError.failed("negative input")
		end 'neg'
		return n
end 'work'

function main() returns ExitCode
		let p = async work(-1)
		let r = try await p otherwise (e) 'failed'
				match e 'which'
						failed(reason) then print("caught: {reason}\n")
						refused(code) then print("refused {code}\n")
				end 'which'
				return 0
		end 'failed'
		return r
end 'main'
```
```exitcode
0
```
```stdout
caught: negative input
```

<!-- test: async-await.try-await.otherwise-bind-cross-block -->
The `async` and its `try await` sit in different basic blocks, so the promise
reaches the await through the cross-block variable-reference path. That path
re-tags the promise value and must carry ALL of its metadata across — it used
to rebuild the promise without its error type, re-erasing it for the common
shape where a spawn is followed by any intervening control flow.
```maxon
typealias Integer = int(i64.min to i64.max)

union WorkError implements Error
		failed(reason String)
end 'WorkError'

function work(n Integer) returns Integer throws WorkError
		_ = File.exists(FilePath from "noyield.txt")
		if n < 0 'neg'
				throw WorkError.failed("cross-block")
		end 'neg'
		return n
end 'work'

function main() returns ExitCode
		let p = async work(-1)
		var guard = 1 as Integer
		if guard == 1 'sep'
				guard = 2
		end 'sep'
		let r = try await p otherwise (e) 'failed'
				match e 'which'
						failed(reason) then print("caught: {reason}\n")
				end 'which'
				return 0
		end 'failed'
		return r
end 'main'
```
```exitcode
0
```
```stdout
caught: cross-block
```

<!-- test: async-await.try-await.otherwise-bind-void -->
A void-returning throwing async function has no result — only an error flag.
The `otherwise (e)` binding must still be typed at the thunk's `throws` type.
```maxon
typealias Code = int(0 to 100)

var flag = 0 as Code

enum TaskError implements Error
		timedOut
		crashed

		function code() returns Code
				return match self 'e'
						timedOut gives 7
						crashed gives 9
				end 'e'
		end 'code'
end 'TaskError'

function maySetFlag(succeed bool) throws TaskError
		_ = File.exists(FilePath from "noyield.txt")
		if succeed 'ok'
				flag = 1
				return
		end 'ok'
		throw TaskError.crashed
end 'maySetFlag'

function main() returns ExitCode
		let p = async maySetFlag(false)
		try await p otherwise (e) 'failed'
				flag = e.code()
		end 'failed'
		return flag
end 'main'
```
```exitcode
9
```

<!-- test: async-await.error.non-promise -->
```maxon
function main() returns ExitCode
		let x = 42
		let result = await x
		return result
end 'main'
```
```maxoncstderr
error E3005: specs/async-await/async-await.error.non-promise.maxon:4:16: 'await' requires a promise value from 'async', got integer
```

<!-- test: async-await.error.no-yield -->
```maxon
typealias Integer = int(i64.min to i64.max)

function heavyCompute(n Integer) returns Integer
		return n * n
end 'heavyCompute'

function main() returns ExitCode
		let p = async heavyCompute(5)
		let result = await p
		return result
end 'main'
```
```maxoncstderr
error E3073: specs/async-await/async-await.error.no-yield.maxon:9:11: 'async heavyCompute(5)' — function never yields; 'async' is for I/O-concurrent work only
```

<!-- test: async-await.storage.bind-error-through-storage -->
The error type SURVIVES storage, so an `otherwise (e)` binding on a promise pulled back out of
an array binds the error the thunk actually throws — a `TaskError`, matched case by case. The
storage type `Promise with (T, E)` names the error type, so `e` is typed exactly as for a direct await.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias TaskPromise = Promise with (Integer, TaskError)
typealias TaskPromiseArray = Array with TaskPromise

enum TaskError implements Error
		timedOut
		crashed
end 'TaskError'

function mayFail() returns Integer throws TaskError
		_ = File.exists(FilePath from "noyield.txt")
		throw TaskError.crashed
end 'mayFail'

function main() returns ExitCode
		var arr = TaskPromiseArray.create()
		arr.push(async mayFail())
		let p = try arr.get(0) otherwise panic("index 0 is in bounds by construction")
		let r = try await p otherwise (e) 'failed'
				return match e 'why'
						timedOut gives 7
						crashed gives 9
				end 'why'
		end 'failed'
		return r
end 'main'
```
```exitcode
9
```

<!-- test: async-await.storage.release-union-payload-through-storage -->
The error carried back out of storage is an associated-value union, so its flag IS a heap pointer
to the payload. The binding takes ownership and scope-end releases it exactly once. Only a worker that DIES
runs this path, so nothing else in the suite shows a leak here: a flag reading "not a heap pointer" (a
bit that cannot distinguish one error type from another) would skip the conditional decref, and every
dead worker would leak its error.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias WorkPromise = Promise with (Integer, WorkError)
typealias WorkPromiseArray = Array with WorkPromise

union WorkError implements Error
		failed(reason String)
		refused(code Integer)
end 'WorkError'

function work(n Integer) returns Integer throws WorkError
		_ = File.exists(FilePath from "noyield.txt")
		if n < 0 'neg'
				throw WorkError.failed("negative input")
		end 'neg'
		return n
end 'work'

function main() returns ExitCode
		var arr = WorkPromiseArray.create()
		arr.push(async work(-1))
		let p = try arr.get(0) otherwise panic("index 0 is in bounds by construction")
		let r = try await p otherwise (e) 'failed'
				match e 'which'
						failed(reason) then print("caught: {reason}\n")
						refused(code) then print("refused {code}\n")
				end 'which'
				return 0
		end 'failed'
		return r
end 'main'
```
```exitcode
0
```
```stdout
caught: negative input
```

<!-- test: async-await.error.storage-erases-error-type -->
Storing a THROWING promise in a `Promise with T` is refused: that type names the result and
nothing else, so boxing into it would erase the thunk's error type — which is what made the `(e)`
binding untypeable, the payload unreleasable, and the propagation check uncheckable. The
diagnostic names the two-parameter type that keeps it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias BadPromise = Promise with Integer
typealias BadPromiseArray = Array with BadPromise

enum TaskError implements Error
		crashed
end 'TaskError'

function mayFail() returns Integer throws TaskError
		_ = File.exists(FilePath from "noyield.txt")
		throw TaskError.crashed
end 'mayFail'

function main() returns ExitCode
		var arr = BadPromiseArray.create()
		arr.push(async mayFail())
		return 0
end 'main'
```
```maxoncstderr
error E3098: specs/async-await/async-await.error.storage-erases-error-type.maxon:17:7: a promise of 'Integer' throwing 'TaskError' cannot be stored in 'BadPromise', which holds 'Integer'
```

<!-- test: async-await.error.storage-names-wrong-error-type -->
The storage type must name the error the thunk ACTUALLY throws. A `Promise with (T, E)` whose E
disagrees with the callee's `throws` would hand the await site one enum's ordinals under another
enum's name — the same reinterpretation the erased form allowed, just spelled out loud.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias WrongPromise = Promise with (Integer, OtherError)
typealias WrongPromiseArray = Array with WrongPromise

enum TaskError implements Error
		crashed
end 'TaskError'

enum OtherError implements Error
		different
end 'OtherError'

function mayFail() returns Integer throws TaskError
		_ = File.exists(FilePath from "noyield.txt")
		throw TaskError.crashed
end 'mayFail'

function main() returns ExitCode
		var arr = WrongPromiseArray.create()
		arr.push(async mayFail())
		return 0
end 'main'
```
```maxoncstderr
error E3098: specs/async-await/async-await.error.storage-names-wrong-error-type.maxon:21:7: a promise of 'Integer' throwing 'TaskError' cannot be stored in 'WrongPromise', which holds 'Integer' throwing 'OtherError'
```

<!-- test: async-await.a-promise-parameter-can-be-stored-in-an-array -->
A promise a function was handed is the function's to store: its declared type states what it carries, so a
container whose type agrees takes it, exactly as it takes a spawn made in the function. The array then owns
the thread, and the loop awaits it out of the array.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function park(p IntPromise) returns Integer
	var pending = IntPromiseArray.create()
	pending.push(p)

	var sum = 0 as Integer

	for q in pending 'drain'
		sum = sum + await q
	end 'drain'

	return sum
end 'park'

function main() returns ExitCode
	let total = park(async work())
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-promise-parameter-can-be-stored-in-a-field -->
A promise parameter is stored in a field by a struct literal, by `self.p = next` in a method and by `h.p = next`
in a function. Each store releases the thread the field was holding, so the two promises that were replaced are
dropped and only the last is awaited.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

type Holder
	public var p as IntPromise

	static function of(p IntPromise) returns Self
		return Self{p: p}
	end 'of'

	function rearm(next IntPromise)
		self.p = next
	end 'rearm'
end 'Holder'

function swap(h Holder, next IntPromise)
	h.p = next
end 'swap'

function main() returns ExitCode
	var holder = Holder.of(async work())
	holder.rearm(async work())
	swap(holder, next: async work())
	print("{await holder.p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.error.a-promise-parameter-is-stored-only-under-a-type-that-agrees-with-its-own -->
A promise parameter is checked against its DECLARED type, the same two comparisons a spawn is checked
against its callee: the result type, then the error type. A `Promise with String` parameter stored in an
array of `Promise with Integer` would hand `await` a string under an integer's name.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias StrPromise = Promise with String
typealias IntPromiseArray = Array with IntPromise

function park(p StrPromise) returns Integer
	var pending = IntPromiseArray.create()
	pending.push(p)
	return 0
end 'park'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3098: specs/async-await/async-await.error.a-promise-parameter-is-stored-only-under-a-type-that-agrees-with-its-own.maxon:9:10: a promise of 'String' cannot be stored in 'IntPromise', which holds 'Integer'
```

<!-- test: async-await.error.a-throwing-promise-parameter-cannot-be-stored-under-a-type-that-omits-its-error -->
A parameter typed `Promise with (T, E)` throws `E`, so storing it in a `Promise with T` would erase the
error type exactly as storing a throwing spawn does.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias ThrowingPromise = Promise with (Integer, TaskError)
typealias IntPromiseArray = Array with IntPromise

enum TaskError implements Error
	crashed
end 'TaskError'

function park(p ThrowingPromise) returns Integer
	var pending = IntPromiseArray.create()
	pending.push(p)
	return 0
end 'park'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3098: specs/async-await/async-await.error.a-throwing-promise-parameter-cannot-be-stored-under-a-type-that-omits-its-error.maxon:13:10: a promise of 'Integer' throwing 'TaskError' cannot be stored in 'IntPromise', which holds 'Integer'
```

<!-- test: async-await.a-promise-a-helper-returned-can-be-stored-in-an-array -->
A promise the storing function owns is storable whatever made it, so a helper's return goes into an array like a spawn made on the spot. The array then owns the thread, and the loop awaits it out of the array.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function ask() returns IntPromise
	return async work()
end 'ask'

function main() returns ExitCode
	var pending = IntPromiseArray.create()
	pending.push(ask())
	pending.push(ask())

	var sum = 0 as Integer

	for p in pending 'drain'
		sum = sum + await p
	end 'drain'

	print("{sum}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
14
```

<!-- test: async-await.a-promise-a-helper-returned-can-be-stored-in-a-field -->
A promise field takes a helper's return as it takes a spawn, and the store releases the thread the field held.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function ask() returns IntPromise
	return async work()
end 'ask'

type Cell
	public var p as IntPromise

	static function of(p IntPromise) returns Self
		return Self{p: p}
	end 'of'
end 'Cell'

function main() returns ExitCode
	var cell = Cell.of(async work())
	cell.p = ask()
	print("{await cell.p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-promise-a-helper-returned-can-be-stored-in-an-array-held-by-a-field -->
A container held in a field is the same storage.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function ask() returns IntPromise
	return async work()
end 'ask'

type Holder
	public var pending as IntPromiseArray

	static function create() returns Self
		return Self{pending: IntPromiseArray.create()}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create()
	holder.pending.push(ask())

	var sum = 0 as Integer

	for p in holder.pending 'drain'
		sum = sum + await p
	end 'drain'

	print("{sum}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-promise-a-helper-returned-can-be-stored-in-a-map -->
A map keeps the promises it is given and drops the ones nobody took out. A map has no slot a read could name, so the promise stays in it and the run is leak-free.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias PromiseMap = Map with (String, IntPromise)

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function ask() returns IntPromise
	return async work()
end 'ask'

function main() returns ExitCode
	var m = PromiseMap.create()
	m.upsert("a", value: ask())
	m.upsert("b", value: async work())
	print("{m.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: async-await.error.a-promise-stored-in-a-map-cannot-be-read-back-out -->
A map has no slot a read could name, so reading a promise value out of it would leave two owners and is refused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias PromiseMap = Map with (String, IntPromise)

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function main() returns ExitCode
	var m = PromiseMap.create()
	m.upsert("a", value: async work())
	let p = try m.get("a") otherwise panic("a is present")
	print("{await p}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3141: <fragment>:14:16: a promise cannot be borrowed through 'get': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. Read it through a door that names its slot (`get(i)`, `first()`, `for … in`, or an array's cursor), or move it out with `pop`/`remove`
```

<!-- test: async-await.error.a-stored-parameter-awaited-afterwards-is-use-after-move -->
Storing a parameter moves it into the container, so the function's own name for it is spent.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function park(p IntPromise) returns Integer
	var pending = IntPromiseArray.create()
	pending.push(p)
	return await p
end 'park'

function main() returns ExitCode
	print("{park(async work())}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:14:15: use of moved value 'p': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: async-await.error.a-parameter-stored-twice-is-use-after-move -->
One thread has one owner, so the second store of the same parameter is refused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function park(p IntPromise) returns Integer
	var pending = IntPromiseArray.create()
	pending.push(p)
	pending.push(p)
	return pending.count() as Integer
end 'park'

function main() returns ExitCode
	print("{park(async work())}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:14:15: use of moved value 'p': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: async-await.a-parameter-stored-on-one-branch-only-runs-clean -->
A parameter stored on one branch only is owned on the other, which drops it at the function's end: both runs leave no thread behind.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function park(p IntPromise, keep bool) returns Integer
	var pending = IntPromiseArray.create()

	if keep 'store'
		pending.push(p)
	end 'store'

	return pending.count() as Integer
end 'park'

function main() returns ExitCode
	print("{park(async work(), keep: true)} {park(async work(), keep: false)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 0
```

<!-- test: async-await.error.a-promise-used-after-it-was-pushed-by-a-callee-is-use-after-move -->
The caller handed the promise to a function that stores it, so the caller's name for it is spent.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function park(pending IntPromiseArray, p IntPromise)
	pending.push(p)
end 'park'

function main() returns ExitCode
	var pending = IntPromiseArray.create()
	let p = async work()
	park(pending, p: p)
	print("{await p}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:19:16: use of moved value 'p': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: async-await.error.a-promise-used-after-it-was-stored-in-a-field-by-a-callee-is-use-after-move -->
The same holds for a callee that stores the promise in a field.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

type Cell
	public var p as IntPromise

	static function of(p IntPromise) returns Self
		return Self{p: p}
	end 'of'
end 'Cell'

function main() returns ExitCode
	let p = async work()
	let cell = Cell.of(p)
	print("{await p}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:21:16: use of moved value 'p': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: async-await.a-popped-promise-can-be-stored-in-a-field -->
A promise moved out of an array by `pop` is the storing function's own, so a field takes it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

type Cell
	public var p as IntPromise

	static function of(p IntPromise) returns Self
		return Self{p: p}
	end 'of'
end 'Cell'

function main() returns ExitCode
	var source = IntPromiseArray.create()
	source.push(async work())

	let popped = try source.pop() otherwise panic("one element was pushed")
	var cell = Cell.of(async work())
	cell.p = popped
	print("{await cell.p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-matched-promise-payload-can-be-wrapped-in-a-union-again -->
A promise bound by a `match` arm is moved out of the union it was in, so it can be wrapped in another.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

union Slot
	held(p IntPromise)
	empty
end 'Slot'

function main() returns ExitCode
	let first = Slot.held(async work())

	match first 'unwrap'
		held(p) then print("{rewrap(p)}\n")
		empty then print("empty\n")
	end 'unwrap'

	return 0
end 'main'

function rewrap(p IntPromise) returns Integer
	let again = Slot.held(p)

	match again 'inner'
		held(q) then return await q
		empty then return 0
	end 'inner'
end 'rewrap'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-promise-taken-through-try-otherwise-can-be-stored -->
The promise a `try … otherwise` merge yields is the function's own, so a container takes it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function main() returns ExitCode
	var source = IntPromiseArray.create()
	source.push(async work())

	var sink = IntPromiseArray.create()
	let moved = try source.pop() otherwise async work()
	sink.push(moved)

	var sum = 0 as Integer

	for p in sink 'drain'
		sum = sum + await p
	end 'drain'

	print("{sum}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-promise-storing-function-called-through-a-function-value-takes-the-promise -->
A function value of a named function has that function's own type, and the type says which promise parameters the callee takes. A call through it hands the promise over exactly where the function takes it, so the array owns the thread and the caller does not drop it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function keep(pending IntPromiseArray, p IntPromise)
	pending.push(p)
end 'keep'

function main() returns ExitCode
	var pending = IntPromiseArray.create()
	let f = keep
	f(pending, async work())

	var sum = 0 as Integer

	for q in pending 'drain'
		sum = sum + await q
	end 'drain'

	print("{sum}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-promise-awaiting-function-called-through-a-function-value-awaits-the-promise -->
The same holds for a function that awaits its promise parameter: the callee reclaims the thread, and the caller must not.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function take(p IntPromise) returns Integer
	return await p
end 'take'

function main() returns ExitCode
	let f = take
	print("{f(async work())}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-function-type-qualifier-says-a-promise-parameter-is-taken -->
A function-type alias declares a promise parameter `keep` or `sole` when the callee takes it. A call through a parameter of that type hands the promise over, and a function that takes its promise converts to it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

typealias Sink = function(pending IntPromiseArray, p keep IntPromise)

function keep(pending IntPromiseArray, p IntPromise)
	pending.push(p)
end 'keep'

function drive(sink Sink, pending IntPromiseArray)
	sink(pending, async work())
end 'drive'

function main() returns ExitCode
	var pending = IntPromiseArray.create()
	drive(keep, pending: pending)

	var sum = 0 as Integer

	for q in pending 'drain'
		sum = sum + await q
	end 'drain'

	print("{sum}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.error.a-function-that-takes-a-promise-does-not-convert-to-a-type-that-does-not-say-so -->
A function that takes a promise parameter breaks a function type that leaves the parameter unqualified, because a call through the type would not hand the promise over.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

typealias Sink = function(pending IntPromiseArray, p IntPromise)

function keep(pending IntPromiseArray, p IntPromise)
	pending.push(p)
end 'keep'

function drive(sink Sink, pending IntPromiseArray)
	sink(pending, async work())
end 'drive'

function main() returns ExitCode
	var pending = IntPromiseArray.create()
	drive(keep, pending: pending)
	return 0
end 'main'
```
```maxoncstderr
error E3189: <fragment>:23:8: function value for 'Sink' keeps its parameter 'p', but 'Sink' does not declare it `keep`
```

<!-- test: async-await.error.a-function-that-only-reads-a-promise-does-not-convert-to-a-type-that-takes-it -->
A function type that declares a promise parameter `keep` hands the promise over at every call, so a function that only reads it would leave nobody to drop it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

typealias Sink = function(pending IntPromiseArray, p keep IntPromise) returns Count

function look(pending IntPromiseArray, p IntPromise) returns Count
	if p.isComplete() 'done'
		return pending.count()
	end 'done'

	return 0
end 'look'

function drive(sink Sink, pending IntPromiseArray) returns Count
	return sink(pending, async work())
end 'drive'

function main() returns ExitCode
	var pending = IntPromiseArray.create()
	print("{drive(look, pending: pending)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3189: <fragment>:27:16: 'look' only reads its promise parameter 'p', but 'Sink' declares it `keep` or `sole`, so a call through the type hands the promise over and nothing would drop it
```

<!-- test: async-await.a-promise-parameter-forwarded-to-a-callee-that-stores-it-is-handed-on -->
A parameter handed to a callee that takes it is handed on itself, however many calls deep: the caller moves its promise into the outermost function, and the innermost one stores it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

type Cell
	public var p as IntPromise

	static function of(p IntPromise) returns Self
		return Self{p: p}
	end 'of'
end 'Cell'

function middle(p IntPromise) returns Cell
	return Cell.of(p)
end 'middle'

function outer(p IntPromise) returns Cell
	return middle(p)
end 'outer'

function main() returns ExitCode
	let cell = outer(async work())
	print("{await cell.p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-promise-parameter-forwarded-to-a-callee-that-only-reads-it-stays-with-the-caller -->
Forwarding to a callee that only reads the promise hands nothing on, so the caller still owns the promise afterwards and awaits it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function peek(p IntPromise) returns bool
	return p.isComplete()
end 'peek'

function relay(p IntPromise) returns bool
	return peek(p)
end 'relay'

function main() returns ExitCode
	let p = async work()
	_ = relay(p)
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-user-generic-type-over-a-promise-owns-it -->
A generic type instantiated at a promise owns the thread it is given: its constructor takes the promise, a reassignment of the field drops the thread it held, and the box's destructor drops the last one. The run is leak-free.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function ask() returns IntPromise
	return async work()
end 'ask'

type Box uses T
	public var value as T

	static function create(v T) returns Self
		return Self{value: v}
	end 'create'

	function replace(v T)
		self.value = v
	end 'replace'
end 'Box'

typealias PromiseBox = Box with IntPromise

function main() returns ExitCode
	var box = PromiseBox.create(async work())
	box.replace(ask())
	box.replace(async work())
	print("{box.value.isComplete() or true}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
```

<!-- test: async-await.a-promise-parameter-can-be-stored-by-an-instance-method -->
A promise parameter is stored by a struct literal in an instance method and by an assignment to a field of `self`, as it is by a static factory.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function w() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'w'

type Cell
	public var p as IntPromise

	static function of(p IntPromise) returns Self
		return Self{p: p}
	end 'of'

	function again(next IntPromise) returns Cell
		return Self{p: next}
	end 'again'

	function rearm(next IntPromise)
		p = next
	end 'rearm'
end 'Cell'

function main() returns ExitCode
	var c = Cell.of(async w())
	let d = c.again(async w())
	c.rearm(async w())
	print("{await c.p} {await d.p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 7
```

<!-- test: async-await.a-promise-parameter-can-be-stored-in-a-tuple -->
A promise parameter is stored in a tuple slot.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function w() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'w'

function pair(p IntPromise) returns (IntPromise, Integer)
	return (p, 1)
end 'pair'

function main() returns ExitCode
	let (q, n) = pair(async w())
	print("{await q} {n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 1
```

<!-- test: async-await.a-promise-parameter-can-be-stored-in-a-union-payload -->
A promise parameter is stored in a union payload.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function w() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'w'

union Slot
	held(p IntPromise)
	empty
end 'Slot'

function roundTrip(p IntPromise) returns Integer
	let s = Slot.held(p)

	match s 'unwrap'
		held(q) then return await q
		empty then return 0
	end 'unwrap'
end 'roundTrip'

function main() returns ExitCode
	print("{roundTrip(async w())}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-promise-parameter-can-be-stored-by-a-parenthesised-assignment -->
A parenthesised right-hand side stores the parameter as the bare name does.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function w() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'w'

type Cell
	public var p as IntPromise

	static function of(p IntPromise) returns Self
		return Self{p: p}
	end 'of'
end 'Cell'

function swap(h Cell, next IntPromise)
	h.p = (next)
end 'swap'

function main() returns ExitCode
	var h = Cell.of(async w())
	swap(h, next: async w())
	print("{await h.p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-promise-parameter-can-be-stored-in-a-map -->
A map takes a promise parameter through `upsert`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias PromiseMap = Map with (String, IntPromise)

function w() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'w'

function park(m PromiseMap, p IntPromise)
	m.upsert("k", value: p)
end 'park'

function main() returns ExitCode
	var m = PromiseMap.create()
	park(m, p: async w())
	print("{m.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: async-await.an-array-literal-of-promises-owns-its-elements -->
An array literal stores its elements, so it takes ownership of each promise it is given, whatever made it. The loop awaits them out of the array, and the run is leak-free.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function ask() returns IntPromise
	return async work()
end 'ask'

function gather(p IntPromise) returns Integer
	let pending = IntPromiseArray from [p, ask(), async work()]
	var sum = 0 as Integer

	for q in pending 'drain'
		sum = sum + await q
	end 'drain'

	return sum
end 'gather'

function main() returns ExitCode
	print("{gather(async work())}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
21
```

<!-- test: async-await.error.an-array-literal-of-promises-must-match-the-array-type -->
A promise in a literal is checked against the array's element type like a promise pushed into it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

enum TaskError implements Error
	crashed
end 'TaskError'

function mayFail() returns Integer throws TaskError
	_ = File.exists(FilePath from "noyield.txt")
	throw TaskError.crashed
end 'mayFail'

function main() returns ExitCode
	var pending = IntPromiseArray from [async mayFail()]
	pending.clear()
	return 0
end 'main'
```
```maxoncstderr
error E3098: <fragment>:16:38: a promise of 'Integer' throwing 'TaskError' cannot be stored in 'IntPromise', which holds 'Integer'
```

<!-- test: async-await.error.a-throwing-promise-given-to-a-map-of-plain-promises-is-refused -->
A map's `upsert` is an ordinary call, so the value's promise type is compared with the map's by the call's argument check, and a disagreement is the same E3098 a store reports: two promise types are one type only when their result and error are the same.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias PromiseMap = Map with (String, IntPromise)

enum TaskError implements Error
	crashed
end 'TaskError'

function mayFail() returns Integer throws TaskError
	_ = File.exists(FilePath from "noyield.txt")
	throw TaskError.crashed
end 'mayFail'

function main() returns ExitCode
	var m = PromiseMap.create()
	m.upsert("a", value: async mayFail())
	return 0
end 'main'
```
```maxoncstderr
error E3098: <fragment>:17:4: a promise of 'Integer' throwing 'TaskError' cannot be stored in 'IntPromise', which holds 'Integer'
```

<!-- test: async-await.error.a-throwing-promise-given-to-a-parameter-that-omits-its-error-is-refused -->
A call's argument is compared with the parameter's promise type by the same rule a store applies, so a promise that throws is not accepted by a parameter that names no error, and the refusal says what the argument is and what the parameter holds.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

enum TaskError implements Error
	crashed
end 'TaskError'

function mayFail() returns Integer throws TaskError
	_ = File.exists(FilePath from "noyield.txt")
	throw TaskError.crashed
end 'mayFail'

function look(p IntPromise) returns bool
	return p.isComplete()
end 'look'

function main() returns ExitCode
	let p = async mayFail()
	_ = look(p)
	return 0
end 'main'
```
```maxoncstderr
error E3098: <fragment>:20:6: a promise of 'Integer' throwing 'TaskError' cannot be stored in 'IntPromise', which holds 'Integer'
```

<!-- test: async-await.error.an-alias-value-converted-to-an-alias-that-takes-the-promise-is-refused -->
A function type that leaves a promise parameter unqualified hands nothing over, so a value of that type does not convert to one that declares it `keep`: the callee through the second type would borrow a promise nobody drops.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

typealias Plain = function(p IntPromise)
typealias Taking = function(p keep IntPromise)

function promote(plain Plain) returns Taking
	return plain
end 'promote'

function look(p IntPromise)
	if p.isComplete() 'done'
		print("done\n")
	end 'done'
end 'look'

function main() returns ExitCode
	let sink = promote(look)
	sink(async work())
	return 0
end 'main'
```
```maxoncstderr
error E3189: <fragment>:14:9: function value of type 'Plain' does not take its promise parameter 'p', but 'Taking' declares it `keep` or `sole`, so a call through the type hands the promise over and nothing would drop it
```

<!-- test: async-await.error.a-promise-named-twice-in-a-call-through-a-function-type-is-refused -->
A promise has one owner, so a call through a function type may not name it at two positions when one of them hands it over.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

typealias Sink = function(pending IntPromiseArray, a IntPromise, b keep IntPromise)

function park(pending IntPromiseArray, a IntPromise, b IntPromise)
	pending.push(b)
	print("{a.isComplete()}\n")
end 'park'

function drive(sink Sink, pending IntPromiseArray)
	let p = async work()
	sink(pending, p, p)
end 'drive'

function main() returns ExitCode
	var pending = IntPromiseArray.create()
	drive(park, pending: pending)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:20:19: use of moved value 'p': it is passed at two arguments of this call, and handing it to a parameter that takes it moves it
```

<!-- test: async-await.error.an-overloaded-function-that-awaits-its-promise-does-not-convert-to-an-unqualified-type -->
Two files may each declare a private function of one name; the one a file means is read by its own declaration, so the conversion door judges the function the shape judges.
```maxon
// --- file: awaiting.maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

typealias Plain = function(p IntPromise) returns Integer

function take(p IntPromise) returns Integer
	return await p
end 'take'

function drive(sink Plain) returns Integer
	return sink(async work())
end 'drive'

function main() returns ExitCode
	print("{drive(take)}\n")
	return 0
end 'main'

// --- file: other.maxon
function take(name String) returns String
	return name
end 'take'
```
```maxoncstderr
error E3189: <fragment>:22:16: function value for 'Plain' keeps its parameter 'p', but 'Plain' does not declare it `keep`
```

<!-- test: async-await.a-promise-parameter-handed-through-self-to-a-storing-method-is-handed-on -->
A method that hands its parameter to a sibling through `self` takes it exactly when the sibling does.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

type Cell
	public var p as IntPromise

	static function of(p IntPromise) returns Self
		return Self{p: p}
	end 'of'

	function hold(next IntPromise)
		self.p = next
	end 'hold'

	function holdVia(next IntPromise)
		self.hold(next)
	end 'holdVia'
end 'Cell'

function main() returns ExitCode
	var cell = Cell.of(async work())
	cell.holdVia(async work())
	print("{await cell.p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-promise-parameter-handed-to-a-method-of-another-value-is-handed-on -->
A call through some other value takes its argument exactly when the method that value dispatches to does.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

type Cell
	public var p as IntPromise

	static function of(p IntPromise) returns Self
		return Self{p: p}
	end 'of'

	function hold(next IntPromise)
		self.p = next
	end 'hold'
end 'Cell'

function relay(cell Cell, next IntPromise)
	cell.hold(next)
end 'relay'

function main() returns ExitCode
	var cell = Cell.of(async work())
	relay(cell, next: async work())
	print("{await cell.p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-promise-parameter-handed-to-an-aliased-generic-constructor-is-handed-on -->
A generic alias names the constructor of its base type, so handing a parameter to `PromiseBox.create` hands it to `Box.create`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

type Box uses T
	public var value as T

	static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'

typealias PromiseBox = Box with IntPromise

function wrap(p IntPromise) returns PromiseBox
	return PromiseBox.create(p)
end 'wrap'

function main() returns ExitCode
	let box = wrap(async work())
	print("{box.value.isComplete() or true}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
```

<!-- test: async-await.a-promise-parameter-handed-to-a-function-typed-parameter-that-keeps-it-is-handed-on -->
A function-typed parameter that declares the promise `keep` takes it, so a function that forwards its own parameter to it takes it too.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

typealias Sink = function(pending IntPromiseArray, p keep IntPromise)

function keep(pending IntPromiseArray, p IntPromise)
	pending.push(p)
end 'keep'

function relay(sink Sink, pending IntPromiseArray, p IntPromise)
	sink(pending, p)
end 'relay'

function main() returns ExitCode
	var pending = IntPromiseArray.create()
	relay(keep, pending: pending, p: async work())

	var sum = 0 as Integer

	for q in pending 'drain'
		sum = sum + await q
	end 'drain'

	print("{sum}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-promise-parameter-forwarded-to-an-overloaded-callee-is-handed-on -->
A call that could bind either of two overloads is judged by what they do with the position: the one that stores the promise makes the forwarder take it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

type Cell
	public var p as IntPromise

	static function of(p IntPromise) returns Self
		return Self{p: p}
	end 'of'
end 'Cell'

function build(p IntPromise) returns Cell
	return Cell.of(p)
end 'build'

function build(n Integer) returns Integer
	return n
end 'build'

function relay(p IntPromise) returns Cell
	return build(p)
end 'relay'

function main() returns ExitCode
	let cell = relay(async work())
	print("{await cell.p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-named-function-that-only-reads-its-promise-leaves-it-with-the-caller -->
A function value of a function that only reads its promise parameter hands nothing over: the caller still owns the promise after the call and awaits it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function look(p IntPromise) returns bool
	return p.isComplete()
end 'look'

function main() returns ExitCode
	let f = look
	let p = async work()
	_ = f(p)
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-forwarder-and-the-function-it-forwards-to-in-two-files-are-closed-forwarder-first -->
The forwarder and the function that stores the promise live in two files, and the whole program is closed over both whichever file the compiler reads first.
```maxon
// --- file: forwarder.maxon
function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function relay(p IntPromise) returns Cell
	return Cell.of(p)
end 'relay'

function main() returns ExitCode
	let cell = relay(async work())
	print("{await cell.p}\n")
	return 0
end 'main'

// --- file: callee.maxon
export typealias Integer = int(i64.min to i64.max)
export typealias IntPromise = Promise with Integer

export type Cell
	export var p as IntPromise

	export static function of(p IntPromise) returns Self
		return Self{p: p}
	end 'of'
end 'Cell'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-forwarder-and-the-function-it-forwards-to-in-two-files-are-closed-callee-first -->
The same two files, read in the other order.
```maxon
// --- file: callee.maxon
export typealias Integer = int(i64.min to i64.max)
export typealias IntPromise = Promise with Integer

export type Cell
	export var p as IntPromise

	export static function of(p IntPromise) returns Self
		return Self{p: p}
	end 'of'
end 'Cell'

// --- file: forwarder.maxon
function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function relay(p IntPromise) returns Cell
	return Cell.of(p)
end 'relay'

function main() returns ExitCode
	let cell = relay(async work())
	print("{await cell.p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.error.a-promise-stored-through-a-conditional-expression-is-refused -->
A conditional expression joins two promise values with no single slot to empty, so storing its result is refused rather than leaving one branch unaccounted for.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

type Cell
	public var p as IntPromise

	static function of(p IntPromise) returns Self
		return Self{p: p}
	end 'of'
end 'Cell'

function choose(c Cell, a IntPromise, b IntPromise, first bool)
	c.p = a if first else b
end 'choose'

function main() returns ExitCode
	var cell = Cell.of(async work())
	choose(cell, a: async work(), b: async work(), first: true)
	print("{await cell.p}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3141: <fragment>:19:4: a promise cannot be borrowed through 'p': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. No frame owns this one: either the slot it was read from has already been consumed by another read of it, or it reached here through a branch or loop join — and a merge has no single slot to empty, because the paths can name different ones. Consume each read once, and do it before the paths join
```

<!-- test: async-await.a-promise-parameter-forwarded-to-a-same-named-function-in-another-directory-is-handed-on -->
Two directories may each export a function of one name; a forwarder that qualifies the call is judged by the function it names, and the other directory's function of that name is left out of the answer.
```maxon
// --- file: alpha/lib.maxon
export typealias Integer = int(i64.min to i64.max)
export typealias IntPromise = Promise with Integer

export type Cell
	export var p as IntPromise

	export static function of(p IntPromise) returns Self
		return Self{p: p}
	end 'of'
end 'Cell'

export function stash(p IntPromise) returns Cell
	return Cell.of(p)
end 'stash'

// --- file: beta/lib.maxon
export function stash(n Integer) returns Integer
	return n
end 'stash'

// --- file: main.maxon
function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function relay(p IntPromise) returns Cell
	return alpha.stash(p)
end 'relay'

function main() returns ExitCode
	print("{beta.stash(1)}\n")
	let cell = relay(async work())
	print("{await cell.p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
7
```

<!-- test: async-await.a-promise-forwarded-beside-a-reassigned-parameter-is-handed-on -->
A parameter another function reassigns is passed by reference, and the program-wide widening that carries a reassigned parameter through its forwarders leaves a promise parameter alone: a promise has no cell, so the forwarder still owns it and hands it on.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function stash(dest String, pending IntPromiseArray, p IntPromise)
	dest = "changed {1 + 1}"
	pending.push(p)
end 'stash'

function relay(dest String, pending IntPromiseArray, p IntPromise)
	stash(dest, pending: pending, p: p)
end 'relay'

function main() returns ExitCode
	var pending = IntPromiseArray.create()
	var word = "hello {2 + 2}"
	relay(word, pending: pending, p: async work())
	print("{word}\n")

	var sum = 0 as Integer

	for q in pending 'drain'
		sum = sum + await q
	end 'drain'

	print("{sum}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
changed 2
7
```

<!-- test: async-await.a-promise-handed-to-a-function-typed-parameter-beside-a-reassigned-parameter-is-handed-on -->
The same widening reaches a call through a function type, whose by-reference positions are those of every function of its arity; the promise position keeps its ownership.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

typealias Sink = function(pending IntPromiseArray, p keep IntPromise)

function keep(pending IntPromiseArray, p IntPromise)
	pending.push(p)
end 'keep'

function relay(sink Sink, pending IntPromiseArray, p IntPromise)
	sink(pending, p)
end 'relay'

function zap(dest String, n Integer)
	dest = "filled {n}"
end 'zap'

function main() returns ExitCode
	var pending = IntPromiseArray.create()
	relay(keep, pending: pending, p: async work())

	var sum = 0 as Integer

	for q in pending 'drain'
		sum = sum + await q
	end 'drain'

	var word = "x {1 + 1}"
	zap(word, n: 2)
	print("{sum} {word}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 filled 2
```

<!-- test: async-await.a-promise-parameter-the-callee-reassigns-is-seen-by-its-caller -->
A promise parameter the callee reassigns is the caller's own storage: the callee drops the thread the cell held and the cell takes the new one, so the caller awaits the new thread and no thread is left over.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function reset(q IntPromise)
	q = async work()
end 'reset'

function main() returns ExitCode
	var x = async work()
	reset(x)
	print("{await x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-write-through-a-forwarded-promise-parameter-reaches-the-callers-variable -->
A function that only hands its promise parameter on to one that reassigns it passes the caller's storage through, so the write lands in the variable the outermost caller named.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function inner(q IntPromise)
	q = async work()
end 'inner'

function relay(p IntPromise)
	inner(p)
end 'relay'

function main() returns ExitCode
	var x = async work()
	relay(x)
	print("{await x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-promise-at-a-by-reference-function-type-position-is-handed-on -->
A call through a function type passes by reference the positions some function of that arity reassigns, and a promise at one of them is held in the caller's storage and still handed on to the callee that keeps it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

typealias Sink = function(pending IntPromiseArray, p keep IntPromise)

function keep(pending IntPromiseArray, p IntPromise)
	pending.push(p)
end 'keep'

function relay(sink Sink, pending IntPromiseArray, p IntPromise)
	sink(pending, p)
end 'relay'

function zap(n Integer, dest String)
	dest = "filled {n}"
end 'zap'

function main() returns ExitCode
	var pending = IntPromiseArray.create()
	relay(keep, pending: pending, p: async work())

	var sum = 0 as Integer

	for q in pending 'drain'
		sum = sum + await q
	end 'drain'

	var word = "x {1 + 1}"
	zap(2, dest: word)
	print("{sum} {word}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 filled 2
```

<!-- test: async-await.a-cell-resident-promise-its-owner-rebinds-drops-the-thread-it-held -->
A promise variable handed to a parameter the callee reassigns lives in a cell, and the owner's own rebind of it is the same three steps a field rebind is: the old thread is dropped, the new one is taken, and the cell owns it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function reset(q IntPromise)
	q = async work()
end 'reset'

function main() returns ExitCode
	var x = async work()
	reset(x)
	x = async work()
	print("{await x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: async-await.a-cell-resident-promise-nobody-awaits-is-dropped-with-its-cell -->
The cell owns the promise it holds, so a variable whose callee re-armed it and that is never awaited releases the thread at the end of its scope.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function reset(q IntPromise)
	q = async work()
end 'reset'

function main() returns ExitCode
	var x = async work()
	reset(x)
	print("done\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
done
```

<!-- test: async-await.error.a-cell-resident-promise-awaited-twice-is-refused -->
Awaiting a cell-resident promise empties its cell and spends the variable, so a second await names a moved value.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function reset(q IntPromise)
	q = async work()
end 'reset'

function main() returns ExitCode
	var x = async work()
	reset(x)
	print("{await x}\n")
	print("{await x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:18:16: use of moved value 'x': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: async-await.error.an-alias-of-a-cell-resident-promise-is-refused-once-the-cell-is-rebound -->
A second name for the thread a cell holds does not own it, and rebinding the cell drops that thread, so the alias is refused rather than left naming a dropped thread.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function reset(q IntPromise)
	q = async work()
end 'reset'

function main() returns ExitCode
	var x = async work()
	reset(x)
	let y = x
	x = async work()
	print("{await y}\n")
	print("{await x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3141: <fragment>:19:10: a promise cannot be borrowed through 'await': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. No frame owns this one: either the slot it was read from has already been consumed by another read of it, or it reached here through a branch or loop join — and a merge has no single slot to empty, because the paths can name different ones. Consume each read once, and do it before the paths join
```

<!-- test: async-await.an-overload-set-that-disagrees-about-passing-a-promise-by-reference-hands-a-spawn-to-the-member-that-takes-it -->
Two overloads differ in whether they write the promise parameter, so the argument is held in a cell until the call is resolved; when the chosen member takes the promise by value the cell gives the thread up and only the member owns it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function put(q IntPromise, n Integer) returns Integer
	q = async work()
	return n
end 'put'

function put(q IntPromise, n String) returns Integer
	let size = n.byteLength() as Integer
	return size + (await q)
end 'put'

function main() returns ExitCode
	print("{put(async work(), n: "x")}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8
```

<!-- test: async-await.an-overload-set-that-disagrees-about-passing-a-promise-by-reference-keeps-a-spawn-in-the-cell-of-the-member-that-writes-it -->
The same two overloads with the writing member chosen: the cell owns the spawn, the member drops it when it reassigns the parameter, and the cell releases the new thread at the end of the call.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function put(q IntPromise, n Integer) returns Integer
	q = async work()
	return n
end 'put'

function put(q IntPromise, n String) returns Integer
	let size = n.byteLength() as Integer
	return size + (await q)
end 'put'

function main() returns ExitCode
	print("{put(async work(), n: 5)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: async-await.error.a-let-promise-called-through-a-function-value-that-reassigns-it-is-refused -->
A promise held by a `let` is lent to a by-reference position, and a callee that writes the parameter would drop the thread the `let` still names, so the write is refused at the call whichever way the function is reached.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function reset(q IntPromise)
	q = async work()
end 'reset'

function main() returns ExitCode
	let x = async work()
	let r = reset
	r(x)
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:17:3: cannot pass 'x', which is read-only (a `let` binding), to a function that writes parameter 'q' (in main); pass a clone()
```

<!-- test: async-await.error.a-let-promise-handed-to-a-function-typed-parameter-bound-to-a-reassigning-function-is-refused -->
The function that reaches the writer is a parameter of function type, so only the type says the position is passed by reference; the `let` is refused all the same.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function reset(q IntPromise)
	q = async work()
end 'reset'

typealias Resetter = function(q IntPromise)

function apply(r Resetter, x IntPromise)
	r(x)
end 'apply'

function main() returns ExitCode
	let x = async work()
	apply(reset, x: x)
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:22:2: cannot pass 'x', which is read-only (a `let` binding), to a function that writes parameter 'x' (in main); pass a clone()
```

<!-- test: async-await.error.a-promise-in-a-let-field-passed-to-a-function-that-reassigns-it-is-refused -->
A `let` field has no other writer, so a callee that reassigns the parameter cannot be handed the promise it holds.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function reset(q IntPromise)
	q = async work()
end 'reset'

type Holder
	export let p as IntPromise

	static function of(p IntPromise) returns Self
		return Self{p: p}
	end 'of'
end 'Holder'

function main() returns ExitCode
	let h = Holder.of(async work())
	reset(h.p)
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:24:2: cannot pass a `let` field of 'h', which is read-only, to a function that writes parameter 'q' (in main); pass a clone()
```

<!-- test: async-await.error.a-for-loop-promise-passed-to-a-function-that-reassigns-it-is-refused -->
The loop variable names a thread the array still owns, so a callee that reassigns its parameter is refused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function reset(q IntPromise)
	q = async work()
end 'reset'

function main() returns ExitCode
	var a = IntPromiseArray.create()
	a.push(async work())

	for q in a 'each'
		reset(q)
	end 'each'

	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:20:3: cannot pass a value read out of a container that still owns the promise, which is read-only, to a function that writes parameter 'q' (in main); pass a clone()
```

<!-- test: async-await.error.a-let-promise-captured-by-a-closure-that-reassigns-it-is-refused -->
A closure holds its own copy of what it captures, so a write through the closure's call cannot reach the original and is refused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function reset(q IntPromise)
	q = async work()
end 'reset'

function reset2(q IntPromise) returns Integer
	q = async work()
	return 1
end 'reset2'

function main() returns ExitCode
	let x = async work()
	let f = function() gives reset2(x)
	print("{f()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:21:27: cannot pass 'x' to a function that writes parameter 'q' (in main$closure_0): the closure holds its own copy of 'x', so a write through 'q' cannot reach the original; pass a clone()
```

<!-- test: async-await.error.a-promise-still-held-by-an-array-passed-to-a-function-that-reassigns-it-is-refused -->
An element read out of an array is neither a `let` nor a variable of this frame, so the promise it names is blamed as one the array still owns, and a callee that reassigns its parameter is refused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function reset(q IntPromise)
	q = async work()
end 'reset'

function main() returns ExitCode
	var a = IntPromiseArray.create()
	a.push(async work())
	reset(try a.get(0) otherwise panic("empty"))
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:18:2: cannot pass a value read out of a container that still owns the promise, which is read-only, to a function that writes parameter 'q' (in main); pass a clone()
```

<!-- test: async-await.a-spawn-handed-to-a-function-typed-parameter-bound-to-a-reassigning-function-is-owned-by-the-cell -->
A spawn has no other name, so the cell the by-reference position needs owns it: the callee drops it when it reassigns the parameter and the cell releases the replacement.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function reset(q IntPromise)
	q = async work()
end 'reset'

typealias Resetter = function(q IntPromise)

function apply(r Resetter, x IntPromise)
	r(x)
end 'apply'

function main() returns ExitCode
	apply(reset, x: async work())
	print("done\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
done
```

<!-- test: async-await.a-var-promise-handed-to-an-overload-set-that-disagrees-about-writing-it-follows-the-member-chosen -->
A variable is already a cell, so an overload set that disagrees about passing the promise by reference hands the cell itself to the writing member and the thread to the member that takes it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function reset(q IntPromise)
	q = async work()
end 'reset'

function put(q IntPromise, n Integer) returns Integer
	q = async work()
	return n
end 'put'

function put(q IntPromise, n String) returns Integer
	let size = n.byteLength() as Integer
	return size + (await q)
end 'put'

function main() returns ExitCode
	var x = async work()
	print("{put(x, n: "x")}\n")
	var y = async work()
	print("{put(y, n: 5)}\n")
	print("{await y}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8
5
7
```

<!-- test: async-await.a-promise-an-array-still-owns-is-lent-to-a-function-type-position-the-callee-only-reads -->
When some function of an arity reassigns its parameter, a call through a function type of that arity passes the position by reference; a callee that only reads the promise sees the array's own thread, leaves it with the array, and the array still hands it out afterwards.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'work'

function reset(q IntPromise)
	q = async work()
end 'reset'

function look(q IntPromise) returns bool
	return q.isComplete() or true
end 'look'

function main() returns ExitCode
	var pending = IntPromiseArray.create()
	pending.push(async work())
	var x = async work()
	reset(x)
	print("{await x}\n")
	let f = look

	for q in pending 'each'
		print("{f(q)}\n")
	end 'each'

	var sum = 0 as Integer

	for q in pending 'drain'
		sum = sum + await q
	end 'drain'

	print("{sum}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
true
7
```

<!-- test: async-await.error.propagate-type-mismatch -->
Bare `try await p` re-throws the awaited thunk's error through the enclosing
function's error-return ABI. If the two error types differ, the caller decodes
one enum's ordinals as another's tags — a silent miscompile. The `try` CALL
form rejects this, and so does the `try await` form: the promise carries the
error type to compare.
```maxon
typealias Integer = int(i64.min to i64.max)

enum AError implements Error
		alpha
end 'AError'

enum BError implements Error
		beta
		gamma
end 'BError'

function throwsA() returns Integer throws AError
		_ = File.exists(FilePath from "noyield.txt")
		throw AError.alpha
end 'throwsA'

function caller() returns Integer throws BError
		let p = async throwsA()
		let r = try await p
		return r
end 'caller'

function main() returns ExitCode
		let v = try caller() otherwise 0
		return v
end 'main'
```
```maxoncstderr
error E3059: specs/async-await/async-await.error.propagate-type-mismatch.maxon:20:11: try propagates 'AError' but enclosing function throws 'BError' — add 'otherwise' to convert
```

<!-- test: async-await.error.propagate-type-mismatch-through-storage -->
The propagation type check needs an error type to check, and it has one even when the promise
came out of storage. Unchecked, this program is a SILENT MISCOMPILE: `mayFail` throws `TaskError`,
`viaStorage` throws `WrapError`, and re-throwing one through the other's error-return ABI makes the
caller decode `TaskError`'s ordinals as `WrapError`'s tags — and since `WrapError` has associated
values, mm_decref an ordinal as a pointer and die. The
storage type names `TaskError`, so the check simply fires, exactly as it does for a direct await.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias TaskPromise = Promise with (Integer, TaskError)
typealias TaskPromiseArray = Array with TaskPromise

enum TaskError implements Error
		timedOut
		crashed
end 'TaskError'

union WrapError implements Error
		wrapped(reason String)
end 'WrapError'

function mayFail() returns Integer throws TaskError
		_ = File.exists(FilePath from "noyield.txt")
		throw TaskError.crashed
end 'mayFail'

function viaStorage() returns Integer throws WrapError
		var arr = TaskPromiseArray.create()
		arr.push(async mayFail())
		let p = try arr.get(0) otherwise panic("index 0 is in bounds by construction")
		let r = try await p
		return r
end 'viaStorage'

function main() returns ExitCode
		let v = try viaStorage() otherwise 55
		return v
end 'main'
```
```maxoncstderr
error E3059: specs/async-await/async-await.error.propagate-type-mismatch-through-storage.maxon:24:11: try propagates 'TaskError' but enclosing function throws 'WrapError' — add 'otherwise' to convert
```

<!-- test: async-await.error.double-await -->
`await` is LINEAR: a promise is awaited exactly once. The thunk owns its result and hands it over
at the await, so a second await takes a second reference to a payload the thunk only owned once —
the two releases underflow the refcount and free it twice ("mm_decref: refcount underflow").
The double-free is made unrepresentable rather than fixed.

Note the thunk does not throw. This is an OWNERSHIP bug, not an error-handling one: a plain
`async` returning a managed `String` double-frees identically.
```maxon
function makeText() returns String
		_ = File.exists(FilePath from "noyield.txt")
		return "hello world"
end 'makeText'

function main() returns ExitCode
		let p = async makeText()
		let a = await p
		let b = await p
		print(a)
		print(b)
		return 0
end 'main'
```
```maxoncstderr
error E3142: specs/async-await/async-await.error.double-await.maxon:10:17: this promise was already consumed by an earlier 'await': a promise owns a green thread, and a green thread has exactly one owner — the consume reclaims the thread's struct, so a later use of any name that spells it reads memory the scheduler has taken back. An alias names the same thread. Re-arm the binding from a fresh `async` spawn to use the name again
```

<!-- test: async-await.error.double-await-in-loop -->
The linear-await check is FLOW-SENSITIVE, and this is why it has to be. There is exactly ONE
`await` here lexically, so a "have I seen this promise awaited before?" check finds nothing — but
it sits in a loop over a promise spawned OUTSIDE the loop, so it awaits the same green thread on
every iteration. Reachability catches it: the await is reachable from itself without passing
through the `async` that would re-arm the binding.
```maxon
function makeText() returns String
		_ = File.exists(FilePath from "noyield.txt")
		return "hello"
end 'makeText'

function main() returns ExitCode
		let p = async makeText()
		for i in 0 upto 3 'each'
				let s = await p
				print("{i}={s} ")
		end 'each'
		return 0
end 'main'
```
```maxoncstderr
error E3100: specs/async-await/async-await.error.double-await-in-loop.maxon:10:13: this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice
```

<!-- test: async-await.linear.await-in-exclusive-branches -->
Two awaits of one promise in MUTUALLY EXCLUSIVE branches are each the only await on their own
path, and are allowed. A lexical "already awaited" check would reject this valid program; the
reachability check does not, because neither await can reach the other.
```maxon
typealias Integer = int(i64.min to i64.max)

function makeValue() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 21
end 'makeValue'

function main() returns ExitCode
		let p = async makeValue()
		let flag = File.exists(FilePath from "definitely-not-here.txt")
		if flag 'branch'
				let a = await p
				return a as ExitCode
		end 'branch'
		let b = await p
		return (b + 1) as ExitCode
end 'main'
```
```exitcode
22
```

<!-- test: async-await.error.double-await-through-alias -->
Linearity is a property of the GREEN THREAD, not of the identifier text. `let q = p` gives one
green thread a second name; awaiting through both names awaits it twice, and the payload the
thunk handed over once is released twice ("mm_decref: refcount underflow"). A check keyed on the
NAME would compile this clean — one extra line would defeat the whole thing.
```maxon
function makeText() returns String
		_ = File.exists(FilePath from "noyield.txt")
		return "hello world"
end 'makeText'

function main() returns ExitCode
		let p = async makeText()
		let q = p
		let a = await p
		let b = await q
		print(a)
		print(b)
		return 0
end 'main'
```
```maxoncstderr
error E3142: specs/async-await/async-await.error.double-await-through-alias.maxon:11:17: this promise was already consumed by an earlier 'await': a promise owns a green thread, and a green thread has exactly one owner — the consume reclaims the thread's struct, so a later use of any name that spells it reads memory the scheduler has taken back. An alias names the same thread. Re-arm the binding from a fresh `async` spawn to use the name again
```

<!-- test: async-await.error.double-await-through-alias-in-branch -->
The same alias, made in a DIFFERENT BLOCK from the `async` that spawned the thread. This is why
the key cannot be the promise value's SSA id either: a cross-block read of a promise variable
re-tags a fresh value around the same green thread, so `p` and `q` here hold two different SSA
ids for one thread. The id that survives the re-tag — the thread's own — is the one linearity
keys on.
```maxon
function makeText() returns String
		_ = File.exists(FilePath from "noyield.txt")
		return "hello world"
end 'makeText'

function main() returns ExitCode
		let p = async makeText()
		let flag = File.exists(FilePath from "definitely-not-here.txt")
		if not flag 'branch'
				let q = p
				let a = await p
				let b = await q
				print(a)
				print(b)
		end 'branch'
		return 0
end 'main'
```
```maxoncstderr
error E3142: specs/async-await/async-await.error.double-await-through-alias-in-branch.maxon:13:19: this promise was already consumed by an earlier 'await': a promise owns a green thread, and a green thread has exactly one owner — the consume reclaims the thread's struct, so a later use of any name that spells it reads memory the scheduler has taken back. An alias names the same thread. Re-arm the binding from a fresh `async` spawn to use the name again
```

<!-- test: async-await.error.double-await-alias-outlives-rebind -->
Re-arming `p` does NOT end the first thread's life while `q` still names it. The walk that proves
linearity therefore cannot stop at "the binding I started from was reassigned" — it stops only
when EVERY binding that awaits the thread has been reassigned. Here `q` still names the first
thread when it is awaited, so that await is the second one, and it is refused.
```maxon
function makeText() returns String
		_ = File.exists(FilePath from "noyield.txt")
		return "hello world"
end 'makeText'

function main() returns ExitCode
		var p = async makeText()
		let q = p
		let a = await p
		p = async makeText()
		let b = await q
		let c = await p
		print(a)
		print(b)
		print(c)
		return 0
end 'main'
```
```maxoncstderr
error E3142: specs/async-await/async-await.error.double-await-alias-outlives-rebind.maxon:12:17: this promise was already consumed by an earlier 'await': a promise owns a green thread, and a green thread has exactly one owner — the consume reclaims the thread's struct, so a later use of any name that spells it reads memory the scheduler has taken back. An alias names the same thread. Re-arm the binding from a fresh `async` spawn to use the name again
```

<!-- test: async-await.linear.rearm-after-await -->
Reassigning a promise binding RE-ARMS it: `p` now names a new green thread, so awaiting it again
is the first await of that thread, not a second await of the old one. This must keep compiling —
the linear check refuses a second await of one thread, not a second `await p` in the text.
```maxon
function makeText() returns String
		_ = File.exists(FilePath from "noyield.txt")
		return "hello"
end 'makeText'

function main() returns ExitCode
		var p = async makeText()
		let a = await p
		p = async makeText()
		let b = await p
		print(a)
		print(b)
		return 0
end 'main'
```
```stdout
hellohello
```
```exitcode
0
```

<!-- test: async-await.linear.await-aliased-loop-element -->
The `for p in promises` idiom with an ALIAS inside the loop. Every iteration re-arms `p` — and
therefore `q` — so the single `await q` is one await per promise, not N awaits of one. This is
the case an over-eager linearity check breaks: the alias makes `p` and `q` one green thread, and
a check that unified them without also honouring the re-arm would reject the central idiom for
draining a pool of promises. Both halves are pinned here, and they must stay pinned together.
```maxon
typealias Idx = int(0 to 100)
typealias StrPromise = Promise with String
typealias StrPromiseArray = Array with StrPromise

function makeText(i Idx) returns String
		_ = File.exists(FilePath from "noyield.txt")
		return "t{i}"
end 'makeText'

function main() returns ExitCode
		var promises = StrPromiseArray.create()
		for i in 0 upto 3 'spawn'
				promises.push(async makeText(i))
		end 'spawn'
		for p in promises 'await'
				let q = p
				let s = await q
				print("{s} ")
		end 'await'
		return 0
end 'main'
```
```stdout
t0 t1 t2 
```
```exitcode
0
```

<!-- test: async-await.linear.await-in-ternary-arms -->
The two arms of a ternary are MUTUALLY EXCLUSIVE — only the selected arm is evaluated — so an
`await` in each is the only await on its own path, exactly as in an `if`/`else`. This is pinned
because the ternary's arms are a pair of blocks: a linearity check that ran against a shape with
both arms hoisted into the entry block and only the store made conditional would see two awaits in
ONE block and reject a valid program. It reads the arms as the branches they are.
```maxon
typealias Integer = int(i64.min to i64.max)

function makeValue() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 21
end 'makeValue'

function main() returns ExitCode
		let p = async makeValue()
		let flag = File.exists(FilePath from "definitely-not-here.txt")
		let v = (await p) if flag else (await p)
		return (v + 1) as ExitCode
end 'main'
```
```exitcode
22
```

<!-- test: async-await.linear.await-aliased-in-ternary-arms -->
The same, through an ALIAS: `p` and `q` are one green thread under two names, and the two arms
await it through different names. Linearity keys on the THREAD, so it sees one thread awaited in
each of two exclusive arms — which is one await per path, and legal. This is the intersection of
the two facts that must both hold: the alias must UNIFY (or `await p; await q` in sequence would
double-free), and the arms must stay EXCLUSIVE (or unifying them would reject this).
```maxon
typealias Integer = int(i64.min to i64.max)

function makeValue() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 21
end 'makeValue'

function main() returns ExitCode
		let p = async makeValue()
		let q = p
		let flag = File.exists(FilePath from "definitely-not-here.txt")
		let v = (await p) if flag else (await q)
		return (v + 1) as ExitCode
end 'main'
```
```exitcode
22
```

<!-- test: async-await.error.double-await-after-ternary-arm -->
The other side of the ternary boundary. An `await` in one arm does NOT make the promise spent on
the path where that arm was not taken — but the await AFTER the ternary is reachable from the arm
that was, so on that path the thread is awaited twice. Exclusivity buys the two arms nothing here:
reachability is what decides, and the arm reaches the tail.
```maxon
typealias Integer = int(i64.min to i64.max)

function makeValue() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 21
end 'makeValue'

function main() returns ExitCode
		let p = async makeValue()
		let flag = File.exists(FilePath from "definitely-not-here.txt")
		let v = (await p) if flag else 0
		let w = await p
		return (v + w) as ExitCode
end 'main'
```
```maxoncstderr
error E3142: specs/async-await/async-await.error.double-await-after-ternary-arm.maxon:13:17: this promise was already consumed by an earlier 'await': a promise owns a green thread, and a green thread has exactly one owner — the consume reclaims the thread's struct, so a later use of any name that spells it reads memory the scheduler has taken back. An alias names the same thread. Re-arm the binding from a fresh `async` spawn to use the name again
```

<!-- test: async-await.error.await-without-try -->
A throwing thunk hands the awaiting frame an OWNED error on its error path. A
plain `await` has nowhere to put it: the value it yields is the undefined success
slot, and an associated-value payload is released by nobody — the run below ends
101 (leak) if it is allowed to compile. `try await` is the only form that can
receive the error, as the top of this spec says, and the compiler enforces it.
```maxon
typealias Integer = int(i64.min to i64.max)

union TaskError implements Error
		crashed(reason String)
end 'TaskError'

function mayFail() returns Integer throws TaskError
		_ = File.exists(FilePath from "noyield.txt")
		throw TaskError.crashed("boom")
end 'mayFail'

function main() returns ExitCode
		let p = async mayFail()
		let r = await p
		return r
end 'main'
```
```maxoncstderr
error E3057: specs/async-await/async-await.error.await-without-try.maxon:15:11: throwing function requires try: 'await' on a promise from a function that throws 'TaskError' drops the error and leaks its payload — use 'try await'
```

<!-- test: async-await.cancel -->
```maxon
typealias Integer = int(i64.min to i64.max)

function yieldingTask() returns Integer
		if File.exists(FilePath from "nonexistent.txt") 'check'
				return 1
		end 'check'
		return 0
end 'yieldingTask'

function main() returns ExitCode
		let p = async yieldingTask()
		p.cancel()
		return 42
end 'main'
```
```exitcode
42
```

<!-- test: async-await.trace-yield -->
<!-- AsyncTrace -->
Verify that async I/O operations yield and resume the green thread.
The trace output is deterministic for single-thread async: spawn, yield at I/O, resume, await with [yield].
```maxon
typealias Integer = int(i64.min to i64.max)

function ioTask() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 42
end 'ioTask'

function main() returns ExitCode
		let p = async ioTask()
		let r = await p
		return r
end 'main'
```
```exitcode
42
```
```stderr
spawn #1
io_yield #1 [file_exists]
io_resume #1 [file_exists]
await #1 [yield]
```

<!-- test: async-await.promise-array -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function compute(n Integer) returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return n
end 'compute'

function main() returns ExitCode
		var arr = IntPromiseArray.create()
		arr.push(async compute(10))
		arr.push(async compute(20))
		arr.push(async compute(12))
		var sum = 0 as Integer
		for p in arr 'each'
				sum = sum + await p
		end 'each'
		return sum
end 'main'
```
```exitcode
42
```

<!-- test: async-await.promise-array-throwing -->
`work` throws, so the storage type names what it throws: `Promise with (Integer, WorkError)`.
Throws-ness is a property of the TYPE and survives the box, so a stored non-throwing promise
still takes a plain `await` (see `async-await.promise-array`) and only a genuinely throwing one
demands `try await`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with (Integer, WorkError)
typealias IntPromiseArray = Array with IntPromise

enum WorkError implements Error
		failed
end 'WorkError'

function work(n Integer) returns Integer throws WorkError
		_ = File.exists(FilePath from "noyield.txt")
		if n < 0 'neg'
				throw WorkError.failed
		end 'neg'
		return n
end 'work'

function main() returns ExitCode
		var arr = IntPromiseArray.create()
		arr.push(async work(10))
		arr.push(async work(20))
		arr.push(async work(-1))
		var sum = 0 as Integer
		for p in arr 'each'
				sum = sum + try await p otherwise 0
		end 'each'
		return sum
end 'main'
```
```exitcode
30
```

<!-- test: async-await.promise-array-throwing-assoc-value -->
A stored promise whose thunk throws an ASSOCIATED-VALUE union: the heap-allocated error payload
must be released on the `otherwise` path, or the third element's `WorkError.failed` allocation
leaks. The storage type names `WorkError`, so the release is a straight-line decref emitted from
the static type — the same code a direct await emits.

Whether the error flag is a heap pointer or a plain ordinal is answered statically by the named
error type, so the box carries no runtime bit and the `otherwise` path does not branch on one.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with (Integer, WorkError)
typealias IntPromiseArray = Array with IntPromise

union WorkError implements Error
		failed(reason String)
end 'WorkError'

function work(n Integer) returns Integer throws WorkError
		_ = File.exists(FilePath from "noyield.txt")
		if n < 0 'neg'
				throw WorkError.failed("negative input")
		end 'neg'
		return n
end 'work'

function main() returns ExitCode
		var arr = IntPromiseArray.create()
		arr.push(async work(10))
		arr.push(async work(20))
		arr.push(async work(-1))
		var sum = 0 as Integer
		for p in arr 'each'
				sum = sum + try await p otherwise 0
		end 'each'
		return sum
end 'main'
```
```exitcode
30
```

<!-- test: async-await.managed-args-many -->
A regression guard for the spawn-site incref of managed (Struct/Enum)
async arguments: spawn eight green threads in a row, each receiving a
freshly built StringArray, then await them all. Without the incref the
caller's scope-end decref would free the StringArray before the green
thread runs, surfacing as a SIGSEGV in `__gt_trampoline` once enough
allocator churn pushes the freed slot into reuse.
```maxon
typealias StringArray = Array with String
typealias StrPromise = Promise with String
typealias StrPromiseArray = Array with StrPromise

function joinArgs(label String, args StringArray) returns String
		_ = File.exists(FilePath from "noyield.txt")
		var out = label
		for a in args 'each'
				out = "{out}|{a}"
		end 'each'
		return out
end 'joinArgs'

function main() returns ExitCode
		var promises = StrPromiseArray.create()
		for i in 0 upto 8 'spawn'
				var argv = StringArray.create()
				argv.push("arg{i}.a")
				argv.push("arg{i}.b")
				promises.push(async joinArgs("L{i}", args: argv))
		end 'spawn'
		var total = 0 as ExitCode
		for p in promises 'await'
				let s = await p
				if not s.isEmpty() 'good'
						total = total + 1
				end 'good'
		end 'await'
		return total
end 'main'
```
```exitcode
8
```

<!-- test: async-await.nested -->
`async` NESTS: a green thread may itself spawn and await another. This is the
minimal shape — one `async` inside one `async`, where the inner one does I/O.
The flag that says "this green thread has finished switching off its stack" must
be published by the switch itself, not by the scheduler loop that DISPATCHED a
thread. One level deep those are the same thread; nested they are not, so a
loop-published flag would leave the inner thread's completion spinning for ever
inside `__netpoll_claim_done`, deadlocking the whole process at N=1 and pinning
a core.
```maxon
typealias Integer = int(i64.min to i64.max)

function leaf(n Integer) returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return n
end 'leaf'

function outer() returns Integer
		let p1 = async leaf(41)
		return await p1
end 'outer'

function main() returns ExitCode
		let q = async outer()
		return await q
end 'main'
```
```exitcode
41
```

<!-- test: async-await.nested-two-levels -->
Two levels of nesting: `main` awaits a thread that awaits a thread that awaits a
thread. Each level hands the next one its M directly out of its own `__gt_await`
scheduling loop, so the dispatcher and the suspending thread differ at every
level rather than only at the innermost one.
```maxon
typealias Integer = int(i64.min to i64.max)

function leaf(n Integer) returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return n + 1
end 'leaf'

function mid(n Integer) returns Integer
		let p = async leaf(n)
		return await p
end 'mid'

function outer(n Integer) returns Integer
		let p = async mid(n)
		return await p
end 'outer'

function main() returns ExitCode
		let q = async outer(11)
		return await q
end 'main'
```
```exitcode
12
```

<!-- test: async-await.nested-in-expression -->
A nested `await` used as an operand rather than bound to its own `let`, and two
of them in one expression: the value has to survive the resume, not just the
scheduling.
```maxon
typealias Integer = int(i64.min to i64.max)

function leaf(n Integer) returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return n * 2
end 'leaf'

function outer() returns Integer
		let a = async leaf(3)
		let b = async leaf(4)
		return (await a) + (await b) + 1
end 'outer'

function main() returns ExitCode
		let q = async outer()
		return await q
end 'main'
```
```exitcode
15
```

<!-- test: async-await.nested-spawn-then-await-late -->
The nested thread is spawned EARLY and awaited LATE — the outer thread does its
own I/O in between, so it suspends and resumes once before it ever awaits its
child, and the child may be picked up either by the outer thread's own await
loop or by a worker.
```maxon
typealias Integer = int(i64.min to i64.max)

function leaf(n Integer) returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return n
end 'leaf'

function outer() returns Integer
		let p1 = async leaf(3)
		_ = File.exists(FilePath from "noyield.txt")
		let p2 = async leaf(4)
		_ = File.exists(FilePath from "noyield.txt")
		return (await p1) + (await p2)
end 'outer'

function main() returns ExitCode
		let q = async outer()
		return await q
end 'main'
```
```exitcode
7
```

<!-- test: async-await.nested-void -->
A nested `async` on a void function: the inner thread's completion has no result
to publish, so the wakeup is the whole notification.
```maxon
typealias Integer = int(i64.min to i64.max)

var flag = 0 as Integer

function setFlag()
		_ = File.exists(FilePath from "noyield.txt")
		flag = 1
end 'setFlag'

function bump() returns Integer
		let p = async setFlag()
		await p
		return flag + 40
end 'bump'

function main() returns ExitCode
		let q = async bump()
		return await q
end 'main'
```
```exitcode
41
```

<!-- test: async-await.nested-try-await -->
`try await` inside an `async` function, on a nested throwing thread — the
throwing await has its own scheduling loop, distinct from plain `await`'s.
```maxon
typealias Integer = int(i64.min to i64.max)

enum TestError implements Error
		failed
end 'TestError'

function mayFail(succeed bool) returns Integer throws TestError
		_ = File.exists(FilePath from "noyield.txt")
		if succeed 'ok'
				return 20
		end 'ok'
		throw TestError.failed
end 'mayFail'

function outer() returns Integer
		let good = async mayFail(true)
		let bad = async mayFail(false)
		let a = try await good otherwise 0
		let b = try await bad otherwise 5
		return a + b
end 'outer'

function main() returns ExitCode
		let q = async outer()
		return await q
end 'main'
```
```exitcode
25
```

<!-- test: async-await.nested-sleep -->
The nested thread parks on the TIMER heap rather than on an I/O registration.
`__gt_timer_check`'s park gate reads the same off-stack flag the I/O completer
does, so nesting has to keep that flag honest for the timer path too.
```maxon
typealias Integer = int(i64.min to i64.max)

function leaf(n Integer) returns Integer
		sleep(5)
		return n
end 'leaf'

function outer() returns Integer
		let p1 = async leaf(9)
		return await p1
end 'outer'

function main() returns ExitCode
		let q = async outer()
		return await q
end 'main'
```
```exitcode
9
```

<!-- test: async-await.try-await.drives-the-timer-heap -->
A `try await` of a coroutine that sleeps costs what an `await` of it costs. The two
are ONE body (`buildGtAwait`, whose `throwing` flag selects only the exit): both
park the caller and neither fires a timer itself — the `sleep()` deadline is fired
by a machine's scheduler loop, whose idle park is bounded by the earliest deadline
(`SchedRuntime.emitSchedParkTimeout`). What this case pins
is that the `try` road waits on that same deadline and not on some longer bound.

A coroutine asleep without end (`ForeverMs`) holds the far end of the timer heap for the whole case, so a park
bounded by anything but the earliest deadline never returns and the runner reports the case TIMED OUT. The
plain `await` rounds are the control in the same process, and no elapsed time is compared to a threshold, so
the case does not depend on how fast the host is.
```maxon
typealias Integer = int(i64.min to i64.max)

enum LeafError implements Error
		failed
end 'LeafError'

function timedThrowing() returns Integer throws LeafError
		if File.exists(FilePath from "noyield.txt") 'present'
				throw LeafError.failed
		end 'present'

		sleep(10)
		return 1
end 'timedThrowing'

function timedPlain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		sleep(10)
		return 1
end 'timedPlain'

function foreverAsleep() returns Integer
		sleep(ForeverMs)
		return 1
end 'foreverAsleep'

function main() returns ExitCode
		let holder = async foreverAsleep()
		Scheduler.yield()

		var plain = 0 as Integer
		for _ in 0 upto 6 'plainRound'
				let p = async timedPlain()
				plain = plain + (await p)
		end 'plainRound'

		var thrown = 0 as Integer
		for _ in 0 upto 6 'tryRound'
				let p = async timedThrowing()
				thrown = thrown + (try await p otherwise 0)
		end 'tryRound'

		print("plain={plain} thrown={thrown} held={not holder.isComplete()}\n")
		return 0
end 'main'
```
```stdout
plain=6 thrown=6 held=true
```

### An `async` Whose Result Is a MANAGED GENERIC INSTANCE

`returns StringArray` is a GENERIC-ALIAS return, and the whole-program sweep records it as a bare
`named("StringArray")` because it runs before the aliases are known (`parseTypeReference`'s
generic-alias arm is gated on `allFilesFolded`). An ordinary call's result is repaired at the READ
door (`repairSweptType`, at `mintOwnedCallResult`); an AWAIT road that read the same registry entry
unrepaired would bind a value tagged `named` under a name no reader can resolve —
`E3011: Unknown type 'StringArray'` at the first member call on it.

⚠ **THE REPAIR COVERS BOTH HALVES: A RANGED ALIAS AND A GENERIC INSTANCE.** `awaitedPromiseFacts`
answers a TYPE rather than a bare TAG, which is what a ranged-alias result — the control this case
is paired with — needs. The repair happens at `calleeReturnType`, the ONE registry read both async result questions are
answered from, so the await's result type, the spawn's carried result tag and the promise-STORAGE
agreement check cannot come to disagree about what the callee returns.

<!-- test: async-await.managed-generic-result -->
```maxon
enum DrainError implements Error
	broken
end 'DrainError'

function drainThunk() returns StringArray throws DrainError
	if File.exists(FilePath from "noyield.txt") 'present'
		throw DrainError.broken
	end 'present'

	var lines = StringArray.create()
	lines.push("one")
	lines.push("two")
	return lines
end 'drainThunk'

function main() returns ExitCode
	let p = async drainThunk()
	let lines = try await p otherwise 'died'
		return 1
	end 'died'
	print("{lines.count()} {try lines.get(0) otherwise "?"}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 one
```

### The Same Result Through an Explicitly Named Promise Storage Type

`SpecWorkerPool`'s own spelling: `typealias DrainPromise = Promise with (StringArray, SubprocessError)`,
stored in an array so the spawn is BOXED and the await is described by the storage type rather than by
a reachable `asyncCall`. The agreement check between the storage's claimed result and the callee's
declared one compares a REPAIRED `claimed` (through `resolvedSlotType`) against a REPAIRED
`returned`. With `returned` unrepaired it would report the storage naming `StringArray` while "this
promise's function returns 'int'" — a sentence naming a type the program does not contain.

<!-- test: async-await.managed-generic-result-through-promise-storage -->
```maxon
enum DrainError implements Error
	broken
end 'DrainError'

typealias DrainPromise = Promise with (StringArray, DrainError)
typealias DrainPromiseArray = Array with DrainPromise

function drainThunk(n Integer) returns StringArray throws DrainError
	_ = File.exists(FilePath from "noyield.txt")
	if n < 0 'bad'
		throw DrainError.broken
	end 'bad'
	var lines = StringArray.create()
	lines.push("one")
	lines.push("two")
	return lines
end 'drainThunk'

function main() returns ExitCode
	var promises = DrainPromiseArray.create()
	promises.push(async drainThunk(1))
	promises.push(async drainThunk(2))
	var total = 0 as Count
	for p in promises 'each'
		view lines = try await p otherwise 'died'
			continue
		end 'died'
		total = total + lines.count()
	end 'each'
	print("{total}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
4
```

### A Ranged-Alias Result Is the Control

The same program with a ranged-int result. It is here because it is what proves the cases above pin
the GENERIC-INSTANCE half of the swept-type repair and not `async` losing its result type generally —
a repair that broke this spelling would be repairing the wrong thing. A ranged
alias deliberately keeps its `named` tag through the repair (`repairSweptType` resolves a struct, a
float alias, a function alias, a generic alias and a tuple alias — never a ranged int), which is what
keeps the alias name available to every downstream range check.

<!-- test: async-await.ranged-alias-result-control -->
```maxon
typealias Tally = int(0 to 1000)

enum DrainError implements Error
	broken
end 'DrainError'

function countThunk() returns Tally throws DrainError
	if File.exists(FilePath from "noyield.txt") 'present'
		throw DrainError.broken
	end 'present'

	return 2
end 'countThunk'

function main() returns ExitCode
	let p = async countThunk()
	let n = try await p otherwise 'died'
		return 1
	end 'died'
	print("{n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

### A Qualified Spawn Names a `static` a Function Declares, and Nothing Else

`async Type.staticMethod(…)` is the one qualified shape a spawn accepts, because a spawn hands the
scheduler the ADDRESS of a linked function and a static call is an ordinary call with a mangled name.
Every other reading of `<base>.<member>(…)` reaches the same door and is refused there in words: a
target that is not a linked function is a link failure or a wrong answer at run time rather than a
diagnostic.

<!-- test: async-await.error.qualified-spawn-compiler-owned-static-outside-the-library-tiers -->
Outside the library tiers the raw type's name is refused first, at the type the spawn names.
```maxon
function main() returns ExitCode
	let p = async __ManagedFile.readAll("x")
	return await p
end 'main'
```
```maxoncstderr
error E3185: <fragment>:3:16: '__ManagedFile' named outside the library tiers: the raw OS file handle type is nameable only from a source file under stdlib/ or runtime/ — use 'File'
```

<!-- test: async-await.error.qualified-spawn-compiler-owned-static -->
<!-- tier: stdlib -->
A static on a compiler-owned surface is served by a runtime entry the compiler emits, so there is no
declared function whose address the scheduler could be handed. The roster (`__ManagedFile`,
`__ManagedDirectory`, `__ManagedSocket`, `__ManagedMemory`, a container alias) shares this one refusal
because it shares one reason.
```maxon
function main() returns ExitCode
	let p = async __ManagedFile.openRead("x")
	return await p
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:16: Unsupported: `async __ManagedFile.…` — a static on this type is served by a runtime entry the compiler emits rather than by a function the program declares, and a spawn hands the scheduler the ADDRESS of a linked function. Call it directly, or spawn a function of your own that wraps it
```

<!-- test: async-await.error.qualified-spawn-compiler-owned-static-it-lacks -->
<!-- tier: stdlib -->
The spawn reads the member through the one resolution a call reads it through, so a static the surface does
not have is refused for that, before the spawn is.
```maxon
function main() returns ExitCode
	let p = async __ManagedFile.readAll("x")
	return await p
end 'main'
```
```maxoncstderr
error E2073: <fragment>:3:30: `__ManagedFile` static 'readAll' — the type has exactly these ten: `openRead`, `openWrite`, `openWriteExecutable`, `openCreateNew`, `exists`, `delete`, `rename`, `stat`, `statField` and `statFree`
```

<!-- test: async-await.error.qualified-spawn-container-alias-static -->
A container alias reaches the same refusal through the same roster: `TallyArray.create()` lowers to an
`Array` create op, not to a call to a function the program declares.
```maxon
typealias Tally = int(0 to 1000)
typealias TallyArray = Array with Tally

function main() returns ExitCode
	let p = async TallyArray.create()
	return await p
end 'main'
```
```maxoncstderr
error E2015: specs/async-await/async-await.error.qualified-spawn-container-alias-static.maxon:6:16: Unsupported: `async TallyArray.…` — a static on this type is served by a runtime entry the compiler emits rather than by a function the program declares, and a spawn hands the scheduler the ADDRESS of a linked function. Call it directly, or spawn a function of your own that wraps it
```

<!-- test: async-await.qualified-spawn-primitive-static -->
A primitive's static spawns like any other: `async int.fromString(s)` hands the scheduler the
compiler-minted `__int_fromString`, and the spawn carries that mint as a direct call does, so every
reader downstream judges it as the compiler's name, not one the author wrote.
```maxon
function main() returns ExitCode
	let s = "41"
	let p = async int.fromString(s)
	let n = try await p otherwise 0
	return (n + 1) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-await.qualified-spawn-primitive-static-throws-through-the-promise -->
The spawned static's error reaches the awaiting caller: `"x"` does not parse, so `try await` takes the
`otherwise` arm.
```maxon
function main() returns ExitCode
	let p = async int.fromString("x")
	let n = try await p otherwise 7
	return n as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: async-await.error.qualified-spawn-instance-receiver -->
A base bound to a VALUE claims the whole expression before any type reading, and an instance spawn has
nowhere to put the receiver: the arguments ride the green thread's inline argument region, which the
trampoline reads back into the ABI argument registers, and nothing there carries a receiver or its
ownership.
```maxon
typealias Tally = int(0 to 1000)

type Counter
	export var n as Tally

	static function create() returns Counter
		return Counter{n: 0}
	end 'create'

	function bump() returns Tally
		self.n = self.n + 1
		return self.n
	end 'bump'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	let p = async c.bump()
	return await p
end 'main'
```
```maxoncstderr
error E2015: specs/async-await/async-await.error.qualified-spawn-instance-receiver.maxon:19:16: Unsupported: `async c.…` spawns a method on a VALUE, and a spawn has no receiver: its arguments ride the green thread's inline argument region, which the hand-assembled trampoline reads back into the ABI argument registers, and nothing there carries a receiver or its ownership. Spawn a free function or a `Type.staticMethod(…)` that takes the value as an argument
```

<!-- test: async-await.error.qualified-spawn-union-case -->
`Move.walk(3)` CONSTRUCTS a union case and calls nothing, so there is no address to spawn. A payload
construct and a static call are the same shape to the token, which is why the enum reading is asked
before the static one.
```maxon
typealias Tally = int(0 to 1000)

union Move
	walk(steps Tally)
	stop
end 'Move'

function main() returns ExitCode
	let p = async Move.walk(3)
	return await p
end 'main'
```
```maxoncstderr
error E2015: specs/async-await/async-await.error.qualified-spawn-union-case.maxon:10:16: Unsupported: `async Move.…` — `Move` is an enum or union, so `Move.<case>(…)` builds a case rather than calling a function. There is nothing there for a green thread to run
```

<!-- test: async-await.error.qualified-spawn-generic-instance-constructor -->
An ordinary call retypes a generic-instance constructor's result to the instance the alias fixes; a
spawn's result is the PROMISE, whose argument comes from the callee's DECLARED return — the generic
BASE. There is no promise-side substitution to make, so the await would hand back a value at the wrong
type.
```maxon
type Box uses T
	export var value as T

	static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'

typealias StrBox = Box with String

function main() returns ExitCode
	let p = async StrBox.create("x")
	return await p
end 'main'
```
```maxoncstderr
error E2015: specs/async-await/async-await.error.qualified-spawn-generic-instance-constructor.maxon:13:23: Unsupported: `async StrBox.create(…)` — `StrBox` names a generic INSTANCE, and the promise a spawn mints is typed from the callee's declared return, which names the generic base rather than the instance this alias fixes. Awaiting it would hand back a value at the wrong type. Call it directly
```

<!-- test: async-await.error.an-async-chain-naming-nothing-is-refused -->
A chain of any length is read the way a direct call reads it. Nothing declares `lib`, so `lib.inner`
names no binding, directory or type, and the spawn is refused as the same call written without `async`
would be.
```maxon
function main() returns ExitCode
	let p = async lib.inner.deep()
	return await p
end 'main'
```
```maxoncstderr
error E3004: specs/async-await/async-await.error.an-async-chain-naming-nothing-is-refused.maxon:3:26: call to undefined function 'lib.inner.deep': nothing declares a binding, directory or type `lib.inner`
```

<!-- test: async-await.error.a-spawn-of-an-undeclared-function-that-is-never-awaited-is-undefined -->
The refusal is the call's, whether or not anything awaits the handle.
```maxon
function main() returns ExitCode
	let p = async nosuch(1)
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:16: call to undefined function 'nosuch'
```

<!-- test: async-await.error.a-try-await-of-an-undeclared-spawn-is-undefined -->
A `try await` over the handle of a spawn nothing declares reports the call, not the await.
```maxon
function main() returns ExitCode
	let p = async nosuch(1)
	return try await p otherwise 1
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:16: call to undefined function 'nosuch'
```

<!-- test: async-await.error.an-await-of-an-undeclared-spawn-written-in-place-is-undefined -->
Awaiting the spawn where it is written reports the call as well.
```maxon
function main() returns ExitCode
	return await async nosuch(1)
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:21: call to undefined function 'nosuch'
```

<!-- test: async-await.error.a-try-await-of-a-call-nothing-declares-is-undefined -->
A `try await` over a call nothing declares reports the call.
```maxon
function main() returns ExitCode
	return try await nosuch(1) otherwise 1
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:19: call to undefined function 'nosuch'
```

<!-- test: async-await.error.a-try-await-of-a-binding-of-a-call-nothing-declares-is-undefined -->
A `try await` over a binding of a call nothing declares reports the call.
```maxon
function main() returns ExitCode
	let v = nosuch(1)
	return try await v otherwise 1
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'nosuch'
```

<!-- test: async-await.error.a-spawn-of-nothing-stored-in-a-promise-array-is-undefined -->
A spawn nothing declares, stored in a promise array and awaited from it, reports the call.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function main() returns ExitCode
	var promises = IntPromiseArray.create()
	promises.push(async nosuch(1))

	for p in promises 'each'
		_ = await p
	end 'each'

	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:8:22: call to undefined function 'nosuch'
```

<!-- test: async-await.error.a-promise-var-rearmed-with-a-spawn-of-nothing-is-undefined -->
Re-arming a promise var with a spawn nothing declares reports that call, and nothing at the assignment it
feeds or at the later await.
```maxon
typealias Integer = int(i64.min to i64.max)

function f(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'f'

function main() returns ExitCode
	var p = async f(1)
	_ = await p
	p = async nosuch(2)
	let v = await p
	print("{v}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:12:12: call to undefined function 'nosuch'
```

<!-- test: async-await.error.a-promise-var-rearmed-on-one-branch-with-a-spawn-of-nothing-is-undefined -->
The same re-arm on one arm of a branch reports the call, and nothing at the assignment or the merge.
```maxon
typealias Integer = int(i64.min to i64.max)

function f(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'f'

function main() returns ExitCode
	var p = async f(1)
	let n = f(2)

	if n == 2 'pick'
		_ = await p
		p = async nosuch(2)
	end 'pick' else 'other'
		_ = await p
		p = async f(3)
	end 'other'

	let v = await p
	print("{v}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:15:13: call to undefined function 'nosuch'
```

<!-- test: async-await.error.a-moved-string-var-assigned-a-call-nothing-declares-is-undefined -->
A `var` whose value moved away and is assigned a call nothing declares holds that unknown value, so reading
it reports nothing beyond the call.
```maxon
function main() returns ExitCode
	var s = "a string padded out long enough to heap allocate"
	let t = s
	s = nosuch(1)
	print("{s}{t}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:5:6: call to undefined function 'nosuch'
```

<!-- test: async-await.error.a-moved-struct-var-assigned-a-call-nothing-declares-is-undefined -->
A struct `var` whose value moved away and is assigned a call nothing declares holds that unknown value, so
reading it reports nothing beyond the call.
```maxon
type Box
	export var name as String

	static function create() returns Self
		return Self{name: "a string padded out long enough to heap allocate"}
	end 'create'
end 'Box'

function main() returns ExitCode
	var box = Box.create()
	let other = box
	box = nosuch(1)
	print("{box.name}{other.name}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:13:8: call to undefined function 'nosuch'
```

<!-- test: async-await.error.a-tuple-assignment-of-a-call-nothing-declares-to-a-moved-var-is-undefined -->
A tuple assignment gives a moved `var` the same unknown value a plain assignment does.
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var s = "a string padded out long enough to heap allocate"
	var n = 0 as Integer
	let t = s
	(s, n) = nosuch(1)
	print("{s}{t}{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:8:11: call to undefined function 'nosuch'
```

<!-- test: async-await.error.a-captured-var-assigned-a-call-nothing-declares-is-undefined -->
A cell-resident `var` assigned a call nothing declares reports the call and nothing at the closure's read.
```maxon
typealias Integer = int(i64.min to i64.max)

function fill(dest String, n Integer)
	dest = "filled {n} padded out long enough to heap allocate"
end 'fill'

function main() returns ExitCode
	var word = "hello padded out long enough to heap allocate"
	fill(word, n: 2)
	let peek = function() gives word.byteLength()
	word = nosuch(1)
	print("{peek()} {word}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:12:9: call to undefined function 'nosuch'
```

<!-- test: async-await.error.a-field-and-an-element-assigned-a-call-nothing-declares-are-undefined -->
A field store and an element store of a call nothing declares report each call, and nothing at the store it
feeds or at the reads.
```maxon
typealias NameArray = Array with String

type Box
	export var name as String

	static function create() returns Self
		return Self{name: "a string padded out long enough to heap allocate"}
	end 'create'
end 'Box'

function main() returns ExitCode
	var box = Box.create()
	box.name = nosuch(1)
	var names = NameArray.create()
	names.push(nosuch(2))
	print("{box.name}{names.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:14:13: call to undefined function 'nosuch'
error E3004: <fragment>:16:13: call to undefined function 'nosuch'
```

<!-- test: async-await.error.a-call-nothing-declares-stored-in-a-promise-array-is-undefined -->
A call nothing declares, stored in a promise array without `async`, reports the call and nothing about
what the array may hold.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function main() returns ExitCode
	var ps = IntPromiseArray.create()
	ps.push(nosuch(1))
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:8:10: call to undefined function 'nosuch'
```

<!-- test: async-await.error.a-captured-struct-var-assigned-a-call-nothing-declares-is-undefined -->
A cell-resident struct `var` assigned a call nothing declares reports the call, and nothing at the
assignment or at the closure's read through it.
```maxon
type Box
	export var name as String

	static function create() returns Self
		return Self{name: "a string padded out long enough to heap allocate"}
	end 'create'
end 'Box'

function refill(dest Box)
	dest = Box.create()
end 'refill'

function main() returns ExitCode
	var box = Box.create()
	refill(box)
	let peek = function() gives box.name.byteLength()
	box = nosuch(1)
	print("{peek()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:18:8: call to undefined function 'nosuch'
```

<!-- test: async-await.qualified-spawn-of-a-directory-types-static -->
A static of a type declared in another directory is spawned by its qualified name, `sub.Calc.seven`, the
spelling a direct call accepts.
```maxon
// --- file: sub/calc.maxon
export type Calc
	export static function seven() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		return 7
	end 'seven'
end 'Calc'

export typealias Integer = int(i64.min to i64.max)
// --- file: main.maxon
function main() returns ExitCode
	let n = await async sub.Calc.seven()
	return n as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: async-await.qualified-spawn-of-a-nested-directory-free-function -->
A free function two directories down is spawned through its whole qualifier, `outer.inner.six`.
```maxon
// --- file: outer/inner/math.maxon
export function six() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return 6
end 'six'

export typealias Integer = int(i64.min to i64.max)
// --- file: main.maxon
function main() returns ExitCode
	let n = await async outer.inner.six()
	return n as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: async-await.qualified-spawn-namespace-free-call -->
The ACCEPT case, and the one that proves the refusals above are a boundary rather than a blanket. A
namespace qualifier is a NAME LOOKUP resolving to a free function the program declares, so the spawn
has exactly what it needs: a linked address and no receiver.
```maxon
// --- file: lib/api.maxon
export typealias Tally = int(0 to 125)

export function bump(v Tally) returns Tally
	_ = File.exists(FilePath from "noyield.txt")
	return v + 1
end 'bump'

// --- file: main.maxon
function main() returns ExitCode
	let p = async lib.bump(41)
	return await p
end 'main'
```
```exitcode
42
```

<!-- test: two-directories-each-spawn-their-own-file-private-function-of-one-name -->
Each directory spawns its own file-private `slowTally`, and each spawn binds the declaration its own file holds.
```maxon
// --- file: a/a.maxon
typealias Tally = int(0 to 100)

function slowTally(n Tally) returns Tally
	sleep(1)
	return n
end 'slowTally'

export function runA()
	let p = async slowTally(1)
	print("a {await p}\n")
end 'runA'

// --- file: b/b.maxon
typealias Tally = int(0 to 100)

function slowTally(n Tally) returns Tally
	sleep(1)
	return n + 10
end 'slowTally'

export function runB()
	let p = async slowTally(2)
	print("b {await p}\n")
end 'runB'

// --- file: main.maxon
function main() returns ExitCode
	runA()
	runB()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a 1
b 12
```

<!-- test: two-directories-each-spawn-their-own-file-private-function-of-one-name-and-different-signatures -->
The same pair with different parameter lists: each spawn still binds its own file's declaration.
```maxon
// --- file: a/a.maxon
typealias Tally = int(0 to 100)

function fetch(n Tally) returns Tally
	sleep(1)
	return n + 1
end 'fetch'

export function runA()
	let p = async fetch(1)
	print("a {await p}\n")
end 'runA'

// --- file: b/b.maxon
typealias Tally = int(0 to 100)

function fetch(n Tally, extra Tally) returns Tally
	sleep(1)
	return n + extra
end 'fetch'

export function runB()
	let p = async fetch(2, extra: 5)
	print("b {await p}\n")
end 'runB'

// --- file: main.maxon
function main() returns ExitCode
	runA()
	runB()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a 2
b 7
```

<!-- test: an-awaited-range-bound-in-a-for-source -->
A non-throwing `await` is a `for` range's lower bound. It is an ordinary operand: awaited once, and the loop
counts up from the value it hands back.
```maxon
typealias Integer = int(i64.min to i64.max)

function start() returns Integer
	Scheduler.yield()
	return 2
end 'start'

function main() returns ExitCode
	let p = async start()
	var total = 0 as Integer

	for i in await p upto 5 'each'
		total = total + i
	end 'each'

	return total as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: an-awaited-collection-as-a-for-source -->
The collection twin: the awaited value IS the collection the loop walks.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function items() returns IntArray
	Scheduler.yield()
	var found = IntArray.create()
	found.push(4)
	found.push(5)
	return found
end 'items'

function main() returns ExitCode
	let q = async items()
	var total = 0 as Integer

	for x in await q 'each'
		total = total + x
	end 'each'

	return total as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: an-awaited-range-bound-in-a-for-source-inside-a-try-block -->
The try-block twin: the loop sits in a block-form `try` body whose own throwing call is routed to the block's
handler, and the non-throwing `await` in the loop's source stays an ordinary operand.
```maxon
typealias Integer = int(i64.min to i64.max)

enum CheckError implements Error
	tooBig
end 'CheckError'

function start() returns Integer
	Scheduler.yield()
	return 2
end 'start'

function check(n Integer) returns Integer throws CheckError
	if n > 100 'big'
		throw CheckError.tooBig
	end 'big'

	return n
end 'check'

function main() returns ExitCode
	let p = async start()
	var total = 0 as Integer

	try 'work'
		for i in await p upto 5 'each'
			total = total + i
		end 'each'

		total = check(total)
	end 'work' otherwise (e) 'h'
		match e 'k'
			tooBig then total = 0
		end 'k'
	end 'h'

	return total as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: async-await.a-string-read-after-an-async-spawn-is-shared -->
A managed `var` handed to `async` is shared with the coroutine, so the caller reads it again after the spawn.
```maxon
typealias Integer = int(i64.min to i64.max)

function shout(text String) returns Integer
	Scheduler.yield()
	print("{text}!\n")
	return 1
end 'shout'

function main() returns ExitCode
	var word = "hello padded out long enough to heap allocate {1}"
	let p = async shout(word)
	print("{word}\n")
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello padded out long enough to heap allocate 1
hello padded out long enough to heap allocate 1!
1
```

<!-- test: async-await.a-let-read-after-an-async-spawn-is-shared -->
A managed `let` handed to `async` is shared with the coroutine exactly as a `var` is.
```maxon
typealias Integer = int(i64.min to i64.max)

function shout(text String) returns Integer
	Scheduler.yield()
	print("{text}!\n")
	return 1
end 'shout'

function main() returns ExitCode
	let word = "hello padded out long enough to heap allocate {1}"
	let p = async shout(word)
	print("{word}\n")
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello padded out long enough to heap allocate 1
hello padded out long enough to heap allocate 1!
1
```

<!-- test: async-await.a-let-shared-with-async-is-freed-once-by-the-coroutine -->
The coroutine holds a count of its own on the shared `let` and releases it once, after the caller's frame has
moved on.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function shout(text String) returns Integer
	Scheduler.yield()
	print("{text}!\n")
	return text.byteLength() as Integer
end 'shout'

function spawnIt() returns IntPromise
	let word = "hello padded out long enough to heap allocate {1}"
	return async shout(word)
end 'spawnIt'

function main() returns ExitCode
	let p = spawnIt()
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello padded out long enough to heap allocate 1!
47
```

<!-- test: async-await.error.a-let-handed-to-async-cannot-be-written-by-the-coroutine -->
A `let` handed to `async` is shared read-only, so a target that writes its parameter may not be handed it.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

function poke(p Box) returns Integer
	Scheduler.yield()
	p.n = p.n + 41
	return p.n
end 'poke'

function main() returns ExitCode
	let b = Box.create()
	let pending = async poke(b)
	return await pending as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:20:22: cannot pass 'b', which is read-only (a `let` binding), to a function that writes parameter 'p' (in main); pass a clone()
```

<!-- test: async-await.a-var-handed-to-async-may-be-written-by-the-coroutine -->
A `var` handed to `async` is shared with the coroutine, so a target that writes its parameter writes the record the
caller's binding still holds.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Box'

function poke(p Box) returns Integer
	Scheduler.yield()
	p.n = p.n + 41
	return p.n
end 'poke'

function main() returns ExitCode
	var b = Box.create()
	let pending = async poke(b)
	let written = await pending

	return (written + b.n) as ExitCode
end 'main'
```
```exitcode
84
```

<!-- test: async-await.a-cell-resident-var-handed-to-async-is-shared -->
`word` lives in a cell because `fill` reassigns its parameter; the coroutine shares the cell's contents.
```maxon
typealias Integer = int(i64.min to i64.max)

function shout(text String) returns Integer
	Scheduler.yield()
	print("{text}!\n")
	return text.byteLength() as Integer
end 'shout'

function fill(dest String, n Integer)
	dest = "filled {n} padded out long enough to heap allocate"
end 'fill'

function main() returns ExitCode
	var word = "hello padded out long enough to heap allocate {1}"
	fill(word, n: 2)
	let p = async shout(word)
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
filled 2 padded out long enough to heap allocate!
48
```

<!-- test: async-await.a-cell-resident-var-read-after-an-async-spawn-is-shared -->
The same cell-resident `var`, read after the spawn.
```maxon
typealias Integer = int(i64.min to i64.max)

function shout(text String) returns Integer
	Scheduler.yield()
	print("{text}!\n")
	return text.byteLength() as Integer
end 'shout'

function fill(dest String, n Integer)
	dest = "filled {n} padded out long enough to heap allocate"
end 'fill'

function main() returns ExitCode
	var word = "hello padded out long enough to heap allocate {1}"
	fill(word, n: 2)
	let p = async shout(word)
	print("{word}\n")
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
filled 2 padded out long enough to heap allocate
filled 2 padded out long enough to heap allocate!
48
```

<!-- test: async-await.a-var-reassigned-after-a-closure-captured-it-is-handed-to-async -->
The closure owns the value it captured, so the `var` reassigned afterwards holds a new value of its own, and the
coroutine shares that value while the closure goes on reading the first.
```maxon
typealias Integer = int(i64.min to i64.max)

function shout(text String) returns Integer
	Scheduler.yield()
	print("{text}!\n")
	return 1
end 'shout'

function main() returns ExitCode
	var word = "hello padded out long enough to heap allocate {0}"
	let peek = function() gives word.byteLength()
	word = "second padded out long enough to heap allocate {1}"
	let p = async shout(word)
	print("{await p} {peek()}\n")
	return 0
end 'main'
```
```stdout
second padded out long enough to heap allocate 1!
1 47
```

<!-- test: async-await.error.a-cell-resident-var-a-closure-only-reads-cannot-be-handed-to-async -->
A closure that only READS a cell-resident `var` takes the cell's occupant, so handing the `var` to the
coroutine afterwards is a use after move.
```maxon
typealias Integer = int(i64.min to i64.max)

function shout(text String) returns Integer
	Scheduler.yield()
	print("{text}!\n")
	return text.byteLength() as Integer
end 'shout'

function opening() returns (String, Integer)
	return ("hello padded out long enough to heap allocate {1}", 1)
end 'opening'

function fill(dest String, n Integer)
	dest = "filled {n} padded out long enough to heap allocate"
end 'fill'

function main() returns ExitCode
	var (word, _) = opening()
	fill(word, n: 2)
	let peek = function() gives word.byteLength()
	let p = async shout(word)
	print("{await p} {peek()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:22:22: use of moved value 'word': its ownership moved to the closure that captures it
```

<!-- test: async-await.error.a-cell-resident-var-read-after-a-nested-closure-cannot-be-handed-to-async -->
The outer closure reads `word` after a nested closure has ended, so it still captures `word`, and handing the
`var` to the coroutine afterwards is a use after move.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function run(thunk Thunk) returns Integer
	return thunk()
end 'run'

function shout(text String) returns Integer
	Scheduler.yield()
	print("{text}!\n")
	return text.byteLength() as Integer
end 'shout'

function opening() returns (String, Integer)
	return ("hello padded out long enough to heap allocate {1}", 1)
end 'opening'

function fill(dest String, n Integer)
	dest = "filled {n} padded out long enough to heap allocate"
end 'fill'

function main() returns ExitCode
	var (word, _) = opening()
	fill(word, n: 2)
	let peek = function() gives run(function() gives 0) + (word.byteLength() as Integer)
	let p = async shout(word)
	print("{await p} {peek()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:22: use of moved value 'word': its ownership moved to the closure that captures it
```

<!-- test: async-await.error.a-cell-resident-var-read-after-a-nested-closure-across-lines-cannot-be-handed-to-async -->
The nested closure's body ends at its own line break, and the outer closure's body goes on past it to read
`word`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function run(thunk Thunk) returns Integer
	return thunk()
end 'run'

function shout(text String) returns Integer
	Scheduler.yield()
	print("{text}!\n")
	return text.byteLength() as Integer
end 'shout'

function opening() returns (String, Integer)
	return ("hello padded out long enough to heap allocate {1}", 1)
end 'opening'

function fill(dest String, n Integer)
	dest = "filled {n} padded out long enough to heap allocate"
end 'fill'

function main() returns ExitCode
	var (word, _) = opening()
	fill(word, n: 2)
	let peek = function() gives [
		run(function() gives 0),
		word.byteLength() as Integer
	]
	let p = async shout(word)
	print("{await p} {peek().count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:22: use of moved value 'word': its ownership moved to the closure that captures it
```

<!-- test: async-await.a-let-whose-clone-a-closure-captured-is-handed-to-async -->
The closure owns the clone it captured, so `b` is shared with the coroutine and `peek` still reads its own record.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

function readIt(b Box) returns Integer
	Scheduler.yield()
	return b.n
end 'readIt'

function main() returns ExitCode
	let b = Box.create()
	let kept = b.clone()
	let peek = function() gives kept.n
	let p = async readIt(b)
	let r = await p
	return (r + peek()) as ExitCode
end 'main'
```
```exitcode
14
```

<!-- test: async-await.a-var-whose-clone-a-closure-captured-is-handed-to-async -->
The same capture of a clone of a `var` that stays out of a cell.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

function readIt(b Box) returns Integer
	Scheduler.yield()
	return b.n
end 'readIt'

function boxed() returns (Box, Integer)
	return (Box.create(), 1)
end 'boxed'

function main() returns ExitCode
	var (b, _) = boxed()
	let kept = b.clone()
	let peek = function() gives kept.n
	let p = async readIt(b)
	let r = await p
	return (r + peek()) as ExitCode
end 'main'
```
```exitcode
14
```

<!-- test: async-await.one-let-handed-twice-to-one-async-call-is-shared -->
Both arguments name the one `let` `b`: the coroutine shares the record under both parameters.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

function pair(a Box, other Box) returns Integer
	Scheduler.yield()
	return a.n + other.n
end 'pair'

function main() returns ExitCode
	let b = Box.create()
	let p = async pair(b, other: b)
	return (await p) as ExitCode
end 'main'
```
```exitcode
14
```

<!-- test: async-await.a-let-and-its-alias-handed-to-one-async-call-are-shared -->
`c` names the record `b` holds, and both are read-only, so the coroutine shares the record under both parameters.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

function pair(a Box, other Box) returns Integer
	Scheduler.yield()
	return a.n + other.n
end 'pair'

function main() returns ExitCode
	let b = Box.create()
	let c = b
	let p = async pair(b, other: c)
	return (await p) as ExitCode
end 'main'
```
```exitcode
14
```

<!-- test: async-await.error.a-match-payload-of-a-let-union-cannot-be-written-by-a-coroutine -->
`b` is bound out of `w`, which is a `let`, so `b` is read-only and a coroutine that writes its parameter may not
be handed it.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

union Wrap
	held(box Box)
	empty
end 'Wrap'

function poke(p Box) returns Integer
	Scheduler.yield()
	p.n = 99
	return p.n
end 'poke'

function main() returns ExitCode
	let w = Wrap.held(Box.create())

	match w 'w'
		held(b) then print("{await async poke(b)}\n")
		empty then print("empty\n")
	end 'w'

	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:27:36: cannot pass 'b', which is read-only (a `match` binding), to a function that writes parameter 'p' (in main); pass a clone()
```

<!-- test: async-await.error.an-element-the-array-still-holds-cannot-be-written-by-a-coroutine -->
`a` is a `let` holding an element `items` still holds, so `a` is read-only and a coroutine that writes its
parameter may not be handed it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Boxes = Array with Box

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

function poke(p Box) returns Integer
	Scheduler.yield()
	p.n = 99
	return p.n
end 'poke'

function main() returns ExitCode
	var items = Boxes.create()
	items.push(Box.create())
	view a = try items.get(0) otherwise panic("no box")
	print("{await async poke(a)}\n")
	print("{items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:23:22: cannot pass 'a', which is read-only (a `view`), to a function that writes parameter 'p' (in main); pass a clone()
```

<!-- test: async-await.a-counted-co-owner-bound-to-a-let-is-shared-with-async -->
`first` hands back an element `items` still holds, so `a` holds its own reference to it: the coroutine counts a
reference of its own and the array keeps its own.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Boxes = Array with Box

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

function first(items Boxes) returns Box
	return try items.get(0) otherwise panic("no box")
end 'first'

function show(p Box) returns Integer
	Scheduler.yield()
	return p.n
end 'show'

function main() returns ExitCode
	var items = Boxes.create()
	items.push(Box.create())
	view a = first(items)
	print("{await async show(a)}\n")
	view kept = try items.get(0) otherwise panic("no box")
	print("{items.count()} {kept.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
1 7
```

<!-- test: async-await.a-counted-co-owner-shared-with-async-is-read-after-the-spawn -->
The same `a`, read after the spawn counted its own reference.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Boxes = Array with Box

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

function first(items Boxes) returns Box
	return try items.get(0) otherwise panic("no box")
end 'first'

function show(p Box) returns Integer
	Scheduler.yield()
	return p.n
end 'show'

function main() returns ExitCode
	var items = Boxes.create()
	items.push(Box.create())
	view a = first(items)
	print("{await async show(a)}\n")
	print("{a.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
7
```

<!-- test: async-await.a-match-payload-the-union-still-holds-is-shared-with-a-reading-coroutine -->
`w` is a `var`, so matching it leaves its payload in place and `b` holds its own reference to the record the
union still holds: a coroutine that only reads it counts a reference of its own.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

union Wrap
	held(box Box)
	empty
end 'Wrap'

function show(p Box) returns Integer
	Scheduler.yield()
	return p.n
end 'show'

function main() returns ExitCode
	var w = Wrap.empty
	w = Wrap.held(Box.create())

	match w 'first'
		held(b) then print("{await async show(b)}\n")
		empty then print("empty\n")
	end 'first'

	match w 'second'
		held(b) then print("{b.n}\n")
		empty then print("empty\n")
	end 'second'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
7
```

<!-- test: async-await.error.an-immutable-global-returned-by-a-call-cannot-be-written-by-a-coroutine -->
`Box.get()` hands back the record the module-level `let G` holds, so a coroutine that writes its parameter may
not be handed it, exactly as the direct call `poke(Box.get())` may not.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'

	static function get() returns Box
		return G
	end 'get'
end 'Box'

let G = Box.create()

function poke(p Box) returns Integer
	Scheduler.yield()
	p.n = 99
	return p.n
end 'poke'

function main() returns ExitCode
	print("{await async poke(Box.get())}\n")
	print("{G.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:25:22: cannot pass the result of 'get', which may share storage with a module-level `let`, to a function that writes parameter 'p' (in main); pass a clone()
```

<!-- test: async-await.error.a-let-naming-an-immutable-global-is-handed-to-neither-a-reassigning-function-nor-a-coroutine -->
`b` is a `let` naming the record the module-level `let G` holds, so it is read-only: neither `bump`,
which reassigns its parameter, nor a coroutine that writes its parameter may be handed it.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

let G = Box.create()

function bump(p Box) returns Integer
	if p.n > 100 'tooBig'
		p = Box.create()
	end 'tooBig'

	return p.n
end 'bump'

function poke(p Box) returns Integer
	Scheduler.yield()
	p.n = 99
	return p.n
end 'poke'

function main() returns ExitCode
	let b = G
	_ = bump(b)
	print("{await async poke(b)}\n")
	print("{G.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:30:6: cannot pass a value, which may share storage with a module-level `let`, to a function that writes parameter 'p' (in main); pass a clone()
error E3159: <fragment>:31:22: cannot pass 'b', which is read-only (a module-level `let`), to a function that writes parameter 'p' (in main); pass a clone()
```

<!-- test: async-await.error.an-immutable-global-in-a-let-cannot-be-written-by-a-coroutine -->
`b` is a `let` naming the record the module-level `let G` holds, so a coroutine that writes its parameter may
not be handed it.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

let G = Box.create()

function poke(p Box) returns Integer
	Scheduler.yield()
	p.n = 99
	return p.n
end 'poke'

function main() returns ExitCode
	let b = G
	print("{await async poke(b)}\n")
	print("{G.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:22:22: cannot pass 'b', which is read-only (a module-level `let`), to a function that writes parameter 'p' (in main); pass a clone()
```

<!-- test: async-await.error.an-immutable-global-is-not-bound-to-a-var-before-a-coroutine -->
A `var` is a writable name, so it may not hold the record the module-level `let G` holds: the binding is refused
before any coroutine is handed it.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

let G = Box.create()

function poke(p Box) returns Integer
	Scheduler.yield()
	p.n = 99
	return p.n
end 'poke'

function main() returns ExitCode
	var b = G
	print("{await async poke(b)}\n")
	print("{G.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: <fragment>:21:6: cannot assign a value, which may share storage with a module-level `let`, to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: async-await.a-cell-resident-co-owner-is-shared-with-a-reading-coroutine -->
A cell-resident `b` holding its own reference to an element `items` still holds is shared with a coroutine that
only reads it, and the array keeps its own.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Boxes = Array with Box

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

function first(items Boxes) returns Box
	return try items.get(0) otherwise panic("no box")
end 'first'

function bump(p Box) returns Integer
	if p.n > 100 'tooBig'
		p = Box.create()
	end 'tooBig'

	return p.n
end 'bump'

function show(p Box) returns Integer
	Scheduler.yield()
	return p.n
end 'show'

function main() returns ExitCode
	var items = Boxes.create()
	items.push(Box.create())
	var b = Box.create()
	print("{bump(b)}\n")
	b = first(items)
	print("{await async show(b)}\n")
	print("{items.count()} {first(items).n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
7
1 7
```

<!-- test: async-await.error.a-closure-body-spanning-lines-takes-a-cell-resident-string -->
The closure's body continues onto a second line inside an array literal, and still captures `word`, so handing
`word` to the coroutine afterwards is a use after move.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Integers = Array with Integer

function shout(text String) returns Integer
	Scheduler.yield()
	print("{text}!\n")
	return text.byteLength() as Integer
end 'shout'

function total(values Integers) returns Integer
	var sum = 0 as Integer

	for v in values 'each'
		sum = sum + v
	end 'each'

	return sum
end 'total'

function fill(dest String, n Integer)
	dest = "filled {n} padded out long enough to heap allocate"
end 'fill'

function main() returns ExitCode
	var word = "hello padded out long enough to heap allocate {1}"
	fill(word, n: 2)
	let peek = function() gives total([1,
		word.byteLength() as Integer])
	let p = async shout(word)
	print("{await p} {peek()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:22: use of moved value 'word': its ownership moved to the closure that captures it
```

<!-- test: async-await.error.a-closure-body-spanning-lines-takes-a-cell-resident-box -->
The same multi-line closure body over a cell-resident `Box`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Integers = Array with Integer

type Box
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Box'

function total(values Integers) returns Integer
	var sum = 0 as Integer

	for v in values 'each'
		sum = sum + v
	end 'each'

	return sum
end 'total'

function touch(b Box)
	if b.n > 100 'big'
		b = Box.create()
	end 'big'
end 'touch'

function readIt(b Box) returns Integer
	Scheduler.yield()
	return b.n
end 'readIt'

function main() returns ExitCode
	var b = Box.create()
	touch(b)
	let peek = function() gives total([1,
		b.n])
	let p = async readIt(b)
	print("{await p} {peek()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:39:23: use of moved value 'b': its ownership moved to the closure that captures it
```

<!-- test: async-await.a-var-the-loop-iterates-is-shared-with-async-inside-the-loop -->
The loop holds its own reference to `items` and `x` its own reference to an element, so the spawn shares
`items` with the coroutine and `x` is still readable after the coroutine has released its reference.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Words = Array with String

function measure(words Words) returns Integer
	Scheduler.yield()
	var total = 0 as Integer

	for word in words 'each'
		total = total + (word.byteLength() as Integer)
	end 'each'

	return total
end 'measure'

function main() returns ExitCode
	var items = Words.create()
	items.push("first padded out long enough to heap allocate {1}")
	items.push("second padded out long enough to heap allocate {2}")

	for x in items 'each'
		let p = async measure(items)
		print("{await p} {x}\n")
		break
	end 'each'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
95 first padded out long enough to heap allocate 1
```

<!-- test: async-await.a-value-a-closure-holds-a-field-of-is-shared-with-async -->
`inner` is a field of `b`; the closure holds a reference of its own to it, so `b` is shared with the coroutine.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var n as Integer

	static function create() returns Self
		return Self{n: 7}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create()}
	end 'create'
end 'Outer'

function consume(b Outer) returns Integer
	Scheduler.yield()
	return b.inner.n
end 'consume'

function main() returns ExitCode
	let b = Outer.create()
	let inner = b.inner
	let peek = function() gives inner.n
	let p = async consume(b)
	let r = await p
	return (r + peek()) as ExitCode
end 'main'
```
```exitcode
14
```

<!-- test: async-await.a-value-whose-field-is-read-after-the-call-is-shared-with-async -->
`inner` holds its own reference to a field of `b`, so `b` is shared with the coroutine and `inner` is still
readable after the coroutine has released `b`.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var n as Integer
	export let text as String

	static function create() returns Self
		return Self{n: 7, text: "inner padded out long enough to heap allocate {7}"}
	end 'create'
end 'Inner'

type Outer
	export let inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create()}
	end 'create'
end 'Outer'

function consume(b Outer) returns Integer
	Scheduler.yield()
	return b.inner.n
end 'consume'

function main() returns ExitCode
	let b = Outer.create()
	let inner = b.inner
	let p = async consume(b)
	let r = await p
	print("{r} {inner.n} {inner.text}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 7 inner padded out long enough to heap allocate 7
```

<!-- test: async-await.a-coroutine-that-reassigns-an-interface-parameter -->
The coroutine reassigns a parameter held at an interface type, which lives in a cell the coroutine owns.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	var side as Integer

	static function create(side Integer) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

function squareShape(side Integer) returns Shape
	return Square.create(side)
end 'squareShape'

function grow(s Shape) returns Integer
	Scheduler.yield()

	if s.area() < 5 'small'
		s = squareShape(4)
	end 'small'

	return s.area()
end 'grow'

function main() returns ExitCode
	let p = async grow(Square.create(1))
	print("{await p}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
16
```

<!-- test: async-await.a-callee-that-keeps-its-argument-is-spawned-and-awaited -->
`Holder.create` moves its `String` parameter into the record it returns. The spawn hands the coroutine the
argument, the callee takes it when it runs, and the awaited record owns it from then on.
```maxon
type Holder
	export var s as String

	static function create(s String) returns Self
		Scheduler.yield()
		return Self{s: s}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let p = async Holder.create("hello {3}")
	let h = await p
	print("{h.s}\n")
	return 0
end 'main'
```
```stdout
hello 3
```
```exitcode
0
```

<!-- test: async-await.a-callee-that-keeps-its-argument-is-spawned-and-dropped-before-it-runs -->
The same spawn dropped before the coroutine ever runs: the callee never took the argument, so the drop
releases it.
```maxon
type Holder
	export var s as String

	static function create(s String) returns Self
		Scheduler.yield()
		return Self{s: s}
	end 'create'
end 'Holder'

function main() returns ExitCode
	_ = async Holder.create("hello {3}")
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: async-await.a-callee-that-keeps-its-argument-completes-and-is-then-dropped -->
The same spawn left to complete while `main` awaits a sibling, then dropped: the callee took the argument when
it ran, so the drop releases only the record it returned, which owns the argument.
```maxon
typealias Integer = int(i64.min to i64.max)

type Holder
	export var s as String

	static function create(s String) returns Self
		Scheduler.yield()
		return Self{s: s}
	end 'create'
end 'Holder'

function ten() returns Integer
	Scheduler.yield()
	return 10
end 'ten'

function main() returns ExitCode
	let a = async Holder.create("hello {3}")
	let b = async ten()
	let n = await b
	print("{n} {a.isComplete()}\n")
	return 0
end 'main'
```
```stdout
10 true
```
```exitcode
0
```

<!-- test: async-await.error.a-cell-handed-by-reference-in-one-spawn-overlaps-the-argument-read-from-it -->
`word` is the first argument and is also handed by reference to `fill`, which reassigns it, inside the second:
one call may not hand a function storage it reassigns together with another view of that storage.
```maxon
typealias Integer = int(i64.min to i64.max)

function shout(text String, n Integer) returns Integer
	Scheduler.yield()
	print("{text} {n}\n")
	return n
end 'shout'

function fill(dest String) returns Integer
	dest = "refilled padded out long enough to heap allocate"
	return 7
end 'fill'

function main() returns ExitCode
	var word = "hello padded out long enough to heap allocate {1}"
	let p = async shout(word, n: fill(word))
	print("{await p}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:17:22: argument 'word' overlaps argument 'word' of 'fill', which 'fill' reassigns (parameter 'dest') — one call may not hand a function storage it reassigns by reference together with another view of that storage. Pass 'word.clone()'
```
