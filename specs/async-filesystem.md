---
feature: async-filesystem
status: experimental
keywords: [async, await, try-await, file, io, throwing, concurrency, promise]
category: concurrency
---

# Async over the file surface

## Documentation

`async` over a THROWING callee hands the error back through the promise, so the result is collected with
`try await p otherwise <handler>` rather than a bare `await`. Every `otherwise` form is available there:
a default value, a `panic`, a block handler, `ignore`, and propagation.

```text
var promise = async mayFail(args)

var result = try await promise otherwise defaultValue
var result = try await promise otherwise panic("message")

try await promise otherwise 'handler'
	// handle error
end 'handler'

try await promise otherwise ignore
```

The file surface is where that combination is load-bearing. Every operation on it is a `static` on `File`,
so spawning one is a QUALIFIED spawn — `async File.readText(p)` — and most of them throw, so the awaits
are `try await`. Nothing about the spawn depends on the callee being a free function: `async` names a call,
and a call whose callee is a namespace member is still a call.

Real file I/O is also a genuine park point, which is what makes the parallel cases mean anything: two
spawned reads have their waits OVERLAPPED, because each coroutine gives up the green thread while its
request is in flight. A callee that only computes is refused (E3073); `File.exists` appears in the cases
below purely to supply that yield point where the subject is the error path rather than the I/O.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the one
statement of it.** These cases need both the green-thread substrate and the managed-file surface, and a
lane missing either is refused by the compiler with **E3104**, which the runner reads directly — so no
case here carries a marker.

## Tests

<!-- test: async-filesystem.try-await-string-success -->
```maxon
enum TestError implements Error
		failed
end 'TestError'

function getName(ok bool) returns String throws TestError
		_ = File.exists(FilePath from "noyield.txt")
		if ok 'check'
				return "Alice"
		end 'check'
		throw TestError.failed
end 'getName'

function main() returns ExitCode
		let p = async getName(true)
		let name = try await p otherwise "unknown"
		print("{name}")
		return 42
end 'main'
```
```exitcode
42
```
```stdout
Alice
```

<!-- test: async-filesystem.try-await-string-error -->
```maxon
enum TestError implements Error
		failed
end 'TestError'

function getName(ok bool) returns String throws TestError
		_ = File.exists(FilePath from "noyield.txt")
		if ok 'check'
				return "Alice"
		end 'check'
		throw TestError.failed
end 'getName'

function main() returns ExitCode
		let p = async getName(false)
		let name = try await p otherwise "unknown"
		print("{name}")
		return 42
end 'main'
```
```exitcode
42
```
```stdout
unknown
```

<!-- test: async-filesystem.try-await-parallel-throws -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum MathError implements Error
		divByZero
end 'MathError'

function checkedDiv(a Integer, b Integer) returns Integer throws MathError
		_ = File.exists(FilePath from "noyield.txt")
		if b == 0 'z'
				throw MathError.divByZero
		end 'z'
		// Reached only when b != 0 (the guard above throws otherwise); the divide rides `try` because
		// `/` is fallible at the type level and b is typed 0-inclusive. The `otherwise` is unreachable,
		// so it panics rather than fold a wrong answer.
		return try (a / b) otherwise panic("safeDivide: b was 0 past the zero guard")
end 'checkedDiv'

function main() returns ExitCode
		let p1 = async checkedDiv(50, b: 2)
		let p2 = async checkedDiv(10, b: 0)
		let p3 = async checkedDiv(30, b: 3)
		let r1 = try await p1 otherwise 0
		let r2 = try await p2 otherwise 0
		let r3 = try await p3 otherwise 0
		return r1 + r2 + r3
end 'main'
```
```exitcode
35
```

The cases below exercise the actual async I/O path rather than using `File.exists` as a no-op yield point.

<!-- test: async-filesystem.async-read-nonexistent -->
```maxon
function main() returns ExitCode
		let p = async File.readText(FilePath from "nonexistent_async_read.txt")
		let content = try await p otherwise "FAILED"
		if content == "FAILED" 'check'
				return 42
		end 'check'
		return 1
end 'main'
```
```exitcode
42
```

<!-- test: async-filesystem.async-write-read -->
```maxon
function main() returns ExitCode
		let path = FilePath from "async_test_file.txt"

		// Write synchronously first
		try File.writeText(path, content: "AsyncTest") otherwise 'werr'
				return 1
		end 'werr'

		// Read asynchronously
		let p = async File.readText(path)
		let content = try await p otherwise 'rerr'
				try File.delete(path) otherwise ignore
				return 2
		end 'rerr'

		// Clean up
		try File.delete(path) otherwise ignore

		// Verify
		if content.count() != 9 'len'
				return 3
		end 'len'
		print("{content}")
		return 42
end 'main'
```
```exitcode
42
```
```stdout
AsyncTest
```

<!-- test: async-filesystem.async-parallel-reads -->
```maxon
function main() returns ExitCode
		let p1 = async File.readText(FilePath from "no_file_a.txt")
		let p2 = async File.readText(FilePath from "no_file_b.txt")
		let r1 = try await p1 otherwise "default1"
		let r2 = try await p2 otherwise "default2"
		print("{r1}")
		print("{r2}")
		return 42
end 'main'
```
```exitcode
42
```
```stdout
default1default2
```

<!-- test: async-filesystem.async-exists -->
```maxon
function main() returns ExitCode
		let p = async File.exists(FilePath from "no_such_file_async.txt")
		let exists = await p
		if exists 'found'
				return 1
		end 'found'
		return 42
end 'main'
```
```exitcode
42
```

<!-- test: async-filesystem.async-write-read-parallel -->
```maxon
function main() returns ExitCode
		let path1 = FilePath from "async_par_a.txt"
		let path2 = FilePath from "async_par_b.txt"

		// Write both files synchronously
		try File.writeText(path1, content: "FileA") otherwise 'e1'
				return 1
		end 'e1'
		try File.writeText(path2, content: "FileB") otherwise 'e2'
				try File.delete(path1) otherwise ignore
				return 2
		end 'e2'

		// Read both asynchronously in parallel
		let p1 = async File.readText(path1)
		let p2 = async File.readText(path2)
		let c1 = try await p1 otherwise "err"
		let c2 = try await p2 otherwise "err"

		// Clean up
		try File.delete(path1) otherwise ignore
		try File.delete(path2) otherwise ignore

		print("{c1}{c2}")
		return 42
end 'main'
```
```exitcode
42
```
```stdout
FileAFileB
```
