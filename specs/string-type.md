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
- stdlib: `stdlib/string/string.maxon` - pure Maxon implementation

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

### String Methods

```maxon
var s = "hello"
var len = s.count()        // Returns 5 (byte count)
var empty = s.isEmpty()    // Returns false
```

### Search Methods

```maxon
var s = "hello world"
s.startsWith("hello")      // true
s.endsWith("world")        // true
s.contains("lo wo")        // true
s.find("world")            // 6 (index, or -1 if not found)
```

### String Concatenation

```maxon
var greeting = "Hello"
var name = "World"
var message = greeting + ", " + name + "!"  // "Hello, World!"
```

### Character Iteration

Iterate over Unicode codepoints in a string:

```maxon
var s = "abc"
for c in s 'loop'
    print(c)  // Prints 97, 98, 99 (ASCII values)
end 'loop'
```

### String Views

Strings provide multiple views for different iteration granularities:

**Default iteration** - Unicode codepoints (same as above):
```maxon
for c in "αβγ" 'chars'
    print(c)  // 945, 946, 947 (Greek letter codepoints)
end 'chars'
```

**Byte view** - Raw UTF-8 bytes:
```maxon
for b in s.bytes() 'bytes'
    print(b)  // Raw byte values
end 'bytes'
```

**UTF-16 view** - UTF-16 code units (useful for Windows API or JavaScript interop):
```maxon
for u in s.utf16() 'utf16'
    print(u)  // UTF-16 code units
end 'utf16'
```

Characters outside the Basic Multilingual Plane (codepoints > U+FFFF like emoji) produce surrogate pairs in UTF-16:
```maxon
var emoji = "😀"
for u in emoji.utf16() 'loop'
    print(u)  // 55357, 56832 (surrogate pair for U+1F600)
end 'loop'
```

### UTF-16 Utility Functions

The `stdlib/string/utf16.maxon` module provides utility functions for working with UTF-16 encoding:

```maxon
// Check surrogate types
utf16_is_lead_surrogate(55357)   // true (0xD83D)
utf16_is_trail_surrogate(56832)  // true (0xDE00)
utf16_is_surrogate(55357)        // true

// Get encoding width (1 or 2 code units)
utf16_width(65)      // 1 (ASCII 'A')
utf16_width(128512)  // 2 (😀 U+1F600 needs surrogate pair)

// Encode codepoint to surrogates
utf16_lead_surrogate(128512)   // 55357 (0xD83D)
utf16_trail_surrogate(128512)  // 56832 (0xDE00)

// Decode surrogate pair to codepoint
utf16_decode_surrogates(55357, 56832)  // 128512 (U+1F600)
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

<!-- test: count-method -->
```maxon
function main() int
    var s = "hello"
    print(s.count())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: isEmpty-method -->
```maxon
function main() int
    var empty = ""
    var nonempty = "hello"
    if empty.isEmpty() 'check1'
        print(1)
    end 'check1'
    if nonempty.isEmpty() 'check2'
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

<!-- test: startsWith -->
```maxon
function main() int
    var s = "hello world"
    if s.startsWith("hello") then print(1)
    if s.startsWith("world") then print(0)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: endsWith -->
```maxon
function main() int
    var s = "hello world"
    if s.endsWith("world") then print(1)
    if s.endsWith("hello") then print(0)
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

<!-- test: for-in-string -->
```maxon
function main() int
    var s = "abc"
    for c in s 'loop'
        print(c)
    end 'loop'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
97
98
99
```

<!-- test: byteview-iteration -->
```maxon
function main() int
    var s = "abc"
    for b in s.bytes() 'loop'
        print(b)
    end 'loop'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
97
98
99
```

<!-- test: utf16-ascii -->
```maxon
function main() int
    var s = "ABC"
    for u in s.utf16() 'loop'
        print(u)
    end 'loop'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
65
66
67
```

<!-- test: utf16-bmp -->
```maxon
function main() int
    var s = "αβγ"
    for u in s.utf16() 'loop'
        print(u)
    end 'loop'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
945
946
947
```

<!-- test: utf16-surrogate-pair -->
```maxon
function main() int
    var s = "😀"
    for u in s.utf16() 'loop'
        print(u)
    end 'loop'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
55357
56832
```

<!-- test: utf16-mixed -->
```maxon
function main() int
    var s = "A😀B"
    for u in s.utf16() 'loop'
        print(u)
    end 'loop'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
65
55357
56832
66
```

<!-- test: utf16-length -->
```maxon
function main() int
    var s = "A😀B"
    var view = s.utf16()
    print(UTF16View.length(view))
    return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: utf16-is-lead-surrogate -->
```maxon
function main() int
    // 0xD83D = 55357 (high surrogate for 😀)
    if utf16_is_lead_surrogate(55357) then print(1)
    if utf16_is_lead_surrogate(56832) then print(0)
    if utf16_is_lead_surrogate(65) then print(0)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: utf16-is-trail-surrogate -->
```maxon
function main() int
    // 0xDE00 = 56832 (low surrogate for 😀)
    if utf16_is_trail_surrogate(56832) then print(1)
    if utf16_is_trail_surrogate(55357) then print(0)
    if utf16_is_trail_surrogate(65) then print(0)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: utf16-is-surrogate -->
```maxon
function main() int
    if utf16_is_surrogate(55357) then print(1)
    if utf16_is_surrogate(56832) then print(2)
    if utf16_is_surrogate(65) then print(0)
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

<!-- test: utf16-width -->
```maxon
function main() int
    print(utf16_width(65))      // ASCII 'A' = 1 code unit
    print(utf16_width(945))     // Greek α = 1 code unit (BMP)
    print(utf16_width(128512))  // 😀 U+1F600 = 2 code units
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
1
2
```

<!-- test: utf16-encode-surrogates -->
```maxon
function main() int
    // 😀 U+1F600 = 128512
    print(utf16_lead_surrogate(128512))   // 55357 (0xD83D)
    print(utf16_trail_surrogate(128512))  // 56832 (0xDE00)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
55357
56832
```

<!-- test: utf16-decode-surrogates -->
```maxon
function main() int
    // Decode surrogate pair back to codepoint
    var cp = utf16_decode_surrogates(55357, 56832)
    print(cp)  // 128512 (U+1F600)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
128512
```

<!-- test: utf16-is-bmp -->
```maxon
function main() int
    if utf16_is_bmp(65) then print(1)       // ASCII
    if utf16_is_bmp(945) then print(2)      // Greek α
    if utf16_is_bmp(65535) then print(3)    // U+FFFF (last BMP)
    if utf16_is_bmp(128512) then print(0)   // 😀 (not BMP)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
2
3
```

<!-- test: utf16-valid-surrogate-pair -->
```maxon
function main() int
    if utf16_is_valid_surrogate_pair(55357, 56832) then print(1)  // valid pair
    if utf16_is_valid_surrogate_pair(56832, 55357) then print(0)  // reversed
    if utf16_is_valid_surrogate_pair(65, 66) then print(0)        // not surrogates
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```
