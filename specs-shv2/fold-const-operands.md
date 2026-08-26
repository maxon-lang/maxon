---
feature: fold-const-operands
status: experimental
keywords: [optimizer, codegen, immediate, identity, fold, runtime]
category: codegen
---
# Folding Constant Operands

## Documentation

`foldConstOperands` is the Std→Std pass that moves a CONSTANT operand of a `binOp` or a `cmp` into
an IMMEDIATE, so the literal never occupies a register. `5 < a` becomes `a > 5`; `a * 3` becomes
`imul a, 3` rather than a `mov` of 3 into a scratch register and a register multiply.

This file pins the two properties of that pass that nothing else in the suite can see, because both
are about instructions the program does NOT contain.

### An immediate that is its opcode's IDENTITY ELEMENT folds the op away entirely

`x * 1`, `x + 0`, `x - 0`, `x and -1`, `x or 0`, `x xor 0`, `x shl 0` and `x shr 0` all COMPUTE `x`.
Once the constant has moved into the immediate field, that is a single op whose result is its own
operand, so every read of the result is rewritten to read the operand and the op is dropped.

⚠ **THEY ARE NOT WRITTEN BY HAND, WHICH IS WHY THIS IS WORTH A PASS.** A programmer does not write
`x * 1`; a LOWERING does. An enum's ordinal is a sum of `(value == case) * 1` terms accumulated onto
a seed of 0 — which is an `imul` by one and an `add` of zero per case, back to back — and an element
address at a byte stride is `index * 1`. Every one of those is a real instruction in the emitted
code until this fold removes it.

⚠ **THE ANSWER DOES NOT DEPEND ON THE OPERAND'S WIDTH**, and the cases below are written at `int`
because that is where the identity is least interesting rather than most: every immediate form
computes at the machine word on all three targets, so `x + 0` at `Byte` is the same 64 bits as `x`.
What a narrow operand type changes is where the value is STORED, which a fold that keeps no op
cannot affect.

⚠ **NO FLOAT IDENTITY IS FOLDED, and `x * 1.0` is the reason rather than an exception to it.**
Multiplying by one quiets a signalling NaN, so it is not the identity on every `float`. The question
never reaches the rule: no SSE, NEON or wasm form takes an immediate operand at all, so
`foldConstOperands` refuses a float operand type and no float immediate form exists to classify.

### The COMPILER-EMITTED bodies are folded too

The runtime the compiler installs after the pipeline — `__managed_*`, `__mm_*`, `__str_*`, the
synthesized destructor and cloner cascades — is hand-built at the Std tier, so the pipeline's own
run of this pass never saw it. It is folded by a second run over exactly that appended band
(`CompilePhase.foldEmittedConstOperands`).

⚠ **A HAND-BUILT BODY HAS NO FOLD TO RE-DERIVE; IT HAS THE SAME CONST OPERANDS USER CODE HAS.** A
runtime builder spells a comparison against a literal as an `emitConst` and a `cmp`, which is
exactly the unfolded form — one materialized immediate and one register held for it, in bodies every
array program links.

## Tests

<!-- test: multiply-by-one-is-the-value -->
`x * 1`. The operand is a call result so that nothing upstream of the fold can see the value: the
parser's own constant folder settles `const * const`, and a case it could settle would pin the
parser rather than this pass.

```maxon
function opaque(n int) returns int
	return n
end 'opaque'

function main() returns ExitCode
	return (opaque(37) * 1) as ExitCode
end 'main'
```
```exitcode
37
```

<!-- test: add-zero-is-the-value -->
`x + 0`.

```maxon
function opaque(n int) returns int
	return n
end 'opaque'

function main() returns ExitCode
	return (opaque(37) + 0) as ExitCode
end 'main'
```
```exitcode
37
```

<!-- test: subtract-zero-is-the-value -->
`x - 0` — the one identity in this file that is NOT reachable through the commutative swap, because
`sub` does not commute. `0 - x` is a negation and is left exactly where it is.

```maxon
function opaque(n int) returns int
	return n
end 'opaque'

function main() returns ExitCode
	return (opaque(37) - 0) as ExitCode
end 'main'
```
```exitcode
37
```

<!-- test: and-all-bits-set-is-the-value -->
`x and -1`. The immediate field is SIGNED and every target sign-extends it to the machine word, so
`-1` is an all-ones mask and this is the identity. A `4294967295` written here would be a 32-bit
TRUNCATION and a different question — and it never reaches the rule, because a value outside the
signed 32-bit immediate range keeps its register operand.

```maxon
function opaque(n int) returns int
	return n
end 'opaque'

function main() returns ExitCode
	return (opaque(37) and -1) as ExitCode
end 'main'
```
```exitcode
37
```

<!-- test: or-zero-is-the-value -->
`x or 0` — an all-zero operand sets no bit, which is the same statement about a bit that `+ 0` is
about a sum.

```maxon
function opaque(n int) returns int
	return n
end 'opaque'

function main() returns ExitCode
	return (opaque(37) or 0) as ExitCode
end 'main'
```
```exitcode
37
```

<!-- test: xor-zero-is-the-value -->
`x xor 0` — and an all-zero operand flips none.

```maxon
function opaque(n int) returns int
	return n
end 'opaque'

function main() returns ExitCode
	return (opaque(37) xor 0) as ExitCode
end 'main'
```
```exitcode
37
```

<!-- test: shift-left-zero-is-the-value -->
`x shl 0`. A count of zero moves no bit, and it is the one shift count on which the imm8 form and
the `cl` form can never disagree — the hardware's mask is a no-op on it.

```maxon
function opaque(n int) returns int
	return n
end 'opaque'

function main() returns ExitCode
	return (opaque(37) shl 0) as ExitCode
end 'main'
```
```exitcode
37
```

<!-- test: shift-right-zero-is-the-value -->
`x shr 0` — the arithmetic, sign-filling direction. Zero bits shifted out is zero sign bits shifted
in, so the identity holds on a negative value too; the case returns a non-negative one only because
`ExitCode` cannot carry the other.

```maxon
function opaque(n int) returns int
	return n
end 'opaque'

function main() returns ExitCode
	return (opaque(37) shr 0) as ExitCode
end 'main'
```
```exitcode
37
```

<!-- test: an-identity-chain-folds-in-one-pass -->
`(x * 1) + 0` — the shape an enum ordinal's accumulator actually produces, and the one that needs
the fold to see through ITSELF: the `add`'s operand is the `mul`'s result, which is being removed in
the same walk. Both go, and the golden below is where that shows.

```maxon
function opaque(n int) returns int
	return n
end 'opaque'

function main() returns ExitCode
	return ((opaque(37) * 1) + 0) as ExitCode
end 'main'
```
```exitcode
37
```

<!-- test: a-non-identity-immediate-is-untouched -->
The control. `x * 2` and `x + 1` are not identities, so both keep their immediate form — this is
what makes the eight cases above evidence that the IDENTITY is what was recognised, rather than that
immediates are being dropped.

```maxon
function opaque(n int) returns int
	return n
end 'opaque'

function main() returns ExitCode
	return ((opaque(18) * 2) + 1) as ExitCode
end 'main'
```
```exitcode
37
```

<!-- test: the-emitted-runtime-is-folded-too -->
`__managed_fill` and `__managed_get` — two bodies the compiler installs after the pipeline, so the
pipeline's own run of the pass never saw them. Every `movRegImm32 <reg>, <k>` immediately followed
by a `cmpRegReg` against that register is an unfolded compare; the golden shows `cmpRegImm32`
instead, and no `movRegImm32` feeding a compare at all.

`refill` is what reaches both: it fills a window through `__managed_fill` and the `get` reads one
slot back out.

```maxon
function main() returns ExitCode
	var a = [1, 2, 3]
	a.refill(6, value: 7)
	return (try a.get(5) otherwise 9) as ExitCode
end 'main'
```
```exitcode
7
```
```RequiredRuntime
__managed_fill
__managed_get
```
