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

### Where the storage came from is never observable

An array literal's elements may be materialized into read-only static data
rather than a heap buffer, and the array then holds a borrowed view of it until
the first write copies it out. That is an allocation strategy, not a type: a
literal-backed array is an ordinary `Array` and answers `resize`, `reserve`,
`push` and `count` exactly as one built by `create()` + `push` does.

```text
var a = [10, 20, 30]
a.resize(0)   // count() == 0, and `a` is still a usable array
a.push(7)     // count() == 1
```

### `resize` accepts only a length it can produce

`resize(newLength ElementIndex)` takes an `ElementIndex`, which is
`int(0 to i64.max)` — the count of elements the array will have afterwards, and
`i64.max` because the buffer beneath it measures its length in a signed machine
word. A value that cannot be a length is not a request the operation can honour,
and the range is what says so: the argument is refused at the DOOR, before the
body runs. A literal the compiler can fold is refused at compile time (E3005); a
value it cannot fold is refused by the callee-entry guard, which PANICS —
`resize` cannot report failure to its caller, because it does not `throw`. It
never quietly publishes the impossible length, because every later `count()`,
`get` and `for..in` would then be reasoning about an array that does not exist.

The rule belongs to the operation, not to the storage: a literal-backed array
and a pushed-into array reject the same lengths for the same reason.

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

### An empty container never written through is ONE shared record

`IntArray.create()` builds a record whose every field is a compile-time constant — no buffer, no
length, no capacity, no parent — so two empty arrays of the same element type have nothing that
could tell them apart. The compiler emits ONE immortal record in static data and hands every empty
`create()` that is never written through a reference to it, exactly as it does for a never-mutated
string literal. `is` therefore answers `true` for two such arrays, for the same reason `"" is ""`
does, and the empty array costs no allocation at all.

Sharing is decided per SITE by the same whole-program escape analysis that decides literal
interning: an array the program ever writes through — `push`, `set`, `resize`, `reserve`, or a
call that does any of those — gets its own heap record and is never shared. One `var` being
pushed into therefore says nothing about another `create()` in the same function.

<!-- test: empty-let-arrays-share-one-record -->
Two empty arrays nothing writes through are the same record.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	let c = IntArray.create()
	let d = IntArray.create()
	if c is d 'shared'
		return 1
	end 'shared'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: empty-shared-array-reads-as-empty -->
The shared record answers every read exactly as a freshly allocated empty array does: no
elements, no capacity, and `get` is out of bounds at index 0.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	let c = IntArray.create()
	if c.count() != 0 'count'
		return 1
	end 'count'
	if c.capacity() != 0 'cap'
		return 2
	end 'cap'
	return try c.get(0) otherwise 0
end 'main'
```
```exitcode
0
```

<!-- test: empty-var-arrays-stay-independent -->
The control: writing through one empty array must not reach another. `a` is pushed into, so it is
never a shared record; `b` is untouched and still reports zero elements.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var a = IntArray.create()
	var b = IntArray.create()
	a.push(1)
	print("mutated a={a.count()} untouched b={b.count()}")
	let c = IntArray.create()
	let d = IntArray.create()
	print(" two empty lets identical? {c is d}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
mutated a=1 untouched b=0 two empty lets identical? true
```

<!-- test: empty-array-mutated-in-another-function-stays-independent -->
The write need not be in the same function as the `create()`. `fill` pushes through its parameter,
so every array reaching it is written through and cannot be shared — including `a`, whose
`create()` is in `main`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function fill(target IntArray)
	target.push(7)
end 'fill'

function main() returns ExitCode
	var a = IntArray.create()
	var b = IntArray.create()
	fill(a)
	if b.count() != 0 'untouched'
		return 1
	end 'untouched'
	return a.count() + (try a.get(0) otherwise 0)
end 'main'
```
```exitcode
8
```

<!-- test: empty-array-returned-from-a-helper-shares-one-record -->
A helper that only forwards `create()` returns the shared record too, and two calls to it are the
same value — the analysis follows the return, not the call site's spelling.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function makeEmpty() returns IntArray
	return IntArray.create()
end 'makeEmpty'

function main() returns ExitCode
	let c = makeEmpty()
	let d = makeEmpty()
	if c is d 'shared'
		return c.count() + 1
	end 'shared'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: empty-managed-element-array-shares-its-own-record -->
An `Array with String` and an `Array with Integer` are different records: their elements are
different widths and are released differently. Both share within their own element type, and
neither read reaches the other's.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias StrArray = Array with String

function main() returns ExitCode
	let si = IntArray.create()
	let sj = IntArray.create()
	let ss = StrArray.create()
	let st = StrArray.create()
	var owned = StrArray.create()
	owned.push("x")
	if si is not sj 'ints'
		return 1
	end 'ints'
	if ss is not st 'strs'
		return 2
	end 'strs'
	return 40 + ss.count() + si.count() + owned.count()
end 'main'
```
```exitcode
41
```

<!-- test: empty-array-dropped-without-leak -->
The shared record is immortal: it is never freed, and going out of scope must not try to. A
pushed-into array beside it is an ordinary heap record and IS freed. Both in one scope, so a
double free or a leak in either shows up as a non-zero exit.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function drops()
	let shared = IntArray.create()
	var owned = IntArray.create()
	owned.push(1)
	owned.push(2)
	print("{shared.count()}{owned.count()}")
end 'drops'

function main() returns ExitCode
	drops()
	drops()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0202
```

#### A container that escapes into a PLACE is never shared

Sharing is only sound while the compiler can see every write. A value stored into a heap place — an
array slot, a struct field, a union payload — can be fetched back out and written through from
anywhere, and the compiler cannot follow it there. Such a value always gets its own record. The rule
is not about arrays: it is the same one that stops a never-mutated `String` literal being shared once
it is stored somewhere, and the cases below pin both readings of it.

<!-- test: empty-array-pushed-into-another-array-is-not-shared -->
Two empty arrays pushed into a container, then one of them grown THROUGH the container. If they had
been one shared record the untouched one would report the other's elements.
```maxon
typealias Score = int(-1000 to 1000)
typealias Inner = Array with Score
typealias Outer = Array with Inner

function main() returns ExitCode
	var outer = Outer.create()
	outer.push(Inner.create())
	outer.push(Inner.create())
	var grown = try outer.get(0) otherwise Inner.create()
	grown.push(7)
	let untouched = try outer.get(1) otherwise Inner.create()
	return grown.count() + untouched.count() * 10
end 'main'
```
```exitcode
1
```

<!-- test: empty-array-in-a-struct-field-is-not-shared -->
The same rule at a field ASSIGN rather than at construction. Both holders are reset to an empty
array, then one is grown; the other must still be empty.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Holder
	export var items as IntArray

	static function create() returns Self
		return Self{items: IntArray.create()}
	end 'create'

	public function reset()
		items = IntArray.create()
	end 'reset'
end 'Holder'

function main() returns ExitCode
	var a = Holder.create()
	var b = Holder.create()
	a.reset()
	b.reset()
	a.items.push(3)
	return a.items.count() + b.items.count() * 10
end 'main'
```
```exitcode
1
```

<!-- test: empty-array-in-a-union-payload-is-not-shared -->
A union payload is a place too. Both boxes carry an empty array; growing one must not grow the other.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

union Box
	holder(items IntArray)
end 'Box'

function main() returns ExitCode
	var first = Box.holder(IntArray.create())
	var second = Box.holder(IntArray.create())
	match first 'grow'
		holder(items) then items.push(9)
	end 'grow'
	var untouched = 0
	match second 'read'
		holder(items) then untouched = items.count()
	end 'read'
	return 1 + untouched * 10
end 'main'
```
```exitcode
1
```

<!-- test: string-element-written-through-an-array-leaves-the-literal-alone -->
The `String` reading of the same rule. `"lit"` is pushed into an array, fetched back out and grown;
an untouched `"lit"` elsewhere in the program must still be `lit`, and nothing may leak.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var arr = StrArray.create()
	arr.push("lit")
	var taken = try arr.get(0) otherwise ""
	taken.append("!")
	let untouched = "lit"
	print("taken={taken} untouched={untouched}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
taken=lit! untouched=lit
```

<!-- test: string-element-written-through-an-array-literal-leaves-the-literal-alone -->
And through an array LITERAL's element slot, which is the same store written a different way.
```maxon
function main() returns ExitCode
	let colors = ["red", "green"]
	var taken = try colors.get(0) otherwise ""
	taken.append("!")
	let untouched = "red"
	print("taken={taken} untouched={untouched} still={colors.count()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
taken=red! untouched=red still=2
```

#### A write REBINDS the local rather than costing the record

An empty container is shared until something writes through it. A write does not have to cost the
sharing on every path that reaches the function: the compiler rebinds the local to a private record
immediately before the write, so a path that never writes never pays. The shared record is therefore
still the answer for the common case of `var s = create()` with a write that only sometimes happens.

The rebind reaches exactly one local, so it is only ever done when the compiler can see that the
record has no second handle: two names for one array are ALIASES, and rebinding one of them would
leave the other reading an array that no longer exists. Those cases keep their own record.

<!-- test: empty-array-conditionally-written-still-shares -->
The write is reachable but not taken. Both calls hand back the shared record, and both are empty.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function makeSites(rare bool) returns IntArray
	var s = IntArray.create()
	if rare 'r'
		s.push(1)
	end 'r'
	return s
end 'makeSites'

function main() returns ExitCode
	let a = makeSites(false)
	let b = makeSites(false)
	print("shared? {a is b} a={a.count()} b={b.count()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
shared? true a=0 b=0
```

<!-- test: empty-array-conditionally-written-is-private-when-taken -->
The same function on the path that DOES write. Each call now owns its record, they are not the same
value, and neither call's push reaches the other.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function makeSites(rare bool) returns IntArray
	var s = IntArray.create()
	if rare 'r'
		s.push(1)
	end 'r'
	return s
end 'makeSites'

function main() returns ExitCode
	let a = makeSites(true)
	let b = makeSites(true)
	let empty = makeSites(false)
	if a is b 'distinct'
		return 1
	end 'distinct'
	return a.count() + b.count() + empty.count()
end 'main'
```
```exitcode
2
```

<!-- test: empty-array-with-a-second-name-is-not-rebound -->
Two names for one array are aliases, so the write through one is visible through the other. A rebind
would reach only the name it was written on, which is why an aliased array keeps its own record from
the start.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var a = IntArray.create()
	var b = a
	a.push(1)
	if a is not b 'aliased'
		return 99
	end 'aliased'
	return a.count() * 10 + b.count()
end 'main'
```
```exitcode
11
```

<!-- test: empty-array-in-an-array-literal-slot-is-not-rebound -->
A second handle does not have to be a NAME. An array literal's element slot holds the very same
record, so once an empty array is written into one, a rebind of the local would reach the local and
leave the slot reading an array that is no longer the one the local names. `held` must report the
element that WAS written, and the untouched build must still report none.
```maxon
typealias Score = int(-1000 to 1000)
typealias Inner = Array with Score
typealias Outer = Array with Inner

function build(flag bool) returns Outer
	var s = Inner.create()
	let held = [s]
	if flag 'f'
		s.push(7)
	end 'f'
	return held
end 'build'

function main() returns ExitCode
	let written = build(true)
	let untouched = build(false)
	let a = try written.get(0) otherwise Inner.create()
	let b = try untouched.get(0) otherwise Inner.create()
	print("held={a.count()} untouched={b.count()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
held=1 untouched=0
```

<!-- test: empty-array-written-in-a-loop-stays-correct -->
The rebind happens in front of the write, so a loop that writes many times materialises once and
appends to the record it owns from then on — the second and later writes find a record that is no
longer the shared one.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function collect(upTo Integer) returns IntArray
	var s = IntArray.create()
	var i = 0
	while i < upTo 'each'
		s.push(i)
		i = i + 1
	end 'each'
	return s
end 'collect'

function main() returns ExitCode
	let none = collect(0)
	let some = collect(5)
	let alsoNone = collect(0)
	if none is not alsoNone 'empties-shared'
		return 90
	end 'empties-shared'
	return none.count() + some.count() + (try some.get(4) otherwise 0)
end 'main'
```
```exitcode
9
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

### Insert

`insert` clamps its index to `count` at the HIGH end and only there — that half is unchanged and is
`stdlib-array.insert-index-clamps-high`'s. There is no LOW clamp, and there used to be one: `at`'s
`ElementIndex` was declared `int(0 to u64.max)`, a FULL range, which is the one shape both compilers leave
unguarded, so a `-1` arrived in the body intact. Before the clamp existed it made the tail shift read the
slot BEFORE the buffer and store it at 0, turning `[10, 20, 30]` into `[0, 10, 20]`.

`ElementIndex` now stops at `i64.max`, so the DOOR refuses the negative and the clamp has nothing left to
do. A negative index is no longer a quiet append-to-front; it is a refusal — and WHICH refusal depends on
whether the compiler can fold the value. A literal is refused where it is written.

<!-- test: error.insert-negative-index-is-refused -->
```maxon
function main() returns ExitCode
	var arr = [10, 20, 30]
	arr.insert(-1, value: 99)
	return arr.count()
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/arrays/error.insert-negative-index-is-refused.test:4:6: Value -1 is outside the range of 'ElementIndex' (int(0 to 9223372036854775807))
```

A negative the compiler cannot fold reaches the callee-entry guard instead, and panics there — uncatchably,
and BEFORE `insert`'s body runs. The `print` is the proof the program never got past the call: the old
clamping behaviour would have reached it and printed a count of 4.

<!-- test: insert-laundered-negative-index-panics-at-the-door -->
```maxon
typealias Signed = int(i64.min to i64.max)

function launder(n Signed) returns Signed
	return n
end 'launder'

function main() returns ExitCode
	var arr = [10, 20, 30]
	arr.insert(launder(-1), value: 99)
	print("inserted, count is now {arr.count()}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at Array.maxon:354: Range check failed: value outside typealias 'ElementIndex'
Stack trace:
  in __Array_i64.insert
  in main
  in mrt_start
```

### Set is bounded by the LIVE LENGTH, not by the capacity

`set` refuses every index at or past `count()`, and the bound is the array's own rather than the storage
layer's: `__ManagedMemory.set` underneath admits any index below the CAPACITY, deliberately, because the
buffer is where a caller stages slots and then publishes them with `setLength`. An `Array` has no staging
idiom — `count()` is its whole contract — so forwarding straight through would write a slot no reader can
reach.

For a MANAGED element that is not merely invisible, it is a LEAK: the record lands outside `[0, count)`,
which is exactly the range the element-destroy walk covers, so nothing ever releases it. Measured before
the guard existed, on both compilers: `push` one string, `reserve(8)`, `set(2, …)` — the store SUCCEEDED,
`count()` still read 1, and the second string was never freed.

<!-- test: set-past-the-live-length-is-refused -->
```maxon
function main() returns ExitCode
	var arr = ["one long heap allocated string value here"]
	arr.reserve(8)
	try arr.set(2, value: "two long heap allocated string value here") otherwise 'pastLength'
		return 42
	end 'pastLength'
	return arr.count()
end 'main'
```
```exitcode
42
```

A NEGATIVE index is refused too, and it is refused by a DIFFERENT mechanism from the one above — which is
why it needs saying separately. The past-the-length refusal is `set`'s own, a catchable `ArrayError`. A
negative never reaches `set` at all: `index`'s `ElementIndex` stops at `i64.max`, so the value is refused
at the door. It used to arrive intact, because the alias was the full `int(0 to u64.max)` and a full range
is the one shape neither compiler guards, and `-1` is below the length on every signed comparison.

A literal is refused at compile time.

<!-- test: error.set-negative-index-is-refused -->
```maxon
function main() returns ExitCode
	var arr = [10, 20, 30]
	try arr.set(-1, value: 99) otherwise 'negative'
		return 42
	end 'negative'
	return try arr.get(0) otherwise 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/arrays/error.set-negative-index-is-refused.test:4:10: Value -1 is outside the range of 'ElementIndex' (int(0 to 9223372036854775807))
```

A laundered one panics at the callee-entry guard — and the `try … otherwise` around the call does NOT
catch it. That is the whole of the difference from the past-the-length case: this refusal is a range
violation, not an error the type declares, so there is no arm for a program to take. The `otherwise` block
returning 42 is what says so: it is never entered.

<!-- test: set-laundered-negative-index-panics-at-the-door -->
```maxon
typealias Signed = int(i64.min to i64.max)

function launder(n Signed) returns Signed
	return n
end 'launder'

function main() returns ExitCode
	var arr = [10, 20, 30]
	try arr.set(launder(-1), value: 99) otherwise 'negative'
		return 42
	end 'negative'
	return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
1
```
```stderr
panic at Array.maxon:266: Range check failed: value outside typealias 'ElementIndex'
Stack trace:
  in __Array_i64.set
  in main
  in mrt_start
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

<!-- test: array-literal-resize-to-zero -->
Emptying a literal-backed array with `resize(0)`, then reusing it. The literal's
elements live in read-only static data, so the array owns NO writable slots —
but `resize(0)` asks for none, so it must simply succeed. (It used to route
through a grow, which asked the allocator for a zero-byte buffer and panicked.)
```maxon
function main() returns ExitCode
	var a = [10, 20, 30]
	a.resize(0)
	if a.count() != 0 'emptied'
		return 1
	end 'emptied'
	a.push(7)
	if a.count() != 1 'regrown'
		return 2
	end 'regrown'
	return try a.get(0) otherwise 3
end 'main'
```
```exitcode
7
```

<!-- test: array-literal-resize-shrink-and-grow -->
The same array literal shrunk to a nonzero length and grown back. The slots the
shrink gave up read as zero when the array grows over them again — the
capacity-slot invariant holds for a literal-backed array too.
```maxon
function main() returns ExitCode
	var a = [10, 20, 30]
	a.resize(1)
	if a.count() != 1 'shrunk'
		return 1
	end 'shrunk'
	a.resize(3)
	if a.count() != 3 'regrown'
		return 2
	end 'regrown'
	let kept = try a.get(0) otherwise 0
	let vacated = try a.get(2) otherwise 0
	return 0 if kept == 10 and vacated == 0 else 3
end 'main'
```
```exitcode
0
```

<!-- test: error.array-resize-negative-length-is-refused -->
A length `resize` cannot produce is refused, and a length the compiler can FOLD
is refused where it is written. This half needs no twin: the fold happens at the
call site, on the argument, before anything about the array's backing is
consulted — so a literal-backed array and a `push`-built one are refused by the
same constant, in the same place. What DOES depend on the backing is the runtime
half, and that keeps its pair below.
```maxon
function main() returns ExitCode
	var a = [10, 20, 30]
	a.resize(-2)
	print("resized to {a.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/arrays/error.array-resize-negative-length-is-refused.test:4:4: Value -2 is outside the range of 'ElementIndex' (int(0 to 9223372036854775807))
```

<!-- test: array-literal-resize-out-of-range-panics -->
A length the compiler cannot fold reaches `resize`'s callee-entry guard and
panics there, and it panics the same way whatever the array is backed by. This
is the literal-backed half; the twin below is the same call on an array built by
`push`. Publishing the length instead would hand every later `count()` a value
no array can have.
```maxon
typealias Signed = int(i64.min to i64.max)

function launder(n Signed) returns Signed
	return n
end 'launder'

function main() returns ExitCode
	var a = [10, 20, 30]
	a.resize(launder(-2))
	print("resized to {a.count()}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at Array.maxon:436: Range check failed: value outside typealias 'ElementIndex'
Stack trace:
  in __Array_i64.resize
  in main
  in mrt_start
```

<!-- test: array-pushed-resize-out-of-range-panics -->
The heap-backed twin of the test above: an array built by `push` refuses the
same length, at the same place, with the same message. The two frames name two
different specializations, which is the point — the guard is copied into each.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function launder(n Integer) returns Integer
	return n
end 'launder'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(10)
	a.push(20)
	a.push(30)
	a.resize(launder(-2))
	print("resized to {a.count()}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at Array.maxon:436: Range check failed: value outside typealias 'ElementIndex'
Stack trace:
  in IntArray.resize
  in main
  in mrt_start
```

<!-- test: array-literal-reserve-never-shrinks -->
`reserve` ensures capacity; it never takes any away. On a literal-backed array the
request goes through a copy-on-write that promotes the borrowed elements into an
owned buffer, and a request for FEWER slots than the array already holds must not
reallocate under the elements that copy just rescued — that would publish
`capacity() < count()`, and every read past the new capacity would be off the end
of the allocation.
```maxon
function main() returns ExitCode
	var a = [10, 20, 30]
	a.reserve(1)
	if a.count() != 3 'cnt'
		return 1
	end 'cnt'
	if a.capacity() < a.count() 'cap'
		return 2
	end 'cap'
	return try a.get(2) otherwise 3
end 'main'
```
```exitcode
30
```
