---
feature: string-type
status: experimental
keywords: [string, sso, utf8, cow]
category: types
---

# String Type

## Developer Notes

Swift-style string implementation with Small String Optimization (SSO) and Copy-on-Write (COW) semantics.

### Memory Layout

**String Type (16 bytes):**

Small String (MSB of byte 15 = 0):
- bytes 0-14: UTF-8 data (inline)
- byte 15: remaining capacity (15 - length)

Large String (MSB of byte 15 = 1):
- bytes 0-7: pointer to heap buffer
- bytes 8-11: count (length in bytes)
- bytes 12-15: capacity | 0x80000000

### Slicing

String slicing operations return an immutable `string` that references the original string's storage (view semantics).
This avoids copying data while simplifying the type system (no separate substring type).
The returned string is immutable to prevent modification of the shared storage.

### Implementation

- Lexer: `string` keyword in `lexer.cpp`
- AST: `StringLiteralExprAST` with `targetType` field
- Codegen: `generateStringLiteral()`, `generateSmallStringLiteral()`, `generateLargeStringLiteral()`
- Runtime: `__string_count()`, `__string_is_empty()`, `__string_data()`, etc.

### SSO Threshold

Strings with length ≤15 bytes are stored inline (no heap allocation).
Strings with length >15 bytes use heap allocation with COW semantics.

## Documentation

The `string` type provides an efficient, UTF-8 encoded string with automatic memory management.

### Small String Optimization (SSO)

Short strings (up to 15 bytes) are stored directly in the string value itself, requiring no heap allocation.

```maxon
var short = "hello"        // Stored inline (5 bytes)
var longer = "this is a longer string"  // Heap allocated
```

### String Operations

```maxon
var s = "hello"
var len = s.count()        // Returns 5
var empty = s.is_empty()   // Returns false
```

## Tests

<!-- test: basic-declaration -->
```maxon
function main() int
    var s = "hello"
    if s == "hello" 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```

<!-- test: empty-string -->
```maxon
function main() int
    var s = ""
    if s == "" 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```


<!-- test: long-string -->

```maxon
function main() int
    var s = "this string is longer than fifteen bytes"
    if s == "this string is longer than fifteen bytes" 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```

<!-- test: inequality -->
```maxon
function main() int
    var s = "hello"
    if s != "world" 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```

<!-- test: print-string -->
```maxon
function main() int
    var s = "hello"
    print(s)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: print-literal -->
```maxon
function main() int
    print("Hello, World!")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
Hello, World!
```

<!-- test: concatenation -->
```maxon
function main() int
    var a = "hello"
    var b = " world"
    var c = a + b
    print(c)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
hello world
```

<!-- test: chained-concat -->
```maxon
function main() int
    var greeting = "Hello"
    var name = "Maxon"
    var full = greeting + ", " + name + "!"
    print(full)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
Hello, Maxon!
```
