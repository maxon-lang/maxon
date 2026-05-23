---
feature: byte-string-literal
status: experimental
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

The byte string literal supports the same escape sequences as regular string literals (`\n`, `\t`, `\\`, `\0`, etc.).

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
		export var value Integer

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
