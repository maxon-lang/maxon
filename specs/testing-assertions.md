---
feature: testing-assertions
status: experimental
keywords: [Expect, assert, assertion, matcher, testing, TestFailure, equal, close, fail]
category: infrastructure
---

# Assertions

## Documentation

`Expect` (`stdlib/Testing.maxon`) is the assertion library a `test` body calls. Every matcher is
a `static` function that **throws `TestFailure.assertion`** when it does not hold, so a test stops
at its first bad assertion and the runner sees a thrown error rather than a tally it has to be
told about.

```text
test 'adds two numbers'
	try Expect.equal(add(2, 2), expected: 4)
end 'adds two numbers'
```

Because a matcher throws, a forgotten `try` is E3057 at compile time — an assertion whose failure
nothing observes cannot be written.

### The message is printed, not carried

The error a matcher throws is `TestFailure.assertion` and nothing more. The human-readable report
is printed to **stderr at the assertion site**, before the throw.

That split is forced by the language and is the right split anyway. A caught error in Maxon is an
enum you `match`; there is no `e.message()` and no way to attach a payload rendering two values of
some arbitrary type. At the assertion site both values are still in hand and still fully typed, so
that is the one place `expected: 4` / `received: 5` can be produced at all.

A failure prints as:

```text
FAIL main.maxon:12: Expect.equal
  expected: 4
  received: 5
```

The file and line are the **caller's**, supplied by the `__file__` / `__line__` parameter defaults
(see `source-location-defaults`), so the report names the test author's line and never a line
inside `Testing.maxon`.

Every matcher also takes an optional `message`, appended as a third line:

```text
FAIL main.maxon:12: Expect.equal
  expected: 4
  received: 5
  message: two plus two
```

### `equal` is one name

`equal` and `notEqual` are overloaded across integer, `String` and `bool` rather than split into
`equalInt` / `equalText`. One name is what makes the library learnable, and overload resolution
sees through a method call, so `Expect.equal(parts.count(), expected: 3)` resolves.

`String` values are rendered **quoted**, so a trailing space or an empty string is visible in the
report; integers and bools are rendered bare.

### Floats get `close`, and deliberately get no `equal`

There is no `equal` overload for floats. A float `==` matcher is a matcher that passes on one
target and fails on another, so the rule is enforced by the signature list rather than by
documentation: `Expect.equal(1.0, expected: 1.0)` does not compile.

Floats are compared with an explicit tolerance instead:

```text
try Expect.close(measured, expected: 1.5, within: 0.01)
```

Ordering matchers (`greaterThan`, `lessThan`, `atLeast`, `atMost`) *do* have float overloads.
Comparing a float against a threshold is a stable question — the hazard that removes `equal` is
exact bit-equality, which an ordering test does not ask for.

### The roster

| Matcher | Holds when |
|---|---|
| `equal(actual, expected:)` | `actual == expected` — integer, `String` or `bool` |
| `notEqual(actual, expected:)` | `actual != expected` — integer, `String` or `bool` |
| `greaterThan(actual, than:)` | `actual > than` — integer or float |
| `lessThan(actual, than:)` | `actual < than` — integer or float |
| `atLeast(actual, than:)` | `actual >= than` — integer or float |
| `atMost(actual, than:)` | `actual <= than` — integer or float |
| `close(actual, expected:, within:)` | `abs(actual - expected) <= within` — float |
| `isTrue(actual)` / `isFalse(actual)` | the `bool` is `true` / `false` |
| `contains(haystack, needle:)` | `haystack` contains `needle` |
| `startsWith(haystack, needle:)` / `endsWith(haystack, needle:)` | prefix / suffix |
| `isEmpty(haystack)` | the `String` has no characters |
| `fail(message)` | never — the escape hatch |

`fail` is what an unreachable branch calls. It has no pair of values to compare, so its report
carries only the message.

### Anything else is `isTrue`

`isTrue` takes a `bool`, so any predicate a program can express is assertable, and `message` is
where the values go:

```text
try Expect.isTrue(a == b, message: "expected {b}, got {a}")
```

That works for any type that is `Equatable` (for `==`) and `Stringable` (for the interpolation),
which is the escape hatch for types the roster above does not name.

## Tests

<!-- test: every-matcher-passes -->
Every matcher on its holding path: nothing is printed and nothing is thrown.
```maxon
// --- file: main.maxon
function main() returns ExitCode
	try Expect.equal(5, expected: 5) otherwise ignore
	try Expect.equal("same", expected: "same") otherwise ignore
	try Expect.equal(true, expected: true) otherwise ignore
	try Expect.notEqual(5, expected: 4) otherwise ignore
	try Expect.notEqual("a", expected: "b") otherwise ignore
	try Expect.notEqual(true, expected: false) otherwise ignore
	try Expect.greaterThan(10, than: 3) otherwise ignore
	try Expect.greaterThan(10.5, than: 3.5) otherwise ignore
	try Expect.lessThan(3, than: 10) otherwise ignore
	try Expect.lessThan(3.5, than: 10.5) otherwise ignore
	try Expect.atLeast(10, than: 10) otherwise ignore
	try Expect.atLeast(10.5, than: 10.5) otherwise ignore
	try Expect.atMost(3, than: 3) otherwise ignore
	try Expect.atMost(3.5, than: 3.5) otherwise ignore
	try Expect.close(1.0, expected: 1.25, within: 0.5) otherwise ignore
	try Expect.isTrue(true) otherwise ignore
	try Expect.isFalse(false) otherwise ignore
	try Expect.contains("hello", needle: "ell") otherwise ignore
	try Expect.startsWith("hello", needle: "he") otherwise ignore
	try Expect.endsWith("hello", needle: "lo") otherwise ignore
	try Expect.isEmpty("") otherwise ignore

	print("all held\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
all held
```

<!-- test: every-matcher-reports-its-failure -->
Every matcher on its failing path, in roster order. This is the format golden.
```maxon
// --- file: main.maxon
function main() returns ExitCode
	try Expect.equal(5, expected: 4) otherwise ignore
	try Expect.equal("got", expected: "want") otherwise ignore
	try Expect.equal(true, expected: false) otherwise ignore
	try Expect.notEqual(7, expected: 7) otherwise ignore
	try Expect.notEqual("same", expected: "same") otherwise ignore
	try Expect.notEqual(false, expected: false) otherwise ignore
	try Expect.greaterThan(3, than: 10) otherwise ignore
	try Expect.greaterThan(3.5, than: 10.5) otherwise ignore
	try Expect.lessThan(10, than: 3) otherwise ignore
	try Expect.lessThan(10.5, than: 3.5) otherwise ignore
	try Expect.atLeast(3, than: 10) otherwise ignore
	try Expect.atLeast(3.5, than: 10.5) otherwise ignore
	try Expect.atMost(10, than: 3) otherwise ignore
	try Expect.atMost(10.5, than: 3.5) otherwise ignore
	try Expect.close(1.0, expected: 5.0, within: 0.5) otherwise ignore
	try Expect.isTrue(false) otherwise ignore
	try Expect.isFalse(true) otherwise ignore
	try Expect.contains("hello", needle: "xyz") otherwise ignore
	try Expect.startsWith("hello", needle: "xyz") otherwise ignore
	try Expect.endsWith("hello", needle: "xyz") otherwise ignore
	try Expect.isEmpty("hello") otherwise ignore
	try Expect.fail("explicit failure") otherwise ignore

	return 0
end 'main'
```
```exitcode
0
```
```stderr
FAIL main.maxon:2: Expect.equal
  expected: 4
  received: 5
FAIL main.maxon:3: Expect.equal
  expected: "want"
  received: "got"
FAIL main.maxon:4: Expect.equal
  expected: false
  received: true
FAIL main.maxon:5: Expect.notEqual
  expected: not 7
  received: 7
FAIL main.maxon:6: Expect.notEqual
  expected: not "same"
  received: "same"
FAIL main.maxon:7: Expect.notEqual
  expected: not false
  received: false
FAIL main.maxon:8: Expect.greaterThan
  expected: > 10
  received: 3
FAIL main.maxon:9: Expect.greaterThan
  expected: > 10.5
  received: 3.5
FAIL main.maxon:10: Expect.lessThan
  expected: < 3
  received: 10
FAIL main.maxon:11: Expect.lessThan
  expected: < 3.5
  received: 10.5
FAIL main.maxon:12: Expect.atLeast
  expected: >= 10
  received: 3
FAIL main.maxon:13: Expect.atLeast
  expected: >= 10.5
  received: 3.5
FAIL main.maxon:14: Expect.atMost
  expected: <= 3
  received: 10
FAIL main.maxon:15: Expect.atMost
  expected: <= 3.5
  received: 10.5
FAIL main.maxon:16: Expect.close
  expected: 5.0 (within 0.5)
  received: 1.0
FAIL main.maxon:17: Expect.isTrue
  expected: true
  received: false
FAIL main.maxon:18: Expect.isFalse
  expected: false
  received: true
FAIL main.maxon:19: Expect.contains
  expected: contains "xyz"
  received: "hello"
FAIL main.maxon:20: Expect.startsWith
  expected: starts with "xyz"
  received: "hello"
FAIL main.maxon:21: Expect.endsWith
  expected: ends with "xyz"
  received: "hello"
FAIL main.maxon:22: Expect.isEmpty
  expected: ""
  received: "hello"
FAIL main.maxon:23: Expect.fail
  message: explicit failure
```

<!-- test: caller-line-is-the-call-site -->
The reported location is the **caller's**, never a line inside `stdlib/Testing.maxon`. Two calls
on adjacent lines report different lines, and a call from another file reports that file.
```maxon
// --- file: helper.maxon
export function assertFromHelper() throws TestFailure
	try Expect.equal(1, expected: 2)
end 'assertFromHelper'

// --- file: main.maxon
function main() returns ExitCode
	try Expect.equal(10, expected: 20) otherwise ignore
	try Expect.equal(30, expected: 40) otherwise ignore
	try assertFromHelper() otherwise ignore

	return 0
end 'main'
```
```exitcode
0
```
```stderr
FAIL main.maxon:2: Expect.equal
  expected: 20
  received: 10
FAIL main.maxon:3: Expect.equal
  expected: 40
  received: 30
FAIL helper.maxon:2: Expect.equal
  expected: 2
  received: 1
```

<!-- test: equal-resolves-across-its-overloads -->
One overloaded name, selected by argument type — including from a method call, which is the shape
that makes `equalInt`/`equalText` unnecessary. The rendering proves which overload ran: integers
and bools are bare, `String`s are quoted.
```maxon
// --- file: main.maxon
function main() returns ExitCode
	let parts = "a,b,c".split(",")
	let word = try parts.get(1) otherwise ""

	try Expect.equal(parts.count(), expected: 3) otherwise ignore
	try Expect.equal(word, expected: "b") otherwise ignore
	try Expect.equal(parts.count() == 3, expected: true) otherwise ignore

	try Expect.equal(parts.count(), expected: 99) otherwise ignore
	try Expect.equal(word, expected: "zzz") otherwise ignore
	try Expect.equal(parts.count() == 3, expected: false) otherwise ignore

	print("done\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
done
```
```stderr
FAIL main.maxon:9: Expect.equal
  expected: 99
  received: 3
FAIL main.maxon:10: Expect.equal
  expected: "zzz"
  received: "b"
FAIL main.maxon:11: Expect.equal
  expected: false
  received: true
```

<!-- test: message-is-appended -->
`message` is optional on every matcher and appends one line; omitting it omits the line.
```maxon
// --- file: main.maxon
function main() returns ExitCode
	try Expect.equal(5, expected: 4, message: "two plus two") otherwise ignore
	try Expect.equal(5, expected: 4) otherwise ignore
	try Expect.isTrue(false, message: "the gate should have opened") otherwise ignore

	return 0
end 'main'
```
```exitcode
0
```
```stderr
FAIL main.maxon:2: Expect.equal
  expected: 4
  received: 5
  message: two plus two
FAIL main.maxon:3: Expect.equal
  expected: 4
  received: 5
FAIL main.maxon:4: Expect.isTrue
  expected: true
  received: false
  message: the gate should have opened
```

<!-- test: the-thrown-error-is-testfailure-assertion -->
The thrown value is `TestFailure.assertion` — the error is an enum to `match`, which is why the
report is printed rather than carried on it.
```maxon
// --- file: main.maxon
function main() returns ExitCode
	try Expect.equal(1, expected: 2) otherwise (e) 'caught'
		match e 'kind'
			assertion then print("caught assertion\n")
		end 'kind'
		print("rendered as {e}\n")
	end 'caught'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
caught assertion
rendered as assertion
```
```stderr
FAIL main.maxon:2: Expect.equal
  expected: 2
  received: 1
```

<!-- test: first-failure-stops-the-body -->
A matcher throws, so statements after a failed assertion do not run. This is what makes a test
report its first real problem instead of a cascade of consequences.
```maxon
// --- file: main.maxon
function runChecks() throws TestFailure
	print("before\n")
	try Expect.equal(1, expected: 2)
	print("after\n")
end 'runChecks'

function main() returns ExitCode
	try runChecks() otherwise ignore

	print("resumed\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
before
resumed
```
```stderr
FAIL main.maxon:3: Expect.equal
  expected: 2
  received: 1
```

<!-- test: isTrue-carries-any-equatable-stringable -->
The escape hatch for a type the roster does not name: `==` supplies the predicate and
interpolation supplies the rendering, so one line reports both values.
```maxon
// --- file: main.maxon
type Money implements Equatable, Stringable
	export let cents as MoneyAmount

	export static function create(cents MoneyAmount) returns Self
		return Self{cents: cents}
	end 'create'

	export function equals(other Self) returns bool
		return self.cents == other.cents
	end 'equals'

	export function toString() returns String
		return "{self.cents}c"
	end 'toString'
end 'Money'

typealias MoneyAmount = int(0 to i64.max)

function main() returns ExitCode
	let a = Money.create(150)
	let b = Money.create(99)

	try Expect.isTrue(a == b, message: "expected {b}, got {a}") otherwise ignore

	return 0
end 'main'
```
```exitcode
0
```
```stderr
FAIL main.maxon:23: Expect.isTrue
  expected: true
  received: false
  message: expected 99c, got 150c
```

<!-- test: fail-is-the-escape-hatch -->
`fail` has no pair of values, so its report is the message alone. A void helper is the shape that
keeps it honest: a `fail` placed after an exhaustive `match` is unreachable code (E3071), and one
placed before a `return` needs a sentinel value nobody will read.
```maxon
// --- file: main.maxon
typealias Reading = int(i64.min to i64.max)

function requireNonNegative(value Reading) throws TestFailure
	if value < 0 'negative'
		try Expect.fail("a negative reading should be impossible: {value}")
	end 'negative'
end 'requireNonNegative'

function main() returns ExitCode
	try requireNonNegative(5) otherwise ignore
	print("5 accepted\n")

	try requireNonNegative(-3) otherwise ignore

	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 accepted
```
```stderr
FAIL main.maxon:5: Expect.fail
  message: a negative reading should be impossible: -3
```

<!-- test: error.float-has-no-equal -->
The design rule is enforced by the signature list rather than by documentation: with no float
`equal` to select, the float argument is measured against the integer arm and refused as a lossy
implicit conversion. Either way it is a compile error, which is the point — a float `==` matcher
would pass on one target and fail on another.
```maxon
// --- file: main.maxon
function main() returns ExitCode
	try Expect.equal(1.5, expected: 1.5) otherwise ignore

	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/testing-assertions/error.float-has-no-equal.test:4:13: argument 'actual': cannot implicitly convert 'float' to 'int': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

<!-- test: error.forgotten-try -->
A matcher is a throwing call, so omitting `try` is a compile error — the property that makes an
assertion whose failure nothing observes unwritable.
```maxon
// --- file: main.maxon
function check() throws TestFailure
	Expect.equal(1, expected: 2)
end 'check'

function main() returns ExitCode
	try check() otherwise ignore

	return 0
end 'main'
```
```maxoncstderr
error E3057: specs/fragments/testing-assertions/error.forgotten-try.test:4:9: throwing function requires try: 'stdlib.Expect.equal'
```
