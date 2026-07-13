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

## Deferred

Tests recorded for re-enablement at the milestone that unblocks them. They live
in this `## Deferred` section — NOT `## Tests` — so the spec-test parser (which
scans only `## Tests`, up to the next `## ` heading) never extracts them, and
they carry NO `<!-- test: … -->` marker. To re-enable: move the test up into
`## Tests` and prefix it with its `<!-- test: NAME -->` marker.

### float-comparison

Re-enable once its prerequisites land: float literals + float comparison (an XMM
`ucomisd` + an unsigned/parity-aware jcc).

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
