---
feature: contextual-literal-typing
status: stable
keywords: [literals, types, contextual, byte, int, type-inference]
category: type-system
---

# Contextual Literal Typing

## Documentation

Maxon uses contextual literal typing to allow integer and byte literals to adapt to their expected type context in comparisons and function calls.

### Byte and Int Literals

Integer literals in the range 0-255 can be compared directly with byte values:

```maxon
typealias Pixel = int(0 to u8.max)

function main() returns ExitCode
	let b = 100 as Pixel
	if b == 50 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

Byte variables can be compared directly with int literals in the 0-255 range:

```maxon
typealias Pixel = int(0 to u8.max)

function main() returns ExitCode
	let b = 200 as Pixel
	if b == 200 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### No Int/Float Mixing

Comparisons between int and float types require explicit casts:

```text
var x = 5
var y = 5.0
if x == y 'check'    // Error: type mismatch
  return 1
end 'check'
```

To compare, cast explicitly:

```maxon
typealias Decimal = float(f64.min to f64.max)

function main() returns ExitCode
	let x = 5
	let y = 5.0
	if (x as Decimal) == y 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

### Math Intrinsics

Math intrinsics like `sin`, `cos`, `sqrt`, etc. accept both int and float arguments (int is promoted to float):

```maxon
function main() returns ExitCode
	let x = sqrt(16.0)
	return trunc(x)
end 'main'
```
```exitcode
4
```

## Tests

<!-- test: int-literal-vs-byte-valid -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = 42 as Byte
	if b == 42 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: int-vs-float-error -->
```maxon
function main() returns ExitCode
	let x = 5
	let y = 5.0
	if x == y 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/contextual-literal-typing/int-vs-float-error.test:5:7: type mismatch: 'cannot compare int with float'
```

<!-- test: float-vs-int-error -->
```maxon
function main() returns ExitCode
	let x = 5.0
	let y = 5
	if x == y 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/contextual-literal-typing/float-vs-int-error.test:5:7: type mismatch: 'cannot compare float with int'
```

<!-- test: int-literal-vs-float-error -->
```maxon
function main() returns ExitCode
	let x = 5.0
	if x == 5 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/contextual-literal-typing/int-literal-vs-float-error.test:4:7: type mismatch: 'cannot compare float with int'
```

<!-- test: float-literal-vs-int-error -->
```maxon
function main() returns ExitCode
	let x = 5
	if x == 5.0 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/contextual-literal-typing/float-literal-vs-int-error.test:4:7: type mismatch: 'cannot compare int with float'
```

<!-- test: explicit-cast-int-to-float -->
```maxon

typealias Float = float(f64.min to f64.max)

function main() returns ExitCode
	let x = 5
	let y = 5.0
	if (x as Float) == y 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: explicit-cast-float-to-int -->
```maxon
function main() returns ExitCode
	let x = 5
	let y = 5.0
	if x == trunc(y) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: math-intrinsic-with-int -->
```maxon
function main() returns ExitCode
	let x = 16
	let result = sqrt(x)
	return trunc(result)
end 'main'
```
```exitcode
4
```

<!-- test: math-intrinsic-with-float-literal -->
```maxon
function main() returns ExitCode
	let x = sqrt(16.0)
	return trunc(x)
end 'main'
```
```exitcode
4
```

<!-- test: byte-vs-byte -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let a = 50 as Byte
	let b = 50 as Byte
	if a == b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: int-vs-int -->
```maxon
function main() returns ExitCode
	let a = 1000
	let b = 1000
	if a == b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: float-vs-float -->
```maxon
function main() returns ExitCode
	let a = 3.14
	let b = 3.14
	if a == b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

