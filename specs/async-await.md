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

## Tests

<!-- test: async-await.basic -->
```maxon
typealias Integer = int(i64.min to i64.max)

function compute() returns Integer
		if File.exists(FilePath from "noyield.txt") 'io'
		end 'io'
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
		if File.exists(FilePath from "noyield.txt") 'io'
		end 'io'
		return 10
end 'taskA'

function taskB() returns Integer
		if File.exists(FilePath from "noyield.txt") 'io'
		end 'io'
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
		if File.exists(FilePath from "noyield.txt") 'io'
		end 'io'
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
		if File.exists(FilePath from "noyield.txt") 'io'
		end 'io'
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
		if File.exists(FilePath from "noyield.txt") 'io'
		end 'io'
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
		if File.exists(FilePath from "noyield.txt") 'io'
		end 'io'
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
		if File.exists(FilePath from "noyield.txt") 'io'
		end 'io'
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
		if File.exists(FilePath from "noyield.txt") 'io'
		end 'io'
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
		if File.exists(FilePath from "noyield.txt") 'io'
		end 'io'
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
		if File.exists(FilePath from "noyield.txt") 'io'
		end 'io'
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
