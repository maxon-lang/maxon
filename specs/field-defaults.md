---
feature: field-defaults
status: experimental
keywords: [field, default, struct, initialization]
category: core
---

# Struct Field Default Expressions

## Documentation

A struct field can declare an arbitrary default expression — not just a literal.
When a struct literal omits that field, the default expression is evaluated and
used as the field's value.

For numeric, boolean, and enum-case defaults, the field's type is inferred from
the literal and can be omitted:

```text
type Counter
	export var count = 0              // inferred as int
	var enabled = true         // inferred as bool
	var level = Priority.low   // inferred as Priority
end 'Counter'
```

For any other default expression (function calls, struct literals, string
interpolations, etc.), the field declaration must include an explicit type
annotation, because the type cannot be inferred from the raw tokens alone:

```text
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Container
	export var items IntArray = IntArray.create()
	var name String = "default"
end 'Container'
```

A default expression is re-evaluated at every struct literal that omits the
field, so each construction gets a fresh value (mirroring how function
parameter defaults work). Literal values in the struct literal always win over
the default.

## Tests

<!-- test: field-defaults.function-call-default -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Bag
	export var items IntArray = IntArray.create()

	export static function create() returns Self
		return Self{}
	end 'create'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	b.items.push(42)
	let v = try b.items.get(0) otherwise 0
	return v
end 'main'
```
```exitcode
42
```

<!-- test: field-defaults.literal-overrides-default -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Bag
	export var items IntArray = IntArray.create()

	export static function createWith(items IntArray) returns Self
		return Self{items: items}
	end 'createWith'
end 'Bag'

function main() returns ExitCode
	var pre = IntArray.create()
	pre.push(7)
	let b = Bag.createWith(pre)
	let v = try b.items.get(0) otherwise 0
	return v
end 'main'
```
```exitcode
7
```

<!-- test: field-defaults.fresh-per-construction -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Bag
	export var items IntArray = IntArray.create()

	export static function create() returns Self
		return Self{}
	end 'create'
end 'Bag'

function main() returns ExitCode
	var a = Bag.create()
	var b = Bag.create()
	a.items.push(1)
	a.items.push(2)
	b.items.push(9)
	return a.items.count() * 10 + b.items.count()
end 'main'
```
```exitcode
21
```

<!-- test: field-defaults.struct-literal-default -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer

	export static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

type Shape
	export var origin Point = Point.create(3, y: 4)

	export static function create() returns Self
		return Self{}
	end 'create'
end 'Shape'

function main() returns ExitCode
	let s = Shape.create()
	return s.origin.x + s.origin.y
end 'main'
```
```exitcode
7
```

<!-- test: field-defaults.string-default -->
```maxon
type Person
	export var name String = "anon"

	export static function create() returns Self
		return Self{}
	end 'create'
end 'Person'

function main() returns ExitCode
	let p = Person.create()
	print("{p.name}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
anon
```

<!-- test: field-defaults.mixed-with-literal-field -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Bag
	export var items IntArray = IntArray.create()
	export var total = 0

	export static function createWithTotal(t Integer) returns Self
		return Self{total: t}
	end 'createWithTotal'
end 'Bag'

function main() returns ExitCode
	var b = Bag.createWithTotal(5)
	b.items.push(10)
	return b.total + b.items.count()
end 'main'
```
```exitcode
6
```

<!-- test: field-defaults.missing-type-annotation-errors -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Bag
	var items = IntArray.create()

	export static function create() returns Self
		return Self{}
	end 'create'
end 'Bag'

function main() returns ExitCode
	var b = Bag.create()
	return b.items.count()
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/field-defaults/field-defaults.missing-type-annotation-errors.test:6:14: Expected default value: literal (int, float, bool, or enum case). For other expressions, add a type annotation: 'var name Type = expr'.
```
