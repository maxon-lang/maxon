---
feature: test-uncaught-throw
status: experimental
keywords: [test, TestFailure, __TestReport, try, uncaught, foreign error, propagation]
category: infrastructure
---

# Uncaught Throws Inside a Test

## Documentation

### The rule

Outside a `test`, a bare `try` — one with no `otherwise` — propagates, so the callee's `throws`
type must be the enclosing function's own. A call that throws something else is E3059, and the
fix is to write an `otherwise` that converts it.

Inside a **`test` body** that requirement is dropped. A bare `try` on **any** error type compiles,
and an error that reaches it **fails that test**:

```text
test 'returns 404 when the user is missing'
	let response = try Api.lookup("nobody")     // throws ApiError — uncaught, FAILS the test
	try Expect.equal(response.status(), expected: 404)
end 'returns 404 when the user is missing'
```

The test author writes `try` and nothing else. No `otherwise`, no conversion to `TestFailure`, no
error type repeated in a place it adds nothing — a test that meets an unexpected error has exactly
one correct outcome, and the language supplies it.

### What it compiles to

The compiler substitutes the handler the author would otherwise have had to write:

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
- **`file` and `line` locate the `try` that threw** — the line a reader has to open — not the
  test's first line and not anything inside the callee.

`"{e}"` renders the error by the language's own interpolation rule, which is the same rule a reader
gets by writing `otherwise (e) … print("{e}")` themselves: the live **case name** for a union and
for a plain enum, and the declared **raw value** for an enum that has one. A report does not get a
second way to spell an error value.

### It applies to a `test` body and to nothing else

The relaxation is a narrowing of the propagation-type check, not a rule of its own, so there is no
second place where it could be reached. An ordinary `function` in the same file, holding the
identical `try`, still gets E3059.

A **closure** written inside a test body is likewise not a test body. It is a separate function,
its errors go to whoever calls it — which may be nobody, and may be long after the test finished —
so there is nothing for the test to fail on its behalf. A function type cannot express `throws` at
all (E3101), so a closure has no error channel to relax: a bare `try` inside one is refused for
that reason, unchanged.

Every construct that *is* part of the test's own body — a loop, an `if`, a `match` arm — is covered,
because the rule is about which function is being parsed and not about how deeply nested the `try` is.
So is `try await`: an awaited thunk's `throws` type is the type of that `try`'s error exactly as a
call's is, and one check reads both.

> The `line` in the reports below counts from the top of the file the report NAMES — here
> `suite.test.maxon`, the file this spec's `// --- file:` marker creates — so it does not match the
> line of the surrounding `.test` fragment, which carries a header and the marker itself. The pair
> is self-consistent, which is what a reader opening the named file needs.

### An explicit `otherwise` wins

Nothing here is reached when the author wrote an `otherwise`: the substitution lives on the
no-`otherwise` path. Every `otherwise` form keeps working exactly as it does in a `function`,
including one that swallows the error and lets the test go on to pass.

Likewise a `try` on a call that throws `TestFailure` itself — an assertion — is not foreign, so it
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

The tests below call the test symbol (`__test_<sanitized>`) from `main` by hand, because the
generated dispatcher that will normally do it is not part of this change. Nothing about the rule
depends on who calls the test.

## Tests

<!-- test: enum-error-fails-the-test -->
A foreign enum error reaches a bare `try` in a test body. The test reports it and fails with
`TestFailure`; the statement after the `try` never runs.
```maxon
// --- file: suite.test.maxon
enum ApiError implements Error
	notFound
	timedOut
end 'ApiError'

function lookup() throws ApiError
	throw ApiError.notFound
end 'lookup'

test 'finds the user'
	try lookup()
	print("kept going\n")
end 'finds the user'

function main() returns ExitCode
	__TestReport.useWireFormat("##", separator: "|")
	try __test_finds_the_user() otherwise 'failed'
		print("test failed\n")
		return 0
	end 'failed'
	print("test passed\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
test failed
```
```stderr
##ApiError|notFound|suite.test.maxon|11##
```

<!-- test: union-error-with-payload -->
A union error carrying a payload. This is why the report renders the bound error rather than the
raw error flag: a heap-boxed union's flag is a pointer, and only the interpolation rule turns it
into the live case name.
```maxon
// --- file: suite.test.maxon
typealias Status = int(0 to 599)

union HttpError implements Error
	refused(code Status)
	unreachable
end 'HttpError'

function fetch() throws HttpError
	throw HttpError.refused(503)
end 'fetch'

test 'fetches the page'
	try fetch()
end 'fetches the page'

function main() returns ExitCode
	__TestReport.useWireFormat("<<", separator: " ")
	try __test_fetches_the_page() otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```
```stderr
<<HttpError refused suite.test.maxon 13<<
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

<!-- test: explicit-otherwise-wins -->
An `otherwise` the author wrote is untouched — the substitution lives on the no-`otherwise` path
and is never consulted. Here the handler swallows the error, so the test runs to completion and
nothing is reported.
```maxon
// --- file: suite.test.maxon
enum ApiError implements Error
	notFound
end 'ApiError'

function lookup() throws ApiError
	throw ApiError.notFound
end 'lookup'

test 'tolerates a missing user'
	try lookup() otherwise (e) 'handled'
		print("handled {e}\n")
	end 'handled'
	print("carried on\n")
end 'tolerates a missing user'

function main() returns ExitCode
	__TestReport.useWireFormat("##", separator: "|")
	try __test_tolerates_a_missing_user() otherwise 'failed'
		print("test failed\n")
		return 0
	end 'failed'
	print("test passed\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
handled notFound
carried on
test passed
```

<!-- test: own-error-type-still-propagates -->
A `try` on a call that throws `TestFailure` is not foreign, so it propagates exactly as it always
did: the test fails with no report. That is what makes a report mean "something UNEXPECTED".
```maxon
// --- file: suite.test.maxon
function assertTrue(ok bool) throws TestFailure
	if not ok 'bad'
		throw TestFailure.assertion
	end 'bad'
end 'assertTrue'

test 'asserts something false'
	try assertTrue(false)
	print("kept going\n")
end 'asserts something false'

function main() returns ExitCode
	__TestReport.useWireFormat("##", separator: "|")
	try __test_asserts_something_false() otherwise 'failed'
		print("test failed\n")
		return 0
	end 'failed'
	print("test passed\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
test failed
```

<!-- test: nested-in-loop-if-and-match -->
The rule is about which FUNCTION is being parsed, not about nesting depth, so a `try` inside a
loop, an `if` and a `match` arm in a test body are all covered. The reported line is the one
holding the `try` that threw — here the `match` arm, three constructs deep.
```maxon
// --- file: suite.test.maxon
typealias Step = int(0 to 9)

enum StepError implements Error
	tooDeep
end 'StepError'

function advance(n Step) throws StepError
	if n > 1 'over'
		throw StepError.tooDeep
	end 'over'
end 'advance'

test 'walks the steps'
	for step in [0, 1, 2] 'walk'
		if step < 9 'guard'
			match step 'pick'
				0 to 9 then try advance(step)
				default panic("out of range")
			end 'pick'
		end 'guard'
	end 'walk'
	print("kept going\n")
end 'walks the steps'

function main() returns ExitCode
	__TestReport.useWireFormat("##", separator: "|")
	try __test_walks_the_steps() otherwise 'failed'
		print("test failed\n")
		return 0
	end 'failed'
	print("test passed\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
test failed
```
```stderr
##StepError|tooDeep|suite.test.maxon|17##
```

<!-- test: success-path-keeps-its-value -->
The substitution only rewrites the error path. On success the `try` still yields the callee's
value, which is what lets `let x = try f()` read naturally in a test body.
```maxon
// --- file: suite.test.maxon
typealias Tally = int(0 to 100)

enum ApiError implements Error
	notFound
end 'ApiError'

function lookup(ok bool) returns Tally throws ApiError
	if not ok 'missing'
		throw ApiError.notFound
	end 'missing'
	return 7
end 'lookup'

test 'reads a value through try'
	let found = try lookup(true)
	print("found {found}\n")
	let missing = try lookup(false)
	print("unreachable {missing}\n")
end 'reads a value through try'

function main() returns ExitCode
	__TestReport.useWireFormat("##", separator: "|")
	try __test_reads_a_value_through_try() otherwise 'failed'
		print("test failed\n")
		return 0
	end 'failed'
	print("test passed\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
found 7
test failed
```
```stderr
##ApiError|notFound|suite.test.maxon|17##
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

<!-- test: two-tests-report-independently -->
Two tests in one file each get their own handler, each naming its own error and its own line. The
synthesized binding is counter-suffixed for exactly this reason — one shared name would alias two
slots.
```maxon
// --- file: suite.test.maxon
enum FirstError implements Error
	alpha
end 'FirstError'

enum SecondError implements Error
	beta
end 'SecondError'

function first() throws FirstError
	throw FirstError.alpha
end 'first'

function second() throws SecondError
	throw SecondError.beta
end 'second'

test 'one'
	try first()
end 'one'

test 'two'
	try second()
end 'two'

function main() returns ExitCode
	__TestReport.useWireFormat("##", separator: "|")
	try __test_one() otherwise ignore
	try __test_two() otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```
```stderr
##FirstError|alpha|suite.test.maxon|18##
##SecondError|beta|suite.test.maxon|22##
```

<!-- test: try-await-is-covered-too -->
`try await` reaches the same rule through the other branch: an awaited thunk's `throws` type is
the type of that `try`'s error exactly as a call's is, so a foreign one fails the test and is
reported the same way. One check, so an await and a call cannot answer differently.
```maxon
// --- file: suite.test.maxon
typealias Integer = int(i64.min to i64.max)

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

test 'awaits a worker'
	let p = async work(-1)
	let got = try await p
	print("got {got}\n")
end 'awaits a worker'

function main() returns ExitCode
	__TestReport.useWireFormat("##", separator: "|")
	try __test_awaits_a_worker() otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```
```stderr
##WorkError|failed|suite.test.maxon|17##
```

<!-- test: file-field-is-repo-relative -->
The `file` field is the path the compile root sees, `/`-separated — the same spelling `__file__`
produces, from the same function. Absolute would be a fact about the machine that ran the compiler
rather than about the program, which is what the byte-parity gate exists to catch; this test is
what makes a subdirectory show it.
```maxon
// --- file: suite/deep.test.maxon
enum ApiError implements Error
	notFound
end 'ApiError'

function lookup() throws ApiError
	throw ApiError.notFound
end 'lookup'

test 'lives in a subdirectory'
	try lookup()
end 'lives in a subdirectory'

export function runDeepTest() throws TestFailure
	try __test_lives_in_a_subdirectory()
end 'runDeepTest'

// --- file: main.maxon
function main() returns ExitCode
	__TestReport.useWireFormat("##", separator: "|")
	try runDeepTest() otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```
```stderr
##ApiError|notFound|suite/deep.test.maxon|10##
```
