---
feature: list
status: experimental
keywords: [list, linked list, doubly linked, collection, prepend, append]
category: collections
---
# List

## Documentation

A `List` is a doubly linked list collection. It provides O(1) insertion and removal at both ends, and O(n) indexed access.

Internally, List is backed by a `__ManagedList` — a doubly linked list of nodes that supports O(1) insertion and removal at both ends.

### Creating a List

Use the `List from` syntax with an array literal or any `Array` value (`List from values`):

```maxon
var list = List from [1, 2, 3]
```

Or create an empty list with an explicit type:

```text
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer
var empty = IntList.create()
```

### Methods

#### count() returns Count

Get the number of elements.

#### isEmpty() returns bool

Check if the list is empty.

#### first() returns Element throws ArrayError

Get the first element. Throws if empty.

#### last() returns Element throws ArrayError

Get the last element. Throws if empty.

#### get(index Index) returns Element throws ArrayError

Get element at index (0-based, front to back). Throws if out of bounds.

#### prepend(value Element)

Add an element to the front of the list. O(1).

#### append(value Element)

Add an element to the back of the list. O(1).

#### insert(at Index, value Element) throws ArrayError

Insert an element at the given index. O(n). Valid range is `[0, count]` —
passing `count` appends at the end. An index greater than `count` is out of
bounds and throws `ArrayError.indexOutOfBounds`.

#### removeFirst() returns Element throws ArrayError

Remove and return the first element. Throws if empty. O(1).

#### removeLast() returns Element throws ArrayError

Remove and return the last element. Throws if empty. O(1).

#### remove(at Index) returns Element throws ArrayError

Remove and return the element at the given index. Throws if out of bounds. O(n).

#### clear()

Remove all elements from the list.

### Iteration

List implements `Iterable`, so it can be used with for-in loops:

```text
for item in list 'loop'
    print("{item}\n")
end 'loop'
```

Elements are iterated front to back. Iteration uses a cached cursor on the underlying managed list for O(n) total traversal (O(1) per element).

## Tests

<!-- test: basic.creation -->
```maxon
function main() returns ExitCode
	let list = List from [10, 20, 30]
	return list.count()
end 'main'
```
```exitcode
3
```

<!-- test: basic.empty -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
	var list = IntList.create()
	if list.isEmpty() 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: basic.first-last -->
```maxon
function main() returns ExitCode
	let list = List from [10, 20, 30]
	let f = (try list.first() otherwise 0) as FullRangeInt
	let l = (try list.last() otherwise 0) as FullRangeInt
	print("{f}\n")
	print("{l}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
30
```

<!-- test: basic.first-empty -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
	var list = IntList.create()
	let f = try list.first() otherwise 99
	return f
end 'main'
```
```exitcode
99
```

<!-- test: basic.last-empty -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
	var list = IntList.create()
	let l = try list.last() otherwise 99
	return l
end 'main'
```
```exitcode
99
```

<!-- test: prepend.single -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
	var list = IntList.create()
	list.prepend(42)
	let f = try list.first() otherwise 0
	return f
end 'main'
```
```exitcode
42
```

<!-- test: prepend.multiple -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
	var list = IntList.create()
	list.prepend(3)
	list.prepend(2)
	list.prepend(1)
	let f = try list.first() otherwise 0
	let l = try list.last() otherwise 0
	print("{f}\n")
	print("{l}\n")
	print("{list.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
3
3
```

<!-- test: append.single -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
	var list = IntList.create()
	list.append(42)
	let f = try list.first() otherwise 0
	return f
end 'main'
```
```exitcode
42
```

<!-- test: append.multiple -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
	var list = IntList.create()
	list.append(1)
	list.append(2)
	list.append(3)
	let f = try list.first() otherwise 0
	let l = try list.last() otherwise 0
	print("{f}\n")
	print("{l}\n")
	print("{list.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
3
3
```

<!-- test: get.valid -->
```maxon
function main() returns ExitCode
	let list = List from [10, 20, 30, 40]
	let v0 = (try list.get(0) otherwise 0) as FullRangeInt
	let v1 = (try list.get(1) otherwise 0) as FullRangeInt
	let v2 = (try list.get(2) otherwise 0) as FullRangeInt
	let v3 = (try list.get(3) otherwise 0) as FullRangeInt
	print("{v0}\n")
	print("{v1}\n")
	print("{v2}\n")
	print("{v3}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
20
30
40
```

<!-- test: get.out-of-bounds -->
```maxon
function main() returns ExitCode
	let list = List from [10, 20, 30]
	let v = (try list.get(5) otherwise 99) as FullRangeInt
	return v as ExitCode
end 'main'
```
```exitcode
99
```

<!-- test: insert.at-beginning -->
```maxon
function main() returns ExitCode
	var list = List from [20, 30]
	try list.insert(0, value: 10) otherwise 'err'
		return 1
	end 'err'
	let v0 = (try list.get(0) otherwise 0) as FullRangeInt
	let v1 = (try list.get(1) otherwise 0) as FullRangeInt
	let v2 = (try list.get(2) otherwise 0) as FullRangeInt
	print("{v0}\n")
	print("{v1}\n")
	print("{v2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
20
30
```

<!-- test: insert.at-middle -->
```maxon
function main() returns ExitCode
	var list = List from [10, 30]
	try list.insert(1, value: 20) otherwise 'err'
		return 1
	end 'err'
	let v0 = (try list.get(0) otherwise 0) as FullRangeInt
	let v1 = (try list.get(1) otherwise 0) as FullRangeInt
	let v2 = (try list.get(2) otherwise 0) as FullRangeInt
	print("{v0}\n")
	print("{v1}\n")
	print("{v2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
20
30
```

<!-- test: insert.at-end -->
```maxon
function main() returns ExitCode
	var list = List from [10, 20]
	try list.insert(2, value: 30) otherwise 'err'
		return 1
	end 'err'
	let v0 = (try list.get(0) otherwise 0) as FullRangeInt
	let v1 = (try list.get(1) otherwise 0) as FullRangeInt
	let v2 = (try list.get(2) otherwise 0) as FullRangeInt
	print("{v0}\n")
	print("{v1}\n")
	print("{v2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
20
30
```

<!-- test: insert.out-of-bounds -->

An index past `count` is out of bounds and throws `ArrayError.indexOutOfBounds`.
Only `at == count` may append; `at > count` must throw and leave the list
unchanged, rather than clamp the index to the tail and grow the list.

```maxon
function main() returns ExitCode
	var list = List from [10]
	try list.insert(5, value: 99) otherwise 'oob'
		print("threw\n")
		print("{list.count()}\n")
		return 0
	end 'oob'
	print("no throw\n")
	return 1
end 'main'
```
```exitcode
0
```
```stdout
threw
1
```

<!-- test: remove-first -->
```maxon
function main() returns ExitCode
	var list = List from [10, 20, 30]
	let removed = (try list.removeFirst() otherwise 0) as FullRangeInt
	print("{removed}\n")
	print("{list.count()}\n")
	let f = (try list.first() otherwise 0) as FullRangeInt
	print("{f}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
2
20
```

<!-- test: remove-last -->
```maxon
function main() returns ExitCode
	var list = List from [10, 20, 30]
	let removed = (try list.removeLast() otherwise 0) as FullRangeInt
	print("{removed}\n")
	print("{list.count()}\n")
	let l = (try list.last() otherwise 0) as FullRangeInt
	print("{l}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
30
2
20
```

<!-- test: remove.at-index -->
```maxon
function main() returns ExitCode
	var list = List from [10, 20, 30, 40]
	let removed = (try list.remove(1) otherwise 0) as FullRangeInt
	print("{removed}\n")
	print("{list.count()}\n")
	let v0 = (try list.get(0) otherwise 0) as FullRangeInt
	let v1 = (try list.get(1) otherwise 0) as FullRangeInt
	let v2 = (try list.get(2) otherwise 0) as FullRangeInt
	print("{v0}\n")
	print("{v1}\n")
	print("{v2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
20
3
10
30
40
```

<!-- test: iteration.for-in -->
```maxon
function main() returns ExitCode
	let list = List from [10, 20, 30]
	for item in list 'loop'
		print("{item}\n")
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
20
30
```

<!-- test: iteration.large-list -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
	var list = IntList.create()
	for i in 1 to 100 'build'
		list.append(i)
	end 'build'
	var sum = 0 as Integer
	for item in list 'loop'
		sum = sum + item
	end 'loop'
	return sum - 5050
end 'main'
```
```exitcode
0
```

<!-- test: iteration.two-loops -->
```maxon
function main() returns ExitCode
	let list = List from [1, 2, 3]
	var sum1 = 0 as FullRangeInt
	for item in list 'loop1'
		sum1 = sum1 + item
	end 'loop1'
	var sum2 = 0 as FullRangeInt
	for item in list 'loop2'
		sum2 = sum2 + item
	end 'loop2'
	if sum1 == sum2 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: mixed-operations -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = List with Integer

function main() returns ExitCode
	var list = IntList.create()
	list.append(2)
	list.append(3)
	list.prepend(1)
	list.append(4)
	let removed1 = try list.removeFirst() otherwise 0
	let removed2 = try list.removeLast() otherwise 0
	print("{removed1}\n")
	print("{removed2}\n")
	print("{list.count()}\n")
	let v0 = try list.get(0) otherwise 0
	let v1 = try list.get(1) otherwise 0
	print("{v0}\n")
	print("{v1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
4
2
2
3
```

<!-- test: strings -->
```maxon
typealias StringList = List with String

function main() returns ExitCode
	var list = StringList.create()
	list.append("hello")
	list.append("world")
	let f = try list.first() otherwise "none"
	let l = try list.last() otherwise "none"
	print("{f}\n")
	print("{l}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
world
```

<!-- test: clear -->
```maxon
function main() returns ExitCode
	var list = List from [1, 2, 3]
	list.clear()
	if list.isEmpty() 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: memory.remove-frees-strings -->
```maxon
typealias StringList = List with String

function testRemove()
	var list = StringList.create()
	list.append("hello world!!!!!!!!!!!!!!")
	list.append("goodbye world!!!!!!!!!!!!!")
	let removed = try list.removeFirst() otherwise "none"
	print("{removed}\n")
	print("{list.count()}\n")
end 'testRemove'

function main() returns ExitCode
	testRemove()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello world!!!!!!!!!!!!!!
1
```

<!-- test: memory.remove-returns-string -->
```maxon
typealias StringList = List with String

function testRemove() returns String
	var list = StringList.create()
	list.append("hello world!!!!!!!!!!!!!!")
	list.append("goodbye world!!!!!!!!!!!!!")
	return try list.removeFirst() otherwise "none"
end 'testRemove'

function main() returns ExitCode
	let removed = testRemove()
	print("{removed}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello world!!!!!!!!!!!!!!
```

<!-- test: memory.clear-frees-strings -->
```maxon
typealias StringList = List with String

function testClear()
	var list = StringList.create()
	list.append("alpha string!!!!!!!!!!!!!!!")
	list.append("beta string!!!!!!!!!!!!!!!!")
	list.append("gamma string!!!!!!!!!!!!!!!")
	list.clear()
	print("{list.count()}\n")
end 'testClear'

function main() returns ExitCode
	testClear()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: memory.clear-passed-list -->
```maxon
typealias StringList = List with String

function clearList(list StringList)
	list.clear()
end 'clearList'

function main() returns ExitCode
	var list = StringList.create()
	list.append("alpha string!!!!!!!!!!!!!!!")
	list.append("beta string!!!!!!!!!!!!!!!!")
	list.append("gamma string!!!!!!!!!!!!!!!")
	clearList(list)
	print("{list.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: memory.value-survives-clear-and-return -->
### The value survives the clear, in a helper function too
The helper twin of `memory.value-survives-clear`, and it carries the same ruling: `first()` is
corpus-served, so the value it returns is the callee's `+1` and outlives the `clear()` that follows —
across a function boundary as much as inside one.
```maxon
typealias StringList = List with String

function clearList(list StringList) returns String
	let val = try list.first() otherwise "none"
	list.clear()
	return val
end 'clearList'

function main() returns ExitCode
	var list = StringList.create()
	list.append("hello world {40 + 2}!!!!!!!!!!!!!!")
	let result = clearList(list)
	print("{result}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello world 42!!!!!!!!!!!!!!
```

<!-- test: memory.value-survives-clear-error -->
### The value survives a clear one indirection out
The third shape of the same ruling — the mutation happens inside a callee that takes the list as a
parameter. The route to the `clear()` changes nothing, because the safety was established at the `first()`:
`val` is an owned `String`, and no path can free it out from under the print.
```maxon
typealias StringList = List with String

function clearList(list StringList)
	list.clear()
end 'clearList'

function main() returns ExitCode
	var list = StringList.create()
	list.append("hello world {40 + 2}!!!!!!!!!!!!!!")
	let val = try list.first() otherwise "none"
	clearList(list)
	print("{val}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello world 42!!!!!!!!!!!!!!
```

<!-- test: memory.value-survives-clear -->
### The value survives the clear
The value `first()` returns holds its own reference, so the `clear()` cannot reach it. This program and
the two above, which move the `clear()` into a helper, run correctly at exit 0 with no leak — the same
answer `Array`'s `get` + `clear` gives. Each builds its string at run time, because a literal is an
immortal record no `clear()` could free.
```maxon
typealias StringList = List with String

function main() returns ExitCode
	var list = StringList.create()
	list.append("hello world {40 + 2}!!!!!!!!!!!!!!")
	let val = try list.first() otherwise "none"
	list.clear()
	print("{val}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello world 42!!!!!!!!!!!!!!
```

<!-- test: memory.get-in-loop-no-over-release -->
### Repeated get() in a loop does not over-release the walked node
`List.get(i)` inlines `List.walkTo`, whose loop-carried `current` node is both
released inside the inlined body and returned on the exit path. The refcount
inserter must not add a *second* release on the loop's continue/error edges, or
the walked node is double-freed (the self-hosted `rewriteUnresolvedOpsPerFunction`
over-release). Summing every element via `get(i)` in a loop must complete cleanly.
```maxon
typealias N = int(0 to u64.max)
typealias NList = List with N

function walkSum(list NList, n N) returns N
	var sum = 0 as N
	var i = 0 as N
	while i < n 'loop'
		let v = try list.get(i as ElementIndex) otherwise panic("oob")
		sum = sum + v
		i = i + 1
	end 'loop'
	return sum
end 'walkSum'

function main() returns ExitCode
	var list = NList.create()
	var k = 0 as N
	while k < 12 'build'
		list.append(k * 10)
		k = k + 1
	end 'build'
	let s = walkSum(list, n: list.count() as N)
	print("sum={s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
sum=660
```

<!-- test: memory.mutating-the-list-inside-for-in-ends-the-loop -->
### Clearing a list from inside `for … in` ends the walk
⭐ **`l.clear()` UNDER A LIVE WALK COMPILES.** `l` is a `var`, and `clear` is an ordinary mutating method
on it; the loop holds its own reference to the list it walks.

⭐ **IT IS SAFE BECAUSE THE NODES ARE REFCOUNTED.** The cursor holds a `+1` on the node it is parked at,
so `clear()` cannot free it underneath the walk. The observable answer is that the iterator finds no
successor and the loop simply ends: the first element prints, the `clear()` takes effect, and `count()`
is 0. **Exit 0, no leak, no fault.**
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntList = List with Int

function main() returns ExitCode
	var l = IntList.create()
	l.append(1)
	l.append(2)
	l.append(3)
	for x in l 'each'
		print("[{x}]\n")
		l.clear()
	end 'each'
	print("count={l.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[1]
count=0
```

<!-- test: an-all-scalar-struct-element -->
### A `List` element may be a plain struct
⚠⚠ **THIS CASE IS PLAIN COVERAGE, NOT A GUARD.** No gate refuses an all-scalar struct element — the
chain's element gate (`requireListElementType`) admits any managed or word-sized element.

⇒ **It is kept because nothing else covers the shape**, and it is documented as coverage so that a
reader does not mistake it for a guard.

⚠ **The element here owns no heap**, which is deliberately the EASY half — a managed-element struct
walks `element_drop@24` and is covered by the `array-clone-managed-elements` and
`union-managed-payload` families. This case pins the half that has no cleanup at all, where a spurious
drop would be a fault rather than a leak.
```maxon
typealias Int = int(i64.min to i64.max)

type Point
	export var x as Int
	export var y as Int

	static function at(x Int, y Int) returns Self
		return Self { x: x, y: y }
	end 'at'
end 'Point'

typealias PointList = List with Point

function main() returns ExitCode
	var l = PointList.create()
	l.append(Point.at(3, y: 4))
	l.append(Point.at(5, y: 6))
	view p = try l.first() otherwise Point.at(0, y: 0)
	print("[{p.x},{p.y}] count={l.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[3,4] count=2
```

<!-- test: error.a-list-from-head-another-file-keeps-private-is-refused -->
A `List` instance alias at the head of `from […]` is a type name, and another file's file-private one is refused.
```maxon
// --- file: probe.maxon
typealias Readings = List with ExitCode

export function probeReadings() returns ExitCode
	let r = Readings from [1, 2]
	print("{r.count()}")
	return 0
end 'probeReadings'

// --- file: main.maxon
function main() returns ExitCode
	let r = Readings from [3]
	print("{r.count()}")
	return probeReadings()
end 'main'
```
```maxoncstderr
error E3008: <fragment>:13:10: typealias 'Readings' is not exported
```

<!-- test: from-an-array-variable-keeps-order-and-duplicates -->
An alias head takes any array of its element type as the source of `from`; the list keeps the array's order and its repeats.
```maxon
typealias Code = int(0 to 100)
typealias Codes = Array with Code
typealias CodeList = List with Code

function main() returns ExitCode
	var codes = Codes.create()
	codes.push(30)
	codes.push(10)
	codes.push(30)
	let list = CodeList from codes
	print("{list.count()}\n")
	for item in list 'loop'
		print("{item}\n")
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
30
10
30
```

<!-- test: from-a-call-result-infers-the-element -->
A bare `List` head takes its element type from the array a call returns.
```maxon
typealias Code = int(0 to 100)
typealias Codes = Array with Code

function makeCodes() returns Codes
	var codes = Codes.create()
	codes.push(8)
	codes.push(4)
	return codes
end 'makeCodes'

function main() returns ExitCode
	let list = List from makeCodes()
	for item in list 'loop'
		print("{item}\n")
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8
4
```

<!-- test: error.from-a-source-that-is-not-an-array-is-a-type-mismatch -->
A bare `List` head needs an array to take its element type from; any other value is a type mismatch.
```maxon
typealias Code = int(0 to 100)

function main() returns ExitCode
	let n = 3 as Code
	let list = List from n
	return list.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:23: argument type mismatch for 'source': expected 'Array', got 'Code'
```

<!-- test: from-a-string-array-variable-leaves-the-source-intact -->
`List from <array>` copies each `String` out of an array the program still holds.
```maxon
typealias Names = Array with String
typealias NameList = List with String

function main() returns ExitCode
	var names = Names.create()
	names.push("a{2}")
	names.push("b")
	let list = NameList from names
	names.push("c")

	for name in list 'each'
		print("{name}\n")
	end 'each'

	print("{names.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a2
b
3
```

<!-- test: from-a-function-declared-later -->
A bare `List` head over a call to a function declared further down the file.
```maxon
typealias Names = Array with String

function main() returns ExitCode
	let list = List from laterNames()

	for name in list 'each'
		print("{name}\n")
	end 'each'

	return 0
end 'main'

function laterNames() returns Names
	var names = Names.create()
	names.push("p")
	names.push("q")
	return names
end 'laterNames'
```
```exitcode
0
```
```stdout
p
q
```

<!-- test: a-let-list-from-a-string-array-variable -->
A `let` may bind `List from` over a `String` array the program holds in a `var`.
```maxon
typealias Names = Array with String
typealias NameList = List with String

function main() returns ExitCode
	var names = Names.create()
	names.push("a")
	names.push("b")
	let list = NameList from names
	print("{list.count()} {names.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 2
```

<!-- test: error.a-let-list-from-an-array-of-buffers-a-var-still-writes -->
A `let` may not bind `List from` over an array whose elements are byte buffers a `var` still writes: the list holds those same buffers.
```maxon
typealias Octet = int(0 to 255)
typealias Buffer = Array with Octet
typealias Buffers = Array with Buffer
typealias BufferList = List with Buffer

function main() returns ExitCode
	var buffer = Buffer.create()
	buffer.push(1)
	var buffers = Buffers.create()
	buffers.push(buffer)
	let list = BufferList from buffers
	buffer.push(2)
	return list.count()
end 'main'
```
```maxoncstderr
error E3188: <fragment>:12:6: cannot bind `let` 'list' to a value reached through 'buffer', which something may still write; use `view` to read it, `var` to write it, or bind a clone()
```

<!-- test: a-let-list-from-strings-a-var-string-appends-to-afterwards -->
A `String` is a value: appending to the `var` a list's member was copied from extends that `var` alone, so the `let` list holds nothing that is still written.
```maxon
typealias Names = Array with String
typealias NameList = List with String

function main() returns ExitCode
	var name = "a{1}"
	var names = Names.create()
	names.push(name)
	let list = NameList from names
	name.append("z")

	for item in list 'each'
		print("{item}\n")
	end 'each'

	print("{name} {names.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a1
a1z 1
```
