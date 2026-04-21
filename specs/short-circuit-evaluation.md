---
feature: short-circuit-evaluation
status: implemented
keywords: [and, or, short-circuit, boolean, logical, operators, lazy]
category: operators
---

# Short-Circuit Evaluation

## Documentation

The `and` and `or` operators use short-circuit evaluation when both operands are
`bool`. The right-hand side is evaluated only if the left-hand side does not
already determine the result:

- `a and b` — if `a` is `false`, `b` is not evaluated; the result is `false`.
- `a or b`  — if `a` is `true`,  `b` is not evaluated; the result is `true`.

This allows idioms that rely on the left-hand guard to make the right-hand side
safe to evaluate, for example checking an index before calling `.get()` or
checking for non-null state before dereferencing it.

```text
if i < arr.count() and arr.get(i) > 0 'check'
	...
end 'check'
```

On integer operands, `and` and `or` remain bitwise — they always evaluate both
sides because there is no bit pattern that makes evaluating the other operand
redundant.

### Chaining

Short-circuit semantics compose through chains of `and`/`or` expressions. In
`a and b and c`, if `a` is `false`, neither `b` nor `c` is evaluated. In
`a or b or c`, if `a` is `true`, neither `b` nor `c` is evaluated.

## Tests

<!-- test: and-skips-right-when-left-false -->
```maxon
var sideEffectCount = 0

function trackAndReturn(result bool) returns bool
	sideEffectCount = sideEffectCount + 1
	return result
end 'trackAndReturn'

function main() returns ExitCode
	let result = trackAndReturn(false) and trackAndReturn(true)
	if result 'r'
		return 99
	end 'r'
	return sideEffectCount
end 'main'
```
```exitcode
1
```

<!-- test: and-evaluates-right-when-left-true -->
```maxon
var sideEffectCount = 0

function trackAndReturn(result bool) returns bool
	sideEffectCount = sideEffectCount + 1
	return result
end 'trackAndReturn'

function main() returns ExitCode
	let result = trackAndReturn(true) and trackAndReturn(true)
	if not result 'r'
		return 99
	end 'r'
	return sideEffectCount
end 'main'
```
```exitcode
2
```

<!-- test: or-skips-right-when-left-true -->
```maxon
var sideEffectCount = 0

function trackAndReturn(result bool) returns bool
	sideEffectCount = sideEffectCount + 1
	return result
end 'trackAndReturn'

function main() returns ExitCode
	let result = trackAndReturn(true) or trackAndReturn(false)
	if not result 'r'
		return 99
	end 'r'
	return sideEffectCount
end 'main'
```
```exitcode
1
```

<!-- test: or-evaluates-right-when-left-false -->
```maxon
var sideEffectCount = 0

function trackAndReturn(result bool) returns bool
	sideEffectCount = sideEffectCount + 1
	return result
end 'trackAndReturn'

function main() returns ExitCode
	let result = trackAndReturn(false) or trackAndReturn(true)
	if not result 'r'
		return 99
	end 'r'
	return sideEffectCount
end 'main'
```
```exitcode
2
```

<!-- test: and-chain-short-circuits-on-first-false -->
```maxon
var sideEffectCount = 0

function trackAndReturn(result bool) returns bool
	sideEffectCount = sideEffectCount + 1
	return result
end 'trackAndReturn'

function main() returns ExitCode
	let result = trackAndReturn(true) and trackAndReturn(false) and trackAndReturn(true)
	if result 'r'
		return 99
	end 'r'
	return sideEffectCount
end 'main'
```
```exitcode
2
```

<!-- test: or-chain-short-circuits-on-first-true -->
```maxon
var sideEffectCount = 0

function trackAndReturn(result bool) returns bool
	sideEffectCount = sideEffectCount + 1
	return result
end 'trackAndReturn'

function main() returns ExitCode
	let result = trackAndReturn(false) or trackAndReturn(true) or trackAndReturn(false)
	if not result 'r'
		return 99
	end 'r'
	return sideEffectCount
end 'main'
```
```exitcode
2
```

<!-- test: guard-protects-right-side -->
```maxon
typealias Index = int(0 to u64.max)
typealias IntArray = Array with Index

function main() returns ExitCode
	let arr = [Index{10}, Index{20}, Index{30}]
	let i = Index{5}

	if i < arr.count() and (try arr.get(i) otherwise Index{0}) > Index{0} 'check'
		return 1
	end 'check'
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: bitwise-and-or-still-evaluate-both -->
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let a = Integer{12}
	let b = Integer{10}
	let andResult = a and b
	let orResult = a or b
	return (andResult + orResult) as ExitCode
end 'main'
```
```exitcode
22
```

<!-- test: short-circuit-in-if-condition -->
```maxon
typealias Integer = int(0 to u64.max)

var trace = Integer{0}

function setFlag(bit Integer) returns bool
	trace = trace or bit
	return false
end 'setFlag'

function main() returns ExitCode
	if setFlag(Integer{1}) and setFlag(Integer{2}) 'never'
		return 99
	end 'never'
	return trace as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: short-circuit-in-while-condition -->
```maxon
var trace = 0

function check() returns bool
	trace = trace + 1
	return trace < 3
end 'check'

function alwaysFalse() returns bool
	trace = trace + 100
	return false
end 'alwaysFalse'

function main() returns ExitCode
	while check() and alwaysFalse() 'loop'
		return 99
	end 'loop'
	return trace
end 'main'
```
```exitcode
101
```

<!-- test: nested-short-circuit -->
```maxon
var calls = 0

function a() returns bool
	calls = calls + 1
	return true
end 'a'

function b() returns bool
	calls = calls + 10
	return false
end 'b'

function c() returns bool
	calls = calls + 100
	return true
end 'c'

function main() returns ExitCode
	let inner = b() or c()
	let result = a() and inner
	if result 'r'
		return calls as ExitCode
	end 'r'
	return 99
end 'main'
```
```exitcode
111
```
