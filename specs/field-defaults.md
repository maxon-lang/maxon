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

A field with a default carries no `as Type` annotation: its type is read off the
default, which takes one of these shapes:

- a `true` or `false` literal (`bool`), or a string literal (`String`);
- `Type.member` or `Type.member(...)` — an enum case or a static factory — typed
  `Type`, and the default is checked against `Type`;
- an expression whose outermost node is a cast, `expr as Type`, typed `Type`.

A bare number has no type of its own, so a numeric default is cast: `0 as Tally`.
`1 + 2 as Tally` is `1 + (2 as Tally)`, whose outermost node is the addition, so it
is not a cast default. Any other default is refused (E2004). An `as Type` written
before the `=` is refused too (E2010): a field with a default is written `var x = e`.

```text
typealias Tally = int(0 to 1000)
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Counter
	export var count = 0 as Tally
	var enabled = true
	var name = "default"
	var level = Priority.low
	export var items = IntArray.create()
end 'Counter'
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
	export var items = IntArray.create()

	static function create() returns Self
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
	static function create() returns Self
		return Self{}
	end 'create'

	export var items = IntArray.create()
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
	export var items = IntArray.create()

	static function createWith(items IntArray) returns Self
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
	export var items = IntArray.create()

	static function create() returns Self
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

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

type Shape
	export var origin = Point.create(3, y: 4)

	static function create() returns Self
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
	export var name = "anon"

	static function create() returns Self
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

<!-- test: field-defaults.cast-literal-default -->
```maxon
typealias Tally = int(0 to 1000)

type Report
	export var lines = 0 as Tally

	static function create() returns Self
		return Self{}
	end 'create'
end 'Report'

function showLines(count Tally)
	print("{count}")
end 'showLines'

function main() returns ExitCode
	let r = Report.create()
	showLines(r.lines)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: field-defaults.cast-constant-default -->
```maxon
typealias SpanId = int(0 to 4095)

let NoSpan = 0 as SpanId

type Mark
	export var span = NoSpan as SpanId

	static function create() returns Self
		return Self{}
	end 'create'
end 'Mark'

function showSpan(id SpanId)
	print("{id}")
end 'showSpan'

function main() returns ExitCode
	let m = Mark.create()
	showSpan(m.span)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: field-defaults.inferred-static-call-default -->
```maxon
typealias Tally = int(0 to 1000)

type Counts
	export var n = 7 as Tally

	static function zero() returns Self
		return Self{}
	end 'zero'
end 'Counts'

type Ledger
	export var total = Counts.zero()

	static function create() returns Self
		return Self{}
	end 'create'
end 'Ledger'

function main() returns ExitCode
	let l = Ledger.create()
	return l.total.n
end 'main'
```
```exitcode
7
```

<!-- test: field-defaults.inferred-enum-case-default -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

type Swatch
	export var shade = Color.green

	static function create() returns Self
		return Self{}
	end 'create'
end 'Swatch'

function main() returns ExitCode
	let s = Swatch.create()
	let name = match s.shade 'shade'
		red gives "red"
		green gives "green"
		blue gives "blue"
	end 'shade'
	print(name)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
green
```

<!-- test: field-defaults.mixed-with-literal-field -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Bag
	export var items = IntArray.create()
	export var total = 0 as Integer

	static function createWithTotal(t Integer) returns Self
		return Self{total: t}
	end 'createWithTotal'
end 'Bag'

function main() returns ExitCode
	var b = Bag.createWithTotal(5)
	b.items.push(10)
	return b.total + (b.items.count() as Integer)
end 'main'
```
```exitcode
6
```

<!-- test: field-defaults.inferred-factory-default -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Bag
	export var items = IntArray.create()

	static function create() returns Self
		return Self{}
	end 'create'
end 'Bag'

function main() returns ExitCode
	let b = Bag.create()
	return b.items.count()
end 'main'
```
```exitcode
0
```

### Error: a field default outside the inferable shapes

<!-- test: field-defaults.error.annotation-with-default -->
```maxon
typealias Tally = int(0 to 1000)

type Report
	export var lines as Tally = 0

	static function create() returns Self
		return Self{}
	end 'create'
end 'Report'

function main() returns ExitCode
	let r = Report.create()
	return r.lines
end 'main'
```
```maxoncstderr
error E2010: specs/field-defaults/field-defaults.error.annotation-with-default.maxon:5:28: Expected newline but got '='
```

<!-- test: field-defaults.error.uncast-number-default -->
```maxon
type Report
	export var lines = 0

	static function create() returns Self
		return Self{}
	end 'create'
end 'Report'

function main() returns ExitCode
	let r = Report.create()
	return r.lines
end 'main'
```
```maxoncstderr
error E2004: specs/field-defaults/field-defaults.error.uncast-number-default.maxon:3:21: Expected default value: a bool or string literal, 'Type.member', 'Type.member(...)', or an expression cast with 'as': 'var name = expr as Type'.
```

<!-- test: field-defaults.error.uncast-float-default -->
```maxon
type Report
	export var ratio = 0.5

	static function create() returns Self
		return Self{}
	end 'create'
end 'Report'

function main() returns ExitCode
	_ = Report.create()
	return 0
end 'main'
```
```maxoncstderr
error E2004: specs/field-defaults/field-defaults.error.uncast-float-default.maxon:3:21: Expected default value: a bool or string literal, 'Type.member', 'Type.member(...)', or an expression cast with 'as': 'var name = expr as Type'.
```

<!-- test: field-defaults.error.cast-not-outermost -->
```maxon
typealias Tally = int(0 to 1000)

type Report
	export var lines = 1 + 2 as Tally

	static function create() returns Self
		return Self{}
	end 'create'
end 'Report'

function main() returns ExitCode
	let r = Report.create()
	return r.lines
end 'main'
```
```maxoncstderr
error E2004: specs/field-defaults/field-defaults.error.cast-not-outermost.maxon:5:21: Expected default value: a bool or string literal, 'Type.member', 'Type.member(...)', or an expression cast with 'as': 'var name = expr as Type'.
```

<!-- test: field-defaults.error.bare-global-default -->
```maxon
typealias SpanId = int(0 to 4095)

let NoSpan = 0 as SpanId

type Mark
	export var span = NoSpan

	static function create() returns Self
		return Self{}
	end 'create'
end 'Mark'

function main() returns ExitCode
	let m = Mark.create()
	return m.span
end 'main'
```
```maxoncstderr
error E2004: specs/field-defaults/field-defaults.error.bare-global-default.maxon:7:20: Expected default value: a bool or string literal, 'Type.member', 'Type.member(...)', or an expression cast with 'as': 'var name = expr as Type'.
```

A `Type.member(...)` default is typed `Type` and checked against it, so a factory that returns another
type is a type mismatch.

<!-- test: field-defaults.error.inferred-head-mismatch -->
```maxon
type Other
	export var ready = true

	static function create() returns Self
		return Self{}
	end 'create'
end 'Other'

type Maker
	export var ready = true

	static function build() returns Other
		return Other.create()
	end 'build'
end 'Maker'

type Holder
	export var made = Maker.build()

	static function create() returns Self
		return Self{}
	end 'create'
end 'Holder'

function main() returns ExitCode
	_ = Holder.create()
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/field-defaults/field-defaults.error.inferred-head-mismatch.maxon:19:20: type mismatch: 'expected Maker, got Other'
```

### Error: A field default must consume everything up to the end of its line

A field default is captured by the same walk a parameter default is, and re-parsed through the same
sub-parse, so a token left over after the expression, as in `var v = 7 as Integer zzz`, is refused rather
than dropped.

<!-- test: field-defaults.error.trailing-tokens -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	var v = 7 as Integer zzz

	static function create() returns Self
		return Self{}
	end 'create'

	function get() returns Integer
		return self.v
	end 'get'
end 'Box'

function main() returns ExitCode
	var b = Box.create()
	return b.get()
end 'main'
```
```maxoncstderr
error E2010: specs/field-defaults/field-defaults.error.trailing-tokens.maxon:5:23: Expected 'end of default value' but got 'zzz'
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
	var items = ElementArray.create()
	var seen = 0 as Count

	static function create() returns Self
		return Self{}
	end 'create'

	function add(item Element)
		items.push(item)
		seen = seen + 1
	end 'add'

	function size() returns Count
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
	var keys = KeyArray.create()
	var values = ValueArray.create()
	var seen = 0 as Count

	static function create() returns Self
		return Self{}
	end 'create'

	function add(key Key, value Value)
		keys.push(key)
		values.push(value)
		seen = seen + 1
	end 'add'

	function size() returns Count
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
descriptor-reading method on it. The receiver is a value of the enclosing type that is not `self`.

<!-- test: field-defaults.descriptor-forwards-from-a-self-literal-local -->
```maxon
typealias Count = int(0 to u64.max)
typealias Integer = int(i64.min to i64.max)

type Basket uses Element
	typealias ElementArray = Array with Element
	var items = ElementArray.create()
	var seen = 0 as Count

	static function create() returns Self
		return Self{}
	end 'create'

	static function of(first Element, second Element) returns Self
		var result = Self{}
		result.add(first)
		result.add(second)
		return result
	end 'of'

	function add(item Element)
		items.push(item)
		seen = seen + 1
	end 'add'

	function size() returns Count
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

### Two type parameters, two independently managed columns

A `Map with (String, int)` is this shape, and it is where the descriptor's single `destroyFunc@40`
was a wild free: both columns were stamped with the FIRST column's destructor, so the trivial column's
words were freed as String records. The program printed the right answer and exited 139.

<!-- test: field-defaults.two-type-params-with-one-managed-column -->
```maxon
typealias Count = int(0 to u64.max)
typealias Integer = int(i64.min to i64.max)

type Pairs uses Key, Value
	typealias KeyArray = Array with Key
	typealias ValueArray = Array with Value
	var keys = KeyArray.create()
	var values = ValueArray.create()

	static function create() returns Self
		return Self{}
	end 'create'

	function add(key Key, value Value)
		keys.push(key)
		values.push(value)
	end 'add'

	function size() returns Count
		return keys.count()
	end 'size'
end 'Pairs'

typealias StrIntPairs = Pairs with (String, Integer)

function main() returns ExitCode
	var p = StrIntPairs.create()
	p.add("alpha", value: 1)
	p.add("beta", value: 2)
	return p.size()
end 'main'
```
```exitcode
2
```

### …and with the MANAGED column second

The mirror image, so the implementation cannot be "always use the first column's destructor" wearing a pass.

<!-- test: field-defaults.two-type-params-with-the-managed-column-second -->
```maxon
typealias Count = int(0 to u64.max)
typealias Integer = int(i64.min to i64.max)

type Pairs uses Key, Value
	typealias KeyArray = Array with Key
	typealias ValueArray = Array with Value
	var keys = KeyArray.create()
	var values = ValueArray.create()

	static function create() returns Self
		return Self{}
	end 'create'

	function add(key Key, value Value)
		keys.push(key)
		values.push(value)
	end 'add'

	function size() returns Count
		return values.count()
	end 'size'
end 'Pairs'

typealias IntStrPairs = Pairs with (Integer, String)

function main() returns ExitCode
	var p = IntStrPairs.create()
	p.add(1, value: "alpha")
	p.add(2, value: "beta")
	p.add(3, value: "gamma")
	return p.size()
end 'main'
```
```exitcode
3
```

### Both columns managed

Two live destructors in one descriptor, one per parameter.

<!-- test: field-defaults.two-type-params-with-both-columns-managed -->
```maxon
typealias Count = int(0 to u64.max)

type Pairs uses Key, Value
	typealias KeyArray = Array with Key
	typealias ValueArray = Array with Value
	var keys = KeyArray.create()
	var values = ValueArray.create()

	static function create() returns Self
		return Self{}
	end 'create'

	function add(key Key, value Value)
		keys.push(key)
		values.push(value)
	end 'add'

	function size() returns Count
		return keys.count() + values.count()
	end 'size'
end 'Pairs'

typealias StrStrPairs = Pairs with (String, String)

function main() returns ExitCode
	var p = StrStrPairs.create()
	p.add("a", value: "alpha")
	p.add("b", value: "beta")
	return p.size()
end 'main'
```
```exitcode
4
```

<!-- test: error.a-redundant-cast-on-an-inferable-field-default-is-unneeded -->
A member-head default already gives the field its type, so a cast to that same type is unneeded.
```maxon
type Gauge
	export var level = 0 as Count

	static function make() returns Self
		return Self{}
	end 'make'
end 'Gauge'

type Panel
	export var gauge = Gauge.make() as Gauge

	static function create() returns Self
		return Self{}
	end 'create'
end 'Panel'

function main() returns ExitCode
	let p = Panel.create()
	return p.gauge.level as ExitCode
end 'main'
```
```maxoncstderr
error E3010: specs/field-defaults/error.a-redundant-cast-on-an-inferable-field-default-is-unneeded.maxon:11:34: unneeded cast: 'Gauge' already fits in 'Gauge'
```

<!-- test: a-field-default-above-i64-max -->
```maxon
typealias Wide = int(0 to u64.max)

type Holder
	export var top = 18446744073709551615 as Wide

	static function create() returns Self
		return Self{}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	print("{h.top}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18446744073709551615
```

<!-- test: error.a-field-default-above-i64-max-into-a-signed-alias -->
```maxon
typealias Offset = int(i64.min to i64.max)

type Holder
	export var top = 9223372036854775808 as Offset

	static function create() returns Self
		return Self{}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	print("{h.top}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/field-defaults/error.a-field-default-above-i64-max-into-a-signed-alias.maxon:8:10: Value 9223372036854775808 is outside the range of 'Offset' (int(-9223372036854775808 to 9223372036854775807))
```
