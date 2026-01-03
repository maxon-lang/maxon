---
feature: string-type
status: experimental
keywords: [string, sso, utf8, cow]
category: types
---

# String Type

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
    print("{c}\n")  // Prints c, a, f, é (4 chars, not 5 bytes)
end 'loop'
```

Each iteration yields a `character` value representing an Extended Grapheme Cluster (EGC).

### String Views

Strings provide multiple views for different iteration granularities:

**Default iteration** - Grapheme clusters (character):
```maxon
for c in "café" 'chars'
    // c is a character (grapheme cluster)
    print("{c}\n")
end 'chars'
```

**Codepoint view** - Unicode codepoints (int):
```maxon
for cp in s.codepoints() 'codepoints'
    print("{cp}\n")  // Unicode codepoint values
end 'codepoints'
```

**Byte view** - Raw UTF-8 bytes:
```maxon
for b in s.bytes() 'bytes'
    print("{b}\n")  // Raw byte values
end 'bytes'
```

**UTF-16 view** - UTF-16 code units (useful for Windows API or JavaScript interop):
```maxon
for u in s.utf16() 'utf16'
    print("{u}\n")  // UTF-16 code units
end 'utf16'
```

Characters outside the Basic Multilingual Plane (codepoints > U+FFFF like emoji) produce surrogate pairs in UTF-16:
```maxon
var emoji = "😀"
for u in emoji.utf16() 'loop'
    print("{u}\n")  // 55357, 56832 (surrogate pair for U+1F600)
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

### String Slicing

Create a substring view that shares storage with the original string:

```maxon
var s = "hello world"
var start = s.startIndex()
if let spaceIdx = s.find(" ") 'found'
    var sub = s.slice(start, end_ = spaceIdx)  // "hello" - shares storage with s
    print(sub)
end 'found'
```

Slices are immutable views into the parent string. They do not copy data, making them efficient for parsing and substring operations.

### Copy-on-Write Behavior

Strings use copy-on-write (COW) semantics for efficiency:

```maxon
var original = "hello"
var copy = original        // Both share the same storage
copy = copy.toLower()      // copy gets its own storage, original unchanged
print(original)            // "hello" - not modified
print(copy)                // "hello" - new copy
```

When you assign a string to another variable, they share storage. If either is mutated, a copy is made automatically, ensuring the other remains unchanged.

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
    var c = "{a}{b}"
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
    var full = "{greeting}, {name}!"
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
    print("{s.count()}\n")
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
        print("{1}\n")
    end 'check1'
    if nonempty.isEmpty() 'check2'
        print("{0}\n")
    end 'check2' else 'not_empty'
        print("{2}\n")
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
        print("{1}\n")
    end 'c1'
    if s.startsWith("world") 'c2'
        print("{0}\n")
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
        print("{1}\n")
    end 'c1'
    if s.endsWith("hello") 'c2'
        print("{0}\n")
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
        print("{1}\n")
    end 'c1'
    if s.contains("xyz") 'c2'
        print("{0}\n")
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
    if let idx = s.find("world") 'found'
        print("{idx.charIndex()}\n")
    end 'found' else 'not_found'
        print("{0 - 1}\n")
    end 'not_found'
    if let idx2 = s.find("xyz") 'found2'
        print("{idx2.charIndex()}\n")
    end 'found2' else 'not_found2'
        print("{0 - 1}\n")
    end 'not_found2'
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

<!-- test: replace-single -->
```maxon
function main() returns int
    var s = "hello world"
    var result = s.replace("world", "there")
    print(result)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
hello there
```

<!-- test: replace-multiple -->
```maxon
function main() returns int
    var s = "aaa bbb aaa"
    var result = s.replace("aaa", "x")
    print(result)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
x bbb x
```

<!-- test: replace-no-match -->
```maxon
function main() returns int
    var s = "hello world"
    var result = s.replace("xyz", "abc")
    print(result)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
hello world
```

<!-- test: replace-empty-needle -->
```maxon
function main() returns int
    var s = "hello"
    var result = s.replace("", "x")
    print(result)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: replace-with-empty -->
```maxon
function main() returns int
    var s = "hello world"
    var result = s.replace("o", "")
    print(result)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
hell wrld
```

<!-- test: replace-adjacent -->
```maxon
function main() returns int
    var s = "aaaa"
    var result = s.replace("aa", "b")
    print(result)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
bb
```

<!-- test: replaceFirst-single -->
```maxon
function main() returns int
    var s = "hello world"
    var result = s.replaceFirst("o", "0")
    print(result)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
hell0 world
```

<!-- test: replaceFirst-multiple-occurrences -->
```maxon
function main() returns int
    var s = "aaa bbb aaa"
    var result = s.replaceFirst("aaa", "x")
    print(result)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
x bbb aaa
```

<!-- test: replaceFirst-no-match -->
```maxon
function main() returns int
    var s = "hello world"
    var result = s.replaceFirst("xyz", "abc")
    print(result)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
hello world
```

<!-- test: for-in-string -->
```maxon
function main() returns int
    var s = "abc"
    for c in s 'loop'
        print("{c}\n")
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
        print("{b}\n")
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
        print("{u}\n")
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
        print("{u}\n")
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
        print("{u}\n")
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
        print("{u}\n")
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
    print("{view.count()}\n")
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
        print("{1}\n")
    end 'c1'
    if utf16IsLeadSurrogate(56832) 'c2'
        print("{0}\n")
    end 'c2'
    if utf16IsLeadSurrogate(65) 'c3'
        print("{0}\n")
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
        print("{1}\n")
    end 'c4'
    if utf16IsTrailSurrogate(55357) 'c5'
        print("{0}\n")
    end 'c5'
    if utf16IsTrailSurrogate(65) 'c6'
        print("{0}\n")
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
        print("{1}\n")
    end 'c7'
    if utf16IsSurrogate(56832) 'c8'
        print("{2}\n")
    end 'c8'
    if utf16IsSurrogate(65) 'c9'
        print("{0}\n")
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
    print("{utf16Width(65)}\n")      // ASCII 'A' = 1 code unit
    print("{utf16Width(945)}\n")     // Greek α = 1 code unit (BMP)
    print("{utf16Width(128512)}\n")  // 😀 U+1F600 = 2 code units
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
    print("{utf16LeadSurrogate(128512)}\n")   // 55357 (0xD83D)
    print("{utf16TrailSurrogate(128512)}\n")  // 56832 (0xDE00)
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
    print("{cp}\n")  // 128512 (U+1F600)
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
        print("{1}\n")       // ASCII
    end 'c10'
    if utf16IsBmp(945) 'c11'
        print("{2}\n")      // Greek α
    end 'c11'
    if utf16IsBmp(65535) 'c12'
        print("{3}\n")    // U+FFFF (last BMP)
    end 'c12'
    if utf16IsBmp(128512) 'c13'
        print("{0}\n")   // 😀 (not BMP)
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
        print("{1}\n")  // valid pair
    end 'c14'
    if utf16IsValidSurrogatePair(56832, 55357) 'c15'
        print("{0}\n")  // reversed
    end 'c15'
    if utf16IsValidSurrogatePair(65, 66) 'c16'
        print("{0}\n")        // not surrogates
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
    print("{s.count()}\n")
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
            print("{b}\n")
            first_printed = true
        end 'print_first'
    end 'read_first'
    // Read last byte ('Z' = 90)
    var last_byte = 0 as byte
    for b in s.bytes() 'read_all'
        last_byte = b
    end 'read_all'
    print("{last_byte}\n")
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
        print("{1}\n")
    end 'check' else 'not_equal'
        print("{0}\n")
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
        print("{1}\n")
    end 'check' else 'are_equal'
        print("{0}\n")
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
    print("{sum}\n")  // 65+66+...+80 = 1160
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
    print("{count}\n")
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
    print("{sso.count()}\n")

    // 16 bytes - should use heap
    var heap = "1234567890123456"
    print("{heap.count()}\n")
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
        print("{1}\n")
    end 'c17'
    if s.startsWith("That is") 'c18'
        print("{0}\n")
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
        print("{1}\n")
    end 'c19'
    if s.endsWith("stack allocated") 'c20'
        print("{0}\n")
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
        print("{1}\n")
    end 'c21'
    if s.contains("short string") 'c22'
        print("{0}\n")
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
    if let idx = s.find("very") 'found'
        print("{idx.charIndex()}\n")
    end 'found' else 'not_found'
        print("{0 - 1}\n")
    end 'not_found'
    if let idx2 = s.find("missing") 'found2'
        print("{idx2.charIndex()}\n")
    end 'found2' else 'not_found2'
        print("{0 - 1}\n")
    end 'not_found2'
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
        print("{1}\n")
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

<!-- test: memory-tracking-simple-interp -->
<!-- TrackAllocs: true -->
```maxon
function main() returns int
    var a = "hello"
    var b = " world"
    var s = "{a}{b}"
    print("{s.count()}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 6 bytes (string buffer)
ALLOC #2: 7 bytes (string buffer)
ALLOC #3: 12 bytes (string concat)
ALLOC #4: 22 bytes (int.toString)
ALLOC #5: 2 bytes (string buffer)
ALLOC #6: 4 bytes (string concat)
11
FREE #4: 22 bytes (string cleanup)
FREE #5: 2 bytes (string cleanup)
FREE #6: 4 bytes (string cleanup)
FREE #2: 7 bytes (string cleanup)
FREE #1: 6 bytes (string cleanup)
FREE #3: 12 bytes (string cleanup)

=== ALLOC STATS ===
Allocated: 53 bytes
Freed:     53 bytes
Leaked:    0 bytes
```

<!-- test: memory-tracking-chained-interp -->
<!-- TrackAllocs: true -->
String interpolation with multiple parts creates intermediate concat results.
Each intermediate result is properly cleaned up at scope exit.
```maxon
function main() returns int
    var a = "a"
    var b = "b"
    var c = "c"
    var d = "d"
    var s = "{a}{b}{c}{d}"
    print("{s.count()}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 2 bytes (string buffer)
ALLOC #2: 2 bytes (string buffer)
ALLOC #3: 2 bytes (string buffer)
ALLOC #4: 2 bytes (string buffer)
ALLOC #5: 3 bytes (string concat)
ALLOC #6: 4 bytes (string concat)
ALLOC #7: 5 bytes (string concat)
ALLOC #8: 22 bytes (int.toString)
ALLOC #9: 2 bytes (string buffer)
ALLOC #10: 3 bytes (string concat)
4
FREE #5: 3 bytes (string cleanup)
FREE #6: 4 bytes (string cleanup)
FREE #8: 22 bytes (string cleanup)
FREE #9: 2 bytes (string cleanup)
FREE #10: 3 bytes (string cleanup)
FREE #2: 2 bytes (string cleanup)
FREE #1: 2 bytes (string cleanup)
FREE #3: 2 bytes (string cleanup)
FREE #7: 5 bytes (string cleanup)
FREE #4: 2 bytes (string cleanup)

=== ALLOC STATS ===
Allocated: 47 bytes
Freed:     47 bytes
Leaked:    0 bytes
```

<!-- test: memory-tracking-loop-interp -->
<!-- TrackAllocs: true -->
String accumulation in loop properly releases old values on reassignment.
The final value is released at scope exit.
```maxon
function main() returns int
    var s = ""
    var x = "x"
    var i = 0
    while i < 3 'loop'
        s = "{s}{x}"
        i = i + 1
    end 'loop'
    print("{s.count()}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 2 bytes (string buffer)
ALLOC #2: 2 bytes (string concat)
ALLOC #3: 3 bytes (string concat)
FREE #2: 2 bytes (string cleanup)
ALLOC #4: 4 bytes (string concat)
FREE #3: 3 bytes (string cleanup)
ALLOC #5: 22 bytes (int.toString)
ALLOC #6: 2 bytes (string buffer)
ALLOC #7: 3 bytes (string concat)
3
FREE #5: 22 bytes (string cleanup)
FREE #6: 2 bytes (string cleanup)
FREE #7: 3 bytes (string cleanup)
FREE #1: 2 bytes (string cleanup)
FREE #4: 4 bytes (string cleanup)

=== ALLOC STATS ===
Allocated: 38 bytes
Freed:     38 bytes
Leaked:    0 bytes
```

<!-- test: memory-tracking-no-leak-scope-exit -->
<!-- TrackAllocs: true -->
```maxon
function main() returns int
    if true 'scope'
        var temp = "heap allocated string here!"
        print("{temp.count()}\n")
    end 'scope'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
ALLOC #1: 28 bytes (string buffer)
ALLOC #2: 22 bytes (int.toString)
ALLOC #3: 2 bytes (string buffer)
ALLOC #4: 4 bytes (string concat)
27
FREE #2: 22 bytes (string cleanup)
FREE #3: 2 bytes (string cleanup)
FREE #4: 4 bytes (string cleanup)
FREE #1: 28 bytes (string cleanup)

=== ALLOC STATS ===
Allocated: 56 bytes
Freed:     56 bytes
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
    print("{s.bytes().count()}\n")
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
    print("{s.bytes().count()}\n")  // 5 bytes (c=1, a=1, f=1, é=2)
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
    print("{s.count()}\n")  // 4 graphemes
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
    print("{s.count()}\n")
    print("{s.bytes().count()}\n")
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
    print("{count}\n")
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
    print("{count}\n")
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
    print("{count}\n")
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
        print("{cp}\n")
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
    print("{s.count()}\n")

    var u = "abc"
    u = "testing"
    print("{u.count()}\n")

    var v = ""
    v = "world"
    print("{v.count()}\n")

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

<!-- test: slice-basic -->
### Basic String Slicing
```maxon
function main() returns int
    var s = "hello world"
    var start = s.startIndex()
    if let spaceIdx = s.find(" ") 'found'
        var sub = s.slice(start, end_ = spaceIdx)
        print(sub)
    end 'found'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: slice-full -->
### Slice Entire String
```maxon
function main() returns int
    var s = "hello"
    var start = s.startIndex()
    var endIdx = s.endIndex()
    var sub = s.slice(start, end_ = endIdx)
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

<!-- test: slice-empty -->
### Empty Slice
```maxon
function main() returns int
    var s = "hello"
    var start = s.startIndex()
    var sub = s.slice(start, end_ = start)
    print("{sub.count()}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: slice-iteration -->
### Iterate Over Sliced String
```maxon
function main() returns int
    var s = "abcdef"
    var start = s.startIndex()
    if let idx = s.find("d") 'found'
        var sub = s.slice(start, end_ = idx)
        for c in sub 'loop'
            print("{c}\n")
        end 'loop'
    end 'found'
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

<!-- test: cow-mutation-copies -->
### COW Mutation Creates Copy
```maxon
function main() returns int
    var original = "HELLO"
    var copy = original
    copy = copy.toLower()
    print("{original}\n")
    print("{copy}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
HELLO
hello
```

<!-- test: cow-original-unchanged -->
### COW Original Unchanged After Copy Mutation
```maxon
function main() returns int
    var a = "TEST STRING"
    var b = a
    var c = a
    b = b.toLower()
    print("{a}\n")
    print("{b}\n")
    print("{c}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
TEST STRING
test string
TEST STRING
```

<!-- test: cow-slice-independent -->
### Slice Is Independent After Parent Goes Out of Scope
Demonstrates that sliced strings work correctly.
```maxon
function main() returns int
    var s = "hello world"
    var start = s.startIndex()
    if let spaceIdx = s.find(" ") 'found'
        var sub = s.slice(start, end_ = spaceIdx)
        print("{sub}\n")
    end 'found'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```
