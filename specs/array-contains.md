---
feature: array-contains
status: experimental
keywords: [array, contains, equatable, bytearray]
category: collections
---

# Array Contains

## Documentation

`Array.contains(element)` checks whether an array contains a specific element using content equality (the `Equatable` interface). This must work correctly for nested array types like `ByteArrayArray` (`Array with ByteArray`), where element comparison requires calling `.equals()` rather than comparing pointers.

## Tests

<!-- test: bytearray-array-contains -->
Check that ByteArrayArray.contains() uses content equality, not pointer identity.
```maxon
typealias Byte = byte(0 to u8.max)
typealias ByteArray = Array with Byte
typealias ByteArrayArray = Array with ByteArray

function main() returns ExitCode
	var names = ByteArrayArray.create()
	names.push(b"hello")
	names.push(b"world")

	// Contains should match by content, not by pointer
	if names.contains(b"hello") 'found'
		print("found\n")
	end 'found' else 'notFound'
		print("not found\n")
	end 'notFound'

	if names.contains(b"missing") 'found2'
		print("found\n")
	end 'found2' else 'notFound2'
		print("not found\n")
	end 'notFound2'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
found
not found
```
