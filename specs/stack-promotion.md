---
feature: stack-promotion
status: experimental
keywords: [stack, allocation, escape-analysis, optimization]
category: optimization
---

# Stack Promotion for Structs

Tests verifying that the escape analysis correctly promotes eligible structs to stack allocation.
Uses `MmTrace: true` to verify that promoted structs produce NO heap allocation trace lines,
while escaped structs still produce normal heap allocation traces.

## Tests

<!-- test: stack-local-primitive-struct -->
<!-- MmTrace -->
Simple struct with all-primitive fields, used only locally. Must produce NO mm_alloc for Point.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let p = Point.create(x: 10, y: 20)
	return p.x + p.y
end 'main'
```
```exitcode
30
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Point #1 size=16 [Point.create]
  sl_alloc Point #1 size=48 class=4
mm_incref Point #1 rc=1 [Point.create]
mm_transfer Point #1 rc=1 [Point.create]
mm_decref Point #1 rc=0 [main]
  mm_free Point #1
    sl_free Point #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: stack-local-field-mutation -->
<!-- MmTrace -->
Stack-promoted struct with field mutation. No heap allocation.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	var p = Point.create(x: 1, y: 2)
	p.x = 100
	return p.x
end 'main'
```
```exitcode
100
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Point #1 size=16 [Point.create]
  sl_alloc Point #1 size=48 class=4
mm_incref Point #1 rc=1 [Point.create]
mm_transfer Point #1 rc=1 [Point.create]
mm_decref Point #1 rc=0 [main]
  mm_free Point #1
    sl_free Point #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: stack-when-aliased -->
<!-- MmTrace -->
Aliasing (`var b = a`) is safe for stack structs when neither alias escapes.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(x: 1, y: 2)
	let b = a
	return b.x
end 'main'
```
```exitcode
1
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Point #1 size=16 [Point.create]
  sl_alloc Point #1 size=48 class=4
mm_incref Point #1 rc=1 [Point.create]
mm_transfer Point #1 rc=1 [Point.create]
mm_incref Point #1 rc=2 [main]
mm_decref Point #1 rc=1 [main]
mm_decref Point #1 rc=0 [main]
  mm_free Point #1
    sl_free Point #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: stack-when-passed-to-readonly-function -->
<!-- MmTrace -->
Struct passed to a function that only reads it remains stack-allocated.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function readX(p Point) returns Integer
	return p.x
end 'readX'

function main() returns ExitCode
	let p = Point.create(x: 42, y: 0)
	return readX(p)
end 'main'
```
```exitcode
42
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Point #1 size=16 [Point.create]
  sl_alloc Point #1 size=48 class=4
mm_incref Point #1 rc=1 [Point.create]
mm_transfer Point #1 rc=1 [Point.create]
mm_decref Point #1 rc=0 [main]
  mm_free Point #1
    sl_free Point #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```

<!-- test: heap-when-stored-in-container -->
<!-- MmTrace -->
Struct pushed into a container must remain heap-allocated (the container stores the pointer).
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
	let arr = ItemArray.create()
	let item = Item.create(value: 7)
	arr.push(item)
	let got = try arr.get(0) otherwise Item.create(value: 0)
	return got.value
end 'main'
```
```exitcode
7
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc __ManagedMemory_Item #1 size=32 [ItemArray.create]
  sl_alloc __ManagedMemory_Item #1 size=64 class=5
mm_alloc ItemArray #2 size=16 [ItemArray.create]
  sl_alloc ItemArray #2 size=48 class=4
mm_incref __ManagedMemory_Item #1 rc=1 [ItemArray.create]
mm_incref ItemArray #2 rc=1 [ItemArray.create]
mm_transfer ItemArray #2 rc=1 [ItemArray.create]
mm_alloc Item #3 size=8 [Item.create]
  sl_alloc Item #3 size=40 class=4
mm_incref Item #3 rc=1 [Item.create]
mm_transfer Item #3 rc=1 [Item.create]
mm_realloc __ManagedMemory_Item #1 size=32
  mm_raw_alloc #R1 size=32 [realloc]
    sl_alloc size=32 class=3
mm_incref Item #3 rc=2 [ItemArray.push]
mm_incref Item #3 rc=3 [ItemArray.get]
mm_incref Item #3 rc=4 [main]
mm_decref Item #3 rc=3 [main]
mm_decref Item #3 rc=2 [main]
mm_decref Item #3 rc=1 [main]
mm_decref ItemArray #2 rc=0 [main]
  mm_decref __ManagedMemory_Item #1 rc=0 [~ItemArray]
    mm_decref Item #3 rc=0 [~ManagedElements]
      mm_free Item #3
        sl_free Item #3 size=48 class=4
    mm_raw_free #R1
      sl_free size=32 class=3
    mm_free __ManagedMemory_Item #1
      sl_free __ManagedMemory_Item #1 size=64 class=5
  mm_free ItemArray #2
    sl_free ItemArray #2 size=48 class=4
mm_raw_alloc #R2 size=40
  sl_alloc size=40 class=4
mm_raw_free #R2
  sl_free size=48 class=4
```

<!-- test: heap-when-returned -->
<!-- MmTrace -->
Struct returned from function must remain heap-allocated.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function makePoint(x Integer, y Integer) returns Point
	return Point.create(x: x, y: y)
end 'makePoint'

function main() returns ExitCode
	let p = makePoint(x: 5, y: 10)
	return p.x
end 'main'
```
```exitcode
5
```
```stderr
sl_init
  os_alloc size=67108864
mm_alloc Point #1 size=16 [Point.create]
  sl_alloc Point #1 size=48 class=4
mm_incref Point #1 rc=1 [Point.create]
mm_transfer Point #1 rc=1 [Point.create]
mm_transfer Point #1 rc=1 [stack-promotion.makePoint]
mm_decref Point #1 rc=0 [main]
  mm_free Point #1
    sl_free Point #1 size=48 class=4
mm_raw_alloc #R1 size=40
  sl_alloc size=40 class=4
mm_raw_free #R1
  sl_free size=48 class=4
```
