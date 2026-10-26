---
feature: fixed-array-static-index
status: experimental
keywords: [fixed array, bounds check, constant index, proven index, codegen]
category: codegen
---

# FixedArray element access at a proven index

## Documentation

A `FixedArray` carries its SIZE in its type (`FixedArray with 3 Int` is a different type from
`FixedArray with 4 Int`, see [fixed array](fixed-array.md)), so whether an index lies inside
`0 to N-1` is a question the compiler can answer at the call site. `get` and `set` on a receiver whose
static type is a concrete `FixedArray with N` have three outcomes, decided by what the compiler knows
about the index:

- **A proven index makes the access total.** It cannot fail, so it throws nothing, the call is written
  bare, and a `try` on it is refused (E3055): there is no out-of-bounds error to catch.
- **A constant index at or past the size is refused** at compile time (E3005), naming the indices
  `int(0 to N-1)` of `FixedArray with N`. A negative constant is outside `ElementIndex` itself and is
  refused as an argument (E3005).
- **Any other index throws.** An index the compiler cannot place inside `0 to N-1` is checked at run
  time, and an out-of-range one throws `ArrayError.indexOutOfBounds`, so the call needs `try` (E3057
  without it, with the hint to give the index a ranged type within `int(0 to N-1)`).

### What counts as a proven index

- A known constant whose value is in `0 to N-1`: an integer literal, a `let` bound to a literal, or a
  folded constant expression (`1 + 1`, `countof(Vec3) - 1`).
- A value whose declared ranged type `int(a to b)` has `0 <= a` and `b <= N-1` — a parameter or a `let`
  of that type, usually `typealias Slot = int(0 to 3) implements ElementIndex` for a `FixedArray with 4`.
- The counter of a counted `for` loop between two literal bounds whose range lies in `0 to N-1`
  (`for i in 0 upto 4` over a `FixedArray with 4`), and a `let` computed from such a counter whose
  computed range does (`let k = s + 2` for `s` in `0 upto 2`).

Not proven: an `ElementIndex` or `Count` parameter, a `var` stepped by a `while` loop, a counter whose
range reaches past `N-1` or whose bound is not a literal, a call result, a `var` reassigned in a branch, a ranged type wider than `0 to N-1`, and any index inside a generic body over `Self` (an
`extension FixedArray`, where the count is a runtime parameter).

### Why only a `FixedArray` has one

An `Array`'s length is a runtime field, not part of its type, so `arr.get(0)` on an empty array is a
genuine failure the compiler cannot rule out. The proof keys on the size the INSTANCE carries, which
only a sized container has — and `an-array-at-a-constant-index-keeps-its-check` is the control.

## Tests

<!-- test: a-constant-index-inside-the-fixed-array-round-trips -->
A write and a read at a constant index inside the fixed array round-trip, both written bare.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	v.set(2, value: 33)
	return v.get(2)
end 'main'
```
```exitcode
33
```

<!-- test: a-variable-index-inside-the-fixed-array-round-trips -->
The CONTROL for the case above: the same fixed array, the same element, reached through a `var` a
`while` loop steps. That `var` is not proven, so both accesses keep their `try`, and a loop-carried
index reads the slot the loop wrote.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	var i = 0 as Int
	while i < 4 'fill'
		try v.set(i as ElementIndex, value: i * 11) otherwise panic("test invariant: set OOB")
		i = i + 1
	end 'fill'
	var total = 0 as Int
	var r = 2 as Int
	while r < 3 'read'
		total = total + (try v.get(r as ElementIndex) otherwise 0)
		r = r + 1
	end 'read'
	return total
end 'main'
```
```exitcode
22
```

<!-- test: a-constant-index-write-round-trips -->
The `set` half of the pair above: a bare write at a constant index, read back at the same one.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	v.set(0, value: 7)
	return v.get(0)
end 'main'
```
```exitcode
7
```

<!-- test: a-variable-index-write-round-trips -->
The CONTROL for the case above, writing through a loop variable and reading its writes back through a
`for … in`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	var i = 0 as Int
	while i < 3 'fill'
		try v.set(i as ElementIndex, value: 4) otherwise panic("test invariant: set OOB")
		i = i + 1
	end 'fill'
	var total = 0 as Int
	for elem in v 'sum'
		total = total + elem
	end 'sum'
	return total
end 'main'
```
```exitcode
12
```

<!-- test: a-counted-loop-counter-inside-the-size-is-proven -->
A counted loop between literal bounds that stay inside the fixed array proves its counter, so both the
writes in the loop and the read after it are bare.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	for i in 0 upto 4 'fill'
		v.set(i, value: (i as Int) * 11)
	end 'fill'
	var total = 0 as Int
	for i in 1 to 2 'read'
		total = total + v.get(i)
	end 'read'
	return total
end 'main'
```
```exitcode
33
```

<!-- test: a-counter-cast-to-an-index-alias-needs-no-try -->
A counter whose interval reaches past the size is narrowed by a cast to an alias inside it: the cast's
guard holds the value to `Slot`, so the access is proven by the narrower of the two facts.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int
typealias Slot = int(0 to 3) implements ElementIndex

function main() returns ExitCode
	var v = Vec4.create()
	var total = 0 as Int
	for i in 0 upto 10 'each'
		if i < 4 'inside'
			v.set(i as Slot, value: (i * 10) as Int)
			total = total + v.get(i as Slot)
		end 'inside'
	end 'each'
	return total as ExitCode
end 'main'
```
```exitcode
60
```

<!-- test: a-counter-bounded-by-the-count-of-the-type-is-proven -->
`countof(Vec4)` folds to the constant 4, so it bounds a counter exactly as the literal does, and a loop
over every slot needs no `try` and no second spelling of the size.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	for i in 0 upto countof(Vec4) 'fill'
		v.set(i, value: 5)
	end 'fill'
	var total = 0 as Int
	for i in 0 upto countof(Vec4) 'sum'
		total = total + v.get(i)
	end 'sum'
	return total
end 'main'
```
```exitcode
20
```

<!-- test: a-let-computed-from-a-counter-is-proven -->
A `let` computed from a proven counter carries the computed range: `s + 2` for `s` in `0 upto 2` lies in
`2 to 3`, inside a `FixedArray with 4`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	v.set(2, value: 5)
	v.set(3, value: 6)
	var total = 0 as Int
	for s in 0 upto 2 'pairs'
		let k = s + 2
		total = total + v.get(k)
	end 'pairs'
	return total
end 'main'
```
```exitcode
11
```

<!-- test: error.a-counter-reaching-past-the-size-requires-try -->
`0 to 4` reaches index 4 of a `FixedArray with 4`, so its counter is not proven and a bare access is
refused.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	var total = 0 as Int
	for i in 0 to 4 'sum'
		total = total + v.get(i)
	end 'sum'
	return total
end 'main'
```
```maxoncstderr
error E3057: <fragment>:9:21: throwing function requires try: 'get' — its index is not proven inside the fixed array's 4 elements; wrap it as `try … otherwise …`, or give the index a ranged type within `int(0 to 3)`
```

<!-- test: a-counter-reaching-past-the-size-keeps-its-try -->
The same loop with its `try`: the last iteration takes the `otherwise` arm.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	var total = 0 as Int
	for i in 0 to 4 'sum'
		total = total + (try v.get(i) otherwise 100)
	end 'sum'
	return total
end 'main'
```
```exitcode
100
```

<!-- test: error.a-counter-with-a-runtime-bound-requires-try -->
A counter whose bound is not a literal has no known range, so a bare access is refused.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function sum(v Vec4, n Count) returns Int
	var total = 0 as Int
	for i in 0 upto n 'sum'
		total = total + v.get(i)
	end 'sum'
	return total
end 'sum'

function main() returns ExitCode
	var v = Vec4.create()
	return sum(v, n: 2)
end 'main'
```
```maxoncstderr
error E3057: <fragment>:8:21: throwing function requires try: 'get' — its index is not proven inside the fixed array's 4 elements; wrap it as `try … otherwise …`, or give the index a ranged type within `int(0 to 3)`
```

<!-- test: a-let-bound-literal-is-a-constant-index -->
A `let` bound to a literal is a known constant, so it proves the index exactly as a bare literal does.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	let slot = 3
	v.set(slot, value: 19)
	return v.get(slot)
end 'main'
```
```exitcode
19
```

<!-- test: a-folded-expression-is-a-constant-index -->
`1 + 1` and `4 - 2` fold to a known constant inside the fixed array, so both accesses are proven.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	v.set(1 + 1, value: 23)
	return v.get(4 - 2)
end 'main'
```
```exitcode
23
```

<!-- test: the-last-slot-is-in-range -->
The boundary the proof must get right: `size - 1` is inside, so the access is proven and reads the
element that was written there.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	v.set(3, value: 44)
	return v.get(3)
end 'main'
```
```exitcode
44
```

<!-- test: an-in-range-constant-set-needs-no-try -->
A `set` at a constant inside the fixed array is total too. The value is read back through an index
that is not proven, so the `get` keeps its `try` and only the `set` is under test.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function runtimeIndex(i ElementIndex) returns ElementIndex
	return i
end 'runtimeIndex'

function main() returns ExitCode
	var v = Vec4.create()
	v.set(0, value: 7)
	return try v.get(runtimeIndex(0)) otherwise 0
end 'main'
```
```exitcode
7
```

<!-- test: an-index-whose-range-fits-the-size-needs-no-try -->
An index that is not a constant is proven by its declared range: `Slot` is `int(0 to 3)`, inside a
`FixedArray with 4`, so the `get` on the parameter is total.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int
typealias Slot = int(0 to 3) implements ElementIndex

function read(v Vec4, at Slot) returns Int
	return v.get(at)
end 'read'

function main() returns ExitCode
	var v = Vec4.create()
	v.set(3, value: 44)
	return read(v, at: 3)
end 'main'
```
```exitcode
44
```

<!-- test: a-ranged-index-wider-than-the-size-keeps-its-try -->
The CONTROL for the case above: `Wide` is `int(0 to 4)`, which reaches one past a `FixedArray with 4`,
so the index is not proven, the access keeps its `try`, and the out-of-range value throws at run time.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int
typealias Wide = int(0 to 4) implements ElementIndex

function read(v Vec4, at Wide) returns Int
	return try v.get(at) otherwise -1
end 'read'

function main() returns ExitCode
	var v = Vec4.create()
	let result = read(v, at: 4)
	print("{result}\n")
	return 0
end 'main'
```
```stdout
-1
```

<!-- test: error.try-on-a-proven-fixed-array-get -->
A proven `get` cannot fail, so a `try` on it has nothing to catch and is refused.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	let a = try v.get(2) otherwise 0
	return a
end 'main'
```
```maxoncstderr
error E3055: <fragment>:7:10: try requires a throwing function: this `get` cannot fail — its index is already proven inside the fixed array's 4 elements, so there is no out-of-bounds error to catch
```

<!-- test: error.try-on-a-proven-fixed-array-set -->
The same refusal for a proven `set`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	try v.set(1, value: 5) otherwise ignore
	return v.get(1)
end 'main'
```
```maxoncstderr
error E3055: <fragment>:7:2: try requires a throwing function: this `set` cannot fail — its index is already proven inside the fixed array's 4 elements, so there is no out-of-bounds error to catch
```

<!-- test: error.an-unproven-fixed-array-index-requires-try -->
An `ElementIndex` parameter is not proven, so the access can throw and a bare call is refused; the
message names the ranged type that would prove it.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function read(v Vec4, at ElementIndex) returns Int
	return v.get(at)
end 'read'

function main() returns ExitCode
	var v = Vec4.create()
	return read(v, at: 2)
end 'main'
```
```maxoncstderr
error E3057: <fragment>:6:11: throwing function requires try: 'get' — its index is not proven inside the fixed array's 4 elements; wrap it as `try … otherwise …`, or give the index a ranged type within `int(0 to 3)`
```

<!-- test: error.a-constant-index-at-the-size-is-refused -->
The other side of the last slot's boundary: `size` itself is out of range, and a constant there is
refused at compile time.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	let result = try v.get(4) otherwise -1
	print("{result}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:21: Value 4 is outside the range of the indices of 'FixedArray with 4' (int(0 to 3))
```

<!-- test: a-runtime-index-at-the-size-throws -->
The same index known only at run time is checked there: it throws, and the `otherwise` arm runs.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function runtimeIndex(i ElementIndex) returns ElementIndex
	return i
end 'runtimeIndex'

function main() returns ExitCode
	var v = Vec4.create()
	let result = try v.get(runtimeIndex(4)) otherwise -1
	print("{result}\n")
	return 0
end 'main'
```
```stdout
-1
```

<!-- test: error.a-constant-index-well-past-the-size-is-refused -->
A constant FAR outside is refused by the same rule as the one at the boundary.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	let result = try v.get(9) otherwise -2
	print("{result}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:21: Value 9 is outside the range of the indices of 'FixedArray with 4' (int(0 to 3))
```

<!-- test: a-runtime-index-well-past-the-size-throws -->
The same index known only at run time throws as the one at the boundary does.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function runtimeIndex(i ElementIndex) returns ElementIndex
	return i
end 'runtimeIndex'

function main() returns ExitCode
	var v = Vec4.create()
	let result = try v.get(runtimeIndex(9)) otherwise -2
	print("{result}\n")
	return 0
end 'main'
```
```stdout
-2
```

<!-- test: error.a-negative-constant-index-is-refused -->
A negative constant is outside `ElementIndex` itself, so it is refused as an argument before the
fixed array's size is consulted.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	let result = try v.get(-1) otherwise -2
	print("{result}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:21: Value -1 is outside the range of 'ElementIndex' (int(0 to 18446744073709551615))
```

<!-- test: error.an-out-of-range-constant-set-is-refused -->
`set` decides the same way `get` does: a constant past the size is refused at compile time.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = FixedArray with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	try v.set(9, value: 99) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:8: Value 9 is outside the range of the indices of 'FixedArray with 4' (int(0 to 3))
```

<!-- test: an-out-of-range-runtime-set-throws -->
A write at an index known only at run time throws when it is out of range, and the rejected write
leaves the fixed array untouched.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec2 = FixedArray with 2 Int

function runtimeIndex(i ElementIndex) returns ElementIndex
	return i
end 'runtimeIndex'

function main() returns ExitCode
	var v = Vec2.create()
	v.set(0, value: 5)
	try v.set(runtimeIndex(9), value: 99) otherwise 'oob'
		return v.get(0) + 60
	end 'oob'
	return 0
end 'main'
```
```exitcode
65
```

<!-- test: an-array-at-a-constant-index-keeps-its-check -->
An `Array`'s length is a runtime field, not part of its type, so a constant index proves
nothing about it. The access keeps its `try`, the emitted call is the checked `__managed_get`, and
the empty array's `get(0)` throws.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var xs = IntArray.create()
	let result = try xs.get(0) otherwise -3
	print("{result}\n")
	return 0
end 'main'
```
```stdout
-3
```

<!-- test: an-index-that-is-a-parameter-keeps-the-check -->
An index that is a parameter of a type wider than the fixed array is not proven however the caller
spelled it — the call below passes a literal — so the access keeps its `try`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function slotOf(v Vec3, at Int) returns Int
	return try v.get(at as ElementIndex) otherwise -1
end 'slotOf'

function main() returns ExitCode
	var v = Vec3.create()
	v.set(2, value: 21)
	return slotOf(v, at: 2)
end 'main'
```
```exitcode
21
```

<!-- test: an-index-that-is-a-call-result-keeps-the-check -->
A call result is not proven either, and the callee returning a literal does not make it one.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function whichSlot() returns Int
	return 1
end 'whichSlot'

function main() returns ExitCode
	var v = Vec3.create()
	try v.set(whichSlot() as ElementIndex, value: 18) otherwise panic("test invariant: set OOB")
	return (try v.get(whichSlot() as ElementIndex) otherwise 0) as ExitCode
end 'main'
```
```exitcode
18
```

<!-- test: a-fixed-array-parameter-at-a-constant-index -->
The size rides the TYPE, so a fixed array arriving as a parameter — whose record the callee
never built — is proven exactly as a local one is.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = FixedArray with 3 Int

function middle(v Vec3) returns Int
	return v.get(1)
end 'middle'

function main() returns ExitCode
	var v = Vec3.create()
	v.set(1, value: 26)
	return middle(v)
end 'main'
```
```exitcode
26
```

<!-- test: every-slot-of-a-fixed-array-at-constant-indexes -->
Each slot addressed by its own constant, written and read back, so a wrong stride would
show as a wrong sum rather than as a crash.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec5 = FixedArray with 5 Int

function main() returns ExitCode
	var v = Vec5.create()
	v.set(0, value: 1)
	v.set(1, value: 2)
	v.set(2, value: 4)
	v.set(3, value: 8)
	v.set(4, value: 16)
	let a = v.get(0)
	let b = v.get(1)
	let c = v.get(2)
	let d = v.get(3)
	let e = v.get(4)
	return a + b + c + d + e
end 'main'
```
```exitcode
31
```

<!-- test: a-byte-fixed-array-at-constant-indexes -->
A one-byte stride: the total access shares its element load with the checked one, so the narrow
element must read back narrow.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteVec4 = FixedArray with 4 Byte

function main() returns ExitCode
	var v = ByteVec4.create()
	v.set(0, value: 200)
	v.set(3, value: 55)
	let a = v.get(0)
	let b = v.get(3)
	return a - b
end 'main'
```
```exitcode
145
```

<!-- test: a-bit-packed-bool-fixed-array-at-constant-indexes -->
A `FixedArray with N bool` is bit-packed (8 elements to a byte), which has no slot ADDRESS at
all — the shared element load is what makes the packed case correct, and the constants
here cross a byte boundary.
```maxon
typealias Bits16 = FixedArray with 16 bool

function main() returns ExitCode
	var v = Bits16.create()
	v.set(0, value: true)
	v.set(7, value: true)
	v.set(8, value: true)
	v.set(15, value: true)
	let b0 = v.get(0)
	let b7 = v.get(7)
	let b8 = v.get(8)
	let b15 = v.get(15)
	let b1 = v.get(1)
	var count = 0 as FullRangeInt
	if b0 'zero'
		count = count + 1
	end 'zero'
	if b7 'seven'
		count = count + 2
	end 'seven'
	if b8 'eight'
		count = count + 4
	end 'eight'
	if b15 'fifteen'
		count = count + 8
	end 'fifteen'
	if b1 'one'
		count = count + 16
	end 'one'
	return count
end 'main'
```
```exitcode
15
```

<!-- test: a-float-fixed-array-at-constant-indexes -->
A float element comes back as its bits and is reinterpreted at the call site, which is the
one thing the accessor does OUTSIDE the runtime entry — so the total access must leave it
intact.
```maxon
typealias Float = float(f64.min to f64.max)
typealias Vec2F = FixedArray with 2 Float

function main() returns ExitCode
	var v = Vec2F.create()
	v.set(0, value: 2.5)
	v.set(1, value: 4.25)
	let a = v.get(0)
	let b = v.get(1)
	return trunc((a + b) * 4.0)
end 'main'
```
```exitcode
27
```
