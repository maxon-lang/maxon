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

### A borrowed opaque `T` FORWARDED to another generic's `create()` outlives its source

Every case above stores the borrow into a record the SAME body builds. This one hands it to a SIBLING
GENERIC's constructor — `EBox.create(x, tag: tag)`, where `EBox = Box with Element` is an inner alias over
the enclosing type's own parameter — and `Box.create`'s `Self{x: x}` deliberately takes no reference: a
constructor feed is settled by the CALL SITE (`opaqueSlotTakesItsOwnReference`), which is what the concrete
spelling below does through `argIsConsumedAt`. A SHARED body has no such transfer to make, so the reference
has to be taken here, through the enclosing instance's `retainFunc@64`.

⚠ **THE HEAP-NESS OF THE `String` IS THE WHOLE MEASUREMENT.** With a literal the identical program exits 0
whatever the compiler does — a `.rdata` record is never freed, so a dangling pointer to one still reads
correctly. `sb.build()` is what makes the caller's release the last one.

⛔⛔ **DISABLED, AND THE REASON IS THAT THE STORE SIDE IS ONLY HALF OF IT — MEASURED, BOTH HALVES (BATCH41).**
As it stands the program is a use-after-free: **`0xC0000005`**. With the reference taken at this feed —
`handleEscapingBorrowFeed`'s constructor-feed arm routed through `coOwnBorrowedOpaque`, narrowed to the
instantiations whose `retainFunc@64` can be non-zero — the fault goes away and the program then exits
**101**, because **nothing releases what was taken**. `Bag.create` stamps its element array
`__managed_create(8, __mm_decref)`: the element is `Box with Element`, an instance over the ENCLOSING type's
parameter, so the destructor that would release the payload (`__destruct_Box_String`) is a fact about an
instantiation the shared body cannot name, and the DECLARATION-VIEW destructor it can name reads its own bare
`T` field through `typeIsManaged` and is told the field owns nothing.

⇒ **The retain may not be turned on before the release exists**, and that was measured on every path a record
built over an opaque `T` can take: RETURNED to a CONCRETE caller it is correct and complete (the caller's
`__destruct_<Instance>_<Arg>` releases it); DROPPED inside the shared body, STORED into a container the shared
body created, or RETURNED to another SHARED body (a `createIterator` reaching a `for … in self`) it leaks,
because in all three the record's owner is a declaration-view instance with no per-instantiation destructor.
The compiler already spells that gap for the one form it can refuse — reassigning such a field is *"a
descriptor slot carrying a nested instance's per-instantiation destructor is a later slice"* — and this case
unlocks when that slot exists.

<!-- disabled-test: a-borrowed-opaque-forwarded-to-a-sibling-generic-outlives-its-source -->
<!-- W171 — a per-instantiation release for a record built over the enclosing type parameter -->
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
```exitcode
0
```
```stdout
hello heap world
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

### A hundred forwarded stores BALANCE

The direction no exit code can see. Each trip forwards the same borrowed `String` into a fresh
`Box with Element`, so a hundred references are taken through `retainFunc@64` and a hundred must be released —
by each box's own slot drop when the array is destroyed, and by the source's own owner last. An
over-retain leaks and exits **101**; an under-retain frees the record out from under the reads.

⛔ **DISABLED for the case above's reason, and it is the case that pins the SECOND half.** Today it faults
(`0xC0000005`); with the store-side retain alone it exits **101** exactly a hundred times over. It is
deliberately the loop spelling, because a hundred unbalanced references is a leak no single-store case can
tell from a rounding error in the gate.

<!-- disabled-test: a-hundred-forwarded-constructor-feeds-balance -->
<!-- W171 — a per-instantiation release for a record built over the enclosing type parameter -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Idx = int(0 to u64.max)

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

	export function fill(x Element, times Idx)
		var n = 0 as Idx
		while n < times 'fill'
			items.push(EBox.create(x, tag: 1))
			n = n + 1
		end 'fill'
	end 'fill'

	export function at(i Idx) returns Element throws ArrayError
		let slot = try self.items.get(i) otherwise 'e'
			throw ArrayError.indexOutOfBounds
		end 'e'
		return slot.value()
	end 'at'

	static function create() returns Self
		return Self{items: BoxArray{}}
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

The false-reject control for the narrowing the case above needs. `Bag with Integer` reaches the identical
`EBox.create(x, tag: tag)` forward, and every instantiation of `Bag` in this program makes `Element` a
scalar — so the reference is not merely a no-op at run time, it is never reserved. A rule that took it
unconditionally would demand a layout descriptor of every body forwarding an opaque `T`, and the bodies
that cannot source one — a `static function` returning something other than `Self` — would be refused
`E2015` on a program that owes no reference at all.

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
