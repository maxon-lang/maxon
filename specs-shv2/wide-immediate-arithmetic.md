---
feature: wide-immediate-arithmetic
status: stable
keywords: [arithmetic, add, sub, immediate, encoding, codegen]
category: codegen
---

# Wide immediate operands of `+` and `-`

## Documentation

`x + K` and `x - K` for a CONSTANT `K` lower to a `binOpImm`, and the shared constant
folder (`FoldConstOperands.constFitsBinOpImmediate`) hands the backends the **whole
`Imm32` domain** — every `K` from `i32.min` to `i32.max`, because that is what x64's
`lea [base ± imm32]` carries verbatim.

**No ISA is obliged to have an immediate form that wide, and AArch64 does not.** Its
ADD/SUB-immediate form is a 12-bit field with an optional left-shift by 12: it reaches
24 bits' worth of value, and only in that shape. So the arm64 instruction selector, like
its `and`/`or`/`xor` and its `mul` before it, has to decide per operand whether an
immediate form exists at all — and when none does, materialise the constant into the IP
scratch register and use the three-register form instead.

That decision is what this spec pins, at the boundary rather than at one lucky constant.
The four bands, and why each is a distinct encoding:

| `K` | arm64 encoding |
|---|---|
| ≤ `0xFFF` (4095) | one `add`/`sub` with a plain imm12 |
| `0x1000` … `0xFFF000` | one `add`/`sub` with the imm12 shifted left by 12 |
| … `0xFFFFFF` (16777215) | the hi/lo PAIR — a shifted imm12 then a plain one |
| ≥ `0x1000000` | **no immediate form** — `movz`/`movk` into the scratch, then a register `add`/`sub` |

The sign is the second axis and it crosses the same boundaries: a NEGATIVE literal on
the `+` side is a SUB of its magnitude (and vice versa), so `x + -1000000007` needs the
same materialisation `x - 1000000007` does, and the band a constant falls in is decided
by its MAGNITUDE.

Every case below computes an answer the program checks, with a distinct exit code per
check: a mis-materialised constant is a WRONG ANSWER, not a crash, and a raw wrong value
could otherwise land on 0 by luck.

### Why the left operand is read out of an array

The left operand of every expression here is a value read back out of a one-element array,
not a literal. A constant on BOTH sides is EVALUATED — by the parser when it is written as
two literals, and by `foldConstants` (EC12) when inlining makes it one — and the arithmetic
then never reaches an immediate form at all, so the test would pin nothing about encoding.

⛔ **IT USED TO BE THE RETURN OF A ONE-LINE `base(p)` HELPER, AND THAT STOPPED WORKING.**
`base` is a tiny leaf, so `inlineLeaves` (EC5) splices its body into the caller and the
argument literal lands where the parameter was; `foldConstants` then evaluates the whole
chain and every case below collapses to `return 0` — still GREEN, and pinning nothing. What
replaces it is not another call but a MEMORY READ, because that rests on a rule the compiler
states rather than on a pass's current inlining policy: a load's result is never a
compile-time constant, however constant the initializer looks, since any store since could
have replaced it (`FoldConstOperands.classifyFoldableDef`, memory band). The `try … otherwise`
merge makes the value a phi as well, which no constant domain here looks through either.

### The scratch register is the risk, and it has to be one nothing else can hold

Materialisation happens after the operands are already values, so the constant needs a
register that no live value can be sitting in. arm64 reserves x16/x17 (the
intra-procedure-call scratch registers) for exactly this, and shv2 keeps both OUT of the
allocatable pool.

What makes that safe is not the reservation alone — it is that the `movz`/`movk` and the
register op that reads it are emitted ADJACENTLY, so x16 is live across exactly one
instruction boundary and nothing can be scheduled into it. `under-register-pressure` is
the case that puts that claim under load: it holds **thirty-four** values live, which is
past the twenty-six GPRs the arm64 pool offers (x0..x15 ∪ x19..x28), so the allocator is
genuinely spilling and reloading around the materialisations rather than merely having
room to spare. A spill store or reload placed BETWEEN the `movz`/`movk` and its consumer
would destroy the constant and the sum would come out wrong rather than the program
crashing — which is the shape of defect BATCH15's own A5b was (a value written into a
register another value already held, between a producer and its consumer).

⚠ A case that merely holds a dozen values live pins nothing here: with a twenty-six
register pool the allocator never approaches exhaustion, so no spill code is emitted at
all and the adjacency is never tested.

## Tests

<!-- test: imm12-boundary -->
The plain 12-bit field and the first constant past it, on both `+` and `-`. `4095` is the
largest immediate that encodes with no shift; `4096` is the first that needs the hi/lo
split.
```maxon
function main() returns ExitCode
	let cell = [1000]
	let x = try cell.get(0) otherwise 0
	let addLo = x + 4095
	if addLo != 5095 'addLo'
		return 11
	end 'addLo'
	let subLo = x - 4095
	if subLo != -3095 'subLo'
		return 12
	end 'subLo'
	let addHi = x + 4096
	if addHi != 5096 'addHi'
		return 13
	end 'addHi'
	let subHi = x - 4096
	if subHi != -3096 'subHi'
		return 14
	end 'subHi'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: shifted-imm12-boundary -->
The top of the SHIFTED field and the top of the hi/lo pair — the last two constants that
still have an immediate form. `16773120` is `0xFFF000` (a shifted imm12 with an empty low
half, so one instruction); `16777215` is `0xFFFFFF` (both halves live, so two).
```maxon
function main() returns ExitCode
	let cell = [1000]
	let x = try cell.get(0) otherwise 0
	let addShifted = x + 16773120
	if addShifted != 16774120 'addShifted'
		return 21
	end 'addShifted'
	let subShifted = x - 16773120
	if subShifted != -16772120 'subShifted'
		return 22
	end 'subShifted'
	let addPair = x + 16777215
	if addPair != 16778215 'addPair'
		return 23
	end 'addPair'
	let subPair = x - 16777215
	if subPair != -16776215 'subPair'
		return 24
	end 'subPair'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: past-the-24-bit-split -->
The first constant with NO arm64 immediate form (`16777216` = `0x1000000`), and one well
past it (`1000000007`, which needs three halfwords of the `movz`/`movk` ladder). Both must
go through the scratch and the register form.
```maxon
function main() returns ExitCode
	let cell = [1000]
	let x = try cell.get(0) otherwise 0
	let addFirst = x + 16777216
	if addFirst != 16778216 'addFirst'
		return 31
	end 'addFirst'
	let subFirst = x - 16777216
	if subFirst != -16776216 'subFirst'
		return 32
	end 'subFirst'
	let addWide = x + 1000000007
	if addWide != 1000001007 'addWide'
		return 33
	end 'addWide'
	let subWide = x - 1000000007
	if subWide != -999999007 'subWide'
		return 34
	end 'subWide'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: negative-wide-immediate -->
The sign twin. A negative literal on `+` is a SUB of its magnitude and a negative literal
on `-` is an ADD of it, so both cross the encoding boundary on the MAGNITUDE — and the
materialised constant must carry its sign into all 64 bits, not just the low 32. The last
three checks take the immediate to `i32.max` and `i32.min`, the widest the shared constant
fold produces.

`i32.min` is the one value where the two SIGNS are not symmetric, and the asymmetry is
NOT arm64's. `constFitsBinOpImmediate` folds `i32.min` on `add` but excludes it on `sub`,
because x64 lowers `sub` to a NEGATED displacement and `-(i32.min)` overflows the field —
so `x + -2147483648` arrives as a `binOpImm` and `x - -2147483648` never does. **The arm64
selector must not DEPEND on that**: its own predicate reads the magnitude, and
`0 - i32.min` is past the 24-bit band whichever side asks, so it answers "no immediate
form" and materialises either way. Both halves are checked here precisely so the answer is
pinned on both sides of a boundary that lives in another file for another target's reason.
```maxon
function main() returns ExitCode
	let cell = [1000]
	let x = try cell.get(0) otherwise 0
	let addNeg = x + -1000000007
	if addNeg != -999999007 'addNeg'
		return 41
	end 'addNeg'
	let subNeg = x - -1000000007
	if subNeg != 1000001007 'subNeg'
		return 42
	end 'subNeg'
	let addMax = x + 2147483647
	if addMax != 2147484647 'addMax'
		return 43
	end 'addMax'
	let subMax = x - 2147483647
	if subMax != -2147482647 'subMax'
		return 44
	end 'subMax'
	let addMin = x + -2147483648
	if addMin != -2147482648 'addMin'
		return 45
	end 'addMin'
	let subMin = x - -2147483648
	if subMin != 2147484648 'subMin'
		return 46
	end 'subMin'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: under-register-pressure -->
Thirty-four values live across four wide-immediate adds. The arm64 pool offers twenty-six
GPRs, so this function SPILLS: the allocator is inserting stores and reloads in the same
straight line the four `movz`/`movk` ladders sit in. Each of those constants has no
immediate form, so it goes through the IP scratch — and anything the allocator placed
between a ladder and the `add` that reads it would overwrite the constant, giving a WRONG
SUM rather than a crash. `p = 0`: `k1`..`k30` are `1`..`30` (sum 465), and the four wide
values are `1000000007`, `1000000009`, `2000000011` and `2000000013` (sum 6000000040). Total 6000000505.
```maxon
function pressure(p Integer) returns Integer
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
	let k17 = p + 17
	let k18 = p + 18
	let k19 = p + 19
	let k20 = p + 20
	let k21 = p + 21
	let k22 = p + 22
	let k23 = p + 23
	let k24 = p + 24
	let k25 = p + 25
	let k26 = p + 26
	let k27 = p + 27
	let k28 = p + 28
	let k29 = p + 29
	let k30 = p + 30
	let w1 = p + 1000000007
	let w2 = p + 1000000009
	let w3 = p + 2000000011
	let w4 = p + 2000000013
	return k1 + k2 + k3 + k4 + k5 + k6 + k7 + k8 + k9 + k10 + k11 + k12 + k13 + k14 + k15 + k16 + k17 + k18 + k19 + k20 + k21 + k22 + k23 + k24 + k25 + k26 + k27 + k28 + k29 + k30 + w1 + w2 + w3 + w4
end 'pressure'

function main() returns ExitCode
	let cell = [0]
	let total = pressure(try cell.get(0) otherwise 0)
	if total != 6000000505 'total'
		return 51
	end 'total'
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: wide-immediate-in-a-loop -->
A wide immediate added inside a loop body, so the materialisation sits between two
loop-carried values' live ranges rather than in straight-line code. `acc` starts at 0 and
takes `+ 1000000007` three times, and `i` counts with a small immediate beside it — the two
bands in one block. `3 * 1000000007 = 3000000021`.
```maxon
function main() returns ExitCode
	let cell = [0]
	var acc = try cell.get(0) otherwise 0
	var i = 0
	while i < 3 'loop'
		acc = acc + 1000000007
		i = i + 1
	end 'loop'
	if acc != 3000000021 'acc'
		return 61
	end 'acc'
	return 0
end 'main'
```
```exitcode
0
```
