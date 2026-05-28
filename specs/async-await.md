---
feature: async-await
status: experimental
keywords: [async, await, green threads, concurrency, promise]
category: concurrency
---

# Async/Await

## Documentation

Maxon supports cooperative concurrency via `async` and `await` with green threads. Each `async` call spawns a lightweight green thread with a growable stack (starting at 2KB). All green threads run on a single OS thread — context switching happens only at `await` points.

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
- No OS threads — all green threads share one OS thread
- Cooperative scheduling — context switches only at `await`
- Growable stacks — 2KB initial, doubles when needed
- No atomics needed — reference counting stays non-atomic
- Fire-and-forget safe — unawaited green threads are drained at program exit

**Restrictions:**
- `async` can only be used on direct function calls (not closures or indirect calls)
- `async` can only be used on functions that yield (contain I/O operations or await points)
- Throwing async functions require `try await` to extract the result
- `promise.cancel()` cancels the associated green thread

**Typed promises:**
Promises can be stored in collections by declaring an explicit `Promise with T` type — the compiler boxes the i64 handle into a `Promise<T>` struct at the storage site and unboxes it at the `await` site. This lets you spawn N green threads with a `for` loop, collect the promises into an `Array with (Promise with T)`, and await them in a second pass.

```text
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

var arr = IntPromiseArray.create()
arr.push(async work(1))
arr.push(async work(2))
for p in arr 'each'
    let result = await p   // unboxed automatically
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
		let r2 = try await p2 otherwise 99
		return r1 + r2
end 'main'
```
```exitcode
141
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
A stored Promise<T> is treated as throwing at the type level — its
compile-time throws-ness is lost across storage, so `try await ... otherwise X`
is always required at the read site. The runtime branches on a stored
throws-bit, so awaiting a non-throwing promise via `try await ... otherwise X`
still succeeds; the `otherwise` handler is just unused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
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
Stored Promise<T> where the async function throws an associated-value
enum: the heap-allocated error payload must be released on the
`otherwise` path. Without this, the third element's `WorkError.failed`
allocation leaks and shows up under `--mm-trace`. Exercises the
runtime-bit `errorIsHeapPtr` field on the boxed Promise struct: the
compile-time error type is lost across array storage, so the otherwise
emitter has to branch on the loaded bit to decide whether to decref.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
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
