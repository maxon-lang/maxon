---
feature: primitive-stringable
status: stable
keywords: toString, stringable, string conversion, primitives
category: type-system
---
# Primitive Stringable

## Documentation

All built-in types (`int`, `float`, `bool`, `byte`) implement the `Stringable`
interface, allowing them to be converted to strings via `.toString()`.

## toString()

Converts a primitive value to its string representation.

**Signatures:**
- `int.toString() -> String`
- `float.toString() -> String`
- `bool.toString() -> String`
- `byte.toString() -> String`

**Example:**
```maxon
var x = 42
var s = x.toString()
print(s)   // prints "42"
```

**Notes:**
- Uses the same conversion as string interpolation

## Tests

<!-- test: int.toString -->
```maxon
function main() returns ExitCode
  var x = 42
  var s = x.toString()
  if s == "42" 'ok'
    return 1
  end 'ok'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: float.toString -->
```maxon
function main() returns ExitCode
  var x = 3.14
  var s = x.toString()
  if s == "3.14" 'ok'
    return 1
  end 'ok'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: bool.toString.true -->
```maxon
function main() returns ExitCode
  var x = true
  var s = x.toString()
  if s == "true" 'ok'
    return 1
  end 'ok'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: bool.toString.false -->
```maxon
function main() returns ExitCode
  var x = false
  var s = x.toString()
  if s == "false" 'ok'
    return 1
  end 'ok'
  return 0
end 'main'
```
```exitcode
1
```

<!-- test: byte.toString -->
```maxon

typealias Byte = u8

function main() returns ExitCode
  var x = 65 as Byte
  var s = x.toString()
  if s == "65" 'ok'
    return 1
  end 'ok'
  return 0
end 'main'
```
```exitcode
1
```
