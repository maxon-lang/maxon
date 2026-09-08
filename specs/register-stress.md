---
feature: register-stress
status: selfhosted
keywords: [register-allocator, e5001, false-positive, trivial-phi, hall, nested-loops, break, forced-spill, boundary]
category: register-allocator
milestone: M5.15
---

# Allocator stress: pressure boundaries, Hall's condition, and the false E5001

## Documentation

A FALSE E5001 is the worst bug this compiler can have. The diagnostic's whole value is that
it means "this program genuinely does not fit the machine" — the moment it can fire on a
program that DOES fit, it stops being information and becomes noise the user must work
around. So the tests here cluster on the BOUNDARIES, where a false positive lives: exactly at
the pool, exactly at the callee-saved limit, and at the shapes where a value looks unspillable
but is not.

### `let` and `var` must compile the same

The parser builds SSA on the fly, and a loop header's phis must be minted BEFORE the body is
parsed — the set of vars the body writes is not yet known — so it mints one phi per MUTABLE var
in scope. Every `var` declared before a loop therefore becomes a loop-carried phi, whether or
not the loop touches it.

`pruneDeadBlockArgs` deletes the ones nothing reads. The ones that ARE read — after the loop —
survived, on the reasoning that a phi carrying an unchanging value costs nothing, since biased
coloring coalesces it with its incoming value into one register.

That is true of the register COUNT and false of everything else, because **a phi used to be exactly
what the cold-spill splitter may not spill.** `isColdSpillable` refused any phi and any edge-passed
value outright, so an idle `var` was PINNED in a register across a loop that never touched it, while
an idle `let` — an ordinary value — was spilled around that loop for free. Fifteen of them and the
compiler refused a program whose real working set was two, ranking values it could have spilled
among the ones the user should delete.

⚠ **Those two blanket bars are GONE (BATCH2), and this section is kept because it is why the FOLD
below exists — not because the bars still stand.** They were over-approximations of "touches a loop"
made while no loop depth was recorded for a phi's block or for an edge use, and each was in turn a
false-E5001 generator in its own right (measured on `stdlib/URL.maxon`'s `URL.parse`). `isColdSpillable`
now asks ONE question — is the value's def, and every use of it, at loop depth 0? — with a phi's block
entry and a branch-edge arg carrying a depth like any op does. A loop-header phi and a loop-carried
edge arg are still refused, by that depth. The tests below are unchanged and still pass: the fold is
the better answer for an IDLE var either way, because a folded phi costs no slot and no reload at all.

`elimTrivialBlockArgs` folds away a phi whose every incoming value, self-references discounted,
is the same value: `phi = φ(v, phi)` IS `v`. `idle-vars-across-a-loop` below is the program that
forced it, and `idle-lets-across-a-loop` is its control — the two are semantically identical and
must now compile identically.

### Forced is not the same as hot

Rule 2 refuses the SEARCH, not the SPILL. Where a placement is FORCED — a value live across a
call has only the five callee-saved registers, and a sixth must go to memory — the compiler emits
the bracket at ANY loop depth, because the ABI already made the decision and there is nothing to
search. E5001 is for a full-pool overflow with no cold-spillable victim, where choosing what to
sacrifice IS the search. `five-across-call-in-loop` and `six-across-call-in-loop` are the two
sides of that boundary.

### Hall's condition, on masks that are not laminar

`maxPressure ≤ pool` is necessary but not sufficient. A value live across a call is forbidden the
nine caller-saved registers for its WHOLE range, not merely at the call — so six such values
collide at a point that is not itself a call and where nothing looks wrong. The splitter therefore
tests Hall's condition: no register subset may hold more values confined to it than it has
registers. The forbidden masks are NOT laminar (three values can have pairwise-incomparable,
equal-sized allowed sets and still be jointly colorable), so the O(16) cardinality prefix-sum is a
SCREEN only, and an exact matching must confirm. A screen without the confirm would spill programs
that did not need it.

Every test is self-verifying: `0` on an exact match, `99` otherwise.

## Tests

<!-- test: idle-lets-across-a-loop -->
The CONTROL. Fifteen `let` bindings are computed before a loop, never touched inside it, and
summed after. They are ordinary values, so the cold-spill splitter stores the excess in the
PREHEADER and reloads them after the loop, adding nothing to the body. This has always worked;
it is here so the next test has something to be identical to.
`k1..k15 = 1..15` sum to 120; the loop adds `0+1+2+3 = 6`. So `126`.
```maxon
function idleLets(p Integer) returns Integer
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
end 'idleLets'

function main() returns ExitCode
	let r = idleLets(0)
	if r == 126 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: idle-vars-across-a-loop -->
THE FALSE E5001, and the program that forced `elimTrivialBlockArgs`. The test above with `let`
replaced by `var` — each declared then assigned once, before the loop, because E3077 refuses a
`var` that is never reassigned at all. What matters is unchanged and is the whole point: **not one
of them is assigned INSIDE the loop**, so the two programs are SEMANTICALLY IDENTICAL across it and
must compile identically.

They did not. The parser mints a loop-header phi per mutable var in scope — it must, since the
body has not been parsed yet — so each of these fifteen became a loop-carried phi, self-sustaining
through the back edge and live across the whole loop. `pruneDeadBlockArgs` could not remove them
(they ARE read, after the loop), and `isColdSpillable` refuses to spill a phi (a cold store goes
in the preheader; a phi's def is the header). Fifteen unspillable values, a pool of fourteen, and
the compiler refused a loop whose actual working set is `sum` and `i`:

  error E5001: the loop at …:20 needs 3 more register(s) than are available

`elimTrivialBlockArgs` folds each of them away — a phi whose only non-self incoming value is `k`
IS `k` — leaving ordinary values the splitter spills around the loop exactly as it does for `let`.
Same answer, 126.
```maxon
function idleVars(p Integer) returns Integer
	var k1 = 0
	k1 = p + 1
	var k2 = 0
	k2 = p + 2
	var k3 = 0
	k3 = p + 3
	var k4 = 0
	k4 = p + 4
	var k5 = 0
	k5 = p + 5
	var k6 = 0
	k6 = p + 6
	var k7 = 0
	k7 = p + 7
	var k8 = 0
	k8 = p + 8
	var k9 = 0
	k9 = p + 9
	var k10 = 0
	k10 = p + 10
	var k11 = 0
	k11 = p + 11
	var k12 = 0
	k12 = p + 12
	var k13 = 0
	k13 = p + 13
	var k14 = 0
	k14 = p + 14
	var k15 = 0
	k15 = p + 15
	var sum = 0
	var i = 0
	while i < 4 'loop'
		sum = sum + i
		i = i + 1
	end 'loop'
	return sum + k1 + k2 + k3 + k4 + k5 + k6 + k7 + k8 + k9 + k10 + k11 + k12 + k13 + k14 + k15
end 'idleVars'

function main() returns ExitCode
	let r = idleVars(0)
	if r == 126 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: idle-vars-across-nested-loops -->
The same folding, but the phis CHAIN. In nested loops the inner header's phi for `k` is fed by the
OUTER header's phi for `k`, which is fed by `k`'s real def — so the inner one only becomes trivial
once the outer one has been folded away. That is why the fold is a FIXPOINT rather than a single
sweep; one pass over the function would leave every inner phi in place.
`k1..k12 = 1..12` sum to 78. The inner loop adds `j = 0,1` (so 1) per outer iteration, and the
outer runs `i = 0,1,2` — so the loop contributes `3 · 1 = 3`. Total `78 + 3 = 81`.
```maxon
function nestedIdle(p Integer) returns Integer
	var k1 = 0
	k1 = p + 1
	var k2 = 0
	k2 = p + 2
	var k3 = 0
	k3 = p + 3
	var k4 = 0
	k4 = p + 4
	var k5 = 0
	k5 = p + 5
	var k6 = 0
	k6 = p + 6
	var k7 = 0
	k7 = p + 7
	var k8 = 0
	k8 = p + 8
	var k9 = 0
	k9 = p + 9
	var k10 = 0
	k10 = p + 10
	var k11 = 0
	k11 = p + 11
	var k12 = 0
	k12 = p + 12
	var sum = 0
	var i = 0
	while i < 3 'outer'
		var j = 0
		while j < 2 'inner'
			sum = sum + j
			j = j + 1
		end 'inner'
		i = i + 1
	end 'outer'
	return sum + k1 + k2 + k3 + k4 + k5 + k6 + k7 + k8 + k9 + k10 + k11 + k12
end 'nestedIdle'

function main() returns ExitCode
	let r = nestedIdle(0)
	if r == 81 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: idle-vars-past-a-break-in-a-branch -->
**The fold chain must be re-asked at EVERY link, not just the first.** Character for character
`idle-vars-across-a-loop`, with the loop moved inside an `if` and given a `break`. Both additions
are load-bearing, and each one adds a link to the chain the fold has to walk:

- a `break` makes the loop's exit block MERGE the condition-false path with the break path, so the
  parser mints an EXIT phi per var in scope (`Parser.wireLoopExit`) on top of the header phi;
- the enclosing `if` makes the continuation MERGE the branch that ran the loop with the one that
  did not, so it mints a THIRD phi per var (`Parser.mergeAtContinuation`).

So each idle `k` now carries `merge ← exit ← header ← k`, and only the header phi is trivial to
begin with. The continuation's merge phi reads the EXIT phi, not the header phi — so a fold that
notifies only the phis reading the value it just folded reaches the exit phi and stops. The merge
phi becomes `φ(k, k)` and is never asked again. Fifteen of those survive, they are phis and
therefore unspillable, and the compiler refuses a program whose real working set is two:

  error E5001: the loop at …:25 needs 4 more register(s) than are available
    …:5:29   read 0 times in the loop

which is the same false E5001, from the same cause, arrived at down a longer chain. A worklist
must therefore RE-FILE a folded phi's readers onto the value it folded TO, so the next fold in the
chain re-asks them; see `ElimTrivialBlockArgs.FeedTriggers`. Same fifteen vars, same answer, 126.
```maxon
function gate(p Integer) returns bool
	return p >= 0
end 'gate'

function idleVarsPastABreak(p Integer) returns Integer
	var k1 = 0
	k1 = p + 1
	var k2 = 0
	k2 = p + 2
	var k3 = 0
	k3 = p + 3
	var k4 = 0
	k4 = p + 4
	var k5 = 0
	k5 = p + 5
	var k6 = 0
	k6 = p + 6
	var k7 = 0
	k7 = p + 7
	var k8 = 0
	k8 = p + 8
	var k9 = 0
	k9 = p + 9
	var k10 = 0
	k10 = p + 10
	var k11 = 0
	k11 = p + 11
	var k12 = 0
	k12 = p + 12
	var k13 = 0
	k13 = p + 13
	var k14 = 0
	k14 = p + 14
	var k15 = 0
	k15 = p + 15
	var sum = 0
	var i = 0
	if gate(p) 'guard'
		while i < 100 'loop'
			sum = sum + i
			i = i + 1
			if i > 3 'done'
				break
			end 'done'
		end 'loop'
	end 'guard'
	return sum + k1 + k2 + k3 + k4 + k5 + k6 + k7 + k8 + k9 + k10 + k11 + k12 + k13 + k14 + k15
end 'idleVarsPastABreak'

function main() returns ExitCode
	let r = idleVarsPastABreak(0)
	if r == 126 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: five-across-call-in-loop -->
The LOWER side of the callee-saved boundary — the case that must need NO bracket at all.

Counting the values live across the call is the whole exercise, and it is easy to get wrong: the
loop COUNTER is one of them. `i` is read after the call (`i = i + 1`) and carried around the back
edge, so it is live across `bump` exactly as the accumulators are. FOUR accumulators plus the
counter is FIVE values live across the call — precisely the five callee-saved registers — so every
one of them gets a register and the splitter emits nothing. Add a fifth accumulator and the
counter becomes a sixth value with nowhere to go, which is the next test's business.

A spurious store/reload here would be the OVER-spill direction, and nothing else in the corpus
pins it: the program would still compute the right answer, and only the golden would notice.
`a1..a4 = 1..4`, each gaining `bump(i) = i + 1` for `i = 0,1,2`, i.e. `+6`. So they end at
`7, 8, 9, 10`, summing to 34.
```maxon
function bump(x Integer) returns Integer
	return x + 1
end 'bump'

function fourAcross(p Integer) returns Integer
	var a1 = p + 1
	var a2 = p + 2
	var a3 = p + 3
	var a4 = p + 4
	var i = 0
	while i < 3 'loop'
		let d = bump(i)
		a1 = a1 + d
		a2 = a2 + d
		a3 = a3 + d
		a4 = a4 + d
		i = i + 1
	end 'loop'
	return a1 + a2 + a3 + a4
end 'fourAcross'

function main() returns ExitCode
	let r = fourAcross(0)
	if r == 34 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: seven-across-call-in-loop -->
The UPPER side, with a deficit of THREE. Seven accumulators plus the loop counter are EIGHT values
live across the call (the counter counts — see the test above), and only five callee-saved
registers survive one, so three must go to memory. The placement is FORCED: the ABI made the
decision, there is nothing to search, and Rule 2 says emit the bracket at ANY loop depth rather
than refuse. This must NOT be E5001 and it must not panic — a store and a reload inside the loop
body are the correct answer here, and the golden pins them.
`a1..a7 = 1..7`, each gaining `bump(i) = i + 1` for `i = 0,1,2`, i.e. `+6`. So they end at
`7..13`, summing to 70.
```maxon
function bump(x Integer) returns Integer
	return x + 1
end 'bump'

function sevenAcross(p Integer) returns Integer
	var a1 = p + 1
	var a2 = p + 2
	var a3 = p + 3
	var a4 = p + 4
	var a5 = p + 5
	var a6 = p + 6
	var a7 = p + 7
	var i = 0
	while i < 3 'loop'
		let d = bump(i)
		a1 = a1 + d
		a2 = a2 + d
		a3 = a3 + d
		a4 = a4 + d
		a5 = a5 + d
		a6 = a6 + d
		a7 = a7 + d
		i = i + 1
	end 'loop'
	return a1 + a2 + a3 + a4 + a5 + a6 + a7
end 'sevenAcross'

function main() returns ExitCode
	let r = sevenAcross(0)
	if r == 70 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: hall-nonlaminar-confined-by-different-calls -->
Hall's condition on masks that are NOT laminar. Each of `v1`..`v6` is used AFTER a call in its OWN
arm of an `else if` chain, so each is live across a DIFFERENT call and each is confined to the five
callee-saved registers. Liveness is path-sensitive, so no single call has more than one of them
live across it and every reduced-pool test AT a clobber op passes. Yet all six are simultaneously
live at the chain's FIRST comparison, where the nominal pool is the full fourteen and nothing looks
wrong at all. Six values, five registers: the point does not colour.

This is why the splitter tests Hall's condition on the per-value effective pools rather than a live
count against one number — and why the O(16) cardinality screen must be CONFIRMED by an exact
matching, since these masks are pairwise incomparable rather than nested. One value is relieved
with a forced bracket, which is NOT E5001: the program fits the machine.
`pick(k) = (k + 11k) + sink(k)`, so the six arms give `13, 26, 39, 52, 65, 78`, summing to 273.
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
		let t2 = sink(2)
		out = v2 + t2
	end 'b2' else if k == 3 'b3'
		let t3 = sink(3)
		out = v3 + t3
	end 'b3' else if k == 4 'b4'
		let t4 = sink(4)
		out = v4 + t4
	end 'b4' else if k == 5 'b5'
		let t5 = sink(5)
		out = v5 + t5
	end 'b5' else 'b6'
		let t6 = sink(6)
		out = v6 + t6
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
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: break-out-of-a-pressured-loop -->
A `break` out of a loop under pressure. The early exit is a SECOND edge out of the loop, carrying
the loop-carried phis to the exit block alongside the normal exit edge — so the exit block has two
predecessors with two arg vectors, and the splitter's reloads must dominate the uses on BOTH paths.
Twelve values are idle across the loop and summed after it, so the splitter is also spilling around
a loop that now has two ways out.
`k1..k12 = 1..12` sum to 78. The loop breaks when `sum` first exceeds 6: `sum` goes 0, 1, 3, 6, 10
— at `i = 4`, `sum = 10 > 6`, so it breaks with `sum = 10`. Total `10 + 78 = 88`.
```maxon
function breakUnderPressure(p Integer) returns Integer
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
	var sum = 0
	var i = 0
	while i < 100 'loop'
		sum = sum + i
		i = i + 1
		if sum > 6 'stop'
			break
		end 'stop'
	end 'loop'
	return sum + k1 + k2 + k3 + k4 + k5 + k6 + k7 + k8 + k9 + k10 + k11 + k12
end 'breakUnderPressure'

function main() returns ExitCode
	let r = breakUnderPressure(0)
	if r == 88 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: nested-loops-pressure-at-depth-two -->
Pressure at loop depth TWO. Twelve values are idle across BOTH loops, and the accumulator work
happens in the inner one — so a cold spill must be hoisted clear out to depth 0, not merely to the
outer loop's preheader (which is still inside nothing, but the inner loop's preheader is at depth
1 and a store there would run once per outer iteration). `assertPlacementLegal` panics on a COLD
store landing at loop depth ≠ 0, so a placement bug here is a hard failure rather than slow code.
`k1..k12 = 1..12` sum to 78. The inner loop adds `j = 0,1,2` (so 3) per outer iteration; the outer
runs `i = 0,1,2` — so `3 · 3 = 9`. Total `78 + 9 = 87`.
```maxon
function depthTwo(p Integer) returns Integer
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
	var sum = 0
	var i = 0
	while i < 3 'outer'
		var j = 0
		while j < 3 'inner'
			sum = sum + j
			j = j + 1
		end 'inner'
		i = i + 1
	end 'outer'
	return sum + k1 + k2 + k3 + k4 + k5 + k6 + k7 + k8 + k9 + k10 + k11 + k12
end 'depthTwo'

function main() returns ExitCode
	let r = depthTwo(0)
	if r == 87 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
