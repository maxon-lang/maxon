---
feature: operator-errors
status: stable
keywords: [error, operator, invalid, parse-error]
category: diagnostics
---

# Operator Errors

## Developer Notes

The lexer and parser detect invalid operators and report clear error messages.

Implementation:
- Lexer detects unexpected characters (e.g., `^`, `&` not followed by identifier)
- Reports character position and context
- Common unsupported operators: `^` (XOR), `&` (bitwise AND), `|` (bitwise OR), `<<`, `>>`
- Prevents confusing compilation errors downstream

Future: May add bitwise operators if needed for systems programming.

## Documentation

Using unsupported operators causes a compilation error.

### Unsupported Operators

Maxon currently does not support:
- Bitwise operators: `&`, `|`, `^`, `~`, `<<`, `>>`
- Logical operators: `&&`, `||`, `!` (planned)
- Compound assignment: `+=`, `-=`, `*=`, etc. (planned)

### Error Example

```maxon
function main() int
    return 3 ^ 4  // Error: '^' is not a valid operator
end 'main'
```
```output
MaxoncStderr: Unexpected character '^' at line 2, column 14
```

Error message:
```
Unexpected character '^'
```

### Workarounds

Use function calls for complex operations:
```maxon
// Instead of: result = a ^ b
function xor(a int, b int) int
    // Implement XOR using other operators
    return (a | b) & ~(a & b)  // When bitwise ops are added
end 'xor'
```

## Tests

<!-- test: xor-operator -->
```maxon
function main() int
    return 3 ^ 4
end 'main'
```
```
MaxoncStderr: Unexpected character '^'
```

<!-- test: bitwise-and -->
```maxon
function main() int
    return 5 & 3
end 'main'
```
```
MaxoncStderr: Unexpected character '&'
```

<!-- test: left-shift -->
```maxon
function main() int
    return 1 << 3
end 'main'
```
```
MaxoncStderr: Unexpected character '<'
```
