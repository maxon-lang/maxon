---
feature: challenge-nested-structs
status: stable
keywords: struct, nested, type-size, memory
category: semantics
---
# Challenge Nested Structs

## Documentation

## Nested Structs

Structs can contain other structs as fields. The compiler must correctly compute sizes and offsets for nested struct access.

## Tests

<!-- test: nested-struct-simple -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Inner
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Inner'

type Outer
	export var inner Inner
	export var z Integer

	static function create(inner Inner, z Integer) returns Self
		return Self{inner: inner, z: z}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let inner = Inner.create(x: 10, y: 20)
	let outer = Outer.create(inner: inner, z: 30)
	return outer.inner.x + outer.inner.y + outer.z
end 'main'
```
```exitcode
60
```

<!-- test: nested-struct-returned -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Inner
	export var value Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Inner'

type Outer
	export var inner Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function makeOuter() returns Outer
	let i = Inner.create(value: 42)
	return Outer.create(inner: i)
end 'makeOuter'

function main() returns ExitCode
	let o = makeOuter()
	return o.inner.value
end 'main'
```
```exitcode
42
```

<!-- test: deeply-nested-struct -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Level1
	export var value Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Level1'

type Level2
	export var inner Level1

	static function create(inner Level1) returns Self
		return Self{inner: inner}
	end 'create'
end 'Level2'

type Level3
	export var inner Level2

	static function create(inner Level2) returns Self
		return Self{inner: inner}
	end 'create'
end 'Level3'

function main() returns ExitCode
	let l1 = Level1.create(value: 42)
	let l2 = Level2.create(inner: l1)
	let l3 = Level3.create(inner: l2)
	return l3.inner.inner.value
end 'main'
```
```exitcode
42
```

<!-- test: struct-with-multiple-nested-fields -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

type Line
	export var start Point
	export var finish Point

	static function create(start Point, finish Point) returns Self
		return Self{start: start, finish: finish}
	end 'create'
end 'Line'

function main() returns ExitCode
	let p1 = Point.create(x: 1, y: 2)
	let p2 = Point.create(x: 10, y: 20)
	let line = Line.create(start: p1, finish: p2)
	return line.start.x + line.start.y + line.finish.x + line.finish.y
end 'main'
```
```exitcode
33
```
