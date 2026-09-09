---
title: Error Handling
description: Throwing functions, try ... otherwise, try blocks, and error propagation.
sidebar:
  order: 10
---

Maxon uses a unified error handling system based on typed errors. Functions either return a value or throw an error—there are no optional types or null values. Error types must be enums conforming to the `Error` interface.

### Defining Error Types

Error types are enums that conform to the `Error` interface:

```maxon
// Simple enum error
enum FileError implements Error
		notFound
		permissionDenied
		alreadyExists
end 'FileError'

// Int-backed enum error (for error codes)
enum HttpError int implements Error
		badRequest = 400
		notFound = 404
		serverError = 500
end 'HttpError'

// String-backed enum error (for messages)
enum ValidationError String implements Error
		emptyField = "Field cannot be empty"
		invalidFormat = "Invalid format"
end 'ValidationError'
```

**Note:** Only enums can conform to `Error`. Attempting to make a type (struct) conform to `Error` produces a compile error (E023).

### Throwing Functions

Functions that can throw errors declare the error type with `throws`:

```maxon
function readFile(path String) returns String throws FileError
		if not exists(path) 'check'
				throw FileError.notFound
		end 'check'
		return contents
end 'readFile'

// Void function that throws
function resetConfig() throws FileError
		if not exists("config.json") 'check'
				throw FileError.notFound
		end 'check'
		// reset logic...
end 'resetConfig'
```

**Syntax:**
```maxon
function name(params) returns ReturnType throws ErrorType
function name(params) throws ErrorType  // void function that throws
```

### Throw Statement

Use `throw` to throw an error value:

```maxon
throw FileError.notFound
throw HttpError.serverError
```

**Rules:**
- `throw` is only valid inside functions with a `throws` declaration
- The thrown value must match the declared error type

### Panic Statement

The `panic` statement immediately terminates the program with an error message and stack trace. It is used to signal unrecoverable errors — situations that represent bugs in the program rather than expected error conditions.

```maxon
panic("something went wrong")
```

The argument can be a plain string literal or an interpolated string. The program prints a panic message to stderr including the source file and line number, followed by a stack trace, then exits with code 1.

```maxon
function processValue(x int) returns int
		if x < 0 'negative'
				panic("processValue: negative input, got {x}")
		end 'negative'
		return x * 2
end 'processValue'
```

Output when called with a negative value:
```text
panic at example.maxon:3: processValue: negative input not allowed
Stack trace:
  in example.processValue
  in example.main
  in _start
```

Use `panic` for invariant violations and unreachable code paths. For expected error conditions (invalid user input, missing files, etc.), use `throw`/`try`/`otherwise` instead.

### Calling Throwing Functions

When calling a function that throws, you must use `try`:

```maxon
// Compile error - must use try
let contents = readFile("config.json")  // ERROR

// Correct - use try with otherwise
let contents = try readFile("config.json") otherwise ""
```

The `try` keyword is always required when calling throwing functions, even when using `otherwise`.

### Handling Errors with `otherwise`

The `otherwise` keyword provides unified error handling for throwing expressions. There are six forms:

#### Default Value Form

Provide a default value when an error occurs:

```maxon
let value = try mayFail() otherwise 42
```

If `mayFail()` throws, `value` is assigned `42`. The default expression must match the return type.

```maxon
function readConfig() returns String
		// If readFile throws, use empty string as default
		let contents = try readFile("config.json") otherwise ""
		return contents
end 'readConfig'
```

#### Ignore Form

Discard errors when you don't need the result:

```maxon
try mayFail() otherwise ignore
```

This silently ignores any thrown error. Use sparingly—typically for cleanup operations where errors can be safely ignored.

```maxon
function cleanup()
		// Best-effort cleanup, ignore failures
		try deleteFile("temp.txt") otherwise ignore
end 'cleanup'
```

#### Panic Form

Crash immediately if an error occurs. Use for unreachable error paths where a failure indicates a bug:

```maxon
let slot = try slots.get(idx) otherwise panic("unreachable: index was validated")
```

This is preferred over a silent default value when the error path should never execute. If it does, the program terminates with a stack trace rather than silently miscompiling or producing wrong results.

#### Single-Statement Form

Run a single statement on the error path. Supported statements are `return`, `break`, `continue`, and `throw`:

```maxon
let value = try mayFail() otherwise return -1
```

Each of these statements terminates the error path, so the success value still flows out of the `try` expression normally. Use single-statement form when the error handler is a single early exit — for anything more complex, use the block form.

```maxon
// Early return on error
function runIt() returns int
		let value = try mayFail() otherwise return -1
		return value
end 'runIt'

// Bail out of a loop on error
while true 'loop'
		let v = try next() otherwise break
		total = total + v
end 'loop'

// Skip failed items
for item in items 'items'
		let parsed = try parse(item) otherwise continue
		results.append(parsed)
end 'items'

// Re-throw as a different error type
function outer() returns int throws OuterError
		let v = try inner() otherwise throw OuterError.failed
		return v
end 'outer'
```

Each statement has the same requirements it normally has: `break`/`continue` must be inside a loop, `throw` requires the enclosing function to declare `throws`, and `return`'s value must match the enclosing function's return type.

#### Block Handler Form

Execute a block of code when an error occurs:

```maxon
try readFile("config.json") otherwise 'handler'
		print("File not found, using defaults")
		useDefaults()
end 'handler'
```

The block executes only if an error is thrown.

```maxon
function loadData() returns int
		var result = 0
		try parseFile("data.txt") otherwise 'err'
				result = -1  // Mark as failed
				logError("Parse failed")
		end 'err'
		return result
end 'loadData'
```

#### Block with Error Binding

Capture the error as a typed enum for inspection:

```maxon
try readFile("config.json") otherwise (e) 'handler'
		match e 'check'
				notFound then print("File not found")
				permissionDenied then print("Permission denied")
				alreadyExists then print("Already exists")
		end 'check'
end 'handler'
```

The error is bound to the variable `e` as a typed enum value, allowing you to match on specific error cases. For error enums with associated values, you can extract the payload in the match arm.

```maxon
function processFile(path String)
		try readFile(path) otherwise (err) 'handler'
				match err 'kind'
						notFound then print("File not found")
						permissionDenied then print("Permission denied")
						alreadyExists then print("Already exists")
				end 'kind'
		end 'handler'
end 'processFile'
```

The `(e)` binding is a regular local variable, so the standard unused-variable check (E3012) rejects a binding that is declared but never read inside the handler. If you only need to detect failure and have no use for the typed error value, drop the binding and use the bare block form `try expr otherwise 'handler' ... end 'handler'` instead.

### Error Propagation

Use `try` without `otherwise` to propagate errors to the caller. This is only valid inside functions declared with `throws`:

```maxon
function loadConfig() returns Config throws FileError
		// If readFile throws, the error propagates to our caller
		let contents = try readFile("config.json")
		return parse(contents)
end 'loadConfig'
```

**Rules:**
- `try` without `otherwise` is only valid in functions with `throws`
- The propagated error type must be the same type as the enclosing function's declared error type. Propagation re-throws the callee's error value through the caller's error flag, so a type mismatch would cause the caller to decode bits of one enum as tags of another. If the types differ, add an `otherwise` clause to convert.
- Using `try` without `otherwise` in a non-throwing function is a compile error

### Try Block (Multi-Call Error Handling)

The `try 'label' ... end 'label' otherwise (e) 'handler' ... end 'handler'` construct wraps a sequence of statements so that every throwing call inside funnels its error to a single shared handler. Inside the body, bare calls to throwing functions do **not** require the `try` keyword — the compiler implicitly promotes them.

```maxon
try 'reading'
		let raw = readFile("config.json")
		let parsed = parseJson(raw)
		let port = parsed.get("port")
		print("{port}")
end 'reading'
otherwise (e) 'handler'
		match e 'kind'
				FileError.notFound        then print("missing")
				FileError.permissionDenied then print("denied")
				ParseError.unexpectedToken then print("bad json")
				MapError.missingKey       then print("no port")
		end 'kind'
end 'handler'
```

**Rules:**

- The `try` body MUST contain at least one bare call to a throwing function (E3083).
- The `otherwise` clause takes one of three forms:
	- **Block handler** — `otherwise (e) 'handler' ... end 'handler'` MUST contain a `match` on the binding (E3084).
	- **Terminal panic** — `otherwise [(e)] panic("message")` halts the program when the body throws.
	- **Terminal throws** — `otherwise [(e)] throws ErrorType.case` re-throws a fixed error to the caller. The enclosing function must declare `throws ErrorType`.
- The `(e)` binding is optional for the terminal forms. When supplied it has the same type as in the block-handler form (single enum or synthesized error union) and may be referenced inside the panic message's interpolation or inside the throw expression — for example, `otherwise (e) throws AppError.wrap(e)` to wrap the original error as a payload of the new error case. A binding declared but never read is rejected by the standard unused-variable check (E3012).
- If the body throws calls of a single error enum type, `e` has that enum type and match patterns use bare case names (e.g. `notFound`).
- If the body throws calls of two or more distinct error enum types, `e` has a synthesized error-union type. Each match arm targets a specific `EnumName.caseName` pair:
	- Fully-qualified form `EnumName.caseName` is always accepted.
	- Bare `caseName` is accepted only when the case name is unique across the union members. Shared names (e.g. two enums both with `notFound`) are rejected with E3085.
- The match must be exhaustive across every `(EnumName, caseName)` pair unless a `default` arm is provided.
- An explicit `try expr otherwise ...` inside the body still works for any single call — its error is consumed by its own handler and does not contribute to the synthesized union.
- Nested try blocks compose: the inner block absorbs its own throws; the outer block sees only what its own bare calls throw. A terminal `otherwise throws E.x` inside an inner try block routes through the outer block's shared error sink, just like a bare `throw`.

#### Terminal Form Examples

```maxon
// Panic when the body throws — useful for unreachable error paths.
try 'reading'
		parseFile("data.json")
end 'reading'
otherwise panic("unreachable: data.json is bundled with the binary")

// Re-throw a fixed error to the caller.
function compute() returns int throws AppError
		try 'work'
				doStuff()
		end 'work'
		otherwise throws AppError.failed
		return 0
end 'compute'

// Bind the original error and wrap it as the payload of the new error.
function compute2() returns int throws AppError
		try 'work'
				doStuff()
		end 'work'
		otherwise (e) throws AppError.wrap(e)
		return 0
end 'compute2'
```

### Conditional Try (if try)

The `if try` construct provides conditional execution based on whether a throwing expression succeeds.

#### Boolean Form

Check if an expression succeeds without binding the result:

```maxon
if try mayFail() 'check'
		print("Success!")
end 'check'
```

The if-block executes only if the expression succeeds (doesn't throw).

#### Binding Form

Unwrap and bind the success value:

```maxon
if let value = try mayFail() 'check'
		print("Got: {value}")
end 'check'
```

If successful, the unwrapped value is bound to `value` and available within the if-block.

#### Mutable Binding Form

Use `var` instead of `let` to make the bound name reassignable inside the then-block:

```maxon
if var value = try mayFail() 'check'
		value = value + 10
		return value
end 'check'
```

The binding is scoped to the then-block; mutations do not propagate back to the source expression.

#### With Else Clause

Handle the error case:

```maxon
if try mayFail() 'check'
		print("Success!")
end 'check' else 'err'
		print("Failed!")
end 'err'
```

#### With Error Binding

Capture the error value in the else block:

```maxon
if let value = try mayFail() 'check'
		print("Got: {value}")
end 'check' else (e) 'err'
		print("Error occurred")
end 'err'
```

The error is bound to `e` and available within the else-block.

### Standard Library Error Types

The standard library provides error types for built-in operations:

```maxon
// Array access errors
enum ArrayError implements Error
		indexOutOfBounds  // index >= length
		emptySlot         // slot pointer is null (e.g. after resize() without push())
end 'ArrayError'

// Map operations
enum MapError implements Error
		keyNotFound
		keyAlreadyExists
end 'MapError'

// Iterator exhaustion
enum IterationError implements Error
		exhausted
end 'IterationError'

// File metadata errors
enum FileInfoError implements Error
		notFound              // file does not exist
end 'FileInfoError'
```

Array and Map access methods throw these errors:

```maxon
var arr = [1, 2, 3]
let val = try arr.get(5) otherwise 0  // Returns 0 on out of bounds

var map = ["key": 42]
let result = try map.get("missing") otherwise -1  // Returns -1 if key not found
```

### Error Enum Types

Functions with `throws` return an error type internally:

```maxon
// This function returns "String or FileError" internally
function readFile(path String) returns String throws FileError
```

**Memory Layout:**
```text
+--------+--------------------------------+
| tag(8) | value OR error ordinal         |
+--------+--------------------------------+
   0=ok    success value
   1=err   enum ordinal (8 bytes)
```

### Complete Example

```maxon
enum ParseError implements Error
		invalidSyntax
		unexpectedEnd
end 'ParseError'

function parseNumber(s String) returns int throws ParseError
		if s.isEmpty() 'empty'
				throw ParseError.unexpectedEnd
		end 'empty'
		// parsing logic...
		return result
end 'parseNumber'

function main() returns ExitCode
		// Use default value on error
		let num1 = try parseNumber("42") otherwise 0
		
		// Handle error in block
		var num2 = 0
		try parseNumber("invalid") otherwise 'err'
				num2 = -1
		end 'err'
		
		// Handle with error binding
		try parseNumber("") otherwise (e) 'handler'
				match e 'kind'
						invalidSyntax then print("Invalid syntax")
						unexpectedEnd then print("Unexpected end of input")
				end 'kind'
		end 'handler'
		
		return num1
end 'main'
```

---
