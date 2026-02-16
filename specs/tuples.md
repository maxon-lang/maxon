---
feature: tuples
status: stable
keywords: [tuple, pair, destructuring, positional]
category: types
---

# Tuples

## Documentation

### Overview

Tuples are fixed-size, ordered collections of values with potentially different types. They use parenthesized syntax for both type annotations and literals.

```text
var point = (10, 20)
var pair = (42, "hello")
```

### Element Access

Access tuple elements using positional dot syntax `.0`, `.1`, `.2`, etc.:

```text
var t = (10, 20)
t.0   // 10
t.1   // 20
```

### Destructuring

Tuples can be destructured into individual variables:

```text
var (x, y) = (10, 20)
// x is 10, y is 20
```

Tuple destructuring also works in `for` loops when the iterator returns a tuple:

```text
var m = ["a": 1, "b": 2]
for (key, value) in m 'loop'
  print("{key}: {value}\n")
end 'loop'
```

### As Function Parameters and Return Types

Tuples can be used as function parameters and return types:

```text
function swap(t (Integer, Integer)) returns (Integer, Integer)
  return (t.1, t.0)
end 'swap'
```

## Tests

<!-- test: basic-tuple -->
```maxon
function main() returns ExitCode
  var t = (10, 32)
  return t.0 + t.1
end 'main'
```
```exitcode
42
```

<!-- test: mixed-type-tuple -->
```maxon
function main() returns ExitCode
  var t = (40, 2.5)
  return t.0 + trunc(t.1)
end 'main'
```
```exitcode
42
```

<!-- test: tuple-as-param -->
```maxon

typealias Integer = int(i64.min to i64.max)

function sum(t (Integer, Integer)) returns Integer
  return t.0 + t.1
end 'sum'

function main() returns ExitCode
  var t = (10, 32)
  return sum(t)
end 'main'
```
```exitcode
42
```

<!-- test: tuple-as-return -->
```maxon

typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
  return (a, b)
end 'makePair'

function main() returns ExitCode
  var t = makePair(10, b: 32)
  return t.0 + t.1
end 'main'
```
```exitcode
42
```

<!-- test: tuple-destructuring -->
```maxon

typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
  return (a, b)
end 'makePair'

function main() returns ExitCode
  var (x, y) = makePair(10, b: 32)
  return x + y
end 'main'
```
```exitcode
42
```

<!-- test: three-element-tuple -->
```maxon
function main() returns ExitCode
  var t = (1, 2, 39)
  return t.0 + t.1 + t.2
end 'main'
```
```exitcode
42
```

<!-- test: tuple-field-write -->
```maxon
function main() returns ExitCode
  var t = (0, 0)
  t.0 = 20
  t.1 = 22
  return t.0 + t.1
end 'main'
```
```exitcode
42
```

<!-- test: tuple-with-string -->
```maxon
function main() returns ExitCode
  var t = (42, "hello")
  return t.0
end 'main'
```
```exitcode
42
```

<!-- test: let-destructuring -->
```maxon
function main() returns ExitCode
  let (x, y) = (10, 32)
  return x + y
end 'main'
```
```exitcode
42
```

<!-- test: for-destructuring-map -->
```maxon
function main() returns ExitCode
  var m = ["a": 10, "b": 32]
  var sum = 0
  for (key, value) in m 'loop'
    sum = sum + value
  end 'loop'
  return sum
end 'main'
```
```exitcode
42
```
