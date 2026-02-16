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

Compares two values and returns an `Ordering` enum value.

**Signatures:**
- `int.compare(other int) -> Ordering`
- `float.compare(other float) -> Ordering`
- `bool.compare(other bool) -> Ordering`
- `byte.compare(other byte) -> Ordering`

**Returns:**
- `Ordering.lessThan` if `self < other`
- `Ordering.equalTo` if `self == other`
- `Ordering.greaterThan` if `self > other`

**Example:**
```maxon
var a = 10
var b = 20
var c = a.compare(b)   // returns Ordering.lessThan
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
  var result = a.compare(b)
  if result == Ordering.lessThan 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: int.compare.equal -->
```maxon
function main() returns ExitCode
  var a = 42
  var b = 42
  var result = a.compare(b)
  if result == Ordering.equalTo 'check'
    return 0
  end 'check'
  return 1
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
  var result = a.compare(b)
  if result == Ordering.greaterThan 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: bool.compare.false-true -->
```maxon
function main() returns ExitCode
  var a = false
  var b = true
  var result = a.compare(b)
  if result == Ordering.lessThan 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: bool.compare.equal -->
```maxon
function main() returns ExitCode
  var a = true
  var b = true
  var result = a.compare(b)
  if result == Ordering.equalTo 'check'
    return 0
  end 'check'
  return 1
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
  var result = a.compare(b)
  if result == Ordering.lessThan 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: float.compare.greater -->
```maxon
function main() returns ExitCode
  var a = 3.14
  var b = 2.71
  var result = a.compare(b)
  if result == Ordering.greaterThan 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: byte.compare.less -->
```maxon
function main() returns ExitCode
  var a = 10 as Byte
  var b = 20 as Byte
  var result = a.compare(b)
  if result == Ordering.lessThan 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```
