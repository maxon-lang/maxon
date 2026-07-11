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
