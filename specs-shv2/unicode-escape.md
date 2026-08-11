---
feature: unicode-escape
status: experimental
keywords: [unicode, escape, character, string, codepoint]
category: literals
---

# Unicode Escape Sequences

## Documentation

The `\uXXXX` escape sequence allows specifying Unicode characters by their code point (exactly 4 hex digits). It works in both character literals and string literals.

### Syntax

```maxon
var nbsp = '\u00A0'          // Non-breaking space character
var s = "Price:\u00A0$5"     // Non-breaking space in string
var sigma = '\u03A3'         // Greek capital sigma
```

### Character Literals

```maxon
var nel = '\u0085'           // U+0085 NEL (Next Line)
var nbsp = '\u00A0'          // U+00A0 Non-Breaking Space
var ideographic = '\u3000'   // U+3000 Ideographic Space
```

### String Literals

Works in both plain and interpolated strings:

```maxon
var s = "hello\u0021"        // "hello!"
var name = "world"
var s2 = "hello\u0021 {name}" // "hello! world"
```

## Tests

<!-- test: unicode-escape.char-basic -->
### Character literal with unicode escape

```maxon
function main() returns ExitCode
	let excl = '\u0021'
	if excl == '!' 'check'
		print("PASS")
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
PASS
```

<!-- test: unicode-escape.char-multibyte -->
### Multi-byte unicode character

```maxon
function main() returns ExitCode
	let sigma = '\u03A3'
	print("{sigma}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Σ
```

<!-- test: unicode-escape.string-basic -->
### String literal with unicode escape

```maxon
function main() returns ExitCode
	let s = "hello\u0021"
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello!
```

<!-- test: unicode-escape.string-interp -->
### Unicode escape in interpolated string

```maxon
function main() returns ExitCode
	let name = "world"
	let s = "hello\u0021 {name}"
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello! world
```

<!-- test: unicode-escape.whitespace-chars -->
### Unicode whitespace characters via escape

```maxon
function main() returns ExitCode
	let ws = CharacterSet.whitespacesAndNewlines()
	let nbsp = '\u00A0'
	let enSpace = '\u2002'
	let ideoSpace = '\u3000'
	if ws.contains(nbsp) 'c1'
		print("nbsp ")
	end 'c1'
	if ws.contains(enSpace) 'c2'
		print("enSpace ")
	end 'c2'
	if ws.contains(ideoSpace) 'c3'
		print("ideoSpace")
	end 'c3'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
nbsp enSpace ideoSpace
```

<!-- test: unicode-escape.invalid-too-few-digits -->
### Error: too few hex digits

```maxon
function main() returns ExitCode
	let x = '\u00'
	return 0
end 'main'
```
```maxoncstderr
error E1004: specs/fragments/unicode-escape/unicode-escape.invalid-too-few-digits.test:3:10: Invalid unicode escape '\u00': expected 4 hex digits in character literal
```

<!-- test: unicode-escape.invalid-too-few-digits-byte-string -->
### Error: too few hex digits in a byte string literal

A `b"..."` literal decodes `\uNNNN` by its own rule (one Latin-1 byte, not UTF-8), but a malformed
one is the same fault as anywhere else and is reported by the same diagnostic, naming the byte
string literal as its context.

```maxon
function main() returns ExitCode
	let bytes = b"\u12"
	return 0
end 'main'
```
```maxoncstderr
error E1004: specs/fragments/unicode-escape/unicode-escape.invalid-too-few-digits-byte-string.test:3:14: Invalid unicode escape '\u12': expected 4 hex digits in byte string literal
```

<!-- test: unicode-escape.nonfirst-escape-column -->
### The reported column of a non-first escape

The column a malformed digit escape is blamed at is the literal token's column advanced by the escape's
byte offset *within the body*, so it is short of the escape itself by the opening delimiter's width — one
character for `'` and `"`, two for `b"`. Every other case in the corpus puts the escape at body offset 0,
where that difference cannot be seen; this one puts it at offset 2 so the rule is under test rather than
only written down. Here the token starts at column 14, the `\` is at source column 18, and the
diagnostic reports 16.

```maxon
function main() returns ExitCode
	let bytes = b"AB\u12"
	return 0
end 'main'
```
```maxoncstderr
error E1004: specs/fragments/unicode-escape/unicode-escape.nonfirst-escape-column.test:3:16: Invalid unicode escape '\u12': expected 4 hex digits in byte string literal
```
