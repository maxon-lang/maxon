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
var fh = f.hash()   // both halves of the bit pattern, folded: 300063655
```

**Notes:**
- `int.hash()` is the low 32 bits of the value (`self and 0xFFFFFFFF`)
- `float.hash()` folds the WHOLE 64-bit IEEE-754 pattern into 32 bits:
  `(bits xor (bits shr 32)) and 0xFFFFFFFF`. Both halves contribute, so floats that differ only in the
  high half — the sign, the exponent, the top of the mantissa — hash differently: `2.5` and `5.0` differ,
  and so do `-3.14` and `3.14`
- `0.0.hash()` and `(-0.0).hash()` both return `0`, because `0.0 == -0.0` and equal values must hash
  alike

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

<!-- test: int.hash.chained -->
`hash()` returns a `HashValue`, and a `HashValue` IS an int — so a hash of a hash is an ordinary chained
dispatch, and so are `.equals`/`.compare` on a binding that holds one. `HashValue` is the one ranged int
alias the COMPILER declares rather than the program, so it is in NO alias registry: the receiver classifier
must reduce it through the same name set `TypeResolution.resolveNamedType` erases to `integer`
(`isSynthesizedIntAliasName`) or it reads the name as nominal and rejects the second hop.
```maxon
function main() returns ExitCode
	let i = 42
	let once = i.hash()
	let twice = i.hash().hash()
	if once != twice 'chainDiffers'
		return 1
	end 'chainDiffers'
	if not once.equals(42) 'hashValueEquals'
		return 2
	end 'hashValueEquals'
	match once.compare(41) 'cmp'
		lessThan then return 3
		equalTo then return 4
		greaterThan then return 0
	end 'cmp'
end 'main'
```
```exitcode
0
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

<!-- test: float.hash.chained -->
`float.hash()` returns a `HashValue` exactly as `int.hash()` does, so a hash of a hash is an ordinary
chained dispatch that leaves the float domain after the first hop: the second `.hash()` dispatches
`int.hash`, whose low-32 mask is the identity on a value already inside `HashValue`'s range. The pinned
number is `3.14`'s IEEE-754 pattern `0x40091EB851EB851F` folded: the low half `0x51EB851F` xor the high
half `0x40091EB8` is `0x11E29BA7`, i.e. `300063655`.
```maxon
function main() returns ExitCode
	let f = 3.14
	let once = f.hash()
	let twice = f.hash().hash()
	if once != twice 'chainDiffers'
		return 1
	end 'chainDiffers'
	if once != 300063655 'pinned'
		return 2
	end 'pinned'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: float.hash.high-half-contributes -->
⭐ **THE HIGH HALF OF THE BIT PATTERN REACHES THE HASH.** `2.5` is `0x4004000000000000` and `5.0` is
`0x4014000000000000`: both low halves are ZERO, so a hash that kept only the low 32 bits answers `0` for
both — and for every float whose mantissa fits in its top 20 bits, which is every small integer-valued
or half-integer float a program is likely to key a map with. Folded, each hash is its high half:
`0x40040000` = `1074003968` and `0x40140000` = `1075052544`.
```maxon
function main() returns ExitCode
	let small = 2.5
	let large = 5.0
	if small.hash() == 0 'lowHalfOnly'
		return 1
	end 'lowHalfOnly'
	if small.hash() == large.hash() 'collide'
		return 2
	end 'collide'
	if small.hash() != 1074003968 'pinnedSmall'
		return 3
	end 'pinnedSmall'
	if large.hash() != 1075052544 'pinnedLarge'
		return 4
	end 'pinnedLarge'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: float.hash.equal-values-equal-hashes -->
The `Hashable` contract: two values that `equals` must hash alike. `3.0 / 2.0` is computed at run time
by a `divsd` and `1.5` is loaded from `.rdata`, so the two hashes are reached by different routes and
agree only because the bit patterns do.
```maxon
function main() returns ExitCode
	let a = 1.5
	let b = 3.0 / 2.0
	if a.hash() != b.hash() 'differ'
		return 1
	end 'differ'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: float.hash.negative -->
⭐ **A NEGATIVE FLOAT DOES NOT HASH TO ITS POSITIVE TWIN'S VALUE.** IEEE-754's sign is bit 63, in the
high half, and the fold brings the high half down onto the low one: `-3.14` is `0xC0091EB851EB851F`,
whose high half `0xC0091EB8` xor its low half `0x51EB851F` is `0x91E29BA7`, i.e. `2447547303` — `3.14`'s
`300063655` plus `2^31`, the sign landing on bit 31. Whether `shr` fills the vacated word with the sign or
with zeros does not move the answer, because the final mask discards that word.
```maxon
function main() returns ExitCode
	let pos = 3.14
	let neg = -3.14
	if neg.hash() == pos.hash() 'signLost'
		return 1
	end 'signLost'
	if neg.hash() != 2447547303 'pinned'
		return 2
	end 'pinned'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: float.hash.nan -->
⭐ **NaN IS DELIBERATELY NOT NORMALIZED — only `-0.0` is — and the reason is that `float.equals` is
plain IEEE.** `NaN.equals(NaN)` is FALSE, so no two `equals`-equal values can differ in hash however a
NaN hashes, and the `Hashable` contract is untouched. `stdlib/PrimitiveExtensions.maxon` folds the bits
and special-cases `-0.0` alone, and this case pins the fold so a later reader cannot "fix" the absence of
a NaN branch.

`inf - inf` yields the platform's default quiet NaN, and the platforms disagree on its SIGN: x64 produces
`0xFFF8000000000000`, arm64 `0x7FF8000000000000`, and wasm leaves the sign unspecified. The fold is
therefore `0xFFF80000` on one lane and `0x7FF80000` on another, and only bits 0-30 are the same
everywhere: `h and 0x7FFFFFFF` is `0x7FF80000` = `2146959360` on every lane — the all-ones exponent and
the quiet bit, reaching the hash from the high half. NaN is built by overflow rather than `0.0 / 0.0` for
`primitive-comparable`'s reason: a literal zero divisor is a compile error.
```maxon
function main() returns ExitCode
	let inf = 1.0e308 * 10.0
	let nan = inf - inf
	let h = nan.hash()
	if nan.equals(nan) 'ieeeEquals'
		return 1
	end 'ieeeEquals'
	if (h and 0x7FFFFFFF) != 2146959360 'pinned'
		return 2
	end 'pinned'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.float-type-argument-still-refused -->
⚠ **MAKING `float` CONFORM TO `Hashable` DOES NOT OPEN THE GENERIC DOOR, AND THIS CASE IS WHAT KEEPS
THE TWO APART.** `float.hash` ships as a DIRECT dispatch on a concrete value; the witness form is
unreachable because a float TYPE ARGUMENT is still E2062 — a type parameter is one opaque 8-byte
general-purpose slot under the compiler's dictionary-passing, and a float travels in a floating-point register.
Before this rung `where T is Hashable` at `float` was refused twice over (no conformance AND no type
argument); now E2062 is the only thing standing between a user and a witness slot that could not carry
its receiver, so the refusal is pinned here rather than left resting on `generic-types.md` alone.
```maxon
type Box uses T where T is Hashable
	export var value as T
	static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias FloatBox = Box with float
function main() returns ExitCode
	let b = FloatBox.create(1.5)
	return 0
end 'main'
```
```maxoncstderr
error E2062: <fragment>:8:31: Cannot use 'float' as a type argument: a float type argument is not supported yet. A type parameter is an opaque 8-byte general-purpose slot under the compiler's dictionary-passing, and a float value travels in a floating-point register, so it has no way through
```
