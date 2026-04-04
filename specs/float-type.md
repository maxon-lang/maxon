---
feature: float-type
status: stable
keywords: [float, floating-point, double, f64]
category: types
---

# Float Type

## Documentation

The `float` type stores 64-bit double-precision floating-point numbers.

### Syntax

```maxon
var pi = 3.14159
let ratio = 2.5
```
Float literals must include a decimal point:
- Valid: `3.14`, `2.0`, `0.5`
- Invalid: `3` (this is an int)

### Example

```maxon

typealias Radius = float(f64.min to f64.max)

function circleArea(radius Radius) returns Radius
	return 3.14159 * radius * radius
end 'circleArea'

function main() returns ExitCode
	let area = circleArea(5.0)
	return trunc(area)  // Returns 78
end 'main'
```
```exitcode
78
```


## Tests

<!-- test: basic-float -->
```maxon
function main() returns ExitCode
	let x = 3.14
	let y = 2.0
	let z = x + y
	let result = trunc(z)
	return result
end 'main'
```
```exitcode
5
```


<!-- test: float-comparison -->
```maxon
function main() returns ExitCode
	let x = 3.5
	let y = 2.1
	if x > y 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```


<!-- test: float-arithmetic -->
```maxon
function main() returns ExitCode
	let a = 10.0
	let b = 3.0
	let result = a / b
	return trunc(result)
end 'main'
```
```exitcode
3
```


<!-- test: float-promotion -->
```maxon
function main() returns ExitCode
	let x = 5
	let y = 2.0
	let result = x + y
	return trunc(result)
end 'main'
```
```exitcode
7
```


<!-- test: float-return-from-function -->
```maxon
typealias Float = float(f64.min to f64.max)

function computePi() returns Float
	return 3.14
end 'computePi'

function main() returns ExitCode
	let x = computePi()
	let result = trunc(x)
	return result
end 'main'
```
```exitcode
3
```

Note: Tests for many float parameters (>4) and float parameter preservation across calls are currently disabled due to known codegen bugs with float register allocation. See test fragments for the disabled tests.
