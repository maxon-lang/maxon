---
title: Text
description: String, Character, Ascii, Unicode, and CharacterSet.
sidebar:
  order: 2
---

## String

`String` is an immutable-by-default UTF-8 string. Its unit of iteration and counting is the **grapheme
cluster**, a user-perceived character: `"é👍🏽".count()` is 2, although it is 10 bytes and 3 codepoints.
Positions are `StringIndex` values, which carry both a grapheme index and a byte position so stepping
never rescans the string.

`String` implements `Hashable`, `Equatable`, `Iterable` (over `Character`) and `Cloneable`.

```maxon
function main() returns ExitCode
	let s = "Hello, World"
	let parts = "a,b,,c".split(",")
	print("{parts.count()}\n")
	print("{"x-y-z".replace("-", with: "+")} {"x-y-z".replaceFirst("-", with: "+")}\n")

	let comma = try s.findFirst(",") otherwise s.endIndex()
	let head = s.slice(s.startIndex(), endIndex: comma)
	let five = s.slice(s.startIndex(), length: 5)
	print("{comma.charIndex()} {head} {five}\n")

	for c in "héllo" 'each'
		print("[{c}]")
	end 'each'

	print("\n{"  padded  ".trim()}|{"Hello".toUpper()}\n")
	return 0
end 'main'
```

Output: `4`, `x+y+z x+y-z`, `5 Hello Hello`, `[h][é][l][l][o]`, `padded|HELLO`.

### Construction and conversion

| Member | Returns | Description |
|--------|---------|-------------|
| `String.from(bytes ByteArray)` | `String` | A string over the bytes. The bytes are not validated as UTF-8. |
| `String.fromCString(cs cstring)` | `String` | Copy the NUL-terminated bytes at `cs`; the string owns its copy. Stops at the first zero byte. |
| `String.init(managed)` | `String` | The compiler's literal initializer; programs write a literal instead. |
| `toByteArray()` | `ByteArray` | The UTF-8 bytes as a new, independent array. |
| `cstr()` | `cstring` | A NUL-terminated view of the bytes, for an intrinsic that takes `cstring`. Valid while the string is alive and unmodified. |
| `clone()` | `String` | An independent copy; writes to either never show through the other. |

### Properties

| Member | Returns | Description |
|--------|---------|-------------|
| `count()` | `GraphemeIndex` | Number of grapheme clusters. O(n) for non-ASCII text; cache it if you need it repeatedly. |
| `byteLength()` | `BytePos` | Number of UTF-8 bytes. |
| `isEmpty()` | `bool` | True when the string has no bytes. |
| `isAscii()` | `bool` | True when every byte is below 128. |
| `hash()` | `HashValue` | Through `Hashable`. |
| `equals(other String)` | `bool` | Byte-wise equality; `==` calls it. |

### Search

| Member | Returns | Description |
|--------|---------|-------------|
| `contains(needle String)` | `bool` | Substring test. |
| `contains(character Character)` | `bool` | Character test. |
| `startsWith(prefix String)` | `bool` | Prefix test. |
| `endsWith(suffix String)` | `bool` | Suffix test. |
| `findFirst(needle String)` | `StringIndex` | First occurrence. Throws `StringError.notFound`. |
| `findLast(needle String)` | `StringIndex` | Last occurrence. Throws `StringError.notFound`. |

### Indexing and Slicing

| Member | Returns | Description |
|--------|---------|-------------|
| `startIndex()` | `StringIndex` | Index of the first grapheme. |
| `endIndex()` | `StringIndex` | One past the last grapheme. |
| `indexAfter(idx StringIndex)` | `StringIndex` | Next grapheme boundary. Throws `StringError` at `endIndex()`. |
| `indexBefore(idx StringIndex)` | `StringIndex` | Previous grapheme boundary. Throws `StringError` at `startIndex()`. |
| `charAt(idx StringIndex)` | `Character` | The grapheme at `idx`. |
| `slice(start StringIndex, endIndex StringIndex)` | `String` | The graphemes in `[start, endIndex)`. |
| `slice(start StringIndex, length GraphemeIndex)` | `String` | `length` graphemes starting at `start`. |

`StringIndex` implements `Equatable` and `Comparable`:

| Member | Returns | Description |
|--------|---------|-------------|
| `StringIndex.create(charIndex GraphemeIndex, bytePos BytePos)` | `StringIndex` | Build an index from both coordinates. |
| `charIndex()` | `GraphemeIndex` | Grapheme position. |
| `bytePos()` | `BytePos` | UTF-8 byte offset. |

```maxon
function main() returns ExitCode
	let s = "héllo"
	var i = s.endIndex()

	while i != s.startIndex() 'backwards'
		i = try s.indexBefore(i) otherwise break
		print("{s.charAt(i)}")
	end 'backwards'

	print("\n")
	return 0
end 'main'
```

Output: `olléh`.

### Transforming

| Member | Returns | Description |
|--------|---------|-------------|
| `split(delimiter String)` | `StringArray` | The pieces between delimiters, empty pieces included. An empty delimiter, or an empty string, gives a one-element array. |
| `replace(old String, with String)` | `String` | Every occurrence replaced. An empty `old` returns a copy. |
| `replaceFirst(old String, with String)` | `String` | The first occurrence replaced. |
| `toLower()` | `String` | ASCII `A`–`Z` lowered; other characters are unchanged. |
| `toUpper()` | `String` | ASCII `a`–`z` raised; other characters are unchanged (`"Héllo".toUpper()` is `"HéLLO"`). |
| `append(other String)` | — | Append in place. |

### Trimming

| Member | Returns | Description |
|--------|---------|-------------|
| `trim()` | `String` | Remove Unicode whitespace and newlines from both ends. |
| `trimStart()` | `String` | From the start only. |
| `trimEnd()` | `String` | From the end only. |
| `trim(chars CharacterSet)` | `String` | Remove characters in `chars` from both ends. |
| `trimStart(chars CharacterSet)` | `String` | From the start only. |
| `trimEnd(chars CharacterSet)` | `String` | From the end only. |

The no-argument forms use `CharacterSet.whitespacesAndNewlines()`. Trimming walks grapheme clusters, so
`"\r\n"` is one unit: a set holding `'\r'` but not `"\r\n"` trims neither.

### Views

| Member | Returns | Iterates |
|--------|---------|----------|
| `bytes()` | `ByteView` | Each UTF-8 `Byte` |
| `codepoints()` | `CodepointView` | Each `Codepoint` |
| `utf16()` | `UTF16View` | Each UTF-16 code unit |
| `createIterator()` | `StringIterator` | Each `Character` (what `for c in s` uses) |

Each view has `count()` and `createIterator()`, and works in `for`-`in`. Constructing a view does not copy.
`StringIterator` implements `Iterator with Character`: `StringIterator.create(s)`, `current()`,
`advance()`.

### StringBuilder

A `String` owns exactly the bytes it holds, so appending to one in a loop copies what is already there on
each append. `StringBuilder` grows geometrically instead; build with it and take the finished `String` at
the end.

| Member | Returns | Description |
|--------|---------|-------------|
| `StringBuilder.create()` | `StringBuilder` | An empty builder. |
| `reserve(byteCount BytePos)` | — | Allocate room up front when the final size is known. |
| `append(other String)` | — | Append a piece. |
| `byteLength()` | `BytePos` | Bytes accumulated so far. |
| `isEmpty()` | `bool` | True when nothing has been appended. |
| `clear()` | — | Discard the contents, keeping the capacity. |
| `build()` | `String` | Hand the bytes over as a `String`. The builder is empty afterwards. |

```maxon
function main() returns ExitCode
	var out = StringBuilder.create()

	for i in 1 to 3 'each'
		out.append("item {i};")
	end 'each'

	let text = out.build()
	print("{text} {out.isEmpty()}\n")
	return 0
end 'main'
```

Output: `item 1;item 2;item 3; true`.

### Errors and aliases

```maxon
enum StringError implements Error
	notFound
	invalidIndex
end 'StringError'
```

`BytePos` and `GraphemeIndex` are both `int(0 to u64.max)`.

## Character

`Character` is one grapheme cluster: a character literal such as `'é'` or `'👍🏽'`, or what iterating a
`String` yields. It implements `Hashable`, `Equatable`, `Comparable` (byte-wise), `Stringable` and
`Cloneable`, and it works as the bound of a range: `for c in 'a' to 'e'`.

| Member | Returns | Description |
|--------|---------|-------------|
| `byteLength()` | `int(0 to u64.max)` | UTF-8 bytes in the cluster. |
| `codepoint()` | `Codepoint` | The first codepoint of the cluster. |
| `codepoints()` | `CodepointView` | Every codepoint of the cluster. |
| `bytes()` | `ByteView` | The UTF-8 bytes. |
| `asciiValue()` | `AsciiValue` | The value 0–127 of a single-byte ASCII character. Throws `CharacterError.notAscii` otherwise. |
| `advanceBy(n IterStep)` | `Character` | The character `n` codepoints later; used by character ranges. |
| `toString()` | `String` | The cluster as a string. |
| `clone()` | `Character` | An independent copy. |
| `equals(other Character)`, `compare(other Character)`, `hash()` | | Interface conformances. |

```maxon
enum CharacterError implements Error
	notAscii
end 'CharacterError'
```

`AsciiValue` is `int(0 to 127)`; `Codepoint` is `int(0 to 1114111)`.

```maxon
function main() returns ExitCode
	let e = 'é'
	let a = try 'A'.asciiValue() otherwise 0
	let refused = try e.asciiValue() otherwise 0
	print("{e.codepoint()} {e.byteLength()} {a} {refused}\n")
	return 0
end 'main'
```

Output: `233 2 65 0`.

## Ascii

`Ascii` classifies a `Character` by ASCII rules only; any character outside ASCII answers `false`. For
Unicode classification use [CharacterSet](#characterset).

| Function | True when the character is |
|----------|----------------------------|
| `Ascii.isDigit(c Character)` | `0`–`9` |
| `Ascii.isAlpha(c Character)` | `a`–`z` or `A`–`Z` |
| `Ascii.isAlphanumeric(c Character)` | a letter or a digit |
| `Ascii.isUpper(c Character)` | `A`–`Z` |
| `Ascii.isLower(c Character)` | `a`–`z` |
| `Ascii.isWhitespace(c Character)` | space, tab, `\n` or `\r` |

```maxon
function main() returns ExitCode
	print("{Ascii.isDigit('7')} {Ascii.isAlpha('é')} {Ascii.isUpper('Q')} {Ascii.isWhitespace('\t')}\n")
	return 0
end 'main'
```

Output: `true false true true`.

## Unicode

| Function | Returns | Description |
|----------|---------|-------------|
| `Unicode.isWhitespace(cp Codepoint)` | `bool` | True for every codepoint with the Unicode `White_Space` property: tab through carriage return, space, NEL, no-break space, the U+2000 space block, line and paragraph separators, and the other spaces. |

```maxon
function main() returns ExitCode
	print("{Unicode.isWhitespace(32)} {Unicode.isWhitespace(160)} {Unicode.isWhitespace(65)}\n")
	return 0
end 'main'
```

Output: `true true false`.

## CharacterSet

A `CharacterSet` is a set of characters defined by Unicode general categories, explicit members, or both.
It is what the `String` trimming methods take.

| Factory | Contents |
|---------|----------|
| `CharacterSet.whitespaces()` | Space separators (Zs) and tab — no newlines |
| `CharacterSet.newlines()` | LF, CR, CRLF, VT, FF, NEL, line and paragraph separators |
| `CharacterSet.whitespacesAndNewlines()` | Both of the above |
| `CharacterSet.decimalDigits()` | Decimal digits (Nd) |
| `CharacterSet.letters()` | Letters and marks (L\*, M\*) |
| `CharacterSet.lowercaseLetters()` | Lowercase letters (Ll) |
| `CharacterSet.uppercaseLetters()` | Uppercase and titlecase letters (Lu, Lt) |
| `CharacterSet.alphanumerics()` | Letters, marks and numbers (L\*, M\*, N\*) |
| `CharacterSet.punctuation()` | Punctuation (P\*) |
| `CharacterSet.symbols()` | Symbols (S\*) |
| `CharacterSet.controlCharacters()` | Control and format characters (Cc, Cf) |
| `CharacterSet.from(chars Set with Character)` | Exactly the characters given |

| Member | Returns | Description |
|--------|---------|-------------|
| `contains(c Character)` | `bool` | True when `c` is a member or falls in one of the set's categories. |

```maxon
typealias Letters = Set with Character

function main() returns ExitCode
	var members = Letters.create()
	members.insert('x')
	members.insert('y')
	let xy = CharacterSet.from(members)

	print("[{"xyhixy".trim(xy)}] [{"123abc456".trim(CharacterSet.decimalDigits())}]\n")
	print("{CharacterSet.symbols().contains('$')} {CharacterSet.lowercaseLetters().contains('ß')}\n")
	return 0
end 'main'
```

Output: `[hi] [abc]` and `true true`.
