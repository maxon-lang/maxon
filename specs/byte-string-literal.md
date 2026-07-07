---
feature: byte-string-literal
status: stable
keywords: [byte, string, literal, bytebuffer]
category: types
---

# Byte String Literal

## Documentation

A byte string literal uses the `b"..."` prefix to create a `ByteArray` (`Array with Byte`) directly from a string, without allocating a `String`. This is useful when working with raw bytes or APIs that expect byte arrays.

### Syntax

```text
let bytes = b"hello"           // ByteArray containing [104, 101, 108, 108, 111]
let empty = b""                // Empty ByteArray
let escaped = b"line\n"        // Supports escape sequences
```

The byte string literal supports the same escape sequences as regular string literals (`\n`, `\t`, `\\`, `\0`, etc.), including the `\xNN` hex escape (see the `hex-escape` spec) and the `\uNNNN` unicode escape.

### Non-ASCII characters and byte values

Unlike a regular string literal — where a source character is stored as its multi-byte UTF-8 encoding — a byte string literal is a raw byte sequence. A literal character (or a `\uNNNN` escape) whose codepoint is in the range `0x00`–`0xFF` (Latin-1) contributes exactly **one** byte equal to that codepoint. So `b"À"` (U+00C0) and `b"\xc0"` produce the identical single byte `0xC0` (192), and `b"é"` (U+00E9) produces the single byte `0xE9` (233).

```text
let a = b"À"        // one byte: [192]
let b = b"\xc0"     // one byte: [192] — identical to b"À"
```

A codepoint above `0xFF` cannot be represented as a single byte, so a literal character or `\uNNNN` escape that names one is rejected at compile time with **E1004**. Use a `\xNN` hex escape to embed a specific raw byte regardless of its Unicode interpretation.

### Use Cases

Byte string literals are particularly useful as map keys when the map uses `ByteArray` keys, avoiding the overhead of `String` construction and `toByteArray()` conversion:

```text
typealias KeywordMap = Map with (ByteArray, int)
let keywords = [b"if": 1, b"else": 2, b"while": 3]
```

### Methods

Byte string literals produce a standard `ByteArray`, so all `Array` methods are available:

```text
let data = b"hello"
data.count()       // 5
data.get(0)        // 104 (ASCII 'h')
```

## Tests

<!-- test: byte-string-literal.basic -->

```maxon
function main() returns ExitCode
		let bytes = b"hello"
		return bytes.count()
end 'main'
```
```exitcode
5
```

<!-- test: byte-string-literal.empty -->

```maxon
function main() returns ExitCode
		let bytes = b""
		return bytes.count()
end 'main'
```
```exitcode
0
```

<!-- test: byte-string-literal.escape-sequences -->

```maxon
function main() returns ExitCode
		let bytes = b"a\nb"
		return bytes.count()
end 'main'
```
```exitcode
3
```

<!-- test: byte-string-literal.content -->

```maxon
function main() returns ExitCode
		let bytes = b"AB"
		let a = try bytes.get(0) otherwise 0
		let b = try bytes.get(1) otherwise 0
		print("{a} {b}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
65 66
```

<!-- test: byte-string-literal.map-key -->

```maxon
function main() returns ExitCode
		let m = [b"hello": 1, b"world": 2]
		let v1 = try m.get(b"hello") otherwise 0
		let v2 = try m.get(b"world") otherwise 0
		print("{v1} {v2}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: byte-string-literal.top-level-map -->

```maxon
var m = [b"hello": 1, b"world": 2]

function main() returns ExitCode
		let v1 = try m.get(b"hello") otherwise 0
		let v2 = try m.get(b"world") otherwise 0
		print("{v1} {v2}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: byte-string-literal.top-level-map-struct -->

```maxon
typealias Integer = int(i64.min to i64.max)

type Info
		export var value as Integer

		static function create(value Integer) returns Self
			return Self{value: value}
		end 'create'
end 'Info'

var m = [b"hello": Info.create(10), b"world": Info.create(20)]

function main() returns ExitCode
		let v1 = try m.get(b"hello") otherwise Info.create(0)
		let v2 = try m.get(b"world") otherwise Info.create(0)
		print("{v1.value} {v2.value}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
10 20
```

<!-- test: byte-string-literal.field-access -->

```maxon
function main() returns ExitCode
		let len = b"test".count()
		return len
end 'main'
```
```exitcode
4
```

<!-- test: byte-string-literal.latin1-char -->

```maxon
function main() returns ExitCode
		let bytes = b"À"
		let b0 = try bytes.get(0) otherwise 0
		print("{bytes.count()} {b0}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
1 192
```

<!-- test: byte-string-literal.latin1-two -->

```maxon
function main() returns ExitCode
		let bytes = b"Àé"
		let b0 = try bytes.get(0) otherwise 0
		let b1 = try bytes.get(1) otherwise 0
		print("{bytes.count()} {b0} {b1}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
2 192 233
```

<!-- test: byte-string-literal.latin1-hex-equivalent -->

```maxon
function main() returns ExitCode
		let literal = b"À"
		let hex = b"\xc0"
		let a = try literal.get(0) otherwise 0
		let b = try hex.get(0) otherwise 1
		if a == b 'equal'
				print("{a}")
				return 0
		end 'equal'
		return 1
end 'main'
```
```exitcode
0
```
```stdout
192
```

<!-- test: byte-string-literal.latin1-mixed -->

```maxon
function main() returns ExitCode
		let bytes = b"AÀB"
		let b0 = try bytes.get(0) otherwise 0
		let b1 = try bytes.get(1) otherwise 0
		let b2 = try bytes.get(2) otherwise 0
		print("{bytes.count()} {b0} {b1} {b2}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
3 65 192 66
```
