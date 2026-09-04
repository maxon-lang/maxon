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
	function advance() throws IterationError
end 'Cursor'

interface Bag uses Element, Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type UpCursor implements Cursor with Integer
	var pos as Integer
	let limit as Integer

	export static function create(limit Integer) returns Self throws IterationError
		if limit < 1 'empty'
			throw IterationError.exhausted
		end 'empty'
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
end 'UpCursor'

type UpBag implements Bag with (Integer, UpCursor)
	let limit as Integer

	export static function create(limit Integer) returns Self
		return Self{limit: limit}
	end 'create'

	export function createIterator() returns UpCursor throws IterationError
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
	function advance() throws IterationError
end 'Cursor'

interface Bag uses Element, Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type UpCursor implements Cursor with Integer
	var pos as Integer
	let limit as Integer

	export static function create(limit Integer) returns Self throws IterationError
		if limit < 1 'empty'
			throw IterationError.exhausted
		end 'empty'
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
end 'UpCursor'

type DownCursor implements Cursor with Integer
	var pos as Integer

	export static function create(start Integer) returns Self throws IterationError
		if start < 1 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{pos: start}
	end 'create'

	export function current() returns Integer
		return self.pos * 100
	end 'current'

	export function advance() throws IterationError
		if self.pos <= 1 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos - 1
	end 'advance'
end 'DownCursor'

type UpBag implements Bag with (Integer, UpCursor)
	let limit as Integer

	export static function create(limit Integer) returns Self
		return Self{limit: limit}
	end 'create'

	export function createIterator() returns UpCursor throws IterationError
		return try UpCursor.create(self.limit)
	end 'createIterator'
end 'UpBag'

type DownBag implements Bag with (Integer, DownCursor)
	let start as Integer

	export static function create(start Integer) returns Self
		return Self{start: start}
	end 'create'

	export function createIterator() returns DownCursor throws IterationError
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
	function advance() throws IterationError
end 'Lines'

interface Doc uses Walk
	function createIterator() returns Walk throws IterationError
end 'Doc'

type NumberedLines implements Lines
	var pos as Integer
	let limit as Integer

	export static function create(limit Integer) returns Self throws IterationError
		if limit < 1 'empty'
			throw IterationError.exhausted
		end 'empty'
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
end 'NumberedLines'

type ShoutedLines implements Lines
	var pos as Integer
	let limit as Integer

	export static function create(limit Integer) returns Self throws IterationError
		if limit < 1 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{pos: 1, limit: limit}
	end 'create'

	export function current() returns String
		return "A MANAGED LINE LONG ENOUGH TO LIVE ON THE HEAP, NUMBER {self.pos}!!"
	end 'current'

	export function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'
end 'ShoutedLines'

type PlainDoc implements Doc with NumberedLines
	let lineCount as Integer

	export static function create(lineCount Integer) returns Self
		return Self{lineCount: lineCount}
	end 'create'

	export function createIterator() returns NumberedLines throws IterationError
		return try NumberedLines.create(self.lineCount)
	end 'createIterator'
end 'PlainDoc'

type LoudDoc implements Doc with ShoutedLines
	let lineCount as Integer

	export static function create(lineCount Integer) returns Self
		return Self{lineCount: lineCount}
	end 'create'

	export function createIterator() returns ShoutedLines throws IterationError
		return try ShoutedLines.create(self.lineCount)
	end 'createIterator'
end 'LoudDoc'

typealias LineDoc = Doc with Lines

function widthOf(source LineDoc) returns Integer
	var total = 0 as Integer
	for text in source 'walk'
		total = total + text.byteLength()
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
	function advance() throws IterationError
end 'Cursor'

interface Sink uses Item
	function take(it Item) returns Integer
end 'Sink'

type OneCursor implements Cursor
	var pos as Integer

	export static function create() returns Self
		return Self{pos: 7}
	end 'create'

	export function current() returns Integer
		return self.pos
	end 'current'

	export function advance() throws IterationError
		throw IterationError.exhausted
	end 'advance'
end 'OneCursor'

type Adder implements Sink with OneCursor
	let base as Integer

	export static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'

	export function take(it OneCursor) returns Integer
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
	function advance() throws IterationError
end 'Cursor'

interface Bag uses Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type Tally
	var pos as Integer

	export static function create() returns Self
		return Self{pos: 3}
	end 'create'

	export function current() returns Integer
		return self.pos
	end 'current'
end 'Tally'

type OddBag implements Bag with Tally
	let seed as Integer

	export static function create(seed Integer) returns Self
		return Self{seed: seed}
	end 'create'

	export function createIterator() returns Tally throws IterationError
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
⚠⚠ **THE HOLDING'S OBLIGATION IS TRIGGERED BY A WIDENING, NOT BY A DISPATCH, AND GETTING THAT WRONG WAS A
COMPILER PANIC.** E3119/E3120 are gated on the interface being DISPATCHED, because their subject is a shared
BODY and only a dispatch compiles one. A holding's subject is the nested TABLE, and a table is minted the
moment a conformer is WIDENED — so this program, which passes an `OddBag` at a held existential and never
calls anything on it, reached `ensureWitnessTable` and panicked with
`witnessSlotImpl: no conformance selected a member for slot 'Tally.Cursor.current'`. The same E3126 the
dispatched case gets, from a check that shares neither of the other two's gates.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor
	function current() returns Integer
	function advance() throws IterationError
end 'Cursor'

interface Bag uses Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type Tally
	var pos as Integer

	export static function create() returns Self
		return Self{pos: 3}
	end 'create'
end 'Tally'

type OddBag implements Bag with Tally
	let seed as Integer

	export static function create(seed Integer) returns Self
		return Self{seed: seed}
	end 'create'

	export function createIterator() returns Tally throws IterationError
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

	export static function create() returns Self
		return Self{pos: 5}
	end 'create'

	export function current() returns Integer
		return self.pos
	end 'current'
end 'OneCursor'

type WeirdBag implements Bag with Cursor
	let seed as Integer

	export static function create(seed Integer) returns Self
		return Self{seed: seed}
	end 'create'

	export function makeIt() returns Cursor
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
⭐⭐ **THE PROGRAM `stdlib/Array.maxon:132` STANDS AT, AND THE ONE `Array.from` IS.** A generic type walking a
value held at a parameterized interface over its OWN parameter — `Bag with (Element, Cursor with Element)`
inside `type Collector uses Element` — and storing each element into its `Array with Element`.

The element is `current()`'s result, and typing it is the whole of this case. An interface's requirement
returns the interface's own associated-type NAME, so the shared body reads `Element`; the receiver's
instantiation is what says WHICH type that is, and for a shared generic body the answer is the enclosing
type's own parameter. Read as a concrete `named("Element")` instead, the store was refused as
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
	function advance() throws IterationError
end 'Cursor'

interface Bag uses Element, Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type Collector uses Element
	typealias ElementArray = Array with Element
	typealias ElementCursor = Cursor with Element
	typealias ElementBag = Bag with (Element, ElementCursor)
	export var items as ElementArray

	export static function create() returns Self
		return Self{items: ElementArray.create()}
	end 'create'

	export static function from (source ElementBag) returns Self
		var result = ElementArray.create()
		for item in source 'collect'
			result.push(item)
		end 'collect'
		return Self{items: result}
	end 'from'
end 'Collector'

typealias IntCollector = Collector with Integer

function main() returns ExitCode
	var c = IntCollector.create()
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
⭐⭐ **THIS CASE PINNED A REFUSAL, AND W58 INVERTED IT — the program below is CORRECT and now compiles.** It
was `error.a-use-site-binding-a-type-parameter-names-it`, and its subject was the SPELLING in E3125's sentence:
a TYPE-PARAMETER argument was rendered by `mangleTypeArg` as the W14 digest `'Td0c4d4635e31e169'`, so the
refusal rested on a hash not matching a source name rather than on two types differing. Fixing the rendering
left the refusal standing on `'Element'` vs `'Integer'` — and THAT comparison is the thing W58 found to be
meaningless. `typealias ElementCursor = Cursor with Element` inside `type Counter uses Element` states nothing
about the program; it states something different at every `Counter with X`, and E3125 has no instantiation in
hand to substitute. So the claim is DEFERRED to the widening (E3127), and here there is no widening to make:
nothing instantiates `Counter` and nothing calls `Counter.of`.
⚠ The rendering fact this case was written for did NOT go with the refusal — `associated-types.md`'s
`error.existential-return-claim-cannot-be-resolved-in-the-declaration-view` pins an E3127 sentence reading
`bound to 'T'`, the author's own spelling, which is the same guarantee against the same digest.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor uses Element
	function current() returns Element
	function tag() returns Integer
	function advance() throws IterationError
end 'Cursor'

type UpCursor implements Cursor with Integer
	var pos as Integer
	let limit as Integer

	export static function create(limit Integer) returns Self throws IterationError
		if limit < 1 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{pos: 1, limit: limit}
	end 'create'

	export function tag() returns Integer
		return self.limit
	end 'tag'

	export function current() returns Integer
		return self.pos
	end 'current'

	export function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'
end 'UpCursor'

type Counter uses Element
	typealias ElementCursor = Cursor with Element
	export var seen as Integer

	export static function of (source ElementCursor) returns Self
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
that is not a declaration at all, and the answer can only be no: the refusal read *"'UpCursor_Integer' does
not implement 'Cursor'"* on a program whose `type UpCursor uses Slot implements Cursor` says otherwise on its
own line.

⚠ Under dictionary-passing the conformance is a property of the DECLARATION — `UpCursor.current` is compiled
once over an opaque layout and one `__witness_UpCursor.Cursor` answers for every instantiation — which is why
the base is the right thing to ask, and it is the reduction `conformerNameOfDeclaredName` already owned for
every other conformance door. The arguments are not lost by it: the base’s own `where` clause is checked at
the INSTANTIATION site by E3017, which is where a conditional conformance lives.

⚠ The same reduction is owed by the LOWERING, and asking it in only one of the two places is worse than
asking it in neither. `stampAssociatedWitnessSlots` named `__witness_UpCursor_Integer.Cursor` and
`witnessSlotImpl` panicked; `existentialDestroyCallee` reached a drop router with no arm for a canonical
mint, on a name `declaredNameIsManaged` had already called managed. This program exercises the accept, the
table and the drop.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor
	function current() returns Integer
	function advance() throws IterationError
end 'Cursor'

interface Bag uses Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type UpCursor uses Slot implements Cursor
	var pos as Integer
	let limit as Integer
	let spare as Slot

	export static function create(limit Integer, spare Slot) returns Self throws IterationError
		if limit < 1 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{pos: 1, limit: limit, spare: spare}
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
end 'UpCursor'

typealias IntUpCursor = UpCursor with Integer

type UpBag implements Bag with IntUpCursor
	let limit as Integer

	export static function create(limit Integer) returns Self
		return Self{limit: limit}
	end 'create'

	export function createIterator() returns IntUpCursor throws IterationError
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
	function advance() throws IterationError
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

	export static function create(limit Integer) returns Self throws IterationError
		if limit < 1 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{pos: 1, limit: limit}
	end 'create'

	export function current() returns Element
		return self.pos
	end 'current'

	export function advance() throws IterationError
		if self.pos >= self.limit 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		self.pos = self.pos + 1
	end 'advance'

	export function retreat() throws IterationError
		throw IterationError.exhausted
	end 'retreat'
end 'UpCursor'

typealias IntUpCursor = UpCursor with Integer

type UpBag implements Bag with (Integer, IntUpCursor)
	let limit as Integer

	export static function create(limit Integer) returns Self
		return Self{limit: limit}
	end 'create'

	export function createIterator() returns IntUpCursor throws IterationError
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
conformance reaches the holding only through `extends`. It was already accepted — `typeDeclaresInterface`
walks `extendsInterfaces` for every conformance question in the compiler — so a red here attributes a
regression to the `extends` walk rather than to the instance-name reduction.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor
	function current() returns Integer
	function advance() throws IterationError
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

	export static function create(limit Integer) returns Self throws IterationError
		if limit < 1 'empty'
			throw IterationError.exhausted
		end 'empty'
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

	export function retreat() throws IterationError
		throw IterationError.exhausted
	end 'retreat'
end 'Tally'

type UpBag implements Bag with Tally
	let limit as Integer

	export static function create(limit Integer) returns Self
		return Self{limit: limit}
	end 'create'

	export function createIterator() returns Tally throws IterationError
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
	function advance() throws IterationError
end 'Cursor'

interface Bag uses Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type Tally uses Element
	var pos as Integer

	export static function create() returns Self
		return Self{pos: 3}
	end 'create'

	export function current() returns Integer
		return self.pos
	end 'current'
end 'Tally'

typealias IntTally = Tally with Integer

type OddBag implements Bag with IntTally
	let seed as Integer

	export static function create(seed Integer) returns Self
		return Self{seed: seed}
	end 'create'

	export function createIterator() returns IntTally throws IterationError
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
