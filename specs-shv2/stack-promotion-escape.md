---
feature: stack-promotion-escape
status: experimental
keywords: [stack, allocation, escape-analysis, optimization, memory-safety]
category: optimization
---

# Stack Promotion — the escape routes, as behaviour

**This spec is shv2's own, and it exists because `stack-promotion.md` cannot see its own subject.**

That file is a byte-identical copy of the canonical `/specs/stack-promotion.md`, and every one of its six
cases pins nothing but an `exitcode`. Those exit codes were **identical before the escape analysis existed**
— all six were green against a compiler that promoted nothing — and they would be green again if the pass
regressed to a no-op. Its Documentation claims the cases use `MmTrace: true` to check that a promoted struct
produces no heap-allocation trace; no case carries that marker and shv2's `SpecParser` does not know it.
So the canonical file pins that promotion is *harmless*, and nothing anywhere pins that it is *correct*.

**A wrong promotion is not a slow program — it is a dangling pointer**, and the compiler has no diagnostic
to fall back on. So the cases below are built the other way round: each one is a program whose **exit code
changes** if a specific escape route stops being detected. Every one of them was MEASURED against a
deliberately sabotaged compiler and observed to fail:

| the rule that is removed | what the sabotaged compiler did |
|---|---|
| the local-escape verdict (a record reaching a phi) | **exit 101** — the drop ran `__mm_free` over stack memory |
| the callee-retains verdict | **a silent wrong answer, 19 where 18 is right** — the callee's `__mm_retain` wrote a refcount into the neighbouring record's payload |
| the spill-slot reservation | **an access violation** — a spilled value was given the slot a live record occupied |

⚠ **Every case pins `exitcode`, never stdout alone.** An unpinned exit code leaves the leak gate disarmed,
and a leak is exactly how a mis-promotion shows up when it does not corrupt anything outright.

The first group must promote and still be right; the second group names one escape route each, and each is a
route the analysis must refuse.

⚠ **ONE ROUTE HAS NO CASE HERE, AND THE REASON IS THE INTERESTING PART: CLOSURE CAPTURE.** The analysis
refuses a record captured by a closure, and that refusal is **currently unobservable** — MEASURED: with the
refusal removed the record is promoted into the closure's environment and every program still answers
correctly. It cannot be otherwise today, because shv2 already refuses a capturing closure that ESCAPES its
frame (`capturingClosureEscapes` — it may not be returned, stored, or passed to anything that keeps it), so
the environment can never outlive the frame the record lives in. The refusal is therefore belt-and-braces
over a rule a different subsystem enforces, and a case pinning it could only pass. It is named here so the
absence is a decision on the record rather than a gap — **if that refusal is ever relaxed, this route
becomes live and needs a case.**

## Tests

### Promoted, and still correct

<!-- test: promoted-record-in-a-loop-reuses-one-slot -->
A promotion site inside a loop writes THE SAME frame slot every iteration, where the heap would hand out
fresh memory each time. That is safe only because a record carried across the back edge is refused, so no
iteration's record is still live when the next one overwrites it — and this is the program that would read a
stale record if that ever stopped being true.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	var total = 0
	for i in 0 upto 6 'each'
		let p = Point.create(i, y: i + 1)
		total = total + p.x + p.y
	end 'each'
	return total
end 'main'
```
```exitcode
36
```

<!-- test: promoted-record-reads-back-every-field -->
A heap record arrives ZEROED from `__mm_alloc`; a frame slot arrives holding whatever the last call left
there. So a promoted record is only safe if the construction writes every byte of it, and this reads all
four fields back with distinct bit values — a slot the constructor failed to cover would show up as one of
them being wrong rather than as a crash.
```maxon
typealias Integer = int(i64.min to i64.max)

type Quad
	export var a as Integer
	export var b as Integer
	export var c as Integer
	export var d as Integer

	static function create(a Integer, b Integer, c Integer, d Integer) returns Self
		return Self{a: a, b: b, c: c, d: d}
	end 'create'
end 'Quad'

function main() returns ExitCode
	let q = Quad.create(1, b: 2, c: 4, d: 8)
	return q.a + q.b + q.c + q.d
end 'main'
```
```exitcode
15
```

<!-- test: promoted-record-survives-register-pressure -->
A promoted record lives in a frame slot, and so does a SPILLED value. They must never be given the same
slot: the promoted region is reserved out of the register allocator's range, so the record takes the low
slots and the first spill starts above them. This function holds enough values live at once to force real
spilling around a live promoted record.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function spread(seed Integer) returns Integer
	let p = Point.create(1000, y: 2000)
	let v0 = seed + 1
	let v1 = seed + 2
	let v2 = seed + 3
	let v3 = seed + 4
	let v4 = seed + 5
	let v5 = seed + 6
	let v6 = seed + 7
	let v7 = seed + 8
	let v8 = seed + 9
	let v9 = seed + 10
	let v10 = seed + 11
	let v11 = seed + 12
	let v12 = seed + 13
	let v13 = seed + 14
	let v14 = seed + 15
	let v15 = seed + 16
	let total = v0 + v1 + v2 + v3 + v4 + v5 + v6 + v7 + v8 + v9 + v10 + v11 + v12 + v13 + v14 + v15
	return total + p.x + p.y
end 'spread'

function main() returns ExitCode
	return spread(0) - 3094
end 'main'
```
```exitcode
42
```

### Not promoted — one escape route each

<!-- test: escape-across-a-branch-merge -->
Two records merged by a branch reach the same binding, so neither may be promoted: the merged value is
dropped once, and the drop cannot know which of the two it holds. Promote either and `__mm_decref` runs over
a frame address — MEASURED as exit 101, the leak gate firing on a corrupted allocation count.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function pick(other bool) returns Integer
	var p = Point.create(11, y: 2)
	if other 'takeTheOther'
		p = Point.create(31, y: 4)
	end 'takeTheOther'
	return p.x
end 'pick'

function main() returns ExitCode
	return pick(false) + pick(true)
end 'main'
```
```exitcode
42
```

<!-- test: escape-into-a-retaining-callee -->
A record handed to a callee that RETAINS it — here by returning it, which makes the caller's reference
outlive the call — may not be promoted. This is the worst failure mode the analysis has: the sabotaged
compiler did not crash, it answered **19 where 18 is right**, because the callee's `__mm_retain` wrote a
refcount into the frame bytes just below the record's address, which is the neighbouring record's payload.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function keep(p Point) returns Point
	return p
end 'keep'

function main() returns ExitCode
	let a = Point.create(3, y: 4)
	let b = Point.create(5, y: 6)
	let ka = keep(a)
	let kb = keep(b)
	return ka.x + ka.y + kb.x + kb.y
end 'main'
```
```exitcode
18
```

<!-- test: escape-into-a-module-level-var -->
A record stored into a module-level `var` outlives every frame, including the one that built it. The store
is the record as a VALUE rather than as an address — the same shape as storing it into a container or into
another record's field.

⚠ **`churn` is what makes this case DECISIVE, and it is not decoration.** Without a call between the store
and the read, a wrongly promoted record still answers correctly: `stash`'s frame is dead but nobody has
written over it yet. `churn` holds enough values live to spill, so it writes exactly the frame bytes the
dead record occupied. MEASURED against the sabotaged compiler: 101 with the call, and a correct 11 without
it — the difference between a test and a test that can fail.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function churn(seed Integer) returns Integer
	let v0 = seed + 1
	let v1 = seed + 2
	let v2 = seed + 3
	let v3 = seed + 4
	let v4 = seed + 5
	let v5 = seed + 6
	let v6 = seed + 7
	let v7 = seed + 8
	let v8 = seed + 9
	let v9 = seed + 10
	let v10 = seed + 11
	let v11 = seed + 12
	let v12 = seed + 13
	let v13 = seed + 14
	let v14 = seed + 15
	let v15 = seed + 16
	return v0 + v1 + v2 + v3 + v4 + v5 + v6 + v7 + v8 + v9 + v10 + v11 + v12 + v13 + v14 + v15
end 'churn'

var kept = Point.create(0, y: 0)

function stash()
	let p = Point.create(11, y: 22)
	kept = p
end 'stash'

function main() returns ExitCode
	stash()
	let noise = churn(1000)
	return kept.x if noise > 0 else 1
end 'main'
```
```exitcode
11
```

<!-- test: escape-into-a-container -->
A container stores the POINTER, so a record pushed into one outlives the frame that built it. The push
reaches a compiler runtime entry, whose body is installed after the analysis has run and which therefore has
no parameter summary at all — the unknown-callee arm, which is the one every runtime door goes through.
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function churn(seed Integer) returns Integer
	let v0 = seed + 1
	let v1 = seed + 2
	let v2 = seed + 3
	let v3 = seed + 4
	let v4 = seed + 5
	let v5 = seed + 6
	let v6 = seed + 7
	let v7 = seed + 8
	let v8 = seed + 9
	let v9 = seed + 10
	let v10 = seed + 11
	let v11 = seed + 12
	let v12 = seed + 13
	let v13 = seed + 14
	let v14 = seed + 15
	let v15 = seed + 16
	return v0 + v1 + v2 + v3 + v4 + v5 + v6 + v7 + v8 + v9 + v10 + v11 + v12 + v13 + v14 + v15
end 'churn'

function fill(arr ItemArray)
	let item = Item.create(42)
	arr.push(item)
end 'fill'

function main() returns ExitCode
	var arr = ItemArray.create()
	fill(arr)
	let noise = churn(1000)
	let got = try arr.get(0) otherwise Item.create(0)
	return got.value if noise > 0 else 1
end 'main'
```
```exitcode
42
```

<!-- test: reference-identity-is-never-promoted -->
`is` compares record ADDRESSES, so it is the one operator that can tell a frame slot from a heap box. A
record reaching an `is` is never promoted, which is what makes the two storage shapes indistinguishable
here: two separate constructions are never identical, and an alias always is, whichever storage they use.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(1, y: 2)
	let b = Point.create(1, y: 2)
	let c = a
	var score = 0
	if a is b 'distinctRecordsAreNotIdentical'
		score = score + 1
	end 'distinctRecordsAreNotIdentical'
	if a is c 'anAliasIsIdentical'
		score = score + 40
	end 'anAliasIsIdentical'
	return score + 2
end 'main'
```
```exitcode
42
```

<!-- test: green-threads-disable-promotion -->
A green thread's stack GROWS BY RELOCATION: it is copied to a larger region, rsp and the saved-frame-pointer
chain are rebased, and the old pages are FREED. Everything addressed off the frame pointer survives that; a
frame ADDRESS already materialized into a register does not, and nothing enumerates registers to fix. So a
program that runs green threads promotes nothing at all.

⚠ **THE RECURSION IS WHAT MAKES THIS CASE DECISIVE.** A shallow green thread never outgrows its initial 2 KB
stack, so a wrongly promoted record is never relocated and answers correctly — MEASURED, 42 either way.
`deep(400)` forces several relocations while the record is live across the call. MEASURED with the gate
removed: **a segmentation fault**, the record's address left pointing into freed pages.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function deep(n Integer) returns Integer
	if n <= 0 'bottom'
		return 0
	end 'bottom'
	return 1 + deep(n - 1)
end 'deep'

function onTheGreenThread() returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	let p = Point.create(41, y: 1)
	let depth = deep(400)
	return p.x + p.y - depth
end 'onTheGreenThread'

function main() returns ExitCode
	let promise = async onTheGreenThread()
	return await promise + 400
end 'main'
```
```exitcode
42
```
