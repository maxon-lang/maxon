---
feature: register-phi-copies
status: selfhosted
keywords: [register-allocator, ssa-destruction, parallel-copy, phi, block-arg, xchg, cycle, swap, permutation]
category: register-allocator
milestone: M5.14
---

# Parallel copies, copy cycles, and `xchg`

## Documentation

Coloring assigns a register to every value. SSA destruction then has to make the phi
model REAL: on each edge, every block-arg of the successor must end up holding the value
the predecessor's `branchEdge` names for it — *simultaneously*, because a phi is a
parallel assignment, not a sequence. `buildEdgePlan` reads the coloring off both sides
and hands `sequenceParallelCopy` a set of register moves `dsts[i] ← srcs[i]`.

Sequencing them is the classic parallel-move problem. Emit a LEAF first — a move whose
destination is nobody else's source, so writing it destroys nothing anyone still needs.
When only cycles remain, every destination is still somebody's source, and no move is
safe on its own. shv2 breaks the cycle with **`xchgRegReg`** and NO scratch register —
which is precisely why `r10`/`r11` are not reserved and stay in the allocatable pool.
After the swap the two registers hold each other's old contents, so every remaining
pending move that read either one must be REWRITTEN to read the other
(`rewriteSourcesThroughSwap`); a move so rewritten may become `r ← r` and is then
dropped, because the `xchg` already delivered it.

**Maxon has no tuple assignment, so a permutation is written with a temp** — `var t = a;
a = b; b = t`. That is not three copies in the emitted code. `a = b` and `b = t` are SSA
REBINDINGS: they emit no op, they just rename which value the variable denotes. What
reaches the allocator is a loop header carrying phis for `a` and `b`, and a back edge
whose arg vector is `(b, a)` — a genuine 2-cycle over the phis' registers. Nothing can
coalesce it away, either: `a` and `b` are both live at the header, so they interfere and
must hold different registers.

Every test in this file therefore forces a permutation across a back edge, and each is
SELF-VERIFYING — it encodes the permuted values positionally as decimal digits and
returns `0` only on an exact match, `99` otherwise. A swap that is dropped, half-applied,
or applied in the wrong order changes a digit.

### Why this file exists

Before it, **not one of the 88 committed goldens contained an `xchgRegReg`**. Nothing in
the corpus made a back edge carry a permutation, so `sequenceParallelCopy`'s cycle arm,
`rewriteSourcesThroughSwap`, `moveOpOf`'s swap branch, the encoder's `REX.W 87 /r`, and
the prologue's callee-saved scan over `xchg` operands had never once executed. The very
first program that did exercise them — `back-edge-swap` below, the smallest possible
input — miscompiled: the sequencer emitted the `xchg` and then ALSO emitted the cycle's
second move as if it were still pending, so the loop body was

```
x64.xchgRegReg rax, rcx      // swap a and b — correct, and complete on its own
x64.movRegReg rcx, rax       // clobbers b with a, undoing half the swap
```

and after one iteration both registers held `b`. The root cause was not in the sequencer
at all — `rewriteSourcesThroughSwap` was correct — but underneath it, in the bootstrap
compiler: `RegNumColumn` is an `Array with RegNum` where `RegNum` is `int(0 to 16)`, a
one-byte element, and `Array.clone()` (which `sequenceParallelCopy` calls on its pending
columns) returned a view that read those bytes EIGHT at a time. Every register number it
compared came back garbage, so the rewrite matched nothing and the stale move survived.
See `specs/array-clone-element-size.md`.

## Tests

<!-- test: back-edge-swap -->
The 2-CYCLE — the smallest input that must emit an `xchg`, and the one that caught the
bug above. `a` and `b` are both live at the loop header, so they interfere and hold
different registers; the back edge passes them to each other's phi. The whole parallel
copy is `reg(a) ← reg(b)`, `reg(b) ← reg(a)`, and a single `xchgRegReg` satisfies BOTH —
the sequencer must recognise the second move as already delivered and emit nothing more.
Three iterations (an ODD count, so the swap is observable — an even count would restore
the original and prove nothing): `(1,2)` → `(2,1)` → `(1,2)` → `(2,1)`, so `a*10 + b = 21`.
```maxon
function swapLoop(p Integer) returns Integer
	var a = p + 1
	var b = p + 2
	var i = 0
	while i < 3 'loop'
		let t = a
		a = b
		b = t
		i = i + 1
	end 'loop'
	return a * 10 + b
end 'swapLoop'

function main() returns ExitCode
	let r = swapLoop(0)
	if r == 21 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: back-edge-rotate-three -->
A 3-CYCLE. One `xchg` cannot discharge it: breaking `(a b c)` leaves a 2-cycle whose
source must be rewritten through the swap before it can be emitted. So this is the
smallest program that exercises `rewriteSourcesThroughSwap` producing a move that is
still PENDING (rather than immediately self-satisfied, as in the 2-cycle above).
`(a,b,c) ← (b,c,a)` over four iterations (4 is not a multiple of 3, so the rotation is
observable): `(1,2,3)` → `(2,3,1)` → `(3,1,2)` → `(1,2,3)` → `(2,3,1)`, so
`a*100 + b*10 + c = 231`.
```maxon
function rotate3(p Integer) returns Integer
	var a = p + 1
	var b = p + 2
	var c = p + 3
	var i = 0
	while i < 4 'loop'
		let t = a
		a = b
		b = c
		c = t
		i = i + 1
	end 'loop'
	return a * 100 + b * 10 + c
end 'rotate3'

function main() returns ExitCode
	let r = rotate3(0)
	if r == 231 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: back-edge-rotate-five -->
A 5-CYCLE — four chained `xchg`s and four successive rounds of
`rewriteSourcesThroughSwap`, each rewriting the sources of the moves that remain. An
off-by-one in that rewrite (updating only one direction of the swap, or using a stale
index after `removeMoveAt` shifts the columns) survives a 2-cycle and dies here.
`(a,b,c,d,e) ← (b,c,d,e,a)` over three iterations: `(1,2,3,4,5)` → `(2,3,4,5,1)` →
`(3,4,5,1,2)` → `(4,5,1,2,3)`, so the digits give
`4*10000 + 5*1000 + 1*100 + 2*10 + 3 = 45123`.
```maxon
function rotate5(p Integer) returns Integer
	var a = p + 1
	var b = p + 2
	var c = p + 3
	var d = p + 4
	var e = p + 5
	var i = 0
	while i < 3 'loop'
		let t = a
		a = b
		b = c
		c = d
		d = e
		e = t
		i = i + 1
	end 'loop'
	return a * 10000 + b * 1000 + c * 100 + d * 10 + e
end 'rotate5'

function main() returns ExitCode
	let r = rotate5(0)
	if r == 45123 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: cycle-plus-leaf-moves -->
A CYCLE AND A LEAF ON THE SAME EDGE, which pins the ORDER. `a`, `b`, `c` rotate — a
3-cycle — and `d` additionally takes `a`'s OLD value, so the edge's move set is
`phi_a ← reg(b)`, `phi_b ← reg(c)`, `phi_c ← reg(a)`, `phi_d ← reg(a)`. Nothing reads
`d`, so `phi_d` is nobody's source: it is the LEAF, and it must be emitted BEFORE the
cycle is broken. Emit the `xchg` first and `reg(a)` no longer holds the old `a`, so `d`
silently captures a post-swap value — a wrong answer, with no crash and no golden
anomaly beyond the move order itself.
Four iterations from `(1,2,3)` with `d` trailing `a`: `d` takes 1, 2, 3, 1 while
`(a,b,c)` goes `(2,3,1)` → `(3,1,2)` → `(1,2,3)` → `(2,3,1)`. Final `a=2, b=3, c=1, d=1`,
so `a*1000 + b*100 + c*10 + d = 2311`.
```maxon
function cycleAndLeaf(p Integer) returns Integer
	var a = p + 1
	var b = p + 2
	var c = p + 3
	var d = p
	var i = 0
	while i < 4 'loop'
		let t = a
		d = a
		a = b
		b = c
		c = t
		i = i + 1
	end 'loop'
	return a * 1000 + b * 100 + c * 10 + d
end 'cycleAndLeaf'

function main() returns ExitCode
	let r = cycleAndLeaf(0)
	if r == 2311 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: two-preds-different-permutations -->
TWO predecessors feeding ONE merge block with DIFFERENT permutations. The `then` arm
swaps `a` with `b`; the `else` arm swaps `b` with `c`. Both arms flow to the same merge,
whose phis for `a`, `b`, `c` therefore receive a different arg vector on each incoming
edge — two distinct parallel copies, each its own cycle, sequenced independently. Any
state the sequencer keeps per BLOCK rather than per EDGE (a cached plan, a reused
scratch, a shared pending column) cross-contaminates the two and one arm gets the other's
swap.
`i mod 2 == 0` alternates the arms. From `(1,2,3)` over `i = 0..3`:
`i=0` even, swap a/b → `(2,1,3)`; `i=1` odd, swap b/c → `(2,3,1)`; `i=2` even, swap a/b
→ `(3,2,1)`; `i=3` odd, swap b/c → `(3,1,2)`. So `a*100 + b*10 + c = 312`.
```maxon
function twoPerms(p Integer) returns Integer
	var a = p + 1
	var b = p + 2
	var c = p + 3
	var i = 0
	while i < 4 'loop'
		if i mod 2 == 0 'even'
			let t = a
			a = b
			b = t
		end 'even' else 'odd'
			let u = b
			b = c
			c = u
		end 'odd'
		i = i + 1
	end 'loop'
	return a * 100 + b * 10 + c
end 'twoPerms'

function main() returns ExitCode
	let r = twoPerms(0)
	if r == 312 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: permutation-under-pressure -->
A back-edge SWAP under enough pressure to force SPILLING. `k1`..`k12` are computed
before the loop and summed after it, and never touched inside, so the cold-spill splitter
lifts several of them out around the loop — while the loop itself carries a 2-cycle in
`a`/`b`. This is the one place the splitter's edge-arg rewriting and the sequencer's cycle
detection meet: a spilled value whose edge arg is repointed to a fresh reload id must
still produce the same permutation, and the spill code must land OUTSIDE the loop (Rule 2)
rather than in the body next to the `xchg`.
`p = 0`: three swaps leave `a = 2`, `b = 1`; `k1..k12` sum to 78. So
`a*100 + b*10 + 78 = 200 + 10 + 78 = 288`.
```maxon
function permutePressure(p Integer) returns Integer
	let k1 = p + 1
	let k2 = p + 2
	let k3 = p + 3
	let k4 = p + 4
	let k5 = p + 5
	let k6 = p + 6
	let k7 = p + 7
	let k8 = p + 8
	let k9 = p + 9
	let k10 = p + 10
	let k11 = p + 11
	let k12 = p + 12
	var a = p + 1
	var b = p + 2
	var i = 0
	while i < 3 'loop'
		let t = a
		a = b
		b = t
		i = i + 1
	end 'loop'
	return a * 100 + b * 10 + k1 + k2 + k3 + k4 + k5 + k6 + k7 + k8 + k9 + k10 + k11 + k12
end 'permutePressure'

function main() returns ExitCode
	let r = permutePressure(0)
	if r == 288 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: swap-across-call-callee-saved -->
The swapped pair is ALSO live across a CALL, so both values are forbidden the nine
caller-saved registers and the `xchg` operates on CALLEE-SAVED ones. That makes the
prologue's `usedCalleeSavedRegs` scan load-bearing over an `xchg`: the op has TWO register
operands and both must be counted, or the function pushes fewer registers than it
clobbers and stomps its caller's state on return.
`sink(1)` is `1`, so `i` advances by one and the loop runs three times; `a`/`b` are
loop-carried and therefore live across the call each iteration. From `(1,2)`, three swaps
give `a = 2`, `b = 1`, so `a*10 + b = 21`.
```maxon
function sink(x Integer) returns Integer
	return x
end 'sink'

function swapAcrossCall(p Integer) returns Integer
	var a = p + 1
	var b = p + 2
	var i = 0
	while i < 3 'loop'
		let t = a
		a = b
		b = t
		i = i + sink(1)
	end 'loop'
	return a * 10 + b
end 'swapAcrossCall'

function main() returns ExitCode
	let r = swapAcrossCall(0)
	if r == 21 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
