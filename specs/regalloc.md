---
feature: regalloc
status: in-progress
keywords: regalloc, rematerialization, spill, register-allocation
category: backend
---
# Register Allocator

## Documentation

This spec exercises specific register-allocator behaviors that are easy
to exhibit but hard to gate via regular feature tests. Each test lives
under `fragments-x64-windows/regalloc/`.

The tests are primarily behavioral: they compile a small handwritten
Maxon program, run it, and check the exit code against the deterministic
value the program produces.

## Tests

<!-- test: many-constants -->
<!-- Args: 4 -->
Phase 1 gating test: forces the allocator to materialize many distinct
integer constants in arithmetic against a large number of working values
kept live across calls. Pre-Phase-1 each constant either takes a
register or a spill slot, inflating pressure / stack usage. Post-Phase-1
constants are re-emitted at each use via `movRegImm` and never enter the
live set, so pressure stays bounded and no constant gets a stack slot.

Shape: a runtime seed feeds 8 working values via opaque function calls
(so the optimizer cannot constant-fold them away), then a body that
combines each working value with many distinct constants. The output
is the deterministic value `total mod 256` for total = sum of all the
per-constant arithmetic operations.

```maxon
typealias Integer = int(i64.min to i64.max)

function ident(x Integer) returns Integer
	return x
end 'ident'

function main() returns ExitCode
	let args = CommandLine.args()
	let seed = try int.fromString(try args.get(1) otherwise "0") otherwise 0

	// 4 working values that live across the body — opaque w.r.t. the
	// constant folder thanks to the runtime seed. The body below uses
	// each one with many distinct constants; pre-Phase-1 each constant
	// would compete with the working values for a register slot,
	// inflating pressure. Post-Phase-1 each constant rematerializes at
	// its use site, so v1..v4 + a couple of scratch reg are enough.
	let v1 = ident(seed + 11)
	let v2 = ident(seed + 22)
	let v3 = ident(seed + 33)
	let v4 = ident(seed + 44)

	// Many distinct constants combined with the working values.
	var total = 0
	total = total + (v1 and 255)
	total = total + (v2 and 61680)
	total = total + (v3 and 1193046)
	total = total + (v4 and 286265908)
	total = total + (v1 or 256)
	total = total + (v2 or 8192)
	total = total + (v3 or 196608)
	total = total + (v4 or 4194304)
	total = total + (v1 xor 21930)
	total = total + (v2 xor 2147483647)
	total = total + (v3 + 65536)
	total = total + (v4 + 131072)
	total = total + (v1 + 1234567)
	total = total + (v2 + 7654321)
	total = total + (v3 - 1048576)
	total = total + (v4 - 2097152)

	let masked = total and 255
	return masked as ExitCode
end 'main'
```
```exitcode
102
```

<!-- test: dialect-roundtrip -->
<!-- Args: 0 -->
Phase 2 gating test for the X64 dialect groundwork. The new op variants
(addRegMem / subRegMem / andRegMem / orRegMem / xorRegMem / imulRegMem /
cmpRegMem and andRegImm / orRegImm / xorRegImm / imulRegImm) live in the
dialect but no codegen path emits them in Phase 2 — Phase 3 will fold
spilled reads into them.

What this test gates:

1. The new variants exist in the `X64Op` union. Adding them is what
   *lets the compiler build at all* — every match site on `X64Op` in the
   regalloc / backend / slot projection / op query files needs an arm
   for each new variant, so missing variants surface as
   "match is not exhaustive" build errors and the self-hosted
   compiler can no longer build itself. (Compile-time gate.)
2. The printer arms for the new variants compile (covered by the build).
3. The encoder dispatch routes the new variants to their encoders
   (covered by the build).
4. The end-to-end pipeline still works on a normal program — Phase 2 is
   behaviorally a no-op, so an existing ALU mix using the unchanged
   reg-reg and reg-imm forms must still compile, run, and produce the
   expected exit code with the new dialect in place.

The program mirrors the and/or/xor/cmp shape that Phase 3 will eventually
fold into reg-mem ALU forms when the source operands spill, so wiring
the fold for these specific operators (Phase 3 work) keeps the same
test source meaningful end-to-end. For Phase 2 the test simply asserts
the exit code; RequiredIR is intentionally omitted to avoid pinning the
exact reg-reg lowering that Phase 3 will deliberately change.

```maxon
typealias Integer = int(i64.min to i64.max)

function ident(x Integer) returns Integer
	return x
end 'ident'

function main() returns ExitCode
	let args = CommandLine.args()
	let seed = try int.fromString(try args.get(1) otherwise "0") otherwise 0

	// Two values defined via opaque calls so the constant folder cannot
	// see through them. Each combines with a small set of distinct masks
	// using and/or/xor/cmp — the exact ALU mix that Phase 3 will eventually
	// fold into addRegMem / andRegMem / xorRegMem when the values spill.
	let v1 = ident(seed + 7)
	let v2 = ident(seed + 13)

	var acc = 0
	acc = acc + (v1 and 255)
	acc = acc + (v2 or 4096)
	acc = acc + (v1 xor 21845)
	acc = acc + (v2 + 1)

	let masked = acc and 255
	return masked as ExitCode
end 'main'
```
```exitcode
116
```

<!-- test: many-call-crossing -->
<!-- Args: 0 -->
Phase 3 gating test: a function with several i64 values live across
multiple helper calls inside a loop body. Pre-Phase-3, every spilled-
source use of a reg-reg ALU op materializes a fresh reload vreg; with
enough such uses chained in close proximity the reload-vreg pressure
exhausts the GPR pool and the allocator panics. With Phase 3's
`tryFoldReadFromSlot` plumbed through `SpillCodeInsertion`, the reg-reg
ALU ops rewrite themselves to read directly from the spill slot
(`addRegMem`, `xorRegMem`, etc.) — fold-eligible source uses no longer
mint a reload vreg, and the in-block reload pressure stays below the
GPR pool size.

What this test gates:

1. The fold dispatcher (`X64RegAllocTarget.tryFoldReadFromSlot`) accepts
   the addRegReg / subRegReg / andRegReg / orRegReg / xorRegReg /
   imulRegReg / cmpRegReg / movRegReg shapes and produces the
   corresponding `*Mem` variant.
2. The capability flag `TargetRegAlloc.hasMemOperandAlu` reaches the
   spill code insertion path through `desc`, so `processOp` actually
   probes the fold on x64.
3. The Pass A / Pass B refactor in `insertSpillCode` (which lets a
   future memory-routed-phi optimization run before block-arg
   spill-stores are emitted) preserves correct behavior on a non-trivial
   program with branches and a loop.
4. The per-block reload-vreg cache reuses the same fresh reload vreg
   across consecutive in-block uses of the same spilled value
   (invalidated at calls and defs), trimming the simultaneous-live
   reload set in tight write chains.

The exit code is the deterministic value `total mod 256` after four
iterations of the loop body.

```maxon
typealias Integer = int(i64.min to i64.max)

function ident(x Integer) returns Integer
	return x
end 'ident'

function add2(a Integer, b Integer) returns Integer
	return a + b
end 'add2'

function main() returns ExitCode
	let args = CommandLine.args()
	let seed = try int.fromString(try args.get(1) otherwise "0") otherwise 0

	// 6 working values live across the loop body. Each `ident`-produced
	// value is opaque to the constant folder, so all 6 land in the live
	// set crossing the helper calls below. The body's reg-reg ALU mix
	// is the fold-eligible shape that Phase 3's tryFoldReadFromSlot
	// rewrites into `*Mem` variants when its source operand spills.
	let v1 = ident(seed + 1)
	let v2 = ident(seed + 2)
	let v3 = ident(seed + 3)
	let v4 = ident(seed + 4)
	let v5 = ident(seed + 5)
	let v6 = ident(seed + 6)

	var total = 0
	var i = 0
	while i < 4 'loop'
		// Each iteration's ALU mix uses every v_i. Spilled-source uses
		// of these reg-reg forms are exactly the fold-eligible shape for
		// Phase 3.
		total = total + add2(v1, b: v2)
		total = total + (v3 xor v4)
		total = total + (v5 and v6)
		total = total + (v1 + v3 + v5)
		total = total + (v2 or v4 or v6)
		i = i + 1
	end 'loop'

	let masked = total and 255
	return masked as ExitCode
end 'main'
```
```exitcode
116
```

<!-- test: eviction-required -->
<!-- Args: 0 -->
Phase 4 gating test: forces the colorer into a scenario where greedy
first-fit assigns cold values to limited callee-saved registers first,
then a hot loop body's higher-spill-weight values can't find callee-
saved slots and either spill or get evicted. With Phase 4's eviction
fixup, the high-weight reloads displace the cold occupants of the
callee-saved tier instead, and the spill picker only has to spill the
cold values whose use sites are outside the hot loop body.

What this test gates:

1. `SSAColoring.runEvictionFixup` runs after first-fit and only fires
   on infinite-weight reload vregs (normal-weight uncolored ranges
   fall through to the spill loop unchanged — that's the
   "no eviction needed → identical to baseline" invariant).
2. `LiveRange.cascade` is loaded from `FunctionRegAllocator.cascadeByValueId`
   on every liveness rebuild, so evictee cascade bumps survive across
   spill iterations and the MAX_CASCADE bound provably terminates the
   PQ-driven eviction loop.
3. `pickEvictionVictim` rejects fixed-reg and infinite-weight neighbors
   so the freed register goes to a range the spill loop can actually
   re-color or spill (no panic-into-panic).
4. The picker class-safety check still rejects caller-saved register
   freed by eviction when the evictor is a call-crossing range — the
   evicted neighbor was caller-saved-OK but the evictor isn't.

The exit code is the deterministic sum-mod-256 of the program output.

```maxon
typealias Integer = int(i64.min to i64.max)

function opaque(x Integer) returns Integer
	return x + 0
end 'opaque'

function combine(a Integer, b Integer, c Integer) returns Integer
	return (a xor b) + c
end 'combine'

function main() returns ExitCode
	let args = CommandLine.args()
	let seed = try int.fromString(try args.get(1) otherwise "0") otherwise 0

	// 6 cold values defined early via opaque calls — the constant folder
	// cannot see through `opaque` so each lives as a real working scalar.
	// Each is used once at the end of the function, so its spill weight
	// is low and its live range is long (spans the entire body).
	let c1 = opaque(seed + 100)
	let c2 = opaque(seed + 200)
	let c3 = opaque(seed + 300)
	let c4 = opaque(seed + 400)
	let c5 = opaque(seed + 500)
	let c6 = opaque(seed + 600)

	// Hot inner loop: 4 high-weight values that benefit from being in
	// callee-saved registers (they survive the inner `combine` call).
	// In total the loop body keeps 4 hot scalars live across each call.
	var sum = 0
	var i = 0
	while i < 10 'hot'
		let h1 = combine(seed, b: i, c: 1)
		let h2 = combine(seed, b: i, c: 2)
		let h3 = combine(seed, b: i, c: 3)
		let h4 = combine(seed, b: i, c: 4)
		sum = sum + h1 + h2 + h3 + h4
		i = i + 1
	end 'hot'

	let cold = c1 + c2 + c3 + c4 + c5 + c6
	let total = sum + cold
	let masked = total and 255
	return masked as ExitCode
end 'main'
```
```exitcode
76
```

<!-- test: scheduler-pressure -->
<!-- Args: 0 -->
Phase 5 gating test: forces the bottom-up list scheduler to make a
pressure-vs-critical-path choice. `kernel` defines four "early" values
(a, b, c, d) at the top of its body, then runs a long chain of
intermediate computations that do not reference a..d, then consumes
all four at the end. The kernel body is a single basic block so all
the reordering decisions happen within one ready set.

What this test gates:

1. With the pre-Phase-5 scheduler, the def-only ops that mint a..d
   carry critical-path weight roughly equal to the intermediate
   chain's ops, so the bottom-up scheduler tends to pick them early
   in bottom-up order (= late in top-down order, near the consume
   point) only when their critical paths dominate. Many shapes
   instead leave a..d at the top of the schedule, where they pin
   four registers across the whole intermediate phase.
2. Phase 5 splits `selectBestReady` into explicit high-pressure and
   low-pressure modes. At/above `pressureThreshold` the picker
   prefers ops whose pressure-delta is most negative: a..d's def-only
   ops have delta +1 (one def, nothing dies), the intermediate
   chain's `(prev op '+' k)`-style steps have delta 0 (one def,
   one use that dies). Bottom-up, the picker therefore prefers the
   intermediate chain over the a..d defs, which defers the a..d
   defs toward the bottom of the block (top-down: closer to the
   consume point). Peak live count drops by approximately the
   number of deferred defs.
3. `estimatePressureDelta` correctly handles ops whose use list
   contains the same valueId multiple times (e.g. `x xor x`) — the
   value dies once regardless of how many slots read it. Without
   the dedup, such ops would over-credit pressure relief and the
   picker would mis-rank them.

The exit code is the deterministic `(a + b + c + d + i12) mod 256`
for seed = 0.

```maxon
typealias Integer = int(i64.min to i64.max)

function opaque(x Integer) returns Integer
	return x + 0
end 'opaque'

function kernel(seed Integer) returns Integer
	// Four "early" values, defined at the top of the source. Each is
	// a small computation rather than a call so the scheduler can
	// freely reorder them — calls would form a barrier chain that
	// pins relative ordering.
	let a = (seed xor 305419896) + 1
	let b = (seed xor 2271560481) + 2
	let c = (seed xor 3735928559) + 3
	let d = (seed xor 4275878552) + 4

	// Long intermediate chain that does not reference a..d. Each
	// step has pressure delta 0 (defines one value, consumes one
	// previous value that then dies). A pressure-aware scheduler
	// can freely interleave these with the deferred a..d defs.
	let i1 = (seed + 17) xor 11
	let i2 = (i1 * 3) + 5
	let i3 = (i2 xor 12345) + 7
	let i4 = (i3 * 5) + 9
	let i5 = (i4 xor 54321) + 11
	let i6 = (i5 * 7) + 13
	let i7 = (i6 xor 98765) + 15
	let i8 = (i7 * 11) + 17
	let i9 = (i8 xor 13579) + 19
	let i10 = (i9 * 13) + 21
	let i11 = (i10 xor 24680) + 23
	let i12 = (i11 * 17) + 25

	// Consume a..d together with the intermediate chain's final
	// value. The naive schedule keeps a..d live across the i1..i12
	// chain; the pressure-aware schedule defers their defs to here.
	return a + b + c + d + i12
end 'kernel'

function main() returns ExitCode
	let args = CommandLine.args()
	let seed = try int.fromString(try args.get(1) otherwise "0") otherwise 0
	let result = kernel(seed)
	// Mask to 0..127 so the value fits both POSIX (0..255) and wasi
	// (0..125 strict) exit-code ranges. The test gates on the
	// deterministic value being produced, not on its specific bit width.
	let masked = result and 127
	return masked as ExitCode
end 'main'
```
```exitcode
61
```
