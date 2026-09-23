---
title: Testing
description: "Writing tests in Maxon: test declarations, expectations, and running them with maxon test."
sidebar:
  order: 11
---

Tests are part of the language: a `test` declaration sits beside the code it tests, and
[`maxon test`](/docs/cli/) finds, compiles and runs them.

## Test Declarations

A `test` is a top-level declaration named with a quoted phrase instead of an identifier:

```maxon
test 'adds two numbers'
	Expect.equal(2 + 2, expected: 4)
end 'adds two numbers'
```

- The name may contain any character except `'`, and the `end` label repeats it exactly (**E2008** on a
  mismatch). An empty name is **E2059**.
- A test takes no parameters and has no `returns` clause.
- `test` is a contextual keyword: it opens a declaration only at the top level and only when a quoted name
  follows, so `test` remains usable as a variable or parameter name.

## Test Files

Tests live in files whose names end in **`.test.maxon`**. A `test` in any other file is **E2058**. A regular
build skips `*.test.maxon` files, and `maxon test` compiles them together with the rest of the project and
a generated entry point — the project does not need a `main`.

```text
temperature/
├── temperature.maxon          # the code
└── temperature.test.maxon     # its tests
```

## Assertions

Every test implicitly declares `throws TestFailure` (you cannot write the clause yourself). The `Expect`
assertions throw `TestFailure.assertion` when they fail, and a test body calls them without `try`: the test
handles every error its body raises ([Uncaught Errors in Tests](#uncaught-errors-in-tests)), and a
`TestFailure` fails it. Outside a test — in a helper function a test calls — an assertion needs `try` like
any throwing call, and a forgotten one is a compile error (**E3057**), never an assertion whose failure goes
unnoticed.

`temperature.maxon`:

```maxon
export typealias Celsius = int(-273 to 10000)
export typealias Fahrenheit = int(-460 to 18032)

/// Converts a Celsius reading to Fahrenheit, rounding toward zero.
export function toFahrenheit(c Celsius) returns Fahrenheit
	return c * 9 / 5 + 32
end 'toFahrenheit'

export function describe(c Celsius) returns String
	if c <= 0 'freezing'
		return "freezing"
	end 'freezing'

	return "above freezing"
end 'describe'
```

`temperature.test.maxon`:

```maxon
test 'boiling point converts'
	Expect.equal(toFahrenheit(100) as AssertedInt, expected: 212)
end 'boiling point converts'

test 'zero is freezing'
	Expect.equal(describe(0), expected: "freezing")
	Expect.startsWith(describe(5), needle: "above")
end 'zero is freezing'

test 'body temperature'
	Expect.equal(toFahrenheit(37) as AssertedInt, expected: 98, message: "rounds toward zero")
end 'body temperature'
```

```text
$ maxon test temperature --no-timing
temperature/temperature.test.maxon:
  ✓ boiling point converts
  ✓ zero is freezing
  ✓ body temperature

 3 pass
 0 fail

 3 tests across 1 file.
```

The integer assertions take `AssertedInt` (`int(i64.min to i64.max)`), so a value of another alias is cast
to it; float assertions take `AssertedReal`. The matchers:

| Matcher | Checks |
|---------|--------|
| `Expect.equal(actual, expected:)`, `Expect.notEqual(actual, expected:)` | integers, strings, booleans |
| `Expect.greaterThan(actual, than:)`, `lessThan`, `atLeast`, `atMost` | integers and floats |
| `Expect.close(actual, expected:, within:)` | floats within a tolerance |
| `Expect.isTrue(actual)`, `Expect.isFalse(actual)` | booleans |
| `Expect.contains(haystack, needle:)`, `startsWith`, `endsWith`, `isEmpty` | strings |
| `Expect.fail(message)` | always fails |

Every matcher also takes an optional `message:` that is printed when it fails, and reports the line of the
assertion itself. A failing assertion prints what it expected and what it received — had the last test
expected `99`, the run would report:

```text
FAIL  temperature/temperature.test.maxon > body temperature
  FAIL temperature.test.maxon:11: Expect.equal
    expected: 99
    received: 98
    message: rounds toward zero
```

The full assertion reference is on the [Testing](/docs/stdlib/testing/) page of the standard library.

## Uncaught Errors in Tests

Inside a test body, a call to a throwing function, a call to a throwing interface method and an `await` of
a throwing promise need no `try`: the test handles every error its body raises, of **any** error type — not
only `TestFailure`. Any other error fails that test and reports the error and the line of the operation that
threw it:

```maxon
test 'a missing user throws'
	let name = lookup(0)          // lookup throws LookupError
	Expect.equal(name, expected: "user")
end 'a missing user throws'
```

```text
FAIL  users/lookup.test.maxon > a missing user throws
  threw LookupError.notFound
  at lookup.test.maxon:2
```

- A bare `try` means the same thing, and an `otherwise` clause you write always takes precedence. An
  explicit `try` covers only the operation that produces its value; a throwing argument or operand inside it
  is handled by the test. A [`try` block](/docs/language/error-handling/#try-blocks) inside the test handles its own body's errors first.
- The rule applies to the test body only. In an ordinary function a throwing call without `try` is still
  **E3057** and a bare `try` on another error type is still **E3059**, and a closure written inside a test
  is an ordinary function.
- A `panic` cannot be caught; the test is reported as crashed.

## Test Diagnostics

| Code | Cause |
|------|-------|
| E2008 | the `end` label does not repeat the test's name |
| E2058 | a `test` declaration outside a `*.test.maxon` file |
| E2059 | an empty test name |
| E3057 | an assertion (or other throwing call) without `try` outside a test body — in a helper function, or in a closure written inside a test |
| E3107 | two tests in one file whose names compile to the same symbol — each character outside `A–Z`, `a–z`, `0–9` and `_` becomes `_`, so `'adds two'` and `'adds-two'` collide |

## Running Tests

`maxon test [directory]` discovers every `test` under the directory, runs them and exits `0` when all pass,
`1` when any fails (or there are none), and `2` when the tests do not compile. `--list` prints the tests
without running them. A test binary's heap is checked for leaks like any program's, and a leaking test is
reported as such. See the [CLI reference](/docs/cli/) for its flags.
