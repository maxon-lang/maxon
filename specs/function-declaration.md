---
feature: function-declaration
status: stable
keywords: [function, return, parameters]
category: functions
---

# Function Declaration

## Developer Notes

Functions are the basic unit of code organization in Maxon. They use the `function` keyword followed by a name, parameter list in parentheses, `returns` keyword with return type, and end with `end 'function-name'` where the identifier matches the function name.

Key implementation details:
- Functions are parsed in `Parser::parseFunctionDeclaration()`
- Function types are represented by `FunctionType` in the AST
- Parameters are parsed as comma-separated list of `name type` pairs
- The closing identifier must match the function name exactly
- Functions are generated as LLVM `Function` objects
- All functions must explicitly specify a return type using `returns` keyword (use `returns nothing` for functions that don't return a value)
- All non-nothing functions must explicitly return a value (no implicit returns)

The `main()` function is special - it's the entry point and must return `int`. The compiler generates a `_start()` wrapper that calls `main()` and passes its return value to `ExitProcess()`.

## Documentation

Functions in Maxon are declared using the `function` keyword. Each function has:
- A name (identifier)
- Zero or more parameters in parentheses
- The `returns` keyword followed by a return type
- A body with statements
- An `end` keyword with the function name as a block identifier

### Syntax

```maxon
function name(param1 type1, param2 type2) returns returnType
    // statements
    return value
end 'name'
```
### Example

```maxon
function add(a int, b int) returns int
    return a + b
end 'add'

function main() returns int
    return add(3, 4)
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
    return add(10, 20)
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


<!-- test: nothing-return-type -->
```maxon
function greet() returns nothing
    print("Hello")
end 'greet'

function main() returns int
    greet()
    return 0
end 'main'
```
```output
Hello
```


<!-- test: missing-returns-keyword-error -->
```maxon
returns int
    return 0
end 'foo'

function main() returns int
    return 0
end 'main'
```
```error
missing 'returns' keyword
```

