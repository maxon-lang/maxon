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
	var arr = ItemArray.create()
	let item = Item.create(7)
	arr.push(item)
	let got = try arr.get(0) otherwise Item.create(0)
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
