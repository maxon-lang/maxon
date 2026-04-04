---
feature: types
status: stable
keywords: [type, field, let, var, mutability, instance]
category: type-system
---

# Types

## Documentation

Types define custom data types with named fields.

### Declaration

```maxon
typealias Score = int(i64.min to i64.max)

type Point
	export var x Score
	export var y Score
end 'Point'
```

Fields must use `let` (immutable) or `var` (mutable), and can be `export` for external access:
```maxon
typealias Score = int(i64.min to i64.max)

type Config
	export let version Score    // Cannot be changed after initialization, accessible externally
	export var count Score      // Can be modified, accessible externally
	var internal Score          // Private - only accessible in methods
end 'Config'
```

### Instantiation

Create type instances with literal syntax:
```maxon
var p = Point.create(x: 10, y: 20)
let config = Config.create(version: 1, count: 0)
```

### Instance Mutability

The mutability of a type instance is determined by `let` vs `var`:

**var type** - Can modify `var` fields:
```maxon
var p = Point.create(x: 10, y: 20)
p.x = 30   // OK: type is mutable, field is var
```

**let type** - Cannot modify any fields:
```maxon
let p = Point.create(x: 10, y: 20)
// p.x = 30   // ERROR: type instance is immutable
```

### Field Mutability

Even on a `var` type, `let` fields cannot be modified:
```maxon
var c = Config.create(version: 1, count: 0)
c.count = 5     // OK: field is var
// c.version = 2   // ERROR: field is let
```

## Tests

<!-- test: var-struct-field-assign -->
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
	var p = Point.create(x: 10, y: 20)
	p.x = 30
	return p.x
end 'main'
```
```exitcode
30
```

<!-- test: var-field-assign -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Config
	export let version Integer
	export var count Integer

	static function create(version Integer, count Integer) returns Self
		return Self{version: version, count: count}
	end 'create'
end 'Config'

function main() returns ExitCode
	var c = Config.create(version: 1, count: 0)
	c.count = 5
	return c.count
end 'main'
```
```exitcode
5
```

<!-- test: error.let-struct-field-assign -->
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
	p.x = 30
	return p.x
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/structs/error.let-struct-field-assign.test:16:2: cannot assign to immutable variable: 'p'
```

<!-- test: error.let-field-assign -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Config
	export let version Integer
	export var count Integer

	static function create(version Integer, count Integer) returns Self
		return Self{version: version, count: count}
	end 'create'
end 'Config'

function main() returns ExitCode
	var c = Config.create(version: 1, count: 0)
	c.version = 2
	return c.version
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/structs/error.let-field-assign.test:16:2: cannot assign to field 'Config.version' because it is immutable (declare with 'var' to make it mutable)
```

<!-- test: simple-type -->
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
	let p = Point.create(3, y: 4)
	return p.x + p.y
end 'main'
```
```exitcode
7
```

<!-- test: struct-field-access -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Rect
	export var width Integer
	export var height Integer

	static function create(width Integer, height Integer) returns Self
		return Self{width: width, height: height}
	end 'create'
end 'Rect'

function main() returns ExitCode
	let r = Rect.create(5, height: 10)
	return r.width * r.height
end 'main'
```
```exitcode
50
```

<!-- test: struct-param -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Vec2
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Vec2'

function dot(a Vec2, b Vec2) returns Integer
	return a.x * b.x + a.y * b.y
end 'dot'

function main() returns ExitCode
	let v1 = Vec2.create(3, y: 4)
	let v2 = Vec2.create(2, y: 1)
	return dot(v1, b: v2)
end 'main'
```
```exitcode
10
```

<!-- test: struct-return -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Pair
	export var first Integer
	export var second Integer

	static function create(first Integer, second Integer) returns Self
		return Self{first: first, second: second}
	end 'create'
end 'Pair'

function makePair(a Integer, b Integer) returns Pair
	return Pair.create(first: a, second: b)
end 'makePair'

function main() returns ExitCode
	let p = makePair(5, b: 7)
	return p.first + p.second
end 'main'
```
```exitcode
12
```

<!-- test: struct-literal-as-arg -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function acceptPoint(p Point) returns Integer
	return p.x + p.y
end 'acceptPoint'

function main() returns ExitCode
	return acceptPoint(Point.create(x: 3, y: 4))
end 'main'
```
```exitcode
7
```

<!-- test: struct-field-default -->
```maxon
type Counter
	export var value = 0
	export var step = 1

	static function create() returns Self
		return Self{}
	end 'create'

	static function create(value Count, step Count) returns Self
		return Self{value: value, step: step}
	end 'create'
end 'Counter'

function main() returns ExitCode
	let c1 = Counter.create()
	let c2 = Counter.create(40, step: 1)
	let c3 = Counter.create(10, step: 2)
	return c1.value + c2.value + c3.step
end 'main'
```
```exitcode
42
```

<!-- test: struct-field-inferred-type -->
```maxon
type Settings
	export let maxRetries = 5
	export var timeout = 50.0

	static function create() returns Self
		return Self{}
	end 'create'
end 'Settings'

function main() returns ExitCode
	let s = Settings.create()
	return s.maxRetries + trunc(s.timeout)
end 'main'
```
```exitcode
55
```

