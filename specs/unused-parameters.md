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
function add(a Integer, b Integer) returns Integer
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
function identity(a Integer) returns Integer
  return a  // OK: 'a' is used
end 'identity'
```
## Tests

<!-- test: single-unused -->
```maxon
function add(a Integer, b Integer) returns Integer
  return a
end 'add'

function main() returns ExitCode
  return add(5, b: 10)
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-parameters/single-unused.test:2:25: unused variable: 'b'
```

<!-- test: multiple-unused -->
```maxon
function test(a Integer, b Integer, c Integer) returns Integer
  return a
end 'test'

function main() returns ExitCode
  return test(1, b: 2, c: 3)
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-parameters/multiple-unused.test:2:26: unused variable: 'b'
```

<!-- test: all-used-ok -->
```maxon
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
function doNothing(x Integer, y Integer)
  var z = 42
end 'doNothing'

function main() returns ExitCode
  doNothing(1, 2)
  return 0
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-parameters/void-function-unused.test:2:20: unused variable: 'x'
```
