---
feature: two-address-regression
status: selfhosted
keywords: [register-allocator, reuse, two-address, lea, imul]
category: codegen
milestone: M5.2
---

# Two-address reuse regression

## Documentation

x64's `sub` and `imul` are genuinely two-address (`dest = dest <op> rhs`), and `neg`
is two-address unary. M5.1 handled that by pre-emitting a seed copy `mov result, lhs`
in ISel and hoping biased coloring would elide it. M5.2 deletes that seed: ISel emits
ONE reuse-def op, and the register allocator supplies the copy ONLY when the two-address
input actually outlives the op.

The structural consequence this spec locks in: a `-` or `*` over **loop-carried,
non-constant** values (a constant operand folds away to an immediate, so both operands
must be loop-carried to stay in registers) emits **no copy at all in the loop body** —
the reuse def coalesces into its input's register because the input dies at the op. If
this regresses (a seed `mov` or an unnecessary reuse copy reappears in a loop), the
fragment below changes visibly.

The complementary case — a two-address op whose input genuinely OUTLIVES it (`b = a -
b` with `a` loop-invariant) — DOES need one copy, because the input must be preserved.
That copy is correct: the program still computes the right answer, and the fragment
below pins the single `mov` so neither a lost copy (a wrong answer) nor an extra one
(worse code) goes unnoticed.

## Tests

<!-- test: mul-loop -->
`product = product * i` over two loop-carried values. The `imul` reuse def coalesces
into `product`'s register (`imulRegReg r8, r8, rcx`) — NO copy in the loop body.
```maxon
function main() returns ExitCode
	var product = 1
	var i = 5
	while i > 1 'loop'
		product = product * i
		i = i - 1
	end 'loop'
	return product
end 'main'
```
```exitcode
120
```

<!-- test: sub-loop -->
`acc = acc - i` over two loop-carried values. The `sub` reuse def coalesces into
`acc`'s register (`subRegReg` with dest == lhs) — NO copy in the loop body.
```maxon
function main() returns ExitCode
	var acc = 100
	var i = 1
	while i < 5 'loop'
		acc = acc - i
		i = i + 1
	end 'loop'
	return acc
end 'main'
```
```exitcode
90
```

<!-- test: reuse-copy-outlives -->
`b = a - b` with `a` loop-invariant (used every iteration, so it outlives the `sub`).
Here the allocator MUST materialize `mov b_new, a` before the `sub` — the one case that
legitimately needs a reuse copy. The loop runs `b = 20 - b` three times from `b = 5`:
15, 5, 15 — a missing or mis-targeted copy would compute on the wrong value and fail.
```maxon
function main() returns ExitCode
	var a = 20
	var b = 5
	var i = 0
	while i < 3 'loop'
		b = a - b
		i = i + 1
	end 'loop'
	return b
end 'main'
```
```exitcode
15
```
