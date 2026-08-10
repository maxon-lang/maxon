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

⭐ **AND THE SAME IS TRUE OF A CONSTRAINED TYPE PARAMETER**, which is the language's OTHER witness-dispatched
receiver: `for v in self.item` inside `type W uses T where T is Seq` jumps through the enclosing body's hidden
witness parameter instead of through a fat pointer's second half. Everything else about it is identical, so
the two share one arm of `MethodSurface` and one refusal.

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

⭐ **A PARAMETERIZED interface IS an existential too, and `for … in` reaches it by the same route.**
`Seq with Integer` resolves to the existential `Seq` (`ProgramSignatures.instanceDenotedType`), so everything
this page says of a bare interface holds of an instance of one word for word — the loop asks the same
requirement list and emits the same two dispatches. See `specs-shv2/parameterized-existential.md`, which owns
that spelling and the E3125 that verifies what a use site claims its `with` arguments are. What remains out of
reach is narrower and is refused where the binding is WRITTEN: an associated type bound to another INTERFACE
needs a second witness table travelling with the first, which is the wall `stdlib/Array.maxon`'s
`Iterable with (Element, ElementIterator)` stands at.

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
error E2015: <fragment>:30:2: Unsupported: `for … in` over a value dispatched through interface 'Labelled', which supplies neither the cursor protocol `current()` + `advance() throws IterationError` nor a `createIterator()` factory as a zero-argument REQUIREMENT — the traversal is a jump through the witness table the value carries, so it has to be something the interface requires; a conformer declaring it supplies no slot to dispatch through
```

<!-- test: error.ambiguous-protocol-requirement -->
⚠ **THE LOOP MAY NOT SILENTLY PICK A SLOT THE WRITTEN CALL REFUSES TO PICK.** `Derived extends Base` and both
declare a zero-argument `current()`, so the dispatch has TWO table slots and nothing to choose by — which is
E3114 for `source.current()`. The loop resolves through the same door and gets the same answer; a first-hit
search here would bind one of two witness slots with no diagnostic, which is a wrong function pointer rather
than a refusal.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Base
	function current() returns Integer
	function advance() throws IterationError
end 'Base'

interface Derived extends Base
	function current() returns Integer
end 'Derived'

type Upto implements Derived
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

function looped(source Derived) returns Integer
	var sum = 0 as Integer
	for item in source 'walk'
		sum = sum + item
	end 'walk'
	return sum
end 'looped'

function main() returns ExitCode
	let u = Upto.create(4)
	print("{looped(u)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3114: <fragment>:35:2: 'current' taking 0 argument(s) is provided by both Derived.current() returns Integer and Base.current() returns Integer through interface 'Derived' — a witness dispatch binds ONE table slot, and these are two, so there is nothing to choose by. Rename one requirement, or drop one of the constraints
```

<!-- test: existential.merge-phi-two-conformers -->
⭐ **THE DISPATCH IS GENUINELY DYNAMIC, and this is the case that proves it.** `s` is a merge PHI over two
DIFFERENT conformers, so no static reading of the source can say which `current`/`advance` a trip will
reach — the witness half travelling with the value is the only thing that decides, and the two answers
differ. `pairInterfaceWitness`'s own list of producers names a merge phi; this is that producer, walked.
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

type Down implements Seq
	var pos as Integer

	export static function create(start Integer) returns Self
		return Self{pos: start}
	end 'create'

	export function current() returns Integer
		return self.pos
	end 'current'

	export function advance() throws IterationError
		if self.pos <= 1 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos - 1
	end 'advance'
end 'Down'

function makeUp() returns Seq
	return Upto.create(4)
end 'makeUp'

function makeDown() returns Seq
	return Down.create(3)
end 'makeDown'

function pick(up bool) returns Integer
	var s = makeUp()
	if not up 'other'
		s = makeDown()
	end 'other'
	var sum = 0 as Integer
	for item in s 'walk'
		sum = sum + item
	end 'walk'
	return sum
end 'pick'

function main() returns ExitCode
	print("{pick(true)}\n")
	print("{pick(false)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
6
```

<!-- test: existential.constrained-type-parameter -->
The OTHER witness-dispatched receiver. `self.item` is a `T` under `where T is Seq`, so the concrete conformer
is unknown in the shared body and the two protocol calls jump through the constraint's hidden witness
parameter. MEASURED before this rung: `E2015 … over a 'type parameter' value — … any type declaring the cursor
protocol`, the same wrong sentence the existential got.
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

type Wrap uses T where T is Seq
	export var item as T

	export static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	export function total() returns Integer
		var sum = 0 as Integer
		for v in self.item 'walk'
			sum = sum + v
		end 'walk'
		return sum
	end 'total'
end 'Wrap'

typealias UptoWrap = Wrap with Upto

function main() returns ExitCode
	var w = UptoWrap.create(Upto.create(4))
	print("{w.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
```

<!-- test: existential.managed-element-early-exits -->
⚠ **THE PER-TRIP RELEASE ON THE EXITS THE FALL-THROUGH CASE CANNOT REACH.** `giveTemporaryScopeLifetime`
enumerates three ways a promoted per-trip temporary leaves — a `return`/`throw` out of the body, a `break`,
and the loop's own `end` — and only the last is exercised by the plain managed case. Each walk here mints
heap `String`s the loop owns for one trip; a missed release on either early exit is exit 101, and a double
release is a fault.
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
		return "a managed line long enough to live on the heap, number {self.pos}"
	end 'current'

	export function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'
end 'Numbered'

function returnsOutOfTheBody(source Lines) returns Integer
	for text in source 'walk'
		if text.byteLength() > 0 'theFirst'
			return text.byteLength()
		end 'theFirst'
	end 'walk'
	return 0
end 'returnsOutOfTheBody'

function breaksOutOfTheBody(source Lines) returns Integer
	var total = 0 as Integer
	for text in source 'walk'
		total = total + text.byteLength()
		if total > 100 'enough'
			break
		end 'enough'
	end 'walk'
	return total
end 'breaksOutOfTheBody'

function main() returns ExitCode
	var i = 0 as Integer
	var returned = 0 as Integer
	var broke = 0 as Integer
	while i < 200 'many'
		returned = returnsOutOfTheBody(Numbered.create(30))
		broke = breaksOutOfTheBody(Numbered.create(30))
		i = i + 1
	end 'many'
	print("{returned} {broke}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
56 112
```
