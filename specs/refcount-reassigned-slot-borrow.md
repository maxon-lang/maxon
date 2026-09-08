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

The refcount inserter now pairs read ↔ reassignment by the STABLE base identity (the
pointer param value, the `loadSlot` slot id, or the global symbol) and, when the
borrow is live past a reassignment of that base, reclassifies it `incomingOwner` and
grants the case-(c) acquire (its own `+1` at the load, released at last use). The
reassignment's decref-old then releases the borrow's peer reference, never the live
read. This mirrors the C# oracle, whose by-ref bindings incref and whose global loads
incref into owned temps. A borrow whose base is NEVER reassigned (a plain read, a
static global getter forward) matches nothing and keeps the pre-existing borrow
contract.

Each test exits with the borrowed field's value. Without the acquire, the displaced
object is freed under the borrow: on a plain build the freed slot may still coincidentally
hold the correct bytes, but under `--rc-sanitize` the read returns the poison byte
`0x11` (17) — so these tests are guarded by the sanitized suite, and by the always-on
leak gate against the reciprocal over-retain.

## Tests

<!-- test: byref-param-reassign-then-read-borrow -->
Reading a by-ref managed param, reassigning it, then using the pre-read borrow.
```maxon
typealias Int = int(0 to 1000000)

type Node
	export var tag as Int

	static function create(t Int) returns Self
		return Self{tag: t}
	end 'create'
end 'Node'

function swap(n Node) returns Int
	let old = n
	n = Node.create(99)
	return old.tag
end 'swap'

function main() returns ExitCode
	var owned = Node.create(5)
	let r = swap(owned)
	return r
end 'main'
```
```exitcode
5
```

<!-- test: module-global-reassign-then-read-borrow -->
Reading a module global, reassigning it, then using the pre-read borrow.
```maxon
typealias Int = int(0 to 1000000)

type Box
	export var n as Int

	static function create(v Int) returns Self
		return Self{n: v}
	end 'create'
end 'Box'

var gbox = Box.create(1)

function main() returns ExitCode
	let old = gbox
	gbox = Box.create(2)
	return old.n
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
	var o = Outer.create(Inner.create(6))
	let old = o.inner
	o.inner = Inner.create(9)
	return old.tag
end 'main'
```
```exitcode
6
```

<!-- test: borrow-dead-before-reassign-no-overacquire -->
A borrow whose last use PRECEDES the reassignment must not be over-acquired (leak gate).
```maxon
typealias Int = int(0 to 1000000)

type Node
	export var tag as Int

	static function create(t Int) returns Self
		return Self{tag: t}
	end 'create'
end 'Node'

function swap(n Node) returns Int
	let old = n
	let t = old.tag
	n = Node.create(99)
	return t
end 'swap'

function main() returns ExitCode
	var owned = Node.create(4)
	return swap(owned)
end 'main'
```
```exitcode
4
```

<!-- test: unrelated-global-reassign-keeps-borrow -->
Loading one global read-only while a DIFFERENT global is reassigned stays balanced.
```maxon
typealias Int = int(0 to 1000000)

type Box
	export var n as Int

	static function create(v Int) returns Self
		return Self{n: v}
	end 'create'
end 'Box'

var ga = Box.create(3)
var gb = Box.create(9)

function main() returns ExitCode
	let a = ga
	gb = Box.create(2)
	return a.n
end 'main'
```
```exitcode
3
```

<!-- test: loop-resident-global-reassign-borrow -->
A loop that reassigns a global with a live borrow each iteration stays balanced.
```maxon
typealias Int = int(0 to 1000000)

type Box
	export var n as Int

	static function create(v Int) returns Self
		return Self{n: v}
	end 'create'
end 'Box'

var gbox = Box.create(0)

function main() returns ExitCode
	var sum = 0
	var i = 0
	while i < 3 'loop'
		let old = gbox
		gbox = Box.create(i + 1)
		sum = sum + old.n
		i = i + 1
	end 'loop'
	return sum
end 'main'
```
```exitcode
3
```
