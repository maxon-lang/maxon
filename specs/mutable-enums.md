---
feature: mutable-enums
status: experimental
keywords: [enum, mutable, associated values, match, write-through]
category: type-system
---

# Writing through a payload binding

## Documentation

A match binding of a payload is a `let`, so assigning to it is `E2013` (`'value' is a let and cannot be
reassigned`); a new payload is a new case assigned to the union. A `let` forbids reassigning the name, not
writing through it, so a payload that is a record is written in place through its binding, and may be handed
to a callee that writes it, whatever the scrutinee is: a `var` or `let` local, a union-typed parameter, or a
union constructed at the `match`.

## Tests

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
