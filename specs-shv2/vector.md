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

<!-- disabled-test: capacity-is-part-of-instance-identity -->
<!-- MISSING MECHANISM: `Vector with <N> <type parameter>` inside a generic body. shv2 refuses this
     cleanly and positionally (E2015 — no statically known element stride to size inline storage
     with), because dictionary-passing shares ONE body across instantiations while a fixed-size
     vector field needs its stride at LAYOUT time. Supplied by board row `S2u`; re-enable there.
     The sibling cases `error.wrong-size-vector-argument` and `same-size-aliases-are-one-type`
     from this same canonical section DO pass, so the size-is-part-of-identity rule itself holds
     wherever the element type is concrete. -->
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

<!-- disabled-test: distinct-capacities-are-distinct-instances -->
<!-- MISSING MECHANISM: `Vector with <N> <type parameter>` inside a generic body. shv2 refuses this
     cleanly and positionally (E2015 — no statically known element stride to size inline storage
     with), because dictionary-passing shares ONE body across instantiations while a fixed-size
     vector field needs its stride at LAYOUT time. Supplied by board row `S2u`; re-enable there.
     The sibling cases `error.wrong-size-vector-argument` and `same-size-aliases-are-one-type`
     from this same canonical section DO pass, so the size-is-part-of-identity rule itself holds
     wherever the element type is concrete. -->
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
