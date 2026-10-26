---
feature: let-union-payloads
status: experimental
keywords: [union, enum, payload, let, read-only, match, write-back, let field]
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

The payload is read-only for the whole life of the value, and it has no other writer:

- Nothing reachable through it may be written. A `match` binding of a `let` payload is read-only whatever the
  scrutinee is, so a field store, a receiver-writing method or a call that writes its parameter is refused
  (`E3159`), and so is assigning to the binding, which would write back into the union.
- The value stored into it at construction must be read-only, a value type, or moved: a still-shared `var` is
  refused (`E3078`). A `var` at its last use moves in.
- A read of the payload is read-only, so binding it to a `var` is refused (`E3078`).

A payload declared without `let` follows its scrutinee as before: writable under a `var`, read-only under a
`let`.

## Tests

<!-- test: a-read-only-value-stored-into-a-let-payload -->
The read-only `source.title` is stored into the `let` payload `callee`.

```maxon
typealias Integer = int(i64.min to i64.max)

union Op
	call(let callee ByteArray, weight Integer)
	nop
end 'Op'

type Source
	export let title as ByteArray

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
	var name = ByteArray.create()
	name.push(65)
	name.push(66)
	let op = Op.call(name, weight: 1)
	return size(op) as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: error.a-still-shared-var-stored-into-a-let-payload -->
`name` is read again after the construction, so it is still shared and cannot be stored into the `let` payload.

```maxon
typealias Integer = int(i64.min to i64.max)

union Op
	call(let callee ByteArray, weight Integer)
	nop
end 'Op'

function main() returns ExitCode
	var name = ByteArray.create()
	name.push(65)
	let op = Op.call(name, weight: 1)
	name.push(66)

	match op 'shape'
		call(_, weight) then return weight as ExitCode
		nop then return 0
	end 'shape'
end 'main'
```
```maxoncstderr
error E3078: specs/let-union-payloads/error.a-still-shared-var-stored-into-a-let-payload.maxon:12:19: cannot store the `var` 'name', which is read again, into a `let` payload 'Op.call.callee': a `let` payload has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: error.a-field-write-through-the-binding-of-a-let-payload -->
`cell` is bound out of a `let` payload, so it is read-only even though the scrutinee `holder` is a `var`.

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
```maxoncstderr
error E3159: specs/let-union-payloads/error.a-field-write-through-the-binding-of-a-let-payload.maxon:21:19: cannot write through 'cell': 'cell' is read-only (a `let` payload) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: error.a-writing-method-on-the-binding-of-a-let-payload -->
`items.push` writes the record bound out of the `let` payload.

```maxon
union Holder
	held(let items ByteArray)
	empty
end 'Holder'

function main() returns ExitCode
	var holder = Holder.held(ByteArray.create())

	match holder 'write'
		held(items) then items.push(1)
		empty then return 1
	end 'write'

	return 0
end 'main'
```
```maxoncstderr
error E3159: specs/let-union-payloads/error.a-writing-method-on-the-binding-of-a-let-payload.maxon:11:26: cannot write through 'items': 'items' is read-only (a `let` payload) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: error.assigning-to-the-binding-of-a-let-payload -->
Assigning to the binding of a payload writes back into the union, which a `let` payload refuses.

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
error E3159: specs/let-union-payloads/error.assigning-to-the-binding-of-a-let-payload.maxon:21:19: cannot write through 'cell': 'cell' is read-only (a `let` payload) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: error.a-let-payload-read-into-a-var -->
A `var` is a writer, and a `let` payload has none besides its construction.

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
```maxoncstderr
error E3078: specs/let-union-payloads/error.a-let-payload-read-into-a-var.maxon:22:19: cannot assign a value read out of a `let` payload to mutable binding 'copy'; use 'let' instead of 'var', or use clone()
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

<!-- test: error.the-binding-of-a-let-payload-passed-to-a-callee-that-writes-it -->
`bump` writes its parameter, so the read-only record bound out of the `let` payload cannot be passed to it.

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
```maxoncstderr
error E3159: specs/let-union-payloads/error.the-binding-of-a-let-payload-passed-to-a-callee-that-writes-it.maxon:25:19: cannot pass 'cell', which is read-only (a `let` payload), to a function that writes parameter 'cell' (in main); pass a clone()
```

<!-- test: error.the-binding-of-a-let-payload-passed-to-a-parameter-kept-in-a-var-field -->
`keep` stores its parameter into a `var` field, which is a place something may write, so the read-only record bound
out of the `let` payload is refused for it.

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
```maxoncstderr
error E3078: specs/let-union-payloads/error.the-binding-of-a-let-payload-passed-to-a-parameter-kept-in-a-var-field.maxon:34:26: cannot pass 'cell', which is read-only (a `let` payload), to parameter 'cell' of 'Pocket.keep', which keeps it where something may write it; pass a clone()
```

<!-- test: error.a-let-payload-returned-by-a-call-is-read-into-a-var -->
A function that hands the payload back hands back a read-only record, so binding the result to a `var` is refused.

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
```maxoncstderr
error E3078: specs/let-union-payloads/error.a-let-payload-returned-by-a-call-is-read-into-a-var.maxon:26:6: cannot assign a value, which may share storage with a `let` payload, to mutable binding 'copy'; use 'let' instead of 'var', or use clone()
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
A read-only record bound out of one `let` payload is stored into another: a `let` payload has no other writer, so
the new holder cannot write it either.

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
		return Self{holders: HolderArray.create()}
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

<!-- test: error.a-var-read-again-after-it-is-passed-to-a-parameter-kept-in-a-let-payload -->
`shelve` keeps its parameter in a `let` payload, so a `var` handed to it and read again is still shared.

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
		return Self{holders: HolderArray.create()}
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
```maxoncstderr
error E3078: specs/let-union-payloads/error.a-var-read-again-after-it-is-passed-to-a-parameter-kept-in-a-let-payload.maxon:37:8: cannot pass the `var` 'cell', which is read again, to parameter 'cell' of 'Shelf.shelve', which stores it into a `let` payload: a `let` payload has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: a-payload-without-let-still-follows-the-scrutinee -->
`open` is declared without `let`, so under a `var` scrutinee it is writable even though `fixed` beside it is not.

```maxon
union Pair
	both(let fixed ByteArray, open ByteArray)
	neither
end 'Pair'

function main() returns ExitCode
	var pair = Pair.both(ByteArray.create(), open: ByteArray.create())

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
	var slot = Slot.numbered(IntegerArray.create())

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

<!-- test: error.a-var-read-again-after-a-wrapper-that-stores-into-a-let-payload -->
`wrapOp` hands its parameter on to a function that stores it in a `let` payload, so the wrapper keeps it there
too and a `var` that is read again is refused, naming the payload.

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
	var scratch = ByteArray.create()
	scratch.push(1)
	let op = wrapOp(scratch)
	scratch.push(2)

	return match op 'shape'
		call(callee) gives callee.count() as ExitCode
		nop gives 0
	end 'shape'
end 'main'
```
```maxoncstderr
error E3078: specs/let-union-payloads/error.a-var-read-again-after-a-wrapper-that-stores-into-a-let-payload.maxon:18:11: cannot pass the `var` 'scratch', which is read again, to parameter 'callee' of 'wrapOp', which stores it into a `let` payload: a `let` payload has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: a-let-payload-of-an-aliased-array-stored-into-a-tuple -->
`Bytes` names the payload type, so the tuple column the two payloads are stored into is declared `Bytes`. The read-only mark travels with the tuple, and `let (a, b)` keeps both payloads read-only, so the store is accepted.
```maxon
typealias Bytes = Array with Byte

union Parts
	parts(let prefix Bytes, let member Bytes)
	none
end 'Parts'

function split() returns Parts
	let a = Bytes.create()
	let b = Bytes.create()
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
	let a = Bytes.create()
	let b = Bytes.create()
	return Parts.parts(a, member: b)
end 'split'

function main() returns ExitCode
	let (a, b) = match split() 'm'
		parts(x, y) gives (x, y)
		none panic("none")
	end 'm'

	return (a.count() + b.count()) as ExitCode
end 'main'

// --- file: b.maxon
typealias Bytes = Array with String

function names(xs Bytes) returns Count
	return xs.count()
end 'names'
```
```exitcode
0
```

<!-- test: error.a-var-read-again-after-a-wrapper-that-stores-it-into-a-let-payload-of-an-alias-over-an-alias -->
The payload's type `Grid` is an array of `Row`, which is itself an alias of an instance, so the payload's kind is spelled through both aliases; a `var` stored into it by a wrapper and read again is refused the same way.

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
	var scratch = Grid.create()
	scratch.push(Row.create())
	let op = wrapOp(scratch)
	scratch.push(Row.create())

	return match op 'shape'
		call(grid) gives grid.count() as ExitCode
		nop gives 0
	end 'shape'
end 'main'
```
```maxoncstderr
error E3078: <fragment>:21:11: cannot pass the `var` 'scratch', which is read again, to parameter 'grid' of 'wrapOp', which stores it into a `let` payload: a `let` payload has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```
