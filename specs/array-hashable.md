---
feature: array-hashable
status: stable
keywords: [array, hash, equals, hashable, equatable, map, key]
category: type-system
---

# Array Hashable and Equatable

## Documentation

Arrays conditionally implement `Hashable` and `Equatable` when their element type implements both interfaces. This enables arrays to be used as keys in `Map` and elements in `Set`.

### hash()

Returns a hash value computed from the raw bytes of the array's managed memory using the djb2 algorithm.

### equals(other)

Compares two arrays byte-by-byte. Arrays are equal if they have the same length and identical backing memory.

## Tests

### Array hash produces a value

<!-- test: array-hash-basic -->
```maxon
function main() returns ExitCode
	var arr = [10, 20, 30]
	var h = arr.hash()
	if h != 0 'nonzero'
		return 1
	end 'nonzero'
	return 0
end 'main'
```
```exitcode
1
```

### Array equals with same elements

<!-- test: array-equals-same -->
```maxon
function main() returns ExitCode
	var a = [1, 2, 3]
	var b = [1, 2, 3]
	if a.equals(b) 'eq'
		return 1
	end 'eq'
	return 0
end 'main'
```
```exitcode
1
```

### Array equals with different elements

<!-- test: array-equals-different -->
```maxon
function main() returns ExitCode
	var a = [1, 2, 3]
	var b = [1, 2, 4]
	if a.equals(b) 'eq'
		return 1
	end 'eq'
	return 0
end 'main'
```
```exitcode
0
```

### Array equals with different lengths

<!-- test: array-equals-different-length -->
```maxon
function main() returns ExitCode
	var a = [1, 2, 3]
	var b = [1, 2]
	if a.equals(b) 'eq'
		return 1
	end 'eq'
	return 0
end 'main'
```
```exitcode
0
```

### Int array as Map key

<!-- test: int-array-map-key -->
```maxon
typealias Val = int(i64.min to i64.max)
typealias IntArr = Array with Val
typealias IntArrMap = Map with (IntArr, Val)

function main() returns ExitCode
	var m = IntArrMap.create()
	var key = IntArr.create()
	key.push(1)
	key.push(2)
	key.push(3)
	try m.insert(key: key, value: 42) otherwise ignore
	var lookup = IntArr.create()
	lookup.push(1)
	lookup.push(2)
	lookup.push(3)
	let val = try m.get(key: lookup) otherwise 'notFound'
		return 0
	end 'notFound'
	return val
end 'main'
```
```exitcode
42
```

### Byte array hash

<!-- test: byte-array-hash -->
```maxon
typealias ByteVal = byte(0 to u8.max)
typealias ByteArr = Array with ByteVal

function main() returns ExitCode
	var arr = ByteArr.create()
	arr.push(65)
	arr.push(66)
	arr.push(67)
	var h = arr.hash()
	if h != 0 'nonzero'
		return 1
	end 'nonzero'
	return 0
end 'main'
```
```exitcode
1
```

### Byte array equals

<!-- test: byte-array-equals -->
```maxon
typealias ByteVal = byte(0 to u8.max)
typealias ByteArr = Array with ByteVal

function main() returns ExitCode
	var a = ByteArr.create()
	a.push(65)
	a.push(66)
	var b = ByteArr.create()
	b.push(65)
	b.push(66)
	if a.equals(b) 'eq'
		return 1
	end 'eq'
	return 0
end 'main'
```
```exitcode
1
```
