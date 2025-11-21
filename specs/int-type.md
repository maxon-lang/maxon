---
feature: int-type
status: stable
keywords: [int, integer, i32]
category: types
---

# Int Type

## Developer Notes

The `int` type is the primary integer type in Maxon, represented as a signed 32-bit integer (LLVM `i32`).

Key implementation details:
- Mapped to LLVM `i32` type
- Default numeric type for integer literals
- Supports arithmetic operators: `+`, `-`, `*`, `/`, `%`
- Supports comparison operators: `=`, `!=`, `<`, `>`, `<=`, `>=`
- Can be cast to `float`, `bool`, `char`, and pointer types
- Integer literals are parsed in `Lexer::readNumber()`
- Overflow behavior follows LLVM semantics (wraps around)

The `main()` function must return `int`, and the value becomes the program's exit code (0-255 on Windows).

## Documentation

The `int` type stores 32-bit signed integers ranging from -2,147,483,648 to 2,147,483,647.

### Syntax

```maxon
var count int = 42
let total = 100
```

### Example

```maxon
function factorial(n int) int
    if n <= 1 'base'
        return 1
    end 'base'
    return n * factorial(n - 1)
end 'factorial'

function main() int
    return factorial(5)  // Returns 120
end 'main'
```
```output
ExitCode: 120
```

## Tests

<!-- test: basic-int -->
```maxon
function main() int
    var x = 42
    return x
end 'main'
```
```
ExitCode: 42
```

<!-- test: int-arithmetic -->
```maxon
function main() int
    var a = 10
    var b = 20
    var sum = a + b
    return sum
end 'main'
```
```
ExitCode: 30
```

<!-- test: negative-int -->
```maxon
function main() int
    var x = -42
    var y = -x
    if y = 42 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```
ExitCode: 1
```

<!-- test: int-expression -->
```maxon
function main() int
    return 2 + 3 * 4
end 'main'
```
```
ExitCode: 14
```
