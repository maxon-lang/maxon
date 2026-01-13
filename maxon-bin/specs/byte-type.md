---
feature: byte-type
status: stable
keywords: [byte, unsigned, 8-bit, cast]
category: types
---

# Byte Type

## Documentation

The `byte` type represents a single 8-bit unsigned value (0-255).

### Creating Bytes

Use the `as byte` cast to create byte values from integers:

```maxon
var b = 255 as byte        // 8-bit unsigned value
var zero = 0 as byte       // Zero byte
var hex = 0xff as byte     // From hex literal
```

### Character Literals

Single-quoted character literals produce `character` type values (Extended Grapheme Clusters):

```maxon
var c = 'A'                // Creates a character value
```

Note: The `character` type is NOT the same as `byte`. See the character-type spec for details.

## Tests

### Byte From Cast

<!-- test: byte-cast -->
```maxon
function main() returns int
    var b = 42 as byte
    return b as int
end 'main'
```
```exitcode
42
```

### Byte Max Value

<!-- test: byte-max -->
```maxon
function main() returns int
    var b = 255 as byte
    return b as int
end 'main'
```
```exitcode
255
```

### Byte Zero

<!-- test: byte-zero -->
```maxon
function main() returns int
    var b = 0 as byte
    return b as int
end 'main'
```
```exitcode
0
```

### Byte From Hex

<!-- test: byte-from-hex -->
```maxon
function main() returns int
    var b = 0xff as byte
    return b as int
end 'main'
```
```exitcode
255
```

### Byte Array Push and Get

<!-- test: byte-array-push-get -->
```maxon
function main() returns int
    var arr = Array of byte{}
    arr.push(10 as byte)
    arr.push(20 as byte)
    arr.push(30 as byte)

    var v0 = try arr.get(0) otherwise 0 as byte
    var v1 = try arr.get(1) otherwise 0 as byte
    var v2 = try arr.get(2) otherwise 0 as byte

    return (v0 as int) + (v1 as int) + (v2 as int)
end 'main'
```
```exitcode
60
```

### Byte Array Initialized

<!-- test: byte-array-initialized -->
```maxon
function main() returns int
    var arr = Array of byte{}
    arr.push(1 as byte)
    arr.push(2 as byte)
    arr.push(3 as byte)

    var v0 = try arr.get(0) otherwise 0 as byte
    var v1 = try arr.get(1) otherwise 0 as byte
    var v2 = try arr.get(2) otherwise 0 as byte

    return (v0 as int) + (v1 as int) + (v2 as int)
end 'main'
```
```exitcode
6
```

### Byte Array Set

<!-- test: byte-array-set -->
```maxon
function main() returns int
    var arr = Array of byte{}
    arr.push(10 as byte)
    arr.push(20 as byte)
    arr.push(30 as byte)

    arr.set(1, value: 99 as byte)

    var val = try arr.get(1) otherwise 0 as byte
    return val as int
end 'main'
```
```exitcode
99
```

### Byte Array Max Values

<!-- test: byte-array-max-values -->
```maxon
function main() returns int
    var arr = Array of byte{}
    arr.push(255 as byte)
    arr.push(0 as byte)
    arr.push(128 as byte)

    var v0 = try arr.get(0) otherwise 0 as byte
    var v1 = try arr.get(1) otherwise 99 as byte
    var v2 = try arr.get(2) otherwise 0 as byte

    if (v0 as int) != 255 'c0'
        return 1
    end 'c0'
    if (v1 as int) != 0 'c1'
        return 2
    end 'c1'
    if (v2 as int) != 128 'c2'
        return 3
    end 'c2'

    return 0
end 'main'
```
```exitcode
0
```

### Byte Array Count

<!-- test: byte-array-count -->
```maxon
function main() returns int
    var arr = Array of byte{}
    arr.push(1 as byte)
    arr.push(2 as byte)
    arr.push(3 as byte)
    arr.push(4 as byte)
    arr.push(5 as byte)

    return arr.count()
end 'main'
```
```exitcode
5
```
