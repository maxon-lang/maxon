---
feature: tuples
status: stable
keywords: [tuple, pair, destructuring, positional]
category: types
---

# Tuples

## Documentation

### Overview

Tuples are fixed-size, ordered collections of values with potentially different types. They use parenthesized syntax for both type annotations and literals.

```text
var point = (10, 20)
var pair = (42, "hello")
```

### Element Access

Access tuple elements using positional dot syntax `.0`, `.1`, `.2`, etc.:

```text
var t = (10, 20)
t.0   // 10
t.1   // 20
```

### Destructuring

Tuples can be destructured into individual variables:

```text
var (x, y) = (10, 20)
// x is 10, y is 20
```

Tuple destructuring also works in `for` loops when the iterator returns a tuple:

```text
var m = ["a": 1, "b": 2]
for (key, value) in m 'loop'
  print("{key}: {value}\n")
end 'loop'
```

### As Function Parameters and Return Types

Tuples can be used as function parameters and return types:

```text
function swap(t (Integer, Integer)) returns (Integer, Integer)
  return (t.1, t.0)
end 'swap'
```

## Tests

<!-- test: basic-tuple -->
```maxon
function main() returns ExitCode
	let t = (10, 32)
	return t.0 + t.1
end 'main'
```
```exitcode
42
```

<!-- test: mixed-type-tuple -->
```maxon
function main() returns ExitCode
	let t = (40, 2.5)
	return t.0 + trunc(t.1)
end 'main'
```
```exitcode
42
```

<!-- test: tuple-as-param -->
```maxon

typealias Integer = int(i64.min to i64.max)

function sum(t (Integer, Integer)) returns Integer
	return t.0 + t.1
end 'sum'

function main() returns ExitCode
	let t = (10, 32)
	return sum(t)
end 'main'
```
```exitcode
42
```

<!-- test: tuple-as-return -->
```maxon

typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	let t = makePair(10, b: 32)
	return t.0 + t.1
end 'main'
```
```exitcode
42
```

<!-- test: tuple-destructuring -->
```maxon

typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	let (x, y) = makePair(10, b: 32)
	return x + y
end 'main'
```
```exitcode
42
```

<!-- test: three-element-tuple -->
```maxon
function main() returns ExitCode
	let t = (1, 2, 39)
	return t.0 + t.1 + t.2
end 'main'
```
```exitcode
42
```

<!-- test: tuple-field-write -->
```maxon
function main() returns ExitCode
	var t = (0, 0)
	t.0 = 20
	t.1 = 22
	return t.0 + t.1
end 'main'
```
```exitcode
42
```

<!-- test: tuple-with-string -->
```maxon
function main() returns ExitCode
	let t = (42, "hello")
	return t.0
end 'main'
```
```exitcode
42
```

<!-- test: let-destructuring -->
```maxon
function main() returns ExitCode
	let (x, y) = (10, 32)
	return x + y
end 'main'
```
```exitcode
42
```

<!-- test: for-destructuring-map -->
```maxon
function main() returns ExitCode
	let m = ["a": 10, "b": 32]
	var sum = 0
	for (_, value) in m 'loop'
		sum = sum + value
	end 'loop'
	return sum
end 'main'
```
```exitcode
42
```

<!-- test: destructure-match-result-then-compare -->
Destructuring `let (a, b) = match X { … gives (x, y) }` binds the elements of a
tuple produced by a match-expression arm, then COMPARES each binding. The
match-result merge slot refines to `genericInstance(__Tuple2, [Slot, bool])`
only on a later type-resolution converge pass; before that, the destructure's
`tmp._0` / `tmp._1` field reads off the still-unspecialised `named(__Tuple2)`
receiver yield the bare `_T0` / `_T1` tuple type-parameters. Recording those
froze the bindings (and the cmps on them — the operand-type stamp is kept once
non-unresolved) at the placeholders, so `slot != 5` demanded `_T0 is Equatable`
and `flag != true` reported a category mismatch. The tuple field-load now stays
unresolved until the receiver refines, so both bindings resolve to their
concrete element types. Returns `2`.
```maxon
typealias Slot = int(0 to 100)

union Thing
	alpha(s Slot, flag bool)
	beta
end 'Thing'

function pick(t Thing) returns ExitCode
	let (slot, flag) = match t 'm'
		alpha(s, f) gives (s, f)
		beta gives (0, false)
	end 'm'
	if slot != 5 'notFive'
		return 1
	end 'notFive'
	if flag != true 'notTrue'
		return 3
	end 'notTrue'
	return 2
end 'pick'

function main() returns ExitCode
	return pick(Thing.alpha(5, flag: true))
end 'main'
```
```exitcode
2
```
