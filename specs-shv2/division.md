---
feature: division
status: selfhosted
keywords: [arithmetic, division, modulo, idiv, register-allocator, fixed-register]
category: operators
milestone: M5.4
---

# Integer division and modulo

## Documentation

Maxon's `/` (division) and `mod` (modulo) are signed integer operations that
truncate toward zero. They bind at the MULTIPLICATIVE precedence level — the same
as `*` — so `10 + 20 / 4` groups as `10 + (20 / 4)` = 15, and `2 * 6 mod 4` (all
left-associative at one level) groups as `((2 * 6) mod 4)` = 0.

They are the first operations to need a HARD fixed-physical-register constraint.
x64's `idiv r/m64` divides the 128-bit `RDX:RAX` by its operand, leaving the
QUOTIENT in `RAX` and the REMAINDER in `RDX`. So `a / b` lowers to:

```
mov rax, a      ; dividend into RAX's low half
cqo             ; sign-extend RAX into RDX:RAX
idiv b          ; RDX:RAX / b  ->  quotient in RAX, remainder in RDX
mov result, rax ; (mov result, rdx for `mod`)
```

`RAX` and `RDX` are pinned by the instruction, so the register allocator must keep
the divisor — and every value live across the `idiv` — OUT of `RAX`/`RDX`. It does
this from `idivReg`'s implicit-register masks (`implicitDefs = {RAX, RDX}`): the
liveness pass forbids those registers for anything crossing the op, and forbids
them for the divisor operand directly (which dies at the `idiv`, so the live-across
sweep alone would miss it). The AllocChecker then verifies the whole sequence — the
dividend reaches `RAX`, the divisor sits in a non-`RAX`/`RDX` register at the
`idiv`, and the result is read back out — on every function of every compile.

Divide-by-zero and `INT_MIN / -1` overflow raise a hardware `#DE`, delivered by the
runtime fault handler (a later deliverable), so there is no compiler-inserted guard.

## Tests

<!-- test: div-simple -->
`20 / 4` = 5. The dividend `20` materializes into `RAX`, the divisor `4` into a
non-`RAX`/`RDX` register (`rcx`), and the quotient is read from `RAX`.
```maxon
function main() returns ExitCode
	return 20 / 4
end 'main'
```
```exitcode
5
```

<!-- test: mod-simple -->
`17 mod 5` = 2. Same `idiv`, but the REMAINDER is read from `RDX` instead of the
quotient from `RAX`.
```maxon
function main() returns ExitCode
	return 17 mod 5
end 'main'
```
```exitcode
2
```

<!-- test: div-truncates -->
`100 / 7` = 14 — integer division truncates toward zero (14.28… → 14).
```maxon
function main() returns ExitCode
	return 100 / 7
end 'main'
```
```exitcode
14
```

<!-- test: mod-remainder -->
`100 mod 7` = 2 (100 = 14·7 + 2).
```maxon
function main() returns ExitCode
	return 100 mod 7
end 'main'
```
```exitcode
2
```

<!-- test: div-precedence -->
`/` binds tighter than `+`: `10 + 20 / 4` is `10 + (20 / 4)` = 10 + 5 = 15, not
`(10 + 20) / 4` = 7.
```maxon
function main() returns ExitCode
	return 10 + 20 / 4
end 'main'
```
```exitcode
15
```

<!-- test: mod-precedence -->
`mod` sits at the multiplicative level alongside `*`, left-associative: `2 * 6 mod 4`
is `((2 * 6) mod 4)` = `12 mod 4` = 0, not `2 * (6 mod 4)` = 4.
```maxon
function main() returns ExitCode
	return 2 * 6 mod 4
end 'main'
```
```exitcode
0
```

<!-- test: div-mod-variables -->
`/` and `mod` over VARIABLES, both dividing the same `a` by the same `b`. `a` and
`b` are loop-free but each live across BOTH `idiv`s, so the allocator keeps them —
and the first quotient, which must survive the second `idiv`'s `RAX`/`RDX` clobber —
in non-`RAX`/`RDX` registers. `q = 100 / 7 = 14`, `r = 100 mod 7 = 2`, `q + r = 16`.
```maxon
function main() returns ExitCode
	var a = 100
	var b = 7
	let q = a / b
	let r = a mod b
	return q + r
end 'main'
```
```exitcode
16
```

<!-- test: div-in-loop -->
Division inside a loop, where the divisor is the loop-carried counter `i`. `i` is
live across every `idiv` AND updated each iteration, so it MUST be colored to a
register that is neither `RAX` (the dividend) nor `RDX` (clobbered by `cqo`/`idiv`)
— the fixed-register constraint under real pressure. Sum of `100 / i` for
`i = 1..6`: 100 + 50 + 33 + 25 + 20 + 16 = 244.
```maxon
function main() returns ExitCode
	var sum = 0
	var i = 1
	while i <= 6 'loop'
		sum = sum + 100 / i
		i = i + 1
	end 'loop'
	return sum
end 'main'
```
```exitcode
244
```
