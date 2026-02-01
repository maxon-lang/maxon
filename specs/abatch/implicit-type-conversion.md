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
function takeFloat(x float) returns int
  return trunc(x)
end 'takeFloat'

function main() returns int
  return takeFloat(42)
end 'main'
```
```exitcode
42
```

## Tests

<!-- test: int-literal-to-float-param -->
```maxon
function takeFloat(x float) returns int
  return trunc(x)
end 'takeFloat'

function main() returns int
  return takeFloat(42)
end 'main'
```
```exitcode
42
```

<!-- test: int-var-to-float-param -->
```maxon
function takeFloat(x float) returns int
  return trunc(x)
end 'takeFloat'

function main() returns int
  var i = 42
  return takeFloat(i)
end 'main'
```
```exitcode
42
```

<!-- test: byte-to-int-param -->
```maxon
function takeInt(x int) returns int
  return x
end 'takeInt'

function main() returns int
  var b = 42 as byte
  return takeInt(b)
end 'main'
```
```exitcode
42
```

<!-- test: int-to-byte-param-truncates -->
```maxon
function takeByte(x byte) returns int
  return x as int
end 'takeByte'

function main() returns int
  return takeByte(300)
end 'main'
```
```exitcode
44
```

<!-- test: int-var-to-byte-param -->
```maxon
function takeByte(x byte) returns int
  return x as int
end 'takeByte'

function main() returns int
  var i = 300
  return takeByte(i)
end 'main'
```
```exitcode
44
```

<!-- test: float-to-int-param-truncates -->
```maxon
function takeInt(x int) returns int
  return x
end 'takeInt'

function main() returns int
  var f = 3.7
  return takeInt(f)
end 'main'
```
```exitcode
3
```

<!-- test: float-to-byte-param -->
```maxon
function takeByte(x byte) returns int
  return x as int
end 'takeByte'

function main() returns int
  var f = 300.9
  return takeByte(f)
end 'main'
```
```exitcode
44
```

<!-- test: function-return-to-byte-param -->
```maxon
function getInt() returns int
  return 300
end 'getInt'

function takeByte(x byte) returns int
  return x as int
end 'takeByte'

function main() returns int
  return takeByte(getInt())
end 'main'
```
```exitcode
44
```

<!-- test: expression-to-float-param -->
```maxon
function takeFloat(x float) returns int
  return trunc(x)
end 'takeFloat'

function main() returns int
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
function main() returns int
  var result = Math.exp(3)
  return trunc(result)
end 'main'
```
```exitcode
20
```
<!-- test: no-string-to-int -->
```maxon
function takeInt(x int) returns int
  return x
end 'takeInt'

function main() returns int
  var s = "hello"
  return takeInt(s)
end 'main'
```
```maxoncstderr
error E022: specs/fragments/implicit-type-conversion.no-string-to-int.1.test:8:5: argument type mismatch for 'x': expected 'int', got 'String'
```

<!-- test: no-bool-to-int -->
```maxon
function takeInt(x int) returns int
  return x
end 'takeInt'

function main() returns int
  var b = true
  return takeInt(b)
end 'main'
```
```maxoncstderr
error E022: specs/fragments/implicit-type-conversion.no-bool-to-int.1.test:8:5: argument type mismatch for 'x': expected 'int', got 'bool'
```

<!-- test: no-int-to-bool -->
```maxon
function takeBool(x bool) returns int
  if x 'check'
    return 1
  end 'check'
  return 0
end 'takeBool'

function main() returns int
  var i = 1
  return takeBool(i)
end 'main'
```
```maxoncstderr
error E022: specs/fragments/implicit-type-conversion.no-int-to-bool.1.test:11:5: argument type mismatch for 'x': expected 'bool', got 'int'
```
