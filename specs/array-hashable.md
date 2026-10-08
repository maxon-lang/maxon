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

Returns a hash value folded from each element's own `hash()` with the djb2 algorithm: `h` starts at
`5381`, and each element in order makes it `(h * 33 + element.hash()) and 0xFFFFFFFF`. Equal arrays hash
equal.

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
	var arr = StrArr.create()
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
	var arr = StrArr.create()
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
	var arr = StrArr.create()
	for i in first upto first + count 'fill'
		arr.push("p{i}")
	end 'fill'
	return arr
end 'numbered'

function main() returns ExitCode
	var m = StrArrMap.create()
	try m.insert(["p1", "p2", "p3"], value: 42) otherwise ignore
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
	var arr = StrArr.create()
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
	var arr = StrArr.create()
	for i in first upto first + count 'fill'
		arr.push("p{i}")
	end 'fill'
	return arr
end 'numbered'

function main() returns ExitCode
	var ints = IntArrMap.create()
	try ints.insert([7], value: 1) otherwise ignore
	var strings = StrArrMap.create()
	try strings.insert(["p1", "p2", "p3"], value: 2) otherwise ignore
	var intProbe = IntArr.create()
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
	var items = ValArray.create()
	items.push(41)
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
	var first = ValArray.create()
	first.push(41)
	first.push(42)
	var second = ValArray.create()
	second.push(41)
	second.push(42)
	var third = ValArray.create()
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
	var held = ValArray.create()
	held.push(41)
	held.push(42)
	let holder = H.create(held)
	var equal = ValArray.create()
	equal.push(41)
	equal.push(42)
	var unequal = ValArray.create()
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

function main() returns ExitCode
	var holder = H.create()
	var first = ValArray.create()
	first.push(41)
	first.push(42)
	var second = ValArray.create()
	second.push(41)
	second.push(42)
	holder.note(first)
	holder.note(second)
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
	var a = IntTags.create()
	a.push(IntTag.create(7))
	var b = IntTags.create()
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
	var a = IntTags.create()
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

### Array hash is the element-wise fold for every scalar element width

<!-- test: array-hash-is-the-element-wise-fold-for-every-scalar-element-width -->
```maxon
typealias Wide = int(i64.min to i64.max)
typealias Word = int(0 to u16.max)
typealias Dword = int(0 to u32.max)
typealias Signed = int(-100 to 100)
typealias WideArray = Array with Wide
typealias WordArray = Array with Word
typealias DwordArray = Array with Dword
typealias SignedArray = Array with Signed

function foldWide(values WideArray) returns HashValue
	var h = 5381

	for item in values 'eachElement'
		h = (h * 33 + item.hash()) and 0xFFFFFFFF
	end 'eachElement'

	return h
end 'foldWide'

function foldBytes(values ByteArray) returns HashValue
	var h = 5381

	for item in values 'eachElement'
		h = (h * 33 + item.hash()) and 0xFFFFFFFF
	end 'eachElement'

	return h
end 'foldBytes'

function foldWords(values WordArray) returns HashValue
	var h = 5381

	for item in values 'eachElement'
		h = (h * 33 + item.hash()) and 0xFFFFFFFF
	end 'eachElement'

	return h
end 'foldWords'

function foldDwords(values DwordArray) returns HashValue
	var h = 5381

	for item in values 'eachElement'
		h = (h * 33 + item.hash()) and 0xFFFFFFFF
	end 'eachElement'

	return h
end 'foldDwords'

function foldSigned(values SignedArray) returns HashValue
	var h = 5381

	for item in values 'eachElement'
		h = (h * 33 + item.hash()) and 0xFFFFFFFF
	end 'eachElement'

	return h
end 'foldSigned'

function main() returns ExitCode
	var wide = WideArray.create()
	wide.push(-3)
	wide.push(7)
	wide.push(-9000000000000)
	wide.push(9000000000000)
	print("{wide.hash() == foldWide(wide)}\n")

	var bytes = ByteArray.create()
	bytes.push(0)
	bytes.push(200)
	bytes.push(255)
	print("{bytes.hash() == foldBytes(bytes)}\n")

	var words = WordArray.create()
	words.push(65535)
	words.push(1)
	print("{words.hash() == foldWords(words)}\n")

	var dwords = DwordArray.create()
	dwords.push(4294967295)
	dwords.push(2)
	print("{dwords.hash() == foldDwords(dwords)}\n")

	var signed = SignedArray.create()
	signed.push(-100)
	signed.push(5)
	signed.push(100)
	print("{signed.hash() == foldSigned(signed)}\n")

	let empty = WideArray.create()
	print("{empty.hash() == foldWide(empty)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
true
true
true
true
true
```

### A set keyed by arrays of a signed narrow alias finds equal keys

<!-- test: a-set-keyed-by-arrays-of-a-signed-narrow-alias-finds-equal-keys -->
```maxon
typealias Signed = int(-100 to 100)
typealias SignedArray = Array with Signed
typealias SignedArraySet = Set with SignedArray

function main() returns ExitCode
	var key = SignedArray.create()
	key.push(-100)
	key.push(5)
	key.push(100)
	var probe = SignedArray.create()
	probe.push(-100)
	probe.push(5)
	probe.push(100)
	var other = SignedArray.create()
	other.push(-100)
	other.push(6)
	other.push(100)
	let sameHash = key.hash() == probe.hash()
	var seen = SignedArraySet.create()
	seen.insert(key)
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
```maxon
typealias Byte = float(-1.0 to 1.0)
typealias Bs = Array with Byte

function fold(values Bs) returns HashValue
	var h = 5381

	for item in values 'eachElement'
		h = (h * 33 + item.hash()) and 0xFFFFFFFF
	end 'eachElement'

	return h
end 'fold'

function main() returns ExitCode
	var a = Bs.create()
	a.push(0.0)
	var b = Bs.create()
	b.push(-0.0)
	print("{a == b} {a.equals(b)}\n")
	var c = Bs.create()
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
typealias Reals = Array with Real

function main() returns ExitCode
	var a = Reals.create()
	a.push(1.5)
	a.push(2.5)
	var b = Reals.create()
	b.push(1.5)
	b.push(2.5)
	var c = Reals.create()
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
