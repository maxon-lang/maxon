---
feature: branch-cleanup
status: stable
keywords: [branch, jump, fallthrough, threading, unreachable, condition-code, codegen]
category: codegen
---

# Branch cleanup on fully allocated code

## Documentation

Every backend that allocates registers runs one Target-tier CFG cleanup between the
allocator and the frame pass. It performs four transforms, and each one deletes something:

1. **Jump elision** — a block whose terminator is the ISA's unconditional branch to the
   block laid down immediately after it loses the branch and falls through instead.
2. **Conditional inversion** — a conditional branch to the NEXT block followed by an
   unconditional branch to the else target becomes the **complemented** condition aimed at
   the else target, plus a fall-through. Two instructions become one.
3. **Jump threading** — a block whose only op is an unconditional branch is bypassed:
   every branch naming it is re-pointed at its target.
4. **Unreachable-block elimination** — blocks with no path from the entry are dropped.

### Where it runs, and why nothing earlier would do

It runs on **fully register-allocated physical ops**, after SSA destruction has placed (or
declined to place) each phi edge's parallel copies. That is the whole correctness argument
for threading: before SSA destruction, a block whose only op is a branch is not empty — it
is an edge that is *about to receive* moves, and bypassing it would delete copies that had
not been written yet. Afterwards, "the block's only op is a branch" is literally true and
permanent, so a critical-edge block that received copies holds them and is left alone,
while one that received none is genuinely empty and is threaded away.

It **never reorders blocks**. It only deletes, so every surviving block keeps its position
relative to every other — which matters because a block with no terminator op falls through
*physically*, an edge the ops-derived CFG cannot see.

### The condition complement is a FLAG complement, not a predicate complement

Inversion needs the code that is taken exactly when the original is not. For both ISAs that
is an exact partition of the flag state — CF / not-CF, ZF / not-ZF, `N==V` / `N!=V`, and De
Morgan for the two-flag codes — so for **any** flags, including the NZCV an AArch64 `fcmp`
leaves on an unordered pair and the `ZF=PF=CF=1` an x86 `ucomisd` leaves, exactly one of a
pair is taken.

⚠ **That is not the same as complementing the source predicate, and on floats the two
disagree.** AArch64's float `<` is `mi`, whose flag complement is `pl` — and `pl` reads TRUE
on NaN. That is correct here and would be wrong in a lowering: float `<` is FALSE on NaN, so
its else-arm is TAKEN on NaN, which is exactly what `pl` says. A cleanup that reached for the
*predicate* opposite (`ge`) instead would send every NaN down the wrong arm.

### These cases pin an ANSWER, not an instruction

An optimization does not change what a program computes, so nothing here can go red for a
missing transform — a suite of instruction strings would only restate the emitter. What the
cases pin is that each branch shape still selects the arm it selected before: an inverted
condition, a threaded edge or a dropped block that gets it wrong routes control to the other
arm, and every arm below carries a distinct exit code. Both polarities of every predicate are
exercised, because inversion is exactly the transform that swaps which arm is the
fall-through and which is the branch.

## Tests

<!-- test: an-inverted-signed-branch-keeps-its-arms -->
Every signed integer predicate, each written so that ONE arm is correct and the other returns
its own exit code. The operands come from a global the constant folder cannot see through, so
each comparison survives to the backend as a real compare-and-branch pair.
```maxon
var seed = 4

function main() returns ExitCode
	let a = seed
	let b = seed + 1
	var hits = 0

	if a < b 'lt'
		hits = hits + 1
	end 'lt' else 'ltElse'
		return 11
	end 'ltElse'

	if a > b 'gt'
		return 12
	end 'gt' else 'gtElse'
		hits = hits + 1
	end 'gtElse'

	if a <= b 'le'
		hits = hits + 1
	end 'le' else 'leElse'
		return 13
	end 'leElse'

	if a >= b 'ge'
		return 14
	end 'ge' else 'geElse'
		hits = hits + 1
	end 'geElse'

	if a == b 'eq'
		return 15
	end 'eq' else 'eqElse'
		hits = hits + 1
	end 'eqElse'

	if a != b 'ne'
		hits = hits + 1
	end 'ne' else 'neElse'
		return 16
	end 'neElse'

	return 0 if hits == 6 else 17
end 'main'
```
```exitcode
0
```

<!-- test: an-inverted-float-branch-still-refuses-a-nan -->
⭐ **THE HAZARD.** All six IEEE predicates against a NaN, each with an explicit else arm — so
whichever arm the layout makes the fall-through, the NaN's destination is pinned. A complement
taken from the source predicate rather than from the flags (`mi` → `ge` on AArch64, or a
lowering that drops the parity test on x86) sends the NaN down the then-arm and returns 21-26
instead of 0. `float-compare-branch.md` pins the same predicates with only ONE arm written,
which no inversion can reach.

The NaN is built at RUNTIME from a global by overflow (`inf - inf`) rather than division: a
folded NaN reaches the backend as a constant and emits no compare at all, and division by zero
is a language-level error with no route to a NaN.
```maxon
var big = 1.0e308

function main() returns ExitCode
	let inf = big * 10.0
	let nan = inf - inf
	let x = 5.0
	var hits = 0

	if nan < x 'lt'
		return 21
	end 'lt' else 'ltElse'
		hits = hits + 1
	end 'ltElse'

	if nan <= x 'le'
		return 22
	end 'le' else 'leElse'
		hits = hits + 1
	end 'leElse'

	if nan > x 'gt'
		return 23
	end 'gt' else 'gtElse'
		hits = hits + 1
	end 'gtElse'

	if nan >= x 'ge'
		return 24
	end 'ge' else 'geElse'
		hits = hits + 1
	end 'geElse'

	if nan == nan 'eq'
		return 25
	end 'eq' else 'eqElse'
		hits = hits + 1
	end 'eqElse'

	if nan != nan 'ne'
		hits = hits + 1
	end 'ne' else 'neElse'
		return 26
	end 'neElse'

	return 0 if hits == 6 else 27
end 'main'
```
```exitcode
0
```

<!-- test: an-inverted-unsigned-branch-keeps-its-arms -->
The unsigned family, which reads a different flag from the signed one — carry rather than the
sign/overflow pair — and therefore has its own complements. The operands are elements of a
non-negative ranged array, so the comparison is selected as unsigned.
```maxon
typealias Cell = int(0 to u8.max)
typealias CellArray = Array with Cell

function main() returns ExitCode
	var xs = CellArray.create()
	xs.push(4)
	xs.push(5)
	let a = try xs.get(0) otherwise 200
	let b = try xs.get(1) otherwise 200
	var hits = 0

	if a < b 'lo'
		hits = hits + 1
	end 'lo' else 'loElse'
		return 31
	end 'loElse'

	if a > b 'hi'
		return 32
	end 'hi' else 'hiElse'
		hits = hits + 1
	end 'hiElse'

	if a <= b 'ls'
		hits = hits + 1
	end 'ls' else 'lsElse'
		return 33
	end 'lsElse'

	if a >= b 'hs'
		return 34
	end 'hs' else 'hsElse'
		hits = hits + 1
	end 'hsElse'

	return 0 if hits == 4 else 35
end 'main'
```
```exitcode
0
```

<!-- test: an-inverted-zero-test-keeps-its-sense -->
A branch on a materialized boolean, which AArch64 selects as a register zero-test (`cbz` /
`cbnz`) carrying no flags at all. Its complement is the other spelling of the same test, so a
cleanup that reached for a condition-code table here would find nothing to invert; one that
swapped the targets without swapping the spelling inverts the program.
```maxon
var seed = 4

function main() returns ExitCode
	let flag = seed > 3
	var hits = 0

	if flag 'yes'
		hits = hits + 1
	end 'yes' else 'no'
		return 41
	end 'no'

	if not flag 'inv'
		return 42
	end 'inv' else 'invElse'
		hits = hits + 1
	end 'invElse'

	return 0 if hits == 2 else 43
end 'main'
```
```exitcode
0
```

<!-- test: threading-does-not-drop-an-edge-copy -->
A value that arrives at a join with a different definition on each arm, inside a loop — so the
join's incoming edges are critical and SSA destruction splits them. Each split block holds this
edge's parallel copies and must therefore NOT be threaded away; a cleanup that bypassed a split
block because it "looks like" a forwarding jump drops the copy and the loop accumulates whatever
the register happened to hold. The four iterations contribute 1, 10, 1, 10.
```maxon
var seed = 4

function main() returns ExitCode
	var total = 0
	var i = 0
	while i < seed 'loop'
		var step = 0
		if i mod 2 == 0 'even'
			step = 1
		end 'even' else 'odd'
			step = 10
		end 'odd'
		total = total + step
		i = i + 1
	end 'loop'
	return total
end 'main'
```
```exitcode
22
```

<!-- test: a-threaded-jump-table-arm-still-reaches-its-block -->
Two shapes at once. `classify` returns from BOTH arms, so its continuation block is unreachable
and carries no terminator op — the block the elimination walk must drop without disturbing the
neighbour a live block physically falls into. `pick` lowers to a multiway jump table whose arms
each reach the join through a forwarding block; threading re-points table entries as well as
branches, and an entry re-pointed at the wrong survivor returns another arm's number.
```maxon
typealias Val = int(i64.min to i64.max)

var seed = 4

function classify(n Val) returns Val
	if n > 3 'big'
		return n * 10
	end 'big' else 'small'
		return n
	end 'small'
end 'classify'

function pick(n Val) returns Val
	match n 'sel'
		0 then return 100
		1 then return 101
		2 then return 102
		3 then return 103
		4 then return 104
		5 then return 105
		default then return 199
	end 'sel'
end 'pick'

function main() returns ExitCode
	let a = classify(seed)
	let b = pick(seed)
	if a != 40 'aWrong'
		return 51
	end 'aWrong'
	if b != 104 'bWrong'
		return 52
	end 'bWrong'
	if classify(seed - 4) != 0 'zeroWrong'
		return 53
	end 'zeroWrong'
	if pick(seed + 3) != 199 'defaultWrong'
		return 54
	end 'defaultWrong'
	return 0
end 'main'
```
```exitcode
0
```
