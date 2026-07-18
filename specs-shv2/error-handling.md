---
feature: error-handling
status: experimental
keywords: [error, throw, try, otherwise, throws, Error]
category: error-handling
---

# Error Handling

## Documentation

### Defining Error Types

Error types must be enums that conform to the `Error` interface:

```maxon
// Simple enum error
enum FileError implements Error
	notFound
	permissionDenied
	alreadyExists
end 'FileError'

// Int-backed enum error (for error codes)
enum HttpError implements Error
	badRequest = 400
	notFound = 404
	serverError = 500
end 'HttpError'

// String-backed enum error (for messages)
enum ValidationError implements Error
	emptyField = "Field cannot be empty"
	invalidFormat = "Invalid format"
end 'ValidationError'
```

### Throwing Functions

Annotate functions that can throw with `throws ErrorType`:

```maxon
function readFile(path string) returns string throws FileError
	if not exists(path) 'check'
		throw FileError.notFound
	end 'check'
	return contents
end 'readFile'
```

### Error Handling with `otherwise`

The `otherwise` keyword provides unified error handling for throwing expressions. There are five forms:

#### Default Value Form

Provide a default value when an error occurs:

```maxon
let value = try mayFail() otherwise 42
```

If `mayFail()` throws, `value` is assigned `42`. The default expression must match the return type.

#### Ignore Form

Discard errors when you don't need the result:

```maxon
try mayFail() otherwise ignore
```

This silently ignores any thrown error. Use sparingly.

#### Single-Statement Form

Run a single `return`, `break`, `continue`, or `throw` statement on the error path:

```maxon
let value = try mayFail() otherwise return -1
```

Each of these statements terminates the error path, so the success value still flows out of the `try` expression normally. Use the block form instead when the error handler needs more than one statement.

#### Block Handler Form

Execute a block of code when an error occurs:

```maxon
try readFile("config.json") otherwise 'handler'
	print("File not found, using defaults")
	useDefaults()
end 'handler'
```

The block executes only if an error is thrown.

#### Block with Error Binding

Capture the error for inspection:

```maxon
try readFile("config.json") otherwise (e) 'handler'
	match e 'check'
		notFound then print("File not found")
		permissionDenied then print("Permission denied")
		alreadyExists then print("Already exists")
	end 'check'
end 'handler'
```

The error is bound to `e` as a typed enum value within the block. You can use `match` to dispatch on specific error cases. For error enums with associated values, you can extract the payload:

```maxon
typealias Score = int(i64.min to i64.max)

union MyError implements Error
	notFound(code Score)
	failed
end 'MyError'

try doWork() otherwise (e) 'handler'
	match e 'check'
		notFound(code) then print(code)
		failed then print("failed")
	end 'check'
end 'handler'
```

### Error Propagation

Use `try` without `otherwise` to propagate errors to the caller (only valid in functions with `throws`):

```maxon
function loadConfig() returns Config throws FileError
	let contents = try readFile("config.json")
	return parse(contents)
end 'loadConfig'
```

## Tests

<!-- test: error.enum-simple-error -->
```maxon
// Simple enum error type
enum MyError implements Error
	invalidInput
	notFound
end 'MyError'

function main() returns ExitCode
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: error.enum-int-backed-error -->
```maxon
// Int-backed enum error type (type inferred from values)
enum MyError implements Error
	invalidInput = 1
	notFound = 404
end 'MyError'

function main() returns ExitCode
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: error.parse-throws-function-signature -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Functions can declare they throw a specific error type
enum MyError implements Error
	failed
end 'MyError'

// This function signature declares it throws MyError
function mayFail() returns Integer throws MyError
	return 10
end 'mayFail'

function main() returns ExitCode
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: error.throw-and-return-success -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Test that throwing function can return success value
enum MyError implements Error
	failed
end 'MyError'

function mayFail(shouldFail bool) returns Integer throws MyError
	if shouldFail 'check'
		throw MyError.failed
	end 'check'
	return 42
end 'mayFail'

function main() returns ExitCode
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: error.propagate-error-to-caller -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Test error propagation: inner function throws, middle propagates, outer handles with otherwise
enum MyError implements Error
	failed
end 'MyError'

function inner() returns Integer throws MyError
	throw MyError.failed
end 'inner'

function middle() returns Integer throws MyError
	let x = try inner()
	return x
end 'middle'

function main() returns ExitCode
	let x = try middle() otherwise 99
	return x
end 'main'
```
```exitcode
99
```

<!-- test: error.otherwise-default-value -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Test try otherwise with default value
enum MyError implements Error
	failed
end 'MyError'

function mayFail() returns Integer throws MyError
	throw MyError.failed
end 'mayFail'

function main() returns ExitCode
	let val = try mayFail() otherwise 42
	return val
end 'main'
```
```exitcode
42
```

<!-- test: error.otherwise-default-success -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Test try otherwise when no error occurs
enum MyError implements Error
	failed
end 'MyError'

function mayFail(shouldFail bool) returns Integer throws MyError
	if shouldFail 'check'
		throw MyError.failed
	end 'check'
	return 100
end 'mayFail'

function main() returns ExitCode
	let val = try mayFail(false) otherwise 42
	return val
end 'main'
```
```exitcode
100
```

<!-- test: error.otherwise-ignore -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Test try otherwise ignore
enum MyError implements Error
	failed
end 'MyError'

var counter = 0 as Integer

function mayFail() returns Integer throws MyError
	counter = counter + 1
	throw MyError.failed
end 'mayFail'

function main() returns ExitCode
	try mayFail() otherwise ignore
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: error.otherwise-block -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Test try otherwise block handler
enum MyError implements Error
	failed
end 'MyError'

var counter = 0 as Integer

function mayFail() returns Integer throws MyError
	counter = counter + 1
	throw MyError.failed
end 'mayFail'

function main() returns ExitCode
	var result = 0
	try mayFail() otherwise 'err'
		result = 42
	end 'err'
	return result
end 'main'
```
```exitcode
42
```

<!-- test: error.otherwise-block-success -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Test try otherwise block when no error
enum MyError implements Error
	failed
end 'MyError'

var counter = 0 as Integer

function mayFail(shouldFail bool) returns Integer throws MyError
	counter = counter + 1
	if shouldFail 'check'
		throw MyError.failed
	end 'check'
	return 100
end 'mayFail'

function main() returns ExitCode
	var result = 0
	try mayFail(false) otherwise 'err'
		result = 42
	end 'err'
	return result
end 'main'
```
```exitcode
0
```

<!-- test: error.otherwise-block-with-binding -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Test try otherwise block with error binding - block is entered on error
enum MyError implements Error
	failed
end 'MyError'

var counter = 0 as Integer

function mayFail() returns Integer throws MyError
	counter = counter + 1
	throw MyError.failed
end 'mayFail'

function main() returns ExitCode
	var caught = 0
	try mayFail() otherwise (e) 'handler'
		match e 'kind'
			failed then caught = 42
		end 'kind'
	end 'handler'
	return caught
end 'main'
```
```exitcode
42
```

<!-- test: error.main-cannot-throw -->
```maxon
// main cannot be declared with throws
enum MyError implements Error
	failed
end 'MyError'

function main() returns ExitCode throws MyError
	return 42
end 'main'
```
```maxoncstderr
error E3054: specs/fragments/error-handling/error.main-cannot-throw.test:7:10: main cannot throw: 'main'
```

<!-- test: error.managed-return-throws-unsupported -->
```maxon
// A throwing function whose return type is MANAGED (a String, a struct box, or a
// boxed union) has no value to hand back on the error path, so its adopted result
// has nothing valid to drop. Rejected at the declaration until P1.4b wave 2.
enum E implements Error
	bad
end 'E'

function mk() returns String throws E
	throw E.bad
end 'mk'

function main() returns ExitCode
	let s = try mk() otherwise "x"
	return s.byteLength()
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/error-handling/error.managed-return-throws-unsupported.test:9:10: Unsupported: a throwing function with a managed return type (String, struct, or union) is not yet supported — its error path yields no value, so a managed result would have nothing valid to drop; return a scalar and signal failure through the thrown error (managed error values arrive at P1.4b wave 2)
```

<!-- test: error.otherwise-type-mismatch -->
```maxon

typealias Integer = int(i64.min to i64.max)

// otherwise expression type must match the success type
enum MyError implements Error
	failed
end 'MyError'

function mayFail() returns Integer throws MyError
	throw MyError.failed
end 'mayFail'

function main() returns ExitCode
	let val = try mayFail() otherwise 5.0
	return val
end 'main'
```
```maxoncstderr
error E3059: specs/fragments/error-handling/error.otherwise-type-mismatch.test:15:12: type mismatch: 'otherwise type 'float' does not match expected type 'int''
```

<!-- test: error.throwing-function-requires-try -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Calling a throwing function without try is an error
enum MyError implements Error
	failed
end 'MyError'

function mayFail() returns Integer throws MyError
	return 42
end 'mayFail'

function main() returns ExitCode
	let val = mayFail()
	return val
end 'main'
```
```maxoncstderr
error E3057: specs/fragments/error-handling/error.throwing-function-requires-try.test:15:12: throwing function requires try: 'mayFail'
```

<!-- test: error.throwing-method-requires-try -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail() returns Integer throws MyError
	return 42
end 'mayFail'

function main() returns ExitCode
	let val = mayFail()
	return val
end 'main'
```
```maxoncstderr
error E3057: specs/fragments/error-handling/error.throwing-method-requires-try.test:13:12: throwing function requires try: 'mayFail'
```

<!-- test: error.try-on-non-throwing-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Using try on a non-throwing function is an error
function noFail() returns Integer
	return 42
end 'noFail'

function main() returns ExitCode
	let val = try noFail() otherwise 0
	return val
end 'main'
```
```maxoncstderr
error E3055: specs/fragments/error-handling/error.try-on-non-throwing-function.test:11:12: try requires a throwing function: 'noFail' does not throw'
```

<!-- test: error.try-on-non-throwing-method -->
```maxon
typealias Int = int(i64.min to i64.max)

function foo() returns Int
  return 42
end 'foo'

// Using try on a non-throwing method is an error
function main() returns ExitCode
	let val = try foo() otherwise 0
	return val
end 'main'
```
```maxoncstderr
error E3055: specs/fragments/error-handling/error.try-on-non-throwing-method.test:10:12: try requires a throwing function: 'foo' does not throw'
```

<!-- test: error.try-on-non-throwing-instance-method -->

Regression: `try recv.method()` on a non-throwing **instance method** must also
report E3055. The error-handling check originally inspected only `call` /
`tryCall` ops, so a `tryMethodCall` (the op a method receiver produces) slipped
through and the spurious `try` was silently accepted — diverging from the C#
bootstrap. The check now resolves the method's qualified callee via
`resolvedCallees` and reports E3055 when it is registered non-throwing.

```maxon
typealias Int = int(i64.min to i64.max)

type Counter
	export var value = 0

	export static function create() returns Self
		return Self{}
	end 'create'

	export function bump() returns Int
		return self.value + 1
	end 'bump'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	let v = try c.bump() otherwise 0
	return v
end 'main'
```
```maxoncstderr
error E3055: specs/fragments/error-handling/error.try-on-non-throwing-instance-method.test:18:10: try requires a throwing function: 'Counter.bump' does not throw'
```

<!-- test: error.otherwise-without-try -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail() returns Integer throws MyError
	return 42
end 'mayFail'

function main() returns ExitCode
	let val = mayFail() otherwise 0
	return val
end 'main'
```
```maxoncstderr
error E3058: specs/fragments/error-handling/error.otherwise-without-try.test:13:22: otherwise requires try expression
```

<!-- test: error.otherwise-ignore-in-assignment -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Using 'otherwise ignore' in an assignment is an error
enum MyError implements Error
	failed
end 'MyError'

function mayFail() returns Integer throws MyError
	throw MyError.failed
end 'mayFail'

function main() returns ExitCode
	let val = try mayFail() otherwise ignore
	return val
end 'main'
```
```maxoncstderr
error E3059: specs/fragments/error-handling/error.otherwise-ignore-in-assignment.test:15:12: type mismatch: ''otherwise ignore' cannot be used in assignment'
```

<!-- test: error.void-try-in-assignment -->
```maxon
// Assigning from a void-returning try call is an error
enum MyError implements Error
	failed
end 'MyError'

function mayFail() throws MyError
	throw MyError.failed
end 'mayFail'

function main() returns ExitCode
	let val = try mayFail() otherwise 'handler'
		return 1
	end 'handler'
	return 0
end 'main'
```
```maxoncstderr
error E3059: specs/fragments/error-handling/error.void-try-in-assignment.test:12:12: type mismatch: ''mayFail' does not return a value'
```

<!-- test: error.binding-match-single-case -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Test matching on typed error binding
enum MyError implements Error
	failed
end 'MyError'

var counter = 0 as Integer

function mayFail() returns Integer throws MyError
	counter = counter + 1
	throw MyError.failed
end 'mayFail'

function main() returns ExitCode
	var result = 0
	try mayFail() otherwise (e) 'handler'
		match e 'check'
			failed then result = 42
		end 'check'
	end 'handler'
	return result
end 'main'
```
```exitcode
42
```

<!-- test: error.binding-match-multi-case -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Test matching on error binding with multiple cases
enum MyError implements Error
	failed
	timeout
	notFound
end 'MyError'

var counter = 0 as Integer

function mayFail(code Integer) returns Integer throws MyError
	counter = counter + 1
	if code == 1 'c1'
		throw MyError.failed
	end 'c1'
	if code == 2 'c2'
		throw MyError.timeout
	end 'c2'
	throw MyError.notFound
end 'mayFail'

function main() returns ExitCode
	var result = 0
	try mayFail(2) otherwise (e) 'handler'
		match e 'check'
			failed then result = 10
			timeout then result = 20
			notFound then result = 30
		end 'check'
	end 'handler'
	return result
end 'main'
```
```exitcode
20
```

<!-- test: error.binding-success-no-block -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Test that error binding block is skipped on success
enum MyError implements Error
	failed
end 'MyError'

var counter = 0 as Integer

function mayFail(shouldFail bool) returns Integer throws MyError
	counter = counter + 1
	if shouldFail 'check'
		throw MyError.failed
	end 'check'
	return 100
end 'mayFail'

function main() returns ExitCode
	var result = 0
	try mayFail(false) otherwise (e) 'handler'
		match e 'kind'
			failed then result = 99
		end 'kind'
	end 'handler'
	return result
end 'main'
```
```exitcode
0
```

<!-- test: error.assoc-value-throw-catch -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Test error enum with associated value - throw and catch
union MyError implements Error
	notFound(code Integer)
	failed
end 'MyError'

var counter = 0 as Integer

function mayFail() returns Integer throws MyError
	counter = counter + 1
	throw MyError.notFound(42)
end 'mayFail'

function main() returns ExitCode
	var result = 0
	try mayFail() otherwise (e) 'handler'
		match e 'check'
			notFound(code) then result = code
			failed then result = 1
		end 'check'
	end 'handler'
	return result
end 'main'
```
```exitcode
42
```

<!-- test: error.assoc-value-throw-catch-2 -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Test error enum with associated value - second case
union MyError implements Error
	notFound(code Integer)
	failed
end 'MyError'

var counter = 0 as Integer

function mayFail() returns Integer throws MyError
	counter = counter + 1
	throw MyError.notFound(42)
end 'mayFail'

function main() returns ExitCode
	var result = 0
	try mayFail() otherwise (e) 'handler'
		match e 'check'
			notFound(code) then result = code
			failed then result = 0
		end 'check'
	end 'handler'
	return result
end 'main'
```
```exitcode
42
```

<!-- disabled-test: error.file-private-union-caught-cross-file -->
<!-- [wave2-managed] union error payload -->
```maxon
// --- file: liberr.maxon
typealias Code = int(i64.min to i64.max)

// File-private (no `export`/`module`) error union: the catch site in another
// file only ever learns of this type through `risky`'s `throws` clause, so it
// is never seeded into the consumer file's type registry during pre-scan.
union LibErr implements Error
	bad(code Code)
end 'LibErr'

export function risky(n Code) returns Code throws LibErr
	if n > 5 'big'
		throw LibErr.bad(42)
	end 'big'
	return n
end 'risky'

// --- file: main.maxon
function main() returns ExitCode
	var result = 0
	try risky(9) otherwise (e) 'handler'
		match e 'check'
			bad(code) then result = code
		end 'check'
	end 'handler'
	return result
end 'main'
```
```exitcode
42
```

<!-- disabled-test: error.cross-file-throws-caught-later-file -->
<!-- [wave2-managed] union error payload -->
```maxon
// --- file: app.maxon
// The throwing callee `risky` is declared in `zzz.maxon`, which sorts AFTER
// this file. The single-pass parser parses `app.maxon` first, so without a
// project-wide throws-clause prescan the `(e)` binding here can't recover
// `risky`'s error union — it falls back to `integer` and the `match e` below
// reports E3005 "match scrutinee must be an enum-typed value". Seeding every
// file's `throws` clause before any body is parsed keeps `e` typed as `Woe`.
function main() returns ExitCode
	var result = 0
	try risky(9) otherwise (e) 'handler'
		match e 'check'
			tooBig(by) then result = by
			negative then result = 1
		end 'check'
	end 'handler'
	return result
end 'main'

// --- file: zzz.maxon
typealias Code = int(i64.min to i64.max)

export union Woe implements Error
	tooBig(by Code)
	negative
end 'Woe'

export function risky(n Code) returns Code throws Woe
	if n > 5 'big'
		throw Woe.tooBig(7)
	end 'big'
	return n
end 'risky'
```
```exitcode
7
```

<!-- test: error.otherwise-block-reused-binding -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Regression: two sibling `try ... otherwise (e) 'label' ... end 'label'`
// blocks reusing the same binding name `e` but with different error types
// (one associated-value enum, one simple enum). Without per-block scoping,
// the first block's managed-type registration of `e` persists and the
// function epilogue incorrectly decrefs the second block's integer `e`.
union AssocError implements Error
	withCode(code Integer)
	plain
end 'AssocError'

enum SimpleError implements Error
	broken
end 'SimpleError'

function mayFailAssoc() returns Integer throws AssocError
	throw AssocError.withCode(7)
end 'mayFailAssoc'

function mayFailSimple() returns Integer throws SimpleError
	throw SimpleError.broken
end 'mayFailSimple'

function main() returns ExitCode
	var result = 0
	try mayFailAssoc() otherwise (e) 'handler1'
		match e 'check1'
			withCode(code) then result = result + code
			plain then result = result + 1
		end 'check1'
	end 'handler1'
	try mayFailSimple() otherwise (e) 'handler2'
		match e 'check2'
			broken then result = result + 35
		end 'check2'
	end 'handler2'
	return result
end 'main'
```
```exitcode
42
```

<!-- test: error.otherwise-return -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Single-statement otherwise: return on error
enum MyError implements Error
	failed
end 'MyError'

function mayFail() returns Integer throws MyError
	throw MyError.failed
end 'mayFail'

function runIt() returns Integer
	let value = try mayFail() otherwise return -1
	return value
end 'runIt'

function main() returns ExitCode
	let v = runIt()
	if v == -1 'check'
		return 99
	end 'check'
	return 0
end 'main'
```
```exitcode
99
```

<!-- test: error.otherwise-return-in-assignment -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Single-statement otherwise: success path still yields a value
enum MyError implements Error
	failed
end 'MyError'

function maybeFail(flag bool) returns Integer throws MyError
	if flag 'check'
		throw MyError.failed
	end 'check'
	return 42
end 'maybeFail'

function runIt(flag bool) returns Integer
	let value = try maybeFail(flag) otherwise return -1
	return value
end 'runIt'

function main() returns ExitCode
	let good = runIt(false)
	if good == 42 'checkGood'
		let bad = runIt(true)
		if bad == -1 'checkBad'
			return 7
		end 'checkBad'
	end 'checkGood'
	return 0
end 'main'
```
```exitcode
7
```

<!-- disabled-test: error.otherwise-return-managed-struct -->
<!-- [wave2-managed] managed struct try-result on the error edge -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Regression: when a try call returns a heap-managed struct and the otherwise
// branch returns early, the uninitialized __try_result_ slot must not be
// decref'd on the error path (would crash mm_decref with garbage pointer).
enum MyError implements Error
	failed
end 'MyError'

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function makeBox(flag bool) returns Box throws MyError
	if flag 'check'
		throw MyError.failed
	end 'check'
	return Box.create(7)
end 'makeBox'

function getBoxValue(flag bool) returns Integer
	let box = try makeBox(flag) otherwise return -1
	return box.value
end 'getBoxValue'

function main() returns ExitCode
	let good = getBoxValue(false)
	if good == 7 'g'
		let bad = getBoxValue(true)
		if bad == -1 'b'
			return 0
		end 'b'
	end 'g'
	return 1
end 'main'
```
```exitcode
0
```

<!-- disabled-test: error.otherwise-return-string -->
<!-- [wave2-managed] managed String try-result on the error edge -->
```maxon

// Regression: try returning String + otherwise return <string literal>.
// Mirrors the shape of the original maxonOpIdxToString segfault in the
// self-hosted compiler.
enum MyError implements Error
	failed
end 'MyError'

function tryIt(flag bool) returns String throws MyError
	if flag 'c'
		throw MyError.failed
	end 'c'
	return "ok"
end 'tryIt'

function wrap(flag bool) returns String
	let s = try tryIt(flag) otherwise return "??"
	return s
end 'wrap'

function main() returns ExitCode
	let a = wrap(false)
	let b = wrap(true)
	if a == "ok" 'x'
		if b == "??" 'y'
			return 0
		end 'y'
	end 'x'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: error.otherwise-break -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Single-statement otherwise: break out of enclosing loop on error
enum MyError implements Error
	failed
end 'MyError'

var counter = 0 as Integer

function mayFail() returns Integer throws MyError
	counter = counter + 1
	if counter == 3 'check'
		throw MyError.failed
	end 'check'
	return counter
end 'mayFail'

function main() returns ExitCode
	var total = 0
	while true 'loop'
		let v = try mayFail() otherwise break
		total = total + v
	end 'loop'
	return total
end 'main'
```
```exitcode
3
```

<!-- test: error.otherwise-continue -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Single-statement otherwise: continue to next iteration on error
enum MyError implements Error
	failed
end 'MyError'

var counter = 0 as Integer

function mayFail() returns Integer throws MyError
	counter = counter + 1
	if counter == 2 'check'
		throw MyError.failed
	end 'check'
	return counter
end 'mayFail'

function main() returns ExitCode
	var total = 0
	var iter = 0
	while iter < 4 'loop'
		iter = iter + 1
		let v = try mayFail() otherwise continue
		total = total + v
	end 'loop'
	return total
end 'main'
```
```exitcode
8
```

<!-- test: error.otherwise-throw -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Single-statement otherwise: rethrow a different error type
enum InnerError implements Error
	low
end 'InnerError'

enum OuterError implements Error
	high
end 'OuterError'

function inner() returns Integer throws InnerError
	throw InnerError.low
end 'inner'

function outer() returns Integer throws OuterError
	let v = try inner() otherwise throw OuterError.high
	return v
end 'outer'

function main() returns ExitCode
	let v = try outer() otherwise 77
	return v
end 'main'
```
```exitcode
77
```

<!-- test: error.rethrow-caught-binding -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Re-throw a caught `(e)` binding from an expression-form `try ... otherwise (e)`
// handler. `outer` catches `inner`'s error into `e`, then `throw e` re-publishes
// the SAME error to `outer`'s caller. The propagated variant must survive the
// hop: `main` distinguishes `low` (1) from `high` (7) by re-throwing it again
// and matching at the top, proving the ordinal carries through every re-throw.
enum Fault implements Error
	low
	high
end 'Fault'

function inner() returns Integer throws Fault
	throw Fault.high
end 'inner'

function outer() returns Integer throws Fault
	return try inner() otherwise (e) 'h'
		throw e
	end 'h'
end 'outer'

function main() returns ExitCode
	var code = 0 as ExitCode
	try outer() otherwise (e) 'top'
		match e 'm'
			low then code = 1
			high then code = 7
		end 'm'
	end 'top'
	return code
end 'main'
```
```exitcode
7
```


<!-- test: error.string-payload-throw-catch -->
```maxon

// A MANAGED (String) payload NESTED in an error union — the only shape that proves the
// caught box's cascade drops the payload before freeing the box. `throw E.failed("boom")`
// hands the box to the caller; `otherwise (e)` adopts it; `match e { failed(m) ... }` moves
// the String out (using it, then dropping it at the arm's end) and the box is freed once by
// its `__destruct_E` cascade (which null-guards the moved-out slot). A leak or double-free of
// either the String or the box is exit 101; a clean `"boom".byteLength()` is 4.
union E implements Error
	failed(msg String)
end 'E'

function mk() returns ExitCode throws E
	throw E.failed("boom")
end 'mk'

function main() returns ExitCode
	var n = 0 as ExitCode
	try mk() otherwise (e) 'h'
		match e 'm'
			failed(msg) then n = msg.byteLength() as ExitCode
		end 'm'
	end 'h'
	return n
end 'main'
```
```exitcode
4
```

<!-- test: error.otherwise-no-binding-boxed-decref -->
```maxon

typealias Integer = int(i64.min to i64.max)

// A binding-less `otherwise` block catching a BOXED (associated-value) error must decref the
// transferred box exactly once — the handler names no `e`, so the release is implicit. (Positional
// twin of throw-transfers-ownership.md's `propagate-throw-otherwise-no-binding-decrefs`, which the
// corpus writes with a labeled union-case argument shv2 does not yet parse.) A leak or double-free
// of the box is exit 101; a clean fall-through returns 5.
union LexErr implements Error
	problem(code Integer)
end 'LexErr'

function tokenize() returns Integer throws LexErr
	throw LexErr.problem(13)
end 'tokenize'

function main() returns ExitCode
	var ran = 0 as ExitCode
	try tokenize() otherwise 'noBinding'
		ran = 5
	end 'noBinding'
	return ran
end 'main'
```
```exitcode
5
```

<!-- test: error.throw-borrowed-union-unsupported -->
```maxon
typealias Integer = int(i64.min to i64.max)

// Throwing a BORROWED union value (a union parameter) would hand its box to the caller while this
// function's own caller — the box's owner — still frees it: a double free. A borrowed aggregate has no
// clone to transfer, so it is refused until cross-call consume (Wave 2c+), the throw twin of the
// borrowed-aggregate RETURN refusal.
union Fault implements Error
	bad(code Integer)
end 'Fault'

function rethrow(e Fault) returns Integer throws Fault
	throw e
end 'rethrow'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/error-handling/error.throw-borrowed-union-unsupported.test:13:2: Unsupported: throwing a borrowed union value — a union parameter (or a re-borrow of one) is a heap box the caller would adopt and free while the borrow's own owner frees it too, a double free. Throw an OWNED value (a fresh `throw U.case(x)` or a caught `(e)` binding); consuming a borrowed union to throw it arrives with cross-call consume
```

<!-- test: error.throw-payload-expr-temp-decref -->
```maxon

typealias Integer = int(i64.min to i64.max)

// A throw whose PAYLOAD expression builds an owned temporary it does NOT hand to the box: the
// interpolation `"val={x}"` is borrowed by `len` (which returns only its byte count), so the buffer
// belongs to this statement and must drop on the throw edge — exactly as a `return len("val={x}")`
// drops it. The throw and return exits share ONE hand-off cleanup for that reason; a throw that dropped
// its bindings but not this leftover temporary leaked the buffer (exit 101). A clean throw/catch of
// `"val=7".byteLength()` is 5.
union E implements Error
	problem(code Integer)
end 'E'

function len(s String) returns Integer
	return s.byteLength()
end 'len'

function mk(x Integer) returns Integer throws E
	throw E.problem(len("val={x}"))
end 'mk'

function main() returns ExitCode
	var n = 0 as ExitCode
	try mk(7) otherwise (e) 'h'
		match e 'm'
			problem(code) then n = code as ExitCode
		end 'm'
	end 'h'
	return n
end 'main'
```
```exitcode
5
```
