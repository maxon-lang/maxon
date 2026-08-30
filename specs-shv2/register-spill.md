
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

### When the split at the eviction point cannot cut the range: reload at every use

Splitting **at the peak** rewrites only the *after*-peak uses; the *before*-peak ones keep the
original value in its register. That is what makes the value dead across the peak — **unless a
before-peak use is still reachable *from* the peak**, which around a **back edge** it usually is. A
loop-invariant read both *before* and *after* a call inside the loop is live across that call on the
back edge no matter how its after-peak uses are rewritten, so the split at the eviction point
relieves nothing at all.

Six such invariants against the five callee-saved registers is not a program that exceeds the
machine — six values, fourteen registers — so it is emphatically **not** `E5001`, and it must not be
a compiler crash either. The ABI simply denies those values the nine caller-saved registers, and the
one placement that survives that is to **load the value before each use** and keep it in a register
only there. So the splitter widens the split: it rewrites **every** use, before-peak ones included,
leaving the original with no reader but its own store. The store still anchors at the **def** — once,
outside the loop — and the loop body pays one load per use. That is exactly the code an author would
hand-write, and it is the placement the ABI forces, not one the allocator searched for.

A **constant** in that position is relieved the same way but for free: it is re-emitted (`mov r, imm`)
before *every* use, with no slot, no store and no load. Rematerialization is preferred over spilling
here for the same reason it is preferred everywhere.

### A reload must not be born with nowhere to live

A reload serves a **run** of uses, so it is live from its own site to the last use of that run. If an
op inside that span denies the reload's whole register **file** — an f64 live across a call is
forbidden all sixteen XMMs, because every one of them is caller-saved — then the fresh reload comes
out of the split with an **empty** allowed set. It cannot be coloured anywhere, so the allocator is
obliged to find it at the next peak and split it *again*: the split that was meant to relieve the
value has minted its own next victim.

That cascade is **quadratic**, and it is invisible in the emitted code, which grows only linearly. Two
f64 locals read after each of N calls cost 2 splits per *call site* rather than 2 in total, each one
re-splitting the previous reload and writing it to a **fresh** stack slot. Measured on the two tests
below, against a compiler without the run-break: the two-call program emitted **4 stores into 4
slots** and the four-call program **8 into 8**, both for the same two values — the bracket count
tracking the call count instead of the value count. At N=128/256/512 the splitter's memory grew ×3.84
then ×3.98 per doubling, against ×2.0 for the same program with the two locals declared `int`.

So a run **breaks** at an op that would strand the value's file, and one split of the original then
mints every reload the value needs — none of them able to become a victim in its turn. The test is
"leaves this file **no** register", not "clobbers something this value could have used": an `int`
across a call keeps the five callee-saved registers, is coloured, and is never re-picked, so breaking
there would emit a reload per call and buy nothing.

The tests below pin the **shape**, not a timing. Doubling the number of call sites leaves the number
of stores and slots **unchanged** — two of each, one per local — while the reloads grow with the reads
that need them, which is the ABI's price and not the allocator's.

### A reload serving a call ARGUMENT must not be born inside the pre-move run

A call's arguments are placed by a run of plain **physical pre-moves** — `mov argReg[0], v0;
mov argReg[1], v1; …; call` — with no parallel-copy sequencer. Nothing in the compiler marks an
argument register live from its move to the call, so the allocator keeps the pre-moves apart by
forbidding each one's destination register to every value live **ACROSS** it. That covers every
value the lowering can produce, because the lowering emits the run with nothing between its
members.

**The splitter can put something between them.** A reload is anchored immediately before the
FIRST use of the run it serves, and that use may be an argument pre-move which is not the call's
first. The fresh reload value is then DEFINED between two pre-moves: it is live across neither,
so it is forbidden nothing, and the colorer may hand it an argument register an EARLIER pre-move
has already loaded. The callee then reads a clobbered argument — a silent wrong answer, with no
diagnostic and no crash where the mistake was made.

So the allocator records, for every value, **which physical registers were already established where
that value is DEFINED** — written by name ahead of its def and not yet destroyed
(`LivenessResult.establishedAtDef`, filled by `sweepEstablishedRegisters`). A register enters that
pending set at the op that writes it by name and leaves it at the first op that destroys it, which at
a call is the call's own clobber mask. The set is folded into the value's `forbidden` mask, so a
reload born between two pre-moves cannot be coloured into an argument register an earlier pre-move
already loaded. It costs nothing where no register is pending, which is every def in a program that
does not spill a call argument.

**The cure is a FORBID, not a different splice point, and the alternative was measured.** Backing a
defining splice out to the head of the run it would land inside makes *k* reloads serving *k*
different arguments all live at the run's head at once, so the pressure the split was relieving RISES
and the driver re-picks for ever (`splitLiveRanges: 'main' did not converge after 33938 splits`). A
forbid changes no live range and no pressure, so it cannot do that.

### What the RUN proves, and what the GOLDEN proves

Every test below is checked twice, and the two halves prove different things — neither substitutes
for the other.

**The run proves the allocation is CORRECT.** A store→slot→reload chain that does not preserve value
identity — a mis-targeted store, a wrong-slot reload, a reload of a slot nothing wrote, a value left
in a register the call it crosses clobbers — hands a use the wrong value, so the program computes the
wrong answer and the exit-code assertion fails. But the run only checks the code it actually EXECUTES:
a branch the single execution never enters is allocated, and its allocation is never tested. So each
`main` below drives **every path** through the function under test — all six arms of `pick`, both arms
of `edgeAnchor`'s `if` — and checks each path's result **alone**, on its own exit code, rather than
folding the paths into one number where two errors can cancel. (They can: in
`values-confined-by-different-calls` a *permuted* register assignment, the exact bug that test guards,
leaves the SUM of the six arms exactly correct.)

**The golden proves the allocation did not get WORSE.** The committed `.test` fragment pins *how many*
stores and reloads each function emits and *where*, so a spill that leaks into a loop body, or a reload
that reappears at every use, fails as a golden mismatch even though the answer is still right — a
regression the run cannot see, because slower code still computes the right value. The golden is also
the only check on code no run reaches (a compile-error path, an assertion's `return` arm). What it
cannot do is tell right from wrong: it pins what the compiler emitted, not what the program means. Only
the run does that, which is why the run must reach every branch it can.

## Tests

<!-- test: idle-across-loop -->
Fifteen values (`k1`..`k15`) are computed before a loop and summed after it, but none
is touched inside the loop. Register pressure across the loop exceeds the pool, so the
splitter stores several of them in the PREHEADER and reloads them after the loop — the
`loop` body stays exactly `sum = sum + i; i = i + 1`, with no spill code. Result is
`sum(0..3)=6 + sum(1..15)=120 = 126`.
```maxon
function idleAcrossLoop(p Integer) returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
126
```

<!-- test: straight-line-belady -->
Eighteen live values with no loop: depth-0 Belady eviction stores the values used
furthest in the future and reloads each right before its use. Result is
`sum(1..18) = 171`.
```maxon
function straightLine(p Integer) returns Integer
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
typealias Integer = int(i64.min to i64.max)
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
function rematConstant(p Integer) returns Integer
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
typealias Integer = int(i64.min to i64.max)
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
function usedBeforeAndAfter(p Integer) returns Integer
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
typealias Integer = int(i64.min to i64.max)
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
function sink(x Integer) returns Integer
	return x
end 'sink'

function caller(p Integer) returns Integer
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
typealias Integer = int(i64.min to i64.max)
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
function reuseTransientOverflow(p Integer) returns Integer
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
typealias Integer = int(i64.min to i64.max)
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
function f(p Integer) returns Integer
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: multi-peak-three -->
`a` (= 100) is live across THREE disjoint peaks (`b`, `c`, `d` sums) and read after each
(`u1`, `u2`, `u3`), so its reload lineage is split three times — once per peak. `s1 = s2 =
s3 = 91`, `u1 = u2 = u3 = 191`, `f(0) = 573`; `main` returns `0` iff correct.
```maxon
function f(p Integer) returns Integer
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
typealias Integer = int(i64.min to i64.max)
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
function f(p Integer) returns Integer
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
typealias Integer = int(i64.min to i64.max)
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
function sink(x Integer) returns Integer
	return x
end 'sink'

function f(p Integer) returns Integer
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
typealias Integer = int(i64.min to i64.max)
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
function leaf(x Integer) returns Integer
	return x + 1
end 'leaf'

function loopDivisor(p Integer) returns Integer
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
typealias Integer = int(i64.min to i64.max)
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

So `main` calls `edgeAnchor` on BOTH paths, because the path that does NOT take the branch is the one
the unsound anchor actually breaks: with `p <= 0` the `br` block never runs, so a store placed there
never executes, and the reload after the call reads a slot nothing wrote — a garbage `t` in the return
sum. A run that only ever passes `p > 0` executes the store and gets the right answer no matter where
the store sits, so it cannot see the bug at all; the `p <= 0` call is the one that can.

`edgeAnchor(1, q: 2)` TAKES the branch: `t = 3`, `a1..a8 = 2..9` (sum 44), `m = t = 3`, `r = leaf(1) =
2` — giving `44 + 3 + 2 + 3 = 52`. `edgeAnchor(0, q: 2)` SKIPS it: `t = 2`, `a1..a8 = 1..8` (sum 36),
`m` keeps its `0`, `r = leaf(0) = 1` — giving `36 + 0 + 1 + 2 = 39`. Each is checked alone, on its own
exit code, so either path can fail on its own.
```maxon
function leaf(x Integer) returns Integer
	return x + 1
end 'leaf'

function edgeAnchor(p Integer, q Integer) returns Integer
	let t = p + q
	let a1 = p + 1
	let a2 = p + 2
	let a3 = p + 3
	let a4 = p + 4
	let a5 = p + 5
	let a6 = p + 6
	let a7 = p + 7
	let a8 = p + 8
	var m = 0
	if p > 0 'br'
		m = t
	end 'br'
	let r = leaf(p)
	return a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + m + r + t
end 'edgeAnchor'

function main() returns ExitCode
	if edgeAnchor(1, q: 2) != 52 'taken'
		return 1
	end 'taken'
	if edgeAnchor(0, q: 2) != 39 'skipped'
		return 2
	end 'skipped'
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
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

`pick(k) = (k + 11k) + sink(k) = 13k`, so the six arms give 13, 26, 39, 52, 65, 78. `main` calls all
six and checks each result **alone**, returning that arm's index if it disagrees — so every arm runs,
every one of `v1`..`v6` is read on an executed path, and a wrong register in any single arm changes
the exit code and names the arm that broke.

Checking the SUM of the six would not do, and the reason is exactly the bug this test guards. If the
colorer hands arm `k` the register holding `v_j` instead of `v_k`, that arm computes `k + 11j + k`.
Sum that over any bijection σ from arms to values and the total is `Σ (2k + 11·σ(k))` = `2·21 + 11·21`
= 273 — the correct total, for **every** σ. A permuted colouring is precisely what a broken assignment
of six confined values to five callee-saved registers produces, and it is invisible to the sum while
being caught by any one of the six separate checks.
```maxon
function sink(x Integer) returns Integer
	return x
end 'sink'

function pick(k Integer) returns Integer
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
	if pick(1) != 13 'a1'
		return 1
	end 'a1'
	if pick(2) != 26 'a2'
		return 2
	end 'a2'
	if pick(3) != 39 'a3'
		return 3
	end 'a3'
	if pick(4) != 52 'a4'
		return 4
	end 'a4'
	if pick(5) != 65 'a5'
		return 5
	end 'a5'
	if pick(6) != 78 'a6'
		return 6
	end 'a6'
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: loop-invariant-read-across-a-call-in-a-loop -->
SIX loop-invariants, each read BOTH BEFORE AND AFTER a call inside the loop. Every one of them is
live across that call — so all six are confined to the five callee-saved registers, and one of them
cannot stay in a register. But the split at the eviction point cannot cut ANY of their ranges: each
has a before-peak use (in `pre`) that is reachable from the call around the BACK EDGE, so rewriting
its after-peak uses would leave it live across the call regardless. `killsValueAtPeak` correctly
refuses every one of them, and the splitter used to have nothing left to choose — it panicked
(`noVictimAtPeak: 'f' has a CONFINED overflow ... yet none of them is forced-spillable`) on a
program that fits the machine six times over.

It is relieved by widening the split: `a6` is stored ONCE at its def in the entry block (loop depth
0) and **every** use of it is rewritten to a reload — one before the `pre` sum, one after the call —
so nothing reads the original and it is dead across the call. `a1`..`a5` keep the five callee-saved
registers. The loop body grows by two loads, which is the ABI's price for a sixth live-across-call
value, not a search: the fragment pins one `storeSlotReg` OUTSIDE the loop and exactly two
`loadRegSlot` of that slot inside it. This must never be `E5001` — the program fits.

Each iteration adds `2 × (a1+…+a6) + i` = `42 + i` to `acc`, so `i = 0, 1, 2` gives `42 + 43 + 44 = 129`.
```maxon
function sink(x Integer) returns Integer
	return x
end 'sink'

function invariantsAcrossCall(p Integer) returns Integer
	let a1 = p + 1
	let a2 = p + 2
	let a3 = p + 3
	let a4 = p + 4
	let a5 = p + 5
	let a6 = p + 6
	var acc = 0
	var i = 0
	while i < 3 'loop'
		let pre = acc + a1 + a2 + a3 + a4 + a5 + a6
		let r = sink(i)
		acc = pre + r + a1 + a2 + a3 + a4 + a5 + a6
		i = i + 1
	end 'loop'
	return acc
end 'invariantsAcrossCall'

function main() returns ExitCode
	return invariantsAcrossCall(0)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
129
```

<!-- test: loop-invariant-constant-read-across-a-call-in-a-loop -->
The same shape, but the sixth confined value is a CONSTANT (`c`, read as `c - i` on both sides of
the call, which needs it in a register — a `sub`'s minuend cannot be an immediate). Five invariants
`a1`..`a5` take the five callee-saved registers, and `c` is the one that cannot stay.

Spilling it would work and would be wrong: a constant is free to recreate. So it is REMATERIALIZED
across **every** use — `movRegImm32 …, 1000` re-emitted before each `sub`, the original def dropped,
and NO stack slot for it at all. (The narrower remat — re-emit only after the peak — cannot be used:
`c`'s before-peak use is reachable from the call around the back edge, exactly as in the test above,
so the original would stay live across the call.) The fragment pins two `movRegImm32 …, 1000` inside
the loop and no slot for `c`; the three slots it does use are `pre`, `d1` and the counter `i`, all
of them ordinary eviction-point splits.

Each iteration adds `pre + d1 + d2 + r + (a1+…+a5)` to `acc`, with `d1 = d2 = 1000 - i` and
`pre = acc + 15`: `i = 0, 1, 2` give `acc = 2030, 4059, 6087`.
```maxon
function sink(x Integer) returns Integer
	return x
end 'sink'

function loopConst(p Integer) returns Integer
	let c = 1000
	let a1 = p + 1
	let a2 = p + 2
	let a3 = p + 3
	let a4 = p + 4
	let a5 = p + 5
	var acc = 0
	var i = 0
	while i < 3 'loop'
		let pre = acc + a1 + a2 + a3 + a4 + a5
		let d1 = c - i
		let r = sink(i)
		let d2 = c - i
		acc = pre + d1 + d2 + r + a1 + a2 + a3 + a4 + a5
		i = i + 1
	end 'loop'
	return acc
end 'loopConst'

function main() returns ExitCode
	let v = loopConst(0)
	if v == 6087 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: float-locals-read-after-each-of-two-calls -->
Two f64 locals, each read after each of TWO calls — and since **Wave 2** neither spills at all.
xmm6–15 are callee-saved, so both locals stay resident in registers across every call and the only
slot traffic in the fragment is the prologue/epilogue pair that preserves xmm6/xmm7 themselves:
**two `storeSlotReg` at entry and two `loadRegSlot` before the `ret`, once per function**, not once
per call.

⚠ **This test used to pin the opposite**, and reading the old shape is what makes the new one legible.
Under Wave 1 every XMM was caller-saved, so a float live across a call was forbidden its ENTIRE file,
had no register left, and was FORCE-SPILLED: the same two stores, plus **one `loadRegSlot` per read**
— four reloads here, eight in the four-call test below. That per-call bracket is what made
`regalloc:splitting` superlinear in the number of cross-call floats, and removing it is what Wave 2
is. Read this fragment against the four-call one: they are now the same shape, and doubling the
calls no longer adds a single instruction inside the body.

`work` adds 1 and each round adds `trunc(1.5) + trunc(2.5)` = 3, so `acc` runs 0 → 1 → 4 → 5 → 8.
```maxon
function work(x Integer) returns Integer
	return x + 1
end 'work'

function main() returns ExitCode
	let a = 1.5
	let b = 2.5
	var acc = 0
	acc = work(acc)
	acc = acc + trunc(a) + trunc(b)
	acc = work(acc)
	acc = acc + trunc(a) + trunc(b)
	return acc
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
8
```

<!-- test: float-locals-read-after-each-of-four-calls -->
The same program with the call sites DOUBLED — the other half of the pair above, and the one that
makes the property checkable rather than asserted. Since Wave 2 the two locals live in xmm6/xmm7
across every call, so doubling the calls adds NOTHING: this fragment carries the same two
`storeSlotReg` and two `loadRegSlot` as the two-call test, and they are the callee-save pair rather
than a spill. Under Wave 1 the reload count followed the reads (eight here, four there); under the
pre-run-break cascade it carried eight stores and eight slots as well.

`acc` runs 0 → 1 → 4 → 5 → 8 → 9 → 12 → 13 → 16.
```maxon
function work(x Integer) returns Integer
	return x + 1
end 'work'

function main() returns ExitCode
	let a = 1.5
	let b = 2.5
	var acc = 0
	acc = work(acc)
	acc = acc + trunc(a) + trunc(b)
	acc = work(acc)
	acc = acc + trunc(a) + trunc(b)
	acc = work(acc)
	acc = acc + trunc(a) + trunc(b)
	acc = work(acc)
	acc = acc + trunc(a) + trunc(b)
	return acc
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
16
```

<!-- test: two-floats-multiplied-between-calls -->
The multiply reads BOTH floats, and that is what made this a compiler CRASH rather than slow code.

A reload that spanned a later call was stranded, so it stayed confined exactly as the value it
relieved was. A stranded reload and a stranded original then met at the `mulsd` that reads them
both, giving a confined peak whose tight set was the peak op's OWN TWO OPERANDS — and
`chooseVictim` excludes the values the peak op reads, because they are needed in registers there.
Nothing was left to choose and `noVictimAtPeak` fired its splitter-bug panic on a program that fits
the machine many times over.

The invariant that panic asserts says candidates always remain, on the grounds that "any witness a
clobber produces has at least the five callee-saved registers in it, so at least six values are
confined here; an op names at most two virtual registers". That was **false for an empty witness**: a
float across a call was confined to ∅, so a tight set of exactly two was a violation, and one op can
read both. With reloads no longer born stranded, the only confined values left are originals, the
call outranks the multiply, and the program compiles.

⚠ **Wave 2 removed the ∅ itself**: a float across a call is now confined to the TEN callee-saved
XMMs, so the counting argument holds for floats exactly as it does for ints, and neither float here
spills at all. This test keeps its value as the record of why the run-break exists — the ∅ case is
no longer reachable through a call, and `noVictimAtPeak` explains what could still reach it.

Each round multiplies `1.5 × 2.5` = 3.75 into `s` and `work` adds 1 to `acc`, so two rounds give
`trunc(7.5) + 2` = 9.
```maxon
function work(x Integer) returns Integer
	return x + 1
end 'work'

function main() returns ExitCode
	let a = 1.5
	let b = 2.5
	var s = 0.0
	var acc = 0
	acc = work(acc)
	s = s + a * b
	acc = work(acc)
	s = s + a * b
	return trunc(s) + acc
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
9
```

<!-- test: two-floats-across-a-call-with-a-ranged-typealias -->
Two f64 locals live across a call in a function whose types are a **ranged typealias**. The floats
stay in callee-saved XMMs (xmm6–15), so nothing spills; the same program with `int` in place of
`Num` compiled before Wave 2 and this one did not, because the range check is what made the
splitter run at all here.

Why the ranged typealias is load-bearing rather than decoration: `InsertRangeChecks` emits a
compare whose boolean result lives only in EFLAGS, so it is a Std value that no Target *operand*
ever names — and it is the LAST id the pass mints, hence the highest. That is what once made the
value-class column longer than the allocator's value space, so a split's fresh reload id landed
inside the already-filled region and kept the column's `gpr` fill (see the next test).

`probe(0)` = `work(0)` + `trunc(3.625 * 1.5 * 64.0)` = 1 + 348 = 349; `probe(1)` = 350. 349 + 350
is past a byte, so the exit code is their difference plus one.
```maxon
typealias Num = int(-1000000000 to 1000000000)

function work(x Num) returns Num
	return x + 1
end 'work'

function probe(k Num) returns Num
	let f0 = 1.5
	let f2 = 3.625
	var acc = k
	acc = work(acc)
	acc = acc + trunc(f2 * f0 * 64.0)
	return acc
end 'probe'

function main() returns ExitCode
	return (probe(1) - probe(0) + 41) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: a-reload-inherits-its-victims-register-file -->
TWELVE f64 locals live across a call — two more than the ten callee-saved XMMs — so at least two of
them genuinely spill and are reloaded, **and the function also carries a ranged typealias**. That
pair is the whole test: the range check pushes the value-class column past the allocator's value
space, and the spill is what mints a fresh id inside the gap.

Before the fix, `LivenessResult.growValueSpace` recorded a split's fresh ids by GROW-FILLING the
class column with the victim's class — which is a silent no-op when the column is already that
long. The reload of an f64 therefore kept the column's `gpr` fill, was coloured into a GPR, and
reached the encoder as either a cross-file `mov xmm1, rax` (`emitRegRegMove`) or a `cvttsd2si`
whose source is `rcx` (`requireClass`) — the same defect with two crash sites. The class of a
split's fresh id is now WRITTEN over the minted range, not merely defaulted into it.

`bump(0)` = 1, plus `trunc(1.0) + … + trunc(12.0)` = 78, so 79.
```maxon
typealias Num = int(-1000000000 to 1000000000)

function bump(x Num) returns Num
	return x + 1
end 'bump'

function pressure(k Num) returns Num
	let a = 1.0
	let b = 2.0
	let c = 3.0
	let d = 4.0
	let e = 5.0
	let f = 6.0
	let g = 7.0
	let h = 8.0
	let i = 9.0
	let j = 10.0
	let l = 11.0
	let m = 12.0
	var acc = k
	acc = bump(acc)
	acc = acc + trunc(a) + trunc(b) + trunc(c) + trunc(d) + trunc(e) + trunc(f)
	acc = acc + trunc(g) + trunc(h) + trunc(i) + trunc(j) + trunc(l) + trunc(m)
	return acc
end 'pressure'

function main() returns ExitCode
	return pressure(0) as ExitCode
end 'main'
```
```exitcode
79
```

<!-- test: ten-floats-across-a-call-fit-the-callee-saved-half -->
EXACTLY ten f64 locals live across a call — the width of the callee-saved XMM half (xmm6–15). None
spills: this is the boundary case of Wave 2.

⚠ Its fragment still carries TEN `storeSlotReg` — the entry instruction count is literally unchanged
by Wave 2 — but they are a different kind of store, and that distinction is the whole point of the
case. Before Wave 2 the ten were VALUE SPILLS, one per local, each paired with a reload at every
read because a float live across a call was forbidden its entire file. After Wave 2 the ten are the
CALLEE-SAVE PAIR the prologue writes once and the epilogue restores once, and the values themselves
stay resident in xmm6–15 for the whole body. Counting stores cannot tell the two apart; only their
position can, which is why this case is read alongside the two-call and four-call siblings above —
there the store count stays flat as the calls double, which a per-call spill bracket could not do.

`bump(0)` = 1 plus `trunc(1.0) + … + trunc(10.0)` = 55, so 56.
```maxon
function bump(x Integer) returns Integer
	return x + 1
end 'bump'

function pressure(k Integer) returns Integer
	let a = 1.0
	let b = 2.0
	let c = 3.0
	let d = 4.0
	let e = 5.0
	let f = 6.0
	let g = 7.0
	let h = 8.0
	let i = 9.0
	let j = 10.0
	var acc = k
	acc = bump(acc)
	acc = acc + trunc(a) + trunc(b) + trunc(c) + trunc(d) + trunc(e)
	acc = acc + trunc(f) + trunc(g) + trunc(h) + trunc(i) + trunc(j)
	return acc
end 'pressure'

function main() returns ExitCode
	return pressure(0) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
56
```

<!-- test: callee-saved-double-survives-a-callee -->
A caller holding an f64 **across a call** is confined to the callee-saved half of the float file
(xmm6–15 on x64, d8–d15 on arm64), and the callee it calls preserves exactly the ones its own
colouring used. This case pins the FULL 64 BITS of that preserve, which nothing else did.

The nesting is the test: `outer` keeps `42.5` live across its call to `inner`, and `inner` — which
keeps `2.0` live across its own call to `leaf` — therefore colours into, and must save and restore,
the very register `outer` left its double in. No register PRESSURE is involved anywhere: three
functions, one float each.

⚠ Its sibling `a-reload-inherits-its-victims-register-file` pins the SPILL SLOT and this one pins the
CALLEE-SAVE SLOT, and a preserve that moves only half a double passes that one while failing this
one — the spilled value there is reloaded by the same function that stored it, so a symmetric
half-width store/reload still round-trips whatever it truncated. Here the store and the reload are in
`inner` while the VALUE belongs to `outer`, so a half-width preserve destroys a double the callee
never touched. arm64 emitted `stur s8` for `stur d8` (the size field in bits 31:30 reading `10`, the
32-bit S form, where 64-bit D is `11`) and the two spellings differ in one hex digit; `42.5` came back
as its low word — zero — and this returned 3.

`leaf(0)` = 1, plus `trunc(2.0)` = 2, plus `trunc(42.5)` = 42, so 45.
```maxon
function leaf(x Integer) returns Integer
	return x + 1
end 'leaf'

function inner(k Integer) returns Integer
	let p = 2.0
	var acc = k
	acc = leaf(acc)
	return acc + trunc(p)
end 'inner'

function outer(k Integer) returns Integer
	let a = 42.5
	var acc = k
	acc = inner(acc)
	return acc + trunc(a)
end 'outer'

function main() returns ExitCode
	return outer(0) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
45
```

<!-- test: a-reload-for-a-later-argument-must-not-clobber-an-earlier-one -->
A RELOAD BORN BETWEEN TWO ARGUMENT PRE-MOVES. `a` is defined first and read last — as the
FOURTH argument of `sink4` — so Belady evicts it at the `s` peak and reloads it before its one
remaining use, which is the pre-move `mov argReg[3], a`. The pre-moves for `c0`/`c1`/`c2` run
ahead of it and their own values die there, so the registers those three arguments now sit in
are held by nothing the allocator can see. The reload is live across none of them, is therefore
forbidden none of them, and took `argReg[0]` — overwriting `c0` after it had been placed.

`sink4` weighs its four arguments by powers of ten so ANY clobber or permutation shows in the
answer rather than cancelling: `sink4(1, 2, 3, 7) = 1237`, `s = sum(1..28) = 406`, `f(0) = 1643`.
Measured before the fix on arm64-macOS: exit 99, from `mov x0, c0 / mov x1, c1 / mov x2, c2 /
ldr x0, [slot] / mov x3, x0 / bl sink4`.

⚠ THE WIDTH IS LOAD-BEARING, for the reason `two-pressure-humps-exhaust-a-positional-gap` gives:
`a` must be evicted, so the straight-line peak has to exceed the pool of the WIDEST target — 26
allocatable GPRs on arm64 against x64's 14 — and 28 is the first round number past it.
```maxon
function sink4(a Integer, b Integer, c Integer, d Integer) returns Integer
	return a * 1000 + b * 100 + c * 10 + d
end 'sink4'

function f(p Integer) returns Integer
	let a = p + 7
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
	let b16 = p + 16
	let b17 = p + 17
	let b18 = p + 18
	let b19 = p + 19
	let b20 = p + 20
	let b21 = p + 21
	let b22 = p + 22
	let b23 = p + 23
	let b24 = p + 24
	let b25 = p + 25
	let b26 = p + 26
	let b27 = p + 27
	let b28 = p + 28
	let s = b1 + b2 + b3 + b4 + b5 + b6 + b7 + b8 + b9 + b10 + b11 + b12 + b13 + b14 + b15 + b16 + b17 + b18 + b19 + b20 + b21 + b22 + b23 + b24 + b25 + b26 + b27 + b28
	let c0 = p + 1
	let c1 = p + 2
	let c2 = p + 3
	let r = sink4(c0, b: c1, c: c2, d: a)
	return s + r
end 'f'

function main() returns ExitCode
	let r = f(0)
	if r == 1643 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: two-pressure-humps-exhaust-a-positional-gap -->
THE RE-SPACE REGRESSION, and the one shape in the suite that reaches it.

The splitter indexes each block's ops by a gap-labelled SLOT rather than a position, so a
splice takes a free slot between its neighbours and no existing entry moves. `SlotGap` is 4
and `freeSlotAt` subdivides at the MIDPOINT, so ONE gap absorbs `log2(4) == 2` insertions
before it is used up and `PressureIndex.makeRoom` has to re-space.

Getting three into one gap takes exactly this shape. A gap takes both the store anchored
after an op's def (`insertStoreAfter`: anchorPos + 1) and every reload anchored before the
NEXT op (`bodyPositionOfUse`: that op's own position, which is the same number). So an add
reading two already-spilled values, sitting immediately after an add whose own result is
spilled, puts a store and two reloads in one gap. It takes TWO pressure humps to arrange:
every reload of a peak's victims lands AFTER that peak and every store at a def BEFORE it,
so within one hump the two never share a gap — it is the second hump's rise that makes the
first hump's `q` values spillable, and their stores land in the first hump's fall.

WHAT IT CAUGHT. `SplitEdits` records the slot of each op it splices as that op is seated,
and `reindexSplitValues` reads them back after the batch. A re-space MOVES those ops, and
nothing repaired the recorded slots — `appendInsertedSlots` merely asserted in a comment
that the re-space "re-derived" them. This program panicked the compiler outright:

  panic: PressureIndex.opAtSlot: slot 166 holds no op — only a slot the layout seated one in names one
    in Compiler.Targets.Shared.reindexSplitValues

⚠ THE WIDTH IS LOAD-BEARING AND ITS WINDOW IS NARROW. At TWELVE values per hump this fires
two re-spaces. MEASURED at this size, two humps: eight splits nine values but never exhausts
a gap, so it would pass against the broken compiler; ten fires one re-space, twelve two,
fourteen three, twenty five; twenty-eight is a legitimate E5001. So twelve sits in the middle
of the window rather than on its edge — which is the point, since the pool size is what both
edges are measured against and arm64 has a larger one. Re-check the width against any change
to a register file, and re-confirm it still RE-SPACES rather than merely still passing.
`Testing/ladders/genrespace.sh` is the same shape with N humps, for measuring rather than
gating.

Each hump pairs `(0,1) (2,3) (4,5) (6,7) (8,9) (10,11)` into `1 + 5 + 9 + 13 + 17 + 21 = 66`,
and there are two of them, so `humps(7)` is `7 + 132` = 139.
```maxon
typealias WideNum = int(0 to 100000000)

function scaleOpaque(x WideNum) returns WideNum
	return x
end 'scaleOpaque'

function humps(g WideNum) returns WideNum
	let a0 = scaleOpaque(0)
	let a1 = scaleOpaque(1)
	let a2 = scaleOpaque(2)
	let a3 = scaleOpaque(3)
	let a4 = scaleOpaque(4)
	let a5 = scaleOpaque(5)
	let a6 = scaleOpaque(6)
	let a7 = scaleOpaque(7)
	let a8 = scaleOpaque(8)
	let a9 = scaleOpaque(9)
	let a10 = scaleOpaque(10)
	let a11 = scaleOpaque(11)
	let p0 = a0 + a1
	let p1 = a2 + a3
	let p2 = a4 + a5
	let p3 = a6 + a7
	let p4 = a8 + a9
	let p5 = a10 + a11
	let b0 = scaleOpaque(0)
	let b1 = scaleOpaque(1)
	let b2 = scaleOpaque(2)
	let b3 = scaleOpaque(3)
	let b4 = scaleOpaque(4)
	let b5 = scaleOpaque(5)
	let b6 = scaleOpaque(6)
	let b7 = scaleOpaque(7)
	let b8 = scaleOpaque(8)
	let b9 = scaleOpaque(9)
	let b10 = scaleOpaque(10)
	let b11 = scaleOpaque(11)
	let q0 = b0 + b1
	let q1 = b2 + b3
	let q2 = b4 + b5
	let q3 = b6 + b7
	let q4 = b8 + b9
	let q5 = b10 + b11
	return g + p0 + p1 + p2 + p3 + p4 + p5 + q0 + q1 + q2 + q3 + q4 + q5
end 'humps'

function main() returns ExitCode
	let r = humps(7)
	if r == 139 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
```
```exitcode
0
```

<!-- test: a-twice-spilled-value-reloads-after-its-own-store -->
A SPILL STORE IS A USE OF THE VALUE IT STORES, AND IT MUST NEVER ANCHOR A RELOAD.

`storeSlotReg slot, v` READS `v`, so `buildUseIndex` records it as an ordinary use of `v`.
When the SAME value is spilled a SECOND time at a deeper peak, `spillOneValue` walks the use
thread and reaches that store's record. Served like any other use, it anchors a reload at the
store's OWN position — splicing `loadRegSlot fresh, slot` IMMEDIATELY BEFORE the only op that
ever writes that slot. `rewriteOpAt` then correctly declines to repoint the store itself, but
the reload is already there and the REST of the run is repointed at it, so the run's real uses
read a stack slot nothing has written.

Here the `let`-bound array is that twice-spilled value: it is live across BOTH loops, and the
single interpolation reading both accumulators at the end is what raises the second peak. The
emitted `entry` block read

	callDirect  __managed_create      ; the array, in r8
	loadRegSlot  rcx, slot0       ; <- reload of a slot NOTHING has written
	storeSlotReg slot0, r8        ; <- the store it was spliced ahead of
	callDirect  __managed_push        ; receiver rcx = uninitialised frame slot

which faults. ALL FOUR of these are required and dropping any ONE hides it (see the near
misses below): a `let`-bound array, TWO `for..in` loops, self-referencing accumulation in
each, and both accumulators read in ONE interpolation.
```maxon
function main() returns ExitCode
	let b = [72]
	var one = ""
	for x in b 'eachOne'
		one = "{one}{x}"
	end 'eachOne'
	var two = ""
	for x in b 'eachTwo'
		two = "{two}{x}"
	end 'eachTwo'
	print("{one} {two}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
72 72
```

<!-- test: one-loop-does-not-raise-the-second-peak -->
NEAR MISS — drops condition 2 (two loops). One loop spills the array at most once, so no split
ever reaches a store's use record.
```maxon
function main() returns ExitCode
	let b = [72]
	var one = ""
	for x in b 'eachOne'
		one = "{one}{x}"
	end 'eachOne'
	print("{one}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
72
```

<!-- test: two-accumulators-printed-separately -->
NEAR MISS — drops condition 4 (both accumulators read in ONE interpolation). Two separate
`print`s never make both accumulators live at once, so the second peak stays below the pool.
```maxon
function main() returns ExitCode
	let b = [72]
	var one = ""
	for x in b 'eachOne'
		one = "{one}{x}"
	end 'eachOne'
	var two = ""
	for x in b 'eachTwo'
		two = "{two}{x}"
	end 'eachTwo'
	print("{one}\n")
	print("{two}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
72
72
```

<!-- test: two-loops-without-self-referencing-accumulation -->
NEAR MISS — drops condition 3 (self-referencing accumulation). Without reading the accumulator
back, each loop body holds one fewer live String across its own peak.
```maxon
function main() returns ExitCode
	let b = [72]
	var one = ""
	for x in b 'eachOne'
		one = "{x}"
	end 'eachOne'
	var two = ""
	for x in b 'eachTwo'
		two = "{x}"
	end 'eachTwo'
	print("{one} {two}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
72 72
```

<!-- test: a-twice-spilled-value-read-on-every-iteration -->
The same four conditions with a two-element array, so each loop iterates more than once and the
reload-before-store is read on every iteration rather than only on the first.
```maxon
function main() returns ExitCode
	let b = [72, 73]
	var one = ""
	for x in b 'eachOne'
		one = "{one}{x}"
	end 'eachOne'
	var two = ""
	for x in b 'eachTwo'
		two = "{two}{x}"
	end 'eachTwo'
	print("{one} {two}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7273 7273
```
