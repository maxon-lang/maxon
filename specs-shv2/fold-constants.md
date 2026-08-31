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
- **A RANGE CHECK still runs.** Folding produces a value; it does not decide whether that value meets
  a ranged typealias, and the guard `insertRangeChecks` emitted must still fire, at the same line and
  with the same message.

### FLOATS FOLD, and the rule is HOST BITS == INSTRUCTION BITS OR NOTHING

A float constant is a `ParsedInt` holding the double's IEEE-754 BITS on the same `StdOp.const` an
integer uses, so folding one is the same rewrite — what is different is the argument that the answer
the compiling host computes is the answer the *target's* instruction would have computed, bit for
bit. Where that argument holds the operation folds; where it does not, the operation keeps its
instruction, which is this pass's standing rule and not a float exception to it.

It holds for `+ - * /`, `min`/`max`, every comparison, `floor`/`ceil`/`round`/`sqrt`/`abs`/`-x`, and
the int↔float conversions and reinterpretations: IEEE-754 fixes each of those to a single correctly
rounded answer, and shv2 emits that one answer on all three targets (`round` is ties-to-even —
`roundsd 0`, `frintn`, `f64.nearest` — and `min`/`max` are the compare-and-select
`StdBinOpcode` defines, which is why a fold may never swap their operands).

**Three things are declined, each for its own reason:**

- **A DIVISION BY A CONSTANT ZERO**, `+0.0` and `-0.0` alike — the emitted divide throws on both, and
  a constant has no run time to throw in.
- **A COMPUTED ANSWER THAT IS A NaN.** A generated NaN's PAYLOAD is the machine's to choose — x64's
  indefinite is `0xFFF8…`, arm64's default is `0x7FF8…`, and wasm leaves it nondeterministic — so
  baking the compiling host's payload into a cross-compiled binary would disagree with the
  instruction it replaced, and `float.hash` and `"{x}"` can both see the difference. `inf - inf` and
  `sqrt(-4.0)` therefore keep their instructions. (`min`/`max` and the sign operations only ever hand
  back an OPERAND's pattern, so they could fold a NaN safely; they are declined with the rest because
  no program can spell a NaN CONSTANT to reach them with, and one rule is worth more than the case.)
- **A TRUNCATION WHOSE ANSWER IS NOT AN `i64`** — `trunc` of a NaN, an infinity, or a magnitude at or
  above `2^63`. The three targets disagree there and are entitled to: x64 `cvttsd2si` answers the
  indefinite integer, arm64 `fcvtzs` saturates, and wasm's `i64.trunc_f64_s` **traps**. Folding one
  would replace a wasm trap with a number.

⛔ **TWO OF THOSE THREE DECLINES HAVE NO CASE IN THIS FILE, AND SAYING SO IS THE POINT.** Both were
sabotage-measured on 2026-08-30 and BOTH SABOTAGES LEAVE THE WHOLE SUITE GREEN, on this lane and on
wasm, because the compiler here runs on an x64 host and on x64 the folded answer and the emitted
instruction's answer are the same number:

- **The NaN decline** is visible only in `a-computed-nan-keeps-its-instruction`'s committed fragment,
  where removing it deletes the `subsd`. It CANNOT be reached by a runtime assertion: no user door
  spells a NaN *constant* (there is no NaN literal, `__Builtins.bitsToFloat` is reserved), and every
  program that can make one prints `nan` and answers `y != y` true whichever payload it holds. The
  divergence is between two MACHINES, and one host's suite cannot stage it.
- **The `fpToSi` range guard** IS observable, on wasm, and could not be pinned for a harness reason
  rather than a semantic one. MEASURED with the guard removed: `trunc(1.0e300)` compiled to
  `wasm32-wasi` prints `-9223372036854775808` — the compiling HOST's `cvttsd2si` answer, baked into a
  binary for a machine that never ran it — and exits 0, where the guard leaves the emitted
  `i64.trunc_f64_s` in place and wasmtime traps with exit 3. A case for it would have to pin
  wasmtime's stderr, which names the module by ABSOLUTE PATH; `checkRunStderr` compares that text
  exactly and has no normalizer for it. **A path normalizer for the wasm runner would make this a real
  case, and it is the one thing missing.**

⚠ **The float control below is INVERTED from what it was.** It used to pin that the guard refusing
every float was load-bearing; it now pins the fold. The same sabotage still measures it, one field
along: mint the folded float's `const` at `FoldedArithType` (i64) instead of `FoldedFloatType` and the
x64 emitter dies with *"rax is in the gpr register file where the xmm file is required"* while the
wasm module fails validation on the local's class.

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

<!-- test: a-float-constant-expression-is-evaluated -->
⭐ **THE CONTROL, AND IT IS STILL SABOTAGE-MEASURED — one field along.** The three multiplications
and the addition below leave no `mulsd`/`addsd` in the committed fragment: each is one
`movsd xmm, [rip + __fconst_…]`. What the sabotage moved to is the MINTED TYPE. Carry the folded
float on a `const` typed `FoldedArithType` (i64) rather than `FoldedFloatType` (f64) and the value
lands in the wrong register file: MEASURED 2026-08-28 on this very program,
`x64 emitter: rax is in the gpr register file where the xmm file is required`; on wasm the same
mistake declares an i64 local that every `f64.*` consumer fails validation against.
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

<!-- test: signed-zero-arithmetic-folds-to-the-right-zero -->
⭐ **THE FIRST OF THE THREE REASONS THIS FOLD WAS DEFERRED, ANSWERED BY MEASUREMENT.** `-0.0 + 0.0`
is `+0.0` and `-0.0 + -0.0` is `-0.0`; the sign flip of `+0.0` is `-0.0` and never `+0.0`, because a
float negate is a sign-bit FLIP and not `0.0 - x`. Every answer here is asserted through `"{…}"`
rather than `==`, deliberately: `-0.0 == 0.0` is TRUE in IEEE-754, so a comparison cannot tell the
two zeros apart and a case written that way would pass over the exact wrong answer it exists to
catch.
```maxon
typealias Real = float(f64.min to f64.max)

function addF(a Real, b Real) returns Real
	return a + b
end 'addF'

function negF(a Real) returns Real
	return -a
end 'negF'

function main() returns ExitCode
	if "{addF(-0.0, b: 0.0)}" != "0.0" 'oppositeZerosMakePositive'
		return 1
	end 'oppositeZerosMakePositive'
	if "{addF(-0.0, b: -0.0)}" != "-0.0" 'twoNegativeZerosStayNegative'
		return 2
	end 'twoNegativeZerosStayNegative'
	if "{negF(0.0)}" != "-0.0" 'theFlipMakesNegativeZero'
		return 3
	end 'theFlipMakesNegativeZero'
	if "{negF(-0.0)}" != "0.0" 'andFlipsBack'
		return 4
	end 'andFlipsBack'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-float-division-by-a-constant-zero-is-not-folded -->
⭐ **THE FLOAT TRAP CASE, AND IT DISCRIMINATES ON BOTH ZEROS.** After `pickF` inlines, both operands
of each divide are constants, so a folder that answered for `1.0 / 0.0` would replace a catchable
throw with `inf` and the `otherwise` arm would stop firing. `-0.0` is the second half and is not a
duplicate: a bit test for a zero divisor reads the negative zero as the ordinary number
`i64.min` and would let it through, which is why the fold declines by riding the host's own divide
rather than by inspecting the pattern. The third check is the control on the control — an ordinary
divisor still folds.
```maxon
typealias Real = float(f64.min to f64.max)

function pickF(a Real) returns Real
	return a
end 'pickF'

function main() returns ExitCode
	let one = pickF(1.0)
	if (try (one / pickF(0.0)) otherwise -77.0) != -77.0 'positiveZeroStillThrows'
		return 1
	end 'positiveZeroStillThrows'
	if (try (one / pickF(-0.0)) otherwise -78.0) != -78.0 'negativeZeroStillThrows'
		return 2
	end 'negativeZeroStillThrows'
	if (try (one / pickF(4.0)) otherwise -79.0) != 0.25 'anOrdinaryDivisionStillFolds'
		return 3
	end 'anOrdinaryDivisionStillFolds'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-overflowing-float-fold-answers-infinity -->
The float edge that the integer `overflow-wraps-exactly-as-the-instruction-does` is the twin of:
where an integer `add` WRAPS, a float `mul` past `f64.max` goes to `+inf` and stays there. A folder
that refused the operation, saturated at `f64.max`, or computed in a wider format would disagree with
`mulsd` on exactly this input. The committed fragment shows the answer arriving as a `.rdata` load of
the infinity pattern, which is also what says the constant pool can NAME a value no literal spells.
```maxon
typealias Real = float(f64.min to f64.max)

function scaleF(a Real, b Real) returns Real
	return a * b
end 'scaleF'

function main() returns ExitCode
	let big = scaleF(1.0e308, b: 10.0)
	if "{big}" != "inf" 'overflowsToInfinity'
		return 1
	end 'overflowsToInfinity'
	if big != big * 2.0 'infinityIsAbsorbing'
		return 2
	end 'infinityIsAbsorbing'
	if "{scaleF(-1.0e308, b: 10.0)}" != "-inf" 'andTheNegativeEnd'
		return 3
	end 'andTheNegativeEnd'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-computed-nan-keeps-its-instruction -->
⭐⭐ **THE SECOND DEFERRAL REASON, AND THE ONE THAT IS STILL A DECLINE.** `inf - inf` and
`sqrt(-4.0)` are NaN, and a NaN's PAYLOAD is the machine's to choose — x64's indefinite is
`0xFFF8000000000000`, arm64's default NaN is `0x7FF8000000000000`, and wasm does not promise one at
all. Folding either would bake THE COMPILING HOST's payload into a cross-compiled binary, so both
keep their instructions and the machine that runs them picks.

⚠ **WHAT THE CHECKS BELOW PIN IS THAT THE ANSWER IS *A* NaN — NOT THAT THE FOLD DECLINED**, and the
difference is worth being exact about. `y != y` is true of every NaN on every target, so it stays true
whichever payload the program ends up holding; MEASURED 2026-08-30, this case is still GREEN with the
decline removed. **What sees the decline is this case's committed FRAGMENT**, where removing it deletes
the `subsd` — and that is the whole of the coverage, for the reason the Documentation gives: a payload
divergence is between two MACHINES, and no assertion runnable on one host can stage it.
```maxon
typealias Real = float(f64.min to f64.max)

function scaleF(a Real, b Real) returns Real
	return a * b
end 'scaleF'

function subF(a Real, b Real) returns Real
	return a - b
end 'subF'

function sqrtF(a Real) returns Real
	return sqrt(a)
end 'sqrtF'

function main() returns ExitCode
	let inf = scaleF(1.0e308, b: 10.0)
	let notANumber = subF(inf, b: inf)
	if notANumber == notANumber 'infinityMinusInfinityIsNan'
		return 1
	end 'infinityMinusInfinityIsNan'
	let root = sqrtF(-4.0)
	if root == root 'rootOfANegativeIsNan'
		return 2
	end 'rootOfANegativeIsNan'
	if "{notANumber}" != "nan" 'andItPrintsAsOne'
		return 3
	end 'andItPrintsAsOne'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: min-and-max-fold-to-the-compare-and-select-definition -->
⭐⭐ **THE FOUR SIGNED-ZERO INPUTS `StdBinOpcode`'s `min`/`max` HEADER TABULATES, ASKED OF THE
FOLDER.** `min(a, b)` is `a` when `a < b` and `b` otherwise; `max(a, b)` is `a` when `a > b` and `b`
otherwise. Two zeros of opposite sign compare EQUAL, so the strict test is false in every direction
and BOTH ops answer with their SECOND operand — which is why the `min` and `max` rows below are
identical and why that is the definition being honest rather than a copy-paste. **A fold that
commuted the operands would invert all four**, and `binOpcodeIsCommutative` answers `false` for both
precisely so that nothing may. Asserted through `"{…}"` for the reason
`signed-zero-arithmetic-folds-to-the-right-zero` gives.
```maxon
typealias Real = float(f64.min to f64.max)

function minF(a Real, b Real) returns Real
	return min(a, b)
end 'minF'

function maxF(a Real, b Real) returns Real
	return max(a, b)
end 'maxF'

function main() returns ExitCode
	if "{minF(0.0, b: -0.0)}" != "-0.0" 'minTakesTheSecond'
		return 1
	end 'minTakesTheSecond'
	if "{minF(-0.0, b: 0.0)}" != "0.0" 'minTakesTheSecondEitherWay'
		return 2
	end 'minTakesTheSecondEitherWay'
	if "{maxF(0.0, b: -0.0)}" != "-0.0" 'maxTakesTheSecondToo'
		return 3
	end 'maxTakesTheSecondToo'
	if "{maxF(-0.0, b: 0.0)}" != "0.0" 'maxTakesTheSecondEitherWay'
		return 4
	end 'maxTakesTheSecondEitherWay'
	if minF(2.5, b: 4.0) != 2.5 'minOrdersDistinctOperands'
		return 5
	end 'minOrdersDistinctOperands'
	if maxF(2.5, b: 4.0) != 4.0 'maxOrdersDistinctOperands'
		return 6
	end 'maxOrdersDistinctOperands'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-comparison-on-float-constants-folds -->
The float twin of `comparisons-fold-signed-at-the-machine-word`, and it exists for the same reason:
one bit pattern, two readings. A folder that compared the raw `ParsedInt`s would get `-1.0 > -2.0`
BACKWARDS — a double's pattern is sign-and-magnitude, so the negative side orders the wrong way round
— and it would call `-0.0 == 0.0` false where IEEE-754 calls it true. The last pair is the payoff: a
folded float `cmp` is a known condition, so `foldConstantBranches` folds the `if` on it and drops the
arm not taken — `elseArm` is absent from the committed fragment entirely, and `andNotBackwards`'
whole check with it.

⚠ **THE `xorRegImm32`/`cmpRegImm32` PAIRS THAT SURVIVE ARE THE `not`s, AND THEY SURVIVE FOR THE
BOUNDARY `a-constant-condition-costs-nothing` ALREADY NAMES**, not for want of a folder. `not X` over a
constant DOES fold — `if not (2 > 1)` compiles to no instructions at all — but a leaf call's result
reaches the caller as a BLOCK ARG of the inliner's continuation (`__il_cont`), and this pass reads only
what a `const` op materializes. So `not greater(…)` sees a phi where `not (…)` sees a constant. What
each `movRegImm32 rax, 1` above IS, is the float comparison this case exists for, already folded.
```maxon
typealias Real = float(f64.min to f64.max)

function greater(a Real, b Real) returns bool
	return a > b
end 'greater'

function equal(a Real, b Real) returns bool
	return a == b
end 'equal'

function main() returns ExitCode
	if not greater(-1.0, b: -2.0) 'negativesOrderAsNumbers'
		return 1
	end 'negativesOrderAsNumbers'
	if greater(-2.0, b: -1.0) 'andNotBackwards'
		return 2
	end 'andNotBackwards'
	if not equal(-0.0, b: 0.0) 'theTwoZerosCompareEqual'
		return 3
	end 'theTwoZerosCompareEqual'

	var taken = 0
	if greater(2.5, b: 1.5) 'thenArm'
		taken = 7
	end 'thenArm' else 'elseArm'
		taken = 11
	end 'elseArm'
	if taken != 7 'thenArmRan'
		return 4
	end 'thenArmRan'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: math-intrinsics-over-constants-fold -->
The six f64 → f64 unary shapes, folded. `sqrt` is the one IEEE-754 requires to be CORRECTLY ROUNDED,
so the host and all three targets answer the same bits; `floor`/`ceil` are exact; `round` is
**ties-to-even** on every target this compiler emits for (`roundsd 0`, `frintn`, `f64.nearest`) and
`round(2.5)` is 2 rather than 3 — which is the check that would go red if a folder reached for a
ties-away-from-zero routine instead. `abs` and `-x` are the two pure bit operations, and their
signed-zero answers are what say so.
```maxon
typealias Real = float(f64.min to f64.max)

function sqrtF(a Real) returns Real
	return sqrt(a)
end 'sqrtF'

function floorF(a Real) returns Real
	return floor(a)
end 'floorF'

function ceilF(a Real) returns Real
	return ceil(a)
end 'ceilF'

function roundF(a Real) returns Real
	return round(a)
end 'roundF'

function absF(a Real) returns Real
	return abs(a)
end 'absF'

function negF(a Real) returns Real
	return -a
end 'negF'

function main() returns ExitCode
	if sqrtF(2.25) != 1.5 'squareRoot'
		return 1
	end 'squareRoot'
	if floorF(-2.5) != -3.0 'floorGoesDown'
		return 2
	end 'floorGoesDown'
	if ceilF(-2.5) != -2.0 'ceilingGoesUp'
		return 3
	end 'ceilingGoesUp'
	if roundF(2.5) != 2.0 'tiesGoToEven'
		return 4
	end 'tiesGoToEven'
	if roundF(3.5) != 4.0 'andEvenIsNotAlwaysDown'
		return 5
	end 'andEvenIsNotAlwaysDown'
	if "{absF(-0.0)}" != "0.0" 'absClearsTheSignBit'
		return 6
	end 'absClearsTheSignBit'
	if "{negF(2.5)}" != "-2.5" 'negFlipsIt'
		return 7
	end 'negFlipsIt'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-promotion-of-a-constant-folds -->
`siToFp` — the int→f64 conversion `1 + 2.0` needs, folded to the double the instruction would have
computed. It is total over `i64` and correctly rounded to nearest-even on the host and on all three
targets, which the second check is the discriminating input for: `2^53 + 1` has no exact double, and
round-to-nearest-EVEN answers `9007199254740992`, one below the integer written. A folder that
truncated or rounded away from zero would answer `9007199254740994`.
```maxon
typealias Real = float(f64.min to f64.max)
typealias Word = int(i64.min to i64.max)

function mix(i Word, f Real) returns Real
	return i + f
end 'mix'

function main() returns ExitCode
	if mix(3, f: 0.5) != 3.5 'anOrdinaryPromotion'
		return 1
	end 'anOrdinaryPromotion'
	if "{mix(9007199254740993, f: 0.0)}" != "9007199254740992.0" 'roundsToNearestEven'
		return 2
	end 'roundsToNearestEven'
	if mix(-2, f: -0.5) != -2.5 'andTheNegativeSide'
		return 3
	end 'andTheNegativeSide'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-truncation-of-a-constant-folds-inside-the-i64-range -->
`fpToSi` — `trunc` toward zero, folded, and ONLY where the answer is an `i64`. Inside the range the
three targets and the host agree exactly, which is what these checks pin. **There is deliberately no
out-of-range case here**: `trunc(1.0e300)` has three different right answers — x64's `cvttsd2si`
gives the indefinite integer, arm64's `fcvtzs` saturates to `i64.max`, and wasm's
`i64.trunc_f64_s` **TRAPS** — so the value that program prints is target-dependent and cannot be
pinned by one expectation. The fold declines there, which is what keeps the wasm trap a trap; a case
that could see it would have to be three cases with three answers.
```maxon
typealias Real = float(f64.min to f64.max)
typealias Word = int(i64.min to i64.max)

function truncF(a Real) returns Word
	return trunc(a)
end 'truncF'

function main() returns ExitCode
	if truncF(3.99) != 3 'towardZeroFromAbove'
		return 1
	end 'towardZeroFromAbove'
	if truncF(-3.99) != -3 'towardZeroFromBelow'
		return 2
	end 'towardZeroFromBelow'
	if truncF(-0.5) != 0 'andTheFractionBelowZero'
		return 3
	end 'andTheFractionBelowZero'
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
