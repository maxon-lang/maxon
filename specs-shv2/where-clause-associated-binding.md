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
requirement returns the associated type `Element` — had no type to give the result: the compiler read
the interface's own PLACEHOLDER NAME as though it were a type the program declares, and a tuple built
from it was a different tuple type from the declared one. It also left the position UNCLAIMED for
E3119, which then refused every program whose conformers bind that position differently — the
overwhelmingly common case, since the whole point of an associated type is that each conformer picks
its own.

`with` on the constraint supplies both answers from one place: the dispatch's result (and its
associated formals) are typed through the binding, and the position counts as CLAIMED at that site.

### What the binding obligates

A binding is a CLAIM about the type argument, checked at each instantiation exactly as `where T is I`
itself is (E3017): the argument must conform, and its conformance must bind each named position to
the type the constraint names, substituted through the instantiation. A disagreement is **E3131**.

### Per-instance witness tables

A generic conforming type shares ONE compiled body across every instantiation, and its witness table
is keyed by the conformer's BASE name for the same reason (`generic-instance-conformance.md`). That
reduction rests on the impls being independent of the type argument. An impl that reads its hidden
dictionary — its type parameter's layout descriptor, or the witness table of one of its own `where`
constraints — is not, and it used to be refused with **E3128** because a dispatch through the shared
table had no instantiation to take the dictionary from.

It does now. Where a witness table is minted for a CONCRETE instance of a generic conformer whose
impls carry a dictionary, the table is minted PER INSTANCE — `__witness_Wrap_IntCur_Integer.Cursor`
rather than `__witness_Wrap.Cursor` — and each of its slots points at a thunk that calls the shared
impl with that instance's own descriptor and witnesses. A conformer whose impls need no dictionary
keeps the one shared table it always had, so nothing else in the program moves.

E3128 remains for the case it is still true of: a witness table minted for a conformer with NO
instance in hand.

## Tests

<!-- test: associated-binding.constrained-parameter-typed-through-the-binding -->
⭐ **THE RESULT OF A DISPATCH THROUGH A CONSTRAINED TYPE PARAMETER.** `s.current()` returns `Cursor`'s
associated `Element`; the constraint says this `Cursor` binds it to `E`; so the tuple `(s, s.current())`
is the declared `(S, E)`. MEASURED before the constraint could carry a binding:
`error E3005: Cannot return '__Tuple2.T….Element' from function declared to return '__Tuple2.T….T…'`
— the interface's own placeholder name read as though it were a declared type.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor uses Element
	function current() returns Element
end 'Cursor'

type IntCur implements Cursor with Integer
	var v as Integer

	export static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'

	export function current() returns Integer
		return self.v
	end 'current'
end 'IntCur'

type Wrap uses S, E where S is Cursor with E
	var s as S

	export static function create(s S) returns Self
		return Self{s: s}
	end 'create'

	export function pair() returns (S, E)
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
names neither, and before the constraint could carry a binding this was E3119 — *"DISPATCHES THROUGH A
RECEIVER THAT DOES NOT SAY WHICH BINDING IT HOLDS"*, whose own message prescribed a spelling the
grammar had no way to write. Both instantiations run, through one compiled body.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Cursor uses Element
	function current() returns Element
end 'Cursor'

type IntCur implements Cursor with Integer
	var v as Integer

	export static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'

	export function current() returns Integer
		return self.v
	end 'current'
end 'IntCur'

type TextCur implements Cursor with String
	var v as String

	export static function create(v String) returns Self
		return Self{v: v}
	end 'create'

	export function current() returns String
		return self.v
	end 'current'
end 'TextCur'

type Wrap uses S, E where S is Cursor with E
	var s as S

	export static function create(s S) returns Self
		return Self{s: s}
	end 'create'

	export function only() returns E
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
reserves, and a dispatch through a table shared by every instantiation had no instantiation to take
that witness from (**E3128**). Widening `LeafBox` into a `Sized` existential now mints a table for
THAT instance, whose slot calls the one shared `Box.size` with `Leaf`'s own witness.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Sized
	function size() returns Integer
end 'Sized'

type Leaf implements Sized
	var n as Integer

	export static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	export function size() returns Integer
		return self.n
	end 'size'
end 'Leaf'

type Box uses T implements Sized where T is Sized
	var t as T

	export static function create(t T) returns Self
		return Self{t: t}
	end 'create'

	export function size() returns Integer
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

	export static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	export function size() returns Integer
		return self.n
	end 'size'
end 'Leaf'

type Twig implements Sized
	var n as Integer

	export static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	export function size() returns Integer
		return self.n * 10
	end 'size'
end 'Twig'

type Box uses T implements Sized where T is Sized
	var t as T

	export static function create(t T) returns Self
		return Self{t: t}
	end 'create'

	export function size() returns Integer
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

	export static function create(v String) returns Self
		return Self{v: v}
	end 'create'

	export function current() returns String
		return self.v
	end 'current'
end 'TextCur'

type Wrap uses S, E where S is Cursor with E
	var s as S

	export static function create(s S) returns Self
		return Self{s: s}
	end 'create'

	export function only() returns E
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
error E3131: <fragment>:32:11: 'Wrap' constrains type parameter 'S' to 'Cursor with Integer', but 'TextCur' binds 'Cursor's associated type 'Element' to 'String' — a `where` constraint's binding is what types every dispatch through that parameter inside the shared body, which is compiled ONCE, so a conformer binding it otherwise would have its bits read as the claimed type. Bind the constraint to what the conformer declares, or supply an argument whose conformance binds what the constraint states
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

	export static function create(t T) returns Self
		return Self{t: t}
	end 'create'

	export function size() returns Integer
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
