---
feature: arrays
status: stable
keywords: [arrays, array literal, get, set, sized array]
category: types
---
# Arrays

## Documentation

Arrays are ordered collections of elements of the same type.

## Mutable Array Literals

Create a mutable array using `var` with square brackets:

```text
var numbers = [10, 20, 30]
numbers.set(0, value: 100)  // Can modify elements
```

## Immutable Array Literals

Create an immutable array using `let` with square brackets:

```text
let constants = [10, 20, 30]
var x = try constants.get(1) otherwise 0  // Can read elements
```

## Preallocated Arrays

Create an array with preallocated capacity and length using `.resize()`:

```text
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var buffer = IntArray.create()
buffer.resize(10)   // Length is now 10, elements are zero-initialized
buffer.set(0, value: 42)
```

Use `.reserve()` to allocate capacity without changing length (for performance when appending):

```text
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var buffer = IntArray.create()
buffer.reserve(100)  // Capacity is 100, length is still 0
buffer.push(42)      // Now length is 1
```

## Element Access

Access array elements using the `.get()` method with a zero-based index.
The method returns an optional value (throws on out of bounds), so use `try ... otherwise default`:

```text
var arr = [10, 20, 30]
var first = try arr.get(0) otherwise 0   // 10
var second = try arr.get(1) otherwise 0  // 20
var third = try arr.get(2) otherwise 0   // 30
```

## Element Assignment

Modify mutable array elements using the `.set()` method:

```text
var arr = [10, 20, 30]
arr.set(0, value: 100)
arr.set(1, value: 200)
```

## Tests

<!-- test: literal-first -->
```maxon
function main() returns ExitCode
	return try [10, 20, 30].get(0) otherwise 0
end 'main'
```
```exitcode
10
```

<!-- test: literal-middle -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
20
```

<!-- test: literal-last -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	return try arr.get(2) otherwise 0
end 'main'
```
```exitcode
30
```

<!-- test: five-elements -->
```maxon
function main() returns ExitCode
	let arr = [5, 10, 15, 20, 25]
	return try arr.get(4) otherwise 0
end 'main'
```
```exitcode
25
```

<!-- test: index-assignment -->
```maxon
function main() returns ExitCode
	var arr = [10, 20, 30]
	try arr.set(0, value: 100) otherwise panic("test invariant: set OOB")
	return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
100
```

<!-- test: assignment-middle -->
```maxon
function main() returns ExitCode
	var arr = [1, 2, 3]
	try arr.set(1, value: 42) otherwise panic("test invariant: set OOB")
	return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: assignment-last -->
```maxon
function main() returns ExitCode
	var arr = [1, 2, 3, 4, 5]
	try arr.set(4, value: 99) otherwise panic("test invariant: set OOB")
	return try arr.get(4) otherwise 0
end 'main'
```
```exitcode
99
```

<!-- test: multiple-access -->
```maxon
function main() returns ExitCode
	let arr = [5, 10, 15, 20, 25]
	let a = try arr.get(2) otherwise 0
	let b = try arr.get(4) otherwise 0
	return a + b
end 'main'
```
```exitcode
40
```

<!-- test: assignment-preserves-others -->
```maxon
function main() returns ExitCode
	var arr = [10, 20, 30]
	try arr.set(0, value: 100) otherwise panic("test invariant: set OOB")
	return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
20
```

<!-- test: multiple-assignments -->
```maxon
function main() returns ExitCode
	var arr = [0, 0, 0]
	try arr.set(0, value: 1) otherwise panic("test invariant: set OOB")
	try arr.set(1, value: 2) otherwise panic("test invariant: set OOB")
	try arr.set(2, value: 3) otherwise panic("test invariant: set OOB")
	let a = try arr.get(0) otherwise 0
	let b = try arr.get(1) otherwise 0
	let c = try arr.get(2) otherwise 0
	return a + b + c
end 'main'
```
```exitcode
6
```

<!-- test: let-array-first -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
10
```

<!-- test: let-array-middle -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
20
```

<!-- test: let-array-last -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	return try arr.get(2) otherwise 0
end 'main'
```
```exitcode
30
```

<!-- test: let-array-multiple-access -->
```maxon
function main() returns ExitCode
	let arr = [5, 10, 15, 20]
	let a = try arr.get(0) otherwise 0
	let b = try arr.get(3) otherwise 0
	return a + b
end 'main'
```
```exitcode
25
```

<!-- test: array-with-reserve -->
Test that arrays can be created with `.reserve()` for preallocated capacity.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.reserve(5)
	arr.push(42)
	return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
42```

<!-- test: array-with-resize -->
Test that arrays can be created with `.resize()` for preallocated length.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
		var arr = IntArray.create()
		arr.resize(5)
		try arr.set(0, value: 99) otherwise panic("test invariant: set OOB")
		return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
99
```

<!-- test: array-growth-realloc -->
Test that arrays grow correctly when pushing many elements (triggers multiple reallocs).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
		var arr = IntArray.create()
		var i = 0
		while i < 100 'loop'
				arr.push(i)
				i = i + 1
		end 'loop'
		return try arr.get(99) otherwise -1
end 'main'
```
```exitcode
99
```

### Byte Array Push and Get

<!-- test: byte-array-push-get -->
```maxon

typealias Byte = int(0 to u8.max)

typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10 as Byte)
	arr.push(20 as Byte)
	arr.push(30 as Byte)

	let v0 = try arr.get(0) otherwise 0 as Byte
	let v1 = try arr.get(1) otherwise 0 as Byte
	let v2 = try arr.get(2) otherwise 0 as Byte

	return v0 + v1 + v2
end 'main'
```
```exitcode
60
```

### Byte Array Initialized

<!-- test: byte-array-initialized -->
```maxon

typealias Byte = int(0 to u8.max)

typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(1 as Byte)
	arr.push(2 as Byte)
	arr.push(3 as Byte)

	let v0 = try arr.get(0) otherwise 0 as Byte
	let v1 = try arr.get(1) otherwise 0 as Byte
	let v2 = try arr.get(2) otherwise 0 as Byte

	return v0 + v1 + v2
end 'main'
```
```exitcode
6
```

### Byte Array Set

<!-- test: byte-array-set -->
```maxon

typealias Byte = int(0 to u8.max)

typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10 as Byte)
	arr.push(20 as Byte)
	arr.push(30 as Byte)

	try arr.set(1, value: 99 as Byte) otherwise panic("test invariant: set OOB")

	let val = try arr.get(1) otherwise 0 as Byte
	return val
end 'main'
```
```exitcode
99
```

### Byte Array Max Values

<!-- test: byte-array-max-values -->
```maxon

typealias Byte = int(0 to u8.max)

typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(255 as Byte)
	arr.push(0 as Byte)
	arr.push(128 as Byte)

	let v0 = try arr.get(0) otherwise 0 as Byte
	let v1 = try arr.get(1) otherwise 99 as Byte
	let v2 = try arr.get(2) otherwise 0 as Byte

	if v0 != 255 'c0'
		return 1
	end 'c0'
	if v1 != 0 'c1'
		return 2
	end 'c1'
	if v2 != 128 'c2'
		return 3
	end 'c2'

	return 0
end 'main'
```
```exitcode
0
```

### Byte Array Count

<!-- test: byte-array-count -->
```maxon

typealias Byte = int(0 to u8.max)

typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(1 as Byte)
	arr.push(2 as Byte)
	arr.push(3 as Byte)
	arr.push(4 as Byte)
	arr.push(5 as Byte)

	return arr.count()
end 'main'
```
```exitcode
5
```

<!-- test: array-literal-constant -->
```maxon
let numbers = [1, 2, 3, 4, 5]

function main() returns ExitCode
	var sum = 0
	for n in numbers 'loop'
		sum = sum + n
	end 'loop'
	return sum
end 'main'
```
```exitcode
15
```

<!-- test: array-literal-with-dependency -->
```maxon
let FIRST = 10
let SECOND = 20
let values = [FIRST, SECOND, 30]

function main() returns ExitCode
	let v0 = try values.get(0) otherwise 0
	let v1 = try values.get(1) otherwise 0
	let v2 = try values.get(2) otherwise 0
	return v0 + v1 + v2
end 'main'
```
```exitcode
60
```

<!-- test: error.unused-array-typealias -->
<!-- P1.9 unused-typealias detection (E3062 not yet emitted for Array typealias) -->
A `typealias X = Array with Y` declaration must be referenced **explicitly**
by name (`X.create()`, `let v X = ...`, etc.) — being implicitly inferable
from a bare `[...]` array literal does not count as a use. This avoids silent
"used implicitly" semantics that masked real unused-typealias mistakes.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

let numbers = [1, 2, 3, 4, 5]

function main() returns ExitCode
	var sum = 0
	for n in numbers 'loop'
		sum = sum + n
	end 'loop'
	return sum
end 'main'
```
```maxoncstderr
error E3062: specs/fragments/arrays/error.unused-array-typealias.test:3:11: unused typealias: 'IntArray'
```

### String Array Literals

<!-- test: string-array-literal-basic -->
```maxon
function main() returns ExitCode
	let arr = ["hello", "world"]
	let s = try arr.get(0) otherwise ""
	if s == "hello" 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: string-array-literal-iterate -->
```maxon
function main() returns ExitCode
	let arr = ["aaa", "bbb", "ccc"]
	var count = 0
	for _ in arr 'loop'
		count = count + 1
	end 'loop'
	return count
end 'main'
```
```exitcode
3
```

<!-- test: string-array-literal-top-level -->
```maxon
var names = ["alice", "bob"]

function main() returns ExitCode
	var count = 0
	for _ in names 'loop'
		count = count + 1
	end 'loop'
	return count
end 'main'
```
```exitcode
2
```

<!-- test: string-array-literal-top-level-pass-to-function -->
```maxon
var items = ["hello"]

function useString(s String) returns ExitCode
	if s == "hello" 'check'
		return 42
	end 'check'
	return 0
end 'useString'

function main() returns ExitCode
	for item in items 'loop'
		return useString(item)
	end 'loop'
	return 1
end 'main'
```
```exitcode
42
```

### Slice

<!-- test: slice-basic -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30, 40, 50]
	let sub = try arr.slice(1, endIndex: 4) otherwise [1]
	let a = try sub.get(0) otherwise 0
	let b = try sub.get(1) otherwise 0
	let c = try sub.get(2) otherwise 0
	return a + b + c
end 'main'
```
```exitcode
90
```

<!-- test: slice-from-start -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30, 40, 50]
	let sub = try arr.slice(0, endIndex: 3) otherwise [1]
	return sub.count()
end 'main'
```
```exitcode
3
```

<!-- test: slice-to-end -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30, 40, 50]
	let sub = try arr.slice(3, endIndex: 5) otherwise [1]
	let a = try sub.get(0) otherwise 0
	let b = try sub.get(1) otherwise 0
	return a + b
end 'main'
```
```exitcode
90
```

<!-- test: slice-empty -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	let sub = try arr.slice(1, endIndex: 1) otherwise [1]
	return sub.count()
end 'main'
```
```exitcode
0
```

<!-- test: slice-full -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	let sub = try arr.slice(0, endIndex: 3) otherwise [1]
	let a = try sub.get(0) otherwise 0
	let b = try sub.get(1) otherwise 0
	let c = try sub.get(2) otherwise 0
	return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: slice-throws-invalid-end -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	let sub = try arr.slice(0, endIndex: 10) otherwise return 42
	return sub.count()
end 'main'
```
```exitcode
42
```

<!-- test: slice-throws-inverted-range -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	let sub = try arr.slice(2, endIndex: 1) otherwise return 42
	return sub.count()
end 'main'
```
```exitcode
42
```

<!-- test: slice-throws-invalid-start -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	let sub = try arr.slice(5, endIndex: 5) otherwise return 42
	return sub.count()
end 'main'
```
```exitcode
42
```

### Negative index

A negative index passes an at-or-over-length bounds test (`StdCmpPred` is signed, so `-1 >= 3` is FALSE) and
would address BEFORE the buffer — an OOB heap read/write. The throwing accessors (`get`/`set`/`remove`/`slice`)
reject it; `insert`, which clamps rather than throws, clamps it to the front.

<!-- test: get-negative-index-throws -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	let val = try arr.get(-1) otherwise 99
	return val
end 'main'
```
```exitcode
99
```

<!-- test: set-negative-index-throws -->
```maxon
function main() returns ExitCode
	var arr = [10, 20, 30]
	try arr.set(-1, value: 5) otherwise return 99
	return 0
end 'main'
```
```exitcode
99
```

<!-- test: remove-negative-index-throws -->
```maxon
function main() returns ExitCode
	var arr = [10, 20, 30]
	let removed = try arr.remove(-1) otherwise return 99
	return removed
end 'main'
```
```exitcode
99
```

<!-- test: slice-negative-start-throws -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	let sub = try arr.slice(-1, endIndex: 2) otherwise return 99
	return sub.count()
end 'main'
```
```exitcode
99
```

<!-- test: slice-negative-end-throws -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	let sub = try arr.slice(-2, endIndex: -1) otherwise return 99
	return sub.count()
end 'main'
```
```exitcode
99
```

<!-- test: insert-negative-index-clamps-to-front -->
```maxon
function main() returns ExitCode
	var arr = [10, 20, 30]
	arr.insert(-1, value: 99)
	let a = try arr.get(0) otherwise 0
	let b = try arr.get(1) otherwise 0
	let c = try arr.get(2) otherwise 0
	let d = try arr.get(3) otherwise 0
	return a + b + c + d
end 'main'
```
```exitcode
159
```

### Append

<!-- test: append-basic -->
```maxon
function main() returns ExitCode
	var a = [1, 2, 3]
	let b = [4, 5, 6]
	a.append(b)
	var sum = 0
	var i = 0
	while i < a.count() 'loop'
		sum = sum + (try a.get(i) otherwise 0)
		i = i + 1
	end 'loop'
	return sum
end 'main'
```
```exitcode
21
```

<!-- test: append-empty-to-nonempty -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var a = [1, 2, 3]
	var b = IntArray.create()
	a.append(b)
	return a.count()
end 'main'
```
```exitcode
3
```

<!-- test: append-nonempty-to-empty -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var a = IntArray.create()
	let b = [10, 20]
	a.append(b)
	let first = try a.get(0) otherwise 0
	let second = try a.get(1) otherwise 0
	return first + second
end 'main'
```
```exitcode
30
```

<!-- test: append-preserves-originals -->
```maxon
function main() returns ExitCode
	var a = [1, 2]
	let b = [3, 4]
	a.append(b)
	// b should still have its original elements
	let b0 = try b.get(0) otherwise 0
	let b1 = try b.get(1) otherwise 0
	return b0 + b1
end 'main'
```
```exitcode
7
```

### Copy-on-Write

<!-- test: slice-cow-modify-slice -->
Modifying a slice must not affect the original array.
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30, 40, 50]
	var sub = try arr.slice(1, endIndex: 4) otherwise return 1
	try sub.set(0, value: 99) otherwise panic("test invariant: set OOB")
	// Original should be unchanged
	let original = try arr.get(1) otherwise 0
	let modified = try sub.get(0) otherwise 0
	if original == 20 'check'
		if modified == 99 'check2'
			return 0
		end 'check2'
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slice-cow-modify-original -->
Modifying the original array must not affect an existing slice.
```maxon
function main() returns ExitCode
	var arr = [10, 20, 30, 40, 50]
	let sub = try arr.slice(1, endIndex: 4) otherwise return 1
	try arr.set(1, value: 99) otherwise panic("test invariant: set OOB")
	// Slice should be unchanged
	let sliceVal = try sub.get(0) otherwise 0
	let origVal = try arr.get(1) otherwise 0
	if sliceVal == 20 'check'
		if origVal == 99 'check2'
			return 0
		end 'check2'
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### Zero-copy slices

`.slice()` and `.clone()` are **O(1)**: the result shares the source's element buffer until either side
writes, at which point that side copies out. The sharing is invisible — every case below is valued against
the reference compiler, which eagerly copies and therefore answers what a correct copy-on-write must too.

<!-- test: slice-capacity-is-a-view-until-written -->
A slice reports a negative capacity — its buffer is not its own — until a write gives it a private one
sized to its live elements. This is the one place the sharing is directly observable, and it is what says
no buffer was allocated: an eager copy would report a grown positive capacity from the moment it was made.
Observable does not mean divergent: the reference compiler answers `-1` here too (verified), because it
also flags a not-yet-owned buffer rather than reporting a capacity it does not have.
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30, 40, 50]
	var sub = try arr.slice(1, endIndex: 4) otherwise return 1
	let notMine = 0 - 1
	let asView = sub.capacity()
	try sub.set(0, value: 99) otherwise panic("index 0 is in bounds")
	let afterWrite = sub.capacity()
	if asView == notMine and afterWrite == 3 'ok'
		return 0
	end 'ok'
	return 2
end 'main'
```
```exitcode
0
```

<!-- test: slice-outlives-its-parent -->
The parent array dies while the slice is still being read. A view holds a reference to the shared buffer,
not to the parent record, so the bytes outlive the array they were cut from.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function makeParent() returns IntArray
	var a = IntArray.create()
	a.push(10)
	a.push(20)
	a.push(30)
	a.push(40)
	return a
end 'makeParent'

function tail(v IntArray) returns IntArray
	return try v.slice(1, endIndex: 4) otherwise IntArray.create()
end 'tail'

function main() returns ExitCode
	let sub = tail(makeParent())
	let a = try sub.get(0) otherwise 0
	let b = try sub.get(1) otherwise 0
	let c = try sub.get(2) otherwise 0
	return a + b + c
end 'main'
```
```exitcode
90
```

<!-- test: slice-bounds-are-its-own-window -->
A slice is bounded by its OWN length, not the parent's, even though the parent's remaining elements sit
immediately after it in the same buffer. Reading past the window throws rather than reading a neighbour.
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30, 40, 50]
	let sub = try arr.slice(1, endIndex: 4) otherwise return 1
	let past = try sub.get(3) otherwise 77
	return past
end 'main'
```
```exitcode
77
```

<!-- test: slice-of-slice-flattens -->
Slicing a slice windows within the window, and writing to the inner one leaves the outer one alone.
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30, 40, 50, 60]
	let s1 = try arr.slice(1, endIndex: 5) otherwise return 1
	var s2 = try s1.slice(1, endIndex: 3) otherwise return 2
	let a = try s2.get(0) otherwise 0
	let b = try s2.get(1) otherwise 0
	try s2.set(0, value: 7) otherwise panic("index 0 is in bounds")
	let after = try s1.get(1) otherwise 0
	if a == 30 and b == 40 and after == 30 and s2.count() == 2 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slice-of-a-dead-slice-still-reads -->
Flattening, pinned by killing the record in the middle. The inner slice is cut from a slice that dies
inside the callee, and the array both were cut from dies with it — yet the inner one still reads. This is
what says `parent@32` holds the shared ALLOCATION and not the parent RECORD: a chain would have the inner
view depending on a record that is already gone.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function build() returns IntArray
	var a = IntArray.create()
	var i = 0
	while i < 10 'fill'
		a.push(i * 10)
		i = i + 1
	end 'fill'
	return a
end 'build'

function innerOf(a IntArray) returns IntArray
	let mid = try a.slice(2, endIndex: 8) otherwise IntArray.create()
	return try mid.slice(1, endIndex: 4) otherwise IntArray.create()
end 'innerOf'

function main() returns ExitCode
	let inner = innerOf(build())
	let a = try inner.get(0) otherwise 0
	let b = try inner.get(1) otherwise 0
	let c = try inner.get(2) otherwise 0
	return (a + b + c) / 10
end 'main'
```
```exitcode
12
```

<!-- test: many-views-detach-one-by-one -->
One buffer held by an owner, two slices and a slice-of-a-slice, then all four write in turn. The shared
count has to walk all the way down and reclaim exactly once — every arm of the detach (owner-with-viewers,
viewer, viewer-of-a-viewer) runs, and each side keeps the bytes it had when it copied out.
```maxon
function main() returns ExitCode
	var arr = [1, 2, 3, 4, 5, 6]
	var a = try arr.slice(0, endIndex: 3) otherwise return 1
	var b = try arr.slice(3, endIndex: 6) otherwise return 2
	var c = try a.slice(0, endIndex: 2) otherwise return 3
	try arr.set(0, value: 100) otherwise panic("index 0 is in bounds")
	try b.set(0, value: 200) otherwise panic("index 0 is in bounds")
	try c.set(0, value: 300) otherwise panic("index 0 is in bounds")
	try a.set(1, value: 400) otherwise panic("index 1 is in bounds")
	let ok = (try arr.get(0) otherwise 0) == 100 and (try arr.get(3) otherwise 0) == 4 and (try b.get(0) otherwise 0) == 200 and (try b.get(1) otherwise 0) == 5 and (try c.get(0) otherwise 0) == 300 and (try c.get(1) otherwise 0) == 2 and (try a.get(0) otherwise 0) == 1 and (try a.get(1) otherwise 0) == 400
	return 0 if ok else 9
end 'main'
```
```exitcode
0
```

<!-- test: two-slices-then-write-parent -->
Two live slices of one array, then writes at both ends of the parent. The parent copies out once; both
slices keep reading the original bytes.
```maxon
function main() returns ExitCode
	var arr = [10, 20, 30, 40]
	let v1 = try arr.slice(0, endIndex: 2) otherwise return 1
	let v2 = try arr.slice(2, endIndex: 4) otherwise return 2
	try arr.set(0, value: 99) otherwise panic("index 0 is in bounds")
	try arr.set(3, value: 88) otherwise panic("index 3 is in bounds")
	let a = try v1.get(0) otherwise 0
	let b = try v2.get(1) otherwise 0
	let c = try arr.get(0) otherwise 0
	let d = try arr.get(3) otherwise 0
	if a == 10 and b == 40 and c == 99 and d == 88 'ok'
		return 0
	end 'ok'
	return 3
end 'main'
```
```exitcode
0
```

<!-- test: slice-parent-grows-while-view-lives -->
Pushing onto the parent reallocates its buffer. The slice keeps the old one.
```maxon
function main() returns ExitCode
	var arr = [10, 20, 30]
	let sub = try arr.slice(0, endIndex: 3) otherwise return 1
	arr.push(40)
	arr.push(50)
	let s0 = try sub.get(0) otherwise 0
	let s2 = try sub.get(2) otherwise 0
	let a3 = try arr.get(3) otherwise 0
	if s0 == 10 and s2 == 30 and sub.count() == 3 and a3 == 40 and arr.count() == 5 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: append-a-slice-of-self -->
`append` reads its source AFTER growing its destination, so appending a slice of an array to that same
array is coherent: the destination copies out first and the slice still names the original bytes.
```maxon
function main() returns ExitCode
	var arr = [1, 2, 3]
	let sub = try arr.slice(0, endIndex: 2) otherwise return 1
	arr.append(sub)
	var sum = 0
	var i = 0
	while i < arr.count() 'loop'
		sum = sum + (try arr.get(i) otherwise 0)
		i = i + 1
	end 'loop'
	let s0 = try sub.get(0) otherwise 0
	if sum == 9 and arr.count() == 5 and s0 == 1 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slice-of-empty-array-then-push -->
An empty slice of an array that never allocated a buffer owes nothing to anyone, and still grows normally.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	let empty = IntArray.create()
	var e = try empty.slice(0, endIndex: 0) otherwise return 1
	e.push(5)
	let v = try e.get(0) otherwise 0
	if v == 5 and e.count() == 1 and empty.count() == 0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: slice-of-byte-string-literal -->
A slice of a `b"…"` literal views the immortal `.rdata` blob — nothing is owed and nothing is freed — and
writing to it copies the window out first.
```maxon
function main() returns ExitCode
	let lit = b"hello"
	var mid = try lit.slice(1, endIndex: 4) otherwise return 1
	try mid.set(0, value: 88) otherwise panic("index 0 is in bounds")
	let m0 = try mid.get(0) otherwise 0
	let m1 = try mid.get(1) otherwise 0
	let l1 = try lit.get(1) otherwise 0
	if m0 == 88 and m1 == 108 and l1 == 101 and mid.count() == 3 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: both-sides-detach-parent-first -->
Both sides write. The parent copies out first and gives up the shared buffer, leaving the slice as its
sole owner; the slice then copies out and reclaims it. This case reaches the path where a detach's release
frees the bytes it just copied — but it cannot *catch* a detach that released BEFORE copying, because
`__mm_free` reclaims nothing under the bump allocator, so freed bytes still read back intact (measured:
reversing that order leaves the suite at 1197/0). The ordering is a rule `__arr_cow_detach` keeps on its
own; it becomes testable the day the allocator reuses memory.
```maxon
function main() returns ExitCode
	var arr = [10, 20, 30, 40, 50]
	var sub = try arr.slice(1, endIndex: 4) otherwise return 1
	try arr.set(1, value: 91) otherwise panic("index 1 is in bounds")
	try sub.set(0, value: 92) otherwise panic("index 0 is in bounds")
	let s0 = try sub.get(0) otherwise 0
	let s1 = try sub.get(1) otherwise 0
	let a1 = try arr.get(1) otherwise 0
	if s0 == 92 and s1 == 30 and a1 == 91 'ok'
		return 0
	end 'ok'
	return 2
end 'main'
```
```exitcode
0
```

<!-- test: both-sides-detach-slice-first -->
The same pair in the other order.
```maxon
function main() returns ExitCode
	var arr = [10, 20, 30, 40, 50]
	var sub = try arr.slice(1, endIndex: 4) otherwise return 1
	try sub.set(0, value: 92) otherwise panic("index 0 is in bounds")
	try arr.set(1, value: 91) otherwise panic("index 1 is in bounds")
	let s0 = try sub.get(0) otherwise 0
	let a1 = try arr.get(1) otherwise 0
	let a2 = try arr.get(2) otherwise 0
	if s0 == 92 and a1 == 91 and a2 == 30 'ok'
		return 0
	end 'ok'
	return 2
end 'main'
```
```exitcode
0
```

<!-- test: every-mutator-detaches-a-view -->
Every in-place mutator that is not already covered by `set` above, applied to a slice of one shared array:
`clear`, `pop`, `remove`, `insert`, `resize`, `reserve`, `push`. Each must copy out before it writes, so
none of those seven writes may reach the source — the gate is that the source reads back untouched.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function make() returns IntArray
	var a = IntArray.create()
	var i = 0
	while i < 8 'fill'
		a.push(i)
		i = i + 1
	end 'fill'
	return a
end 'make'

function main() returns ExitCode
	let src = make()

	var vClear = try src.slice(1, endIndex: 5) otherwise return 1
	vClear.clear()

	var vPop = try src.slice(1, endIndex: 5) otherwise return 2
	let popped = try vPop.pop() otherwise 0 - 1

	var vRemove = try src.slice(2, endIndex: 6) otherwise return 3
	let removed = try vRemove.remove(0) otherwise 0 - 1

	var vInsert = try src.slice(0, endIndex: 3) otherwise return 4
	vInsert.insert(1, value: 99)

	var vResize = try src.slice(0, endIndex: 4) otherwise return 5
	vResize.resize(2)

	var vGrow = try src.slice(0, endIndex: 2) otherwise return 6
	vGrow.reserve(64)
	vGrow.push(1000)

	// Not one of those writes may have reached the shared source.
	var k = 0
	while k < 8 'check'
		if (try src.get(k) otherwise 0 - 1) != k 'mismatch'
			return 7
		end 'mismatch'
		k = k + 1
	end 'check'

	let ok = vClear.count() == 0 and popped == 4 and vPop.count() == 3 and removed == 2 and vRemove.count() == 3 and (try vInsert.get(1) otherwise 0) == 99 and vInsert.count() == 4 and vResize.count() == 2 and vGrow.count() == 3 and (try vGrow.get(2) otherwise 0) == 1000
	return 0 if ok else 8
end 'main'
```
```exitcode
0
```

<!-- test: clone-of-byte-string-literal -->
```maxon
function main() returns ExitCode
	let lit = b"abc"
	var copy = lit.clone()
	try copy.set(0, value: 90) otherwise panic("index 0 is in bounds")
	let c0 = try copy.get(0) otherwise 0
	let l0 = try lit.get(0) otherwise 0
	if c0 == 90 and l0 == 97 and copy.count() == 3 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: array-literal-return-from-function -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function makeNumbers() returns IntArray
	var arr = IntArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	return arr
end 'makeNumbers'

function main() returns ExitCode
	let nums = makeNumbers()
	let a = try nums.get(0) otherwise 0
	let b = try nums.get(1) otherwise 0
	let c = try nums.get(2) otherwise 0
	return a + b + c
end 'main'
```
```exitcode
60
```

<!-- test: array-literal-return-push-no-leak -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function makeNumbers(a Integer, b Integer) returns IntArray
	var arr = IntArray.create()
	arr.push(a)
	arr.push(b)
	arr.push(a + b)
	return arr
end 'makeNumbers'

function main() returns ExitCode
	let nums = makeNumbers(10, b: 20)
	let c = try nums.get(2) otherwise 0
	return c
end 'main'
```
```exitcode
30
```

<!-- test: array-literal-struct-return-from-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

typealias PointArray = Array with Point

function makePoints() returns PointArray
	let p1 = Point.create(1, y: 2)
	let p2 = Point.create(3, y: 4)
	return [p1, p2]
end 'makePoints'

function main() returns ExitCode
	let pts = makePoints()
	let p0 = try pts.get(0) otherwise Point.create(0, y: 0)
	let p1 = try pts.get(1) otherwise Point.create(0, y: 0)
	return p0.x + p1.y
end 'main'
```
```exitcode
5
```

### Multi-line array literals

A `[…]` literal may be written across several lines — after the `[`, after an element, and after a
separating `,`. A newline in those positions is LAYOUT, not the end of the expression, exactly as it is
inside a `{…}` struct literal. shv2-authored: the corpus writes multi-line literals only in cases blocked
on other slices (`array-realloc-dangling-ref`'s E3070 borrow-liveness pass, `map`'s P1.8 map literal), so
it has no runnable case for the rule itself. The expected values are the bootstrap oracle's, which
compiles every program below.

<!-- test: multi-line-literal-break-after-comma -->
```maxon
function main() returns ExitCode
	let a = [10,
		20]
	let v = try a.get(1) otherwise 0
	return v
end 'main'
```
```exitcode
20
```

<!-- test: multi-line-literal-one-element-per-line -->
```maxon
function main() returns ExitCode
	let a = [
		10,
		20,
		30
	]
	let v = try a.get(2) otherwise 0
	return a.count() + v
end 'main'
```
```exitcode
33
```

<!-- test: multi-line-literal-trailing-newline-before-bracket -->
```maxon
function main() returns ExitCode
	let a = [10, 20, 30
	]
	return a.count()
end 'main'
```
```exitcode
3
```

<!-- test: multi-line-string-literal-elements -->
```maxon
function main() returns ExitCode
	let words = ["alpha",
		"beta",
		"gamma"]
	let w = try words.get(1) otherwise ""
	if w == "beta" 'middle'
		return words.count() as ExitCode
	end 'middle'
	return 99
end 'main'
```
```exitcode
3
```

<!-- test: multi-line-literal-top-level-var -->
```maxon
var xs = [10,
	20]

function main() returns ExitCode
	let v = try xs.get(1) otherwise 0
	return v
end 'main'
```
```exitcode
20
```

<!-- test: multi-line-literal-empty-still-rejected -->
A `[` and `]` with only a newline between them is still the EMPTY literal, and still carries the empty
literal's own advice — the newline skip must not turn it into "expected expression".
```maxon
function main() returns ExitCode
	let a = [
	]
	return a.count()
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/arrays/multi-line-literal-empty-still-rejected.test:3:10: Unsupported: an empty array literal `[]` — its element type cannot be inferred; use `Array with T` + `.create()` for an empty typed array
```

### An `as`-cast element fixes the literal's element type

The literal's element type comes from what the first element IS, not from the token it starts with. An
`as`-cast to a ranged alias starts with an int literal and is not an `int`, and reading the token alone
reported "mixed element types" against a literal with ONE element. shv2-authored: the corpus's own case
(`short-circuit-evaluation`'s `guard-protects-right-side`) covers the multi-element spelling, and these
pin the one-element and formatting-independence halves it cannot.

<!-- test: single-cast-element-literal -->
```maxon
typealias Index = int(0 to u64.max)

function main() returns ExitCode
	let single = [10 as Index]
	let v = try single.get(0) otherwise 0
	return v
end 'main'
```
```exitcode
10
```

<!-- test: cast-element-literal-matches-instance-alias -->
```maxon
typealias Index = int(0 to u64.max)
typealias IndexArray = Array with Index

function total(xs IndexArray) returns ExitCode
	let a = try xs.get(0) otherwise 0
	let b = try xs.get(1) otherwise 0
	return a + b
end 'total'

function main() returns ExitCode
	return total([10 as Index,
		20 as Index])
end 'main'
```
```exitcode
30
```

<!-- test: int-literal-expression-element-stays-integer -->
```maxon
function main() returns ExitCode
	let a = [10 + 5, 20]
	let v = try a.get(0) otherwise 0
	return v + a.count()
end 'main'
```
```exitcode
17
```

### Element Type Checking

An element handed to `push` / `set` / `insert` must agree with the instance's element type.
The rule is the shared coercion rule every other declared slot asks — `checkDeclaredType` for a
trivial element, the managed heap-kind check for a managed one — because an array element is a
storage slot like a struct field, not a special case.

<!-- test: element-int-roundtrip -->
### An int element pushed into an int-element array reads back
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(42)
	return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
42
```

<!-- test: element-string-roundtrip -->
### A String element pushed into a String-element array reads back
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var arr = StringArray.create()
	arr.push("hi")
	let s = try arr.get(0) otherwise ""
	if s == "hi" 'match'
		return 7
	end 'match'
	return 0
end 'main'
```
```exitcode
7
```

<!-- test: element-struct-roundtrip -->
### A struct element pushed into an array of that struct reads back
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
	var arr = ItemArray.create()
	arr.push(Item.create(9))
	let first = try arr.get(0) otherwise Item.create(0)
	return first.value
end 'main'
```
```exitcode
9
```

<!-- test: element-byte-in-range -->
### An in-range int literal is a legal element of a Byte-ranged array
```maxon
typealias Byte = int(0 to 255)
typealias ByteSlots = Array with Byte

function main() returns ExitCode
	var arr = ByteSlots.create()
	arr.push(65)
	arr.insert(0, value: 12)
	try arr.set(1, value: 77) otherwise panic("test invariant: set OOB")
	return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
77
```

<!-- test: error.push-int-into-string-array -->
### An int pushed into a String-element array is refused
Unchecked, the element is stored raw and the array's decref walk frees `42` as a String
pointer — a wild free (0xC0000005), not a diagnostic.
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var arr = StringArray.create()
	arr.push(42)
	return arr.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:6: cannot assign 'int' to variable 'push' of type 'String'
```

<!-- test: error.push-string-into-int-array -->
### A String pushed into an int-element array is refused
Unchecked, the raw record pointer is stored and read back as an integer — a silent wrong answer.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push("hello")
	return arr.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:6: cannot assign 'String' to variable 'push' of type 'int'
```

<!-- test: error.push-float-into-int-array -->
### A float pushed into an int-element array is the lossy-narrowing refusal
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1.5)
	return arr.count()
end 'main'
```
```maxoncstderr
error E3009: <fragment>:7:6: cannot implicitly convert 'float' to 'int': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

<!-- test: error.push-struct-into-string-array -->
### A struct pushed into a String-element array is refused
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias StringArray = Array with String

function main() returns ExitCode
	var arr = StringArray.create()
	arr.push(Item.create(1))
	return arr.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:16:6: cannot assign 'struct' to variable 'push' of type 'String'
```

<!-- test: error.push-string-into-struct-array -->
### A String pushed into a struct-element array is refused
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
	var arr = ItemArray.create()
	arr.push("hello")
	return arr.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:16:6: cannot assign 'String' to variable 'push' of type 'struct'
```

<!-- test: error.push-wrong-struct-into-struct-array -->
### A struct of the WRONG declared type is refused
Both values are `structRef` pointers, so only the aggregate NAME separates them — the same
identity check a struct field's managed slot makes.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

type Other
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Other'

typealias ItemArray = Array with Item

function main() returns ExitCode
	var arr = ItemArray.create()
	arr.push(Other.create(1))
	return arr.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:24:6: cannot assign 'Other' to variable 'push' of type 'Item'
```

<!-- test: error.insert-int-into-string-array -->
### `insert` checks its `value:` argument too
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var arr = StringArray.create()
	arr.push("hi")
	arr.insert(0, value: 42)
	return arr.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:6: cannot assign 'int' to variable 'insert' of type 'String'
```

<!-- test: error.insert-string-into-int-array -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	arr.insert(0, value: "hello")
	return arr.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:8:6: cannot assign 'String' to variable 'insert' of type 'int'
```

<!-- test: error.insert-float-into-int-array -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	arr.insert(0, value: 1.5)
	return arr.count()
end 'main'
```
```maxoncstderr
error E3009: <fragment>:8:6: cannot implicitly convert 'float' to 'int': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

<!-- test: error.set-int-into-string-array -->
### `set` checks its `value:` argument too
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var arr = StringArray.create()
	arr.push("hi")
	try arr.set(0, value: 42) otherwise panic("test invariant: set OOB")
	return arr.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:10: cannot assign 'int' to variable 'set' of type 'String'
```

<!-- test: error.set-string-into-int-array -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	try arr.set(0, value: "hello") otherwise panic("test invariant: set OOB")
	return arr.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:8:10: cannot assign 'String' to variable 'set' of type 'int'
```

<!-- test: error.set-float-into-int-array -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	try arr.set(0, value: 1.5) otherwise panic("test invariant: set OOB")
	return arr.count()
end 'main'
```
```maxoncstderr
error E3009: <fragment>:8:10: cannot implicitly convert 'float' to 'int': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

### `append` checks its ARGUMENT's element type too

`append` is the one `Array` member whose argument is a whole other `Array`, so it is a COERCION
site like a declared parameter — and its argument is not merely handed over, it is COPIED
element-wise into the receiver's own storage. That makes it a bulk `push`, and it owes exactly
what a `push` owes: **every value the argument's element can hold must be a value the receiver's
element admits.** A `push` gets a runtime range check where the answer is not static; a bulk byte
copy cannot have one, so the question is settled at compile time or the append is refused.

The REPRESENTATION question (same managedness, same element width — what makes the byte copy
legal at all) and the VALUE-DOMAIN question are separate, and both are asked. Keeping only the
first is how a `Small` walked into a `Bytes`; replacing it with instance identity would refuse
`[1, 2, 3]` appended to an `Array with Integer`, which the four cases above require.

<!-- test: error.append-narrower-element-array -->
### An array of a WIDER element is refused where a narrower one is declared
`Small` and `Byte` are both byte-packed, so the shape test alone admitted this — and a `150`
pushed as a `Small` then read back through an accessor typed `int(0 to 100)` was a silent wrong
answer with no diagnostic anywhere.

The element reads `Byte$0_100` and not `Byte` because `stdlib/` declares `Byte` over a different
range, which makes the name range-CONTESTED and gives each distinct range its own mint
(`RangedAliasRegistry.settleRangeContests`); it is the same `Byte` this file declares.
```maxon
typealias Byte = int(0 to 100)
typealias Small = int(0 to 200)
typealias Bytes = Array with Byte
typealias Smalls = Array with Small

function main() returns ExitCode
	var s = Smalls.create()
	s.push(150)
	var b = Bytes.create()
	b.append(s)
	return try b.get(0) otherwise 1
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:11: argument type mismatch for 'other': expected 'Array_Byte$0_100', got 'Array_Small'
```

<!-- test: error.append-narrower-element-array-through-the-buffer-surface -->
### The BUFFER surface's `append` is the same door and answers the same way
One record, two surfaces: `b.managed.append(s)` reaches `__ManagedMemory`'s `append`, whose rule
used to be a second spelling of the `Array` one. It returned the identical `150`.
```maxon
typealias Byte = int(0 to 100)
typealias Small = int(0 to 200)
typealias Bytes = Array with Byte
typealias Smalls = Array with Small

function main() returns ExitCode
	var s = Smalls.create()
	s.push(150)
	var b = Bytes.create()
	try b.managed.append(s) otherwise panic("test invariant: append")
	return try b.get(0) otherwise 1
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:16: argument type mismatch for 'other': expected '__ManagedMemory', got 'Array_Small'
```

<!-- test: error.append-bool-array-into-a-byte-array -->
### A `bool` element is one byte and is still not a `Byte`
The shape test admits ANY one-byte trivial element, and a `bool` is one. It has no integer value
range to compare, so only its own identity can admit it.
```maxon
typealias Byte = int(0 to 100)
typealias Bytes = Array with Byte
typealias Bools = Array with bool

function main() returns ExitCode
	var f = Bools.create()
	f.push(true)
	var b = Bytes.create()
	b.append(f)
	return b.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:11: argument type mismatch for 'other': expected 'Array_Byte$0_100', got 'Array_bool'
```

<!-- test: error.append-enum-array-into-a-different-enum-array -->
### Two enums are two machine-word elements and two types
```maxon
enum Colour
	red
	green
end 'Colour'

enum Shape
	circle
	square
end 'Shape'

typealias Colours = Array with Colour
typealias Shapes = Array with Shape

function main() returns ExitCode
	var sh = Shapes.create()
	sh.push(Shape.square)
	var c = Colours.create()
	c.append(sh)
	return c.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:19:11: argument type mismatch for 'other': expected 'Array_Colour', got 'Array_Shape'
```

<!-- test: error.append-a-non-array -->
### A value that is not an `Array` at all names both types, like every other argument door
```maxon
function main() returns ExitCode
	var a = [1, 2, 3]
	a.append(42)
	return a.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:4:11: argument type mismatch for 'other': expected 'Array_int', got 'int'
```

<!-- test: append-wider-element-array-takes-a-narrower-one -->
### The WIDENING direction is safe and stays legal
Every `Byte` value is a `Small` value, so nothing can be read back out of range. A rule that
refused both directions would be over-refusal.
```maxon
typealias Byte = int(0 to 100)
typealias Small = int(0 to 200)
typealias Bytes = Array with Byte
typealias Smalls = Array with Small

function main() returns ExitCode
	var b = Bytes.create()
	b.push(99)
	var s = Smalls.create()
	s.push(150)
	s.append(b)
	let first = try s.get(0) otherwise 0
	let second = try s.get(1) otherwise 0
	return first + second
end 'main'
```
```exitcode
249
```

<!-- test: append-same-alias-arrays -->
### Two arrays of the same alias are one instance and append freely
```maxon
typealias Byte = int(0 to 100)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var a = Bytes.create()
	a.push(7)
	var b = Bytes.create()
	b.push(9)
	a.append(b)
	let first = try a.get(0) otherwise 0
	let second = try a.get(1) otherwise 0
	return first + second
end 'main'
```
```exitcode
16
```

<!-- test: error.append-across-two-element-widths -->
### The REPRESENTATION half is live on its own
Every `Byte` value is a `Wide` value, so the value-domain half says yes — and the two records stride
1 and 4, so `__arr_append`'s byte copy would read the source at the wrong width. The two halves are
INDEPENDENT: this one is refused by the shape test with the domain test agreeing, and
`error.append-narrower-element-array` above is refused by the domain test with the shape test agreeing.
```maxon
typealias Byte = int(0 to 100)
typealias Wide = int(0 to 100000)
typealias Bytes = Array with Byte
typealias Wides = Array with Wide

function main() returns ExitCode
	var b = Bytes.create()
	b.push(9)
	var w = Wides.create()
	w.push(5)
	w.append(b)
	return w.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:11: argument type mismatch for 'other': expected 'Array_Wide', got 'Array_Byte$0_100'
```

<!-- test: append-reads-the-value-set-never-the-alias-name -->
### The rule reads the VALUE SET, never the element's name
`A` and `B` are two names for one value set, which is what `RangedTypeAlias`'s own header means by
*"two aliases over the same range are the same type"* — so nothing can be read back out of range and
the append stands. It is the same admission that lets `[1, 2, 3]` (`Array with int`) into an
`Array with Integer`; only there the two spellings are a primitive and an alias.
```maxon
typealias A = int(0 to 100)
typealias B = int(0 to 100)
typealias As = Array with A
typealias Bs = Array with B

function main() returns ExitCode
	var a = As.create()
	a.push(7)
	var b = Bs.create()
	b.push(9)
	a.append(b)
	return (try a.get(0) otherwise 0) + (try a.get(1) otherwise 0)
end 'main'
```
```exitcode
16
```
