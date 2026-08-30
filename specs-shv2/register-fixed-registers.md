---
feature: register-fixed-registers
status: selfhosted
keywords: [register-allocator, fixed-register, idiv, rax, rdx, calling-convention, argument-registers, forbidden]
category: register-allocator
milestone: M5.4
---

# Fixed-register interactions: `idiv`, calls, and the argument registers

## Documentation

There are two kinds of point where the allocator loses control of a register, and this
file is about what happens where they COLLIDE.

**`idiv` pins RAX and RDX.** `a / b` lowers to `mov rax, a; cqo; idivReg b; mov result,
rax|rdx`. The `mov rax, a` is a physical DEF of RAX, `cqo` implicitly defs RDX, and
`idivReg` implicitly defs both. Liveness folds those into `forbiddenPhys` for every value
live ACROSS the sequence (`applyForbidden`) — and separately into the `idiv`'s own virtual
operand, the DIVISOR, which dies at the op and would otherwise be invisible to the
live-across sweep (`forbidOperandsFromImplicit`).

**A call clobbers all nine caller-saved registers**, so a value live across one has only
the five callee-saved to live in. Argument setup is a run of `mov argReg[k], arg_k`
physical defs emitted in slot order with **no parallel-copy sequencer**; it is sound only
because the same `applyForbidden` path forbids a value still needed by a LATER argument
move from sitting in an EARLIER argument register.

**The two mechanisms share registers.** The argument registers, in slot order, are
`[rcx, rdx, rax, r9, rsi, rdi]` — so **`RDX` is argument slot 1 and `RAX` is argument slot
2, exactly the pair `idiv` pins.** Therefore:

- A division result is BORN in `RAX` (quotient) or `RDX` (remainder), and the biased-coloring
  hint pulls it toward that register. If it must then be passed as argument 1 or 2, it has to
  travel *the other way through the same two registers*, with nothing sequencing the moves.
- A value live across an `idiv` is forbidden `RAX`/`RDX`, which are also the homes of
  arguments 2 and 1 — so a parameter arriving in `RAX` or `RDX` cannot stay there if the
  function divides before it uses that parameter.
- The `idiv` divisor is forbidden `RAX`/`RDX` even though it DIES at the op; if it is also
  live across a call it is forbidden the nine caller-saved as well, leaving only callee-saved.

Every test RUNS and is SELF-VERIFYING: it compares against a hand-computed constant and
returns `0` only on a match, `99` otherwise, so a wrong answer is caught even where the true
value exceeds 255 and a raw exit code would wrap mod 256. A dividend that never reached
`RAX`, a divisor coloured into `RDX` (which `cqo` has just filled with the sign extension —
for a positive dividend that is a divide by ZERO, a hardware `#DE`, not a wrong number), or a
value destroyed by an argument pre-move all surface as a wrong constant or a hard fault.

## Tests

<!-- test: live-across-idiv-and-call -->
`a` is live across BOTH an `idiv` (it is the dividend, and is read again after) and a CALL.
Its forbidden set is the UNION of two reductions — `{RAX, RDX}` from the divide and the nine
caller-saved from the call — leaving only the five callee-saved. The quotient `q` is live
across the call too, so its `RAX` birth hint must MISS and it must land callee-saved as well.
`a = 100`, `q = 100 / 7 = 14` (7·14 = 98), `s = sink(3) = 4`; `100 + 14 + 4 = 118`.
```maxon
function sink(x Integer) returns Integer
	return x + 1
end 'sink'

function f(p Integer) returns Integer
	let a = p + 100
	let q = a / 7
	let s = sink(3)
	return a + q + s
end 'f'

function main() returns ExitCode
	let v = f(0)
	if v == 118 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: div-and-mod-into-rdx-and-rax -->
THE TWO-CYCLE. `n / d`'s quotient is born in **RAX**; `n mod d`'s remainder is born in
**RDX**. They are then passed as arguments 2 and 3, whose slots are **RDX** and **RAX** — so
the two values must cross through exactly the pair `idiv` pinned, over physical-def `mov`s
with no sequencer. The remainder is hinted to `RDX` and is live across `mov rdx, q`; only the
physical-def clobber path forbids it `RDX` there. Lose that one edge and `mov rdx, q` destroys
the remainder before `mov rax, m` reads it, and the third argument silently becomes the second.
`97 / 5 = 19` (5·19 = 95), `97 mod 5 = 2`, so `take3(1, 19, 2)` = `10000 + 1900 + 2` = 11902.
(The clobber above would give `take3(1, 19, 19)` = 11919.)
```maxon
function take3(p1 Integer, p2 Integer, p3 Integer) returns Integer
	return p1 * 10000 + p2 * 100 + p3
end 'take3'

function main() returns ExitCode
	let n = 97
	let d = 5
	let r = take3(1, p2: n / d, p3: n mod d)
	if r == 11902 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: six-div-results-into-six-arg-slots -->
SIX `idiv` sequences whose results are all simultaneously live, feeding all six argument
slots — including slot 1 (`RDX`) and slot 2 (`RAX`). Every earlier quotient is live across
every later divide, so each is forbidden `{RAX, RDX}`, and each is additionally forbidden
every argument register written before its own move. The callee encodes its arguments
POSITIONALLY as decimal digits, so any permutation, clobber or lost value changes exactly one
digit.
`100/12 = 8`, `100 mod 12 = 4`; `45/7 = 6`, `45 mod 7 = 3`; `23/9 = 2`, `23 mod 9 = 5`.
Digits `8,4,6,3,2,5` → `800000 + 40000 + 6000 + 300 + 20 + 5` = 846325.
```maxon
function digits6(p1 Integer, p2 Integer, p3 Integer, p4 Integer, p5 Integer, p6 Integer) returns Integer
	return p1 * 100000 + p2 * 10000 + p3 * 1000 + p4 * 100 + p5 * 10 + p6
end 'digits6'

function main() returns ExitCode
	let n1 = 100
	let n2 = 45
	let n3 = 23
	let d1 = 12
	let d2 = 7
	let d3 = 9
	let r = digits6(n1 / d1, p2: n1 mod d1, p3: n2 / d2, p4: n2 mod d2, p5: n3 / d3, p6: n3 mod d3)
	if r == 846325 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: six-div-results-live-across-the-call -->
THE DEEPEST STACK IN THIS FILE. The same six `idiv` results, but every one is ALSO read
AFTER the call. So each carries both reductions at once: `{RAX, RDX}` from the divides it is
live across, and the nine caller-saved from the call — leaving the five callee-saved. SIX
values, FIVE registers: Hall's condition must fire and the splitter must bracket one of them
(a store at its def — the `mov q, rax` that reads the quotient out — and a reload after the
call), while argument setup still writes `RCX`, `RDX`, `RAX`, `R9`, `RSI`, `RDI` in slot order.
Same digits as above, and the tail re-encodes the same six values, so
`846325 + 846325 = 1692650`.

⚠ The three divisors are cast into `Positive` (A1). `p + 12` is a PARAMETER expression, not a
constant, so an unguarded divide is now E3057 — and this case's whole subject is Hall's condition
over the six `idiv` RESULTS, which only exists if the divides are the bare instruction. A range
excluding 0 keeps them bare. Each cast costs one `cmp`/branch against the lower bound — an
`int(1 to i64.max)` needs no upper check, and the bound folds into a `cmpImm`, so the COMPARE holds no
register — plus the one `lea rN, [rM + 0]` that mints the retagged value (`emitRetaggedCastValue`, whose
header says why an in-place re-tag is not an option). The six quotients still cross the call in the five
callee-saved registers with no spill, which is the claim above and is what the golden shows.
```maxon
typealias Positive = int(1 to i64.max)

function digits6(p1 Integer, p2 Integer, p3 Integer, p4 Integer, p5 Integer, p6 Integer) returns Integer
	return p1 * 100000 + p2 * 10000 + p3 * 1000 + p4 * 100 + p5 * 10 + p6
end 'digits6'

function divsAcross(p Integer) returns Integer
	let n1 = p + 100
	let n2 = p + 45
	let n3 = p + 23
	let d1 = (p + 12) as Positive
	let d2 = (p + 7) as Positive
	let d3 = (p + 9) as Positive
	let q1 = n1 / d1
	let q2 = n1 mod d1
	let q3 = n2 / d2
	let q4 = n2 mod d2
	let q5 = n3 / d3
	let q6 = n3 mod d3
	let r = digits6(q1, p2: q2, p3: q3, p4: q4, p5: q5, p6: q6)
	return r + q1 * 100000 + q2 * 10000 + q3 * 1000 + q4 * 100 + q5 * 10 + q6
end 'divsAcross'

function main() returns ExitCode
	let v = divsAcross(0)
	if v == 1692650 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: nested-div-as-divisor -->
`a / (b / c)` — the INNER quotient, born in `RAX` and hinted there, is the OUTER divide's
DIVISOR. It must be out of `RAX` before `mov rax, a` overwrites it, and out of `RDX` before
`cqo` fills that with the sign extension — and it must be out of both AT the `idiv`, which is
what `forbidOperandsFromImplicit` guarantees for an operand that dies at the op.
(shv2 has no parenthesized expressions, so the inner divide is named. The IR is the same: two
`idiv`s, the first's result feeding the second's divisor operand.)
`84 / 4 = 21`; `1000 / 21 = 47` (21·47 = 987). Honour the `RAX` hint and `mov rax, a` would
clobber the divisor, computing `1000 / 1000 = 1`.
```maxon
function main() returns ExitCode
	let a = 1000
	let b = 84
	let c = 4
	let inner = b / c
	let r = a / inner
	if r == 47 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
```
```exitcode
0
```

<!-- test: nested-div-both-sides -->
`(a / b) / (c / d)` — THREE `idiv`s. The first quotient is live across the SECOND divide (so
it is forbidden `RAX`/`RDX`) and then becomes the THIRD divide's DIVIDEND (so it is copied
back INTO `RAX`); the second quotient is the third divide's DIVISOR (so it must stay OUT of
`RAX`/`RDX`). Two values born in the same pinned register, with opposite destinations.
`1000 / 7 = 142` (994); `60 / 4 = 15`; `142 / 15 = 9` (135).
```maxon
function main() returns ExitCode
	let a = 1000
	let b = 7
	let c = 60
	let d = 4
	let q1 = a / b
	let q2 = c / d
	let r = q1 / q2
	if r == 9 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
```
```exitcode
0
```

<!-- test: divide-by-self -->
`x / x` and `x mod x` — the SAME virtual value is the dividend (read by the physical-def
`mov rax, x`) AND the divisor (an explicit `idiv` operand). Two different mechanisms must
agree about it: `applyForbidden` keeps it out of `RAX` because it is live across the
`mov rax, x`, and `forbidOperandsFromImplicit` keeps it out of `RAX`/`RDX` because it is an
`idiv` operand. `x` is read once more afterwards, so a clobber cannot hide inside the quotient.
The `RDX` half is the sharp one: colour `x` into `RDX` and `cqo` overwrites it with the sign
extension (zero, for a positive `x`), so the `idiv` divides by ZERO — a hardware `#DE`, not a
wrong answer.
`x = 37`: `x / x = 1`, `x mod x = 0`, so `37·100 + 1·10 + 0 = 3710`.
```maxon
function main() returns ExitCode
	let x = 37
	let q = x / x
	let m = x mod x
	let r = x * 100 + q * 10 + m
	if r == 3710 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
```
```exitcode
0
```

<!-- test: divisor-live-across-call -->
The DIVISOR is live across a CALL, so it carries both constraints at once: forbidden the nine
caller-saved (so it survives `bump`) and forbidden `RAX`/`RDX` (so the `idiv` can read it).
Its only home is the callee-saved set, and the prologue must push it.
`bump(6) = 7`; `500 / 6 = 83` (498); `83 + 7 = 90`. Leave `d` in a caller-saved register and
`bump` destroys it, so the divide runs on garbage.
```maxon
function bump(x Integer) returns Integer
	return x + 1
end 'bump'

function main() returns ExitCode
	let n = 500
	var d = 6
	let t = bump(d)
	let q = n / d
	let r = q + t
	if r == 90 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: div-result-crosses-a-call -->
The `idiv` RESULT is passed as an argument AND read after the call. Its birth register is
`RAX` and the `mov q, rax` hints it there — but `RAX` is caller-saved, so the call forbids it,
the fixed hint MISSES, and `q` must land callee-saved. It is then argument 0, so `mov rcx, q`
is a real move out of a callee-saved register.
`1000 / 7 = 142` (994); `twice(142) = 284`; `142 + 284 = 426`.
```maxon
function twice(x Integer) returns Integer
	return x + x
end 'twice'

function main() returns ExitCode
	let n = 1000
	let d = 7
	let q = n / d
	let t = twice(q)
	let r = q + t
	if r == 426 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: idiv-under-loop-pressure -->
An `idiv` inside a loop with a genuine eleven-value working set. `k1`..`k9` are re-read every
iteration, so the cold-spill splitter may NOT lift them out (Rule 2); the accumulator `sum` is
live across the divide; and the loop counter `i` is the DIVISOR. All eleven are live across the
`idiv`, so all eleven are forbidden `RAX`/`RDX`: the effective pool AT that op is `14 − 2 = 12`,
and eleven values must fit in it. That is the reduced-pool bound with ONE register of slack — an
off-by-one anywhere in the `implicitDefs` accounting panics the colorer, and a cold spill landing
in the loop body is a golden mismatch.
The dividend is summed INSIDE the loop (`t`), not hoisted before it — that is what keeps
`k1`..`k9` live across the back edge and therefore live across the `idiv`. Sum them outside and
only one value would cross the divide, and the test would prove nothing.
`k1..k9 = 11..19`, summing to 135, so `t = 135` every iteration. The loop runs `i = 1..5`:
`135/1 = 135`, `135/2 = 67`, `135/3 = 45`, `135/4 = 33`, `135/5 = 27`; total
`135 + 67 + 45 + 33 + 27 = 307`.

⚠ The loop counter is cast into `Positive` at the divide (A1); an unguarded loop-carried divisor is
E3057, and this case needs the bare `idiv` (a `try` here would add the fork's own values to a working
set that has exactly one register of slack). The guard's `cmp` folds its bound into an immediate and so
holds no register; the cast's retag mint takes one (`r15` in the golden), and the eleven-value set still
colors with NO spill in the loop body — which is the reduced-pool claim above, re-proven at the tighter
bound. `i` runs 1..6, so the guard never fires.
```maxon
typealias Positive = int(1 to i64.max)

function press(p Integer) returns Integer
	let k1 = p + 11
	let k2 = p + 12
	let k3 = p + 13
	let k4 = p + 14
	let k5 = p + 15
	let k6 = p + 16
	let k7 = p + 17
	let k8 = p + 18
	let k9 = p + 19
	var sum = 0
	var i = 1
	while i <= 5 'loop'
		let t = k1 + k2 + k3 + k4 + k5 + k6 + k7 + k8 + k9
		sum = sum + t / (i as Positive)
		i = i + 1
	end 'loop'
	return sum
end 'press'

function main() returns ExitCode
	let r = press(0)
	if r == 307 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
