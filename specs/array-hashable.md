---
feature: array-hashable
status: stable
keywords: [array, hash, equals, hashable, equatable, map, key]
category: type-system
---

# Array Hashable and Equatable

## Documentation

Arrays conditionally implement `Hashable` and `Equatable` when their element type implements both interfaces. This enables arrays to be used as keys in `Map` and elements in `Set`. A `Map` key and a `Set` element are `immutable` slots, so an array built at run time enters one frozen: `m.insert(freeze buildKey(), value: 1)`. A lookup (`get`, `contains`) takes any array.

### hash()

An array whose element's equality is bitwise — an integer of any width, or a payload-free enum that declares
neither `hash` nor `equals` — hashes as one run of bytes over its storage:
a `Hasher` that folds `storage` with `combineBytes`, and the `HashValue` its `finalize()` answers. A `ByteArray`'s
storage is its bytes, so it answers the hash a `String` of the same bytes does, and an `int(i64.min to i64.max)` element is stored as its
eight bytes, little-endian. How many bytes a narrower integer element or an enum element occupies is not part of
the language, so its array's hash is defined by its properties alone: equal arrays hash equal, and a call made
directly, through a `Hashable` value and through a generic `T` all answer the same hash.

An array whose element's equality is not bitwise, or whose element type declares its own `hash` — a float
(`0.0 == -0.0`), a `String`, an interface value, a type that declares its own `hash` or `equals`, an enum
included — chains its elements' own `hash()` through `Hasher`: each element in order is one `combine(element.hash())`
of a `Hasher.create()`, and the array's hash is that hasher's `finalize()`.

Equal arrays hash equal within a process; the hash starts from the process seed, so its value differs from one
process to the next.

### equals(other)

Compares two arrays element by element, through each element's own `equals`. Arrays are equal if they have
the same length and the elements at each index are equal. `==` calls `equals`, and `!=` is its negation.

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

function threeKey() returns IntArr
	let key = IntArr.create(0)
	key.push(1)
	key.push(2)
	key.push(3)
	return key
end 'threeKey'

function main() returns ExitCode
	let m = IntArrMap.create()
	try m.insert(freeze threeKey(), value: 42) otherwise ignore
	let lookup = IntArr.create(0)
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
	let arr = ByteArr.create(0)
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
	let a = ByteArr.create(0)
	a.push(65)
	a.push(66)
	let b = ByteArr.create(0)
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
to `Array.equals` — an element-by-element content compare. The two arrays
below are separate allocations with identical contents, so they are equal; an
identity compare would answer `!=`.

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

### String array equality compares element content

Two `Array with String` values whose elements are separate allocations holding equal text are equal:
`equals` compares element by element through `String.equals`, never the references the buffer holds.

<!-- test: string-array-equals-compares-element-content -->
```maxon
typealias Val = int(i64.min to i64.max)
typealias StrArr = Array with String

function numbered(first Val, count Val) returns StrArr
	let arr = StrArr.create(0)
	for i in first upto first + count 'fill'
		arr.push("p{i}")
	end 'fill'
	return arr
end 'numbered'

function main() returns ExitCode
	let built = numbered(1, count: 3)
	print("{["p1", "p2", "p3"].equals(built)}\n")
	print("{["p1", "p2", "p4"].equals(built)}\n")
	print("{["p1", "p2"].equals(built)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
false
false
```

### Equal string arrays hash equal

<!-- test: string-array-hash-follows-element-content -->
```maxon
typealias Val = int(i64.min to i64.max)
typealias StrArr = Array with String

function numbered(first Val, count Val) returns StrArr
	let arr = StrArr.create(0)
	for i in first upto first + count 'fill'
		arr.push("p{i}")
	end 'fill'
	return arr
end 'numbered'

function main() returns ExitCode
	let literal = ["p1", "p2", "p3"]
	let built = numbered(1, count: 3)
	print("{literal.hash() == built.hash()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
```

### String array as Map key

<!-- test: string-array-map-key -->
```maxon
typealias Val = int(i64.min to i64.max)
typealias StrArr = Array with String
typealias StrArrMap = Map with (StrArr, Val)

function numbered(first Val, count Val) returns StrArr
	let arr = StrArr.create(0)
	for i in first upto first + count 'fill'
		arr.push("p{i}")
	end 'fill'
	return arr
end 'numbered'

function main() returns ExitCode
	let m = StrArrMap.create()
	try m.insert(freeze numbered(1, count: 3), value: 42) otherwise ignore
	let found = try m.get(numbered(1, count: 3)) otherwise 'notFound'
		print("not found\n")
		return 1
	end 'notFound'
	print("found {found}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
found 42
```

### String array `==` and `!=` compare element content

<!-- test: string-array-operator-equality-compares-element-content -->
```maxon
typealias Val = int(i64.min to i64.max)
typealias StrArr = Array with String

function numbered(first Val, count Val) returns StrArr
	let arr = StrArr.create(0)
	for i in first upto first + count 'fill'
		arr.push("p{i}")
	end 'fill'
	return arr
end 'numbered'

function main() returns ExitCode
	let built = numbered(1, count: 3)
	print("{["p1", "p2", "p3"] == built}\n")
	print("{["p1", "p2", "p3"] != built}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
false
```

### Array keys of two element types in one program

<!-- test: array-keys-of-two-element-types-in-one-program -->
```maxon
typealias Val = int(i64.min to i64.max)
typealias IntArr = Array with Val
typealias StrArr = Array with String
typealias IntArrMap = Map with (IntArr, Val)
typealias StrArrMap = Map with (StrArr, Val)

function numbered(first Val, count Val) returns StrArr
	let arr = StrArr.create(0)
	for i in first upto first + count 'fill'
		arr.push("p{i}")
	end 'fill'
	return arr
end 'numbered'

function seven() returns IntArr
	let xs = IntArr.create(0)
	xs.push(7)
	return xs
end 'seven'

function main() returns ExitCode
	let ints = IntArrMap.create()
	try ints.insert(freeze seven(), value: 1) otherwise ignore
	let strings = StrArrMap.create()
	try strings.insert(freeze numbered(1, count: 3), value: 2) otherwise ignore
	let intProbe = IntArr.create(0)
	intProbe.push(7)
	let a = try ints.get(intProbe) otherwise 0
	let b = try strings.get(numbered(1, count: 3)) otherwise 0
	print("{a} {b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

### A generic body hashes an array as concrete code does

<!-- test: a-generic-body-hashes-an-array-as-concrete-code-does -->
```maxon
typealias Val = int(i64.min to i64.max)
type Holder uses T where T is Hashable and Equatable
	export typealias Items = Array with T
	var v as T
	static function create(v T) returns Self
		return Self{v: v}
	end 'create'
	function digest(items Items) returns HashValue
		return items.hash()
	end 'digest'
end 'Holder'
typealias H = Holder with Val
typealias ValArray = Array with Val
function main() returns ExitCode
	let holder = H.create(1)
	let items = ValArray.create(0)
	items.push(41)
	items.push(1099511627817)
	items.push(-1099511627817)
	items.push(-5)
	print("{holder.digest(items) == items.hash()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
```

### A generic body compares arrays with the operator

<!-- test: a-generic-body-compares-arrays-with-the-operator -->
```maxon
typealias Val = int(i64.min to i64.max)

type Holder uses T where T is Hashable and Equatable
	export typealias Items = Array with T

	var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	function same(a Items, b Items) returns bool
		return a == b
	end 'same'
end 'Holder'

typealias H = Holder with Val
typealias ValArray = Array with Val

function main() returns ExitCode
	let holder = H.create(1)
	let first = ValArray.create(0)
	first.push(41)
	first.push(42)
	let second = ValArray.create(0)
	second.push(41)
	second.push(42)
	let third = ValArray.create(0)
	third.push(41)
	third.push(43)
	print("{holder.same(first, b: second)}\n")
	print("{holder.same(first, b: third)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
false
```

### A generic body compares a field array with the operator

<!-- test: a-generic-body-compares-a-field-array-with-the-operator -->
```maxon
typealias Val = int(i64.min to i64.max)

type Holder uses T where T is Hashable and Equatable
	export typealias Items = Array with T

	var items as Items

	static function create(items Items) returns Self
		return Self{items: items}
	end 'create'

	function matches(b Items) returns bool
		return items == b
	end 'matches'
end 'Holder'

typealias H = Holder with Val
typealias ValArray = Array with Val

function main() returns ExitCode
	let held = ValArray.create(0)
	held.push(41)
	held.push(42)
	let holder = H.create(held)
	let equal = ValArray.create(0)
	equal.push(41)
	equal.push(42)
	let unequal = ValArray.create(0)
	unequal.push(41)
	unequal.push(43)
	print("{holder.matches(equal)}\n")
	print("{holder.matches(unequal)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
false
```

### A set of type-parameter arrays inside a generic

<!-- test: a-set-of-type-parameter-arrays-inside-a-generic -->
```maxon
typealias Val = int(i64.min to i64.max)

type Holder uses T where T is Hashable and Equatable
	export typealias Items = Array with T
	export typealias Seen = Set with Items

	var seen as Seen

	static function create() returns Self
		return Self{seen: Seen.create()}
	end 'create'

	function note(i Items)
		seen.insert(i)
	end 'note'

	function seenCount() returns MemberCount
		return seen.count()
	end 'seenCount'
end 'Holder'

typealias H = Holder with Val
typealias ValArray = Array with Val

function numbers() returns ValArray
	let xs = ValArray.create(0)
	xs.push(41)
	xs.push(42)
	return xs
end 'numbers'

function main() returns ExitCode
	let holder = H.create()
	holder.note(freeze numbers())
	holder.note(freeze numbers())
	print("{holder.seenCount()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

### A generic body compares arrays of a parametric instance

<!-- test: a-generic-body-compares-arrays-of-a-parametric-instance -->
```maxon
typealias Val = int(i64.min to i64.max)
type Tag uses T implements Hashable, Equatable
	export var n as Val
	static function create(n Val) returns Self
		return Self{n: n}
	end 'create'
	function hash() returns HashValue
		return n
	end 'hash'
	function equals(other Self) returns bool
		return n == other.n
	end 'equals'
end 'Tag'
type Holder uses T
	export typealias Tagged = Tag with T
	export typealias Tags = Array with Tagged
	var tags as Tags
	static function create(tags Tags) returns Self
		return Self{tags: tags}
	end 'create'
	function sameAs(other Self) returns bool
		return tags == other.tags
	end 'sameAs'
end 'Holder'
typealias IntTag = Tag with Val
typealias IntTags = Array with IntTag
typealias H = Holder with Val
function main() returns ExitCode
	let a = IntTags.create(0)
	a.push(IntTag.create(7))
	let b = IntTags.create(0)
	b.push(IntTag.create(7))
	print("{H.create(a).sameAs(H.create(b))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
```

### A generic body hashes arrays of a parametric instance

<!-- test: a-generic-body-hashes-arrays-of-a-parametric-instance -->
```maxon
typealias Val = int(i64.min to i64.max)
type Tag uses T implements Hashable, Equatable
	export var n as Val
	static function create(n Val) returns Self
		return Self{n: n}
	end 'create'
	function hash() returns HashValue
		return n
	end 'hash'
	function equals(other Self) returns bool
		return n == other.n
	end 'equals'
end 'Tag'
type Holder uses T
	export typealias Tagged = Tag with T
	export typealias Tags = Array with Tagged
	var tags as Tags
	static function create(tags Tags) returns Self
		return Self{tags: tags}
	end 'create'
	function hashOf() returns HashValue
		return tags.hash()
	end 'hashOf'
end 'Holder'
typealias IntTag = Tag with Val
typealias IntTags = Array with IntTag
typealias H = Holder with Val
function main() returns ExitCode
	let a = IntTags.create(0)
	a.push(IntTag.create(7))
	let expected = a.hash()
	print("{H.create(a).hashOf() == expected}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
```

### Every integer element width hashes alike through every route

<!-- test: every-integer-element-width-hashes-alike-through-every-route -->
An integer array of every element width answers one hash called directly, through a `Hashable` field and
from a generic body; a separately built equal array hashes equal, and one element changed changes the hash.
```maxon
typealias Wide = int(i64.min to i64.max)
typealias Word = int(0 to u16.max)
typealias Dword = int(0 to u32.max)
typealias WideArray = Array with Wide
typealias WordArray = Array with Word
typealias DwordArray = Array with Dword

type Holder
	let key as Hashable

	static function create(key Hashable) returns Self
		return Self{key: key}
	end 'create'
end 'Holder'

type Route uses T where T is Hashable and Equatable
	typealias Items = Array with T

	static function hashOf(items Items) returns HashValue
		return items.hash()
	end 'hashOf'
end 'Route'

typealias WideRoute = Route with Wide
typealias WordRoute = Route with Word
typealias DwordRoute = Route with Dword
typealias ByteRoute = Route with Byte

function verdict(direct HashValue, existential HashValue, generic HashValue, equal HashValue, changed HashValue) returns String
	return "through Hashable {direct == existential} generic {direct == generic} equal {direct == equal} changed {direct != changed}"
end 'verdict'

function main() returns ExitCode
	let wide = WideArray from [-3, 7, -9000000000000, 9000000000000]
	let wideEqual = WideArray from [-3, 7, -9000000000000, 9000000000000]
	let wideChanged = WideArray from [-3, 7, -9000000000000, 9000000000001]
	print("wide: {verdict(wide.hash(), existential: Holder.create(wide).key.hash(), generic: WideRoute.hashOf(wide), equal: wideEqual.hash(), changed: wideChanged.hash())}\n")

	let words = WordArray from [65535, 1, 0]
	let wordsEqual = WordArray from [65535, 1, 0]
	let wordsChanged = WordArray from [65535, 1, 256]
	print("word: {verdict(words.hash(), existential: Holder.create(words).key.hash(), generic: WordRoute.hashOf(words), equal: wordsEqual.hash(), changed: wordsChanged.hash())}\n")

	let dwords = DwordArray from [4294967295, 2, 0]
	let dwordsEqual = DwordArray from [4294967295, 2, 0]
	let dwordsChanged = DwordArray from [4294967295, 2, 65536]
	print("dword: {verdict(dwords.hash(), existential: Holder.create(dwords).key.hash(), generic: DwordRoute.hashOf(dwords), equal: dwordsEqual.hash(), changed: dwordsChanged.hash())}\n")

	let bytes = ByteArray from [0, 200, 255]
	let bytesEqual = ByteArray from [0, 200, 255]
	let bytesChanged = ByteArray from [0, 201, 255]
	print("byte: {verdict(bytes.hash(), existential: Holder.create(bytes).key.hash(), generic: ByteRoute.hashOf(bytes), equal: bytesEqual.hash(), changed: bytesChanged.hash())}\n")

	let empty = WideArray.create(0)
	print("empty: {verdict(empty.hash(), existential: Holder.create(empty).key.hash(), generic: WideRoute.hashOf(empty), equal: WideArray.create(0).hash(), changed: wide.hash())}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
wide: through Hashable true generic true equal true changed true
word: through Hashable true generic true equal true changed true
dword: through Hashable true generic true equal true changed true
byte: through Hashable true generic true equal true changed true
empty: through Hashable true generic true equal true changed true
```

### A set keyed by arrays of a signed narrow alias finds equal keys

<!-- test: a-set-keyed-by-arrays-of-a-signed-narrow-alias-finds-equal-keys -->
```maxon
typealias Signed = int(-100 to 100)
typealias SignedArray = Array with Signed
typealias SignedArraySet = Set with SignedArray

function signedKey() returns SignedArray
	let key = SignedArray.create(0)
	key.push(-100)
	key.push(5)
	key.push(100)
	return key
end 'signedKey'

function main() returns ExitCode
	let key = SignedArray.create(0)
	key.push(-100)
	key.push(5)
	key.push(100)
	let probe = SignedArray.create(0)
	probe.push(-100)
	probe.push(5)
	probe.push(100)
	let other = SignedArray.create(0)
	other.push(-100)
	other.push(6)
	other.push(100)
	let sameHash = key.hash() == probe.hash()
	let seen = SignedArraySet.create()
	seen.insert(freeze signedKey())
	print("{seen.contains(probe)}\n")
	print("{seen.contains(other)}\n")
	print("{sameHash}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
false
true
```

### A user `Byte` declared as a float compares and hashes element-wise

<!-- test: a-user-byte-declared-as-a-float-compares-element-wise -->
An element type named `Byte` that is a float is not a byte: the array compares element by element and hashes
as the chain of its elements' hashes, never as raw bytes.
```maxon
typealias Byte = float(-1.0 to 1.0)
typealias Bs = Array with Byte

function fold(values Bs) returns HashValue
	let hasher = Hasher.create()

	for item in values 'eachElement'
		hasher.combine(item.hash() as HashDigest)
	end 'eachElement'

	return hasher.finalize()
end 'fold'

function main() returns ExitCode
	let a = Bs.create(0)
	a.push(0.0)
	let b = Bs.create(0)
	b.push(-0.0)
	print("{a == b} {a.equals(b)}\n")
	let c = Bs.create(0)
	c.push(0.5)
	print("{c.hash() == fold(c)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true true
true
```

### A float array compares and searches by its values

<!-- test: a-float-array-compares-and-searches-by-its-values -->
```maxon
typealias Reals = Array with FullRangeFloat

function main() returns ExitCode
	let a = Reals.create(0)
	a.push(1.5)
	a.push(2.5)
	let b = Reals.create(0)
	b.push(1.5)
	b.push(2.5)
	let c = Reals.create(0)
	c.push(1.5)
	c.push(3.5)
	print("{a == b} {a == c} {a.equals(c)}\n")
	print("{a.contains(2.5)} {a.contains(7.5)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true false false
true false
```

### A byte array hashes as its bytes

<!-- test: a-byte-array-hashes-as-its-bytes -->
A `ByteArray` is hashed as one run of bytes, not element by element: its hash is
a `Hasher` that folds `bytes` with `combineBytes`, which is also the hash of a `String` holding the same
bytes. Every length on either side of the hash's block boundaries, the empty array included.
```maxon
typealias LengthList = Array with Count

function bytesOf(length Count) returns ByteArray
	let bytes = ByteArray.create(0)

	for index in 0 upto length 'eachByte'
		let mixed = ((index + length * 977 + 1) * 2654435761 + 12345) mod 4294967296
		bytes.push((0x40 + ((mixed shr 11) and 0x3F)) as Byte)
	end 'eachByte'

	return bytes
end 'bytesOf'

function folded(bytes ByteArray) returns HashValue
	let hasher = Hasher.create()
	hasher.combineBytes(bytes)
	return hasher.finalize()
end 'folded'

function main() returns ExitCode
	let lengths = LengthList from [0, 1, 15, 16, 17, 31, 32, 33, 63, 64, 65, 127, 128, 129, 300]

	for length in lengths 'eachLength'
		let bytes = bytesOf(length)
		print("{length} as bytes: {bytes.hash() == folded(bytes)} as its string: {bytes.hash() == String.from(bytes).hash()}\n")
	end 'eachLength'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 as bytes: true as its string: true
1 as bytes: true as its string: true
15 as bytes: true as its string: true
16 as bytes: true as its string: true
17 as bytes: true as its string: true
31 as bytes: true as its string: true
32 as bytes: true as its string: true
33 as bytes: true as its string: true
63 as bytes: true as its string: true
64 as bytes: true as its string: true
65 as bytes: true as its string: true
127 as bytes: true as its string: true
128 as bytes: true as its string: true
129 as bytes: true as its string: true
300 as bytes: true as its string: true
```

### An int array hashes as the bytes of its storage

<!-- test: an-int-array-hashes-as-the-bytes-of-its-storage -->
An array of `int(i64.min to i64.max)` hashes as one run over its storage, each element its eight bytes
little-endian: a `Hasher` that folds `storage` with `combineBytes`, which is the hash of a
`ByteArray` holding those bytes. The run is ordered, so `[1, 2]` and `[2, 1]` differ, and an empty array hashes
as an empty `ByteArray`. A bare literal array hashes as the typed array of its values.
```maxon
typealias Wide = int(i64.min to i64.max)
typealias WideArray = Array with Wide

function storageOf(values WideArray) returns ByteArray
	let bytes = ByteArray.create(0)

	for item in values 'eachElement'
		for index in 0 upto 8 'eachByte'
			bytes.push(((item shr (index * 8)) and 0xFF) as Byte)
		end 'eachByte'
	end 'eachElement'

	return bytes
end 'storageOf'

function storageHash(values WideArray) returns HashValue
	let hasher = Hasher.create()
	hasher.combineBytes(storageOf(values))
	return hasher.finalize()
end 'storageHash'

function main() returns ExitCode
	let empty = WideArray.create(0)
	let one = WideArray from [7]
	let three = WideArray from [10, 20, 30]
	let wide = WideArray from [-3, 9000000000000, -9000000000000, 0, -1]
	let forward = WideArray from [1, 2]
	let backward = WideArray from [2, 1]

	print("empty: {empty.hash() == storageHash(empty)}\n")
	print("empty is an empty byte array: {empty.hash() == ByteArray.create(0).hash()}\n")
	print("one: {one.hash() == storageHash(one)}\n")
	print("three: {three.hash() == storageHash(three)}\n")
	print("wide: {wide.hash() == storageHash(wide)}\n")
	print("as a byte array: {wide.hash() == storageOf(wide).hash()}\n")
	print("literal: {[10, 20, 30].hash() == storageHash(three)}\n")
	print("ordered: {forward.hash() != backward.hash()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
empty: true
empty is an empty byte array: true
one: true
three: true
wide: true
as a byte array: true
literal: true
ordered: true
```

### One flipped bit of one element moves about half of the 32 hash bits

<!-- test: hash-avalanches -->
Changing one element by one bit moves the array's hash by at least 8 of its 32 bits on average, for an
`int` array (each of the low 31 bits of each of eight elements) and a byte array (each bit of each of
sixteen bytes). A multiply-and-add fold moves one bit for a change in the last element and fails here. Over
~400 changes two 32-bit hashes collide by chance about once in 10^7 runs, so one unmoved hash is allowed and
two are an element the hash did not read.
```maxon
typealias Wide = int(i64.min to i64.max)
typealias WideArray = Array with Wide

function bitsSet(pattern HashValue) returns Count
	var rest = pattern
	var count = 0 as Count

	while rest != 0 'eachBit'
		count = count + ((rest and 1) as Count)
		rest = rest shr 1
	end 'eachBit'

	return count
end 'bitsSet'

function verdict(moved Count, flips Count, unmoved Count) returns String
	return "mean>=8: {moved >= flips * 8} at most one 0: {unmoved <= 1}"
end 'verdict'

function intVerdict() returns String
	let values = WideArray.create(0)

	for index in 0 upto 8 'eachElement'
		values.push(((index * 2654435761 + 12345) mod 2147483648) as Wide)
	end 'eachElement'

	let masks = WideArray.create(0)
	var mask = 1 as Wide

	for _ in 0 upto 31 'eachBit'
		masks.push(mask)
		mask = mask * 2
	end 'eachBit'

	let hash = values.hash()
	var moved = 0 as Count
	var flips = 0 as Count
	var unmoved = 0 as Count

	for index in 0 upto values.count() 'eachIndex'
		let original = try values.get(index) otherwise panic("the index is inside the array")

		for bit in masks 'eachMask'
			let changed = values.clone()
			try changed.set(index, value: original xor bit) otherwise panic("the index is inside the array")
			let distance = bitsSet(hash xor changed.hash())
			moved = moved + distance
			flips = flips + 1

			if distance == 0 'unchanged'
				unmoved = unmoved + 1
			end 'unchanged'
		end 'eachMask'
	end 'eachIndex'

	return verdict(moved, flips: flips, unmoved: unmoved)
end 'intVerdict'

function byteVerdict() returns String
	let bytes = ByteArray.create(0)

	for index in 0 upto 16 'eachByte'
		bytes.push((((index * 2654435761 + 12345) mod 4294967296) shr 11 and 0xFF) as Byte)
	end 'eachByte'

	let masks = ByteArray from [1, 2, 4, 8, 16, 32, 64, 128]
	let hash = bytes.hash()
	var moved = 0 as Count
	var flips = 0 as Count
	var unmoved = 0 as Count

	for index in 0 upto bytes.count() 'eachIndex'
		let original = try bytes.get(index) otherwise panic("the index is inside the array")

		for bit in masks 'eachMask'
			let changed = bytes.clone()
			try changed.set(index, value: original xor bit) otherwise panic("the index is inside the array")
			let distance = bitsSet(hash xor changed.hash())
			moved = moved + distance
			flips = flips + 1

			if distance == 0 'unchanged'
				unmoved = unmoved + 1
			end 'unchanged'
		end 'eachMask'
	end 'eachIndex'

	return verdict(moved, flips: flips, unmoved: unmoved)
end 'byteVerdict'

function main() returns ExitCode
	print("int {intVerdict()}\n")
	print("byte {byteVerdict()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
int mean>=8: true at most one 0: true
byte mean>=8: true at most one 0: true
```

### An int array whose hash only a witness table reaches links and hashes

<!-- test: an-int-array-hashed-only-through-a-witness-table-links -->
An `int` array stored in a `Hashable` field is hashed through the field's witness table, and nothing else in
the program hashes anything. The program links, equal arrays hash equal through the field, and arrays that
differ in one element hash differently.
```maxon
typealias Wide = int(i64.min to i64.max)
typealias WideArray = Array with Wide

type Holder
	let key as Hashable

	static function create(key Hashable) returns Self
		return Self{key: key}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let first = Holder.create(WideArray from [10, -20, 9000000000000])
	let same = Holder.create(WideArray from [10, -20, 9000000000000])
	let other = Holder.create(WideArray from [10, -20, 9000000000001])

	print("equal arrays: {first.key.hash() == same.key.hash()}\n")
	print("different arrays: {first.key.hash() != other.key.hash()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
equal arrays: true
different arrays: true
```

### A narrow int array hashes alike through every route

<!-- test: a-narrow-int-array-hashes-alike-through-every-route -->
An array whose element is one byte wide but is not `Byte` — an `int(0 to 100)`, a signed `int(-128 to 127)` —
answers one hash called directly, through a `Hashable` field and from a generic body; a separately built equal
array hashes equal, and one element changed changes the hash.
```maxon
typealias Percent = int(0 to 100)
typealias Small = int(-128 to 127)
typealias PercentArray = Array with Percent
typealias SmallArray = Array with Small

type Holder
	let key as Hashable

	static function create(key Hashable) returns Self
		return Self{key: key}
	end 'create'
end 'Holder'

type Route uses T where T is Hashable and Equatable
	typealias Items = Array with T

	static function hashOf(items Items) returns HashValue
		return items.hash()
	end 'hashOf'
end 'Route'

typealias PercentRoute = Route with Percent
typealias SmallRoute = Route with Small

function verdict(direct HashValue, existential HashValue, generic HashValue, equal HashValue, changed HashValue) returns String
	return "through Hashable {direct == existential} generic {direct == generic} equal {direct == equal} changed {direct != changed}"
end 'verdict'

function main() returns ExitCode
	let percents = PercentArray from [0, 7, 100, 42]
	let percentsEqual = PercentArray from [0, 7, 100, 42]
	let percentsChanged = PercentArray from [0, 7, 100, 43]
	print("percent: {verdict(percents.hash(), existential: Holder.create(percents).key.hash(), generic: PercentRoute.hashOf(percents), equal: percentsEqual.hash(), changed: percentsChanged.hash())}\n")

	let smalls = SmallArray from [-128, 0, 5, 127]
	let smallsEqual = SmallArray from [-128, 0, 5, 127]
	let smallsChanged = SmallArray from [-127, 0, 5, 127]
	print("small: {verdict(smalls.hash(), existential: Holder.create(smalls).key.hash(), generic: SmallRoute.hashOf(smalls), equal: smallsEqual.hash(), changed: smallsChanged.hash())}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
percent: through Hashable true generic true equal true changed true
small: through Hashable true generic true equal true changed true
```

### A payload-free enum array hashes alike through every route

<!-- test: a-payload-free-enum-array-hashes-alike-through-every-route -->
A payload-free enum's equality is bitwise, so its array hashes as one run over its storage, like an integer
array: called directly, through a `Hashable` field and from a generic body it answers one hash; a separately
built equal array hashes equal, and one element changed changes the hash.
```maxon
enum Suit
	clubs
	diamonds
	hearts
	spades
end 'Suit'

typealias SuitArray = Array with Suit

type Holder
	let key as Hashable

	static function create(key Hashable) returns Self
		return Self{key: key}
	end 'create'
end 'Holder'

type Route uses T where T is Hashable and Equatable
	typealias Items = Array with T

	static function hashOf(items Items) returns HashValue
		return items.hash()
	end 'hashOf'
end 'Route'

typealias SuitRoute = Route with Suit

function main() returns ExitCode
	let suits = SuitArray from [Suit.spades, Suit.clubs, Suit.hearts, Suit.diamonds]
	let suitsEqual = SuitArray from [Suit.spades, Suit.clubs, Suit.hearts, Suit.diamonds]
	let suitsChanged = SuitArray from [Suit.spades, Suit.clubs, Suit.hearts, Suit.hearts]
	let direct = suits.hash()

	print("through Hashable: {direct == Holder.create(suits).key.hash()}\n")
	print("generic: {direct == SuitRoute.hashOf(suits)}\n")
	print("equal: {suits == suitsEqual} {direct == suitsEqual.hash()}\n")
	print("changed: {suits != suitsChanged} {direct != suitsChanged.hash()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
through Hashable: true
generic: true
equal: true true
changed: true true
```

### An enum that declares its own hash chains it

<!-- test: an-enum-array-chains-the-enum-s-own-hash -->
An enum that declares its own `hash()` is hashed by it, so its array chains each element's `hash()` from
a `Hasher.create()`, called directly and through a `Hashable` field alike. Two
cases this `hash()` maps to one value give arrays that differ and hash equal.
```maxon
enum Parity implements Hashable
	zero
	one
	two
	three

	function hash() returns HashValue
		return match self 'parity'
			zero gives 0
			one gives 1
			two gives 0
			three gives 1
		end 'parity'
	end 'hash'
end 'Parity'

typealias ParityArray = Array with Parity

type Holder
	let key as Hashable

	static function create(key Hashable) returns Self
		return Self{key: key}
	end 'create'
end 'Holder'

function chained(values ParityArray) returns HashValue
	let hasher = Hasher.create()

	for item in values 'eachElement'
		hasher.combine(item.hash() as HashDigest)
	end 'eachElement'

	return hasher.finalize()
end 'chained'

function main() returns ExitCode
	let values = ParityArray from [Parity.three, Parity.zero, Parity.two]
	let sameParity = ParityArray from [Parity.one, Parity.two, Parity.zero]

	print("direct: {values.hash() == chained(values)}\n")
	print("through Hashable: {Holder.create(values).key.hash() == chained(values)}\n")
	print("own hash decides: {values != sameParity} {values.hash() == sameParity.hash()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
direct: true
through Hashable: true
own hash decides: true true
```

### A float array hashes as its element chain

<!-- test: a-float-array-hashes-as-its-element-chain -->
A float's equality is not bitwise (`0.0 == -0.0`), so a float array hashes as the chain
`hasher.combine(element.hash())` over a `Hasher.create()`, finalized —
called directly and through a `Hashable` field alike — and arrays holding `0.0` and `-0.0` are equal and hash
equal.
```maxon
typealias Reals = Array with FullRangeFloat

type Holder
	let key as Hashable

	static function create(key Hashable) returns Self
		return Self{key: key}
	end 'create'
end 'Holder'

function chained(values Reals) returns HashValue
	let hasher = Hasher.create()

	for item in values 'eachElement'
		hasher.combine(item.hash() as HashDigest)
	end 'eachElement'

	return hasher.finalize()
end 'chained'

function main() returns ExitCode
	let reals = Reals from [1.5, -2.25, 0.0, 1000000.125]
	let empty = Reals.create(0)
	let positive = Reals from [1.5, 0.0]
	let negative = Reals.create(0)
	negative.push(1.5)
	negative.push(-0.0)

	print("direct: {reals.hash() == chained(reals)}\n")
	print("through Hashable: {Holder.create(reals).key.hash() == chained(reals)}\n")
	print("empty: {empty.hash() == chained(empty)}\n")
	print("signed zeros: {positive == negative} {positive.hash() == negative.hash()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
direct: true
through Hashable: true
empty: true
signed zeros: true true
```

<!-- test: error.elements-hash-as-storage-needs-a-hashable-constraint -->
<!-- tier: stdlib -->
`__Builtins.elementsHashAsStorage()` reads the witness table of the enclosing type's `where … is Hashable`
constraint, so a type that carries none is refused at the call.
```maxon
type Box uses Element
	var item as Element

	static function create(item Element) returns Self
		return Self{item: item}
	end 'create'

	function storageHashed() returns bool
		return __Builtins.elementsHashAsStorage()
	end 'storageHashed'
end 'Box'

function main() returns ExitCode
	print("{Box.create("a").storageHashed()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:21: Unsupported: '__Builtins.elementsHashAsStorage' reads the witness table of the enclosing type's `where … is Hashable` constraint, and the function calling it carries none — call it from an instance method of a type or extension constrained to a Hashable element
```

<!-- test: error.elements-hash-as-storage-names-no-parameter -->
<!-- tier: stdlib -->
`__Builtins.elementsHashAsStorage()` takes no type parameter, so a type carrying two `Hashable` constraints
leaves it no one constraint to read and is refused at the call.
```maxon
type Pair uses First, Second where First is Hashable, Second is Hashable
	var first as First
	var second as Second

	static function create(first First, second Second) returns Self
		return Self{first: first, second: second}
	end 'create'

	function storageHashed() returns bool
		return __Builtins.elementsHashAsStorage()
	end 'storageHashed'
end 'Pair'

function main() returns ExitCode
	print("{Pair.create("a", second: "b").storageHashed()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:11:21: Unsupported: '__Builtins.elementsHashAsStorage' takes no type parameter, so it reads the one `where … is Hashable` constraint the enclosing type or extension carries, and the function calling it carries several — call it from a type or extension constrained to exactly one Hashable type parameter
```

<!-- test: elements-hash-as-storage-reads-a-constraint-that-extends-hashable -->
<!-- tier: stdlib -->
A `where … is` constraint naming an interface that extends `Hashable` is a `Hashable` constraint, so
`__Builtins.elementsHashAsStorage()` reads its witness table: an element type that declares its own `hash()`
does not hash as its storage.
```maxon
interface KeyLike extends Hashable
end 'KeyLike'

type Key implements KeyLike, Equatable
	var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'

	function hash() returns HashValue
		return self.name.hash()
	end 'hash'

	function equals(other Key) returns bool
		return self.name == other.name
	end 'equals'
end 'Key'

type Box uses Element where Element is KeyLike
	var item as Element

	static function create(item Element) returns Self
		return Self{item: item}
	end 'create'

	function storageHashed() returns bool
		return __Builtins.elementsHashAsStorage()
	end 'storageHashed'
end 'Box'

function main() returns ExitCode
	print("{Box.create(Key.create("a")).storageHashed()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
false
```

<!-- test: elements-hash-as-storage-reads-one-parameter-under-two-hashable-constraints -->
<!-- tier: stdlib -->
`__Builtins.elementsHashAsStorage()` counts the type parameters its constraints name, so one type parameter
constrained to `Hashable` and to an interface that extends it leaves one constraint to read.
```maxon
interface KeyLike extends Hashable
end 'KeyLike'

type Key implements KeyLike, Equatable
	var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'

	function hash() returns HashValue
		return self.name.hash()
	end 'hash'

	function equals(other Key) returns bool
		return self.name == other.name
	end 'equals'
end 'Key'

type Box uses Element where Element is Hashable, Element is KeyLike
	var item as Element

	static function create(item Element) returns Self
		return Self{item: item}
	end 'create'

	function storageHashed() returns bool
		return __Builtins.elementsHashAsStorage()
	end 'storageHashed'
end 'Box'

function main() returns ExitCode
	print("{Box.create(Key.create("a")).storageHashed()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
false
```

<!-- test: elements-hash-as-storage-reads-an-enum-through-a-constraint-that-extends-hashable -->
<!-- tier: stdlib -->
An enum that declares no `hash()` of its own hashes as its storage, and a constraint naming an interface
that extends `Hashable` reads that the same as `where … is Hashable` does.
```maxon
interface KeyLike extends Hashable
end 'KeyLike'

enum Suit implements KeyLike
	clubs
	hearts
end 'Suit'

type Box uses Element where Element is KeyLike
	var item as Element

	static function create(item Element) returns Self
		return Self{item: item}
	end 'create'

	function storageHashed() returns bool
		return __Builtins.elementsHashAsStorage()
	end 'storageHashed'
end 'Box'

function main() returns ExitCode
	print("{Box.create(Suit.hearts).storageHashed()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
```

<!-- test: error.hash-array-storage-takes-an-array -->
<!-- tier: stdlib -->
`__Builtins.hashArrayStorage` hashes an array's storage, so an operand that is not an array is refused.
```maxon
function main() returns ExitCode
	let digest = __Builtins.hashArrayStorage(5)
	print("{digest}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:26: '__Builtins.hashArrayStorage' requires an array, but its argument is int
```

<!-- test: error.elements-hash-as-storage-refused-outside-the-library-tiers -->
Outside `stdlib/` and `runtime/` the call is refused at the member it names. It has no public replacement.
```maxon
function main() returns ExitCode
	let stored = __Builtins.elementsHashAsStorage()
	print("{stored}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:3:26: call to '__Builtins.elementsHashAsStorage' from outside the library tiers: '__Builtins' names a raw runtime intrinsic, callable only from a source file under stdlib/ or runtime/ — it has no public replacement
```

<!-- test: error.hash-array-storage-refused-outside-the-library-tiers -->
Outside `stdlib/` and `runtime/` the call is refused at the member it names. It has no public replacement.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntegerArray = Array with Integer

function main() returns ExitCode
	let digest = __Builtins.hashArrayStorage(IntegerArray from [1, 2])
	print("{digest}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:6:26: call to '__Builtins.hashArrayStorage' from outside the library tiers: '__Builtins' names a raw runtime intrinsic, callable only from a source file under stdlib/ or runtime/ — it has no public replacement
```
