---
feature: generic-opaque-element-loop-store
status: stable
keywords: [generics, type-parameter, ownership, layout-descriptor, retain, loop, array, dictionary]
category: type-system
---

# Writing ONE opaque type-parameter value into N slots

## Documentation

A store into a container has to hand the container a reference the container OWNS, and a shared
generic body has exactly two ways to supply one: **give up the reference it holds** (a move), or
**take a new one** through the enclosing instance's layout descriptor (`__retain_type_param` reading
`retainFunc@64`).

A move is the cheaper answer and it is the right one — while this frame will not read the value
again. Inside a LOOP it provably will: the back edge is a later use, and a `movedFrom` bit set once
at the store's TEXT cannot say "moved on iteration 3 and live on iteration 4". So an opaque store
whose source binding was declared OUTSIDE the innermost enclosing loop takes a REFERENCE instead of
moving, and the binding keeps its own — released once at scope exit, exactly as if the store had
never been written.

The arithmetic is the argument. The caller of a consuming feed hands over one reference; the loop
runs N times and takes N; scope exit releases the frame's own one. The container is left holding
exactly N, for **every** N — including **zero**, which is the case the move gets wrong in the other
direction: a body that poisons its parameter at parse time has already decided the store happened,
and a loop that never runs then leaks the caller's reference with nothing left to release it.

`stdlib/Array.maxon`'s `growFilled` and `refill` are this shape and are why this exists: one `value`,
N slots, and each slot destroyed exactly once by the array's own `__managed_decref` walk through
`destroyFunc@40`.

**A BORROWED element reaches the same `__retain_type_param` by the shorter road**, because it has no
reference to give up in the first place — see `generic-opaque-borrowed-element-store`, which owns that
half. What distinguishes the two arms is what happens to the SOURCE: an owned binding keeps its own
reference and releases it at scope exit, and a borrow never had one. A borrow crossing a CALL is a
third question again, answered at the caller (`handleEscapingBorrowFeed`).

**A trivial instantiation pays nothing.** `retainFunc@64` is the zero word for a type argument that
owns no record, so `__retain_type_param` loads it, skips the indirect call and hands back exactly the
value it was given.

## Tests

### One opaque value, three slots

Three slots, one `String`, one shared body. Each slot is released once when the array is destroyed,
so a store that took no reference double-frees and a store that took one too many leaks — the leak
gate reports the second as exit 101 while the program still prints the right answer.

<!-- test: one-opaque-value-written-into-three-slots -->
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

	export function fillTo(newLength Idx, value Element)
		items.resize(newLength)

		var n = 0 as Idx
		while n < newLength 'fill'
			try items.set(n, value: value) otherwise 'setErr'
				panic("Bag.fillTo: set OOB — the length was just resized to newLength")
			end 'setErr'
			n = n + 1
		end 'fill'
	end 'fillTo'

	export function at(i Idx) returns Element throws ArrayError
		return try items.get(i)
	end 'at'
end 'Bag'

typealias StrBag = Bag with String

function main() returns ExitCode
	var b = StrBag.create()
	let s = "a filled string long enough to force a heap allocation"
	b.fillTo(3, value: s)
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
a filled string long enough to force a heap allocation
a filled string long enough to force a heap allocation
```

### A loop that never runs still releases the value

`fillTo(0, …)` stores nothing. The frame was still handed a reference by its consuming caller, so it
still owes exactly one release — the half a parse-time move gets wrong by deciding at the store's
TEXT that the store happened.

<!-- test: a-loop-that-never-runs-still-releases-the-value -->
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

	export function fillTo(newLength Idx, value Element)
		items.resize(newLength)

		var n = 0 as Idx
		while n < newLength 'fill'
			try items.set(n, value: value) otherwise 'setErr'
				panic("Bag.fillTo: set OOB — the length was just resized to newLength")
			end 'setErr'
			n = n + 1
		end 'fill'
	end 'fillTo'
end 'Bag'

typealias StrBag = Bag with String

function main() returns ExitCode
	var b = StrBag.create()
	let s = "an unstored string long enough to force a heap allocation"
	b.fillTo(0, value: s)
	print("{s}\n")
	return b.count() as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
an unstored string long enough to force a heap allocation
```

### Many slots, so a leak is a hundred records rather than one

A hundred slots from one value. A reference taken per iteration and released per slot balances at
any N; one taken and never released is a hundred leaked records, and one released twice faults on
the poison byte.

<!-- test: one-opaque-value-written-into-a-hundred-slots -->
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

	export function fillTo(newLength Idx, value Element)
		items.resize(newLength)

		var n = 0 as Idx
		while n < newLength 'fill'
			try items.set(n, value: value) otherwise 'setErr'
				panic("Bag.fillTo: set OOB — the length was just resized to newLength")
			end 'setErr'
			n = n + 1
		end 'fill'
	end 'fillTo'

	export function at(i Idx) returns Element throws ArrayError
		return try items.get(i)
	end 'at'
end 'Bag'

typealias StrBag = Bag with String

function main() returns ExitCode
	var b = StrBag.create()
	b.fillTo(100, value: "a repeated string long enough to force a heap allocation")
	let middle = try b.at(50) otherwise return 1
	print("{middle}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a repeated string long enough to force a heap allocation
```

### A trivial element's loop store is inert

The identical body at `Bag with Num`. `retainFunc@64` is the zero word for an argument that owns no
record, so the reference the managed case takes costs a load and nothing else here — and the answer
is the one every slot was filled with.

<!-- test: a-trivial-element-loop-store-is-inert -->
```maxon
typealias Idx = int(0 to u64.max)
typealias Num = int(0 to 1000)

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	export static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	export function count() returns Idx
		return items.count()
	end 'count'

	export function fillTo(newLength Idx, value Element)
		items.resize(newLength)

		var n = 0 as Idx
		while n < newLength 'fill'
			try items.set(n, value: value) otherwise 'setErr'
				panic("Bag.fillTo: set OOB — the length was just resized to newLength")
			end 'setErr'
			n = n + 1
		end 'fill'
	end 'fillTo'

	export function at(i Idx) returns Element throws ArrayError
		return try items.get(i)
	end 'at'
end 'Bag'

typealias NumBag = Bag with Num

function main() returns ExitCode
	var b = NumBag.create()
	b.fillTo(4, value: 9 as Num)
	let third = try b.at(3) otherwise return 1
	return third
end 'main'
```
```exitcode
9
```

### The value is still readable after the loop

The binding was never moved, so reading it after the fill is legal — the property a move-based store
takes away, and the one a body that fills a column and then reports what it filled with needs.

<!-- test: the-filled-value-is-still-readable-after-the-loop -->
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

	export function fillTo(newLength Idx, value Element) returns Element
		items.resize(newLength)

		var n = 0 as Idx
		while n < newLength 'fill'
			try items.set(n, value: value) otherwise 'setErr'
				panic("Bag.fillTo: set OOB — the length was just resized to newLength")
			end 'setErr'
			n = n + 1
		end 'fill'
		return value
	end 'fillTo'
end 'Bag'

typealias StrBag = Bag with String

function main() returns ExitCode
	var b = StrBag.create()
	let echoed = b.fillTo(2, value: "a re-read string long enough to force a heap allocation")
	print("{echoed}\n")
	return b.count() as ExitCode
end 'main'
```
```exitcode
2
```
```stdout
a re-read string long enough to force a heap allocation
```

### A borrowed element in the loop takes a reference per slot

The `set` spelling of the borrowed store, and the case that used to pin this door's refusal of one.
`other`'s element is a BORROW — `other` still owns it and still releases it — so each slot takes its
own reference through `retainFunc@64` and the bag's element walk releases exactly as many. Both
containers are destroyed at the end of `main`, so a missing retain is a double free and a missing
release is exit 101.

<!-- test: a-borrowed-element-stored-in-a-loop-takes-a-reference-per-slot -->
```maxon
typealias Idx = int(0 to u64.max)
typealias Strs = Array with String

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	export static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	export function copyFrom(other Items, upTo Idx)
		items.resize(upTo)

		var n = 0 as Idx
		while n < upTo 'copy'
			let borrowed = try other.get(n) otherwise 'getErr'
				break
			end 'getErr'
			try items.set(n, value: borrowed) otherwise 'setErr'
				panic("Bag.copyFrom: set OOB — the length was just resized")
			end 'setErr'
			n = n + 1
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
	source.push("a source string long enough to force a heap allocation")
	source.push("a second source string, also long enough to force a heap allocation")
	b.copyFrom(source, upTo: 2)
	let copied = try b.at(1) otherwise return 1
	let original = try source.get(1) otherwise return 1
	print("{copied}\n")
	print("{original}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a second source string, also long enough to force a heap allocation
a second source string, also long enough to force a heap allocation
```

### The same loop, discharged through a consuming SIBLING rather than through the store

The rule above is about the LOOP and about what the sink owes, not about which door emitted the
store — so a call that CONSUMES its opaque argument owes the identical discharge. `fill`'s loop
hands `v` to the consuming sibling `store`, which is the same three references the `set` spelling
takes, arriving one call boundary out. Written only into the container-store arm, this program was
refused outright (`moving a value declared outside this loop from inside the loop body`) while the
`set` spelling beside it compiled — one rule with two answers.

⭐ **AND IT IS ONE OF THE THINGS THAT KEPT `set` ON `Parser.arraySurfaceMemberNames` (ARRG).** Once
the corpus serves the store, `Bag.fillTo`'s `items.set(n, value: value)` STOPS being the arm and
BECOMES this door: an ordinary call with a consuming opaque parameter. MEASURED with `set`
temporarily struck, before this case existed: five of this file's cases took that refusal. They are
the reason the rule is read here rather than only inside `moveElementIntoContainer` — and this case
pins it WITHOUT the strike, so nothing about it waits on the retirement.

`v` keeps its own reference and releases it at scope exit; the bag's element walk releases the three
it took. A missing retain is a double free at teardown and a surplus one is exit 101.

<!-- test: a-consuming-sibling-called-in-a-loop-takes-a-reference-per-call -->
```maxon
typealias Idx = int(0 to u64.max)

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	export static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	export function store(v Element)
		items.push(v)
	end 'store'

	export function fill(n Idx, v Element)
		var i = 0 as Idx
		while i < n 'fill'
			store(v)
			i = i + 1
		end 'fill'
	end 'fill'

	export function at(i Idx) returns Element throws ArrayError
		return try items.get(i)
	end 'at'

	export function count() returns Idx
		return items.count()
	end 'count'
end 'Bag'

typealias StrBag = Bag with String

function main() returns ExitCode
	var b = StrBag.create()
	let s = "a filled string long enough to force a heap allocation"
	b.fill(3, v: s)
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
a filled string long enough to force a heap allocation
a filled string long enough to force a heap allocation
```

### A loop that never runs still releases what a consuming sibling did not take

The zero-iteration half of the case above, and the one a parse-time move gets wrong in the other
direction: `fill(0, …)` calls `store` never, so the frame still owes exactly the one release its
consuming caller handed it.

<!-- test: a-consuming-sibling-loop-that-never-runs-still-releases-the-value -->
```maxon
typealias Idx = int(0 to u64.max)

type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	export static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	export function store(v Element)
		items.push(v)
	end 'store'

	export function fill(n Idx, v Element)
		var i = 0 as Idx
		while i < n 'fill'
			store(v)
			i = i + 1
		end 'fill'
	end 'fill'

	export function count() returns Idx
		return items.count()
	end 'count'
end 'Bag'

typealias StrBag = Bag with String

function main() returns ExitCode
	var b = StrBag.create()
	let s = "a filled string long enough to force a heap allocation"
	b.fill(0, v: s)
	print("{s}\n")
	return b.count() as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
a filled string long enough to force a heap allocation
```
