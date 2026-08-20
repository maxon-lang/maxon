---
feature: generic-opaque-value-store
status: stable
keywords: [generics, type-parameter, ownership, layout-descriptor, retain, dictionary]
category: type-system
---

# Storing a borrowed opaque `T` into a record

## Documentation

A shared generic body compiles ONCE for every instantiation, so when it stores a value of its own type
parameter into a durable slot — a tuple it builds, a `Self{…}` field, an array element — it cannot name
the reference protocol that slot owes. The concrete twin of the same body can: a `String` element read
out of a container and stored into a tuple takes a COPY, and a struct element takes an INCREF.

The record's destructor is CONCRETE either way: the caller's `(String, Integer)` is a tuple whose first
slot is a `String`, and its `__destruct_` decrefs that slot whoever filled it. So a shared body that
stored the raw borrow made the record a second OWNER of a reference nobody took — a double free.

The reference is therefore taken at run time, through the enclosing instance's layout descriptor: the
`retainFunc` word holds `__str_clone` for a byte-record argument, `__mm_retain` for a managed aggregate,
and 0 for an argument that owns nothing. It is the same three-way protocol a witness table's
`retainFunc@16` carries, because it answers the same question about a type the code cannot name.

**A managed aggregate SHARES, and that is the observable half.** A struct has reference identity a
program can see, so the slot becomes a second owner of the ONE record — a write through the record read
back out of the container shows through the container. A deep copy would be a different struct, which is
a wrong answer and not merely a slower one.

## Tests

### A managed aggregate argument is SHARED by the record the shared body builds

<!-- test: aggregate-argument-is-shared-not-copied -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Index = int(0 to u64.max)

type Cell
	export var n as Integer

	export static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	export function bump(by Integer)
		n = n + by
	end 'bump'
end 'Cell'

type Holder uses Element
	typealias EArray = Array with Element
	typealias Entry = (Element, Integer)

	var items as EArray = EArray.create()

	export static function create() returns Self
		return Self{}
	end 'create'

	export function add(item Element)
		items.push(item)
	end 'add'

	export function entryAt(i Index) returns Entry
		let v = try items.get(i) otherwise panic("Holder.entryAt: out of range")
		return (v, 1)
	end 'entryAt'
end 'Holder'

typealias CellHolder = Holder with Cell

function main() returns ExitCode
	var h = CellHolder.create()
	h.add(Cell.create(40))

	let e = h.entryAt(0)
	e.0.bump(2)

	let again = h.entryAt(0)
	return again.0.n
end 'main'
```
```exitcode
42
```

### A `String` argument outlives the container the shared body read it from

<!-- test: string-argument-outlives-its-source -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Index = int(0 to u64.max)

type Holder uses Element
	typealias EArray = Array with Element
	typealias Entry = (Element, Integer)

	var items as EArray = EArray.create()

	export static function create() returns Self
		return Self{}
	end 'create'

	export function add(item Element)
		items.push(item)
	end 'add'

	export function entryAt(i Index) returns Entry
		let v = try items.get(i) otherwise panic("Holder.entryAt: out of range")
		return (v, 1)
	end 'entryAt'
end 'Holder'

typealias StringHolder = Holder with String

function pluck() returns (String, Integer)
	var h = StringHolder.create()
	h.add("the source is gone")
	return h.entryAt(0)
end 'pluck'

function main() returns ExitCode
	let e = pluck()
	if e.0.equals("the source is gone") 'kept'
		return 42
	end 'kept'
	return 1
end 'main'
```
```exitcode
42
```

### A trivial argument's retain word is 0, so the same body stores it raw

<!-- test: trivial-argument-takes-no-reference -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Index = int(0 to u64.max)
typealias SmallInt = int(0 to 100)

type Holder uses Element
	typealias EArray = Array with Element
	typealias Entry = (Element, Integer)

	var items as EArray = EArray.create()

	export static function create() returns Self
		return Self{}
	end 'create'

	export function add(item Element)
		items.push(item)
	end 'add'

	export function entryAt(i Index) returns Entry
		let v = try items.get(i) otherwise panic("Holder.entryAt: out of range")
		return (v, 1)
	end 'entryAt'
end 'Holder'

typealias SmallHolder = Holder with SmallInt

function main() returns ExitCode
	var h = SmallHolder.create()
	h.add(40)
	h.add(2)

	let a = h.entryAt(0)
	let b = h.entryAt(1)
	return a.0 + b.0
end 'main'
```
```exitcode
42
```

### A generic with NO `T`-typed field still releases the reference its record took

The retain word is a fact about the type ARGUMENT and the release must be the same fact. A base that
declares no `Array with T` and no bare `T` — only an `Integer` — can still take a borrowed `T` into a tuple
it builds, and if that tuple is dropped HERE the reference has to go with it. While `destroyFunc@40` was
gated on the base's FIELD LIST instead, this exact shape retained through a live `retainFunc@64` and
released through a zero: exit 101 with the right answer printed.

<!-- test: a-record-in-a-fieldless-generic-releases-what-it-took -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Pack uses T
	var n as Integer

	export static function create() returns Self
		return Self{n: 40}
	end 'create'

	export function tag(t T) returns Integer
		let pair = (t, n)
		return pair.1
	end 'tag'
end 'Pack'

typealias StrPack = Pack with String

function main() returns ExitCode
	var p = StrPack.create()
	let s = "hello"
	return p.tag(s) + p.tag("literal")
end 'main'
```
```exitcode
80
```

### An interface type argument is REFUSED, not crashed on

An existential is a two-word fat pointer whose retain and release live in its witness, and a descriptor
word carries no witness — so `Box with Named` has no ownership protocol to name. The refusal is the
container-element rule's, already recorded by the time the destructor walk runs; the walk must therefore
contribute NOTHING for such an argument rather than route it into a one-argument drop router, which
replaced the diagnostic with a compiler stack trace.

<!-- test: error.an-interface-type-argument-is-refused -->
```maxon
typealias Integer = int(i64.min to i64.max)

interface Named
	function label() returns String
end 'Named'

type Thing implements Named
	var n as Integer

	export static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	export function label() returns String
		return "thing"
	end 'label'
end 'Thing'

type Box uses T
	var value as T

	export static function create(value T) returns Self
		return Self{value: value}
	end 'create'

	export function get() returns T
		return value
	end 'get'
end 'Box'

typealias NamedBox = Box with Named

function main() returns ExitCode
	let b = NamedBox.create(Thing.create(3))
	return b.get().label().length()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:32:31: Unsupported: a container's element type declared at the interface type 'Named' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and an element slot is one machine word. Declare it at a concrete type, or take the interface as a PARAMETER of a plain function, which carries its witness as an adjacent argument
```

### A container of records written over the enclosing parameter is REFUSED

Every case above stores the borrow into a record the SAME body builds and then lets that record die where it
was built. This one hands the borrow to a SIBLING GENERIC's constructor — `EBox.create(x, tag: tag)`, where
`EBox = Box with Element` is an inner alias over the enclosing type's own parameter — and then **puts the
record into a container**, so it outlives the borrow.

⛔⛔ **THAT PROGRAM WAS A USE-AFTER-FREE, MEASURED `0xC0000005`**, and it is refused rather than compiled.
`Box.create`'s `Self{x: x}` deliberately takes no reference: a constructor feed is settled by the CALL SITE
(`opaqueSlotTakesItsOwnReference`), which is what the concrete spelling below does through `argIsConsumedAt`.
A SHARED body has no such transfer to make — and it may not simply take one either. **MEASURED at the same
time: with the reference taken through `retainFunc@64` the fault goes away and the identical program exits
101**, because `Bag.create` stamps its element array `__managed_create(8, __mm_decref)` and nothing releases
what was taken. The destructor that WOULD release it (`__destruct_Box_String`) exists — `Box with String` is
interned by the substitution — but a shared body can name only the DECLARATION VIEW's, and that one reads its
own bare `T` field through `typeIsManaged` and is told the field owns nothing.

⇒ **The refusal stands in for a release facility that does not exist: a layout-descriptor slot carrying a
NESTED INSTANCE's per-instantiation destructor.** The compiler already names that slot where it has a
diagnostic to hang it on — reassigning such a field is refused in exactly those words
(`Parser.emitOpaqueFieldReassign`) — and this is the same sentence at the CONSTRUCTION form, which is the
only other way such a column is born. When the slot exists, this case becomes the runtime program its
`maxoncstderr` currently pins the absence of, and `generic-instance-clone`'s
`clone-of-an-instance-over-the-enclosing-type-parameter` recovers its managed spelling with it.

⚠ **THE REFUSAL IS AT THE CONTAINER, NOT AT THE FORWARD**, which is what keeps the shapes that genuinely
work working: a record built from a borrowed opaque `T` and RETURNED (`generic-opaque-cursor-element`'s
`createIterator`, `inner-alias-construction`'s `boxed()`) or DROPPED where it was built is untouched, and so
is every trivial instantiation of this very shape (below).

<!-- test: error.a-container-of-records-over-the-enclosing-parameter-is-refused -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	let x as T
	let tag as Integer

	export function value() returns T
		return x
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias BoxArray = Array with EBox
	var items as BoxArray

	export function add(x Element, tag Integer)
		items.push(EBox.create(x, tag: tag))
	end 'add'

	export function first() returns Element throws ArrayError
		let slot = try self.items.first() otherwise 'e'
			throw ArrayError.indexOutOfBounds
		end 'e'
		return slot.value()
	end 'first'

	static function create() returns Self
		return Self{items: BoxArray{}}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String

function fill(b StrBag)
	var sb = StringBuilder.create()
	sb.append("hello ")
	sb.append("heap world")
	let s = sb.build()
	b.add(s, tag: 7)
end 'fill'

function main() returns ExitCode
	var b = StrBag.create()
	fill(b)
	let got = try b.first() otherwise 'e'
		return 9 as ExitCode
	end 'e'
	print(got)
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:34:22: Unsupported: a container whose element is 'Bag.EBox' — a generic instance written over this type's OWN parameter, resting (itself, or through an instance it holds) a field declared AT that parameter — cannot be a CONTAINER ELEMENT: the shared generic body compiles once for every instantiation, so releasing such a record means releasing a field whose destructor is a fact about the enclosing instantiation, and the only entry that knows it takes the instantiation's layout descriptor as a second argument (`__destruct_dict_<instance>(descriptor, box)`). A container stamps ONE machine word as its element destructor and calls it with the element alone, so there is nowhere to carry that descriptor. It is refused at the two arrivals that stamp one: a container's element (an `Array`/`__ManagedList` `create` inside the body, and any `push`/`insert`/`upsert`/`set` of one), a `List` node, and a `Map` or `Set` column. Holding ONE such record in a FIELD of the enclosing type (`Self{one: Inner.create(x, …)}`) is admitted — the enclosing instantiation is concrete wherever it is freed, so its own destructor releases the field. Otherwise: hold the values in a container of the type PARAMETER itself (`Array with <type parameter>`, whose element destructor IS carried by the enclosing instance's layout descriptor), give the inner type a concrete field instead of one declared at the parameter, or build and hold the record in a method of a concrete instantiation
```

### …and the CONCRETE instantiation of the same constructor, which always worked

The byte-identical program with `Box with String` written at top level and a NON-generic `Bag` holding
`Array with StrBox`. Same `Box uses T`, same `Box.create` body; only the INSTANTIATION differs. Here
`argIsConsumedAt` sees a concrete `String` argument, consumes it and hands `Box.create` a `+1` outright.
It is the control the case above is measured against, and it belongs beside it so a future reader sees
which half of the pair moved.

<!-- test: the-concrete-spelling-of-the-same-constructor-feed -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	let x as T
	let tag as Integer

	export function value() returns T
		return x
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

typealias StrBox = Box with String

type Bag
	typealias BoxArray = Array with StrBox
	var items as BoxArray = BoxArray.create()

	export static function create() returns Self
		return Self{}
	end 'create'

	export function add(x String, tag Integer)
		items.push(StrBox.create(x, tag: tag))
	end 'add'

	export function first() returns String throws ArrayError
		let slot = try self.items.first() otherwise 'e'
			throw ArrayError.indexOutOfBounds
		end 'e'
		return slot.value()
	end 'first'
end 'Bag'

function fill(b Bag)
	var sb = StringBuilder.create()
	sb.append("hello ")
	sb.append("heap world")
	let s = sb.build()
	b.add(s, tag: 7)
end 'fill'

function main() returns ExitCode
	var b = Bag.create()
	fill(b)
	let got = try b.first() otherwise 'e'
		return 9 as ExitCode
	end 'e'
	print(got)
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
hello heap world
```

### …and the slot it cannot release need not be the element's OWN field

**THE PREDICATE READ THE RAW DECLARED FIELD LIST AND THIS SHAPE WALKED PAST IT — MEASURED `0xC0000005`,
FOUND BY THE BATCH41 REVIEW.** Everything above puts the bare `Element` in `Box`'s own field list, where a
`slotTypeIsOpaque` test of the declared column sees it. Here `Box` holds an `Inner with T` instead, and
`Inner` is the one holding the bare parameter — so `Box`'s own columns are a `genericInstance` and an `int`,
neither of them a `typeParameter`, and the first cut of the refusal answered *"owns nothing"* and compiled
the program. The array was then stamped with `Box`'s DECLARATION-VIEW destructor
(`__destruct_Box_T<hash>` — MEASURED off `--emit-ir`), which frees the inner record and leaves the `String`
it holds, the store kept a raw borrow, and **the read faulted** exactly as the case above does.

⇒ The walk resolves and SUBSTITUTES each column through `substituteInstanceFieldType` — the one derivation
`genericInstanceFieldIsManaged`, `genericInstanceFieldDropCallee` and `genericInstanceFieldCloneStrategy`
already share, whose own header carries the exit-101 leak a second, hand-rolled reading of that column
produced — and recurses into a column that is itself an instance. A refusal deriving the column its own way
is free to disagree with the destructor it refuses on behalf of.

<!-- test: error.a-nested-instance-two-levels-over-the-enclosing-parameter-is-refused -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner uses U
	let x as U

	export function value() returns U
		return x
	end 'value'

	static function create(x U) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Box uses T
	typealias TInner = Inner with T
	let inner as TInner
	let tag as Integer

	export function value() returns T
		return inner.value()
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{inner: TInner.create(x), tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias BoxArray = Array with EBox
	var items as BoxArray

	export function add(x Element, tag Integer)
		items.push(EBox.create(x, tag: tag))
	end 'add'

	export function first() returns Element throws ArrayError
		let slot = try self.items.first() otherwise 'e'
			throw ArrayError.indexOutOfBounds
		end 'e'
		return slot.value()
	end 'first'

	static function create() returns Self
		return Self{items: BoxArray{}}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String

function fill(b StrBag)
	var sb = StringBuilder.create()
	sb.append("hello ")
	sb.append("heap world")
	let s = sb.build()
	b.add(s, tag: 7)
end 'fill'

function main() returns ExitCode
	var b = StrBag.create()
	fill(b)
	let got = try b.first() otherwise 'e'
		return 9 as ExitCode
	end 'e'
	print(got)
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:47:22: Unsupported: a container whose element is 'Bag.EBox' — a generic instance written over this type's OWN parameter, resting (itself, or through an instance it holds) a field declared AT that parameter — cannot be a CONTAINER ELEMENT: the shared generic body compiles once for every instantiation, so releasing such a record means releasing a field whose destructor is a fact about the enclosing instantiation, and the only entry that knows it takes the instantiation's layout descriptor as a second argument (`__destruct_dict_<instance>(descriptor, box)`). A container stamps ONE machine word as its element destructor and calls it with the element alone, so there is nowhere to carry that descriptor. It is refused at the two arrivals that stamp one: a container's element (an `Array`/`__ManagedList` `create` inside the body, and any `push`/`insert`/`upsert`/`set` of one), a `List` node, and a `Map` or `Set` column. Holding ONE such record in a FIELD of the enclosing type (`Self{one: Inner.create(x, …)}`) is admitted — the enclosing instantiation is concrete wherever it is freed, so its own destructor releases the field. Otherwise: hold the values in a container of the type PARAMETER itself (`Array with <type parameter>`, whose element destructor IS carried by the enclosing instance's layout descriptor), give the inner type a concrete field instead of one declared at the parameter, or build and hold the record in a method of a concrete instantiation
```

### …and the TRIVIAL instantiation of that two-level shape still runs

The false-reject control for the widening above, and it is the reason the walk asks the INSTANTIATION at the
bottom of the recursion rather than refusing every nested instance it meets. Byte-identical to the program
above with `String` replaced by a ranged `int`: `Inner`'s bare `U` owns nothing at run time under every `with`
the program writes, so the column's `__mm_decref` IS the correct element destructor and the program is whole.

<!-- test: a-trivial-instantiation-of-the-two-level-shape-still-runs -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Small = int(0 to 100)

type Inner uses U
	let x as U

	export function value() returns U
		return x
	end 'value'

	static function create(x U) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Box uses T
	typealias TInner = Inner with T
	let inner as TInner
	let tag as Integer

	export function value() returns T
		return inner.value()
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{inner: TInner.create(x), tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias BoxArray = Array with EBox
	var items as BoxArray

	export function add(x Element, tag Integer)
		items.push(EBox.create(x, tag: tag))
	end 'add'

	export function first() returns Element throws ArrayError
		let slot = try self.items.first() otherwise 'e'
			throw ArrayError.indexOutOfBounds
		end 'e'
		return slot.value()
	end 'first'

	static function create() returns Self
		return Self{items: BoxArray{}}
	end 'create'
end 'Bag'

typealias SmallBag = Bag with Small

function fill(b SmallBag)
	b.add(41, tag: 7)
end 'fill'

function main() returns ExitCode
	var b = SmallBag.create()
	fill(b)
	let got = try b.first() otherwise 'e'
		return 9 as ExitCode
	end 'e'
	return got
end 'main'
```
```exitcode
41
```

### A hundred borrowed stores into the PARAMETER'S OWN container balance

The refusal above tells the author to hold the values in `Array with <type parameter>` instead, and a
refusal's message is a claim the corpus has to check. This is that program: a hundred trips storing the same
borrowed `String` into the enclosing parameter's own container, whose element destructor IS carried by the
enclosing instance's layout descriptor (`destroyFunc@40`) — so a hundred references are taken through
`retainFunc@64` and a hundred are released.

⚠ **THE SOURCE DIES BEFORE THE READ, WHICH IS THE WHOLE DISCRIMINATOR.** `load`'s heap `String` is released
at its scope exit while the bag lives on in `main`; a store that kept a raw borrow would fault on the read.
And the direction no exit code can see is the other one: an over-retain leaks and the gate reports **101**,
which a single store could not tell from a rounding error and a hundred can.

<!-- test: a-hundred-borrowed-stores-into-the-parameters-own-container-balance -->
```maxon
typealias Idx = int(0 to u64.max)

type Bag uses Element
	typealias Items = Array with Element
	var items as Items

	export function fill(x Element, times Idx)
		var n = 0 as Idx
		while n < times 'fill'
			items.push(x)
			n = n + 1
		end 'fill'
	end 'fill'

	export function at(i Idx) returns Element throws ArrayError
		return try self.items.get(i)
	end 'at'

	static function create() returns Self
		return Self{items: Items{}}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String

function load(b StrBag)
	var sb = StringBuilder.create()
	sb.append("a repeated borrowed string long enough to force a heap allocation")
	let s = sb.build()
	b.fill(s, times: 100)
end 'load'

function main() returns ExitCode
	var b = StrBag.create()
	load(b)
	let middle = try b.at(50) otherwise return 1
	print("{middle}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a repeated borrowed string long enough to force a heap allocation
```

### A TRIVIAL instantiation forwards through the same path and takes nothing

**The false-reject control for the refusal above, and it is load-bearing.** This is the byte-identical
program with `String` replaced by a ranged `int`: the same `EBox.create(x, tag: tag)` forward into the same
`Array with EBox`. Every instantiation of `Bag` here makes `Element` a scalar, so the box's bare `T` field
owns nothing at run time, the column's `__mm_decref` IS the correct element destructor, and the program is
whole. The refusal therefore asks the INSTANTIATION and not merely the shape — a rule that refused every
container of records over the enclosing parameter would reject this one, which owes nothing to anybody.

<!-- test: a-trivial-instantiation-forwards-and-takes-nothing -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	let x as T
	let tag as Integer

	export function value() returns T
		return x
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias BoxArray = Array with EBox
	var items as BoxArray

	export function add(x Element, tag Integer)
		items.push(EBox.create(x, tag: tag))
	end 'add'

	export function first() returns Element throws ArrayError
		let slot = try self.items.first() otherwise 'e'
			throw ArrayError.indexOutOfBounds
		end 'e'
		return slot.value()
	end 'first'

	static function create() returns Self
		return Self{items: BoxArray{}}
	end 'create'
end 'Bag'

typealias IntBag = Bag with Integer

function fill(b IntBag)
	b.add(42, tag: 7)
end 'fill'

function main() returns ExitCode
	var b = IntBag.create()
	fill(b)
	let got = try b.first() otherwise 'e'
		return 9 as ExitCode
	end 'e'
	return got as ExitCode
end 'main'
```
```exitcode
42
```

### The same record reaching a `List` node — the arrival the container guard could not see

`List` has been a DECLARED corpus generic since W153, so a `List with EBox` never passes the container
`create` guard at all: its chain is built inside `List`'s OWN shared body over `List`'s own opaque `Element`,
which routes to `emitOpaqueChainCreateOp`. And `Bag`'s `items.append(EBox.create(…))` is an ordinary CALL, so
it reaches no move-in door in `Bag` either. **MEASURED at the BATCH41 review's merge and again after it:
compiled, and segfaulted at the read.**

⇒ It is refused at the third arrival — a FEED, which is the position whose whole meaning is *"the callee will
keep this"*. Both feed sinks are durable (a constructor feed fills a record the callee returns, a
callee-storage feed fills a container the callee keeps), so the fact is asked of the FEED and not of the
sink, and it is asked before the consume/borrow split because both halves store.

<!-- test: error.a-record-over-the-enclosing-parameter-fed-to-a-list-is-refused -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Idx = int(0 to u64.max)

type Box uses T
	export let x as T
	export let tag as Integer

	export function value() returns T
		return x
	end 'value'

	export static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias Store = List with EBox
	var items as Store

	export function add(x Element, tag Integer)
		items.append(EBox.create(x, tag: tag))
	end 'add'

	export function first() returns Element
		let slot = try items.first() otherwise panic("empty")
		return slot.value()
	end 'first'

	static function create() returns Self
		return Self{items: Store.create()}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String

function fill(b StrBag)
	var sb = StringBuilder.create()
	sb.append("hello ")
	sb.append("heap world")
	b.add(sb.build(), tag: 7)
end 'fill'

function main() returns ExitCode
	var b = StrBag.create()
	fill(b)
	print("{b.first()}\n")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:24:9: Unsupported: a container element fed as a value of type 'Bag.EBox' — a generic instance written over this type's OWN parameter, resting (itself, or through an instance it holds) a field declared AT that parameter — cannot be a CONTAINER ELEMENT: the shared generic body compiles once for every instantiation, so releasing such a record means releasing a field whose destructor is a fact about the enclosing instantiation, and the only entry that knows it takes the instantiation's layout descriptor as a second argument (`__destruct_dict_<instance>(descriptor, box)`). A container stamps ONE machine word as its element destructor and calls it with the element alone, so there is nowhere to carry that descriptor. It is refused at the two arrivals that stamp one: a container's element (an `Array`/`__ManagedList` `create` inside the body, and any `push`/`insert`/`upsert`/`set` of one), a `List` node, and a `Map` or `Set` column. Holding ONE such record in a FIELD of the enclosing type (`Self{one: Inner.create(x, …)}`) is admitted — the enclosing instantiation is concrete wherever it is freed, so its own destructor releases the field. Otherwise: hold the values in a container of the type PARAMETER itself (`Array with <type parameter>`, whose element destructor IS carried by the enclosing instance's layout descriptor), give the inner type a concrete field instead of one declared at the parameter, or build and hold the record in a method of a concrete instantiation
```

### …and the same `List` shape at a TRIVIAL instantiation still runs

The false-reject control for the arrival above. Byte-identical but for `String` → a ranged `int`: the box's
bare `T` slot owns nothing at run time, so the chain's `element_drop@24` is right as it stands and the
program is whole.

<!-- test: a-trivial-instantiation-of-the-list-shape-still-runs -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Small = int(0 to 100)

type Box uses T
	export let x as T
	export let tag as Integer

	export function value() returns T
		return x
	end 'value'

	export static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias Store = List with EBox
	var items as Store

	export function add(x Element, tag Integer)
		items.append(EBox.create(x, tag: tag))
	end 'add'

	export function first() returns Element
		let slot = try items.first() otherwise panic("empty")
		return slot.value()
	end 'first'

	static function create() returns Self
		return Self{items: Store.create()}
	end 'create'
end 'Bag'

typealias SmallBag = Bag with Small

function main() returns ExitCode
	var b = SmallBag.create()
	b.add(41, tag: 7)
	return b.first()
end 'main'
```
```exitcode
41
```

### The same record with NO CONTAINER ANYWHERE — a plain field, and it ROUND-TRIPS

⭐⭐ **THE ARRIVAL THAT WAS A REFUSAL AND IS NOW AN ANSWER.** `var one as EBox` filled by
`Self{one: EBox.create(x, tag: tag)}`: no container is created, no element is pushed, and the record comes
to rest in a slot that outlives the borrow. Both halves it needs now exist, and they are different halves
in different places:

* the **reference** is taken at the constructor feed, because the `x` handed to `EBox.create` is a borrowed
  opaque `T` and a body compiled once takes its reference through the descriptor's `retainFunc@64`;
* the **release** is `__destruct_Bag_String`'s substituted cascade reaching `__destruct_Box_String`, which
  it has always been able to do — the enclosing instantiation is CONCRETE wherever the bag is freed, so
  nothing here needs the dictionary destructor at all.

**MEASURED at the BATCH41 review's merge: compiled, and segfaulted** — the release existed and the
reference did not. The `String` is built in `fill`, whose `StringBuilder` result dies at that frame's exit,
so a missing reference is a read of freed memory and a surplus one is a leak the gate exits 101 on.

<!-- test: a-record-over-the-enclosing-parameter-in-a-plain-field-round-trips -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export let x as T
	export let tag as Integer

	export function value() returns T
		return x
	end 'value'

	export static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	let one as EBox

	export function first() returns Element
		return one.value()
	end 'first'

	static function create(x Element, tag Integer) returns Self
		return Self{one: EBox.create(x, tag: tag)}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String

function fill() returns StrBag
	var sb = StringBuilder.create()
	sb.append("hello ")
	sb.append("heap world")
	let s = sb.build()
	return StrBag.create(s, tag: 7)
end 'fill'

function main() returns ExitCode
	let b = fill()
	print("{b.first()}\n")
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
hello heap world
```

### …and a record this frame built dies in the frame, which is no arrival at all

The other side of PROVENANCE, and the case that keeps the refusal from being a rule about the TYPE. The
identical `EBox.create(<a borrowed opaque T>)` builds a record whose slot holds a borrow nobody referenced —
and it is a LOCAL that dies before the borrow's owner does, so nothing releases the slot and nothing reads it
afterwards. **MEASURED: exit 0.** A refusal that fired here would be refusing a program that is whole.

<!-- test: a-record-built-from-a-borrow-may-die-in-the-frame -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Idx = int(0 to u64.max)

type Box uses T
	export let x as T
	export let tag as Integer

	export function value() returns T
		return x
	end 'value'

	export static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias Items = Array with Element
	var items as Items

	export function add(x Element)
		items.push(x)
	end 'add'

	export function tagOfFirst() returns Integer
		let w = EBox.create(try items.get(0 as Idx) otherwise panic("oob"), tag: 7)
		return w.tag
	end 'tagOfFirst'

	static function create() returns Self
		return Self{items: Items{}}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String

function main() returns ExitCode
	var b = StrBag.create()
	var sb = StringBuilder.create()
	sb.append("a heap payload the record only borrows")
	b.add(sb.build())
	return b.tagOfFirst() as ExitCode
end 'main'
```
```exitcode
7
```

### …and the third cure the message names: give the inner type a CONCRETE field

A refusal's advice is a claim the corpus has to check, and this file already checks the other two — the
parameter's own container (`a-hundred-borrowed-stores-into-the-parameters-own-container-balance`) and a
concrete instantiation (`the-concrete-spelling-of-the-same-constructor-feed`). This is the third: the same
`Bag with String` holding the same `Array with EBox`, where `Box`'s payload slot is declared `String` rather
than at `Box`'s own parameter. The column's element destructor is then a fact the shared body CAN name, the
store is an ordinary concrete move-in, and the heap payload outlives the helper that made it.

⚠ **THE `Box` IS STILL GENERIC AND `EBox` IS STILL AN INSTANCE OVER THE ENCLOSING PARAMETER** — what changed
is only that no slot of it stands at a parameter this body cannot name. That is exactly the boundary the
refusal reads, so this case is what shows the boundary is the SLOT and not the instantiation.

<!-- test: an-inner-alias-whose-slots-are-all-concrete-is-admitted -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export let x as String
	export let tag as Integer

	export function value() returns String
		return x
	end 'value'

	export static function create(x String, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias Store = Array with EBox
	var items as Store

	export function add(x String, tag Integer)
		items.push(EBox.create(x, tag: tag))
	end 'add'

	export function first() returns String throws ArrayError
		let slot = try self.items.first()
		return slot.value()
	end 'first'

	static function create() returns Self
		return Self{items: Store{}}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String

function fill(b StrBag)
	var sb = StringBuilder.create()
	sb.append("hello ")
	sb.append("heap world")
	b.add(sb.build(), tag: 7)
end 'fill'

function main() returns ExitCode
	var b = StrBag.create()
	fill(b)
	let got = try b.first() otherwise return 9 as ExitCode
	print("{got}\n")
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
hello heap world
```

## A shared body RETURNS the record it built out of a borrow

⭐⭐⭐ **THE SHAPE THE WHOLE MECHANISM EXISTS FOR, AND THE ONE THAT SEGFAULTED ON `main`.**
`Bag.wrap(x Element) returns EBox` builds a `Box with Element` out of a borrowed opaque `Element` and hands
it back. Every other arrival in this file is a value coming to REST; this one leaves the frame entirely, and
the caller that receives it is CONCRETE — `makeOne` holds a `Box with String`, drops it through
`__destruct_Box_String`, and that destructor releases the payload.

⛔⛔ **THE RELEASE WAS ALREADY RIGHT AND THE REFERENCE WAS MISSING, WHICH IS WHY IT FAULTED RATHER THAN
LEAKED.** `makeOne`'s `StringBuilder` result dies at that frame's exit, so the box was left holding a pointer
into freed memory and `main`'s read of it took the fault; the concrete destructor then released a record
nobody had referenced. **MEASURED on the merge base and on `main`, twice: `0xC0000005`, exit 139.** The
constructor feed now takes the reference through the descriptor's `retainFunc@64`
(`Parser.referenceOrMarkOpaqueFeed`), and the pair balances.

⚠ **A `static` SPELLING OF `wrap` IS ADMITTED TOO — provided it returns the enclosing type**, which is the
gate `staticLayoutNeedsSelfReturn` draws and which the descriptor-need seed now asks (see
`a-record-over-the-enclosing-parameter-in-a-plain-field-round-trips`, whose feeding `create` is exactly that).

<!-- test: a-returned-record-outlives-the-borrows-source -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	let x as T
	let tag as Integer

	export function value() returns T
		return x
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	var seed as Integer

	export function wrap(x Element) returns EBox
		return EBox.create(x, tag: seed)
	end 'wrap'

	static function create() returns Self
		return Self{seed: 3}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String
typealias StrBox = Box with String

function makeOne(b StrBag) returns StrBox
	var sb = StringBuilder.create()
	sb.append("a heap payload ")
	sb.append("long enough to allocate")
	let s = sb.build()
	return b.wrap(s)
end 'makeOne'

function main() returns ExitCode
	var b = StrBag.create()
	let boxed = makeOne(b)
	print("{boxed.value()}\n")
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
a heap payload long enough to allocate
```

### A hundred of them, kept in a concrete column, and the arithmetic balances

The exit code says a program did not FAULT; only the leak gate says it took as many references as it gave
back. A hundred trips, each building a fresh heap `String` that dies inside the loop body, each wrapped and
pushed into an `Array with StrBox` whose element destructor is the concrete `__destruct_Box_String` — then
the whole column is destroyed at scope exit. One retain per trip against one release per trip: a missing
release is **exit 101** and a surplus one frees a `String` a live box still holds.

⚠ The column's element is the CONCRETE `Box with String`, not the declaration view — which is the whole
distinction `error.a-container-of-records-over-the-enclosing-parameter-is-refused` draws one section up. A
concrete element's destructor is a symbol and fits the record's one-word stamp; a declaration view's is
`__destruct_dict_<instance>(descriptor, box)` and does not.

<!-- test: a-hundred-returned-records-balance -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Count = int(0 to u32.max)

type Box uses T
	let x as T
	let tag as Integer

	export function value() returns T
		return x
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	var seed as Integer

	export function wrap(x Element) returns EBox
		return EBox.create(x, tag: seed)
	end 'wrap'

	static function create() returns Self
		return Self{seed: 3}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String
typealias StrBox = Box with String
typealias BoxArray = Array with StrBox

function main() returns ExitCode
	var b = StrBag.create()
	var kept = BoxArray.create()
	var i = 0 as Count
	while i < 100 'fill'
		var sb = StringBuilder.create()
		sb.append("payload number ")
		sb.append("{i} long enough to be a heap record")
		kept.push(b.wrap(sb.build()))
		i = i + 1
	end 'fill'
	print("{kept.count()}\n")
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
100
```
