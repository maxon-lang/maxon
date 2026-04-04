---
feature: primitive-comparable
status: stable
keywords: compare, comparable, ordering, primitives
category: type-system
---
# Primitive Comparable

## Documentation

All built-in types (`int`, `float`, `bool`, `byte`) implement the `Comparable`
interface, allowing them to be ordered and compared.

## compare(other)

Compares two values and returns an `Ordering` enum value.

**Signatures:**
- `int.compare(other int) -> Ordering`
- `float.compare(other float) -> Ordering`
- `bool.compare(other bool) -> Ordering`
- `byte.compare(other byte) -> Ordering`

**Returns:**
- `Ordering.lessThan` if `self < other`
- `Ordering.equalTo` if `self == other`
- `Ordering.greaterThan` if `self > other`

**NaN handling (float only):**
Float comparison uses a total ordering where NaN sorts below all other values
(including negative infinity). Two NaN values compare as equal. This matches
Swift's `isTotallyOrdered(belowOrEqualTo:)` semantics.

**Example:**
```maxon
var a = 10
var b = 20
var c = a.compare(b)   // returns Ordering.lessThan
```

**Notes:**
- For bool, `false < true`
- Float comparison follows IEEE semantics

## Tests

<!-- test: int.compare.less -->
```maxon
function main() returns ExitCode
	let a = 10
	let b = 20
	let result = a.compare(b)
	match result 'check'
		lessThan then return 0
		equalTo then return 1
		greaterThan then return 1
	end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: int.compare.equal -->
```maxon
function main() returns ExitCode
	let a = 42
	let b = 42
	let result = a.compare(b)
	match result 'check'
		lessThan then return 1
		equalTo then return 0
		greaterThan then return 1
	end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: int.compare.greater -->
```maxon
function main() returns ExitCode
	let a = 20
	let b = 10
	let result = a.compare(b)
	match result 'check'
		lessThan then return 1
		equalTo then return 1
		greaterThan then return 0
	end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: bool.compare.false-true -->
```maxon
function main() returns ExitCode
	let a = false
	let b = true
	let result = a.compare(b)
	match result 'check'
		lessThan then return 0
		equalTo then return 1
		greaterThan then return 1
	end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: bool.compare.equal -->
```maxon
function main() returns ExitCode
	let a = true
	let b = true
	let result = a.compare(b)
	match result 'check'
		lessThan then return 1
		equalTo then return 0
		greaterThan then return 1
	end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: float.compare.less -->
```maxon
function main() returns ExitCode
	let a = 1.5
	let b = 2.5
	let result = a.compare(b)
	match result 'check'
		lessThan then return 0
		equalTo then return 1
		greaterThan then return 1
	end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: float.compare.greater -->
```maxon
function main() returns ExitCode
	let a = 3.14
	let b = 2.71
	let result = a.compare(b)
	match result 'check'
		lessThan then return 1
		equalTo then return 1
		greaterThan then return 0
	end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: float.compare.nan-less-than-normal -->
```maxon
function main() returns ExitCode
	let nan = 0.0 / 0.0
	let x = 42.0
	let result = nan.compare(x)
	match result 'check'
		lessThan then return 0
		equalTo then return 1
		greaterThan then return 1
	end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: float.compare.normal-greater-than-nan -->
```maxon
function main() returns ExitCode
	let nan = 0.0 / 0.0
	let x = 42.0
	let result = x.compare(nan)
	match result 'check'
		lessThan then return 1
		equalTo then return 1
		greaterThan then return 0
	end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: float.compare.nan-nan-equal -->
```maxon
function main() returns ExitCode
	let a = 0.0 / 0.0
	let b = 0.0 / 0.0
	let result = a.compare(b)
	match result 'check'
		lessThan then return 1
		equalTo then return 0
		greaterThan then return 1
	end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: float.compare.nan-less-than-negative -->
```maxon
function main() returns ExitCode
	let nan = 0.0 / 0.0
	let x = -999999.0
	let result = nan.compare(x)
	match result 'check'
		lessThan then return 0
		equalTo then return 1
		greaterThan then return 1
	end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: float.compare.positive-negative-zero -->
```maxon
function main() returns ExitCode
	let pos = 0.0
	let neg = -0.0
	let result = pos.compare(neg)
	match result 'check'
		lessThan then return 1
		equalTo then return 0
		greaterThan then return 1
	end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: byte.compare.less -->
```maxon

typealias Byte = byte(0 to u8.max)

function main() returns ExitCode
	let a = 10 as Byte
	let b = 20 as Byte
	let result = a.compare(b)
	match result 'check'
		lessThan then return 0
		equalTo then return 1
		greaterThan then return 1
	end 'check'
end 'main'
```
```exitcode
0
```
