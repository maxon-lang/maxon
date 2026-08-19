---
feature: comparison-operators
status: selfhosted
keywords: [operators, comparison, equals, not-equals, greater, less]
category: operators
milestone: M4a
---

# Comparison Operators

## Documentation

Comparison operators compare two integer values and yield a boolean (`i1`):

- `==` equal to
- `!=` not equal to
- `<` less than
- `>` greater than
- `<=` less than or equal to
- `>=` greater than or equal to

At M4a the comparisons are integer-only and bind LOOSER than the arithmetic
operators (below additive), so `x + 1 == 5` groups as `(x + 1) == 5`. A comparison
in shv2 exists to feed an `if`: the Std→x64 lowering FUSES the comparison with the
branch it feeds — `cmp reg, reg` + a signed `jcc` (`==`→JE, `<`→JL, `>=`→JGE, …) —
rather than materializing a boolean. See `specs-shv2/if-statements.md`.

## Tests

The M4a slice of `specs/comparison-operators.md`: `==`, `!=`, `>`, and `<=`, each
inside an `if`. `float-comparison` is DEFERRED (floats) and recorded under
`## Deferred` below.

Each of those four takes its branch, so each asserts only the TRUE direction of one
operator — and a `jcc` that is wrong in a way that still lands on the same answer
would pass every one of them. `false-direction-and-boundary` is the companion that
closes it, and it is also the only test of `<` and `>=`.

<!-- test: equality -->
```maxon
function main() returns ExitCode
	let x = 42
	if x == 42 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: not-equal -->
```maxon
function main() returns ExitCode
	let x = 10
	if x != 20 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: greater-than -->
```maxon
function main() returns ExitCode
	if 5 > 3 'check'
		return 42
	end 'check'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: less-than-or-equal -->
```maxon
function main() returns ExitCode
	let a = 5
	let b = 10
	if a <= b 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: false-direction-and-boundary -->
Every test above takes its branch, and that is a hole: a comparison lowered to an
UNCONDITIONAL jump passes `not-equal`, `greater-than`, and `less-than-or-equal`
unchanged, and an off-by-one condition code — `>` emitted as `JGE`, `<=` as `JL` —
passes all four, because none of them compares a value against ITSELF. This test takes
the false direction of each operator and pins the boundary, `x` against `x`, where the
strict and non-strict forms disagree. It is also the only test of `<` and `>=`.

Each operator's outcome is a distinct BIT of the exit code, so a single mis-lowered
`jcc` flips exactly one bit and the exit code names the operator that moved. With
`x = 5`: `x == 6` is false (+0), `x != 5` is false (+0), `x > 5` is false (+0 — a `>`
lowered as `>=` would add 4), `x <= 5` is true (+8 — a `<=` lowered as `<` would drop
it), `x >= 5` is true (+16), and `x < 5` is false (+0 — a `<` lowered as `<=` would add
32). The result is `8 + 16 = 24`.
```maxon
function main() returns ExitCode
	let x = 5
	var r = 0
	if x == 6 'eqFalse'
		r = r + 1
	end 'eqFalse'
	if x != 5 'neFalse'
		r = r + 2
	end 'neFalse'
	if x > 5 'gtBoundary'
		r = r + 4
	end 'gtBoundary'
	if x <= 5 'leBoundary'
		r = r + 8
	end 'leBoundary'
	if x >= 5 'geBoundary'
		r = r + 16
	end 'geBoundary'
	if x < 5 'ltFalse'
		r = r + 32
	end 'ltFalse'
	return r
end 'main'
```
```exitcode
24
```

<!-- test: fused-compare-is-highest-value -->
A fused compare — one whose boolean is consumed only as the EFLAGS a `jcc` reads, never
out of a register — leaves NO trace among the target ops: it is neither an operand nor a
def there. So when it is the function's HIGHEST-numbered value, `scanFunctionValueCount`
(which counts target operands) sizes the value-class column one short of it, and recording
that boolean's class runs off the end — a backend panic on a valid program. This pins the
shape: both `if` branches return an already-bound value (`a`, `b`), so nothing is minted
after the `a < b` compare and the compare IS the top id. With `a = 7`, `b = 3`, `7 < 3` is
false, so it falls through to `return a` and exits 7. See `setValueClass` in
StdToX64Conversion — the column now GROWS to cover every defined id.
```maxon
function main() returns ExitCode
	let a = 7
	let b = 3
	if a < b 'lt'
		return b
	end 'lt'
	return a
end 'main'
```
```exitcode
7
```

<!-- test: compare-against-a-literal-keeps-the-operand-width -->
<!-- targets: x64-windows -->
⚠ **A WINDOWS-LANE READING SINCE BATCH27.** `return 4000000000` is E3005 on every other target —
`ExitCode` is `int(0 to 255)` there — so those lanes cannot express this program, which is what the
`targets:` restriction says. It cannot be re-pinned on wasm through any other type, and the reason it
cannot (plus the array-element route that looks like a substitute and measurably is not) is stated once,
in `exit-code-range.md`'s *"What the narrowing costs the other lanes"*.

⭐⭐ **A COMPARE AGAINST A LITERAL IS A DIFFERENT INSTRUCTION, AND IT MUST NOT BE A DIFFERENT
QUESTION (X5).** `foldConstOperands` rewrites `e > 100` into the immediate form, and the immediate
form used to carry no operand TYPE — so a backend that does not keep every value in a 64-bit register
had to re-derive the compare's width from the left operand. `ExitCode` is a **u32**
(`valueTagToStdType`), so on `wasm32-wasi` it lives in an `i32`, and the re-derived width made this a
32-bit SIGNED compare of a number whose top bit is set. MEASURED, before the fix: x64 printed `gt` and
wasm printed `le` — the same source, the same value, opposite answers.

The companion is the SAME comparison against a non-constant, which never folded and was right all
along: printing both is the assertion, because a fix that widened only one of the two forms leaves the
pair disagreeing, and a case with a single reading cannot see that. `4000000000` exceeds `i32.max` and
fits `u32.max` — exactly the band where a signed and an unsigned reading disagree.
```maxon
function big() returns ExitCode
	return 4000000000
end 'big'

function hundred() returns ExitCode
	return 100
end 'hundred'

function main() returns ExitCode
	let e = big()
	if e > 100 'literalForm'
		print("literal=gt\n")
	end 'literalForm' else 'literalNot'
		print("literal=le\n")
	end 'literalNot'

	if e > hundred() 'valueForm'
		print("value=gt\n")
	end 'valueForm' else 'valueNot'
		print("value=le\n")
	end 'valueNot'
	return 0
end 'main'
```
```stdout
literal=gt
value=gt
```
```exitcode
0
```

<!-- test: float-comparison -->
```maxon
function main() returns ExitCode
	let x = 3.5
	let y = 2.1
	if x > y 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```
