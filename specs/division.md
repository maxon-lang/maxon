---
feature: division
status: selfhosted
keywords: [arithmetic, division, modulo, idiv, register-allocator, fixed-register]
category: operators
---

# Integer division and modulo

## Documentation

Maxon's `/` (division) and `mod` (modulo) are signed integer operations that
truncate toward zero. They bind at the MULTIPLICATIVE precedence level — the same
as `*` — so `10 + 20 / 4` groups as `10 + (20 / 4)` = 15, and `2 * 6 mod 4` (all
left-associative at one level) groups as `((2 * 6) mod 4)` = 0.

They are the first operations to need a HARD fixed-physical-register constraint.
x64's `idiv r/m64` divides the 128-bit `RDX:RAX` by its operand, leaving the
QUOTIENT in `RAX` and the REMAINDER in `RDX`. So `a / b` lowers to:

```
mov rax, a      ; dividend into RAX's low half
cqo             ; sign-extend RAX into RDX:RAX
idiv b          ; RDX:RAX / b  ->  quotient in RAX, remainder in RDX
mov result, rax ; (mov result, rdx for `mod`)
```

`RAX` and `RDX` are pinned by the instruction, so the register allocator must keep
the divisor — and every value live across the `idiv` — OUT of `RAX`/`RDX`. It does
this from `idivReg`'s implicit-register masks (`implicitDefs = {RAX, RDX}`): the
liveness pass forbids those registers for anything crossing the op, and forbids
them for the divisor operand directly (which dies at the `idiv`, so the live-across
sweep alone would miss it). The tests below RUN, so a dividend that never reached `RAX`
or a divisor colored into `RDX` computes the wrong quotient and the exit-code assertion
catches it; the emitted `mov rax` / `cqo` / `idiv` sequence itself is visible with `--emit-ir`.

⭐ **DIVIDE-BY-ZERO IS A LANGUAGE-LEVEL THROW, NOT A HARDWARE TRAP.** `/` and `mod` are FALLIBLE: a divisor the compiler cannot prove non-zero makes the
divide a throwing operation, so it must sit in a `try (a / b) otherwise …` or the program is refused
with E3057; a divisor it holds as the constant 0 is refused outright with E3103; and a divisor it
proves non-zero — a non-zero literal, or a ranged type whose range excludes 0 — compiles to the bare
`idiv` sequence above with no check, no branch and no call. `specs/safety.md` owns the rule and
its tests; every divide in THIS file is deliberately over a provably non-zero divisor, so the
sequence they emit is the unguarded one.

⚠ **`/` AND `mod` PART WAYS AT `i64.min` BY `-1`.** `a mod -1` is `0` for every `a`, so `mod` guards
its divisor against `-1` as well as `0` and answers `0`. `i64.min / -1` has no representable quotient:
it panics `integer overflow` with the standard backtrace and exits 1, on every target. `DivisionByZero`
is about the DIVISOR and does not cover it, so a `try` around the divide does not catch it.
`specs/safety.md` owns both rules and their cases; the `mod` guard's COST is below, and it is nothing
at all wherever the divisor's type or value rules `-1` out.

The guard, where one is needed, costs **three instructions and no branch** — `cmp divisor, 0`, a
`setcc` materializing the answer, and `or safe, divisor, flag`. The `idiv` then runs on `safe`, which
is the divisor itself whenever it is non-zero (the flag is 0) and 1 when it is zero, whose quotient the
`try` fork discards. So a checked divide is still an `idiv` under exactly the `RAX`/`RDX` constraint
described above; it is not a call, and the fixed-register cases below would still be testing that
instruction if they were written with a `try`.

⭐ **`mod`'s SECOND GUARD COSTS FOUR MORE INSTRUCTIONS, ALSO WITHOUT A BRANCH, AND ONLY WHERE THE
DIVISOR MIGHT BE `-1`.** `cmp divisor, -1` · a `setcc` · `add mask, isNegOne, -1` (so `mask` is 0 when
the divisor is `-1` and all-ones otherwise) · `and safeDividend, dividend, mask`. The `idiv` then
divides **0** whenever the divisor is `-1`, whose remainder is the `0` the language promises and whose
quotient cannot overflow. It masks the DIVIDEND rather than fixing up the divisor because the answer
is dividend-independent, and it reuses the one `-1` constant for both the compare and the decrement —
which is why the third instruction is an `add` and not a `sub`.

⚠ **THE PROOF IS WHAT KEEPS IT OFF THE COMMON PATH, and every divide in this file is evidence: none
of them emits it.** A divisor that is a literal OTHER THAN `-1`, or a variable the
parser folded to one, or a ranged type whose range excludes `-1` — `int(1 to 1000)` included — emits none
of those four instructions. `/` never emits them at all.

⭐ **`/` HAS A GUARD OF ITS OWN AT THE SAME PAIR, AND IT IS A BRANCH TO A PANIC.** A signed quotient whose
divisor might be `-1` and whose dividend might be `i64.min` tests `(dividend xor i64.min) or (divisor xor
-1)` against `0` and branches to `panic: integer overflow` ahead of the divide — one compare-and-branch on
the hot path, because there is no quotient to repair the operands toward. The same proof removes it: a
divisor excluding `-1`, or a dividend whose type or value excludes `i64.min`, and the divide is bare —
which is every divide in this file, so none of them emits it. `safety.md`'s two
`…-int-min-over-minus-one` cases are the ones that do.

⚠ **A LITERAL `-1` IS THE EXCEPTION, AND IT IS NOT OPTIMIZED AWAY: `a mod -1` EMITS THE GUARD AND AN
`idiv` FOR AN ANSWER THAT IS STATICALLY `0`.** The guard fires on the value the compiler can see, so the
emitted code is the same eight instructions any other unprovable divisor gets — visible in the
emitted code of `safety.md`'s `mod-by-a-minus-one-literal-is-zero`. The parser DOES fold the result into its
constant domain (which is what keeps `100 / (10 mod -1)` an E3103 rather than a runtime throw), but
`foldConstants` — rewriting a folded expression to a single `mov` — is deliberately deferred for the whole
language (`FoldConstOperands`' header), and a `mod` by `-1` is not the place to make an exception.

⚠ **`safe` IS A FRESH SSA VALUE, NOT AN OVERWRITE OF THE DIVISOR** — and the emitted code shows the allocator
reusing the divisor's register for it whenever the divisor is dead after the divide, which is the
correct thing to do and looks alarming. `divisor-is-read-after-a-checked-divide` below is what turns
that from a reading of the emitted code into a checked claim: a divisor still live afterwards must come back
UNCHANGED, including the zero that took the error edge.

## Tests

<!-- test: div-simple -->
`20 / 4` = 5. The dividend `20` materializes into `RAX`, the divisor `4` into a
non-`RAX`/`RDX` register (`rcx`), and the quotient is read from `RAX`.
```maxon
function main() returns ExitCode
	return 20 / 4
end 'main'
```
```exitcode
5
```

<!-- test: mod-simple -->
`17 mod 5` = 2. Same `idiv`, but the REMAINDER is read from `RDX` instead of the
quotient from `RAX`.
```maxon
function main() returns ExitCode
	return 17 mod 5
end 'main'
```
```exitcode
2
```

<!-- test: div-truncates -->
`100 / 7` = 14 — integer division truncates toward zero (14.28… → 14).
```maxon
function main() returns ExitCode
	return 100 / 7
end 'main'
```
```exitcode
14
```

<!-- test: mod-remainder -->
`100 mod 7` = 2 (100 = 14·7 + 2).
```maxon
function main() returns ExitCode
	return 100 mod 7
end 'main'
```
```exitcode
2
```

<!-- test: div-precedence -->
`/` binds tighter than `+`: `10 + 20 / 4` is `10 + (20 / 4)` = 10 + 5 = 15, not
`(10 + 20) / 4` = 7.
```maxon
function main() returns ExitCode
	return 10 + 20 / 4
end 'main'
```
```exitcode
15
```

<!-- test: mod-precedence -->
`mod` sits at the multiplicative level alongside `*`, left-associative: `2 * 6 mod 4`
is `((2 * 6) mod 4)` = `12 mod 4` = 0, not `2 * (6 mod 4)` = 4.
```maxon
function main() returns ExitCode
	return 2 * 6 mod 4
end 'main'
```
```exitcode
0
```

<!-- test: div-mod-variables -->
`/` and `mod` over VARIABLES, both dividing the same `a` by the same `b`. `a` and
`b` are loop-free but each live across BOTH `idiv`s, so the allocator keeps them —
and the first quotient, which must survive the second `idiv`'s `RAX`/`RDX` clobber —
in non-`RAX`/`RDX` registers. `q = 100 / 7 = 14`, `r = 100 mod 7 = 2`, `q + r = 16`.
```maxon
function main() returns ExitCode
	let a = 100
	let b = 7
	let q = a / b
	let r = a mod b
	return q + r
end 'main'
```
```exitcode
16
```

<!-- test: div-in-loop -->
Division inside a loop, where the divisor is the loop-carried counter `i`. `i` is
live across every `idiv` AND updated each iteration, so it MUST be colored to a
register that is neither `RAX` (the dividend) nor `RDX` (clobbered by `cqo`/`idiv`)
— the fixed-register constraint under real pressure. Sum of `100 / i` for
`i = 1..6`: 100 + 50 + 33 + 25 + 20 + 16 = 244.

⚠ The counter is cast into `Positive` at the divide. A loop-carried phi is not a
constant the compiler can fold, so an unguarded `100 / i` is E3057 — and the point of
this case is a BARE `idiv` whose divisor is loop-carried, which is exactly what a range
that excludes 0 buys back. The cast costs one `cmp`/branch against the lower bound
(`int(1 to i64.max)` needs no upper check) plus the `lea` that mints the retagged value, and
`i` runs 1..7, so the guard never fires.
```maxon
typealias Positive = int(1 to i64.max)

function main() returns ExitCode
	var sum = 0
	var i = 1
	while i <= 6 'loop'
		sum = sum + 100 / (i as Positive)
		i = i + 1
	end 'loop'
	return sum
end 'main'
```
```exitcode
244
```

### The fallible-division rule, at the instruction level

`specs/safety.md` owns the RULE — what throws, what is refused, what is caught. These own its
CODE, because the exemption's whole claim is about what is emitted and an exit code cannot see the
difference between a bare divide and a guarded one. Per the relational-assertions rule, a case that
merely runs pins nothing here: the emitted code (`--emit-ir`) is where the difference shows.

<!-- test: ranged-divisor-is-a-bare-idiv -->
A divisor whose declared range EXCLUDES 0 is the escape hatch, and this is what it buys: the emitted code
holds the same `mov rax` / `cqo` / `idivReg` sequence `div-simple` does, with no `cmp`, no `or`, no
branch and no error flag anywhere near it. The divisor is a PARAMETER, so nothing folds it — the
range is doing all the work. `100 / 7 = 14`, `100 mod 7 = 2`, `14 + 2 = 16`.
```maxon
typealias NonZero = int(1 to 1000)

function divide(n NonZero, d NonZero) returns Integer
	return (n / d + n mod d) as Integer
end 'divide'

function main() returns ExitCode
	return divide(100, d: 7) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
16
```

<!-- test: checked-divide-keeps-the-idiv -->
And this is what a CHECKED divide buys: the emitted code still holds an `idivReg`, because the guard is two
instructions and no branch (`flag = divisor == 0`, `safe = divisor or flag`) rather than a call or a
skipped divide. That is the whole reason the fixed-register cases in this file could have been
written either way. `d` comes from an opaque call, so it cannot be folded; `100 / 7 = 14`.
```maxon
function opaque(x Integer) returns Integer
	return x
end 'opaque'

function main() returns ExitCode
	let d = opaque(7)
	let q = try (100 / d) otherwise 99
	return q as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
14
```

<!-- test: checked-divide-in-a-loop -->
A checked divide inside a LOOP, in expression position, with the quotient accumulated across the
back edge — the shape `stdlib/Math.maxon` writes at fourteen sites. The fork's blocks are created per
iteration's worth of control flow, not per iteration, and the loop-carried `sum` phi must survive
both edges of every fork. Sum of `100 / i` for `i = 1..6` — the same 244 `div-in-loop` computes,
reached the other way.
```maxon
function opaque(x Integer) returns Integer
	return x
end 'opaque'

function main() returns ExitCode
	var sum = 0
	var i = 1
	while i <= 6 'loop'
		sum = sum + (try (100 / opaque(i)) otherwise 0)
		i = i + 1
	end 'loop'
	return sum as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
244
```

<!-- test: checked-divide-with-a-managed-value-live-across-the-fork -->
An OWNED String is live when the divide forks, so it flows to BOTH edges — and each edge must release
it exactly once. That is the drop-set shape `desugarTry` splits per edge, and getting it wrong is a
leak (exit 101) or a double free (a poison fault), never a wrong answer, which is why an exit code
alone would not catch it. The divisor is non-zero, so the OK edge is the one taken.

⚠ The `try` wraps the DIVIDE, not the interpolation. `try ("q={100 / d}\n") otherwise …` is **E3055**
— correctly: the group's outermost operation there is `__str_*`, which cannot fail, so the `try` has
nothing to attach to. The rule is `rewriteLastCallToTryCall`'s and it is not division-specific.
```maxon
function opaque(x Integer) returns Integer
	return x
end 'opaque'

function main() returns ExitCode
	let d = opaque(4)
	let held = "held d={d}\n"
	let q = try (100 / d) otherwise 0
	print(held)
	print("q={q}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
held d=4
q=25
```
```exitcode
0
```

<!-- test: checked-divide-error-edge-reassigns-a-managed-var -->
The ERROR edge is the one taken here, and its handler REASSIGNS an owned String — dropping the one
the var held and taking a new one — inside the handler's own scope frame. The `(e)` binding
discriminates `divisionByZero`, so the caught error's type is the one `runtimeThrowsClause` answers
with and not an untyped flag.
```maxon
function opaque(x Integer) returns Integer
	return x
end 'opaque'

function main() returns ExitCode
	let z = opaque(0)
	var label = "not caught\n"
	try (100 / z) otherwise (e) 'handle'
		match e 'kind'
			divisionByZero then label = "caught divisionByZero\n"
		end 'kind'
	end 'handle'
	print(label)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```stdout
caught divisionByZero
```
```exitcode
0
```

<!-- test: ranged-divisor-excluding-minus-one-is-still-a-bare-idiv -->
⭐ **THE `mod` OVERFLOW GUARD IS ABSENT WHEN THE RANGE RULES `-1` OUT**, and only the emitted code can
say so — an exit code cannot tell four elided instructions from four emitted ones.
`ranged-divisor-is-a-bare-idiv` above already covers a POSITIVE range; this one is wholly NEGATIVE
(`int(i64.min to -2)`), which is the case that would fall to the guard if the proof had been written
as "the range is positive" instead of "the range excludes `-1`". The emitted code holds the same
`mov rax`/`cqo`/`idivReg` sequence with no `cmp` and no `and` anywhere near it. `-13 mod -5` is `-3`
(the remainder takes the DIVIDEND's sign), and `-3 + 45 = 42`.
```maxon
typealias BelowMinusOne = int(i64.min to -2)

function remainder(n BelowMinusOne, d BelowMinusOne) returns Integer
	return (n mod d) as Integer
end 'remainder'

function main() returns ExitCode
	return (remainder(-13, d: -5) + 45) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: divisor-is-read-after-a-checked-divide -->
⭐ **THE DIVISOR SURVIVES THE GUARD.** `safe = divisor or flag` is a fresh SSA value, but the register
allocator reuses the divisor's register for it wherever the divisor dies at the divide — so the only
way to know the SSA form is honest is to keep the divisor LIVE past the divide and read it back. Both
paths are exercised: `d = 7` takes the ok edge and must still read 7, and `z = 0` takes the ERROR edge,
where `safe` was forced to 1 and `z` must nonetheless still read 0. `14 + 7 + 0 + 0 = 21`.
```maxon
function opaque(x Integer) returns Integer
	return x
end 'opaque'

function main() returns ExitCode
	let d = opaque(7)
	let z = opaque(0)
	let q = try (100 / d) otherwise 99
	let r = try (100 / z) otherwise 0
	return (q + d + r + z) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
21
```

<!-- test: divide-a-value-whose-declared-type-is-narrower-than-a-machine-word -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
⚠ **A WINDOWS-LANE READING.** `return 4000000000` is E3005 on every other target —
`ExitCode` is `int(0 to 255)` there — so those lanes cannot express this program, which is what the
`unsupported-targets:` restriction says. It cannot be re-pinned on wasm through any other type, and the reason it
cannot (plus the array-element route that looks like a substitute and measurably is not) is stated once,
in `exit-code-range.md`'s *"What the narrowing costs the other lanes"*.

⭐⭐ **THE OPERANDS' STORAGE WIDTH IS NOT THE DIVISION'S WIDTH.** `div`/`mod` are the two Std ops
that carry no operand type, so a backend has to take the width from somewhere — and taking it from the
LEFT OPERAND is what this case refuses. An `ExitCode` is a **u32** (`valueTagToStdType`), an int→int
promotion emits NO conversion op (the value keeps the width its DEFINING op gave it while its consumer
treats it as a machine word), and on a target whose locals are typed the two disagree: taking the width
from the operand, `wasm32-wasi` would divide at 32 bits and SIGNED, so `4000000000 / 7` would answer
`4252829111` where x64 — which divides in 64-bit registers whatever the Std type says — answers
`571428571`. Every narrow value the compiler mints is
UNSIGNED and zero-extended (see `coerceOnStack`), so the machine-word answer is the language's one.

⚠ **THE REMAINDER IS RIGHT EVEN UNDER THAT READING, AND FOR A REASON WORTH KEEPING IN THE CASE.** The
`mod` reads the OVERFLOW GUARD's `safe = dividend and mask`, a `binOp` that carries its own i64 operand
type — so the guard hands the `mod` a full-width value and its width is never taken from the u32, while
the `/` has no such op in front of it and would read the u32 directly. Both are asserted because that
asymmetry is the whole shape of the defect: a rule keyed on the `mod` path would change nothing, and one
that only widened the operands of ops that happen to sit behind a guard would leave `q` wrong.
```maxon
function big() returns ExitCode
	return 4000000000
end 'big'

function seven() returns ExitCode
	return 7
end 'seven'

function main() returns ExitCode
	let e = big()
	let d = seven()
	print("q={try (e / d) otherwise 0}\n")
	print("r={try (e mod d) otherwise 0}\n")
	return 0
end 'main'
```
```stdout
q=571428571
r=3
```
```exitcode
0
```

### An UNSIGNED divide keeps every one of the fallible rule's routes (X10)

`specs/ranged-typealias.md` owns the SIGNEDNESS rule — what makes a `/` unsigned, and that a
ranged typealias on one operand may not decide the arithmetic done to the other. These own the
INTERSECTION of that rule with the fallible-division one, which is where the two could have come
apart: the signedness is decided in the parser and has to survive a desugar that reconstructs the
whole divide from a CALLEE NAME.

<!-- test: an-unsigned-divide-survives-the-throwing-desugar -->
An unsigned divisor whose range ADMITS zero — `bits(64)` is every 64-bit pattern, so it cannot
exclude one — takes the THROWING expansion rather than the bare divide. The expansion rebuilds
the divide from the callee it was handed, so a signedness that did not ride that name would silently
come back signed here and nowhere else: `u64.max / 3` would answer `0` inside a `try` while answering
`6148914691236517205` outside one, from one source operator. Both operators, because the remainder
takes a different callee and a different guard.
```maxon
typealias Word = bits(64)
typealias Any64 = int(i64.min to i64.max)

function ident(v Any64) returns Any64
	return v
end 'ident'

function main() returns ExitCode
	let w = ident(-1) as Word
	let d = ident(3) as Word
	print("q={try (w / d) otherwise 0}\n")
	print("r={try (w mod d) otherwise 0}\n")
	print("z={try (w / (ident(0) as Word)) otherwise 111}\n")
	return 0
end 'main'
```
```stdout
q=6148914691236517205
r=0
z=111
```
```exitcode
0
```

<!-- test: an-unsigned-remainder-at-u64-max-is-not-the-overflow-case -->
⭐⭐ **THE `-1` OVERFLOW GUARD MUST NOT FIRE ON AN UNSIGNED `mod`, AND IT ANSWERS `0` IF IT DOES.** `mod`
has a guard because `idiv` raises `#DE` on `i64.min mod -1`, and the guard's answer at that
divisor is `0` — correct, because `a mod -1` is `0` for every signed `a`. Read UNSIGNED the same bit
pattern is `u64.max`, and `5 mod u64.max` is **5**: the divisor is simply larger than the dividend.
So the guard is not merely unnecessary there — applying it is a WRONG ANSWER, which is why an unsigned
remainder is excluded from it by its OPCODE rather than by a second reading of the operand ranges.

The dividend is a plain literal and the divisor's range excludes 0, so this is a BARE divide: no
`try`, no error flag, and nothing between the two operands and the instruction.
```maxon
typealias NonZeroWord = int(1 to u64.max)
typealias Any64 = int(i64.min to i64.max)

function ident(v Any64) returns Any64
	return v
end 'ident'

function main() returns ExitCode
	let big = ident(-1) as NonZeroWord
	print("r={5 mod big}\n")
	print("q={5 / big}\n")
	return 0
end 'main'
```
```stdout
r=5
q=0
```
```exitcode
0
```

<!-- test: bits64-divides-by-a-divisor-above-i64-max -->
A non-zero range reaching `u64.max` divides unsigned, and a divisor above `i64.max` is a large positive number: `u64.max / (2^63 + 1)` is `1`, remainder `2^63 - 2`.
```maxon
typealias NonZeroWord = int(1 to u64.max)

function word(d NonZeroWord) returns NonZeroWord
	return d
end 'word'

function main() returns ExitCode
	let n = word(18446744073709551615)
	let d = word(9223372036854775809)
	print("q={n / d}\n")
	print("r={n mod d}\n")
	return 0
end 'main'
```
```stdout
q=1
r=9223372036854775806
```
```exitcode
0
```

<!-- test: bits64-divides-by-a-divisor-whose-upper-is-below-u64-max -->
A range whose upper bound is above `i64.max` is unsigned even when that bound is not `u64.max`, so its divide is unsigned: `(2^63 + 1) / 2` is `2^62`, remainder `1`.
```maxon
typealias Divisor = int(1 to 9223372036854775809)

function divisor(d Divisor) returns Divisor
	return d
end 'divisor'

function main() returns ExitCode
	let n = divisor(9223372036854775809)
	let d = divisor(2)
	print("q={n / d}\n")
	print("r={n mod d}\n")
	return 0
end 'main'
```
```stdout
q=4611686018427387904
r=1
```
```exitcode
0
```

<!-- test: unsigned-divisors-at-or-above-two-to-the-63 -->
Unsigned constant divisors from 2^63 up agree with the hardware divide.
```maxon
typealias Unsigned = bits(64)
typealias UnsignedArray = Array with Unsigned
typealias UBigDivisor = int(2 to u64.max)

function refUDiv(n Unsigned, d UBigDivisor) returns Unsigned
	return try (n / (d as Unsigned)) otherwise panic("d is never zero")
end 'refUDiv'

function refUMod(n Unsigned, d UBigDivisor) returns Unsigned
	return try (n mod (d as Unsigned)) otherwise panic("d is never zero")
end 'refUMod'

function dividendsAround(d UBigDivisor) returns UnsignedArray
	var ns = UnsignedArray.create()
	let top = (9223372036854775807 as Unsigned) + 1
	let dw = d as Unsigned
	ns.push(0)
	ns.push(1)
	ns.push(top - 1)
	ns.push(top)
	ns.push(top + 1)
	ns.push(dw - 1)
	ns.push(dw)
	ns.push(dw + 1)
	ns.push(u64.max as Unsigned)
	ns.push((u64.max as Unsigned) - 1)
	ns.push(dw + dw)
	return ns
end 'dividendsAround'

function main() returns ExitCode
	for u in dividendsAround(9223372036854775808) 'd9223372036854775808'
		if u / 9223372036854775808 != refUDiv(u, d: 9223372036854775808) 'q9223372036854775808'
			return 1
		end 'q9223372036854775808'
		if u mod 9223372036854775808 != refUMod(u, d: 9223372036854775808) 'r9223372036854775808'
			return 2
		end 'r9223372036854775808'
	end 'd9223372036854775808'
	for u in dividendsAround(9223372036854775809) 'd9223372036854775809'
		if u / 9223372036854775809 != refUDiv(u, d: 9223372036854775809) 'q9223372036854775809'
			return 3
		end 'q9223372036854775809'
		if u mod 9223372036854775809 != refUMod(u, d: 9223372036854775809) 'r9223372036854775809'
			return 4
		end 'r9223372036854775809'
	end 'd9223372036854775809'
	for u in dividendsAround(13835058055282163711) 'd13835058055282163711'
		if u / 13835058055282163711 != refUDiv(u, d: 13835058055282163711) 'q13835058055282163711'
			return 5
		end 'q13835058055282163711'
		if u mod 13835058055282163711 != refUMod(u, d: 13835058055282163711) 'r13835058055282163711'
			return 6
		end 'r13835058055282163711'
	end 'd13835058055282163711'
	for u in dividendsAround(18446744073709551600) 'd18446744073709551600'
		if u / 18446744073709551600 != refUDiv(u, d: 18446744073709551600) 'q18446744073709551600'
			return 7
		end 'q18446744073709551600'
		if u mod 18446744073709551600 != refUMod(u, d: 18446744073709551600) 'r18446744073709551600'
			return 8
		end 'r18446744073709551600'
	end 'd18446744073709551600'
	for u in dividendsAround(18446744073709551614) 'd18446744073709551614'
		if u / 18446744073709551614 != refUDiv(u, d: 18446744073709551614) 'q18446744073709551614'
			return 9
		end 'q18446744073709551614'
		if u mod 18446744073709551614 != refUMod(u, d: 18446744073709551614) 'r18446744073709551614'
			return 10
		end 'r18446744073709551614'
	end 'd18446744073709551614'
	for u in dividendsAround(18446744073709551615) 'd18446744073709551615'
		if u / 18446744073709551615 != refUDiv(u, d: 18446744073709551615) 'q18446744073709551615'
			return 11
		end 'q18446744073709551615'
		if u mod 18446744073709551615 != refUMod(u, d: 18446744073709551615) 'r18446744073709551615'
			return 12
		end 'r18446744073709551615'
	end 'd18446744073709551615'
	for u in dividendsAround(13835058055282163712) 'd13835058055282163712'
		if u / 13835058055282163712 != refUDiv(u, d: 13835058055282163712) 'q13835058055282163712'
			return 13
		end 'q13835058055282163712'
		if u mod 13835058055282163712 != refUMod(u, d: 13835058055282163712) 'r13835058055282163712'
			return 14
		end 'r13835058055282163712'
	end 'd13835058055282163712'
	for u in dividendsAround(10000000000000000000) 'd10000000000000000000'
		if u / 10000000000000000000 != refUDiv(u, d: 10000000000000000000) 'q10000000000000000000'
			return 15
		end 'q10000000000000000000'
		if u mod 10000000000000000000 != refUMod(u, d: 10000000000000000000) 'r10000000000000000000'
			return 16
		end 'r10000000000000000000'
	end 'd10000000000000000000'
	for u in dividendsAround(12345678901234567891) 'd12345678901234567891'
		if u / 12345678901234567891 != refUDiv(u, d: 12345678901234567891) 'q12345678901234567891'
			return 17
		end 'q12345678901234567891'
		if u mod 12345678901234567891 != refUMod(u, d: 12345678901234567891) 'r12345678901234567891'
			return 18
		end 'r12345678901234567891'
	end 'd12345678901234567891'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: signed-divisors-near-two-to-the-62-point-5-and-i64-min -->
Signed constant divisors where the old refinement declined agree with the hardware divide.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word
typealias PosDivisor = int(2 to i64.max)
typealias NegDivisor = int(i64.min to -2)

function refDiv(n Word, d PosDivisor) returns Word
	return try (n / (d as Word)) otherwise panic("d is never zero")
end 'refDiv'

function refMod(n Word, d PosDivisor) returns Word
	return try (n mod (d as Word)) otherwise panic("d is never zero")
end 'refMod'

function refDivNeg(n Word, d NegDivisor) returns Word
	return try (n / (d as Word)) otherwise panic("d is never zero")
end 'refDivNeg'

function refModNeg(n Word, d NegDivisor) returns Word
	return try (n mod (d as Word)) otherwise panic("d is never zero")
end 'refModNeg'

function dividends(d Word) returns WordArray
	var ns = WordArray.create()
	let m = i64.min as Word
	let x = i64.max as Word
	ns.push(0)
	ns.push(1)
	ns.push(-1)
	ns.push(m)
	ns.push(m + 1)
	ns.push(x)
	ns.push(x - 1)
	ns.push(d)
	ns.push(d - 1)
	ns.push(d + 1)
	ns.push(0 - d)
	ns.push(1 - d)
	ns.push(-1 - d)
	ns.push(x - d)
	ns.push(m + d)
	ns.push(d + d)
	ns.push(m / 2)
	ns.push(x / 2)
	return ns
end 'dividends'

function main() returns ExitCode
	for n in dividends(6521908912666391107) 'p6521908912666391107'
		if n / 6521908912666391107 != refDiv(n, d: 6521908912666391107) 'q6521908912666391107'
			return 1
		end 'q6521908912666391107'
		if n mod 6521908912666391107 != refMod(n, d: 6521908912666391107) 'r6521908912666391107'
			return 2
		end 'r6521908912666391107'
	end 'p6521908912666391107'
	for n in dividends(6521908912666391106) 'p6521908912666391106'
		if n / 6521908912666391106 != refDiv(n, d: 6521908912666391106) 'q6521908912666391106'
			return 3
		end 'q6521908912666391106'
		if n mod 6521908912666391106 != refMod(n, d: 6521908912666391106) 'r6521908912666391106'
			return 4
		end 'r6521908912666391106'
	end 'p6521908912666391106'
	for n in dividends(6521908912666391109) 'p6521908912666391109'
		if n / 6521908912666391109 != refDiv(n, d: 6521908912666391109) 'q6521908912666391109'
			return 5
		end 'q6521908912666391109'
		if n mod 6521908912666391109 != refMod(n, d: 6521908912666391109) 'r6521908912666391109'
			return 6
		end 'r6521908912666391109'
	end 'p6521908912666391109'
	for n in dividends(4611686018427387905) 'p4611686018427387905'
		if n / 4611686018427387905 != refDiv(n, d: 4611686018427387905) 'q4611686018427387905'
			return 7
		end 'q4611686018427387905'
		if n mod 4611686018427387905 != refMod(n, d: 4611686018427387905) 'r4611686018427387905'
			return 8
		end 'r4611686018427387905'
	end 'p4611686018427387905'
	for n in dividends(4611686018427387907) 'p4611686018427387907'
		if n / 4611686018427387907 != refDiv(n, d: 4611686018427387907) 'q4611686018427387907'
			return 9
		end 'q4611686018427387907'
		if n mod 4611686018427387907 != refMod(n, d: 4611686018427387907) 'r4611686018427387907'
			return 10
		end 'r4611686018427387907'
	end 'p4611686018427387907'
	for n in dividends(9223372036854775807) 'p9223372036854775807'
		if n / 9223372036854775807 != refDiv(n, d: 9223372036854775807) 'q9223372036854775807'
			return 11
		end 'q9223372036854775807'
		if n mod 9223372036854775807 != refMod(n, d: 9223372036854775807) 'r9223372036854775807'
			return 12
		end 'r9223372036854775807'
	end 'p9223372036854775807'
	for n in dividends(9223372036854775806) 'p9223372036854775806'
		if n / 9223372036854775806 != refDiv(n, d: 9223372036854775806) 'q9223372036854775806'
			return 13
		end 'q9223372036854775806'
		if n mod 9223372036854775806 != refMod(n, d: 9223372036854775806) 'r9223372036854775806'
			return 14
		end 'r9223372036854775806'
	end 'p9223372036854775806'
	for n in dividends(9223372036854775805) 'p9223372036854775805'
		if n / 9223372036854775805 != refDiv(n, d: 9223372036854775805) 'q9223372036854775805'
			return 15
		end 'q9223372036854775805'
		if n mod 9223372036854775805 != refMod(n, d: 9223372036854775805) 'r9223372036854775805'
			return 16
		end 'r9223372036854775805'
	end 'p9223372036854775805'
	for n in dividends(7766279631452241920) 'p7766279631452241920'
		if n / 7766279631452241920 != refDiv(n, d: 7766279631452241920) 'q7766279631452241920'
			return 17
		end 'q7766279631452241920'
		if n mod 7766279631452241920 != refMod(n, d: 7766279631452241920) 'r7766279631452241920'
			return 18
		end 'r7766279631452241920'
	end 'p7766279631452241920'
	for n in dividends(3037000500) 'p3037000500'
		if n / 3037000500 != refDiv(n, d: 3037000500) 'q3037000500'
			return 19
		end 'q3037000500'
		if n mod 3037000500 != refMod(n, d: 3037000500) 'r3037000500'
			return 20
		end 'r3037000500'
	end 'p3037000500'
	for n in dividends(4294967297) 'p4294967297'
		if n / 4294967297 != refDiv(n, d: 4294967297) 'q4294967297'
			return 21
		end 'q4294967297'
		if n mod 4294967297 != refMod(n, d: 4294967297) 'r4294967297'
			return 22
		end 'r4294967297'
	end 'p4294967297'
	for n in dividends(6148914691236517205) 'p6148914691236517205'
		if n / 6148914691236517205 != refDiv(n, d: 6148914691236517205) 'q6148914691236517205'
			return 23
		end 'q6148914691236517205'
		if n mod 6148914691236517205 != refMod(n, d: 6148914691236517205) 'r6148914691236517205'
			return 24
		end 'r6148914691236517205'
	end 'p6148914691236517205'
	for n in dividends(-6521908912666391107) 'n6521908912666391107'
		if n / (-6521908912666391107) != refDivNeg(n, d: -6521908912666391107) 'qn6521908912666391107'
			return 25
		end 'qn6521908912666391107'
		if n mod (-6521908912666391107) != refModNeg(n, d: -6521908912666391107) 'rn6521908912666391107'
			return 26
		end 'rn6521908912666391107'
	end 'n6521908912666391107'
	for n in dividends(-6521908912666391106) 'n6521908912666391106'
		if n / (-6521908912666391106) != refDivNeg(n, d: -6521908912666391106) 'qn6521908912666391106'
			return 27
		end 'qn6521908912666391106'
		if n mod (-6521908912666391106) != refModNeg(n, d: -6521908912666391106) 'rn6521908912666391106'
			return 28
		end 'rn6521908912666391106'
	end 'n6521908912666391106'
	for n in dividends(-6521908912666391109) 'n6521908912666391109'
		if n / (-6521908912666391109) != refDivNeg(n, d: -6521908912666391109) 'qn6521908912666391109'
			return 29
		end 'qn6521908912666391109'
		if n mod (-6521908912666391109) != refModNeg(n, d: -6521908912666391109) 'rn6521908912666391109'
			return 30
		end 'rn6521908912666391109'
	end 'n6521908912666391109'
	for n in dividends(-4611686018427387905) 'n4611686018427387905'
		if n / (-4611686018427387905) != refDivNeg(n, d: -4611686018427387905) 'qn4611686018427387905'
			return 31
		end 'qn4611686018427387905'
		if n mod (-4611686018427387905) != refModNeg(n, d: -4611686018427387905) 'rn4611686018427387905'
			return 32
		end 'rn4611686018427387905'
	end 'n4611686018427387905'
	for n in dividends(-4611686018427387907) 'n4611686018427387907'
		if n / (-4611686018427387907) != refDivNeg(n, d: -4611686018427387907) 'qn4611686018427387907'
			return 33
		end 'qn4611686018427387907'
		if n mod (-4611686018427387907) != refModNeg(n, d: -4611686018427387907) 'rn4611686018427387907'
			return 34
		end 'rn4611686018427387907'
	end 'n4611686018427387907'
	for n in dividends(-9223372036854775807) 'n9223372036854775807'
		if n / (-9223372036854775807) != refDivNeg(n, d: -9223372036854775807) 'qn9223372036854775807'
			return 35
		end 'qn9223372036854775807'
		if n mod (-9223372036854775807) != refModNeg(n, d: -9223372036854775807) 'rn9223372036854775807'
			return 36
		end 'rn9223372036854775807'
	end 'n9223372036854775807'
	for n in dividends(-9223372036854775806) 'n9223372036854775806'
		if n / (-9223372036854775806) != refDivNeg(n, d: -9223372036854775806) 'qn9223372036854775806'
			return 37
		end 'qn9223372036854775806'
		if n mod (-9223372036854775806) != refModNeg(n, d: -9223372036854775806) 'rn9223372036854775806'
			return 38
		end 'rn9223372036854775806'
	end 'n9223372036854775806'
	for n in dividends(-9223372036854775805) 'n9223372036854775805'
		if n / (-9223372036854775805) != refDivNeg(n, d: -9223372036854775805) 'qn9223372036854775805'
			return 39
		end 'qn9223372036854775805'
		if n mod (-9223372036854775805) != refModNeg(n, d: -9223372036854775805) 'rn9223372036854775805'
			return 40
		end 'rn9223372036854775805'
	end 'n9223372036854775805'
	for n in dividends(-7766279631452241920) 'n7766279631452241920'
		if n / (-7766279631452241920) != refDivNeg(n, d: -7766279631452241920) 'qn7766279631452241920'
			return 41
		end 'qn7766279631452241920'
		if n mod (-7766279631452241920) != refModNeg(n, d: -7766279631452241920) 'rn7766279631452241920'
			return 42
		end 'rn7766279631452241920'
	end 'n7766279631452241920'
	for n in dividends(-3037000500) 'n3037000500'
		if n / (-3037000500) != refDivNeg(n, d: -3037000500) 'qn3037000500'
			return 43
		end 'qn3037000500'
		if n mod (-3037000500) != refModNeg(n, d: -3037000500) 'rn3037000500'
			return 44
		end 'rn3037000500'
	end 'n3037000500'
	for n in dividends(-4294967297) 'n4294967297'
		if n / (-4294967297) != refDivNeg(n, d: -4294967297) 'qn4294967297'
			return 45
		end 'qn4294967297'
		if n mod (-4294967297) != refModNeg(n, d: -4294967297) 'rn4294967297'
			return 46
		end 'rn4294967297'
	end 'n4294967297'
	for n in dividends(-6148914691236517205) 'n6148914691236517205'
		if n / (-6148914691236517205) != refDivNeg(n, d: -6148914691236517205) 'qn6148914691236517205'
			return 47
		end 'qn6148914691236517205'
		if n mod (-6148914691236517205) != refModNeg(n, d: -6148914691236517205) 'rn6148914691236517205'
			return 48
		end 'rn6148914691236517205'
	end 'n6148914691236517205'
	for n in dividends(-9223372036854775808) 'n9223372036854775808'
		if n / (-9223372036854775808) != refDivNeg(n, d: -9223372036854775808) 'qn9223372036854775808'
			return 49
		end 'qn9223372036854775808'
		if n mod (-9223372036854775808) != refModNeg(n, d: -9223372036854775808) 'rn9223372036854775808'
			return 50
		end 'rn9223372036854775808'
	end 'n9223372036854775808'
	return 0
end 'main'
```
```exitcode
0
```
