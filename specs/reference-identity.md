---
feature: reference-identity
status: experimental
keywords: [is, is not, reference, identity, pointer, equality]
category: operators
---

# Reference Identity

## Documentation

### Overview

In Maxon, all struct-typed variables are references (heap pointers). The `==` operator compares **contents** (value equality via `Equatable`), while the `is` operator compares **reference identity** — whether two variables point to the same object in memory.

Assignment (`var b = a`) creates a **reference** (alias) — `b` points to the same object as `a`. To create a new independent object, use `var b = a.clone()`. To check if two references point to the same object, use `is`.

### Operators

- `a is b` — returns `true` if `a` and `b` refer to the same object
- `a is not b` — returns `true` if `a` and `b` refer to different objects

### Example

```text
function areSame(a Point, b Point) returns bool
  return a is b
end 'areSame'

var p = Point{x: 1, y: 2}
areSame(p, b: p)  // true  — same reference passed twice
```

### Rules

- `is` and `is not` work on struct-typed values (including String, Array, and user-defined types).
- Using `is` or `is not` on primitive types (int, float, bool, byte) is a compile error — primitives are values, not references.
- Both operands must be the same type.

## Tests

<!-- test: self-identity -->
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
	if a is a 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: assignment-creates-alias -->
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
	if a is b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: clone-creates-new-object -->
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
	let b = a.clone()
	if a is b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: different-objects -->
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
	let b = Point.create(x: 1, y: 2)
	if a is b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: is-not-operator -->
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
	let b = Point.create(x: 1, y: 2)
	if a is not b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: is-not-self -->
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
	if a is not a 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: function-same-arg -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function areSame(a Box, b Box) returns bool
	return a is b
end 'areSame'

function main() returns ExitCode
	let x = Box.create(value: 42)
	let y = Box.create(value: 42)
	var result = 0
	if areSame(x, b: x) 'same'
		result = result + 1
	end 'same'
	if areSame(x, b: y) 'diff'
		result = result + 10
	end 'diff'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: string-identity -->
```maxon
function main() returns ExitCode
	let a = "hello"
	let b = "hello"
	var result = 0
	if a is a 'self'
		result = result + 1
	end 'self'
	if a is b 'diff'
		result = result + 10
	end 'diff'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: mutation-through-alias -->
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
	var a = Point.create(x: 1, y: 2)
	a.x = 1
	var b = a
	b.x = 99
	return a.x
end 'main'
```
```exitcode
99
```

<!-- test: clone-isolates-mutation -->
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
	var b = a.clone()
	b.x = 99
	return a.x
end 'main'
```
```exitcode
1
```

<!-- test: primitive-error -->
```maxon
function main() returns ExitCode
	let a = 42
	let b = 42
	if a is b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
error E3068: specs/fragments/reference-identity/primitive-error.test:5:7: 'is' requires reference types (structs), not primitive values
```
