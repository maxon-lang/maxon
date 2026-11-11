---
feature: unicode-scalar-values
status: stable
keywords: [unicode, surrogate, scalar, character, codepoint, utf8, json]
category: types
---

# Unicode scalar values

## Documentation

A `Character` is made of Unicode scalar values: U+0000 to U+D7FF and U+E000 to U+10FFFF. The surrogate
block U+D800 to U+DFFF holds no scalar value, UTF-8 has no spelling for it, and no `Character` or text
the language builds from a number holds one. `Codepoint` is the integer range `int(0 to 1114111)`, so it
can name a surrogate as a number; the places that turn a number into text refuse it.

- A literal whose bytes are not well-formed UTF-8 is **E1004**: `'\uD800'`, `"\uDFFF"`, and a raw
  surrogate or malformed byte sequence written into the source.
- `Character.advanceBy(n)` counts scalar values, so it steps over the surrogate block: one step forward
  from U+D7FF is U+E000, and one step back from U+E000 is U+D7FF. A target before U+0000 or after
  U+10FFFF panics with the range check of `Codepoint`.
- A character range (`'a' to 'z'`) visits scalar values in order and skips the block.
- A JSON `\uD800` escape that is not half of a high-then-low pair is `JsonError.invalidSurrogate`.
- `utf8AppendCodepoint` panics on a surrogate.

## Tests

<!-- test: error.surrogate-escape-in-a-character-literal -->
### A character literal cannot spell a surrogate

```maxon
function main() returns ExitCode
	let c = '\uD800'
	return c.byteLength()
end 'main'
```
```maxoncstderr
error E1004: <fragment>:3:10: Malformed UTF-8 in character literal: a surrogate (U+D800 to U+DFFF) or an invalid byte sequence is not a Unicode scalar value
```

<!-- test: error.surrogate-escape-in-a-string-literal -->
### A string literal cannot spell a surrogate

```maxon
function main() returns ExitCode
	let s = "a\uDFFFb"
	return s.byteLength()
end 'main'
```
```maxoncstderr
error E1004: <fragment>:3:10: Malformed UTF-8 in string interpolation: a surrogate (U+D800 to U+DFFF) or an invalid byte sequence is not a Unicode scalar value
```

<!-- test: error.surrogate-escape-in-an-interpolated-string -->
### A string fragment beside an interpolation cannot spell a surrogate

```maxon
function main() returns ExitCode
	let n = 7
	let s = "{n}\uDC00"
	return s.byteLength()
end 'main'
```
```maxoncstderr
error E1004: <fragment>:4:14: Malformed UTF-8 in string interpolation: a surrogate (U+D800 to U+DFFF) or an invalid byte sequence is not a Unicode scalar value
```

<!-- test: the-scalar-values-beside-the-surrogate-block-are-literals -->
### U+D7FF and U+E000 are the last and first scalar values beside the block

```maxon
function show(label String, c Character)
	let wellFormed = utf8IsWellFormed(c.toString().toByteArray())
	print("{label}: {c.codepoint()} {c.byteLength()} {wellFormed}\n")
end 'show'

function main() returns ExitCode
	show("last before", c: '\uD7FF')
	show("first after", c: '\uE000')
	show("last", c: '😀')
	return 0
end 'main'
```
```exitcode
0
```
```stdout
last before: 55295 3 true
first after: 57344 3 true
last: 128512 4 true
```

<!-- test: advance-by-steps-over-the-surrogate-block -->
### `advanceBy` counts scalar values, in either direction

```maxon
function show(label String, c Character)
	let wellFormed = utf8IsWellFormed(c.toString().toByteArray())
	print("{label}: {c.codepoint()} {c.byteLength()} {wellFormed}\n")
end 'show'

function main() returns ExitCode
	let beforeBlock = '\uD7FF'
	let twoBefore = '\uD7FE'
	let afterBlock = '\uE000'
	let twoAfter = '\uE001'
	let first = '\0'

	show("D7FF +1", c: beforeBlock.advanceBy(1))
	show("D7FF +2", c: beforeBlock.advanceBy(2))
	show("D7FE +2", c: twoBefore.advanceBy(2))
	show("E000 -1", c: afterBlock.advanceBy(-1))
	show("E001 -2", c: twoAfter.advanceBy(-2))
	show("0 +55296", c: first.advanceBy(55296))
	show("E000 -55296", c: afterBlock.advanceBy(-55296))
	show("D7FF +0", c: beforeBlock.advanceBy(0))
	show("a +1", c: 'a'.advanceBy(1))
	return 0
end 'main'
```
```exitcode
0
```
```stdout
D7FF +1: 57344 3 true
D7FF +2: 57345 3 true
D7FE +2: 57344 3 true
E000 -1: 55295 3 true
E001 -2: 55295 3 true
0 +55296: 57344 3 true
E000 -55296: 0 1 true
D7FF +0: 55295 3 true
a +1: 98 1 true
```

<!-- test: a-character-range-skips-the-surrogate-block -->
### A character range over the block visits scalar values only

```maxon
function main() returns ExitCode
	for c in '\uD7FE' to '\uE001' 'closed'
		print("{c.codepoint()} ")
	end 'closed'

	print("\n")

	for c in '\uD7FF' upto '\uE001' 'halfOpen'
		print("{c.codepoint()} ")
	end 'halfOpen'

	print("\n")

	var trips = 0 as Count
	var wellFormed = true

	for c in '\u0000' to '\uFFFF' 'basicPlane'
		trips = trips + 1

		if not utf8IsWellFormed(c.toString().toByteArray()) 'malformed'
			wellFormed = false
		end 'malformed'
	end 'basicPlane'

	print("{trips} {wellFormed}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
55294 55295 57344 57345 
55295 57344 
63488 true
```

<!-- test: json-refuses-a-lone-surrogate-escape -->
### A JSON `\u` escape is a scalar value or half of a surrogate pair

```maxon
function show(label String, text String)
	let doc = try Json.parse(text) otherwise 'refused'
		print("{label}: refused\n")
		return
	end 'refused'

	let value = doc.get(doc.root).stringValue
	print("{label}: {value.byteLength()} {utf8IsWellFormed(value.toByteArray())}\n")
end 'show'

function main() returns ExitCode
	show("pair", text: "\"\\ud83d\\ude00\"")
	show("high alone", text: "\"\\ud800\"")
	show("low alone", text: "\"\\ude00\"")
	show("high then non-low", text: "\"\\ud800\\u0041\"")
	show("high then end", text: "\"\\ud800x\"")
	show("last before block", text: "\"\\ud7ff\"")
	show("first after block", text: "\"\\ue000\"")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
pair: 4 true
high alone: refused
low alone: refused
high then non-low: refused
high then end: refused
last before block: 3 true
first after block: 3 true
```

<!-- test: utf8-append-codepoint-refuses-a-surrogate -->
### Encoding a surrogate codepoint panics

```maxon
function main() returns ExitCode
	let out = ByteArray.create(0)
	utf8AppendCodepoint(65, out: out)
	utf8AppendCodepoint(55296, out: out)
	return out.count()
end 'main'
```
```exitcode
1
```
```stderr
panic at utf8.maxon:226: utf8AppendCodepoint: a surrogate (U+D800 to U+DFFF) is not a Unicode scalar value
Stack trace:
  in utf8AppendCodepoint
  in main
  in mrt_start
```
