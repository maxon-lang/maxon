---
feature: common-subexpression-elimination
status: experimental
keywords: [optimizer, codegen, cse, dominance, purity, register-pressure]
category: codegen
---
# Reusing a value the program already computed

## Documentation

`eliminateCommonSubexpressions` is the Std→Std pass that replaces a PURE computation whose operator
and operands the program has already evaluated on the way here with a read of that earlier result.
`x * 31 + y` written three times stops being three `imul`/`lea` pairs into three simultaneously-live
registers and becomes one.

It pays twice: fewer instructions, and a narrower live set for the chordal register allocator. ⚠ And
it can cost in that second column, which is what the call rule below is about.

### The operands have to be RUNTIME values, or the case tests the wrong pass

`foldConstants` (EC12) runs two passes earlier and evaluates `const <op> const` outright, so a
repeated expression over literals is gone before this pass sees it. Every case below therefore feeds
its expression a value the compiler cannot see through — a parameter, a loop counter, an argument —
which is the only spelling that reaches this pass at all.

### What is a subject, and what is not

The roster is the dialect's — `classifyArithOperands`, shared with the two constant-folding passes —
plus the dialect's own `isPure`, which this pass is the first reader of:

- **`binOp`, `binOpImm`, `unaryOp`, `cmp`, `cmpImm`** are subjects. Two of them compute the same
  value when their operands, opcode/predicate and operand type all agree.
- **`div` and `mod` are NOT**, and the flag is honoured rather than reasoned around. They carry
  `isPure: false` because x64 `idiv` faults on a zero divisor and on `i64.min / -1`. Two divisions
  with equal operands do fault identically, so dropping the second is arguably safe — and a pass that
  special-cases its way past a purity flag is a pass that will be wrong about the next impure op
  somebody adds. ⚠ **There is no runtime discriminator for this one and the case below says so**: the
  only difference a wrong answer would make is how many times the program traps, and one trap ends it.
  The committed fragment showing two `idivReg` is the record.
- **MEMORY IS OUT, entirely and on purpose.** A `loadIndirect` is `isPure: false` — a load's value can
  be changed by a store the pass cannot see — so it is neither in the roster nor past the purity gate.
  Two loads of one address are left as two, ALWAYS, with no attempt at store invalidation. Hoisting
  loads is `EC14`'s row, and it needs the aliasing analysis this rung does not have.
- **FLOATS ARE IN**, and unlike constant folding that needs no argument about NaNs or signed zero:
  CSE changes no value, it reuses one the program already computed, so the bits are the bits the
  second `mulsd` would have produced.

### Dominance decides where a value is available, and a CALL decides whether reusing it is worth it

A candidate is compared only against expressions computed in blocks that DOMINATE it — exactly the
set whose values are available on every path reaching it. Sibling arms of an `if` do not dominate each
other, so an expression computed in one is not reused in the other.

⚠ **A CALL IS A BARRIER, and that rule is what keeps this pass from turning a compile into an
`E5001`.** shv2 REFUSES rather than spills when a loop needs more registers than the target has, so a
CSE that lengthens a live range is not "slower code", it is a compile error — and the ranges that cost
most are those crossing a call, which are confined to the five callee-saved registers x64-windows
leaves. MEASURED: without the rule,
`generic-hash-table-regalloc/generic-hash-table-regalloc.witness-dispatch-inside-a-pressured-loop`
went red with `E5001 … needs 1 more register`, for ONE reused expression.

### Commutativity is a canonical ORDER, and it stops at the opcodes that have one

`a + b` and `b + a` are one expression; the pass puts the lower `ValueId` on the left so the two
compare equal. `a - b` and `b - a` are NOT, and the case below is what makes that a wrong ANSWER
rather than a wrong comment. Float `add`/`mul` are excluded from the reordering even though they are
commutative on real numbers: x64's `addsd`/`mulsd` answer with the destination operand's NaN, so a
swap can change which payload propagates.

## Tests

<!-- test: a-repeated-expression-is-computed-once -->
The shape the row was opened for. Three occurrences of `x * 31 + y` over runtime operands: the
committed fragment carries ONE `imulRegRegImm32` where it used to carry three, and the answer is the
same either way, which is the point.
```maxon
typealias Word = int(i64.min to i64.max)

function work(x Word, y Word) returns Word
	let a = x * 31 + y
	let b = x * 31 + y
	let c = x * 31 + y
	return a + b + c
end 'work'

function main() returns ExitCode
	var seed = 0
	for i in 0 upto 4 'feed'
		seed = seed + work(i, y: i + 1)
	end 'feed'
	if seed != 588 'wrongSum'
		return 1
	end 'wrongSum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-dominating-block-supplies-the-value -->
The second occurrence is in a block the first DOMINATES, so it is reused across the block boundary —
and BOTH arms reuse it, which is what says the entry block's value is available on either path. The
loop is what keeps `x` and `y` runtime values; called with literals the whole function folds to a
`mov` and the case would pass without ever reaching this pass.
```maxon
typealias Word = int(i64.min to i64.max)

function pick(x Word, y Word) returns Word
	let base = x * 31 + y
	if y > 0 'positive'
		return base + x * 31 + y
	end 'positive' else 'negative'
		return base * 2 - (x * 31 + y)
	end 'negative'
end 'pick'

function main() returns ExitCode
	var seed = 0
	for i in 0 upto 4 'feed'
		seed = seed + pick(i, y: i - 2)
	end 'feed'
	if seed != 278 'wrongSum'
		return 1
	end 'wrongSum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-sibling-arm-does-not-supply-the-value -->
Neither arm of an `if` dominates the other, so the expression computed in one is NOT reused in the
other — both arms compute it. A pass that reused across siblings would be reading a value that is
not defined on the path it landed on; here the two arms differ in what they do with it, so the
answers are what would move.
```maxon
typealias Word = int(i64.min to i64.max)

function branchy(x Word, y Word) returns Word
	var out = 0
	if y > 0 'positive'
		out = (x * 31 + y) * 2
	end 'positive' else 'negative'
		out = (x * 31 + y) * 3
	end 'negative'
	return out
end 'branchy'

function main() returns ExitCode
	var seed = 0
	for i in 0 upto 4 'feed'
		seed = seed + branchy(i, y: i - 2)
	end 'feed'
	if seed != 458 'wrongSum'
		return 1
	end 'wrongSum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-commutative-pair-is-one-expression-and-a-subtraction-is-not -->
⭐ **THE DISCRIMINATING CASE FOR THE CANONICAL ORDER.** `a + b` and `b + a` are the same expression
and may be computed once. `a - b` and `b - a` are not, and reordering their operands would return one
where the other was asked for — so the second half fails with a distinct code if the commutativity
rule ever grows a `sub` arm.
```maxon
typealias Word = int(i64.min to i64.max)

function sums(a Word, b Word) returns Word
	return (a + b) + (b + a)
end 'sums'

function differences(a Word, b Word) returns Word
	return (a - b) * 10 + (b - a)
end 'differences'

function main() returns ExitCode
	var total = 0
	for i in 0 upto 4 'feed'
		total = total + sums(i, b: i + 3) + differences(i, b: i + 3)
	end 'feed'
	// `sums` is `4i + 6`; `differences` is `-30 + 3`, and it is `-33` rather than `-27` exactly when
	// `b - a` has been rewritten to `a - b`.
	if total != -60 'wrongTotal'
		return 1
	end 'wrongTotal'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-load-across-a-store-is-not-reused -->
⭐ **THE DISCRIMINATING CASE FOR MEMORY.** The same field is read, WRITTEN, and read again. A pass
that treated the second read as a recomputation of the first would hand back the stale value and the
sum would be 2 instead of 3 — which is why memory being `isPure: false` is a rule the roster enforces
rather than a note.
```maxon
typealias Word = int(i64.min to i64.max)

type Cell
	export var slot as Word

	export static function create(slot Word) returns Cell
		return Self{slot: slot}
	end 'create'
end 'Cell'

function main() returns ExitCode
	var c = Cell.create(1)
	let before = c.slot
	c.slot = 2
	let after = c.slot
	if before + after != 3 'staleRead'
		return 1
	end 'staleRead'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-division-is-never-reused -->
Two identical divisions and two identical remainders over runtime operands. ⚠ **NOTHING AT RUN TIME
CAN TELL THE TWO ANSWERS APART, AND SAYING SO IS THE HONEST VERSION**: `div` is excluded because it
TRAPS, and the only observable difference a wrong reuse would make is how many times a faulting
program faults — which is once either way, because the first one ends it. The case checks the
arithmetic, and the committed fragment showing TWO `idivReg` is the record of the exclusion.
```maxon
typealias Word = int(i64.min to i64.max)
// A divisor whose type EXCLUDES ZERO, so the divisions do not throw and the case can be about CSE
// rather than about `try`. The op is still `StdOp.div`/`StdOp.mod` and still `isPure: false`.
typealias NonZeroWord = int(1 to i64.max)

function halves(a Word, b NonZeroWord) returns Word
	return (a / b) + (a / b) + (a mod b) + (a mod b)
end 'halves'

function main() returns ExitCode
	if halves(17, b: 5) != 10 'quotientAndRemainder'
		return 1
	end 'quotientAndRemainder'
	if halves(-17, b: 5) != -10 'negativeTruncatesTowardZero'
		return 2
	end 'negativeTruncatesTowardZero'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-float-expression-is-reused -->
Floats are subjects: CSE changes no value, it reuses one the program already computed, so the bits
are the bits the second `mulsd` would have produced. The answer is printed rather than only compared,
so a reuse that produced a different double would show in the output.
```maxon
typealias Real = float(f64.min to f64.max)

function energy(m Real, v Real) returns Real
	return (m * v * v) + (m * v * v)
end 'energy'

function main() returns ExitCode
	print("{energy(2.0, v: 3.0)}\n")
	print("{energy(0.5, v: -4.0)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
36.0
16.0
```

<!-- test: an-expression-across-a-call-is-recomputed -->
The register-pressure rule, in the shape it exists for: the two occurrences are separated by a CALL,
so the value is recomputed rather than carried across it in a callee-saved register. Nothing at run
time distinguishes the two codegens — the answer is the same — so the committed fragment showing TWO
`imulRegRegImm32` either side of the `callDirect` is the record. What the case pins here is that the
program still answers correctly under the rule.
```maxon
typealias Word = int(i64.min to i64.max)

// RECURSIVE, so `inlineLeaves` refuses it and the call in `across` survives to be a barrier. A
// non-recursive body would be spliced into the caller and the case would silently lose its call —
// measured on the first cut of this spec, whose fragment showed no `callDirect` at all.
function noise(v Word) returns Word
	if v <= 0 'base'
		return 0
	end 'base'
	return v + noise(v - 1)
end 'noise'

function across(x Word, y Word) returns Word
	let first = x * 31 + y
	let middle = noise(first)
	return first + middle + (x * 31 + y)
end 'across'

function main() returns ExitCode
	var seed = 0
	for i in 0 upto 3 'feed'
		seed = seed + across(i, y: i + 1)
	end 'feed'
	if seed != 2905 'wrongSum'
		return 1
	end 'wrongSum'
	return 0
end 'main'
```
```exitcode
0
```
