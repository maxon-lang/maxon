---
id: type-cast
status: implemented
---

# Type Cast (`as` Operator)

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
