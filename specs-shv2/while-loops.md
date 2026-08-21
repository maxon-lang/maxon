---
feature: while-loops
status: selfhosted
keywords: [while, loop, iteration, control flow, break, continue]
category: control-flow
milestone: M4b
---

# While Loops

## Documentation

Execute a block repeatedly while a comparison condition is true:

```maxon
while <condition> 'identifier'
	<statements>
end 'identifier'
```

Lowering (M4b) — the first BACKWARD-branching CFG. The current block becomes the
PREHEADER and branches unconditionally into a fresh HEADER. The header re-evaluates
the condition (a comparison, fused into `cmp`+`jcc` exactly as an `if` — see
`specs-shv2/comparison-operators.md`) and takes a two-way branch to the BODY or the
EXIT. The body ends with a BACK-EDGE branch to the header; `break` branches to the
exit, `continue` to the header. Intra-function backward `jmp`/`jcc` rel32 are patched
by `resolveBlockJumps`, the same path forward `if` jumps use.

On-the-fly SSA carries each mutable variable through the header as a **phi**
(block-arg); a loop-carried var whose update feeds only the back-edge coalesces to a
single register (no move), while a var also read inside the loop (an induction
variable) keeps a distinct register and an explicit phi-elimination move. The M4b
condition must be a comparison — a bare boolean condition (`while true`) needs the
boolean-value support deferred past M4b.

## Tests

The M4b slice of `specs/while-loops.md` that fits the placeholder register allocator
(each distinct SSA value takes its own GPR from a 6-register pool): a
zero-iteration loop, and a loop whose body carries a `continue`. The register-heavy
`while-loops.basic` (two loop-carried vars plus a `break`, which needs a loop-exit
phi), the `while true` loop (`while-loops.break`), and the `mod`-using
`nested-control` are DEFERRED under `## Deferred`.

<!-- test: while-loops.zero-iterations -->
```maxon
function main() returns ExitCode
	var x = 10
	while x < 5 'loop'
		x = x + 1
	end 'loop'
	return x
end 'main'
```
```exitcode
10
```

<!-- test: while-loops.continue -->
```maxon
function main() returns ExitCode
	var sum = 0
	var i = 0
	while i < 5 'loop'
		i = i + 1
		if i == 3 'skip'
			continue
		end 'skip'
		sum = sum + i
	end 'loop'
	return sum
end 'main'
```
```exitcode
12
```


<!-- test: while-loops.sequential-loops -->
Two loops one after another, each with its own counter. The second loop's blocks are reachable
only after the first loop's exit, so nothing from one is live in the other. `i0` counts to 3 and
`i1` counts to 3, so `3 + 3 = 6`.
```maxon
function big(n int) returns int
	var i0 = 0
	while i0 < n 'L0'
		i0 = i0 + 1
	end 'L0'
	var i1 = 0
	while i1 < n 'L1'
		i1 = i1 + 1
	end 'L1'
	return i0 + i1
end 'big'

function main() returns ExitCode
	return big(3)
end 'main'
```
```exitcode
6
```

<!-- test: while-loops.sequential-loops-across-a-call -->
**A register-allocator regression test, and the reason it exists is worth stating.** Two sequential
loops, each CALLING a function and each carrying an accumulator, plus a `total` that is live ACROSS
both loops. Every loop-carried value here is live across a call, so it is forbidden all nine
caller-saved registers and can only live in one of the **five callee-saved** ones — while `total`,
which merely passes through, is not.

The allocator used to die on exactly this shape (`chooseRegister: no free register`). Its biased
coloring would honour a copy hint that handed a **callee-saved** register to a value that did not
need one, and a value that could live *nowhere else* then found none. Five values needing the five
scarce registers is a perfect fit — it is only reachable if nothing wastes one. This is the case the
chordal-exactness argument does NOT cover: with forbidden sets the problem is LIST colouring, which is
NP-hard, so protecting the scarce class is a MITIGATION and not an exactness rule — the residue it
cannot prevent is repaired by `SplitDriver.repairAtExhaustion`. (`HallCondition.hallVerdictAt`'s
header carries that argument and is the one place it is made.)

`work(x) = x + 1`, so each loop adds `1+2+3 = 6` to its accumulator over three iterations:
`a = 1 + 6 = 7`, `b = 1 + 6 = 7`, and `total = 0 + 7 + 7 = 14`.
```maxon
function work(x int) returns int
	return x + 1
end 'work'

function big(n int) returns int
	var total = 0
	var a = 1
	var i0 = 0
	while i0 < n 'L0'
		let r = work(i0)
		a = a + r
		i0 = i0 + 1
	end 'L0'
	total = total + a
	var b = 1
	var i1 = 0
	while i1 < n 'L1'
		let s = work(i1)
		b = b + s
		i1 = i1 + 1
	end 'L1'
	total = total + b
	return total
end 'big'

function main() returns ExitCode
	return big(3)
end 'main'
```
```exitcode
14
```

<!-- test: while-loops.sequential-loops-dead-phis -->
**The false-`E5001` regression test.** Two sequential loops, each calling a function, each carrying
SIX accumulators — and the first loop's accumulators are DEAD by the time the second loop starts.

This shape used to be rejected outright: *"17 values must be held in registers at once inside this
loop, but only 14 registers are available"*, with the first loop's accumulators ranked first among
the values to delete — described as **"used 0 times in the loop"**, which is the tell. They were not
used in that loop. They were not used anywhere. The real working set is **9**.

The cause was in the front end, not the allocator. On-the-fly SSA must mint a loop header's phis
BEFORE it parses the body, so it mints one per mutable var IN SCOPE — and a phi for a var the loop
never touches is SELF-SUSTAINING: the back edge passes it to itself, so it *has* a use, and liveness
holds it live around the entire loop. Seven of them (six accumulators plus the first loop's counter)
inflated `maxlive` by seven, the splitter forced-spilled them around the second loop's call — a
store AND a reload every iteration, for values nothing reads — and past 14 the compiler raised
`E5001` against a program that fits the machine comfortably. `pruneDeadBlockArgs` deletes them.

**A false `E5001` is the worst bug this compiler can have** (it sends an author to restructure code
that was fine, and can break an agent's convergence loop), so this test is a gate on the whole
contract, not on one loop shape.

`work(x) = x + 1`, so each loop adds `1+2+3 = 6` to each of its accumulators over three iterations.
Per loop the accumulators start at `1..6` and finish at `7..12`, summing to `21 + 36 = 57`; two
loops give `total = 114`.
```maxon
function work(x int) returns int
	return x + 1
end 'work'

function big(n int) returns int
	var total = 0
	var a0 = 1
	var a1 = 2
	var a2 = 3
	var a3 = 4
	var a4 = 5
	var a5 = 6
	var i0 = 0
	while i0 < n 'L0'
		let r = work(i0)
		a0 = a0 + r
		a1 = a1 + r
		a2 = a2 + r
		a3 = a3 + r
		a4 = a4 + r
		a5 = a5 + r
		i0 = i0 + 1
	end 'L0'
	total = total + a0 + a1 + a2 + a3 + a4 + a5
	var b0 = 1
	var b1 = 2
	var b2 = 3
	var b3 = 4
	var b4 = 5
	var b5 = 6
	var i1 = 0
	while i1 < n 'L1'
		let s = work(i1)
		b0 = b0 + s
		b1 = b1 + s
		b2 = b2 + s
		b3 = b3 + s
		b4 = b4 + s
		b5 = b5 + s
		i1 = i1 + 1
	end 'L1'
	total = total + b0 + b1 + b2 + b3 + b4 + b5
	return total
end 'big'

function main() returns ExitCode
	return big(3)
end 'main'
```
```exitcode
114
```


<!-- test: while-loops.break -->
```maxon
function main() returns ExitCode
	var x = 5
	while true 'loop'
		x = x + 2
		if x == 11 'check'
			break
		end 'check'
	end 'loop'
	return x
end 'main'
```
```exitcode
11
```


<!-- test: while-loops.basic -->
```maxon
function main() returns ExitCode
	var x = 5
	var i = 3
	while i > 0 'loop'
		x = x + 2
		i = i - 1
		if i == 0 'check'
			break
		end 'check'
	end 'loop'
	return x
end 'main'
```
```exitcode
11
```

<!-- test: while-loops.carried-var-assigned-only-inside-an-if -->
**The guard on WHEN a loop-header phi is minted, and it is a wrong-answer test, not a crash test.**

A loop carries a phi only for the mutable vars its body ASSIGNS (`Parser.parseWhileStatement` reads
them off the tokens). The tempting way to compute that set is *lazily* — mint the phi at the var's
first mention while parsing the body — and this program is why that is **unsound**.

`parseIfStatement` snapshots every mutable var's value into a local `ValueId` array *before* it parses
the then-branch. Here that snapshot is taken while `x` still has no phi, so it captures `x`'s
**pre-loop** value. A phi minted later, when `x = 2` is finally reached, is invisible to it — and the
`if`'s false edge would then carry `1` into the merge, resetting `x` on every iteration where `i != 1`.
The loop would return **1**. The snapshot lives on the parser's call stack, so nothing can retroactively
patch it; **minting eagerly, before the body is parsed, is what makes every snapshot inside it true.**

`x` becomes 2 on the iteration `i == 1` and must STAY 2 through `i == 2`.
```maxon
function main() returns ExitCode
	var x = 1
	var i = 0
	while i < 3 'l'
		if i == 1 'b'
			x = 2
		end 'b'
		i = i + 1
	end 'l'
	return x
end 'main'
```
```exitcode
2
```

<!-- test: while-loops.loop-reads-a-var-it-never-assigns -->
**A var the loop READS but never ASSIGNS gets no phi at all — and that is correct, not a shortcut.**

Its value cannot change across iterations, so its pre-loop definition **dominates** the header and the
read binds it directly. (This is why the carried set is keyed on *assignments* rather than *mentions*:
once minting is eager, a read needs no phi to be correct.)

`limit` is 6, so the loop runs 6 times adding 6 each: `acc = 36`.
```maxon
function main() returns ExitCode
	var limit = 0
	limit = 6
	var i = 0
	var acc = 0
	while i < limit 'l'
		acc = acc + limit
		i = i + 1
	end 'l'
	return acc
end 'main'
```
```exitcode
36
```

<!-- test: while-loops.inner-declaration-shadows-a-carried-var -->
**The token scan OVER-approximates, on purpose, and this is the case that proves it is harmless.**

The inner `var t` shadows the outer one, and at token level `t =` is indistinguishable from a
reassignment — so the OUTER `t` is given a header phi it does not need. That is the safe direction: a
surplus phi is φ(preheader: v, back-edge: v), which `elimTrivialBlockArgs` folds away and biased coloring
would coalesce regardless. A MISSING phi would be a silent miscompile, so the scan is built to err this
way and never the other.

The outer `t` is never written INSIDE THE LOOP, so it must still be 100. (Its `t = 100` one line above
the loop is what keeps it a `var` at all: a `var` nothing ever assigns is E3077, and this case needs it to
be a `var` to be given the surplus phi at all.)
```maxon
function main() returns ExitCode
	var t = 0
	t = 100
	var i = 0
	while i < 3 'l'
		var t = 0
		t = i + 1
		i = i + t
	end 'l'
	return t
end 'main'
```
```exitcode
100
```

<!-- test: while-loops.loop-carried-var-narrower-than-its-update -->
**A loop-carried var whose declared width differs from a value assigned into it — a wasm codegen
regression test.** `r` is seeded from an `ExitCode` value (a narrow ranged int) but is then assigned
the plain-`int` counter `i` inside the loop. int→int width changes carry NO IR conversion op (the
value just flows), so `r`'s header phi and the `i` an edge assigns it end up different WIDTHS. The
register backends never notice — every value sits in a 64-bit register — but wasm's typed locals do:
the phi-edge copy must coerce, exactly as a `return` or a call argument does. Without the coercion the
emitted core module fails validation (`type mismatch: expected i64, found i32`); the native hosts, and
this run, all return 2 (`r` follows `i` = 0,1,2, stopping when `i` reaches 3).
```maxon
function firstExit() returns ExitCode
	return 0
end 'firstExit'

function pick() returns ExitCode
	var r = firstExit()
	var i = 0
	while i < 3 'lp'
		r = i
		i = i + 1
	end 'lp'
	return r
end 'pick'

function main() returns ExitCode
	return pick()
end 'main'
```
```exitcode
2
```


<!-- test: nested-control -->
```maxon
function main() returns ExitCode
	var result = 0
	var i = 0
	while i < 3 'outer'
		var j = 0
		while j < 3 'inner'
			if (i + j) mod 2 == 0 'even'
				result = result + 1
			end 'even'
			j = j + 1
		end 'inner'
		i = i + 1
	end 'outer'
	return result
end 'main'
```
```exitcode
5
```

<!-- test: while-loops.counter-survives-a-narrow-ranged-neighbour -->
A loop counter must survive a NARROW RANGED local allocated next to it.

Each variable gets a stack slot, and the slot has to hold the widest access made to it. A local whose
type is a narrow ranged int (`SlotTally` below is `int(0 to 16)`, and the arithmetic over it stays
narrow) once got a FOUR-byte slot while the stores and loads reaching it were EIGHT bytes wide — so the
overrun landed on whatever was allocated next to it. Here that neighbour is `pass`, the loop counter:
every iteration reset it, `pass < 2` never went false, and the loop ran forever.

The guard turns that into a distinguishable exit code rather than a hang, so the case fails loudly
instead of timing out. `99` means the counter was corrupted; `2` is the two passes the loop owes.
```maxon
typealias SlotTally = int(0 to 16)

enum Pair
	first
	second
end 'Pair'

function liveCount(both bool) returns SlotTally
	var n = 0
	for c in Pair.allCases 'walk'
		if c == Pair.first or both 'live'
			n = n + 1
		end 'live'
	end 'walk'
	return n
end 'liveCount'

function main() returns ExitCode
	let scaled = 20 * (liveCount(false) + 1)
	let seed = 100 + scaled

	var guard = 0
	var pass = 0
	while pass < 2 'passes'
		var running = seed
		for c in Pair.allCases 'inner'
			if c == Pair.first 'one'
				running = running + 1
			end 'one'
		end 'inner'
		// ⚠ `running` is NARROW-typed, so it takes a 4-byte store while this read is 8 bytes wide — the
		// high half is slot padding. Checking it here means that padding can never be depended on
		// SILENTLY: if it is ever non-zero this case says 98 instead of quietly computing the right
		// answer. (Measured: exactly one variable in the whole bootstrap suite has this store/load
		// width pair, and it is this one.)
		if running != 141 'paddingLeakedIn'
			return 98
		end 'paddingLeakedIn'

		guard = guard + running
		pass = pass + 1
		if guard > 2000 'runaway'
			return 99
		end 'runaway'
	end 'passes'

	return pass
end 'main'
```
```exitcode
2
```
