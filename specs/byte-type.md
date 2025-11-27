---
feature: byte-type
status: stable
keywords: [byte, literal, unsigned, 8-bit]
category: types
---

# Byte Type

## Developer Notes

The `byte` type represents an 8-bit unsigned integer value (0-255).

### Implementation

- Lexer: `BYTE_LITERAL` token type for `42b` syntax in `lexer.cpp`
- AST: `ByteExprAST` node in `ast.h`
- Codegen: Generates `i8` constants

### Byte Literal Syntax

Integer literals with a `b` suffix are parsed as byte literals:
- `42b` - byte with value 42
- `0b` - zero byte
- `255b` - maximum byte value

Range checking is performed at compile time - values outside 0-255 produce an error.

## Documentation

The `byte` type represents a single 8-bit unsigned value (0-255).

### Byte Literals

Use the `b` suffix on integer literals to create byte values:

```maxon
var b = 255b               // 8-bit unsigned value
var zero = 0b              // Zero byte
```

The `b` suffix is range-checked at compile time - values outside 0-255 produce an error.

### Character Type

The `char` type is an alias for `byte`, representing a single ASCII character or UTF-8 code unit:

```maxon
var c = 'A'                // Single character (char literal)
```

## Tests

### Byte Literal

```maxon
function main() int
    var b = 42b
    return b as int
end 'main'
```
```output
ExitCode: 42
```

### Byte Max Value

```maxon
function main() int
    var b = 255b
    return b as int
end 'main'
```
```output
ExitCode: 255
```

### Byte Zero

```maxon
function main() int
    var b = 0b
    return b as int
end 'main'
```
```output
ExitCode: 0
```
