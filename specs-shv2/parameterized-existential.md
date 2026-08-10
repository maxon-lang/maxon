---
feature: parameterized-existential
status: experimental
keywords: [interface, existential, witness, associated, uses, with, generic]
category: types
---

# An INSTANCE of an interface is an existential

## Documentation

`Seq with Integer` — a `with` clause over an interface rather than over a `type` — names the existential
`Seq`, held with its associated type `Element` bound to `Integer`. It is a fat pointer exactly as the bare
`Seq` is (`MaxonType.interfaceRef`), it widens from any conformer, and every method the interface requires
dispatches through the table the value carries.

⭐⭐ **THE `with` ARGUMENTS ARE NOT RECORDED ON THE TYPE — THEY ARE CHECKED AGAINST THE CONFORMANCES,
WHICH ALREADY RECORD THEM ONCE.** An interface's associated type has exactly ONE legal binding across a
program that DISPATCHES through it: that is E3119, and it is forced rather than chosen — under
dictionary-passing the shared body is compiled once, against one type, and every other conformer
reinterprets those bits. Producing an existential IS such a dispatch. So the binding is a function of the
interface NAME, the conformances are where the program states it, and a second copy carried on the type
would be one fact written twice with nothing keeping the copies honest.

⇒ what a use site writes is a CLAIM the compiler verifies. `Seq with Integer` where every conformer binds
`Element` to `Integer` is admitted; `Seq with Text` against those same conformers is **E3125**, naming both
bindings and the conformer that settled the program's.

⛔ **AN ASSOCIATED TYPE HELD AT AN INTERFACE IS NOT YET CARRYABLE.** `Bag with (Integer, SomeCursor)` where
`SomeCursor` is itself an interface asks a requirement to hand back a value at an interface type — two words,
and a witness call carries one machine word per result, so the cursor would arrive with no table to dispatch
through. Supplying it needs a SECOND witness table travelling with the first, which shv2 does not carry yet.
It is its own `DeclaredStoragePosition` (`associatedTypeBinding`) rather than the `containerElement` sentence
the same reader gives every other type argument, because a parameterized interface has no element slot and
being told it has one names a container the program does not have. It is the wall `stdlib/Array.maxon`'s
`Iterable with (Element, ElementIterator)` stands at.

## Tests

<!-- test: parameterized.cursor-protocol -->
⭐ **THE PROGRAM THAT USED TO BE THE WALL.** `Seq uses Element`, `Upto implements Seq with Integer`, and the
parameter is written at the INSTANCE. Both protocol calls are witness dispatches through the fat pointer.
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
```exitcode
0
```
```stdout
10
```

<!-- test: parameterized.two-conformers -->
⭐⭐ **THE DISPATCH THROUGH A PARAMETERIZED EXISTENTIAL IS GENUINELY DYNAMIC.** Two conformers reach ONE
parameter, and a merge phi decides which at run time — so no static reading of the source can fold the
witness away, and the two answers differ.
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

type Down implements Seq with Integer
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

typealias IntegerSeq = Seq with Integer

function total(source IntegerSeq) returns Integer
	var sum = 0 as Integer
	for item in source 'walk'
		sum = sum + item
	end 'walk'
	return sum
end 'total'

function makeUp() returns IntegerSeq
	return Upto.create(4)
end 'makeUp'

function makeDown() returns IntegerSeq
	return Down.create(3)
end 'makeDown'

function pick(up bool) returns Integer
	var s = makeUp()
	if not up 'other'
		s = makeDown()
	end 'other'
	return total(s)
end 'pick'

function main() returns ExitCode
	print("{pick(true)}\n")
	print("{pick(false)}\n")
	print("{total(Upto.create(2)) + total(Down.create(2))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
6
6
```

<!-- test: parameterized.managed-values -->
⚠ **THE VALUE THE LOOP HANDS THE BODY IS AN OWNED HEAP RECORD.** The interface is parameterized and the
traversal element is a `String` the conformer MINTS per call, so each trip must release exactly once. A leak
or a double release is a non-zero exit here, not a wrong number.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Lines uses Tag
	function tag() returns Tag
	function current() returns String
	function advance() throws IterationError
end 'Lines'

type Numbered implements Lines with Integer
	var pos as Integer
	let limit as Integer

	export static function create(limit Integer) returns Self
		return Self{pos: 1, limit: limit}
	end 'create'

	export function tag() returns Integer
		return self.limit
	end 'tag'

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

typealias IntegerLines = Lines with Integer

function widthOf(source IntegerLines) returns Integer
	var total = 0 as Integer
	for text in source 'walk'
		total = total + text.byteLength()
	end 'walk'
	return total + source.tag()
end 'widthOf'

function main() returns ExitCode
	var i = 0 as Integer
	var seen = 0 as Integer
	while i < 200 'many'
		seen = widthOf(Numbered.create(4))
		i = i + 1
	end 'many'
	print("{seen}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
228
```

<!-- test: parameterized.early-exits -->
⚠ **THE PER-TRIP RELEASE ON THE EXITS THE FALL-THROUGH CASE CANNOT REACH.** A `return` out of the body and a
`break` each leave a promoted per-trip temporary live; over 200 rounds a missed release is exit 101 and a
double release is a fault.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Lines uses Tag
	function tag() returns Tag
	function current() returns String
	function advance() throws IterationError
end 'Lines'

type Numbered implements Lines with Integer
	var pos as Integer
	let limit as Integer

	export static function create(limit Integer) returns Self
		return Self{pos: 1, limit: limit}
	end 'create'

	export function tag() returns Integer
		return self.limit
	end 'tag'

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

typealias IntegerLines = Lines with Integer

function returnsOutOfTheBody(source IntegerLines) returns Integer
	for text in source 'walk'
		if text.byteLength() > 0 'theFirst'
			return text.byteLength()
		end 'theFirst'
	end 'walk'
	return 0
end 'returnsOutOfTheBody'

function breaksOutOfTheBody(source IntegerLines) returns Integer
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

<!-- test: parameterized.every-producer-of-one -->
Every way a program can hand a parameterized existential on: a CALL RESULT, a LOCAL binding, and a struct
FIELD declared at the instance. Each is its own producer of the witness half, and an unpaired one panics the
compiler rather than answering.
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

type Holder
	export var seq as IntegerSeq

	export static function create(seq IntegerSeq) returns Self
		return Self{seq: seq}
	end 'create'
end 'Holder'

function makeSeq(limit Integer) returns IntegerSeq
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

<!-- test: error.binding-contradicts-the-conformers -->
⭐⭐ **A WRITTEN BINDING IS A CLAIM, AND A FALSE ONE IS REFUSED RATHER THAN IGNORED.** The conformers bind
`Element` to `Integer`; the use site writes `Text`. Nothing in the program could make that true — one
dispatched interface has one binding — so accepting it would type every dispatch off a claim no conformer
honours.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Text = int(0 to 255)

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

typealias TextSeq = Seq with Text

function total(source TextSeq) returns Integer
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
error E3125: <fragment>:30:11: this use of 'Seq' binds its associated type 'Element' to 'Text', but 'Upto' binds it to 'Integer' — an interface this program DISPATCHES through has exactly one binding per associated type (E3119's rule), so a use site does not choose one, it states the one the conformances already settled. Write the binding the conformers declare, or give the two readings different interfaces
```

<!-- test: error.associated-type-held-at-an-interface -->
⛔ **THE SECOND TABLE shv2 DOES NOT CARRY.** `Iter` is bound to an INTERFACE, so `createIterator()` would have
to hand back a value plus the table to walk it through — two words out of a call that returns one. This is
the wall `stdlib/Array.maxon`'s `Iterable with (Element, ElementIterator)` stands at, refused where the
binding is written rather than as a wrong noun further down.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor uses Element
	function current() returns Element
	function advance() throws IterationError
end 'Cursor'

interface Bag uses Element, Iter
	function createIterator() returns Iter throws IterationError
end 'Bag'

type UptoCursor implements Cursor with Integer
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
end 'UptoCursor'

type Upto implements Bag with (Integer, UptoCursor)
	let limit as Integer

	export static function create(limit Integer) returns Self
		return Self{limit: limit}
	end 'create'

	export function createIterator() returns UptoCursor throws IterationError
		return try UptoCursor.create(self.limit)
	end 'createIterator'
end 'Upto'

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
	let u = Upto.create(4)
	print("{total(u)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:49:43: Unsupported: an interface instance's associated-type binding declared at the interface type 'Cursor' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a witness call carries one machine word per result, so the value a requirement returned would arrive with no table to dispatch through — and the table differs per conformer, so it would have to travel beside the one the existential already carries. Bind the associated type to a concrete conformer, and hold values of it at that type
```

<!-- test: error.associated-type-bound-to-an-existential-alias -->
⚠ **E3120 MUST STILL CATCH THE BINDING WHEN IT IS SPELLED AS AN ALIAS**, and what makes it do so is a
RENDERING rather than a second predicate: `ProgramSignatures.declaredNameIsFatPointer` knows only the bare
interface name, and `Parser.readConformanceWithArg` renders `IntegerSeq` to exactly that — `Seq` — so the
alias arrives at the bare spelling's refusal. This case pins that rendering, because the alias only began
denoting an existential in this rung and nothing else would notice if it started rendering to `Seq_Integer`
instead: the binding would slip past E3120 and the dispatch would carry one machine word where the impl reads
two, which is the exit 139 `associated-types.error.associated-type-bound-to-an-interface` measured.
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

interface Taker uses Element
	function take(e Element) returns Integer
end 'Taker'

type Runner implements Taker with IntegerSeq
	let base as Integer

	export static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'

	export function take(e IntegerSeq) returns Integer
		return self.base + e.current()
	end 'take'
end 'Runner'

function useIt(t Taker) returns Integer
	return t.take(Upto.create(11))
end 'useIt'

function main() returns ExitCode
	print("{useIt(Runner.create(20))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3120: <fragment>:35:6: 'Runner' binds 'Taker's associated type 'Element' to the interface type 'Seq', and 'Element' reaches the calling convention of a requirement this program DISPATCHES — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a witness call carries one machine word per argument and one per result — so the second word is dropped and the impl reads a witness that was never passed. Bind the associated type to a concrete type
```
