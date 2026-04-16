---
feature: primitive-hashable
status: stable
keywords: hash, equals, hashable, equatable, primitives
category: type-system
---
# Primitive Hashable and Equatable

## Documentation

Built-in numeric types (`int`, `float`, `byte`) implement the `Hashable` and `Equatable`
interfaces, allowing them to be used in hash-based collections like `Set` and `Map`.
Note: `bool` does not implement `Hashable` or `Equatable` because bool arrays use bit-packing.

## hash()

Returns an integer hash value for the primitive.

**Signatures:**
- `int.hash() -> int`
- `float.hash() -> int`
- `byte.hash() -> int`

**Example:**
```maxon
var x = 42
var h = x.hash()    // returns 42

var f = 3.14
var fh = f.hash()   // returns bit pattern as int
```

**Notes:**
- `0.0.hash()` and `(-0.0).hash()` return the same value
- Integer hash is identity function

## equals(other)

Compares two values for equality.

**Signatures:**
- `int.equals(other int) -> bool`
- `float.equals(other float) -> bool`
- `byte.equals(other byte) -> bool`

**Example:**
```maxon
var a = 42
var b = 42
if a.equals(b) 'check'
	print("equal\n")
end 'check'
```

**Notes:**
- Float comparison follows IEEE semantics: `NaN.equals(NaN)` returns false

## Tests

<!-- test: int.hash -->
```maxon
function main() returns ExitCode
	let i = 42
	let h = i.hash()
	return h
end 'main'
```
```exitcode
42
```

<!-- test: float.hash.nonzero -->
```maxon
function main() returns ExitCode
	let f = 3.14
	let h = f.hash()
	if h != 0 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: float.hash.zero-normalization -->
```maxon
function main() returns ExitCode
	let pos = 0.0
	let neg = -0.0
	let h1 = pos.hash()
	let h2 = neg.hash()
	if h1 == h2 'eq'
		return 1
	end 'eq'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: int.equals.same -->
```maxon
function main() returns ExitCode
	let a = 42
	let b = 42
	if a.equals(b) 'eq'
		return 1
	end 'eq'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: int.equals.different -->
```maxon
function main() returns ExitCode
	let a = 42
	let b = 17
	if a.equals(b) 'eq'
		return 1
	end 'eq'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: float.equals -->
```maxon
function main() returns ExitCode
	let a = 3.14
	let b = 3.14
	if a.equals(b) 'eq'
		return 1
	end 'eq'
	return 0
end 'main'
```
```exitcode
1
```
