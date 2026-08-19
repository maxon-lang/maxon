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
error E2015: <fragment>:34:22: Unsupported: a container whose element is 'Bag.EBox' — a generic instance written over this type's OWN parameter, holding a field declared AT that parameter — has no element destructor this body can name: the shared generic body compiles once for every instantiation, so the destructor that releases such a field is a fact about the enclosing instantiation, and the only symbol available here is the declaration view's, which drops the record and leaves the field it holds. Three shapes reach this: a container FIELD declared at an inner alias over the enclosing parameter (`typealias EBox = Box with Element` then `Array with EBox`), the same alias built as a LOCAL, and a `List` over one. Hold the values in a container of the type PARAMETER itself (`Array with <type parameter>`, whose element destructor IS carried by the enclosing instance's layout descriptor), give the inner type a concrete field instead of one declared at the parameter, or build the container in a method of a concrete instantiation. A descriptor slot carrying a nested instance's per-instantiation destructor is a later slice
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
