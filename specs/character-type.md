---
feature: character-type
status: experimental
keywords: [character, grapheme, egc, utf8]
category: types
---

# Character Type

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
"{c}"                 // Converts to string via interpolation

var a = 'A'
a.asciiValue()         // Returns 65 (ASCII code for 'A')
```

### ASCII Value

The `asciiValue()` method returns the ASCII code (0-127) for single-byte ASCII characters:

```maxon
var letter = 'A'
print("{letter.asciiValue()}\n")  // Prints: 65

var digit = '0'
print("{digit.asciiValue()}\n")   // Prints: 48
```

For non-ASCII characters (multi-byte UTF-8 or values >= 128), `asciiValue()` returns `nil`.

## Tests

<!-- test: basic-character -->
### Basic Character

```maxon
function main() returns ExitCode
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
function main() returns ExitCode
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
function main() returns ExitCode
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
function main() returns ExitCode
  var c = 'é'
  print("{c.bytes().count()}\n")
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
function main() returns ExitCode
  var c = '中'
  print("{c.bytes().count()}\n")
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
function main() returns ExitCode
  var c = '🎉'
  print("{c.bytes().count()}\n")
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
function main() returns ExitCode
  var c = 'A'
  var s = "{c}"
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
function main() returns ExitCode
  var c = '中'
  var s = "{c}"
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
function main() returns ExitCode
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
function main() returns ExitCode
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
function main() returns ExitCode
  var emoji = '🎉'
  print("{emoji}\n")
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
function main() returns ExitCode
  var flag = '🇺🇸'
  print("{flag.bytes().count()}\n")
  print("{flag}\n")
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
function main() returns ExitCode
  var family = '👨‍👩‍👧'
  print("{family.bytes().count()}\n")
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
function main() returns ExitCode
  var wave = '👋🏽'
  print("{wave.bytes().count()}\n")
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
function main() returns ExitCode
  var newline = '\n'
  var tab = '\t'
  var backslash = '\\'
  var quote = '\''
  print("{newline.bytes().count()}\n")
  print("{tab.bytes().count()}\n")
  print("{backslash.bytes().count()}\n")
  print("{quote.bytes().count()}\n")
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
function main() returns ExitCode
  var c = 'A'
  var val = try c.asciiValue() otherwise -1
  print("{val}\n")
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
function main() returns ExitCode
  var c = '0'
  var val = try c.asciiValue() otherwise -1
  print("{val}\n")
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
function main() returns ExitCode
  var c = 'a'
  var val = try c.asciiValue() otherwise -1
  print("{val}\n")
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
function main() returns ExitCode
  var c = ' '
  var val = try c.asciiValue() otherwise -1
  print("{val}\n")
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
function main() returns ExitCode
  var c = '\n'
  var val = try c.asciiValue() otherwise -1
  print("{val}\n")
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
### ASCII Value for Non-ASCII Returns Error

```maxon
function main() returns ExitCode
  var c = 'é'
  var val = try c.asciiValue() otherwise -1
  print("{val}")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
-1
```

<!-- test: ascii-value-emoji -->
### ASCII Value for Emoji Returns Error

```maxon
function main() returns ExitCode
  var c = '🎉'
  var val = try c.asciiValue() otherwise -1
  print("{val}")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
-1
```

<!-- test: match-escape-character -->
### Match with Escape Character Literals

Character match patterns must correctly handle escape sequences like `'\n'`, `'\t'`, `'\r'`, and `'\\'`.

```maxon
function main() returns ExitCode
  let c = '\n'
  match c 'check'
    '\n' then return 0
    default then return 1
  end 'check'
end 'main'
```
```exitcode
0
```
