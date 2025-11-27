---
feature: char-type
status: experimental
keywords: [char, character, grapheme, egc, utf8]
category: types
---

# Char Type

## Developer Notes

Swift-style `char` type representing an Extended Grapheme Cluster (EGC) — a user-perceived character.

### Memory Layout

**Char Type (16 bytes):**

Uses identical SSO layout as `string`:

Small Char (MSB of byte 15 = 0):
- bytes 0-14: UTF-8 data (inline)
- byte 15: remaining capacity (15 - length)

Large Char (MSB of byte 15 = 1):
- bytes 0-7: pointer to heap buffer
- bytes 8-11: count (length in bytes)
- bytes 12-15: capacity | 0x80000000

### Implementation

- Represented as LLVM `{i64, i64}` struct (same as `string`)
- Character literals enclosed in single quotes: `'A'`, `'é'`, `'👨‍👩‍👧‍👦'`
- Most characters fit in SSO (15 bytes covers vast majority of grapheme clusters)
- Complex emoji sequences may require heap allocation
- Lexer handles escape sequences in `readCharLiteral()`
- Used for string iteration (iterating a `string` yields `char` values)

Common escape sequences: `\n` (newline), `\t` (tab), `\\` (backslash), `\'` (single quote).

### Char vs Byte

The `char` type is NOT an alias for `byte`. UTF-8 characters can span multiple bytes:
- `'A'` = 1 byte (ASCII)
- `'é'` = 2 bytes (Latin Extended)
- `'中'` = 3 bytes (CJK)
- `'🎉'` = 4 bytes (Emoji)
- `'👨‍👩‍👧‍👦'` = 25 bytes (Family emoji with ZWJ sequences)

Use `byte` for raw byte access when working with binary data or UTF-8 code units.

## Documentation

The `char` type represents an Extended Grapheme Cluster (EGC) — what users perceive as a single character.

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

Iterating over a string yields `char` values (EGCs):

```maxon
var s = "café"
for c in s 'chars'
    // c is 'c', 'a', 'f', 'é' (4 iterations, not 5 bytes)
end 'chars'
```

## Tests

<!-- test: basic-char -->
### Basic Char

```maxon
function main() int
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

<!-- test: char-comparison -->
### Char Comparison

```maxon
function main() int
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

<!-- test: char-in-variable -->
### Char in Variable

```maxon
function main() int
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

