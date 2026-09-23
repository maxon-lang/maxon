---
feature: inline-leaves
status: experimental
keywords: [optimizer, inliner, leaf, codegen, panic, runtime]
category: codegen
---
# Inlining Tiny Leaf Functions

## Documentation

`inlineLeaves` is the Std→Std pass that replaces a direct call to a TINY LEAF function with a copy of
that function's body. It runs after `insertRangeChecks` and after `inlineManagedPrimitives` — last of
the three Std→Std splicing passes, for the reason below.

A callee is eligible when ALL of the following hold — computed ONCE per callee, on the body as it
stands BEFORE any splice, and memoised:

- its body contains **no call of any kind** (`call`, `tryCall`, `callIndirect`, `witnessCall`,
  `witnessTryCall`) — which is also what makes ownership free here, because a retain, a release and a
  scope drop are all parser-emitted calls;
- it contains **no op the dialect marks `isUnsupportedInInlineBody`** except the two PANIC ops, which
  are copied like any other block (see `THE PANIC RULE` below) — so an `errorReturn` (a throwing body),
  a `stackRecordAddr`, a `covPoint` and every OS primitive refuse the callee. The OS band carries the
  mark because it is `isCall: true` and `inlineOpRole` classifies it off the flag, where the only two
  answers are "body op" and "refused": a system op that is NOT a call is a body op like any other, which
  is what admits `memFill`, the two atomics and the per-OS-thread slot read;
- it has **no more than 24 body ops** (over all its blocks, terminators counted, the `param` ops not).
  24 is measured rather than chosen: `regMaskContains` — the function this pass exists for — is 23 Std
  ops, because a shift whose count the compiler cannot fold carries the 6-op saturation `THE SHIFT RULE`
  emits at the Maxon tier, and its `int(0 to 63)` parameter adds a 9-op entry guard on top of that;
- it takes **no by-reference parameter**, and it does not run user code on its caller's stack;
- **its body was actually lowered.** A stdlib function no path from `main` reaches keeps its relocated
  blocks and gets none of its ops — not even its `param` ops — so it looks exactly like a zero-op leaf.
  A block still in `Terminator.unset` is what says "never written";
- it is **not the caller** — a self-recursive function is refused, and a mutually recursive pair is
  already refused by the leaf rule.

Only a direct `StdOp.call` site is ever rewritten. A `tryCall` is never touched: it is the throwing
call's spelling AND the existential-returning call's, and neither is what a tiny leaf is.

### ⭐⭐ `__Raw.splicedAtEverySite()` — a body that overrules the BUDGET and nothing that would break it

A `runtime/` body may declare `__Raw.splicedAtEverySite()`. It says the body has no frame worth keeping:
the inliner must splice it into every one of its call sites whatever its size. It exists so a family a
BUILDER used to emit inline can live in tier source instead — a builder splices its code at each site and
pays nothing, and a tier body reached by a call would pay the frame, the argument moves and the `ret` the
builder never paid.

**What it waives** is every judgement about what inlining is WORTH:

- the 24-op budget above;
- the called-once rule's register-pressure budget;
- the inline-frame-record rule, on the ground that a builder's spliced code never carried a frame record
  either — so a panic inside a copy prints the caller's frame on a target without them;
- the leaf rule's *"no call of any kind"* for ONE edge: a call into another body that declares the row.
  Those are spliced away callees-first, before either body is copied anywhere. It is this pass's only
  exception to ONE ROUND, NO CASCADE, and it is bounded by the graph of declared rows being a DAG.

**What it may never waive** is every judgement about what inlining would BREAK, and each still refuses:
`splicingWouldWidenTheSafePoint`, a by-reference parameter, the green-thread stack guard, `ownFrame` (a
contradiction, refused at the declaration as E3157), an `isUnsupportedInInlineBody` op, and a
```RequiredRuntime request for the body itself.

**A refusal is never silent.** `requireAlwaysSplicedBodiesAreGone` reads the SURVIVING module in
`BackendDispatch.buildBackend` and reports **E3158** at the body's declaration, naming the reference that
survived and the rule that refused — a cycle of declared rows among them. It is asked there rather than
inside this pass because a check the pass performs can see only the sites the pass reached.

The leaf rule is one of the inliner's TWO admission rules. The other — a function with exactly one
direct call site in the program is spliced regardless of size — is `specs/inline-called-once.md`,
which also holds the trace mechanism both rules share.

### ⭐ WHY IT RUNS AFTER `inlineManagedPrimitives` (EC17)

`__managed_count(a)` is the one managed primitive `inlineManagedPrimitives` rewrites IN PLACE — into a
single `loadIndirect` of the record's `length@8`. Every accessor whose whole body is that one call is
therefore a body **holding a call** until that pass has run, and the leaf rule refuses one outright. Run
the other way round, this pass refused them one pass before the call stopped existing: MEASURED on the
compiler's own self-compile, `Array.isEmpty` kept **209** call sites, `Parser.advance` **106** and
`String.byteLength` **92**, plus about a hundred more `count`/`size` accessors of the same shape — **641
direct calls in one program.**

⭐ **THE REORDER CANNOT COST THIS PASS A CALLEE, AND THAT IS A PROOF RATHER THAN A MEASUREMENT.**
`inlineManagedPrimitives` rewrites only bodies that hold a `__managed_*` CALL, and a leaf holds no call
by rule — so no body it can reach is one this pass would have accepted. (Measured anyway: eligible
callees 469 → 530, sites 4,138 → 4,921, and that pass's own expansion count unchanged at 6,032.)

⚠ **IT IS A REORDER AND NOT A SECOND ROUND**, which was the other shape considered. A second round
would admit the cascade the next paragraph refuses on purpose.

**ONE ROUND, NO CASCADE.** Eligibility is decided on the pre-splice body, so a caller that becomes
call-free BY being inlined into does not become eligible in the same compile. That is what bounds the
work at one copy per site and keeps a chain of helpers from pulling a large body through three of them.

### ⭐ THE PANIC RULE — how a ranged-parameter leaf is inlined without moving a stack trace

A ranged parameter's entry guard ends in an `osPanic` block, so nearly every small function with a
narrowed parameter or return would be excluded by a rule that refused to copy one. It is not refused:
a leaf's panic blocks are **copied with the rest of its body**, and every copied block carries its
INLINE SITE — the callee it came out of. The backend turns those tags into inline range records in
the `__inlframes` table behind `__symtable` and the frame printers walk them, so a panic from a copied block still prints
`in clampPct / in main / in mrt_start` off a frame that belongs to `main`. The mechanism is
`specs/inline-called-once.md`'s, and its cases are the gate on the trace; the two stderr cases below
are the leaf rule's own pins on it.

Stores make no difference to eligibility: a leaf with a store and a panic is copied whole, exactly as
one with stores and no panic is. `/` and `mod` are admitted for the same reason — a hardware fault's
address falls inside a range record too.

### What the splice does

The call's block is split at the site. The head keeps everything before the call; a CONTINUATION takes
everything after it, plus the block's whole exit; and the continuation's single block arg **IS the
call's original result id**, so no op after the site changes. The callee's blocks are copied as fresh
blocks tagged with the site, its `param i` maps to the site's argument `i` (a generic callee's trailing
layout / count / witness parameters are ordinary parameters and map the same way), and every `ret v`
becomes a branch to the continuation carrying `v`.

## Tests

<!-- test: a-pure-leaf-is-inlined -->
A `regMaskContains`-shaped leaf: a ranged parameter (so it carries an entry guard and a panic block),
a shift and a mask, called from a loop. The mask 5 has bits 0 and 2 set, so two of the eight
iterations count.

```maxon
typealias RegNum = int(0 to 63)
typealias RegMask = int(0 to u64.max)

function maskContains(mask RegMask, regNum RegNum) returns bool
	return ((mask shr regNum) and 1) != 0
end 'maskContains'

function main() returns ExitCode
	let mask = 5 as RegMask
	var count = 0
	var i = 0
	while i < 8 'each'
		if maskContains(mask, regNum: i as RegNum) 'set'
			count = count + 1
		end 'set'
		i = i + 1
	end 'each'
	return count as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: the-inlined-guard-still-panics-with-the-callees-frame -->
⭐ **THE PANIC RULE'S GATE.** `clampPct` is a leaf whose entry guard panics, and the argument is
COMPUTED from a loop counter so the compile-time half cannot fold it — and carries no typealias, so it
reaches `Percent` with no cast and the guard that refuses it is the CALLEE's own. The inlined guard
refuses the value from a panic block copied into `main`, and the block's inline site is what prints
`in clampPct` above `in main` — which is why this stderr is byte-identical to what the same program
prints with the pass disabled.

```maxon
typealias Percent = int(0 to 100)

function clampPct(x Percent) returns Percent
	return x
end 'clampPct'

function main() returns ExitCode
	var last = 0
	for i in 1 to 1 'once'
		last = clampPct(i * 101)
	end 'once'
	return last as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at the-inlined-guard-still-panics-with-the-callees-frame.test:4: Range check failed: value outside typealias 'Percent'
Stack trace:
  in clampPct
  in main
  in mrt_start
```

<!-- test: a-leaf-with-a-store-is-copied-whole -->
A `ValueMinter.mint`-shaped leaf — read a field, add one, write it back — has a store and no panic, so
it is copied whole. Called three times in ONE block, which is also the shape a carve-then-rewalk splice
would copy the block's tail three times for.

```maxon
typealias Integer = int(i64.min to i64.max)

type Minter
	export var next as Integer

	static function create() returns Self
		return Self{next: 0}
	end 'create'

	function mint() returns Integer
		let id = self.next
		self.next = self.next + 1
		return id
	end 'mint'
end 'Minter'

function main() returns ExitCode
	var m = Minter.create()
	let a = m.mint()
	let b = m.mint()
	let c = m.mint()
	return (a + b + c + 39) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: a-leaf-with-a-store-and-a-panic-is-inlined -->
`record` writes a field AND carries a ranged parameter's panic block. The panic block is copied with
its inline site rather than re-run through the call, so the store runs once and the leaf is spliced
at both sites like any other.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Percent = int(0 to 100)

type Meter
	export var level as Integer

	static function create() returns Self
		return Self{level: 0}
	end 'create'

	function record(p Percent) returns Integer
		self.level = self.level + (p as Integer)
		return self.level
	end 'record'
end 'Meter'

function main() returns ExitCode
	var m = Meter.create()
	let first = m.record(40 as Percent)
	let second = m.record(2 as Percent)
	return (second - first + 40) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: an-identity-leaf-returns-its-argument -->
`return a` — the result is a PARAMETER, so the continuation's block arg carries the site's ARGUMENT
value rather than anything the copy defines.

```maxon
typealias Integer = int(i64.min to i64.max)

function identity(a Integer) returns Integer
	return a
end 'identity'

function main() returns ExitCode
	return identity(42) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: a-void-leaf -->
A void leaf writes a module-level `var`. Its continuation takes NO block arg — there is no result to
carry — and its `retVoid` becomes a bare branch.

```maxon
typealias Integer = int(i64.min to i64.max)

var total = 0

function bump(n Integer)
	total = total + n
end 'bump'

function main() returns ExitCode
	bump(40)
	bump(2)
	return total as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: two-returns -->
A branchy leaf with two `return`s gives the continuation TWO incoming edges, each carrying its own
value into the same block arg. Two sites in one block as well.

```maxon
typealias Integer = int(i64.min to i64.max)

function pick(a Integer) returns Integer
	if a > 10 'big'
		return 4
	end 'big'
	return 2
end 'pick'

function main() returns ExitCode
	return (pick(50) * 10 + pick(1)) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: a-recursive-tiny-function-is-not-inlined -->
A self-call is a call, so the function is not a leaf and nothing is spliced — into its caller or into
itself. It still runs.

```maxon
typealias Integer = int(i64.min to i64.max)

function countDown(n Integer) returns Integer
	if n <= 0 'done'
		return 0
	end 'done'
	return countDown(n - 1) + 1
end 'countDown'

function main() returns ExitCode
	return countDown(42) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: a-throwing-tiny-function-is-not-inlined -->
A throwing body leaves through `errorReturn`, which the dialect marks unsupported in an inline body,
and its call site is a `tryCall`, which this pass never rewrites. Both halves refuse it.

```maxon
typealias Integer = int(i64.min to i64.max)

enum HalveError
	odd
end 'HalveError'

function halve(n Integer) returns Integer throws HalveError
	if n < 0 'negative'
		throw HalveError.odd
	end 'negative'
	return n / 2
end 'halve'

function main() returns ExitCode
	let v = try halve(84) otherwise 0
	return v as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: a-generic-leaf -->
A `Map.count`-shaped accessor on a type declared with `uses`: ONE shared body, reached from two
instantiations. Its trailing LAYOUT-DESCRIPTOR parameter is an ordinary parameter and maps to the
site's argument like any other — which is what the two `__il_body` blocks in the golden say, one per
instantiation, each carrying that instantiation's own `__layout_Box_*` address in.

⚠ **IT READS A FIELD OF A CONCRETE TYPE, AND THAT IS THE WHOLE DIFFERENCE FROM `get() returns T`.**
Handing back the type PARAMETER makes the body retain it — `call __retain_type_param`, since `T` may be
managed — and a body with a call is not a leaf. So the shape that inlines is the accessor whose ANSWER
is concrete, however generic the record holding it is; measured on the self-compile, `Map.count` and
`Set.count` are exactly this and both reach zero call sites.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)

type Box uses T
	export var value as T
	export var weight as Integer

	static function create(v T, weight Integer) returns Self
		return Self{value: v, weight: weight}
	end 'create'

	function weightOf() returns Integer
		return self.weight
	end 'weightOf'
end 'Box'

typealias IntBox = Box with Integer
typealias ByteBox = Box with Byte

function main() returns ExitCode
	let a = IntBox.create(7, weight: 40)
	let b = ByteBox.create(2 as Byte, weight: 2)
	return (a.weightOf() + b.weightOf()) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: a-byte-typed-leaf -->
A narrow `StdType` through the value column: the parameter is a `Byte`, so the callee carries the
two-bound entry guard and its panic block, and it is pure.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)

function doubled(b Byte) returns Integer
	return b + b
end 'doubled'

function main() returns ExitCode
	return doubled(21 as Byte) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: a-float-leaf -->
An `f64` parameter and an `f64` return travel through the same column and the same continuation block
arg — whose TYPE is the callee's return type, which is what puts the value in the right register file.

```maxon
function scale(x Real) returns Real
	return x * 2.0
end 'scale'

function main() returns ExitCode
	return trunc(scale(21.0))
end 'main'
typealias Real = float(f64.min to f64.max)
```
```exitcode
42
```

<!-- test: a-leaf-called-from-a-leaf-is-not-cascaded -->
`inner` is a leaf and is spliced into `outer`. `outer` had a call when eligibility was decided, so it
is not a leaf and `main`'s call to it stands — one round, no cascade.

```maxon
typealias Integer = int(i64.min to i64.max)

function inner(a Integer) returns Integer
	return a + 1
end 'inner'

function outer(a Integer) returns Integer
	return inner(a) * 2
end 'outer'

function main() returns ExitCode
	return outer(20) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: an-accessor-that-becomes-a-leaf-after-the-managed-rewrite-is-inlined -->
⭐ **EC17's GATE.** `Array.isEmpty`'s whole body is one call to `__managed_count`, which
`inlineManagedPrimitives` rewrites in place into a single `loadIndirect`. Ordered before that pass this
one saw a body holding a call and refused it; ordered after, the accessor is a two-op leaf and both call
sites are spliced. The fragment is the pin: `main` holds no `callDirect Array.isEmpty`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var a = IntArray.create()
	if not a.isEmpty() 'startsEmpty'
		return 1
	end 'startsEmpty'
	a.push(42)
	if a.isEmpty() 'nowHoldsOne'
		return 2
	end 'nowHoldsOne'
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: a-whole-loop-over-an-array-becomes-a-leaf -->
⭐ The reorder reaches further than the `count` accessors: EC15 made a `for v in a` over a concrete
`Array with Integer` CALL-FREE (a known stride needs no fork and no slow arm), so a function whose only
calls were the loop's own element access is a leaf too. `total` is spliced into `main`, frame and all.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function total(a IntArray) returns Integer
	var t = 0
	for v in a 'each'
		t = t + v
	end 'each'
	return t
end 'total'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(20)
	a.push(22)
	return total(a) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: the-panic-rule-holds-when-the-argument-is-an-inlined-element -->
⭐ **THE PANIC RULE, UNDER THE EC17 ORDER.** The value the inlined guard tests is an ELEMENT, produced
by the access `inlineManagedPrimitives` has already expanded into this loop — so the splice reads a
value that pass wrote, in a block it shaped. The guard still refuses it, the copied panic block still
carries `clampPct` as its inline site, and the trace still names `clampPct` above `main`.

The array is a LITERAL because its element must carry no alias: a `Percent`-typed store is guarded at
every write, so no element of an `Array with Percent` can be out of `Percent`'s range, and an element of
any other alias cannot reach `clampPct` without a cast. A literal's element is unnamed, decays into the
`Percent` slot with no cast, and the only guard it meets is the callee's.

```maxon
typealias Percent = int(0 to 100)

function clampPct(x Percent) returns Percent
	return x
end 'clampPct'

function main() returns ExitCode
	let a = [101]
	var last = 0
	for v in a 'each'
		last = clampPct(v)
	end 'each'
	return last as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at the-panic-rule-holds-when-the-argument-is-an-inlined-element.test:4: Range check failed: value outside typealias 'Percent'
Stack trace:
  in clampPct
  in main
  in mrt_start
```

<!-- test: a-loop-whose-element-access-keeps-a-slow-arm-is-not-a-leaf -->
⛔ **THE CONTROL, AND IT IS THE SAME SOURCE AS `a-whole-loop-over-an-array-becomes-a-leaf` WITH ONE
TYPE CHANGED.** A `Byte` element is stamped 1, and EC15's plan for a byte stamp is `runtimeFork` — both
width arms AND the slow arm holding `__managed_get_unchecked`. So this loop still holds a call after the
managed rewrite, `totalBytes` is still not a leaf, and `main` still calls it. What the reorder changes
is which bodies stop holding a call, not what a leaf is.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Bytes = Array with Byte

function totalBytes(b Bytes) returns Integer
	var t = 0
	for v in b 'each'
		t = t + v
	end 'each'
	return t
end 'totalBytes'

function main() returns ExitCode
	var b = Bytes.create()
	b.push(20 as Byte)
	b.push(22 as Byte)
	return totalBytes(b) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: a-runtime-body-that-asks-to-be-spliced-is-spliced-past-the-budget -->
⭐⭐ **THE BUDGET IS A COST RULE, AND `__Raw.splicedAtEverySite()` OVERRULES IT.** `__probe_wide` is a
runtime body far past `MaxInlinedLeafOps`, so the budget alone refuses it — and it has TWO call sites, so
the called-once rule never looks at it either. The row is the whole difference: the golden below is
`__probe_wide_caller` with both copies of the body spliced in and no `callDirect` left.

⚠ **A COMPILER WITHOUT THE ROW RENDERS `call __probe_wide` TWICE HERE**, which is what this case exists
to hold. It is the same shape the `tlsSlotLoad` case above uses, and for the same reason: a golden
difference is a REFERENCE rather than a gate, so the reading that makes it evidence is the one taken from
a compiler built without the change.

⚠ **THE ROW OVERRIDES WHAT INLINING IS WORTH AND NEVER WHAT IT WOULD BREAK.** Both bodies here are tier
source, which is what keeps `InlineLeaves.splicingWouldWidenTheSafePoint` — a correctness rule the row
does not touch — out of the way; a tier callee spliced into a caller the compiler does not own is refused
by it, and the row then ends the compile rather than falling back to a call.
```maxon
// --- runtime-file: Probe.maxon
module function __probe_wide(seed MachineWord) returns MachineWord
	__Raw.splicedAtEverySite()

	var acc = seed + 1
	acc = acc + 2
	acc = acc + 3
	acc = acc + 4
	acc = acc + 5
	acc = acc + 6
	acc = acc + 7
	acc = acc + 8
	acc = acc + 9
	acc = acc + 10
	acc = acc + 11
	acc = acc + 12
	acc = acc + 13
	acc = acc + 14

	return acc
end '__probe_wide'

function __probe_wide_caller(seed ExitCode) returns ExitCode
	let base = __probe_wide(seed as MachineWord)
	let again = __probe_wide(base and 1)

	return (base + again) as ExitCode
end '__probe_wide_caller'
// --- stdlib-overlay: Builtins.maxon
export function probeWideSplice(seed ExitCode) returns ExitCode
	return __probe_wide_caller(seed)
end 'probeWideSplice'
// --- file: main.maxon
function main() returns ExitCode
	return probeWideSplice(0)
end 'main'
```
```exitcode
211
```
```RequiredRuntime
__probe_wide_caller
```

<!-- test: a-body-that-asks-to-be-spliced-may-call-another-that-does -->
⭐⭐ **THE ONE CASCADE THE ROW BUYS, AND WHY IT IS NOT OPTIONAL.** A builder composes its emitters freely
and pays nothing for it — `emitElementByteLen` calls `emitElementBits` and `emitBitsToBytes`, and all three
land inline at every site. A tier family that could hold no helpers would be a family written in one
function, so a body declaring the row may call another that does.

⭐ **CALLEES FIRST, AND ONCE.** `__probe_inner` is spliced out of `__probe_outer` before `__probe_outer`
is copied anywhere, so each body is copied exactly once per site and the work is bounded by the chain of
DECLARED rows rather than by anything the program controls. The golden is `__probe_chain_caller` with both
levels flattened into it and no `callDirect` left.

⚠ **`__probe_outer` IS WHAT THIS CASE DISCRIMINATES ON, NOT `__probe_inner`.** The inner body holds no
call and is an ordinary tiny leaf, which a compiler without the row inlines anyway; the outer body holds
TWO calls, so the leaf rule refuses it outright and the called-once rule never sees it. Without the row a
compiler renders `callDirect __probe_outer` twice here.
```maxon
// --- runtime-file: Probe.maxon
module function __probe_inner(seed MachineWord) returns MachineWord
	__Raw.splicedAtEverySite()

	return seed + 1
end '__probe_inner'

module function __probe_outer(seed MachineWord) returns MachineWord
	__Raw.splicedAtEverySite()

	return __probe_inner(seed) + __probe_inner(seed + 1)
end '__probe_outer'

function __probe_chain_caller(seed ExitCode) returns ExitCode
	let base = __probe_outer(seed as MachineWord)
	let again = __probe_outer(base and 1)

	return (base + again) as ExitCode
end '__probe_chain_caller'
// --- stdlib-overlay: Builtins.maxon
export function probeSpliceChain(seed ExitCode) returns ExitCode
	return __probe_chain_caller(seed)
end 'probeSpliceChain'
// --- file: main.maxon
function main() returns ExitCode
	return probeSpliceChain(0)
end 'main'
```
```exitcode
8
```
```RequiredRuntime
__probe_chain_caller
```

<!-- test: error.a-body-that-asks-to-be-spliced-where-it-may-not-be-is-refused -->
⛔⛔ **THE ROW OVERRIDES WHAT INLINING IS WORTH AND NEVER WHAT IT WOULD BREAK, AND THIS IS THE CASE THAT
HOLDS THE SECOND HALF.** `__probe_unowned` is compiler-owned scaffolding and `probeUnowned` is not, so
`InlineLeaves.splicingWouldWidenTheSafePoint` refuses the splice: `__symtable` carries ONE safe-point flag
per SYMBOL, and the copied bytes would sit inside a symbol the predicate answers PREEMPTIBLE for. The row
does not lift that, and must not.

⛔⛔ **WHAT IS UNDER TEST IS THAT THE REFUSAL IS LOUD.** A compiler that quietly left the call would be
green on every case in this file and every golden in the suite, and a family ported on the strength of the
row would ship at exactly the cost the port was written to remove — with nothing anywhere saying so. The
report names the body, the reference that survived and the rule that refused, and it is positioned at the
declaration that has to change.

⚠ **`probeUnowned` IS CALLED TWICE SO THE REPORT NAMES THE SAME CALLER ON EVERY LANE.** With one site the
called-once rule moves it into `main` where the target records inline frames and leaves it where the target
does not, and the sentence would then differ between x64 and wasm for a reason that has nothing to do with
the row. The report reads the module as it IS rather than as it was written, which is the point of asking
after the last round — so a case pinning it has to fix what that module looks like.

⚠ **IT IS ASKED OF THE FINISHED MODULE** (`InlineLeaves.requireAlwaysSplicedBodiesAreGone`, called from
`BackendDispatch.buildBackend` beside the emitted-call arity gate), not inside the inliner. A check the
pass performs can see only the sites the pass reached, so the next narrowing of an optimization would take
the detector away with it.
```maxon
// --- runtime-file: Probe.maxon
module function __probe_unowned(seed ExitCode) returns ExitCode
	__Raw.splicedAtEverySite()

	return seed + 1
end '__probe_unowned'
// --- stdlib-overlay: Builtins.maxon
export function probeUnowned(seed ExitCode) returns ExitCode
	return __probe_unowned(seed)
end 'probeUnowned'
// --- file: main.maxon
function main() returns ExitCode
	return probeUnowned(0) + probeUnowned(1)
end 'main'
```
```maxoncstderr
error E3158: <fragment>:3:17: '__probe_unowned' declares '__Raw.splicedAtEverySite()', so it must be left at no call site, but 'probeUnowned' still calls it: it is the compiler's own scaffolding and its caller is not, so the spliced bytes could be stopped where the callee never may be. The row overrides the inliner's cost rules — the op budget, the pressure budget, the inline-frame-record rule — and never its correctness rules, so a body one of those refuses is a body that must not declare it
```

<!-- test: error.a-cycle-of-bodies-that-ask-to-be-spliced-is-refused -->
⛔⛔ **THE ROW BUYS ONE CASCADE — A BODY DECLARING IT MAY CALL ANOTHER — AND A CYCLE IS WHERE THAT
CASCADE HAS NO END.** Splicing any member of one would nest a body inside itself, so no member can be
spliced away and every one of them keeps a call its author was promised would not exist.

⛔ **IT IS DETECTED EXACTLY AND REFUSED, NEVER CAPPED.** A fixpoint with an iteration limit would splice
some number of levels, leave a call standing at the boundary and report nothing — a silent wrong answer at
precisely the edge this row cannot afford one. The search is over the graph of declared rows alone, which
is the tier's own handful of bodies, and the report renders the ring.

⚠ Neither body is called once, so the called-once rule does not collapse the pair ahead of the leaf rule
and the cycle the report names is the one the source wrote.
```maxon
// --- runtime-file: Probe.maxon
module function __probe_cycle_a(seed MachineWord) returns MachineWord
	__Raw.splicedAtEverySite()

	return __probe_cycle_b(seed) + __probe_cycle_b(seed + 1)
end '__probe_cycle_a'

module function __probe_cycle_b(seed MachineWord) returns MachineWord
	__Raw.splicedAtEverySite()

	return __probe_cycle_a(seed) + 2
end '__probe_cycle_b'

function __probe_cycle_entry(seed ExitCode) returns ExitCode
	return __probe_cycle_a(seed as MachineWord) as ExitCode
end '__probe_cycle_entry'
// --- stdlib-overlay: Builtins.maxon
export function probeCycle(seed ExitCode) returns ExitCode
	return __probe_cycle_entry(seed)
end 'probeCycle'
// --- file: main.maxon
function main() returns ExitCode
	return probeCycle(0)
end 'main'
```
```maxoncstderr
error E3158: <fragment>:9:17: '__probe_cycle_b' declares '__Raw.splicedAtEverySite()', so it must be left at no call site, but '__probe_cycle_a' still calls it: it lies on a cycle of bodies declaring the row — '__probe_cycle_b' calls '__probe_cycle_a' calls '__probe_cycle_b' — so splicing any member would nest a body inside itself. The row overrides the inliner's cost rules — the op budget, the pressure budget, the inline-frame-record rule — and never its correctness rules, so a body one of those refuses is a body that must not declare it
```

<!-- test: error.a-body-holding-an-op-the-splice-cannot-copy-is-refused -->
⛔ **THE THIRD CORRECTNESS RULE THE ROW DOES NOT LIFT: the dialect's own `isUnsupportedInInlineBody`.**
`__Raw.scratch` lowers to a `stackRecordAddr`, which names a slot of the function that OWNS it — copied
into a caller it would address the caller's frame instead. That is a wrong answer rather than a cost, so
the splice refuses and the promise cannot be kept.

⚠ The reason the report gives is the op's own refusal, not a generic one: the body is walked and the first
op that puts it outside the rule is what the sentence names.
```maxon
// --- runtime-file: Probe.maxon
module function __probe_unhonourable(seed MachineWord) returns MachineWord
	__Raw.splicedAtEverySite()

	return __Raw.loadWord(__Raw.scratch(8), offset: 0) + seed
end '__probe_unhonourable'

function __probe_unhonourable_entry(seed ExitCode) returns ExitCode
	return __probe_unhonourable(seed as MachineWord) as ExitCode
end '__probe_unhonourable_entry'
// --- stdlib-overlay: Builtins.maxon
export function probeUnhonourable(seed ExitCode) returns ExitCode
	return __probe_unhonourable_entry(seed)
end 'probeUnhonourable'
// --- file: main.maxon
function main() returns ExitCode
	return probeUnhonourable(0)
end 'main'
```
```maxoncstderr
error E3158: <fragment>:3:17: '__probe_unhonourable' declares '__Raw.splicedAtEverySite()', so it must be left at no call site, but '__probe_unhonourable_entry' still calls it: it holds an op the splice cannot copy. The row overrides the inliner's cost rules — the op budget, the pressure budget, the inline-frame-record rule — and never its correctness rules, so a body one of those refuses is a body that must not declare it
```

<!-- test: a-runtime-leaf-reading-its-machines-tls-slot-is-spliced -->
⭐⭐ **THE ADMISSION RULE READS `isUnsupportedInInlineBody`, AND THE `system` BAND IS NOT ONE ANSWER.**
`__probe_tls_read` is a two-op leaf whose whole body is the per-OS-thread slot read every allocation and
every current-GT read begins with. It is `isCall: false`, so the flag's tail files it a body op and both
admission rules may carry it; the golden below is `__probe_tls_caller` with the read spliced in and no
`callDirect` left. A compiler that refused the callee renders the call instead, which is the difference
this case exists to hold.

⚠ **SPLICING IS NOT HOISTING, AND ONLY THE SECOND WOULD BE WRONG.** A copy stays where it was written, so
the M whose slot is read is the M the surrounding code is running on. What forbids the move is the pair of
rosters `classifyArithOperands` and `classifyLoadOperands`, which answer `neither` and `notALoad` for this
variant — so CSE, LICM and the unswitcher's invariance test each decline it before `isPure` is reached.

⚠ **BOTH FUNCTIONS ARE TIER SOURCE, WHICH IS WHAT LETS THE SPLICE HAPPEN AT ALL.**
`InlineLeaves.splicingWouldWidenTheSafePoint` refuses the compiler's own scaffolding spliced into code
that is not, so a runtime callee reaches only a runtime caller — and the pair here is inside one
`runtime/` file.

⚠ It carries NO `unsupported-targets` marker: wasm32-wasi has no per-thread storage and answers E3104,
which the harness counts as a SKIP naming this case.
```maxon
// --- runtime-file: Probe.maxon
module function __probe_tls_read(tebOffset MachineWord) returns MachineWord
	return __Raw.tlsSlotLoad(tebOffset)
end '__probe_tls_read'

function __probe_tls_caller(tebOffset ExitCode) returns ExitCode
	if tebOffset == 0 'neverAMachine'
		return 0
	end 'neverAMachine'

	return __probe_tls_read(tebOffset as MachineWord) as ExitCode
end '__probe_tls_caller'
// --- stdlib-overlay: Builtins.maxon
export function probeTlsSlotRead(tebOffset ExitCode) returns ExitCode
	return __probe_tls_caller(tebOffset)
end 'probeTlsSlotRead'
// --- file: main.maxon
function main() returns ExitCode
	return probeTlsSlotRead(0)
end 'main'
```
```exitcode
0
```
```RequiredRuntime
__probe_tls_caller
```
