---
feature: bitwise-operators
status: implemented
keywords: [bitwise, and, or, xor, shift, operators]
category: operators
---

# Bitwise Operators

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
    return a & b
end 'main'
```
```exitcode
8
```

<!-- test: bitwise-or -->
```maxon
function main() returns int
    var a = 12
    var b = 10
    return a | b
end 'main'
```
```exitcode
14
```

<!-- test: bitwise-xor -->
```maxon
function main() returns int
    var a = 12
    var b = 10
    return a ^ b
end 'main'
```
```exitcode
6
```

<!-- test: left-shift -->
```maxon
function main() returns int
    var a = 1
    return a << 3
end 'main'
```
```exitcode
8
```

<!-- test: right-shift -->
```maxon
function main() returns int
    var a = 16
    return a >> 2
end 'main'
```
```exitcode
4
```

<!-- test: shift-chained -->
```maxon
function main() returns int
    var a = 1
    return a << 4 >> 2
end 'main'
```
```exitcode
4
```

<!-- test: bitwise-and-or-precedence -->
```maxon
function main() returns int
    // & has higher precedence than |
    // 12 & 10 = 8, then 8 | 1 = 9
    return 12 & 10 | 1
end 'main'
```
```exitcode
9
```

<!-- test: bitwise-xor-precedence -->
```maxon
function main() returns int
    // & has higher precedence than ^
    // 12 & 10 = 8, then 8 ^ 3 = 11
    return 12 & 10 ^ 3
end 'main'
```
```exitcode
11
```

<!-- test: shift-vs-comparison -->
```maxon
function main() returns int
    // Shift has higher precedence than comparison
    // 1 << 3 = 8, then 8 > 5 = true
    if 1 << 3 > 5 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: bitwise-with-logical -->
```maxon
function main() returns int
    // Logical operators have lower precedence than bitwise
    var a = 5 & 3        // 1
    if a > 0 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: bit-masking -->
```maxon
function main() returns int
    var flags = 5    // binary 101 (bit 0 and bit 2 set)
    return flags & 4 // returns 4 (bit 2 is set)
end 'main'
```
```exitcode
4
```

<!-- test: bit-clear -->
```maxon
function main() returns int
    var flags = 7        // binary 111
    // Clear bit 1 using XOR
    flags = flags ^ 2
    return flags         // 5 (binary 101)
end 'main'
```
```exitcode
5
```

<!-- test: power-of-two -->
```maxon
function main() returns int
    // Calculate 2^n using shift
    var n = 5
    return 1 << n        // 32
end 'main'
```
```exitcode
32
```

<!-- test: divide-by-power-of-two -->
```maxon
function main() returns int
    // Divide by 4 using shift
    var value = 100
    return value >> 2    // 25
end 'main'
```
```exitcode
25
```

<!-- test: multiply-by-power-of-two -->
```maxon
function main() returns int
    // Multiply by 8 using shift
    var value = 25
    return value << 3    // 200
end 'main'
```
```exitcode
200
```
