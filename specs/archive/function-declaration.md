---
feature: function-declaration
status: stable
keywords: [function, return, parameters]
category: functions
---

# Function Declaration

## Documentation

Functions in Maxon are declared using the `function` keyword. Each function has:
- A name (identifier)
- Zero or more parameters in parentheses
- An optional `returns` clause with a return type (omit for void functions)
- A body with statements
- An `end` keyword with the function name as a block identifier

### Syntax

```maxon
// Function with return value
function name(param1 type1, param2 type2) returns returnType
  // statements
  return value
end 'name'

// Function with no return value (implicit void)
function name(param1 type1, param2 type2)
  // statements
end 'name'

// Function with discard parameter (unused parameter)
function name(_ type1, param2 type2) returns returnType
  // param2 is usable, _ is discarded
  return value
end 'name'
```

### Discard Parameters

Use `_` as a parameter name to indicate an unused parameter. This is useful when:
- Implementing an interface method where you don't need all parameters
- Callback functions where some arguments are unused
- Future-proofing function signatures for API compatibility

Multiple discard parameters can be declared using names that start with `_`:

```maxon
function callback(_a int, _b String, value float) returns float
  return value * 2.0
end 'callback'
```

Discard parameters:
- Names starting with `_` indicate unused parameters
- Cannot be referenced in the function body (compile error)
- Do not generate "unused parameter" warnings
- Each discard parameter must have a unique name (e.g., `_a`, `_b`)
### Example

```maxon
function add(a int, b int) returns int
  return a + b
end 'add'

function main() returns int
  return add(3, b: 4)
end 'main'
```
```exitcode
7
```


## Tests

<!-- test: simple-function -->
```maxon
function add() returns int
  return 3 + 4
end 'add'

function main() returns int
  return add()
end 'main'
```
```exitcode
7
```


<!-- test: with-parameters -->
```maxon
function add(a int, b int) returns int
  return a + b
end 'add'

function main() returns int
  return add(10, b: 20)
end 'main'
```
```exitcode
30
```


<!-- test: nested-calls -->
```maxon
function double(x int) returns int
  return x * 2
end 'double'

function quadruple(x int) returns int
  return double(double(x))
end 'quadruple'

function main() returns int
  return quadruple(3)
end 'main'
```
```exitcode
12
```


<!-- test: void-return-type -->
```maxon
function doNothing()
  var x = 1
end 'doNothing'

function main() returns int
  doNothing()
  return 0
end 'main'
```
```exitcode
0
```


<!-- test: missing-returns-keyword-error -->
```maxon
function foo() int
  return 0
end 'foo'

function main() returns int
  return 0
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/function-declaration/missing-returns-keyword-error.test:2:16: Expected expression, got Int
```


<!-- test: discard-single-parameter -->
```maxon
function useSecond(_ int, b int) returns int
  return b
end 'useSecond'

function main() returns int
  return useSecond(10, b: 42)
end 'main'
```
```exitcode
42
```

