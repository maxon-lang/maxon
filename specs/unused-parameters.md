---
feature: unused-parameters
status: stable
keywords: [parameters, warnings, errors, unused]
category: diagnostics
---

# Unused Parameter Detection

## Documentation

Maxon requires all function parameters to be used. Declaring unused parameters causes a compilation error.

### Example Error

```maxon
typealias Score = i64

function add(a Score, b Score) returns Score
  return a  // Error: 'b' is unused
end 'add'
```
Error message:
```
Semantic Error: The parameter 'b' is declared but its value is never used
```

### Solution

Only declare parameters you need:

```maxon
typealias Score = i64

function identity(a Score) returns Score
  return a  // OK: 'a' is used
end 'identity'
```
## Tests

<!-- test: single-unused -->
```maxon

typealias Integer = i64

function add(a Integer, b Integer) returns Integer
  return a
end 'add'

function main() returns ExitCode
  return add(5, b: 10)
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-parameters/single-unused.test:5:25: unused variable: 'b'
```

<!-- test: multiple-unused -->
```maxon

typealias Integer = i64

function test(a Integer, b Integer, c Integer) returns Integer
  return a
end 'test'

function main() returns ExitCode
  return test(1, b: 2, c: 3)
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-parameters/multiple-unused.test:5:26: unused variable: 'b'
```

<!-- test: all-used-ok -->
```maxon

typealias Integer = i64

function add(a Integer, b Integer) returns Integer
  return a + b
end 'add'

function main() returns ExitCode
  return add(5, b: 10)
end 'main'
```
```exitcode
15
```


<!-- test: none-unused -->
```maxon

typealias Integer = i64

function multiply(a Integer, b Integer) returns Integer
  return a * b
end 'multiply'

function main() returns ExitCode
  return multiply(7, b: 6)
end 'main'
```
```exitcode
42
```


<!-- test: void-function-unused -->
```maxon

typealias Integer = i64

function doNothing(x Integer, y Integer)
  var z = 42
end 'doNothing'

function main() returns ExitCode
  doNothing(1, 2)
  return 0
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-parameters/void-function-unused.test:5:20: unused variable: 'x'
```
