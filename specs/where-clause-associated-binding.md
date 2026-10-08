---
feature: where-clause-associated-binding
status: stable
keywords: [where, constraints, associated, witness, generics, dictionary, instance]
category: type-system
---

# A `where` Constraint Can Bind the Interface's Associated Types

## Documentation

`where Source is Iterator with Element` constrains `Source` to conform to `Iterator` **and** states
what that conformance binds `Iterator`'s associated type to — the enclosing declaration's own
`Element` parameter. It is the `with` clause an `implements` line already carries, written on the
other side of the same conformance.

### Why the binding has to be writable

Inside a shared generic body the receiver of a witness dispatch is a value of a TYPE PARAMETER, and a
type parameter says nothing about what the conformance behind it bound. So `source.current()` — whose
requirement returns the associated type `Element` — has no type to give the result from the receiver
alone, and the position stays UNCLAIMED for E3119, which refuses every program whose conformers bind that
position differently — the overwhelmingly common case, since the whole point of an associated type is that
each conformer picks its own.

`with` on the constraint supplies both answers from one place: the dispatch's result (and its
associated formals) are typed through the binding, and the position counts as CLAIMED at that site.

### What the binding obligates

A binding is a CLAIM about the type argument, checked at each instantiation exactly as `where T is I`
itself is (E3017): the argument must conform, and its conformance must bind each named position to
the type the constraint names, substituted through the instantiation. A disagreement is **E3133**.

### Per-instance witness tables

A generic conforming type shares ONE compiled body across every instantiation, and its witness table
is keyed by the conformer's BASE name for the same reason (`generic-instance-conformance.md`). That
reduction rests on the impls being independent of the type argument. An impl that reads its hidden
dictionary — its type parameter's layout descriptor, or the witness table of one of its own `where`
constraints — is not: a dispatch through one shared table has no instantiation to take the dictionary
from.

Where a witness table is minted for a CONCRETE instance of a generic conformer whose
impls carry a dictionary, the table is minted PER INSTANCE — `__witness_Wrap_IntCur_Integer.Cursor`
rather than `__witness_Wrap.Cursor` — and each of its slots points at a thunk that calls the shared
impl with that instance's own descriptor and witnesses. A conformer whose impls need no dictionary
keeps one shared table.

E3128 remains for the case it is still true of: a witness table minted for a conformer with NO
instance in hand.

## Tests

<!-- test: associated-binding.constrained-parameter-typed-through-the-binding -->
⭐ **THE RESULT OF A DISPATCH THROUGH A CONSTRAINED TYPE PARAMETER.** `s.current()` returns `Cursor`'s
associated `Element`; the constraint says this `Cursor` binds it to `E`; so the tuple `(s, s.current())`
is the declared `(S, E)`, and the function returns it as that type.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor uses Element
	function current() returns Element
end 'Cursor'

type IntCur implements Cursor with Integer
	var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'

	function current() returns Integer
		return self.v
	end 'current'
end 'IntCur'

type Wrap uses S, E where S is Cursor with E
	var s as S

	static function create(s S) returns Self
		return Self{s: s}
	end 'create'

	function pair() returns (S, E)
		return (self.s, self.s.current())
	end 'pair'
end 'Wrap'

typealias IntWrap = Wrap with (IntCur, Integer)

function main() returns ExitCode
	let w = IntWrap.create(IntCur.create(7))
	let (c, e) = w.pair()
	print("{e}:{c.current()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7:7
```

<!-- test: associated-binding.the-constraint-settles-a-disagreement -->
⭐⭐ **TWO CONFORMERS BIND THE POSITION DIFFERENTLY AND THE DISPATCH IS STILL WELL TYPED.** `IntCur`
binds `Cursor`'s `Element` to `Integer` and `TextCur` binds it to `String`; the dispatch inside `Wrap`
names neither, and the constraint's binding types the dispatch. Both instantiations run, through one
compiled body.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor uses Element
	function current() returns Element
end 'Cursor'

type IntCur implements Cursor with Integer
	var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'

	function current() returns Integer
		return self.v
	end 'current'
end 'IntCur'

type TextCur implements Cursor with String
	var v as String

	static function create(v String) returns Self
		return Self{v: v}
	end 'create'

	function current() returns String
		return self.v
	end 'current'
end 'TextCur'

type Wrap uses S, E where S is Cursor with E
	var s as S

	static function create(s S) returns Self
		return Self{s: s}
	end 'create'

	function only() returns E
		return self.s.current()
	end 'only'
end 'Wrap'

typealias IntWrap = Wrap with (IntCur, Integer)
typealias TextWrap = Wrap with (TextCur, String)

function main() returns ExitCode
	let a = IntWrap.create(IntCur.create(41))
	let b = TextWrap.create(TextCur.create("ok"))
	print("{a.only()}:{b.only()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
41:ok
```

<!-- test: associated-binding.a-generic-conformer-reaches-its-own-witness -->
⭐⭐ **THE PER-INSTANCE TABLE.** `Box` is a GENERIC conformer of `Sized` whose `size()` dispatches
through its own `where T is Sized` constraint — so the impl reads the hidden witness its declaration
reserves, and a table shared by every instantiation has no instantiation to take that witness from.
Widening `LeafBox` into a `Sized` existential mints a table for THAT instance, whose slot calls the one shared `Box.size` with `Leaf`'s own witness.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Sized
	function size() returns Integer
end 'Sized'

type Leaf implements Sized
	var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function size() returns Integer
		return self.n
	end 'size'
end 'Leaf'

type Box uses T implements Sized where T is Sized
	var t as T

	static function create(t T) returns Self
		return Self{t: t}
	end 'create'

	function size() returns Integer
		return self.t.size() + 1
	end 'size'
end 'Box'

typealias LeafBox = Box with Leaf

function measure(s Sized) returns Integer
	return s.size()
end 'measure'

function main() returns ExitCode
	let b = LeafBox.create(Leaf.create(7))
	print("{measure(b)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8
```

<!-- test: associated-binding.two-instances-of-one-generic-conformer-get-their-own-tables -->
⭐⭐ **ONE SHARED BODY, TWO TABLES, TWO DICTIONARIES.** The proof that the table is per INSTANCE and not
merely per conformer: `Box with Leaf` and `Box with Twig` share `Box.size`, and the answer differs only
because each instance's table hands that one body a different `T` witness.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Sized
	function size() returns Integer
end 'Sized'

type Leaf implements Sized
	var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function size() returns Integer
		return self.n
	end 'size'
end 'Leaf'

type Twig implements Sized
	var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function size() returns Integer
		return self.n * 10
	end 'size'
end 'Twig'

type Box uses T implements Sized where T is Sized
	var t as T

	static function create(t T) returns Self
		return Self{t: t}
	end 'create'

	function size() returns Integer
		return self.t.size() + 1
	end 'size'
end 'Box'

typealias LeafBox = Box with Leaf
typealias TwigBox = Box with Twig

function measure(s Sized) returns Integer
	return s.size()
end 'measure'

function main() returns ExitCode
	print("{measure(LeafBox.create(Leaf.create(7)))}:{measure(TwigBox.create(Twig.create(7)))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8:71
```

<!-- test: error.the-type-argument-binds-the-associated-type-to-something-else -->
⭐ **A BINDING IS A CLAIM, AND IT IS CHECKED AT THE INSTANTIATION.** `Wrap with (TextCur, Integer)`
says `TextCur`'s `Cursor` conformance binds `Element` to `Integer`; it binds it to `String`. Admitting
it would compile one shared body against `Integer` and hand it a `String` conformer's bits.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor uses Element
	function current() returns Element
end 'Cursor'

type TextCur implements Cursor with String
	var v as String

	static function create(v String) returns Self
		return Self{v: v}
	end 'create'

	function current() returns String
		return self.v
	end 'current'
end 'TextCur'

type Wrap uses S, E where S is Cursor with E
	var s as S

	static function create(s S) returns Self
		return Self{s: s}
	end 'create'

	function only() returns E
		return self.s.current()
	end 'only'
end 'Wrap'

typealias BadWrap = Wrap with (TextCur, Integer)

function main() returns ExitCode
	let w = BadWrap.create(TextCur.create("no"))
	print("{w.only()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3133: <fragment>:32:11: 'Wrap' constrains type parameter 'S' to 'Cursor with Integer', but 'TextCur' binds 'Cursor's associated type 'Element' to 'String' — a `where` constraint's binding is what types every dispatch through that parameter inside the shared body, which is compiled ONCE, so a conformer binding it otherwise would have its bits read as the claimed type. Bind the constraint to what the conformer declares, or supply an argument whose conformance binds what the constraint states
```

<!-- test: error.a-constraint-over-one-files-limit-is-not-met-by-another-files-wider-or-narrower-limit -->
The binding is a declaration, not a spelling. `pkg/lib.maxon`'s constraint binds `Element` to its own
`Limit`; `Tick` binds it to `pkg/main.maxon`'s `Limit`, a different declaration over a different range.
```maxon
// --- file: pkg/lib.maxon
typealias Limit = int(0 to 9)

export interface Cursor uses Element
	function current() returns Element
end 'Cursor'

export type Wrap uses S where S is Cursor with Limit
	export var s as S

	export static function create(s S) returns Self
		return Self{s: s}
	end 'create'
end 'Wrap'

// --- file: pkg/main.maxon
typealias Limit = int(0 to 5)

type Tick implements Cursor with Limit
	var v as Limit

	static function create() returns Self
		return Self{v: 3}
	end 'create'

	function current() returns Limit
		return self.v
	end 'current'
end 'Tick'

typealias TickWrap = Wrap with Tick

function main() returns ExitCode
	let w = TickWrap.create(Tick.create())
	return w.s.current() as ExitCode
end 'main'
```
```maxoncstderr
error E3133: pkg/<fragment>:32:11: 'Wrap' constrains type parameter 'S' to 'Cursor with Limit', but 'Tick' binds 'Cursor's associated type 'Element' to 'Limit' — a `where` constraint's binding is what types every dispatch through that parameter inside the shared body, which is compiled ONCE, so a conformer binding it otherwise would have its bits read as the claimed type. Bind the constraint to what the conformer declares, or supply an argument whose conformance binds what the constraint states
```

<!-- test: error.a-constraint-over-one-files-limit-is-not-met-by-another-files-limit-of-the-same-range -->
The same program with both declarations over one range. They are still two declarations, so the binding
still disagrees.
```maxon
// --- file: pkg/lib.maxon
typealias Limit = int(0 to 9)

export interface Cursor uses Element
	function current() returns Element
end 'Cursor'

export type Wrap uses S where S is Cursor with Limit
	export var s as S

	export static function create(s S) returns Self
		return Self{s: s}
	end 'create'
end 'Wrap'

// --- file: pkg/main.maxon
typealias Limit = int(0 to 9)

type Tick implements Cursor with Limit
	var v as Limit

	static function create() returns Self
		return Self{v: 3}
	end 'create'

	function current() returns Limit
		return self.v
	end 'current'
end 'Tick'

typealias TickWrap = Wrap with Tick

function main() returns ExitCode
	let w = TickWrap.create(Tick.create())
	return w.s.current() as ExitCode
end 'main'
```
```maxoncstderr
error E3133: pkg/<fragment>:32:11: 'Wrap' constrains type parameter 'S' to 'Cursor with Limit', but 'Tick' binds 'Cursor's associated type 'Element' to 'Limit' — a `where` constraint's binding is what types every dispatch through that parameter inside the shared body, which is compiled ONCE, so a conformer binding it otherwise would have its bits read as the claimed type. Bind the constraint to what the conformer declares, or supply an argument whose conformance binds what the constraint states
```

<!-- test: error.a-where-constraint-cannot-bind-more-than-the-interface-declares -->
⭐ **AN OVER-LONG BINDING LIST IS THE `implements` CLAUSE'S REFUSAL, ONE DOOR OVER.** `Sized` declares
no associated types at all, so there is no position for `with Integer` to name.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Sized
	function size() returns Integer
end 'Sized'

type Box uses T where T is Sized with (Integer)
	var t as T

	static function create(t T) returns Self
		return Self{t: t}
	end 'create'

	function size() returns Integer
		return self.t.size()
	end 'size'
end 'Box'

function main() returns ExitCode
	print("{Box.create(0).size()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2066: <fragment>:8:39: interface 'Sized' declares 0 associated type(s), but this parenthesized 'with' clause binds 1
```

<!-- test: associated-binding.a-constraint-argument-written-as-a-typealias-gets-its-own-table -->
⭐⭐ **AN INSTANCE IS AN INSTANCE HOWEVER IT IS SPELLED.** `Holder with LeafBox` names the same argument as
`Holder with (Box with Leaf)` — one through a `typealias`, one inline — and both need the per-instance table
`Box.size`'s hidden witness comes from. The lowering resolves the alias to the instance it names, so both
spellings get the per-instance table and answer 16.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Sized
	function size() returns Integer
end 'Sized'

type Leaf implements Sized
	var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function size() returns Integer
		return self.n
	end 'size'
end 'Leaf'

type Box uses T implements Sized where T is Sized
	var t as T

	static function create(t T) returns Self
		return Self{t: t}
	end 'create'

	function size() returns Integer
		return self.t.size() + 1
	end 'size'
end 'Box'

type Holder uses S where S is Sized
	var s as S

	static function create(s S) returns Self
		return Self{s: s}
	end 'create'

	function twice() returns Integer
		return self.s.size() * 2
	end 'twice'
end 'Holder'

typealias LeafBox = Box with Leaf
typealias BoxHolder = Holder with LeafBox

function main() returns ExitCode
	print("{BoxHolder.create(LeafBox.create(Leaf.create(7))).twice()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
16
```

<!-- test: associated-binding.a-parametric-constraint-argument-is-a-zero-nothing-reads -->
⭐⭐ **THE WITNESS A SHARED BODY CANNOT NAME STATICALLY.** `Outer.tally` builds `Holder with (Box with E)`
while `E` is still its own type parameter, so the `where S is Sized` witness it owes is a table for
`Box with E` — an instance whose adapter names a layout descriptor only the running frame holds, so no
static table exists to pass. `tally` carries `Outer`'s layout descriptor, and the table travels in it: the
descriptor minted for `Outer with Leaf` holds `Box with Leaf`'s table, and the call loads it from there. A
frame with no descriptor passes a ZERO instead, and the compiler PROVES the callee reads none of it
(`Holder.create` is a bare `Self` literal; `Holder.tag` returns a constant) — the fallback this case's id
names. This is the shape `stdlib/Interfaces.maxon`'s `extension Iterable.withIterator` is, without the
stdlib.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Sized
	function size() returns Integer
end 'Sized'

type Leaf implements Sized
	var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function size() returns Integer
		return self.n
	end 'size'
end 'Leaf'

type Box uses T implements Sized where T is Sized
	var t as T

	static function create(t T) returns Self
		return Self{t: t}
	end 'create'

	function size() returns Integer
		return self.t.size() + 1
	end 'size'
end 'Box'

type Holder uses S where S is Sized
	var s as S

	static function create(s S) returns Self
		return Self{s: s}
	end 'create'

	function twice() returns Integer
		return self.s.size() * 2
	end 'twice'

	function tag() returns Integer
		return 5
	end 'tag'
end 'Holder'

type Outer uses E where E is Sized
	typealias BoxE = Box with E
	typealias Inner = Holder with BoxE
	var e as E

	static function create(e E) returns Self
		return Self{e: e}
	end 'create'

	function tally() returns Integer
		return Inner.create(BoxE.create(self.e)).tag() + self.e.size()
	end 'tally'
end 'Outer'

typealias LeafOuter = Outer with Leaf

function main() returns ExitCode
	print("{LeafOuter.create(Leaf.create(7)).tally()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
12
```

<!-- test: associated-binding.a-parametric-constraint-argument-the-callee-reads -->
⭐⭐ **AND WHERE THE CALLEE DOES READ IT, THE CALLER SUPPLIES THE TABLE.** The same program with
`twice()` — which dispatches `self.s.size()` through the very witness — in place of `tag()`. The witness
table for `Box with E` travels in the caller's layout descriptor, so `twice()` dispatches through the
instance's own table: `(7 + 1) * 2 + 7` is 23.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Sized
	function size() returns Integer
end 'Sized'

type Leaf implements Sized
	var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function size() returns Integer
		return self.n
	end 'size'
end 'Leaf'

type Box uses T implements Sized where T is Sized
	var t as T

	static function create(t T) returns Self
		return Self{t: t}
	end 'create'

	function size() returns Integer
		return self.t.size() + 1
	end 'size'
end 'Box'

type Holder uses S where S is Sized
	var s as S

	static function create(s S) returns Self
		return Self{s: s}
	end 'create'

	function twice() returns Integer
		return self.s.size() * 2
	end 'twice'

	function tag() returns Integer
		return 5
	end 'tag'
end 'Holder'

type Outer uses E where E is Sized
	typealias BoxE = Box with E
	typealias Inner = Holder with BoxE
	var e as E

	static function create(e E) returns Self
		return Self{e: e}
	end 'create'

	function tally() returns Integer
		return Inner.create(BoxE.create(self.e)).twice() + self.e.size()
	end 'tally'
end 'Outer'

typealias LeafOuter = Outer with Leaf

function main() returns ExitCode
	print("{LeafOuter.create(Leaf.create(7)).tally()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
23
```

<!-- test: associated-binding.a-shared-table-serves-a-generic-conformer-from-a-static -->
A static whose owner is unconstrained calls a constrained method on an instance over a generic conformer
whose impls need no dictionary; the conformer's one shared table serves the call.
```maxon
typealias Integer = int(i64.min to i64.max)
type Tagged uses T implements Equatable
	var tag as Integer
	var v as T
	static function create(tag Integer, v T) returns Self
		return Self{tag: tag, v: v}
	end 'create'
	function equals(other Self) returns bool
		return self.tag == other.tag
	end 'equals'
end 'Tagged'
type Holder uses S where S is Equatable
	var s as S
	static function create(s S) returns Self
		return Self{s: s}
	end 'create'
	function same(other S) returns bool
		return self.s.equals(other)
	end 'same'
end 'Holder'
type Outer uses E
	typealias TagE = Tagged with E
	typealias Inner = Holder with TagE
	var e as E
	static function check(h Inner, x TagE) returns bool
		return h.same(x)
	end 'check'
end 'Outer'
typealias IntOuter = Outer with Integer
typealias IntTagged = Tagged with Integer
typealias IntHolder = Holder with IntTagged
function main() returns ExitCode
	let h = IntHolder.create(IntTagged.create(1, v: 5))
	print("{IntOuter.check(h, x: IntTagged.create(1, v: 9))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
```

<!-- test: associated-binding.error.a-static-whose-callee-reads-a-parametric-witness -->
```maxon
typealias Integer = int(i64.min to i64.max)

interface Sized
	function size() returns Integer
end 'Sized'

type Leaf implements Sized
	var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function size() returns Integer
		return self.n
	end 'size'
end 'Leaf'

type Box uses T implements Sized where T is Sized
	var t as T

	static function create(t T) returns Self
		return Self{t: t}
	end 'create'

	function size() returns Integer
		return self.t.size() + 1
	end 'size'
end 'Box'

type Holder uses S where S is Sized
	var s as S

	static function create(s S) returns Self
		return Self{s: s}
	end 'create'

	function twice() returns Integer
		return self.s.size() * 2
	end 'twice'

	function tag() returns Integer
		return 5
	end 'tag'
end 'Holder'

type Outer uses E where E is Sized
	typealias BoxE = Box with E
	typealias Inner = Holder with BoxE
	var e as E

	static function create(e E) returns Self
		return Self{e: e}
	end 'create'

	static function tally(b BoxE) returns Integer
		return Inner.create(b).twice()
	end 'tally'
end 'Outer'

typealias LeafOuter = Outer with Leaf
typealias LeafBox = Box with Leaf

function main() returns ExitCode
	print("{LeafOuter.tally(LeafBox.create(Leaf.create(7)))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3132: <fragment>:57:18: 'Outer.tally' calls 'Holder.twice', whose `where` constraint requires a witness table for `Box` conforming to `Sized` at a generic instance written over this body's OWN type parameters — and 'Holder.twice' READS that witness. A conformance whose impls carry a hidden dictionary is given a table PER INSTANCE, whose slots supply that instantiation's layout descriptor; an instance that is still parametric names a descriptor the enclosing frame only holds at run time, so no static table exists to pass. Reach the constrained method through a concrete instance, or make the conformance's impls independent of their type argument
```

<!-- test: associated-binding.a-conformance-reading-its-layout-through-a-blind-edge-is-served-per-instance -->
```maxon
typealias Val = int(i64.min to i64.max)
type Bag uses T implements Equatable
	export typealias Items = Array with T
	var items as Items
	static function create(items Items) returns Self
		return Self{items: items}
	end 'create'
	function equals(other Self) returns bool
		let copy = try other.items.slice(0, endIndex: other.items.count()) otherwise return false
		return copy.count() == items.count()
	end 'equals'
end 'Bag'
type Keeper uses A where A is Equatable
	var n as Val
	static function create(n Val) returns Self
		return Self{n: n}
	end 'create'
	function touch() returns Val
		return n
	end 'touch'
end 'Keeper'
type Outer uses U
	export typealias Bagged = Bag with U
	export typealias Kept = Keeper with Bagged
	var kept as Kept
	static function create(kept Kept) returns Self
		return Self{kept: kept}
	end 'create'
	function poke() returns Val
		return kept.touch()
	end 'poke'
end 'Outer'
typealias VBag = Bag with Val
typealias VKeeper = Keeper with VBag
typealias O = Outer with Val
function main() returns ExitCode
	print("{O.create(VKeeper.create(1)).poke()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: associated-binding.mutually-recursive-conformers-filing-witness-entries-terminate -->
```maxon
typealias Val = int(i64.min to i64.max)

interface Sized
	function size() returns Val
end 'Sized'

type Keeper uses A where A is Sized
	var n as Val

	static function create(n Val) returns Self
		return Self{n: n}
	end 'create'

	function touch() returns Val
		return n
	end 'touch'
end 'Keeper'

type Ping uses T implements Sized
	export typealias Wrapped = Array with T
	export typealias Back = Pong with Wrapped
	export typealias Keep = Keeper with Back

	var keep as Keep

	static function create(keep Keep) returns Self
		return Self{keep: keep}
	end 'create'

	function size() returns Val
		return sizeof(T) + self.keep.touch()
	end 'size'
end 'Ping'

type Pong uses U implements Sized
	export typealias Wrapped = Array with U
	export typealias Back = Ping with Wrapped
	export typealias Keep = Keeper with Back

	var keep as Keep

	static function create(keep Keep) returns Self
		return Self{keep: keep}
	end 'create'

	function size() returns Val
		return sizeof(U) + self.keep.touch()
	end 'size'
end 'Pong'

typealias ValArray = Array with Val
typealias ValArrayPong = Pong with ValArray
typealias ValPingKeeper = Keeper with ValArrayPong
typealias ValPing = Ping with Val

function main() returns ExitCode
	let p = ValPing.create(ValPingKeeper.create(1))
	print("{p.size()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9
```

<!-- test: error.an-extension-declared-binding-is-checked-against-the-constraint -->
A conformance declared by an `extension` is the one `Wrap`'s constraint is checked against, whichever declaration of the type comes first.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Named
	function code() returns Integer
end 'Named'

interface Cursor uses Element
	function current() returns Element
end 'Cursor'

type TextCur implements Named
	var v as String

	static function create(v String) returns Self
		return Self{v: v}
	end 'create'

	function code() returns Integer
		return 100
	end 'code'
end 'TextCur'

extension TextCur implements Cursor with String
	function current() returns String
		return self.v
	end 'current'
end 'TextCur'

type Wrap uses S, E where S is Cursor with E
	var s as S

	static function create(s S) returns Self
		return Self{s: s}
	end 'create'

	function only() returns E
		return self.s.current()
	end 'only'
end 'Wrap'

typealias BadWrap = Wrap with (TextCur, Integer)

function main() returns ExitCode
	let w = BadWrap.create(TextCur.create("no"))
	print("{w.only()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3133: <fragment>:42:11: 'Wrap' constrains type parameter 'S' to 'Cursor with Integer', but 'TextCur' binds 'Cursor's associated type 'Element' to 'String' — a `where` constraint's binding is what types every dispatch through that parameter inside the shared body, which is compiled ONCE, so a conformer binding it otherwise would have its bits read as the claimed type. Bind the constraint to what the conformer declares, or supply an argument whose conformance binds what the constraint states
```
