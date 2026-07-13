
---
feature: register-spill
status: selfhosted
keywords: [register-allocator, spill, reload, cold-spill, splitter, belady, rematerialization, live-range-splitting]
category: register-allocator
milestone: M5.3
---

# Cold-spill live-range splitting

## Documentation

The register allocator's pool is 14 GPRs. When a function needs more values in
registers than that at some program point, the **cold-spill live-range splitter**
(M5.3) relieves the pressure — but only where doing so is FREE, i.e. COLD. Where a
loop genuinely needs more registers than exist, that is a hot spill, and the compiler
refuses it (the `E5001` diagnostic, M5.7); it does not silently emit slow code.

Three mechanisms, in order of preference:

### Rematerialization (constants are free)

An integer constant (`movRegImm` / `movRegImm32`) that would otherwise occupy a
register across a high-pressure region is **rematerialized** — re-emitted at each use
with a fresh value, never held in a register across the gap and never stored to
memory. It is always preferred over spilling a real value, because it costs nothing.

### Cold spilling around a loop (the signature case)

A value that is **idle across a loop** — live across it but never used or defined
inside it — is split out: a `storeSlotReg` in the loop's PREHEADER, a `loadRegSlot`
after the loop at its next use, and **nothing added to the loop body**. So a loop that
carries fifteen unrelated values through it pays no per-iteration cost for them; they
sit in memory across the loop and return to registers only where they are used.

This rests on two invariants (design: `maxon-shv2/ARCHITECTURE.md`, register allocator, Rules 1 + 2):
- **Rule 2** — a store or reload is placed only where, for every loop containing it,
  the value has no use or def in that loop. So spill code never lands in a loop body
  for a value that loop uses.
- **Dominating reloads** — every reload is placed in the use's own block (or before
  the loop), which DOMINATES the uses it serves, so no phi is ever needed and the
  program stays SSA. Each reload defines a fresh value; the uses are rewritten to it.

### Depth-0 Belady eviction (straight-line pressure)

In straight-line (loop-free) code, when more than fourteen values are live at once,
the splitter evicts by **Belady / MIN — farthest next use**: the value used furthest
in the future is stored, and reloaded before its next use. The store is placed only
where its own live set fits the pool, so a store never itself overflows.

### Splitting at the eviction point

A value's live range is split **at the peak**, not shipped whole to memory. The store goes
at the value's **def**; the reload goes before its next use *after* the peak, and only the
after-peak uses are rewritten to the reload — the before-peak uses keep the original value
in its register. This makes the value **dead across the peak**, which is what frees the
register there and actually lowers peak pressure. A value used both before and after a peak
is the case that forces this: reloading before its *first* use would leave the reloaded
value spanning the peak, so pressure would never drop.

**The store anchors at the DEF because that is the only point guaranteed to DOMINATE every
reload** (by SSA, Rule 1). Anchoring it after the value's last *before-peak use* — which
merely *precedes* the peak in layout order — is unsound: layout order is not dominance, so
that use can sit in a block (the `then` arm of an `if`) that never runs on the path reaching
the reload, leaving the slot unwritten; and if the use is a branch-edge **arg**, its anchor
op is the block's terminator, which a store cannot follow at all. Anchoring at the def also
emits strictly better code — a value defined outside a loop but used inside it before the
loop's peak stores **once**, rather than every iteration.

### A remat must kill the value at the peak, exactly as a spill must

Rematerialization partitions a value's uses around the peak the same way a spill does: the
after-peak uses are re-emitted as fresh copies, the before-peak uses keep the original. So
the original is dead across the peak under precisely the same condition, and remat is gated
on the same `killsValueAtPeak` test. A constant whose only use *precedes* the peak in layout
order but which is live across it **around a back edge** re-emits nothing, relieves nothing,
and would be re-picked forever.

### Multiple splits across disjoint peaks

A value may be live across **several** disjoint high-pressure peaks, with uses between
them — an accumulator computed early, read after peak 1, and read again after peak 2. It
must be in memory across *each* peak and in a register at *each* use between them, which
takes **more than one split**. The splitter does this incrementally: it relieves the
worst peak, splitting the value out — a store before the peak and a fresh **reload** id
serving the uses after it. That reload is then an ordinary value, so when a *later* peak
is relieved the reload may itself be split again (a fresh store + a fresh reload), one
store/reload per peak the lineage crosses. Each split cuts a strictly smaller run of
uses, so the process terminates (the over-pool pressure Σ strictly decreases every
split). The "spill a value at most once" guard is **per value id** — it stops a single id
being stored twice — and never blocks this, because every split works on a fresh reload
id, not the original.

### Fixed-register points reduce the pool locally

A **call** clobbers every caller-saved register, so a value live *across* a call can only
sit in one of the five callee-saved registers — the effective pool at the call is the
callee-saved subset, not the nominal fourteen. An **idiv** reserves `RAX`/`RDX`, so values
live across it are confined to `pool ∖ {RAX, RDX}`. The splitter treats each such point as
a pressure point against its **reduced pool** (`pool ∖ implicitDefs`) and spills the excess
values *across* the point — a store before it and a reload after — exactly the cold-spill
machinery. So `maxPressure ≤ pool` is necessary but not sufficient; the splitter checks each
op against its own reduced pool.

A store→slot→reload chain that does not preserve value identity — a mis-targeted store,
a wrong-slot reload, a reload of a slot nothing wrote — hands a use the wrong value, so
the program computes the wrong answer and the exit-code assertion below fails. The
committed `.test` goldens carry the other half: they pin *how many* stores and reloads
each function emits and *where*, so a spill that leaks into a loop body, or a reload
that reappears at every use, fails as a golden mismatch even though the answer is still
right.

## Tests

<!-- test: idle-across-loop -->
Fifteen values (`k1`..`k15`) are computed before a loop and summed after it, but none
is touched inside the loop. Register pressure across the loop exceeds the pool, so the
splitter stores several of them in the PREHEADER and reloads them after the loop — the
`loop` body stays exactly `sum = sum + i; i = i + 1`, with no spill code. Result is
`sum(0..3)=6 + sum(1..15)=120 = 126`.
```maxon
function idleAcrossLoop(p int) returns int
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
	let k13 = p + 13
	let k14 = p + 14
	let k15 = p + 15
	var sum = 0
	var i = 0
	while i < 4 'loop'
		sum = sum + i
		i = i + 1
	end 'loop'
	return sum + k1 + k2 + k3 + k4 + k5 + k6 + k7 + k8 + k9 + k10 + k11 + k12 + k13 + k14 + k15
end 'idleAcrossLoop'

function main() returns ExitCode
	return idleAcrossLoop(0)
end 'main'
```
```exitcode
126
```

<!-- test: straight-line-belady -->
Eighteen live values with no loop: depth-0 Belady eviction stores the values used
furthest in the future and reloads each right before its use. Result is
`sum(1..18) = 171`.
```maxon
function straightLine(p int) returns int
	let a1 = p + 1
	let a2 = p + 2
	let a3 = p + 3
	let a4 = p + 4
	let a5 = p + 5
	let a6 = p + 6
	let a7 = p + 7
	let a8 = p + 8
	let a9 = p + 9
	let a10 = p + 10
	let a11 = p + 11
	let a12 = p + 12
	let a13 = p + 13
	let a14 = p + 14
	let a15 = p + 15
	let a16 = p + 16
	let a17 = p + 17
	let a18 = p + 18
	return a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15 + a16 + a17 + a18
end 'straightLine'

function main() returns ExitCode
	return straightLine(0)
end 'main'
```
```exitcode
171
```

<!-- test: rematerialized-constant -->
The constant `c` is used as a division divisor (which needs a register — it cannot be
an immediate) and is live across fourteen other values, so it would push the pool over
its limit. Instead of spilling it, the splitter REMATERIALIZES it: the fragment
re-emits `movRegImm32 …, 7` right before the `idiv`, with NO stack slot for it. Result
is `sum(1..14)/7 = 105/7 = 15`.
```maxon
function rematConstant(p int) returns int
	let c = 7
	let a1 = p + 1
	let a2 = p + 2
	let a3 = p + 3
	let a4 = p + 4
	let a5 = p + 5
	let a6 = p + 6
	let a7 = p + 7
	let a8 = p + 8
	let a9 = p + 9
	let a10 = p + 10
	let a11 = p + 11
	let a12 = p + 12
	let a13 = p + 13
	let a14 = p + 14
	let s = a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14
	return s / c
end 'rematConstant'

function main() returns ExitCode
	return rematConstant(0)
end 'main'
```
```exitcode
15
```

<!-- test: used-before-and-after-peak -->
`a1` is used BEFORE a high-pressure peak (`w = a1 + p`) and AGAIN after it (`z = a1 + peak`),
while fifteen other values (`b1`..`b15`) are live at the peak. The splitter splits `a1`'s live
range AT the peak — storing it at its DEF and reloading it before its post-peak use,
rewriting only the post-peak use — so `a1` is dead across the peak (freeing its register there)
and the pre-peak use keeps the original value. (Reloading before the FIRST use instead would
leave the reloaded value spanning the peak, so pressure would never drop and the splitter could
not converge — the bug this guards.) With `p = 0`: `a1 = 1`, `w = 1`, `peak = (1+2+…+15) + 1 =
121`, `z = 1 + 121 = 122`.
```maxon
function usedBeforeAndAfter(p int) returns int
	let a1 = p + 1
	let w = a1 + p
	let b1 = p + 1
	let b2 = p + 2
	let b3 = p + 3
	let b4 = p + 4
	let b5 = p + 5
	let b6 = p + 6
	let b7 = p + 7
	let b8 = p + 8
	let b9 = p + 9
	let b10 = p + 10
	let b11 = p + 11
	let b12 = p + 12
	let b13 = p + 13
	let b14 = p + 14
	let b15 = p + 15
	let peak = b1 + b2 + b3 + b4 + b5 + b6 + b7 + b8 + b9 + b10 + b11 + b12 + b13 + b14 + b15 + w
	let z = a1 + peak
	return z
end 'usedBeforeAndAfter'

function main() returns ExitCode
	return usedBeforeAndAfter(0)
end 'main'
```
```exitcode
122
```

<!-- test: six-values-live-across-call -->
Six values `a1`..`a6` are all live ACROSS a call to `sink`, but only five callee-saved registers
survive a call — so the nominal pool of fourteen is not the binding constraint; the reduced pool
of five AT the call is. The splitter treats the call as a pressure point against `pool ∖
caller-saved` and spills one value ACROSS the call: a `storeSlotReg` BEFORE the call and a
`loadRegSlot` AFTER it, with nothing added inside any loop. Without this the colorer would run out
of callee-saved registers and panic. `caller(0) = (1+2+3+4+5+6) + sink(0) = 21 + 0 = 21`.
```maxon
function sink(x int) returns int
	return x
end 'sink'

function caller(p int) returns int
	let a1 = p + 1
	let a2 = p + 2
	let a3 = p + 3
	let a4 = p + 4
	let a5 = p + 5
	let a6 = p + 6
	let r = sink(p)
	return a1 + a2 + a3 + a4 + a5 + a6 + r
end 'caller'

function main() returns ExitCode
	return caller(0)
end 'main'
```
```exitcode
21
```

<!-- test: reuse-transient-overflow -->
At the two-address `d = a - rhs`, fourteen values are live entering the op (raw pressure equals
the pool) AND the reuse input `a` OUTLIVES it — so the colorer materializes a `mov dest, a` before
it, one extra register the peak-finder must count. The peak-finder adds the same reuse-copy
transient the liveness pass folds into `maxPressure`, so it and the driver guard agree: the op is
found as the peak and one value crossing it is spilled. Without the transient in the peak-finder
the function would enter the splitter loop yet find no raw peak, and panic. `p = 0`: `a = 100`,
`rhs = 50`, `d = 50`, and `(2+3+…+13) + d + a = 90 + 50 + 100 = 240`.
```maxon
function reuseTransientOverflow(p int) returns int
	let a = p + 100
	let b1 = p + 2
	let b2 = p + 3
	let b3 = p + 4
	let b4 = p + 5
	let b5 = p + 6
	let b6 = p + 7
	let b7 = p + 8
	let b8 = p + 9
	let b9 = p + 10
	let b10 = p + 11
	let b11 = p + 12
	let b12 = p + 13
	let rhs = p + 50
	let d = a - rhs
	let s = b1 + b2 + b3 + b4 + b5 + b6 + b7 + b8 + b9 + b10 + b11 + b12 + d + a
	return s
end 'reuseTransientOverflow'

function main() returns ExitCode
	return reuseTransientOverflow(0)
end 'main'
```
```exitcode
240
```

<!-- test: multi-peak-two -->
`a` (= 100) is live across TWO disjoint pressure peaks — the `b`-sum and the `c`-sum, each
of which alone exceeds the pool — and is read BETWEEN them (`u1 = a + s1`) and AFTER the
second (`u2 = a + s2`). So `a` must be split out around *both* peaks: stored, reloaded for
`u1`, and (the reload) split again around the second peak, reloaded for `u2`. `s1 = s2 =
sum(1..13) = 91`, `u1 = u2 = 191`, so `f(0) = 382`. `main` returns `0` iff `f` computes it —
a wrong value (the historical multi-peak miscompile computed ≈ 226 here) returns `99`
instead. (The self-check avoids the exit-code's mod-256 wrap: `382 & 0xFF` is `126`, which
would masquerade as a plausible direct exit code.)
```maxon
function f(p int) returns int
	let a = p + 100
	let b1 = p + 1
	let b2 = p + 2
	let b3 = p + 3
	let b4 = p + 4
	let b5 = p + 5
	let b6 = p + 6
	let b7 = p + 7
	let b8 = p + 8
	let b9 = p + 9
	let b10 = p + 10
	let b11 = p + 11
	let b12 = p + 12
	let b13 = p + 13
	let s1 = b1 + b2 + b3 + b4 + b5 + b6 + b7 + b8 + b9 + b10 + b11 + b12 + b13
	let u1 = a + s1
	let c1 = p + 1
	let c2 = p + 2
	let c3 = p + 3
	let c4 = p + 4
	let c5 = p + 5
	let c6 = p + 6
	let c7 = p + 7
	let c8 = p + 8
	let c9 = p + 9
	let c10 = p + 10
	let c11 = p + 11
	let c12 = p + 12
	let c13 = p + 13
	let s2 = c1 + c2 + c3 + c4 + c5 + c6 + c7 + c8 + c9 + c10 + c11 + c12 + c13
	let u2 = a + s2
	return u1 + u2
end 'f'

function main() returns ExitCode
	let r = f(0)
	if r == 382 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
```
```exitcode
0
```

<!-- test: multi-peak-three -->
`a` (= 100) is live across THREE disjoint peaks (`b`, `c`, `d` sums) and read after each
(`u1`, `u2`, `u3`), so its reload lineage is split three times — once per peak. `s1 = s2 =
s3 = 91`, `u1 = u2 = u3 = 191`, `f(0) = 573`; `main` returns `0` iff correct.
```maxon
function f(p int) returns int
	let a = p + 100
	let b1 = p + 1
	let b2 = p + 2
	let b3 = p + 3
	let b4 = p + 4
	let b5 = p + 5
	let b6 = p + 6
	let b7 = p + 7
	let b8 = p + 8
	let b9 = p + 9
	let b10 = p + 10
	let b11 = p + 11
	let b12 = p + 12
	let b13 = p + 13
	let s1 = b1 + b2 + b3 + b4 + b5 + b6 + b7 + b8 + b9 + b10 + b11 + b12 + b13
	let u1 = a + s1
	let c1 = p + 1
	let c2 = p + 2
	let c3 = p + 3
	let c4 = p + 4
	let c5 = p + 5
	let c6 = p + 6
	let c7 = p + 7
	let c8 = p + 8
	let c9 = p + 9
	let c10 = p + 10
	let c11 = p + 11
	let c12 = p + 12
	let c13 = p + 13
	let s2 = c1 + c2 + c3 + c4 + c5 + c6 + c7 + c8 + c9 + c10 + c11 + c12 + c13
	let u2 = a + s2
	let d1 = p + 1
	let d2 = p + 2
	let d3 = p + 3
	let d4 = p + 4
	let d5 = p + 5
	let d6 = p + 6
	let d7 = p + 7
	let d8 = p + 8
	let d9 = p + 9
	let d10 = p + 10
	let d11 = p + 11
	let d12 = p + 12
	let d13 = p + 13
	let s3 = d1 + d2 + d3 + d4 + d5 + d6 + d7 + d8 + d9 + d10 + d11 + d12 + d13
	let u3 = a + s3
	return u1 + u2 + u3
end 'f'

function main() returns ExitCode
	let r = f(0)
	if r == 573 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
```
```exitcode
0
```

<!-- test: multi-peak-two-values -->
TWO independent values `a` (= 100) and `e` (= 50) are BOTH live across BOTH peaks and read
between and after each — so the splitter maintains two separate reload lineages at once,
each split around both peaks. `u1 = a + s1 = 191`, `w1 = e + s1 = 141`, `u2 = 191`, `w2 =
141`, `f(0) = 664`; `main` returns `0` iff correct.
```maxon
function f(p int) returns int
	let a = p + 100
	let e = p + 50
	let b1 = p + 1
	let b2 = p + 2
	let b3 = p + 3
	let b4 = p + 4
	let b5 = p + 5
	let b6 = p + 6
	let b7 = p + 7
	let b8 = p + 8
	let b9 = p + 9
	let b10 = p + 10
	let b11 = p + 11
	let b12 = p + 12
	let b13 = p + 13
	let s1 = b1 + b2 + b3 + b4 + b5 + b6 + b7 + b8 + b9 + b10 + b11 + b12 + b13
	let u1 = a + s1
	let w1 = e + s1
	let c1 = p + 1
	let c2 = p + 2
	let c3 = p + 3
	let c4 = p + 4
	let c5 = p + 5
	let c6 = p + 6
	let c7 = p + 7
	let c8 = p + 8
	let c9 = p + 9
	let c10 = p + 10
	let c11 = p + 11
	let c12 = p + 12
	let c13 = p + 13
	let s2 = c1 + c2 + c3 + c4 + c5 + c6 + c7 + c8 + c9 + c10 + c11 + c12 + c13
	let u2 = a + s2
	let w2 = e + s2
	return u1 + u2 + w1 + w2
end 'f'

function main() returns ExitCode
	let r = f(0)
	if r == 664 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
```
```exitcode
0
```

<!-- test: multi-peak-across-call -->
`a` (= 100) crosses a straight-line peak (the `b`-sum, forcing it out) AND a `sink` CALL,
whose reduced pool of five callee-saved registers is the binding constraint. `a` is read
after the peak (`u1 = a + s1`) and after the call (`u2 = a + d… + r`), so its reload must
survive — or be split again — across the call. `s1 = sum(1..14) = 105`, `u1 = 205`; `u2 =
100 + sum(1..6) + sink(0) = 121`; `f(0) = 326`; `main` returns `0` iff correct.
```maxon
function sink(x int) returns int
	return x
end 'sink'

function f(p int) returns int
	let a = p + 100
	let b1 = p + 1
	let b2 = p + 2
	let b3 = p + 3
	let b4 = p + 4
	let b5 = p + 5
	let b6 = p + 6
	let b7 = p + 7
	let b8 = p + 8
	let b9 = p + 9
	let b10 = p + 10
	let b11 = p + 11
	let b12 = p + 12
	let b13 = p + 13
	let b14 = p + 14
	let s1 = b1 + b2 + b3 + b4 + b5 + b6 + b7 + b8 + b9 + b10 + b11 + b12 + b13 + b14
	let u1 = a + s1
	let d1 = p + 1
	let d2 = p + 2
	let d3 = p + 3
	let d4 = p + 4
	let d5 = p + 5
	let d6 = p + 6
	let r = sink(p)
	let u2 = a + d1 + d2 + d3 + d4 + d5 + d6 + r
	return u1 + u2
end 'f'

function main() returns ExitCode
	let r = f(0)
	if r == 326 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
```
```exitcode
0
```

<!-- test: remat-constant-live-only-around-the-back-edge -->
A REMAT victim must be killed at the peak, exactly as a spill victim must — and this is the
program that proves it. The divisor `c` is a constant that needs a register (an `idiv` divisor
cannot be an immediate), and its ONLY use is the loop header's `i mod c`, which sits BEFORE the
loop body's call in layout order. But the header is re-entered around the BACK EDGE, so `c` is
live across the body's peak all the same.

Rematerializing `c` there re-emits it before each use AFTER the peak — and it has none. So the
old splitter emitted nothing, dropped nothing, relieved nothing, and the next iteration re-picked
the same value: it spun until the runaway bound panicked ("did not converge after 1416 splits").
`isRematVictim` now applies the same `killsValueAtPeak` gate the spill path has, so `c` is
refused and the peak is relieved by a forced bracket on an accumulator instead.

`loopDivisor(1)`: `a1..a6 = 2..7`, and the loop runs for `i = 0, 1, 2` (it exits at `i = 3`,
where `3 mod 7 = 3` is not `< 3`), leaving `a1..a6 = 8, 19, 40, 76, 133, 218` — a sum of 494.
```maxon
function leaf(x int) returns int
	return x + 1
end 'leaf'

function loopDivisor(p int) returns int
	let c = 7
	var a1 = p + 1
	var a2 = p + 2
	var a3 = p + 3
	var a4 = p + 4
	var a5 = p + 5
	var a6 = p + 6
	var i = 0
	while i mod c < 3 'loop'
		a1 = a1 + leaf(i)
		a2 = a2 + a1
		a3 = a3 + a2
		a4 = a4 + a3
		a5 = a5 + a4
		a6 = a6 + a5
		i = i + 1
	end 'loop'
	return a1 + a2 + a3 + a4 + a5 + a6
end 'loopDivisor'

function main() returns ExitCode
	let r = loopDivisor(1)
	if r == 494 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
```
```exitcode
0
```

<!-- test: forced-spill-with-edge-arg-before-the-peak -->
THE STORE ANCHOR IS THE DEF, and this program is why. `t` is used once BEFORE the call's peak —
as the branch-edge ARG the `then` arm passes to `m`'s merge phi — and once after it, in the
return sum. The old splitter anchored a store "after the value's last before-peak use", which
here is an EDGE arg whose anchor op is the block's TERMINATOR: a store cannot be spliced after a
terminator, and the splitter panicked outright (`positionInBlock: op N not in block 'br' opRefs`).

Anchoring at the last before-peak use was unsound even when it did not crash: the `then` block
does not DOMINATE the merge, so the store would never run on the `else` path and the reload after
the call would read an unwritten slot. The def dominates every use of a value by SSA (Rule 1), so
it is the only anchor that always dominates every reload.

`edgeAnchor(1, 2)`: `t = 3`, `a1..a8 = 2..9` (sum 44), `p > 0` so `m = t = 3`, and `r = leaf(1) =
2` — giving `44 + 3 + 2 + 3 = 52`.
```maxon
function leaf(x int) returns int
	return x + 1
end 'leaf'

function edgeAnchor(p int, q int) returns int
	let t = p + q
	var a1 = p + 1
	var a2 = p + 2
	var a3 = p + 3
	var a4 = p + 4
	var a5 = p + 5
	var a6 = p + 6
	var a7 = p + 7
	var a8 = p + 8
	var m = 0
	if p > 0 'br'
		m = t
	end 'br'
	let r = leaf(p)
	return a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + m + r + t
end 'edgeAnchor'

function main() returns ExitCode
	let v = edgeAnchor(1, q: 2)
	if v == 52 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
```
```exitcode
0
```

<!-- test: values-confined-by-different-calls -->
The reduced pool at a call is a property of the VALUE, not of the call — a value live across a
call is forbidden the 9 caller-saved registers for its *whole* live range, not merely at the call
op. So the confinement can bite at a point that is **not itself a call**, and the peak-finder must
be able to see it there.

Here each of `v1`..`v6` is used AFTER a `sink` call in its OWN arm of the `else if` chain, so each
is live across a DIFFERENT call and each is confined to the five callee-saved registers. Liveness
is path-sensitive, so no single call has more than one of them live across it — every reduced-pool
test AT a clobber op passes. Yet all six are simultaneously live at the chain's first `cmp`, where
the nominal pool is the full fourteen and nothing looks wrong at all. Six values, five registers:
the point does not colour.

This is why the splitter tests HALL'S CONDITION on the per-value effective pools rather than the
live count against one number: no register subset may hold more values confined to it than it has
registers. The witness here is the callee-saved subset, at an op that clobbers nothing. One value
is relieved with a forced store/reload bracket — which is NOT E5001: the program fits the machine,
and the bracket is the placement the ABI forces.

Before the Hall test the splitter relieved nothing here and the colorer died with every register
blocked (`chooseRegister: no free register for value 12 (blocked mask 65535)`).

`pick(k) = (k + 11k) + sink(k) = k + 12k`, so the six arms give 13, 26, 39, 52, 65, 78 — every one
of the six confined values is read on some path, and all six must survive.
```maxon
function sink(x int) returns int
	return x
end 'sink'

function pick(k int) returns int
	let v1 = k + 11
	let v2 = k + 22
	let v3 = k + 33
	let v4 = k + 44
	let v5 = k + 55
	let v6 = k + 66
	var out = 0
	if k == 1 'b1'
		let t = sink(1)
		out = v1 + t
	end 'b1' else if k == 2 'b2'
		let t = sink(2)
		out = v2 + t
	end 'b2' else if k == 3 'b3'
		let t = sink(3)
		out = v3 + t
	end 'b3' else if k == 4 'b4'
		let t = sink(4)
		out = v4 + t
	end 'b4' else if k == 5 'b5'
		let t = sink(5)
		out = v5 + t
	end 'b5' else 'b6'
		let t = sink(6)
		out = v6 + t
	end 'b6'
	return out
end 'pick'

function main() returns ExitCode
	let total = pick(1) + pick(2) + pick(3) + pick(4) + pick(5) + pick(6)
	if total == 273 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
```
```exitcode
0
```
