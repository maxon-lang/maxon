---
feature: store-detaches-before-it-stores
status: experimental
keywords: [optimizer, codegen, array, fixed array, copy-on-write, view, immortal, destructor, inline, managed-memory]
category: codegen
---
# An inlined element store gives the record a private buffer before it writes

## Documentation

`InlineManagedPrimitives` expands every `set` into the guards that decide whether the store may land in the
buffer as it stands: the buffer exists, it is this record's, and nobody views it. A guard that fails calls
`__managed_cow_detach` and rejoins, and ONE store follows the join. The cases below put the store on each
side of every guard, and an element type that owes a destructor on the arm that is not inlined at all.

## Tests

<!-- test: a-proven-store-through-a-viewed-owner-detaches-first -->
<!-- tier: stdlib -->
The owner of a viewed buffer is written at a proven index. The view keeps what it read (11 and 22) and the
owner sees the new words (99 and 98): the store went to a private copy.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function main() returns ExitCode
	let v = Vec3.create()
	v.set(0, value: 11)
	v.set(1, value: 22)
	let seen = try v.managed.slice(0, 3) otherwise panic("slice: 0..3 of a three-element fixed array")
	v.set(0, value: 99)
	v.set(1, value: 98)

	let first = try seen.get(0) otherwise panic("get: index 0 of a three-element view")
	let second = try seen.get(1) otherwise panic("get: index 1 of a three-element view")

	if first == 11 and second == 22 and v.get(0) == 99 and v.get(1) == 98 'detached'
		return 0
	end 'detached'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: a-view-taken-between-two-proven-stores-forces-the-second-to-detach -->
<!-- tier: stdlib -->
The first store establishes that the buffer is private; the view taken after it takes that away, so the
second store must detach again. A store that trusted the first would write through the view.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function main() returns ExitCode
	let v = Vec3.create()
	v.set(0, value: 1)
	let seen = try v.managed.slice(0, 3) otherwise panic("slice: 0..3 of a three-element fixed array")
	v.set(1, value: 2)
	v.set(2, value: 3)

	let first = try seen.get(0) otherwise panic("get: index 0 of a three-element view")
	let second = try seen.get(1) otherwise panic("get: index 1 of a three-element view")
	let third = try seen.get(2) otherwise panic("get: index 2 of a three-element view")

	if first == 1 and second == 0 and third == 0 and v.get(1) == 2 and v.get(2) == 3 'detached'
		return 0
	end 'detached'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: a-checked-store-through-a-viewed-owner-detaches-first -->
The same hazard on the checked `set`, and on a growable array whose bound is its length.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	let arr = IntArray from [11, 22, 33]
	let sub = try arr.slice(0, endIndex: 2) otherwise panic("slice: 0..2 of a length-3 array")
	try arr.set(0, value: 99) otherwise panic("set: index 0 of a length-3 array")
	try arr.set(1, value: 98) otherwise panic("set: index 1 of a length-3 array")

	let viewed = try sub.get(0) otherwise panic("get: index 0 of a length-2 view")
	let owner = try arr.get(0) otherwise panic("get: index 0 of a length-3 array")
	let viewedSecond = try sub.get(1) otherwise panic("get: index 1 of a length-2 view")

	if viewed == 11 and owner == 99 and viewedSecond == 22 'detached'
		return 0
	end 'detached'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: a-store-into-a-view-detaches-the-view-and-leaves-its-parent -->
The view is not its buffer's owner. Writing it gives it a private copy and the parent keeps its words.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	let arr = IntArray from [11, 22, 33]
	let sub = try arr.slice(1, endIndex: 3) otherwise panic("slice: 1..3 of a length-3 array")
	try sub.set(0, value: 77) otherwise panic("set: index 0 of a length-2 view")
	try sub.set(1, value: 78) otherwise panic("set: index 1 of a length-2 view")

	let parentFirst = try arr.get(1) otherwise panic("get: index 1 of a length-3 array")
	let parentSecond = try arr.get(2) otherwise panic("get: index 2 of a length-3 array")
	let viewFirst = try sub.get(0) otherwise panic("get: index 0 of a length-2 view")

	if parentFirst == 22 and parentSecond == 33 and viewFirst == 77 'detached'
		return 0
	end 'detached'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: a-store-into-an-immortal-literal-never-reaches-its-image -->
A byte string literal is an immortal image, written only through its `.clone()`. Each call reads the literal, writes the clone and
reads the copy back, so a write that reached the image would show on the second call (or fault).
```maxon
function bumped() returns Byte
	let text = b"abc".clone()
	let before = try text.get(0) otherwise panic("get: index 0 of a three-byte literal")
	try text.set(0, value: 90) otherwise panic("set: index 0 of a three-byte literal")
	let after = try text.get(0) otherwise panic("get: index 0 of a three-byte literal")

	if after != 90 'stored'
		panic("the store was lost")
	end 'stored'

	return before
end 'bumped'

function main() returns ExitCode
	let first = bumped()
	let second = bumped()

	if first == 97 and second == 97 'untouched'
		return 0
	end 'untouched'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: a-proven-store-into-a-literal-fixed-array-never-reaches-its-image -->
A fixed array built from a literal may be an immortal image. The clone has a private buffer that the proven
stores write, and a second call over the same literal still reads the original.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function bumped() returns Int
	let v = (Vec3 from [1, 2, 3]).clone()
	let before = v.get(0)
	v.set(0, value: 10)
	v.set(1, value: 20)

	if v.get(0) != 10 or v.get(1) != 20 'stored'
		panic("the store was lost")
	end 'stored'

	return before
end 'bumped'

function main() returns ExitCode
	let first = bumped()
	let second = bumped()

	if first == 1 and second == 1 'untouched'
		return 0
	end 'untouched'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: a-store-into-an-empty-array-throws-and-allocates-nothing -->
An array that never grew has no buffer. The checked store refuses before it detaches, so the array stays
empty and unallocated.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	let arr = IntArray.create(0)
	var refused = false

	try arr.set(0, value: 5) otherwise 'outOfRange'
		refused = true
	end 'outOfRange'

	if refused and arr.count() == 0 and arr.capacity() == 0 'untouched'
		return 0
	end 'untouched'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: a-refused-store-into-a-viewed-owner-allocates-nothing -->
A store past the length of a viewed owner throws without detaching: the owner still shares its buffer with
the view, which the owner's capacity (the buffer's own, not a private copy's) shows.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	let arr = IntArray from [11, 22, 33]
	let sub = try arr.slice(0, endIndex: 2) otherwise panic("slice: 0..2 of a length-3 array")
	let before = arr.capacity()
	var refused = false

	try arr.set(3, value: 5) otherwise 'outOfRange'
		refused = true
	end 'outOfRange'

	let seen = try sub.get(1) otherwise panic("get: index 1 of a length-2 view")

	if refused and arr.capacity() == before and seen == 22 'untouched'
		return 0
	end 'untouched'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: a-store-over-an-element-that-owes-a-destructor-releases-it -->
A `String` element owes a drop, so its store takes the runtime's arm, which releases the element it
overwrites. A leak is exit 101.
```maxon
typealias NameArray = Array with String

function main() returns ExitCode
	let names = NameArray from ["alpha", "beta", "gamma"]
	try names.set(0, value: "one") otherwise panic("set: index 0 of a length-3 array")
	try names.set(1, value: "two") otherwise panic("set: index 1 of a length-3 array")
	try names.set(0, value: "three") otherwise panic("set: index 0 of a length-3 array")

	let first = try names.get(0) otherwise panic("get: index 0 of a length-3 array")
	let second = try names.get(1) otherwise panic("get: index 1 of a length-3 array")

	if first == "three" and second == "two" 'replaced'
		return 0
	end 'replaced'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: a-store-over-a-viewed-element-that-owes-a-destructor-releases-it -->
The same, with the owner viewed: the runtime detaches and releases the occupant of the private copy.
```maxon
typealias NameArray = Array with String

function main() returns ExitCode
	let names = NameArray from ["alpha", "beta", "gamma"]
	let sub = try names.slice(0, endIndex: 2) otherwise panic("slice: 0..2 of a length-3 array")
	try names.set(0, value: "one") otherwise panic("set: index 0 of a length-3 array")

	let viewed = try sub.get(0) otherwise panic("get: index 0 of a length-2 view")
	let owner = try names.get(0) otherwise panic("get: index 0 of a length-3 array")

	if viewed == "alpha" and owner == "one" 'detached'
		return 0
	end 'detached'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: consecutive-stores-with-computed-words-land-in-order -->
Eight words, each computed from the others and stored in turn: every store lands with the value computed
for it, and the sum of the stored words pins the order.
```maxon
typealias Word = bits(64)
typealias Planes = FixedArray with 8 Word

function mix(planes Planes)
	let a = planes.get(0)
	let b = planes.get(1)
	let c = planes.get(2)
	let d = planes.get(3)
	let e = a xor b
	let f = c xor d
	let g = e xor f
	let h = g xor a

	planes.set(0, value: e xor h)
	planes.set(1, value: f xor g)
	planes.set(2, value: g xor h)
	planes.set(3, value: h xor f)
	planes.set(4, value: a xor d)
	planes.set(5, value: b xor c)
	planes.set(6, value: e xor f)
	planes.set(7, value: g xor a)
end 'mix'

function main() returns ExitCode
	let planes = Planes.create()
	planes.set(0, value: 1)
	planes.set(1, value: 2)
	planes.set(2, value: 4)
	planes.set(3, value: 8)
	mix(planes)

	if planes.get(0) == 13 and planes.get(1) == 3 and planes.get(2) == 1 and planes.get(3) == 2 and planes.get(4) == 9 and planes.get(5) == 6 and planes.get(6) == 15 and planes.get(7) == 14 'ordered'
		return 0
	end 'ordered'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: a-staged-store-into-an-empty-array-throws-out-of-range -->
<!-- tier: stdlib -->
`managed.set` bounds its index by the capacity, after the detach decision. An array that never grew has
capacity 0 and no buffer, so the store is refused as out of range, as the runtime entry refuses it.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	let arr = IntArray.create(0)
	var refused = false

	try arr.managed.set(0, value: 5) otherwise 'outOfRange'
		refused = true
	end 'outOfRange'

	if refused and arr.count() == 0 and arr.capacity() == 0 'untouched'
		return 0
	end 'untouched'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: a-staged-store-into-a-view-of-an-empty-array-throws-out-of-range -->
<!-- tier: stdlib -->
A view of an empty array is not its buffer's owner and has no buffer of its own to write. Its store is
refused as out of range, and the parent stays empty.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	let arr = IntArray.create(0)
	let sub = try arr.slice(0, endIndex: 0) otherwise panic("slice: 0..0 of an empty array")
	var refused = false

	try sub.managed.set(0, value: 5) otherwise 'outOfRange'
		refused = true
	end 'outOfRange'

	if refused and arr.count() == 0 and sub.count() == 0 'untouched'
		return 0
	end 'untouched'

	return 1
end 'main'
```
```exitcode
0
```
