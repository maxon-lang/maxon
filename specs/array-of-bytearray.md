---
feature: array-of-bytebuffer
status: experimental
keywords: [array, bytebuffer, nested, struct]
category: collections
---

# Array of ByteArray

## Documentation

Storing and retrieving ByteArray (`Array with Byte`) values from an outer Array.

## Tests

<!-- test: array-of-bytebuffer-push-get-count -->
Push a ByteArray into an outer Array, retrieve it, and call .count() on it.
```maxon
typealias Byte = byte(0 to u8.max)
typealias ByteArray = Array with Byte
typealias ByteArrayArray = Array with ByteArray

function main() returns ExitCode
	let outer = ByteArrayArray.create()
	let inner = ByteArray.create()
	inner.push(10)
	inner.push(20)
	inner.push(30)
	outer.push(inner)
	let retrieved = try outer.get(0) otherwise ByteArray.create()
	print("{retrieved.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```
