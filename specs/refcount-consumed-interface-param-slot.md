---
feature: refcount-consumed-interface-param-slot
status: selfhosted
status-reason: both cases PASS here, but both committed goldens were minted by another compiler and disagree with what this one emits (its functions come out in a different order), so un-suspending re-mints them and overwrites the only record of what v1 emitted (measured 2026-08-06, BATCH29/A3a, by un-suspending the file and running the full suite). Already re-homed: specs-shv2/refcount-consumed-interface-param-slot.md, 2 of 2 active.
keywords: [refcount, interface, param, consumed, slot, loop, borrow, memory]
category: memory-safety
---

# Refcount: Consumed Interface Parameter Slot Disposal

## Documentation

An interface-typed value is a 16-byte fat pointer `{value, witness}`. When it is
a function PARAMETER, mem2reg leaves the value half in an unpromoted stack slot
and re-derives it with a `memref.load` at every use — each load is a fresh SSA
value. When the parameter is also CONSUMED (Concept C moves the caller's `+1`
into the callee), the callee owns that single reference and must dispose it
exactly once.

Keying the disposal off any single load is wrong once the slot is loaded more
than once:

- **Pre-loop borrow + loop consume.** A function that borrows the parameter
  once before a loop (e.g. `collectFuncsNeedingRegalloc(regTarget)`) and then
  CONSUMES it inside the loop (e.g. `allocateRegistersForFunc(.., regTarget)` per
  function) had its owned `+1` released at the *entry borrow's* last use — the
  analysis attributed the param's death to that one load and never saw the later
  loop loads (separate SSA values). The premature `lastUseDecref` frees the
  object; the loop's re-load then `incref`s freed memory
  (`rc-sanitize: INCREF of freed object … lastUseDecref @ … → INCREF @ …`), or,
  unsanitized, reads a dangling pointer. This is the shape that blocked the
  self-hosted bootstrap fixpoint (`allocateRegistersWithTarget`).

- **Straight-line multiple consume.** A function that consumes the parameter at
  two separate call sites (two loads, each moved into a container) transferred
  the same single `+1` twice — an over-release / double-free at the second
  destructor.

- **No borrow, single loop consume.** Symmetrically, when nothing releases the
  moved-in `+1` at all, it LEAKS once per call.

The fix treats a consumed interface parameter's value-half slot as SLOT-OWNED
(the same discipline a local interface value gets): the entry param→slot store
is a MOVE, every load is a borrow that `incref`s a fresh reference when it feeds
a consuming call, and the slot's single `+1` is dropped exactly once at each
function exit. This disposes the reference once regardless of how many times the
unpromoted slot is re-loaded.

This test is restricted to the register-frame targets. On `wasm32-wasi` every
function shares ONE linear-memory slot region, so an unpromoted slot does not
persist across the calls in its own body — its content is unrecoverable at scope
exit — and the slot-owned drop is deliberately disabled there (a pre-existing
wasm slot-model limitation needing a per-invocation shadow stack). The consumed
interface param therefore keeps its unfixed behavior on wasm and this test would
fault there.

## Tests

<!-- test: pre-loop-borrow-then-loop-consume -->
<!-- targets: x64-windows, x64-linux, x64-macos, arm64-windows, arm64-macos, arm64-linux -->
A consumed interface param borrowed before a loop and consumed inside it must
survive the borrow's last use — the owned `+1` is released once at scope exit,
not at the pre-loop borrow (which would free it before the loop re-reads the
slot).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ItemArray = Array with Integer

interface Payload
	function tag() returns Integer
end 'Payload'

type Desc implements Payload
	export var v as Integer

	export static function create(x Integer) returns Desc
		return Self{v: x}
	end 'create'

	export function tag() returns Integer
		return self.v
	end 'tag'
end 'Desc'

// Consumes `p` by storing it; destroyed at scope end.
type Holder
	export var p as Payload

	export static function create(p Payload) returns Holder
		return Holder{p: p}
	end 'create'

	export function run() returns Integer
		return self.p.tag()
	end 'run'
end 'Holder'

// Non-inlined BORROW of `p` (mirrors collectFuncsNeedingRegalloc(regTarget)).
function borrowCount(p Payload, base Integer) returns Integer
	var acc = base
	for _ in 0 upto 2 'r'
		acc = acc + p.tag()
	end 'r'
	return acc
end 'borrowCount'

// Consumes `p` (mirrors allocateRegistersForFunc storing into FunctionRegAllocator).
function forFunc(item Integer, p Payload) returns Integer
	var h = Holder.create(p)
	return h.run() + item
end 'forFunc'

// Pre-loop borrow + early-return guard + per-item loop consume
// (mirrors allocateRegistersWithTarget).
function withTarget(items ItemArray, p Payload) returns Integer
	let n = borrowCount(p, base: items.count())
	if n == 0 'empty'
		return 0
	end 'empty'

	var total = 0
	for item in items 'each'
		total = total + forFunc(item, p: p)
	end 'each'
	return total + n
end 'withTarget'

function withX64(items ItemArray) returns Integer
	let p = Desc.create(5)
	return withTarget(items, p: p)
end 'withX64'

function main() returns ExitCode
	var items = ItemArray.create()
	items.push(1)
	items.push(2)
	items.push(3)
	return withX64(items)
end 'main'
```
```exitcode
34
```

<!-- test: straight-line-multiple-consume -->
<!-- targets: x64-windows, x64-linux, x64-macos, arm64-windows, arm64-macos, arm64-linux -->
A consumed interface param moved into two separate containers must copy its `+1`
for all but the last consume, so the single owned reference is not transferred
twice (an over-release at the second destructor).
```maxon
typealias Integer = int(i64.min to i64.max)

interface Payload
	function tag() returns Integer
end 'Payload'

type Desc implements Payload
	export var v as Integer

	export static function create(x Integer) returns Desc
		return Self{v: x}
	end 'create'

	export function tag() returns Integer
		return self.v
	end 'tag'
end 'Desc'

type Holder
	export var p as Payload

	export static function create(p Payload) returns Holder
		return Holder{p: p}
	end 'create'

	export function run() returns Integer
		return self.p.tag()
	end 'run'
end 'Holder'

function twice(item Integer, p Payload) returns Integer
	var h1 = Holder.create(p)
	let a = h1.run()
	var h2 = Holder.create(p)
	let b = h2.run()
	return a + b + item
end 'twice'

function driver() returns Integer
	let p = Desc.create(9)
	return twice(4, p: p)
end 'driver'

function main() returns ExitCode
	return driver()
end 'main'
```
```exitcode
22
```
