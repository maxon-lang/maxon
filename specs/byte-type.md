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
typealias Pixel = byte(0 to u8.max)

var b = 255 as Pixel        // 8-bit unsigned value
var zero = 0 as Pixel       // Zero byte
var hex = 0xff as Pixel     // From hex literal
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

typealias Integer = int(i64.min to i64.max)
typealias Byte = byte(0 to u8.max)

function main() returns ExitCode
  var b = 42 as Byte
  return b as Integer
end 'main'
```
```exitcode
42
```

### Byte Max Value

<!-- test: byte-max -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Byte = byte(0 to u8.max)

function main() returns ExitCode
  var b = 255 as Byte
  return b as Integer
end 'main'
```
```exitcode
255
```

### Byte Zero

<!-- test: byte-zero -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Byte = byte(0 to u8.max)

function main() returns ExitCode
  var b = 0 as Byte
  return b as Integer
end 'main'
```
```exitcode
0
```

### Byte From Hex

<!-- test: byte-from-hex -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Byte = byte(0 to u8.max)

function main() returns ExitCode
  var b = 0xff as Byte
  return b as Integer
end 'main'
```
```exitcode
255
```
