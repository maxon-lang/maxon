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
	export var items as IntArray = IntArray.create()
	var name as String = "default"
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
	export var items as IntArray = IntArray.create()

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

<!-- test: field-defaults.method-before-field -->

A `Self{}` literal in a method declared *above* the defaulted field must still
initialize the field. Field/method declaration order inside the type body must
not affect which defaults are applied.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Bag
	export static function create() returns Self
		return Self{}
	end 'create'

	export var items as IntArray = IntArray.create()
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
	export var items as IntArray = IntArray.create()

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
	export var items as IntArray = IntArray.create()

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
	export var x as Integer
	export var y as Integer

	export static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

type Shape
	export var origin as Point = Point.create(3, y: 4)

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
	export var name as String = "anon"

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
	export var items as IntArray = IntArray.create()
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

### Error: A field default must consume everything up to the end of its line

A field default is captured by the same walk a parameter default is, and re-parsed through the same
sub-parse, so it inherited the same silent drop: `var v as Integer = 7 zzz` initialized `v` to 7 and
said nothing.

<!-- test: field-defaults.error.trailing-tokens -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	var v as Integer = 7 zzz

	export static function create() returns Self
		return Self{}
	end 'create'

	export function get() returns Integer
		return self.v
	end 'get'
end 'Box'

function main() returns ExitCode
	var b = Box.create()
	return b.get()
end 'main'
```
```maxoncstderr
error E2010: specs/fragments/field-defaults/field-defaults.error.trailing-tokens.test:5:23: Expected 'end of default value' but got 'zzz'
```

### A field default in a GENERIC type reads the layout descriptor

A default expression is compiled to a synthesized nullary function, so a default that constructs a
container over the enclosing type's own type parameter (`Array with Element`) reads the same
per-instance layout descriptor a METHOD doing the same thing reads. Nothing scanned that expression,
so nothing reserved the hidden slot, and lowering aborted the compiler:
`appendOpaqueArrayCreate: opaque 'Array.create()' in '__fieldDefault#Bag#items' but the function
carries no layout descriptor parameter`.

<!-- test: field-defaults.opaque-array-default-in-generic-type -->
```maxon
typealias Count = int(0 to u64.max)

type Bag uses Element
	typealias ElementArray = Array with Element
	var items as ElementArray = ElementArray.create()
	var seen = 0

	export static function create() returns Self
		return Self{}
	end 'create'

	export function add(item Element)
		items.push(item)
		seen = seen + 1
	end 'add'

	export function size() returns Count
		return seen
	end 'size'
end 'Bag'

typealias Integer = int(i64.min to i64.max)
typealias IntBag = Bag with Integer

function main() returns ExitCode
	var b = IntBag.create()
	b.add(4)
	b.add(9)
	return b.size()
end 'main'
```
```exitcode
2
```

### The same default, in a TWO-parameter generic type

`stdlib/Map.maxon` is this shape: `uses Key, Value`, with one defaulted `Array` column per parameter.

<!-- test: field-defaults.opaque-array-default-two-type-params -->
```maxon
typealias Count = int(0 to u64.max)

type Pairs uses Key, Value
	typealias KeyArray = Array with Key
	typealias ValueArray = Array with Value
	var keys as KeyArray = KeyArray.create()
	var values as ValueArray = ValueArray.create()
	var seen = 0

	export static function create() returns Self
		return Self{}
	end 'create'

	export function add(key Key, value Value)
		keys.push(key)
		values.push(value)
		seen = seen + 1
	end 'add'

	export function size() returns Count
		return seen
	end 'size'
end 'Pairs'

typealias Integer = int(i64.min to i64.max)
typealias IntPairs = Pairs with (Integer, Integer)

function main() returns ExitCode
	var p = IntPairs.create()
	p.add(1, value: 5)
	p.add(2, value: 6)
	p.add(3, value: 7)
	return p.size()
end 'main'
```
```exitcode
3
```

### A method call on a `Self{…}` LOCAL forwards the layout descriptor

`stdlib/Map.maxon:58-68` builds `var result = Self{…}` inside a static and then calls a
descriptor-reading method on it. The receiver is a value of the enclosing type that is not `self`,
which used to be refused outright.

<!-- test: field-defaults.descriptor-forwards-from-a-self-literal-local -->
```maxon
typealias Count = int(0 to u64.max)
typealias Integer = int(i64.min to i64.max)

type Basket uses Element
	typealias ElementArray = Array with Element
	var items as ElementArray = ElementArray.create()
	var seen = 0

	export static function create() returns Self
		return Self{}
	end 'create'

	export static function of(first Element, second Element) returns Self
		var result = Self{}
		result.add(first)
		result.add(second)
		return result
	end 'of'

	export function add(item Element)
		items.push(item)
		seen = seen + 1
	end 'add'

	export function size() returns Count
		return seen
	end 'size'
end 'Basket'

typealias IntBasket = Basket with Integer

function main() returns ExitCode
	let b = IntBasket.of(11, second: 22)
	return b.size()
end 'main'
```
```exitcode
2
```
