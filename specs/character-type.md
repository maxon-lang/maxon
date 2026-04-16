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
	print("{c}")  // iterates 4 times: 'c', 'a', 'f', 'é' (not 5 bytes)
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
	let x = 'A'
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
	let a = 'A'
	let b = 'B'
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
	let letter = 'Z'
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
	let c = 'é'
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
	let c = '中'
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
	let c = '🎉'
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
	let c = 'A'
	let s = "{c}"
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
	let c = '中'
	let s = "{c}"
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
	let a = 'é'
	let b = 'é'
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
	let a = 'é'
	let b = 'è'
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
	let emoji = '🎉'
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
	let flag = '🇺🇸'
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
	let family = '👨‍👩‍👧'
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
	let wave = '👋🏽'
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
	let newline = '\n'
	let tab = '\t'
	let backslash = '\\'
	let quote = '\''
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
	let c = 'A'
	let val = try c.asciiValue() otherwise 0
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
	let c = '0'
	let val = try c.asciiValue() otherwise 0
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
	let c = 'a'
	let val = try c.asciiValue() otherwise 0
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
	let c = ' '
	let val = try c.asciiValue() otherwise 0
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
	let c = '\n'
	let val = try c.asciiValue() otherwise 0
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
	let c = 'é'
	if try c.asciiValue() 'hasAscii'
		return 1
	end 'hasAscii'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: ascii-value-emoji -->
### ASCII Value for Emoji Returns Error

```maxon
function main() returns ExitCode
	let c = '🎉'
	if try c.asciiValue() 'hasAscii'
		return 1
	end 'hasAscii'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.otherwise-out-of-range -->
### Otherwise value must be within ranged type bounds

```maxon
function main() returns ExitCode
	let c = 'x'
	let val = try c.asciiValue() otherwise -1
	return val
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/character-type/error.otherwise-out-of-range.test:4:12: otherwise value -1 is outside the range of 'AsciiValue' (int(0 to 127))
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
