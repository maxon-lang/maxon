---
feature: top-level-let
status: stable
keywords: let, global, constant, compile-time
category: language
---
# Top Level Let

## Documentation

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
- Array literals: `[1, 2, 3]` (elements must be constant expressions)
- Enum member access: `Color.Red`
- Map literals: `["key": value]` (keys and values must be constant expressions; initialized at runtime)

### Examples

```maxon
let PI = 3.14159265358979
let TAU = PI * 2.0
let MAX_SIZE = 1024
let DEBUG = false
let GREETING = "Hello, World!"
let PRIMES = [2, 3, 5, 7, 11]
```

### Restrictions

- Function calls are not allowed in constant expressions
- Map literals are supported, but require runtime initialization
- Only immutable `let` is supported at top level (no `var`)

---

## Tests

<!-- test: basic-integer-constant -->
```maxon
let ANSWER = 42

function main() returns int
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

function main() returns int
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

function main() returns int
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

function main() returns int
    return TOTAL
end 'main'
```
```exitcode
42
```

<!-- test: boolean-constant -->
```maxon
let DEBUG = true

function main() returns int
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

function main() returns int
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

function main() returns int
    return A + B + C
end 'main'
```
```exitcode
6
```

<!-- test: unary-minus-in-constant -->
```maxon
let NEGATIVE = -42

function main() returns int
    return 0 - NEGATIVE
end 'main'
```
```exitcode
42
```

<!-- test: comparison-in-constant -->
```maxon
let IS_LARGE = 100 > 50

function main() returns int
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

function main() returns int
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

function main() returns int
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

function main() returns int
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

function main() returns int
    return 0
end 'main'
```
```maxoncstderr
error E005: specs/fragments/top-level-let.circular-dependency-error.1.test:3:1: Circular dependency detected among global constants: A, B
```

<!-- test: array-literal-constant -->
```maxon
let numbers = [1, 2, 3, 4, 5]

function main() returns int
    var sum = 0
    for n in numbers 'loop'
        sum = sum + n
    end 'loop'
    return sum
end 'main'
```
```exitcode
15
```

<!-- test: array-literal-with-dependency -->
```maxon
let FIRST = 10
let SECOND = 20
let values = [FIRST, SECOND, 30]

function main() returns int
    return (values[0] or 0) + (values[1] or 0) + (values[2] or 0)
end 'main'
```
```exitcode
60
```

<!-- test: map-literal-with-enum-values -->
```maxon
enum TokenKind
    Function
    Var
end 'TokenKind'

let KEYWORDS = ["function": TokenKind.Function, "var": TokenKind.Var]

function main() returns int
    if let kind = KEYWORDS.get("function") 'valid'
        match kind 'match'
            TokenKind.Function then return 1
            TokenKind.Var then return 2
        end 'match'
    end 'valid'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-member-constant -->
```maxon
enum Color
    Red
    Green
    Blue
end 'Color'

let DEFAULT_COLOR = Color.Green

function main() returns int
    match DEFAULT_COLOR 'check'
        Color.Red then return 1
        Color.Green then return 2
        Color.Blue then return 3
    end 'check'
    return 0
end 'main'
```
```exitcode
2
```
