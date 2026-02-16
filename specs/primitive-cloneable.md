---
feature: primitive-cloneable
status: stable
keywords: clone, cloneable, copy, primitives
category: type-system
---
# Primitive Cloneable

## Documentation

All built-in types (`int`, `float`, `bool`, `byte`) implement the `Cloneable`
interface. Since primitives are value types, `clone()` returns a copy of the value.

## clone()

Returns a copy of the primitive value.

**Signatures:**
- `int.clone() -> int`
- `float.clone() -> float`
- `bool.clone() -> bool`
- `byte.clone() -> byte`

**Example:**
```maxon
var x = 42
var y = x.clone()   // y is 42
```

## Tests

<!-- test: int.clone -->
```maxon
function main() returns ExitCode
  var x = 42
  return x.clone()
end 'main'
```
```exitcode
42
```

<!-- test: float.clone -->
```maxon
function main() returns ExitCode
  var x = 3.14
  var y = x.clone()
  if y == 3.14 'ok'
    return 1
  end 'ok'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: bool.clone -->
```maxon
function main() returns ExitCode
  var x = true
  if x.clone() 'ok'
    return 1
  end 'ok'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: byte.clone -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Byte = byte(0 to u8.max)

function main() returns ExitCode
  var x = 65 as Byte
  var y = x.clone() as Integer
  return y
end 'main'
```
```exitcode
65
```
