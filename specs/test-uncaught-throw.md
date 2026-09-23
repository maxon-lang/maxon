---
feature: test-uncaught-throw
status: experimental
keywords: [test, TestFailure, __TestReport, try, uncaught, foreign error, propagation]
category: infrastructure
---

# Uncaught Throws Inside a Test

## Documentation

### The rule

Outside a `test`, a throwing call, a throwing interface call and an `await` of a throwing promise
must each be written with `try`, and a bare `try` — one with no `otherwise` — propagates, so the
callee's `throws` type must be the enclosing function's own. A call that throws something else is
E3059, and the fix is to write an `otherwise` that converts it.

Inside a **`test` body** both requirements are dropped. Such an operation needs no `try` at all:
the body behaves as if wrapped in an implied bare `try`, and each throwing operation gets exactly
the handler an explicit bare `try` gets there. A bare `try` on **any** error type compiles too.
Either way, an error that reaches the handler **fails that test**:

```text
test 'returns 404 when the user is missing'
	let response = Api.lookup("nobody")     // throws ApiError — uncaught, FAILS the test
	Expect.equal(response.status(), expected: 404)
end 'returns 404 when the user is missing'
```

The test author writes neither `try` nor `otherwise`, no conversion to `TestFailure`, and no error
type repeated in a place it adds nothing — a test that meets an unexpected error has exactly one
correct outcome, and the language supplies it. An explicit `try` stays legal and means the same.

### What it compiles to

For each throwing operation, the compiler substitutes the handler the author would otherwise have
had to write:

```text
try Api.lookup("nobody") otherwise (e) 'uncaught throw in test'
	__TestReport.threw("ApiError", errorCase: "{e}", file: "api.test.maxon", line: 12)
	throw TestFailure.assertion
end 'uncaught throw in test'
```

Three things follow from that shape, and they are the reasons for it:

- **A test still throws exactly `TestFailure`.** The foreign error is reported and dropped; only
  the test's own error type ever leaves the test, so the runner has one type to handle.
- **The report reaches the runner before the failure does**, so a test that fails on an unexpected
  error says which error, rather than only that an assertion failed.
- **`file` and `line` locate the operation that threw** — the call, interface call or `await`
  itself, or the explicit `try` — the line a reader has to open, not the test's first line and not
  anything inside the callee.

`"{e}"` renders the error by the language's own interpolation rule, which is the same rule a reader
gets by writing `otherwise (e) … print("{e}")` themselves: the live **case name** for a union and
for a plain enum, and the declared **raw value** for an enum that has one. A report does not get a
second way to spell an error value.

### It applies to a `test` body and to nothing else

An ordinary `function` in the same file is unchanged: a throwing call there without `try` is E3057,
and a bare `try` on a foreign error is E3059.

A **closure** written inside a test body is likewise not a test body. It is a separate function,
its errors go to whoever calls it — which may be nobody, and may be long after the test finished —
so there is nothing for the test to fail on its behalf. A throwing call inside one still needs a
`try` (E3057), and since a function type cannot express `throws` at all (E3101), a bare `try` inside
one is refused as well.

Every construct that *is* part of the test's own body — a loop, an `if`, a `match` arm — is covered,
because the rule is about which function is being parsed and not about how deeply nested the
operation is. So is `await`: an awaited thunk's `throws` type is the type of the error exactly as a
call's is, and one check reads both.

> The `line` in the reports below counts from the top of the file the report NAMES — here
> `suite.test.maxon`, the file this spec's `// --- file:` marker creates — so it does not match the
> line of the surrounding `.test` fragment, which carries a header and the marker itself. The pair
> is self-consistent, which is what a reader opening the named file needs.

### An explicit `otherwise` wins

Nothing here is reached for an operation an explicit `try` with an `otherwise` covers. Every
`otherwise` form keeps working exactly as it does in a `function`, including one that swallows the
error and lets the test go on to pass. An enclosing block-form `try` likewise takes its body's
throwing operations before the implied handler does.

An explicit `try` covers only the operation that produces its value. A throwing call among its
arguments or operands, a throwing receiver of its chain, or a throwing bound of a range it builds
goes to the enclosing handler — in a test body, the implied one. So does a `for` loop's source
expression, with one exception: a call that produces the loop's cursor and throws `IterationError`
is absorbed by the loop, so an empty collection runs zero trips rather than failing the test.

Likewise an operation that throws `TestFailure` itself — an assertion — is not foreign, so it
propagates plainly, with no report. That is what makes a report mean *"something unexpected"*.

### What this does not cover

`panic` is uncatchable — there is no `recover()` (`specs/safety.md`) — so a panicking test takes the
process down with it and no handler runs, this one included. Nothing here changes that or pretends
to: the rule is about THROWN errors, which have a value and an error channel. Surviving a panicking
test is a property of whatever launched the binary, not of the binary.

### `__TestReport`

`__TestReport` (`stdlib/Testing.maxon`) is the surface a test binary reports to its runner through,
and `threw` is the only method on it the compiler emits calls to. Its `__` prefix marks it as
compiler-owned: no program writes either call.

Every byte the runner matches on arrives as an argument. The generated dispatcher calls
`__TestReport.useWireFormat(wrapper, separator:)` once before the first test runs, so the wire
protocol is written down once — in the code that emits the dispatcher and parses what the binary
printed — rather than once there and once in the stdlib, where the two would drift. Both default to
empty, which is a real setting rather than an unconfigured one: a runner that wants bare fields asks
for exactly that, so nothing guesses a marker on a caller's behalf.

The report goes to **stderr**, which is why a wrapper exists at all — a test may print whatever it
likes, and the runner has to find its own lines among it. The whole surface a program can see is
those two functions; the compiler emits `threw` and nothing else, and no program writes either.

⚠ **AND the compiler ENFORCES THAT LAST SENTENCE, WHICH IS WHY THIS FILE PORTS THREE OF THE REFERENCE'S ELEVEN
CASES AND NOT ALL OF THEM** (it carries FOUR: the fourth, `bare-try-on-a-boxed-foreign-error-compiles`,
is the compiler's own and has no counterpart there — see its note).
*"No program writes either call"* is a rule here, not a convention: `requireCalleeIsNotReservedName`
refuses a `__` callee whose bytes an author typed, wherever it stands. The reference's remaining cases
hand-roll a dispatcher and therefore write exactly those calls — see the PORT NOTE below. Nothing
about the RULE depends on who calls the test; what depends on it is whether a spec can invoke one
without the runner, and here it cannot.

## Tests

⚠ **PORT NOTE — 8 OF THE REFERENCE'S 11 CASES ARE HELD BACK, UNEDITED, FOR THE RUNNER SLICE.**
They hand-roll a dispatcher: with no `maxon test` to invoke a test, each writes
`__TestReport.useWireFormat(...)` and calls the mangled `__test_<name>()` **from its own source**.
The bootstrap allows that because it has no call-side reservation at all; The compiler has one on purpose,
and scopes its exemptions to the head's PROVENANCE rather than to a spelling — a `__` callee whose
bytes an author typed stays refused, which `stdlib-user-shadows.error.the-mint-is-not-reachable-from-user-code`
pins shut in exactly those words. Rewording those 8 to suit this compiler would be inventing a claim
nobody has satisfied, so they are NOT reworded and NOT shelved here: they stay verbatim in
`/specs/test-uncaught-throw.md` and land with the `maxon test` command, whose generated dispatcher
is compiler-minted and therefore passes the rule as it stands.

⇒ What is pinned here is the relaxation's COMPILE-TIME half, which is all of it that a program can
observe without a runner: it admits a bare `try` on a foreign error inside a `test`, and it admits
one NOWHERE ELSE. The runtime half — the `__TestReport.threw` report itself — is pinned by the
minted fragment goldens of the first TWO cases below, and by those 8 cases when they land.

<!-- test: bare-try-on-a-foreign-error-compiles -->
The whole relaxation in one program, and the exact counterpart of `error.function-does-not-relax`
below: the SAME `try lookup()` over the SAME foreign error, moved from an ordinary `function` into
a `test`. There it is E3059; here it compiles. Nothing else distinguishes the two, so this pair is
the narrowing stated as a difference rather than as a description.

⚠ **THE GOLDEN IS WHERE THE SUBSTITUTED HANDLER IS VISIBLE.** The exit code cannot see it — `main`
returns 0 and nothing invokes the test — so the minted fragment golden is what records that
`__test_tolerates_a_foreign_error` reports through `__TestReport.threw` and then throws
`TestFailure.assertion`, rather than propagating `ApiError`. Read it when you mint it.
```maxon
// --- file: suite.test.maxon
enum ApiError implements Error
	notFound
end 'ApiError'

function lookup() throws ApiError
	throw ApiError.notFound
end 'lookup'

test 'tolerates a foreign error'
	try lookup()
end 'tolerates a foreign error'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: bare-try-on-a-boxed-foreign-error-compiles -->
The same relaxation over a **payload-carrying union**, which is a different mechanism and not a second
spelling of the case above. A payload-free enum's error arrives as an `ordinal + bias` in the flag
register and owns nothing; a union's arrives as a BOX POINTER the catcher now owns, so the substituted
handler has to enrol it, release it on the way out, and — the half a compile can get wrong silently —
un-enrol it again, because the enrolment belongs to the terminated error edge and to nothing after it.

⛔ **WITHOUT THAT LAST STEP THIS PROGRAM DOES NOT COMPILE AT ALL: the compiler PANICS** in
`restoreMoveMark`, whose owned-binding height no longer matches the mark `finishTerminatedTry` rewinds
to. The scalar case above cannot see it — it enrols nothing — which is exactly why this case exists.

The callee can RETURN as well as throw, so the ok edge is reachable and the golden shows both edges: the
report, the `__str_decref` of the interpolation, the `__destruct_ApiError` of the box and the
`TestFailure` throw on one; the statement after the `try`, with no drop of anything, on the other.
```maxon
// --- file: suite.test.maxon
union ApiError implements Error
	notFound(detail String)
end 'ApiError'

function lookup(hit bool) throws ApiError
	if not hit 'miss'
		throw ApiError.notFound("nobody")
	end 'miss'
end 'lookup'

test 'tolerates a boxed foreign error'
	try lookup(true)
	print("reached the end of the test\n")
end 'tolerates a boxed foreign error'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.function-does-not-relax -->
The most important test in this spec. The identical `try` inside an ordinary `function` — same
file, same callee — still gets the propagation-type error. The relaxation is a narrowing of that
one check, so there is nowhere else it could leak from.
```maxon
// --- file: suite.test.maxon
enum ApiError implements Error
	notFound
end 'ApiError'

enum OwnError implements Error
	bad
end 'OwnError'

function lookup() throws ApiError
	throw ApiError.notFound
end 'lookup'

function notATest() throws OwnError
	try lookup()
end 'notATest'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3059: specs/fragments/test-uncaught-throw/error.function-does-not-relax.test:16:2: try propagates 'ApiError' but enclosing function throws 'OwnError' — add 'otherwise' to convert
```

<!-- test: error.closure-in-a-test-is-not-a-test -->
A closure inside a test body is a separate function, and a function type cannot express `throws`
(E3101) — so there is no error channel to relax and a bare `try` inside one is refused, exactly as
it is anywhere else. The relaxation does not follow the `test` keyword down into nested functions.
```maxon
// --- file: suite.test.maxon
enum ApiError implements Error
	notFound
end 'ApiError'

function lookup() returns Tally throws ApiError
	throw ApiError.notFound
end 'lookup'

typealias Tally = int(0 to 100)
typealias Producer = function() returns Tally

function callIt(produce Producer) returns Tally
	return produce()
end 'callIt'

test 'runs a closure'
	let got = callIt(function() gives try lookup())
	print("got {got}\n")
end 'runs a closure'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2001: specs/fragments/test-uncaught-throw/error.closure-in-a-test-is-not-a-test.test:19:36: try without otherwise requires the enclosing function to have 'throws'
```

<!-- test: implied-try-foreign-error -->
The no-`try` twin of `bare-try-on-a-foreign-error-compiles`: the test body is an implied `try`, so a
bare throwing call compiles to the same handler. The golden must show the same `__TestReport.threw`
report and `TestFailure` throw as the `try` form.
```maxon
// --- file: suite.test.maxon
enum ApiError implements Error
	notFound
end 'ApiError'

function lookup() throws ApiError
	throw ApiError.notFound
end 'lookup'

test 'tolerates a foreign error'
	lookup()
end 'tolerates a foreign error'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-boxed-foreign-error -->
The no-`try` twin of `bare-try-on-a-boxed-foreign-error-compiles`: a payload-carrying union reaches the
implied handler as a box the handler owns, releases, and un-enrols on the terminated error edge.
```maxon
// --- file: suite.test.maxon
union ApiError implements Error
	notFound(detail String)
end 'ApiError'

function lookup(hit bool) throws ApiError
	if not hit 'miss'
		throw ApiError.notFound("nobody")
	end 'miss'
end 'lookup'

test 'tolerates a boxed foreign error'
	lookup(true)
	print("reached the end of the test\n")
end 'tolerates a boxed foreign error'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-value-position -->
A bare throwing call in value position: the implied handler guards the binding's initializer, and the
ok edge binds the returned value.
```maxon
// --- file: suite.test.maxon
typealias Tally = int(0 to 100)

enum ApiError implements Error
	notFound
end 'ApiError'

function lookup(hit bool) returns Tally throws ApiError
	if not hit 'miss'
		throw ApiError.notFound
	end 'miss'

	return 7
end 'lookup'

test 'binds a value from a throwing call'
	let got = lookup(true)
	print("got {got}\n")
end 'binds a value from a throwing call'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-managed-temporary -->
The throwing call is an ARGUMENT evaluated after a managed `String` temporary already exists in the same
statement, so the implied handler's error edge must release that temporary before it reports.
```maxon
// --- file: suite.test.maxon
typealias Tally = int(0 to 100)

enum ApiError implements Error
	notFound
end 'ApiError'

function lookup(hit bool) returns Tally throws ApiError
	if not hit 'miss'
		throw ApiError.notFound
	end 'miss'

	return 7
end 'lookup'

function prefix(word String) returns String
	return "{word}: "
end 'prefix'

function describe(label String, n Tally) returns String
	return "{label}{n}"
end 'describe'

test 'owns a temporary when the call throws'
	print(describe(prefix("value"), n: lookup(true)))
end 'owns a temporary when the call throws'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-runtime-callee -->
A throwing compiler-owned callee, the array accessor, written bare in a test body takes the implied
handler exactly as a user function does.
```maxon
// --- file: suite.test.maxon
test 'reads an element bare'
	let a = [10, 20, 30]
	let x = a.get(1)
	print("{x}\n")
end 'reads an element bare'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-witness-dispatch -->
A throwing interface requirement dispatched through an existential in a test body, with no `try`: the
witness dispatch takes the implied handler exactly as a direct call does.
```maxon
// --- file: suite.test.maxon
typealias Code = int(0 to u32.max)

enum DigestError implements Error
	tooSmall
end 'DigestError'

interface Digest
	function digest() returns Code throws DigestError
end 'Digest'

type Point implements Digest
	let x as Code

	static function create(x Code) returns Self
		return Self{x: x}
	end 'create'

	function digest() returns Code throws DigestError
		if self.x < 10 'small'
			throw DigestError.tooSmall
		end 'small'

		return self.x
	end 'digest'
end 'Point'

function make(x Code) returns Digest
	return Point.create(x)
end 'make'

test 'dispatches a throwing requirement'
	let d = make(42)
	let v = d.digest()
	print("digest {v}\n")
end 'dispatches a throwing requirement'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-await -->
An `await` of a throwing promise written without `try` in a test body takes the implied handler, as a
`try await` does.
```maxon
// --- file: suite.test.maxon
typealias Integer = int(i64.min to i64.max)

enum WorkError implements Error
	failed
end 'WorkError'

function mayFail(succeed bool) returns Integer throws WorkError
	Scheduler.yield()

	if succeed 'ok'
		return 10
	end 'ok'

	throw WorkError.failed
end 'mayFail'

test 'awaits a throwing promise'
	let p = async mayFail(false)
	let r = await p
	print("awaited {r}\n")
end 'awaits a throwing promise'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-argument-of-explicit-try -->
An explicit `try` covers its own target call only. A throwing call in that target's ARGUMENT list is a
separate call, so its foreign error goes to the test body's implied handler.
```maxon
// --- file: suite.test.maxon
enum ApiError implements Error
	notFound
end 'ApiError'

function count(hit bool) returns AssertedInt throws ApiError
	if not hit 'miss'
		throw ApiError.notFound
	end 'miss'

	return 3
end 'count'

test 'asserts on a counted value'
	try Expect.equal(count(true), expected: 3)
end 'asserts on a counted value'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-operand-of-a-parenthesized-target -->
A parenthesized `try` target claims the operation that produces the group's value, which here is the
division. The throwing call that is an OPERAND of that division is a separate call, so its foreign
error goes to the test body's implied handler.
```maxon
// --- file: suite.test.maxon
enum ApiError implements Error
	notFound
end 'ApiError'

function count(hit bool) returns AssertedInt throws ApiError
	if not hit 'miss'
		throw ApiError.notFound
	end 'miss'

	return 12
end 'count'

function divisor() returns AssertedInt
	return 4
end 'divisor'

test 'divides a counted value'
	let share = try (count(true) / divisor()) otherwise 0
	Expect.equal(share, expected: 3)
end 'divides a counted value'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-iteration-error-in-a-for-range-bound -->
A `for` loop absorbs the `IterationError` of the call that hands it its cursor, and nothing else. Here the
source is a RANGE whose lower bound is `peek`, which throws `IterationError` but hands the loop no cursor, so
the loop has nothing to absorb and the call takes the test body's implied handler like any other.
```maxon
// --- file: suite.test.maxon
test 'counts up from a peeked bound'
	let steps = [1, 2, 3]
	let it = steps.cursor()
	var total = 0

	for i in it.peek(1) upto 4 'each'
		total = total + i
	end 'each'

	Expect.equal(total as AssertedInt, expected: 5)
end 'counts up from a peeked bound'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-await-as-a-for-range-bound -->
An `await` that is a range's lower bound hands the loop no cursor, so the loop absorbs nothing and the
awaited error takes the test body's implied handler, as a bare `await` does anywhere else in the body.
```maxon
// --- file: suite.test.maxon
typealias Integer = int(i64.min to i64.max)

enum WorkError implements Error
	failed
end 'WorkError'

function start(succeed bool) returns Integer throws WorkError
	Scheduler.yield()

	if succeed 'ok'
		return 2
	end 'ok'

	throw WorkError.failed
end 'start'

test 'counts up from an awaited bound'
	let p = async start(true)
	var total = 0

	for i in await p upto 5 'each'
		total = total + i
	end 'each'

	Expect.equal(total as AssertedInt, expected: 9)
end 'counts up from an awaited bound'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-await-receiver-of-an-explicit-try -->
An explicit `try` on `(await p).check()` covers the chain's last call. The awaited promise is the
RECEIVER, a separate throwing operation, so its error goes to the test body's implied handler.
```maxon
// --- file: suite.test.maxon
typealias Tally = int(0 to 100)

enum WorkError implements Error
	failed
end 'WorkError'

type Holder
	let total as Tally

	static function create(total Tally) returns Self
		return Self{total: total}
	end 'create'

	function check() throws TestFailure
		if self.total != 3 'wrong'
			throw TestFailure.assertion
		end 'wrong'
	end 'check'
end 'Holder'

function make(succeed bool) returns Holder throws WorkError
	Scheduler.yield()

	if succeed 'ok'
		return Holder.create(3)
	end 'ok'

	throw WorkError.failed
end 'make'

test 'checks an awaited holder'
	let p = async make(true)
	try (await p).check()
end 'checks an awaited holder'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-true-arm-of-a-for-source-ternary -->
A ternary `for` source hands the loop the value of whichever arm ran. Neither arm's call is the loop's
cursor, so the TRUE arm's throwing call takes the test body's implied handler exactly as the false arm's does.
```maxon
// --- file: suite.test.maxon
typealias Tally = int(0 to 100)
typealias TallyArray = Array with Tally

enum ApiError implements Error
	notFound
end 'ApiError'

function items(hit bool) returns TallyArray throws ApiError
	if not hit 'miss'
		throw ApiError.notFound
	end 'miss'

	var found = TallyArray.create()
	found.push(1)
	found.push(2)
	return found
end 'items'

test 'walks the chosen items'
	let useFirst = true
	var total = 0

	for x in items(true) if useFirst else items(true) 'each'
		total = total + x
	end 'each'

	Expect.equal(total as AssertedInt, expected: 3)
end 'walks the chosen items'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-bounds-of-a-parenthesized-range-target -->
An explicit `try` on `(a upto b).createIterator()` covers the chain's last call. The range's BOUNDS are
operands of the range operator, so a throwing call in either takes the test body's implied handler.
```maxon
// --- file: suite.test.maxon
enum ApiError implements Error
	notFound
end 'ApiError'

function first(hit bool) returns RangeBound throws ApiError
	if not hit 'miss'
		throw ApiError.notFound
	end 'miss'

	return 2
end 'first'

function last(hit bool) returns RangeBound throws ApiError
	if not hit 'miss'
		throw ApiError.notFound
	end 'miss'

	return 5
end 'last'

test 'iterates a range built from throwing bounds'
	let it = try (first(true) upto last(true)).createIterator()
	Expect.equal(it.current() as AssertedInt, expected: 2)
end 'iterates a range built from throwing bounds'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-reinterpreted-element-as-a-for-range-bound -->
A narrow signed enum element is read back through a sign extension the accessor appends after its call.
That extension is part of the call's result, so when the element read is the receiver of `.rawValue` in a
range's lower bound, the receiver is still the throwing call and it takes the test body's implied handler.
```maxon
// --- file: suite.test.maxon
enum Level
	low = -1
	high = 2
end 'Level'

typealias LevelArray = Array with Level

test 'counts up from a stored level'
	var levels = LevelArray.create()
	levels.push(Level.high)
	var total = 0

	for i in levels.get(0).rawValue upto 4 'each'
		total = total + i
	end 'each'

	Expect.equal(total as AssertedInt, expected: 5)
end 'counts up from a stored level'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-for-source-iteration-error-is-absorbed -->
The call that hands a `for` loop its cursor throws `IterationError` for an EMPTY collection, and the loop
absorbs it: the loop runs zero trips and the test goes on. The implied handler must not claim that call.
```maxon
// --- file: suite.test.maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

test 'an empty traversal runs no trips'
	var total = 0

	for (_, value) in IntArray.create().withIterator() 'each'
		total = total + value
	end 'each'

	Expect.equal(total as AssertedInt, expected: 0)
end 'an empty traversal runs no trips'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-for-source-iteration-error-is-absorbed-parenthesized -->
The same loop with its source written in parentheses. A group is transparent, so the loop still absorbs
the empty collection's `IterationError` and runs zero trips.
```maxon
// --- file: suite.test.maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

test 'an empty parenthesized traversal runs no trips'
	var total = 0

	for (_, value) in (IntArray.create().withIterator()) 'each'
		total = total + value
	end 'each'

	Expect.equal(total as AssertedInt, expected: 0)
end 'an empty parenthesized traversal runs no trips'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-cast-operand-as-a-for-range-bound -->
A cast that emits an op makes its operand an operand like any other. The throwing call under the cast is a
range's lower bound, so it takes the test body's implied handler.
```maxon
// --- file: suite.test.maxon
typealias Integer = int(i64.min to i64.max)
typealias Small = int(0 to 100)

enum ApiError implements Error
	notFound
end 'ApiError'

function first(hit bool) returns Integer throws ApiError
	if not hit 'miss'
		throw ApiError.notFound
	end 'miss'

	return 2
end 'first'

test 'counts up from a cast bound'
	var total = 0

	for i in first(true) as Small upto 5 'each'
		total = total + i
	end 'each'

	Expect.equal(total as AssertedInt, expected: 9)
end 'counts up from a cast bound'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-chain-receiver -->
An explicit `try` on a postfix chain covers the chain's last call. The RECEIVER is a separate throwing
call, so its foreign error goes to the test body's implied handler.
```maxon
// --- file: suite.test.maxon
typealias Tally = int(0 to 100)

enum ApiError implements Error
	notFound
end 'ApiError'

type Holder
	let total as Tally

	static function create(total Tally) returns Self
		return Self{total: total}
	end 'create'

	function check() throws TestFailure
		if self.total != 3 'wrong'
			throw TestFailure.assertion
		end 'wrong'
	end 'check'
end 'Holder'

function make(hit bool) returns Holder throws ApiError
	if not hit 'miss'
		throw ApiError.notFound
	end 'miss'

	return Holder.create(3)
end 'make'

test 'checks a made holder'
	try make(true).check()
end 'checks a made holder'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.implied-try-closure-still-needs-try -->
The implied `try` belongs to the test body alone. A closure inside it is a separate function with no
error channel, so a bare throwing call there is still E3057.
```maxon
// --- file: suite.test.maxon
enum ApiError implements Error
	notFound
end 'ApiError'

function lookup() returns Tally throws ApiError
	throw ApiError.notFound
end 'lookup'

typealias Tally = int(0 to 100)
typealias Producer = function() returns Tally

function callIt(produce Producer) returns Tally
	return produce()
end 'callIt'

test 'runs a closure'
	let got = callIt(function() gives lookup())
	print("got {got}\n")
end 'runs a closure'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3057: specs/fragments/test-uncaught-throw/error.implied-try-closure-still-needs-try.test:19:36: throwing function requires try: 'lookup'
```
<!-- test: implied-try-explicit-try-of-a-rebranded-call -->
An explicit `try` on a parenthesized call renamed to another brand of the same instance claims the call:
the rebrand emits no op, so the call IS the value the group produces, and the author's `otherwise` owns its
error.
```maxon
// --- file: suite.test.maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias Scores = Array with Integer

enum ApiError implements Error
	notFound
end 'ApiError'

function items(hit bool) returns IntArray throws ApiError
	if not hit 'miss'
		throw ApiError.notFound
	end 'miss'

	var found = IntArray.create()
	found.push(4)
	found.push(5)
	return found
end 'items'

test 'rebrands the fetched items'
	let s = try (items(true) as Scores) otherwise Scores.create()
	Expect.equal(s.count() as AssertedInt, expected: 2)
end 'rebrands the fetched items'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-explicit-try-of-a-range-checked-division -->
An explicit `try` on a parenthesized checked division narrowed to a ranged alias whose representation is
the division's own: the cast emits no op and only records its range site, so the division IS the value the
group produces and the author's `otherwise` owns its divide-by-zero.
```maxon
// --- file: suite.test.maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias Wide = int(-1000000000000 to 1000000000000)

test 'divides by a counted value'
	let d = IntArray.create().count()
	let n = try ((8 / d) as Wide) otherwise 5
	Expect.equal(n as AssertedInt, expected: 5)
end 'divides by a counted value'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: implied-try-for-source-rebranded-cursor-is-absorbed -->
A cursor-producing call renamed to another brand of the same instance is still the call that hands the loop
its cursor: the rebrand emits no op, so the loop absorbs the empty collection's `IterationError`.
```maxon
// --- file: suite.test.maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias Walk = ArrayIterator with Integer

test 'an empty rebranded traversal runs no trips'
	var total = 0

	for x in IntArray.create().cursor() as Walk 'each'
		total = total + x
	end 'each'

	Expect.equal(total as AssertedInt, expected: 0)
end 'an empty rebranded traversal runs no trips'

// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```
