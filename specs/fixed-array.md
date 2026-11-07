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
let v = Vec3.create()  // zero-initialized, 3 elements on the stack
```

The size is part of the type. A `FixedArray with 3 Int` is a different type from `FixedArray with 4 Int`.

### Creating from Array Literals

Fixed arrays implement `BuiltinArrayLiteral`, so you can initialize them from an array literal using `from`. The element type and size are inferred from the literal:

```text
let v = FixedArray from [10, 20, 30]  // inferred as FixedArray with 3 Int
```

The inferred type is compatible with a typealias of the same element type and size, so a `FixedArray from [...]` can be passed to a function expecting the typealias:

```text
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function process(v Vec3) returns Int
  return v.get(0)
end 'process'

let v = FixedArray from [10, 20, 30]
process(v)  // works — inferred type matches Vec3
```

### Element Access

Access elements with `.get()` and modify them with `.set()`. An index the compiler can prove inside
the fixed array makes the access total: it cannot fail, so it is written bare, and a `try` on it is
refused (E3055).

```text
let value = v.get(0)
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
let value = try v.get(i) otherwise 0
try v.set(i, value: 42) otherwise ignore
```

### Size and Count

The `.count()` method always returns the fixed size of the fixed array:

```text
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int
let v = Vec4.create()
let n = v.count()  // always 4
```

### Storage

A fixed array's elements live in a heap record, whatever the count; `FixedArray with 100 Int` is not stack-placed. The record is freed with its owner.

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
the ARRAY roster, which is what makes `stdlib/Array.maxon`'s own `containsSequence(sequence)` reach `get`. Its gate
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
`__managed_create_fixed` handed the strides it cannot look up, which sizes the
record). Its three premises are the ones
[top-level-factory-globals](top-level-factory-globals.md) pins for every declared generic: the `create()`
must EXIST, be NAMEABLE from its declaring file, and return `Self`. `__module_init` then emits a bare
`call FixedArray.create` with no stamps at all, and the record it hands back is already published.

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

<!-- test: error.an-enum-instantiation-whose-raw-values-exclude-zero-refuses-a-fixed-array-field -->
The shared body publishes zeroed slots for every instantiation, so an instantiation at an enum whose raw values exclude 0 refuses it as a concrete `FixedArray with 4 Level` is refused.
```maxon
typealias Int = int(i64.min to i64.max)

enum Level
	low = 10
	high = 20
end 'Level'

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

typealias LevelHolder = Holder with Level

function main() returns ExitCode
	var h = LevelHolder.create()
	return h.size()
end 'main'
```
```maxoncstderr
error E3106: <fragment>:15:21: 'FixedArray.create' cannot publish slots of a type parameter: 'LevelHolder' is instantiated with a type that has no zero value (a managed type, an enum or union whose raw values include no 0, or a ranged alias that excludes 0), and every slot is zero. Instantiate it only with types that admit zero, or hold the elements in an 'Array' and append with 'push(value)'
```

<!-- test: error.a-zero-excluding-alias-instantiation-refuses-a-fixed-array-field -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Percent = int(1 to 100)

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

typealias PercentHolder = Holder with Percent

function main() returns ExitCode
	var h = PercentHolder.create()
	return h.size()
end 'main'
```
```maxoncstderr
error E3106: <fragment>:11:21: 'FixedArray.create' cannot publish slots of a type parameter: 'PercentHolder' is instantiated with a type that has no zero value (a managed type, an enum or union whose raw values include no 0, or a ranged alias that excludes 0), and every slot is zero. Instantiate it only with types that admit zero, or hold the elements in an 'Array' and append with 'push(value)'
```

<!-- test: an-enum-instantiation-with-a-zero-case-keeps-a-fixed-array-field -->
```maxon
typealias Int = int(i64.min to i64.max)

enum Gauge
	idle = 0
	busy = 5
end 'Gauge'

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

typealias GaugeHolder = Holder with Gauge

function main() returns ExitCode
	var h = GaugeHolder.create()
	return h.size()
end 'main'
```
```exitcode
4
```

<!-- test: error.one-zero-excluding-instantiation-refuses-the-shared-body -->
The rule is existential here as it is for a managed instantiation: a trivial instantiation that admits 0 standing beside the enum does not buy the body back.
```maxon
typealias Int = int(i64.min to i64.max)

enum Level
	low = 10
	high = 20
end 'Level'

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
typealias LevelHolder = Holder with Level

function main() returns ExitCode
	var a = IntHolder.create()
	var b = LevelHolder.create()
	return a.size() + b.size()
end 'main'
```
```maxoncstderr
error E3106: <fragment>:15:21: 'FixedArray.create' cannot publish slots of a type parameter: 'LevelHolder' is instantiated with a type that has no zero value (a managed type, an enum or union whose raw values include no 0, or a ranged alias that excludes 0), and every slot is zero. Instantiate it only with types that admit zero, or hold the elements in an 'Array' and append with 'push(value)'
```

<!-- test: a-fixed-array-literal-publishes-no-zero-slot -->
A `FixedArray from [..]` writes every slot, so an enum whose raw values exclude 0 is accepted as its element, where `Level.create()` over the same element is refused.
```maxon
enum Level
	low = 10
	high = 20
end 'Level'

typealias Pair = FixedArray with 2 Level

function main() returns ExitCode
	let literal = FixedArray from [Level.low, Level.high]
	let named = Pair from [Level.high, Level.low]

	print("{literal.get(1).name} {named.get(0).name}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
high high
```

<!-- test: error.a-contested-alias-instantiation-that-excludes-zero-refuses-a-fixed-array-field -->
`Count` is the standard library's `int(0 to u64.max)`, which admits 0, and this program declares its own `Count` that does not. The program's instantiation means its own declaration, so the body is refused although the library's declaration of the name admits 0.
```maxon
typealias Count = int(1 to 9)

type Holder uses Element
	typealias Slot = FixedArray with 4 Element

	var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	function size() returns Count
		return 4
	end 'size'
end 'Holder'

typealias CountHolder = Holder with Count

function main() returns ExitCode
	var h = CountHolder.create()
	return h.size() as ExitCode
end 'main'
```
```maxoncstderr
error E3106: <fragment>:10:21: 'FixedArray.create' cannot publish slots of a type parameter: 'CountHolder' is instantiated with a type that has no zero value (a managed type, an enum or union whose raw values include no 0, or a ranged alias that excludes 0), and every slot is zero. Instantiate it only with types that admit zero, or hold the elements in an 'Array' and append with 'push(value)'
```

<!-- test: error.a-library-tier-fixed-array-field-is-refused-for-a-zero-excluding-instantiation -->
<!-- tier: stdlib -->
The refusal of a `FixedArray with N <type parameter>` field holds in every tier.
```maxon
typealias Int = int(i64.min to i64.max)

enum Level
	low = 10
	high = 20
end 'Level'

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

typealias LevelHolder = Holder with Level

function main() returns ExitCode
	var h = LevelHolder.create()
	return h.size()
end 'main'
```
```maxoncstderr
error E3106: <fragment>:15:21: 'FixedArray.create' cannot publish slots of a type parameter: 'LevelHolder' is instantiated with a type that has no zero value (a managed type, an enum or union whose raw values include no 0, or a ranged alias that excludes 0), and every slot is zero. Instantiate it only with types that admit zero, or hold the elements in an 'Array' and append with 'push(value)'
```

<!-- test: error.a-nested-generic-instantiation-refuses-the-inner-fixed-array-field -->
`Outer` holds a `Holder with T` and is itself instantiated at `Level`; the only `Holder` row that exists is the one `Outer with Level` substitutes into its inner alias, which no author wrote.
```maxon
typealias Int = int(i64.min to i64.max)

enum Level
	low = 10
	high = 20
end 'Level'

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

type Outer uses Item
	typealias Inner = Holder with Item

	var inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create()}
	end 'create'

	function size() returns Int
		return inner.size()
	end 'size'
end 'Outer'

typealias LevelOuter = Outer with Level

function main() returns ExitCode
	var o = LevelOuter.create()
	return o.size()
end 'main'
```
```maxoncstderr
error E3106: <fragment>:15:21: 'FixedArray.create' cannot publish slots of a type parameter: 'LevelOuter' is instantiated with a type that has no zero value (a managed type, an enum or union whose raw values include no 0, or a ranged alias that excludes 0), and every slot is zero. Instantiate it only with types that admit zero, or hold the elements in an 'Array' and append with 'push(value)'
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

## A Fixed Array Can Start From A Value, And Be Overwritten In Place

`create(n, value: v)` builds a runtime-sized fixed array whose every element starts as `v`, and a type-sized alias takes the same `value:` argument with no length (`Pair.create(value: v)`). Because every slot is written before anything can read it, the value form is legal for an element whose zero is not a value (a ranged alias that excludes 0, an enum whose raw values include no 0), which the plain `create` refuses with E3106. `refill(value)` writes `value` into every element of an existing fixed array and changes neither its length nor its identity.

<!-- test: create-with-a-value-fills-every-slot -->
Every slot of the four starts as `7`: `4 28`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	let buf = Buf.create(launder(4), value: 7)
	var sum = 0 as Int

	for x in buf 'each'
		sum = sum + x
	end 'each'

	print("{buf.count()} {sum}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 28
```

<!-- test: create-with-a-value-admits-a-ranged-element-that-excludes-zero -->
`Level` has no zero, so `Levels.create(n)` is E3106; with a value every slot is a `Level` from the start: `3 15`.
```maxon
typealias Level = int(1 to 9)
typealias Levels = FixedArray with Level

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	let levels = Levels.create(launder(3), value: 5)
	var sum = 0 as Count

	for level in levels 'each'
		sum = sum + (level as Count)
	end 'each'

	print("{levels.count()} {sum}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 15
```

<!-- test: create-with-a-value-admits-an-enum-whose-raw-values-exclude-zero -->
No `Tier` has the raw value 0, and all three slots read back `high`: `3 3`.
```maxon
enum Tier
	low = 1
	high = 2
end 'Tier'

typealias Tiers = FixedArray with Tier

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	let tiers = Tiers.create(launder(3), value: Tier.high)
	var highs = 0 as Count

	for tier in tiers 'each'
		if tier == Tier.high 'isHigh'
			highs = highs + 1
		end 'isHigh'
	end 'each'

	print("{tiers.count()} {highs}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 3
```

<!-- test: a-type-sized-create-takes-a-value-and-no-length -->
The count is the type's, so only `value:` is written, and the proven indices need no `try`: `2 8`.
```maxon
typealias Level = int(1 to 9)
typealias Pair = FixedArray with 2 Level

function main() returns ExitCode
	let pair = Pair.create(value: 4)

	print("{pair.count()} {pair.get(0) + pair.get(1)}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 8
```

<!-- test: create-with-a-value-packs-bools -->
Eleven bools cross a byte boundary and all start `true`: `11 11`.
```maxon
typealias Flags = FixedArray with bool

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	let flags = Flags.create(launder(11), value: true)
	var set = 0 as Count

	for flag in flags 'each'
		if flag 'isSet'
			set = set + 1
		end 'isSet'
	end 'each'

	print("{flags.count()} {set}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
11 11
```

<!-- test: create-with-a-value-of-count-zero-is-empty -->
A length of zero that is only known at run time builds an empty fixed array: `0`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	let buf = Buf.create(launder(0), value: 9)

	print("{buf.count()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: create-with-a-value-and-a-constant-length-is-sized -->
A constant length gives the sized type, so the proven indices are written without `try`: `4 7 7`.
```maxon
typealias Level = int(1 to 9)
typealias Levels = FixedArray with Level

function main() returns ExitCode
	let levels = Levels.create(4, value: 7)

	print("{levels.count()} {levels.get(0)} {levels.get(3)}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 7 7
```

<!-- test: error.a-value-label-inside-a-braced-argument-is-not-a-value-fill -->
A `value:` that sits inside a struct literal in the length argument labels a field, not the fill, so the plain `create` rule applies: `Level` has no zero and `Levels.create(n)` is E3106.
```maxon
typealias Level = int(1 to 9)
typealias Levels = FixedArray with Level

type Cell
	export var value as ElementIndex
	export var n as ElementIndex

	static function slots(n ElementIndex) returns Count
		let levels = Levels.create(Cell{value: 3, n: n}.n)

		return levels.count()
	end 'slots'
end 'Cell'

function main() returns ExitCode
	return Cell.slots(2) as ExitCode
end 'main'
```
```maxoncstderr
error E3106: <fragment>:10:16: 'FixedArray.create' cannot publish slots of 'Level': every slot is zero and zero is not a value of 'Level'
```

<!-- test: error.a-value-label-inside-a-braced-argument-is-not-a-type-sized-value-fill -->
A type-sized alias states its length, so a length argument is E3194 however the argument is written, and a braced `value:` is not the fill; `Level` has no zero, so the plain `create` rule is asked first and refuses with E3106.
```maxon
typealias Level = int(1 to 9)
typealias Pair = FixedArray with 2 Level

type Cell
	export var value as ElementIndex
	export var n as ElementIndex

	static function slots(n ElementIndex) returns Count
		let pair = Pair.create(Cell{value: 1, n: n}.n)

		return pair.count()
	end 'slots'
end 'Cell'

function main() returns ExitCode
	return Cell.slots(2) as ExitCode
end 'main'
```
```maxoncstderr
error E3106: <fragment>:10:14: 'FixedArray.create' cannot publish slots of 'Level': every slot is zero and zero is not a value of 'Level'
```

<!-- test: refill-overwrites-every-element-in-place -->
Slots written one by one are all replaced, and the length is unchanged: `5 35`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	var buf = Buf.create(launder(5))

	for i in 0 upto buf.count() 'fill'
		try buf.set(i, value: i as Int) otherwise panic("set")
	end 'fill'

	buf.refill(7)

	var sum = 0 as Int

	for x in buf 'each'
		sum = sum + x
	end 'each'

	print("{buf.count()} {sum}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 35
```

<!-- test: refill-writes-a-value-that-is-not-zero-into-a-type-sized-array -->
`refill` reaches a type-sized fixed array too, and the new value may be any element: `3 6`.
```maxon
typealias Level = int(1 to 9)
typealias Trio = FixedArray with 3 Level

function main() returns ExitCode
	var trio = Trio.create(value: 1)

	trio.refill(2)

	print("{trio.count()} {trio.get(0) + trio.get(1) + trio.get(2)}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 6
```

<!-- test: refill-of-an-empty-fixed-array-does-nothing -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	var buf = Buf.create(launder(0))

	buf.refill(3)

	print("{buf.count()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: refill-packs-bools -->
Overwriting a bit-packed fixed array writes every bit, including the partial last byte: `10 0`.
```maxon
typealias Flags = FixedArray with bool

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	var flags = Flags.create(launder(10), value: true)

	flags.refill(false)

	var set = 0 as Count

	for flag in flags 'each'
		if flag 'isSet'
			set = set + 1
		end 'isSet'
	end 'each'

	print("{flags.count()} {set}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
10 0
```

<!-- test: error.a-managed-element-is-refused-with-a-value -->
A value does not make a managed element inline: the value form keeps the E2015 refusal the plain `create` gives.
```maxon
typealias Names = FixedArray with String

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	let names = Names.create(launder(2), value: "a")

	return names.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:9:14: Unsupported: `FixedArray` holds its elements INLINE and publishes every slot at `create`, so it has no element destructor to stamp and a 'String' element would leak its storage and be read back as a null slot — a fixed array's element is an integer, a bool or a float
```

<!-- test: error.a-value-form-needs-a-length-on-a-runtime-sized-alias -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function main() returns ExitCode
	let buf = Buf.create(value: 3)

	return buf.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3194: <fragment>:6:16: 'Buf' states no length in its type, so 'create' needs one: write 'Buf.create(n)'
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
	var a = Ints.create(0)
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
error E3185: <fragment>:10:18: 'ElementMemory' named outside the library tiers: the raw buffer type is nameable only from a source file under stdlib/ or runtime/ — use 'Array'
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
		var b = Bufs.create(0)
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
		var a = Arr.create(0)
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
		var b = Bufs.create(0)
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
	var a = Ints.create(0)
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

	var cells = CellArray.create(0)
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

	var cells = CellArray.create(0)
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
	var held as isolated Cell

	static function create(held isolated Cell) returns Self
		return Self{held: held}
	end 'create'

	export function report(other isolated Cell) returns isolated Cell
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

## A Fixed Array Beside Counted Records

A `FixedArray` holds scalars only, so a write into one never releases a record, and a read from one is never a record to hold. What these cases pin is that its being an array record does not change the verdict on the records around it: a record read out of an `Array` is still kept alive across the array's overwrite of that slot, whatever fixed arrays are written between the read and the use. Each exits 0, and a leak would exit 101.

<!-- test: a-record-read-from-an-array-survives-its-slots-overwrite-between-fixed-array-writes -->
`held` is the record in slot 0 of `nodes`. The overwrite of that slot releases the record `nodes` owned, and `held` is read after it between two writes to a fixed array: `7 1 2`.
```maxon
typealias Tally = int(0 to 1000)

type Node
	export var weight as Tally

	static function create(weight Tally) returns Node
		return Node{weight: weight}
	end 'create'
end 'Node'

typealias Nodes = Array with Node
typealias Marks = FixedArray with 2 Tally

function main() returns ExitCode
	var nodes = Nodes.create(0)
	nodes.push(Node.create(7))
	var marks = Marks.create()
	var held = try nodes.get(0) otherwise panic("get")
	marks.set(0, value: 1)
	try nodes.set(0, value: Node.create(9)) otherwise panic("set")
	marks.set(1, value: 2)
	print("{held.weight} {marks.get(0)} {marks.get(1)}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 1 2
```

<!-- test: a-record-read-through-a-field-survives-its-slots-overwrite-beside-a-fixed-array-field -->
The same read through a record that holds both containers as fields, so the read is a chain of a field hop and an element hop, and the fixed array it sits beside is a field of the same holder: `7 1 2`.
```maxon
typealias Tally = int(0 to 1000)

type Node
	export var weight as Tally

	static function create(weight Tally) returns Node
		return Node{weight: weight}
	end 'create'
end 'Node'

typealias Nodes = Array with Node
typealias Marks = FixedArray with 2 Tally

type Ledger
	export var nodes as Nodes
	export var marks as Marks

	static function create() returns Ledger
		return Ledger{nodes: Nodes.create(0), marks: Marks.create()}
	end 'create'
end 'Ledger'

function main() returns ExitCode
	var ledger = Ledger.create()
	ledger.nodes.push(Node.create(7))
	var held = try ledger.nodes.get(0) otherwise panic("get")
	ledger.marks.set(0, value: 1)
	try ledger.nodes.set(0, value: Node.create(9)) otherwise panic("set")
	ledger.marks.set(1, value: 2)
	print("{held.weight} {ledger.marks.get(0)} {ledger.marks.get(1)}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 1 2
```

<!-- test: a-generic-fixed-array-slot-write-keeps-the-element-it-replaced -->
A fixed array over a type parameter has elements the generic body cannot name, so the body is compiled for any element: the slot's old value is read, the slot is overwritten with the replacement, and the old value is returned. At `Int` the swaps answer `0 5`.
```maxon
typealias Int = int(i64.min to i64.max)

type Ring uses Element
	typealias Slots = FixedArray with 4 Element

	var slots as Slots

	static function create() returns Self
		return Self{slots: Slots.create()}
	end 'create'

	function swapFirst(replacement Element) returns Element
		var old = slots.get(0)
		slots.set(0, value: replacement)

		return old
	end 'swapFirst'
end 'Ring'

typealias IntRing = Ring with Int

function main() returns ExitCode
	var ring = IntRing.create()
	let before = ring.swapFirst(5)
	let after = ring.swapFirst(8)
	print("{before} {after}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 5
```

## A Runtime-Sized Fixed Array Takes Its Length At `create`

A `FixedArray with T` that states no count is a fixed array whose length is chosen when it is built: `Buf.create(n)` makes one heap allocation of `n` zeroed elements, and the length never changes afterwards. Its length is a fact about the value and not about the type, so `count()` and the bound of `get` and `set` read the record, and an index is never proven inside it: `get` and `set` need `try` (E3057 without it). A length that is a compile-time constant gives the type `FixedArray with n T` instead, so every property of a sized fixed array follows from it.

<!-- test: runtime-sized-create-holds-n-zeros -->
`launder` makes the length a value the compiler cannot fold. The array holds five elements, every one zero: `5 5`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	let buf = Buf.create(launder(5))
	var zeros = 0 as Int

	for x in buf 'each'
		if x == 0 'zero'
			zeros = zeros + 1
		end 'zero'
	end 'each'

	print("{buf.count()} {zeros}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 5
```

<!-- test: runtime-sized-set-and-get-round-trip -->
Every slot is written and read back through `try`, and the last slot is the fourth: `10 40`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	var buf = Buf.create(launder(4))
	var i = 0 as Int

	while i < 4 'fill'
		try buf.set(i as ElementIndex, value: (i + 1) * 10) otherwise panic("fill")
		i = i + 1
	end 'fill'

	let first = try buf.get(0) otherwise panic("first")
	let last = try buf.get(3) otherwise panic("last")
	print("{first} {last}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
10 40
```

<!-- test: runtime-sized-get-past-the-length-throws -->
The buffer under the array may be larger than three elements, and the bound is the length: the read at 3 throws.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	let buf = Buf.create(launder(3))
	let missed = try buf.get(launder(3)) otherwise -1
	print("{missed}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
-1
```

<!-- test: runtime-sized-set-past-the-length-throws -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	var buf = Buf.create(launder(3))

	try buf.set(launder(3), value: 99) otherwise 'oob'
		return 7
	end 'oob'

	return 0
end 'main'
```
```exitcode
7
```

<!-- test: error.a-runtime-sized-bare-get-requires-try -->
No index is proven inside a length the type does not state, so a bare `get` is E3057.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	let buf = Buf.create(launder(3))
	let first = buf.get(0)
	print("{first}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3057: <fragment>:11:18: throwing function requires try: 'stdlib.FixedArray.get'
```

<!-- test: runtime-sized-runtime-zero-length -->
A length of zero known only at run time is legal: the array is empty, a read throws and a `for` runs no iteration.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	let buf = Buf.create(launder(0))
	var seen = 0 as Int

	for _ in buf 'each'
		seen = seen + 1
	end 'each'

	let missed = try buf.get(0) otherwise -1
	print("{buf.count()} {seen} {missed}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 0 -1
```

<!-- test: runtime-sized-bool-packs-across-a-byte -->
A `bool` element is one bit, so seventeen of them straddle a third byte. Slots 0 and 16 are set, and the walk finds exactly those two: `17 2 true false`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Flags = FixedArray with bool

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	var flags = Flags.create(launder(17))
	try flags.set(0, value: true) otherwise panic("set 0")
	try flags.set(16, value: true) otherwise panic("set 16")

	var ones = 0 as Int

	for flag in flags 'each'
		if flag 'on'
			ones = ones + 1
		end 'on'
	end 'each'

	let last = try flags.get(16) otherwise panic("get 16")
	let before = try flags.get(15) otherwise panic("get 15")
	print("{flags.count()} {ones} {last} {before}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
17 2 true false
```

<!-- test: runtime-sized-float -->
```maxon
typealias Real = float(f64.min to f64.max)
typealias Samples = FixedArray with Real

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	var samples = Samples.create(launder(3))
	try samples.set(1, value: 2.5) otherwise panic("set")
	try samples.set(2, value: 4.0) otherwise panic("set")

	var total = 0.0 as Real

	for sample in samples 'each'
		total = total + sample
	end 'each'

	print("{samples.count()} {total}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 6.5
```

<!-- test: runtime-sized-as-field-and-parameter -->
A runtime-sized array is a field of a record and a parameter of a function, and in both places its length is the value's: `3 12`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

type Ledger
	export var cells as Buf

	static function create(size ElementIndex) returns Ledger
		return Ledger{cells: Buf.create(size)}
	end 'create'

	function record(value Int)
		try self.cells.set(0, value: value) otherwise panic("record")
		try self.cells.set(2, value: value) otherwise panic("record")
	end 'record'
end 'Ledger'

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function total(cells Buf) returns Int
	var sum = 0 as Int

	for cell in cells 'each'
		sum = sum + cell
	end 'each'

	return sum
end 'total'

function main() returns ExitCode
	var ledger = Ledger.create(launder(3))
	ledger.record(6)
	print("{ledger.cells.count()} {total(ledger.cells)}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 12
```

<!-- test: runtime-sized-in-a-generic-body -->
The generic body is compiled once for every element. It builds the array over its own type parameter at a length the caller chose, and the length comes back through `count()`: `4 7`.
```maxon
typealias Int = int(i64.min to i64.max)

type Ring uses Element
	typealias Slots = FixedArray with Element

	var slots as Slots

	static function create(size ElementIndex) returns Self
		return Self{slots: Slots.create(size)}
	end 'create'

	function put(at ElementIndex, value Element) throws ArrayError
		try slots.set(at, value: value)
	end 'put'

	function take(at ElementIndex) returns Element throws ArrayError
		return try slots.get(at)
	end 'take'

	function size() returns Count
		return slots.count()
	end 'size'
end 'Ring'

typealias IntRing = Ring with Int

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	var ring = IntRing.create(launder(4))
	try ring.put(3, value: 7) otherwise panic("put")
	let got = try ring.take(3) otherwise panic("take")
	print("{ring.size()} {got}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 7
```

<!-- test: an-extension-reading-countof-Self-answers-the-record-length -->
`countof(Self)` inside `extension FixedArray` is the receiver's count. For a runtime-sized receiver the count is not in the type, so the call site passes the record's length: `6`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

extension FixedArray
	function slots() returns Count
		return countof(Self)
	end 'slots'
end 'FixedArray'

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	let buf = Buf.create(launder(6))
	print("{buf.slots()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
6
```

<!-- test: error.a-type-sized-create-takes-no-length -->
The length is in the type, so `create` takes no argument.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function main() returns ExitCode
	let v = Vec3.create(3)

	return v.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3194: <fragment>:6:15: 'Vec3' states its length in its type (FixedArray with 3 Int), so 'create' takes no length: write 'Vec3.create()' or 'Vec3.create(value: v)'
```

<!-- test: error.a-runtime-sized-create-needs-a-length -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function main() returns ExitCode
	let buf = Buf.create()

	return buf.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3194: <fragment>:6:16: 'Buf' states no length in its type, so 'create' needs one: write 'Buf.create(n)'
```

<!-- test: error.a-runtime-sized-create-takes-no-zeroed-argument -->
A fixed array is always zeroed and never grows, so there is no `zeroed:` argument; the growable `Array` has the unpublished buffer.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function main() returns ExitCode
	let buf = Buf.create(4, zeroed: false)

	return buf.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3194: <fragment>:6:16: 'Buf.create' takes a length and an optional 'value:': a fixed array never grows. Use 'Array.create(n, zeroed: false)' for a growable buffer
```

<!-- test: error.push-is-refused-on-a-runtime-sized-fixed-array -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	var buf = Buf.create(launder(2))
	buf.push(4)

	return 0
end 'main'
```
```maxoncstderr
error E4006: <fragment>:11:6: Type 'FixedArray' has no method named 'push'
```

<!-- test: error.a-zero-excluding-element-under-create-n -->
The slots of `create(n)` are zero, and zero is not a value of `Positive`.
```maxon
typealias Positive = int(1 to 100)
typealias Buf = FixedArray with Positive

function main() returns ExitCode
	let buf = Buf.create(4)

	return buf.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3106: <fragment>:6:12: 'FixedArray.create' cannot publish slots of 'Positive': every slot is zero and zero is not a value of 'Positive'
```

<!-- test: error.an-enum-element-whose-raw-values-exclude-zero-under-create-n -->
The slots of `create(n)` are zero, and zero names no case of `Level`.
```maxon
enum Level
	low = 10
	high = 20
end 'Level'

typealias Buf = FixedArray with Level

function main() returns ExitCode
	let buf = Buf.create(4)

	return buf.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3106: <fragment>:10:12: 'FixedArray.create' cannot publish slots of 'Level': every slot is zero and zero is not a value of 'Level'
```

<!-- test: an-enum-element-with-a-zero-case-under-create-n -->
```maxon
enum Gauge
	idle = 0
	busy = 5
end 'Gauge'

typealias Buf = FixedArray with Gauge

function main() returns ExitCode
	let buf = Buf.create(4)
	let gauge = buf.get(3)

	print("{gauge.name}\n")

	return buf.count() as ExitCode
end 'main'
```
```exitcode
4
```
```stdout
idle
```

<!-- test: error.a-managed-element-under-create-n -->
```maxon
typealias Names = FixedArray with String

function main() returns ExitCode
	let names = Names.create(4)

	return names.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:14: Unsupported: `FixedArray` holds its elements INLINE and publishes every slot at `create`, so it has no element destructor to stamp and a 'String' element would leak its storage and be read back as a null slot — a fixed array's element is an integer, a bool or a float
```

<!-- test: error.a-length-of-another-integer-type-names-the-cast -->
A length that may be negative is not an `ElementIndex` until a cast proves it, as for `Array.create(n)`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function main() returns ExitCode
	var wanted = 5 as FullRangeInt
	wanted = wanted - 10
	let buf = Buf.create(wanted)

	return buf.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:8:12: argument type mismatch for 'newLength': expected 'ElementIndex', got 'FullRangeInt' — cast the argument with 'as ElementIndex'
```

<!-- test: a-constant-length-create-is-the-sized-type -->
`Buf.create(4)` is a `FixedArray with 4 Int`, unbranded, so it is accepted where the `Vec4` alias is declared, its bare `set` and `get` at proven indices need no `try`, and `count()` is the constant: `4 5`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int
typealias Buf = FixedArray with Int

function sumOf(v Vec4) returns Int
	return v.get(0) + v.get(1) + v.get(2) + v.get(3)
end 'sumOf'

function main() returns ExitCode
	var v = Buf.create(4)
	v.set(2, value: 5)
	print("{v.count()} {sumOf(v)}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 5
```

<!-- test: a-let-bound-a-top-level-let-and-a-countof-fold-are-constant-lengths -->
A `let` bound to a constant, a top-level `let` and a folded `countof` expression are all constants, so each array has its length in its type and its last element is read bare: `2 3 4`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int
typealias Buf = FixedArray with Int

let Width = 3

function main() returns ExitCode
	let local = 2
	let a = Buf.create(local)
	let b = Buf.create(Width)
	let c = Buf.create(countof(Vec3) + 1)
	print("{a.count()} {b.count()} {c.count()} {a.get(1) + b.get(2) + c.get(3)}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 3 4 0
```

<!-- test: a-var-length-is-the-constant-it-holds-where-it-is-read -->
A `var` that is reassigned only after the `create` still holds the known constant where the `create` reads it, so its array has the length in its type and a bare `get` at a proven index is legal: `3 0 4`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function main() returns ExitCode
	var n = 3 as ElementIndex
	let buf = Buf.create(n)
	n = n + 1
	print("{buf.count()} {buf.get(0)} {n}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 0 4
```

<!-- test: a-loop-counter-length-is-runtime-sized -->
A loop counter is no constant, so each array is runtime-sized and `try` on its `get` is required, not refused: `6 0`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function main() returns ExitCode
	var total = 0 as Int
	var firsts = 0 as Int

	for n in 1 to 3 'each'
		let buf = Buf.create(n)
		total = total + (buf.count() as Int)
		firsts = firsts + (try buf.get(0) otherwise -100)
	end 'each'

	print("{total} {firsts}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
6 0
```

<!-- test: error.try-on-a-proven-index-of-a-constant-length-create -->
The array is a `FixedArray with 4 Int`, so slot 0 is proven and the `try` is E3055, exactly as on a `Vec4`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function main() returns ExitCode
	let buf = Buf.create(4)
	let first = try buf.get(0) otherwise -1

	return first as ExitCode
end 'main'
```
```maxoncstderr
error E3055: <fragment>:7:14: try requires a throwing function: this `get` cannot fail — its index is already proven inside the fixed array's 4 elements, so there is no out-of-bounds error to catch
```

<!-- test: error.a-constant-index-past-a-constant-length -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function main() returns ExitCode
	let buf = Buf.create(4)
	let last = buf.get(4)

	return last as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:17: Value 4 is outside the range of the indices of 'FixedArray with 4' (int(0 to 3))
```

<!-- test: error.a-constant-length-of-zero -->
A fixed array of zero constant length has nothing to hold: it is E2069, as `FixedArray with 0 Int` is.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function main() returns ExitCode
	let buf = Buf.create(0)

	return buf.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2069: <fragment>:6:23: A `FixedArray` element count runs from 1 to 4294967295, and create's length is the constant 0
```

<!-- test: a-top-level-let-of-a-constant-length-create-is-sized -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

let table = Buf.create(4)

function main() returns ExitCode
	print("{table.count()} {table.get(3)}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 0
```

<!-- test: a-sized-fixed-array-widens-to-runtime-sized -->
A `Vec3` is accepted where a `Buf` is declared: the record's length is the 3 the type states, so nothing converts. The callee sees a runtime-sized array, so its `get` needs `try`: `14`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int
typealias Buf = FixedArray with Int

function describe(buf Buf) returns Int
	let last = try buf.get(2) otherwise -1
	return (buf.count() as Int) * 3 + last
end 'describe'

function main() returns ExitCode
	var v = Vec3.create()
	v.set(2, value: 5)
	print("{describe(v)}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
14
```

<!-- test: a-literal-through-a-runtime-sized-alias-is-sized -->
`Buf from [...]` writes its length, so it is a `FixedArray with 3 Int` and its bare `get` at 2 is proven: `3 30`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function main() returns ExitCode
	let buf = Buf from [10, 20, 30]
	print("{buf.count()} {buf.get(2)}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 30
```

<!-- test: error.a-runtime-sized-value-where-a-sized-one-is-declared -->
The widening runs one way. A runtime-sized value has no length in its type to satisfy a `Vec3`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int
typealias Buf = FixedArray with Int

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function takes(v Vec3) returns Int
	return v.get(0)
end 'takes'

function main() returns ExitCode
	let buf = Buf.create(launder(3))

	return takes(buf) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:17:9: argument type mismatch for 'v': expected 'Vec3', got 'Buf' — a runtime-sized fixed array's length is not part of its type; build it with a constant length
```

<!-- test: error.fixed-array-with-a-constant-name-as-count -->
The count of a `FixedArray with` spelling is an integer literal. A name before the element type is refused for that, and the element type after it is read as written.
```maxon
typealias Int = int(i64.min to i64.max)

let K = 3

typealias Vec = FixedArray with K Int

function main() returns ExitCode
	return K as ExitCode
end 'main'
```
```maxoncstderr
error E2069: <fragment>:6:33: A `FixedArray` element count runs from 1 to 4294967295, and the element count of a `FixedArray` type is an integer literal, not `K`
```

<!-- test: error.fixed-array-with-a-negative-count -->
```maxon
typealias Int = int(i64.min to i64.max)

typealias Vec = FixedArray with -3 Int

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2069: <fragment>:4:33: A `FixedArray` element count runs from 1 to 4294967295, and the element count of a `FixedArray` type is an integer literal, not `-3`
```

<!-- test: error.fixed-array-with-a-float-count -->
```maxon
typealias Int = int(i64.min to i64.max)

typealias Vec = FixedArray with 3.5 Int

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2069: <fragment>:4:33: A `FixedArray` element count runs from 1 to 4294967295, and the element count of a `FixedArray` type is an integer literal, not `3.5`
```

<!-- test: runtime-sized-as-an-array-element -->
A runtime-sized array is an element of a growable `Array`, and a sized one pushed there widens to the element type. Each element answers its own length when it is read back and written through: `2 4 7`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int
typealias Bufs = Array with Buf

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	var bufs = Bufs.create(0)
	bufs.push(Buf.create(launder(2)))
	bufs.push(Buf.create(4))

	var second = try bufs.get(1) otherwise panic("second")
	try second.set(3, value: 7) otherwise panic("set")

	var first = try bufs.get(0) otherwise panic("first")
	let written = try second.get(3) otherwise panic("get")
	print("{first.count()} {second.count()} {written}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 4 7
```

<!-- test: runtime-sized-in-a-closure -->
A closure reads a runtime-sized array it captured, and the length it sees is the record's: `14`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	var buf = Buf.create(launder(5))
	try buf.set(4, value: 9) otherwise panic("set")

	let probe = function() gives (try buf.get(4) otherwise -1) + (buf.count() as Int)
	print("{probe()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
14
```

<!-- test: error.a-runtime-sized-alias-is-not-built-by-a-struct-literal -->
A runtime-sized alias is built by `create(n)` alone. A struct literal of it is the refusal every record gets.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function main() returns ExitCode
	let buf = Buf{}

	return buf.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3076: <fragment>:6:16: type 'Buf' can only be constructed from within its own methods; use a static factory method instead
```

<!-- test: error.an-inner-runtime-sized-alias-literal-states-no-length -->
Inside the type that declares the alias, `Cells{}` would be an empty container for a growable `Array`; for a runtime-sized fixed array there is no length to build it at.
```maxon
typealias Int = int(i64.min to i64.max)

type Holder
	typealias Cells = FixedArray with Int

	static function make() returns Int
		let cells = Cells{}

		return cells.count() as Int
	end 'make'
end 'Holder'

function main() returns ExitCode
	return Holder.make() as ExitCode
end 'main'
```
```maxoncstderr
error E3194: <fragment>:8:15: 'Cells' states no length in its type, so 'create' needs one: write 'Cells.create(n)'
```

<!-- test: runtime-sized-reached-through-a-witness-reads-its-record-length -->
An interface conformance declared on `extension FixedArray` is dispatched through a witness table. The table's adapter passes the count the impl reads, and for a runtime-sized instance that count is the receiver's record length: `6`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

interface Measured
	function measure() returns Count
end 'Measured'

extension FixedArray implements Measured
	function measure() returns Count
		return countof(Self)
	end 'measure'
end 'FixedArray'

type Probe uses Item where Item is Measured
	var item as Item

	static function create(item Item) returns Self
		return Self{item: item}
	end 'create'

	function measured() returns Count
		return self.item.measure()
	end 'measured'
end 'Probe'

typealias BufProbe = Probe with Buf

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	let probe = BufProbe.create(Buf.create(launder(6)))
	print("{probe.measured()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
6
```

<!-- test: error.a-static-witness-reading-countof-Self-through-a-runtime-sized-alias -->
A `static` requirement is not dispatchable through a type parameter at all, so a witness table can never hand a runtime-sized instance a count that only a record could answer: the call is refused before any table is read.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

interface Counting
	static function slots() returns Count
end 'Counting'

extension FixedArray implements Counting
	static function slots() returns Count
		return countof(Self)
	end 'slots'
end 'FixedArray'

type Probe uses Item where Item is Counting
	var item as Item

	static function create(item Item) returns Self
		return Self{item: item}
	end 'create'

	function slots() returns Count
		return Item.slots()
	end 'slots'
end 'Probe'

typealias BufProbe = Probe with Buf

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	let probe = BufProbe.create(Buf.create(launder(6)))
	print("{probe.slots()}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3116: <fragment>:23:15: 'Item.slots' is a `static` requirement, and a `static` member has no receiver to dispatch on — so the type parameter 'Item' cannot reach it. Call a `static` member on a concrete type
```

<!-- test: a-conditional-joins-a-sized-and-a-runtime-sized-fixed-array-in-either-order -->
The two arms meet in a type that holds either, which is the runtime-sized one whichever arm is written first. Both results are runtime-sized, so `get` needs `try`: `8 8 0 0`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int
typealias Buf = FixedArray with Int

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	let flag = launder(1) == 1
	let first = Vec3.create() if flag else Buf.create(launder(5))
	let second = Buf.create(launder(5)) if flag else Vec3.create()
	let a = try first.get(0) otherwise -1
	let b = try second.get(0) otherwise -1
	print("{first.count() + second.count()} {second.count() + first.count()} {a} {b}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
8 8 0 0
```

<!-- test: a-generic-type-built-from-a-sized-and-a-runtime-sized-fixed-array-in-either-order -->
A generic factory takes the type argument from its arguments. A sized and a runtime-sized fixed array give the runtime-sized one whichever is written first: `8 8`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int
typealias Buf = FixedArray with Int

type Pair uses T
	export var left as T
	export var right as T

	static function create(left T, right T) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	let sizedFirst = Pair.create(Vec3.create(), right: Buf.create(launder(5)))
	let runtimeFirst = Pair.create(Buf.create(launder(5)), right: Vec3.create())
	print("{sizedFirst.left.count() + sizedFirst.right.count()} {runtimeFirst.left.count() + runtimeFirst.right.count()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
8 8
```

<!-- test: a-sized-fixed-array-widens-on-a-return-and-on-a-field-store -->
A `Vec3` returned from a function declared to return a `Buf`, written into a `Buf` field by a struct literal and stored into it by a later assignment: `3 3 3`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int
typealias Buf = FixedArray with Int

type Holder
	export var cells as Buf

	static function create() returns Holder
		return Holder{cells: Vec3.create()}
	end 'create'
end 'Holder'

function make() returns Buf
	return Vec3.create()
end 'make'

function main() returns ExitCode
	let made = make()
	var holder = Holder.create()
	let first = holder.cells.count()
	holder.cells = Vec3.create()
	print("{made.count()} {first} {holder.cells.count()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 3 3
```

<!-- test: a-literal-through-an-inner-runtime-sized-alias-is-sized -->
`Cells from [...]` inside the type that declares the alias writes its length, so the result is a `FixedArray with 3 Int` and a bare `get` at 2 is proven: `3 6`.
```maxon
typealias Int = int(i64.min to i64.max)

type Holder
	typealias Cells = FixedArray with Int

	static function make() returns Int
		let cells = Cells from [4, 5, 6]

		return (cells.count() as Int) * 100 + cells.get(2)
	end 'make'
end 'Holder'

function main() returns ExitCode
	print("{Holder.make()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
306
```

<!-- test: error.a-constant-length-above-the-largest-count -->
A constant length past `4294967295` cannot be a type's count either: it is E2069.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function main() returns ExitCode
	let buf = Buf.create(4294967296)

	return buf.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2069: <fragment>:6:23: A `FixedArray` element count runs from 1 to 4294967295, and create's length is the constant 4294967296
```

<!-- test: a-negative-runtime-length-panics-at-the-range-check -->
A length that is negative at run time is not an `ElementIndex`: the cast that proves it panics where it is written.
```maxon
typealias Signed = int(i64.min to i64.max)
typealias Buf = FixedArray with Signed

function launder(n Signed) returns Signed
	return n
end 'launder'

function main() returns ExitCode
	let buf = Buf.create(launder(-1) as ElementIndex)

	return buf.count() as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at a-negative-runtime-length-panics-at-the-range-check.test:10: Range check failed: value outside typealias 'ElementIndex'
Stack trace:
  in main
  in mrt_start
```

<!-- test: error.a-negative-constant-length-is-one-diagnostic -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

function main() returns ExitCode
	let buf = Buf.create(-1)

	return buf.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:12: Value -1 is outside the range of 'ElementIndex' (int(0 to 18446744073709551615))
```

<!-- test: runtime-sized-reached-through-a-witness-table-that-also-holds-a-static-requirement -->
A conformance whose interface also declares a `static` requirement reading the count builds a table with a slot for each. The instance slot reads the record's length; the static slot is never dispatched through the type parameter, so the table needs no receiver for it: `6`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Buf = FixedArray with Int

interface Measured
	function measure() returns Count
	static function slots() returns Count
end 'Measured'

extension FixedArray implements Measured
	function measure() returns Count
		return countof(Self)
	end 'measure'

	static function slots() returns Count
		return countof(Self)
	end 'slots'
end 'FixedArray'

type Probe uses Item where Item is Measured
	var item as Item

	static function create(item Item) returns Self
		return Self{item: item}
	end 'create'

	function measured() returns Count
		return self.item.measure()
	end 'measured'
end 'Probe'

typealias BufProbe = Probe with Buf

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	let probe = BufProbe.create(Buf.create(launder(6)))
	print("{probe.measured()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
6
```

<!-- test: a-sized-fixed-array-minted-in-two-files-is-one-type -->
`Buf.create(4)` in one file and `Buf from [...]` in another each build a `FixedArray with 4 Int` while their bodies are parsed. Both are the one instance the `Vec4` alias names, so each value crosses to a `Vec4` parameter in the other file: `9 4`.
```maxon
// --- file: lib/types.maxon
module typealias Int = int(i64.min to i64.max)
module typealias Vec4 = FixedArray with 4 Int
module typealias Buf = FixedArray with Int

// --- file: lib/a.maxon
module function fromA() returns Vec4
	return Buf.create(4)
end 'fromA'

module function lastOf(v Vec4) returns Int
	return v.get(3)
end 'lastOf'

// --- file: lib/b.maxon
module function sumB(v Vec4) returns Int
	let own = Buf from [1, 2, 3, 4]

	return v.get(3) + lastOf(own)
end 'sumB'

// --- file: lib/main.maxon
function main() returns ExitCode
	var v = fromA()
	v.set(3, value: 5)
	print("{sumB(v)} {lastOf(Buf.create(4)) + 4}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
9 4
```

<!-- test: a-try-otherwise-joins-a-sized-and-a-runtime-sized-fixed-array-in-either-order -->
The call's result and its fallback meet in the runtime-sized type whichever of them is the sized one, as the arms of a conditional do. Each result is runtime-sized, so `get` needs `try`, and each keeps the length of the array that reached the merge: `5 5 3 3 0 0`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int
typealias Buf = FixedArray with Int

enum ProbeError implements Error
	failed
end 'ProbeError'

function sizedOrFail(fail bool) returns Vec3 throws ProbeError
	if fail 'failing'
		throw ProbeError.failed
	end 'failing'

	return Vec3.create()
end 'sizedOrFail'

function runtimeOrFail(fail bool, n ElementIndex) returns Buf throws ProbeError
	if fail 'failing'
		throw ProbeError.failed
	end 'failing'

	return Buf.create(n)
end 'runtimeOrFail'

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function main() returns ExitCode
	let first = try sizedOrFail(true) otherwise Buf.create(launder(5))
	let second = try runtimeOrFail(false, n: launder(5)) otherwise Vec3.create()
	let third = try sizedOrFail(false) otherwise Buf.create(launder(5))
	let fourth = try runtimeOrFail(true, n: launder(5)) otherwise Vec3.create()
	let a = try first.get(0) otherwise -1
	let b = try third.get(0) otherwise -1
	print("{first.count()} {second.count()} {third.count()} {fourth.count()} {a} {b}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 5 3 3 0 0
```

## A Sized Fixed Array Strides By Its Element, Not By The Machine Word

<!-- test: a-sized-fixed-array-strides-by-its-element-and-reads-back-every-element -->
<!-- tier: stdlib -->
A fixed array whose type states its length lays its elements out at the element's own width, exactly as the runtime-sized form does: one byte for a `Byte` or a `bits(8)`, two for a 16-bit element, four for a 32-bit one, and a packed 1, 2 or 4 bits for a `bool`, an `int(0 to 3)` and an `int(0 to 15)`. Every element written through `set` reads back through `get`, through `for` and through a `clone`.
```maxon
typealias Half = int(0 to 65535)
typealias Wide = int(0 to u32.max)
typealias Octet = bits(8)
typealias Lane = bits(16)
typealias Crumb = int(0 to 3)
typealias Nibble = int(0 to 15)
typealias Bytes = FixedArray with 300 Byte
typealias Halves = FixedArray with 300 Half
typealias Wides = FixedArray with 300 Wide
typealias Octets = FixedArray with 300 Octet
typealias Lanes = FixedArray with 300 Lane
typealias Flags = FixedArray with 301 bool
typealias Crumbs = FixedArray with 301 Crumb
typealias Nibbles = FixedArray with 301 Nibble

extension FixedArray
	function strideText() returns String
		return "{managed.elementSize()}"
	end 'strideText'
end 'FixedArray'

function bytes() returns String
	var cells = Bytes.create()

	for at in 0 upto 300 'write'
		cells.set(at, value: (at mod 251) as Byte)
	end 'write'

	var viaGet = 0 as Count

	for at in 0 upto 300 'readEach'
		viaGet = viaGet + (cells.get(at) as Count)
	end 'readEach'

	var viaFor = 0 as Count

	for cell in cells 'walk'
		viaFor = viaFor + (cell as Count)
	end 'walk'

	var viaClone = 0 as Count
	let copy = cells.clone()

	for cell in copy 'walkCopy'
		viaClone = viaClone + (cell as Count)
	end 'walkCopy'

	return "byte {cells.strideText()} {copy.strideText()} {viaGet} {viaFor} {viaClone}"
end 'bytes'

function halves() returns String
	var cells = Halves.create()

	for at in 0 upto 300 'write'
		cells.set(at, value: ((at * 217) mod 65536) as Half)
	end 'write'

	var viaGet = 0 as Count

	for at in 0 upto 300 'readEach'
		viaGet = viaGet + (cells.get(at) as Count)
	end 'readEach'

	var viaFor = 0 as Count

	for cell in cells 'walk'
		viaFor = viaFor + (cell as Count)
	end 'walk'

	var viaClone = 0 as Count
	let copy = cells.clone()

	for cell in copy 'walkCopy'
		viaClone = viaClone + (cell as Count)
	end 'walkCopy'

	return "half {cells.strideText()} {copy.strideText()} {viaGet} {viaFor} {viaClone}"
end 'halves'

function wides() returns String
	var cells = Wides.create()

	for at in 0 upto 300 'write'
		cells.set(at, value: ((at * 16777259) mod 4294967296) as Wide)
	end 'write'

	var viaGet = 0 as Count

	for at in 0 upto 300 'readEach'
		viaGet = viaGet + (cells.get(at) as Count)
	end 'readEach'

	var viaFor = 0 as Count

	for cell in cells 'walk'
		viaFor = viaFor + (cell as Count)
	end 'walk'

	var viaClone = 0 as Count
	let copy = cells.clone()

	for cell in copy 'walkCopy'
		viaClone = viaClone + (cell as Count)
	end 'walkCopy'

	return "wide {cells.strideText()} {copy.strideText()} {viaGet} {viaFor} {viaClone}"
end 'wides'

function octets() returns String
	var cells = Octets.create()

	for at in 0 upto 300 'write'
		cells.set(at, value: (at mod 251) as Octet)
	end 'write'

	var viaGet = 0 as Count

	for at in 0 upto 300 'readEach'
		viaGet = viaGet + (cells.get(at) as Count)
	end 'readEach'

	var viaFor = 0 as Count

	for cell in cells 'walk'
		viaFor = viaFor + (cell as Count)
	end 'walk'

	var viaClone = 0 as Count
	let copy = cells.clone()

	for cell in copy 'walkCopy'
		viaClone = viaClone + (cell as Count)
	end 'walkCopy'

	return "octet {cells.strideText()} {copy.strideText()} {viaGet} {viaFor} {viaClone}"
end 'octets'

function lanes() returns String
	var cells = Lanes.create()

	for at in 0 upto 300 'write'
		cells.set(at, value: ((at * 217) mod 65536) as Lane)
	end 'write'

	var viaGet = 0 as Count

	for at in 0 upto 300 'readEach'
		viaGet = viaGet + (cells.get(at) as Count)
	end 'readEach'

	var viaFor = 0 as Count

	for cell in cells 'walk'
		viaFor = viaFor + (cell as Count)
	end 'walk'

	var viaClone = 0 as Count
	let copy = cells.clone()

	for cell in copy 'walkCopy'
		viaClone = viaClone + (cell as Count)
	end 'walkCopy'

	return "lane {cells.strideText()} {copy.strideText()} {viaGet} {viaFor} {viaClone}"
end 'lanes'

function flags() returns String
	var cells = Flags.create()

	for at in 0 upto 301 'write'
		cells.set(at, value: at mod 3 == 0)
	end 'write'

	var viaGet = 0 as Count

	for at in 0 upto 301 'readEach'
		if cells.get(at) 'set'
			viaGet = viaGet + 1
		end 'set'
	end 'readEach'

	var viaFor = 0 as Count

	for cell in cells 'walk'
		if cell 'set'
			viaFor = viaFor + 1
		end 'set'
	end 'walk'

	var viaClone = 0 as Count
	let copy = cells.clone()

	for cell in copy 'walkCopy'
		if cell 'set'
			viaClone = viaClone + 1
		end 'set'
	end 'walkCopy'

	return "flag {cells.strideText()} {copy.strideText()} {viaGet} {viaFor} {viaClone}"
end 'flags'

function crumbs() returns String
	var cells = Crumbs.create()

	for at in 0 upto 301 'write'
		cells.set(at, value: (at mod 4) as Crumb)
	end 'write'

	var viaGet = 0 as Count

	for at in 0 upto 301 'readEach'
		viaGet = viaGet + (cells.get(at) as Count)
	end 'readEach'

	var viaFor = 0 as Count

	for cell in cells 'walk'
		viaFor = viaFor + (cell as Count)
	end 'walk'

	var viaClone = 0 as Count
	let copy = cells.clone()

	for cell in copy 'walkCopy'
		viaClone = viaClone + (cell as Count)
	end 'walkCopy'

	return "crumb {cells.strideText()} {copy.strideText()} {viaGet} {viaFor} {viaClone}"
end 'crumbs'

function nibbles() returns String
	var cells = Nibbles.create()

	for at in 0 upto 301 'write'
		cells.set(at, value: (at mod 16) as Nibble)
	end 'write'

	var viaGet = 0 as Count

	for at in 0 upto 301 'readEach'
		viaGet = viaGet + (cells.get(at) as Count)
	end 'readEach'

	var viaFor = 0 as Count

	for cell in cells 'walk'
		viaFor = viaFor + (cell as Count)
	end 'walk'

	var viaClone = 0 as Count
	let copy = cells.clone()

	for cell in copy 'walkCopy'
		viaClone = viaClone + (cell as Count)
	end 'walkCopy'

	return "nibble {cells.strideText()} {copy.strideText()} {viaGet} {viaFor} {viaClone}"
end 'nibbles'

function main() returns ExitCode
	print("{bytes()}\n")
	print("{halves()}\n")
	print("{wides()}\n")
	print("{octets()}\n")
	print("{lanes()}\n")
	print("{flags()}\n")
	print("{crumbs()}\n")
	print("{nibbles()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
byte 1 1 32551 32551 32551
half 2 2 9732450 9732450 9732450
wide 4 4 563481505126 563481505126 563481505126
octet 1 1 32551 32551 32551
lane 2 2 9732450 9732450 9732450
flag -1 -1 101 101 101
crumb -2 -2 450 450 450
nibble -4 -4 2238 2238 2238
```

<!-- test: a-second-type-parameter-strides-by-its-own-instantiation -->
<!-- tier: stdlib -->
A generic type holding a fixed array over its SECOND parameter lays the elements out at that parameter's instantiation, whatever the first parameter is: one byte for `Byte` beside an `Integer`, a packed bit for `bool` beside a `String`. Every element reads back through `get` and through `for`.
```maxon
typealias Integer = int(i64.min to i64.max)

extension FixedArray
	function strideText() returns String
		return "{managed.elementSize()}"
	end 'strideText'
end 'FixedArray'

type Two uses A, B
	typealias Cells = FixedArray with 8 B

	var tag as A
	var cells as Cells

	static function create(tag A) returns Self
		return Self{tag: tag, cells: Cells.create()}
	end 'create'

	function put(at ElementIndex, value B)
		try cells.set(at, value: value) otherwise panic("put")
	end 'put'

	function take(at ElementIndex) returns B
		return try cells.get(at) otherwise panic("take")
	end 'take'

	function walked() returns Count
		var walked = 0 as Count

		for _ in cells 'walk'
			walked = walked + 1
		end 'walk'

		return walked
	end 'walked'

	function strideText() returns String
		return cells.strideText()
	end 'strideText'
end 'Two'

typealias ByteTwo = Two with (Integer, Byte)
typealias FlagTwo = Two with (String, bool)

function main() returns ExitCode
	var bytes = ByteTwo.create(7)
	var flags = FlagTwo.create("flags")

	for at in 0 upto 8 'write'
		bytes.put(at, value: (at * 31) as Byte)
		flags.put(at, value: at mod 3 == 0)
	end 'write'

	var byteSum = 0 as Count
	var flagCount = 0 as Count

	for at in 0 upto 8 'read'
		byteSum = byteSum + (bytes.take(at) as Count)

		if flags.take(at) 'set'
			flagCount = flagCount + 1
		end 'set'
	end 'read'

	print("byte {bytes.strideText()} {byteSum} {bytes.walked()}\n")
	print("flag {flags.strideText()} {flagCount} {flags.walked()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
byte 1 868 8
flag -1 3 8
```

<!-- test: a-nested-generic-passes-its-instantiation-to-the-fixed-array-it-holds -->
<!-- tier: stdlib -->
A generic `Outer uses T` holding an `Inner with T`, whose `create` builds a `FixedArray with 6 T`, reaches that array through the descriptor table: the elements still stride at `T`'s own width, one byte for `Byte` and a packed bit for `bool`.
```maxon
extension FixedArray
	function strideText() returns String
		return "{managed.elementSize()}"
	end 'strideText'
end 'FixedArray'

type Inner uses T
	typealias Slots = FixedArray with 6 T

	var slots as Slots

	static function create() returns Self
		return Self{slots: Slots.create()}
	end 'create'

	function put(at ElementIndex, value T)
		try slots.set(at, value: value) otherwise panic("put")
	end 'put'

	function take(at ElementIndex) returns T
		return try slots.get(at) otherwise panic("take")
	end 'take'

	function strideText() returns String
		return slots.strideText()
	end 'strideText'
end 'Inner'

type Outer uses T
	typealias Held = Inner with T

	var inner as Held

	static function create() returns Self
		return Self{inner: Held.create()}
	end 'create'

	function put(at ElementIndex, value T)
		inner.put(at, value: value)
	end 'put'

	function take(at ElementIndex) returns T
		return inner.take(at)
	end 'take'

	function strideText() returns String
		return inner.strideText()
	end 'strideText'
end 'Outer'

typealias ByteOuter = Outer with Byte
typealias FlagOuter = Outer with bool

function main() returns ExitCode
	var bytes = ByteOuter.create()
	var flags = FlagOuter.create()

	for at in 0 upto 6 'write'
		bytes.put(at, value: (at * 41) as Byte)
		flags.put(at, value: at mod 2 == 1)
	end 'write'

	var byteSum = 0 as Count
	var flagCount = 0 as Count

	for at in 0 upto 6 'read'
		byteSum = byteSum + (bytes.take(at) as Count)

		if flags.take(at) 'set'
			flagCount = flagCount + 1
		end 'set'
	end 'read'

	print("byte {bytes.strideText()} {byteSum}\n")
	print("flag {flags.strideText()} {flagCount}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
byte 1 615
flag -1 3
```

<!-- test: a-user-generic-over-a-packed-element-strides-by-its-bits -->
<!-- tier: stdlib -->
A user generic over a packed element passes a negative stride through its own body: forty `bool` elements pack into five bytes, and 40 `int(0 to 3)` elements into ten, and every element written through the body reads back.
```maxon
typealias Crumb = int(0 to 3)

extension FixedArray
	function strideText() returns String
		return "{managed.elementSize()}"
	end 'strideText'
end 'FixedArray'

type Bits uses T
	typealias Cells = FixedArray with 40 T

	var cells as Cells

	static function create() returns Self
		return Self{cells: Cells.create()}
	end 'create'

	function put(at ElementIndex, value T)
		try cells.set(at, value: value) otherwise panic("put")
	end 'put'

	function take(at ElementIndex) returns T
		return try cells.get(at) otherwise panic("take")
	end 'take'

	function strideText() returns String
		return cells.strideText()
	end 'strideText'
end 'Bits'

typealias FlagBits = Bits with bool
typealias CrumbBits = Bits with Crumb

function main() returns ExitCode
	var flags = FlagBits.create()
	var crumbs = CrumbBits.create()

	for at in 0 upto 40 'write'
		flags.put(at, value: at mod 5 == 0)
		crumbs.put(at, value: (at mod 4) as Crumb)
	end 'write'

	var flagCount = 0 as Count
	var crumbSum = 0 as Count

	for at in 0 upto 40 'read'
		if flags.take(at) 'set'
			flagCount = flagCount + 1
		end 'set'

		crumbSum = crumbSum + (crumbs.take(at) as Count)
	end 'read'

	print("flag {flags.strideText()} {flagCount}\n")
	print("crumb {crumbs.strideText()} {crumbSum}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
flag -1 8
crumb -2 60
```
