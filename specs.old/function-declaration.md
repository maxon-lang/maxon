---
feature: function-declaration
status: stable
keywords: [function, return, parameters]
category: functions
---

# Function Declaration

## Developer Notes

Functions are the basic unit of code organization in Maxon. They use the `function` keyword followed by a name, parameter list in parentheses, optional `returns` keyword with return type, and end with `end 'function-name'` where the identifier matches the function name.

Key implementation details:
- Functions are parsed in `Parser::parseFunctionDeclaration()`
- Function types are represented by `FunctionType` in the AST
- Parameters are parsed as comma-separated list of `name type` pairs
- The closing identifier must match the function name exactly
- Functions are generated as LLVM `Function` objects
- Return type is optional - functions without `returns` clause implicitly return void
- All non-void functions must explicitly return a value (no implicit returns)

The `main()` function is special - it's the entry point and must return `int`. The compiler generates a `_start()` wrapper that calls `main()` and passes its return value to `ExitProcess()`.

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

Multiple `_` parameters are allowed in the same function:

```maxon
function callback(_ int, _ String, value float) returns float
    return value * 2.0
end 'callback'
```

Discard parameters:
- Cannot be referenced in the function body (compile error)
- Do not generate "unused parameter" warnings
- Multiple `_` parameters are allowed
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


<!-- test: void-return-type -->
```maxon
function greet()
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
function foo() int
    return 0
end 'foo'

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
In file 'temp_fragment.maxon':
Unexpected token: 'int'
  Note: Expected a statement (var, let, if, while, return, break, continue, match, or assignment)
  Location: line 2, column 16
```


<!-- test: discard-single-parameter -->
```maxon
function useSecond(_ int, b int) returns int
    return b
end 'useSecond'

function main() returns int
    return useSecond(10, 42)
end 'main'
```
```exitcode
42
```


<!-- test: discard-multiple-parameters -->
```maxon
function useThird(_ int, _ String, c int) returns int
    return c
end 'useThird'

function main() returns int
    return useThird(1, "ignored", 99)
end 'main'
```
```exitcode
99
```


<!-- test: discard-all-parameters -->
```maxon
function ignoreAll(_ int, _ float) returns int
    return 7
end 'ignoreAll'

function main() returns int
    return ignoreAll(100, 3.14)
end 'main'
```
```exitcode
7
```

