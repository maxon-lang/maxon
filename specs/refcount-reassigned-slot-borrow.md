---
feature: refcount-reassigned-slot-borrow
keywords: [refcount, borrow, byref, param, global, reassign, overwrite, uaf, memory, leak]
category: memory-safety
---

# Refcount: Borrow of a Reassigned Slot Must Acquire Its Own +1

## Documentation

A managed value read as a *borrow* from a slot that can be REASSIGNED — a
by-reference managed parameter (`*P`, mem2reg-promoted to a `load_indirect` off the
pointer param), or a module GLOBAL (`[global_addr @__data_*]`) — dangles when the
reassignment's decref-old releases the displaced occupant while the borrow is still
live:

```text
let old = n          // by-ref deref  (or: let old = gbox)
n = Node.create(99)  // reassign: decref-olds the caller's SOLE-OWNED occupant
return old.tag       // old dangles -> reads freed memory (audit gaps #9, #7b)
```

The self-hosted lowering classifies both reads `borrowed` (no acquire). The
`incomingOwner` case-(c) field-snapshot rule already handles the plain-field twin
(`let old = self.f; self.f = new; use(old)`) by pairing the read with its overwrite by
exact `(addrId, offset)` SSA identity and granting the reader its own `+1`. But these
two shapes escape it: the by-ref deref is `borrowed` (case (c) only considers
`incomingOwner`), and a module global mints a FRESH `global_addr` SSA per access so the
read's and the write's addresses differ.

The refcount inserter pairs read ↔ reassignment by the STABLE base identity (the
pointer param value, the `loadSlot` slot id, or the global symbol) and, when the
borrow is live past a reassignment of that base, reclassifies it `incomingOwner` and
grants the case-(c) acquire (its own `+1` at the load, released at last use). The
reassignment's decref-old then releases the borrow's peer reference, never the live
read. A borrow whose base is NEVER reassigned (a plain read, a static global getter
forward) matches nothing and keeps the plain borrow contract.

Each test exits with the borrowed field's value. Without the acquire, the displaced
object is freed under the borrow: on a plain build the freed slot may still coincidentally
hold the correct bytes, but under `--rc-sanitize` the read returns the poison byte
`0x11` (17) — so these tests are guarded by the sanitized suite, and by the always-on
leak gate against the reciprocal over-retain.

## Tests

<!-- test: by-reference-reassign-then-read-borrow -->
Reading a by-reference parameter, reassigning it, then using the pre-read borrow.
```maxon
function swapIn(label mutable String) returns ExitCode
	let old = label
	label = "second label, long enough to live on the heap {40 + 2}"
	return 1 if old.startsWith("first") else 0
end 'swapIn'

function main() returns ExitCode
	var label = "first label, long enough to live on the heap {40 + 2}"
	return swapIn(label)
end 'main'
```
```exitcode
1
```

<!-- test: field-reassign-then-read-borrow-regression -->
The plain-field twin the exact-SSA case-(c) already covers stays correct.
```maxon
typealias Int = int(0 to 1000000)

type Inner
	export var tag as Int

	static function create(t Int) returns Self
		return Self{tag: t}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static function create(i Inner) returns Self
		return Self{inner: i}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let o = Outer.create(Inner.create(6))
	let old = o.inner
	o.inner = Inner.create(9)
	return old.tag
end 'main'
```
```exitcode
6
```

<!-- test: unrelated-by-reference-reassign-keeps-borrow -->
Loading one by-reference parameter read-only while a DIFFERENT one is reassigned stays balanced.
```maxon
function swapOther(la String, lb mutable String) returns ExitCode
	let a = la
	lb = "replacement label, long enough to live on the heap {40 + 2}"
	return 3 if a.startsWith("third") else 0
end 'swapOther'

function main() returns ExitCode
	let la = "third label, long enough to live on the heap {40 + 2}"
	var lb = "ninth label, long enough to live on the heap {40 + 2}"
	return swapOther(la, lb: lb)
end 'main'
```
```exitcode
3
```

<!-- test: loop-resident-by-reference-reassign-borrow -->
A loop that reassigns a by-reference parameter with a live borrow each iteration stays balanced.
```maxon
typealias Int = int(0 to 1000000)

function churn(label mutable String) returns Int
	var sum = 0 as Int
	var i = 0 as Int
	while i < 3 'loop'
		let old = label
		label = "label number {i + 1}, long enough to live on the heap"
		if old.contains("number {i}") 'seen'
			sum = sum + 1
		end 'seen'
		i = i + 1
	end 'loop'
	return sum
end 'churn'

function main() returns ExitCode
	var label = "label number 0, long enough to live on the heap"
	return churn(label)
end 'main'
```
```exitcode
3
```
