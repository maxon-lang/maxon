---
feature: wide-compare-immediates
status: stable
keywords: [comparison, immediate, encoding, codegen]
category: codegen
---

# Wide immediate operands of a comparison

## Documentation

`x == K`, `x < K` and their siblings for a CONSTANT `K` lower to a `cmpImm`, and the
shared constant folder hands the backends the **whole `Imm32` domain** — the same
target-neutral promise `wide-immediate-arithmetic` describes for `+` and `-`.

**AArch64's compare-immediate is narrower than that, and narrower than its own
add/sub-immediate.** `CMP Xn, #imm` is `SUBS XZR, Xn, #imm12{, LSL #12}`: a 12-bit field
with an optional left-shift by 12, exactly as ADD/SUB has — but a compare cannot use the
hi/lo PAIR that `x + 16777215` splits into, because the flags a compare exists to set
would then describe only the second half of the subtraction. So a comparison's immediate
must fit **ONE** field:

| `K` | arm64 encoding |
|---|---|
| `0` … `4095` | `cmp Xn, #K` |
| a multiple of 4096 up to `16773120` (`0xFFF000`) | `cmp Xn, #(K >> 12), lsl #12` |
| anything else non-negative | **no immediate form** — `movz`/`movk` into the scratch, then `cmp Xn, Xm` |
| `-1` … `-4095`, and the negative multiples of 4096 down to `-16773120` | `cmn Xn, #-K{, lsl #12}` |
| any other negative | **no immediate form** — the ladder and the register form |

The negative row is the one that pays. `CMN Xn, #m` is `ADDS XZR, Xn, #m`, and
`x − (−m)` IS `x + m` — the same 64-bit result, so the same NZCV, including the carry the
unsigned conditions read (subtracting `K` computes `x + ~K + 1`, and for `K = −m` that is
`x + m` with the identical carry out). A comparison against `−1` is therefore ONE
instruction; without the form it is a four-instruction `movz`/`movk` ladder — all four
halfwords of `0xFFFF_FFFF_FFFF_FFFF` are live — plus a register compare.

### Where the decision is made, and why it is not in the encoder

The selector asks whether an immediate form exists and materialises the constant itself
when it does not, exactly as it does for `+`/`-`, `*` and the logicals. That places the
`movz`/`movk` where **the allocator, the liveness model and the emitted IR all see it**,
rather than expanding it invisibly inside one op's encoding. The encoder keeps a panic for
an immediate with no form, as the assertion that the selector asked.

### These cases pin an ENCODING, not an answer

Every band below computed the right answer before the shifted and `cmn` forms existed —
the fallback ladder is correct, merely long. What the cases pin is that each band still
computes it once the shorter form is selected: a `cmn` emitted where a `cmp` was meant, or
a shifted field filled with the unshifted value, is a WRONG COMPARISON and therefore a
distinct exit code here. Both orderings and both equalities are exercised per band,
because a mis-selected form shows up in the condition codes before it shows up in
equality.

## Tests

<!-- test: cmp-imm12-boundary -->
The plain 12-bit field and the first constant past it. `4095` is the largest immediate
that encodes unshifted; `4096` is the first that needs the `lsl #12` form, and it is the
value that would compare against `0` if the shift bit were set without shifting the field.
```maxon
typealias Val = int(i64.min to i64.max)

function base(p Val) returns Val
	return p
end 'base'

function main() returns ExitCode
	let x = base(4096)
	if x == 4095 'eqLo'
		return 11
	end 'eqLo'
	if not (x > 4095) 'gtLo'
		return 12
	end 'gtLo'
	if not (x == 4096) 'eqHi'
		return 13
	end 'eqHi'
	if x > 4096 'gtHi'
		return 14
	end 'gtHi'
	if not (x < 4097) 'ltNext'
		return 15
	end 'ltNext'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: cmp-shifted-imm12-band -->
The top of the shifted field and the first constant with no single-field form at all.
`16773120` is `0xFFF000`; `16777216` is `0x1000000`, whose shifted half no longer fits the
12 bits — it is inside the band `+`/`-` still reach with their hi/lo PAIR, and outside
what a compare can use.
```maxon
typealias Val = int(i64.min to i64.max)

function base(p Val) returns Val
	return p
end 'base'

function main() returns ExitCode
	let x = base(16773120)
	if not (x == 16773120) 'eqTop'
		return 21
	end 'eqTop'
	if x > 16773120 'gtTop'
		return 22
	end 'gtTop'
	if not (x < 16777216) 'ltPair'
		return 23
	end 'ltPair'
	if x == 16777216 'eqPair'
		return 24
	end 'eqPair'
	let y = base(16777216)
	if not (y == 16777216) 'eqPairSelf'
		return 25
	end 'eqPairSelf'
	if not (y > 16773120) 'gtTopFromPair'
		return 26
	end 'gtTopFromPair'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: cmp-not-a-shifted-imm12 -->
Past the plain field but NOT a multiple of 4096, so neither shape fits and the constant is
materialised. `65535` is `0xFFFF` — one halfword, so one `movz`; `16777215` is `0xFFFFFF`,
two halfwords and the largest magnitude an add/sub PAIR still reaches, which a compare
does not.
```maxon
typealias Val = int(i64.min to i64.max)

function base(p Val) returns Val
	return p
end 'base'

function main() returns ExitCode
	let x = base(65535)
	if not (x == 65535) 'eqWord'
		return 31
	end 'eqWord'
	if x > 65535 'gtWord'
		return 32
	end 'gtWord'
	if not (x < 65536) 'ltNext'
		return 33
	end 'ltNext'
	let y = base(16777215)
	if not (y == 16777215) 'eqPairMax'
		return 34
	end 'eqPairMax'
	if not (y > 65535) 'gtWordFromPair'
		return 35
	end 'gtWordFromPair'
	if y == 16777214 'eqOffByOne'
		return 36
	end 'eqOffByOne'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: cmp-negative-immediates -->
The sign twin, and the band that pays most. `-1` and `-4095` fit `cmn`'s plain field;
`-4096` and `-16773120` fit its shifted one; `-65535` fits neither and is materialised;
`-2147483648` is `i32.min`, the widest the shared fold produces, and its magnitude is past
every field. The ordering checks are what would catch a `cmn` emitted with the wrong sign
— `x > -1` and `x < -1` cannot both be wrong the same way.
```maxon
typealias Val = int(i64.min to i64.max)

function base(p Val) returns Val
	return p
end 'base'

function main() returns ExitCode
	let x = base(-4096)
	if x == -1 'eqMinusOne'
		return 41
	end 'eqMinusOne'
	if not (x < -1) 'ltMinusOne'
		return 42
	end 'ltMinusOne'
	if not (x < -4095) 'ltPlainField'
		return 43
	end 'ltPlainField'
	if not (x == -4096) 'eqShifted'
		return 44
	end 'eqShifted'
	if not (x > -16773120) 'gtShiftedTop'
		return 45
	end 'gtShiftedTop'
	if not (x > -65535) 'gtNoForm'
		return 46
	end 'gtNoForm'
	if x == -65535 'eqNoForm'
		return 47
	end 'eqNoForm'
	if not (x > -2147483648) 'gtMin'
		return 48
	end 'gtMin'
	let y = base(-16773120)
	if not (y == -16773120) 'eqShiftedTop'
		return 49
	end 'eqShiftedTop'
	if not (y < -4096) 'ltShifted'
		return 50
	end 'ltShifted'
	return 0
end 'main'
```
```exitcode
0
```
