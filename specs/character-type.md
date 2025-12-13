---
feature: character-type
status: experimental
keywords: [character, grapheme, egc, utf8]
category: types
---

# Character Type

## Developer Notes

`character` type representing an Extended Grapheme Cluster (EGC) — a user-perceived character.

### Memory Layout

**Character Type (16 bytes):**

Uses identical SSO layout as `string`:

Small Character (MSB of byte 15 = 0):
- bytes 0-14: UTF-8 data (inline)
- byte 15: remaining capacity (15 - length)

Large Character (MSB of byte 15 = 1):
- bytes 0-7: pointer to heap buffer
- bytes 8-11: count (length in bytes)
- bytes 12-15: capacity | 0x80000000

### Implementation

- Represented as LLVM `{i64, i64}` type (same as `string`)
- Character literals enclosed in single quotes: `'A'`, `'é'`, `'👨‍👩‍👧‍👦'`
- Most characters fit in SSO (15 bytes covers vast majority of grapheme clusters)
- Complex emoji sequences may require heap allocation
- Lexer handles escape sequences in `readCharLiteral()`
- Used for string iteration (iterating a `string` yields `character` values)

Common escape sequences: `\n` (newline), `\t` (tab), `\\` (backslash), `\'` (single quote).

### Character vs Byte

The `character` type is NOT an alias for `byte`. UTF-8 characters can span multiple bytes:
- `'A'` = 1 byte (ASCII)
- `'é'` = 2 bytes (Latin Extended)
- `'中'` = 3 bytes (CJK)
- `'🎉'` = 4 bytes (Emoji)
- `'👨‍👩‍👧‍👦'` = 25 bytes (Family emoji with ZWJ sequences)

Use `byte` for raw byte access when working with binary data or UTF-8 code units.

## Documentation

The `character` type represents an Extended Grapheme Cluster (EGC) — what users perceive as a single character.

### Syntax

```maxon
var letter = 'A'
var accent = 'é'
var emoji = '🎉'
```
Character literals are enclosed in single quotes.

### Extended Grapheme Clusters

An EGC represents what a user perceives as a single character, even if composed of multiple Unicode code points:

```maxon
var family = '👨‍👩‍👧‍👦'  // Family emoji (multiple code points joined with ZWJ)
var flag = '🇺🇸'          // Flag (regional indicator pair)
```

### String Iteration

Iterating over a string yields `character` values (EGCs):

```maxon
var s = "café"
for c in s 'chars'
    // c is 'c', 'a', 'f', 'é' (4 iterations, not 5 bytes)
end 'chars'
```

### Character Methods

```maxon
var c = 'é'
var b = c.bytes()
b.count()              // Returns byte length of UTF-8 encoding (2 for é)
var cp = c.codepoints()
cp.count()             // Returns number of Unicode codepoints
c.toString()           // Converts to string

var a = 'A'
a.asciiValue()         // Returns 65 (ASCII code for 'A')
```

### ASCII Value

The `asciiValue()` method returns the ASCII code (0-127) for single-byte ASCII characters:

```maxon
var letter = 'A'
print("{letter.asciiValue()}")  // Prints: 65

var digit = '0'
print("{digit.asciiValue()}")   // Prints: 48
```

For non-ASCII characters (multi-byte UTF-8 or values >= 128), `asciiValue()` returns `nil`.

## Tests

<!-- test: basic-character -->
### Basic Character

```maxon
function main() returns int
    var x = 'A'
    if x == 'A' 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```

<!-- test: character-comparison -->
### Character Comparison

```maxon
function main() returns int
    var a = 'A'
    var b = 'B'
    if a < b 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```

<!-- test: character-in-variable -->
### Character in Variable

```maxon
function main() returns int
    var letter = 'Z'
    if letter == 'Z' 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```

<!-- test: multibyte-character-2byte -->
### Multi-byte Character (2-byte UTF-8)

```maxon
function main() returns int
    var c = 'é'
    print("{c.bytes().count()}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: multibyte-character-3byte -->
### Multi-byte Character (3-byte UTF-8)

```maxon
function main() returns int
    var c = '中'
    print("{c.bytes().count()}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: multibyte-character-4byte -->
### Multi-byte Character (4-byte Emoji)

```maxon
function main() returns int
    var c = '🎉'
    print("{c.bytes().count()}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: character-to-string -->
### Character to String Conversion

```maxon
function main() returns int
    var c = 'A'
    var s = c.toString()
    print(s)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
A
```

<!-- test: multibyte-character-to-string -->
### Multi-byte Character to String

```maxon
function main() returns int
    var c = '中'
    var s = c.toString()
    print(s)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
中
```

<!-- test: character-equality-multibyte -->
### Multi-byte Character Equality

```maxon
function main() returns int
    var a = 'é'
    var b = 'é'
    if a == b 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```

<!-- test: character-inequality-multibyte -->
### Multi-byte Character Inequality

```maxon
function main() returns int
    var a = 'é'
    var b = 'è'
    if a != b 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```

<!-- test: emoji-character -->
### Emoji Character

```maxon
function main() returns int
    var emoji = '🎉'
    print(emoji.toString())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
🎉
```

<!-- test: flag-emoji-character -->
### Flag Emoji (Regional Indicator Pair)

```maxon
function main() returns int
    var flag = '🇺🇸'
    print("{flag.bytes().count()}")
    print(flag.toString())
    return 0
end 'main'
```
```exitcode
0
```
```stdout
8
🇺🇸
```

<!-- test: family-emoji-character -->
### Family Emoji (ZWJ Sequence)

```maxon
function main() returns int
    var family = '👨‍👩‍👧'
    print("{family.bytes().count()}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
18
```

<!-- test: skin-tone-emoji -->
### Skin Tone Modifier Emoji

```maxon
function main() returns int
    var wave = '👋🏽'
    print("{wave.bytes().count()}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
8
```

<!-- test: escape-sequences -->
### Escape Sequences in Character

```maxon
function main() returns int
    var newline = '\n'
    var tab = '\t'
    var backslash = '\\'
    var quote = '\''
    print("{newline.bytes().count()}")
    print("{tab.bytes().count()}")
    print("{backslash.bytes().count()}")
    print("{quote.bytes().count()}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1
1
1
1
```

<!-- test: ascii-value-letter -->
### ASCII Value for Letter

```maxon
function main() returns int
    var c = 'A'
    if let val = c.asciiValue() 'unwrap'
        print("{val}")
    end 'unwrap'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
65
```

<!-- test: ascii-value-digit -->
### ASCII Value for Digit

```maxon
function main() returns int
    var c = '0'
    if let val = c.asciiValue() 'unwrap'
        print("{val}")
    end 'unwrap'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
48
```

<!-- test: ascii-value-lowercase -->
### ASCII Value for Lowercase

```maxon
function main() returns int
    var c = 'a'
    if let val = c.asciiValue() 'unwrap'
        print("{val}")
    end 'unwrap'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
97
```

<!-- test: ascii-value-space -->
### ASCII Value for Space

```maxon
function main() returns int
    var c = ' '
    if let val = c.asciiValue() 'unwrap'
        print("{val}")
    end 'unwrap'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
32
```

<!-- test: ascii-value-newline -->
### ASCII Value for Newline Escape

```maxon
function main() returns int
    var c = '\n'
    if let val = c.asciiValue() 'unwrap'
        print("{val}")
    end 'unwrap'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
10
```

<!-- test: ascii-value-non-ascii -->
### ASCII Value for Non-ASCII Returns nil

```maxon
function main() returns int
    var c = 'é'
    if let val = c.asciiValue() 'unwrap'
        print("{val}")
    end 'unwrap' else 'nil_case'
        print("nil")
    end 'nil_case'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
nil
```

<!-- test: ascii-value-emoji -->
### ASCII Value for Emoji Returns nil

```maxon
function main() returns int
    var c = '🎉'
    if let val = c.asciiValue() 'unwrap'
        print("{val}")
    end 'unwrap' else 'nil_case'
        print("nil")
    end 'nil_case'
    return 0
end 'main'
```
```exitcode
0
```
```stdout
nil
```
