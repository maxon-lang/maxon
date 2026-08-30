---
feature: short-circuit-lowering
status: stable
keywords: [and, or, short-circuit, boolean, lowering, blocks, phi]
category: operators
---

# Short-Circuit Lowering — Block Structure And Merge Phis

## Documentation

A bool `and`/`or` is **control flow**, not an operator: `Parser.emitShortCircuit` mints blocks
and a merge phi, and no later pass can retrofit that. This spec pins the SHAPE that lowering
produces, by checking VALUES a mis-placed block or a mis-wired phi would get wrong.

**It is not a test of elision**, and that distinction is why this file exists separately.
Whether the right operand is *skipped* is pinned by
[`short-circuit-evaluation.md`](./short-circuit-evaluation.md), which counts evaluations in a
top-level `var` — the sharpest possible oracle, and the one the language's own spec uses.

These cases ask a different question: given that the lowering branches, does it branch to the
right places? A short-circuit in a call ARGUMENT moves the block the call must be emitted
into; one in a loop CONDITION decides which block the exit phi's operands come from; `not`
over a merged value must pick the LOGICAL opcode, because the integer one turns `false` into
-1 which still reads as true. Every one of those is a wrong answer with a correct-looking
program, and none of them is about elision at all.

## Provenance — what this file replaced, and why the replacement is narrower

These nine cases were rescued from `short-circuit-elision.md` (**deleted at P1.0d.5b**), a
workaround that existed because shv2 had no globals. That file proved elision the only way a
compiler with no observable side effects can: it made the guarded operand **divide by zero**,
so a clean exit was the proof. Its own header said to retire it when globals landed, and
`OPEN.md` #4 said the same.

Globals landed, `short-circuit-evaluation.md`'s counting cases went green, and the five
divide-by-zero cases were deleted with the file — they tested elision, worse than the spec
that now tests elision. **These nine never used that oracle.** They check ordinary values,
they were never a workaround for anything, and deleting them along with the hack would have
been a silent coverage regression dressed up as a cleanup.

## Tests

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

<!-- test: loop-carried-var-across-a-short-circuit-condition -->
The same shape, but the loop actually RUNS and carries a `var` through its header phis while
the condition short-circuits every iteration. The exit's phi operands come from the block the
condition ended in, not the header — get that wrong and the post-loop value is the pre-loop
one.

```maxon
function positive(v Integer) returns bool
	return v > 0
end 'positive'

function underLimit(v Integer) returns bool
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
typealias Integer = int(i64.min to i64.max)
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
