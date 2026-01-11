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
function add(a int, b int) returns int
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
function identity(a int) returns int
    return a  // OK: 'a' is used
end 'identity'
```
## Tests

<!-- test: single-unused -->
```maxon
function add(a int, b int) returns int
    return a
end 'add'

function main() returns int
    return add(5, b: 10)
end 'main'
```
```maxoncstderr
error E014: specs/fragments/unused-parameters.single-unused.1.test:3:5: unused variable: 'b'
```

<!-- test: multiple-unused -->
```maxon
function test(a int, b int, c int) returns int
    return a
end 'test'

function main() returns int
    return test(1, b: 2, c: 3)
end 'main'
```
```maxoncstderr
error E014: specs/fragments/unused-parameters.multiple-unused.1.test:3:5: unused variable: 'b'
```

<!-- test: all-used-ok -->
```maxon
function add(a int, b int) returns int
    return a + b
end 'add'

function main() returns int
    return add(5, b: 10)
end 'main'
```
```exitcode
15
```


<!-- test: none-unused -->
```maxon
function multiply(a int, b int) returns int
    return a * b
end 'multiply'

function main() returns int
    return multiply(7, b: 6)
end 'main'
```
```exitcode
42
```


<!-- test: void-function-unused -->
```maxon
function doNothing(x int, y int)
    var z = 42
end 'doNothing'

function main() returns int
    doNothing(1, 2)
    return 0
end 'main'
```
```maxoncstderr
error E014: specs/fragments/unused-parameters.void-function-unused.1.test:3:5: unused variable: 'x'
```
