---
feature: register-volatility-preference
status: experimental
keywords: [register-allocator, caller-saved, callee-saved, volatility, abi, arm64, coloring]
category: register-allocator
---

# The colorer's volatility preference is the TARGET's

## Documentation

Every allocatable register file splits in two at a call: the **caller-saved** (volatile)
half, which a call destroys, and the **callee-saved** half, which the callee must preserve
and which a function's prologue therefore has to save and restore for every one it uses.

The colorer prefers the volatile half (`preferredVolatilityMask`). That looks like a
cost bias — a value that never crosses a call should not make the prologue save anything —
and it is one, but **it is also what makes greedy colouring EXACT here**, and that is the
part this spec is about. `HallCondition.hallVerdictAt` says so in as many words:

> It answers FEASIBILITY, not the colorer's CHOICE. The colorer is a dominance-order
> greedy, exact for this constraint family **because it prefers caller-saved registers
> first** (so an unconstrained value never takes a callee-saved one while a caller-saved
> one is free) … A point this call declares feasible is one the colorer can colour.

A value live ACROSS a call is forbidden every caller-saved register, so the callee-saved
half is its **only** home. If unconstrained values are allowed to settle there while
volatile registers stand free, that home is gone by the time the value that needs it is
coloured — and the colorer dies on a program the feasibility test has already declared
colourable. It is not a spill decision the splitter can rescue: at the failing point the
values FIT.

### The two halves are per ISA, and the register NUMBERS are not shared

`RegNum` is one unified numbering across every target, and each ISA files its own
registers into it. x64 puts `rax`..`r15` at 0..15 and `xmm0`..`xmm15` at 16..31; arm64
puts `x0`..`x30` at 0..30 and `d0`..`d31` at 31..62. **A mask built for one ISA read
against the other is not merely inexact, it is scrambled**: x64's caller-saved
`xmm0`..`xmm5` are RegNums 16..21, which in the arm64 file are `x16`..`x21`.

So a colorer that asks x64's mask on arm64 believes:

- `x19`, `x20`, `x21` — genuinely **callee-saved** — are volatile, and hands them to
  values that will never cross a call;
- `x3`, `x4`, `x5`, `x12`..`x15` — genuinely **caller-saved** — are not, and leaves them
  unused until everything else is taken;
- and on the SIMD side, that there are **no** volatile registers at all (x64's mask stops
  at RegNum 31 and arm64's `d0` starts there), so the preference degrades to no preference:
  a float lands in `d8`..`d15`, the eight registers a float that DOES cross a call has as
  its only home.

Each case below is a program that **fits** its target's pool and compiles on
`x64-windows`, `x64-linux` and `wasm32-wasi`. Neither is gated: the shapes are ordinary
straight-line code, and pinning them on every lane is what stops the next ISA repeating
this one's mistake.

⚠ **THESE CASES FAIL BY NOT COMPILING, AND THAT IS THE ONLY WAY THEY COULD FAIL.** A
volatility preference is a preference and never a legality claim: `blocked` carries the
call's own clobber mask independently, every hint is tested against `blocked` FIRST, and
the fallback ignores the preference entirely — so a wrong preference cannot produce a wrong
ANSWER, and no exit code distinguishes a good allocation from a bad one. The one thing it
CAN do is take the greedy's exactness away, and that surfaces as `chooseRegister` panicking
with every register in the file blocked. That is why each case here stands right at the
boundary rather than merely "under pressure": a program with room to spare would pass under
either mask and pin nothing.

## Tests

<!-- test: unconstrained-ints-leave-the-callee-saved-half-free -->
Twelve integers that never cross a call, defined first, then ten that do.

On arm64 the pool is 26 GPRs — `x0`..`x15` caller-saved, `x19`..`x28` callee-saved. The
twelve `a` values are coloured first and take volatile registers; the ten `b` values are
live across `sink(s)`, so they are forbidden all sixteen volatile ones and the ten
callee-saved registers are their only home. Ten values, ten registers: the point fits
exactly, and Hall's condition says so.

With x64's mask standing in, only nine of arm64's volatile registers looked volatile
(`x0`,`x1`,`x2`,`x6`..`x11`); the next three preferred were `x19`,`x20`,`x21`, so the `a`
values ate three of the ten registers the `b` values had nowhere else to go for, while
seven genuinely volatile registers (`x3`,`x4`,`x5`,`x12`..`x15`) were offered to nobody at
all. The colorer ran out. Its panic printed `HELD` as exactly the whole pool minus those
seven — the shape of the wrong mask, read straight off the diagnostic.

`n = sink(3) = 4`, so `a0`..`a11` are `5`..`16` and `s = 126`; `c = sink(126) = 127`; the
ten `b` values are `a0*3 = 15` through `a9*21 = 294`, summing to `1305`. Total `1432`.
```maxon
typealias Integer = int(i64.min to i64.max)

function sink(x Integer) returns Integer
	return x + 1
end 'sink'

function main() returns ExitCode
	let n = sink(3)
	let a0 = n + 1
	let a1 = n + 2
	let a2 = n + 3
	let a3 = n + 4
	let a4 = n + 5
	let a5 = n + 6
	let a6 = n + 7
	let a7 = n + 8
	let a8 = n + 9
	let a9 = n + 10
	let a10 = n + 11
	let a11 = n + 12
	let b0 = a0 * 3
	let b1 = a1 * 5
	let b2 = a2 * 7
	let b3 = a3 * 9
	let b4 = a4 * 11
	let b5 = a5 * 13
	let b6 = a6 * 15
	let b7 = a7 * 17
	let b8 = a8 * 19
	let b9 = a9 * 21
	let s = a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11
	let c = sink(s)
	let total = b0 + b1 + b2 + b3 + b4 + b5 + b6 + b7 + b8 + b9 + c
	return 0 if total == 1432 else 99
end 'main'
```
```exitcode
0
```

<!-- test: unconstrained-floats-leave-the-callee-saved-half-free -->
The SIMD twin, and it fails for the opposite reason: not a scrambled overlap but an EMPTY
one.

Ten floats that never cross a call, then eight that do. arm64's SIMD file is 32 registers
— `d0`..`d7` and `d16`..`d31` caller-saved, `d8`..`d15` callee-saved — so the eight `b`
values, live across `fsink(s)`, have exactly the eight `d8`..`d15` to live in.

x64's mask covers no arm64 SIMD register at all, so `classPool and callerSaved` came out
`0`, `anyVolatileFree` was false at every float, and `preferredVolatilityMask` returned the
whole class pool. The ten `a` values then simply took the lowest ten registers —
`d0`..`d9` — eating two of the eight the `b` values needed, and the colorer ran out. With
the mask arm64's, the `a` values take `d0`..`d7` and then `d16`,`d17`, and `d8`..`d15` are
still free when the values that can live nowhere else arrive.

⚠ The counts are deliberate: **twelve** non-crossing floats and four crossing ones panics
on arm64 too, but nineteen live floats also passes x64's own limit, and this case is about
the volatility preference rather than about pressure. Ten and eight is the widest shape
that is comfortably inside every other target's pool.

`n = fsink(3.0) = 4.0`, so `a0`..`a9` are `5.0`..`14.0` and `s = 95.0`; `c = 96.0`; the
eight `b` values are `10.0`..`24.0`, summing to `136.0`. Total `232.0`.
```maxon
function fsink(x Real) returns Real
	return x + 1.0
end 'fsink'

function main() returns ExitCode
	let n = fsink(3.0)
	let a0 = n + 1.0
	let a1 = n + 2.0
	let a2 = n + 3.0
	let a3 = n + 4.0
	let a4 = n + 5.0
	let a5 = n + 6.0
	let a6 = n + 7.0
	let a7 = n + 8.0
	let a8 = n + 9.0
	let a9 = n + 10.0
	let b0 = a0 * 2.0
	let b1 = a1 * 2.0
	let b2 = a2 * 2.0
	let b3 = a3 * 2.0
	let b4 = a4 * 2.0
	let b5 = a5 * 2.0
	let b6 = a6 * 2.0
	let b7 = a7 * 2.0
	let s = a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9
	let c = fsink(s)
	let total = b0 + b1 + b2 + b3 + b4 + b5 + b6 + b7 + c
	return 0 if total == 232.0 else 99
end 'main'
typealias Real = float(f64.min to f64.max)
```
```exitcode
0
```
