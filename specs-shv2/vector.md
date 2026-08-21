---
feature: vector
status: experimental
keywords: [vector, fixed size, stack, collection, generic]
category: stdlib
---

# Vector

## Documentation

### Overview

`Vector` is a generic fixed-size collection. A `Vector with N bool` is bit-packed
(8 elements per byte, `element_size = -1`) exactly like `Array with bool` — a
`Vector with 16 bool` allocates a 2-byte buffer, not 16 — and the packing is
transparent to the `get`/`set`/iterate API. See [bool-bit-packing](bool-bit-packing.md).

### Creating Vectors

Create a concrete vector type using `typealias` with element type and size:

```text
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int
var v = Vec3.create()  // zero-initialized, 3 elements on the stack
```

The size is part of the type. A `Vector with 3 Int` is a different type from `Vector with 4 Int`.

### Creating from Array Literals

Vectors implement `BuiltinArrayLiteral`, so you can initialize them from an array literal using `from`. The element type and size are inferred from the literal:

```text
var v = Vector from [10, 20, 30]  // inferred as Vector with 3 Int
```

The inferred type is compatible with a typealias of the same element type and size, so a `Vector from [...]` can be passed to a function expecting the typealias:

```text
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function process(v Vec3) returns Int
  return try v.get(0) otherwise 0
end 'process'

var v = Vector from [10, 20, 30]
process(v)  // works — inferred type matches Vec3
```

### Element Access

Access elements with `.get()`:

```text
var value = try v.get(0) otherwise 0
```

Modify elements with `.set()`:

```text
v.set(0, value: 42)
```

### Size and Count

The `.count()` method always returns the fixed size of the vector:

```text
typealias Int = int(i64.min to i64.max)
typealias Vec4 = Vector with 4 Int
var v = Vec4.create()
var n = v.count()  // always 4
```

### Stack vs Heap

Vectors are designed for small, fixed-size data. The compiler places the storage on the stack when the total byte size (element size x count) is 8192 bytes or less. Larger vectors are automatically heap-allocated.

```text
typealias Int = int(i64.min to i64.max)
typealias SmallVec = Vector with 100 Int    // 800 bytes → stack
typealias LargeVec = Vector with 2000 Int   // 16000 bytes → heap
```

### Use Cases

Vectors are ideal for:
- Small fixed-size collections (coordinates, colors, matrices)
- Performance-sensitive code where heap allocation is undesirable
- Types with a known compile-time size

```text
typealias Float = float(f64.min to f64.max)
typealias Byte = int(0 to u8.max)
typealias Point3D = Vector with 3 Float
typealias Color = Vector with 4 Byte      // RGBA
typealias Mat2x2 = Vector with 4 Float    // 2x2 matrix stored flat
```

### Iteration

Vectors support `for-in` loops:

```text
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int
var v = Vec3.create()
v.set(0, value: 10)
v.set(1, value: 20)
v.set(2, value: 30)

for elem in v 'loop'
  print("{elem}")
end 'loop'
```

## Tests

<!-- test: create-zero-initialized -->
⚠ THE `/specs` ORIGINAL PINS THREE `RequiredIR:<target>` BLOCKS AND A `<!-- SelfhostedOnly -->` DIRECTIVE, AND NEITHER SURVIVES THE PORT. shv2's spec parser has an arm for neither, so both would be read by nobody while reading as coverage — the shape BATCH29 exists to remove, and `SpecParser.isUnimplementedFenceOpen` now refuses the fence rather than walking past it. What pins the emitted code here is this case's minted fragment golden, which records what THIS compiler emits rather than what v1 did.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function main() returns ExitCode
	let v = Vec3.create()
	return try v.get(0) otherwise -1
end 'main'
```
```exitcode
0
```
<!-- test: count -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = Vector with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	return v.count()
end 'main'
```
```exitcode
4
```

<!-- test: set-and-get -->
⚠ THE `/specs` ORIGINAL PINS THREE `RequiredIR:<target>` BLOCKS AND A `<!-- SelfhostedOnly -->` DIRECTIVE, AND NEITHER SURVIVES THE PORT. shv2's spec parser has an arm for neither, so both would be read by nobody while reading as coverage — the shape BATCH29 exists to remove, and `SpecParser.isUnimplementedFenceOpen` now refuses the fence rather than walking past it. What pins the emitted code here is this case's minted fragment golden, which records what THIS compiler emits rather than what v1 did.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	try v.set(0, value: 42) otherwise panic("test invariant: set OOB")
	return try v.get(0) otherwise 0
end 'main'
```
```exitcode
42
```
<!-- test: set-all-elements -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	try v.set(0, value: 10) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 20) otherwise panic("test invariant: set OOB")
	try v.set(2, value: 30) otherwise panic("test invariant: set OOB")
	let a = try v.get(0) otherwise 0
	let b = try v.get(1) otherwise 0
	let c = try v.get(2) otherwise 0
	return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: get-out-of-bounds -->
Accessing an index beyond the fixed size throws ArrayError.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec2 = Vector with 2 Int

function main() returns ExitCode
	var v = Vec2.create()
	try v.set(0, value: 10) otherwise panic("test invariant: set OOB")
	let result = try v.get(5) otherwise -1
	print("{result}\n")
	return 0
end 'main'
```
```stdout
-1
```

<!-- test: set-out-of-bounds-throws -->
Setting an out-of-bounds index throws ArrayError.indexOutOfBounds.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec2 = Vector with 2 Int

function main() returns ExitCode
	var v = Vec2.create()
	try v.set(0, value: 10) otherwise panic("test invariant: set OOB")
	try v.set(5, value: 99) otherwise 'oob'
		return 7
	end 'oob'
	return try v.get(0) otherwise 0
end 'main'
```
```exitcode
7
```

<!-- test: single-element -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec1 = Vector with 1 Int

function main() returns ExitCode
	var v = Vec1.create()
	try v.set(0, value: 77) otherwise panic("test invariant: set OOB")
	return try v.get(0) otherwise 0
end 'main'
```
```exitcode
77
```

<!-- test: larger-vector -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec10 = Vector with 10 Int

function main() returns ExitCode
	var v = Vec10.create()
	var i = 0
	while i < 10 'fill'
		try v.set(i, value: i * 10) otherwise panic("test invariant: set OOB")
		i = i + 1
	end 'fill'
	let first = try v.get(0) otherwise -1
	let last = try v.get(9) otherwise -1
	return first + last
end 'main'
```
```exitcode
90
```

<!-- test: count-single -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec1 = Vector with 1 Int

function main() returns ExitCode
	var v = Vec1.create()
	return v.count()
end 'main'
```
```exitcode
1
```

<!-- test: overwrite-element -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	try v.set(1, value: 10) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 42) otherwise panic("test invariant: set OOB")
	return try v.get(1) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: float-vector -->
```maxon
typealias Float = float(f64.min to f64.max)
typealias Vec2F = Vector with 2 Float

function main() returns ExitCode
	var v = Vec2F.create()
	try v.set(0, value: 2.5) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 3.5) otherwise panic("test invariant: set OOB")
	let a = try v.get(0) otherwise 0.0
	let b = try v.get(1) otherwise 0.0
	return trunc(a + b)
end 'main'
```
```exitcode
6
```

<!-- test: byte-vector -->
```maxon

typealias Byte = int(0 to u8.max)
typealias ByteVec4 = Vector with 4 Byte

function main() returns ExitCode
	var v = ByteVec4.create()
	try v.set(0, value: 10) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 20) otherwise panic("test invariant: set OOB")
	try v.set(2, value: 30) otherwise panic("test invariant: set OOB")
	try v.set(3, value: 40) otherwise panic("test invariant: set OOB")
	let a = try v.get(0) otherwise 0
	let b = try v.get(3) otherwise 0
	return a + b
end 'main'
```
```exitcode
50
```

<!-- test: pass-to-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias Vec3 = Vector with 3 Integer

function sum(v Vec3) returns Integer
	let a = try v.get(0) otherwise 0
	let b = try v.get(1) otherwise 0
	let c = try v.get(2) otherwise 0
	return a + b + c
end 'sum'

function main() returns ExitCode
	var v = Vec3.create()
	try v.set(0, value: 10) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 20) otherwise panic("test invariant: set OOB")
	try v.set(2, value: 12) otherwise panic("test invariant: set OOB")
	return sum(v)
end 'main'
```
```exitcode
42
```

<!-- test: return-from-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias Vec2 = Vector with 2 Integer

function makeVec(a Integer, b Integer) returns Vec2
	var v = Vec2.create()
	try v.set(0, value: a) otherwise panic("test invariant: set OOB")
	try v.set(1, value: b) otherwise panic("test invariant: set OOB")
	return v
end 'makeVec'

function main() returns ExitCode
	let v = makeVec(30, b: 12)
	let a = try v.get(0) otherwise 0
	let b = try v.get(1) otherwise 0
	return a + b
end 'main'
```
```exitcode
42
```

<!-- test: iterate -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = Vector with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	try v.set(0, value: 1) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 2) otherwise panic("test invariant: set OOB")
	try v.set(2, value: 3) otherwise panic("test invariant: set OOB")
	try v.set(3, value: 4) otherwise panic("test invariant: set OOB")
	var sum = 0
	for elem in v 'loop'
		sum = sum + elem
	end 'loop'
	return sum
end 'main'
```
```exitcode
10
```

<!-- test: let-vector-read -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function makeVec() returns Vec3
	var v = Vec3.create()
	try v.set(0, value: 10) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 20) otherwise panic("test invariant: set OOB")
	try v.set(2, value: 12) otherwise panic("test invariant: set OOB")
	return v
end 'makeVec'

function main() returns ExitCode
	let v = makeVec()
	let a = try v.get(0) otherwise 0
	let b = try v.get(1) otherwise 0
	let c = try v.get(2) otherwise 0
	return a + b + c
end 'main'
```
```exitcode
42
```

<!-- test: from-array-literal -->
```maxon
function main() returns ExitCode
	let v = Vector from [10, 20, 30]
	return try v.get(0) otherwise 0
end 'main'
```
```exitcode
10
```

<!-- test: from-array-literal-sum -->
```maxon
function main() returns ExitCode
	let v = Vector from [10, 20, 30]
	let a = try v.get(0) otherwise 0
	let b = try v.get(1) otherwise 0
	let c = try v.get(2) otherwise 0
	return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: from-array-literal-float -->
```maxon
function main() returns ExitCode
	let v = Vector from [1.5, 2.5]
	let a = try v.get(0) otherwise 0.0
	let b = try v.get(1) otherwise 0.0
	return trunc(a + b)
end 'main'
```
```exitcode
4
```

<!-- test: from-array-literal-iterate -->
```maxon
function main() returns ExitCode
	let v = Vector from [1, 2, 3, 4]
	var sum = 0
	for elem in v 'loop'
		sum = sum + elem
	end 'loop'
	return sum
end 'main'
```
```exitcode
10
```

<!-- test: from-array-literal-single -->
```maxon
function main() returns ExitCode
	let v = Vector from [99]
	return try v.get(0) otherwise 0
end 'main'
```
```exitcode
99
```

<!-- test: from-literal-typealias-compatible -->
The inferred type from a literal is compatible with a typealias of the same element type and size.
```maxon

typealias Integer = int(i64.min to i64.max)

typealias Vec3 = Vector with 3 Integer

function sum(v Vec3) returns Integer
	let a = try v.get(0) otherwise 0
	let b = try v.get(1) otherwise 0
	let c = try v.get(2) otherwise 0
	return a + b + c
end 'sum'

function main() returns ExitCode
	var v = Vec3.create()
	try v.set(0, value: 10) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 20) otherwise panic("test invariant: set OOB")
	try v.set(2, value: 12) otherwise panic("test invariant: set OOB")
	return sum(v)
end 'main'
```
```exitcode
42
```

<!-- test: accumulate-sum -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec5 = Vector with 5 Int

function main() returns ExitCode
	var v = Vec5.create()
	try v.set(0, value: 1) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 2) otherwise panic("test invariant: set OOB")
	try v.set(2, value: 3) otherwise panic("test invariant: set OOB")
	try v.set(3, value: 4) otherwise panic("test invariant: set OOB")
	try v.set(4, value: 5) otherwise panic("test invariant: set OOB")
	var sum = 0
	var i = 0
	while i < v.count() 'loop'
		sum = sum + (try v.get(i) otherwise 0)
		i = i + 1
	end 'loop'
	return sum
end 'main'
```
```exitcode
15
```

<!-- test: bool-vector-cross-byte -->
```maxon
typealias Bits16 = Vector with 16 bool

function main() returns ExitCode
	var v = Bits16.create()
	try v.set(0, value: true) otherwise panic("test invariant: set OOB")
	try v.set(3, value: true) otherwise panic("test invariant: set OOB")
	try v.set(8, value: true) otherwise panic("test invariant: set OOB")
	try v.set(15, value: true) otherwise panic("test invariant: set OOB")
	var count = 0
	var i = 0
	while i < v.count() 'scan'
		let bit = try v.get(i) otherwise false
		if bit 'isSet'
			count = count + 1
		end 'isSet'
		i = i + 1
	end 'scan'
	return count
end 'main'
```
```exitcode
4
```

<!-- test: bool-vector-overwrite-clear -->
```maxon
typealias Bits8 = Vector with 8 bool

function main() returns ExitCode
	var v = Bits8.create()
	try v.set(2, value: true) otherwise panic("test invariant: set OOB")
	try v.set(5, value: true) otherwise panic("test invariant: set OOB")
	try v.set(2, value: false) otherwise panic("test invariant: set OOB")
	let a = try v.get(2) otherwise true
	let b = try v.get(5) otherwise false
	var r = 0
	if not a 'cleared'
		r = r + 1
	end 'cleared'
	if b 'stillSet'
		r = r + 10
	end 'stillSet'
	return r
end 'main'
```
```exitcode
11
```

<!-- test: bool-vector-from-literal -->
```maxon
function main() returns ExitCode
	let v = Vector from [true, false, true, true, false, true, false, false, true]
	var count = 0
	for bit in v 'each'
		if bit 'isSet'
			count = count + 1
		end 'isSet'
	end 'each'
	return count
end 'main'
```
```exitcode
5
```

## The Size Is Part of the Type

A `Vector with 3 Int` and a `Vector with 4 Int` are two types, wherever they are reached from: a
declared alias, a field of a generic type, or a synthesized instance nothing has named.

<!-- test: capacity-is-part-of-instance-identity -->
The size is part of the type, so a generic type's capacity-4 field must keep that capacity
rather than adopting a declared `Vector with 3` alias that happens to share its element type.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

type Holder uses Element
	typealias Slot = Vector with 4 Element

	var slot as Slot

	export static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	export function size() returns Int
		return slot.count()
	end 'size'
end 'Holder'

typealias IntHolder = Holder with Int

function main() returns ExitCode
	var v = Vec3.create()
	var h = IntHolder.create()
	return v.count() + h.size()
end 'main'
```
```exitcode
7
```

<!-- test: distinct-capacities-are-distinct-instances -->
Two generic types whose fields differ only in capacity must not collapse onto one instance,
even when nothing in the project declares a name for either.
```maxon
typealias Int = int(i64.min to i64.max)

type Holder4 uses Element
	typealias Slot4 = Vector with 4 Element

	var quad as Slot4

	export static function create() returns Self
		return Self{quad: Slot4.create()}
	end 'create'

	export function size() returns Int
		return quad.count()
	end 'size'
end 'Holder4'

type Holder7 uses Element
	typealias Slot7 = Vector with 7 Element

	var septet as Slot7

	export static function create() returns Self
		return Self{septet: Slot7.create()}
	end 'create'

	export function size() returns Int
		return septet.count()
	end 'size'
end 'Holder7'

typealias IntHolder4 = Holder4 with Int
typealias IntHolder7 = Holder7 with Int

function main() returns ExitCode
	var a = IntHolder4.create()
	var b = IntHolder7.create()
	return a.size() * 10 + b.size()
end 'main'
```
```exitcode
47
```

<!-- test: error.wrong-size-vector-argument -->
The size is part of the type, so a differently-sized vector is not a widening — it is a different
type, and passing one where the other is declared is refused rather than silently accepted.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int
typealias Vec4 = Vector with 4 Int

function wants4(v Vec4) returns Int
	return v.count()
end 'wants4'

function main() returns ExitCode
	var v = Vec3.create()
	return wants4(v)
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:9: argument type mismatch for 'v': expected 'Vec4', got 'Vec3'
```

<!-- test: same-size-aliases-are-one-type -->
Two names for one size are one type, and stay interchangeable — separating the sizes must not
separate two spellings of the same one.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int
typealias Triple = Vector with 3 Int

function first(v Vec3) returns Int
	return try v.get(0) otherwise 0
end 'first'

function main() returns ExitCode
	var t = Triple.create()
	try t.set(0, value: 21) otherwise panic("test invariant: set OOB")
	return first(t) + t.count()
end 'main'
```
```exitcode
24
```

## Every Member Is The Declaration's, And A Member It Does Not Have Is Refused Off It

⭐⭐⭐ **THE ROSTER IS GONE (W190), AND THIS SECTION IS WHAT REPLACED IT.** It held a case named
`error.a-member-off-the-roster-is-refused-with-the-roster`, pinning the sentence
`Parser.vectorSurfaceMemberNames` rendered for a member the SYNTHESIZED surface did not carry — *"shv2
provides count/get/set; that list IS the surface"*. `create`, `count`, `get` and `set` are
`stdlib/Vector.maxon`'s now, so there is no synthesized surface to render a sentence from and a member a
`Vector` does not have is `E3004` off the declaration, exactly as it is for a `List` (W153) or a `Map` (W41).

⛔⛔ **THE FIVE CASES BELOW ARE A PROBE THAT FOUND NOTHING, WRITTEN DOWN SO IT COUNTS AS HAVING HAPPENED**,
and the thing they probe is a LANDMINE `W86` recorded in advance: a vector losing its dispatch arm would
fall through to `dispatchArrayMethod` and be served the GROWABLE surface — *"a fixed-size container grown
through its own type, a WRONG ANSWER rather than a refusal"*. It is defused, and by construction rather
than by luck: `dispatchMethodOnReceiver`'s array arm tests `isArrayInstanceAt`, which reads the base NAME,
and a `Vector`'s base is not an `Array`'s. **MEASURED, all five growth spellings, on the binary this rung
ships.** ⚠ W86's citation for the cure (`SignatureIndex.maxon:8334`) had gone stale and pointed at
`descriptorNeeds`; probing is what settled it.

<!-- test: error.push-is-refused-off-the-declaration -->
The spelling `W86` named first, and the one a hijacked receiver would have ANSWERED rather than refused.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	v.push(4)
	return v.count()
end 'main'
```
```maxoncstderr
error E3004: <fragment>:7:4: call to undefined function 'Vector.push'
```

<!-- test: error.resize-is-refused-off-the-declaration -->
The one `W112` MEASURED a user-declared `Vector` being served: `resize(9)` then `count()` answered **9**,
a three-element type grown to nine with no diagnostic.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	v.resize(9)
	return v.count()
end 'main'
```
```maxoncstderr
error E3004: <fragment>:7:4: call to undefined function 'Vector.resize'
```

<!-- test: error.clear-is-refused-off-the-declaration -->
`clear` empties the record. A fixed-size container whose `count()` could reach 0 is not fixed.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	v.clear()
	return v.count()
end 'main'
```
```maxoncstderr
error E3004: <fragment>:7:4: call to undefined function 'Vector.clear'
```

<!-- test: error.insert-is-refused-off-the-declaration -->
⚠ **`insert` AND `remove` ARE THE TWO THE OLD HAND-WRITTEN SENTENCE NEVER NAMED**, which is what
`vectorSurfaceMemberNames`' own header recorded as the usual finding when a typed-out list is replaced by
a rendered one. Neither is served now, and neither has to be listed anywhere for that to be true.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	try v.insert(0, value: 4) otherwise panic("insert")
	return v.count()
end 'main'
```
```maxoncstderr
error E3004: <fragment>:7:8: call to undefined function 'Vector.insert'
```

<!-- test: error.remove-is-refused-off-the-declaration -->
The second of that pair, and the one that SHRINKS.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	return try v.remove(0) otherwise 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:7:15: call to undefined function 'Vector.remove'
```

<!-- test: error.a-bare-growth-call-inside-an-extension-body-is-refused -->
⛔⛔ **THE SECOND ENTRANCE `W86`'s LANDMINE COULD HAVE COME IN BY, AND IT IS SHUT FOR A DIFFERENT REASON
FROM THE FIVE ABOVE.** `implicitSelfTakesTheArrayRoster` gives a BARE call inside a container's own body
the ARRAY roster, which is what makes `stdlib/Array.maxon`'s own `contains(sequence)` reach `get`. Its gate
is `declarationIsTheManagedRecord` — the GROWABLE record's declaration and not
`declarationsValueIsTheManagedRecord`, which is the `or` that also admits the sized one — so an
`extension Vector` body does not take that roster and a bare `push` resolves as an ordinary sibling call to
a method nothing declares.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function grow() returns Int
		push(4)
		return 0
	end 'grow'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	return v.grow()
end 'main'
```
```maxoncstderr
error E3004: <fragment>:7:3: call to undefined function 'push'
```

<!-- test: error.the-buffer-under-a-vector-is-still-the-buffer -->
⛔ **THE THIRD ENTRANCE.** `bufferSurfaceOfDeclaredRecord` deliberately RETYPES a sized container's
`managed` read to `Array with Element`, so that `stdlib/Vector.maxon`'s own `VectorIter.create(managed)`
type-checks — one record, two ids. The retype is a TYPE and not a surface: the value is still marked the
BUFFER's, so what it serves is `bufferSurfaceMemberNames` and the growable array's members are not on it.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function grow() returns Int
		managed.push(4)
		return 0
	end 'grow'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	return v.grow()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:11: Unsupported: `__ManagedMemory` member 'push' — R4.4 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

## The Members Are Declared FUNCTIONS, Which Is A Thing A Dispatch Arm Cannot Be

⭐⭐⭐ **THE DIFFERING-DECLARATIONS CONTROL, IN THE SUITE (W190).** Every ANSWER a retired member gives is
the answer the compiler-served arm gave — that is what makes the retirement safe, and it is also what makes
a value-COMPARING control impossible to write for `count`, `get` or `set`: both roads answer 42. What
separates them is not the value but the KIND of thing that produced it. A dispatch arm is reachable through
one syntax and nothing else; a declared method is a function of the program, and Maxon lets an instance
method be named statically with its receiver as the first argument (`Adder.bump(a)`, which
`parseQualifiedCall`'s header settles as legal). **`Vector.get(v, index: 1)` therefore cannot compile while
`get` is a dispatch arm — there is no such function to call — and it is a RUNTIME ANSWER rather than a
message text.**

⚠ It also exercises the corpus body end to end: sabotaging `stdlib/Vector.maxon`'s `get` to `throw
ArrayError.indexOutOfBounds` turns this case red where the intact declaration answers 42, which is the
control `stdlib-whitelist.md` runs for a listed module and this rung owes for a retired member.

<!-- test: the-members-are-functions-and-answer-to-their-static-spelling -->
Both retired accessors, named statically, with the receiver passed as the first argument and the
declaration's own `index:`/`value:` labels on the rest.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	try Vector.set(v, index: 1, value: 41) otherwise panic("set")
	return (try Vector.get(v, index: 1) otherwise 0) + 1
end 'main'
```
```exitcode
42
```

<!-- test: the-count-is-a-function-too -->
The same for `count`, which had no arguments at all to tell the two roads apart by — so the static spelling
is the whole of the difference. Under the dispatch arm it folded to a literal at the CALL and `Vector.count`
named nothing.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec5 = Vector with 5 Int

function main() returns ExitCode
	var v = Vec5.create()
	return Vector.count(v) as ExitCode
end 'main'
```
```exitcode
5
```

## A `let` Vector Still Refuses The Write, By The Declaration's Own Rule

⚖ **THE RULING SAID THIS REFUSAL WOULD BE DROPPED BY DESIGN, AND FOR THIS MEMBER IT IS NOT — MEASURED
(W190).** `STDLIB-BRINGUP.md`'s E3019 ruling is that the immutable-receiver rule is a BUILTIN-SURFACE rule
and a declared type is exempt, *"so a retirement DROPS the immutable-receiver refusal by design"*; `Set`
paid three cases for it. A `Vector` pays none: `stdlib/Vector.maxon`'s `set` writes through its receiver
(`managed.set`), so the ORDINARY parameter-mutation rule reaches the same conclusion from the declaration
instead of from a roster — which is what `surfaceRosterProvider`'s note records happening for `Set`'s and
`Map`'s own mutators at W105.

⚠ **THE SENTENCE MOVED EVEN THOUGH THE VERDICT DID NOT**, and that is the whole of what this case pins:
the old one named the surface, this one names the PARAMETER the callee declares. A case that asserted only
"refused" would not have noticed either.

<!-- test: error.a-write-through-a-let-vector-is-refused-off-the-declaration -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function main() returns ExitCode
	let v = Vec3.create()
	try v.set(0, value: 7) otherwise panic("set")
	return try v.get(0) otherwise 0
end 'main'
```
```maxoncstderr
error E3019: <fragment>:7:8: cannot pass 'v' to function that mutates parameter 'self' (in main)
```

## A Vector Global Takes The Declared Generic's Road

⭐⭐ **A ROAD THIS CONTAINER HAD NEVER TAKEN (W190).** A top-level `var g = Vec3.create()` used to be
gated by `requireContainerIsCreatable` and emitted by `containerCreateCall`'s BUILTIN arm — a runtime
`__managed_create` handed the strides it cannot look up, plus one `__managed_resize` to size the record.
With `create` declared, `instanceCreateIsDeclared` routes the global through
`requireDeclaredGenericGlobalCreate` instead, whose three premises are the ones
[top-level-factory-globals](top-level-factory-globals.md) pins for every declared generic: the `create()`
must EXIST, be NAMEABLE from its declaring file, and return `Self`. `__module_init` then emits a bare
`call Vector.create` with no stamps at all, and the record it hands back is already published — which is
what let `ContainerSizing` be deleted rather than kept for one producer.

<!-- test: a-vector-global-is-built-by-the-declarations-own-static -->
The global is filled from one function and read from another, so what is under test is the slot
`__module_init` wrote before `main` and not a local the same statement built.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

var shared = Vec3.create()

function fill() returns Int
	try shared.set(0, value: 20) otherwise panic("set")
	try shared.set(2, value: 21) otherwise panic("set")
	return 0
end 'fill'

function main() returns ExitCode
	_ = fill()
	return (try shared.get(0) otherwise 0) + (try shared.get(2) otherwise 0) + shared.count()
end 'main'
```
```exitcode
44
```

## The Fixed Size Is The Bound, And The Buffer Under It Is Not

⛔⛔ **NOT FROM `/specs/vector.md` — shv2's own, and it pins a WRONG ANSWER this rung introduced and
measured (W190).** `stdlib/Vector.maxon`'s `set` forwards to `managed.set`, and the BUFFER surface's setter is
bounded by CAPACITY where the array surface's is bounded by LENGTH (⚖ user ruling 2026-07-30, recorded at
`ManagedMemoryRuntime.ManagedMemSetName`: the wider bound is what makes the stage-then-`setLength` idiom
spellable). A vector's capacity is NOT its count — `Vec3.create()` grows through
`stdlib/Array.maxon`'s policy, whose `MinimumCapacity` is **4**, so a three-element vector's record has a
fourth slot the type does not have.

**MEASURED, with `set` forwarding straight to the buffer**: `made.set(3, value: 99)` on a
`Vec3.create()` SUCCEEDED — a write to the fourth slot of a three-slot type, no diagnostic, `count()`
unmoved at 3. The compiler-served `set` it replaced could not do this: it emitted `__managed_set`, which is
length-bounded. So the guard here is not belt-and-braces, it is THE WHOLE OF THE VECTOR'S BOUND —
`stdlib/Array.maxon`'s `set` carries the identical guard for the identical measured reason, and its header
says so.

⚠ The two spellings below are the two ways a vector comes into being, and they have DIFFERENT capacities
(a literal's is the array literal's, a `create()`'s is the growth policy's) — so a case over only one of
them would pass on the other's arithmetic.

<!-- test: a-write-past-the-fixed-size-is-refused-however-the-vector-was-built -->
Both vectors hold three elements and both refuse index 3. The index is laundered through a call so no
constant is in the compiler's hands at the write — what is under test is the RUNTIME bound, and a folded
index would be answering a different question.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function runtimeIndex(n Int) returns Int
	return n
end 'runtimeIndex'

function main() returns ExitCode
	var made = Vec3.create()
	var lit = Vector from [1, 2, 3]
	var refused = 0
	try made.set(runtimeIndex(3), value: 99) otherwise 'madePastEnd'
		refused = refused + 1
	end 'madePastEnd'
	try lit.set(runtimeIndex(3), value: 99) otherwise 'litPastEnd'
		refused = refused + 1
	end 'litPastEnd'
	let madeRead = try made.get(runtimeIndex(3)) otherwise 0 - 1
	print("refused={refused} madeRead={madeRead} madeCount={made.count()} litCount={lit.count()}\n")
	return refused as ExitCode
end 'main'
```
```exitcode
2
```
```stdout
refused=2 madeRead=-1 madeCount=3 litCount=3
```

## A Published Slot Must Be A Value At Every Instantiation

⚠ NOT FROM `/specs/vector.md` — shv2's own, and it is the other half of the mechanism `S2u` built. A
`Vector with <N> <type parameter>` is created ONCE, in a body every instantiation shares, and `create()`
publishes all N slots by zeroing them. A zeroed slot is an element for a trivial instantiation and a NULL
for a managed one, and the shared body cannot branch on which. MEASURED before the gate existed, on the
program below with a `for … in` read added: `count()` answered 4 while every `get` reported an empty slot
(exit 0, no diagnostic), and the loop read **SEGFAULTED**. It is the concrete managed element's refusal
(`error.a-concrete-managed-element-is-refused`, just below) asked of the thing that is knowable inside a
shared body — the instantiation set — rather than of an element type there is none of.

<!-- test: a-vector-field-in-a-generic-body-round-trips-its-elements -->
NOT FROM `/specs/vector.md` — the CAPABILITY this rung delivers, which the two canonical cases above do
not reach. Both of them only call `count()`, and a vector's count folds to a literal off the instance's
own size, so **they would both still pass if every slot read and wrote the wrong address.** This one
drives `set` and `get` through the shared generic body: a written slot reads back, an UNWRITTEN slot
reads back the published zero (which is the whole reason the managed instantiation is refused), and the
count is unchanged by the writes. Oracle-agreed byte-for-byte on this source — `a=10 b=7 zero=0 count=4`,
exit 17 — measured on the bootstrap, not assumed.
```maxon
typealias Int = int(i64.min to i64.max)

type Holder uses Element
	typealias Slot = Vector with 4 Element

	var slot as Slot

	export static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	export function put(index Int, value Element)
		try slot.set(index, value: value) otherwise panic("put")
	end 'put'

	export function at(index Int) returns Element
		return try slot.get(index) otherwise panic("at")
	end 'at'

	export function size() returns Int
		return slot.count()
	end 'size'
end 'Holder'

typealias IntHolder = Holder with Int

function main() returns ExitCode
	var h = IntHolder.create()
	h.put(0, value: 10)
	h.put(3, value: 7)
	print("a={h.at(0)} b={h.at(3)} zero={h.at(1)} count={h.size()}\n")
	return h.at(0) + h.at(3)
end 'main'
```
```exitcode
17
```
```stdout
a=10 b=7 zero=0 count=4
```

<!-- test: error.a-managed-instantiation-of-a-vector-field-is-refused -->
A type parameter can be instantiated at a managed type, so a fixed-size vector over one is refused at
the `create()` that would publish its slots — not at the instantiation, and not at run time.

⚠ **THIS REFUSES A PROGRAM THE ORACLE ACCEPTS, AND THAT IS A STATED POSITION RATHER THAN AN OVERSIGHT — COORDINATOR-MEASURED 2026-08-13.** The bootstrap compiles this exact source and **runs it to exit 4**: `count()` answers 4 because four slots were published, and nothing here ever READS one. It is the read that is unsound — a zeroed slot is a value for a trivial element and a NULL for a managed one — so the oracle's exit 4 is the hazard not yet triggered, not a disagreement about whether the hazard exists. shv2 refuses at compile time what both compilers otherwise break on at run time; the same shape with a `for … in` read **SEGFAULTS (139)** on an ungated shv2, and the bootstrap dies in `__ArrayIterator_String.current` with `mm_incref called with NULL pointer`. ⇒ this extends the position shv2 already holds for a CONCRETE managed element (see `error.a-concrete-managed-element-is-refused` below) to the instantiated case, so the two arms of one rule agree. **The divergence itself is owned by the `Vector` retirement chain**, whose corpus `stdlib/Vector.maxon` is generic over its element where shv2's synthesized vector is scalar-only — not by this case.
```maxon
typealias Int = int(i64.min to i64.max)

type Holder uses Element
	typealias Slot = Vector with 4 Element

	var slot as Slot

	export static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	export function size() returns Int
		return slot.count()
	end 'size'
end 'Holder'

typealias StrHolder = Holder with String

function main() returns ExitCode
	var h = StrHolder.create()
	return h.size()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:21: Unsupported: `Vector with <N> <type parameter>` — a vector PUBLISHES all N of its slots at `create` by zeroing them, and this generic type is instantiated with a type whose slot is a heap POINTER: a zeroed slot is an element for a trivial instantiation and a NULL for a managed one, so `count()` would answer N while every `get` reports an empty slot and a `for … in` read dereferences the null. Instantiate this type at integer or bool elements only — a `float` TYPE ARGUMENT is refused separately today, for its own reason — or hold the elements in an `Array with <type parameter>`, which publishes nothing and grows by `push`
```

<!-- test: error.one-managed-instantiation-refuses-the-shared-body -->
The body is compiled ONCE and serves every instantiation, so the rule is EXISTENTIAL: one managed
instantiation refuses it, and a trivial instantiation standing beside it does not buy it back.
```maxon
typealias Int = int(i64.min to i64.max)

type Holder uses Element
	typealias Slot = Vector with 4 Element

	var slot as Slot

	export static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	export function size() returns Int
		return slot.count()
	end 'size'
end 'Holder'

typealias IntHolder = Holder with Int
typealias StrHolder = Holder with String

function main() returns ExitCode
	var a = IntHolder.create()
	var b = StrHolder.create()
	return a.size() + b.size()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:21: Unsupported: `Vector with <N> <type parameter>` — a vector PUBLISHES all N of its slots at `create` by zeroing them, and this generic type is instantiated with a type whose slot is a heap POINTER: a zeroed slot is an element for a trivial instantiation and a NULL for a managed one, so `count()` would answer N while every `get` reports an empty slot and a `for … in` read dereferences the null. Instantiate this type at integer or bool elements only — a `float` TYPE ARGUMENT is refused separately today, for its own reason — or hold the elements in an `Array with <type parameter>`, which publishes nothing and grows by `push`
```

<!-- test: a-vector-in-an-extension-body-over-an-associated-type -->
NOT FROM `/specs/vector.md` — the construct reached from its third position, which sources its layout
descriptor differently from the other two: an `extension` body has no `uses` clause of its own and is
scanned in its target's scope, so the descriptor a `Slot.create()` reads there is reserved through the
interface's parameter rather than a struct's. The refusal above is a REFUSAL, and a refusal that fires
everywhere proves only that nothing works — this is the capability half, at the position the two cases
above do not stand in.
```maxon
typealias Int = int(i64.min to i64.max)

interface Sized uses Element
	typealias Slot = Vector with 4 Element
	function tally() returns Int
end 'Sized'

extension Sized
	export function tallyTwice() returns Int
		var v = Slot.create()
		return v.count() + tally()
	end 'tallyTwice'
end 'Sized'

type Bag uses Element implements Sized with Element
	typealias Slot = Vector with 4 Element
	var n as Int

	export static function create() returns Self
		return Self{n: 1}
	end 'create'

	export function tally() returns Int
		return n
	end 'tally'
end 'Bag'

typealias IntBag = Bag with Int

function main() returns ExitCode
	var b = IntBag.create()
	return b.tallyTwice()
end 'main'
```
```exitcode
5
```

<!-- test: error.a-concrete-managed-element-is-refused -->
NOT FROM `/specs/vector.md` — the CONCRETE half of the same invariant, which until this case nothing ran
at all. `Parser.requireVectorElementType`'s two arms are one rule about one hazard, and `S2u` was about to
land two cases on the shared-body arm and leave this one where it found it: the door that refuses a
written `Vector with 2 String` could have been deleted and the whole suite would have stayed green. ⚠ It is a KNOWN DIVERGENCE and not agreement — the bootstrap compiles and
runs this program (`W82` measured `got=beta count=3` on the byte-identical shape), because it reads the
corpus `stdlib/Vector.maxon`, which is generic over its element, where shv2's synthesized vector is
scalar-only. The row that owns that divergence is the `Vector` retirement chain, not this case; what this
case pins is that shv2's position is a stated refusal rather than an accident.
```maxon
typealias Vec2 = Vector with 2 String

function main() returns ExitCode
	var v = Vec2.create()
	return v.count()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:10: Unsupported: `Vector` holds its elements INLINE and publishes every slot at `create`, so it has no element destructor to stamp and a 'String' element would leak its storage and be read back as a null slot — a vector's element is an integer, a bool or a float
```

## A Sized Container's Buffer May Not Change Its Own Length (W192)

⛔⛔ **`count()` ANSWERS FROM THE TYPE WHILE `get` AND THE WALK ANSWER FROM THE RECORD, AND UNTIL W192
NOTHING DEFENDED THE INVARIANT BETWEEN THEM.** `stdlib/Vector.maxon`'s `count()` is `countof(Self)` —
folded to a literal, no load — while `get` forwards to `managed.get` (LENGTH-bounded) and
`createIterator` hands the raw buffer to a cursor that walks the same length. All three agree only
while `managed.length() == countof(Self)`, which every route into the record establishes at
construction. What was undefended is the record being reached PAST the surface: `managed` is visible
to an `extension Vector`, and the buffer surface served the length-changing members. MEASURED on both
compilers, byte-identical: `before=3 w0=3 after=3 walked=0 g0=-1`.

⭐ **THE REFUSED SET IS THE MEASURED ONE — THE MEMBERS THAT WRITE THE RECORD'S LENGTH WORD, AND NO
OTHERS.** Measured on a `Vector with 3 Int`'s buffer by printing `managed.length()` after each call:
`setLength` 3 to 5, `remove` 5 to 4, `clear` 4 to 0 all move it; `grow(64)` moves CAPACITY 4 to 64 and
leaves the length at 3; `swap`, `shiftRight` and `shiftLeft` move elements the buffer already owns and
leave it alone. **`grow` is deliberately SERVED**: a vector whose buffer has room to spare still has
the size its type states, and refusing it would be over-refusal — which is a wrong answer too.

⚠ **NOT A MEMORY-SAFETY HOLE, WHICH IS WHY THE CURE IS HERE AND NOT ON `get`.** `setLength` is
capacity-refused, so every slot the walk reaches is a slot the buffer really owns. A guard on `get`
would cost a compare on every vector read in every program to defend a state the type's own surface
cannot produce; this refusal costs nothing at runtime and closes the seam one level up, the way
`push` is already refused on this surface.

<!-- test: error.a-sized-containers-buffer-may-not-be-cleared -->
**ENTRANCE A — the bare `managed` read inside an `extension Vector`**, which is the shape the defect
was measured on. `managed` is not exported, so this is the shortest spelling that reaches the record.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function bust()
		managed.clear()
	end 'bust'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	v.bust()
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:11: Unsupported: `__ManagedMemory` member 'clear' — this buffer is a `Vector`'s own record, and a vector's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record. 'clear' WRITES the record's length, which would leave the type saying one number while `get` and the walk answer another. A sized container's buffer refuses setLength/append/clear/remove and serves every other member of the surface; the growable `Array`'s buffer serves all of them, because an `Array`'s count IS its record's length
```

<!-- test: error.a-sized-containers-buffer-may-not-be-relengthened -->
`setLength` is the member the measurement caught moving the length furthest — `grow(64)` then
`setLength(64)` read `count=3 walked=64 g40=0`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function stretch()
		try managed.setLength(2) otherwise ignore
	end 'stretch'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	v.stretch()
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:15: Unsupported: `__ManagedMemory` member 'setLength' — this buffer is a `Vector`'s own record, and a vector's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record. 'setLength' WRITES the record's length, which would leave the type saying one number while `get` and the walk answer another. A sized container's buffer refuses setLength/append/clear/remove and serves every other member of the surface; the growable `Array`'s buffer serves all of them, because an `Array`'s count IS its record's length
```

<!-- test: error.a-sized-containers-buffer-may-not-lose-an-element -->
`remove` shrinks by one and slides the tail — measured 5 to 4.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function drop()
		try managed.remove(0) otherwise ignore
	end 'drop'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	v.drop()
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:15: Unsupported: `__ManagedMemory` member 'remove' — this buffer is a `Vector`'s own record, and a vector's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record. 'remove' WRITES the record's length, which would leave the type saying one number while `get` and the walk answer another. A sized container's buffer refuses setLength/append/clear/remove and serves every other member of the surface; the growable `Array`'s buffer serves all of them, because an `Array`'s count IS its record's length
```

<!-- test: error.a-sized-containers-buffer-may-not-be-appended-to -->
⭐ **THIS CASE ALSO PRE-EMPTS A COMPILER PANIC, AND THAT IS A CONSEQUENCE OF THE REFUSAL RATHER THAN ITS
POINT.** On the base, `managed.append(managed)` inside ANY generic container extension body — sized or
growable — dies with `panic at LayoutDescriptor.maxon:571: primitiveTypeByteSize: a 'typeParameter's
size is a runtime layout-descriptor read, not a compile-time constant`, through
`parseArrayAppend -> requireAppendArg -> arrayAppendArgAdmits`. The refusal here runs AHEAD of
`parseArrayAppend`, so the sized half of that panic becomes a sentence.

⭐ **THE GROWABLE HALF WAS A SEPARATE ROW AND W194 CLOSED IT** — this paragraph said *"untouched … do not
read this case as a fix for it"*, which was true for exactly one rung. `extension Array`'s
`managed.append(managed)` now COMPILES AND RUNS (`managed-memory-methods/the-bare-fused-append-answers-the-same`,
exit 4), because the fused record door hands the receiver its OWN buffer instance instead of the
synthesized BYTE one — so `containerElementIsOpaque` answers about the element the receiver actually has.
⚠ **NOTHING ABOUT THIS CASE CHANGED, AND THE ROUTE IT TAKES DID NOT EITHER.** The bare spelling still
reaches the fused door, still mints no value, and is still refused there off the ENCLOSING DECLARATION —
`declarationIsASizedContainerRecordNamed(enclosingType)`, the by-name form that exists for exactly a
caller with no value to mark. It could not be otherwise: the instance the door now hands over is spelled
`Array with Element` for a vector too (W189's load-bearing retype), so a `giid` test cannot tell a
vector's record from a growable array's. Measured at the W194 review against that rung's own RED
baseline, four reachable `Vector` spellings compiled on BOTH binaries — `try managed.append(…)`, the bare
statement form, `self.managed.append(…)`, and the buffer bound to a local first — each answering the
byte-identical `E2015` at its own byte-identical line:column. (A fifth, `v.managed.append(…)` from
outside the declaration, is `E3014` on both: `managed` is not exported.)
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function double()
		try managed.append(managed) otherwise ignore
	end 'double'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	v.double()
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:15: Unsupported: `__ManagedMemory` member 'append' — this buffer is a `Vector`'s own record, and a vector's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record. 'append' WRITES the record's length, which would leave the type saying one number while `get` and the walk answer another. A sized container's buffer refuses setLength/append/clear/remove and serves every other member of the surface; the growable `Array`'s buffer serves all of them, because an `Array`'s count IS its record's length
```

<!-- test: error.the-chained-self-managed-reaches-the-same-refusal -->
**ENTRANCE B — `self.managed`, which mints NO value to mark** and passes the surface along the dispatch
instead (`viaManagedField`). It is a different carrier from entrance A and had to be closed separately;
measured on the base at `count=3 len=0`, identically to A.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function bust()
		self.managed.clear()
	end 'bust'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	v.bust()
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:16: Unsupported: `__ManagedMemory` member 'clear' — this buffer is a `Vector`'s own record, and a vector's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record. 'clear' WRITES the record's length, which would leave the type saying one number while `get` and the walk answer another. A sized container's buffer refuses setLength/append/clear/remove and serves every other member of the surface; the growable `Array`'s buffer serves all of them, because an `Array`'s count IS its record's length
```

<!-- test: error.a-local-bound-to-the-buffer-reaches-the-same-refusal -->
**ENTRANCE C — the buffer bound to a local first.** shv2 has no stack slots, so `m` binds to the
surface's own `ValueId` and the value mark answers for it; the case exists because that is a PROPERTY
of the binding rule rather than a thing this refusal arranges, and a rung that changed it would
silently reopen the seam here.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function bust()
		var m = managed
		m.clear()
	end 'bust'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	v.bust()
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:5: Unsupported: `__ManagedMemory` member 'clear' — this buffer is a `Vector`'s own record, and a vector's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record. 'clear' WRITES the record's length, which would leave the type saying one number while `get` and the walk answer another. A sized container's buffer refuses setLength/append/clear/remove and serves every other member of the surface; the growable `Array`'s buffer serves all of them, because an `Array`'s count IS its record's length
```

<!-- test: error.a-buffer-the-declaration-hands-out-reaches-the-same-refusal -->
⛔⛔ **ENTRANCE D — THE RECORD LEAVING BY `return`, AND THE CASE THAT SETTLED *WHERE* THE RULE BELONGS.**
A `ValueId` mark dies at a function boundary, so the first build re-derived the fact in the CALLER from the
callee's declaration — and that was measured over-refusing a `slice`, which has the very same type as the
record (see `the-slice-a-member-returns-is-served`). ⇒ The refusal moved to the one place the mark still
EXISTS: the `return` itself, inside the declaration. Note where it now points — line 6, the `return managed`,
not the caller's `got.clear()` — and that it is a DIFFERENT refusal from a length change, with its own
sentence, because *"you moved the number the type states"* and *"past here nothing can tell your record from
a slice of it"* are two injuries with two cures.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function buf() returns ElementMemory
		return managed
	end 'buf'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	var got = v.buf()
	got.clear()
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:3: Unsupported: a `Vector`'s own record may not be RETURNED from it — a vector's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record, and the two agree only while nothing moves the record's length. `__ManagedMemory` is equally the type of a `slice` of that record, whose length IS its own and may be moved freely, so once the value crosses a call NOTHING distinguishes the two and setLength/append/clear/remove would be served on the record itself. Hand out `slice(…)` instead, or do the work here, where the record is still known to be the vector's. The growable `Array` has no such rule, because an `Array`'s count IS its record's length
```

<!-- test: the-length-preserving-buffer-members-are-still-served -->
⭐⭐ **THE OVER-REFUSAL CONTROL, AND IT IS THE HALF A REFUSAL RUNG USUALLY FORGETS.** Everything on the
buffer surface that does NOT write the length word is still served on a sized container's buffer. Each
call here was measured leaving `length()` at its start value: `grow` moves capacity 4 to 64, `swap` and
the two shifts move elements the buffer already owns. Delete the length-writer list's gate and make the
refusal blanket, and THIS case is what goes red.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function survey() returns Int
		try managed.grow(64) otherwise ignore
		try managed.swap(0, 1) otherwise ignore
		try managed.shiftRight(0, 1) otherwise ignore
		try managed.shiftLeft(0, 1) otherwise ignore
		let e = try managed.get(0) otherwise panic("get")
		try managed.set(1, e) otherwise ignore
		return (managed.length() as Int) + (managed.capacity() as Int)
	end 'survey'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	return v.survey() as ExitCode
end 'main'
```
```exitcode
67
```

<!-- test: the-growable-arrays-buffer-still-changes-its-length -->
⭐⭐ **THE UNDER-REACH CONTROL — THE GROWABLE `Array`'s BUFFER KEEPS ALL FOUR, and this one cannot be
faked green, because `stdlib/Array.maxon` itself calls `managed.setLength` five times,
`managed.remove` twice and `managed.clear` once. A refusal that reached the growable record would fail
the STDLIB BUILD before it ever reached this case.** An `Array`'s count IS its record's length, so
moving the length is not a disagreement there — it is the operation.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Ints = Array with Int

function main() returns ExitCode
	var a = Ints.create()
	a.push(1)
	a.push(2)
	a.push(3)
	try a.managed.setLength(2) otherwise ignore
	try a.managed.remove(0) otherwise ignore
	let afterRemove = a.managed.length()
	a.managed.clear()
	return ((afterRemove * 10) + a.managed.length()) as ExitCode
end 'main'
```
```exitcode
10
```

<!-- test: error.a-buffer-handed-to-the-declarations-own-helper-reaches-the-same-refusal -->
⛔⛔ **ENTRANCE E — THE ARGUMENT DIRECTION OF D, AND IT WAS A LIVE WRONG ANSWER WITH EVERY OTHER ROUTE
ALREADY CLOSED.** Found by probing the CURE rather than the defect: with A–D shut, an `extension Vector` whose
`bust()` is `wipe(managed)` and whose `wipe(m ElementMemory)` is `m.clear()` still compiled, ran, and printed
**`count=3 walked=0`** — the rung's own reproducer, one helper method away.

⛔ **THE REFUSAL WAS FIRST SCOPED TO "another member of the SIZED CONTAINER", AND THAT SCOPE WAS ITSELF A
MEASURED HOLE** — see `error.a-buffer-handed-to-a-foreign-generic-reaches-the-same-refusal` below, which is
the same injury through a callee the sized container never heard of. The scope is gone; the ONE call the
corpus needs (`stdlib/Vector.maxon`'s `createIterator` handing the record to `VectorIter.create`) is exempted
by LOCATION — the declaring library may hand its own record around — and no user file is.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function bust()
		wipe(managed)
	end 'bust'

	function wipe(m ElementMemory)
		m.clear()
	end 'wipe'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	v.bust()
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:8: Unsupported: a `Vector`'s own record may not be passed to a call — a vector's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record, and the two agree only while nothing moves the record's length. `__ManagedMemory` is equally the type of a `slice` of that record, whose length IS its own and may be moved freely, so once the value crosses a call NOTHING distinguishes the two and setLength/append/clear/remove would be served on the record itself. Hand out `slice(…)` instead, or do the work here, where the record is still known to be the vector's. The growable `Array` has no such rule, because an `Array`'s count IS its record's length
```

<!-- test: the-slice-a-member-hands-to-a-helper-is-served -->
⭐⭐ **THE OVER-REFUSAL CONTROL FOR THE ARGUMENT ESCAPE, AND IT CAUGHT A REAL ONE.** The escape rule is about
the RECORD, and `managed.slice(0, 2)` is not the record — `__managed_slice` mints a fresh VIEW whose
`length@8` is its own, so clearing it cannot move the vector's length and no answer can disagree with the
type. ⛔ **The first build of this rung keyed the rule on the DECLARATION instead of on the value** — "a
`Vector` member's `ElementMemory` parameter is the vector's storage" — and a slice has exactly that type, so
THIS PROGRAM WAS REFUSED, with the sentence *"this buffer is a `Vector`'s own record"* about a thing that is
not. A legal program refused by a false claim, while the identical `s.clear()` written in place compiled and
ran. ⇒ The fact is carried on the value and never inferred from the declaration; delete that carry and this
case is what goes red. The exit code proves BOTH halves: the slice really was cleared (0) and the vector's
own record was untouched (3).
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function sliceThenClear() returns Int
		var s = try managed.slice(0, 2) otherwise panic("slice")
		return wipe(s) + (10 * (managed.length() as Int))
	end 'sliceThenClear'

	function wipe(m ElementMemory) returns Int
		m.clear()
		return m.length() as Int
	end 'wipe'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	return v.sliceThenClear() as ExitCode
end 'main'
```
```exitcode
30
```

<!-- test: the-slice-a-member-returns-is-served -->
⭐⭐ **THE OVER-REFUSAL CONTROL FOR THE RETURN ESCAPE — the twin of the case above, in the direction entrance
D refuses.** A member may hand a `slice` of its record to any caller and the caller may change it freely:
that is precisely what the escape refusal's own sentence tells the reader to do instead of returning the
record. So the two are one decision seen from both sides — the record may not leave, a view of it always may
— and a rule that could not tell them apart would make the advice impossible to follow.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function part() returns ElementMemory
		return try managed.slice(0, 2) otherwise panic("slice")
	end 'part'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	var got = v.part()
	got.clear()
	return ((got.length() as Int) + (10 * (v.count() as Int))) as ExitCode
end 'main'
```
```exitcode
30
```

## And The Record May Not LEAVE By Any Of The Other Three Doors Either (W192, second review)

⛔⛔ **THREE MORE ROUTES WERE MEASURED LIVE AFTER THE FIRST CURE LANDED, EACH PRINTING THE RUNG'S OWN
REPRODUCER `len=0 count=3` ON A `Vector with 3 Int`.** They share one cause, and it is worth stating once:
`bufferSurfaceOfDeclaredRecord` RETYPES the record to `Array with Element` so `VectorIter.create(managed)`
type-checks (W189). That retype makes the value indistinguishable from a growable array to everything except
the per-value MARK — so any route that carries the record somewhere a `ValueId` mark cannot follow hands
back a fully working `Array` over the vector's storage, and it is `stdlib/Array.maxon`'s own `clear()` that
answers on it, not merely the buffer roster's. ⇒ There is no third thing to test for at the far end. The
only defence is that the marked value never reaches a place the mark cannot go: a `return`, ANY call
argument, and any durable STORE.

<!-- test: error.a-buffer-pushed-into-a-container-reaches-the-same-refusal -->
**ROUTE F — THE RECORD AS A CONTAINER'S ELEMENT.** `Array with ElementMemory` is spellable inside the
declaration, `push` takes the record by reference, and `get` hands it back with no mark. MEASURED before this
refusal existed: `len=0 count=3 walked=0`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	typealias Bufs = Array with ElementMemory

	export function bust() returns Int
		var b = Bufs.create()
		b.push(managed)
		var got = try b.get(0) otherwise panic("no element")
		got.clear()
		return managed.length() as Int
	end 'bust'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	return v.bust() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:10: Unsupported: a `Vector`'s own record may not be stored into a container, a tuple or a field — a vector's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record, and the two agree only while nothing moves the record's length. `__ManagedMemory` is equally the type of a `slice` of that record, whose length IS its own and may be moved freely, so once the value crosses a call NOTHING distinguishes the two and setLength/append/clear/remove would be served on the record itself. Hand out `slice(…)` instead, or do the work here, where the record is still known to be the vector's. The growable `Array` has no such rule, because an `Array`'s count IS its record's length
```

<!-- test: error.a-buffer-put-in-a-tuple-reaches-the-same-refusal -->
**ROUTE G — THE RECORD AS A TUPLE SLOT**, which needs no container type spelled anywhere: two tokens of
punctuation launder the mark. ⭐ The slot read comes back on the ARRAY surface rather than the buffer one —
`t.0.count()` and `t.0.push(1)` both compiled, `t.0.length()` was refused as an unknown `Array` member —
which is the clearest single measurement of what the retype costs once the mark is gone.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function bust() returns Int
		let t = (managed, 1)
		t.0.clear()
		return (managed.length() as Int) + t.1
	end 'bust'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	return v.bust() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:11: Unsupported: a `Vector`'s own record may not be stored into a container, a tuple or a field — a vector's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record, and the two agree only while nothing moves the record's length. `__ManagedMemory` is equally the type of a `slice` of that record, whose length IS its own and may be moved freely, so once the value crosses a call NOTHING distinguishes the two and setLength/append/clear/remove would be served on the record itself. Hand out `slice(…)` instead, or do the work here, where the record is still known to be the vector's. The growable `Array` has no such rule, because an `Array`'s count IS its record's length
```

<!-- test: error.a-buffer-assigned-into-another-records-field-reaches-the-same-refusal -->
**ROUTE H — THE RECORD ASSIGNED INTO A GROWABLE `Array`'s OWN `managed` FIELD.** The growable record's buffer
carries no sized mark, so every length writer is served on it.

⚠⚠ **THE CRASH THIS ROUTE ALSO PRODUCED IS NOT THIS RULE'S, AND SAYING SO IS THE POINT.** Before the
refusal this program compiled and SEGFAULTED at run time, and the review first filed that as the escape's
doing. It is not. **The same `x.managed = <a buffer>` store segfaults with NO `Vector` in the program at
all** — coordinator-measured on this tip: `var b = Ints.create()` then `b.managed = a.managed` prints
`b=4557430888798830399`, which is `0x3f3f3f3f3f3f3f3f`, **eight `__mm_free` poison bytes read back as a
count**, and then exits **139**. The `managed` READ alone is clean (`let m = a.managed` gives `m=1 a=1`,
exit 0), so the fault is the field STORE. That is a separate, pre-existing release fault filed as its own
row; **this case refuses the ESCAPE, and the escape is a wrong answer, not the crash.** A refusal that
claimed the crash would be taking credit for curing a bug that is still live.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	typealias Arr = Array with Element

	export function bust() returns Int
		var a = Arr.create()
		a.managed = managed
		a.managed.clear()
		return managed.length() as Int
	end 'bust'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	return v.bust() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:3: Unsupported: a `Vector`'s own record may not be stored into a container, a tuple or a field — a vector's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record, and the two agree only while nothing moves the record's length. `__ManagedMemory` is equally the type of a `slice` of that record, whose length IS its own and may be moved freely, so once the value crosses a call NOTHING distinguishes the two and setLength/append/clear/remove would be served on the record itself. Hand out `slice(…)` instead, or do the work here, where the record is still known to be the vector's. The growable `Array` has no such rule, because an `Array`'s count IS its record's length
```

<!-- test: error.a-buffer-handed-to-a-foreign-generic-reaches-the-same-refusal -->
⛔⛔ **ROUTE I — THE ARGUMENT ESCAPE'S CALLEE SCOPE WAS THE HOLE.** The first cure refused an argument only
when the callee was declared on the sized container, on the argument that nothing else can name the type.
INSIDE an `extension Vector` the type IS nameable, so a user's own generic — no library, no `stdlib`, nine
lines — takes the record in and hands it straight back out with no mark on it. This case is why the scope is
gone and why the corpus's one legitimate escape is exempted by LOCATION instead: a callee ALLOWLIST would
have to be right about which of a foreign declaration's members merely read the record, which is not a fact
any declaration holds.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

type Box uses T
	var held as T

	static function create(held T) returns Self
		return Self{held: held}
	end 'create'

	export function get() returns T
		return held
	end 'get'
end 'Box'

extension Vector
	typealias BufBox = Box with ElementMemory

	export function bust() returns Int
		let b = BufBox.create(managed)
		let got = b.get()
		got.clear()
		return managed.length() as Int
	end 'bust'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	return v.bust() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:21:25: Unsupported: a `Vector`'s own record may not be passed to a call — a vector's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record, and the two agree only while nothing moves the record's length. `__ManagedMemory` is equally the type of a `slice` of that record, whose length IS its own and may be moved freely, so once the value crosses a call NOTHING distinguishes the two and setLength/append/clear/remove would be served on the record itself. Hand out `slice(…)` instead, or do the work here, where the record is still known to be the vector's. The growable `Array` has no such rule, because an `Array`'s count IS its record's length
```

<!-- test: a-slice-may-be-stored-passed-and-returned-freely -->
⭐⭐ **THE OVER-REFUSAL CONTROL FOR ALL THREE NEW DOORS, IN ONE PROGRAM.** Every route above is legal on a
`slice`, because a view's `length@8` is its own and no answer about it can contradict the vector's type. The
slice here is stored into a container, put in a tuple, passed to a foreign generic and cleared through each
— and the vector's own record is untouched at the end. Delete the mark test in
`refuseASizedContainersRecordEscaping` and make the three doors unconditional, and THIS case is what goes red.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

type Box uses T
	var held as T

	static function create(held T) returns Self
		return Self{held: held}
	end 'create'

	export function get() returns T
		return held
	end 'get'
end 'Box'

extension Vector
	typealias Bufs = Array with ElementMemory
	typealias BufBox = Box with ElementMemory

	export function survey() returns Int
		var b = Bufs.create()
		b.push(try managed.slice(0, 2) otherwise panic("slice"))
		var fromContainer = try b.get(0) otherwise panic("no element")
		fromContainer.clear()

		let t = (try managed.slice(0, 2) otherwise panic("slice"), 1)
		t.0.clear()

		let boxed = BufBox.create(try managed.slice(0, 2) otherwise panic("slice"))
		let fromBox = boxed.get()
		fromBox.clear()

		return (fromContainer.length() as Int) + (fromBox.length() as Int) + (10 * (managed.length() as Int))
	end 'survey'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	return v.survey() as ExitCode
end 'main'
```
```exitcode
30
```

## And A MERGE Is A Door Too — The Ternary And The `try` Fallback (W192, second review)

⛔⛔ **A PHI IS A NEW `ValueId`, SO EVERY PER-VALUE FACT STOPS AT A MERGE UNLESS SOMETHING CARRIES IT
ACROSS — AND THE WHOLE BUFFER-SURFACE FAMILY DID NOT.** The parser has three value merges
(`finalizeMatchMerge`, which serves the ternary AND `match … gives`; `finishValueTry`; and a
short-circuit's, whose phi is a bool and cannot be a buffer). Two facts were laundered by the first two,
and both were MEASURED:

* the SIZED mark — so an `extension Vector` could clear the vector's own record by routing the value
  through a join, with every other door already shut (`len=0 count=3`, the rung's own reproducer);
* the BUFFER mark itself — an **A2j roster inversion at the one position A2j did not reach**: on a
  plain growable array, `var m = a.managed if flag else a.managed` then `m.length()` was refused as an
  unknown `Array` member while `m.count()` compiled. Nothing to do with `Vector`; repaired by the same
  carry, and pinned by the third case here.

<!-- test: error.a-buffer-through-a-ternary-reaches-the-same-refusal -->
**ROUTE J — THE TERNARY MERGE.** The degenerate `x if c else x` is deliberate: both edges are the same
expression, so nothing about the program needs two answers — only the phi's fresh `ValueId` did.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function bust(flag bool) returns Int
		var m = managed if flag else managed
		m.clear()
		return managed.length() as Int
	end 'bust'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	return v.bust(true) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:5: Unsupported: `__ManagedMemory` member 'clear' — this buffer is a `Vector`'s own record, and a vector's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record. 'clear' WRITES the record's length, which would leave the type saying one number while `get` and the walk answer another. A sized container's buffer refuses setLength/append/clear/remove and serves every other member of the surface; the growable `Array`'s buffer serves all of them, because an `Array`'s count IS its record's length
```

<!-- test: error.a-buffer-through-a-try-fallback-reaches-the-same-refusal -->
**ROUTE K — THE `try … otherwise` MERGE**, which needs no second spelling of the record at all: the ok
edge is a legal `slice` and only the FALLBACK is the record, so a rule that looked at either edge alone
would have to be right about both.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function bust() returns Int
		var m = try managed.slice(0, 99) otherwise managed
		m.clear()
		return managed.length() as Int
	end 'bust'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	return v.bust() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:5: Unsupported: `__ManagedMemory` member 'clear' — this buffer is a `Vector`'s own record, and a vector's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record. 'clear' WRITES the record's length, which would leave the type saying one number while `get` and the walk answer another. A sized container's buffer refuses setLength/append/clear/remove and serves every other member of the surface; the growable `Array`'s buffer serves all of them, because an `Array`'s count IS its record's length
```

<!-- test: a-growable-buffer-keeps-its-own-roster-through-a-merge -->
⭐⭐ **THE OTHER HALF OF THE CARRY, AND IT IS A REPAIR RATHER THAN A REFUSAL.** The merged value here is
an ordinary growable buffer: it must come out on the BUFFER roster, which is what A2j ruled a value
spelled `__ManagedMemory` is on, and before the carry existed it came out on the `Array` roster instead —
`setLength` and `length` both refused as unknown `Array` members. The exit code proves both halves: the
length really moved to 1 through the phi, and the array's own `count()` followed it.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Ints = Array with Int

function main() returns ExitCode
	var a = Ints.create()
	a.push(1)
	a.push(2)
	let flag = a.count() > 0
	var m = a.managed if flag else a.managed
	try m.setLength(1) otherwise ignore
	return ((m.length() as Int) * 10 + (a.count() as Int)) as ExitCode
end 'main'
```
```exitcode
11
```

<!-- test: an-extension-still-walks-its-own-vector -->
⭐⭐ **THE OVER-REFUSAL CONTROL FOR THE WIDENED CALL ESCAPE, AND IT IS THE ONE THE WIDENING COULD PLAUSIBLY
HAVE BROKEN.** Dropping the callee scope means `for e in managed` — which hands the record to an iterator
factory — is now refused inside an `extension Vector`, and that is deliberate: the compiler cannot know a
foreign callee only READS, and `ArrayIterator` is compiled for the growable `Array` and the sized `Vector`
alike, so "this record's count comes from a type" is not a fact its declaration can hold. What must survive
is the way an extension actually walks its own vector, and both spellings do: `for e in self`, which goes
through the declaration's own `createIterator`, and the one the refusal's sentence names, a `slice`. The
exit code is the two walks side by side.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

extension Vector
	export function walkedTwoWays() returns Int
		var viaSelf = 0
		for e in self 'eachOwn'
			viaSelf = viaSelf + (e - e) + 1
		end 'eachOwn'

		var viaSlice = 0
		for e in try managed.slice(0, countof(Self)) otherwise panic("slice") 'eachSliced'
			viaSlice = viaSlice + (e - e) + 1
		end 'eachSliced'

		return (viaSelf * 10) + viaSlice
	end 'walkedTwoWays'
end 'Vector'

function main() returns ExitCode
	var v = Vec3.create()
	return v.walkedTwoWays() as ExitCode
end 'main'
```
```exitcode
33
```
