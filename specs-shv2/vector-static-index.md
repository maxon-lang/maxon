---
feature: vector-static-index
status: experimental
keywords: [vector, bounds check, constant index, codegen, optimization]
category: codegen
---

# Vector element access at a compile-time constant index

## Documentation

A `Vector` carries its SIZE in its type (`Vector with 3 Int` is a different type from
`Vector with 4 Int`, see [vector](vector.md)), so when the index is a compile-time
constant the compiler can decide `0 <= index < size` itself. It does, and the emitted
access then carries NO bounds check: `get` and `set` take a second runtime entry
(`__managed_get_in_bounds` / `__managed_set_in_bounds`) whose body is the checked entry's minus
the `index < 0 or index >= length` test and its out-of-range return.

Nothing about the SOURCE changes. `Array.get`/`Array.set` are declared throwing by
`stdlib/Array.maxon`, a `Vector` publishes that same surface, and the `try … otherwise`
the author writes stays exactly as it was — what the proof discharges is the ONE failure
that clause admits for a vector, not the clause.

### What counts as a constant index

The compiler's own constant view — the same one that decides a shift count and folds
`emitBinOp`'s operands. A literal, a `let` bound to one, and an expression folded from
them all qualify; a loop variable, a parameter and a call result do not, and those keep
the check.

### An index outside the vector

An out-of-range CONSTANT index is decided the same way, and the answer is that the access
can only fail — so it keeps the checked entry and throws `ArrayError.indexOutOfBounds` at
run time, exactly as [vector](vector.md)'s `get-out-of-bounds` requires. The elision is an
optimization over a proof of SUCCESS; it never removes a check whose verdict is failure.

### Why only a `Vector`

An `Array`'s length is a runtime field, not part of its type, so `arr.get(0)` on an empty
array is a genuine failure the compiler cannot rule out. The elision keys on the size the
INSTANCE carries, which only a sized container has.

## Tests

<!-- test: constant-index-get-has-no-bounds-check -->
`__managed_get_in_bounds` is `__managed_get` without the `index < 0 or index >= length` test and
without the `oob` block that returns `ArrayError.indexOutOfBounds` — the element load, the
element-destructor gate and the ok-return are shared with it verbatim.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = Vector with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	try v.set(2, value: 33) otherwise panic("test invariant: set OOB")
	return try v.get(2) otherwise 0
end 'main'
```
```exitcode
33
```

<!-- test: variable-index-get-keeps-the-bounds-check -->
The CONTROL for the case above: the same vector, the same element, an index the compiler
cannot see. `__managed_get`'s body still carries the negative test, the at-or-over-length test
and the `oob` return.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = Vector with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	var i = 0
	while i < 4 'fill'
		try v.set(i, value: i * 11) otherwise panic("test invariant: set OOB")
		i = i + 1
	end 'fill'
	var total = 0
	var r = 2
	while r < 3 'read'
		total = total + (try v.get(r) otherwise 0)
		r = r + 1
	end 'read'
	return total
end 'main'
```
```exitcode
22
```

<!-- test: constant-index-set-has-no-bounds-check -->
`__managed_set_in_bounds` is `__managed_set` without the bounds test, without the `oob` block and
without the rejected-element destroy that block owes — a write that cannot be rejected has
no rejected element. The COW detach and the store are shared with `__managed_set` verbatim.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	try v.set(0, value: 7) otherwise panic("test invariant: set OOB")
	return try v.get(0) otherwise 0
end 'main'
```
```exitcode
7
```
```RequiredRuntime
__managed_set_in_bounds
```

<!-- test: variable-index-set-keeps-the-bounds-check -->
The CONTROL for the case above.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function main() returns ExitCode
	var v = Vec3.create()
	var i = 0
	while i < 3 'fill'
		try v.set(i, value: 4) otherwise panic("test invariant: set OOB")
		i = i + 1
	end 'fill'
	var total = 0
	for elem in v 'sum'
		total = total + elem
	end 'sum'
	return total
end 'main'
```
```exitcode
12
```
```RequiredRuntime
__managed_set
```

<!-- test: a-let-bound-literal-is-a-constant-index -->
The constant view sees a value named through a `let`, so this elides exactly as a bare
literal does.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = Vector with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	let slot = 3
	try v.set(slot, value: 19) otherwise panic("test invariant: set OOB")
	return try v.get(slot) otherwise 0
end 'main'
```
```exitcode
19
```

<!-- test: a-folded-expression-is-a-constant-index -->
`1 + 1` is folded before the access is emitted, so the index is known and in range.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = Vector with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	try v.set(1 + 1, value: 23) otherwise panic("test invariant: set OOB")
	return try v.get(4 - 2) otherwise 0
end 'main'
```
```exitcode
23
```

<!-- test: the-last-slot-is-in-range -->
The boundary the in-range test must get right: `size - 1` elides, and reads the element
that was written there.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = Vector with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	try v.set(3, value: 44) otherwise panic("test invariant: set OOB")
	return try v.get(3) otherwise 0
end 'main'
```
```exitcode
44
```

<!-- test: a-constant-index-at-the-size-still-throws -->
The other side of that boundary: `size` itself is out of range, so the access keeps its
check and the `otherwise` arm runs.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = Vector with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	let result = try v.get(4) otherwise -1
	print("{result}\n")
	return 0
end 'main'
```
```stdout
-1
```

<!-- test: a-negative-constant-index-still-throws -->
A negative constant is out of range on the other end, and the check that rejects it is the
one this optimization must never remove.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec4 = Vector with 4 Int

function main() returns ExitCode
	var v = Vec4.create()
	let result = try v.get(-1) otherwise -2
	print("{result}\n")
	return 0
end 'main'
```
```stdout
-2
```

<!-- test: an-out-of-range-constant-set-still-throws -->
`set` decides the same way `get` does, and a rejected write leaves the vector untouched.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec2 = Vector with 2 Int

function main() returns ExitCode
	var v = Vec2.create()
	try v.set(0, value: 5) otherwise panic("test invariant: set OOB")
	try v.set(9, value: 99) otherwise 'oob'
		return (try v.get(0) otherwise 0) + 60
	end 'oob'
	return 0
end 'main'
```
```exitcode
65
```

<!-- test: an-array-at-a-constant-index-keeps-its-check -->
An `Array`'s length is a runtime field, not part of its type, so a constant index proves
nothing about it. The emitted call is the checked `__managed_get`, and the empty array's
`get(0)` throws.
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
A parameter's value is the one thing the compiler provably cannot see, so an index that IS a parameter
never elides however the caller spelled it — the call below passes a literal.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function slotOf(v Vec3, at Int) returns Int
	return try v.get(at) otherwise -1
end 'slotOf'

function main() returns ExitCode
	var v = Vec3.create()
	try v.set(2, value: 21) otherwise panic("test invariant: set OOB")
	return slotOf(v, at: 2)
end 'main'
```
```exitcode
21
```

<!-- test: an-index-that-is-a-call-result-keeps-the-check -->
A call result is not a constant either, and the callee returning a literal does not make it one.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function whichSlot() returns Int
	return 1
end 'whichSlot'

function main() returns ExitCode
	var v = Vec3.create()
	try v.set(whichSlot(), value: 18) otherwise panic("test invariant: set OOB")
	return try v.get(whichSlot()) otherwise 0
end 'main'
```
```exitcode
18
```

<!-- test: a-vector-parameter-at-a-constant-index -->
The size rides the TYPE, so a vector arriving as a parameter — whose record the callee
never built — is decided exactly as a local one is.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Int

function middle(v Vec3) returns Int
	return try v.get(1) otherwise 0
end 'middle'

function main() returns ExitCode
	var v = Vec3.create()
	try v.set(1, value: 26) otherwise panic("test invariant: set OOB")
	return middle(v)
end 'main'
```
```exitcode
26
```

<!-- test: every-slot-of-a-vector-at-constant-indexes -->
Each slot addressed by its own constant, written and read back, so a wrong stride would
show as a wrong sum rather than as a crash.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Vec5 = Vector with 5 Int

function main() returns ExitCode
	var v = Vec5.create()
	try v.set(0, value: 1) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 2) otherwise panic("test invariant: set OOB")
	try v.set(2, value: 4) otherwise panic("test invariant: set OOB")
	try v.set(3, value: 8) otherwise panic("test invariant: set OOB")
	try v.set(4, value: 16) otherwise panic("test invariant: set OOB")
	let a = try v.get(0) otherwise 0
	let b = try v.get(1) otherwise 0
	let c = try v.get(2) otherwise 0
	let d = try v.get(3) otherwise 0
	let e = try v.get(4) otherwise 0
	return a + b + c + d + e
end 'main'
```
```exitcode
31
```

<!-- test: a-byte-vector-at-constant-indexes -->
A one-byte stride: the unchecked entry shares its element load with the checked one, so
the narrow element must read back narrow.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteVec4 = Vector with 4 Byte

function main() returns ExitCode
	var v = ByteVec4.create()
	try v.set(0, value: 200) otherwise panic("test invariant: set OOB")
	try v.set(3, value: 55) otherwise panic("test invariant: set OOB")
	let a = try v.get(0) otherwise 0
	let b = try v.get(3) otherwise 0
	return a - b
end 'main'
```
```exitcode
145
```

<!-- test: a-bit-packed-bool-vector-at-constant-indexes -->
A `Vector with N bool` is bit-packed (8 elements to a byte), which has no slot ADDRESS at
all — the shared element load is what makes the packed case correct, and the constants
here cross a byte boundary.
```maxon
typealias Bits16 = Vector with 16 bool

function main() returns ExitCode
	var v = Bits16.create()
	try v.set(0, value: true) otherwise panic("test invariant: set OOB")
	try v.set(7, value: true) otherwise panic("test invariant: set OOB")
	try v.set(8, value: true) otherwise panic("test invariant: set OOB")
	try v.set(15, value: true) otherwise panic("test invariant: set OOB")
	let b0 = try v.get(0) otherwise false
	let b7 = try v.get(7) otherwise false
	let b8 = try v.get(8) otherwise false
	let b15 = try v.get(15) otherwise false
	let b1 = try v.get(1) otherwise true
	var count = 0
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

<!-- test: a-float-vector-at-constant-indexes -->
A float element comes back as its bits and is reinterpreted at the call site, which is the
one thing the accessor does OUTSIDE the runtime entry — so the elided entry must leave it
intact.
```maxon
typealias Float = float(f64.min to f64.max)
typealias Vec2F = Vector with 2 Float

function main() returns ExitCode
	var v = Vec2F.create()
	try v.set(0, value: 2.5) otherwise panic("test invariant: set OOB")
	try v.set(1, value: 4.25) otherwise panic("test invariant: set OOB")
	let a = try v.get(0) otherwise 0.0
	let b = try v.get(1) otherwise 0.0
	return trunc((a + b) * 4.0)
end 'main'
```
```exitcode
27
```
