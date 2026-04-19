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
typealias Byte = byte(0 to u8.max)
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
typealias Byte = byte(0 to u8.max)
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

	return i0 + i1 * 10 + i2 * 100
end 'main'
```
```exitcode
210
```

<!-- test: cursor-peek -->
Peek ahead without moving the cursor.
```maxon
typealias Byte = byte(0 to u8.max)
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
typealias Byte = byte(0 to u8.max)
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
typealias Byte = byte(0 to u8.max)
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
typealias Byte = byte(0 to u8.max)
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
typealias Byte = byte(0 to u8.max)
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
typealias Byte = byte(0 to u8.max)
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
typealias Byte = byte(0 to u8.max)
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
typealias Byte = byte(0 to u8.max)
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
typealias Byte = byte(0 to u8.max)
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
typealias Byte = byte(0 to u8.max)
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
typealias Byte = byte(0 to u8.max)
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

	return count
end 'main'
```
```exitcode
1101
```
