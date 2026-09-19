---
feature: array-cursor
status: experimental
keywords: [array, cursor, managed-memory-cursor, bytearray]
category: collections
---

# Array Cursor

## Documentation

`Array.cursor()` creates an `ArrayCursor` that provides efficient, bounds-check-free access to array elements. The cursor is always at a valid position — navigation methods (`advance`, `retreat`, `advanceBy`, `retreatBy`, `seek`) throw `IterationError` on invalid moves, and `current()` reads the element at the current position without any bounds check. `advanceBy` and `retreatBy` come from the `Iterator` and `BidirectionalIterator` extensions and default to calling `advance`/`retreat` n times.

## Tests

<!-- test: cursor-basic-traversal -->
Create a cursor and traverse all elements using advance/current.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)

	let cursor = try arr.cursor() otherwise 'fail'
		return 99
	end 'fail'

	var sum = cursor.current()
	try cursor.advance() otherwise ignore
	sum = sum + cursor.current()
	try cursor.advance() otherwise ignore
	sum = sum + cursor.current()

	return sum
end 'main'
```
```exitcode
60
```

<!-- test: cursor-index -->
Verify that index() returns the current position.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)

	let cursor = try arr.cursor() otherwise 'fail'
		return 99
	end 'fail'

	let i0 = cursor.index()
	try cursor.advance() otherwise ignore
	let i1 = cursor.index()
	try cursor.advance() otherwise ignore
	let i2 = cursor.index()

	// 0 + 1*10 + 2*100 = 210 (printed, not returned: 210 > the valid
	// ExitCode range of 0..125 on the wasm target)
	print("{i0 + i1 * 10 + i2 * 100}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
210
```

<!-- test: cursor-peek -->
Peek ahead without moving the cursor.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)

	let cursor = try arr.cursor() otherwise 'fail'
		return 99
	end 'fail'

	let cur = cursor.current()
	let p1 = try cursor.peek(1) otherwise 0
	let p2 = try cursor.peek(2) otherwise 0

	return cur + p1 + p2
end 'main'
```
```exitcode
60
```

<!-- test: cursor-retreat -->
Advance then retreat and verify position.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)

	let cursor = try arr.cursor() otherwise 'fail'
		return 99
	end 'fail'

	try cursor.advance() otherwise ignore
	let afterAdv = cursor.current()
	try cursor.retreat() otherwise ignore
	let afterRet = cursor.current()

	return afterAdv + afterRet
end 'main'
```
```exitcode
30
```

<!-- test: cursor-advance-by -->
Skip multiple positions with advanceBy(n).
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	arr.push(40)
	arr.push(50)

	let cursor = try arr.cursor() otherwise 'fail'
		return 99
	end 'fail'

	try cursor.advanceBy(3) otherwise ignore

	return cursor.current()
end 'main'
```
```exitcode
40
```

<!-- test: cursor-seek -->
Seek jumps to an arbitrary valid position.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	arr.push(40)

	let cursor = try arr.cursor() otherwise 'fail'
		return 99
	end 'fail'

	try cursor.seek(2) otherwise ignore
	return cursor.current()
end 'main'
```
```exitcode
30
```

<!-- test: cursor-seek-out-of-bounds -->
Seek to an out-of-bounds index throws and leaves position unchanged.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)

	let cursor = try arr.cursor() otherwise 'fail'
		return 99
	end 'fail'

	try cursor.seek(5) otherwise 'caught'
		// position should still be 0
		return cursor.current()
	end 'caught'
	return 77
end 'main'
```
```exitcode
10
```

<!-- test: cursor-advance-throws-at-end -->
Verify advance throws IterationError.exhausted at end.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)

	let cursor = try arr.cursor() otherwise 'fail'
		return 99
	end 'fail'

	// Advance to last element
	try cursor.advance() otherwise 'done'
		return 88
	end 'done'

	// Try to advance past end — should throw
	try cursor.advance() otherwise 'caught'
		return 1
	end 'caught'

	// Should not reach here
	return 77
end 'main'
```
```exitcode
1
```

<!-- test: cursor-retreat-throws-at-start -->
Verify retreat throws IterationError.atStart at position 0.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)

	let cursor = try arr.cursor() otherwise 'fail'
		return 99
	end 'fail'

	// Try to retreat at position 0 — should throw
	try cursor.retreat() otherwise 'caught'
		return 1
	end 'caught'

	// Should not reach here
	return 77
end 'main'
```
```exitcode
1
```

<!-- test: cursor-retreat-by -->
Rewind the cursor multiple positions with retreatBy.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	arr.push(40)
	arr.push(50)

	let cursor = try arr.cursor() otherwise 'fail'
		return 99
	end 'fail'

	try cursor.advanceBy(4) otherwise ignore
	try cursor.retreatBy(2) otherwise ignore

	return cursor.current()
end 'main'
```
```exitcode
30
```

<!-- test: cursor-retreat-by-throws-at-start -->
Verify retreatBy throws IterationError.atStart when asked to move past position 0.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)

	let cursor = try arr.cursor() otherwise 'fail'
		return 99
	end 'fail'

	try cursor.advance() otherwise ignore

	// Retreating by 2 from position 1 should throw — only 1 step is possible.
	try cursor.retreatBy(2) otherwise 'caught'
		return 1
	end 'caught'

	return 77
end 'main'
```
```exitcode
1
```

<!-- test: cursor-empty-array-throws -->
Verify cursor() throws IterationError.exhausted on empty array.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let arr = ByteArray.create()

	try arr.cursor() otherwise 'caught'
		return 1
	end 'caught'

	return 77
end 'main'
```
```exitcode
1
```

<!-- test: cursor-peek-throws-out-of-bounds -->
Verify peek throws when looking past the end.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)

	let cursor = try arr.cursor() otherwise 'fail'
		return 99
	end 'fail'

	// peek(1) should work (element at index 1 exists)
	let p1 = try cursor.peek(1) otherwise 0

	// peek(2) should throw (only 2 elements, index 2 is out of bounds)
	try cursor.peek(2) otherwise 'caught'
		return p1
	end 'caught'

	return 77
end 'main'
```
```exitcode
20
```

<!-- test: cursor-bool-array -->
Cursor over a bit-packed `Array with bool` must extract individual bits rather than loading whole bytes. Using a pattern that differs at every bit position catches the bug where `current()` and `peek()` read a byte instead of a single bit.
```maxon
typealias BoolArray = Array with bool

function main() returns ExitCode
	var arr = BoolArray.create()
	arr.push(true)
	arr.push(false)
	arr.push(true)
	arr.push(true)
	arr.push(false)

	let cursor = try arr.cursor() otherwise 'fail'
		return 99
	end 'fail'

	var count = 0
	if cursor.current() 'c0'
		count = count + 1
	end 'c0'
	let p1 = try cursor.peek(1) otherwise true
	if p1 'c1'
		count = count + 10
	end 'c1'
	let p2 = try cursor.peek(2) otherwise false
	if p2 'c2'
		count = count + 100
	end 'c2'
	let p3 = try cursor.peek(3) otherwise false
	if p3 'c3'
		count = count + 1000
	end 'c3'
	let p4 = try cursor.peek(4) otherwise true
	if p4 'c4'
		count = count + 10000
	end 'c4'

	// true,false,true,true,false → 1 + 100 + 1000 = 1101 (printed, not
	// returned: 1101 > the valid ExitCode range of 0..125 on the wasm target)
	print("{count}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1101
```

<!-- test: cursor-peek-packed-byte-elements -->
Peek over a byte-string-backed array, whose buffer is genuinely byte-packed (element size 1, unlike a pushed `Array with Byte`, which stores 8-byte slots). A fixed-width qword load in peek's lowering read 7 bytes of adjacent buffer state past the element, so the peeked values carried garbage high bytes — the self-hosted compiler's own lexer tripped over this when `peek(1)` for the `0x` hex prefix returned a huge value that matched neither `x` nor `0`, splitting every hex literal into `0` + identifier.
```maxon
function main() returns ExitCode
	let bytes = b"0xFF"
	var it = try bytes.createIterator() otherwise 'fail'
		return 99
	end 'fail'

	let cur = it.current()
	let p1 = try it.peek(1) otherwise 255
	let p2 = try it.peek(2) otherwise 255
	let p3 = try it.peek(3) otherwise 255
	let oob = try it.peek(4) otherwise 255

	print("{cur} {p1} {p2} {p3} {oob}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
48 120 70 70 255
```

### A step COUNT is unsigned, so a negative step is a MISTAKE and not a direction

`IterStep` is `int(0 to u64.max)`. Direction is carried by WHICH METHOD is called — `advanceBy` goes
forward, `retreatBy` goes back — so the argument is left with one job, *how far*, and a count of how
far is never negative.

⛔ The alternative was a signed step whose sign chose the direction, and it cannot be had at this
price: `advanceBy` is published by `extension Iterator`, which has only `advance()`, so honouring a
negative there needs a backward move every iterator has — and the only way to give it one is a default
`retreat()` on that same extension. An extension method SATISFIES an interface requirement, so that
default silently discharges `BidirectionalIterator.retreat()`: a type that declares the conformance and
forgets the method stops earning **E3016** and compiles. ⇒ **the unsigned step is what lets `retreat()`
stay a requirement nobody can forget.**

A written negative is therefore refused where it is WRITTEN, by the ordinary range rule every
`int(0 to …)` alias carries, rather than interpreted as a backwards step or — worse — counted by a
`while i < n` loop that is false on its first test and returns having moved nothing.

<!-- test: error.cursor-advance-by-a-written-negative-step-is-refused -->
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)

	let cursor = try arr.cursor() otherwise 'fail'
		return 99
	end 'fail'

	try cursor.advanceBy(-2) otherwise 'moved'
		return 98
	end 'moved'

	return cursor.current()
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/array-cursor/error.cursor-advance-by-a-written-negative-step-is-refused.test:15:13: Value -2 is outside the range of 'IterStep' (int(0 to 18446744073709551615))
```

### And the mirror, so neither door is the one that was remembered

`retreatBy` carries the same `IterStep` and owes the same refusal. Stating both is what makes this a
property of the ALIAS rather than of one call site: a step count is unsigned wherever it is spelled.

<!-- test: error.cursor-retreat-by-a-written-negative-step-is-refused -->
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)

	let cursor = try arr.cursor() otherwise 'fail'
		return 99
	end 'fail'

	try cursor.retreatBy(-2) otherwise 'moved'
		return 98
	end 'moved'

	return cursor.current()
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/array-cursor/error.cursor-retreat-by-a-written-negative-step-is-refused.test:15:13: Value -2 is outside the range of 'IterStep' (int(0 to 18446744073709551615))
```

### The control: refusing the negative step did not break the positive one

⚠ Two refusals pass just as well against methods that move nothing at all, so the set needs a case that
reads a POSITION back. Four forward then three back from element 0 lands on element 1, and the answer is
`20` only if `advanceBy` counted four forward moves and `retreatBy` counted three backward ones.

<!-- test: cursor-advance-by-and-retreat-by-still-move-in-their-own-directions -->
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	arr.push(40)
	arr.push(50)

	let cursor = try arr.cursor() otherwise 'fail'
		return 99
	end 'fail'

	try cursor.advanceBy(4) otherwise 'forward'
		return 98
	end 'forward'

	try cursor.retreatBy(3) otherwise 'backward'
		return 97
	end 'backward'

	return cursor.current()
end 'main'
```
```exitcode
20
```
