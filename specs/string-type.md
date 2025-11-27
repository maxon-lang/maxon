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

### String Properties

```maxon
var s = "hello"
var len = s.count          // Returns 5 (codepoint count)
var empty = s.isEmpty      // Returns false
```

### String Slicing

Extract substrings using the slice syntax `s[start..end]`:

```maxon
var s = "hello world"
var sub1 = s[0..5]         // "hello" (start to end, exclusive)
var sub2 = s[6..]          // "world" (start to end of string)
var sub3 = s[..5]          // "hello" (beginning to end, exclusive)
```

### Search Methods

```maxon
var s = "hello world"
s.starts_with("hello")     // true
s.ends_with("world")       // true
s.contains("lo wo")        // true
s.find("world")            // 6 (index, or -1 if not found)
```

### Transform Methods

```maxon
var s = "Hello World"
s.to_upper()               // "HELLO WORLD"
s.to_lower()               // "hello world"

var padded = "  hello  "
padded.trim()              // "hello"
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

<!-- test: count-property -->
```maxon
function main() int
    var s = "hello"
    print(s.count)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
11
```

<!-- test: isEmpty-property -->
```maxon
function main() int
    var empty = ""
    var nonempty = "hello"
    if empty.isEmpty 'check1'
        print(1)
    end 'check1'
    if nonempty.isEmpty 'check2'
        print(0)
    else 'check2'
        print(2)
    end 'check2'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
2
```

<!-- test: slice-start-end -->
```maxon
function main() int
    var s = "hello world"
    var sub = s[0..5]
    print(sub)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: slice-from -->
```maxon
function main() int
    var s = "hello world"
    var sub = s[6..]
    print(sub)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
world
```

<!-- test: slice-to -->
```maxon
function main() int
    var s = "hello world"
    var sub = s[..5]
    print(sub)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: starts-with -->
```maxon
function main() int
    var s = "hello world"
    if s.starts_with("hello") then print(1)
    if s.starts_with("world") then print(0)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: ends-with -->
```maxon
function main() int
    var s = "hello world"
    if s.ends_with("world") then print(1)
    if s.ends_with("hello") then print(0)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: contains -->
```maxon
function main() int
    var s = "hello world"
    if s.contains("lo wo") then print(1)
    if s.contains("xyz") then print(0)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: find -->
```maxon
function main() int
    var s = "hello world"
    print(s.find("world"))
    print(s.find("xyz"))
    return 0
end 'main'
```
```exitcode
0
```
```stdout
6
-1
```

<!-- test: to-upper -->
```maxon
function main() int
    var s = "Hello World"
    print(s.to_upper())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
HELLO WORLD
```

<!-- test: to-lower -->
```maxon
function main() int
    var s = "Hello World"
    print(s.to_lower())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
hello world
```

<!-- test: trim -->
```maxon
function main() int
    var s = "  hello  "
    print(s.trim())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```
