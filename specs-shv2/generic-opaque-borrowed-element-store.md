---
feature: generic-opaque-borrowed-element-store
status: stable
keywords: [generics, type-parameter, ownership, layout-descriptor, retain, array, container]
category: type-system
---

# Storing a BORROWED opaque type parameter into a container

## Documentation

A store into a container has to hand the container a reference the container OWNS — the container's
own element walk (`__arr_decref` through the layout descriptor's `destroyFunc@40`) releases exactly
one per slot. A shared generic body has two ways to supply that reference: **give up the one it
holds** (a move), or **take a new one** through the enclosing instance's descriptor
(`__retain_type_param` reading `retainFunc@64`).

When the element is a BORROW the first way does not exist — there is nothing this frame may give up
— so the second is the only one, and it is the one that is taken. `items.push(try other.get(i) …)`,
`items.push(self.saved)` and a `for` element pushed into a second container are all this shape.

**The arithmetic is the argument.** The borrow's owner still owns it and still releases it once. The
store takes one reference; the container releases one when it is destroyed. Neither side depends on
the other's lifetime, so the source may be dropped first, the container may be dropped first, and a
loop may take N of them.

**A trivial instantiation pays nothing.** `retainFunc@64` is the zero word for a type argument that
owns no record, so `__retain_type_param` loads it, skips the indirect call and hands back exactly the
value it was given — and `destroyFunc@40` is zero with it, so the container's walk releases nothing.
The two words are stamped from one `typeParamOwnershipProtocol` answer and are therefore non-zero
together.

**A consumed feed parameter is NOT this shape.** It is a value the frame OWNS, and it discharges by
moving (or, across a loop back edge, by taking a reference of its own —
`generic-opaque-element-loop-store`). The distinction is the frame's ownership of the value, not the
spelling of the store.

**A borrowed value of a CONCRETE type is refused, and that refusal is about TYPES.** The reference
would be taken through a descriptor written for whatever type the caller chose, and a shared body has
no caller frame to check a literal against — so `items.push("text")` stays a positioned error rather
than becoming a `Bag with Int` storing a String record through a zero retain word.

## Tests

### A borrowed element read from another container is stored

The straight-line shape. `other`'s element is a borrow — `other` still owns it, and still releases it
— so the store takes its own reference through the descriptor. Both containers are destroyed at the
end of `main` and the record is freed exactly once, which a missing retain turns into a double free
and a missing release turns into exit 101.

<!-- test: a-borrowed-element-read-from-another-container-is-stored -->
```maxon
typealias Idx = int(0 to u64.max)
typealias Strs = Array with String

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	export static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	export function count() returns Idx
		return items.count()
	end 'count'

	export function takeFirstOf(other Items)
		let borrowed = try other.get(0) otherwise 'getErr'
			panic("Bag.takeFirstOf: get(0) OOB — the caller passed a non-empty container")
		end 'getErr'
		items.push(borrowed)
	end 'takeFirstOf'

	export function at(i Idx) returns Element throws ArrayError
		return try items.get(i)
	end 'at'
end 'Bag'

typealias StrBag = Bag with String

function main() returns ExitCode
	var b = StrBag.create()
	var source = Strs.create()
	source.push("a borrowed string long enough to force a heap allocation")
	b.takeFirstOf(source)
	let stored = try b.at(0) otherwise return 1
	let original = try source.get(0) otherwise return 1
	print("{stored}\n")
	print("{original}\n")
	return b.count() as ExitCode
end 'main'
```
```exitcode
1
```
```stdout
a borrowed string long enough to force a heap allocation
a borrowed string long enough to force a heap allocation
```

### The loop form copies every borrowed element

`stdlib/Array.maxon`'s `appendMemory` is this program: read element `i` out of a source the callee
does not own, and push it. Each iteration takes its own reference and each slot releases one.

<!-- test: the-loop-form-copies-every-borrowed-element -->
```maxon
typealias Idx = int(0 to u64.max)
typealias Strs = Array with String

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	export static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	export function count() returns Idx
		return items.count()
	end 'count'

	export function copyFrom(other Items)
		let n = other.count()
		for i in 0 upto n 'copy'
			let value = try other.get(i) otherwise 'getErr'
				panic("Bag.copyFrom: get OOB — i < the count just read")
			end 'getErr'
			items.push(value)
		end 'copy'
	end 'copyFrom'

	export function at(i Idx) returns Element throws ArrayError
		return try items.get(i)
	end 'at'
end 'Bag'

typealias StrBag = Bag with String

function main() returns ExitCode
	var b = StrBag.create()
	var source = Strs.create()
	source.push("the first copied string, long enough to force a heap allocation")
	source.push("the second copied string, long enough to force a heap allocation")
	source.push("the third copied string, long enough to force a heap allocation")
	b.copyFrom(source)
	let first = try b.at(0) otherwise return 1
	let last = try b.at(2) otherwise return 1
	print("{first}\n")
	print("{last}\n")
	return b.count() as ExitCode
end 'main'
```
```exitcode
3
```
```stdout
the first copied string, long enough to force a heap allocation
the third copied string, long enough to force a heap allocation
```

### A hundred borrowed stores, so an unpaired retain is a hundred records

One borrowed element, a hundred slots. A reference taken per store and released per slot balances at
any N; one taken and never released is a hundred leaked records and exit 101, and one released twice
faults on the poison byte.

<!-- test: a-hundred-borrowed-stores-balance -->
```maxon
typealias Idx = int(0 to u64.max)
typealias Strs = Array with String

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	export static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	export function count() returns Idx
		return items.count()
	end 'count'

	export function fillFrom(other Items, times Idx)
		var n = 0 as Idx
		while n < times 'fill'
			let value = try other.get(0) otherwise 'getErr'
				panic("Bag.fillFrom: get(0) OOB — the caller passed a non-empty container")
			end 'getErr'
			items.push(value)
			n = n + 1
		end 'fill'
	end 'fillFrom'

	export function at(i Idx) returns Element throws ArrayError
		return try items.get(i)
	end 'at'
end 'Bag'

typealias StrBag = Bag with String

function main() returns ExitCode
	var b = StrBag.create()
	var source = Strs.create()
	source.push("a repeated borrowed string long enough to force a heap allocation")
	b.fillFrom(source, times: 100)
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

### A container dropped straight after the store releases what it took

The container is a LOCAL of the shared body and dies at the method's exit, before the borrow's owner
does. The reference it took is released by its own element walk there — so the source is intact
afterwards, and the record is freed once when the source itself goes.

<!-- test: a-container-dropped-after-the-store-releases-what-it-took -->
```maxon
typealias Idx = int(0 to u64.max)
typealias Strs = Array with String

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	export static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	// The scratch container never leaves this frame: it takes a reference at the store and releases it
	// at the method's exit, so the count it reports is the only thing that survives.
	export function scratchCountOf(other Items) returns Idx
		var scratch = Items.create()
		let borrowed = try other.get(0) otherwise 'getErr'
			panic("Bag.scratchCountOf: get(0) OOB — the caller passed a non-empty container")
		end 'getErr'
		scratch.push(borrowed)
		return scratch.count()
	end 'scratchCountOf'
end 'Bag'

typealias StrBag = Bag with String

function main() returns ExitCode
	var b = StrBag.create()
	var source = Strs.create()
	source.push("a scratch-stored string long enough to force a heap allocation")
	let n = b.scratchCountOf(source)
	let survivor = try source.get(0) otherwise return 1
	print("{survivor}\n")
	return n as ExitCode
end 'main'
```
```exitcode
1
```
```stdout
a scratch-stored string long enough to force a heap allocation
```

### A borrowed opaque FIELD read is stored

The other spelling the refusal named: a bare `T` field the record still owns. The record releases its
field and the container releases the slot, and they are two references to one String.

<!-- test: a-borrowed-opaque-field-read-is-stored -->
```maxon
typealias Idx = int(0 to u64.max)

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items
	export var saved as Element

	export static function of(value Element) returns Self
		return Self{items: Items.create(), saved: value}
	end 'of'

	export function count() returns Idx
		return items.count()
	end 'count'

	export function keepSaved()
		items.push(saved)
	end 'keepSaved'

	export function at(i Idx) returns Element throws ArrayError
		return try items.get(i)
	end 'at'
end 'Bag'

typealias StrBag = Bag with String

function main() returns ExitCode
	var b = StrBag.of("a saved field string long enough to force a heap allocation")
	b.keepSaved()
	b.keepSaved()
	let stored = try b.at(1) otherwise return 1
	print("{stored}\n")
	return b.count() as ExitCode
end 'main'
```
```exitcode
2
```
```stdout
a saved field string long enough to force a heap allocation
```

### A trivial element's borrowed store is inert

The identical body at `Bag with Num`. `retainFunc@64` and `destroyFunc@40` are both the zero word for
an argument that owns no record, so the store costs a load and the container's walk releases nothing
— and the answer is the element that was copied across.

<!-- test: a-trivial-elements-borrowed-store-is-inert -->
```maxon
typealias Idx = int(0 to u64.max)
typealias Num = int(0 to 1000)
typealias Nums = Array with Num

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	export static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	export function count() returns Idx
		return items.count()
	end 'count'

	export function copyFrom(other Items)
		let n = other.count()
		for i in 0 upto n 'copy'
			let value = try other.get(i) otherwise 'getErr'
				panic("Bag.copyFrom: get OOB — i < the count just read")
			end 'getErr'
			items.push(value)
		end 'copy'
	end 'copyFrom'

	export function at(i Idx) returns Element throws ArrayError
		return try items.get(i)
	end 'at'
end 'Bag'

typealias NumBag = Bag with Num

function main() returns ExitCode
	var b = NumBag.create()
	var source = Nums.create()
	source.push(4 as Num)
	source.push(7 as Num)
	b.copyFrom(source)
	let second = try b.at(1) otherwise return 1
	return second
end 'main'
```
```exitcode
7
```

### The borrow's owner may be destroyed first

The source container is dropped before the bag is. The reference the store took is the bag's own, so
the element survives its original owner and prints after it is gone.

<!-- test: the-borrows-owner-may-be-destroyed-first -->
```maxon
typealias Idx = int(0 to u64.max)
typealias Strs = Array with String

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	export static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	export function count() returns Idx
		return items.count()
	end 'count'

	export function takeFirstOf(other Items)
		let borrowed = try other.get(0) otherwise 'getErr'
			panic("Bag.takeFirstOf: get(0) OOB — the caller passed a non-empty container")
		end 'getErr'
		items.push(borrowed)
	end 'takeFirstOf'

	export function at(i Idx) returns Element throws ArrayError
		return try items.get(i)
	end 'at'
end 'Bag'

typealias StrBag = Bag with String

function filled() returns StrBag
	var b = StrBag.create()
	var source = Strs.create()
	source.push("an outliving string long enough to force a heap allocation")
	b.takeFirstOf(source)
	return b
end 'filled'

function main() returns ExitCode
	let b = filled()
	let stored = try b.at(0) otherwise return 1
	print("{stored}\n")
	return b.count() as ExitCode
end 'main'
```
```exitcode
1
```
```stdout
an outliving string long enough to force a heap allocation
```

### A borrowed CONCRETE value is refused

The one borrowed store this door still refuses, and it is refused as a TYPE answer rather than an
ownership one. `requireContainerElementType` declines to check an opaque element because the
instantiation's argument is checked where it is concrete — in the CALLER's frame — and a literal
written inside the shared body has no such frame. Accepted, a `Bag with Int` would store a String
record and release it through a zero word; refused, the program gets a position.

<!-- test: error.a-borrowed-concrete-value-is-refused -->
```maxon
typealias Idx = int(0 to u64.max)

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	export static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	export function count() returns Idx
		return items.count()
	end 'count'

	export function pushLiteral()
		items.push("a concrete literal written inside a shared generic body")
	end 'pushLiteral'
end 'Bag'

typealias StrBag = Bag with String

function main() returns ExitCode
	var b = StrBag.create()
	b.pushLiteral()
	return b.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:17:14: Unsupported: storing a borrowed value of a CONCRETE type into an `Array with <type parameter>` in a shared generic body — the body compiles once for every instantiation, so nothing here can check the value against the element type the caller chose, and the reference the container needs would be taken through a descriptor written for a different type. Store a value of the element's own type parameter (a `get`/`first`/`last` read, an opaque field read, a `for` element, or a parameter the method consumes), or write the store in a method of a concrete instantiation
```
