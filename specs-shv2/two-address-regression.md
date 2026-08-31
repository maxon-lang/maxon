---
feature: two-address-regression
status: selfhosted
keywords: [register-allocator, reuse, two-address, lea, imul, neg, reuse-copy-transient, return-register]
category: codegen
milestone: M5.2
---

# Two-address reuse regression

## Documentation

x64's `sub` and `imul` are genuinely two-address (`dest = dest <op> rhs`), and `neg`
is two-address unary. M5.1 handled that by pre-emitting a seed copy `mov result, lhs`
in ISel and hoping biased coloring would elide it. M5.2 deletes that seed: ISel emits
ONE reuse-def op, and the register allocator supplies the copy ONLY when the two-address
input actually outlives the op.

The structural consequence this spec locks in: a `-` or `*` over **loop-carried,
non-constant** values (a constant operand folds away to an immediate, so both operands
must be loop-carried to stay in registers) emits **no copy at all in the loop body** —
the reuse def coalesces into its input's register because the input dies at the op. If
this regresses (a seed `mov` or an unnecessary reuse copy reappears in a loop), the
fragment below changes visibly.

The complementary case — a two-address op whose input genuinely OUTLIVES it (`b = a -
b` with `a` loop-invariant) — DOES need one copy, because the input must be preserved.
That copy is correct: the program still computes the right answer, and the fragment
below pins the single `mov` so neither a lost copy (a wrong answer) nor an extra one
(worse code) goes unnoticed.

### The reuse copy costs a REGISTER, and the pressure model has to know it

`allocateReuseDef` records `mov dest, input` whenever `destReg != inputReg` — which is NOT
the same condition as "the input outlives the op". The copy runs BEFORE the op, at a point
where nothing has died yet, so the dest's register co-exists with everything live entering
the op, INCLUDING a dying input. The op's true register demand is therefore
`|live-before| + 1` whenever the dest cannot coalesce into the input's register.

`reuseInputOutlivesOp` — which both the liveness pass and the splitter's peak-finder read —
reports only the case where the input OUTLIVES. That raises an obvious question: what about
a reuse dest that cannot take its DYING input's register, because the DEST is forbidden it?
The copy would be just as mandatory, and neither the liveness pass nor the peak-finder would
have counted it.

**That situation is prevented upstream, not handled downstream** — and
`reuse-dest-cannot-coalesce` below is the program that pins it. The reuse hint makes the dest
and the input COPY PARTNERS, and `preferredClassMask` (M5.12) makes a copy group adopt the
scarcest register class any of its members needs. So when the dest is confined to the
callee-saved set (because it is live across a call), the INPUT is allocated a callee-saved
register too — even though nothing about the input alone requires one — and the reuse
coalesces into it. The transient never has to exist.

The scarce-class rule is therefore not just an optimization that avoids a copy; it is what
keeps the reuse path's register demand equal to what the pressure model actually counts.

### The R8 return hint and the reuse-dest hint want different registers

`return a - b` gives the `sub`'s dest TWO hints: `collectReuseHints` makes it a copy partner
of `a` (take `a`'s register and elide the copy), while `collectOpHints` pins it to **R8** for
the return move. `chooseRegister` tries the FIXED hint first, so the dest takes R8 and a
`mov r8, a` reuse copy appears in place of the return move — the same instruction count, a
different register, and the two hints must not fight their way into a wrong ANSWER.

### An op that reads one value TWICE

`x - x` and `x * x` name the same value as both operands. `isFirstUseOfValueInOp` collapses
that to ONE use record, so the value dies once; and when it OUTLIVES the op the reuse copy is
mandatory, because an `imul x, x` in place would destroy `x`.

## Tests

<!-- test: mul-loop -->
`product = product * i` over two loop-carried values. The `imul` reuse def coalesces
into `product`'s register (`imulRegReg r8, r8, rcx`) — NO copy in the loop body.
```maxon
function main() returns ExitCode
	var product = 1
	var i = 5
	while i > 1 'loop'
		product = product * i
		i = i - 1
	end 'loop'
	return product
end 'main'
```
```exitcode
120
```

<!-- test: sub-loop -->
`acc = acc - i` over two loop-carried values. The `sub` reuse def coalesces into
`acc`'s register (`subRegReg` with dest == lhs) — NO copy in the loop body.
```maxon
function main() returns ExitCode
	var acc = 100
	var i = 1
	while i < 5 'loop'
		acc = acc - i
		i = i + 1
	end 'loop'
	return acc
end 'main'
```
```exitcode
90
```

<!-- test: reuse-copy-outlives -->
`b = a - b` with `a` loop-invariant (used every iteration, so it outlives the `sub`).
Here the allocator MUST materialize `mov b_new, a` before the `sub` — the one case that
legitimately needs a reuse copy. The loop runs `b = 20 - b` three times from `b = 5`:
15, 5, 15 — a missing or mis-targeted copy would compute on the wrong value and fail.
```maxon
function main() returns ExitCode
	let a = 20
	var b = 5
	var i = 0
	while i < 3 'loop'
		b = a - b
		i = i + 1
	end 'loop'
	return b
end 'main'
```
```exitcode
15
```

<!-- test: same-value-twice -->
`x * x` and `x - x` read ONE value as BOTH operands of a two-address op. `x` OUTLIVES both
(it is read again at the end), so both need a reuse copy — emit the `imul` in place and `x`
is destroyed. `y * y` is the same op with its input DYING, so its dest coalesces; the value
must be recorded as dying exactly ONCE even though it appears twice in the op.
`p = 0`: `x = 9`, `y = 7`; `sq = 81`, `df = 0`, `t = 49`. So
`81·100 + 0·10 + 9 + 49 = 8100 + 0 + 9 + 49 = 8158`. (A clobbered `x` would make the tail
`81` instead of `9`, giving 8230.)
```maxon
function sameTwice(p Integer) returns Integer
	let x = p + 9
	let y = p + 7
	let sq = x * x
	let df = x - x
	let t = y * y
	return sq * 100 + df * 10 + x + t
end 'sameTwice'

function main() returns ExitCode
	let r = sameTwice(0)
	if r == 8158 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: return-a-minus-b -->
`return a - b` — the R8 return hint and the reuse-dest hint want DIFFERENT registers for the
same value. The fixed hint wins, so the `sub`'s dest takes R8 and the reuse copy becomes
`mov r8, a`, after which the return move self-elides. `chainDiff` then chains two `sub`s, so
the second one's input is the first one's dest (dying, therefore coalesced) while the R8 pin
sits on the second. The first call's result is live across the second call, so it is
additionally forced callee-saved.
`diff(200, 137) = 63`; `chainDiff(90, 20, 5) = 90 − 20 − 5 = 65`; `63·1000 + 65 = 63065`.
```maxon
function diff(a Integer, b Integer) returns Integer
	return a - b
end 'diff'

function chainDiff(a Integer, b Integer, c Integer) returns Integer
	return a - b - c
end 'chainDiff'

function main() returns ExitCode
	let r = diff(200, b: 137) * 1000 + chainDiff(90, b: 20, c: 5)
	if r == 63065 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: deep-reuse-chain -->
FIVE two-address `sub`s that all reuse the SAME input, and `a` outlives every one of them (it
is read again at the end). So EVERY `sub` needs its own `mov r_i, a` reuse copy — five copies,
not one — and the copy-partner hint (`copyHint[a]` is set once, to `r1`) must not convince the
allocator that the later ones can coalesce too. A single missed copy turns `a` into `a − b`,
and every subsequent difference is then wrong.
The operands are derived from a parameter so none folds to an immediate: a constant right
operand lowers to a `lea` and never reaches the two-address path at all.
`p = 0`: `a = 100`, `b..f = 1..5`; `r1..r5 = 99, 98, 97, 96, 95`, summing to 485; `+ a` = 585.
```maxon
function chain(p Integer) returns Integer
	let a = p + 100
	let b = p + 1
	let c = p + 2
	let d = p + 3
	let e = p + 4
	let f = p + 5
	let r1 = a - b
	let r2 = a - c
	let r3 = a - d
	let r4 = a - e
	let r5 = a - f
	return r1 + r2 + r3 + r4 + r5 + a
end 'chain'

function main() returns ExitCode
	let s = chain(0)
	if s == 585 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: neg-chain -->
Three chained two-address `neg`s. `x` OUTLIVES the first one (it is read again in `x * 100`),
so that one needs a reuse copy; the two inner results each DIE at the next `neg`, so those
coalesce and negate in place. Exactly one `mov` for three `neg`s — drop it and the first `neg`
destroys `x`.
(shv2 has no parenthesized expressions, so the chain is written with named intermediates
rather than `-(-(-x))`. The IR is the same: three `negReg` reuse-defs in a row.)
`p = 0`: `x = 7`; `n1 = -7`, `n2 = 7`, `n3 = -7`. So `x·100 − n3 = 700 − (−7) = 707`. A
clobbered `x` gives `(−7)·100 + 7 = −693`, which the self-check catches — a raw exit code from
a wrong run could otherwise land on 0.

⚠ `p` is read from a MODULE-LEVEL `var`, not passed as a literal: `inlineLeaves` splices `negs` into
`main`, and with a literal argument `foldConstants` (which since 2026-08-31 evaluates a `neg` over a
constant) folds the whole chain to `mov rax, 707` — no `neg` is emitted and the self-check above
checks a constant. Neither the parser's constant domain nor the fold pass looks through memory, so a
load of a global keeps the three `neg`s, and the reuse copy this case exists to pin, on every lane.
```maxon
var seed = 0

function negs(p Integer) returns Integer
	let x = p + 7
	let n1 = -x
	let n2 = -n1
	let n3 = -n2
	return x * 100 - n3
end 'negs'

function main() returns ExitCode
	let r = negs(seed)
	if r == 707 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: loop-reuse-transient-overflow -->
The reuse-copy transient is the SOLE cause of an overflow, INSIDE A LOOP. `b = a - b` with `a`
loop-invariant: `a` outlives the `sub`, so the allocator materializes `mov b_new, a` before it
— one extra register. Entering the `sub`, fourteen values are live (`k1`..`k11`, `a`, `b`, `i`)
— exactly the pool — so the transient takes the demand to FIFTEEN.

The relief must come from OUTSIDE the loop: `k1`..`k11` are IDLE across it (computed before,
summed after, never touched inside), so Rule 2 permits a store in the PREHEADER and a reload
after the loop, with NOTHING added to the loop body. The splitter must both SEE the transient
(the peak-finder's `reuseInputOutlivesOp` correction) and pick a victim the loop does not use.
Miss the first and the colorer panics with no free register; miss the second and spill code
lands in the loop body — still correct, but a golden mismatch.
`p = 0`: `a = 20`, `b = 5`; the loop runs `i = 0, 1, 2` giving `b = 15, 5, 15`; `k1..k11 = 1..11`
sum to 66. So `15 + 66 = 81`.
```maxon
function loopReuse(p Integer) returns Integer
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
	let a = p + 20
	var b = p + 5
	var i = 0
	while i < 3 'loop'
		b = a - b
		i = i + 1
	end 'loop'
	return b + k1 + k2 + k3 + k4 + k5 + k6 + k7 + k8 + k9 + k10 + k11
end 'loopReuse'

function main() returns ExitCode
	let r = loopReuse(0)
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

<!-- test: reuse-dest-cannot-coalesce -->
A reuse def whose DEST is confined and whose INPUT, on its own, is not — the case that would
need a reuse copy the pressure model never counted, and the one the SCARCE-CLASS RULE exists
to make impossible.

At `d = a - b`, FOURTEEN values are live (`a`, `b`, `k1`..`k12`) — exactly the pool, so every
register is held and there is no room for a transient. `a` DIES at the `sub`, so
`reuseInputOutlivesOp` is FALSE and neither `maxPressure` nor the splitter's `effective` adds
one; the splitter sees `14 ≤ 14` and relieves nothing. Hall's condition sees nothing either,
because it is stepped over the LIVE set at each op and `d` — the op's own DEF — is not in it.
So if `d` could not coalesce into `a`'s register, the only register `allocateReuseDef` is
allowed to vacate for it, every register would be blocked and the colorer would have nothing
to pick.

`d` IS live across the `sink` call, so it is forbidden all nine caller-saved registers. Nothing
about `a` requires a callee-saved register — it is defined early, while eight caller-saved ones
are still free, and `pickPreferredRegister` prefers caller-saved so a leaf function pays no
prologue. Left alone, `a` would take one, and `d` could not follow it there.

It does not, and THAT is what this test pins. The reuse hint makes `a` and `d` copy PARTNERS,
and `preferredClassMask` (M5.12) makes a copy group adopt the scarcest class any member needs —
so `a` is allocated **`rbx`**, callee-saved, purely because its reuse partner will need to live
there. The fragment shows the payoff directly: `subRegReg rbx, rbx, rax`, dest == input, a clean
coalesce, no copy, and `d` sits in `rbx` across the call with no spill anywhere in the function.
The scarce-class rule is not merely saving a `mov` here; it is keeping the reuse path's true
register demand equal to the demand the pressure model counted.
`p = 0`: `a = 100`, `b = 50`, `d = 50`; `k1..k12 = 1..12` sum to 78; `s = sink(78) = 78`. So
`50 + 78 = 128`.
```maxon
function sink(x Integer) returns Integer
	return x
end 'sink'

function tight(p Integer) returns Integer
	let a = p + 100
	let b = p + 50
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
	let d = a - b
	let t = k1 + k2 + k3 + k4 + k5 + k6 + k7 + k8 + k9 + k10 + k11 + k12
	let s = sink(t)
	return d + s
end 'tight'

function main() returns ExitCode
	let r = tight(0)
	if r == 128 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
