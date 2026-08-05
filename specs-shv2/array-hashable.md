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
	let arr = [10, 20, 30]
	let h = arr.hash()
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
	let a = [1, 2, 3]
	let b = [1, 2, 3]
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
	let a = [1, 2, 3]
	let b = [1, 2, 4]
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
	let a = [1, 2, 3]
	let b = [1, 2]
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
	try m.insert(key, value: 42) otherwise ignore
	var lookup = IntArr.create()
	lookup.push(1)
	lookup.push(2)
	lookup.push(3)
	let val = try m.get(lookup) otherwise 'notFound'
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
typealias ByteVal = int(0 to u8.max)
typealias ByteArr = Array with ByteVal

function main() returns ExitCode
	var arr = ByteArr.create()
	arr.push(65)
	arr.push(66)
	arr.push(67)
	let h = arr.hash()
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
typealias ByteVal = int(0 to u8.max)
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

### Array `==` operator dispatches to content equality

The `==` / `!=` operators on a generic instance whose element is `Equatable`
(here `Array with Int`, a `genericInstance`, not a plain struct) must dispatch
to `Array.equals` — a byte-by-byte content compare — exactly as the C# bootstrap
does. A prior self-hosted gap left the operators as a POINTER (identity) compare,
so two distinct-but-content-equal arrays wrongly reported `!=`. The two arrays
below are separate allocations with identical contents; identity compare would
return the wrong answer.

<!-- test: array-eq-operator-same-content -->
```maxon
function main() returns ExitCode
	let a = [1, 2, 3]
	let b = [1, 2, 3]
	if a == b 'eq'
		return 0
	end 'eq'
	return 1
end 'main'
```
```exitcode
0
```

### Array `==` operator with different content

<!-- test: array-eq-operator-different-content -->
```maxon
function main() returns ExitCode
	let a = [1, 2, 3]
	let b = [1, 2, 4]
	if a == b 'eq'
		return 0
	end 'eq'
	return 1
end 'main'
```
```exitcode
1
```

### Array `!=` operator on distinct-but-equal arrays is false

The `!=` rewrite (`not (a.equals(b))`) must return `false` when the contents
match — the negation of the content compare, never of a pointer compare.

<!-- test: array-ne-operator-equal-content -->
```maxon
function main() returns ExitCode
	let a = [7, 8, 9]
	let b = [7, 8, 9]
	if a != b 'ne'
		return 1
	end 'ne'
	return 0
end 'main'
```
```exitcode
0
```

### Byte array `==` operator against a fresh literal decode

Mirrors the compiler's own `methodName == "set".toByteArray()` predicate shape:
a `ByteArray` value compared with `==` against a freshly decoded string literal.
These are always distinct allocations, so only content equality answers correctly.

<!-- test: byte-array-eq-operator-fresh-literal -->
```maxon
function main() returns ExitCode
	let name = "set".toByteArray()
	let probe = "set".toByteArray()
	let other = "get".toByteArray()
	if name != probe 'shouldMatch'
		return 1
	end 'shouldMatch'
	if name == other 'shouldDiffer'
		return 2
	end 'shouldDiffer'
	return 0
end 'main'
```
```exitcode
0
```
