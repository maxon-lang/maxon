---
feature: commute-for-coalescing
status: experimental
keywords: [optimizer, codegen, regalloc, coalescing, commutative, two-address, copy]
category: codegen
---
# Operand order on a destructive instruction — the copy it decides

## Documentation

x64's integer ALU is **two-address**: `and`/`or`/`xor`/`imul` compute `dest := dest ⊕ src`, so the
destination register and the LEFT operand must be the same register. shv2 does not emit a seed `mov`
for that — the operand model declares `dest` a REUSE of `lhs` (`pushReuseBinOperands`), and
`RegisterAllocator.allocateReuseDef` materialises `mov dest, lhs` **only when `lhs` outlives the op**.
When `lhs` dies there, the destination simply takes its register and nothing is emitted.

So for a **commutative** op the operand ORDER decides whether a copy exists:

```
                          ; `flags` is read again below, `edge` is not
mov  rcx, r15             ; <- the copy the surviving LHS costs
or   rcx, r15, rax        ;    dest=rcx, lhs=r15(flags), rhs=rax(edge)
```

against the same operation with its operands the other way round:

```
or   rax, rax, r15        ; dest coalesced into `edge`'s dying register — no copy at all
```

`StdToX64Conversion.commutesForCoalescing` makes that choice at instruction selection.

### THE RULE, and each half of it is a case below

- **The opcode must be commutative.** `binOpcodeIsCommutative` is the dialect's one answer, and it is
  already right about the hard ones: `min`/`max` are NOT commutative in this language (`min(a, b)` is
  `a` when `a < b` and `b` otherwise, and a strict comparison is false on unordered, so
  `min(nan, 5.0)` is `5.0` while `min(5.0, nan)` is `nan`).
- **The x64 instruction must be two-address.** Integer `+` lowers to the three-operand
  `lea dest, [lhs + rhs]`, which has an independent destination and never costs a copy — commuting it
  would move a golden and save nothing.
- **The RIGHT operand must PROVABLY DIE HERE**, and "read exactly once" is not that.
  `valueDiesAtItsOnlyReader` asks TWO things: the value is read exactly once in the whole function,
  AND it is defined in the SAME BLOCK as the op reading it. Both are needed — a value defined OUTSIDE
  a loop and read ONCE inside it has one reader and is live across the back edge, so it dies nowhere
  near that reader; swapping on the weaker test would put a copy INTO a loop that had none, and shv2
  REFUSES rather than spills, so the price of that is an `E5001` on a program that compiled before.
  With the block test the range is closed inside one block and the answer is exact. This half carries
  the whole safety argument.
- **The LEFT operand must have a second reader** — *necessary* for a copy to exist, and not
  sufficient: a second reader that comes BEFORE this op leaves `lhs` dying here anyway, and the swap
  then removes nothing. Answering that exactly needs a use POSITION, a dense per-function column on
  the isel's hot path, and it is not bought. **Because the RIGHT half is exact, this imprecision can
  only cost a golden that moved for nothing** — the swapped order's reuse input provably dies, so no
  copy is recorded for it whatever `lhs` was doing.

Both halves come off the ONE descent the instruction selector already makes for EC16
(`ScaledIndexFolds`): `collectFunctionValueUses` counted with multiplicity over every operand of every
op, every terminator and every branch-edge argument, plus the def block of every arithmetic result.
That also refuses `x ⊕ x` for free (one value inserted twice reads as repeated).

### ⛔ FLOATS ARE EXCLUDED, AND NOT BY OMISSION

x64 `addsd`/`mulsd` propagate the **destination's** NaN payload, so `a ⊕ b` and `b ⊕ a` are not
bit-identical when both operands are NaN. That is the same rule that keeps `foldConstOperands`'
operand reordering integer-only (EC13), and `a-float-multiply-keeps-its-copy` is its pin: the
`movsd` survives in the committed fragment where the integer twin's `mov` does not.

### ⚠ THIS IS A SMALL ROW AND THE CENSUS IS WHY

Measured on the self-compile (1,275,649 emitted x64 ops, 248,207 `movRegReg`): only **969** of those
copies are two-address reuse copies at all, and only **314** of them sit on a commutative integer op.
**91.6% of shv2's register-to-register copies are the ABI** — an argument moved into its calling
register, a result captured out of one, a parameter captured at entry — and 93% of those read a
CALLEE-SAVED register, i.e. a value that crosses a call and therefore cannot live in an argument
register at all. `docs/emitted-code-roadmap.md`'s `EC19` row carries the full table.

⚠ It is also not uniformly a win, and the row says so: against a real control the self-compile emits
175 fewer copies and 512 fewer bytes and `fannkuch-redux` runs 4.7% faster, while `scale-test`'s
generated corpus emits **1.45% MORE code**, all of it from `regalloc:splitting` doing more work on a
corpus whose pressure knob is sized to sit exactly at the register pool. Which of the two generalises
is open.

## Tests

<!-- test: a-commutative-op-with-a-dying-right-operand-needs-no-copy -->
The gate. `flags` is read by the `or` AND by the sum below it, so it survives the op; `edge` is read
by the `or` and nowhere else, so it dies there. The `or` is emitted with its operands the other way
round and the destination coalesces into `edge`'s register: the committed fragment holds **no
`movRegReg` before the `orRegReg`**, and the `or`'s printed `dest` and `lhs` are the same register.
Its own operation is symmetric, so no order bug HERE could change its answer — the case that turns an
order bug into an exit code is `a-non-commutative-op-keeps-its-copy` below. This one is a golden pin,
and disabling the transform is what moves it.
```maxon
typealias Word = int(i64.min to i64.max)

function classify(flags Word, n Word) returns Word
	let edge = n and 1
	let merged = flags or edge
	return merged + flags
end 'classify'

function main() returns ExitCode
	var seen = 0
	for i in 0 upto 4 'feed'
		seen = seen + classify(i * 2, n: i)
	end 'feed'
	// i = 0,1,2,3 -> flags = 0,2,4,6; edge = 0,1,0,1
	// merged = 0,3,4,7; merged+flags = 0,5,8,13 -> 26
	if seen != 26 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-commutative-op-whose-both-operands-survive-keeps-its-copy -->
The control that says the RIGHT-operand half of the rule is load-bearing. The same program with
`edge` read a second time: now neither operand dies at the `or`, swapping would move the copy rather
than remove it, and the rule declines. The committed fragment keeps its `movRegReg` before the
`orRegReg`.
```maxon
typealias Word = int(i64.min to i64.max)

function classifyBoth(flags Word, n Word) returns Word
	let edge = n and 1
	let merged = flags or edge
	return merged + flags + edge
end 'classifyBoth'

function main() returns ExitCode
	var seen = 0
	for i in 0 upto 4 'feed'
		seen = seen + classifyBoth(i * 2, n: i)
	end 'feed'
	// merged+flags = 0,5,8,13; plus edge 0,1,0,1 -> 0,6,8,14 -> 28
	if seen != 28 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-non-commutative-op-keeps-its-copy -->
The control that says the OPCODE half is load-bearing, and the one whose sabotage is a **wrong
answer** rather than a moved golden. `a - scaled` has exactly the liveness the gate case has — the
left operand is read again below, the right one is read here and nowhere else — so every other
condition of the rule holds, and the copy stays anyway because `sub` is not commutative and
`scaled - a` is a different number. Remove the `binOpcodeIsCommutative` guard and this program
returns 1.

⚠ `scaled` is `b * 3` and not `b` itself, and that is what makes this a control at all: the
straightforward spelling `a - b` passes under that sabotage FOR THE WRONG REASON — after inlining `b`
is the loop counter, the counter has half a dozen readers, so the dying-`rhs` condition fails first
and the commutativity guard is never reached. MEASURED: with the guard removed, the `a - b` spelling
left all five fragments byte-identical and all five green.
```maxon
typealias Word = int(i64.min to i64.max)

function difference(a Word, b Word) returns Word
	let scaled = b * 3
	let d = a - scaled
	return d + a
end 'difference'

function main() returns ExitCode
	var seen = 0
	for i in 0 upto 4 'feed'
		seen = seen + difference(i * 10, b: i)
	end 'feed'
	// a = 0,10,20,30; scaled = 0,3,6,9; d = 0,7,14,21; d+a = 0,17,34,51 -> 102
	// commuted, `d` would be scaled - a = 0,-7,-14,-21 and the sum 18
	if seen != 102 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-float-multiply-keeps-its-copy -->
The NaN control. `x * k` is commutative in arithmetic and the liveness is the gate case's exactly —
`x` is read again, `k` is not — but `mulsd` propagates the DESTINATION's NaN payload, so the two
orders are not bit-identical on a NaN input and the rule refuses every float. The committed fragment
keeps its `movRegReg` before the `mulsdRegReg` where the integer twin has none.
```maxon
typealias Wide = float(f64.min to f64.max)

function scale(x Wide, k Wide) returns Wide
	let p = x * k
	return p + x
end 'scale'

function main() returns ExitCode
	var seen = 0.0
	for i in 0 upto 4 'feed'
		seen = seen + scale(i + 1.0, k: 2.0)
	end 'feed'
	// p = 2,4,6,8; p+x = 3,6,9,12 -> 30
	if seen != 30.0 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-integer-add-is-not-commuted -->
The control for the DESTRUCTIVENESS half. Every other condition of the rule holds — `+` is
commutative, `total` is read again below, and `scaled` is read here and nowhere else — and it is still
not commuted, because integer `+` lowers to the three-operand `lea`, whose destination is independent
of both operands and costs no copy whatever the order. The pin is the fragment's operand ORDER:
`leaRegRegReg` names `total` before `scaled`, the order the source wrote. Flip `add gives false` to
`true` in `integerBinOpIsTwoAddress` and this golden moves.

⚠ **`scaled` is `step * 5`, and BOTH of those choices are a control that was caught failing.** The
straightforward `total + step` cannot reach the destructiveness test at all — after inlining `step` IS
the loop counter and the counter has several readers, so the dying-`rhs` condition refuses first
(MEASURED: with `add gives true`, that spelling left this fragment byte-identical). And `step * 3`
fails too, differently: the call passes `i * 3` as `total`, so CSE makes `scaled` the SAME VALUE as
`total` and the `x ⊕ x` guard refuses it — the fragment showed `lea rsi, rdx, rdx`. A multiplier that
is neither `total`'s nor a power of two (which `EC16` would absorb into an addressing mode, leaving no
`lea` to pin) is what makes the case reach the roster.
```maxon
typealias Word = int(i64.min to i64.max)

function accumulate(total Word, step Word) returns Word
	let scaled = step * 5
	let next = total + scaled
	return next + total
end 'accumulate'

function main() returns ExitCode
	var seen = 0
	for i in 0 upto 4 'feed'
		seen = seen + accumulate(i * 3, step: i)
	end 'feed'
	// total = 0,3,6,9; scaled = 0,5,10,15; next = 0,8,16,24; next+total = 0,11,22,33 -> 66
	if seen != 66 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```
