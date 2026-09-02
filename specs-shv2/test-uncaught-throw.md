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

⚠ **AND shv2 ENFORCES THAT LAST SENTENCE, WHICH IS WHY THIS FILE PORTS THREE OF THE REFERENCE'S ELEVEN
CASES AND NOT ALL OF THEM** (it carries FOUR: the fourth, `bare-try-on-a-boxed-foreign-error-compiles`,
is shv2's own and has no counterpart there — see its note).
*"No program writes either call"* is a rule here, not a convention: `requireCalleeIsNotReservedName`
refuses a `__` callee whose bytes an author typed, wherever it stands. The reference's remaining cases
hand-roll a dispatcher and therefore write exactly those calls — see the PORT NOTE below. Nothing
about the RULE depends on who calls the test; what depends on it is whether a spec can invoke one
without the runner, and here it cannot.

## Tests

⚠ **PORT NOTE — 8 OF THE REFERENCE'S 11 CASES ARE HELD BACK, UNEDITED, FOR THE RUNNER SLICE.**
They hand-roll a dispatcher: with no `maxon test` to invoke a test, each writes
`__TestReport.useWireFormat(...)` and calls the mangled `__test_<name>()` **from its own source**.
The bootstrap allows that because it has no call-side reservation at all; shv2 has one on purpose,
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
