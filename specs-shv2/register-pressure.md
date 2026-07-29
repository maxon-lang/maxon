---
feature: register-pressure
status: selfhosted
keywords: [register-allocator, E5001, register-pressure, hot-spill, diagnostic, value-origin, callee-saved]
category: register-allocator
milestone: M5.7
---

# Register-pressure diagnostic (E5001)

## Documentation

The register allocator's pool is 14 GPRs. E5001 exists to refuse **the search**, not to refuse
**the spill** — and those are different things. The allocator will always emit a spill whose
placement is *forced*, because deciding it costs nothing. It raises E5001 only where relieving the
pressure would require it to *search* — an eviction tournament, a spill-cost model, iterated
re-splitting — which is the expensive, heuristic, non-deterministic machinery this design exists
to avoid. That gives three cases:

**1. A value idle across a pressured region → split it, free.** The **cold-spill splitter** (see
`register-spill`) stores it before the region and reloads it after, with **nothing added to the
loop body**. One placement, no choice.

**2. A value live across a fixed-register point → bracket it, cheap.** At a **call**, the callee
clobbers all 9 caller-saved registers, so a value that must survive the call can only live in one
of the **5 callee-saved** registers — or on the stack. When more than 5 values are live across a
call, the excess *must* go to memory: a store before the call and a reload after. That placement
is **forced by the ABI, not chosen by a search**, so the allocator simply emits it — *even inside
a loop*. The pair is cheap against the call it brackets: one store and one load, against a call
that already costs far more. (An `idiv` is the same case in miniature, reserving `RAX`/`RDX`.)

This is **not** E5001, and it must never become E5001. Such a program fits the machine — the
values are excluded only from the registers one op happens to clobber. Refusing it would be a
false positive, and the "restructuring" it would demand (hoist the loop's values into an array)
emits a load *and* a store at **every use** — strictly worse code than the single bracket the
compiler declined to emit.

**3. A value the loop genuinely USES, when the loop's working set exceeds the whole pool →
E5001.** Here there is nothing to bracket: spilling any of them puts a reload at *every use*,
every iteration, and choosing which to sacrifice is exactly the search this allocator refuses to
run. The working set is simply larger than the machine, and no spiller can fix that — only a
restructuring the programmer can do: hold the working set in an array (array elements are never
promoted into registers, so the spill stays spilled).

The diagnostic is the feature, not the error path. Because SSA interference is chordal, the
per-program-point `maxlive` **is** the exact minimum register count for the program as lowered —
so E5001 fires **iff** the loop truly does not fit the full pool, never on a loop that a smarter
allocator would have colored. It is designed to let its consumer converge in one step:

- **The exact deficit** — "remove N values", not "too many" — against the **full pool of 14**.
- **Each blocking value's source def site**, recovered through the `ValueOrigin` table
  (`(funcIndex, ValueId)` → the Maxon op that defined it → its source span), ranked
  cheapest-to-move first: fewest uses inside the loop means fewest reloads after the array
  rewrite. A loop-carried value (an SSA phi) has no defining op, so it is chased to the
  incoming value it copies — its declaration.
- **The transformation**, named, not just the diagnosis.

The message is **deterministic byte-for-byte** — same program, same message — which is what
the ` ```maxoncstderr ` blocks below assert.

**Targets — a REGISTER-POOL gate, and the pool is the subject.** The deficit each message quotes is
computed against the target's own file, so the same program yields a DIFFERENT byte-exact message per
ISA — which is why the cases below come in `x64-windows, x64-linux` / `arm64-macos, arm64-linux` twins
rather than one shared case. `wasm32-wasi` is excluded from all of them because a stack machine has no
register cap to exceed, so E5001 cannot fire there at all. The float case is gated to x64 for the same
reason one file down: it is calibrated to x64's sixteen-deep XMM pool.

⚠ Verified 2026-07-28 by widening every marker and re-running: each x64 twin fails on arm64 and each
arm64 twin on x64 with a *compiler-error mismatch* — the deficits genuinely differ — so no twin is
gated merely because nobody generated its golden.

## Tests

<!-- test: hot-loop-overflow -->
<!-- targets: x64-windows, x64-linux -->
Sixteen accumulators `s1`..`s16` are ALL updated every iteration, plus the loop counter `i`
— seventeen values the loop genuinely uses, against a pool of fourteen. None is idle across
the loop, so the cold-spill splitter cannot relieve any of them: this is a HOT overflow, and
the compiler reports E5001. The deficit is exactly 3 (17 − 14). The accumulators are each
used once in the loop (their own update), so they rank first (cheapest to hoist into an
array); the counter `i`, read by all sixteen updates plus the condition plus its own
increment, ranks last. Each value points at its declaration's source span.
```maxon
function hot(p int) returns int
	var s1 = 1
	var s2 = 2
	var s3 = 3
	var s4 = 4
	var s5 = 5
	var s6 = 6
	var s7 = 7
	var s8 = 8
	var s9 = 9
	var s10 = 10
	var s11 = 11
	var s12 = 12
	var s13 = 13
	var s14 = 14
	var s15 = 15
	var s16 = 16
	var i = 0
	while i < 5 'loop'
		s1 = s1 + i
		s2 = s2 + i
		s3 = s3 + i
		s4 = s4 + i
		s5 = s5 + i
		s6 = s6 + i
		s7 = s7 + i
		s8 = s8 + i
		s9 = s9 + i
		s10 = s10 + i
		s11 = s11 + i
		s12 = s12 + i
		s13 = s13 + i
		s14 = s14 + i
		s15 = s15 + i
		s16 = s16 + i
		i = i + 1
	end 'loop'
	return s1 + s2 + s3 + s4 + s5 + s6 + s7 + s8 + s9 + s10 + s11 + s12 + s13 + s14 + s15 + s16
end 'hot'

function main() returns ExitCode
	return hot(0)
end 'main'
```
```maxoncstderr
error E5001: the loop at <fragment>:21 needs 3 more register(s) than are available
  17 values must be held in registers at once inside this loop, but
  only 14 registers are available. The values idle across the loop were already
  spilled around it at no cost; spilling any of these would put a load or store inside
  the loop body, which is exactly what this error exists to prevent.

  remove 3 of these 17 value(s) from the loop, cheapest first (ranked by uses inside the loop):
    <fragment>:3:11   used 1 time in the loop
    <fragment>:4:11   used 1 time in the loop
    <fragment>:5:11   used 1 time in the loop
    <fragment>:6:11   used 1 time in the loop
    <fragment>:7:11   used 1 time in the loop
    <fragment>:8:11   used 1 time in the loop
    <fragment>:9:11   used 1 time in the loop
    <fragment>:10:11   used 1 time in the loop
    <fragment>:11:11   used 1 time in the loop
    <fragment>:12:12   used 1 time in the loop
    <fragment>:13:12   used 1 time in the loop
    <fragment>:14:12   used 1 time in the loop
    <fragment>:15:12   used 1 time in the loop
    <fragment>:16:12   used 1 time in the loop
    <fragment>:17:12   used 1 time in the loop
    <fragment>:18:12   used 1 time in the loop
    <fragment>:19:10   used 18 times in the loop

  to fix: hold the loop's working set in an array and index it inside the loop.
  array elements are never promoted into registers, so the values stay in memory
  and the loop body no longer needs a register for each one.
```

<!-- test: hot-loop-overflow-arm64 -->
<!-- targets: arm64-macos, arm64-linux -->
arm64 allocates from 26 GPRs (x0-x15 ∪ x19-x28), not x64's 14, so an overflow needs more live values than `hot-loop-overflow`. Twenty-eight accumulators `s1`..`s28` are all updated every iteration, plus the counter `i`, plus the loop condition's materialized boolean — arm64 lowers `i < N` to `cmp`+`cset` into a GPR, where x64 fuses `cmp`+`jcc` and materializes nothing — so thirty values are live at the loop header against a pool of twenty-six. The deficit is exactly 4 (30 − 26), reported against the FULL arm64 pool, and each accumulator points at its declaration span.
```maxon
function hot(p int) returns int
	var s1 = 1
	var s2 = 2
	var s3 = 3
	var s4 = 4
	var s5 = 5
	var s6 = 6
	var s7 = 7
	var s8 = 8
	var s9 = 9
	var s10 = 10
	var s11 = 11
	var s12 = 12
	var s13 = 13
	var s14 = 14
	var s15 = 15
	var s16 = 16
	var s17 = 17
	var s18 = 18
	var s19 = 19
	var s20 = 20
	var s21 = 21
	var s22 = 22
	var s23 = 23
	var s24 = 24
	var s25 = 25
	var s26 = 26
	var s27 = 27
	var s28 = 28
	var i = 0
	while i < 5 'loop'
		s1 = s1 + i
		s2 = s2 + i
		s3 = s3 + i
		s4 = s4 + i
		s5 = s5 + i
		s6 = s6 + i
		s7 = s7 + i
		s8 = s8 + i
		s9 = s9 + i
		s10 = s10 + i
		s11 = s11 + i
		s12 = s12 + i
		s13 = s13 + i
		s14 = s14 + i
		s15 = s15 + i
		s16 = s16 + i
		s17 = s17 + i
		s18 = s18 + i
		s19 = s19 + i
		s20 = s20 + i
		s21 = s21 + i
		s22 = s22 + i
		s23 = s23 + i
		s24 = s24 + i
		s25 = s25 + i
		s26 = s26 + i
		s27 = s27 + i
		s28 = s28 + i
		i = i + 1
	end 'loop'
	return s1 + s2 + s3 + s4 + s5 + s6 + s7 + s8 + s9 + s10 + s11 + s12 + s13 + s14 + s15 + s16 + s17 + s18 + s19 + s20 + s21 + s22 + s23 + s24 + s25 + s26 + s27 + s28
end 'hot'

function main() returns ExitCode
	return hot(0)
end 'main'
```
```maxoncstderr
error E5001: the loop at <fragment>:32 needs 4 more register(s) than are available
  30 values must be held in registers at once inside this loop, but
  only 26 registers are available. The values idle across the loop were already
  spilled around it at no cost; spilling any of these would put a load or store inside
  the loop body, which is exactly what this error exists to prevent.

  remove 4 of these 30 value(s) from the loop, cheapest first (ranked by uses inside the loop):
    <fragment>:3:11   used 1 time in the loop
    <fragment>:4:11   used 1 time in the loop
    <fragment>:5:11   used 1 time in the loop
    <fragment>:6:11   used 1 time in the loop
    <fragment>:7:11   used 1 time in the loop
    <fragment>:8:11   used 1 time in the loop
    <fragment>:9:11   used 1 time in the loop
    <fragment>:10:11   used 1 time in the loop
    <fragment>:11:11   used 1 time in the loop
    <fragment>:12:12   used 1 time in the loop
    <fragment>:13:12   used 1 time in the loop
    <fragment>:14:12   used 1 time in the loop
    <fragment>:15:12   used 1 time in the loop
    <fragment>:16:12   used 1 time in the loop
    <fragment>:17:12   used 1 time in the loop
    <fragment>:18:12   used 1 time in the loop
    <fragment>:19:12   used 1 time in the loop
    <fragment>:20:12   used 1 time in the loop
    <fragment>:21:12   used 1 time in the loop
    <fragment>:22:12   used 1 time in the loop
    <fragment>:23:12   used 1 time in the loop
    <fragment>:24:12   used 1 time in the loop
    <fragment>:25:12   used 1 time in the loop
    <fragment>:26:12   used 1 time in the loop
    <fragment>:27:12   used 1 time in the loop
    <fragment>:28:12   used 1 time in the loop
    <fragment>:29:12   used 1 time in the loop
    <fragment>:30:12   used 1 time in the loop
    <fragment>:32:10   used 1 time in the loop
    <fragment>:31:10   used 30 times in the loop

  to fix: hold the loop's working set in an array and index it inside the loop.
  array elements are never promoted into registers, so the values stay in memory
  and the loop body no longer needs a register for each one.
```

<!-- test: hot-loop-across-call -->
A call inside a loop is **NOT** E5001 — it is case 2, the forced bracket. Five accumulators AND
the loop counter (six values) are live across the `sink` call inside the loop, but only five
callee-saved registers survive a call. One value therefore *cannot* stay in a register: the ABI
leaves it exactly one home, the stack. So the splitter stores it before the call and reloads it
after — a placement it does not choose, only obeys — and the loop body grows by one store and one
load, bracketing a call that costs far more. Nothing is searched, and no error is raised.

This is the case that must never regress to E5001. The program fits the machine: six values,
fourteen registers. The array rewrite an E5001 would have demanded puts all five accumulators in
memory and reads *and writes* each one every iteration — ten memory ops per iteration to avoid
two. Refusing the spill would have produced strictly worse code AND a false error.

Every accumulator is loop-carried (an SSA phi) and its update is a back-edge arg, so this also
covers the two shapes a forced spill must handle: a phi's store anchors at its block's entry, and
a spilled value's branch-edge args are repointed at the reload alongside its op uses.

`sink(i) = i`, so each `sk` accumulates `0+1+2+3+4 = 10`: `s1..s5 = 11,12,13,14,15`, summing to 65.
```maxon
function sink(x int) returns int
	return x
end 'sink'

function hotCall(p int) returns int
	var s1 = 1
	var s2 = 2
	var s3 = 3
	var s4 = 4
	var s5 = 5
	var i = 0
	while i < 5 'loop'
		let r = sink(i)
		s1 = s1 + r
		s2 = s2 + r
		s3 = s3 + r
		s4 = s4 + r
		s5 = s5 + r
		i = i + 1
	end 'loop'
	return s1 + s2 + s3 + s4 + s5
end 'hotCall'

function main() returns ExitCode
	return hotCall(0)
end 'main'
```
```exitcode
65
```

<!-- test: rescued-idle-around-loop -->
The CONTRAST to `hot-loop-overflow`: the SAME sixteen values, but now they are idle across
the loop (computed before it, summed after it) rather than updated inside it. The loop's
genuine working set is just `sum` and `i` — two values — so it fits, and the cold-spill
splitter stores the sixteen idle values around the loop. The loop body stays exactly
`sum = sum + i; i = i + 1` with NOTHING added (verify in the fragment: the `loop` block is
two `lea`s and a `jmp`). No E5001. Result is `sum(0..4)=10 + sum(1..16)=136 = 146`.
```maxon
function rescued(p int) returns int
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
	let k16 = p + 16
	var sum = 0
	var i = 0
	while i < 5 'loop'
		sum = sum + i
		i = i + 1
	end 'loop'
	return sum + k1 + k2 + k3 + k4 + k5 + k6 + k7 + k8 + k9 + k10 + k11 + k12 + k13 + k14 + k15 + k16
end 'rescued'

function main() returns ExitCode
	return rescued(0)
end 'main'
```
```exitcode
146
```

<!-- test: hot-loop-param-used -->
<!-- targets: x64-windows, x64-linux -->
A PARAMETER used every iteration is part of the hot working set, so it can appear in a
blocking set — and it is a user-visible, deletable value. Here `p` is read inside the loop
(`s1 = s1 + i + p`), so with thirteen accumulators plus the counter it is one of fifteen
values live at once against a pool of fourteen. `p` has NO defining op (it is captured at
entry, ValueId 0), yet it must NOT trip the Rule-3 "compiler-introduced value" panic: it is
resolved to its DECLARATION span in the signature (`<fragment>:2:14` — the `p` token) through
the `ParamOriginTable`. It ranks first (used once in the loop); the counter `i` ranks last.
```maxon
function hot(p int) returns int
	var s1 = 1
	var s2 = 2
	var s3 = 3
	var s4 = 4
	var s5 = 5
	var s6 = 6
	var s7 = 7
	var s8 = 8
	var s9 = 9
	var s10 = 10
	var s11 = 11
	var s12 = 12
	var s13 = 13
	var i = 0
	while i < 5 'loop'
		s1 = s1 + i + p
		s2 = s2 + i
		s3 = s3 + i
		s4 = s4 + i
		s5 = s5 + i
		s6 = s6 + i
		s7 = s7 + i
		s8 = s8 + i
		s9 = s9 + i
		s10 = s10 + i
		s11 = s11 + i
		s12 = s12 + i
		s13 = s13 + i
		i = i + 1
	end 'loop'
	return s1 + s2 + s3 + s4 + s5 + s6 + s7 + s8 + s9 + s10 + s11 + s12 + s13
end 'hot'

function main() returns ExitCode
	return hot(0)
end 'main'
```
```maxoncstderr
error E5001: the loop at <fragment>:18 needs 1 more register(s) than are available
  15 values must be held in registers at once inside this loop, but
  only 14 registers are available. The values idle across the loop were already
  spilled around it at no cost; spilling any of these would put a load or store inside
  the loop body, which is exactly what this error exists to prevent.

  remove 1 of these 15 value(s) from the loop, cheapest first (ranked by uses inside the loop):
    <fragment>:2:14   used 1 time in the loop
    <fragment>:3:11   used 1 time in the loop
    <fragment>:4:11   used 1 time in the loop
    <fragment>:5:11   used 1 time in the loop
    <fragment>:6:11   used 1 time in the loop
    <fragment>:7:11   used 1 time in the loop
    <fragment>:8:11   used 1 time in the loop
    <fragment>:9:11   used 1 time in the loop
    <fragment>:10:11   used 1 time in the loop
    <fragment>:11:11   used 1 time in the loop
    <fragment>:12:12   used 1 time in the loop
    <fragment>:13:12   used 1 time in the loop
    <fragment>:14:12   used 1 time in the loop
    <fragment>:15:12   used 1 time in the loop
    <fragment>:16:10   used 15 times in the loop

  to fix: hold the loop's working set in an array and index it inside the loop.
  array elements are never promoted into registers, so the values stay in memory
  and the loop body no longer needs a register for each one.
```

<!-- test: straight-line-overflow-names-no-loop -->
<!-- targets: x64-windows, x64-linux -->
**A FULL-POOL OVERFLOW NEED NOT BE IN A LOOP.** While a function was capped at six parameters, straight-line
code could not hold more live values than the pool, and the anchor helper's line-0 fallback carried a comment
saying it "cannot happen for a real loop overflow". Stack arguments removed the cap: twenty-one parameters,
each read by both calls and again by the sum, are all live at the call and overflow the fourteen-GPR pool with
no loop anywhere. The program is correctly REFUSED — what this pins is that the message says so truthfully.
It must name `the code at`, not a loop that does not exist; it must anchor on a REAL source line, not `:0`;
and the uses must be counted over the FUNCTION, because counting them over a loop that is not there ranked
every candidate 0 and made "cheapest first" an empty promise.
```maxon
function sink(p1 int, p2 int, p3 int, p4 int, p5 int, p6 int, p7 int, p8 int, p9 int, p10 int, p11 int, p12 int, p13 int, p14 int, p15 int, p16 int, p17 int, p18 int, p19 int, p20 int, p21 int) returns int
	return p1 + p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9 + p10 + p11 + p12 + p13 + p14 + p15 + p16 + p17 + p18 + p19 + p20 + p21
end 'sink'

function wide(p1 int, p2 int, p3 int, p4 int, p5 int, p6 int, p7 int, p8 int, p9 int, p10 int, p11 int, p12 int, p13 int, p14 int, p15 int, p16 int, p17 int, p18 int, p19 int, p20 int, p21 int) returns int
	let a = sink(p1, p2: p2, p3: p3, p4: p4, p5: p5, p6: p6, p7: p7, p8: p8, p9: p9, p10: p10, p11: p11, p12: p12, p13: p13, p14: p14, p15: p15, p16: p16, p17: p17, p18: p18, p19: p19, p20: p20, p21: p21)
	let b = sink(p1, p2: p2, p3: p3, p4: p4, p5: p5, p6: p6, p7: p7, p8: p8, p9: p9, p10: p10, p11: p11, p12: p12, p13: p13, p14: p14, p15: p15, p16: p16, p17: p17, p18: p18, p19: p19, p20: p20, p21: p21)
	return a + b + p1 + p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9 + p10 + p11 + p12 + p13 + p14 + p15 + p16 + p17 + p18 + p19 + p20 + p21
end 'wide'

function main() returns ExitCode
	return wide(1, p2: 1, p3: 1, p4: 1, p5: 1, p6: 1, p7: 1, p8: 1, p9: 1, p10: 1, p11: 1, p12: 1, p13: 1, p14: 1, p15: 1, p16: 1, p17: 1, p18: 1, p19: 1, p20: 1, p21: 1)
end 'main'
```
```maxoncstderr
error E5001: the code at <fragment>:6 needs 1 more register(s) than are available
  15 values must be held in registers at once at this point, but
  only 14 registers are available. Every value that could be moved out of the
  way already was; the ones listed below are all live across this point at the same
  time, so no further spill can relieve it.

  remove 1 of these 15 value(s) from this point, cheapest first (ranked by uses in the function):
    <fragment>:6:23   used 2 times in the function
    <fragment>:6:39   used 2 times in the function
    <fragment>:6:55   used 2 times in the function
    <fragment>:6:63   used 2 times in the function
    <fragment>:6:87   used 2 times in the function
    <fragment>:6:150   used 2 times in the function
    <fragment>:6:168   used 2 times in the function
    <fragment>:6:177   used 2 times in the function
    <fragment>:6:79   used 3 times in the function
    <fragment>:6:105   used 3 times in the function
    <fragment>:6:114   used 3 times in the function
    <fragment>:6:123   used 3 times in the function
    <fragment>:6:132   used 3 times in the function
    <fragment>:6:141   used 3 times in the function
    <fragment>:6:186   used 3 times in the function

  to fix: hold this working set in an array and index it, or split the function so
  fewer of these values are live at the same time.
  array elements are never promoted into registers, so the values stay in memory
  and the code no longer needs a register for each one.
```

<!-- test: hot-loop-param-used-arm64 -->
<!-- targets: arm64-macos, arm64-linux -->
The arm64 twin of `hot-loop-param-used`: a PARAMETER read every iteration is part of the hot working set and must resolve to its declaration span through `ParamOriginTable` (it is minted by no op) rather than trip the Rule-3 panic. `p` is read in `s1 = s1 + i + p`, so with twenty-six accumulators, the counter, and the condition boolean it is one of twenty-nine values live against arm64's 26-GPR pool. It ranks first (`<fragment>:2:14` — the `p` token); the counter `i` ranks last. Deficit 3.
```maxon
function hot(p int) returns int
	var s1 = 1
	var s2 = 2
	var s3 = 3
	var s4 = 4
	var s5 = 5
	var s6 = 6
	var s7 = 7
	var s8 = 8
	var s9 = 9
	var s10 = 10
	var s11 = 11
	var s12 = 12
	var s13 = 13
	var s14 = 14
	var s15 = 15
	var s16 = 16
	var s17 = 17
	var s18 = 18
	var s19 = 19
	var s20 = 20
	var s21 = 21
	var s22 = 22
	var s23 = 23
	var s24 = 24
	var s25 = 25
	var s26 = 26
	var i = 0
	while i < 5 'loop'
		s1 = s1 + i + p
		s2 = s2 + i
		s3 = s3 + i
		s4 = s4 + i
		s5 = s5 + i
		s6 = s6 + i
		s7 = s7 + i
		s8 = s8 + i
		s9 = s9 + i
		s10 = s10 + i
		s11 = s11 + i
		s12 = s12 + i
		s13 = s13 + i
		s14 = s14 + i
		s15 = s15 + i
		s16 = s16 + i
		s17 = s17 + i
		s18 = s18 + i
		s19 = s19 + i
		s20 = s20 + i
		s21 = s21 + i
		s22 = s22 + i
		s23 = s23 + i
		s24 = s24 + i
		s25 = s25 + i
		s26 = s26 + i
		i = i + 1
	end 'loop'
	return s1 + s2 + s3 + s4 + s5 + s6 + s7 + s8 + s9 + s10 + s11 + s12 + s13 + s14 + s15 + s16 + s17 + s18 + s19 + s20 + s21 + s22 + s23 + s24 + s25 + s26
end 'hot'

function main() returns ExitCode
	return hot(0)
end 'main'
```
```maxoncstderr
error E5001: the loop at <fragment>:30 needs 3 more register(s) than are available
  29 values must be held in registers at once inside this loop, but
  only 26 registers are available. The values idle across the loop were already
  spilled around it at no cost; spilling any of these would put a load or store inside
  the loop body, which is exactly what this error exists to prevent.

  remove 3 of these 29 value(s) from the loop, cheapest first (ranked by uses inside the loop):
    <fragment>:2:14   used 1 time in the loop
    <fragment>:3:11   used 1 time in the loop
    <fragment>:4:11   used 1 time in the loop
    <fragment>:5:11   used 1 time in the loop
    <fragment>:6:11   used 1 time in the loop
    <fragment>:7:11   used 1 time in the loop
    <fragment>:8:11   used 1 time in the loop
    <fragment>:9:11   used 1 time in the loop
    <fragment>:10:11   used 1 time in the loop
    <fragment>:11:11   used 1 time in the loop
    <fragment>:12:12   used 1 time in the loop
    <fragment>:13:12   used 1 time in the loop
    <fragment>:14:12   used 1 time in the loop
    <fragment>:15:12   used 1 time in the loop
    <fragment>:16:12   used 1 time in the loop
    <fragment>:17:12   used 1 time in the loop
    <fragment>:18:12   used 1 time in the loop
    <fragment>:19:12   used 1 time in the loop
    <fragment>:20:12   used 1 time in the loop
    <fragment>:21:12   used 1 time in the loop
    <fragment>:22:12   used 1 time in the loop
    <fragment>:23:12   used 1 time in the loop
    <fragment>:24:12   used 1 time in the loop
    <fragment>:25:12   used 1 time in the loop
    <fragment>:26:12   used 1 time in the loop
    <fragment>:27:12   used 1 time in the loop
    <fragment>:28:12   used 1 time in the loop
    <fragment>:30:10   used 1 time in the loop
    <fragment>:29:10   used 28 times in the loop

  to fix: hold the loop's working set in an array and index it inside the loop.
  array elements are never promoted into registers, so the values stay in memory
  and the loop body no longer needs a register for each one.
```

<!-- test: hot-loop-rematerialized-constant -->
<!-- targets: x64-windows, x64-linux -->
A constant the loop uses (`let d`, read by `s14 = d - s14`) is REMATERIALIZED by the
splitter — re-emitted before its use with a FRESH ValueId — and that fresh id, minted after
parsing, has no origin of its own. When it lands in the blocking set it must NOT trip the
Rule-3 panic: it is chased through `SplitLineage` back to the original constant, so it resolves
to the `let d` literal (`<fragment>:24:11`). The remaining working set (thirteen accumulators
plus the counter) still overflows by two, and the deficit (2) never exceeds the sixteen listed
values. Regression for the fresh-rematerialized-id false panic.
```maxon
function hot() returns int
	var s1 = 1
	var s2 = 2
	var s3 = 3
	var s4 = 4
	var s5 = 5
	var s6 = 6
	var s7 = 7
	var s8 = 8
	var s9 = 9
	var s10 = 10
	var s11 = 11
	var s12 = 12
	var s13 = 13
	var s14 = 14
	var i = 0
	while i < 5 'loop'
		s1 = s1 + i
		s2 = s2 + i
		s3 = s3 + i
		s4 = s4 + i
		s5 = s5 + i
		let d = 1000000007
		s6 = s6 + i
		s7 = s7 + i
		s8 = s8 + i
		s9 = s9 + i
		s10 = s10 + i
		s11 = s11 + i
		s12 = s12 + i
		s13 = s13 + i
		s14 = d - s14
		i = i + 1
	end 'loop'
	return s1 + s2 + s3 + s4 + s5 + s6 + s7 + s8 + s9 + s10 + s11 + s12 + s13 + s14
end 'hot'

function main() returns ExitCode
	return hot()
end 'main'
```
```maxoncstderr
error E5001: the loop at <fragment>:19 needs 2 more register(s) than are available
  16 values must be held in registers at once inside this loop, but
  only 14 registers are available. The values idle across the loop were already
  spilled around it at no cost; spilling any of these would put a load or store inside
  the loop body, which is exactly what this error exists to prevent.

  remove 2 of these 16 value(s) from the loop, cheapest first (ranked by uses inside the loop):
    <fragment>:19:11   used 0 times in the loop
    <fragment>:20:11   used 0 times in the loop
    <fragment>:21:11   used 0 times in the loop
    <fragment>:22:11   used 0 times in the loop
    <fragment>:23:11   used 0 times in the loop
    <fragment>:25:11   used 0 times in the loop
    <fragment>:26:11   used 0 times in the loop
    <fragment>:27:11   used 0 times in the loop
    <fragment>:28:11   used 0 times in the loop
    <fragment>:29:13   used 0 times in the loop
    <fragment>:30:13   used 0 times in the loop
    <fragment>:31:13   used 0 times in the loop
    <fragment>:32:13   used 0 times in the loop
    <fragment>:16:12   used 1 time in the loop
    <fragment>:24:11   used 1 time in the loop
    <fragment>:17:10   used 15 times in the loop

  to fix: hold the loop's working set in an array and index it inside the loop.
  array elements are never promoted into registers, so the values stay in memory
  and the loop body no longer needs a register for each one.
```

<!-- test: hot-loop-rematerialized-constant-arm64 -->
<!-- targets: arm64-macos, arm64-linux -->
The arm64 twin of `hot-loop-rematerialized-constant`: a constant the loop uses (`let d`, read by `s26 = d - s26`) is REMATERIALIZED by the splitter with a fresh ValueId that has no origin of its own, and must be chased through `SplitLineage` back to the `let d` literal (`<fragment>:56:11`) rather than trip the Rule-3 panic. Twenty-six accumulators plus the counter plus the rematerialized constant overflow arm64's 26-GPR pool by two.
```maxon
function hot() returns int
	var s1 = 1
	var s2 = 2
	var s3 = 3
	var s4 = 4
	var s5 = 5
	var s6 = 6
	var s7 = 7
	var s8 = 8
	var s9 = 9
	var s10 = 10
	var s11 = 11
	var s12 = 12
	var s13 = 13
	var s14 = 14
	var s15 = 15
	var s16 = 16
	var s17 = 17
	var s18 = 18
	var s19 = 19
	var s20 = 20
	var s21 = 21
	var s22 = 22
	var s23 = 23
	var s24 = 24
	var s25 = 25
	var s26 = 26
	var i = 0
	while i < 5 'loop'
		s1 = s1 + i
		s2 = s2 + i
		s3 = s3 + i
		s4 = s4 + i
		s5 = s5 + i
		s6 = s6 + i
		s7 = s7 + i
		s8 = s8 + i
		s9 = s9 + i
		s10 = s10 + i
		s11 = s11 + i
		s12 = s12 + i
		s13 = s13 + i
		s14 = s14 + i
		s15 = s15 + i
		s16 = s16 + i
		s17 = s17 + i
		s18 = s18 + i
		s19 = s19 + i
		s20 = s20 + i
		s21 = s21 + i
		s22 = s22 + i
		s23 = s23 + i
		s24 = s24 + i
		s25 = s25 + i
		let d = 1000000007
		s26 = d - s26
		i = i + 1
	end 'loop'
	return s1 + s2 + s3 + s4 + s5 + s6 + s7 + s8 + s9 + s10 + s11 + s12 + s13 + s14 + s15 + s16 + s17 + s18 + s19 + s20 + s21 + s22 + s23 + s24 + s25 + s26
end 'hot'

function main() returns ExitCode
	return hot()
end 'main'
```
```maxoncstderr
error E5001: the loop at <fragment>:30 needs 2 more register(s) than are available
  28 values must be held in registers at once inside this loop, but
  only 26 registers are available. The values idle across the loop were already
  spilled around it at no cost; spilling any of these would put a load or store inside
  the loop body, which is exactly what this error exists to prevent.

  remove 2 of these 28 value(s) from the loop, cheapest first (ranked by uses inside the loop):
    <fragment>:31:11   used 0 times in the loop
    <fragment>:32:11   used 0 times in the loop
    <fragment>:33:11   used 0 times in the loop
    <fragment>:34:11   used 0 times in the loop
    <fragment>:35:11   used 0 times in the loop
    <fragment>:36:11   used 0 times in the loop
    <fragment>:37:11   used 0 times in the loop
    <fragment>:38:11   used 0 times in the loop
    <fragment>:39:11   used 0 times in the loop
    <fragment>:40:13   used 0 times in the loop
    <fragment>:41:13   used 0 times in the loop
    <fragment>:42:13   used 0 times in the loop
    <fragment>:43:13   used 0 times in the loop
    <fragment>:44:13   used 0 times in the loop
    <fragment>:45:13   used 0 times in the loop
    <fragment>:46:13   used 0 times in the loop
    <fragment>:47:13   used 0 times in the loop
    <fragment>:48:13   used 0 times in the loop
    <fragment>:49:13   used 0 times in the loop
    <fragment>:50:13   used 0 times in the loop
    <fragment>:51:13   used 0 times in the loop
    <fragment>:52:13   used 0 times in the loop
    <fragment>:53:13   used 0 times in the loop
    <fragment>:54:13   used 0 times in the loop
    <fragment>:55:13   used 0 times in the loop
    <fragment>:28:12   used 1 time in the loop
    <fragment>:56:11   used 1 time in the loop
    <fragment>:29:10   used 27 times in the loop

  to fix: hold the loop's working set in an array and index it inside the loop.
  array elements are never promoted into registers, so the values stay in memory
  and the loop body no longer needs a register for each one.
```

<!-- test: relievable-param-live-across-loop -->
A parameter LIVE ACROSS the loop but not USED inside it is cold-spillable, so high pressure
here is relieved — no E5001. `p` is read before the loop (the `k` computations) and after it
(the `return`), so it is live across the loop; but the loop body touches only `sum` and `i`.
The sixteen `k` values and `p` are all idle across the loop, so the splitter stores them
around it and the body stays two `lea`s and a `jmp`. It compiles and runs. Result is
`sum(0..4)=10 + sum(1..16)=136 + p=0 = 146`.
```maxon
function relievable(p int) returns int
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
	let k16 = p + 16
	var sum = 0
	var i = 0
	while i < 5 'loop'
		sum = sum + i
		i = i + 1
	end 'loop'
	return sum + k1 + k2 + k3 + k4 + k5 + k6 + k7 + k8 + k9 + k10 + k11 + k12 + k13 + k14 + k15 + k16 + p
end 'relievable'

function main() returns ExitCode
	return relievable(0)
end 'main'
```
```exitcode
146
```

<!-- test: dead-def-parameter-holds-a-register -->
A DEAD DEF still costs a register, and the pressure model has to say so. `a14` is never read, so
it is live at NO program point and no popcount over a live set can see it — yet `mov rax, [rbp+k]`
CLOBBERS a register whatever becomes of the value, so the colorer must hand it one. Fourteen live
parameters plus that one dead materialization is FIFTEEN registers against a pool of fourteen, at a
single op. Before `addOpTransientPressure` counted it, the splitter found no overflow at all,
declared the function relieved, and `chooseRegister` then panicked with every register blocked —
the one demand a live-set model is structurally blind to. Result is `sum(1..14) = 105`.
```maxon
function f(a0 int, a1 int, a2 int, a3 int, a4 int, a5 int, a6 int, a7 int, a8 int, a9 int, a10 int, a11 int, a12 int, a13 int, a14 int) returns int
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13
end 'f'

function main() returns ExitCode
	return f(1, a1: 2, a2: 3, a3: 4, a4: 5, a5: 6, a6: 7, a7: 8, a8: 9, a9: 10, a10: 11, a11: 12, a12: 13, a13: 14, a14: 15)
end 'main'
```
```exitcode
105
```

<!-- test: dead-def-parameter-at-the-pool-boundary -->
The BOUNDARY below `dead-def-parameter-holds-a-register`, so a regression that moves the cliff is
caught from both sides. Thirteen live parameters plus one dead materialization is exactly the pool
of fourteen: it fits, nothing is split, and the fragment must show no store and no reload. Result
is `sum(1..13) = 91`.
```maxon
function f(a0 int, a1 int, a2 int, a3 int, a4 int, a5 int, a6 int, a7 int, a8 int, a9 int, a10 int, a11 int, a12 int, a13 int) returns int
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12
end 'f'

function main() returns ExitCode
	return f(1, a1: 2, a2: 3, a3: 4, a4: 5, a5: 6, a6: 7, a7: 8, a8: 9, a9: 10, a10: 11, a11: 12, a12: 13, a13: 14)
end 'main'
```
```exitcode
91
```

<!-- test: dead-def-mid-block-holds-a-register -->
The same demand where the dead def is NOT a parameter, so the correction cannot be mistaken for a
fact about the entry block. All fourteen parameters are read by the `return`, so all fourteen are
live across `unused` — and `unused` itself is read by nothing. Its `lea` still writes a register,
and with no operand of its own dying there (`a1` and `a2` are both read later) there is none to
inherit, so the point genuinely wants fifteen. Result is `sum(1..14) = 105`.
```maxon
function f(a0 int, a1 int, a2 int, a3 int, a4 int, a5 int, a6 int, a7 int, a8 int, a9 int, a10 int, a11 int, a12 int, a13 int) returns int
	let unused = a1 + a2
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13
end 'f'

function main() returns ExitCode
	return f(1, a1: 2, a2: 3, a3: 4, a4: 5, a5: 6, a6: 7, a7: 8, a8: 9, a9: 10, a10: 11, a11: 12, a12: 13, a13: 14)
end 'main'
```
```exitcode
105
```

<!-- test: dead-def-inherits-only-its-own-register-file -->
A dead def may inherit the register of a use that DIES at the same op — but only one of its OWN FILE.
`dyingRegsAt` names every dying operand's register whatever file it is in, yet `allocateDef` ORs
`fullRegisterMask() and not classPool` into `blocked`, so a dying XMM frees nothing a GPR def can take.
Here `x as int` is a `cvttsd2si` whose GPR def is read by nothing and whose only dying operand is the
FLOAT `x`: fourteen live parameters plus that dead GPR def still want fifteen GPRs, and scoring the
dying float as "this op frees a register" put the colorer back into `chooseRegister`'s panic — the
dead-def correction's own wrong answer, one register file over. Result is `sum(1..14) = 105`.
```maxon
function f(a0 int, a1 int, a2 int, a3 int, a4 int, a5 int, a6 int, a7 int, a8 int, a9 int, a10 int, a11 int, a12 int, a13 int, x float) returns int
	let unused = x as int
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13
end 'f'

function main() returns ExitCode
	return f(1, a1: 2, a2: 3, a3: 4, a4: 5, a5: 6, a6: 7, a7: 8, a8: 9, a9: 10, a10: 11, a11: 12, a12: 13, a13: 14, x: 1.5)
end 'main'
```
```exitcode
105
```

<!-- test: dead-def-inherits-only-its-own-register-file-float -->
<!-- targets: x64-windows, x64-linux -->
The MIRROR of `dead-def-inherits-only-its-own-register-file`, so the correction is pinned in the file it
is easier to forget. `y as float` is a `cvtsi2sd` whose XMM def is read by nothing and whose only dying
operand is the INT `y`; sixteen live float parameters plus that dead XMM def want a seventeenth XMM
against the sixteen-register float pool. A class-blind "does this op free a register?" scored the dying
GPR as relief and panicked the colorer in the XMM file. Gated to x64 because it counts against x64's
sixteen-deep float pool. Every argument is `1.0`, so the sum is `16.0`.
```maxon
function g(f0 float, f1 float, f2 float, f3 float, f4 float, f5 float, f6 float, f7 float, f8 float, f9 float, f10 float, f11 float, f12 float, f13 float, f14 float, f15 float, y int) returns float
	let unused = y as float
	return f0 + f1 + f2 + f3 + f4 + f5 + f6 + f7 + f8 + f9 + f10 + f11 + f12 + f13 + f14 + f15
end 'g'

function main() returns ExitCode
	return g(1.0, f1: 1.0, f2: 1.0, f3: 1.0, f4: 1.0, f5: 1.0, f6: 1.0, f7: 1.0, f8: 1.0, f9: 1.0, f10: 1.0, f11: 1.0, f12: 1.0, f13: 1.0, f14: 1.0, f15: 1.0, y: 3) as int as ExitCode
end 'main'
```
```exitcode
16
```

<!-- test: dead-def-past-the-arm64-pool-across-register-files -->
The arm64 twin of `dead-def-inherits-only-its-own-register-file`, at the arm64 cliff: twenty-six live
integer parameters plus a dead GPR def whose only dying operand is a float. It panicked `chooseRegister`
on arm64 for exactly the reason the x64 case did, so the class filter is pinned on BOTH lanes of the
shared pressure model rather than on the one that happened to be probed. Ungated: on x64 the same
program is well past the pool and the splitter relieves it cold, which is worth pinning too. The
trailing arguments are zero so the sum fits an exit code. Result is `sum(1..20) = 210`.
```maxon
function f(a0 int, a1 int, a2 int, a3 int, a4 int, a5 int, a6 int, a7 int, a8 int, a9 int, a10 int, a11 int, a12 int, a13 int, a14 int, a15 int, a16 int, a17 int, a18 int, a19 int, a20 int, a21 int, a22 int, a23 int, a24 int, a25 int, x float) returns int
	let unused = x as int
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15 + a16 + a17 + a18 + a19 + a20 + a21 + a22 + a23 + a24 + a25
end 'f'

function main() returns ExitCode
	return f(1, a1: 2, a2: 3, a3: 4, a4: 5, a5: 6, a6: 7, a7: 8, a8: 9, a9: 10, a10: 11, a11: 12, a12: 13, a13: 14, a14: 15, a15: 16, a16: 17, a17: 18, a18: 19, a19: 20, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, x: 1.5)
end 'main'
```
```exitcode
210
```

<!-- test: dead-reuse-def-costs-one-copy-not-two -->
The dead-def transient and the reuse-copy transient are the SAME register and must be charged ONCE.
`a1 * a2` is a two-address `imul` whose dest is a REUSE of `a1`, and `a1` is live after (the `return`
reads it), so the allocator materializes `mov dest, a1` before the op — and the dest is then read by
nothing. That is one extra register at one point, not two: `addOpTransientPressure` answers in the reuse
arm and stops, rather than charging the copy and then charging the dead def again. Double-charging would
not crash, it would over-split — a store and a reload this program does not need. Result is
`sum(1..14) = 105`.
```maxon
function f(a0 int, a1 int, a2 int, a3 int, a4 int, a5 int, a6 int, a7 int, a8 int, a9 int, a10 int, a11 int, a12 int, a13 int) returns int
	let unused = a1 * a2
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13
end 'f'

function main() returns ExitCode
	return f(1, a1: 2, a2: 3, a3: 4, a4: 5, a5: 6, a6: 7, a7: 8, a8: 9, a9: 10, a10: 11, a11: 12, a12: 13, a13: 14)
end 'main'
```
```exitcode
105
```

<!-- test: dead-def-parameter-past-the-arm64-pool -->
The arm64 twin of the boundary. arm64 allocates from 26 GPRs (x0-x15 ∪ x19-x28), so the dead-def
cliff sits at 27 where x64's sits at 15 — twenty-six live parameters plus one dead materialization
is one past the arm64 pool, and it panicked `chooseRegister` there for exactly the reason it did on
x64. It is not gated to arm64: on x64 the same program is simply well past the pool and the
splitter relieves it cold, which is worth pinning too. The trailing arguments are zero so the sum
fits an exit code while the first twenty stay distinct — a swapped register still changes the
answer. Result is `sum(1..20) = 210`.
```maxon
function f(a0 int, a1 int, a2 int, a3 int, a4 int, a5 int, a6 int, a7 int, a8 int, a9 int, a10 int, a11 int, a12 int, a13 int, a14 int, a15 int, a16 int, a17 int, a18 int, a19 int, a20 int, a21 int, a22 int, a23 int, a24 int, a25 int, a26 int) returns int
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15 + a16 + a17 + a18 + a19 + a20 + a21 + a22 + a23 + a24 + a25
end 'f'

function main() returns ExitCode
	return f(1, a1: 2, a2: 3, a3: 4, a4: 5, a5: 6, a6: 7, a7: 8, a8: 9, a9: 10, a10: 11, a11: 12, a12: 13, a13: 14, a14: 15, a15: 16, a16: 17, a17: 18, a18: 19, a19: 20, a20: 0, a21: 0, a22: 0, a23: 0, a24: 0, a25: 0, a26: 0)
end 'main'
```
```exitcode
210
```
