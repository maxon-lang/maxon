---
feature: float-compare-branch
status: selfhosted
keywords: [float, f64, ucomisd, unordered, nan, parity, phi, ssa-destruction, block-args]
category: codegen
milestone: P1.0d.4
---

# Float compare, branch, and the unordered edge

## Documentation

`ucomisd` does not set flags the way `cmp` does, and the difference is the whole of this spec.

### It sets the UNSIGNED flags, not the signed ones

`cmp a, b` on two i64s is a subtraction, and the signed `jl`/`jge`/`jle`/`jg` family reads
the OF/SF pair it leaves behind. `ucomisd a, b` leaves OF=SF=AF=0 always, and answers in
**ZF/PF/CF** — the same three flags an UNSIGNED compare writes. So a float compare's branch
is the `jb`/`jae`/`jbe`/`ja` family, and lowering one to `jl` would read flags `ucomisd`
never wrote. `condCodeForPred` therefore takes the compare's OPERAND TYPE: the predicate
alone (`StdCmpPred.less`) does not determine the condition code, and never did — it only
looked that way while every Std compare was a signed i64.

### There is a FOURTH answer, and CF/ZF/PF encode it

An integer compare has three outcomes. A float compare has **four**: less, equal, greater,
and **unordered** — the answer when either operand is NaN. IEEE-754 says every relational
predicate is FALSE on unordered, and that `!=` is TRUE. `ucomisd` reports it by setting
**ZF=PF=CF=1**, which is the same flag state as "equal" except for PF.

That aliasing is what makes the naive lowering wrong, and it is wrong in different ways per
predicate:

| Predicate | Naive jcc | On NaN | Correct? |
|---|---|---|---|
| `a > b` | `ja` (CF=0 and ZF=0) | CF=1 ⇒ not taken | ✅ FALSE |
| `a >= b` | `jae` (CF=0) | CF=1 ⇒ not taken | ✅ FALSE |
| `a < b` | `jb` (CF=1) | CF=1 ⇒ **taken** | ❌ must be FALSE |
| `a <= b` | `jbe` (CF=1 or ZF=1) | both set ⇒ **taken** | ❌ must be FALSE |
| `a == b` | `je` (ZF=1) | ZF=1 ⇒ **taken** | ❌ must be FALSE |
| `a != b` | `jne` (ZF=0) | ZF=1 ⇒ not taken | ❌ must be TRUE |

**`>` and `>=` are already correct** — `ucomisd` sets CF on unordered precisely so the
"above" family needs no parity test. So `<` and `<=` are lowered by **SWAPPING THE
OPERANDS** and using that already-correct family: `a < b` becomes `ucomisd b, a` + `ja`. A
swap is free (the operands are two register names), and it buys the NaN answer with no
extra instruction. Four of the six predicates therefore need exactly one jump, and are
indistinguishable in shape from an integer compare-branch.

`==` and `!=` cannot be fixed by swapping — they are symmetric. They need the parity bit,
which is a SECOND condition, which is a second conditional jump.

### ⚠ The second jump is a second BLOCK, not a second jump in one block

This is the hazard the whole file exists for, and v1 shipped it as a **silent wrong answer**
(`project_x64_f64_compare_phi_copy_fix`; the case is preserved in `specs/float-type.md`'s
`float-print-negative-and-repeat`, whose formatted output made it visible). v1 lowered
`a == b` into ONE block ending in TWO conditional jumps:

```
	ucomisd a, b
	jp   else        ; unordered ⇒ not equal
	jne  else
	jmp  then
```

The IR said that block had one conditional branch and therefore ONE else edge. The machine
code had **two**. SSA destruction places a phi's copies on an edge by rewriting the jump
that takes it — so it found and rewired one of those jumps, and the other kept its original
target and **bypassed the copies entirely**. A phi whose value arrived on the `jp` path was
simply never written.

shv2 does not emit that shape. `lowerFloatEqualityBranch` **splits the compare into two
blocks**, each ending in exactly one conditional branch:

```
	cmp:    ucomisd a, b ; jp -> else          ; unordered: decided
	ordered:               je -> then          ; fallthrough -> else
```

The machine code is the same three jumps. The difference is that the IR now **says what the
machine code does**: two edges reach `else`, from two different blocks, and they are two
edges in the CFG. SSA destruction needs no float-specific case — it places copies on both,
because both are edges, and `IrBlock.CondBranch`'s one-conditional-branch-per-block
invariant is never bent. The bug is not fixed here; it is made **unrepresentable**.

The tests below put a phi on the edge each of those jumps takes, and check the value that
arrives.

## Tests

<!-- test: gt-else-edge-phi -->
The single-jump relational case. `r` is assigned only in the then-branch, so the merge block
takes a phi and the ELSE edge carries `r`'s incoming `7`. `3.5 > 9.5` is false, so the else
edge is the one taken and 7 is the value that must arrive.
```maxon
function main() returns ExitCode
	var r = 7
	let x = 3.5
	let y = 9.5
	if x > y 'check'
		r = 1
	end 'check'
	return r
end 'main'
```
```exitcode
7
```

<!-- test: lt-operand-swap -->
`<` is lowered by SWAPPING the operands and emitting the `above` family, so this must agree
with `gt-else-edge-phi` read backwards. A lowering that instead emitted `jb` would still
pass THIS test — only `lt-nan-is-false` below can tell the two apart — so the pair has to be
read together.
```maxon
function main() returns ExitCode
	var r = 7
	let x = 3.5
	let y = 9.5
	if x < y 'check'
		r = 1
	end 'check'
	return r
end 'main'
```
```exitcode
1
```

<!-- test: le-boundary-equal -->
`<=` swaps to `jae`, whose ZF=1 case is the one that distinguishes it from `ja`. Equal
operands take the then-branch.
```maxon
function main() returns ExitCode
	var r = 7
	let x = 4.25
	let y = 4.25
	if x <= y 'check'
		r = 1
	end 'check'
	return r
end 'main'
```
```exitcode
1
```

<!-- test: eq-ordered-else-edge-phi -->
`==` on two ORDERED, unequal operands: the `jp` is not taken, the `je` is not taken, and the
else edge is reached by the fallthrough out of the SECOND block. That is the edge v1 got
right. `r`'s incoming `7` must arrive on it.
```maxon
function main() returns ExitCode
	var r = 7
	let x = 3.5
	let y = 2.5
	if x == y 'check'
		r = 1
	end 'check'
	return r
end 'main'
```
```exitcode
7
```

<!-- test: eq-ordered-then-edge-phi -->
The same lowering with the `je` TAKEN — the then edge, out of the second block, past a phi.
```maxon
function main() returns ExitCode
	var r = 7
	let x = 2.5
	let y = 2.5
	if x == y 'check'
		r = 1
	end 'check'
	return r
end 'main'
```
```exitcode
1
```

<!-- test: eq-nan-else-edge-phi -->
⭐ **THE REGRESSION.** `nan == nan` is FALSE, and it is decided by the `jp` in the FIRST
block — the edge v1's SSA destruction never rewired. `r`'s incoming `7` must arrive on it;
v1's bug is exactly the case where it does not, and the value that arrives is whatever the
register happened to hold.

The NaN is computed at RUNTIME from a global, not written as a literal: a folded NaN would
reach the backend as a constant, a folded compare emits no `ucomisd` and no `jp` at all, and
the test would pass while testing nothing.

⚠ **THE SOURCE IS OVERFLOW (`inf - inf`), NOT `0.0 / 0.0` (A1)** — and that is the corpus's own
resolution, not a workaround: `specs/primitive-comparable.md:169` and
`specs-shv2/primitive-hashable.md`'s `float.hash.nan` already build their NaN this way, each
saying why. Division by zero is now a language-level error (a constant zero divisor is E3103,
a possibly-zero one throws), so there is no route from a divide to a NaN at all. Overflow to
`inf` and `inf - inf` remain silent, which is exactly what this case needs — and the subject,
the `ucomisd`/`jp` compare-branch path, is untouched: only where the unordered value came from
has changed.
```maxon
var big = 1.0e308

function main() returns ExitCode
	var r = 7
	let inf = big * 10.0
	let nan = inf - inf
	if nan == nan 'check'
		r = 1
	end 'check'
	return r
end 'main'
```
```exitcode
7
```

<!-- test: ne-nan-then-edge-phi -->
The mirror: `nan != nan` is TRUE, IEEE's one predicate that is true on unordered, and here
the `jp` takes the THEN edge. So this is the same first-block jump as `eq-nan-else-edge-phi`
carrying a phi to the OTHER successor — a lowering that routed only the second block's jump
through the trampoline fails here with `7` instead of `1`.
```maxon
var big = 1.0e308

function main() returns ExitCode
	var r = 7
	let inf = big * 10.0
	let nan = inf - inf
	if nan != nan 'check'
		r = 1
	end 'check'
	return r
end 'main'
```
```exitcode
1
```

<!-- test: lt-nan-is-false -->
`nan < x` must be FALSE. This is the case the operand swap buys: a `jb` lowering reads CF,
which `ucomisd` SETS on unordered, so it would take the then-branch and return 1. Only an
`above`-family jump on swapped operands returns 7.
```maxon
var big = 1.0e308

function main() returns ExitCode
	var r = 7
	let inf = big * 10.0
	let nan = inf - inf
	let x = 5.0
	if nan < x 'check'
		r = 1
	end 'check'
	return r
end 'main'
```
```exitcode
7
```

<!-- test: le-nan-is-false -->
`nan <= x` must be FALSE, for the same reason with ZF also set.
```maxon
var big = 1.0e308

function main() returns ExitCode
	var r = 7
	let inf = big * 10.0
	let nan = inf - inf
	let x = 5.0
	if nan <= x 'check'
		r = 1
	end 'check'
	return r
end 'main'
```
```exitcode
7
```

<!-- test: gt-nan-is-false -->
`nan > x` must be FALSE — the case that needs NO parity test, because `ja` requires CF=0 and
unordered sets CF. It is here to pin that the lowering does NOT grow a `jp` it does not
need: if this ever starts emitting one, the `above` family's whole reason for existing has
been forgotten.
```maxon
var big = 1.0e308

function main() returns ExitCode
	var r = 7
	let inf = big * 10.0
	let nan = inf - inf
	let x = 5.0
	if nan > x 'check'
		r = 1
	end 'check'
	return r
end 'main'
```
```exitcode
7
```

<!-- test: float-cmp-materialized -->
A float compare whose result is a VALUE rather than a branch condition — it is stored into a
`var` the merge reads, so it cannot be fused into a `jcc` and must materialize through
`setcc`. The unordered case has to survive that path too: `nan == nan` materializes FALSE
and `nan != nan` materializes TRUE, which for `==` means the parity bit has to be folded
into the byte `sete` produced rather than into a jump.
```maxon
var big = 1.0e308

function main() returns ExitCode
	let inf = big * 10.0
	let nan = inf - inf
	let a = nan == nan
	let b = nan != nan
	var r = 0
	if a 'isEq'
		r = r + 1
	end 'isEq'
	if b 'isNe'
		r = r + 2
	end 'isNe'
	return r
end 'main'
```
```exitcode
2
```
