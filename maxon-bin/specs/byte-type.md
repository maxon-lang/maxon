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
