---
feature: generic-opaque-cursor-element
status: stable
keywords: [generics, type-parameter, ownership, layout-descriptor, iterable, for-in, cursor, dictionary]
category: type-system
---

# A `for … in self` whose element is the enclosing type's own parameter

## Documentation

`stdlib/Interfaces.maxon`'s `extension Iterable` is re-parsed once per CONFORMER, so a
`type Bag uses Element implements Iterable with (Element, BagIter)` gets a `Bag.contains` whose body is
`for item in self`. The loop's element is `current()`'s result, and a `returns <type parameter>` impl
hands its caller a `+1` (`generic-opaque-owned-return` owns that convention) — so the trip OWNS its
element and releases it before the next one begins.

For a shared generic body that release is `__drop_type_param`, which reads the destructor out of the
enclosing instance's layout descriptor at run time. The descriptor arrives as a hidden parameter reserved
before the body is parsed, and **nothing could see that this body needed one**: the loop's
`createIterator()`, `current()` and `advance()` are emitted by the compiler, so they appear in no token
the descriptor-need pre-scan reads, and the self-call edges it follows are token-shaped too. The body
therefore emitted a drop with nothing to drop through and was refused with `E2015` — at
`stdlib/Interfaces.maxon`, a line the user never wrote.

**`contains` is the one traversal in the library that drops rather than moves**, which is why the hole
stayed shut until a corpus type reached it: `map` and `filter` `push` their element into an
`Array with Element` and that store already reserves the descriptor. And every listed conformer escapes
for a different accidental reason — `Set` and `Array` declare their own `contains`, `Map`'s element is a
tuple rather than a bare parameter, `Range` and `String` have concrete elements — so a user's own generic
`Iterable` is the first thing in the language to reach it.

### What the reservation rests on, and what it deliberately does not reach

The source must be the BARE receiver. `for … in self` walks the enclosing instance, so the protocol calls
are DIRECT into the conformer's own shared bodies and the descriptor the drop reads is the one the method
already carries.

Any other source — a parameter, a field, a local — may be a value held at a parameterized interface or at
a `where`-constrained type parameter, whose protocol calls are witness dispatches through a table that
carries no dictionary at all. That shape is still refused, and the refusal is what keeps it from being
built; see `error.a-cursor-over-a-parameter-is-still-refused` below.

Nor does the reservation follow from the LOOP alone: it is taken only when the cursor genuinely yields a
bare type parameter. A conformer whose element is concrete — `Map`, whose `MapIterator.current()` hands
back a `(Key, Value)` tuple — owes no `__drop_type_param` at all, and handing it a descriptor would feed a
real one into a chain that is deliberately inert on both ends.

### A cursor has TWO spellings, and the reservation is a property of the TYPE, not of the spelling

`createIterator()` may name its cursor directly (`returns BagIterator`) or through an inner generic-instance
alias (`typealias BagIter = BagIterator with Element`, then `returns BagIter`). Only the first is a type's
own name: a method is filed under the type that declares it, so `BagIter.current` resolves to nothing and the
question "does this cursor hand out a bare type parameter?" silently answered *no* for the aliased spelling —
no descriptor, and the loop's own drop refused at a line the user never wrote.

**The corpus writes it the second way.** `stdlib/List.maxon`, `stdlib/Set.maxon`, `stdlib/Array.maxon` and
`stdlib/Vector.maxon` all declare a `<Type>Iter` alias; `stdlib/Map.maxon` is the only conformer in the library
that spells its cursor bare. So the spelling that worked was the one nothing in the library uses.

## Tests

### `contains` on a generic conformer

The program the refusal stood in front of. `isBig(30)` is true, so the loop takes the early-exit path out
of the middle of the body — the trip's release has to happen on that exit as well as on the fall-through.

<!-- test: contains-on-a-generic-conformer -->
```maxon
typealias Int = int(i64.min to i64.max)

type BagIter uses Element implements Iterator with Element
	var slot as Element

	export static function create(v Element) returns Self
		return Self{slot: v}
	end 'create'

	export function current() returns Element
		return slot
	end 'current'

	export function advance() throws IterationError
		throw IterationError.exhausted
	end 'advance'
end 'BagIter'

type Bag uses Element implements Iterable with (Element, BagIter)
	var slot as Element

	export static function create(v Element) returns Self
		return Self{slot: v}
	end 'create'

	export function createIterator() returns BagIter throws IterationError
		return BagIter.create(slot)
	end 'createIterator'
end 'Bag'

typealias IntBag = Bag with Int

function isBig(n Int) returns bool
	return n > 10
end 'isBig'

function main() returns ExitCode
	var b = IntBag.create(30)
	if b.contains(isBig) 'yes'
		return 1
	end 'yes'
	return 0
end 'main'
```
```exitcode
1
```

### The same `contains`, with the cursor named through an alias

The program above with ONE thing changed: the cursor is reached through
`typealias BagIter = BagIterator with Element` instead of by its own name. Nothing about what the loop OWNS
has moved — the element is still `current()`'s bare type parameter and is still released per trip — so this
case and the one above must answer identically. It is `stdlib/List.maxon:147`'s spelling, and while it was
unresolved every program whose cone reached `extension Iterable` was refused.

<!-- test: contains-through-an-aliased-cursor -->
```maxon
typealias Int = int(i64.min to i64.max)

type BagIterator uses Element implements Iterator with Element
	var slot as Element

	export static function create(v Element) returns Self
		return Self{slot: v}
	end 'create'

	export function current() returns Element
		return slot
	end 'current'

	export function advance() throws IterationError
		throw IterationError.exhausted
	end 'advance'
end 'BagIterator'

type Bag uses Element implements Iterable with (Element, BagIter)
	typealias BagIter = BagIterator with Element
	var slot as Element

	export static function create(v Element) returns Self
		return Self{slot: v}
	end 'create'

	export function createIterator() returns BagIter throws IterationError
		return BagIter.create(slot)
	end 'createIterator'
end 'Bag'

typealias IntBag = Bag with Int

function isBig(n Int) returns bool
	return n > 10
end 'isBig'

function main() returns ExitCode
	var b = IntBag.create(30)
	if b.contains(isBig) 'yes'
		return 1
	end 'yes'
	return 0
end 'main'
```
```exitcode
1
```

### An aliased cursor over a MANAGED element, every trip

The aliased spelling's half of the refcount arithmetic, and the half a wrong reservation cannot hide in: five
trips take five references and give five back, on the fall-through exit. A missing release is exit 101 and a
surplus one frees the `String` the bag still holds.

<!-- test: aliased-cursor-releases-every-trip -->
```maxon
typealias Count = int(0 to u32.max)

type BagIterator uses Element implements Iterator with Element
	var slot as Element
	var remaining as Count

	export static function create(v Element, remaining Count) returns Self
		return Self{slot: v, remaining: remaining}
	end 'create'

	export function current() returns Element
		return slot
	end 'current'

	export function advance() throws IterationError
		if remaining <= 1 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		remaining = remaining - 1
	end 'advance'
end 'BagIterator'

type Bag uses Element implements Iterable with (Element, BagIter)
	typealias BagIter = BagIterator with Element
	var slot as Element
	let repeats as Count

	export static function create(v Element, repeats Count) returns Self
		return Self{slot: v, repeats: repeats}
	end 'create'

	export function createIterator() returns BagIter throws IterationError
		return BagIter.create(slot, remaining: repeats)
	end 'createIterator'
end 'Bag'

typealias StrBag = Bag with String

function isEmpty(s String) returns bool
	return s.byteLength() == 0
end 'isEmpty'

function main() returns ExitCode
	var b = StrBag.create("a string long enough to be heap allocated", repeats: 5)
	if b.contains(isEmpty) 'found'
		return 1
	end 'found'
	print("none of 5\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
none of 5
```

### The same body over a MANAGED element

`Bag with String` makes the per-trip `+1` a real reference. One retain per trip against one release per
trip is the whole of the contract: a missing release is exit 101, and a surplus one frees the `String` the
bag still holds.

<!-- test: contains-over-a-managed-element -->
```maxon
type BagIter uses Element implements Iterator with Element
	var slot as Element

	export static function create(v Element) returns Self
		return Self{slot: v}
	end 'create'

	export function current() returns Element
		return slot
	end 'current'

	export function advance() throws IterationError
		throw IterationError.exhausted
	end 'advance'
end 'BagIter'

type Bag uses Element implements Iterable with (Element, BagIter)
	var slot as Element

	export static function create(v Element) returns Self
		return Self{slot: v}
	end 'create'

	export function createIterator() returns BagIter throws IterationError
		return BagIter.create(slot)
	end 'createIterator'
end 'Bag'

typealias StrBag = Bag with String

function isLong(s String) returns bool
	return s.byteLength() > 5
end 'isLong'

function main() returns ExitCode
	var b = StrBag.create("a string long enough to be heap allocated")
	if b.contains(isLong) 'long'
		print("long\n")
		return 1
	end 'long'
	print("short\n")
	return 0
end 'main'
```
```exitcode
1
```
```stdout
long
```

### Every trip releases its own element

The arithmetic is the argument: N trips take N references and give N back, and the fall-through exit —
the one the early-return case above never reaches — is where the last of them is released. The predicate
matches nothing, so every trip runs.

<!-- test: every-trip-releases-its-own-element -->
```maxon
typealias Count = int(0 to u32.max)

type BagIter uses Element implements Iterator with Element
	var slot as Element
	var remaining as Count

	export static function create(v Element, remaining Count) returns Self
		return Self{slot: v, remaining: remaining}
	end 'create'

	export function current() returns Element
		return slot
	end 'current'

	export function advance() throws IterationError
		if remaining <= 1 'atTheLast'
			throw IterationError.exhausted
		end 'atTheLast'
		remaining = remaining - 1
	end 'advance'
end 'BagIter'

type Bag uses Element implements Iterable with (Element, BagIter)
	var slot as Element
	let repeats as Count

	export static function create(v Element, repeats Count) returns Self
		return Self{slot: v, repeats: repeats}
	end 'create'

	export function createIterator() returns BagIter throws IterationError
		return BagIter.create(slot, remaining: repeats)
	end 'createIterator'
end 'Bag'

typealias StrBag = Bag with String

function isEmpty(s String) returns bool
	return s.byteLength() == 0
end 'isEmpty'

function main() returns ExitCode
	var b = StrBag.create("a string long enough to be heap allocated", repeats: 5)
	if b.contains(isEmpty) 'found'
		return 1
	end 'found'
	print("none of 5\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
none of 5
```

### error.a-cursor-over-a-parameter-is-still-refused

⛔ **THE HALF THAT IS NOT SERVED, AND THE REFUSAL IS LOAD-BEARING.** `source` is held at
`Bag with (Element, Cursor with Element)` — a PARAMETERIZED INTERFACE over the enclosing type's own
parameter — so `current()` is a witness dispatch into a generic conformer's impl, and a witness table
carries no layout descriptor for that impl to read. Reserving a descriptor here would let the body
compile and the dispatch would jump with a null dictionary: measured, byte-identical on two tips,
`0xC0000005`. The receiver `self` is the one spelling that can never be an existential, which is why it
could be served ahead of the witness-ABI slot and this could not.

<!-- test: error.a-cursor-over-a-parameter-is-still-refused -->
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

	export function count(source ElementBag) returns Integer
		var n = 0 as Integer
		for item in source 'collect'
			n = n + 1
		end 'collect'
		return n
	end 'count'
end 'Collector'

typealias IntCollector = Collector with Integer

function main() returns ExitCode
	var c = IntCollector.create()
	c.items.push(7)
	print("{c.items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:23:18: Unsupported: 'count' owns an opaque type-parameter value it must release on some path, but the method reserves no layout descriptor to release it through — the shared generic body compiles once for every instantiation, so the value's destructor is read from the enclosing instance's descriptor at run time, and the parameter carrying it is reserved only for the method shapes that are known ahead of the body to need one. Three shapes reach this: a type-parameter argument handed to a `push`/`set`/`insert` on something that is NOT an `Array` and so never takes ownership of it (move it into an `Array with <type parameter>` or a type-parameter field instead); a `pop`/`remove`/`removeFirst` of an opaque element in a `static function` (do it on an instance method, which can source the descriptor from `self`); a `for … in` over a value held at a PARAMETERIZED interface, whose element is the enclosing type's own parameter and is owned per trip (store it into an `Array with <type parameter>`, which reserves the descriptor, or iterate in a method of a concrete instantiation); and a CLOSURE written inside any of them, whose lifted body is a function of its own and reserves no descriptor however well served the method around it is (do the owning work in the method and hand the closure a value it need not release)
```
