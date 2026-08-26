---
feature: inline-leaves
status: experimental
keywords: [optimizer, inliner, leaf, codegen, panic, runtime]
category: codegen
---
# Inlining Tiny Leaf Functions

## Documentation

`inlineLeaves` is the Std→Std pass that replaces a direct call to a TINY LEAF function with a copy of
that function's body. It runs between `insertRangeChecks` and `inlineManagedPrimitives`.

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
- it is **not the caller** — a self-recursive function is refused, and a mutually recursive pair is
  already refused by the leaf rule.

Only a direct `StdOp.call` site is ever rewritten. A `tryCall` is never touched: it is the throwing
call's spelling AND the existential-returning call's, and neither is what a tiny leaf is.

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
An `Array.isEmpty`-shaped accessor on a type declared with `uses`: ONE shared body, reached from two
instantiations. Its trailing layout parameter is an ordinary parameter and maps to the site's argument
like any other.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)

type Box uses T
	export var value as T

	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'

	export function get() returns T
		return self.value
	end 'get'
end 'Box'

typealias IntBox = Box with Integer
typealias ByteBox = Box with Byte

function main() returns ExitCode
	let a = IntBox.create(40)
	let b = ByteBox.create(2 as Byte)
	return (a.get() + b.get()) as ExitCode
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
function scale(x float) returns float
	return x * 2.0
end 'scale'

function main() returns ExitCode
	return trunc(scale(21.0))
end 'main'
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
