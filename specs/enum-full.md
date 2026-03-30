---
feature: enum-full
status: experimental
keywords: [enum, enumeration, associated values]
category: type-system
---

# Enums

## Documentation

# Enums

Enums define a type with a fixed set of named variants called cases. Maxon enums support simple enums and enums with associated values.

### Simple Enums

The simplest form of enum defines named cases with no additional data:

```maxon
enum Direction
	north
	south
	east
	west
end 'Direction'
```

Create enum values using dot notation:

```maxon
var dir = Direction.north
```

### Associated Values

Cases can carry additional data called associated values:

```maxon
typealias ID = int(i64.min to i64.max)

enum Result
	success(value ID)
	failure(code ID, message String)
	pending
end 'Result'
```

Contype cases with associated values:

```maxon
var r1 = Result.success(42)
var r2 = Result.failure(404, "Not found")
var r3 = Result.pending
```

### Pattern Matching with Value Extraction

Use `match` statements to extract associated values from enum cases:

```maxon
match result 'handle'
	success(value) then return value
	failure(code, msg) then print(msg)
	pending then print("waiting...")
end 'handle'
```

Each binding name becomes a local variable within the case body, with the type inferred from the enum case definition.

### Discarding Associated Values

When matching a case with associated values, you can discard them in two ways:

Use `_` to explicitly discard:

```maxon
match container 'check'
	value(_) then return 1
	empty then return 0
end 'check'
```

Or omit the parentheses entirely when you don't need the associated value:

```maxon
match container 'check'
	value then return 1
	empty then return 0
end 'check'
```

Both forms match the case without binding the associated value to a variable.

Match expressions also support value extraction using `gives`:

```maxon
var extracted = match container 'get'
	empty gives 0
	value(n) gives n
end 'get'
```

### Enum Methods

Enums can have methods, similar to structs:

```maxon
enum Direction
	north
	south
	east
	west

	function opposite() returns Direction
		match self 'check'
			north then return Direction.south
			south then return Direction.north
			east then return Direction.west
			west then return Direction.east
		end 'check'
	end 'opposite'

	function isVertical() returns bool
		var result = match self 'check'
			north gives true
			south gives true
			east gives false
			west gives false
		end 'check'
		return result
	end 'isVertical'
end 'Direction'
```

Call methods using instance-dot-method syntax:

```maxon
var dir = Direction.north
var opp = dir.opposite()    // Direction.south
var vert = dir.isVertical() // true
```

## Tests

<!-- test: simple-enum -->
```maxon
enum Direction
	north
	south
	east
	west
end 'Direction'

function main() returns ExitCode
	var dir = Direction.north
	var result = match dir 'check'
		north gives 1
		south gives 0
		east gives 0
		west gives 0
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: enum-assignment -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	var c = Color.red
	c = Color.blue
	var result = match c 'check'
		red gives 0
		green gives 0
		blue gives 1
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: enum-not-equal -->
```maxon
enum Status
	pending
	active
	done
end 'Status'

function main() returns ExitCode
	var s = Status.pending
	var result = match s 'check'
		active gives 0
		pending gives 1
		done gives 1
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: enum-comparison -->
```maxon
enum Status
	pending
	active
	done
end 'Status'

function main() returns ExitCode
	var s1 = Status.pending
	match s1 'check'
		pending then return 1
		active then return 0
		done then return 0
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: enum-function-param -->
```maxon
enum Status
	on
	off
end 'Status'

function isOn(s Status) returns bool
	var result = match s 'check'
		on gives true
		off gives false
	end 'check'
	return result
end 'isOn'

function main() returns ExitCode
	var status = Status.on
	if isOn(status) 'test'
		return 1
	end 'test'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-return-type -->
```maxon
enum Result
	success
	failure
end 'Result'

function getResult(succeed bool) returns Result
	if succeed 'check'
		return Result.success
	end 'check'
	return Result.failure
end 'getResult'

function main() returns ExitCode
	var r = getResult(true)
	var result = match r 'handle'
		success gives 1
		failure gives 0
	end 'handle'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: associated-value-construction -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Container
	empty
	value(n Integer)
end 'Container'

function main() returns ExitCode
	var c = Container.value(42)
	match c 'check'
		value(_) then return 1
		empty then return 0
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: associated-value-function-param -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Container
		empty
		value(n Integer)
end 'Container'

function process(c Container) returns Integer
		match c 'handle'
				empty then return 0
				value(n) then return n
		end 'handle'
end 'process'

function main() returns ExitCode
		var c = Container.value(42)
		return process(c)
end 'main'
```
```exitcode
42
```

<!-- test: associated-value-function-param-empty -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Container
		empty
		value(n Integer)
end 'Container'

function process(c Container) returns Integer
		match c 'handle'
				empty then return 0
				value(n) then return n
		end 'handle'
end 'process'

function main() returns ExitCode
		var c = Container.empty
		return process(c)
end 'main'
```
```exitcode
0
```

<!-- test: associated-value-function-return -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Result
		success(value Integer)
		failure(code Integer)
end 'Result'

function getResult(succeed bool) returns Result
		if succeed 'check'
				return Result.success(42)
		end 'check'
		return Result.failure(99)
end 'getResult'

function main() returns ExitCode
		var r = getResult(true)
		match r 'handle'
				success(v) then return v
				failure(c) then return c
		end 'handle'
end 'main'
```
```exitcode
42
```

<!-- test: associated-value-function-param-multi -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum TwoParts
		none
		values(a Integer, b Integer)
end 'TwoParts'

function sum(p TwoParts) returns Integer
		match p 'handle'
				none then return 0
				values(a, b) then return a + b
		end 'handle'
end 'sum'

function main() returns ExitCode
		var p = TwoParts.values(10, 20)
		return sum(p)
end 'main'
```
```exitcode
30
```

<!-- test: associated-value-array-push -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Item
		empty
		value(n Integer)
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
		var items = ItemArray{}
		items.push(Item.value(10))
		items.push(Item.value(20))
		items.push(Item.empty)
		var first = try items.get(0) otherwise Item.empty
		match first 'check'
				empty then return 0
				value(n) then return n
		end 'check'
end 'main'
```
```exitcode
10
```

<!-- test: associated-value-for-iterator -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Item
		empty
		value(n Integer)
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
		var items = ItemArray{}
		items.push(Item.value(10))
		items.push(Item.value(20))
		items.push(Item.value(12))
		var total = 0
		for item in items 'loop'
				match item 'add'
						empty then break
						value(n) then total = total + n
				end 'add'
		end 'loop'
		return total
end 'main'
```
```exitcode
42
```

<!-- test: associated-value-for-iterator-mixed -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Slot
		none
		val(n Integer)
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
		var slots = SlotArray{}
		slots.push(Slot.val(5))
		slots.push(Slot.none)
		slots.push(Slot.val(3))
		var total = 0
		for e in slots 'loop'
				match e 'check'
						none then total = total + 100
						val(n) then total = total + n
				end 'check'
		end 'loop'
		return total
end 'main'
```
```exitcode
108
```

<!-- test: associated-value-for-iterator-single -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Box
		empty
		full(n Integer)
end 'Box'

typealias BoxArray = Array with Box

function main() returns ExitCode
		var boxes = BoxArray{}
		boxes.push(Box.full(42))
		var result = 0
		for b in boxes 'loop'
				match b 'check'
						empty then result = 0
						full(n) then result = n
				end 'check'
		end 'loop'
		return result
end 'main'
```
```exitcode
42
```

<!-- test: enum-method -->
```maxon
enum Direction
	north
	south

	function isNorth() returns bool
		var result = match self 'check'
			north gives true
			south gives false
		end 'check'
		return result
	end 'isNorth'
end 'Direction'

function main() returns ExitCode
	let d = Direction.north
	if d.isNorth() 'test'
		return 1
	end 'test'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: enum-method-returns-enum -->
```maxon
enum Toggle
	on
	off

	function flip() returns Toggle
		var result = match self 'check'
			on gives Toggle.off
			off gives Toggle.on
		end 'check'
		return result
	end 'flip'
end 'Toggle'

function main() returns ExitCode
	let t = Toggle.on
	let flipped = t.flip()
	var result = match flipped 'check'
		off gives 1
		on gives 0
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: error.duplicate-case -->
```maxon
enum Color
	red
	red
end 'Color'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3030: specs/fragments/enum-full/error.duplicate-case.test:4:2: duplicate enum case: 'red'
```

<!-- test: error.unknown-enum-case -->
```maxon
enum Color
	red
	blue
end 'Color'

function main() returns ExitCode
	let _c = Color.green
	return 0
end 'main'
```
```maxoncstderr
error E3034: specs/fragments/enum-full/error.unknown-enum-case.test:8:11: unknown enum case: 'green'
```

<!-- test: error.associated-value-wrong-count -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Result
	success(value Integer)
	failure
end 'Result'

function main() returns ExitCode
	let _r = Result.success(1, 2)
	return 0
end 'main'
```
```maxoncstderr
error E3036: specs/fragments/enum-full/error.associated-value-wrong-count.test:11:11: wrong argument count: 'expected 1, got 2'
```

<!-- test: error.associated-value-type-mismatch -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Container
	value(n Integer)
end 'Container'

function main() returns ExitCode
	let _c = Container.value("hello")
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/enum-full/error.associated-value-type-mismatch.test:10:34: type mismatch: 'expected Integer, got String'
```

<!-- test: match-enum-binding-simple -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Container
	empty
	value(n Integer)
end 'Container'

function main() returns ExitCode
	var c = Container.value(42)
	match c 'extract'
		empty then return 0
		value(n) then return n
	end 'extract'
end 'main'
```
```exitcode
42
```

<!-- test: match-enum-binding-multiple -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Result
	success(value Integer)
	failure(code Integer)
end 'Result'

function main() returns ExitCode
	var r = Result.failure(99)
	match r 'handle'
		success(v) then return v
		failure(c) then return c
	end 'handle'
end 'main'
```
```exitcode
99
```

<!-- test: match-expr-enum-binding -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Container
	empty
	value(n Integer)
end 'Container'

function main() returns ExitCode
	var c = Container.value(10)
	var result = match c 'get'
		empty gives 0
		value(n) gives n * 2
	end 'get'
	return result
end 'main'
```
```exitcode
20
```

<!-- test: match-enum-no-binding -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Container
	empty
	value(n Integer)
end 'Container'

function main() returns ExitCode
	var c = Container.empty
	match c 'check'
		empty then return 1
		value(n) then return n
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: error.match-enum-wrong-binding-count -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Container
	value(n Integer)
end 'Container'

function main() returns ExitCode
	var c = Container.value(42)
	match c 'extract'
		value(a, b) then return a
	end 'extract'
end 'main'
```
```maxoncstderr
error E3035: specs/fragments/enum-full/error.match-enum-wrong-binding-count.test:12:3: wrong binding count: 'value'
```

<!-- test: error.match-enum-unknown-case -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Container
	empty
	value(n Integer)
end 'Container'

function main() returns ExitCode
	var c = Container.value(42)
	match c 'extract'
		unknown(x) then return x
	end 'extract'
end 'main'
```
```maxoncstderr
error E3034: specs/fragments/enum-full/error.match-enum-unknown-case.test:13:3: unknown enum case: 'unknown'
```

<!-- test: implicit-string-backed -->
```maxon
enum StringBacked
	"North"
	"South"
	"East"
end 'StringBacked'

function main() returns ExitCode
	var dir = StringBacked.North
	var result = match dir 'check'
		North gives 1
		South gives 0
		East gives 0
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: implicit-char-backed -->
```maxon
enum CharBacked
	'N'
	'S'
	'E'
end 'CharBacked'

function main() returns ExitCode
	var dir = CharBacked.N
	var result = match dir 'check'
		N gives 1
		S gives 0
		E gives 0
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: explicit-char-backed -->
```maxon
enum Direction
	North = 'n'
	South = 's'
	East = 'e'
end 'Direction'

function main() returns ExitCode
	var d = Direction.North
	var result = match d 'check'
		North gives 1
		South gives 0
		East gives 0
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: float-backed -->
```maxon
enum FloatBacked
	North = 1.1
	South = 2.2
	East = 3.3
end 'FloatBacked'

function main() returns ExitCode
	var f = FloatBacked.North
	var result = match f 'check'
		North gives 1
		South gives 0
		East gives 0
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: error.duplicate-raw-value -->
```maxon
enum Status
	ok = 200
	success = 200
end 'Status'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3031: specs/fragments/enum-full/error.duplicate-raw-value.test:4:2: duplicate raw value: '200'
```

<!-- test: error.raw-value-type-mismatch -->
```maxon
enum Status
	ok = 100
	fail = "error"
end 'Status'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3032: specs/fragments/enum-full/error.raw-value-type-mismatch.test:4:2: raw value type mismatch: 'expected int, got String'
```

<!-- test: error.mixed-backing-types -->
```maxon
enum Mixed
	first = 1
	second = "two"
end 'Mixed'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3032: specs/fragments/enum-full/error.mixed-backing-types.test:4:2: raw value type mismatch: 'expected int, got String'
```

<!-- test: fromName-associated-compile-time -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Container
	empty
	value(n Integer)
end 'Container'

function main() returns ExitCode
	var c = try Container.fromName("value", 42) otherwise Container.empty
	match c 'check'
		value(n) then return n
		empty then return 0
	end 'check'
end 'main'
```
```exitcode
42
```

<!-- test: fromName-associated-empty-case -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Container
	empty
	value(n Integer)
end 'Container'

function main() returns ExitCode
	var c = try Container.fromName("empty") otherwise Container.value(99)
	match c 'check'
		empty then return 1
		value(_) then return 0
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: error.fromName-invalid-case -->
```maxon
enum Direction
	north
	south
end 'Direction'

function main() returns ExitCode
	let _d = try Direction.fromName("invalid_case_name_that_does_not_exist") otherwise Direction.north
	return 0
end 'main'
```
```maxoncstderr
error E3034: specs/fragments/enum-full/error.fromName-invalid-case.test:8:25: no enum case named 'invalid_case_name_that_does_not_exist': 'Direction'
```

<!-- test: error.fromName-wrong-arg-count -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Container
	value(n Integer)
end 'Container'

function main() returns ExitCode
	let _c = try Container.fromName("value") otherwise Container.value(0)
	return 0
end 'main'
```
```maxoncstderr
error E3036: specs/fragments/enum-full/error.fromName-wrong-arg-count.test:10:25: wrong argument count: 'case 'value' requires 1 associated value(s)'
```

<!-- test: fromName-associated-runtime-empty -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Container
	empty
	value(n Integer)
end 'Container'

function getName() returns String
	return "empty"
end 'getName'

function main() returns ExitCode
	var name = getName()
	var c = try Container.fromName(name) otherwise Container.value(99)
	match c 'check'
		empty then return 1
		value(_) then return 0
	end 'check'
end 'main'
```
```exitcode
1
```

<!-- test: error.fromRawValue-associated-values -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum Container
	empty
	value(n Integer)
end 'Container'

function main() returns ExitCode
	let _c = try Container.fromRawValue(0) otherwise Container.empty
	return 0
end 'main'
```
```maxoncstderr
error E3034: specs/fragments/enum-full/error.fromRawValue-associated-values.test:11:15: unknown enum case: 'fromRawValue'
```

<!-- test: enum-member-constant -->
```maxon
enum Color
	Red
	Green
	Blue
end 'Color'

let DEFAULT_COLOR = Color.Green

function main() returns ExitCode
	match DEFAULT_COLOR 'check'
		Red then return 1
		Green then return 2
		Blue then return 3
	end 'check'
end 'main'
```
```exitcode
2
```

<!-- test: match-enum-binding-string -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum StringResult
	ok(value Integer)
	err(message String)
end 'StringResult'

function main() returns ExitCode
	var r = StringResult.err("bad")
	match r 'handle'
		ok(v) then return v
		err(msg) then return msg.byteLength()
	end 'handle'
end 'main'
```
```exitcode
3
```

<!-- test: match-enum-binding-struct -->
```maxon

typealias Integer = int(i64.min to i64.max)

type EnumPoint
	export var x Integer
	export var y Integer
end 'EnumPoint'

enum Shape
	circle(radius Integer)
	rect(origin EnumPoint)
end 'Shape'

function main() returns ExitCode
	var s = Shape.rect(EnumPoint{x: 10, y: 20})
	match s 'handle'
		circle(r) then return r
		rect(p) then return p.x + p.y
	end 'handle'
end 'main'
```
```exitcode
30
```
