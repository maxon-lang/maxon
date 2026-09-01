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

<!-- test: error.propagate-from-non-throwing-function -->
```maxon
// A bare `try` (the PROPAGATE form) needs somewhere to re-publish the caught flag, and a function
// declaring no `throws` has nowhere: `propagateError` writes an error register the caller never reads,
// so the error is silently discarded and the callee's throw-path primary (0) comes back as a real
// answer. Accepted, this program exited 0. The runnable oracle refuses it too.
typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function inner() returns Integer throws MyError
	throw MyError.failed
end 'inner'

function outer() returns Integer
	return try inner()
end 'outer'

function main() returns ExitCode
	return outer() as ExitCode
end 'main'
```
```maxoncstderr
error E3059: specs/fragments/error-handling/error.propagate-from-non-throwing-function.test:17:9: type mismatch: 'try propagates 'MyError' but the enclosing function declares no 'throws' — the error has nowhere to go and would be dropped; add 'otherwise' to handle it, or declare 'throws MyError''
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

<!-- test: error.void-try-otherwise-value -->
A void-returning throwing call has no success value, so an `otherwise <value>`
fallback is ill-formed — there is nothing for the fallback to stand in for. This is
the statement-position twin of the value-position void-`try` reject (`parseTry`);
both are E3059. (The valueless forms a void `try` DOES take — `otherwise ignore`, an
`otherwise 'block' … end`, a single-statement `otherwise return`/`throw` — compile.)
```maxon
enum Fault implements Error
	broken
end 'Fault'

function doIt(n Integer) throws Fault
	if n > 5 'big'
		throw Fault.broken
	end 'big'
end 'doIt'

function main() returns ExitCode
	try doIt(9) otherwise 0
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3059: <fragment>:13:2: type mismatch: 'a void `try` (its call returns nothing) cannot take an `otherwise <value>` fallback — the success path yields no value for the fallback to stand in for; use `otherwise ignore`, an `otherwise 'block' … end`, or a single-statement `otherwise return`/`throw`'
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
error E3059: specs/fragments/error-handling/error.otherwise-ignore-in-assignment.test:15:12: type mismatch: 'a `try` used for its value needs a value on the error path too, but this `otherwise` handler catches the error without producing one (`otherwise ignore`, or a handler block that runs off its end) — give it a fallback value with `otherwise <expr>`, or make every path of the handler terminate (`return`/`throw`/`break`/`continue`)'
```

<!-- test: error.otherwise-block-fallthrough-in-assignment -->
```maxon

typealias Integer = int(i64.min to i64.max)

// A block-form `otherwise` that runs off its end catches the error but yields NO value, so it cannot
// stand in a value position — the same rule `otherwise ignore` obeys, enforced at the one `fellThrough`
// gate. Before the fix a managed result here LEAKED (the ok edge dropped the result, then the binding
// read the freed box); a scalar silently bound an undefined value.
enum MyError implements Error
	failed
end 'MyError'

function mayFail() returns Integer throws MyError
	throw MyError.failed
end 'mayFail'

function main() returns ExitCode
	let val = try mayFail() otherwise (e) 'handler'
		let note = 1
	end 'handler'
	return val
end 'main'
```
```maxoncstderr
error E3059: specs/fragments/error-handling/error.otherwise-block-fallthrough-in-assignment.test:18:12: type mismatch: 'a `try` used for its value needs a value on the error path too, but this `otherwise` handler catches the error without producing one (`otherwise ignore`, or a handler block that runs off its end) — give it a fallback value with `otherwise <expr>`, or make every path of the handler terminate (`return`/`throw`/`break`/`continue`)'
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

<!-- test: error.file-private-union-caught-cross-file -->
A file-private error union declared in the FORWARD file (`liberr.maxon` sorts
before `main.maxon`, so it is folded into the signatures index before the catch
site is parsed) is caught across files. Its `bad(code Code)` payload type crosses
by NAME through the union-payload adopt door (OPEN #52), so `(e)` recovers
`LibErr` and `match e` dispatches — the wrong-interner misread that kept this
disabled is gone.
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

<!-- test: error.cross-file-throws-caught-later-file -->
BACKWARD ordering: `risky`/`Woe` are declared in `zzz.maxon`, which sorts AFTER
the catch site in `app.maxon`. The whole-program signature sweep
(`queryProgramSignatures` → `foldDeclaredSignaturesInto` → `foldFile`) folds every
file's union LAYOUTS and `throws` clauses into the shared index BEFORE any body is
parsed, so `Woe`'s layout is already seeded when `app.maxon`'s body is parsed; the
union-payload adopt doors then re-intern the payload type by name into the reader
file's interner. That is the declaration-order union-layout prescan OPEN #52's
backward slice asked for — it already exists.
```maxon
// --- file: app.maxon
// The throwing callee `risky` is declared in `zzz.maxon`, which sorts AFTER
// this file. The single-pass parser parses `app.maxon` first, so a project-wide
// throws-clause prescan is what lets the `(e)` binding here recover `risky`'s
// error union `Woe` (rather than falling back to `integer`, which would make the
// `match e` below report E3005 "match scrutinee must be an enum-typed value").
// Seeding every file's `throws` clause before any body is parsed keeps `e` typed
// as `Woe`.
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

<!-- test: error.cross-file-throws-string-payload-later-file -->
BACKWARD ordering with a MANAGED (String) payload: `Woe.bad(msg String)` is
declared in `zzz.maxon` (sorts after `app.maxon`), thrown by `risky`, and caught
in `app.maxon` where `match e` binds and reads the String payload. The payload's
own drop (`__str_decref` at the handler `end`) must route through the union's
destructor cascade — resolved against the reader file's interner via the adopt
door — so a wrong-interner misread here would be a leak or a wild-free, not just a
wrong number. This pins the managed backward path the scalar case above cannot.
```maxon
// --- file: app.maxon
function main() returns ExitCode
	var result = 0
	try risky(9) otherwise (e) 'handler'
		match e 'check'
			bad(msg) then result = msg.byteLength()
			ok then result = 1
		end 'check'
	end 'handler'
	return result
end 'main'

// --- file: zzz.maxon
typealias Code = int(i64.min to i64.max)

export union Woe implements Error
	bad(msg String)
	ok
end 'Woe'

export function risky(n Code) returns Code throws Woe
	if n > 5 'big'
		throw Woe.bad("hello")
	end 'big'
	return n
end 'risky'
```
```exitcode
5
```

<!-- test: error.cross-file-throws-struct-payload-later-file -->
BACKWARD ordering with a MANAGED (struct) payload: `Woe.bad(p Payload)` and the
struct `Payload` are declared in `zzz.maxon` (sorts after `app.maxon`). The catch
site in `app.maxon` binds the struct payload and reads `p.mass`. Classifying the
`named` payload `Payload` at the reader's match-bind site resolves its type id
against `app.maxon`'s interner (via the adopt door) — the exact interner-mismatch
family that panicked `classifyUnionPayload` before the doors landed.
```maxon
// --- file: app.maxon
function main() returns ExitCode
	var result = 0
	try risky(9) otherwise (e) 'handler'
		match e 'check'
			bad(p) then result = p.mass
			ok then result = 1
		end 'check'
	end 'handler'
	return result
end 'main'

// --- file: zzz.maxon
typealias Code = int(i64.min to i64.max)

export type Payload
	export var mass as Code

	export static function create(m Code) returns Self
		return Self{mass: m}
	end 'create'
end 'Payload'

export union Woe implements Error
	bad(p Payload)
	ok
end 'Woe'

export function risky(n Code) returns Code throws Woe
	if n > 5 'big'
		throw Woe.bad(Payload.create(7))
	end 'big'
	return n
end 'risky'
```
```exitcode
7
```

<!-- test: error.cross-file-union-constructed-later-file -->
BACKWARD ordering with the earlier file DECLARING DECOY types that SHIFT its
interner: `app.maxon` constructs and matches `Woe.bad(Payload.create(7))` whose
`Woe`/`Payload` live in `zzz.maxon` (sorts later), but first declares `Decoy1`
(three fields) and `Decoy2` (one field) so `app.maxon`'s type interner is offset
from `zzz.maxon`'s. If the payload type id were resolved against the wrong
interner it would name a decoy (a wrong layout / wrong field offset → a wrong
answer or crash), not `Payload`. This is the adversarial interner-robustness pin.
```maxon
// --- file: app.maxon
typealias Tiny = int(0 to 7)

type Decoy1
	export var alpha as Tiny
	export var beta as Tiny
	export var gamma as Tiny
end 'Decoy1'

type Decoy2
	export var only as Tiny
end 'Decoy2'

function main() returns ExitCode
	let w = Woe.bad(Payload.create(7))
	var result = 0
	match w 'check'
		bad(p) then result = p.mass
		ok then result = 1
	end 'check'
	return result
end 'main'

// --- file: zzz.maxon
typealias Code = int(i64.min to i64.max)

export type Payload
	export var mass as Code

	export static function create(m Code) returns Self
		return Self{mass: m}
	end 'create'
end 'Payload'

export union Woe implements Error
	bad(p Payload)
	ok
end 'Woe'
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

<!-- test: error.otherwise-return-managed-struct -->
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

<!-- test: error.otherwise-return-string -->
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

<!-- test: error.otherwise-value-managed-string -->
```maxon

// A managed String try-result with a fallback STRING LITERAL. The result phi merges the owned ok
// result with the immortal `otherwise` literal, which must be PROMOTED to an owned copy so the phi
// drops exactly once on every edge (no decref of read-only rdata, no leak, no NULL decref on the throw).
enum MyError implements Error
	failed
end 'MyError'

function tryIt(flag bool) returns String throws MyError
	if flag 'c'
		throw MyError.failed
	end 'c'
	return "ok"
end 'tryIt'

function pick(flag bool) returns String
	let s = try tryIt(flag) otherwise "fallback"
	return s
end 'pick'

function main() returns ExitCode
	let a = pick(false)
	let b = pick(true)
	if a == "ok" 'x'
		if b == "fallback" 'y'
			return 0
		end 'y'
	end 'x'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: error.otherwise-value-managed-string-call -->
```maxon

// A managed String try-result whose `otherwise` fallback is itself an OWNED String from a call — both
// phi edges are owned, so the phi drops once with no promotion, on the ok AND the error edge alike.
enum MyError implements Error
	failed
end 'MyError'

function tryIt(flag bool) returns String throws MyError
	if flag 'c'
		throw MyError.failed
	end 'c'
	return "ok"
end 'tryIt'

function fallback() returns String
	return "fb"
end 'fallback'

function pick(flag bool) returns String
	let s = try tryIt(flag) otherwise fallback()
	return s
end 'pick'

function main() returns ExitCode
	let a = pick(false)
	let b = pick(true)
	if a == "ok" 'x'
		if b == "fb" 'y'
			return 0
		end 'y'
	end 'x'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: error.discard-managed-result -->
```maxon

typealias Integer = int(i64.min to i64.max)

// A managed String try-result whose value is DISCARDED (bare `try` propagate). On the ok edge the box
// must drop on the ok path (statement drain); on the error edge the result register is NULL and the
// propagate must not touch it.
enum MyError implements Error
	failed
end 'MyError'

function makeStr(flag bool) returns String throws MyError
	if flag 'c'
		throw MyError.failed
	end 'c'
	return "hi"
end 'makeStr'

function runIt(flag bool) returns Integer throws MyError
	try makeStr(flag)
	return 5
end 'runIt'

function main() returns ExitCode
	let good = try runIt(false) otherwise return 1
	let bad = try runIt(true) otherwise return good
	return bad
end 'main'
```
```exitcode
5
```

<!-- test: error.propagate-managed-return -->
```maxon

// A throwing function that RETURNS a managed String, binding a propagated try-result and handing it
// back. The ok path moves the box out to the caller; the error path propagates (result NULL, untouched),
// and the caller's own live owned binding still drops on its `otherwise return`.
enum MyError implements Error
	failed
end 'MyError'

function inner(flag bool) returns String throws MyError
	if flag 'c'
		throw MyError.failed
	end 'c'
	return "value"
end 'inner'

function outer(flag bool) returns String throws MyError
	let s = try inner(flag)
	return s
end 'outer'

function main() returns ExitCode
	let a = try outer(false) otherwise return 1
	if a == "value" 'x'
		let b = try outer(true) otherwise return 0
		print("{b}")
		return 2
	end 'x'
	return 3
end 'main'
```
```exitcode
0
```

<!-- test: error.otherwise-ignore-managed -->
```maxon

typealias Integer = int(i64.min to i64.max)

// A managed String try-result discarded via `otherwise ignore` (the fell-through path). On the ok edge
// the box drops in the ok block before the merge; on the error edge nothing is owned. No leak, no NULL
// decref in the shared continuation.
enum MyError implements Error
	failed
end 'MyError'

function makeStr(flag bool) returns String throws MyError
	if flag 'c'
		throw MyError.failed
	end 'c'
	return "hi"
end 'makeStr'

function run(flag bool) returns Integer
	try makeStr(flag) otherwise ignore
	return 9
end 'run'

function main() returns ExitCode
	let a = run(false)
	let b = run(true)
	if a == 9 'x'
		if b == 9 'y'
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
// hands the box to the caller; `otherwise (e)` adopts it; `match e { failed(m) ... }` binds
// the String by RETAIN — a caught box is a CO-OWNER, because the thrower may have retained
// it out of a container it still holds (`retainThrownField`) and the flag register carries
// no note of which transfer ran — so the binding drops its own reference at the arm's end
// and `__destruct_E` drops the slot's. A leak or double-free of either the String or the box
// is exit 101; a clean `"boom".byteLength()` is 4.
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

<!-- test: throw-borrowed-union-co-owns -->
### Throwing a BORROWED union CO-OWNS its box — it does not move it
⛔⛔ **THIS CASE PINNED A REFUSAL, AND THE REFUSAL'S OWN STATED REASON HAD ALREADY BEEN RETIRED.** Its prose
called it *"the throw twin of the borrowed-aggregate RETURN refusal"*, refused *"until cross-call consume"* —
but S5 lifted exactly that on the RETURN door, and `Parser.valueIsNonTextAggregate`'s header says why in as many
words: *"the two that did not were held back by a WRONG PREMISE … the callee increfs before the `ret`, the caller
adopts and decrefs once, and the borrow's own owner never notices … **what genuinely needs the cross-call consume
is a MOVE**, where the source must be poisoned so exactly one owner remains."*

⭐ **A `throw` IS A HAND-OFF, NOT A MOVE.** Nothing is poisoned, so nothing needs the consume — the thrown
reference is a SECOND owner, the catch consumes it, and the borrow's own owner drops its own. The mechanism was
already there and already spent one bullet up, on a borrowed union FIELD: `retainThrownValue` is two lines, an
incref and a co-own mark, and **neither is about a field** — it was called `retainThrownField` while it served
every borrowed box, which is why the non-field case looked like it needed a mechanism it did not.

⚠ **THE ASSERTION IS THE SURVIVAL OF THE ORIGINAL, NOT MERELY THAT IT COMPILES.** The caught throw is followed by
a READ of the very binding that was thrown, and `useAfterRethrow` is called TWICE on ONE box: a move would leave
the second call reading a freed box, and a missing incref would free it twice. A leak is exit 101. Only a balanced
refcount prints `a=7 b=7` and exits 0.
```maxon
typealias Integer = int(i64.min to i64.max)

union Fault implements Error
	bad(code Integer)
end 'Fault'

function rethrow(e Fault) returns Integer throws Fault
	throw e
end 'rethrow'

function useAfterRethrow(e Fault) returns Integer
	let got = try rethrow(e) otherwise 'caught'
		return match e 'stillLive'
			bad(code) gives code
		end 'stillLive'
	end 'caught'
	return got
end 'useAfterRethrow'

function main() returns ExitCode
	let f = Fault.bad(7)
	let a = useAfterRethrow(f)
	let b = useAfterRethrow(f)
	print("a={a} b={b}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=7 b=7
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

<!-- test: error.otherwise-wrong-struct -->
An `otherwise` fallback of a DIFFERENT named aggregate than the try's result would merge into the
owned result phi and be dropped under the RESULT's destructor — a wild free (OPEN #54; this program
compiled clean and exited 139 before the check). The scalar checks below cannot see it: `BoxA` and
`BoxB` share the `structRef` tag. Identity is the interned name, exact.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Err implements Error
	bad
end 'Err'

type BoxA
	export var s as String

	static function create(x Integer) returns Self
		return Self{s: "v{x}"}
	end 'create'
end 'BoxA'

type BoxB
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'BoxB'

function makeA(x Integer) returns BoxA throws Err
	if x > 5 'guard'
		throw Err.bad
	end 'guard'
	return BoxA.create(x)
end 'makeA'

function main() returns ExitCode
	let a = try makeA(9) otherwise BoxB.create(9)
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3059: specs/fragments/error-handling/error.otherwise-wrong-struct.test:32:10: type mismatch: 'otherwise type 'BoxB' does not match expected type 'BoxA''
```

<!-- test: error.throwing-float-return -->
A THROWING function that RETURNS a float. The OK edge places the real double in the return register
(F2a); the THROW edge's family-default primary value — which the caller ignores once the error flag is
set — must be classed for the SAME register file, an XMM zero rather than a GPR zero. Before F3b the
throw edge emitted an i64 GPR zero, which the backend's `errorReturn` move then routed to XMM0 for the
float return type and panicked (`move from rax to xmm0 crosses register files`). F3b types the throw-edge
default to the function's return type. `safeRoot(-1)` throws (caught → 0.0) and `safeRoot(5)` returns
2.0, so `trunc(bad) + trunc(ok)` is 0 + 2.
```maxon
enum MathError implements Error
	negative
end 'MathError'

typealias Float = float(f64.min to f64.max)

function safeRoot(n Integer) returns Float throws MathError
	if n < 0 'neg'
		throw MathError.negative
	end 'neg'
	return 2.0
end 'safeRoot'

function main() returns ExitCode
	let bad = try safeRoot(-1) otherwise 0.0
	let ok = try safeRoot(5) otherwise 0.0
	return trunc(bad) + trunc(ok)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
2
```

<!-- test: error.try-binding-block-in-while -->
A block-form `try … otherwise (e) 'label' … end` STATEMENT nested inside a `while`
loop. The loop's carried-variable pre-scan re-derives block structure at the token
level (`opensBlockAt`), and must recognize the block-form `otherwise` as opening a
block — else it mis-predicts the loop's closing `end` and the drift guard panics
(OPEN #62). `i` is assigned AFTER the try-block, so it is exactly the carried var a
too-short extent would lose. `risky` always throws, so the handler runs each
iteration: `acc` counts the 4 iterations.
```maxon
typealias Code = int(i64.min to i64.max)

enum Fault implements Error
	broken
end 'Fault'

function risky(_ Code) returns Code throws Fault
	throw Fault.broken
end 'risky'

function main() returns ExitCode
	var acc = 0 as Code
	var i = 0 as Code
	while i < 4 'loop'
		try risky(i) otherwise (e) 'h'
			acc = acc + 1
		end 'h'
		i = i + 1
	end 'loop'
	return acc
end 'main'
```
```exitcode
4
```

<!-- test: error.try-label-block-in-while -->
The no-binding block form `try … otherwise 'label' … end` (a bare charLiteral label
after `otherwise`, no `(e)`) nested in a `while`. Exercises the other `opensBlockAt`
branch. `i` increments after the try-block.
```maxon
typealias Code = int(i64.min to i64.max)

enum Fault implements Error
	broken
end 'Fault'

function risky(_ Code) returns Code throws Fault
	throw Fault.broken
end 'risky'

function main() returns ExitCode
	var acc = 0 as Code
	var i = 0 as Code
	while i < 4 'loop'
		try risky(i) otherwise 'h'
			acc = acc + 1
		end 'h'
		i = i + 1
	end 'loop'
	return acc
end 'main'
```
```exitcode
4
```

<!-- test: error.try-block-in-if -->
A block-form `try … otherwise (e) 'label' … end` inside an `if` body, with a var
assigned AFTER the try-block. `parseIfStatement` shares the same carried-variable
pre-scan + drift guard as `parseWhileStatement`, so it panics identically without
the `opensBlockAt` fix. `acc` is set to 7 in the handler then incremented to 8.
```maxon
typealias Code = int(i64.min to i64.max)

enum Fault implements Error
	broken
end 'Fault'

function risky(_ Code) returns Code throws Fault
	throw Fault.broken
end 'risky'

function main() returns ExitCode
	var acc = 0 as Code
	if acc < 5 'guard'
		try risky(3) otherwise (e) 'h'
			acc = 7
		end 'h'
		acc = acc + 1
	end 'guard'
	return acc
end 'main'
```
```exitcode
8
```

<!-- test: error.throws-interface-on-a-plain-function -->
⭐⭐ **A PLAIN FUNCTION'S `throws` CLAUSE MUST NAME A DECLARED ENUM OR UNION, AND THIS IS THE PROGRAM THAT
BOUGHT THE RULE (A1s-throwsbox).** The error-flag ABI has two shapes — `ordinal + ErrorFlagOrdinalBias` for a
payload-free enum, a heap BOX POINTER for a payload-carrying union — and the two ends of a throw derive which
one is in play from different places: the THROW site from the value it actually throws, the CATCH site from
the DECLARED clause. `Error` is an interface, so it is in no enum registry, so the catch decoded a heap
pointer as an ordinal and never released the box. **MEASURED before the check existed: exit 101 — a leak —
in shv2 AND in the C# oracle, with `MM leak: 1 allocation(s) remain`.** Nothing reconciled the two
derivations, and nothing can: a clause with no declared cases has no flag shape to reconcile to. The
abstract `throws Error` an INTERFACE REQUIREMENT declares is a different door, dispatched through the witness
ABI and guarded by E3016 — see `specs-shv2/interface-conformance.md`.
```maxon
typealias Code = int(0 to u32.max)

union BoxedError implements Error
	withMessage(msg String)
end 'BoxedError'

function f(x Code) returns Code throws Error
	if x < 10 'small'
		throw BoxedError.withMessage("nope")
	end 'small'
	return x
end 'f'

function main() returns ExitCode
	return try f(3) otherwise 55
end 'main'
```
```maxoncstderr
error E3113: <fragment>:8:10: 'throws Error' names an INTERFACE. A caught error is decoded off the DECLARED clause, and an interface declares no case to decode — a payload-carrying conformer arrives as a heap box pointer that would be read back as an ordinal and never released. Name the error enum or union this function actually throws
```

<!-- test: throws-concrete-union-is-untouched -->
⭐ **THE CONTROL THAT PROVES THE RIGHT THING WAS REFUSED.** The identical program with the CONCRETE clause —
the only difference is `throws BoxedError` for `throws Error` — still compiles, still catches the boxed
error, and still releases the box. This is the bisection the refusal above rests on: same union, same throw,
same catch, only the declared clause differs.
```maxon
typealias Code = int(0 to u32.max)

union BoxedError implements Error
	withMessage(msg String)
end 'BoxedError'

function f(x Code) returns Code throws BoxedError
	if x < 10 'small'
		throw BoxedError.withMessage("nope")
	end 'small'
	return x
end 'f'

function main() returns ExitCode
	return try f(3) otherwise 55
end 'main'
```
```exitcode
55
```

<!-- test: error.throws-unresolvable-type-on-a-plain-function -->
⭐ **THE SIBLING THROUGH THE SAME DOOR: A `throws` CLAUSE NAMING NOTHING AT ALL.** `throws Bogus` names no
declared enum, no union, and no interface — and before the check it COMPILED AND RAN, the author's typo read
as a licence for an untyped error channel. It is the same argument
`ConformanceCheck.throwsRequirementIsAbstract` makes one door over: an error type is something a function
DECLARES, not something a name fails to be.
```maxon
typealias Code = int(0 to u32.max)

union BoxedError implements Error
	withMessage(msg String)
end 'BoxedError'

function f(x Code) returns Code throws Bogus
	if x < 10 'small'
		throw BoxedError.withMessage("nope")
	end 'small'
	return x
end 'f'

function main() returns ExitCode
	return try f(3) otherwise 55
end 'main'
```
```maxoncstderr
error E3113: <fragment>:8:10: 'throws Bogus' names no declared enum or union. A caught error is decoded off the DECLARED clause, so the clause has to name the type whose cases it decodes into
```

<!-- test: error.throws-interface-on-a-method -->
⭐ **THE RULE REACHES A METHOD AND A STATIC, NOT ONLY A TOP-LEVEL FUNCTION** — every function with a BODY
declares its own clause and every one of them is caught the same way, so the check walks the merged module's
functions rather than the top-level declarations. `Holder` implements nothing, so no conformance rule is in
play: this is the plain-function refusal reaching a method.
```maxon
typealias Code = int(0 to u32.max)

union BoxedError implements Error
	withMessage(msg String)
end 'BoxedError'

type Holder
	export var x as Code

	export static function create(x Code) returns Self
		return Self{ x: x }
	end 'create'

	export function get() returns Code throws Error
		if self.x < 10 'small'
			throw BoxedError.withMessage("nope")
		end 'small'
		return self.x
	end 'get'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(3)
	return try h.get() otherwise 55
end 'main'
```
```maxoncstderr
error E3113: <fragment>:15:18: 'throws Error' names an INTERFACE. A caught error is decoded off the DECLARED clause, and an interface declares no case to decode — a payload-carrying conformer arrives as a heap box pointer that would be read back as an ordinal and never released. Name the error enum or union this function actually throws
```

<!-- test: error.throws-a-struct-type -->
⭐ **A STRUCT IS NOT AN ERROR TYPE EITHER, AND THE CHECK ASKS ONE QUESTION TO SAY SO.** The rule is "names a
declared enum or union" — the registry the catch DECODE itself consults — rather than a list of things a
clause may not be, so a `type`, a ranged alias and a primitive are all refused by the same arm, with no
per-shape case to add today or to forget tomorrow.
```maxon
typealias Code = int(0 to u32.max)

type Payload
	export var v as Code

	export static function create(v Code) returns Self
		return Self{ v: v }
	end 'create'
end 'Payload'

function f(x Code) returns Code throws Payload
	return x
end 'f'

function main() returns ExitCode
	return try f(55) otherwise 1
end 'main'
```
```maxoncstderr
error E3113: <fragment>:12:10: 'throws Payload' names no declared enum or union. A caught error is decoded off the DECLARED clause, so the clause has to name the type whose cases it decodes into
```

<!-- test: throws-a-stdlib-error-shv2-synthesizes -->
⭐ **THE NARROWING THIS CASE ONCE PINNED IS GONE, AND THE FLIP IS THE SIGNAL ITS OWN NOTE PROMISED.** Until
`StringError` was synthesized, this program was refused `E3113: 'throws StringError' names no declared enum
or union` — because `StringError` is declared in `stdlib/String.maxon`, which the loader's whitelist
did not then list: shv2 EMITS its String runtime rather
than compiling that file. The name resolved to nothing, so the clause was accepted only as an unchecked
opaque label — `throws Bogus` wearing a real name — and the old note recorded this as **the one place the
rule read differently from the ORACLE**.

⭐ **It now agrees with the oracle, and that is why the case flipped rather than being deleted.** MEASURED on
this exact program: the C# bootstrap compiles it and exits **2**, and shv2 now compiles it and exits **2** —
the byte position of the space in `"ab cd"`. A divergence became an agreement, so what is worth pinning is
the agreement.

⚠ **The old note predicted the flip and named the wrong cause**, which is worth keeping rather than quietly
correcting: it said *"this case flips the day `stdlib/String.maxon` is listed"*. It had not been listed when
it flipped, and it flipped because the compiler **synthesizes** the declaration instead —
`Project.builtinStringErrorEnum`, seeded with `implements Error`, exactly as it has synthesized
`ArrayError` from `stdlib/Array.maxon:6` since R4.4. ⚠ **The loader's filter is gone and every file under
`stdlib/` now loads, so the clause that read "still cannot be" has expired**; the synthesized declaration
is still what this case's expectation rests on, and the expectation is unchanged. The old note also argued that
doing so *"would need a hardcoded second copy of the stdlib's declarations inside the compiler — exactly
what listing a module exists to avoid"*; that argument was already false when written, because
`ArrayError` is that copy and R4.4 accepted it deliberately.

⚠ **The bill, and it is the same one `ArrayError` carries:** the name is now RESERVED program-wide, so a
user program declaring its own `enum StringError` meets `E2015 … a declaration of the type name
'StringError', which the compiler owns`. `throws-a-stdlib-error-has-a-user-declared-spelling` below is
still the control and still answers 2 — an author's own error enum remains the spelling that can be
`match`ed.
```maxon
typealias Num = int(0 to 1000)

function firstSpace(s String) returns Num throws StringError
	let idx = try s.findFirst(" ")
	return idx.bytePos() as Num
end 'firstSpace'

function main() returns ExitCode
	return (try firstSpace("ab cd") otherwise 99)
end 'main'
```
```exitcode
2
```

<!-- test: throws-a-stdlib-error-has-a-user-declared-spelling -->
⭐ **THE CONTROL FOR THE CASE ABOVE**: the author's own `enum SearchFailed implements Error` compiles and
answers 2 — the byte position of the space in `"ab cd"` — so nothing that rung refuses leaves a program with
no way to say what it meant.

⛔⛔ **THIS CASE SPENT ITS WHOLE LIFE ASSERTING A WRONG ANSWER, AND W49 WAVE 3 IS WHAT MADE THE COMPILER
DISAGREE WITH IT.** It was authored with `let idx = try s.findFirst(" ")` and NO `otherwise` under a
`throws SearchFailed` clause — i.e. propagating a `StringError` out of a function that declares it throws
something else. **The runnable oracle refuses exactly that, and always has:** `error E3059: try propagates
'StringError' but enclosing function throws 'SearchFailed' — add 'otherwise' to convert` (MEASURED on the
C# bootstrap, this exact program). shv2 compiled it and exited 2, because `findFirst` was a SYNTHESIZED
runtime callee (`__strix_first`) and E3059's `try` gate reads the thrown type off a DECLARED signature,
which a synthesized callee does not have. Retiring `findFirst` onto `stdlib/String.maxon` gives it one, and
shv2 now raises the oracle's diagnostic at the oracle's position.
⇒ **The fix is the `otherwise` the diagnostic asks for, which is also what the case's own prose describes**
— converting a stdlib error into the author's own spelling is the whole point of the control, and it was
never actually doing it. Both compilers answer 2 on the program below.
```maxon
typealias Num = int(0 to 1000)

enum SearchFailed implements Error
	notFound
end 'SearchFailed'

function firstSpace(s String) returns Num throws SearchFailed
	let idx = try s.findFirst(" ") otherwise throw SearchFailed.notFound
	return idx.bytePos() as Num
end 'firstSpace'

function main() returns ExitCode
	return (try firstSpace("ab cd") otherwise 99)
end 'main'
```
```exitcode
2
```

<!-- test: error.throw-a-boxed-union-under-a-scalar-clause -->
⭐⭐ **THE SIBLING DOOR INTO A1s-throwsbox's OWN DEFECT, FOUND BY PROBING ITS FIX (A1s-throwsbox review).**
The rung refused a clause that names no declared enum; it did not refuse a clause that names a DIFFERENT one.
The mechanism is identical and so is the failure: the THROW site stamps boxedness off the value it actually
throws (a heap BOX POINTER for this payload-carrying union), the CATCH site derives it off the DECLARED
clause (`ScalarError`, an ordinal), and the box is decoded as an ordinal and never released. **MEASURED
before this check: `exit 101` — `MM leak: 1 allocation(s) remain` — in shv2 AND in the C# oracle**, the same
signature the rung's own motivating program produced. An error leaves a function by exactly two doors, and
the `try` door has refused this since P1.4b (E3059, `try propagates 'X' but enclosing function throws 'Y'`);
this is that one rule reaching its other door.
```maxon
typealias Code = int(0 to u32.max)

union BoxedError implements Error
	withMessage(msg String)
end 'BoxedError'

enum ScalarError implements Error
	plain
end 'ScalarError'

function f(x Code) returns Code throws ScalarError
	if x < 10 'small'
		throw BoxedError.withMessage("nope")
	end 'small'
	return x
end 'f'

function main() returns ExitCode
	return try f(3) otherwise 55
end 'main'
```
```maxoncstderr
error E3059: <fragment>:14:3: type mismatch: 'throw of 'BoxedError' but the enclosing function throws 'ScalarError' — the caller decodes the error flag against 'ScalarError', so one enum's ordinals would be read as another's tags, and a payload-carrying union arriving where a scalar is expected leaks its box; throw a 'ScalarError' case, or declare 'throws BoxedError''
```

<!-- test: error.throw-a-different-scalar-error-than-declared -->
⭐⭐ **THE SAME HOLE WITH NO LEAK IN IT — A SILENT WRONG ANSWER, which is why the rule is about the TYPE and
not about boxedness.** Both enums are scalar, so nothing is allocated and the `exit 101` gate above is blind.
The caller still decodes the flag against the DECLARED clause: **measured before this check, `throw ErrB.bOne`
under `throws ErrA` ran the handler's `aOne` arm and the program answered 7** — one enum's ordinals read as
another's tags, in shv2 and in the C# oracle alike. A refusal that only asked "do the two agree about a box?"
would have let this through.
```maxon
typealias Code = int(0 to u32.max)

enum ErrA implements Error
	aZero
	aOne
end 'ErrA'

enum ErrB implements Error
	bZero
	bOne
end 'ErrB'

function f(x Code) returns Code throws ErrA
	if x < 10 'small'
		throw ErrB.bOne
	end 'small'
	return x
end 'f'

function main() returns ExitCode
	return try f(3) otherwise (e) 'caught'
		match e 'which'
			aZero then return 1
			aOne then return 2
		end 'which'
	end 'caught'
end 'main'
```
```maxoncstderr
error E3059: <fragment>:16:3: type mismatch: 'throw of 'ErrB' but the enclosing function throws 'ErrA' — the caller decodes the error flag against 'ErrA', so one enum's ordinals would be read as another's tags, and a payload-carrying union arriving where a scalar is expected leaks its box; throw a 'ErrA' case, or declare 'throws ErrB''
```

<!-- test: error.throw-with-no-enclosing-throws -->
⭐ **A `throw` IN A FUNCTION THAT DECLARES NO `throws` HAD NOWHERE TO PUBLISH THE FLAG, so the error was
silently discarded — measured, the program exited 0 where the answer is the error path**, in both compilers.
`rejectPropagateAgainstEnclosing`'s `none` arm had already measured and refused exactly this discard at the
`try` door; the `throw` door is the same rule's other half, and the `none` case is that mismatch at its
limit — there is no there to fit into.
```maxon
typealias Code = int(0 to u32.max)

enum ErrA implements Error
	aZero
end 'ErrA'

function f(x Code) returns Code
	if x < 10 'small'
		throw ErrA.aZero
	end 'small'
	return x
end 'f'

function main() returns ExitCode
	return f(3)
end 'main'
```
```maxoncstderr
error E3059: <fragment>:10:3: type mismatch: 'throw of 'ErrA' but the enclosing function declares no 'throws' — the error flag has nowhere to be published and would be silently dropped, the throw path handing back the primary register's 0 as a real answer; declare 'throws ErrA', or handle it here with a `try … otherwise`'
```

<!-- test: error.throw-a-value-that-is-not-an-error -->
⭐ **THE THIRD SHAPE THE FLAG HAS NO ENCODING FOR: a value that is not an error type at all.** The error flag
carries an enum ORDINAL or a union BOX POINTER and has no third shape, so `throw 1` produced a flag nothing
could decode — shv2 compiled and RAN it. **E3005, and the runnable oracle's own sentence verbatim**, because
the bootstrap has always refused this: one rule refused by two compilers reads as one rule.
```maxon
typealias Code = int(0 to u32.max)

enum ErrA implements Error
	aZero
end 'ErrA'

function f(x Code) returns Code throws ErrA
	if x < 10 'small'
		throw 1
	end 'small'
	return x
end 'f'

function main() returns ExitCode
	return try f(3) otherwise 55
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:3: throw requires an error enum value
```

<!-- test: throw-matching-the-declared-clause-is-untouched -->
⭐ **THE CONTROL FOR ALL FOUR REFUSALS ABOVE.** The same two error types, the same throw, the same catch —
only the thrown type now IS the declared one, and it compiles, runs, and decodes `bOne` as `bOne`, answering
2. Nothing the review refuses leaves an author without a spelling for what they meant.
```maxon
typealias Code = int(0 to u32.max)

enum ErrA implements Error
	aZero
	aOne
end 'ErrA'

enum ErrB implements Error
	bZero
	bOne
end 'ErrB'

function f(x Code) returns Code throws ErrB
	if x < 10 'small'
		throw ErrB.bOne
	end 'small'
	return x
end 'f'

function main() returns ExitCode
	return try f(3) otherwise (e) 'caught'
		match e 'which'
			bZero then return 1
			bOne then return 2
		end 'which'
	end 'caught'
end 'main'
```
```exitcode
2
```

<!-- test: error.default-throws-arm-a-different-error-than-declared -->
⭐⭐ **THE SPELLING THAT FOUND A MISSED DOOR.** A match's `default throws <E.case>` publishes the enclosing
function's error flag exactly as a `throw` statement does, so it owes the declared clause the same debt — and
when this rule was first wired in, the C# bootstrap had THREE copies of the thrown-error emission and the fix
landed in two of them, leaving this arm quietly accepting `default throws ErrB.bZero` inside `throws ErrA`.
shv2 has one emission site and inherited the rule for free; the bootstrap's three are now one. Pinned in both
corpora so a fourth spelling cannot reopen it.
```maxon
typealias Code = int(0 to u32.max)

enum Kind
	alpha
	beta
end 'Kind'

enum ErrA implements Error
	aZero
end 'ErrA'

enum ErrB implements Error
	bZero
end 'ErrB'

function f(k Kind) returns Code throws ErrA
	match k 'm'
		alpha then return 1
		default throws ErrB.bZero
	end 'm'
end 'f'

function main() returns ExitCode
	return try f(Kind.beta) otherwise 55
end 'main'
```
```maxoncstderr
error E3059: <fragment>:20:11: type mismatch: 'throw of 'ErrB' but the enclosing function throws 'ErrA' — the caller decodes the error flag against 'ErrA', so one enum's ordinals would be read as another's tags, and a payload-carrying union arriving where a scalar is expected leaks its box; throw a 'ErrA' case, or declare 'throws ErrB''
```

<!-- test: error.default-throws-arm-with-no-enclosing-throws -->
⭐⭐ **THE `default` ARM IS THE "UNREACHABLE" MARKER THE ENUM-`match` GRAMMAR DEMANDS, and in a function that
declares no `throws` it has to be spelled `default panic(...)`** — there is no error channel for a `throws` to
publish into. It was accepted in both compilers, and it is the SAME leak by another door: **measured with a
payload-carrying union, `exit 101` / `MM leak: 1 allocation(s) remain`**, because the arm minted a heap box no
caller ever adopted. Four committed programs in the bootstrap's corpus (`tcp-client`, `managed-socket`) held
exactly this shape and were the false-reject sweep's only hits — they now say `panic`, which is what they
always meant.
```maxon
typealias Code = int(0 to u32.max)

enum Kind
	alpha
	beta
end 'Kind'

union BoxedError implements Error
	withMessage(msg String)
end 'BoxedError'

function f(k Kind) returns Code
	match k 'm'
		alpha then return 1
		default throws BoxedError.withMessage("reached")
	end 'm'
end 'f'

function main() returns ExitCode
	return f(Kind.beta)
end 'main'
```
```maxoncstderr
error E3059: <fragment>:16:11: type mismatch: 'throw of 'BoxedError' but the enclosing function declares no 'throws' — the error flag has nowhere to be published and would be silently dropped, the throw path handing back the primary register's 0 as a real answer; declare 'throws BoxedError', or handle it here with a `try … otherwise`'
```
