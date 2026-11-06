---
feature: stack-promotion
status: experimental
keywords: [stack, allocation, escape-analysis, optimization]
category: optimization
---

# Stack Promotion for Structs

Tests of the escape analysis that promotes eligible structs to stack allocation. Each case pins its
exit code; `specs/stack-promotion-escape.md` pins that an escaping struct is not promoted.

## Tests

<!-- test: stack-local-primitive-struct -->
Simple struct with all-primitive fields, used only locally. Must produce NO mm_alloc for Point.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let p = Point.create(10, y: 20)
	return p.x + p.y
end 'main'
```
```exitcode
30
```

<!-- test: stack-local-field-mutation -->
Stack-promoted struct with field mutation. No heap allocation.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	var p = Point.create(1, y: 2)
	p.x = 100
	return p.x
end 'main'
```
```exitcode
100
```

<!-- test: stack-when-aliased -->
Aliasing (`var b = a`) is safe for stack structs when neither alias escapes.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(1, y: 2)
	let b = a
	return b.x
end 'main'
```
```exitcode
1
```

<!-- test: stack-when-passed-to-readonly-function -->
Struct passed to a function that only reads it remains stack-allocated.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function readX(p Point) returns Integer
	return p.x
end 'readX'

function main() returns ExitCode
	let p = Point.create(42, y: 0)
	return readX(p)
end 'main'
```
```exitcode
42
```

<!-- test: heap-when-stored-in-container -->
Struct pushed into a container must remain heap-allocated (the container stores the pointer).
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
	var arr = ItemArray.create(0)
	let item = Item.create(7)
	arr.push(item)
	view got = try arr.get(0) otherwise Item.create(0)
	return got.value
end 'main'
```
```exitcode
7
```

<!-- test: heap-when-returned -->
Struct returned from function must remain heap-allocated.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function makePoint(x Integer, y Integer) returns Point
	return Point.create(x, y: y)
end 'makePoint'

function main() returns ExitCode
	let p = makePoint(5, y: 10)
	return p.x
end 'main'
```
```exitcode
5
```

<!-- test: a-var-handed-to-a-mutable-value-parameter-costs-no-allocation -->
<!-- unsupported-targets: wasm32-wasi -->
`bump` reassigns its parameter, so `count` lives in a parameter cell. The cell never outlives the call and nothing captures it, so it is frame storage: the thread makes no allocation at all.

```maxon
typealias Count = int(0 to 1000)

function bump(count mutable Count) returns Count
	count = count + 1

	return count
end 'bump'

function main() returns ExitCode
	let allocsBefore = HeapStats.threadAllocations()
	let freesBefore = HeapStats.threadFrees()
	var count = 1 as Count
	let first = bump(count)
	let second = bump(count)
	let allocs = HeapStats.threadAllocations() - allocsBefore
	let frees = HeapStats.threadFrees() - freesBefore

	print("{first} {second} {count} {allocs} {frees}\n")
	return 0
end 'main'
```
```stdout
2 3 3 0 0
```
```exitcode
0
```

<!-- test: a-var-captured-by-a-closure-and-handed-by-reference-keeps-its-heap-cell -->
The closure holds the cell's address after `bump` returns, so the cell is heap storage: it is allocated and freed, and the counts balance.

```maxon
typealias Count = int(0 to 1000)

function bump(count mutable Count) returns Count
	count = count + 1

	return count
end 'bump'

function main() returns ExitCode
	let liveBefore = HeapStats.allocationsLive()
	var count = 1 as Count
	let first = bump(count)
	let read = function() gives count
	let second = read()
	let live = HeapStats.allocationsLive() - liveBefore

	print("{first} {second} {live > 0}\n")
	return 0
end 'main'
```
```stdout
2 2 true
```
```exitcode
0
```

<!-- test: a-cell-lent-to-a-coroutine-stays-on-the-heap-until-it-is-drained -->
`async` hands the coroutine the caller's own cell for a `mutable` parameter and runs on frames that grow by relocation, so a program with a coroutine keeps every parameter cell on the heap: the one live allocation after the await is the caller's `count`, which holds the coroutine's write.

```maxon
typealias Count = int(0 to 1000)

function bump(count mutable Count) returns Count
	Scheduler.yield()
	count = count + 1

	return count
end 'bump'

function main() returns ExitCode
	let liveBefore = HeapStats.allocationsLive()
	var count = 1 as Count
	let pending = async bump(count)
	let result = await pending
	let live = HeapStats.allocationsLive() - liveBefore

	print("{result} {count} {live}\n")
	return 0
end 'main'
```
```stdout
2 2 1
```
```exitcode
0
```
