---
feature: for-in-existential
status: experimental
keywords: [for, iteration, interface, existential, witness, Iterator, Iterable, IterationError]
category: control-flow
---

# `for … in` over an interface EXISTENTIAL

## Documentation

A value whose declared type is an INTERFACE is an existential — a fat pointer carrying the conformer's
value beside its witness table (`MaxonType.interfaceRef`). `for … in` over one is the SAME traversal every
other source gets; only the two protocol calls change shape. A struct source emits `<Type>.current()` and
`<Type>.advance()` as direct calls, because the declarations ARE what makes the loop expressible there. An
existential has no one conformer to name, so it dispatches each of them through the table the value carries,
exactly as a written `s.current()` on that value already does.

**So the traversal must be something the INTERFACE REQUIRES, and a conformer supplying it is not enough.**
That is the one rule that differs from the struct source's structural test, and it is forced rather than
chosen: under dictionary-passing the shared body has no concrete callee to look up, so a requirement is the
only thing common to every conformer. Both halves of the protocol are read off the requirement list:

- the **cursor protocol** — `current()` plus a throwing `advance()` — walks the existential directly;
- an **`Iterable`** existential — one requiring `createIterator()` — is replaced by the cursor that factory
  returns, the same rewrite a struct `Iterable` gets, and the cursor is then walked as a cursor.

The requirement list is TRANSITIVE, so an interface that inherits `current()`/`advance()` through `extends`
is iterable at the derived interface: the dispatch reads the DERIVED table's slot while the requirement it
binds against is the base's, which is the split `WitnessDispatchTarget.interfaceDeclIndex` already carries.

⚠ **The `advance()` throw is pinned to `IterationError` here for the reason it is pinned on a struct
source**: the loop absorbs what the throw carries without binding it, which is sound only for a
compiler-owned, payload-free enum. One clause classifier answers for both stores
(`throwsClauseIsIterationError`), so a declared callee and an interface requirement cannot come to accept
different error types.

⚠ **An existential whose interface requires NEITHER protocol is a refusal that names the interface.** A
silent no-op would be the worst outcome available — the loop would compile, run zero trips and report
nothing — and the generic "over a `interface` value" sentence the other sources share does not say what is
actually missing.

⛔ **A PARAMETERIZED interface is NOT an existential in shv2, and is refused in its own words.**
`MaxonType.interfaceRef` carries an interface NAME and a witness table and has nowhere to record `with`
arguments, so `Iterable with (Element, ElementIterator)` resolves to a `genericInstance` whose base is an
interface no file declares as a `type`. Every door refuses it — a field read, a method call, and this loop —
so it is inert rather than wrong; but the shared sentence called it a `struct` (that is `typeTagName` of a
`genericInstance`), which pointed at a missing struct instead of at the absent mechanism. NEITHER reference
compiler solves it either: v1's interface registry is a bare name table with no argument vector and drops
`Array.from`'s body in dead-function elimination before its lowering could reach `mrt_panic`, and the C#
bootstrap monomorphizes per concrete argument and dies as `E9001 … Function
'CounterSeq.createIterator$CounterSeq' not found in module` the moment one is supplied.

## Tests

<!-- test: existential.cursor-protocol -->
⭐ **THE WHOLE RUNG IN ONE PROGRAM, with no generics and no `Array` anywhere.** `Seq` requires the cursor
protocol, `Upto` conforms, and `total` takes the EXISTENTIAL. Both protocol calls are witness dispatches.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Seq
	function current() returns Integer
	function advance() throws IterationError
end 'Seq'

type Upto implements Seq
	var pos as Integer
	let limit as Integer

	export static function create(limit Integer) returns Self
		return Self{pos: 1, limit: limit}
	end 'create'

	export function current() returns Integer
		return self.pos
	end 'current'

	export function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'
end 'Upto'

function total(source Seq) returns Integer
	var sum = 0 as Integer
	for item in source 'walk'
		sum = sum + item
	end 'walk'
	return sum
end 'total'

function main() returns ExitCode
	let u = Upto.create(4)
	print("{total(u)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
```

<!-- test: existential.cursor-protocol-inherited -->
The protocol reaches the existential through `extends`. `Counted` declares only `size()`; `current()` and
`advance()` are `Seq`'s, and the loop finds them because the requirement list is transitive.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Seq
	function current() returns Integer
	function advance() throws IterationError
end 'Seq'

interface Counted extends Seq
	function size() returns Integer
end 'Counted'

type Upto implements Counted
	var pos as Integer
	let limit as Integer

	export static function create(limit Integer) returns Self
		return Self{pos: 1, limit: limit}
	end 'create'

	export function size() returns Integer
		return self.limit
	end 'size'

	export function current() returns Integer
		return self.pos
	end 'current'

	export function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'
end 'Upto'

function report(source Counted) returns Integer
	var sum = 0 as Integer
	for item in source 'walk'
		sum = sum + item
	end 'walk'
	return sum + source.size()
end 'report'

function main() returns ExitCode
	let u = Upto.create(3)
	print("{report(u)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9
```

<!-- test: existential.generic-conformer -->
The conformer is a GENERIC type. shv2 compiles one body per generic type, so one
`__witness_Tagged.Seq` answers for every instantiation — the existential holds a `Tagged with Integer`
and the loop dispatches through that one table.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Seq
	function current() returns Integer
	function advance() throws IterationError
end 'Seq'

type Tagged uses T implements Seq
	var pos as Integer
	let limit as Integer
	let tag as T

	export static function create(limit Integer, tag T) returns Self
		return Self{pos: 1, limit: limit, tag: tag}
	end 'create'

	export function current() returns Integer
		return self.pos
	end 'current'

	export function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'
end 'Tagged'

typealias IntTagged = Tagged with Integer

function total(source Seq) returns Integer
	var sum = 0 as Integer
	for item in source 'walk'
		sum = sum + item
	end 'walk'
	return sum
end 'total'

function main() returns ExitCode
	let t = IntTagged.create(5, tag: 7)
	print("{total(t)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
15
```

<!-- test: existential.managed-element -->
⚠ **The element is MANAGED.** `current()` returns a `String` the conformer MINTS per call, so the value the
loop hands the body is an owned heap record that must be released once per trip and not once more. A leak
or a double release is a non-zero exit here, not a wrong number.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Lines
	function current() returns String
	function advance() throws IterationError
end 'Lines'

type Numbered implements Lines
	var pos as Integer
	let limit as Integer

	export static function create(limit Integer) returns Self
		return Self{pos: 1, limit: limit}
	end 'create'

	export function current() returns String
		return "line number {self.pos} of this managed sequence"
	end 'current'

	export function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'
end 'Numbered'

function show(source Lines)
	for text in source 'walk'
		print("{text}\n")
	end 'walk'
end 'show'

function main() returns ExitCode
	let n = Numbered.create(3)
	show(n)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
line number 1 of this managed sequence
line number 2 of this managed sequence
line number 3 of this managed sequence
```

<!-- test: existential.every-producer-of-one -->
Every way a program can hand the loop an existential, in one program: a CALL RESULT, a LOCAL binding, and a
FIELD read. The witness half is an SSA value paired with the value half at the moment each is produced, and
the dispatch PANICS on an unpaired one — so a producer that forgot to pair is a compiler crash, not a wrong
answer, and each of the three is its own producer.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Seq
	function current() returns Integer
	function advance() throws IterationError
end 'Seq'

type Upto implements Seq
	var pos as Integer
	let limit as Integer

	export static function create(limit Integer) returns Self
		return Self{pos: 1, limit: limit}
	end 'create'

	export function current() returns Integer
		return self.pos
	end 'current'

	export function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'
end 'Upto'

type Holder
	export var seq as Seq

	export static function create(seq Seq) returns Self
		return Self{seq: seq}
	end 'create'
end 'Holder'

function makeSeq(limit Integer) returns Seq
	return Upto.create(limit)
end 'makeSeq'

function fromCall(limit Integer) returns Integer
	var sum = 0 as Integer
	for item in makeSeq(limit) 'walk'
		sum = sum + item
	end 'walk'
	return sum
end 'fromCall'

function fromLocal(limit Integer) returns Integer
	let s = makeSeq(limit)
	var sum = 0 as Integer
	for item in s 'walk'
		sum = sum + item
	end 'walk'
	return sum
end 'fromLocal'

function fromField(h Holder) returns Integer
	var sum = 0 as Integer
	for item in h.seq 'walk'
		sum = sum + item
	end 'walk'
	return sum
end 'fromField'

function main() returns ExitCode
	print("{fromCall(4)}\n")
	print("{fromLocal(4)}\n")
	let h = Holder.create(makeSeq(4))
	print("{fromField(h)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
10
10
```

<!-- test: existential.iterable-factory -->
The existential requires the `Iterable` half instead. `createIterator()` is dispatched once through the
witness table, in the preheader, and what it returns is a concrete cursor the loop then walks directly.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type IntCursor
	let items as IntArray
	var pos as Integer

	export static function create(items IntArray) returns Self throws IterationError
		if items.count() == 0 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{items: items, pos: 0}
	end 'create'

	export function current() returns Integer
		return try self.items.get(self.pos) otherwise 0
	end 'current'

	export function advance() throws IterationError
		if self.pos + 1 >= self.items.count() 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'
end 'IntCursor'

interface Bag
	function createIterator() returns IntCursor throws IterationError
end 'Bag'

type Numbers implements Bag
	let items as IntArray

	export static function create(items IntArray) returns Self
		return Self{items: items}
	end 'create'

	export function createIterator() returns IntCursor throws IterationError
		return try IntCursor.create(self.items)
	end 'createIterator'
end 'Numbers'

function total(source Bag) returns Integer
	var sum = 0 as Integer
	for item in source 'walk'
		sum = sum + item
	end 'walk'
	return sum
end 'total'

function main() returns ExitCode
	var filled = IntArray.create()
	filled.push(4)
	filled.push(5)
	filled.push(6)
	let some = Numbers.create(filled)
	print("{total(some)}\n")

	let none = Numbers.create(IntArray.create())
	print("{total(none)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
15
0
```

<!-- test: error.existential-requires-no-traversal -->
An existential whose interface requires NEITHER protocol is refused, and the refusal NAMES the interface —
a conformer that happens to declare `current`/`advance` cannot be reached through a table that has no slot
for them, so the generic "over a `interface` value" sentence would point at the wrong thing.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Labelled
	function label() returns Integer
end 'Labelled'

type Widget implements Labelled
	let id as Integer

	export static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'

	export function label() returns Integer
		return self.id
	end 'label'

	export function current() returns Integer
		return self.id
	end 'current'

	export function advance() throws IterationError
		throw IterationError.exhausted
	end 'advance'
end 'Widget'

function total(source Labelled) returns Integer
	var sum = 0 as Integer
	for item in source 'walk'
		sum = sum + item
	end 'walk'
	return sum
end 'total'

function main() returns ExitCode
	let w = Widget.create(3)
	print("{total(w)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:30:2: Unsupported: `for … in` over a value held at interface 'Labelled', which requires neither the cursor protocol `current()` + `advance() throws IterationError` nor a `createIterator()` factory — an existential is walked through the witness table the value carries, so the traversal has to be one the INTERFACE requires; a conformer declaring it supplies no slot to dispatch through
```

<!-- test: error.parameterized-interface-is-not-an-existential -->
An INSTANCE of an interface is refused by a sentence that names the interface and the missing mechanism —
not by the shared one, whose `typeTagName` calls a `genericInstance` a `struct`. This is the wall
`stdlib/Array.maxon:132` stands at, pinned here so the rung that lifts it has to move this case.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Seq uses Element
	function current() returns Element
	function advance() throws IterationError
end 'Seq'

type Upto implements Seq with Integer
	var pos as Integer
	let limit as Integer

	export static function create(limit Integer) returns Self
		return Self{pos: 1, limit: limit}
	end 'create'

	export function current() returns Integer
		return self.pos
	end 'current'

	export function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'
end 'Upto'

typealias IntegerSeq = Seq with Integer

function total(source IntegerSeq) returns Integer
	var sum = 0 as Integer
	for item in source 'walk'
		sum = sum + item
	end 'walk'
	return sum
end 'total'

function main() returns ExitCode
	let u = Upto.create(4)
	print("{total(u)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:33:2: Unsupported: `for … in` over an INSTANCE of the interface 'Seq' — shv2 holds an existential as an interface NAME plus a witness table (`interfaceRef`) and has nowhere to record the `with` arguments, so a parameterized interface never becomes one: the cursor `createIterator()` would return is typed by an associated type with no binding here, and carries no table to walk through. Iterate a concrete conformer, or hold the source at an interface that requires the traversal directly
```
