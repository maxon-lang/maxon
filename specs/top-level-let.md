---
feature: top-level-let
status: stable
keywords: let, global, constant, compile-time
category: language
---

## Developer Notes

Top-level `let` declarations define compile-time constants at module scope. Only constant expressions are allowed as initializers. Forward references between globals are supported via dependency-ordered evaluation.

## Implementation Details

### Parser Changes
- Added `GlobalLetDeclAST` node to `ast.h` with fields: name, type, initializer, isExported
- Extended `parseProgram()` in `parser.cpp` to handle `let` and `export let` keywords at top level
- Added `parseTopLevelLet()` function in `parser_stmt.cpp`
- Updated `ProgramAST` to include `globals` vector

### Semantic Analysis
- Added `GlobalConstInfo` struct and `globalConstants` map to `SemanticAnalyzer`
- Pass 2c registers global names for forward reference support
- Pass 2d analyzes initializers and infers types
- `lookupVariable()` checks `globalConstants` after local scopes

### Constant Expression Evaluation
- `const_eval.h/cpp` implements `ConstExprEvaluator` class
- Supports: literals (int, float, bool, string), arithmetic ops, comparisons, logical ops, casts, global references
- Builds dependency graph from initializer expressions
- Topologically sorts globals using Kahn's algorithm
- Reports circular dependency errors with involved global names

### Code Generation
- Pass 1d in `codegen_mir.cpp` evaluates all global initializers
- Creates `MIRGlobal` with `isConstant=true` for each global
- Integer globals: 4-byte data in `.rdata` section
- Float globals: 8-byte data in `.rdata` section
- Bool globals: 1-byte data in `.rdata` section
- String globals: Use existing string literal mechanism
- Variable expressions check `module->getGlobal()` for global references

### Supported Constant Operations
- Literals: `int`, `float`, `bool`, `string`, `byte`, `char`
- Binary: `+`, `-`, `*`, `/`, `mod`, `==`, `!=`, `<`, `>`, `<=`, `>=`, `and`, `or`, `&`, `|`, `^`, `<<`, `>>`
- Unary: `-`, `+`, `not`, `~`
- Cast: `as int`, `as float`, `as bool`
- References to other global constants

---

# Documentation

## Top-Level Let Declarations

Top-level `let` declarations define compile-time constants at module scope. These constants are evaluated at compile time and stored in the executable's read-only data section.

### Syntax

```maxon
let CONSTANT_NAME = expression
export let EXPORTED_NAME = expression
```

### Features

- **Compile-time evaluation**: Initializers must be constant expressions
- **Type inference**: Type is inferred from the initializer
- **Forward references**: Constants can reference other constants declared later in the file
- **Export support**: Use `export let` to make constants available to other modules

### Constant Expressions

The following are valid in constant expressions:
- Literals: integers, floats, booleans, strings, bytes, characters
- Arithmetic: `+`, `-`, `*`, `/`, `mod`
- Comparison: `==`, `!=`, `<`, `>`, `<=`, `>=`
- Logical: `and`, `or`, `not`
- Bitwise: `&`, `|`, `^`, `~`, `<<`, `>>`
- Type casts: `as int`, `as float`, `as bool`
- References to other top-level constants

### Examples

```maxon
let PI = 3.14159265358979
let TAU = PI * 2.0
let MAX_SIZE = 1024
let DEBUG = false
let GREETING = "Hello, World!"
```

### Restrictions

- Function calls are not allowed in constant expressions
- Array and struct literals are not supported (yet)
- Only immutable `let` is supported at top level (no `var`)

---

## Tests

<!-- test: basic-integer-constant -->
```maxon
let ANSWER = 42

function main() int
    return ANSWER
end 'main'
```
```exitcode
42
```

<!-- test: basic-float-constant -->
```maxon
let PI = 3.14
let PI_INT = 3

function main() int
    return PI_INT
end 'main'
```
```exitcode
3
```

<!-- test: arithmetic-in-constant -->
```maxon
let BASE = 10
let DOUBLED = BASE * 2

function main() int
    return DOUBLED
end 'main'
```
```exitcode
20
```

<!-- test: forward-reference -->
```maxon
let TOTAL = FIRST + SECOND
let FIRST = 30
let SECOND = 12

function main() int
    return TOTAL
end 'main'
```
```exitcode
42
```

<!-- test: boolean-constant -->
```maxon
let DEBUG = true

function main() int
    if DEBUG 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: constant-in-expression -->
```maxon
let OFFSET = 10

function main() int
    let x = 5
    return x + OFFSET
end 'main'
```
```exitcode
15
```

<!-- test: multiple-constants -->
```maxon
let A = 1
let B = 2
let C = 3

function main() int
    return A + B + C
end 'main'
```
```exitcode
6
```

<!-- test: unary-minus-in-constant -->
```maxon
let NEGATIVE = -42

function main() int
    return 0 - NEGATIVE
end 'main'
```
```exitcode
42
```

<!-- test: comparison-in-constant -->
```maxon
let IS_LARGE = 100 > 50

function main() int
    if IS_LARGE 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: logical-operations -->
```maxon
let BOTH = true and true
let EITHER = false or true
let NEITHER = not false

function main() int
    if BOTH and EITHER and NEITHER 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: cast-in-constant -->
```maxon
let OFFSET = 10
let SCALED = OFFSET * 3

function main() int
    return SCALED
end 'main'
```
```exitcode
30
```

<!-- test: complex-constant-chain -->
```maxon
let A = 2
let B = A * 3
let C = B + 4
let D = C * 2

function main() int
    return D
end 'main'
```
```exitcode
20
```

<!-- test: circular-dependency-error -->
```maxon
let A = B + 1
let B = A + 1

function main() int
    return 0
end 'main'
```
```maxoncstderr
Semantic Error: line 2, column 1
Circular dependency detected among global constants: A, B

  2 | let A = B + 1
    | ^
```
