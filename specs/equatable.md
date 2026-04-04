---
feature: equatable
status: stable
keywords: [equatable, equals, equality, interface, conformance]
category: type-system
---

# Equatable

## Documentation

### Overview

`Equatable` is a standard library interface for types that can be compared for equality. Types conforming to `Equatable` implement an `equals` method that compares two instances of the same type.

**Interface definition:**

```text
export interface Equatable
  function equals(other Self) returns bool
end 'Equatable'
```

### Conformance

Declare conformance with `implements Equatable` and implement `equals`:

```text
type Point implements Equatable
  var x int
  var y int

  function equals(other Point) returns bool
    return x == other.x and y == other.y
  end 'equals'
end 'Point'
```

### Usage

Call `equals` on an instance, passing another instance of the same type:

```text
var a = Point{x: 1, y: 2}
var b = Point{x: 1, y: 2}
var same = a.equals(b)  // true
```

## Tests

<!-- test: basic-equal -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point implements Equatable
	export var x Integer
	export var y Integer

	function equals(other Point) returns bool
		return x == other.x and y == other.y
	end 'equals'

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(x: 1, y: 2)
	let b = Point.create(x: 1, y: 2)
	if a.equals(b) 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: basic-not-equal -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point implements Equatable
	export var x Integer
	export var y Integer

	function equals(other Point) returns bool
		return x == other.x and y == other.y
	end 'equals'

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(x: 1, y: 2)
	let b = Point.create(x: 3, y: 4)
	if a.equals(b) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: single-field -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Wrapper implements Equatable
	export var value Integer

	function equals(other Wrapper) returns bool
		return value == other.value
	end 'equals'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Wrapper'

function main() returns ExitCode
	let a = Wrapper.create(value: 42)
	let b = Wrapper.create(value: 42)
	let c = Wrapper.create(value: 99)
	if a.equals(b) 'eq'
		if a.equals(c) 'neq'
			return 1
		end 'neq'
		return 0
	end 'eq'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: partial-field-match -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point implements Equatable
	export var x Integer
	export var y Integer

	function equals(other Point) returns bool
		return x == other.x and y == other.y
	end 'equals'

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(x: 1, y: 2)
	let b = Point.create(x: 1, y: 99)
	if a.equals(b) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: self-equality -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Box implements Equatable
	export var value Integer

	function equals(other Box) returns bool
		return value == other.value
	end 'equals'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = Box.create(value: 7)
	if a.equals(a) 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: equals-in-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Id implements Equatable
	export var n Integer

	function equals(other Id) returns bool
		return n == other.n
	end 'equals'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Id'

function areEqual(a Id, b Id) returns bool
	return a.equals(b)
end 'areEqual'

function main() returns ExitCode
	let x = Id.create(n: 5)
	let y = Id.create(n: 5)
	let z = Id.create(n: 6)
	if areEqual(x, b: y) 'eq'
		if areEqual(x, b: z) 'neq'
			return 1
		end 'neq'
		return 0
	end 'eq'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: equals-branching -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Token implements Equatable
	export var id Integer

	function equals(other Token) returns bool
		return id == other.id
	end 'equals'

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Token'

function main() returns ExitCode
	let a = Token.create(id: 10)
	let b = Token.create(id: 10)
	let c = Token.create(id: 20)
	var result = 0
	if a.equals(b) 'first'
		result = result + 1
	end 'first'
	if a.equals(c) 'second'
		result = result + 10
	end 'second'
	if b.equals(c) 'third'
		result = result + 100
	end 'third'
	return result
end 'main'
```
```exitcode
1
```
