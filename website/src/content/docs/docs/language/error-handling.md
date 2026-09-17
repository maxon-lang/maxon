---
title: Error Handling
description: Throwing functions, try ... otherwise, try blocks, and error propagation.
sidebar:
  order: 10
---

Maxon has two kinds of failure:

- **Errors** are expected outcomes — a missing file, invalid input. A function declares the error type it
  `throws`, and every caller handles it with `try`. There are no exceptions that unwind silently, no null
  values and no optional types.
- **Panics** are bugs — a broken invariant, an out-of-range value reaching a checked place. A panic stops
  the program with a message and a stack trace, and cannot be caught.

## Defining Error Types

An error type is an `enum` or a `union` that implements `Error`:

```maxon
typealias HttpCode = int(100 to 599)

enum FileError implements Error
	notFound
	permissionDenied
	alreadyExists
end 'FileError'

union FetchError implements Error
	timedOut
	status(code HttpCode)
end 'FetchError'
```

A union error carries data about the failure, read back with `match`. Only enum and union values can be
thrown; throwing a record is **E3005** (`throw requires an error enum value`).

## Throwing Functions

A function that can fail declares its error type with `throws`, after any `returns`:

```maxon
typealias Amount = int(i64.min to i64.max)

enum ParseError implements Error
	empty
	invalidSyntax
end 'ParseError'

function parseDigit(s String) returns Amount throws ParseError
	if s.isEmpty() 'empty'
		throw ParseError.empty
	end 'empty'

	return match s 'digit'
		"0" gives 0
		"1" gives 1
		"2" gives 2
		default throws ParseError.invalidSyntax
	end 'digit'
end 'parseDigit'

function requireName(name String) throws ParseError
	if name.isEmpty() 'missing'
		throw ParseError.empty
	end 'missing'
end 'requireName'
```

`throw` is legal only in a function that declares `throws`, and the value must be of the declared type.

## Panic

`panic("message")` stops the program. The message may be interpolated. The program writes the message with
its source location and a stack trace to stderr and exits with code **1**:

```maxon
typealias Amount = int(i64.min to i64.max)

function processValue(x Amount) returns Amount
	if x < 0 'negative'
		panic("processValue: negative input, got {x}")
	end 'negative'

	return x * 2
end 'processValue'

function main() returns ExitCode
	print("{processValue(-3)}\n")
	return 0
end 'main'
```

```text
panic at main.maxon:5: processValue: negative input, got -3
Stack trace:
  in processValue
  in main
  in mrt_start
```

The trace lists the call chain innermost first, up to 100 frames. The runtime raises the same kind of panic
for a failed [range check](/docs/language/ranged-typealiases/#range-checks), a negative shift count and `i64.min / -1`
(`panic: integer overflow`). A recursion that outgrows its thread's stack stops with
`panic: stack overflow` and the same trace on every native target; on `wasm32-wasi` it is the engine's own
trap.

Use `panic` for invariant violations and unreachable paths; use `throw` for conditions a caller should
handle.

## Calling Throwing Functions

Every call to a throwing function is marked with `try`. A call without it is **E3057** (`throwing function
requires try`). `try` is followed by exactly one of:

- **nothing** — propagate the error to the caller ([Error Propagation](#error-propagation)), or
- an **`otherwise`** clause that handles it.

## Handling Errors with `otherwise`

### Default Value

```maxon
let digit = try parseDigit(text) otherwise 0
```

If the call throws, the expression takes the fallback value, which must have the call's result type.

### Ignore

```maxon
try requireName(name) otherwise ignore
```

Discards the error. Reserve it for best-effort work such as cleanup.

### Panic

```maxon
let slot = try slots.get(index) otherwise panic("unreachable: index was validated")
```

Turns an error that cannot happen into a panic, instead of hiding it behind a made-up default.

### Single Statement

`return`, `break`, `continue` or `throw` on the error path:

```maxon
typealias Amount = int(i64.min to i64.max)
typealias StringArray = Array with String

enum AppError implements Error
	badInput
end 'AppError'

function firstDigitOrMinusOne(text String) returns Amount
	let value = try parseDigit(text) otherwise return -1
	return value
end 'firstDigitOrMinusOne'

function sumDigits(parts StringArray) returns Amount
	var total = 0
	for part in parts 'each'
		let d = try parseDigit(part) otherwise continue      // skip bad parts
		total = total + d
	end 'each'

	return total
end 'sumDigits'

function strictDigit(text String) returns Amount throws AppError
	return try parseDigit(text) otherwise throw AppError.badInput   // convert the error
end 'strictDigit'
```

Each statement follows its usual rules: `break` and `continue` need a loop, `throw` needs a `throws` clause.

### Block Handler

```maxon
try parseDigit(text) otherwise 'failed'
	print("could not parse {text}\n")
	return 1
end 'failed'
```

### Block with Error Binding

`otherwise (e)` binds the error value for a `match`:

```maxon
try parseDigit(text) otherwise (e) 'failed'
	match e 'kind'
		empty then print("no input\n")
		invalidSyntax then print("not a digit\n")
	end 'kind'
end 'failed'
```

The binding must be used (**E3012**); if the kind of error does not matter, use the plain block form.

## Error Propagation

A bare `try` passes the error on to the caller. It is legal only in a function that declares `throws` with
the **same** error type:

```maxon
function doubleDigit(text String) returns Amount throws ParseError
	let d = try parseDigit(text)       // a ParseError propagates to our caller
	return d * 2
end 'doubleDigit'
```

- In a function with no `throws` clause it is **E3059** (`the error has nowhere to go`).
- When the callee's error type differs from the function's, it is **E3059** (`try propagates 'E' but
  enclosing function throws 'Other' — add 'otherwise' to convert`); convert with
  `otherwise throw Other.case`.
- Inside a [test](/docs/language/testing/), a bare `try` on any error type is allowed: an error that reaches
  it fails the test.

## Try Blocks

A `try` block runs several statements and sends every error to one handler. Inside the block, calls to
throwing functions need no `try` of their own:

```maxon
typealias Amount = int(i64.min to i64.max)

enum FileError implements Error
	notFound
end 'FileError'

enum ConfigError implements Error
	badPort
end 'ConfigError'

function readConfig(name String) returns String throws FileError
	if name == "missing" 'absent'
		throw FileError.notFound
	end 'absent'

	return "port=80"
end 'readConfig'

function parsePort(text String) returns Amount throws ConfigError
	if text == "port=80" 'ok'
		return 80
	end 'ok'

	throw ConfigError.badPort
end 'parsePort'

function load(name String)
	try 'reading'
		let raw = readConfig(name)
		let port = parsePort(raw)
		print("port {port}\n")
	end 'reading'
	otherwise (e) 'handler'
		match e 'kind'
			FileError.notFound then print("missing\n")
			ConfigError.badPort then print("bad config\n")
		end 'kind'
	end 'handler'
end 'load'

function main() returns ExitCode
	load("app")        // port 80
	load("missing")    // missing
	return 0
end 'main'
```

- The block must contain at least one call that throws (**E3083**).
- The `otherwise` clause is one of:
  - a **handler block** `otherwise (e) 'label' … end 'label'`, which must `match` on the binding (**E3084**);
  - **`otherwise [(e)] panic("message")`**, which panics if the block throws;
  - **`otherwise [(e)] throws ErrorType.case`**, which throws a fixed error to the caller — the binding
    can be wrapped as a payload: `otherwise (e) throws AppError.wrap(e)`.
- When the block's calls throw one error type, `e` has that type and arms use bare case names. When they
  throw several, `e` is a combined error: arms name `ErrorType.case`, and a bare case name is accepted only
  when it is unique across the types (**E3085** otherwise). The match is exhaustive over every pair unless
  it has a `default` arm.
- A call inside the block with its own `try … otherwise` handles its own error, which does not reach the
  block's handler. Nested try blocks compose the same way.

## Conditional Try (`if let … = try`)

`if let` runs a block only when a throwing call succeeds, binding its result:

```maxon
if let value = try parseDigit(text) 'parsed'
	print("got {value}\n")
end 'parsed' else (e) 'failed'
	match e 'kind'
		empty then print("no input\n")
		invalidSyntax then print("not a digit\n")
	end 'kind'
end 'failed'
```

- `if var value = try …` makes the binding reassignable inside the block.
- The `else` block is optional, and so is its `(e)` binding.
- `if try call() 'label'`, with no binding, is only for a call that returns nothing; testing a call that
  returns a value that way discards the value and is **E3124**.

## Errors in Other Positions

- A match arm or match expression can throw with `throws ErrorType.case` or `default throws` (see
  [Default Throws and Default Panic](/docs/language/statements/#default-throws-and-default-panic)).
- A promise from a throwing `async` call is awaited with `try await` (see
  [Concurrency](/docs/language/async/#throwing-async-functions)).
- A division whose divisor might be zero throws `DivisionByZero` (see
  [Division by Zero](/docs/language/expressions/#division-by-zero)).
- A function type cannot declare `throws`, so a throwing function cannot be used as a function value
  (**E3101**).

## Standard Library Error Types

| Error | Cases | Thrown by |
|-------|-------|-----------|
| `ArrayError` | `indexOutOfBounds`, `emptySlot` | `Array` element access |
| `MapError` | `keyNotFound`, `keyAlreadyExists` | `Map.get`, `Map.insert` |
| `IterationError` | `exhausted`, `atStart` | iterators |
| `StringError` | `notFound`, `invalidIndex` | `String` search and indexing |
| `ParseError` | `invalidFormat` | `int.fromString`, `float.fromString`, `bool.fromString` |
| `DivisionByZero` | `divisionByZero` | `/` and `mod` with a divisor that may be zero |
| enum lookups | `noSuchCaseName`, `noSuchRawValue` | `fromName`, `fromRawValue` |

The [standard library reference](/docs/stdlib/) lists the error types of each module.
