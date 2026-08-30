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
  have a rule of their own below — so an `errorReturn` (a throwing body) and every `os*` primitive
  refuse the callee;
- it contains **no `/` or `mod`**. Those lower to `idiv`, which can FAULT (`i64.min / -1`, or a divisor
  the compiler could not prove non-zero), and a hardware fault's backtrace is taken from where the
  INSTRUCTION is. A panic OP has a slow arm this pass can re-issue the call through, so its frame
  survives; a faulting instruction has nothing to re-issue, so the only way to keep its frame is not to
  move it. `specs-shv2/safety.md`'s `integer-overflow-fault-from-int-min-over-minus-one` is what pins
  this, and it is what caught the rule missing;
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
would also re-expand the `__il_slow` arms this pass mints — they hold *the very call the splice moved*,
so re-inlining one copies a panicking leaf's body again for no call removed — and it would admit the
cascade the next paragraph refuses on purpose.

**ONE ROUND, NO CASCADE.** Eligibility is decided on the pre-splice body, so a caller that becomes
call-free BY being inlined into does not become eligible in the same compile. That is what bounds the
work at one copy per site and keeps a chain of helpers from pulling a large body through three of them.

### ⭐ THE PANIC RULE — how a ranged-parameter leaf is inlined without moving a stack trace

A ranged parameter's entry guard ends in an `osPanic` block, so nearly every small function with a
narrowed parameter or return would be excluded by a rule that simply refused to copy one. It is not
refused. Instead:

- a leaf holding a panic block is eligible **only if it is PURE** — no op anywhere in its body writes
  memory (the panic ops themselves excepted, since they are never copied);
- its panic blocks are **NOT copied**. Every edge that led into one is redirected to ONE slow block per
  splice, which re-issues the **original call** with a fresh result and branches to the continuation
  carrying it.

Because the callee is pure, re-running it from the start on the same arguments takes the same path and
panics with the same message, from **its own frame**. So the trace still reads `in clampPct / in main /
in mrt_start`, the callee stays alive (it is still called) and no `.rdata` blob moves.

A leaf with a store AND a panic is refused outright — re-running it would repeat the store. A leaf with
stores and no panic is copied whole.

### What the splice does

The call's block is split at the site. The head keeps everything before the call; a CONTINUATION takes
everything after it, plus the block's whole exit; and the continuation's single block arg **IS the
call's original result id**, so no op after the site changes. The callee's non-panic blocks are copied
as fresh blocks, its `param i` maps to the site's argument `i` (a generic callee's trailing layout /
count / witness parameters are ordinary parameters and map the same way), and every `ret v` becomes a
branch to the continuation carrying `v`.

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
<!-- targets: x64-windows, x64-linux -->
⭐ **THE PANIC RULE'S GATE.** `clampPct` is a pure leaf whose entry guard panics, and the argument is
COMPUTED so the compile-time half cannot fold it. The inlined guard refuses the value, control leaves
for the slow block, the ORIGINAL call runs, and the panic comes out of `clampPct`'s own frame — which
is why this stderr is byte-identical to what the same program prints with the pass disabled.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Percent = int(0 to 100)

function clampPct(x Percent) returns Percent
	return x
end 'clampPct'

function grow(n Integer) returns Integer
	return n * 101
end 'grow'

function main() returns ExitCode
	let big = grow(1)
	return clampPct(big)
end 'main'
```
```exitcode
1
```
```stderr
panic at the-inlined-guard-still-panics-with-the-callees-frame.test:5: Range check failed: value outside typealias 'Percent'
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

	export static function create() returns Self
		return Self{next: 0}
	end 'create'

	export function mint() returns Integer
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

<!-- test: a-leaf-with-a-store-and-a-panic-stays-a-call -->
`record` writes a field AND carries a ranged parameter's panic block. Re-running it on the slow arm
would repeat the store, so the panic rule refuses it and the call stands.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Percent = int(0 to 100)

type Meter
	export var level as Integer

	export static function create() returns Self
		return Self{level: 0}
	end 'create'

	export function record(p Percent) returns Integer
		self.level = self.level + p
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

	export static function create(v T, weight Integer) returns Self
		return Self{value: v, weight: weight}
	end 'create'

	export function weightOf() returns Integer
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
<!-- targets: x64-windows, x64-linux -->
⭐ **THE PANIC RULE, UNDER THE NEW ORDER.** The value the inlined guard tests is an ELEMENT, produced by
the access `inlineManagedPrimitives` has already expanded into this loop — so the splice reads a value
that pass wrote, in a block it shaped, which is the arrangement the old order could never produce. The
guard still refuses it, control still leaves for `__il_slow`, the ORIGINAL call still runs, and the
panic still comes out of `clampPct`'s own frame.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Percent = int(0 to 100)
typealias IntArray = Array with Integer

function clampPct(x Percent) returns Percent
	return x
end 'clampPct'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(101)
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
panic at the-panic-rule-holds-when-the-argument-is-an-inlined-element.test:6: Range check failed: value outside typealias 'Percent'
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
