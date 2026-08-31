---
feature: fold-constants
status: experimental
keywords: [optimizer, codegen, constant, fold, overflow, shift, branch]
category: codegen
---
# Evaluating `const <op> const`

## Documentation

`foldConstants` is the Std→Std pass that EVALUATES an integer operation — two-operand or one-operand —
whose operands are all constant and replaces it with the answer, and that folds a `condBranch` on a
known condition into an unconditional `branch`. `3 * 31 + 4` stops being `mov`/`imul`/`lea` and becomes
one `mov rax, 97`; `-K` stops being `mov`/`neg` and becomes one `mov`.

### The expressions it exists for are the ones the PARSER never saw

`2 + 3 * 4` written as such is settled by the parser's own constant domain long before this pass, so
nothing here is written that way. What this pass folds is what INLINING makes constant: `inlineLeaves`
(EC5) splices a tiny leaf's body into its call site with the caller's literal arguments substituted
for the parameters, so `scale(3, b: 4)` becomes `const 3 * const 31 + const 4` in the caller —
three constant expressions that were parameters at parse time.

Every case below therefore hands its literals to a **tiny leaf function** and checks the answer. That
is not decoration: it is the only spelling that reaches this pass.

### ⭐ A WRONG FOLD IS A WRONG EXIT CODE, NOT A SILENT PASS

Each case compares the folded answer against the value the *instruction* produces and returns a
distinct non-zero code when the two disagree, so a fold that computed the wrong number picks the arm
that fails rather than passing quietly.

⭐ **AND THE COMPARISON ITSELF IS NOT FOLDED, WHICH IS WHY THESE CASES STILL RUN.** A call's result
arrives in the caller as a BLOCK ARG of the inliner's continuation block (`__il_cont`), and this pass
reads only what a `const` op materializes — it does not look through a phi. So the committed
fragments show the folded literal being materialized and then COMPARED at run time
(`movRegImm rax, 9223372036854775805` … `cmpRegReg`), which is exactly the shape that makes a wrong
constant an observable wrong answer instead of a compile-time tautology.

### What it must NOT fold, and why each is separate

- **`div` and `mod` TRAP.** They are not `binOp`s at all — integer division has its own `StdOp.div`
  and `StdOp.mod` carrying `isPure: false`, because x64 `idiv` faults on a zero divisor and on
  `i64.min / -1`. A folder that answered for `10 / 0` would replace a catchable throw with a number.
- **Overflow must WRAP, exactly as the hardware wraps.** A fold that saturated, widened or panicked
  where `add`/`imul` wrap is a wrong answer, and it is invisible on every input but the edges.
- **A SHIFT COUNT outside `0..63` is left alone.** Inside that window the hardware's mask is a no-op
  and the fold, the imm8 form and the `cl` form provably agree. Outside it the parser has already
  emitted a saturation cascade (`Parser.emitGuardedShift`), whose answer this pass must not
  second-guess — so both readings appear below and must agree.
- **FLOAT arithmetic is not folded**, on an argument of its own: signed zero, NaN payloads and
  shortest-round-trip printing all make a folded float observable in a program's OUTPUT rather than
  only in its bits. The control below is what says the guard is load-bearing rather than tidy —
  remove it and two IEEE-754 bit patterns are multiplied as integers.
- **A RANGE CHECK still runs.** Folding produces a value; it does not decide whether that value meets
  a ranged typealias, and the guard `insertRangeChecks` emitted must still fire, at the same line and
  with the same message.

### The constant `if` costs nothing at all

Folding a `condBranch` orphans the arm it did not take, and the pass removes that arm itself (see
`FoldConstants.dropUnreachableBlocks` for the register-allocator invariant that makes this the pass's
own job rather than a later one's). What survives is the taken arm's ops with no compare, no
conditional jump and no merge phi — which the committed fragment for `a-constant-condition-costs-nothing`
is the record of.

## Tests

<!-- test: an-expression-inlining-made-constant-is-evaluated -->
The shape the row was opened for: three constant operations the parser could not see, because at
parse time they were a parameter, a literal and a parameter.
```maxon
typealias Word = int(i64.min to i64.max)

function scale(a Word, b Word) returns Word
	return a * 31 + b
end 'scale'

function main() returns ExitCode
	if scale(3, b: 4) != 97 'value'
		return 1
	end 'value'
	if scale(0, b: 0) != 0 'zero'
		return 2
	end 'zero'
	if scale(-2, b: 5) != -57 'negative'
		return 3
	end 'negative'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: overflow-wraps-exactly-as-the-instruction-does -->
The four edges. A fold that saturated at `i64.max`, widened to a bigger type, or refused the
operation outright would disagree with `lea`/`sub`/`imul` on exactly these inputs and on no others.
`i64.max * 3` is the one that is neither a saturation nor a sign flip — it wraps twice.
```maxon
typealias Word = int(i64.min to i64.max)

function add(a Word, b Word) returns Word
	return a + b
end 'add'

function sub(a Word, b Word) returns Word
	return a - b
end 'sub'

function mul(a Word, b Word) returns Word
	return a * b
end 'mul'

function main() returns ExitCode
	if add(i64.max, b: 1) != i64.min 'addWrapsToMin'
		return 1
	end 'addWrapsToMin'
	if sub(i64.min, b: 1) != i64.max 'subWrapsToMax'
		return 2
	end 'subWrapsToMax'
	if mul(i64.min, b: -1) != i64.min 'negateMinIsMin'
		return 3
	end 'negateMinIsMin'
	if mul(i64.max, b: 3) != 9223372036854775805 'mulWrapsTwice'
		return 4
	end 'mulWrapsTwice'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-negation-of-a-constant-is-evaluated -->
`-a` and `not a` are the ONE-operand shapes, and they reach this pass the way the two-operand ones
do: a parameter a leaf negates becomes `neg (const)` once `inlineLeaves` has substituted the caller's
literal. Until 2026-08-31 the pass evaluated only two-operand shapes, so a `-K` over an inlined (or
top-level) constant kept a `neg` instruction that the same value spelled `0 - K` never had. The
evaluator is the parser's own (`foldIntUnaryOp`, reached through `maxonUnaryOpOfStdOpcode`), so
`i64.min` negated is `i64.min` — the wrap the instruction computes, folded to the same bits.
```maxon
typealias Word = int(i64.min to i64.max)

function negate(a Word) returns Word
	return -a
end 'negate'

function complement(a Word) returns Word
	return not a
end 'complement'

function main() returns ExitCode
	if negate(42) != -42 'negative'
		return 1
	end 'negative'
	if negate(-9223372036854775808) != -9223372036854775808 'wraps'
		return 2
	end 'wraps'
	if complement(0) != -1 'allOnes'
		return 3
	end 'allOnes'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-shift-folds-inside-the-window-and-is-left-alone-outside-it -->
The two readings of THE SHIFT RULE, in one program. Counts 62 and 63 are inside the window the
instruction takes as written, so they FOLD; 64 and 100 are outside it, so the fold declines and the
saturation cascade the parser emitted computes the answer at run time. **Both must give the same
number** — that is the whole reason the window is asked rather than assumed, and `1 shl 63` is the
count where an unmasked fold and a masked instruction would first part company.

The right shifts are the sign-filling direction, where saturation is a CLAMP of the count rather than
a zeroed result: `-8 shr 70` is the sign, `-1`.

⭐⭐ **THE NEGATIVE COUNTS ARE THE TWO CHECKS THAT DISCRIMINATE, AND WITHOUT THEM THIS CASE PROVES
NOTHING ABOUT THE WINDOW.** MEASURED 2026-08-28: with `FoldConstants.evaluateIntBinOp`'s
`shiftCountIsUnguarded` gate removed, every POSITIVE count here stays green — `evalShift` saturates
to the same answer the emitted cascade computes, so folding one would have been correct. What the
gate actually stands in front of is a count the folder refuses to answer for at all: a negative
literal reaches this pass as a single `const` once `inlineLeaves` has substituted it for the
parameter, and `evalShift` PANICS on one (`negative count -1 — a negative count is E2054 and must
never reach the folder`), taking the whole compiler down. shv2 does not yet panic at RUN time for a
negative count — the emitted cascade reads it as out of range, so `1 shl -1` is 0 and `-8 shr -1` is
the sign — and those are the answers the last two checks pin.
```maxon
typealias Word = int(i64.min to i64.max)

function shiftLeft(v Word, n Word) returns Word
	return v shl n
end 'shiftLeft'

function shiftRight(v Word, n Word) returns Word
	return v shr n
end 'shiftRight'

function main() returns ExitCode
	if shiftLeft(1, n: 62) != 4611686018427387904 'inWindow'
		return 1
	end 'inWindow'
	if shiftLeft(1, n: 63) != i64.min 'atTheTopOfTheWindow'
		return 2
	end 'atTheTopOfTheWindow'
	if shiftLeft(1, n: 64) != 0 'firstCountPastTheWindow'
		return 3
	end 'firstCountPastTheWindow'
	if shiftLeft(1, n: 100) != 0 'wellPastTheWindow'
		return 4
	end 'wellPastTheWindow'
	if shiftRight(-8, n: 1) != -4 'signFillingInWindow'
		return 5
	end 'signFillingInWindow'
	if shiftRight(-8, n: 70) != -1 'signFillingPastTheWindow'
		return 6
	end 'signFillingPastTheWindow'
	if shiftLeft(1, n: -1) != 0 'negativeCountZeroFilling'
		return 7
	end 'negativeCountZeroFilling'
	if shiftRight(-8, n: -1) != -1 'negativeCountSignFilling'
		return 8
	end 'negativeCountSignFilling'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-division-by-a-constant-zero-is-not-folded -->
⭐ **THE TRAP CASE, AND IT DISCRIMINATES.** Both operands are constant after inlining, so a folder
that treated `/` and `mod` as ordinary arithmetic would have to invent an answer for `10 / 0` — and
whatever it invented, the `otherwise` arm would stop firing and this program would return 1 instead
of 0. The division stays a real instruction that faults into a catchable throw.
```maxon
typealias Word = int(i64.min to i64.max)

function pick(a Word) returns Word
	return a
end 'pick'

function main() returns ExitCode
	let ten = pick(10)
	let zero = pick(0)
	if (try (ten / zero) otherwise -77) != -77 'quotientByZeroStillThrows'
		return 1
	end 'quotientByZeroStillThrows'
	if (try (ten mod zero) otherwise -78) != -78 'remainderByZeroStillThrows'
		return 2
	end 'remainderByZeroStillThrows'
	if (try (ten / pick(3)) otherwise -79) != 3 'anOrdinaryDivisionStillDivides'
		return 3
	end 'anOrdinaryDivisionStillDivides'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: the-remainder-at-the-overflow-pair-is-zero -->
`i64.min mod -1`. The QUOTIENT is unrepresentable there and the REMAINDER is 0, and this compiler's
constant folder once refused the pair for both operators — the folder disagreeing with the language
it folds for (A1x). Nothing here folds the operation itself (a `mod` is never folded), so what this
pins is that the runtime answer and the constant domain still agree about the one input where they
could differ.
```maxon
typealias Word = int(i64.min to i64.max)

function pick(a Word) returns Word
	return a
end 'pick'

function main() returns ExitCode
	if (try (pick(i64.min) mod pick(-1)) otherwise -79) != 0 'remainderAtTheOverflowPair'
		return 1
	end 'remainderAtTheOverflowPair'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: comparisons-fold-signed-at-the-machine-word -->
Every Std integer comparison is SIGNED at 64 bits on all three targets, so the folder is
`TypeRules.foldIntCompare` and nothing else. A folder that read the operands unsigned would answer
`false` for `-1 < 0` — the first check here — and `true` for `0 < -1`.
```maxon
typealias Word = int(i64.min to i64.max)

function less(a Word, b Word) returns bool
	return a < b
end 'less'

function equal(a Word, b Word) returns bool
	return a == b
end 'equal'

function main() returns ExitCode
	if not less(-1, b: 0) 'negativeIsLessThanZero'
		return 1
	end 'negativeIsLessThanZero'
	if less(0, b: -1) 'zeroIsNotLessThanNegative'
		return 2
	end 'zeroIsNotLessThanNegative'
	if not less(i64.min, b: i64.max) 'theWholeSpan'
		return 3
	end 'theWholeSpan'
	if not equal(-1, b: -1) 'equality'
		return 4
	end 'equality'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-constant-condition-costs-nothing -->
The `condBranch` fold, end to end. Each folded condition costs the taken arm's ops and NOTHING
ELSE: the condition folds to a constant, the branch becomes unconditional, the arm that can no
longer be reached is dropped here, and `EC11`'s jump elision removes the jump that is left. In the
committed fragment `thenArm` stores 7 and `ifelse` stores 11 with no compare, no `jcc` and no block
between them. A fold that picked the WRONG arm returns 1 or 2 rather than 0.

⚠ **The two `cmpRegImm32`/`jcc` pairs that DO survive are the assertions** (`taken != 7`,
`other != 11`), and they survive on purpose: this pass is not a constant PROPAGATOR. It folds what a
`const` op materializes, and `taken` is a value merged from two arms, not a materialized constant —
so it stays a real comparison. That boundary is the pass header's, stated here because a reader
counting compares in the fragment will find these two and should know which question they answer.
```maxon
typealias Word = int(i64.min to i64.max)

function pick(a Word) returns Word
	return a
end 'pick'

function main() returns ExitCode
	var taken = 0
	if pick(5) > 3 'thenArm'
		taken = 7
	end 'thenArm' else 'elseArm'
		taken = 11
	end 'elseArm'
	if taken != 7 'thenArmRan'
		return 1
	end 'thenArmRan'

	var other = 0
	if pick(5) < 3 'falseThenArm'
		other = 7
	end 'falseThenArm' else 'falseElseArm'
		other = 11
	end 'falseElseArm'
	if other != 11 'elseArmRan'
		return 2
	end 'elseArmRan'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-float-constant-expression-is-not-folded -->
⭐ **THE CONTROL, AND IT IS SABOTAGE-MEASURED.** Remove the float guard in
`FoldConstants.evaluateBinOp` and these operands reach the INTEGER folder, which combines two
IEEE-754 bit patterns as if they were numbers and writes the result back as an INTEGER `const` — so
the failure is not even a wrong number. MEASURED 2026-08-28, this case takes the compiler down:
`x64 emitter: rax is in the gpr register file where the xmm file is required`, because the value the
fold produced is a GPR immediate and every consumer of it expects an XMM. Float folding is a rung of
its own for the reasons in the Documentation above; what this case pins is that the guard standing in
front of it is real.
```maxon
typealias Real = float(f64.min to f64.max)

function scaleF(a Real, b Real) returns Real
	return a * b
end 'scaleF'

function addF(a Real, b Real) returns Real
	return a + b
end 'addF'

function main() returns ExitCode
	if scaleF(2.5, b: 4.0) != 10.0 'product'
		return 1
	end 'product'
	if addF(0.5, b: 0.25) != 0.75 'sum'
		return 2
	end 'sum'
	if scaleF(-1.5, b: 2.0) != -3.0 'negativeProduct'
		return 3
	end 'negativeProduct'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-folded-value-still-meets-its-range-check -->
<!-- targets: x64-windows, x64-linux -->
Folding produces a VALUE; it does not decide whether that value meets a ranged typealias. `50 * 4`
is 200 and `Small` stops at 100, so the guard `insertRangeChecks` emitted before this pass ever ran
must still fire — at its own line, with its own message, and after the `print` that precedes it, so
the check is still at the POSITION the value comes to rest at rather than hoisted to the top.
```maxon
typealias Word = int(i64.min to i64.max)
typealias Small = int(0 to 100)

function scale(a Word, b Word) returns Word
	return a * b
end 'scale'

function main() returns ExitCode
	print("before\n")
	let v = scale(50, b: 4) as Small
	print("{v}\n")
	return 0
end 'main'
```
```stdout
before
```
```exitcode
1
```
```stderr
panic at a-folded-value-still-meets-its-range-check.test:11: Range check failed: value outside typealias 'Small'
Stack trace:
  in main
  in mrt_start
```

<!-- test: a-folded-value-inside-its-range-passes-the-check -->
The other half: an in-range folded value passes the guard and is used.
```maxon
typealias Word = int(i64.min to i64.max)
typealias Small = int(0 to 100)

function scale(a Word, b Word) returns Word
	return a * b
end 'scale'

function main() returns ExitCode
	let v = scale(25, b: 2) as Small
	if v != 50 'value'
		return 1
	end 'value'
	return 0
end 'main'
```
```exitcode
0
```
