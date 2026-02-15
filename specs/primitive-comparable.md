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

Compares two values and returns an ordering indicator.

**Signatures:**
- `int.compare(other int) -> int`
- `float.compare(other float) -> int`
- `bool.compare(other bool) -> int`
- `byte.compare(other byte) -> int`

**Returns:**
- `-1` if `self < other`
- `0` if `self == other`
- `1` if `self > other`

**Example:**
```maxon
var a = 10
var b = 20
var c = a.compare(b)   // returns -1
```

**Notes:**
- For bool, `false < true`
- Float comparison follows IEEE semantics

## Tests

<!-- test: int.compare.less -->
```maxon
function main() returns ExitCode
  var a = 10
  var b = 20
  print("{a.compare(b)}\n")
  return 0
end 'main'
```
```stdout
-1
```

<!-- test: int.compare.equal -->
```maxon
function main() returns ExitCode
  var a = 42
  var b = 42
  return a.compare(b)
end 'main'
```
```exitcode
0
```

<!-- test: int.compare.greater -->
```maxon
function main() returns ExitCode
  var a = 20
  var b = 10
  return a.compare(b)
end 'main'
```
```exitcode
1
```

<!-- test: bool.compare.false-true -->
```maxon
function main() returns ExitCode
  var a = false
  var b = true
  print("{a.compare(b)}\n")
  return 0
end 'main'
```
```stdout
-1
```

<!-- test: bool.compare.equal -->
```maxon
function main() returns ExitCode
  var a = true
  var b = true
  return a.compare(b)
end 'main'
```
```exitcode
0
```

<!-- test: float.compare.less -->
```maxon
function main() returns ExitCode
  var a = 1.5
  var b = 2.5
  print("{a.compare(b)}\n")
  return 0
end 'main'
```
```stdout
-1
```

<!-- test: float.compare.greater -->
```maxon
function main() returns ExitCode
  var a = 3.14
  var b = 2.71
  return a.compare(b)
end 'main'
```
```exitcode
1
```

<!-- test: byte.compare.less -->
```maxon
function main() returns ExitCode
  var a = 10 as Byte
  var b = 20 as Byte
  print("{a.compare(b)}\n")
  return 0
end 'main'
```
```stdout
-1
```
