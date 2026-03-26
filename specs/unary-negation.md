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
	var x = 47
	var y = -x
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
	var x = -5
	return 47 + x
end 'main'
```
```exitcode
42
```

<!-- test: unary-double-negate -->
```maxon
function main() returns ExitCode
	var x = 42
	var y = --x
	return y
end 'main'
```
```exitcode
42
```

<!-- test: unary-negate-expression -->
```maxon
function main() returns ExitCode
	var x = 10
	var y = 5
	var z = -(x + y)
	return 57 + z
end 'main'
```
```exitcode
42
```

<!-- test: unary-negate-float -->
```maxon
function main() returns ExitCode
	var f = 3.5
	var g = -f
	return trunc(g + 45.5)
end 'main'
```
```exitcode
42
```

<!-- test: unary-double-negate-float -->
```maxon
function main() returns ExitCode
	var f = 42.0
	var g = --f
	return trunc(g)
end 'main'
```
```exitcode
42
```
