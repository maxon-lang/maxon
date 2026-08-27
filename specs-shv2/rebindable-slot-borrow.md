---
feature: rebindable-slot-borrow
status: stable
keywords: [ownership, borrow, field, global, rebind, retain, use-after-free, slot]
category: memory-management
---

# A Name Bound To A Value Read Out Of A REBINDABLE SLOT Takes Its Own Reference

## Documentation

shv2 leaves an immutable binding of a BORROWED managed value borrowed — no retain, no scope-exit
drop — and the reason is stated at `Parser.declareInitializedBinding`'s `aliasImmutableSource` arm:
*"the owner always outlives every alias of it, structurally"*. A binding can only be declared after
the name it reads, in that name's scope or one nested inside it, so the owner outlives the alias
with no liveness analysis needed.

**That argument reaches a PARAMETER and an immortal `.rdata` literal. It does not reach a writable
storage SLOT**, and the difference is what this file pins. A `var` field and a `var` top-level
global are storage this function can overwrite:

```maxon
let old = items      // a borrow of whatever the field holds
items = fresh        // ...which this store DROPS
sum(old)             // ...and this reads
```

The STORAGE outlives the alias. The VALUE in it does not. `emitFieldWrite` and
`emitCheckedGlobalStore` each drop the old occupant on the way past, so the name is left pointing at
freed memory.

⇒ **A managed value read out of a rebindable slot is marked at the load** (`emitFieldLoad`,
`recordGlobalReadValue`'s writable arm — see `Parser.rebindableSlotReads`) **and PROMOTED when it is
bound to a name**, through the one `promoteBorrowedToOwned` door a `var` of the same read already
took: a String copies, an aggregate increfs, an existential goes through its witness's
`retainFunc@16`. The refcount balances on every path — the store's drop releases the slot's
reference and the binding's scope exit releases its own.

⚠ **The `var` spelling of every program below was ALREADY correct**, which is what pins the cure
rather than inventing one: the mutable arm promotes and the identical program runs. What was missing
was the answer to *which IMMUTABLE bindings owe the same promotion* — and the `mutable` gate alone
answered the wrong half of it. The hazard is not this BINDING being rebound; it is the STORAGE the
value came out of being rebound underneath it.

⚠ **An IMMUTABLE field is deliberately not marked.** `let` storage refuses the store (E2013), so no
write can replace its occupant and the structural argument holds for the original reason. The mark
is about REBINDABILITY, not about being a slot.

Every exit code and every printed line below was measured against the C# bootstrap first, and every
one of them REPRODUCED as a defect before the fix: the array case exited **0xC0000005**, the global
case exited **0xC0000005**, and the String case was worse than either — it silently printed
`len 4557430888798830399` where the oracle prints `len 11`.

## Tests

<!-- test: managed-field-read-survives-the-field-being-replaced -->
⭐ **THE ORIGINAL REPRODUCER.** `let old = items` binds a borrow of the array the field holds;
`items = fresh` then `__managed_decref`s that very record; the walk below reads it. Exit 0 with no leak
is the whole claim — the sum proves the elements were still there, and the exit code proves the
refcount balanced (a leak is exit 101).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Holder
	var items as IntArray

	export static function create() returns Self
		return Self{items: IntArray.create()}
	end 'create'

	export function fill(n Integer)
		for i in 0 upto n 'each'
			items.push(i)
		end 'each'
	end 'fill'

	export function swapAndRead() returns Integer
		let old = items
		var fresh = IntArray.create()
		fresh.push(99)
		items = fresh

		var sum = 0
		for i in 0 upto old.count() 'walk'
			sum = sum + (try old.get(i) otherwise 0)
		end 'walk'
		return sum
	end 'swapAndRead'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	h.fill(5)
	return h.swapAndRead() as ExitCode
end 'main'
```
```exitcode
10
```

<!-- test: the-var-spelling-was-always-correct -->
**THE CONTROL, and it is what makes the fix a widening rather than an invention.** One keyword
apart from the case above. This spelling already promoted (`mutable and valueIsManagedHeap and not
valueIsOwnedHeap`) and already ran; a cure that changed its answer would have been fixing the wrong
thing.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Holder
	var items as IntArray

	export static function create() returns Self
		return Self{items: IntArray.create()}
	end 'create'

	export function swapAndRead() returns Integer
		items.push(3)
		items.push(4)
		var old = items
		items = IntArray.create()

		var sum = 0
		for i in 0 upto old.count() 'walk'
			sum = sum + (try old.get(i) otherwise 0)
		end 'walk'
		return sum
	end 'swapAndRead'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	return h.swapAndRead() as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: string-field-read-survives-the-field-being-replaced -->
⭐⭐ **THE SPELLING THAT DID NOT CRASH, WHICH IS WHY IT IS HERE.** A `String` field is the same
defect with a quieter symptom: the store's `__str_decref` frees the record and the read comes back
with whatever the allocator left behind. MEASURED at `HEAD~` before the fix: `len
4557430888798830399`. A garbage length is a WRONG ANSWER a green suite would never have noticed,
where the array case at least announced itself with an access violation.

The promotion here is the `binding` door (`let old = name`), and that door still COPIES:
`promoteBorrowedToOwned` routes it to `promoteToOwnedString`, the `__mm_alloc` + `__str_copy`
pair this case's golden shows. It is no longer every door's protocol, and the reason it used to
be is gone. Since `ca5169e231` a HAND-OFF (`return` / `gives`, or a merge edge) and a DURABLE
store into a field, element or column both take a REFERENCE instead
(`retainBorrowedByteRecord` → `__str_retain`), because Maxon is single-ownership with reference
semantics and a copy the author did not write makes the caller's value stop being the callee's.
Nor does the immortal-record argument force a copy any more: `__str_retain` tells an immortal
`.rdata` record from a heap one at RUN TIME, off `capacity@16`, and clones only the former, so
no door needs an unconditional copy to stay off read-only memory.

⚠ **THE `binding` DOOR IS NOT THE ONLY ONE STILL COPYING, AND A LIST OF WHAT RETAINS IS NOT A
LIST OF EVERY DOOR.** A CONSUMED ARGUMENT — a borrowed `String` handed to a callee that STORES
it — takes `transferConsumedArg`'s byte-record arm, which is `promoteToOwnedString` as well.
MEASURED: `relay(s String) returns Rec` whose whole body is `return Rec.create(s)` emits
`__mm_alloc` + `__str_copy` on `s` BEFORE the call, and `let r = relay(v)` followed by
`v.append("XY")` prints `v=abXY name=ab` — where the direct store of the same shape,
`dst.name = src.name`, emits `__str_retain` and the two names share one record. So the answer a
borrowed `String` gets at a durable sink still depends on whether it crossed a call boundary to
reach it. What the `binding` door
should do is a separate, still-open question about a REFUSAL rather than about a cost
(`ownedFormOfBorrowedValue` states it), and this case is indifferent to the answer: a retain
would keep the old record alive across the rebind exactly as the copy does — what must never
happen is the read seeing a record the store already released.
```maxon
typealias Len = int(0 to u64.max)

type Holder
	var name as String

	export static function create(n String) returns Self
		return Self{name: n}
	end 'create'

	export function swapAndRead(fresh String) returns Len
		let old = name
		name = fresh
		return old.byteLength()
	end 'swapAndRead'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create("hello world")
	let n = h.swapAndRead("hi")
	print("len {n}")
	return 0
end 'main'
```
```stdout
len 11
```

<!-- test: mutable-global-read-survives-the-global-being-replaced -->
**THE SECOND SLOT KIND.** A top-level `var` is rebindable storage exactly as a field is, and
`emitCheckedGlobalStore` decrefs the old occupant on the way past — so the immutable arm of
`recordGlobalReadValue` marking and the writable arm not marking was the whole of the gap. Before
the fix this exited **0xC0000005**; the oracle prints `sum 3`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

var G = IntArray.create()

function main() returns ExitCode
	G.push(1)
	G.push(2)
	let old = G
	var fresh = IntArray.create()
	fresh.push(99)
	G = fresh

	var sum = 0
	for i in 0 upto old.count() 'walk'
		sum = sum + (try old.get(i) otherwise 0)
	end 'walk'
	print("sum {sum}")
	return 0
end 'main'
```
```stdout
sum 3
```

<!-- test: an-immutable-field-is-not-promoted -->
**THE OTHER HALF OF THE DISCRIMINATOR.** A `let` field cannot be stored to, so its occupant cannot
be replaced and the borrow's owner outlives it for the original structural reason. The binding stays
a borrow and the program still answers — a promotion here would be refcount traffic bought for
nothing.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Frozen
	let items as IntArray

	export static function create() returns Self
		var a = IntArray.create()
		a.push(4)
		a.push(5)
		return Self{items: a}
	end 'create'

	export function total() returns Integer
		let held = items

		var sum = 0
		for i in 0 upto held.count() 'walk'
			sum = sum + (try held.get(i) otherwise 0)
		end 'walk'
		return sum
	end 'total'
end 'Frozen'

function main() returns ExitCode
	var f = Frozen.create()
	return f.total() as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: the-promoted-name-keeps-its-declared-surface -->
⭐ **THE REGRESSION THE PROMOTION WOKE, PINNED WHERE THE PROMOTION IS.** `__ManagedMemory` and the
`Array` around it are one record told apart by PROVENANCE, and that provenance is keyed by ValueId
— so a promotion, which mints a FRESH id, must carry it or `m.length()` becomes
`E2015: Array member 'length'`. `let (m, n) = pair()` binds `m` to a field load off the hidden tuple
temp, which is a rebindable-slot read, so this is the first program to take the promotion with a
surface to lose.

⛔ The carry belongs to `promoteBorrowedToOwned` and NOT to `retainBorrowedAggregate` one level
down: the ADOPTION door (`array-init.md`'s `the-surface-flips`) shares that retain and depends on
the fresh name being UNMARKED. Carried there instead, nine `array-init` cases turn red with the
roster exactly inverted.
```maxon
typealias Int = int(i64.min to i64.max)

function pair() returns (__ManagedMemory, Int)
	return ("hello".toByteArray(), 7)
end 'pair'

function main() returns ExitCode
	let (m, _) = pair()
	return m.length() as ExitCode
end 'main'
```
```exitcode
5
```
