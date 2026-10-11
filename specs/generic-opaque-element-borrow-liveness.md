---
feature: generic-opaque-element-borrow-liveness
status: selfhosted
keywords: [borrow, counted, generic, type parameter, opaque, element, use-after-free, liveness]
category: memory
---
# A read of an OPAQUE element is counted in a shared generic body

## Documentation

`specs/borrow-liveness.md` pins counted reads of a CONCRETE managed element. This file pins the same
rule over the element a shared generic body cannot name — a `typeParameter`. The body is compiled once
for every instantiation, and every `get` in it takes its own reference to the element it hands back, so
a later `set` that releases the slot's occupant leaves the value already read intact:

```text
let a = try managed.get(0) otherwise panic("oob")   // a holds its own reference to slot 0's record
let b = try managed.get(1) otherwise panic("oob")
try managed.set(0, value: b) otherwise panic("oob") // releases the slot's reference; a still holds one
try managed.set(1, value: a) otherwise panic("oob") // … and stores it
```

A swap, a rotate or a compaction built from `get` + `set` is therefore correct for a managed element
and for a trivial one alike.

The programs that declare the body of a library container over its raw `__ManagedMemory` are library-tier
cases. The rest are user programs: a write nested in another's arguments, and the generic `Box` bodies,
whose element reads reach an `Array with T` through its public methods.

## Tests

<!-- test: a-swap-through-get-and-set-keeps-both-elements -->
<!-- tier: stdlib -->
### A swap built from get + set keeps the element it is mid-way through moving
The first `set` releases the slot holding the record `a` read; `a` holds its own reference, so the
second `set` stores a live record.
```maxon
type Array uses Element implements BuiltinArrayLiteral
	export typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	static function create() returns Self
		return Self{}
	end 'create'

	function rotateFirstTwo()
		let a = try managed.get(0) otherwise panic("oob")
		let b = try managed.get(1) otherwise panic("oob")
		try managed.set(0, value: b) otherwise panic("oob")
		try managed.set(1, value: a) otherwise panic("oob")
	end 'rotateFirstTwo'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha {40 + 2} value long enough for a heap record")
	a.push("beta {40 + 2} value long enough for a heap record")
	a.rotateFirstTwo()
	print("{try a.get(0) otherwise "?"}\n")
	print("{try a.get(1) otherwise "?"}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
beta 42 value long enough for a heap record
alpha 42 value long enough for a heap record
```

<!-- test: a-three-way-rotate-through-get-and-set-keeps-every-element -->
<!-- tier: stdlib -->
### … and a three-way rotate is the same write, one slot further
Each of the first two writes releases a record that a later write stores.
```maxon
type Array uses Element implements BuiltinArrayLiteral
	export typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	static function create() returns Self
		return Self{}
	end 'create'

	function rotateThree()
		let a = try managed.get(0) otherwise panic("oob")
		let b = try managed.get(1) otherwise panic("oob")
		let c = try managed.get(2) otherwise panic("oob")
		try managed.set(0, value: c) otherwise panic("oob")
		try managed.set(1, value: a) otherwise panic("oob")
		try managed.set(2, value: b) otherwise panic("oob")
	end 'rotateThree'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha {40 + 2} value long enough for a heap record")
	a.push("beta {40 + 2} value long enough for a heap record")
	a.push("gamma {40 + 2} value long enough for a heap record")
	a.rotateThree()
	print("{try a.get(0) otherwise "?"}\n")
	print("{try a.get(1) otherwise "?"}\n")
	print("{try a.get(2) otherwise "?"}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
gamma 42 value long enough for a heap record
alpha 42 value long enough for a heap record
beta 42 value long enough for a heap record
```

<!-- test: an-element-displaced-by-a-set-stays-readable -->
<!-- tier: stdlib -->
### An element the write DISPLACES stays readable even when nothing stores it back
The value written comes from a DIFFERENT container; the write releases the slot's occupant, and the
`return` hands back the reference `a` took.
```maxon
type Array uses Element implements BuiltinArrayLiteral
	export typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	static function create() returns Self
		return Self{}
	end 'create'

	function readThenDisplace(other Self) returns Element
		let a = try managed.get(0) otherwise panic("oob")
		let v = try other.managed.get(0) otherwise panic("oob")
		try managed.set(0, value: v) otherwise panic("oob")
		return a
	end 'readThenDisplace'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha {40 + 2} value long enough for a heap record")
	var b = StrArray.create()
	b.push("beta {40 + 2} value long enough for a heap record")
	print("{a.readThenDisplace(b)}\n")
	print("{try a.get(0) otherwise "?"}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
alpha 42 value long enough for a heap record
beta 42 value long enough for a heap record
```

<!-- test: the-bare-spelling-swaps-the-same-way -->
<!-- tier: stdlib -->
### … and the BARE spelling is the same write, which is the spelling `stdlib/Array.maxon` uses
`get(0)` and `set(0, …)` with no receiver written at all count the element exactly as the
`managed.`-prefixed spelling does.
```maxon
type Array uses Element implements BuiltinArrayLiteral
	export typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	static function create() returns Self
		return Self{}
	end 'create'

	function rotateBare()
		let a = try get(0) otherwise panic("oob")
		let b = try get(1) otherwise panic("oob")
		try set(0, value: b) otherwise panic("oob")
		try set(1, value: a) otherwise panic("oob")
	end 'rotateBare'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha {40 + 2} value long enough for a heap record")
	a.push("beta {40 + 2} value long enough for a heap record")
	a.rotateBare()
	print("{try a.get(0) otherwise "?"}\n")
	print("{try a.get(1) otherwise "?"}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
beta 42 value long enough for a heap record
alpha 42 value long enough for a heap record
```

<!-- test: a-trivial-instantiation-of-the-swap-body-runs -->
<!-- tier: stdlib -->
### The same shared body at a trivial instantiation
`Array with <ranged int>` runs the body the `Array with String` instantiation runs; the element is a
bare word, and the swap answers the same way.
```maxon
typealias Small = int(0 to 100)

type Array uses Element implements BuiltinArrayLiteral
	export typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	static function create() returns Self
		return Self{}
	end 'create'

	function rotateFirstTwo()
		let a = try managed.get(0) otherwise panic("oob")
		let b = try managed.get(1) otherwise panic("oob")
		try managed.set(0, value: b) otherwise panic("oob")
		try managed.set(1, value: a) otherwise panic("oob")
	end 'rotateFirstTwo'
end 'Array'

typealias SmallArray = Array with Small

function main() returns ExitCode
	var a = SmallArray.create()
	a.push(7)
	a.push(9)
	a.rotateFirstTwo()
	print("{try a.get(0) otherwise 0} {try a.get(1) otherwise 0}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9 7
```

<!-- test: a-set-fed-from-another-container-stores-a-live-record -->
<!-- tier: stdlib -->
### A write of ONE storage fed from ANOTHER
This is the shape `stdlib/Array.maxon:283`'s `appendMemory` is.
```maxon
type Array uses Element implements BuiltinArrayLiteral
	export typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	static function create() returns Self
		return Self{}
	end 'create'

	function setFrom(other Self)
		let v = try other.managed.get(0) otherwise panic("oob")
		try managed.set(0, value: v) otherwise panic("oob")
	end 'setFrom'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha {40 + 2} value long enough for a heap record")
	var b = StrArray.create()
	b.push("beta {40 + 2} value long enough for a heap record")
	a.setFrom(b)
	print("{try a.get(0) otherwise "?"}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
beta 42 value long enough for a heap record
```

<!-- test: an-opaque-element-stored-elsewhere-survives-its-source-shortened -->
<!-- tier: stdlib -->
### An element read, stored elsewhere, and then its source shortened
The element is read, used to write a DIFFERENT storage, and never named again before `managed`
itself is shortened.
```maxon
type Array uses Element implements BuiltinArrayLiteral
	export typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	static function create() returns Self
		return Self{}
	end 'create'

	function moveFirstInto(other Self)
		let a = try managed.get(0) otherwise panic("oob")
		try other.managed.set(0, value: a) otherwise panic("oob")
		try managed.setLength(0) otherwise panic("length")
	end 'moveFirstInto'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha {40 + 2} value long enough for a heap record")
	var b = StrArray.create()
	b.push("beta {40 + 2} value long enough for a heap record")
	a.moveFirstInto(b)
	print("{a.count()} {try b.get(0) otherwise "?"}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 alpha 42 value long enough for a heap record
```

<!-- test: an-element-moved-down-to-a-lower-slot-stays-live -->
<!-- tier: stdlib -->
### The loop SORTING rests on — `set(w, value: get(i))` through a name
`stdlib/helpers/sort/driftQuicksort.maxon:59-61`, `:103-104` and every `mergeSort.maxon` merge
step are this loop: read the element at `i`, write it down to `w`. The read holds its own reference,
so even `w == i` stores a live record.
```maxon
typealias Small = int(0 to 100)

type Array uses Element implements BuiltinArrayLiteral
	export typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	static function create() returns Self
		return Self{}
	end 'create'

	function compactDown(hi Small)
		var w = 0 as Small
		var i = 0 as Small
		while i < hi 'scan'
			let v = try managed.get(i) otherwise panic("oob")
			try managed.set(w, value: v) otherwise panic("oob")
			w = w + 1
			i = i + 1
		end 'scan'
	end 'compactDown'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha {40 + 2} value long enough for a heap record")
	a.push("beta {40 + 2} value long enough for a heap record")
	a.compactDown(2)
	print("{try a.get(0) otherwise "?"} {try a.get(1) otherwise "?"}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
alpha 42 value long enough for a heap record beta 42 value long enough for a heap record
```

<!-- test: a-push-of-the-containers-own-element-keeps-both-slots-live -->
<!-- tier: stdlib -->
### … and so is appending an element the container already holds
`push` adds a slot and destroys nothing, so the array simply gains a second reference to what it
already owned. `stdlib/Array.maxon:284`'s `appendMemory` is this write.
```maxon
type Array uses Element implements BuiltinArrayLiteral
	export typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	static function create() returns Self
		return Self{}
	end 'create'

	function copyFirst()
		let a = try managed.get(0) otherwise panic("oob")
		push(a)
	end 'copyFirst'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha {40 + 2} value long enough for a heap record")
	a.copyFirst()
	print("{a.count()} {try a.get(1) otherwise "?"}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 alpha 42 value long enough for a heap record
```

<!-- test: a-write-nested-inside-another-writes-arguments -->
### A container write NESTED in another's arguments happens FIRST
The `pop` inside `set`'s argument list runs before the `set`, and the `set` releases the record
`borrowed` read, which `borrowed` still holds.
```maxon
function main() returns ExitCode
	var xs = ["alpha {40 + 2} value long enough for a heap record", "beta {40 + 2} value long enough for a heap record"]
	var ys = ["gamma {40 + 2} value long enough for a heap record"]
	let borrowed = try xs.get(0) otherwise panic("oob")
	try xs.set(0, value: try ys.pop() otherwise "z") otherwise panic("oob")
	print("{borrowed}\n")
	print("{try xs.get(0) otherwise "?"} {ys.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
alpha 42 value long enough for a heap record
gamma 42 value long enough for a heap record 0
```

<!-- test: a-trivial-element-container-is-untouched -->
<!-- tier: stdlib -->
### The trivial-element control
An `Array with <ranged int>` element owns no heap, so an element read is a bare word that no write
can affect.
```maxon
typealias Small = int(0 to 100)

type Array uses Element implements BuiltinArrayLiteral
	export typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	static function create() returns Self
		return Self{}
	end 'create'
end 'Array'

typealias SmallArray = Array with Small

function main() returns ExitCode
	var a = SmallArray.create()
	a.push(7)
	a.push(9)
	let x = try a.get(0) otherwise 0
	let y = try a.get(1) otherwise 0
	try a.set(0, value: y) otherwise panic("oob")
	try a.set(1, value: x) otherwise panic("oob")
	print("{try a.get(0) otherwise 0}{try a.get(1) otherwise 0}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
97
```

<!-- test: an-opaque-element-argument-survives-the-callee-clearing-its-source -->
### An element read straight into an argument survives the callee clearing the array it came from
`clearThenKeep` empties `self.items` and then returns its parameter; the argument holds its own
reference to the element, so what comes back is the record that was read.
```maxon
type Box uses T
	export typealias Items = Array with T
	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	function add(item T)
		items.push(item)
	end 'add'

	function size() returns Count
		return items.count()
	end 'size'

	function clearThenKeep(item T) returns T
		self.items.clear()
		return item
	end 'clearThenKeep'

	function firstAfterClear() returns T
		return clearThenKeep(try self.items.get(0) otherwise panic("oob"))
	end 'firstAfterClear'
end 'Box'

typealias StrBox = Box with String

function main() returns ExitCode
	var b = StrBox.create()
	b.add("alpha {40 + 2} value long enough for a heap record")
	let kept = b.firstAfterClear()
	print("[{kept}] {b.size()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[alpha 42 value long enough for a heap record] 0
```

<!-- test: an-opaque-for-element-survives-a-method-clearing-the-source -->
### An opaque `for` element survives a method that clears the array the loop walks
The body clears `self.items` before it stores `item`; the element holds its own reference, and the
clear ends the loop, so only the first element is stored.
```maxon
type Box uses T
	export typealias Items = Array with T
	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	function add(item T)
		items.push(item)
	end 'add'

	function size() returns Count
		return items.count()
	end 'size'

	function wipe()
		self.items.clear()
	end 'wipe'

	function collectWhileWiping() returns Items
		var seen = Items.create()

		for item in self.items 'scan'
			wipe()
			seen.push(item)
		end 'scan'

		return seen
	end 'collectWhileWiping'
end 'Box'

typealias StrBox = Box with String

function main() returns ExitCode
	var b = StrBox.create()
	b.add("alpha {40 + 2} value long enough for a heap record")
	b.add("beta {40 + 2} value long enough for a heap record")
	let seen = b.collectWhileWiping()
	print("{seen.count()} [{try seen.get(0) otherwise "?"}] {b.size()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 [alpha 42 value long enough for a heap record] 0
```

<!-- test: an-opaque-for-element-in-a-static-survives-a-clear-of-its-source -->
### An opaque `for` element in a `static function` survives a clear of the array the loop walks
A `static function` that does not return `Self` has no receiver, so the descriptor an opaque element is counted
through arrives from the call: `StrBox.moveWhileClearing` hands its instantiation's descriptor to the shared body.
The element holds its own reference, so the `clear` in the body does not free the record before `into.push(item)`
stores it, and the clear ends the loop after the first trip.
```maxon
type Box uses T
	export typealias Items = Array with T
	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	static function moveWhileClearing(items Items, into Items) returns Count
		for item in items 'scan'
			items.clear()
			into.push(item)
		end 'scan'

		return into.count()
	end 'moveWhileClearing'
end 'Box'

typealias StrBox = Box with String
typealias Words = Array with String

function main() returns ExitCode
	var xs = Words.create()
	xs.push("alpha {40 + 2} value long enough for a heap record")
	xs.push("beta {40 + 2} value long enough for a heap record")
	var ys = Words.create()
	let moved = StrBox.moveWhileClearing(xs, into: ys)
	print("{moved} [{try ys.get(0) otherwise "?"}] {xs.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 [alpha 42 value long enough for a heap record] 0
```

<!-- test: an-opaque-element-read-in-a-static-survives-a-clear-of-its-source -->
### An opaque element read by `get` in a `static function` survives a clear of its array
`keepFirst` binds `items.get(0)`, clears `items`, and only then stores the binding, so the binding holds its own
reference through the descriptor the call passes.
```maxon
type Box uses T
	export typealias Items = Array with T
	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	static function keepFirst(items Items, into Items)
		let first = try items.get(0) otherwise panic("empty")
		items.clear()
		into.push(first)
	end 'keepFirst'
end 'Box'

typealias StrBox = Box with String
typealias Words = Array with String

function main() returns ExitCode
	var xs = Words.create()
	xs.push("alpha {40 + 2} value long enough for a heap record")
	var ys = Words.create()
	StrBox.keepFirst(xs, into: ys)
	print("[{try ys.get(0) otherwise "?"}] {xs.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[alpha 42 value long enough for a heap record] 0
```

<!-- test: a-static-reaching-a-static-passes-the-descriptor-on -->
### A `static function` that calls a sibling static hands it the descriptor it was given
`drainAndCount` holds no element itself; it calls `Self.moveWhileClearing`, which does, so it carries the descriptor
for its own caller to fill and forwards it.
```maxon
type Box uses T
	export typealias Items = Array with T
	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	static function moveWhileClearing(items Items, into Items) returns Count
		for item in items 'scan'
			items.clear()
			into.push(item)
		end 'scan'

		return into.count()
	end 'moveWhileClearing'

	static function drainAndCount(items Items, into Items) returns Count
		let moved = Self.moveWhileClearing(items, into: into)

		return moved + into.count()
	end 'drainAndCount'
end 'Box'

typealias StrBox = Box with String
typealias Words = Array with String

function main() returns ExitCode
	var xs = Words.create()
	xs.push("alpha {40 + 2} value long enough for a heap record")
	xs.push("beta {40 + 2} value long enough for a heap record")
	var ys = Words.create()
	let counted = StrBox.drainAndCount(xs, into: ys)
	print("{counted} [{try ys.get(0) otherwise "?"}] {xs.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 [alpha 42 value long enough for a heap record] 0
```

<!-- test: a-static-reaching-a-static-through-an-instance-method-passes-the-descriptor-on -->
### An instance method reaching a `static function` forwards the descriptor it carries
`relocate` is an instance method that calls `Self.moveWhileClearing`; its own receiver supplies the descriptor the
static needs, and a caller outside the type reaches the static through the alias directly.
```maxon
type Box uses T
	export typealias Items = Array with T
	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	static function moveWhileClearing(items Items, into Items) returns Count
		for item in items 'scan'
			items.clear()
			into.push(item)
		end 'scan'

		return into.count()
	end 'moveWhileClearing'

	function add(item T)
		items.push(item)
	end 'add'

	function relocate(into Items) returns Count
		return Self.moveWhileClearing(self.items, into: into)
	end 'relocate'

	function size() returns Count
		return items.count()
	end 'size'
end 'Box'

typealias StrBox = Box with String
typealias Words = Array with String

function main() returns ExitCode
	var b = StrBox.create()
	b.add("alpha {40 + 2} value long enough for a heap record")
	b.add("beta {40 + 2} value long enough for a heap record")
	var ys = Words.create()
	let moved = b.relocate(ys)
	print("{moved} [{try ys.get(0) otherwise "?"}] {b.size()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 [alpha 42 value long enough for a heap record] 0
```

<!-- test: a-static-iterating-a-record-element-holds-it-across-a-clear -->
### The same static over a record element, whose reference is an incref and not a copy
`Tag` is a record, so the descriptor's reference is an incref of the shared box rather than a copy of bytes; the
element read before the `clear` is the record the destination stores.
```maxon
typealias Weight = int(0 to 1000)

type Tag
	var label as String
	var weight as Weight

	static function make(label String, weight Weight) returns Tag
		return Tag{label: label, weight: weight}
	end 'make'

	function describe() returns String
		return "{self.label}/{self.weight}"
	end 'describe'
end 'Tag'

type Box uses T
	export typealias Items = Array with T
	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	static function moveWhileClearing(items Items, into Items) returns Count
		for item in items 'scan'
			items.clear()
			into.push(item)
		end 'scan'

		return into.count()
	end 'moveWhileClearing'
end 'Box'

typealias TagBox = Box with Tag
typealias Tags = Array with Tag

function main() returns ExitCode
	var xs = Tags.create()
	xs.push(Tag.make("alpha {40 + 2} label long enough for a heap record", weight: 7))
	xs.push(Tag.make("beta {40 + 2} label long enough for a heap record", weight: 9))
	var ys = Tags.create()
	let moved = TagBox.moveWhileClearing(xs, into: ys)
	let first = try ys.get(0) otherwise panic("empty")
	print("{moved} [{first.describe()}] {xs.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 [alpha 42 label long enough for a heap record/7] 0
```

<!-- test: a-static-iterating-a-trivial-element-needs-no-reference -->
### The same static over an `int` element, whose reference is nothing
The one body serves every instantiation: for an `int` element the descriptor's retain is inert, so the element is
stored as it stands.
```maxon
typealias Count8 = int(0 to 255)

type Box uses T
	export typealias Items = Array with T
	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	static function moveWhileClearing(items Items, into Items) returns Count
		for item in items 'scan'
			items.clear()
			into.push(item)
		end 'scan'

		return into.count()
	end 'moveWhileClearing'
end 'Box'

typealias SmallBox = Box with Count8
typealias Smalls = Array with Count8

function main() returns ExitCode
	var xs = Smalls.create()
	xs.push(7)
	xs.push(9)
	var ys = Smalls.create()
	let moved = SmallBox.moveWhileClearing(xs, into: ys)
	print("{moved} [{try ys.get(0) otherwise 0}] {xs.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 [7] 0
```

<!-- test: a-static-of-another-generic-reached-through-an-alias-over-the-callers-parameter-gets-the-callers-descriptor -->
### A static of one generic, called through an alias written over another generic's parameter
`Outer.relocate` reaches `Inner.keepLast` through `Inner with U`, an instance over its own parameter, so the descriptor
the static takes its references through is composed from the one `relocate` was handed.
```maxon
type Inner uses T
	export typealias Items = Array with T

	static function keepLast(items Items, into Items)
		for item in items 'scan'
			into.push(item)
			items.clear()
		end 'scan'
	end 'keepLast'
end 'Inner'

type Outer uses U
	typealias InnerU = Inner with U
	typealias Us = Array with U
	var us as Us

	static function create() returns Self
		return Self{us: Us.create()}
	end 'create'

	function add(u U)
		us.push(u)
	end 'add'

	function relocate(into Us) returns Count
		InnerU.keepLast(self.us, into: into)

		return into.count()
	end 'relocate'
end 'Outer'

typealias StrOuter = Outer with String
typealias Words = Array with String

function main() returns ExitCode
	var o = StrOuter.create()
	o.add("alpha {40 + 2} value long enough for a heap record")
	o.add("beta {40 + 2} value long enough for a heap record")
	var ys = Words.create()
	let n = o.relocate(ys)
	print("{n} [{try ys.get(0) otherwise "?"}]\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 [alpha 42 value long enough for a heap record]
```

<!-- test: a-throwing-static-over-an-opaque-element-passes-the-descriptor-to-its-try-call -->
### A throwing `static function` receives the descriptor through a `try` call as well
`keepFirst` throws on an empty source; the descriptor travels with the checked call exactly as it does with a plain
one, so the second, failing call is caught and the first call's element survives the clear.
```maxon
enum BoxError implements Error
	empty
end 'BoxError'

type Box uses T
	export typealias Items = Array with T
	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	static function keepFirst(items Items, into Items) throws BoxError
		if items.isEmpty() 'none'
			throw BoxError.empty
		end 'none'

		let first = try items.get(0) otherwise panic("empty")
		items.clear()
		into.push(first)
	end 'keepFirst'
end 'Box'

typealias StrBox = Box with String
typealias Words = Array with String

function main() returns ExitCode
	var xs = Words.create()
	xs.push("alpha {40 + 2} value long enough for a heap record")
	var ys = Words.create()
	try StrBox.keepFirst(xs, into: ys) otherwise return 3
	try StrBox.keepFirst(xs, into: ys) otherwise 'second'
		print("second call refused\n")
	end 'second'
	print("[{try ys.get(0) otherwise "?"}] {xs.count()} {ys.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
second call refused
[alpha 42 value long enough for a heap record] 0 1
```

<!-- test: error.a-static-reached-through-a-bare-generic-base-has-no-descriptor-to-take -->
`Box.moveWhileClearing` names the generic base without type arguments, so nothing fixes the instantiation whose
descriptor the static takes its references through, and the call is refused rather than handed a descriptor that
describes some other type. Naming the instance through a `typealias` is the remedy.
```maxon
type Box uses T
	export typealias Items = Array with T
	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	static function moveWhileClearing(items Items, into Items) returns Count
		for item in items 'scan'
			items.clear()
			into.push(item)
		end 'scan'

		return into.count()
	end 'moveWhileClearing'
end 'Box'

typealias StrBox = Box with String
typealias Words = Array with String

function main() returns ExitCode
	var xs = Words.create()
	xs.push("alpha {40 + 2} value long enough for a heap record")
	var ys = Words.create()
	let moved = Box.moveWhileClearing(xs, into: ys)
	let named = StrBox.moveWhileClearing(xs, into: ys)
	print("{moved} {named}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3162: <fragment>:27:18: 'Box' is named without type arguments and nothing binds its parameter 'T', so 'Box.moveWhileClearing' has no layout descriptor to be handed: the base has none, and neither does this function. Name the instance with a typealias, giving each of its parameters a type argument
```

<!-- test: a-field-read-binary-operand-survives-a-later-call-releasing-its-owner -->
### A field read held as the left operand of `==` survives the call that follows it
`replace()` reassigns the field the left operand was read from, which frees the record the operand names, and then
allocates a record of the same size. The comparison still sees the record that was read.
```maxon
type Slot uses T where T is Equatable
	var current as T
	var spare as T
	var scrub as String

	static function create(first T, second T) returns Self
		return Self{current: first, spare: second, scrub: ""}
	end 'create'

	function replace() returns T
		self.current = self.spare
		self.scrub = "zzzzz {40 + 2} value long enough for a heap record"
		return self.spare
	end 'replace'

	function run() returns bool
		return self.current == replace()
	end 'run'
end 'Slot'

typealias StrSlot = Slot with String

function main() returns ExitCode
	var s = StrSlot.create("alpha {40 + 2} value long enough for a heap record", second: "alpha {40 + 2} value long enough for a heap record")
	print("{s.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
```

<!-- test: a-bare-field-read-binary-operand-survives-a-later-call-releasing-its-owner -->
### … and the bare spelling of the field is the same read
`current` with no receiver written is `self.current`; it is held across `replace()` exactly as the explicit spelling is.
```maxon
type Slot uses T where T is Equatable
	var current as T
	var spare as T
	var scrub as String

	static function create(first T, second T) returns Self
		return Self{current: first, spare: second, scrub: ""}
	end 'create'

	function replace() returns T
		self.current = self.spare
		self.scrub = "zzzzz {40 + 2} value long enough for a heap record"
		return self.spare
	end 'replace'

	function run() returns bool
		return current == replace()
	end 'run'
end 'Slot'

typealias StrSlot = Slot with String

function main() returns ExitCode
	var s = StrSlot.create("alpha {40 + 2} value long enough for a heap record", second: "alpha {40 + 2} value long enough for a heap record")
	print("{s.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
```

<!-- test: a-field-read-call-operand-survives-a-later-argument-releasing-its-owner -->
### A field read passed as an argument survives the call in the next argument
`glue`'s first argument is read from `current`; evaluating its second argument runs `replace()`, which frees that record.
```maxon
type Slot uses T
	var current as T
	var spare as T
	var scrub as String

	static function create(first T, second T) returns Self
		return Self{current: first, spare: second, scrub: ""}
	end 'create'

	function glue(kept T, other T) returns T
		self.spare = other
		return kept
	end 'glue'

	function replace() returns T
		self.current = self.spare
		self.scrub = "zzzzz {40 + 2} value long enough for a heap record"
		return self.spare
	end 'replace'

	function run() returns T
		return glue(self.current, other: replace())
	end 'run'
end 'Slot'

typealias StrSlot = Slot with String

function main() returns ExitCode
	var s = StrSlot.create("alpha {40 + 2} value long enough for a heap record", second: "beta {40 + 2} value long enough for a heap record")
	print("{s.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
alpha 42 value long enough for a heap record
```

<!-- test: a-bare-field-read-in-a-method-with-no-other-opaque-signal-is-counted -->
### A bare field read bound to a name, and a bare field read passed on, are counted
Neither `look` nor `lookSelf` stores, reassigns or moves an element, so the field read is the only reason either owes a layout
descriptor.
```maxon
type Slot uses T where T is Equatable
	var current as T
	var seen as Count

	static function create(first T) returns Self
		return Self{current: first, seen: 0}
	end 'create'

	function observe(item T) returns Count
		if item == item 'same'
			self.seen = self.seen + 1
		end 'same'

		return self.seen
	end 'observe'

	function look() returns Count
		let held = current
		return observe(held)
	end 'look'

	function pass() returns Count
		return observe(current)
	end 'pass'

	function lookSelf() returns Count
		let held = self.current
		return observe(held)
	end 'lookSelf'
end 'Slot'

typealias StrSlot = Slot with String

function main() returns ExitCode
	var s = StrSlot.create("alpha {40 + 2} value long enough for a heap record")
	print("{s.look()} {s.pass()} {s.lookSelf()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2 3
```

<!-- test: an-opaque-for-element-in-a-method-with-no-other-opaque-signal-is-counted -->
### A `for` element survives a call that wipes its source, in a method that needs no descriptor for anything else
`walk` stores nothing and moves nothing out, so the loop element is the only reason it owes a layout descriptor. `wipe` clears the
array and allocates a record of the size it freed; the element holds its own reference, and the clear ends the loop.
```maxon
type Bag uses T where T is Equatable
	export typealias Items = Array with T
	var items as Items
	var scrub as String
	var seen as Count

	static function create() returns Self
		return Self{items: Items.create(), scrub: "", seen: 0}
	end 'create'

	function add(item T)
		items.push(item)
	end 'add'

	function wipe()
		self.items.clear()
		self.scrub = "zzzzz {40 + 2} value long enough for a heap record"
	end 'wipe'

	function observe(item T) returns Count
		if item == item 'same'
			self.seen = self.seen + 1
		end 'same'

		return self.seen
	end 'observe'

	function walk() returns Count
		var total = 0 as Count

		for item in self.items 'scan'
			wipe()
			total = total + observe(item)
		end 'scan'

		return total
	end 'walk'
end 'Bag'

typealias StrBag = Bag with String

function main() returns ExitCode
	var b = StrBag.create()
	b.add("alpha {40 + 2} value long enough for a heap record")
	b.add("beta {40 + 2} value long enough for a heap record")
	print("{b.walk()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: an-opaque-for-element-binary-operand-survives-a-call-releasing-its-source -->
### A `for` element compared against a call that wipes its source
The element is the left operand of `==`; the right operand is `wipeAndGive(extra)`, which clears the array before it returns.
```maxon
type Bag uses T where T is Equatable
	export typealias Items = Array with T
	var items as Items
	var scrub as String

	static function create() returns Self
		return Self{items: Items.create(), scrub: ""}
	end 'create'

	function add(item T)
		items.push(item)
	end 'add'

	function wipeAndGive(extra T) returns T
		self.items.clear()
		self.scrub = "zzzzz {40 + 2} value long enough for a heap record"
		return extra
	end 'wipeAndGive'

	function matches(extra T) returns bool
		for item in self.items 'scan'
			return item == wipeAndGive(extra)
		end 'scan'

		return false
	end 'matches'
end 'Bag'

typealias StrBag = Bag with String

function main() returns ExitCode
	var b = StrBag.create()
	b.add("alpha {40 + 2} value long enough for a heap record")
	print("{b.matches("alpha {40 + 2} value long enough for a heap record")}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
```

<!-- test: error.an-opaque-value-has-no-interpolation-door -->
An opaque value has no conversion to text, so it is never an interpolation operand: there is no read at that door to count.
```maxon
type Slot uses T
	var current as T

	static function create(first T) returns Self
		return Self{current: first}
	end 'create'

	function describe() returns String
		return "{self.current}"
	end 'describe'
end 'Slot'

typealias StrSlot = Slot with String

function main() returns ExitCode
	let s = StrSlot.create("alpha {40 + 2} value long enough for a heap record")
	print("{s.describe()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:12: Unsupported: interpolating a value of type 'type parameter' — no conversion to text exists for it
```

<!-- test: error.a-let-read-of-an-element-record-is-still-not-written -->
The counted element read of the cases above does not make the element writable: `first` is a `let` naming the
record the container holds in slot 0, so a write through it is refused, as it is for any other `let`.

```maxon
typealias Count = int(0 to 1000)
typealias Row = Array with Count
typealias Rows = Array with Row

function main() returns ExitCode
	var rows = Rows.create()
	rows.push(Row.create())
	let first = try rows.get(0) otherwise Row.create()
	first.push(1)
	return rows.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:10:8: cannot write through 'first': 'first' is read-only (a `let` binding) and nothing reachable through it may be written; use 'var', or write a clone()
```
