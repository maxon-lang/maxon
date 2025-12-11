---
feature: bitwise-operators
status: implemented
keywords: [bitwise, and, or, xor, shift, operators]
category: operators
---

# Bitwise Operators

## Developer Notes

Bitwise operators operate on integer values at the bit level.

### Implementation

- Lexer: `&`, `|`, `^`, `<<`, `>>` tokens in `lexer.cpp`
- Parser: `parseBitwiseAnd()`, `parseBitwiseXor()`, `parseBitwiseOr()`, `parseShift()` in `parser_expr.cpp`
- Codegen: `createAnd`, `createOr`, `createXor`, `createShl`, `createLShr` MIR instructions

### Operator Precedence (lowest to highest)

1. Logical OR (`or`)
2. Logical AND (`and`)
3. Bitwise OR (`|`)
4. Bitwise XOR (`^`)
5. Bitwise AND (`&`)
6. Equality (`==`, `!=`)
7. Comparison (`<`, `>`, `<=`, `>=`)
8. Shift (`<<`, `>>`)
9. Additive (`+`, `-`)
10. Multiplicative (`*`, `/`, `mod`)
11. Unary (`-`, `!`)

### Internal Representation

To distinguish bitwise from logical operators internally:
- Bitwise AND: `'&'`
- Bitwise OR: `'|'`
- Bitwise XOR: `'^'`
- Left shift: `'S'`
- Right shift: `'H'`
- Logical AND: `'A'`
- Logical OR: `'O'`

## Documentation

Maxon provides bitwise operators for manipulating individual bits of integer values.

### Bitwise AND (`&`)

Returns 1 for each bit position where both operands have 1:

```maxon
var a = 12      // 1100 in binary
var b = 10      // 1010 in binary
var c = a & b   // 1000 = 8
```

### Bitwise OR (`|`)

Returns 1 for each bit position where either operand has 1:

```maxon
var a = 12      // 1100 in binary
var b = 10      // 1010 in binary
var c = a | b   // 1110 = 14
```

### Bitwise XOR (`^`)

Returns 1 for each bit position where operands differ:

```maxon
var a = 12      // 1100 in binary
var b = 10      // 1010 in binary
var c = a ^ b   // 0110 = 6
```

### Left Shift (`<<`)

Shifts bits left by the specified amount, filling with zeros:

```maxon
var a = 1
var b = a << 3  // 1000 = 8
```

### Right Shift (`>>`)

Shifts bits right by the specified amount (logical shift, fills with zeros):

```maxon
var a = 16
var b = a >> 2  // 0100 = 4
```

## Tests

<!-- test: bitwise-and -->
```maxon
function main() returns int
    var a = 12
    var b = 10
    var c = a & b
    printInt(c)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
8
```

<!-- test: bitwise-or -->
```maxon
function main() returns int
    var a = 12
    var b = 10
    var c = a | b
    printInt(c)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
14
```

<!-- test: bitwise-xor -->
```maxon
function main() returns int
    var a = 12
    var b = 10
    var c = a ^ b
    printInt(c)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
6
```

<!-- test: left-shift -->
```maxon
function main() returns int
    var a = 1
    var b = a << 3
    printInt(b)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
8
```

<!-- test: right-shift -->
```maxon
function main() returns int
    var a = 16
    var b = a >> 2
    printInt(b)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: shift-chained -->
```maxon
function main() returns int
    var a = 1
    var b = a << 4 >> 2
    printInt(b)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: bitwise-and-or-precedence -->
```maxon
function main() returns int
    // & has higher precedence than |
    // 12 & 10 = 8, then 8 | 1 = 9
    var result = 12 & 10 | 1
    printInt(result)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
9
```

<!-- test: bitwise-xor-precedence -->
```maxon
function main() returns int
    // & has higher precedence than ^
    // 12 & 10 = 8, then 8 ^ 3 = 11
    var result = 12 & 10 ^ 3
    printInt(result)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
11
```

<!-- test: shift-vs-comparison -->
```maxon
function main() returns int
    // Shift has higher precedence than comparison
    // 1 << 3 = 8, then 8 > 5 = true
    if 1 << 3 > 5 'check'
        printInt(1)
    end 'check' else 'else_check'
        printInt(0)
    end 'else_check'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: bitwise-with-logical -->
```maxon
function main() returns int
    // Logical operators have lower precedence than bitwise
    var a = 5 & 3        // 1
    var b = 5 | 2        // 7
    if a > 0 and b > 0 'check'
        printInt(1)
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: bit-masking -->
```maxon
function main() returns int
    var flags = 0
    flags = flags | 1    // Set bit 0
    flags = flags | 4    // Set bit 2
    printInt(flags)         // 5 (binary 101)
    
    // Check if bit 2 is set
    if (flags & 4) > 0 'check'
        printInt(1)
    end 'check'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
5
1
```

<!-- test: bit-clear -->
```maxon
function main() returns int
    var flags = 7        // binary 111
    // Clear bit 1 using XOR
    flags = flags ^ 2
    printInt(flags)         // 5 (binary 101)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: power-of-two -->
```maxon
function main() returns int
    // Calculate 2^n using shift
    var n = 5
    var result = 1 << n
    printInt(result)        // 32
    return 0
end 'main'
```
```exitcode
0
```
```stdout
32
```

<!-- test: divide-by-power-of-two -->
```maxon
function main() returns int
    // Divide by 4 using shift
    var value = 100
    var result = value >> 2
    printInt(result)        // 25
    return 0
end 'main'
```
```exitcode
0
```
```stdout
25
```

<!-- test: multiply-by-power-of-two -->
```maxon
function main() returns int
    // Multiply by 8 using shift
    var value = 25
    var result = value << 3
    printInt(result)        // 200
    return 0
end 'main'
```
```exitcode
0
```
```stdout
200
```
