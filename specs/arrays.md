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

typealias Integer = int(i64.min to i64.max)
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

	return (v0 as Integer) + (v1 as Integer) + (v2 as Integer)
end 'main'
```
```exitcode
60
```

### Byte Array Initialized

<!-- test: byte-array-initialized -->
```maxon

typealias Integer = int(i64.min to i64.max)
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

	return (v0 as Integer) + (v1 as Integer) + (v2 as Integer)
end 'main'
```
```exitcode
6
```

### Byte Array Set

<!-- test: byte-array-set -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)

typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10 as Byte)
	arr.push(20 as Byte)
	arr.push(30 as Byte)

	try arr.set(1, value: 99 as Byte) otherwise panic("test invariant: set OOB")

	let val = try arr.get(1) otherwise 0 as Byte
	return val as Integer
end 'main'
```
```exitcode
99
```

### Byte Array Max Values

<!-- test: byte-array-max-values -->
```maxon

typealias Integer = int(i64.min to i64.max)
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

	if (v0 as Integer) != 255 'c0'
		return 1
	end 'c0'
	if (v1 as Integer) != 0 'c1'
		return 2
	end 'c1'
	if (v2 as Integer) != 128 'c2'
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
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

typealias PointArray = Array with Point

function makePoints() returns PointArray
	let p1 = Point.create(x: 1, y: 2)
	let p2 = Point.create(x: 3, y: 4)
	return [p1, p2]
end 'makePoints'

function main() returns ExitCode
	let pts = makePoints()
	let p0 = try pts.get(0) otherwise Point.create(x: 0, y: 0)
	let p1 = try pts.get(1) otherwise Point.create(x: 0, y: 0)
	return p0.x + p1.y
end 'main'
```
```exitcode
5
```
