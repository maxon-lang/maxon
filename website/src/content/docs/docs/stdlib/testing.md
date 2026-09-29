---
title: Testing
description: The expectation API tests are written with.
sidebar:
  order: 8
---

`Expect` is the assertion library a `test` declaration uses. Every matcher throws `TestFailure.assertion`
when it does not hold, so a test stops at its first failed assertion. A test body calls matchers without
`try`; in a helper function a test calls, a matcher needs `try` like any throwing call.
`maxon test` runs a project's tests — see [CLI_REFERENCE.md](/docs/cli/).

```maxon
test 'splits on commas'
	let parts = "a,b,c".split(",")
	Expect.equal(parts.count(), expected: 3)
	Expect.equal("a,b".replace(",", with: ";"), expected: "a;b")
	Expect.close(0.1 + 0.2, expected: 0.3, within: 0.000001)
	Expect.isTrue(parts.count() == 3, message: "three parts")
	Expect.contains("haystack", needle: "st")
end 'splits on commas'
```

A failure prints a report to stderr at the assertion, naming the caller's file and line:

```text
FAIL split.maxtest:20: Expect.equal
  expected: 5
  received: 4
  message: two plus two
```

The `message:` line appears only when a message was given.

## Matchers

Every matcher also takes `message String = ""`, `file String = __file__` and
`line SourceLineNumber = __line__`; only `fail` requires its message.

| Matcher | Argument types | Holds when |
|---------|----------------|------------|
| `Expect.equal(actual, expected:)` | any `Equatable` type | `actual == expected` |
| `Expect.notEqual(actual, expected:)` | any `Equatable` type | `actual != expected` |
| `Expect.greaterThan(actual, than:)` | any `Comparable` type | `actual > than` |
| `Expect.lessThan(actual, than:)` | any `Comparable` type | `actual < than` |
| `Expect.atLeast(actual, than:)` | any `Comparable` type | `actual >= than` |
| `Expect.atMost(actual, than:)` | any `Comparable` type | `actual <= than` |
| `Expect.close(actual, expected:, within:)` | `AssertedReal` | `abs(actual - expected) <= within` |
| `Expect.isTrue(actual)` | `bool` | `actual` is `true` |
| `Expect.isFalse(actual)` | `bool` | `actual` is `false` |
| `Expect.contains(haystack, needle:)` | `String` | `haystack` contains `needle` |
| `Expect.startsWith(haystack, needle:)` | `String` | prefix match |
| `Expect.endsWith(haystack, needle:)` | `String` | suffix match |
| `Expect.isEmpty(haystack)` | `String` | no characters |
| `Expect.fail(message)` | — | never; fails unconditionally |

`equal`, `notEqual` and the four ordering matchers are each one
[generic function](/docs/language/composite-types/#generic-functions): the type is inferred from the two arguments, so
`Expect.equal(parts.count(), expected: 3)` compares two `ElementIndex` values and a value of any alias needs
no cast. Each value is printed as its interpolation prints it; `String` values are quoted, so empty or
space-padded values stay visible.

`close` compares two floats within a tolerance and takes `AssertedReal`, so a non-float argument is refused
at the call. NaN satisfies no comparison, so it fails `close` and every ordering matcher.

`isTrue` is the general form for any predicate:
`Expect.isTrue(a == b, message: "expected {b}, got {a}")`.

```maxon
enum TestFailure implements Error
	assertion
end 'TestFailure'
```

A `test` declaration is implicitly `throws TestFailure`. An error of any other type raised in a test body is
reported as a failure naming the error and the line of the operation that threw it.
