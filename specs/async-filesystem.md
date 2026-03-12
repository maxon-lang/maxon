---
feature: async-filesystem
status: experimental
keywords: [async, await, try-await, throwing, concurrency]
category: concurrency
---

# Async with Throwing Functions

## Documentation

`async` can be used with throwing functions. Since the spawned green thread may throw, `try await` is required to handle errors when awaiting the result. All `try ... otherwise` forms are supported: default value, panic, block handler, ignore, and propagation.

```text
// Spawn an async call to a throwing function
var promise = async mayFail(args)

// Await with default value on error
var result = try await promise otherwise defaultValue

// Await with panic on error
var result = try await promise otherwise panic("message")

// Await with block handler
try await promise otherwise 'handler'
    // handle error
end 'handler'

// Await with ignore (void functions)
try await promise otherwise ignore
```

## Tests

<!-- test: async-filesystem.try-await-success-path -->
```maxon
typealias Integer = int(i64.min to i64.max)

union ComputeError implements Error
    overflow
end 'ComputeError'

function safeDivide(a Integer, b Integer) returns Integer throws ComputeError
    if File.exists(FilePath from "noyield.txt") 'io'
    end 'io'
    if b == 0 'check'
        throw ComputeError.overflow
    end 'check'
    return a / b
end 'safeDivide'

function main() returns ExitCode
    var p = async safeDivide(100, b: 5)
    var r = try await p otherwise 0
    return r
end 'main'
```
```exitcode
20
```

<!-- test: async-filesystem.try-await-error-path -->
```maxon
typealias Integer = int(i64.min to i64.max)

union ComputeError implements Error
    overflow
end 'ComputeError'

function safeDivide(a Integer, b Integer) returns Integer throws ComputeError
    if File.exists(FilePath from "noyield.txt") 'io'
    end 'io'
    if b == 0 'check'
        throw ComputeError.overflow
    end 'check'
    return a / b
end 'safeDivide'

function main() returns ExitCode
    var p = async safeDivide(100, b: 0)
    var r = try await p otherwise 99
    return r
end 'main'
```
```exitcode
99
```

<!-- test: async-filesystem.try-await-string-success -->
```maxon
union TestError implements Error
    failed
end 'TestError'

function getName(ok bool) returns String throws TestError
    if File.exists(FilePath from "noyield.txt") 'io'
    end 'io'
    if ok 'check'
        return "Alice"
    end 'check'
    throw TestError.failed
end 'getName'

function main() returns ExitCode
    var p = async getName(true)
    var name = try await p otherwise "unknown"
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
union TestError implements Error
    failed
end 'TestError'

function getName(ok bool) returns String throws TestError
    if File.exists(FilePath from "noyield.txt") 'io'
    end 'io'
    if ok 'check'
        return "Alice"
    end 'check'
    throw TestError.failed
end 'getName'

function main() returns ExitCode
    var p = async getName(false)
    var name = try await p otherwise "unknown"
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

union MathError implements Error
    divByZero
end 'MathError'

function checkedDiv(a Integer, b Integer) returns Integer throws MathError
    if File.exists(FilePath from "noyield.txt") 'io'
    end 'io'
    if b == 0 'z'
        throw MathError.divByZero
    end 'z'
    return a / b
end 'checkedDiv'

function main() returns ExitCode
    var p1 = async checkedDiv(50, b: 2)
    var p2 = async checkedDiv(10, b: 0)
    var p3 = async checkedDiv(30, b: 3)
    var r1 = try await p1 otherwise 0
    var r2 = try await p2 otherwise 0
    var r3 = try await p3 otherwise 0
    return r1 + r2 + r3
end 'main'
```
```exitcode
35
```

<!-- test: async-filesystem.try-await-cross-block -->
```maxon
typealias Integer = int(i64.min to i64.max)

union TestError implements Error
    failed
end 'TestError'

function mayFail(n Integer) returns Integer throws TestError
    if File.exists(FilePath from "noyield.txt") 'io'
    end 'io'
    if n < 0 'neg'
        throw TestError.failed
    end 'neg'
    return n * 2
end 'mayFail'

function compute() returns Integer throws TestError
    var r1 = try mayFail(5) otherwise 0
    var p = async mayFail(r1)
    var r2 = try await p otherwise 0
    return r2
end 'compute'

function main() returns ExitCode
    var result = try compute() otherwise 0
    return result
end 'main'
```
```exitcode
20
```

## Real Async I/O Tests

These tests exercise actual IOCP async I/O paths rather than using `File.exists` as a no-op yield point.

<!-- test: async-filesystem.async-read-nonexistent -->
```maxon
function main() returns ExitCode
    var p = async File.readText(FilePath from "nonexistent_async_read.txt")
    var content = try await p otherwise "FAILED"
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
    var p = async File.readText(path)
    var content = try await p otherwise 'rerr'
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
    var p1 = async File.readText(FilePath from "no_file_a.txt")
    var p2 = async File.readText(FilePath from "no_file_b.txt")
    var r1 = try await p1 otherwise "default1"
    var r2 = try await p2 otherwise "default2"
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
    var p = async File.exists(FilePath from "no_such_file_async.txt")
    var exists = await p
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
    var p1 = async File.readText(path1)
    var p2 = async File.readText(path2)
    var c1 = try await p1 otherwise "err"
    var c2 = try await p2 otherwise "err"

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
