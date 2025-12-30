---
id: type-cast
status: implemented
---

# Type Cast (`as` Operator)

## Developer Notes

The `as` operator provides explicit type conversion between primitive types. Implementation spans:

- **Lexer (1-lexer.zig)**: Added `as` keyword token at line 40, and keyword recognition at line 426
- **Parser (2-parser.zig)**: Handles `as` as a postfix operator in `parsePostfix()` (around line 1345), binding tighter than comparison. Also updated `parseCall()` to call `parsePostfix` after parsing function calls so `foo() as int` works.
- **AST (ast.zig)**: New `CastExpr` struct with `expr` and `target_type` fields (line 274), and `cast` variant in Expression union (line 301)
- **Mutation Analysis (3-mutation_analysis.zig)**: Added case for `.cast` to check inner expression
- **AST-to-IR (4-ast_to_ir.zig)**: New `convertCast` function handles conversions using existing instructions
- **IR (ir.zig)**: Added `band` (bitwise AND) instruction for byte truncation
- **Codegen (ir_codegen.zig)**: Added `band` to integer binary ops
- **x86.zig**: Added `andRaxRcx` instruction encoding

Supported conversions:
- `int` to `float`: `sitofp` (signed int to floating point)
- `float` to `int`: `fptosi` (floating point to signed int, truncates toward zero)
- `int` to `byte`: bitwise AND with 0xFF (keeps lower 8 bits)
- `float` to `byte`: `fptosi` then AND with 0xFF
- `byte` to `int`: no-op (byte already stored as i64)
- Same type: no-op

## Documentation

The `as` operator converts a value from one type to another.

### Syntax

```maxon
expression as Type
```

### Supported Conversions

| From    | To      | Behavior                                    |
|---------|---------|---------------------------------------------|
| `int`   | `float` | Convert integer to floating point           |
| `float` | `int`   | Truncate toward zero                        |
| `int`   | `byte`  | Keep lower 8 bits (0-255)                   |
| `float` | `byte`  | Truncate to int, then keep lower 8 bits     |
| `byte`  | `int`   | Zero-extend to 64-bit integer               |

### Examples

```maxon
// Basic type conversions
var x = 42 as float       // 42.0
var y = 3.7 as int        // 3 (truncates toward zero)
var z = -2.9 as int       // -2 (truncates toward zero)

// Byte conversions
var b = 300 as byte       // 44 (300 mod 256)
var n = b as int          // 44

// Chained with expressions
var result = (a + b) as byte

// Cast on function call results
var val = getValue() as byte
```

## Tests

### Test: int-to-float-cast
<!-- expect: 42 -->
```maxon
function main()
    var x = 42 as float
    print(x as int)
end 'main'
```

### Test: float-to-int-cast
<!-- expect: 3 -->
```maxon
function main()
    var x = 3.7 as int
    print(x)
end 'main'
```

### Test: negative-float-to-int
<!-- expect: -2 -->
```maxon
function main()
    var x = -2.9 as int
    print(x)
end 'main'
```

### Test: int-to-byte-cast
<!-- expect: 44 -->
```maxon
function main()
    var x = 300 as byte
    print(x)
end 'main'
```

### Test: byte-to-int-cast
<!-- expect: 255 -->
```maxon
function main()
    var b = 255 as byte
    var n = b as int
    print(n)
end 'main'
```

### Test: expression-cast
<!-- expect: 15 -->
```maxon
function main()
    var a = 10
    var b = 5
    var result = (a + b) as byte
    print(result)
end 'main'
```

### Test: chained-cast
<!-- expect: 7 -->
```maxon
function main()
    var x = 7.9 as int as float as int
    print(x)
end 'main'
```

### Test: cast-in-arithmetic
<!-- expect: 52 -->
```maxon
function main()
    var x = 10.5
    var y = x as int + 42
    print(y)
end 'main'
```
