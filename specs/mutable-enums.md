---
feature: mutable-enums
status: experimental
keywords: [enum, mutable, associated values, match, write-back]
category: type-system
---

# Mutable Enum Associated Values

## Documentation

### Mutable Match Bindings

When an enum variable is mutable (`var`), match bindings on associated values are also mutable. Assigning to a match binding writes the new value back to the enum's heap block in-place.

For scalar bindings (integers, booleans, etc.), use direct assignment:

```text
var b = Box.full(10)
match b 'update'
  full(value) then value = 42
  empty then return
end 'update'
```

For heap-pointer bindings (structs, enums with associated values, strings), use direct assignment:

```text
var b = Named.named("hello")
match b 'update'
  named(name) then name = "world"
  anonymous then return
end 'update'
```

When the enum variable is immutable (`let`), a match binding cannot be assigned: the assignment is `E2013`
(`'value' is a let and cannot be reassigned`), because the payload replacement must reach a union that can be
written. A `let` forbids reassigning the name, not writing through it, so a payload that is a record may still be
written in place through its binding, and may be handed to a callee that writes it.

### What counts as a mutable scrutinee

A `var` local is one, and so is a union-typed **parameter**: a parameter is a borrowed reference to the
caller's record, which the callee may already write — `s.append(…)` on a `String` parameter is ordinary
Maxon. A union-typed parameter lets an arm write a field of a payload record in place; assigning the payload
requires the parameter to be `mutable` (`E2013` otherwise).

A TEMPORARY is one too: a union constructed at the `match` has no other holder, so a write through its
payload binding is a write nobody else can see. A `let` local is not one: assigning its payload binding is
`E2013`, though a write through a record payload is legal.

## Tests

<!-- test: basic-mutable -->
Mutate an associated value through a match binding on a `var` enum.
```maxon
typealias Integer = int(i64.min to i64.max)

union Box
	empty
	full(value Integer)
end 'Box'

function main() returns ExitCode
	var b = Box.full(10)
	match b 'update'
		full(value) then value = 42
		empty then return 0
	end 'update'
	match b 'read'
		full(value) then return value
		empty then return 0
	end 'read'
end 'main'
```
```exitcode
42
```

<!-- test: write-back-verify -->
Verify that mutation through match binding persists after the match.
```maxon
typealias Integer = int(i64.min to i64.max)

union Wrapper
	none
	some(n Integer)
end 'Wrapper'

function readValue(w Wrapper) returns Integer
	match w 'r'
		some(n) then return n
		none then return 0
	end 'r'
end 'readValue'

function main() returns ExitCode
	var w = Wrapper.some(5)
	match w 'mutate'
		some(n) then n = 99
		none then return 0
	end 'mutate'
	return readValue(w)
end 'main'
```
```exitcode
99
```

<!-- test: multiple-mutable-fields -->
Multiple mutable fields in one case, mutate one at a time.
```maxon
typealias Integer = int(i64.min to i64.max)

union Pair
	empty
	values(a Integer, b Integer)
end 'Pair'

function main() returns ExitCode
	var p = Pair.values(1, b: 2)
	match p 'update-a'
		values(a, _) then a = 10
		empty then return 0
	end 'update-a'
	match p 'update-b'
		values(_, b) then b = 32
		empty then return 0
	end 'update-b'
	match p 'read'
		values(a, b) then return a + b
		empty then return 0
	end 'read'
end 'main'
```
```exitcode
42
```

<!-- test: mixed-readonly-and-mutable -->
Only assign to some bindings, leave others unchanged.
```maxon
typealias Integer = int(i64.min to i64.max)

union Record
	blank
	data(id Integer, count Integer)
end 'Record'

function main() returns ExitCode
	var e = Record.data(1, count: 0)
	match e 'inc'
		data(id, count) then count = count + id
		blank then return 0
	end 'inc'
	match e 'read'
		data(id, count) then return id + count
		blank then return 0
	end 'read'
end 'main'
```
```exitcode
2
```

<!-- test: mutate-twice -->
Mutate the same associated value twice in sequence.
```maxon
typealias Integer = int(i64.min to i64.max)

union Cell
	empty
	holding(value Integer)
end 'Cell'

function main() returns ExitCode
	var c = Cell.holding(10)
	match c 'first'
		holding(value) then value = 20
		empty then return 0
	end 'first'
	match c 'second'
		holding(value) then value = value + 22
		empty then return 0
	end 'second'
	match c 'read'
		holding(value) then return value
		empty then return 0
	end 'read'
end 'main'
```
```exitcode
42
```

<!-- test: error.assign-to-let-enum-binding -->
Error: assigning to a match binding when the enum variable is `let`.
```maxon
typealias Integer = int(i64.min to i64.max)

union Box
	empty
	full(value Integer)
end 'Box'

function main() returns ExitCode
	let b = Box.full(10)
	match b 'bad'
		full(value) then value = 42
		empty then return 0
	end 'bad'
	return 0
end 'main'
```
```maxoncstderr
error E2013: specs/mutable-enums/error.assign-to-let-enum-binding.maxon:12:20: 'value' is a let and cannot be reassigned
```

<!-- test: mutate-string-payload -->
Mutate a String-typed associated value.
```maxon
union Named
	anonymous
	named(name String)
end 'Named'

function main() returns ExitCode
	var n = Named.named("hello")
	match n 'update'
		named(name) then name = "world"
		anonymous then return 0
	end 'update'
	match n 'read'
		named(name) then return name.byteLength()
		anonymous then return 0
	end 'read'
end 'main'
```
```exitcode
5
```

<!-- test: mutate-struct-payload -->
Mutate a struct-typed associated value.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

union Shape
	nothing
	located(pos Point)
end 'Shape'

function main() returns ExitCode
	var s = Shape.located(Point.create(1, y: 2))
	match s 'update'
		located(pos) then pos = Point.create(10, y: 32)
		nothing then return 0
	end 'update'
	match s 'read'
		located(pos) then return pos.x + pos.y
		nothing then return 0
	end 'read'
end 'main'
```
```exitcode
42
```

<!-- test: mutable-payload-of-a-parameter -->
A union-typed PARAMETER is a mutable scrutinee for a field write, so its payload binding takes one — and the
write lands in the caller's record, which is what the exit code reads back.
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

union Slot
	empty
	holding(c Counter)
end 'Slot'

function bump(s Slot)
	match s 'step'
		holding(c) then c.n = c.n + 32
		empty then return
	end 'step'
end 'bump'

function main() returns ExitCode
	var s = Slot.holding(Counter.create(10))
	bump(s)
	match s 'read'
		holding(c) then return c.n
		empty then return 0
	end 'read'
end 'main'
```
```exitcode
42
```

<!-- test: parameter-payload-to-a-writing-callee -->
The cross-function half of the same rule: the payload is handed to a callee that writes a field of it, which
is the callee's question rather than the arm's own. A parameter scrutinee's payload may fill that position, for a
field write only.
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

union Slot
	empty
	holding(c Counter)
end 'Slot'

function raise(c Counter)
	c.n = c.n + 32
end 'raise'

function bump(s Slot)
	match s 'step'
		holding(c) then raise(c)
		empty then return
	end 'step'
end 'bump'

function main() returns ExitCode
	var s = Slot.holding(Counter.create(10))
	bump(s)
	match s 'read'
		holding(c) then return c.n
		empty then return 0
	end 'read'
end 'main'
```
```exitcode
42
```

<!-- test: a-temporary-scrutinee-payload-is-writable -->
⚖ A union constructed AT the `match` is a temporary this frame owns and nothing else holds, so its payload
binding is writable: the callee below writes `c`, and the arm reads the write back.
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

union Slot
	empty
	holding(c Counter)
end 'Slot'

function raiseAndRead(c Counter) returns Integer
	c.n = c.n + 32
	return c.n
end 'raiseAndRead'

function main() returns ExitCode
	match Slot.holding(Counter.create(10)) 'step'
		holding(c) then return raiseAndRead(c)
		empty then return 0
	end 'step'
end 'main'
```
```exitcode
42
```

<!-- test: error.let-to-a-callee-that-writes-back-a-payload -->
⚖ The write-back is a store into the SCRUTINEE's box, so a callee whose arm assigns a payload of its
`mutable` parameter writes the caller's record — and a `let` may not fill that position. Without this the
parameter ruling would have handed `bump` a licence its caller never granted.
```maxon
typealias Integer = int(i64.min to i64.max)

union Slot
	empty
	holding(n Integer)
end 'Slot'

function bump(s mutable Slot)
	match s 'step'
		holding(n) then n = 42
		empty then return
	end 'step'
end 'bump'

function main() returns ExitCode
	let s = Slot.holding(10)
	bump(s)
	match s 'read'
		holding(n) then return n
		empty then return 0
	end 'read'
end 'main'
```
```maxoncstderr
error E2013: specs/mutable-enums/error.let-to-a-callee-that-writes-back-a-payload.maxon:18:2: 's' is a let and cannot be passed to the `mutable` parameter 's'; bind it to a `var`
```

<!-- test: error.let-to-a-callee-that-writes-back-a-managed-payload -->
The MANAGED twin, which writes the caller's box twice over: the store publishes the new payload and the
displaced one is decrefed out of the `let`'s own record.
```maxon
union Named
	anonymous
	named(name String)
end 'Named'

function rename(n mutable Named)
	match n 'step'
		named(name) then name = "world"
		anonymous then return
	end 'step'
end 'rename'

function main() returns ExitCode
	let n = Named.named("hello")
	rename(n)
	match n 'read'
		named(name) then return name.byteLength()
		anonymous then return 0
	end 'read'
end 'main'
```
```maxoncstderr
error E2013: specs/mutable-enums/error.let-to-a-callee-that-writes-back-a-managed-payload.maxon:16:2: 'n' is a let and cannot be passed to the `mutable` parameter 'n'; bind it to a `var`
```

<!-- test: a-let-scrutinee-payload-record-is-written-by-a-callee -->
⚖ A `let` local is NOT a mutable scrutinee, so its payload cannot be REASSIGNED, but `let` forbids only
reassignment: the payload binding names a record, and a callee that writes the record writes it in place.
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

union Slot
	empty
	holding(c Counter)
end 'Slot'

function raise(c Counter)
	c.n = c.n + 32
end 'raise'

function makeSlot() returns Slot
	return Slot.holding(Counter.create(10))
end 'makeSlot'

function main() returns ExitCode
	let s = makeSlot()
	match s 'step'
		holding(c) then raise(c)
		empty then return 0
	end 'step'
	match s 'read'
		holding(c) then return c.n
		empty then return 0
	end 'read'
end 'main'
```
```exitcode
42
```

<!-- test: a-freshly-built-scalar-payload-union-passed-to-a-let-field-keeper-is-sole -->
A union is a mutable box even when its payload is a scalar. A `let` field forbids reassigning the field, not
writing the box, so it takes a union however the box is held. A union built in the argument itself is the
only reference to its box.

```maxon
typealias Integer = int(i64.min to i64.max)

union Settle
	moved
	someMoved(count Integer)
end 'Settle'

type Holder
	export let settle as Settle

	static function create(settle Settle) returns Self
		return Self{settle: settle}
	end 'create'
end 'Holder'

typealias HolderArray = Array with Holder

type Registry
	export var held as HolderArray

	static function create() returns Self
		return Self{held: HolderArray.create(0)}
	end 'create'

	function keep(settle Settle)
		self.held.push(Holder.create(settle))
	end 'keep'

	function run(count Integer)
		self.keep(Settle.someMoved(count))
		self.keep(Settle.moved)
	end 'run'
end 'Registry'

function main() returns ExitCode
	var registry = Registry.create()
	registry.run(3)
	return registry.held.count() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: a-scalar-payload-union-var-at-its-last-use-passed-to-a-let-field-keeper-is-sole -->
A `var` holding a freshly built union whose last use is the call is moved into the `let` field's holder.

```maxon
typealias Integer = int(i64.min to i64.max)

union Settle
	moved
	someMoved(count Integer)
end 'Settle'

type Holder
	export let settle as Settle

	static function create(settle Settle) returns Self
		return Self{settle: settle}
	end 'create'
end 'Holder'

typealias HolderArray = Array with Holder

type Registry
	export var held as HolderArray

	static function create() returns Self
		return Self{held: HolderArray.create(0)}
	end 'create'

	function keep(settle Settle)
		self.held.push(Holder.create(settle))
	end 'keep'

	function run(count Integer)
		var settle = Settle.someMoved(count)
		self.keep(settle)
	end 'run'
end 'Registry'

function main() returns ExitCode
	var registry = Registry.create()
	registry.run(3)
	return registry.held.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: a-scalar-payload-union-var-read-after-the-call-is-kept-too -->
A `var` union the program reads again after the call has another reader, and the `let` field takes it all the same: the first call shares the box with the later read, the final call is the `var`'s last use and moves it. Both pushes succeed.

```maxon
typealias Integer = int(i64.min to i64.max)

union Settle
	moved
	someMoved(count Integer)
end 'Settle'

type Holder
	export let settle as Settle

	static function create(settle Settle) returns Self
		return Self{settle: settle}
	end 'create'
end 'Holder'

typealias HolderArray = Array with Holder

type Registry
	export var held as HolderArray

	static function create() returns Self
		return Self{held: HolderArray.create(0)}
	end 'create'

	function keep(settle Settle)
		self.held.push(Holder.create(settle))
	end 'keep'

	function run(count Integer)
		var settle = Settle.someMoved(count)
		self.keep(settle)
		self.keep(settle)
	end 'run'
end 'Registry'

function main() returns ExitCode
	var registry = Registry.create()
	registry.run(3)
	return registry.held.count() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: a-scalar-payload-union-also-stored-elsewhere-is-kept-too -->
A union pushed into a collection before the call is held by the collection too, and the `let` field takes it all the same.

```maxon
typealias Integer = int(i64.min to i64.max)

union Settle
	moved
	someMoved(count Integer)
end 'Settle'

typealias SettleArray = Array with Settle

type Holder
	export let settle as Settle

	static function create(settle Settle) returns Self
		return Self{settle: settle}
	end 'create'
end 'Holder'

typealias HolderArray = Array with Holder

type Registry
	export var held as HolderArray

	static function create() returns Self
		return Self{held: HolderArray.create(0)}
	end 'create'

	function keep(settle Settle)
		self.held.push(Holder.create(settle))
	end 'keep'

	function run(count Integer)
		var settle = Settle.someMoved(count)
		var spare = SettleArray.create(0)
		spare.push(settle)
		self.keep(settle)
		spare.clear()
	end 'run'
end 'Registry'

function main() returns ExitCode
	var registry = Registry.create()
	registry.run(3)
	return registry.held.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: a-var-scrutinee-from-a-call-writes-through-its-payload-binding -->
A match binding follows its scrutinee BINDING: a `var` gives writable bindings whatever produced its value, so
a `var` holding a call's result takes the same payload write a `var` holding a constructed union does.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function raise()
		self.n = self.n + 32
	end 'raise'
end 'Counter'

union Slot
	empty
	holding(c Counter)
end 'Slot'

function makeSlot() returns Slot
	return Slot.holding(Counter.create(10))
end 'makeSlot'

function main() returns ExitCode
	var s = makeSlot()
	var seen = 0 as Integer

	match s 'step'
		holding(c) then c.raise()
		empty then seen = 1
	end 'step'

	match s 'read'
		holding(c) then return c.n + seen
		empty then return 0
	end 'read'
end 'main'
```
```exitcode
42
```

<!-- test: a-let-scrutinee-from-a-call-writes-through-its-payload-record -->
The twin: the same call result bound by a `let` gives bindings that cannot be reassigned, but the record a binding names is written in place all the same.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function raise()
		self.n = self.n + 32
	end 'raise'
end 'Counter'

union Slot
	empty
	holding(c Counter)
end 'Slot'

function makeSlot() returns Slot
	return Slot.holding(Counter.create(10))
end 'makeSlot'

function main() returns ExitCode
	let s = makeSlot()
	var seen = 0 as Integer

	match s 'step'
		holding(c) then c.raise()
		empty then seen = 1
	end 'step'

	match s 'read'
		holding(c) then return c.n + seen
		empty then return 0
	end 'read'
end 'main'
```
```exitcode
42
```
