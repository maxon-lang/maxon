---
feature: unused-parameters
status: stable
keywords: [parameters, warnings, errors, unused]
category: diagnostics
---

# Unused Parameter Detection

## Developer Notes

The semantic analyzer detects when function parameters are declared but never used in the function body.

Implementation:
- Checked in `SemanticAnalyzer::checkUnusedParameters()`
- Iterates through all parameters of a function
- Checks if parameter name appears in any expression
- Reports error for unused parameters
- Compilation fails if unused parameters found
- Helps catch bugs and improve code quality

This is a semantic error, not a warning. Functions must use all their parameters or not declare them.

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
    return add(5, 10)
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:2:21
The parameter 'b' is declared but its value is never used

  2 | function add(a int, b int) returns int
    |                     ^
```

<!-- test: multiple-unused -->
```maxon
function test(a int, b int, c int) returns int
    return a
end 'test'

function main() returns int
    return test(1, 2, 3)
end 'main'
```
```maxoncstderr
Semantic Error: temp_fragment.maxon:2:22
The parameter 'b' is declared but its value is never used

  2 | function test(a int, b int, c int) returns int
    |                      ^

Semantic Error: temp_fragment.maxon:2:29
The parameter 'c' is declared but its value is never used

  2 | function test(a int, b int, c int) returns int
    |                             ^
```

<!-- test: all-used-ok -->
```maxon
function add(a int, b int) returns int
    return a + b
end 'add'

function main() returns int
    return add(5, 10)
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
    return multiply(7, 6)
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
Semantic Error: temp_fragment.maxon:2:20
The parameter 'x' is declared but its value is never used

  2 | function doNothing(x int, y int)
    |                    ^

Semantic Error: temp_fragment.maxon:2:27
The parameter 'y' is declared but its value is never used

  2 | function doNothing(x int, y int)
    |                           ^

Semantic Error: temp_fragment.maxon:3:5
The variable 'z' is assigned but its value is never used

  3 |     var z = 42
    |     ^
```
