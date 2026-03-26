---
feature: implicit-type-conversion
status: stable
keywords: [types, conversion, implicit, coercion, int, float, byte]
category: type-system
---

# Implicit Type Conversion

## Documentation

Maxon supports implicit type conversions between compatible numeric types. These conversions happen automatically when passing arguments to functions.

### Supported Implicit Conversions

| From    | To      | Behavior                                    |
|---------|---------|---------------------------------------------|
| `int`   | `float` | Convert integer to floating point           |
| `float` | `int`   | Truncate toward zero                        |
| `int`   | `byte`  | Keep lower 8 bits (0-255)                   |
| `float` | `byte`  | Truncate to int, then keep lower 8 bits     |
| `byte`  | `int`   | Zero-extend to 64-bit integer               |

### Function Arguments

Function arguments are implicitly converted to match parameter types:

```maxon
typealias Score = int(i64.min to i64.max)
typealias Weight = float(f64.min to f64.max)

function takeFloat(x Weight) returns Score
	return trunc(x)
end 'takeFloat'

function main() returns ExitCode
	return takeFloat(42)
end 'main'
```
```exitcode
42
```

## Tests

<!-- test: int-literal-to-float-param -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

function takeFloat(x Float) returns Integer
	return trunc(x)
end 'takeFloat'

function main() returns ExitCode
	return takeFloat(42)
end 'main'
```
```exitcode
42
```

<!-- test: int-var-to-float-param -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

function takeFloat(x Float) returns Integer
	return trunc(x)
end 'takeFloat'

function main() returns ExitCode
	var i = 42
	return takeFloat(i)
end 'main'
```
```exitcode
42
```

<!-- test: byte-to-int-param -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Byte = byte(0 to u8.max)

function takeInt(x Integer) returns Integer
	return x
end 'takeInt'

function main() returns ExitCode
	var b = 42 as Byte
	return takeInt(b)
end 'main'
```
```exitcode
42
```

<!-- test: int-to-byte-param-truncates -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Byte = byte(0 to u8.max)

function takeByte(x Byte) returns Integer
	return x as Integer
end 'takeByte'

function main() returns ExitCode
	return takeByte(300)
end 'main'
```
```exitcode
44
```

<!-- test: int-var-to-byte-param -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Byte = byte(0 to u8.max)

function takeByte(x Byte) returns Integer
	return x as Integer
end 'takeByte'

function main() returns ExitCode
	var i = 300
	return takeByte(i)
end 'main'
```
```exitcode
44
```

<!-- test: float-to-int-param-truncates -->
```maxon

typealias Integer = int(i64.min to i64.max)

function takeInt(x Integer) returns Integer
	return x
end 'takeInt'

function main() returns ExitCode
	var f = 3.7
	return takeInt(f)
end 'main'
```
```exitcode
3
```

<!-- test: float-to-byte-param -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Byte = byte(0 to u8.max)

function takeByte(x Byte) returns Integer
	return x as Integer
end 'takeByte'

function main() returns ExitCode
	var f = 300.9
	return takeByte(f)
end 'main'
```
```exitcode
44
```

<!-- test: function-return-to-byte-param -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Byte = byte(0 to u8.max)

function getInt() returns Integer
	return 300
end 'getInt'

function takeByte(x Byte) returns Integer
	return x as Integer
end 'takeByte'

function main() returns ExitCode
	return takeByte(getInt())
end 'main'
```
```exitcode
44
```

<!-- test: expression-to-float-param -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

function takeFloat(x Float) returns Integer
	return trunc(x)
end 'takeFloat'

function main() returns ExitCode
	var a = 20
	var b = 22
	return takeFloat(a + b)
end 'main'
```
```exitcode
42
```

<!-- test: math-intrinsic-int-promotion -->
```maxon
function main() returns ExitCode
	var result = sqrt(16)
	return trunc(result)
end 'main'
```
```exitcode
4
```

<!-- test: no-string-to-int -->
```maxon

typealias Integer = int(i64.min to i64.max)

function takeInt(x Integer) returns Integer
	return x
end 'takeInt'

function main() returns ExitCode
	var s = "hello"
	return takeInt(s)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/implicit-type-conversion/no-string-to-int.test:11:9: argument type mismatch for 'x': expected 'Integer', got 'String'
```

<!-- test: no-bool-to-int -->
```maxon

typealias Integer = int(i64.min to i64.max)

function takeInt(x Integer) returns Integer
	return x
end 'takeInt'

function main() returns ExitCode
	var b = true
	return takeInt(b)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/implicit-type-conversion/no-bool-to-int.test:11:9: argument type mismatch for 'x': expected 'int', got 'bool'
```

<!-- test: no-int-to-bool -->
```maxon

typealias Integer = int(i64.min to i64.max)

function takeBool(x bool) returns Integer
	if x 'check'
		return 1
	end 'check'
	return 0
end 'takeBool'

function main() returns ExitCode
	var i = 1
	return takeBool(i)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/implicit-type-conversion/no-int-to-bool.test:14:9: argument type mismatch for 'x': expected 'bool', got 'int'
```
