---
feature: async-await
status: experimental
keywords: [async, await, green threads, concurrency, promise]
category: concurrency
---

# Async/Await

## Documentation

Maxon supports cooperative concurrency via `async` and `await` with green threads. Each `async` call spawns a lightweight green thread with a growable stack (starting at 2KB). Green threads are multiplexed over a POOL of OS worker threads — one per processor, up to the detected CPU count — so two green threads can be running at the same instant on different cores. Scheduling is still cooperative: a green thread yields the processor it is on only at an `await` or an I/O point, never by pre-emption.

```text
// Spawn a green thread
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
- Worker pool — green threads run on OS worker threads, one per processor, spawned on demand as work
  appears. `MAXON_MAX_PROCS` caps the count; `MAXON_MAX_PROCS=1` pins the program to one processor,
  which is what makes a concurrent program's execution order reproducible.
- Cooperative scheduling — a green thread keeps its processor until it reaches an `await` or an I/O
  point. Cooperative is not the same as serial: other green threads run on the other processors
  meanwhile.
- Growable stacks — 2KB initial, doubles when needed
- Reference counting IS atomic — `mm_incref`/`mm_decref` use atomic read-modify-write, because two
  worker threads can hold references to one object at the same time. Anything else shared between
  green threads needs the same care.
- Fire-and-forget safe — unawaited green threads are drained at program exit

**Restrictions:**
- `async` can only be used on direct function calls (not closures or indirect calls)
- `async` can only be used on functions that yield (contain I/O operations or await points)
- Throwing async functions require `try await` to extract the result
- `promise.cancel()` cancels the associated green thread
- **`await` is LINEAR**: a promise is awaited exactly once, and a second await is a compile error (E3100)

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

**Known boundary.** The check sees awaits of BINDINGS within one function's control flow. A promise
that ESCAPES that is not tracked, and awaiting it twice still double-frees at runtime: the same
container slot (`await arr[0]` twice) or a struct field (`await h.pr`), whose box holds a runtime
handle naming no statically-known thread; and a promise passed as a call ARGUMENT to a callee that
awaits it, whose second await lives in another frame. These need ownership tracked through storage
and across frames — shv2's ownership milestone. They are missed, never mis-reported: a promise out
of storage is never spuriously equal to another, so the check stays silent rather than guessing.

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
var flag = 0

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
		_ = File.exists(FilePath from "noyield.txt")
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
var flag = 0

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
and can be matched. (It used to be handed back as the raw error flag typed
`int`, so any use of `e` failed with "Primitive type 'int' has no method".)
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
		var guard = 1
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

var flag = 0

enum TaskError implements Error
		timedOut
		crashed

		export function code() returns Code
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
error E3005: specs/fragments/async-await/async-await.error.non-promise.test:4:16: 'await' requires a promise value from 'async', got integer
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
error E3073: specs/fragments/async-await/async-await.error.no-yield.test:9:11: 'async heavyCompute(5)' — function never yields; 'async' is for I/O-concurrent work only
```

<!-- test: async-await.storage.bind-error-through-storage -->
The error type SURVIVES storage, so an `otherwise (e)` binding on a promise pulled back out of
an array binds the error the thunk actually throws — a `TaskError`, matched case by case. This
is the bug that started all of this: `e` used to come back typed `int` (it was the raw promise
handle), so any use of it failed with "no method named ...". Then it was refused outright, because
`Promise with T` had nowhere to keep the error type. Now the type names it, and it just works.
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
to the payload. The binding takes ownership and scope-end releases it exactly once. This is the
in-tree leak that a green suite could never show: the path only runs when a worker DIES, and when
it did, the box said "not a heap pointer" (a bit that could not distinguish one error type from
another), the conditional decref never fired, and every dead worker leaked its error.
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
error E3098: specs/fragments/async-await/async-await.error.storage-erases-error-type.test:17:7: cannot store a promise from a function that throws 'TaskError' in 'BadPromise': it names the result type only, which would erase the error type — declare the storage as 'Promise with (T, TaskError)' so 'try await' can bind and release the error
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
error E3098: specs/fragments/async-await/async-await.error.storage-names-wrong-error-type.test:21:7: 'WrongPromise' names the error type 'OtherError', but this promise's function throws 'TaskError'
```

<!-- test: async-await.error.propagate-type-mismatch -->
Bare `try await p` re-throws the awaited thunk's error through the enclosing
function's error-return ABI. If the two error types differ, the caller decodes
one enum's ordinals as another's tags — a silent miscompile. The `try` CALL
form has always rejected this; the `try await` form used to skip the check
entirely, because it had no error type to compare. Now it has one.
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
error E3059: specs/fragments/async-await/async-await.error.propagate-type-mismatch.test:20:11: try propagates 'AError' but enclosing function throws 'BError' — add 'otherwise' to convert
```

<!-- test: async-await.error.propagate-type-mismatch-through-storage -->
The propagation type check needs an error type to check, and now it has one even when the promise
came out of storage. This is the case that used to be a SILENT MISCOMPILE and then an outright
refusal: `mayFail` throws `TaskError`, `viaStorage` throws `WrapError`, and re-throwing one
through the other's error-return ABI made the caller decode `TaskError`'s ordinals as `WrapError`'s
tags — and since `WrapError` has associated values, mm_decref an ordinal as a pointer and die. The
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
error E3059: specs/fragments/async-await/async-await.error.propagate-type-mismatch-through-storage.test:24:11: try propagates 'TaskError' but enclosing function throws 'WrapError' — add 'otherwise' to convert
```

<!-- test: async-await.error.double-await -->
<!-- targets: x64-windows -->
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
error E3100: specs/fragments/async-await/async-await.error.double-await.test:10:11: this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice
```

<!-- test: async-await.error.double-await-in-loop -->
<!-- targets: x64-windows -->
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
error E3100: specs/fragments/async-await/async-await.error.double-await-in-loop.test:10:13: this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice
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
<!-- targets: x64-windows -->
Linearity is a property of the GREEN THREAD, not of the identifier text. `let q = p` gives one
green thread a second name; awaiting through both names awaits it twice, and the payload the
thunk handed over once is released twice. This compiled clean and double-freed at runtime
("mm_decref: refcount underflow") for as long as the check keyed on the NAME — one extra line
defeated the whole thing.
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
error E3100: specs/fragments/async-await/async-await.error.double-await-through-alias.test:11:11: this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice
```

<!-- test: async-await.error.double-await-through-alias-in-branch -->
<!-- targets: x64-windows -->
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
error E3100: specs/fragments/async-await/async-await.error.double-await-through-alias-in-branch.test:13:13: this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice
```

<!-- test: async-await.error.double-await-alias-outlives-rebind -->
<!-- targets: x64-windows -->
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
error E3100: specs/fragments/async-await/async-await.error.double-await-alias-outlives-rebind.test:12:11: this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice
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
because the ternary's arms are a *recent* pair of blocks: they used to be hoisted into the entry
block with only the store made conditional, and a linearity check that ran against the hoisted
shape would have seen two awaits in ONE block and rejected a valid program. It reads the arms as
the branches they now are.
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
<!-- targets: x64-windows -->
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
error E3100: specs/fragments/async-await/async-await.error.double-await-after-ternary-arm.test:13:11: this promise has already been awaited: 'await' is linear — a promise is awaited exactly once, because the awaited thunk hands its result over and a second await would release it twice
```

<!-- test: async-await.error.await-without-try -->
A throwing thunk hands the awaiting frame an OWNED error on its error path. A
plain `await` has nowhere to put it: the value it yields is the undefined success
slot, and an associated-value payload is released by nobody — the run below ends
101 (leak) if it is allowed to compile. `try await` is the only form that can
receive the error, which is what this spec has said since the top of the file;
now the compiler enforces it.
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
error E3057: specs/fragments/async-await/async-await.error.await-without-try.test:15:11: throwing function requires try: 'await' on a promise from a function that throws 'TaskError' drops the error and leaks its payload — use 'try await'
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
worker_start #1
io_resume #1 [file_exists]
await #1 [yield]
worker_exit #1
worker_start #2
worker_exit #2
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
		var sum = 0
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
demands `try await`. It used to be that EVERY stored promise was treated as throwing, because
the box could not say which it was.
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
		var sum = 0
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

This is the spec that used to justify the `errorIsHeapPtr` bit. With the error type erased by
storage, the compiler could not know whether the error flag was a heap pointer or a plain ordinal,
so the box carried a runtime bit and the otherwise path BRANCHED on it. The bit is gone: naming
the error type answers the question statically, and there is nothing left to approximate.
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
		var sum = 0
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
		var total = 0
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
minimal shape — one `async` inside one `async`, where the inner one does I/O —
and it deadlocked the whole process at N=1 for as long as the feature existed,
because the flag that says "this green thread has finished switching off its
stack" was published by the scheduler loop that DISPATCHED a thread rather than
by the switch itself. One level deep those are the same thread; nested they are
not, so the inner thread's completion spun for ever inside `__netpoll_claim_done`
and pinned a core. Nothing in this file exercised the shape.
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

var flag = 0

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
A `try await` park loop drives the TIMER heap, exactly as its `await` twin does.
Both loops are the same machine — "nothing runnable, poll every engine, recheck" —
and a park loop that omits one engine leaves the green threads parked on that
engine unwoken. For a worker whose only wakeup is a `sleep()` deadline, omitting
the timer poll means the awaiting thread never fires it: the run then survives only
on whatever OTHER scheduler thread happens to poll timers, at that thread's park
period, and on a single-processor run (`MAXON_MAX_PROCS=1`) it does not survive at
all — it hangs.

The assertion is RELATIVE and its control is IN THE SAME PROCESS, so it states the
invariant directly ("`try await` costs what `await` costs") and is immune to how
fast the host is. The 200 ms slack is a wide band over six rounds: the defect this
pins cost ~55 ms per round (measured 328 ms vs 656 ms for six 10 ms sleeps), so a
regression clears the threshold by 100+ ms while a healthy run sits ~0 ms above its
own control. The absolute bound on the control is the anchor that keeps the
relation from passing because BOTH halves regressed.
```maxon
typealias Integer = int(i64.min to i64.max)

enum LeafError implements Error
		failed
end 'LeafError'

function timedThrowing() returns Integer throws LeafError
		_ = File.exists(FilePath from "noyield.txt")
		sleep(10)
		return 1
end 'timedThrowing'

function timedPlain() returns Integer
		_ = File.exists(FilePath from "noyield.txt")
		sleep(10)
		return 1
end 'timedPlain'

function main() returns ExitCode
		let awaitStart = Clock.nowMs()
		var plain = 0
		for _ in 0 upto 6 'plainRound'
				let p = async timedPlain()
				plain = plain + (await p)
		end 'plainRound'
		let awaitMs = Clock.elapsedMs(awaitStart)

		let tryStart = Clock.nowMs()
		var thrown = 0
		for _ in 0 upto 6 'tryRound'
				let p = async timedThrowing()
				thrown = thrown + (try await p otherwise 0)
		end 'tryRound'
		let tryMs = Clock.elapsedMs(tryStart)

		var score = 0
		if awaitMs < 10000 'controlSane'
				score = score + 1
		end 'controlSane'
		if tryMs < awaitMs + 200 'noTimerPenalty'
				score = score + 1
		end 'noTimerPenalty'
		print("plain={plain} thrown={thrown} score={score}\n")
		return 0
end 'main'
```
```stdout
plain=6 thrown=6 score=2
```
