---
feature: short-circuit-elision
status: stable
keywords: [and, or, short-circuit, boolean, logical, operators, lazy]
category: operators
---

# Short-Circuit Elision — Provable Without Globals

## Documentation

**This spec exists because shv2 cannot yet run the one that should cover this.**

The language's short-circuit contract is defined by
[`specs/short-circuit-evaluation.md`](../specs/short-circuit-evaluation.md), which is ported
into this suite verbatim — and **every one of its twelve cases is shelved**, nine of them for
the same reason: they count side effects in a **top-level `var`**, and shv2 has no globals (no
writable data section, no global load/store ops, no cross-function binding scope). Two more
need the `as` cast; one needs structs.

That would leave `and`/`or` shipping with **no evidence that the right operand is skipped at
all** — and an eagerly-evaluated `and` computes the *same answer* on every input, so no
value-checking test can catch it. It is a purely observational property, and without globals
shv2 has exactly one observable side effect: **an integer divide by zero faults the process**
(`STATUS_INTEGER_DIVIDE_BY_ZERO`, exit `0xC0000094`).

So the oracle here is a `boom()` whose body divides by a runtime zero. A guarded
`… and boom()` that **exits cleanly** is a proof the operand was never evaluated; a
compiler that evaluated it eagerly would take the fault and produce a wildly different
exit code. The counterpart cases pin the other direction — that the right operand IS
evaluated when the left does not determine the result — by checking the value.

**Retire this file when globals land**, and enable `short-circuit-evaluation.md` in its
place: that one tests the same property against a *count* of evaluations, which is strictly
sharper. Until then, this is the only thing standing between the short-circuit lowering and
a silent regression.

## Tests

<!-- test: and-skips-rhs-when-lhs-false -->
`false and boom()` must not evaluate `boom()`. A clean exit 0 is the proof: reaching
`boom()` divides by zero and faults the process.

```maxon
function no() returns bool
	return false
end 'no'

function boom() returns bool
	let zero = 0
	let q = 1 / zero
	return q == 0
end 'boom'

function main() returns ExitCode
	if no() and boom() 'never'
		return 1
	end 'never'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: or-skips-rhs-when-lhs-true -->
`true or boom()` must not evaluate `boom()`.

```maxon
function yes() returns bool
	return true
end 'yes'

function boom() returns bool
	let zero = 0
	let q = 1 / zero
	return q == 0
end 'boom'

function main() returns ExitCode
	if yes() or boom() 'taken'
		return 7
	end 'taken'
	return 9
end 'main'
```
```exitcode
7
```

<!-- test: and-chain-skips-every-later-operand -->
Short-circuit composes: in `a and b and c`, a false `a` skips BOTH `b` and `c`. Two
separate faulting operands, neither of which may run.

```maxon
function no() returns bool
	return false
end 'no'

function boom() returns bool
	let zero = 0
	let q = 1 / zero
	return q == 0
end 'boom'

function boomAgain() returns bool
	let zero = 0
	let q = 2 / zero
	return q == 0
end 'boomAgain'

function main() returns ExitCode
	if no() and boom() and boomAgain() 'never'
		return 1
	end 'never'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: or-chain-skips-every-later-operand -->
In `a or b or c`, a true `a` skips both `b` and `c`.

```maxon
function yes() returns bool
	return true
end 'yes'

function boom() returns bool
	let zero = 0
	let q = 1 / zero
	return q == 0
end 'boom'

function boomAgain() returns bool
	let zero = 0
	let q = 2 / zero
	return q == 0
end 'boomAgain'

function main() returns ExitCode
	if yes() or boom() or boomAgain() 'taken'
		return 5
	end 'taken'
	return 9
end 'main'
```
```exitcode
5
```

<!-- test: rhs-is-evaluated-when-lhs-does-not-decide -->
The other direction, and the one an over-eager elision would break: `and` MUST evaluate its
right operand when the left is true, and `or` MUST when the left is false. All four
truth-table entries of each operator, folded into one exit code.

```maxon
function t() returns bool
	return true
end 't'

function f() returns bool
	return false
end 'f'

function main() returns ExitCode
	var code = 0
	if t() and f() 'a'
		code = code + 1
	end 'a'
	if t() and t() 'b'
		code = code + 2
	end 'b'
	if f() and t() 'c'
		code = code + 4
	end 'c'
	if f() or f() 'd'
		code = code + 8
	end 'd'
	if f() or t() 'e'
		code = code + 16
	end 'e'
	if t() or f() 'g'
		code = code + 32
	end 'g'
	return code
end 'main'
```
```exitcode
50
```

<!-- test: comparison-operands -->
The operands of a short-circuit may be COMPARISONS, and this is the case that forces
boolean materialization: the right-hand compare's result flows into the merge phi, so it
cannot stay in EFLAGS — it must be written to a register (`setcc`).

```maxon
function main() returns ExitCode
	let x = 4
	let y = 0
	var code = 0
	if x > 0 and y > 0 'both'
		code = code + 1
	end 'both'
	if x > 0 or y > 0 'either'
		code = code + 2
	end 'either'
	if x > 0 and y == 0 'mixed'
		code = code + 4
	end 'mixed'
	return code
end 'main'
```
```exitcode
6
```

<!-- test: result-bound-to-a-let -->
A short-circuit is an EXPRESSION, not a statement: its merged value can be bound and read
later, in a block the merge does not terminate.

```maxon
function t() returns bool
	return true
end 't'

function f() returns bool
	return false
end 'f'

function main() returns ExitCode
	let a = t() and t()
	let b = t() and f()
	let c = f() or t()
	var code = 0
	if a 'a'
		code = code + 1
	end 'a'
	if b 'b'
		code = code + 2
	end 'b'
	if c 'c'
		code = code + 4
	end 'c'
	return code
end 'main'
```
```exitcode
5
```

<!-- test: not-over-a-short-circuit -->
`not` applied to a short-circuit's merged value. The `not` must be the LOGICAL one (flip
bit 0) — the integer one would turn `false` into -1, which still reads as TRUE in a
condition, so a wrong opcode here is invisible to the value and fatal to the branch.

```maxon
function t() returns bool
	return true
end 't'

function f() returns bool
	return false
end 'f'

function main() returns ExitCode
	var code = 0
	if not (f() or f()) 'a'
		code = code + 1
	end 'a'
	if not (t() and t()) 'b'
		code = code + 2
	end 'b'
	if not (t() and f()) 'c'
		code = code + 4
	end 'c'
	return code
end 'main'
```
```exitcode
5
```

<!-- test: in-a-call-argument -->
A short-circuit inside a call ARGUMENT moves the block the call itself must be emitted
into. Emitting the call where it started would strand it on a path its own arguments never
reach.

```maxon
typealias Tag = int(0 to 100)

function t() returns bool
	return true
end 't'

function f() returns bool
	return false
end 'f'

function consume(flag bool) returns Tag
	if flag 'isTrue'
		return 1
	end 'isTrue'
	return 0
end 'consume'

function main() returns ExitCode
	let a = consume(t() or f())
	let b = consume(t() and f())
	let c = consume(f() or t())
	return a * 4 + b * 2 + c
end 'main'
```
```exitcode
5
```

<!-- test: in-a-return-value -->
`return a and b` — the `ret` terminator belongs on the block the expression ENDED in (the
merge), not the one it started in. Put it on the wrong block and the merge is left
unterminated and the returned value is unreachable from the `ret`.

```maxon
function t() returns bool
	return true
end 't'

function f() returns bool
	return false
end 'f'

function bothTrue() returns bool
	return t() and t()
end 'bothTrue'

function eitherTrue() returns bool
	return f() or t()
end 'eitherTrue'

function main() returns ExitCode
	var code = 0
	if bothTrue() 'a'
		code = code + 1
	end 'a'
	if eitherTrue() 'b'
		code = code + 2
	end 'b'
	return code
end 'main'
```
```exitcode
3
```

<!-- test: in-a-while-condition -->
A short-circuit in a loop CONDITION spreads the condition's evaluation over blocks between
the loop header and its two-way branch. The header stays the `continue`/back-edge target;
the exit's incoming edge moves to the block the condition ends in. The loop body must never
run, and `boom()` must never be evaluated.

```maxon
function no() returns bool
	return false
end 'no'

function boom() returns bool
	let zero = 0
	let q = 1 / zero
	return q == 0
end 'boom'

function main() returns ExitCode
	var n = 0
	while no() and boom() 'loop'
		n = n + 1
	end 'loop'
	return n
end 'main'
```
```exitcode
0
```

<!-- test: loop-carried-var-across-a-short-circuit-condition -->
The same shape, but the loop actually RUNS and carries a `var` through its header phis while
the condition short-circuits every iteration. The exit's phi operands come from the block the
condition ended in, not the header — get that wrong and the post-loop value is the pre-loop
one.

```maxon
function positive(v int) returns bool
	return v > 0
end 'positive'

function underLimit(v int) returns bool
	return v < 4
end 'underLimit'

function main() returns ExitCode
	var n = 1
	var steps = 0
	while positive(n) and underLimit(n) 'loop'
		n = n + 1
		steps = steps + 1
		if steps > 10 'runaway'
			break
		end 'runaway'
	end 'loop'
	return n * 10 + steps
end 'main'
```
```exitcode
43
```

<!-- test: nested-in-the-right-operand -->
A short-circuit nested inside another's RIGHT operand: the inner one's blocks slot between
the outer's rhs and merge, and the outer's merge edge must come from the INNER merge (the
block control actually leaves the rhs in), not from the rhs block it started in.

```maxon
function t() returns bool
	return true
end 't'

function f() returns bool
	return false
end 'f'

function main() returns ExitCode
	var code = 0
	if f() or (t() and t()) 'a'
		code = code + 1
	end 'a'
	if t() and (f() or t()) 'b'
		code = code + 2
	end 'b'
	if f() or (t() and f()) 'c'
		code = code + 4
	end 'c'
	return code
end 'main'
```
```exitcode
3
```

<!-- test: integer-and-is-bitwise-not-logical -->
`and` on INTEGERS is the bit operation, and this is the case that tells the two readings
apart: `12 and 3` is `1100 & 0011` = **0**, where a logical reading of two non-zero
(truthy) operands would be `true`. It also proves the integer path evaluates both operands —
there is no bit pattern that makes the other redundant.

```maxon
function main() returns ExitCode
	let r = 12 and 3
	if r == 0 'bitwise'
		return 0
	end 'bitwise'
	return 1
end 'main'
```
```exitcode
0
```
