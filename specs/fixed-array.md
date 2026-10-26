---
feature: fixed-array
status: experimental
keywords: [fixed array, fixed size, stack, collection, generic]
category: stdlib
---

# FixedArray

## Documentation

### Overview

`FixedArray` is a generic fixed-size collection. A `FixedArray with N bool` is bit-packed
(8 elements per byte, `element_size = -1`) exactly like `Array with bool` — a
`FixedArray with 16 bool` allocates a 2-byte buffer, not 16 — and the packing is
transparent to the `get`/`set`/iterate API. See [bool-bit-packing](bool-bit-packing.md).

### Creating Fixed Arrays

Create a concrete fixed array type using `typealias` with element type and size:

```text
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int
var v = Vec3.create()  // zero-initialized, 3 elements on the stack
```

The size is part of the type. A `FixedArray with 3 Int` is a different type from `FixedArray with 4 Int`.

### Creating from Array Literals

Fixed arrays implement `BuiltinArrayLiteral`, so you can initialize them from an array literal using `from`. The element type and size are inferred from the literal:

```text
var v = FixedArray from [10, 20, 30]  // inferred as FixedArray with 3 Int
```

The inferred type is compatible with a typealias of the same element type and size, so a `FixedArray from [...]` can be passed to a function expecting the typealias:

```text
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function process(v Vec3) returns Int
  return v.get(0)
end 'process'

var v = FixedArray from [10, 20, 30]
process(v)  // works — inferred type matches Vec3
```

### Element Access

Access elements with `.get()` and modify them with `.set()`. An index the compiler can prove inside
the fixed array makes the access total: it cannot fail, so it is written bare, and a `try` on it is
refused (E3055).

```text
var value = v.get(0)
v.set(0, value: 42)
```

An index is proven when it is a known constant inside `0 to N-1` (a literal, a `let` bound to one, or
a folded expression such as `countof(Vec3) - 1`), or when its declared ranged type lies inside
`0 to N-1`:

```text
typealias Slot = int(0 to 2) implements ElementIndex

function middle(v Vec3, at Slot) returns Int
  return v.get(at)
end 'middle'
```

A constant index at or past the size is refused at compile time (E3005). Any other index — an
`ElementIndex` or `Count` parameter, a loop variable, a call result, an index inside a generic body over
`Self` — may be out of range at run time, so the access throws `ArrayError.indexOutOfBounds` and needs
`try` (E3057 without it):

```text
var value = try v.get(i) otherwise 0
try v.set(i, value: 42) otherwise ignore
```

### Size and Count

The `.count()` method always returns the fixed size of the fixed array:

```text
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int
var v = Vec4.create()
var n = v.count()  // always 4
```

### Stack vs Heap

Fixed arrays are designed for small, fixed-size data. The compiler places the storage on the stack when the total byte size (element size x count) is 8192 bytes or less. Larger fixed arrays are automatically heap-allocated.

```text
typealias Int = int(i64.min to i64.max)
typealias SmallVec = FixedArray with 100 Int    // 800 bytes → stack
typealias LargeVec = FixedArray with 2000 Int   // 16000 bytes → heap
```

### Use Cases

Fixed arrays are ideal for:
- Small fixed-size collections (coordinates, colors, matrices)
- Performance-sensitive code where heap allocation is undesirable
- Types with a known compile-time size

```text
typealias Float = float(f64.min to f64.max)
typealias Byte = int(0 to u8.max)
typealias Point3D = FixedArray with 3 Float
typealias Color = FixedArray with 4 Byte      // RGBA
typealias Mat2x2 = FixedArray with 4 Float    // 2x2 matrix stored flat
```

### Iteration

Fixed arrays support `for-in` loops:

```text
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int
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
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function main() returns ExitCode
	let v = Vec3.create()
	return v.get(0)
end 'main'
```
```exitcode
0
```
<!-- test: count -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	return v.count()
end 'main'
```
```exitcode
4
```

<!-- test: set-and-get -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	v.set(0, value: 42)
	return v.get(0)
end 'main'
```
```exitcode
42
```
<!-- test: set-all-elements -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	v.set(0, value: 10)
	v.set(1, value: 20)
	v.set(2, value: 30)
	let a = v.get(0)
	let b = v.get(1)
	let c = v.get(2)
	return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: get-out-of-bounds -->
Reading at an index beyond the fixed size that is only known at run time throws ArrayError.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec2 = FixedArray with 2 Int

function runtimeIndex(i ElementIndex) returns ElementIndex
	return i
end 'runtimeIndex'

function main() returns ExitCode
	var v = Vec2.create()
	v.set(0, value: 10)
	let result = try v.get(runtimeIndex(5)) otherwise -1
	print("{result}\n")
	return 0
end 'main'
```
```stdout
-1
```

<!-- test: error.get-at-a-constant-index-past-the-size-is-refused -->
A constant index beyond the fixed size is refused at compile time.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec2 = FixedArray with 2 Int

function main() returns ExitCode
	var v = Vec2.create()
	let result = try v.get(5) otherwise -1
	print("{result}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:21: Value 5 is outside the range of the indices of 'FixedArray with 2' (int(0 to 1))
```

<!-- test: set-out-of-bounds-throws -->
Writing at an index beyond the fixed size that is only known at run time throws ArrayError.indexOutOfBounds.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec2 = FixedArray with 2 Int

function runtimeIndex(i ElementIndex) returns ElementIndex
	return i
end 'runtimeIndex'

function main() returns ExitCode
	var v = Vec2.create()
	v.set(0, value: 10)
	try v.set(runtimeIndex(5), value: 99) otherwise 'oob'
		return 7
	end 'oob'
	return v.get(0)
end 'main'
```
```exitcode
7
```

<!-- test: error.set-at-a-constant-index-past-the-size-is-refused -->
A constant index beyond the fixed size is refused at compile time for a write too.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec2 = FixedArray with 2 Int

function main() returns ExitCode
	var v = Vec2.create()
	try v.set(5, value: 99) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:8: Value 5 is outside the range of the indices of 'FixedArray with 2' (int(0 to 1))
```

<!-- test: single-element -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec1 = FixedArray with 1 Int

function main() returns ExitCode
	var v = Vec1.create()
	v.set(0, value: 77)
	return v.get(0)
end 'main'
```
```exitcode
77
```

<!-- test: larger-fixed-array -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec10 = FixedArray with 10 Int

function main() returns ExitCode
	var v = Vec10.create()
	var i = 0 as Int
	while i < 10 'fill'
		try v.set(i as ElementIndex, value: i * 10) otherwise panic("test invariant: set OOB")
		i = i + 1
	end 'fill'
	let first = v.get(0)
	let last = v.get(9)
	return first + last
end 'main'
```
```exitcode
90
```

<!-- test: count-single -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec1 = FixedArray with 1 Int

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
typealias Vec3 = FixedArray with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	v.set(1, value: 10)
	v.set(1, value: 42)
	return v.get(1)
end 'main'
```
```exitcode
42
```

<!-- test: float-fixed-array -->
```maxon
typealias Float = float(f64.min to f64.max)
typealias Vec2F = FixedArray with 2 Float

function main() returns ExitCode
	var v = Vec2F.create()
	v.set(0, value: 2.5)
	v.set(1, value: 3.5)
	let a = v.get(0)
	let b = v.get(1)
	return trunc(a + b)
end 'main'
```
```exitcode
6
```

<!-- test: byte-fixed-array -->
```maxon

typealias Byte = int(0 to u8.max)
typealias ByteVec4 = FixedArray with 4 Byte

function main() returns ExitCode
	var v = ByteVec4.create()
	v.set(0, value: 10)
	v.set(1, value: 20)
	v.set(2, value: 30)
	v.set(3, value: 40)
	let a = v.get(0)
	let b = v.get(3)
	return a + b
end 'main'
```
```exitcode
50
```

<!-- test: pass-to-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias Vec3 = FixedArray with 3 Integer

function sum(v Vec3) returns Integer
	let a = v.get(0)
	let b = v.get(1)
	let c = v.get(2)
	return a + b + c
end 'sum'

function main() returns ExitCode
	var v = Vec3.create()
	v.set(0, value: 10)
	v.set(1, value: 20)
	v.set(2, value: 12)
	return sum(v)
end 'main'
```
```exitcode
42
```

<!-- test: return-from-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias Vec2 = FixedArray with 2 Integer

function makeVec(a Integer, b Integer) returns Vec2
	var v = Vec2.create()
	v.set(0, value: a)
	v.set(1, value: b)
	return v
end 'makeVec'

function main() returns ExitCode
	let v = makeVec(30, b: 12)
	let a = v.get(0)
	let b = v.get(1)
	return a + b
end 'main'
```
```exitcode
42
```

<!-- test: iterate -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	v.set(0, value: 1)
	v.set(1, value: 2)
	v.set(2, value: 3)
	v.set(3, value: 4)
	var sum = 0 as Int
	for elem in v 'loop'
		sum = sum + elem
	end 'loop'
	return sum
end 'main'
```
```exitcode
10
```

<!-- test: let-fixed-array-read -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function makeVec() returns Vec3
	var v = Vec3.create()
	v.set(0, value: 10)
	v.set(1, value: 20)
	v.set(2, value: 12)
	return v
end 'makeVec'

function main() returns ExitCode
	let v = makeVec()
	let a = v.get(0)
	let b = v.get(1)
	let c = v.get(2)
	return a + b + c
end 'main'
```
```exitcode
42
```

<!-- test: from-array-literal -->
```maxon
function main() returns ExitCode
	let v = FixedArray from [10, 20, 30]
	return v.get(0)
end 'main'
```
```exitcode
10
```

<!-- test: from-array-literal-sum -->
```maxon
function main() returns ExitCode
	let v = FixedArray from [10, 20, 30]
	let a = v.get(0)
	let b = v.get(1)
	let c = v.get(2)
	return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: from-array-literal-float -->
```maxon
function main() returns ExitCode
	let v = FixedArray from [1.5, 2.5]
	let a = v.get(0)
	let b = v.get(1)
	return trunc(a + b)
end 'main'
```
```exitcode
4
```

<!-- test: from-array-literal-iterate -->
```maxon
function main() returns ExitCode
	let v = FixedArray from [1, 2, 3, 4]
	var sum = 0 as FullRangeInt
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
	let v = FixedArray from [99]
	return v.get(0)
end 'main'
```
```exitcode
99
```

<!-- test: from-literal-typealias-compatible -->
The inferred type from a literal is compatible with a typealias of the same element type and size.
```maxon

typealias Integer = int(i64.min to i64.max)

typealias Vec3 = FixedArray with 3 Integer

function sum(v Vec3) returns Integer
	let a = v.get(0)
	let b = v.get(1)
	let c = v.get(2)
	return a + b + c
end 'sum'

function main() returns ExitCode
	var v = Vec3.create()
	v.set(0, value: 10)
	v.set(1, value: 20)
	v.set(2, value: 12)
	return sum(v)
end 'main'
```
```exitcode
42
```

<!-- test: accumulate-sum -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec5 = FixedArray with 5 Int

function main() returns ExitCode
	var v = Vec5.create()
	v.set(0, value: 1)
	v.set(1, value: 2)
	v.set(2, value: 3)
	v.set(3, value: 4)
	v.set(4, value: 5)
	var sum = 0 as Int
	var i = 0 as Count
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

<!-- test: bool-fixed-array-cross-byte -->
```maxon
typealias Bits16 = FixedArray with 16 bool

function main() returns ExitCode
	var v = Bits16.create()
	v.set(0, value: true)
	v.set(3, value: true)
	v.set(8, value: true)
	v.set(15, value: true)
	var count = 0 as FullRangeInt
	var i = 0 as Count
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

<!-- test: bool-fixed-array-overwrite-clear -->
```maxon
typealias Bits8 = FixedArray with 8 bool

function main() returns ExitCode
	var v = Bits8.create()
	v.set(2, value: true)
	v.set(5, value: true)
	v.set(2, value: false)
	let a = v.get(2)
	let b = v.get(5)
	var r = 0 as FullRangeInt
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

<!-- test: bool-fixed-array-from-literal -->
```maxon
function main() returns ExitCode
	let v = FixedArray from [true, false, true, true, false, true, false, false, true]
	var count = 0 as FullRangeInt
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

A `FixedArray with 3 Int` and a `FixedArray with 4 Int` are two types, wherever they are reached from: a
declared alias, a field of a generic type, or a synthesized instance nothing has named.

<!-- test: capacity-is-part-of-instance-identity -->
The size is part of the type, so a generic type's capacity-4 field must keep that capacity
rather than adopting a declared `FixedArray with 3` alias that happens to share its element type.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

type Holder uses Element
	typealias Slot = FixedArray with 4 Element

	var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	function size() returns Int
		return slot.count()
	end 'size'
end 'Holder'

typealias IntHolder = Holder with Int

function main() returns ExitCode
	var v = Vec3.create()
	var h = IntHolder.create()
	return ((v.count() as Int) + h.size()) as ExitCode
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
	typealias Slot4 = FixedArray with 4 Element

	var quad as Slot4

	static function create() returns Self
		return Self{quad: Slot4.create()}
	end 'create'

	function size() returns Int
		return quad.count()
	end 'size'
end 'Holder4'

type Holder7 uses Element
	typealias Slot7 = FixedArray with 7 Element

	var septet as Slot7

	static function create() returns Self
		return Self{septet: Slot7.create()}
	end 'create'

	function size() returns Int
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

<!-- test: error.wrong-size-fixed-array-argument -->
The size is part of the type, so a differently-sized fixed array is not a widening — it is a different
type, and passing one where the other is declared is refused rather than silently accepted.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int
typealias Vec4 = FixedArray with 4 Int

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

<!-- test: error.same-size-aliases-are-two-brands -->
Two names for one size are one INSTANCE under two brands, and the brand is the type: a `Triple` does
not reach a `Vec3` parameter unasked (`nominal-generic-alias.md`).
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int
typealias Triple = FixedArray with 3 Int

function first(v Vec3) returns Int
	return v.get(0)
end 'first'

function main() returns ExitCode
	var t = Triple.create()
	t.set(0, value: 21)
	let f = first(t)
	print("{f} {t.count()}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:13:10: argument type mismatch for 'v': expected 'Vec3', got 'Triple'
```

<!-- test: same-size-aliases-cross-through-as -->
The legal twin: `t as Vec3` re-brands the same three slots, and separating the sizes has not separated
two spellings of one size.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int
typealias Triple = FixedArray with 3 Int

function first(v Vec3) returns Int
	return v.get(0)
end 'first'

function main() returns ExitCode
	var t = Triple.create()
	t.set(0, value: 21)
	return (first(t as Vec3) + (t.count() as Int)) as ExitCode
end 'main'
```
```exitcode
24
```

## Every Member Is The Declaration's, And A Member It Does Not Have Is Refused Off It

⭐⭐⭐ **THERE IS NO SYNTHESIZED ROSTER.** `create`, `count`, `get` and `set` are
`stdlib/FixedArray.maxon`'s, so there is no synthesized surface to render a sentence from, and a member a
`FixedArray` does not have is a METHOD MISS off the declaration (`E4006`), exactly as it is for a `List`
or a `Map`.

⛔⛔ **THE FIVE CASES BELOW PROBE A LANDMINE**: a fixed array with no dispatch arm of its own could fall
through to `dispatchArrayMethod` and be served the GROWABLE surface — *"a fixed-size container grown
through its own type, a WRONG ANSWER rather than a refusal"*. It is defused, and by construction rather
than by luck: `dispatchMethodOnReceiver`'s array arm tests `isArrayInstanceAt`, which reads the base NAME,
and a `FixedArray`'s base is not an `Array`'s. All five growth spellings are pinned.

<!-- test: error.push-is-refused-off-the-declaration -->
The spelling `W86` named first, and the one a hijacked receiver would have ANSWERED rather than refused.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	v.push(4)
	return v.count()
end 'main'
```
```maxoncstderr
error E4006: <fragment>:7:4: Type 'FixedArray' has no method named 'push'
```

<!-- test: error.resize-is-refused-off-the-declaration -->
The growth spelling that, served, would let `resize(9)` then `count()` answer **9** — a three-element
type grown to nine with no diagnostic.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	v.resize(9)
	return v.count()
end 'main'
```
```maxoncstderr
error E4006: <fragment>:7:4: Type 'FixedArray' has no method named 'resize'
```

<!-- test: error.clear-is-refused-off-the-declaration -->
`clear` empties the record. A fixed-size container whose `count()` could reach 0 is not fixed.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	v.clear()
	return v.count()
end 'main'
```
```maxoncstderr
error E4006: <fragment>:7:4: Type 'FixedArray' has no method named 'clear'
```

<!-- test: error.insert-is-refused-off-the-declaration -->
⚠ **`insert` AND `remove` ARE REFUSED WITHOUT BEING LISTED ANYWHERE.** Neither is declared, so neither
is served — no typed-out list of refused members exists to drift.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	try v.insert(0, value: 4) otherwise panic("insert")
	return v.count()
end 'main'
```
```maxoncstderr
error E4006: <fragment>:7:8: Type 'FixedArray' has no method named 'insert'
```

<!-- test: error.remove-is-refused-off-the-declaration -->
The second of that pair, and the one that SHRINKS.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	return try v.remove(0) otherwise 0
end 'main'
```
```maxoncstderr
error E4006: <fragment>:7:15: Type 'FixedArray' has no method named 'remove'
```

<!-- test: error.a-bare-growth-call-inside-an-extension-body-is-refused -->
⛔⛔ **THE SECOND ENTRANCE `W86`'s LANDMINE COULD HAVE COME IN BY, AND IT IS SHUT FOR A DIFFERENT REASON
FROM THE FIVE ABOVE.** `implicitSelfTakesTheArrayRoster` gives a BARE call inside a container's own body
the ARRAY roster, which is what makes `stdlib/Array.maxon`'s own `contains(sequence)` reach `get`. Its gate
is `declarationIsTheManagedRecord` — the GROWABLE record's declaration and not
`declarationsValueIsTheManagedRecord`, which is the `or` that also admits the sized one — so an
`extension FixedArray` body does not take that roster and a bare `push` resolves as an ordinary sibling call to
a method nothing declares.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function grow() returns Int
		push(4)
		return 0
	end 'grow'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	return v.grow()
end 'main'
```
```maxoncstderr
error E3004: <fragment>:7:3: call to undefined function 'push'
```

<!-- test: error.the-buffer-under-a-fixed-array-is-still-the-buffer -->
<!-- tier: stdlib -->
⛔ **THE THIRD ENTRANCE.** `bufferSurfaceOfDeclaredRecord` deliberately RETYPES a sized container's
`managed` read to `Array with Element`, so that `stdlib/FixedArray.maxon`'s own `FixedArrayIter.create(managed)`
type-checks — one record, two ids. The retype is a TYPE and not a surface: the value is still marked the
BUFFER's, so what it serves is `bufferSurfaceMemberNames` and the growable array's members are not on it.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function grow() returns Int
		managed.push(4)
		return 0
	end 'grow'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	return v.grow()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:11: Unsupported: `__ManagedMemory` member 'push' — the compiler provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

## The Members Are Declared FUNCTIONS, Which Is A Thing A Dispatch Arm Cannot Be

⭐⭐⭐ **THE DIFFERING-DECLARATIONS CONTROL, IN THE SUITE.** A compiler-served dispatch arm for `count`,
`get` or `set` would give the same ANSWERS the declared members give, which makes a value-COMPARING
control impossible to write: both roads answer 42. What separates them is not the value but the KIND of
thing that produced it. A dispatch arm is reachable through
one syntax and nothing else; a declared method is a function of the program, and Maxon lets an instance
method be named statically with its receiver as the first argument (`Adder.bump(a)`, which
`parseQualifiedCall`'s header settles as legal). **`FixedArray.get(v, index: 1)` therefore cannot compile while
`get` is a dispatch arm — there is no such function to call — and it is a RUNTIME ANSWER rather than a
message text.**

⚠ It also exercises the corpus body end to end: sabotaging `stdlib/FixedArray.maxon`'s `get` to `throw
ArrayError.indexOutOfBounds` turns this case red where the intact declaration answers 42, which is the
control `stdlib-loading.md` runs for a stdlib module.

<!-- test: the-members-are-functions-and-answer-to-their-static-spelling -->
Both declared accessors, named statically, with the receiver passed as the first argument and the
declaration's own `index:`/`value:` labels on the rest. The index is not proven, so both calls reach the
declared bodies.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function runtimeIndex(i ElementIndex) returns ElementIndex
	return i
end 'runtimeIndex'

function main() returns ExitCode
	var v = Vec3.create()
	try FixedArray.set(v, index: runtimeIndex(1), value: 41) otherwise panic("set")
	return (try FixedArray.get(v, index: runtimeIndex(1)) otherwise 0) + 1
end 'main'
```
```exitcode
42
```

<!-- test: the-static-spelling-at-a-proven-index-needs-no-try -->
The static spelling is the same call, so a proven index makes it total as well.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	FixedArray.set(v, index: 1, value: 41)
	return FixedArray.get(v, index: 1) + 1
end 'main'
```
```exitcode
42
```

<!-- test: the-count-is-a-function-too -->
The same for `count`, which had no arguments at all to tell the two roads apart by — so the static spelling
is the whole of the difference. Under the dispatch arm it folded to a literal at the CALL and `FixedArray.count`
named nothing.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec5 = FixedArray with 5 Int

function main() returns ExitCode
	var v = Vec5.create()
	return FixedArray.count(v) as ExitCode
end 'main'
```
```exitcode
5
```

## A `let` Fixed Array Still Refuses The Write, By The Declaration's Own Rule

⚖ **A `let` FIXED ARRAY IS REFUSED THE WRITE BY THE ORDINARY PARAMETER-MUTATION RULE, NOT BY A ROSTER.**
`stdlib/FixedArray.maxon`'s `set` writes through its receiver (`managed.set`), so the refusal (E3159) comes from the
declaration, as it does for `Set`'s and `Map`'s own mutators.

⚠ **THE SENTENCE NAMES THE PARAMETER**, and that is the whole of what this case pins: it names the
PARAMETER the callee declares, not a surface. A case that asserted only "refused" would not see the
difference.

<!-- test: error.a-write-through-a-let-fixed-array-is-refused-off-the-declaration -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function main() returns ExitCode
	let v = Vec3.create()
	v.set(0, value: 7)
	return v.get(0)
end 'main'
```
```maxoncstderr
error E3159: <fragment>:7:4: cannot pass 'v', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
```

## A Fixed Array Global Takes The Declared Generic's Road

⭐⭐ **THE DECLARED GENERIC'S ROAD, NOT THE BUILTIN ARM.** With `create` declared,
`instanceCreateIsDeclared` routes a top-level `var g = Vec3.create()` through
`requireDeclaredGenericGlobalCreate` rather than `containerCreateCall`'s BUILTIN arm (a runtime
`__managed_create` handed the strides it cannot look up, plus one `__managed_resize` to size the
record). Its three premises are the ones
[top-level-factory-globals](top-level-factory-globals.md) pins for every declared generic: the `create()`
must EXIST, be NAMEABLE from its declaring file, and return `Self`. `__module_init` then emits a bare
`call FixedArray.create` with no stamps at all, and the record it hands back is already published.

<!-- test: a-fixed-array-global-is-built-by-the-declarations-own-static -->
The global is filled from one function and read from another, so what is under test is the slot
`__module_init` wrote before `main` and not a local the same statement built.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

var shared = Vec3.create()

function fill() returns Int
	shared.set(0, value: 20)
	shared.set(2, value: 21)
	return 0
end 'fill'

function main() returns ExitCode
	_ = fill()
	return (shared.get(0) + shared.get(2) + (shared.count() as Int)) as ExitCode
end 'main'
```
```exitcode
44
```

## The Fixed Size Is The Bound, And The Buffer Under It Is Not

⛔⛔ **IT PINS A WRONG ANSWER A FORWARDING `set` WOULD GIVE.** `stdlib/FixedArray.maxon`'s `set` forwards to `managed.set`, and the BUFFER surface's setter is
bounded by CAPACITY where the array surface's is bounded by LENGTH (recorded at
`ManagedMemoryRuntime.ManagedMemSetName`: the wider bound is what makes the stage-then-`setLength` idiom
spellable). A fixed array's capacity is NOT its count — `Vec3.create()` grows through
`stdlib/Array.maxon`'s policy, whose `MinimumCapacity` is **4**, so a three-element fixed array's record has a
fourth slot the type does not have.

**With `set` forwarding straight to the buffer**, `made.set(3, value: 99)` on a `Vec3.create()`
SUCCEEDS — a write to the fourth slot of a three-slot type, no diagnostic, `count()` unmoved at 3. So the
guard here is not belt-and-braces, it is THE WHOLE OF THE FIXED ARRAY'S BOUND — `stdlib/Array.maxon`'s `set`
carries the identical guard for the identical reason, and its header says so.

⚠ The two spellings below are the two ways a fixed array comes into being, and they have DIFFERENT capacities
(a literal's is the array literal's, a `create()`'s is the growth policy's) — so a case over only one of
them would pass on the other's arithmetic.

<!-- test: a-write-past-the-fixed-size-is-refused-however-the-fixed-array-was-built -->
Both fixed arrays hold three elements and both refuse index 3. The index is laundered through a call so no
constant is in the compiler's hands at the write — what is under test is the RUNTIME bound, and a folded
index would be answering a different question.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function runtimeIndex(n Int) returns Int
	return n
end 'runtimeIndex'

function main() returns ExitCode
	var made = Vec3.create()
	var lit = FixedArray from [1, 2, 3]
	var refused = 0 as Int
	try made.set(runtimeIndex(3) as ElementIndex, value: 99) otherwise 'madePastEnd'
		refused = refused + 1
	end 'madePastEnd'
	try lit.set(runtimeIndex(3) as ElementIndex, value: 99) otherwise 'litPastEnd'
		refused = refused + 1
	end 'litPastEnd'
	let madeRead = try made.get(runtimeIndex(3) as ElementIndex) otherwise -1
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

⚠ This is the other half of the shared-body mechanism. A
`FixedArray with <N> <type parameter>` is created ONCE, in a body every instantiation shares, and `create()`
publishes all N slots by zeroing them. A zeroed slot is an element for a trivial instantiation and a NULL
for a managed one, and the shared body cannot branch on which. Ungated, the program below with a
`for … in` read added has `count()` answer 4 while every `get` reports an empty slot (exit 0, no
diagnostic), and the loop read **SEGFAULTS**. It is the concrete managed element's refusal
(`error.a-concrete-managed-element-is-refused`, just below) asked of the thing that is knowable inside a
shared body — the instantiation set — rather than of an element type there is none of.

<!-- test: a-fixed-array-field-in-a-generic-body-round-trips-its-elements -->
The CAPABILITY itself, which the two cases above do not reach. Both of them only call `count()`, and a fixed array's count folds to a literal off the instance's
own size, so **they would both still pass if every slot read and wrote the wrong address.** This one
drives `set` and `get` through the shared generic body: a written slot reads back, an UNWRITTEN slot
reads back the published zero (which is the whole reason the managed instantiation is refused), and the
count is unchanged by the writes: `a=10 b=7 zero=0 count=4`, exit 17.
```maxon
typealias Int = int(i64.min to i64.max)

type Holder uses Element
	typealias Slot = FixedArray with 4 Element

	var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	function put(index Int, value Element)
		try slot.set(index as ElementIndex, value: value) otherwise panic("put")
	end 'put'

	function at(index Int) returns Element
		return try slot.get(index as ElementIndex) otherwise panic("at")
	end 'at'

	function size() returns Int
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

<!-- test: error.a-managed-instantiation-of-a-fixed-array-field-is-refused -->
A type parameter can be instantiated at a managed type, so a fixed array over one is refused at
the `create()` that would publish its slots — not at the instantiation, and not at run time.

⚠ **THE REFUSAL IS A STATED POSITION RATHER THAN AN OVERSIGHT.** Ungated, this exact source compiles and **runs to exit 4**: `count()` answers 4 because four slots were published, and nothing here ever READS one. It is the read that is unsound — a zeroed slot is a value for a trivial element and a NULL for a managed one — so exit 4 is the hazard not yet triggered, not evidence that the hazard does not exist. The compiler refuses at compile time what would otherwise break at run time; the same shape with a `for … in` read **SEGFAULTS (139)** on an ungated the compiler. ⇒ this extends the position the compiler already holds for a CONCRETE managed element (see `error.a-concrete-managed-element-is-refused` below) to the instantiated case, so the two arms of one rule agree. **The corpus `stdlib/FixedArray.maxon` is generic over its element where the compiler's synthesized fixed array is scalar-only**; that gap is owned by the `FixedArray` retirement chain, not by this case.
```maxon
typealias Int = int(i64.min to i64.max)

type Holder uses Element
	typealias Slot = FixedArray with 4 Element

	var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	function size() returns Int
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
error E2015: <fragment>:10:21: Unsupported: `FixedArray with <N> <type parameter>` — a fixed array PUBLISHES all N of its slots at `create` by zeroing them, and this generic type is instantiated with a type whose slot is a heap POINTER: a zeroed slot is an element for a trivial instantiation and a NULL for a managed one, so `count()` would answer N while every `get` reports an empty slot and a `for … in` read dereferences the null. Instantiate this type at integer or bool elements only — a `float` TYPE ARGUMENT is refused separately today, for its own reason — or hold the elements in an `Array with <type parameter>`, which publishes nothing and grows by `push`
```

<!-- test: error.one-managed-instantiation-refuses-the-shared-body -->
The body is compiled ONCE and serves every instantiation, so the rule is EXISTENTIAL: one managed
instantiation refuses it, and a trivial instantiation standing beside it does not buy it back.
```maxon
typealias Int = int(i64.min to i64.max)

type Holder uses Element
	typealias Slot = FixedArray with 4 Element

	var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	function size() returns Int
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
error E2015: <fragment>:10:21: Unsupported: `FixedArray with <N> <type parameter>` — a fixed array PUBLISHES all N of its slots at `create` by zeroing them, and this generic type is instantiated with a type whose slot is a heap POINTER: a zeroed slot is an element for a trivial instantiation and a NULL for a managed one, so `count()` would answer N while every `get` reports an empty slot and a `for … in` read dereferences the null. Instantiate this type at integer or bool elements only — a `float` TYPE ARGUMENT is refused separately today, for its own reason — or hold the elements in an `Array with <type parameter>`, which publishes nothing and grows by `push`
```

<!-- test: error.a-managed-instantiation-another-file-reaches-refuses-the-shared-body-holder-first -->
The same rule when the managed instantiation is written nowhere: `Holder with String` exists only because a
SECOND file builds an `Outer with String`, whose field is a `Holder` over `Outer`'s own parameter. The body is
refused whichever file is parsed first — an instantiation is a fact about the program, not about the files
parsed before the body.
```maxon
// --- file: a_holder.maxon
export typealias Int = int(i64.min to i64.max)

export type Holder uses T
	typealias Slot = FixedArray with 4 T

	var slot as Slot

	export static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	export function size() returns Int
		return slot.count()
	end 'size'
end 'Holder'

export type Outer uses T
	var holder as Holder

	export static function create() returns Self
		return Self{holder: Holder.create()}
	end 'create'

	export function size() returns Int
		return holder.size()
	end 'size'
end 'Outer'

// --- file: b_use.maxon
typealias StrOuter = Outer with String

function main() returns ExitCode
	var o = StrOuter.create()
	return o.size()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:11:21: Unsupported: `FixedArray with <N> <type parameter>` — a fixed array PUBLISHES all N of its slots at `create` by zeroing them, and this generic type is instantiated with a type whose slot is a heap POINTER: a zeroed slot is an element for a trivial instantiation and a NULL for a managed one, so `count()` would answer N while every `get` reports an empty slot and a `for … in` read dereferences the null. Instantiate this type at integer or bool elements only — a `float` TYPE ARGUMENT is refused separately today, for its own reason — or hold the elements in an `Array with <type parameter>`, which publishes nothing and grows by `push`
```

<!-- test: error.a-managed-instantiation-another-file-reaches-refuses-the-shared-body-use-first -->
```maxon
// --- file: a_use.maxon
typealias StrOuter = Outer with String

function main() returns ExitCode
	var o = StrOuter.create()
	return o.size()
end 'main'

// --- file: b_holder.maxon
export typealias Int = int(i64.min to i64.max)

export type Holder uses T
	typealias Slot = FixedArray with 4 T

	var slot as Slot

	export static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	export function size() returns Int
		return slot.count()
	end 'size'
end 'Holder'

export type Outer uses T
	var holder as Holder

	export static function create() returns Self
		return Self{holder: Holder.create()}
	end 'create'

	export function size() returns Int
		return holder.size()
	end 'size'
end 'Outer'
```
```maxoncstderr
error E2015: <fragment>:19:21: Unsupported: `FixedArray with <N> <type parameter>` — a fixed array PUBLISHES all N of its slots at `create` by zeroing them, and this generic type is instantiated with a type whose slot is a heap POINTER: a zeroed slot is an element for a trivial instantiation and a NULL for a managed one, so `count()` would answer N while every `get` reports an empty slot and a `for … in` read dereferences the null. Instantiate this type at integer or bool elements only — a `float` TYPE ARGUMENT is refused separately today, for its own reason — or hold the elements in an `Array with <type parameter>`, which publishes nothing and grows by `push`
```

<!-- test: a-fixed-array-in-an-extension-body-over-an-associated-type -->
The construct reached from its third position, which sources its layout
descriptor differently from the other two: an `extension` body has no `uses` clause of its own and is
scanned in its target's scope, so the descriptor a `Slot.create()` reads there is reserved through the
interface's parameter rather than a struct's. The refusal above is a REFUSAL, and a refusal that fires
everywhere proves only that nothing works — this is the capability half, at the position the two cases
above do not stand in.
```maxon
typealias Int = int(i64.min to i64.max)

interface Sized uses Element
	typealias Slot = FixedArray with 4 Element
	function tally() returns Int
end 'Sized'

extension Sized
	function tallyTwice() returns Int
		var v = Slot.create()
		return (v.count() as Int) + tally()
	end 'tallyTwice'
end 'Sized'

type Bag uses Element implements Sized with Element
	typealias Slot = FixedArray with 4 Element
	var n as Int

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	function tally() returns Int
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
The CONCRETE half of the same invariant. `Parser.requireFixedArrayElementType`'s two arms are one rule about
one hazard, and without this case the door that refuses a written `FixedArray with 2 String` could be deleted
with the whole suite staying green. ⚠ `stdlib/FixedArray.maxon` is generic over its element, where the
compiler's synthesized fixed array is scalar-only; what this case pins is that the compiler's position is a
stated refusal rather than an accident.
```maxon
typealias Vec2 = FixedArray with 2 String

function main() returns ExitCode
	var v = Vec2.create()
	return v.count()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:10: Unsupported: `FixedArray` holds its elements INLINE and publishes every slot at `create`, so it has no element destructor to stamp and a 'String' element would leak its storage and be read back as a null slot — a fixed array's element is an integer, a bool or a float
```

## A Sized Container's Buffer May Not Change Its Own Length

⛔⛔ **`count()` ANSWERS FROM THE TYPE WHILE `get` AND THE WALK ANSWER FROM THE RECORD, AND THIS REFUSAL
DEFENDS THE INVARIANT BETWEEN THEM.** `stdlib/FixedArray.maxon`'s `count()` is `countof(Self)` —
folded to a literal, no load — while `get` forwards to `managed.get` (LENGTH-bounded) and
`createIterator` hands the raw buffer to a cursor that walks the same length. All three agree only
while `managed.length() == countof(Self)`, which every route into the record establishes at
construction. What needs defending is the record being reached PAST the surface: `managed` is visible
to an `extension FixedArray`, and a buffer surface serving the length-changing members would let it drift
(`before=3 w0=3 after=3 walked=0 g0=-1`).

⭐ **THE REFUSED SET IS THE MEMBERS THAT WRITE THE RECORD'S LENGTH WORD, AND NO OTHERS.** On a
`FixedArray with 3 Int`'s buffer, printing `managed.length()` after each call:
`setLength` 3 to 5, `remove` 5 to 4, `clear` 4 to 0 all move it; `grow(64)` moves CAPACITY 4 to 64 and
leaves the length at 3; `swap`, `shiftRight` and `shiftLeft` move elements the buffer already owns and
leave it alone. **`grow` is deliberately SERVED**: a fixed array whose buffer has room to spare still has
the size its type states, and refusing it would be over-refusal — which is a wrong answer too.

⚠ **NOT A MEMORY-SAFETY HOLE, WHICH IS WHY THE CURE IS HERE AND NOT ON `get`.** `setLength` is
capacity-refused, so every slot the walk reaches is a slot the buffer really owns. A guard on `get`
would cost a compare on every fixed array read in every program to defend a state the type's own surface
cannot produce; this refusal costs nothing at runtime and closes the seam one level up, the way
`push` is already refused on this surface.

<!-- test: error.a-sized-containers-buffer-may-not-be-cleared -->
<!-- tier: stdlib -->
**ENTRANCE A — the bare `managed` read inside an `extension FixedArray`**, the primary shape. `managed` is
not exported, so this is the shortest spelling that reaches the record.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function bust()
		managed.clear()
	end 'bust'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	v.bust()
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:11: Unsupported: `__ManagedMemory` member 'clear' — this buffer is a `FixedArray`'s own record, and a fixed array's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record. 'clear' WRITES the record's length, which would leave the type saying one number while `get` and the walk answer another. A sized container's buffer refuses setLength/append/clear/remove and serves every other member of the surface; the growable `Array`'s buffer serves all of them, because an `Array`'s count IS its record's length
```

<!-- test: error.a-sized-containers-buffer-may-not-be-relengthened -->
<!-- tier: stdlib -->
`setLength` is the member the measurement caught moving the length furthest — `grow(64)` then
`setLength(64)` read `count=3 walked=64 g40=0`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function stretch()
		try managed.setLength(2) otherwise ignore
	end 'stretch'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	v.stretch()
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:15: Unsupported: `__ManagedMemory` member 'setLength' — this buffer is a `FixedArray`'s own record, and a fixed array's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record. 'setLength' WRITES the record's length, which would leave the type saying one number while `get` and the walk answer another. A sized container's buffer refuses setLength/append/clear/remove and serves every other member of the surface; the growable `Array`'s buffer serves all of them, because an `Array`'s count IS its record's length
```

<!-- test: error.a-sized-containers-buffer-may-not-lose-an-element -->
<!-- tier: stdlib -->
`remove` shrinks by one and slides the tail — 5 to 4.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function drop()
		try managed.remove(0) otherwise ignore
	end 'drop'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	v.drop()
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:15: Unsupported: `__ManagedMemory` member 'remove' — this buffer is a `FixedArray`'s own record, and a fixed array's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record. 'remove' WRITES the record's length, which would leave the type saying one number while `get` and the walk answer another. A sized container's buffer refuses setLength/append/clear/remove and serves every other member of the surface; the growable `Array`'s buffer serves all of them, because an `Array`'s count IS its record's length
```

<!-- test: error.a-sized-containers-buffer-may-not-be-appended-to -->
<!-- tier: stdlib -->
⭐ **THIS CASE ALSO PRE-EMPTS A COMPILER PANIC, AND THAT IS A CONSEQUENCE OF THE REFUSAL RATHER THAN ITS
POINT.** The refusal here runs AHEAD of `parseArrayAppend`, so the path
`parseArrayAppend -> requireAppendArg -> arrayAppendArgAdmits` — which would size a `typeParameter` element
as a compile-time constant and panic in `primitiveTypeByteSize` — is never reached for a sized container.

⭐ **THE GROWABLE HALF COMPILES AND RUNS.** `extension Array`'s `managed.append(managed)`
(`managed-memory-methods/the-bare-fused-append-answers-the-same`, exit 4) works because the fused record
door hands the receiver its OWN buffer instance rather than a synthesized BYTE one — so
`containerElementIsOpaque` answers about the element the receiver actually has.
⚠ **THE BARE SPELLING HERE REACHES THE FUSED DOOR, MINTS NO VALUE, AND IS REFUSED THERE OFF THE ENCLOSING
DECLARATION** — `declarationIsASizedContainerRecordNamed(enclosingType)`, the by-name form that exists for
exactly a caller with no value to mark. It could not be otherwise: the instance the door hands over is
spelled `Array with Element` for a fixed array too (a load-bearing retype), so a `giid` test cannot tell a
fixed array's record from a growable array's. Four reachable `FixedArray` spellings — `try managed.append(…)`, the
bare statement form, `self.managed.append(…)`, and the buffer bound to a local first — each answer the
same `E2015` at their own line:column. (A fifth, `v.managed.append(…)` from outside the declaration, is
`E3014`: `managed` is not exported.)
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function double()
		try managed.append(managed) otherwise ignore
	end 'double'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	v.double()
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:15: Unsupported: `__ManagedMemory` member 'append' — this buffer is a `FixedArray`'s own record, and a fixed array's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record. 'append' WRITES the record's length, which would leave the type saying one number while `get` and the walk answer another. A sized container's buffer refuses setLength/append/clear/remove and serves every other member of the surface; the growable `Array`'s buffer serves all of them, because an `Array`'s count IS its record's length
```

<!-- test: error.the-chained-self-managed-reaches-the-same-refusal -->
<!-- tier: stdlib -->
**ENTRANCE B — `self.managed`, which mints NO value to mark** and passes the surface along the dispatch
instead (`viaManagedField`). It is a different carrier from entrance A and is closed separately; open,
it drifts to `count=3 len=0`, identically to A.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function bust()
		self.managed.clear()
	end 'bust'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	v.bust()
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:16: Unsupported: `__ManagedMemory` member 'clear' — this buffer is a `FixedArray`'s own record, and a fixed array's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record. 'clear' WRITES the record's length, which would leave the type saying one number while `get` and the walk answer another. A sized container's buffer refuses setLength/append/clear/remove and serves every other member of the surface; the growable `Array`'s buffer serves all of them, because an `Array`'s count IS its record's length
```

<!-- test: error.a-local-bound-to-the-buffer-reaches-the-same-refusal -->
<!-- tier: stdlib -->
**ENTRANCE C — the buffer bound to a local first.** The compiler has no stack slots, so `m` binds to the
surface's own `ValueId` and the value mark answers for it; the case exists because that is a PROPERTY
of the binding rule rather than a thing this refusal arranges, and a change to it would silently
reopen the seam here.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function bust()
		var m = managed
		m.clear()
	end 'bust'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	v.bust()
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:5: Unsupported: `__ManagedMemory` member 'clear' — this buffer is a `FixedArray`'s own record, and a fixed array's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record. 'clear' WRITES the record's length, which would leave the type saying one number while `get` and the walk answer another. A sized container's buffer refuses setLength/append/clear/remove and serves every other member of the surface; the growable `Array`'s buffer serves all of them, because an `Array`'s count IS its record's length
```

<!-- test: error.a-user-extension-cannot-read-a-fixed-arrays-buffer-to-return-it -->
⛔⛔ **ENTRANCE D — THE RECORD LEAVING BY `return`, FROM A USER FILE.** A user extension cannot reach a
fixed array's record at all, so it cannot hand it out either. The first raw door in this program is the
signature: `returns ElementMemory` names the raw buffer type (`ElementMemory` is `__ManagedMemory with
Element`), and it is refused there, ahead of the `return managed` and the caller's `got.clear()`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function buf() returns ElementMemory
		return managed
	end 'buf'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	var got = v.buf()
	got.clear()
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3185: <fragment>:6:25: 'ElementMemory' named outside the library tiers: the raw buffer type is nameable only from a source file under stdlib/ or runtime/ — use 'Array'
```

<!-- test: the-length-preserving-buffer-members-are-still-served -->
<!-- tier: stdlib -->
⭐⭐ **THE OVER-REFUSAL CONTROL, AND IT IS THE HALF A REFUSAL USUALLY FORGETS.** Everything on the
buffer surface that does NOT write the length word is still served on a sized container's buffer. Each
call here leaves `length()` at its start value: `grow` moves capacity 4 to 64, `swap` and
the two shifts move elements the buffer already owns. Delete the length-writer list's gate and make the
refusal blanket, and THIS case is what goes red.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function survey() returns Int
		try managed.grow(64) otherwise ignore
		try managed.swap(0, 1) otherwise ignore
		try managed.shiftRight(0, 1) otherwise ignore
		try managed.shiftLeft(0, 1) otherwise ignore
		view e = try managed.get(0) otherwise panic("get")
		try managed.set(1, e) otherwise ignore
		return (managed.length() as Int) + (managed.capacity() as Int)
	end 'survey'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	return v.survey() as ExitCode
end 'main'
```
```exitcode
67
```

<!-- test: the-growable-arrays-buffer-still-changes-its-length -->
<!-- tier: stdlib -->
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

<!-- test: error.a-user-extension-cannot-read-a-fixed-arrays-buffer-to-pass-it-to-a-helper -->
⛔⛔ **ENTRANCE E — THE ARGUMENT DIRECTION OF D.** An `extension FixedArray` whose `bust()` is `wipe(managed)`
and whose `wipe(m ElementMemory)` is `m.clear()` would leave the type saying 3 while the walk finds nothing
(`count=3 walked=0`). From a user file it never gets that far: the bare `managed` in `bust()` is the first
raw door, and it is refused at the read, before the helper's `ElementMemory` parameter is reached.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function bust()
		wipe(managed)
	end 'bust'

	function wipe(m ElementMemory)
		m.clear()
	end 'wipe'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	v.bust()
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3185: <fragment>:7:8: '.managed' read from outside the library tiers: a value's raw '__ManagedMemory' buffer is reachable only from a source file under stdlib/ or runtime/ — use the 'Array' methods
```

<!-- test: the-slice-a-member-hands-to-a-helper-is-served -->
<!-- tier: stdlib -->
⭐ **A SLICE IS A VIEW WITH A LENGTH OF ITS OWN.** `managed.slice(0, 2)` is not the fixed array's record —
`__managed_slice` mints a fresh view whose `length@8` is its own — so a helper that takes it as an
`ElementMemory` and clears it cannot move the fixed array's length, and no answer can disagree with the type.
The exit code proves both halves: the slice really was cleared (0) and the fixed array's own record was
untouched (3).
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function sliceThenClear() returns Int
		var s = try managed.slice(0, 2) otherwise panic("slice")
		return wipe(s) + (10 * (managed.length() as Int))
	end 'sliceThenClear'

	function wipe(m ElementMemory) returns Int
		m.clear()
		return m.length() as Int
	end 'wipe'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	return v.sliceThenClear() as ExitCode
end 'main'
```
```exitcode
30
```

<!-- test: the-slice-a-member-returns-is-served -->
<!-- tier: stdlib -->
⭐ **… AND A MEMBER MAY RETURN A SLICE, WHICH ITS CALLER MAY CHANGE FREELY.** The caller clears the view
it was handed (0); the fixed array's own record keeps its three elements (3).
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function part() returns ElementMemory
		return try managed.slice(0, 2) otherwise panic("slice")
	end 'part'
end 'FixedArray'

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

## And The Record May Not LEAVE By Any Of The Other Three Doors Either

⛔⛔ **EACH OF THESE ROUTES WOULD PRINT `len=0 count=3` ON A `FixedArray with 3 Int`.** They share one cause:
`bufferSurfaceOfDeclaredRecord` RETYPES the record to `Array with Element` so `FixedArrayIter.create(managed)`
type-checks, and that retype makes the value indistinguishable from a growable array to everything except
the per-value MARK — so any route that carries the record somewhere a `ValueId` mark cannot follow hands
back a fully working `Array` over the fixed array's storage. ⇒ The record is reachable only from the library
tiers. A user extension cannot read `managed` or name the raw buffer type, so each route below is refused
at its first raw door by **E3185**, before any value of the record exists to carry.

<!-- test: error.a-user-extension-cannot-read-a-fixed-arrays-buffer-to-push-it-into-a-container -->
**ROUTE F — THE RECORD AS A CONTAINER'S ELEMENT.** `Array with ElementMemory` would hold the record, and
`get` would hand it back with no mark. The first raw door is the element type in the extension's
`typealias`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	typealias Bufs = Array with ElementMemory

	function bust() returns Int
		var b = Bufs.create()
		b.push(managed)
		var got = try b.get(0) otherwise panic("no element")
		got.clear()
		return managed.length() as Int
	end 'bust'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	return v.bust() as ExitCode
end 'main'
```
```maxoncstderr
error E3185: <fragment>:6:30: 'ElementMemory' named outside the library tiers: the raw buffer type is nameable only from a source file under stdlib/ or runtime/ — use 'Array'
```

<!-- test: error.a-user-extension-cannot-read-a-fixed-arrays-buffer-to-put-it-in-a-tuple -->
**ROUTE G — THE RECORD AS A TUPLE SLOT**, which needs no container type spelled anywhere: two tokens of
punctuation would launder the mark. The only raw door is the bare `managed` inside the tuple, and it is
refused there.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function bust() returns Int
		let t = (managed, 1)
		t.0.clear()
		return (managed.length() as Int) + t.1
	end 'bust'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	return v.bust() as ExitCode
end 'main'
```
```maxoncstderr
error E3185: <fragment>:7:12: '.managed' read from outside the library tiers: a value's raw '__ManagedMemory' buffer is reachable only from a source file under stdlib/ or runtime/ — use the 'Array' methods
```

<!-- test: error.a-user-extension-cannot-read-a-fixed-arrays-buffer-to-store-it-in-a-field -->
**ROUTE H — THE RECORD ASSIGNED INTO A GROWABLE `Array`'s OWN `managed` FIELD.** The growable record's
buffer carries no sized mark, so every length writer would be served on it. The first raw door is the
`.managed` on the assignment's target, `a.managed`, ahead of the bare `managed` it would be assigned.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	typealias Arr = Array with Element

	function bust() returns Int
		var a = Arr.create()
		a.managed = managed
		a.managed.clear()
		return managed.length() as Int
	end 'bust'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	return v.bust() as ExitCode
end 'main'
```
```maxoncstderr
error E3185: <fragment>:10:5: '.managed' read from outside the library tiers: a value's raw '__ManagedMemory' buffer is reachable only from a source file under stdlib/ or runtime/ — use the 'Array' methods
```

<!-- test: error.a-user-extension-cannot-read-a-fixed-arrays-buffer-to-hand-it-to-a-generic -->
⛔⛔ **ROUTE I — THE RECORD THROUGH A USER'S OWN GENERIC.** A nine-line `Box uses T` would take the record
in and hand it straight back out with no mark on it. From a user file the extension cannot spell the
instantiation: `Box with ElementMemory` names the raw buffer type, and that is the first raw door.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

type Box uses T
	var held as T

	static function create(held T) returns Self
		return Self{held: held}
	end 'create'

	function get() returns T
		return held
	end 'get'
end 'Box'

extension FixedArray
	typealias BufBox = Box with ElementMemory

	function bust() returns Int
		let b = BufBox.create(managed)
		let got = b.get()
		got.clear()
		return managed.length() as Int
	end 'bust'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	return v.bust() as ExitCode
end 'main'
```
```maxoncstderr
error E3185: <fragment>:18:30: 'ElementMemory' named outside the library tiers: the raw buffer type is nameable only from a source file under stdlib/ or runtime/ — use 'Array'
```

<!-- test: a-slice-may-be-stored-passed-and-returned-freely -->
<!-- tier: stdlib -->
⭐ **EVERY ROUTE ABOVE IS LEGAL ON A `slice`, IN THE LIBRARY TIER.** A view's `length@8` is its own, so no
answer about it can contradict the fixed array's type. The slice here is stored into a container, put in a
tuple, passed to a foreign generic and cleared through each — and the fixed array's own record is untouched at
the end.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

type Box uses T
	var held as T

	static function create(held T) returns Self
		return Self{held: held}
	end 'create'

	function get() returns T
		return held
	end 'get'
end 'Box'

extension FixedArray
	typealias Bufs = Array with ElementMemory
	typealias BufBox = Box with ElementMemory

	function survey() returns Int
		var b = Bufs.create()
		b.push(try managed.slice(0, 2) otherwise panic("slice"))
		var fromContainer = try b.get(0) otherwise panic("no element")
		fromContainer.clear()

		var t = (try managed.slice(0, 2) otherwise panic("slice"), 1 as Int)
		t.0.clear()

		var boxed = BufBox.create(try managed.slice(0, 2) otherwise panic("slice"))
		var fromBox = boxed.get()
		fromBox.clear()

		return (fromContainer.length() as Int) + (fromBox.length() as Int) + (10 * (managed.length() as Int))
	end 'survey'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	return v.survey() as ExitCode
end 'main'
```
```exitcode
30
```

## And A MERGE Is A Door Too — The Ternary And The `try` Fallback

⛔⛔ **A PHI IS A NEW `ValueId`, SO EVERY PER-VALUE FACT STOPS AT A MERGE UNLESS SOMETHING CARRIES IT
ACROSS — AND THE BUFFER-SURFACE FAMILY IS CARRIED.** The parser has three value merges
(`finalizeMatchMerge`, which serves the ternary AND `match … gives`; `finishValueTry`; and a
short-circuit's, whose phi is a bool and cannot be a buffer). Uncarried, the first two would launder two
facts:

* the SIZED mark — so an `extension FixedArray` could clear the fixed array's own record by routing the value
  through a join, with every other door shut (`len=0 count=3`);
* the BUFFER mark itself — a **roster inversion**: on a plain growable array,
  `var m = a.managed if flag else a.managed` then `m.length()` would be refused as an unknown `Array`
  member while `m.count()` compiles. Nothing to do with `FixedArray`; the same carry covers it, and the
  third case here pins it.

<!-- test: error.a-buffer-through-a-ternary-reaches-the-same-refusal -->
<!-- tier: stdlib -->
**ROUTE J — THE TERNARY MERGE.** The degenerate `x if c else x` is deliberate: both edges are the same
expression, so nothing about the program needs two answers — only the phi's fresh `ValueId` could give one.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function bust(flag bool) returns Int
		var m = managed if flag else managed
		m.clear()
		return managed.length() as Int
	end 'bust'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	return v.bust(true) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:5: Unsupported: `__ManagedMemory` member 'clear' — this buffer is a `FixedArray`'s own record, and a fixed array's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record. 'clear' WRITES the record's length, which would leave the type saying one number while `get` and the walk answer another. A sized container's buffer refuses setLength/append/clear/remove and serves every other member of the surface; the growable `Array`'s buffer serves all of them, because an `Array`'s count IS its record's length
```

<!-- test: error.a-buffer-through-a-try-fallback-reaches-the-same-refusal -->
<!-- tier: stdlib -->
**ROUTE K — THE `try … otherwise` MERGE**, which needs no second spelling of the record at all: the ok
edge is a legal `slice` and only the FALLBACK is the record, so a rule that looked at either edge alone
would have to be right about both.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function bust() returns Int
		var m = try managed.slice(0, 99) otherwise managed
		m.clear()
		return managed.length() as Int
	end 'bust'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	return v.bust() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:5: Unsupported: `__ManagedMemory` member 'clear' — this buffer is a `FixedArray`'s own record, and a fixed array's size is a coordinate of its TYPE, so `count()` answers `countof(Self)` and never reads the record. 'clear' WRITES the record's length, which would leave the type saying one number while `get` and the walk answer another. A sized container's buffer refuses setLength/append/clear/remove and serves every other member of the surface; the growable `Array`'s buffer serves all of them, because an `Array`'s count IS its record's length
```

<!-- test: a-growable-buffer-keeps-its-own-roster-through-a-merge -->
<!-- tier: stdlib -->
⭐⭐ **THE OTHER HALF OF THE CARRY, AND IT IS A REPAIR RATHER THAN A REFUSAL.** The merged value here is
an ordinary growable buffer: it must come out on the BUFFER roster, which is the roster a value
spelled `__ManagedMemory` is on — not the `Array` roster, where `setLength` and `length` would both be
refused as unknown `Array` members. The exit code proves both halves: the
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

<!-- test: an-extension-still-walks-its-own-fixed-array -->
<!-- tier: stdlib -->
⭐ **AN EXTENSION WALKS ITS OWN FIXED ARRAY BOTH WAYS:** `for e in self`, which goes through the declaration's
own `createIterator`, and a `for` over a `slice` of its record. The exit code is the two walks side by
side.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

extension FixedArray
	function walkedTwoWays() returns Int
		var viaSelf = 0 as Int
		for e in self 'eachOwn'
			viaSelf = viaSelf + (e - e) + 1
		end 'eachOwn'

		var viaSlice = 0 as Int
		for e in try managed.slice(0, countof(Self)) otherwise panic("slice") 'eachSliced'
			viaSlice = viaSlice + (e - e) + 1
		end 'eachSliced'

		return (viaSelf * 10) + viaSlice
	end 'walkedTwoWays'
end 'FixedArray'

function main() returns ExitCode
	var v = Vec3.create()
	return v.walkedTwoWays() as ExitCode
end 'main'
```
```exitcode
33
```

<!-- test: fixed-array.an-array-element-may-hold-a-union-carrying-a-fixed-array -->
A record whose field is a union with a `FixedArray` payload is an ordinary array element: pushing it and
reading it back copies nothing a `slice` would, so an array of such records is legal.
```maxon
typealias Flags = FixedArray with 4 bool
typealias CellArray = Array with Cell

union Content
	empty
	marked(flags Flags)
end 'Content'

type Cell
	var content as Content

	static function create(content Content) returns Self
		return Self{content: content}
	end 'create'

	function firstFlag() returns bool
		return match self.content 'kind'
			empty gives false
			marked(flags) gives flags.get(0)
		end 'kind'
	end 'firstFlag'
end 'Cell'

function main() returns ExitCode
	var flags = Flags.create()
	flags.set(0, value: true)

	var cells = CellArray.create()
	cells.push(Cell.create(Content.marked(flags)))

	for cell in cells 'each'
		print("{cell.firstFlag()}\n")
	end 'each'

	return 0
end 'main'
```
```stdout
true
```

<!-- test: fixed-array.a-slice-deep-copies-a-fixed-array-held-in-a-field-and-in-a-union-payload -->
A `FixedArray`'s record is an `Array`'s, so `slice` copies it like one: writing the original cells after the
slice — replacing the union and setting a flag in the fixed array field — leaves the copies as they were.
```maxon
typealias Flags = FixedArray with 4 bool
typealias CellArray = Array with Cell

union Content
	empty
	marked(flags Flags)
end 'Content'

type Cell
	var content as Content
	var mask as Flags

	static function create(content Content) returns Self
		return Self{content: content, mask: Flags.create()}
	end 'create'

	function firstFlag() returns bool
		return match self.content 'kind'
			empty gives false
			marked(flags) gives flags.get(0)
		end 'kind'
	end 'firstFlag'

	function secondMaskBit() returns bool
		return self.mask.get(1)
	end 'secondMaskBit'

	function overwrite()
		self.content = Content.empty
		self.mask.set(1, value: true)
	end 'overwrite'
end 'Cell'

function main() returns ExitCode
	var flags = Flags.create()
	flags.set(0, value: true)

	var cells = CellArray.create()
	cells.push(Cell.create(Content.marked(flags)))
	cells.push(Cell.create(Content.marked(flags)))

	let copies = try cells.slice(0, endIndex: 2) otherwise return 1

	for cell in cells 'overwriteOriginals'
		cell.overwrite()
	end 'overwriteOriginals'

	for cell in cells 'eachOriginal'
		print("original {cell.firstFlag()} {cell.secondMaskBit()}\n")
	end 'eachOriginal'

	for cell in copies 'eachCopy'
		print("copy {cell.firstFlag()} {cell.secondMaskBit()}\n")
	end 'eachCopy'

	return 0
end 'main'
```
```stdout
original false true
original false true
copy true false
copy true false
```

<!-- test: fixed-array.a-record-holding-fixed-arrays-crosses-to-a-service-and-back -->
A service boundary walks a `FixedArray` as it walks an `Array`: a record holding one in a field and one in a union
payload crosses at the `spawn` and as a call argument, and a record the handler builds comes back as the reply.
```maxon
typealias Flags = FixedArray with 4 bool

union Content
	empty
	marked(flags Flags)
end 'Content'

type Cell
	export var content as Content
	export var mask as Flags

	static function create(content Content, mask Flags) returns Self
		return Self{content: content, mask: mask}
	end 'create'

	function describe() returns String
		let first = match self.content 'kind'
			empty gives false
			marked(flags) gives flags.get(0)
		end 'kind'

		let second = self.mask.get(1)
		return "{first} {second}"
	end 'describe'
end 'Cell'

type Svc
	var held as Cell

	static function create(held Cell) returns Self
		return Self{held: held}
	end 'create'

	export function report(other Cell) returns Cell
		print("held {self.held.describe()}\n")
		print("argument {other.describe()}\n")
		return Cell.create(Content.empty, mask: flagsOf(false, second: true))
	end 'report'
end 'Svc'

function flagsOf(first bool, second bool) returns Flags
	var flags = Flags.create()
	flags.set(0, value: first)
	flags.set(1, value: second)
	return flags
end 'flagsOf'

function main() returns ExitCode
	let h = spawn Svc.create(Cell.create(Content.marked(flagsOf(true, second: false)), mask: Flags.create()))
	let reply = try await h.report(Cell.create(Content.marked(flagsOf(true, second: false)), mask: flagsOf(false, second: true))) otherwise return 1
	print("reply {reply.describe()}\n")
	return 0
end 'main'
```
```stdout
held true false
argument true true
reply false true
```
```exitcode
0
```

<!-- test: error.a-fixed-array-from-head-another-file-keeps-private-is-refused -->
A `FixedArray` instance alias at the head of `from […]` is a type name, and another file's file-private one is
refused.
```maxon
// --- file: probe.maxon
typealias Pair = FixedArray with 2 ExitCode

export function probePair() returns ExitCode
	let v = Pair from [1, 2]
	print("{v.count()}")
	return 0
end 'probePair'

// --- file: main.maxon
function main() returns ExitCode
	let v = Pair from [3, 4]
	print("{v.count()}")
	return probePair()
end 'main'
```
```maxoncstderr
error E3008: <fragment>:13:10: typealias 'Pair' is not exported
```

<!-- test: a-let-fixed-array-read-is-range-known -->
A `FixedArray from […]` bound by `let` is a constant, so a value read from it carries the range of the elements written.
```maxon
typealias Small = int(0 to 100)

function take(v Small) returns Small
	return v
end 'take'

function main() returns ExitCode
	let v = FixedArray from [10, 20, 30]
	let a = v.get(1)
	print("{take(a)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
20
```

<!-- test: error.a-let-fixed-array-read-outside-a-narrower-alias -->
The range a `let` fixed array's elements carry is checked at the use, and the reachable extreme is named.
```maxon
typealias Small = int(0 to 100)

function take(v Small) returns Small
	return v
end 'take'

function main() returns ExitCode
	let v = FixedArray from [10, 2000]
	let a = v.get(0)
	let b = take(a)
	return b as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:10: Value 2000 is outside the range of 'Small' (int(0 to 100))
```

<!-- test: error.a-let-fixed-array-mutation-is-refused -->
A `let` fixed array literal is immutable, so a write through it is refused as a `let` array's is.
```maxon
function main() returns ExitCode
	let v = FixedArray from [10, 20, 30]
	v.set(0, value: 99)
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:4:4: cannot pass 'v', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: error.the-name-vector-is-not-a-builtin -->
The fixed-size type is `FixedArray`. `Vector` names no builtin, so `Vector with 3 Int` does not parse as
a sized instance: the size `3` stands where a type argument is expected.
```maxon
typealias Int = int(i64.min to i64.max)
typealias V = Vector with 3 Int

function main() returns ExitCode
	var v = V.create()
	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2010: <fragment>:3:27: Expected 'a type' but got '3'
```
