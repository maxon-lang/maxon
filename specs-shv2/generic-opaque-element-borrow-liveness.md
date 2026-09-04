---
feature: generic-opaque-element-borrow-liveness
status: selfhosted
keywords: [borrow, E3070, generic, type parameter, opaque, element, use-after-free, liveness]
category: memory
---
# A borrow of an OPAQUE element is a borrow (E3070 in a shared generic body)

## Documentation

`specs-shv2/borrow-liveness.md` pins E3070 over a CONCRETE managed element. This file pins the
same rule over the element a shared generic body cannot name — a `typeParameter` — and it exists
because that half was missing and the gap was a **use-after-free the whole suite was green over**.

`Parser.emitContainerElementAccessor` minted its E3070 borrow only when
`containerElementIsManaged(giid)` answered true. That predicate is a LOSSY PROJECTION on a shared
generic body: `false` there means *"no static answer"*, not *"owns nothing"* — the element of
`Array with String` owns a heap record whatever the body can see. Read as "nothing to borrow", it
left every write through an opaque container invisible to the one rule that guards this hazard.

Reachable, and MEASURED `0xC0000005` on the tree that shipped it:

```maxon
let a = try managed.get(0) otherwise panic("oob")   // borrows slot 0's record
let b = try managed.get(1) otherwise panic("oob")
try managed.set(0, value: b) otherwise panic("oob") // FREES slot 0's occupant — what `a` holds
try managed.set(1, value: a) otherwise panic("oob") // … and stores it
```

`stdlib/helpers/sort/smallSort.maxon:24-32` names this hazard verbatim and routes `swap` through
the raw `managed.swap` builtin to avoid it — *"building it from get+set instead would decref the
displaced occupant while the borrowed copy of it is still waiting to be stored into the other
slot"*. The refusal below is that comment enforced.

**Why a REFUSAL and not a retain at the read.** The reference an opaque value takes is
`retainFunc@64`, and for a byte record that word is `__str_clone` — a DEEP CLONE, deliberately,
because a String literal's record is immortal `.rdata` and increfing one writes read-only memory
(`SignatureIndex.retainCalleeForProtocol`). Retaining at every element read would therefore copy
the record on every `get`, and hand the reader a DIFFERENT record than the slot holds. The USER
RULING at `emitContainerElementAccessor`'s borrow arm already settled the same question one door
over: *"it is the BASE that is promoted, not the element that is retained"*.

**Why the trivial instantiation is refused too.** A shared generic body is compiled ONCE for every
instantiation, so the refusal cannot be conditioned on the element the caller chose. That is the
same conservatism `referenceBorrowedOpaqueElement` already applies to a concrete-typed value in a
shared body, and it is the sound direction: the alternative is a body that is a use-after-free for
one instantiation and silent about it.

## Tests

<!-- test: error.a-self-overwriting-swap-through-get-and-set -->
### A swap built from get + set frees the element it is mid-way through moving
The sort-of-managed-elements UAF, in the smallest program that reaches it. Both borrows are of the
same storage, and the first `set` destroys the record the OTHER one holds. Measured
**`0xC0000005`** before this rule, with the suite green over it.
```maxon
type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	export function rotateFirstTwo()
		let a = try managed.get(0) otherwise panic("oob")
		let b = try managed.get(1) otherwise panic("oob")
		try managed.set(0, value: b) otherwise panic("oob")
		try managed.set(1, value: a) otherwise panic("oob")
	end 'rotateFirstTwo'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha value long enough for a heap record")
	a.push("beta value long enough for a heap record")
	a.rotateFirstTwo()
	print("{try a.get(0) otherwise "?"}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/generic-opaque-element-borrow-liveness/error.a-self-overwriting-swap-through-get-and-set.test:17:15: cannot mutate 'managed' via 'set' while it is borrowed by 'a' (borrowed at line 15)
```

<!-- test: error.a-three-way-rotate-through-get-and-set -->
### … and a three-way rotate is the same write, one slot further
Two diagnostics, and the pair is the rule reading precisely rather than shouting: the first write
is blamed for `a` (still to be stored, one line down) and the second for `b`. The THIRD write
draws none — by then every borrow's last use is behind it, and a borrow's use as the write's own
argument does not outlive the write (`Parser.containerWriteToken`).
```maxon
type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	export function rotateThree()
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
	a.push("alpha value long enough for a heap record")
	a.push("beta value long enough for a heap record")
	a.push("gamma value long enough for a heap record")
	a.rotateThree()
	print("{try a.get(0) otherwise "?"}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/generic-opaque-element-borrow-liveness/error.a-three-way-rotate-through-get-and-set.test:18:15: cannot mutate 'managed' via 'set' while it is borrowed by 'a' (borrowed at line 15)
error E3070: specs/fragments/generic-opaque-element-borrow-liveness/error.a-three-way-rotate-through-get-and-set.test:19:15: cannot mutate 'managed' via 'set' while it is borrowed by 'b' (borrowed at line 16)
```

<!-- test: error.a-borrow-displaced-by-a-set-is-refused-even-when-it-is-never-stored -->
### A borrow the write DISPLACES is a conflict even when nothing stores it back
The value written here comes from a DIFFERENT container, so the store itself is sound — the
conflict is the borrow the write destroys on its way past. Measured **`0xC0000005`**: the freed
record was read again by the `return`'s own `retainFunc@64`.
```maxon
type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	export function readThenDisplace(other Self) returns Element
		let a = try managed.get(0) otherwise panic("oob")
		let v = try other.managed.get(0) otherwise panic("oob")
		try managed.set(0, value: v) otherwise panic("oob")
		return a
	end 'readThenDisplace'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha value long enough for a heap record")
	var b = StrArray.create()
	b.push("beta value long enough for a heap record")
	print("{a.readThenDisplace(b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/generic-opaque-element-borrow-liveness/error.a-borrow-displaced-by-a-set-is-refused-even-when-it-is-never-stored.test:17:15: cannot mutate 'managed' via 'set' while it is borrowed by 'a' (borrowed at line 15)
```

<!-- test: error.the-bare-spelling-is-the-same-write -->
### … and the BARE spelling is the same write, which is the spelling `stdlib/Array.maxon` uses
`get(0)` and `set(0, …)` with no receiver written at all. The refusal above reached only the
`managed.`-prefixed spelling, so this program **SEGFAULTED while its own prefixed twin was
refused** — one storage under two keys, which is the shape the fix collapses
(`Parser.receiverBorrowSubjectName`). An unnamed receiver that IS the enclosing `self` keys on
`self`, and the blame reads as the source would spell it, never as the `__self` the receiver
parameter is bound under.
```maxon
type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	export function rotateBare()
		let a = try get(0) otherwise panic("oob")
		let b = try get(1) otherwise panic("oob")
		try set(0, value: b) otherwise panic("oob")
		try set(1, value: a) otherwise panic("oob")
	end 'rotateBare'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha value long enough for a heap record")
	a.push("beta value long enough for a heap record")
	a.rotateBare()
	print("{try a.get(0) otherwise "?"}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/generic-opaque-element-borrow-liveness/error.the-bare-spelling-is-the-same-write.test:17:7: cannot mutate 'self' via 'set' while it is borrowed by 'a' (borrowed at line 15)
```

<!-- test: error.a-trivial-instantiation-of-a-refused-body-is-refused-with-it -->
### The cost, pinned: a shared body is refused for EVERY instantiation
`Array with <ranged int>` cannot dangle — the element is a bare word — and this program runs
correctly. It is refused anyway, because the body it is refused in is compiled ONCE for every
instantiation and the `Array with String` instantiation of that same body is a use-after-free.
The runnable oracle monomorphizes and accepts this; shv2 cannot, and the refusing direction is the
only sound one. **This case exists so the cost is a decision on the record rather than a surprise.**
```maxon
typealias Small = int(0 to 100)

type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	export function rotateFirstTwo()
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
	print("{try a.get(0) otherwise 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/generic-opaque-element-borrow-liveness/error.a-trivial-instantiation-of-a-refused-body-is-refused-with-it.test:19:15: cannot mutate 'managed' via 'set' while it is borrowed by 'a' (borrowed at line 17)
```

<!-- test: a-set-fed-from-another-container-is-not-a-conflict -->
### The over-rejection guard: a write of ONE storage fed from ANOTHER
The borrow is of `other`, the write is of `managed`, and E3070's subject is the storage — so there
is nothing to conflict with. This is the shape `stdlib/Array.maxon:282`'s `appendMemory` is, and
the rule must not reach it.
```maxon
type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	export function setFrom(other Self)
		let v = try other.managed.get(0) otherwise panic("oob")
		try managed.set(0, value: v) otherwise panic("oob")
	end 'setFrom'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha value long enough for a heap record")
	var b = StrArray.create()
	b.push("beta value long enough for a heap record")
	a.setFrom(b)
	print("{try a.get(0) otherwise "?"}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
beta value long enough for a heap record
```

<!-- test: an-opaque-element-borrow-that-dies-before-the-write-is-not-a-conflict -->
### … and NLL reaches the opaque element too — a borrow read and dropped before the write
The element is read, used to write a DIFFERENT storage, and never named again — so by the time
`managed` itself is written the borrow is dead. The whole point of joining E3070 rather than
inventing a second rule is that its non-lexical lifetimes come with it.
```maxon
type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	export function moveFirstInto(other Self)
		let a = try managed.get(0) otherwise panic("oob")
		try other.managed.set(0, value: a) otherwise panic("oob")
		try managed.setLength(0) otherwise panic("length")
	end 'moveFirstInto'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha value long enough for a heap record")
	var b = StrArray.create()
	b.push("beta value long enough for a heap record")
	a.moveFirstInto(b)
	print("{a.count()} {try b.get(0) otherwise "?"}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 alpha value long enough for a heap record
```

<!-- test: an-element-moved-down-to-a-lower-slot-is-not-a-conflict -->
### The over-rejection guard that SORTING rests on — `set(w, value: get(i))` through a name
`stdlib/helpers/sort/driftQuicksort.maxon:59-61`, `:103-104` and every `mergeSort.maxon` merge
step are this loop: read the element at `i`, write it down to `w`. It is SOUND — the store takes
its reference through `retainFunc@64` BEFORE the callee destroys the slot it is displacing, so
even `w == i` survives — and it stays legal because a container write's site is the token its
write HAPPENS at, past its own arguments (`Parser.containerWriteToken`). Recorded at the method
name instead, this whole family was refused.
```maxon
typealias Small = int(0 to 100)

type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	export function compactDown(hi Small)
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
	a.push("alpha value long enough for a heap record")
	a.push("beta value long enough for a heap record")
	a.compactDown(2)
	print("{try a.get(0) otherwise "?"} {try a.get(1) otherwise "?"}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
alpha value long enough for a heap record beta value long enough for a heap record
```

<!-- test: a-push-of-the-containers-own-element-is-not-a-conflict -->
### … and so is appending an element the container already holds
`push` adds a slot and destroys nothing, and the element it is handed has its reference taken
before the call — so the borrow is dead at the write and the array simply gains a second
reference to what it already owned. `stdlib/Array.maxon:283`'s `appendMemory` is this write.
```maxon
type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	export function copyFirst()
		let a = try managed.get(0) otherwise panic("oob")
		push(a)
	end 'copyFirst'
end 'Array'

typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha value long enough for a heap record")
	a.copyFirst()
	print("{a.count()} {try a.get(1) otherwise "?"}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 alpha value long enough for a heap record
```

<!-- test: error.a-write-nested-inside-another-writes-arguments -->
### A container write NESTED in another's arguments happens FIRST, and the order is restored
Because a write's site is the closing `)` of its own argument list, the OUTER write is recorded
first and happens last — so the two sites arrive in decreasing token order, which the resolver's
linear walk forbids and panics on. `Parser.pushMutationSiteInTokenOrder` restores the order at the
one push. MEASURED with that insert removed: **`panic at BorrowCheck.maxon:303 … site 1 is at
token 55, below its predecessor's 58`** — a compiler crash on a legal program.
```maxon
function main() returns ExitCode
	var xs = ["alpha value long enough for a heap record", "beta value long enough for a heap record"]
	var ys = ["gamma value long enough for a heap record"]
	let borrowed = try xs.get(0) otherwise panic("oob")
	try xs.set(1, value: try ys.pop() otherwise "z") otherwise panic("oob")
	print("{borrowed}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/generic-opaque-element-borrow-liveness/error.a-write-nested-inside-another-writes-arguments.test:6:9: cannot mutate 'xs' via 'set' while it is borrowed by 'borrowed' (borrowed at line 5)
```

<!-- test: a-trivial-element-container-is-untouched -->
### The trivial-element control
An `Array with <ranged int>` element owns no heap, so an element read is a bare word that no write
can dangle — and `emitContainerElementAccessor`'s trivial arm returns before the borrow is minted
at all. Nothing about this program changed.
```maxon
typealias Small = int(0 to 100)

type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
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
