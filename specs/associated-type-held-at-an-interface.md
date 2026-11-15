---
feature: associated-type-held-at-an-interface
status: experimental
keywords: [interface, existential, witness, associated, uses, with, nested]
category: types
---

# An associated type held at an interface

## Documentation

`Bag with (Integer, SomeCursor)` — where `SomeCursor` is itself an INTERFACE — asks a requirement to hand
back a value at an interface type. That value is a two-word fat pointer `(value, witness)`, and a witness
call carries one machine word per result, so the cursor arrives with a value and no table to dispatch
through. The table it needs is not one table either: each conformer binds the associated type to its own
concrete cursor, so the table differs per conformer.

⭐⭐ **THE SECOND TABLE TRAVELS IN THE FIRST.** A witness table's slots are its interface's transitive
requirements; past them, an interface with a HELD associated position carries one further slot per associated
position. Slot `methodCount + p` of `__witness_<Conformer>.<Iface>` holds the SELF-RELATIVE byte offset of
`__witness_<ConformerBinding>.<HeldAt>` — the nested table for whatever THAT conformer bound position `p` to.
A dispatch whose result is such an associated type therefore takes its **value** half from the call and its
**witness** half from a load off the source existential's own table, plus the add that turns the offset back
into an address. No ABI changes, no per-instantiation tables, and "conformers bind `Iter` differently" works
by construction, because each conformer's table names its own nested one.

⚠ **THE OFFSET IS SELF-RELATIVE BECAUSE `.rdata` HAS NO RELOCATION TO ITSELF.** The one channel a `.rdata`
slot has is `funcAbs64InRdata`, whose target is a `.text` function, and its value is format-specific in three
different ways (an absolute VA on fixed-base PE/ELF, a funcref-table index on wasm, a dyld chained-fixup
rebase on Mach-O). Two labels in ONE section are a different question: their distance is a link-time constant
on every target and under PIE, so the slot needs no relocation kind at all.

⭐ **WHERE THE HOLDING IS DECLARED — the USE SITE, and it is not a second copy of the conformances.** The
conformances say what each conformer binds; NOTHING in them says the program means to hold those values
existentially rather than at one concrete type. That is what `Bag with (Integer, IntegerCursor)` adds, and it
is a whole-program fact keyed by `(interface, position)` exactly as E3119's one-binding-per-associated-type
rule is: the holding IS that position's ABI binding, and the conformers' concrete bindings are the types
widened into it.

⛔ **ONLY A RESULT CAN BE RESCUED THIS WAY, AND A PARAMETER GENUINELY CANNOT BE.** A requirement's RESULT is
produced BY the impl, whose own conformance pins the concrete type statically — so its nested table is a
compile-time constant and can live in a static slot. An ARGUMENT's witness travels IN from the caller and is
a different conformer at every call site, so no static slot can be right about it. An associated type held at
an interface that is written as a requirement's PARAMETER is refused where the holding is written.

## Tests

<!-- test: held.nested-dispatch -->
⭐ **THE PROGRAM `stdlib/Array.maxon` STANDS AT.** `Bag`'s `Iter` is held at `Cursor`, so `createIterator()`
hands back an existential and the loop walks it through a table it loaded out of the bag's own table.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor uses Element
	function current() returns Element
	function advance(mutable self) throws IterationError
end 'Cursor'

interface Bag uses Element, Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type UpCursor implements Cursor with Integer
	var pos as Integer
	let limit as Integer

	static function create(limit Integer) returns Self throws IterationError
		if limit < 1 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{pos: 1, limit: limit}
	end 'create'

	function current() returns Integer
		return self.pos
	end 'current'

	function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'
end 'UpCursor'

type UpBag implements Bag with (Integer, UpCursor)
	let limit as Integer

	static function create(limit Integer) returns Self
		return Self{limit: limit}
	end 'create'

	function createIterator() returns UpCursor throws IterationError
		return try UpCursor.create(self.limit)
	end 'createIterator'
end 'UpBag'

typealias IntegerCursor = Cursor with Integer
typealias IntegerBag = Bag with (Integer, IntegerCursor)

function total(source IntegerBag) returns Integer
	var sum = 0 as Integer
	for item in source 'walk'
		sum = sum + item
	end 'walk'
	return sum
end 'total'

function main() returns ExitCode
	print("{total(UpBag.create(4))}\n")
	print("{total(UpBag.create(0))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
0
```

<!-- test: held.two-conformers-bind-differently -->
⭐⭐ **THE CASE THE WHOLE MECHANISM EXISTS FOR, AND THE ONE A SINGLE STATIC TABLE GETS WRONG.** `UpBag` binds
`Iter` to `UpCursor` and `DownBag` binds it to `DownCursor` — two different concrete types under one held
position — and a merge phi decides at RUN TIME which of them the loop walks. The two cursors answer
differently on purpose, so a nested table picked statically would print one of these lines twice.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor uses Element
	function current() returns Element
	function advance(mutable self) throws IterationError
end 'Cursor'

interface Bag uses Element, Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type UpCursor implements Cursor with Integer
	var pos as Integer
	let limit as Integer

	static function create(limit Integer) returns Self throws IterationError
		if limit < 1 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{pos: 1, limit: limit}
	end 'create'

	function current() returns Integer
		return self.pos
	end 'current'

	function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'
end 'UpCursor'

type DownCursor implements Cursor with Integer
	var pos as Integer

	static function create(start Integer) returns Self throws IterationError
		if start < 1 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{pos: start}
	end 'create'

	function current() returns Integer
		return self.pos * 100
	end 'current'

	function advance() throws IterationError
		if self.pos <= 1 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos - 1
	end 'advance'
end 'DownCursor'

type UpBag implements Bag with (Integer, UpCursor)
	let limit as Integer

	static function create(limit Integer) returns Self
		return Self{limit: limit}
	end 'create'

	function createIterator() returns UpCursor throws IterationError
		return try UpCursor.create(self.limit)
	end 'createIterator'
end 'UpBag'

type DownBag implements Bag with (Integer, DownCursor)
	let start as Integer

	static function create(start Integer) returns Self
		return Self{start: start}
	end 'create'

	function createIterator() returns DownCursor throws IterationError
		return try DownCursor.create(self.start)
	end 'createIterator'
end 'DownBag'

typealias IntegerCursor = Cursor with Integer
typealias IntegerBag = Bag with (Integer, IntegerCursor)

function total(source IntegerBag) returns Integer
	var sum = 0 as Integer
	for item in source 'walk'
		sum = sum + item
	end 'walk'
	return sum
end 'total'

function makeUp() returns IntegerBag
	return UpBag.create(4)
end 'makeUp'

function makeDown() returns IntegerBag
	return DownBag.create(3)
end 'makeDown'

function pick(up bool) returns Integer
	var b = makeUp()
	if not up 'other'
		b = makeDown()
	end 'other'
	return total(b)
end 'pick'

function main() returns ExitCode
	print("{pick(true)}\n")
	print("{pick(false)}\n")
	print("{total(UpBag.create(4)) + total(DownBag.create(3))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
600
610
```

<!-- test: held.managed-values-through-the-nested-dispatch -->
⚠ **AN OWNED HEAP RECORD ON BOTH SIDES OF THE NESTED DISPATCH.** The CURSOR the outer dispatch returns is
itself a heap record the loop owns and must release through the NESTED table's `destroyFunc@8`, and each trip
mints a `String` the body must release too. Over 200 rounds a missed release is exit 101 and a double release
is a fault, so the EXIT CODE is the subject here as much as the number.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Lines
	function current() returns String
	function advance(mutable self) throws IterationError
end 'Lines'

interface Doc uses Walk
	function createIterator() returns Walk throws IterationError
end 'Doc'

type NumberedLines implements Lines
	var pos as Integer
	let limit as Integer

	static function create(limit Integer) returns Self throws IterationError
		if limit < 1 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{pos: 1, limit: limit}
	end 'create'

	function current() returns String
		return "a managed line long enough to live on the heap, number {self.pos}"
	end 'current'

	function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'
end 'NumberedLines'

type ShoutedLines implements Lines
	var pos as Integer
	let limit as Integer

	static function create(limit Integer) returns Self throws IterationError
		if limit < 1 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{pos: 1, limit: limit}
	end 'create'

	function current() returns String
		return "A MANAGED LINE LONG ENOUGH TO LIVE ON THE HEAP, NUMBER {self.pos}!!"
	end 'current'

	function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'
end 'ShoutedLines'

type PlainDoc implements Doc with NumberedLines
	let lineCount as Integer

	static function create(lineCount Integer) returns Self
		return Self{lineCount: lineCount}
	end 'create'

	function createIterator() returns NumberedLines throws IterationError
		return try NumberedLines.create(self.lineCount)
	end 'createIterator'
end 'PlainDoc'

type LoudDoc implements Doc with ShoutedLines
	let lineCount as Integer

	static function create(lineCount Integer) returns Self
		return Self{lineCount: lineCount}
	end 'create'

	function createIterator() returns ShoutedLines throws IterationError
		return try ShoutedLines.create(self.lineCount)
	end 'createIterator'
end 'LoudDoc'

typealias LineDoc = Doc with Lines

function widthOf(source LineDoc) returns Integer
	var total = 0 as Integer
	for text in source 'walk'
		total = total + (text.byteLength() as Integer)
	end 'walk'
	return total
end 'widthOf'

function main() returns ExitCode
	var i = 0 as Integer
	var plain = 0 as Integer
	var loud = 0 as Integer
	while i < 200 'many'
		plain = widthOf(PlainDoc.create(4))
		loud = widthOf(LoudDoc.create(4))
		i = i + 1
	end 'many'
	print("{plain} {loud}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
224 232
```

<!-- test: error.held-position-reaches-a-parameter -->
⛔ **A HELD ASSOCIATED TYPE IN AN ARGUMENT POSITION IS REFUSED, AND THE RESULT POSITION IS NOT.** The witness
the argument carries is the CALLER's, a different conformer at every call site, so no slot in the callee's
table can be right about it — unlike a result, whose conformer is pinned by the impl that produces it.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor
	function current() returns Integer
	function advance(mutable self) throws IterationError
end 'Cursor'

interface Sink uses Item
	function take(it Item) returns Integer
end 'Sink'

type OneCursor implements Cursor
	var pos as Integer

	static function create() returns Self
		return Self{pos: 7}
	end 'create'

	function current() returns Integer
		return self.pos
	end 'current'

	function advance() throws IterationError
		throw IterationError.exhausted
	end 'advance'
end 'OneCursor'

type Adder implements Sink with OneCursor
	let base as Integer

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'

	function take(it OneCursor) returns Integer
		return self.base + it.current()
	end 'take'
end 'Adder'

typealias CursorSink = Sink with Cursor

function useIt(s CursorSink) returns Integer
	return s.take(OneCursor.create())
end 'useIt'

function main() returns ExitCode
	print("{useIt(Adder.create(20))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:41:34: Unsupported: an interface instance's associated-type binding declared at the interface type 'Cursor' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a witness call carries one machine word per argument, so an argument's witness half is dropped — and unlike a result, whose conformer the impl that produces it pins statically, an argument's table is the CALLER's and differs per call site, so no slot of the callee's table can supply it. Hold the associated type at an interface only where every requirement that writes it RETURNS it, or bind it to a concrete conformer
```

<!-- test: error.a-conformer-binds-something-that-does-not-conform -->
⭐ **THE HOLDING IS A CLAIM ABOUT EVERY CONFORMER, AND ONE THAT DOES NOT CONFORM IS REFUSED.** Widening
`OddBag`'s `Tally` into a `Cursor` existential would stamp a nested table for a conformance that does not
exist, so the slot would name `__witness_Tally.Cursor` — a symbol nothing mints.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor
	function current() returns Integer
	function advance(mutable self) throws IterationError
end 'Cursor'

interface Bag uses Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type Tally
	var pos as Integer

	static function create() returns Self
		return Self{pos: 3}
	end 'create'

	function current() returns Integer
		return self.pos
	end 'current'
end 'Tally'

type OddBag implements Bag with Tally
	let seed as Integer

	static function create(seed Integer) returns Self
		return Self{seed: seed}
	end 'create'

	function createIterator() returns Tally throws IterationError
		return Tally.create()
	end 'createIterator'
end 'OddBag'

typealias CursorBag = Bag with Cursor

function total(source CursorBag) returns Integer
	var sum = 0 as Integer
	for item in source 'walk'
		sum = sum + item
	end 'walk'
	return sum
end 'total'

function main() returns ExitCode
	print("{total(OddBag.create(1))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3126: <fragment>:25:6: 'OddBag' binds 'Bag's associated type 'Iter' to 'Tally', and this program holds 'Iter' at the interface type 'Cursor' — every value of a held associated type is widened into that existential, so every conformer's binding must conform to it, and 'Tally' does not implement 'Cursor'. Declare the conformance, or hold the associated type at an interface every conformer implements
```

<!-- test: error.a-non-conforming-binding-that-is-never-dispatched -->
⚠⚠ **THE HOLDING'S OBLIGATION IS TRIGGERED BY A WIDENING, NOT BY A DISPATCH, AND GETTING THAT WRONG IS A
COMPILER PANIC.** E3119/E3120 are gated on the interface being DISPATCHED, because their subject is a shared
BODY and only a dispatch compiles one. A holding's subject is the nested TABLE, and a table is minted the
moment a conformer is WIDENED — so this program, which passes an `OddBag` at a held existential and never
calls anything on it, reaches `ensureWitnessTable`, which without the check panics with
`witnessSlotImpl: no conformance selected a member for slot 'Tally.Cursor.current'`. It gets the same E3126
the dispatched case gets, from a check that shares neither of the other two's gates.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor
	function current() returns Integer
	function advance(mutable self) throws IterationError
end 'Cursor'

interface Bag uses Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type Tally
	var pos as Integer

	static function create() returns Self
		return Self{pos: 3}
	end 'create'
end 'Tally'

type OddBag implements Bag with Tally
	let seed as Integer

	static function create(seed Integer) returns Self
		return Self{seed: seed}
	end 'create'

	function createIterator() returns Tally throws IterationError
		return Tally.create()
	end 'createIterator'
end 'OddBag'

typealias CursorBag = Bag with Cursor

function neverDispatches(source CursorBag) returns Integer
	let held = source
	_ = held
	return 7
end 'neverDispatches'

function main() returns ExitCode
	print("{neverDispatches(OddBag.create(1))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3126: <fragment>:21:6: 'OddBag' binds 'Bag's associated type 'Iter' to 'Tally', and this program holds 'Iter' at the interface type 'Cursor' — every value of a held associated type is widened into that existential, so every conformer's binding must conform to it, and 'Tally' does not implement 'Cursor'. Declare the conformance, or hold the associated type at an interface every conformer implements
```

<!-- test: error.a-conformer-binds-the-holding-interface-itself -->
⚠ **A HOLDING SUPPLIES THE SECOND WORD FOR A CONCRETE BINDING, AND HAS NOTHING LEFT TO SUPPLY WHEN THE
BINDING IS ALREADY AN INTERFACE.** `WeirdBag` binds `Iter` to `Cursor` itself, so the impl is asked to hand
back both words out of a witness call that returns one — E3120's fat-pointer clause, which the holding
narrows rather than lifts. The requirement is deliberately NON-throwing: a throwing one is refused one line
earlier and at the impl's own `returns`, by the second-return-register contention
(`DeclaredStoragePosition.throwingReturnType`), so only this shape reaches the clause.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor
	function current() returns Integer
end 'Cursor'

interface Bag uses Iter
	function makeIt() returns Iter
end 'Bag'

type OneCursor implements Cursor
	var pos as Integer

	static function create() returns Self
		return Self{pos: 5}
	end 'create'

	function current() returns Integer
		return self.pos
	end 'current'
end 'OneCursor'

type WeirdBag implements Bag with Cursor
	let seed as Integer

	static function create(seed Integer) returns Self
		return Self{seed: seed}
	end 'create'

	function makeIt() returns Cursor
		return OneCursor.create()
	end 'makeIt'
end 'WeirdBag'

typealias CursorBag = Bag with Cursor

function firstOf(source CursorBag) returns Integer
	return source.makeIt().current()
end 'firstOf'

function main() returns ExitCode
	print("{firstOf(WeirdBag.create(1))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3120: <fragment>:24:6: 'WeirdBag' binds 'Bag's associated type 'Iter' to the interface type 'Cursor', and this program holds 'Iter' at the interface type 'Cursor' — a value held at an interface type is a two-word fat pointer `(value, witness)`, so the impl would have to hand BOTH words back out of a witness call that returns one. A holding supplies the second word for a CONCRETE binding, from a slot naming that conformer's own nested table; there is nothing left to supply when the binding is already an interface. Bind the associated type to a concrete conformer
```

<!-- test: held.the-element-is-the-enclosing-types-own-parameter -->
⭐⭐ **THE PROGRAM `stdlib/Array.maxon`'s `Array.from` IS.** A generic type walking a
value held at a parameterized interface over its OWN parameter — `Bag with (Element, Cursor with Element)`
inside `type Collector uses Element` — and storing each element into its `Array with Element`.

The element is `current()`'s result, and typing it is the whole of this case. An interface's requirement
returns the interface's own associated-type NAME, so the shared body reads `Element`; the receiver's
instantiation is what says WHICH type that is, and for a shared generic body the answer is the enclosing
type's own parameter. Read as a concrete `named("Element")` instead, the store would be refused as
*"storing a borrowed value of a CONCRETE type into an `Array with <type parameter>`"* — a refusal whose own
sentence lists *"a `for` element"* among the things it permits.

⚠ **THE CHAIN IS TWO DISPATCHES, AND BOTH ENDS ARE NEEDED.** `createIterator()` hands back a `Cursor`
existential whose own `Element` nothing in the cursor's type can state — it is bound by the SOURCE's
instantiation, at the position the holding was read at — so the instantiation travels onto the cursor and
`current()` resolves through it there.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor uses Element
	function current() returns Element
	function advance(mutable self) throws IterationError
end 'Cursor'

interface Bag uses Element, Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type Collector uses Element
	typealias ElementArray = Array with Element
	export typealias ElementCursor = Cursor with Element
	export typealias ElementBag = Bag with (Element, ElementCursor)
	export var items as ElementArray

	static function create() returns Self
		return Self{items: ElementArray.create(0)}
	end 'create'

	static function from (source ElementBag) returns Self
		let result = ElementArray.create(0)
		for item in source 'collect'
			result.push(item)
		end 'collect'
		return Self{items: result}
	end 'from'
end 'Collector'

typealias IntCollector = Collector with Integer

function main() returns ExitCode
	let c = IntCollector.create()
	c.items.push(7)
	print("{c.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: a-use-site-binding-a-type-parameter-is-a-claim-about-each-instantiation -->
⭐⭐ **THE PROGRAM BELOW IS CORRECT AND COMPILES.** A comparison of `'Element'` against `'Integer'` at the
declaration is meaningless. `typealias ElementCursor = Cursor with Element` inside `type Counter uses Element` states nothing
about the program; it states something different at every `Counter with X`, and E3125 has no instantiation in
hand to substitute. So the claim is DEFERRED to the widening (E3127), and here there is no widening to make:
nothing instantiates `Counter` and nothing calls `Counter.of`.
⚠ A TYPE-PARAMETER argument is named in the author's own spelling, never as a `mangleTypeArg` digest:
`associated-types.md`'s `error.existential-return-claim-cannot-be-resolved-in-the-declaration-view` pins an
E3127 sentence reading `bound to 'T'`.
```maxon
public typealias Integer = int(i64.min to i64.max)

public interface Cursor uses Element
	function current() returns Element
	function tag() returns Integer
	function advance(mutable self) throws IterationError
end 'Cursor'

public type UpCursor implements Cursor with Integer
	var pos as Integer
	let limit as Integer

	static function create(limit Integer) returns Self throws IterationError
		if limit < 1 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{pos: 1, limit: limit}
	end 'create'

	function tag() returns Integer
		return self.limit
	end 'tag'

	function current() returns Integer
		return self.pos
	end 'current'

	function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'
end 'UpCursor'

public type Counter uses Element
	export typealias ElementCursor = Cursor with Element
	export var seen as Integer

	static function of (source ElementCursor) returns Self
		return Self{seen: source.tag()}
	end 'of'
end 'Counter'

function main() returns ExitCode
	print("ok\n")
	return 0
end 'main'
```
```stdout
ok
```
```exitcode
0
```

<!-- test: held.a-conformer-binds-a-generic-instance -->
⭐⭐ **A CONFORMER MAY BIND A HELD POSITION TO A GENERIC INSTANCE, AND THE QUESTION MUST BE ASKED OF THE
BASE DECLARATION.** A conformance’s `with` argument is recorded by `renderDeclaredTypeName` as the compiler’s
own CANONICAL name (`UpCursor_Integer`), and no `implements` clause is filed under one — the registry is keyed
by the DECLARED struct name. Asking whether `UpCursor_Integer` implements `Cursor` therefore asks about a name
that is not a declaration at all, and the answer can only be no: the refusal would read *"'UpCursor_Integer'
does not implement 'Cursor'"* on a program whose `type UpCursor uses Slot implements Cursor` says otherwise on its
own line.

⚠ Under dictionary-passing the conformance is a property of the DECLARATION — `UpCursor.current` is compiled
once over an opaque layout and one `__witness_UpCursor.Cursor` answers for every instantiation — which is why
the base is the right thing to ask, and it is the reduction `conformerNameOfDeclaredName` owns for
every other conformance door. The arguments are not lost by it: the base’s own `where` clause is checked at
the INSTANTIATION site by E3017, which is where a conditional conformance lives.

⚠ The same reduction is owed by the LOWERING, and asking it in only one of the two places is worse than
asking it in neither: unreduced, `stampAssociatedWitnessSlots` would name `__witness_UpCursor_Integer.Cursor`
and `witnessSlotImpl` would panic, and `existentialDestroyCallee` would reach a drop router with no arm for a
canonical mint, on a name `declaredNameIsManaged` calls managed. This program exercises the accept, the
table and the drop.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor
	function current() returns Integer
	function advance(mutable self) throws IterationError
end 'Cursor'

interface Bag uses Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type UpCursor uses Slot implements Cursor
	var pos as Integer
	let limit as Integer
	let spare as Slot

	static function create(limit Integer, spare Slot) returns Self throws IterationError
		if limit < 1 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{pos: 1, limit: limit, spare: spare}
	end 'create'

	function current() returns Integer
		return self.pos
	end 'current'

	function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'
end 'UpCursor'

typealias IntUpCursor = UpCursor with Integer

type UpBag implements Bag with IntUpCursor
	let limit as Integer

	static function create(limit Integer) returns Self
		return Self{limit: limit}
	end 'create'

	function createIterator() returns IntUpCursor throws IterationError
		return try IntUpCursor.create(self.limit, spare: 0)
	end 'createIterator'
end 'UpBag'

typealias CursorBag = Bag with Cursor

function total(source CursorBag) returns Integer
	var sum = 0 as Integer
	for item in source 'walk'
		sum = sum + item
	end 'walk'
	return sum
end 'total'

function main() returns ExitCode
	print("{total(UpBag.create(4))}\n")
	print("{total(UpBag.create(0))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
0
```

<!-- test: held.a-generic-instance-conforming-through-extends -->
⭐⭐ **`stdlib/Array.maxon`'s OWN SHAPE, TO THE LETTER.** `type ArrayIterator uses Element implements
BidirectionalIterator with Element` with `interface BidirectionalIterator extends Iterator`, bound into
`Array`'s `implements … Iterable with (Element, ArrayIter)` and held at `Iterator` by
`typealias ElementIterator = Iterator with Element`. Both facts are in play at once — the binding is a
generic INSTANCE, and it reaches the holding only THROUGH `extends` — and the two are decided by different
code, so this case is what says they compose.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor uses Element
	function current() returns Element
	function advance(mutable self) throws IterationError
end 'Cursor'

interface BiCursor extends Cursor
	function retreat() throws IterationError
end 'BiCursor'

interface Bag uses Element, Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type UpCursor uses Element implements BiCursor with Element
	var pos as Integer
	let limit as Integer

	static function create(limit Integer) returns Self throws IterationError
		if limit < 1 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{pos: 1, limit: limit}
	end 'create'

	function current() returns Element
		return self.pos
	end 'current'

	function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'

	function retreat() throws IterationError
		throw IterationError.exhausted
	end 'retreat'
end 'UpCursor'

typealias IntUpCursor = UpCursor with Integer

type UpBag implements Bag with (Integer, IntUpCursor)
	let limit as Integer

	static function create(limit Integer) returns Self
		return Self{limit: limit}
	end 'create'

	function createIterator() returns IntUpCursor throws IterationError
		return try IntUpCursor.create(self.limit)
	end 'createIterator'
end 'UpBag'

typealias IntegerCursor = Cursor with Integer
typealias IntegerBag = Bag with (Integer, IntegerCursor)

function total(source IntegerBag) returns Integer
	var sum = 0 as Integer
	for item in source 'walk'
		sum = sum + item
	end 'walk'
	return sum
end 'total'

function main() returns ExitCode
	print("{total(UpBag.create(4))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
```

<!-- test: held.a-conformer-reaching-the-holding-through-extends -->
The FALSE-REJECT CONTROL that separates the two facts of the case above: a CONCRETE conformer whose
conformance reaches the holding only through `extends`. It is accepted — `typeDeclaresInterface`
walks `extendsInterfaces` for every conformance question in the compiler — so a red here attributes a
regression to the `extends` walk rather than to the instance-name reduction.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor
	function current() returns Integer
	function advance(mutable self) throws IterationError
end 'Cursor'

interface BiCursor extends Cursor
	function retreat() throws IterationError
end 'BiCursor'

interface Bag uses Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type Tally implements BiCursor
	var pos as Integer
	let limit as Integer

	static function create(limit Integer) returns Self throws IterationError
		if limit < 1 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{pos: 1, limit: limit}
	end 'create'

	function current() returns Integer
		return self.pos
	end 'current'

	function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'

	function retreat() throws IterationError
		throw IterationError.exhausted
	end 'retreat'
end 'Tally'

type UpBag implements Bag with Tally
	let limit as Integer

	static function create(limit Integer) returns Self
		return Self{limit: limit}
	end 'create'

	function createIterator() returns Tally throws IterationError
		return try Tally.create(self.limit)
	end 'createIterator'
end 'UpBag'

typealias CursorBag = Bag with Cursor

function total(source CursorBag) returns Integer
	var sum = 0 as Integer
	for item in source 'walk'
		sum = sum + item
	end 'walk'
	return sum
end 'total'

function main() returns ExitCode
	print("{total(UpBag.create(4))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
```

<!-- test: error.a-generic-instance-whose-base-does-not-conform -->
⭐ **THE OTHER DIRECTION OF THE SAME REDUCTION: A BASE THAT GENUINELY DOES NOT CONFORM IS STILL REFUSED, AND
THE SENTENCE NAMES THE TYPE THE AUTHOR WROTE.** `Tally` declares no conformance at all, so no reduction can
rescue `Tally with Integer` and the widening is refused exactly as the non-generic case is. What must NOT
survive into the message is `Tally_Integer` — the compiler's own canonical mint, which no source line holds;
the author wrote `IntTally`, and `instanceDisplayName` is the door that says so.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor
	function current() returns Integer
	function advance(mutable self) throws IterationError
end 'Cursor'

interface Bag uses Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type Tally uses Element
	var pos as Integer

	static function create() returns Self
		return Self{pos: 3}
	end 'create'

	function current() returns Integer
		return self.pos
	end 'current'
end 'Tally'

typealias IntTally = Tally with Integer

type OddBag implements Bag with IntTally
	let seed as Integer

	static function create(seed Integer) returns Self
		return Self{seed: seed}
	end 'create'

	function createIterator() returns IntTally throws IterationError
		return IntTally.create()
	end 'createIterator'
end 'OddBag'

typealias CursorBag = Bag with Cursor

function total(source CursorBag) returns Integer
	var sum = 0 as Integer
	for item in source 'walk'
		sum = sum + item
	end 'walk'
	return sum
end 'total'

function main() returns ExitCode
	print("{total(OddBag.create(1))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3126: <fragment>:27:6: 'OddBag' binds 'Bag's associated type 'Iter' to 'IntTally', and this program holds 'Iter' at the interface type 'Cursor' — every value of a held associated type is widened into that existential, so every conformer's binding must conform to it, and 'IntTally' does not implement 'Cursor'. Declare the conformance, or hold the associated type at an interface every conformer implements
```

<!-- test: held.a-generic-conformer-binding-an-instance-over-its-own-parameter-is-frozen -->
`UpBag with Tally` binds `Cursor` to `UpCursor with Slot`, an instance over its own parameter, so the nested table its `Bag` table carries is the instance's own, and a freeze in a program that spawns a service walks the cursor through it.

```maxon
typealias Tally = int(0 to u64.max)

interface Cursor
	function current() returns Tally
end 'Cursor'

interface Bag uses Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type UpCursor uses Slot implements Cursor
	var pos as Tally
	let spare as Slot

	static function create(pos Tally, spare Slot) returns Self
		return Self{pos: pos, spare: spare}
	end 'create'

	function current() returns Tally
		return self.pos
	end 'current'
end 'UpCursor'

type UpBag uses Slot implements Bag with SlotCursor
	typealias SlotCursor = UpCursor with Slot
	let spare as Slot

	static function create(spare Slot) returns Self
		return Self{spare: spare}
	end 'create'

	function createIterator() returns SlotCursor throws IterationError
		return SlotCursor.create(7, spare: self.spare)
	end 'createIterator'
end 'UpBag'

typealias TallyBag = UpBag with Tally
typealias CursorBag = Bag with Cursor

type Holder
	export let cursor as Cursor

	static function create(cursor Cursor) returns Self
		return Self{cursor: cursor}
	end 'create'
end 'Holder'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

function pick(source CursorBag) returns Cursor
	return try source.createIterator() otherwise panic("none")
end 'pick'

function main() returns ExitCode
	let h = spawn Door.create()
	h.shutdown()

	let held = freeze Holder.create(pick(TallyBag.create(1)))
	print("{held.cursor.current()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: error.held-binding-whose-conformer-holds-a-function-value-is-frozen -->
`StepBag` binds `Bag`'s associated type to `StepCursor`, so its `Bag` table carries `StepCursor` to `Cursor` without any widening in a body; a freeze of a value holding a `Cursor` is refused because no share walk can mark `StepCursor`'s function value.

```maxon
typealias Tally = int(0 to u64.max)
typealias Step = function(Tally) returns Tally

interface Cursor
	function current() returns Tally
end 'Cursor'

interface Bag uses Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type PlainCursor implements Cursor
	var pos as Tally

	static function create(pos Tally) returns Self
		return Self{pos: pos}
	end 'create'

	function current() returns Tally
		return self.pos
	end 'current'
end 'PlainCursor'

type StepCursor implements Cursor
	let step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'

	function current() returns Tally
		return self.step(1)
	end 'current'
end 'StepCursor'

type PlainBag implements Bag with PlainCursor
	static function create() returns Self
		return Self{}
	end 'create'

	function createIterator() returns PlainCursor throws IterationError
		return PlainCursor.create(3)
	end 'createIterator'
end 'PlainBag'

type StepBag implements Bag with StepCursor
	static function create() returns Self
		return Self{}
	end 'create'

	function createIterator() returns StepCursor throws IterationError
		return StepCursor.create(function(n Tally) gives n + 1)
	end 'createIterator'
end 'StepBag'

typealias CursorBag = Bag with Cursor

type Holder
	export let cursor as Cursor

	static function create(cursor Cursor) returns Self
		return Self{cursor: cursor}
	end 'create'
end 'Holder'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

function pick(source CursorBag) returns Cursor
	return try source.createIterator() otherwise panic("none")
end 'pick'

function main() returns ExitCode
	let h = spawn Door.create()
	h.shutdown()

	let held = freeze Holder.create(pick(StepBag.create()))
	let other = pick(PlainBag.create())
	print("{held.cursor.current()} {other.current()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:87:13: `Holder` cannot be frozen: its `cursor` is a `Cursor`, and `StepCursor` stands at `Cursor` but no share walk can mark it: its `step` is a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:47:34: `StepCursor` is converted to `Cursor` here
```

<!-- test: error.held-bindings-naming-ever-deeper-instances-are-refused -->
`Nest` holds a value of its parameter and binds the associated type of `A` and of `B`, each held at the other, to an instance that nests `Nest`'s own parameter one level deeper, so the conformers a share walk must judge never end.

```maxon
typealias Tally = int(0 to u64.max)

interface A uses X
	function makeX() returns X
end 'A'

interface B uses Y
	function makeY() returns Y
end 'B'

type Nest uses T implements A with Deeper, B with Deeper
	typealias Deeper = Nest with (Nest with T)
	let n as Tally
	let held as T

	static function create(n Tally, held T) returns Self
		return Self{n: n, held: held}
	end 'create'

	function makeX() returns Deeper
		return Deeper.create(self.n + 1, held: Self{n: self.n, held: self.held})
	end 'makeX'

	function makeY() returns Deeper
		return Deeper.create(self.n + 2, held: Self{n: self.n, held: self.held})
	end 'makeY'
end 'Nest'

typealias HeldA = A with B
typealias HeldB = B with A
typealias TextNest = Nest with String

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Holder
	export let a as A

	static function create(a A) returns Self
		return Self{a: a}
	end 'create'
end 'Holder'

function viaA(source HeldA) returns Tally
	_ = source
	return 0
end 'viaA'

function viaB(source HeldB) returns Tally
	_ = source
	return 0
end 'viaB'

function main() returns ExitCode
	let door = spawn Door.create()
	door.shutdown()
	let frozen = freeze Holder.create(TextNest.create(1, held: "held"))
	print("{viaA(TextNest.create(1, held: "held"))} {viaB(TextNest.create(2, held: "held"))}\n")
	_ = frozen
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:12:36: Unsupported: sharing is judged at every conformer an interface that holds an associated type reaches, and 'TextNest' binds the held `B` to 'Nest.Deeper', an instance whose type arguments nest its own type parameters deeper — so the conformers a share walk must judge are an unbounded chain of ever-deeper instances. Give that type argument a concrete type, or pass the value it wraps as a type parameter of its own
error E2015: <fragment>:12:51: Unsupported: sharing is judged at every conformer an interface that holds an associated type reaches, and 'TextNest' binds the held `A` to 'Nest.Deeper', an instance whose type arguments nest its own type parameters deeper — so the conformers a share walk must judge are an unbounded chain of ever-deeper instances. Give that type argument a concrete type, or pass the value it wraps as a type parameter of its own
```

<!-- test: held-bindings-naming-ever-deeper-instances-over-an-unread-parameter-run -->
`Nest` binds the associated type of `A` and of `B` to an instance that nests its own parameter one level deeper, and nothing reads that parameter, so every conformer a share walk judges is the same.

```maxon
typealias Tally = int(0 to u64.max)

interface A uses X
	function makeX() returns X
end 'A'

interface B uses Y
	function makeY() returns Y
end 'B'

type Nest uses T implements A with Deeper, B with Deeper
	typealias Deeper = Nest with (Nest with T)
	let n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function makeX() returns Deeper
		return Deeper.create(self.n + 1)
	end 'makeX'

	function makeY() returns Deeper
		return Deeper.create(self.n + 2)
	end 'makeY'
end 'Nest'

typealias HeldA = A with B
typealias HeldB = B with A
typealias TextNest = Nest with String

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Holder
	export let a as A

	static function create(a A) returns Self
		return Self{a: a}
	end 'create'
end 'Holder'

function viaA(source HeldA) returns Tally
	_ = source
	return 0
end 'viaA'

function viaB(source HeldB) returns Tally
	_ = source
	return 0
end 'viaB'

function main() returns ExitCode
	let door = spawn Door.create()
	door.shutdown()
	let frozen = freeze Holder.create(TextNest.create(1))
	print("{viaA(TextNest.create(1))} {viaB(TextNest.create(2))}\n")
	_ = frozen
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 0
```

<!-- test: held-binding-chains-follow-only-the-conformers-converted-to-an-interface -->
`Bar` binds `J`'s associated type to `Foo` over its own parameter, and `Foo` binds `I`'s to `Bar` over a `Box` of its own; only `Foo` is converted to `I`, and `I` holds its associated type at an interface with none of its own, so the chain ends at `Bar`.

```maxon
typealias Tally = int(0 to u64.max)

interface Plain0
	function id0() returns Tally
end 'Plain0'

interface I uses A
	function makeA() returns A
end 'I'

interface J uses B
	function makeB() returns B
end 'J'

type Box uses T
	let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

type Foo uses T implements Plain0, I with Next
	typealias Boxed = Box with T
	typealias Next = Bar with Boxed
	let held as T
	let n as Tally

	static function create(held T, n Tally) returns Self
		return Self{held: held, n: n}
	end 'create'

	function id0() returns Tally
		return self.n
	end 'id0'

	function makeA() returns Next
		return Next.create(Boxed.create(self.held), n: self.n + 1)
	end 'makeA'
end 'Foo'

type Bar uses U implements Plain0, J with Back
	typealias Back = Foo with U
	let held as U
	let n as Tally

	static function create(held U, n Tally) returns Self
		return Self{held: held, n: n}
	end 'create'

	function id0() returns Tally
		return self.n
	end 'id0'

	function makeB() returns Back
		panic("makeB is never called")
	end 'makeB'
end 'Bar'

type Other implements Plain0, J with Other
	let n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function id0() returns Tally
		return self.n
	end 'id0'

	function makeB() returns Other
		return Other.create(self.n + 1)
	end 'makeB'
end 'Other'

typealias HeldI = I with Plain0
typealias HeldJ = J with Plain0
typealias TextFoo = Foo with String

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Holder
	export let i as I

	static function create(i I) returns Self
		return Self{i: i}
	end 'create'
end 'Holder'

function viaI(source HeldI) returns Tally
	_ = source
	return 0
end 'viaI'

function viaJ(source HeldJ) returns Tally
	_ = source
	return 0
end 'viaJ'

function main() returns ExitCode
	let door = spawn Door.create()
	door.shutdown()
	let frozen = freeze Holder.create(TextFoo.create("held", n: 1))
	print("{viaI(TextFoo.create("held", n: 1))} {viaJ(Other.create(2))}\n")
	_ = frozen
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 0
```

<!-- test: held-binding-cycle-over-an-unread-parameter-runs -->
`P`, `Q` and `R` bind each other's associated types in a cycle that nests `Cell` around the parameter once, and no `T` is stored anywhere, so the cycle builds no deeper conformer.

```maxon
typealias Tally = int(0 to u64.max)

interface A uses X
	function makeX() returns X
end 'A'

type Cell uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type P uses T implements A with Next
	typealias Next = Q with T
	let n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function makeX() returns Next
		return Next.create(self.n + 1)
	end 'makeX'
end 'P'

type Q uses T implements A with Next
	typealias Next = R with (Cell with T)
	let n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function makeX() returns Next
		return Next.create(self.n + 1)
	end 'makeX'
end 'Q'

type R uses T implements A with Next
	typealias Next = P with T
	let n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function makeX() returns Next
		return Next.create(self.n + 1)
	end 'makeX'
end 'R'

typealias HeldA = A with A
typealias TextP = P with String

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Holder
	export let a as A

	static function create(a A) returns Self
		return Self{a: a}
	end 'create'
end 'Holder'

function viaA(source HeldA) returns Tally
	_ = source
	return 0
end 'viaA'

function main() returns ExitCode
	let door = spawn Door.create()
	door.shutdown()
	let frozen = freeze Holder.create(TextP.create(1))
	print("{viaA(TextP.create(1))}\n")
	_ = frozen
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: held.bindings-differing-in-a-parameter-only-a-nested-instance-reads-keep-their-own-tables -->
`Outer` binds `Bag`'s associated type to `Inner with (Wrap with (X, Y))`. `Wrap` stores only its `X`, so a share walk cannot tell `Wrap with (Tally, bool)` from `Wrap with (Tally, Tally)`; but `Wrap.width` reads `sizeof(Y)`, and `Inner.current` reaches it through its `where W is Probe` witness. The two bindings' `Cursor` tables must stay apart, each handing `current` its own descriptor: 1 and 8.

```maxon
typealias Tally = int(0 to u64.max)

interface Probe
	function width() returns Tally
end 'Probe'

interface Cursor
	function current() returns Tally
end 'Cursor'

interface Bag uses Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type Wrap uses X, Y implements Probe
	let x as X

	static function create(x X) returns Self
		return Self{x: x}
	end 'create'

	function width() returns Tally
		return sizeof(Y)
	end 'width'
end 'Wrap'

type Inner uses W implements Cursor where W is Probe
	let w as W

	static function create(w W) returns Self
		return Self{w: w}
	end 'create'

	function current() returns Tally
		return self.w.width()
	end 'current'
end 'Inner'

type Outer uses X, Y implements Bag with In
	typealias Pairing = Wrap with (X, Y)
	typealias In = Inner with Pairing
	let x as X

	static function create(x X) returns Self
		return Self{x: x}
	end 'create'

	function createIterator() returns In throws IterationError
		return In.create(Pairing.create(self.x))
	end 'createIterator'
end 'Outer'

typealias Wide = int(0 to u64.max)
typealias SmallOuter = Outer with (Tally, bool)
typealias WideOuter = Outer with (Tally, Wide)
typealias CursorBag = Bag with Cursor

type Holder
	export let cursor as Cursor

	static function create(cursor Cursor) returns Self
		return Self{cursor: cursor}
	end 'create'
end 'Holder'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

function pick(source CursorBag) returns Cursor
	return try source.createIterator() otherwise panic("none")
end 'pick'

function main() returns ExitCode
	let h = spawn Door.create()
	h.shutdown()

	let small = freeze Holder.create(pick(SmallOuter.create(1)))
	let wide = freeze Holder.create(pick(WideOuter.create(1)))
	print("{small.cursor.current()} {wide.cursor.current()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 8
```

<!-- test: held.bindings-differing-in-a-held-position-no-share-walk-reaches-keep-their-own-tables -->
`Item` holds its `P` only as the element of an `Array`, a position no share walk of `Item` names. `Item with (Tally, String)` and `Item with (Tally, Tally)` still release different arrays, so each binding's `Cursor` table keeps its own.

```maxon
typealias Tally = int(0 to u64.max)

interface Cursor
	function current() returns Tally
end 'Cursor'

interface Bag uses Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type Item uses T, P implements Cursor
	export let t as T
	typealias PItems = Array with P
	export let items as PItems

	static function create(t T, items PItems) returns Self
		return Self{t: t, items: items}
	end 'create'

	function current() returns Tally
		return self.items.count()
	end 'current'
end 'Item'

type Outer uses X, Y implements Bag with In
	typealias In = Item with (Tally, Y)
	typealias Items = Array with Y
	let y as Y

	static function create(y Y) returns Self
		return Self{y: y}
	end 'create'

	function createIterator() returns In throws IterationError
		let items = Items.create(1, zeroed: false)
		items.push(self.y)
		return In.create(1, items: items)
	end 'createIterator'
end 'Outer'

typealias TextOuter = Outer with (Tally, String)
typealias TallyOuter = Outer with (Tally, Tally)
typealias CursorBag = Bag with Cursor

type Holder
	export let cursor as Cursor

	static function create(cursor Cursor) returns Self
		return Self{cursor: cursor}
	end 'create'
end 'Holder'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

function pick(source CursorBag) returns Cursor
	return try source.createIterator() otherwise panic("none")
end 'pick'

function main() returns ExitCode
	let h = spawn Door.create()
	h.shutdown()

	let tally = freeze Holder.create(pick(TallyOuter.create(1)))
	let text = freeze Holder.create(pick(TextOuter.create("a heap string long enough to allocate")))
	print("{tally.cursor.current()} {text.cursor.current()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1
```

<!-- test: held.bindings-differing-in-a-constrained-position-keep-their-own-tables -->
`Item` constrains `P` with `where P is Weigh` and reads `sizeof(P)` in `current`, which the `Cursor` table's adapter reaches through its descriptor and `P`'s witness; the two bindings' tables stay apart: 8 and 16.

```maxon
typealias Tally = int(0 to u64.max)

interface Weigh
	function weigh() returns Tally
end 'Weigh'

type Light implements Weigh
	let n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function weigh() returns Tally
		return self.n
	end 'weigh'
end 'Light'

type Heavy implements Weigh
	let n as Tally
	let m as Tally

	static function create(n Tally) returns Self
		return Self{n: n, m: n}
	end 'create'

	function weigh() returns Tally
		return self.n + self.m
	end 'weigh'
end 'Heavy'

interface Cursor
	function current() returns Tally
end 'Cursor'

interface Bag uses Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type Item uses T, P implements Cursor where P is Weigh
	let t as T

	static function create(t T) returns Self
		return Self{t: t}
	end 'create'

	function current() returns Tally
		return sizeof(P) as Tally
	end 'current'
end 'Item'

type Outer uses X, Y implements Bag with In where Y is Weigh
	typealias In = Item with (Tally, Y)
	let x as X

	static function create(x X) returns Self
		return Self{x: x}
	end 'create'

	function createIterator() returns In throws IterationError
		return In.create(1)
	end 'createIterator'
end 'Outer'

typealias LightOuter = Outer with (Tally, Light)
typealias HeavyOuter = Outer with (Tally, Heavy)
typealias CursorBag = Bag with Cursor

type Holder
	export let cursor as Cursor

	static function create(cursor Cursor) returns Self
		return Self{cursor: cursor}
	end 'create'
end 'Holder'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

function pick(source CursorBag) returns Cursor
	return try source.createIterator() otherwise panic("none")
end 'pick'

function main() returns ExitCode
	let h = spawn Door.create()
	h.shutdown()

	let light = freeze Holder.create(pick(LightOuter.create(1)))
	let heavy = freeze Holder.create(pick(HeavyOuter.create(1)))
	print("{light.cursor.current()} {heavy.cursor.current()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8 16
```

<!-- test: held.bindings-differing-in-a-constrained-position-nothing-reads-run -->
`Item` constrains `P` and nothing reads it, so the two bindings stay correct whichever tables they get: both answer 1.

```maxon
typealias Tally = int(0 to u64.max)

interface Weigh
	function weigh() returns Tally
end 'Weigh'

type Light implements Weigh
	let n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function weigh() returns Tally
		return self.n
	end 'weigh'
end 'Light'

type Heavy implements Weigh
	let n as Tally
	let m as Tally

	static function create(n Tally) returns Self
		return Self{n: n, m: n}
	end 'create'

	function weigh() returns Tally
		return self.n + self.m
	end 'weigh'
end 'Heavy'

interface Cursor
	function current() returns Tally
end 'Cursor'

interface Bag uses Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type Item uses T, P implements Cursor where P is Weigh
	let n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function current() returns Tally
		return self.n
	end 'current'
end 'Item'

type Outer uses X, Y implements Bag with In where Y is Weigh
	typealias In = Item with (Tally, Y)
	let x as X

	static function create(x X) returns Self
		return Self{x: x}
	end 'create'

	function createIterator() returns In throws IterationError
		return In.create(1)
	end 'createIterator'
end 'Outer'

typealias LightOuter = Outer with (Tally, Light)
typealias HeavyOuter = Outer with (Tally, Heavy)
typealias CursorBag = Bag with Cursor

type Holder
	export let cursor as Cursor

	static function create(cursor Cursor) returns Self
		return Self{cursor: cursor}
	end 'create'
end 'Holder'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

function pick(source CursorBag) returns Cursor
	return try source.createIterator() otherwise panic("none")
end 'pick'

function main() returns ExitCode
	let h = spawn Door.create()
	h.shutdown()

	let light = freeze Holder.create(pick(LightOuter.create(1)))
	let heavy = freeze Holder.create(pick(HeavyOuter.create(1)))
	print("{light.cursor.current()} {heavy.cursor.current()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1
```
