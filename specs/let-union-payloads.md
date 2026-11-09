---
feature: let-union-payloads
status: experimental
keywords: [union, enum, payload, let, immutable, match, write-back, let field]
category: memory
---

# `let` payloads of a union

## Documentation

A case of an `enum` or `union` may declare a payload with `let`, exactly as a record declares a `let` field:

```text
union Op
	call(result Integer, let callee ByteArray)
	nop
end 'Op'
```

The payload is never reassigned for the whole life of the value:

- A `match` binding of a `let` payload is a `let`: assigning to the binding, which would write back into the union, is
  `E2013`. A field store, a receiver-writing method or a call that writes its parameter writes the record the
  union holds.
- The value stored into it at construction may be a `var` that is read again: the payload and the `var` name one
  record. A `var` at its last use moves in.
- A read of the payload may be bound to a `var`.

A payload takes a capability word as a record field does (`let callee immutable ByteArray`), and an immutable value
such as a constant byte string is stored only into a payload declared `immutable` (`E3196`).

A payload declared without `let` is writable through its binding under a `var` scrutinee.

## Tests

<!-- test: a-read-only-value-stored-into-a-let-payload -->
The immutable `source.title` is stored into the `let` payload `callee`, which is declared `immutable` to take it.

```maxon
typealias Integer = int(i64.min to i64.max)

union Op
	call(let callee immutable ByteArray, weight Integer)
	nop
end 'Op'

type Source
	export let title as immutable ByteArray

	static function create() returns Self
		return Self{title: b"title"}
	end 'create'
end 'Source'

function size(op Op) returns Integer
	return match op 'shape'
		call(callee, _) gives callee.count() as Integer
		nop gives 0
	end 'shape'
end 'size'

function main() returns ExitCode
	let source = Source.create()
	let op = Op.call(source.title, weight: 1)
	return size(op) as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: a-var-at-its-last-use-moves-into-a-let-payload -->
`name` is a `var` that nothing reads after the construction, so it moves into the payload.

```maxon
typealias Integer = int(i64.min to i64.max)

union Op
	call(let callee ByteArray, weight Integer)
	nop
end 'Op'

function size(op Op) returns Integer
	return match op 'shape'
		call(callee, _) gives callee.count() as Integer
		nop gives 0
	end 'shape'
end 'size'

function main() returns ExitCode
	var name = ByteArray.create(0)
	name.push(65)
	name.push(66)
	let op = Op.call(name, weight: 1)
	return size(op) as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: a-still-shared-var-stored-into-a-let-payload -->
`name` is read again after the construction, and the `let` payload names the same record.

```maxon
typealias Integer = int(i64.min to i64.max)

union Op
	call(let callee ByteArray, weight Integer)
	nop
end 'Op'

function main() returns ExitCode
	var name = ByteArray.create(0)
	name.push(65)
	let op = Op.call(name, weight: 1)
	name.push(66)

	match op 'shape'
		call(_, weight) then return weight as ExitCode
		nop then return 0
	end 'shape'
end 'main'
```
```exitcode
1
```

<!-- test: a-field-write-through-the-binding-of-a-let-payload -->
`cell` is bound out of a `let` payload; the payload forbids only reassigning, so a field write through `cell` writes the record the union holds.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var level as Integer

	static function create() returns Self
		return Self{level: 0}
	end 'create'
end 'Cell'

union Holder
	held(let cell Cell)
	empty
end 'Holder'

function main() returns ExitCode
	var holder = Holder.held(Cell.create())

	match holder 'write'
		held(cell) then cell.level = 5
		empty then return 1
	end 'write'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-writing-method-on-the-binding-of-a-let-payload -->
`items.push` writes the record bound out of the `let` payload.

```maxon
union Holder
	held(let items ByteArray)
	empty
end 'Holder'

function main() returns ExitCode
	var holder = Holder.held(ByteArray.create(0))

	match holder 'write'
		held(items) then items.push(1)
		empty then return 1
	end 'write'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.assigning-to-the-binding-of-a-let-payload -->
Assigning to the binding of a payload reassigns a `let`.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var level as Integer

	static function create() returns Self
		return Self{level: 0}
	end 'create'
end 'Cell'

union Holder
	held(let cell Cell)
	empty
end 'Holder'

function main() returns ExitCode
	var holder = Holder.held(Cell.create())

	match holder 'write'
		held(cell) then cell = Cell.create()
		empty then return 1
	end 'write'

	return 0
end 'main'
```
```maxoncstderr
error E2013: specs/let-union-payloads/error.assigning-to-the-binding-of-a-let-payload.maxon:21:19: 'cell' is a let and cannot be reassigned
```

<!-- test: a-let-payload-read-into-a-var -->
A `var` may name the record a `let` payload holds.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var level as Integer

	static function create() returns Self
		return Self{level: 0}
	end 'create'
end 'Cell'

union Holder
	held(let cell Cell)
	empty
end 'Holder'

function main() returns ExitCode
	let holder = Holder.held(Cell.create())
	var copy = Cell.create()

	match holder 'read'
		held(cell) then copy = cell
		empty then return 1
	end 'read'

	return copy.level as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: a-non-let-payload-read-into-a-var -->
The accepted twin of the case above: a payload that is not `let` is held by a `var` holder, so the record bound out
of it is a writable name for the payload the holder owns and `copy` may take it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var level as Integer

	static function create() returns Self
		return Self{level: 0}
	end 'create'
end 'Cell'

union Holder
	held(cell Cell)
	empty
end 'Holder'

function main() returns ExitCode
	var holder = Holder.held(Cell.create())
	var copy = Cell.create()

	match holder 'read'
		held(cell) then copy = cell
		empty then return 1
	end 'read'

	copy.level = 4

	return copy.level as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: the-binding-of-a-let-payload-passed-to-a-callee-that-writes-it -->
`bump` writes its parameter, and the record bound out of the `let` payload may be passed to it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var level as Integer

	static function create() returns Self
		return Self{level: 0}
	end 'create'
end 'Cell'

union Holder
	held(let cell Cell)
	empty
end 'Holder'

function bump(cell Cell)
	cell.level = cell.level + 1
end 'bump'

function main() returns ExitCode
	var holder = Holder.held(Cell.create())

	match holder 'write'
		held(cell) then bump(cell)
		empty then return 1
	end 'write'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: the-binding-of-a-let-payload-passed-to-a-parameter-kept-in-a-var-field -->
`keep` stores its parameter into a `var` field, and the record bound out of the `let` payload may be passed to it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var level as Integer

	static function create() returns Self
		return Self{level: 0}
	end 'create'
end 'Cell'

union Holder
	held(let cell Cell)
	empty
end 'Holder'

type Pocket
	export var kept as Cell

	static function create() returns Self
		return Self{kept: Cell.create()}
	end 'create'

	function keep(cell Cell)
		self.kept = cell
	end 'keep'
end 'Pocket'

function main() returns ExitCode
	let holder = Holder.held(Cell.create())
	var pocket = Pocket.create()

	match holder 'read'
		held(cell) then pocket.keep(cell)
		empty then return 1
	end 'read'

	return pocket.kept.level as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: a-let-payload-returned-by-a-call-is-read-into-a-var -->
A function that hands the payload back hands back the record, and the result may be bound to a `var`.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var level as Integer

	static function create() returns Self
		return Self{level: 0}
	end 'create'
end 'Cell'

union Holder
	held(let cell Cell)
	empty
end 'Holder'

function payloadOf(holder Holder) returns Cell
	return match holder 'read'
		held(cell) gives cell
		empty gives Cell.create()
	end 'read'
end 'payloadOf'

function main() returns ExitCode
	let holder = Holder.held(Cell.create())
	var copy = payloadOf(holder)
	copy.level = 4

	return copy.level as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: a-non-let-payload-returned-by-a-call-is-read-into-a-var -->
The accepted twin of the case above: the payload is not `let` and the holder is a `var`, so the function hands back
a writable record and the `var` binding takes it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var level as Integer

	static function create() returns Self
		return Self{level: 0}
	end 'create'
end 'Cell'

union Holder
	held(cell Cell)
	empty
end 'Holder'

function payloadOf(holder Holder) returns Cell
	return match holder 'read'
		held(cell) gives cell
		empty gives Cell.create()
	end 'read'
end 'payloadOf'

function main() returns ExitCode
	var holder = Holder.held(Cell.create())
	var copy = payloadOf(holder)
	copy.level = 4

	return copy.level as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: a-let-payload-binding-goes-to-a-parameter-kept-in-a-let-payload -->
A record bound out of one `let` payload is stored into another, and the new holder names the same record.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias HolderArray = Array with Holder

type Cell
	export var level as Integer

	static function create() returns Self
		return Self{level: 3}
	end 'create'
end 'Cell'

union Holder
	held(let cell Cell)
	empty
end 'Holder'

type Shelf
	var holders as HolderArray

	static function create() returns Self
		return Self{holders: HolderArray.create(0)}
	end 'create'

	function shelve(cell Cell)
		self.holders.push(Holder.held(cell))
	end 'shelve'

	function count() returns Integer
		return self.holders.count() as Integer
	end 'count'
end 'Shelf'

function main() returns ExitCode
	var shelf = Shelf.create()
	let source = Holder.held(Cell.create())

	match source 'copy'
		held(cell) then shelf.shelve(cell)
		empty then return 1
	end 'copy'

	return shelf.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: a-var-read-again-after-it-is-passed-to-a-parameter-kept-in-a-let-payload -->
`shelve` keeps its parameter in a `let` payload, and a `var` handed to it and read again names the same record.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias HolderArray = Array with Holder

type Cell
	export var level as Integer

	static function create() returns Self
		return Self{level: 3}
	end 'create'
end 'Cell'

union Holder
	held(let cell Cell)
	empty
end 'Holder'

type Shelf
	var holders as HolderArray

	static function create() returns Self
		return Self{holders: HolderArray.create(0)}
	end 'create'

	function shelve(cell Cell)
		self.holders.push(Holder.held(cell))
	end 'shelve'

	function count() returns Integer
		return self.holders.count() as Integer
	end 'count'
end 'Shelf'

function main() returns ExitCode
	var shelf = Shelf.create()
	var cell = Cell.create()
	shelf.shelve(cell)
	cell.level = 9

	return shelf.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: a-payload-without-let-still-follows-the-scrutinee -->
`open` is declared without `let`, so under a `var` scrutinee it is writable even though `fixed` beside it is not.

```maxon
union Pair
	both(let fixed ByteArray, open ByteArray)
	neither
end 'Pair'

function main() returns ExitCode
	var pair = Pair.both(ByteArray.create(0), open: ByteArray.create(0))

	match pair 'write'
		both(_, open) then open.push(1)
		neither then return 1
	end 'write'

	match pair 'read'
		both(_, open) then return open.count() as ExitCode
		neither then return 1
	end 'read'
end 'main'
```
```exitcode
1
```

<!-- test: a-writable-payload-sharing-a-slot-with-a-let-payload-of-another-case -->
Slot 0 is a `let` payload in `named` and a `var` payload in `numbered`; the two hold different kinds of record, so
the `var` one stays writable.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntegerArray = Array with Integer

union Slot
	named(let label ByteArray)
	numbered(values IntegerArray)
end 'Slot'

function main() returns ExitCode
	var slot = Slot.numbered(IntegerArray.create(0))

	match slot 'write'
		numbered(values) then values.push(7)
		named then return 1
	end 'write'

	match slot 'read'
		numbered(values) then return values.count() as ExitCode
		named then return 1
	end 'read'
end 'main'
```
```exitcode
1
```

<!-- test: an-all-let-record-and-a-scalar-go-into-let-payloads -->
A record whose fields are all `let` is a value type and a scalar owns no storage, so a `var` of either goes into a
`let` payload and stays usable afterwards.

```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export let x as Integer
	export let y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

union Mark
	at(let point Point, let tick Integer)
	cleared
end 'Mark'

function main() returns ExitCode
	var point = Point.create(3, y: 4)
	var tick = 5 as Integer
	let mark = Mark.at(point, tick: tick)
	tick = tick + point.x

	return match mark 'read'
		at(p, t) gives (p.x + p.y + t + tick) as ExitCode
		cleared gives 0
	end 'read'
end 'main'
```
```exitcode
20
```

<!-- test: a-var-read-again-after-a-wrapper-that-stores-into-a-let-payload -->
`wrapOp` hands its parameter on to a function that stores it in a `let` payload, and a `var` that is read again names the same record.

```maxon
union Op
	call(let callee ByteArray)
	nop
end 'Op'

function makeOp(callee ByteArray) returns Op
	return Op.call(callee)
end 'makeOp'

function wrapOp(callee ByteArray) returns Op
	return makeOp(callee)
end 'wrapOp'

function main() returns ExitCode
	var scratch = ByteArray.create(0)
	scratch.push(1)
	let op = wrapOp(scratch)
	scratch.push(2)

	return match op 'shape'
		call(callee) gives callee.count() as ExitCode
		nop gives 0
	end 'shape'
end 'main'
```
```exitcode
2
```

<!-- test: a-let-payload-of-an-aliased-array-stored-into-a-tuple -->
`Bytes` names the payload type, so the tuple column the two payloads are stored into is declared `Bytes`. `let (a, b)` binds both payloads' records, so the store is accepted.
```maxon
typealias Bytes = Array with Byte

union Parts
	parts(let prefix Bytes, let member Bytes)
	none
end 'Parts'

function split() returns Parts
	let a = Bytes.create(0)
	let b = Bytes.create(0)
	return Parts.parts(a, member: b)
end 'split'

function main() returns ExitCode
	let (a, b) = match split() 'm'
		parts(x, y) gives (x, y)
		none panic("none")
	end 'm'

	return (a.count() + b.count()) as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: a-let-payload-of-a-contested-alias-stored-into-a-tuple -->
The same program where `Bytes` is also declared, over another instance, in a second file. The name is contested, so it brands nothing and the tuple column is declared without a brand; the payloads are accepted the same way.
```maxon
// --- file: a.maxon
typealias Bytes = Array with Byte

union Parts
	parts(let prefix Bytes, let member Bytes)
	none
end 'Parts'

function split() returns Parts
	let a = Bytes.create(0)
	let b = Bytes.create(0)
	return Parts.parts(a, member: b)
end 'split'

function main() returns ExitCode
	let (a, b) = match split() 'm'
		parts(x, y) gives (x, y)
		none panic("none")
	end 'm'

	return (a.count() + b.count()) as ExitCode
end 'main'

// --- file: b/b.maxon
typealias Bytes = Array with String

function names(xs Bytes) returns Count
	return xs.count()
end 'names'
```
```exitcode
0
```

<!-- test: a-var-read-again-after-a-wrapper-that-stores-it-into-a-let-payload-of-an-alias-over-an-alias -->
The payload's type `Grid` is an array of `Row`, which is itself an alias of an instance, so the payload's kind is spelled through both aliases; a `var` stored into it by a wrapper and read again names the same record.

```maxon
typealias Row = Array with Byte
typealias Grid = Array with Row

union Op
	call(let grid Grid)
	nop
end 'Op'

function makeOp(grid Grid) returns Op
	return Op.call(grid)
end 'makeOp'

function wrapOp(grid Grid) returns Op
	return makeOp(grid)
end 'wrapOp'

function main() returns ExitCode
	var scratch = Grid.create(0)
	scratch.push(Row.create(0))
	let op = wrapOp(scratch)
	scratch.push(Row.create(0))

	return match op 'shape'
		call(grid) gives grid.count() as ExitCode
		nop gives 0
	end 'shape'
end 'main'
```
```exitcode
2
```

<!-- test: a-let-payload-passed-to-a-constructor-that-stores-it -->
`Shape.create` stores its parameter into a `var` field, and the record bound out of the `let` payload may be passed to it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var level as Integer

	static function create() returns Self
		return Self{level: 0}
	end 'create'
end 'Cell'

union Holder
	held(let cell Cell)
	empty
end 'Holder'

type Shape
	export var cell as Cell

	static function create(cell Cell) returns Self
		return Self{cell: cell}
	end 'create'
end 'Shape'

function shapeOf(holder Holder) returns Shape
	match holder 'which'
		held(cell) then return Shape.create(cell)
		empty then return Shape.create(Cell.create())
	end 'which'
end 'shapeOf'

function main() returns ExitCode
	let holder = Holder.held(Cell.create())
	let shape = shapeOf(holder)

	return shape.cell.level as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: a-let-payload-projected-into-a-tuple-is-read-by-the-caller -->
A tuple lends its items: the projection returns the payload's record and the caller reads it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var level as Integer

	static function create() returns Self
		return Self{level: 4}
	end 'create'
end 'Cell'

union Holder
	held(let cell Cell, let tag Integer)
	empty
end 'Holder'

function partsOf(holder Holder) returns (Cell, Integer)
	match holder 'which'
		held(cell, tag) then return (cell, tag)
		empty then return (Cell.create(), 0)
	end 'which'
end 'partsOf'

function main() returns ExitCode
	let holder = Holder.held(Cell.create(), tag: 3)
	let parts = partsOf(holder)

	print("{parts.0.level} {parts.1}\n")
	return 0
end 'main'
```
```stdout
4 3
```
```exitcode
0
```
