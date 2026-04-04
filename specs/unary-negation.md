---
feature: unary-negation
status: stable
keywords: [unary, negation, minus, operator]
category: expressions
---

# Unary Negation

## Documentation

### Unary Negation

The unary minus operator `-` negates a numeric value:

```text
var x = 5
var y = -x       // y is -5
var z = -10      // z is -10
```

It works with both integers and floats:

```text
var f = 3.14
var g = -f       // g is -3.14
```

## Tests

<!-- test: unary-negate-variable -->
```maxon
function main() returns ExitCode
	let x = 47
	let y = -x
	print("{42 + y}\n")
	return 0
end 'main'
```
```stdout
-5
```

<!-- test: unary-negate-literal -->
```maxon
function main() returns ExitCode
	let x = -5
	return 47 + x
end 'main'
```
```exitcode
42
```

<!-- test: unary-double-negate -->
```maxon
function main() returns ExitCode
	let x = 42
	let y = --x
	return y
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/unary-negation/unary-double-negate.test:4:11: Expected expression but got '-'
```

<!-- test: unary-negate-expression -->
```maxon
function main() returns ExitCode
	let x = 10
	let y = 5
	let z = -(x + y)
	return 57 + z
end 'main'
```
```exitcode
42
```

<!-- test: unary-negate-float -->
```maxon
function main() returns ExitCode
	let f = 3.5
	let g = -f
	return trunc(g + 45.5)
end 'main'
```
```exitcode
42
```

<!-- test: unary-double-negate-float -->
```maxon
function main() returns ExitCode
	let f = 42.0
	let g = --f
	return trunc(g)
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/unary-negation/unary-double-negate-float.test:4:11: Expected expression but got '-'
```
