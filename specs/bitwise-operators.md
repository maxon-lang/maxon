---
feature: bitwise-operators
status: implemented
keywords: [bitwise, and, or, xor, shift, not, shl, shr, operators]
category: operators
---

# Bitwise Operators

## Documentation

Maxon provides bitwise operators for manipulating individual bits of integer values. The `and`, `or`, `xor`, and `not` keywords are context-dependent: they perform bitwise operations on integers and logical operations on booleans.

### Bitwise AND (`and`)

Returns 1 for each bit position where both operands have 1:

```maxon
var a = 12       // 1100 in binary
var b = 10       // 1010 in binary
var c = a and b  // 1000 = 8
```

### Bitwise OR (`or`)

Returns 1 for each bit position where either operand has 1:

```maxon
var a = 12      // 1100 in binary
var b = 10      // 1010 in binary
var c = a or b  // 1110 = 14
```

### Bitwise XOR (`xor`)

Returns 1 for each bit position where operands differ:

```maxon
var a = 12       // 1100 in binary
var b = 10       // 1010 in binary
var c = a xor b  // 0110 = 6
```

### Bitwise NOT (`not`)

Flips all bits of an integer value:

```maxon
var a = 5        // ...0101 in binary
var b = not a    // ...1010 = -6
```

### Left Shift (`shl`)

Shifts bits left by the specified amount, filling with zeros:

```maxon
var a = 1
var b = a shl 3  // 1000 = 8
```

### Right Shift (`shr`)

Shifts bits right by the specified amount:

```maxon
var a = 16
var b = a shr 2  // 0100 = 4
```

## Tests

<!-- test: bitwise-and -->
```maxon
function main() returns Integer
  var a = 12
  var b = 10
  return a and b
end 'main'
```
```exitcode
8
```

<!-- test: bitwise-or -->
```maxon
function main() returns Integer
  var a = 12
  var b = 10
  return a or b
end 'main'
```
```exitcode
14
```

<!-- test: bitwise-xor -->
```maxon
function main() returns Integer
  var a = 12
  var b = 10
  return a xor b
end 'main'
```
```exitcode
6
```

<!-- test: left-shift -->
```maxon
function main() returns Integer
  var a = 1
  return a shl 3
end 'main'
```
```exitcode
8
```

<!-- test: right-shift -->
```maxon
function main() returns Integer
  var a = 16
  return a shr 2
end 'main'
```
```exitcode
4
```

<!-- test: shift-chained -->
```maxon
function main() returns Integer
  var a = 1
  return a shl 4 shr 2
end 'main'
```
```exitcode
4
```

<!-- test: bitwise-and-or-precedence -->
```maxon
function main() returns Integer
  // and has higher precedence than or
  // 12 and 10 = 8, then 8 or 1 = 9
  return 12 and 10 or 1
end 'main'
```
```exitcode
9
```

<!-- test: bitwise-xor-precedence -->
```maxon
function main() returns Integer
  // and has higher precedence than xor
  // 12 and 10 = 8, then 8 xor 3 = 11
  return 12 and 10 xor 3
end 'main'
```
```exitcode
11
```

<!-- test: shift-vs-comparison -->
```maxon
function main() returns Integer
  // Shift has higher precedence than comparison
  // 1 shl 3 = 8, then 8 > 5 = true
  if 1 shl 3 > 5 'check'
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
function main() returns Integer
  var a = 5 and 3        // 1
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
function main() returns Integer
  var flags = 5         // binary 101 (bit 0 and bit 2 set)
  return flags and 4    // returns 4 (bit 2 is set)
end 'main'
```
```exitcode
4
```

<!-- test: bit-clear -->
```maxon
function main() returns Integer
  var flags = 7        // binary 111
  // Clear bit 1 using xor
  flags = flags xor 2
  return flags         // 5 (binary 101)
end 'main'
```
```exitcode
5
```

<!-- test: power-of-two -->
```maxon
function main() returns Integer
  // Calculate 2^n using shift
  var n = 5
  return 1 shl n        // 32
end 'main'
```
```exitcode
32
```

<!-- test: divide-by-power-of-two -->
```maxon
function main() returns Integer
  // Divide by 4 using shift
  var value = 100
  return value shr 2    // 25
end 'main'
```
```exitcode
25
```

<!-- test: multiply-by-power-of-two -->
```maxon
function main() returns Integer
  // Multiply by 8 using shift
  var value = 25
  return value shl 3    // 200
end 'main'
```
```exitcode
200
```

<!-- test: bitwise-not-basic -->
```maxon
function main() returns Integer
  print("{not 0}\n")
  return 0
end 'main'
```
```stdout
-1
```
```exitcode
0
```

<!-- test: bitwise-not-value -->
```maxon
function main() returns Integer
  var a = 5
  print("{not a}\n")
  return 0
end 'main'
```
```stdout
-6
```
```exitcode
0
```

<!-- test: bitwise-not-double -->
```maxon
function main() returns Integer
  var a = 42
  return not not a
end 'main'
```
```exitcode
42
```

<!-- test: bitwise-not-masking -->
```maxon
function main() returns Integer
  var value = 255    // 0xFF
  // Clear lower 4 bits: 255 and not 15 = 240
  return value and not 15
end 'main'
```
```exitcode
240
```

<!-- test: bitwise-not-const -->
```maxon
let MASK = not 0xFF

function main() returns Integer
  print("{MASK}\n")
  return 0
end 'main'
```
```stdout
-256
```
```exitcode
0
```

<!-- test: shr-in-method-call-arg -->
```maxon
type ShrBuf
  var managed __ManagedMemory
  var len Integer

  static function create(capacity Integer) returns Self
    return {managed: __managed_memory_create(capacity, 1), len: 0}
  end 'create'

  export function push(value Integer)
    __managed_memory_set_byte(managed, len, value)
    len = len + 1
    __managed_memory_set_length(managed, len)
  end 'push'

  export function getByte(index Integer) returns Integer
    return __managed_memory_byte_at(managed, index)
  end 'getByte'
end 'ShrBuf'

function main() returns Integer
  var buf = ShrBuf.create(16)
  buf.push(42)
  let x = 0xABCD
  buf.push(x shr 8)
  return buf.getByte(1)
end 'main'
```
```exitcode
171
```

<!-- test: shr-consecutive-method-calls -->
```maxon
type ShrBuf2
  var managed __ManagedMemory
  var len Integer

  static function create(capacity Integer) returns Self
    return {managed: __managed_memory_create(capacity, 1), len: 0}
  end 'create'

  export function push(value Integer)
    __managed_memory_set_byte(managed, len, value)
    len = len + 1
    __managed_memory_set_length(managed, len)
  end 'push'

  export function getByte(index Integer) returns Integer
    return __managed_memory_byte_at(managed, index)
  end 'getByte'
end 'ShrBuf2'

function main() returns Integer
  var buf = ShrBuf2.create(16)
  let value = 0xAABBCCDD
  buf.push(value and 0xFF)
  buf.push((value shr 8) and 0xFF)
  buf.push((value shr 16) and 0xFF)
  buf.push((value shr 24) and 0xFF)
  let b0 = buf.getByte(0)
  let b1 = buf.getByte(1)
  let b2 = buf.getByte(2)
  let b3 = buf.getByte(3)
  if b0 != 0xDD 'c0'
    return 10
  end 'c0'
  if b1 != 0xCC 'c1'
    return 20
  end 'c1'
  if b2 != 0xBB 'c2'
    return 30
  end 'c2'
  if b3 != 0xAA 'c3'
    return 40
  end 'c3'
  return 0
end 'main'
```
```exitcode
0
```
