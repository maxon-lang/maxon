---
feature: string-type
status: experimental
keywords: [string, sso, utf8, cow]
category: types
---

# String Type

## Developer Notes

String implementation with Small String Optimization (SSO) and Copy-on-Write (COW) semantics.

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
var len = s.count()           // Returns 5 (grapheme count)
var bytes = s.bytes().count() // Returns 5 (byte count)
var empty = s.isEmpty()       // Returns false
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

Iterate over grapheme clusters (user-perceived characters) in a string:

```maxon
var s = "café"
for c in s 'loop'
    print(c.toString())  // Prints c, a, f, é (4 chars, not 5 bytes)
end 'loop'
```

Each iteration yields a `character` value representing an Extended Grapheme Cluster (EGC).

### String Views

Strings provide multiple views for different iteration granularities:

**Default iteration** - Grapheme clusters (character):
```maxon
for c in "café" 'chars'
    // c is a character (grapheme cluster)
    print(c.toString())
end 'chars'
```

**Codepoint view** - Unicode codepoints (int):
```maxon
for cp in s.codepoints() 'codepoints'
    printInt(cp)  // Unicode codepoint values
end 'codepoints'
```

**Byte view** - Raw UTF-8 bytes:
```maxon
for b in s.bytes() 'bytes'
    printInt(b)  // Raw byte values
end 'bytes'
```

**UTF-16 view** - UTF-16 code units (useful for Windows API or JavaScript interop):
```maxon
for u in s.utf16() 'utf16'
    printInt(u)  // UTF-16 code units
end 'utf16'
```

Characters outside the Basic Multilingual Plane (codepoints > U+FFFF like emoji) produce surrogate pairs in UTF-16:
```maxon
var emoji = "😀"
for u in emoji.utf16() 'loop'
    printInt(u)  // 55357, 56832 (surrogate pair for U+1F600)
end 'loop'
```

### UTF-16 Utility Functions

The `stdlib/string/utf16.maxon` module provides utility functions for working with UTF-16 encoding:

```maxon
// Check surrogate types
utf16IsLeadSurrogate(55357)   // true (0xD83D)
utf16IsTrailSurrogate(56832)  // true (0xDE00)
utf16IsSurrogate(55357)        // true

// Get encoding width (1 or 2 code units)
utf16Width(65)      // 1 (ASCII 'A')
utf16Width(128512)  // 2 (😀 U+1F600 needs surrogate pair)

// Encode codepoint to surrogates
utf16LeadSurrogate(128512)   // 55357 (0xD83D)
utf16TrailSurrogate(128512)  // 56832 (0xDE00)

// Decode surrogate pair to codepoint
utf16DecodeSurrogates(55357, 56832)  // 128512 (U+1F600)
```

## Tests

<!-- test: basic-declaration -->
```maxon
function main() returns int
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
function main() returns int
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
function main() returns int
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
function main() returns int
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

<!-- test: equality-with-logical-and -->
```maxon
function main() returns int
    var s = "hello"
    var c = 'A'
    if s == "hello" and c == 'A' 'check'
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
function main() returns int
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
function main() returns int
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
function main() returns int
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
function main() returns int
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
function main() returns int
    var s = "hello"
    printInt(s.count())
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
function main() returns int
    var empty = ""
    var nonempty = "hello"
    if empty.isEmpty() 'check1'
        printInt(1)
    end 'check1'
    if nonempty.isEmpty() 'check2'
        printInt(0)
    end 'check2' else 'not_empty'
        printInt(2)
    end 'not_empty'
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
function main() returns int
    var s = "hello world"
    if s.startsWith("hello") 'c1'
        printInt(1)
    end 'c1'
    if s.startsWith("world") 'c2'
        printInt(0)
    end 'c2'
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
function main() returns int
    var s = "hello world"
    if s.endsWith("world") 'c1'
        printInt(1)
    end 'c1'
    if s.endsWith("hello") 'c2'
        printInt(0)
    end 'c2'
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
function main() returns int
    var s = "hello world"
    if s.contains("lo wo") 'c1'
        printInt(1)
    end 'c1'
    if s.contains("xyz") 'c2'
        printInt(0)
    end 'c2'
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
function main() returns int
    var s = "hello world"
    printInt(s.find("world"))
    printInt(s.find("xyz"))
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
function main() returns int
    var s = "abc"
    for c in s 'loop'
        print(c.toString())
    end 'loop'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
a
b
c
```

<!-- test: byteview-iteration -->
```maxon
function main() returns int
    var s = "abc"
    for b in s.bytes() 'loop'
        printInt(b as int)
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
function main() returns int
    var s = "ABC"
    for u in s.utf16() 'loop'
        printInt(u)
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
function main() returns int
    var s = "αβγ"
    for u in s.utf16() 'loop'
        printInt(u)
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
function main() returns int
    var s = "😀"
    for u in s.utf16() 'loop'
        printInt(u)
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
function main() returns int
    var s = "A😀B"
    for u in s.utf16() 'loop'
        printInt(u)
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
function main() returns int
    var s = "A😀B"
    var view = s.utf16()
    printInt(view.count())
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
function main() returns int
    // 0xD83D = 55357 (high surrogate for 😀)
    if utf16IsLeadSurrogate(55357) 'c1'
        printInt(1)
    end 'c1'
    if utf16IsLeadSurrogate(56832) 'c2'
        printInt(0)
    end 'c2'
    if utf16IsLeadSurrogate(65) 'c3'
        printInt(0)
    end 'c3'
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
function main() returns int
    // 0xDE00 = 56832 (low surrogate for 😀)
    if utf16IsTrailSurrogate(56832) 'c4'
        printInt(1)
    end 'c4'
    if utf16IsTrailSurrogate(55357) 'c5'
        printInt(0)
    end 'c5'
    if utf16IsTrailSurrogate(65) 'c6'
        printInt(0)
    end 'c6'
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
function main() returns int
    if utf16IsSurrogate(55357) 'c7'
        printInt(1)
    end 'c7'
    if utf16IsSurrogate(56832) 'c8'
        printInt(2)
    end 'c8'
    if utf16IsSurrogate(65) 'c9'
        printInt(0)
    end 'c9'
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
function main() returns int
    printInt(utf16Width(65))      // ASCII 'A' = 1 code unit
    printInt(utf16Width(945))     // Greek α = 1 code unit (BMP)
    printInt(utf16Width(128512))  // 😀 U+1F600 = 2 code units
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
function main() returns int
    // 😀 U+1F600 = 128512
    printInt(utf16LeadSurrogate(128512))   // 55357 (0xD83D)
    printInt(utf16TrailSurrogate(128512))  // 56832 (0xDE00)
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
function main() returns int
    // Decode surrogate pair back to codepoint
    var cp = utf16DecodeSurrogates(55357, 56832)
    printInt(cp)  // 128512 (U+1F600)
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
function main() returns int
    if utf16IsBmp(65) 'c10'
        printInt(1)       // ASCII
    end 'c10'
    if utf16IsBmp(945) 'c11'
        printInt(2)      // Greek α
    end 'c11'
    if utf16IsBmp(65535) 'c12'
        printInt(3)    // U+FFFF (last BMP)
    end 'c12'
    if utf16IsBmp(128512) 'c13'
        printInt(0)   // 😀 (not BMP)
    end 'c13'
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
function main() returns int
    if utf16IsValidSurrogatePair(55357, 56832) 'c14'
        printInt(1)  // valid pair
    end 'c14'
    if utf16IsValidSurrogatePair(56832, 55357) 'c15'
        printInt(0)  // reversed
    end 'c15'
    if utf16IsValidSurrogatePair(65, 66) 'c16'
        printInt(0)        // not surrogates
    end 'c16'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: heap-string-count -->
```maxon
function main() returns int
    // String > 15 bytes triggers heap allocation
    var s = "This is a longer string that exceeds 15 bytes"
    printInt(s.count())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
45
```

<!-- test: heap-string-data-access -->
```maxon
function main() returns int
    // Verify heap-allocated string data is accessible via bytes()
    var s = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
    // Read first byte ('A' = 65)
    var first_printed = false
    for b in s.bytes() 'read_first'
        if not first_printed 'print_first'
            printInt(b as int)
            first_printed = true
        end 'print_first'
    end 'read_first'
    // Read last byte ('Z' = 90)
    var last_byte = 0 as byte
    for b in s.bytes() 'read_all'
        last_byte = b
    end 'read_all'
    printInt(last_byte as int)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
65
90
```

<!-- test: heap-string-equality -->
```maxon
function main() returns int
    var a = "This string is definitely longer than fifteen bytes"
    var b = "This string is definitely longer than fifteen bytes"
    if a == b 'check'
        printInt(1)
    end 'check' else 'not_equal'
        printInt(0)
    end 'not_equal'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: heap-string-inequality -->
```maxon
function main() returns int
    var a = "This string is definitely longer than fifteen bytes"
    var b = "This string is definitely longer than fifteen chars"
    if a != b 'check'
        printInt(1)
    end 'check' else 'are_equal'
        printInt(0)
    end 'are_equal'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: heap-string-iteration -->
```maxon
function main() returns int
    var s = "ABCDEFGHIJKLMNOP"  // 16 bytes, triggers heap
    var sum = 0
    for c in s 'loop'
        var cps = c.codepoints()
        if let cp = cps.next() 'get_cp'
            sum = sum + cp
        end 'get_cp'
    end 'loop'
    printInt(sum)  // 65+66+...+80 = 1160
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1160
```

<!-- test: heap-string-byteview -->
```maxon
function main() returns int
    var s = "ABCDEFGHIJKLMNOPQR"  // 18 bytes, heap allocated
    var count = 0
    for b in s.bytes() 'loop'
        // Use b to avoid unused variable warning
        if b > 0 'use'
            count = count + 1
        end 'use'
    end 'loop'
    printInt(count)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
18
```

<!-- test: sso-vs-heap-boundary -->
```maxon
function main() returns int
    // Exactly 15 bytes - should use SSO (constant data)
    var sso = "123456789012345"
    printInt(sso.count())

    // 16 bytes - should use heap
    var heap = "1234567890123456"
    printInt(heap.count())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
15
16
```

<!-- test: heap-string-startsWith -->
```maxon
function main() returns int
    var s = "This is a very long string that is heap allocated"
    if s.startsWith("This is") 'c17'
        printInt(1)
    end 'c17'
    if s.startsWith("That is") 'c18'
        printInt(0)
    end 'c18'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: heap-string-endsWith -->
```maxon
function main() returns int
    var s = "This is a very long string that is heap allocated"
    if s.endsWith("heap allocated") 'c19'
        printInt(1)
    end 'c19'
    if s.endsWith("stack allocated") 'c20'
        printInt(0)
    end 'c20'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: heap-string-contains -->
```maxon
function main() returns int
    var s = "This is a very long string that is heap allocated"
    if s.contains("long string") 'c21'
        printInt(1)
    end 'c21'
    if s.contains("short string") 'c22'
        printInt(0)
    end 'c22'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: heap-string-find -->
```maxon
function main() returns int
    var s = "This is a very long string that is heap allocated"
    printInt(s.find("very"))
    printInt(s.find("missing"))
    return 0
end 'main'
```
```exitcode
0
```
```stdout
10
-1
```

<!-- test: mixed-sso-heap-comparison -->
```maxon
function main() returns int
    var small = "hello"
    var large = "This is a longer string"
    if small != large 'check'
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

<!-- test: memory-tracking-simple-concat -->
<!-- TrackAllocs: true -->
```maxon
function main() returns int
    var s = "hello" + " world"
    printInt(s.count())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 20 bytes (string concat)
11ALLOC #2: 10 bytes (cstring conversion)
FREE #2: 10 bytes (cstring release)

FREE #1: 20 bytes (string concat)

=== ALLOC STATS ===
Allocated: 30 bytes
Freed:     30 bytes
Leaked:    0 bytes
```

<!-- test: memory-tracking-chained-concat -->
<!-- TrackAllocs: true -->
```maxon
function main() returns int
    var s = "a" + "b" + "c" + "d"
    printInt(s.count())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 11 bytes (string concat)
ALLOC #2: 12 bytes (string concat)
ALLOC #3: 13 bytes (string concat)
4ALLOC #4: 10 bytes (cstring conversion)
FREE #4: 10 bytes (cstring release)

FREE #1: 11 bytes (string concat)
FREE #2: 12 bytes (string concat)
FREE #3: 13 bytes (string concat)

=== ALLOC STATS ===
Allocated: 46 bytes
Freed:     46 bytes
Leaked:    0 bytes
```

<!-- test: memory-tracking-loop-concat -->
<!-- TrackAllocs: true -->
```maxon
function main() returns int
    var s = ""
    var i = 0
    while i < 3 'loop'
        s = s + "x"
        i = i + 1
    end 'loop'
    printInt(s.count())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 10 bytes (string concat)
ALLOC #2: 11 bytes (string concat)
FREE #1: 10 bytes (string reassign)
ALLOC #3: 12 bytes (string concat)
FREE #2: 11 bytes (string reassign)
3ALLOC #4: 10 bytes (cstring conversion)
FREE #4: 10 bytes (cstring release)

FREE #3: 12 bytes (string concat)

=== ALLOC STATS ===
Allocated: 43 bytes
Freed:     43 bytes
Leaked:    0 bytes
```

<!-- test: memory-tracking-no-leak-scope-exit -->
<!-- TrackAllocs: true -->
```maxon
function main() returns int
    if true 'scope'
        var temp = "heap allocated string here!"
        printInt(temp.count())
    end 'scope'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 36 bytes (string literal)
27ALLOC #2: 10 bytes (cstring conversion)
FREE #2: 10 bytes (cstring release)

FREE #1: 36 bytes (string literal)

=== ALLOC STATS ===
Allocated: 46 bytes
Freed:     46 bytes
Leaked:    0 bytes
```

<!-- test: toLower -->
```maxon
function main() returns int
    var s = "HELLO"
    print(s.toLower())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: bytes-count-method -->
### bytes().count() Method
```maxon
function main() returns int
    var s = "hello"
    printInt(s.bytes().count())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: bytes-count-multibyte -->
### bytes().count() with Multi-byte Characters
```maxon
function main() returns int
    var s = "café"
    printInt(s.bytes().count())  // 5 bytes (c=1, a=1, f=1, é=2)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: count-graphemes -->
### count Returns Grapheme Count
```maxon
function main() returns int
    var s = "café"
    printInt(s.count())  // 4 graphemes
    return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: count-vs-bytes-count -->
### count vs bytes().count()
```maxon
function main() returns int
    var s = "🇺🇸"  // Flag emoji (1 grapheme, 8 bytes)
    printInt(s.count())
    printInt(s.bytes().count())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
8
```

<!-- test: grapheme-iteration-emoji -->
### Grapheme Iteration with Emoji
```maxon
function main() returns int
    var s = "a🎉b"
    var count = 0
    for c in s 'loop'
        var _unused = c.bytes().count()  // Use c to avoid unused warning
        _unused = _unused
        count = count + 1
    end 'loop'
    printInt(count)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: grapheme-iteration-flag -->
### Grapheme Iteration with Flag Emoji
```maxon
function main() returns int
    var s = "🇺🇸🇬🇧"  // Two flag emojis
    var count = 0
    for c in s 'loop'
        var _unused = c.bytes().count()  // Use c to avoid unused warning
        _unused = _unused
        count = count + 1
    end 'loop'
    printInt(count)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: grapheme-iteration-zwj -->
### Grapheme Iteration with ZWJ Sequence
```maxon
function main() returns int
    var s = "👨‍👩‍👧"  // Family emoji (1 grapheme)
    var count = 0
    for c in s 'loop'
        var _unused = c.bytes().count()  // Use c to avoid unused warning
        _unused = _unused
        count = count + 1
    end 'loop'
    printInt(count)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: codepoints-view -->
### Codepoints View
```maxon
function main() returns int
    var s = "Aé"  // A (1 codepoint) + é (1 codepoint if precomposed)
    for cp in s.codepoints() 'loop'
        printInt(cp)
    end 'loop'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
65
233
```

<!-- test: string-reassignment -->
```maxon
function main() returns int
    var s = "hello"
    printInt(s.count())

    var u = "abc"
    u = "testing"
    printInt(u.count())

    var v = ""
    v = "world"
    printInt(v.count())

    return 0
end 'main'
```
```exitcode
0
```
```stdout
5
7
5
```
