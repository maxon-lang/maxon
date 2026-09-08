---
feature: register-parameter-permutation
status: selfhosted
keywords: [register-allocator, calling-convention, argument-registers, parallel-copy, entry-parameters, permutation, miscompile]
category: register-allocator
milestone: M5.6
---

# Parameter / argument register permutation

## Documentation

The internal calling convention's six integer argument registers, in SLOT order, are
`[rcx, rdx, rax, r9, rsi, rdi]`. Two parallel copies bracket every call, and they are
protected by DIFFERENT mechanisms:

- **On ENTRY** the parameters are captured with `mov virtual(v_i), physical(argReg[i])` — a
  copy whose SOURCES are physical registers, emitted naively in slot order.
- **At a CALL** the arguments are set up with `mov physical(argReg[k]), virtual(arg_k)` — a
  copy whose DESTINATIONS are physical registers, again emitted naively in slot order.

**Neither runs through a parallel-copy sequencer.** There is no `xchg` here, no leaf ordering,
no cycle detection — just a straight run of moves. They are correct only because liveness
forbids the registers that would make a naive in-order emit wrong:

- The SETUP direction is protected by `applyForbidden`. Each `mov argReg[k], …` is a physical
  DEF, so every value live ACROSS it is forbidden `argReg[k]`. A value still needed by a later
  argument move therefore can never be sitting in an earlier argument register.
- The CAPTURE direction has no def to hang that on — there, the physical register is the
  SOURCE. So `forbidEntryParamCrossRegisters` forbids each parameter its **siblings'** incoming
  registers, which forces `v_i` either into its OWN `argReg[i]` (where the capture self-elides)
  or into a non-argument register (whose write clobbers no sibling's source).

**Crucially, `forbidEntryParamCrossRegisters` does NOT forbid a parameter its own incoming
register — nor should it**, since staying put is the case where the capture disappears entirely.
So a parameter may legitimately sit in, say, `RDX`, and it is then ONLY the setup-direction
forbid that stops a later `mov rdx, <some other arg>` from destroying it before its own argument
move reads it. The two mechanisms have to interlock exactly. This is the class ARCHITECTURE.md
records as having **shipped once as a silent miscompile** (the v1 bug
`project_call_arg_parallel_copy_fix`).

**A PERMUTATION is what makes the interlock load-bearing.** An identity mapping never needs it:
every parameter is already in the register its own argument move wants, every capture and every
setup self-elides, and a compiler with both mechanisms deleted would still emit correct code.
Only when slot order and value order disagree does anything have to be forbidden.

Every test below permutes six values through six registers — reversals, a 6-cycle, a
label-driven reordering, a recursion that permutes at every level — and encodes them
POSITIONALLY as decimal digits, so a single clobbered register changes exactly one digit of a
six-digit answer. All are self-verifying (`0` on an exact match, `99` otherwise), because these
answers are far larger than 255 and a raw exit code would wrap mod 256.

## Tests

<!-- test: reverse-six-params -->
A six-parameter function that immediately calls a six-parameter function with the arguments
FULLY REVERSED — a permutation through the argument registers on capture AND on setup. Slot `k`
receives parameter `5 − k`, so `RCX ← f`, `RDX ← e`, `RAX ← d`, `R9 ← c`, `RSI ← b`, `RDI ← a`.
Parameter `a` (incoming `RCX`) is live across ALL FIVE earlier setup moves, so it is forbidden
every argument register and must be captured out to a non-argument one; `f` (incoming `RDI`)
dies at the FIRST move and may keep its own register.
`reverse6(1,2,3,4,5,6)` → `digits6(6,5,4,3,2,1)` = `600000 + 50000 + 4000 + 300 + 20 + 1` = 654321.
```maxon
function digits6(p1 Integer, p2 Integer, p3 Integer, p4 Integer, p5 Integer, p6 Integer) returns Integer
	return p1 * 100000 + p2 * 10000 + p3 * 1000 + p4 * 100 + p5 * 10 + p6
end 'digits6'

function reverse6(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer) returns Integer
	return digits6(f, p2: e, p3: d, p4: c, p5: b, p6: a)
end 'reverse6'

function main() returns ExitCode
	let r = reverse6(1, b: 2, c: 3, d: 4, e: 5, f: 6)
	if r == 654321 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: rotate-six-params -->
A 6-CYCLE (`g(b, c, d, e, f, a)`) — the permutation with NO fixed point and no 2-cycle to
decompose into, so every argument register is both a source and a destination of the copy. Slot
`k` gets parameter `k + 1` (mod 6): `RCX ← b`, `RDX ← c`, `RAX ← d`, `R9 ← e`, `RSI ← f`,
`RDI ← a`. Each of `b`..`f` dies exactly one move before its own incoming register is
overwritten, which is the ONLY reason a naive in-order emit is legal at all.
`rotate6(1,2,3,4,5,6)` → `digits6(2,3,4,5,6,1)` = `200000 + 30000 + 4000 + 500 + 60 + 1` = 234561.
```maxon
function digits6(p1 Integer, p2 Integer, p3 Integer, p4 Integer, p5 Integer, p6 Integer) returns Integer
	return p1 * 100000 + p2 * 10000 + p3 * 1000 + p4 * 100 + p5 * 10 + p6
end 'digits6'

function rotate6(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer) returns Integer
	return digits6(b, p2: c, p3: d, p4: e, p5: f, p6: a)
end 'rotate6'

function main() returns ExitCode
	let r = rotate6(1, b: 2, c: 3, d: 4, e: 5, f: 6)
	if r == 234561 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: named-args-out-of-declaration-order -->
**THE SHARPEST PROBE IN THIS FILE.** The labelled arguments are WRITTEN in one order and BOUND
to the callee's parameters in another, so evaluation order and slot order disagree. The
resulting slot map is `RCX ← a`, `RDX ← d`, `RAX ← b`, `R9 ← f`, `RSI ← e`, `RDI ← c`.

Look at `b`. Its INCOMING register is `RDX`. `forbidEntryParamCrossRegisters` forbids `b` its
siblings' incoming registers — `{rcx, rax, r9, rsi, rdi}` — and DELIBERATELY leaves `RDX`
available, because `b` staying in its own register is the case where the capture self-elides.
But `RDX` is now argument slot 1, filled with `d` BEFORE `b` is read at slot 2. The ONLY thing
stopping `b` from being coloured into `RDX` and destroyed there is `applyForbidden` on the
physical DEF of `mov rdx, d`. Drop that one edge and `b` silently becomes `d`.
`shuffle6(1..6)` binds `p1=a=1, p2=d=4, p3=b=2, p4=f=6, p5=e=5, p6=c=3` →
`100000 + 40000 + 2000 + 600 + 50 + 3` = 142653. (The clobber above yields 144653 — one digit,
and a raw exit code would never have shown it.)
```maxon
function digits6(p1 Integer, p2 Integer, p3 Integer, p4 Integer, p5 Integer, p6 Integer) returns Integer
	return p1 * 100000 + p2 * 10000 + p3 * 1000 + p4 * 100 + p5 * 10 + p6
end 'digits6'

function shuffle6(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer) returns Integer
	return digits6(a, p3: b, p6: c, p2: d, p5: e, p4: f)
end 'shuffle6'

function main() returns ExitCode
	let r = shuffle6(1, b: 2, c: 3, d: 4, e: 5, f: 6)
	if r == 142653 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: order-sensitive-body-through-a-reversal -->
The callee's body is ORDER-SENSITIVE by construction: the coefficients `+1, −2, +3, −4, +5, −6`
are pairwise DISTINCT, so exchanging any two arguments changes the result by
`(c_i − c_j)(v_j − v_i) ≠ 0`. A symmetric sum (`a + b + c + …`) would absorb a register mixup
in silence; this cannot. It is reached through a full reversal, so BOTH parallel copies are
permutations, and the callee's `sub`/`imul` reuse-defs additionally compete for the same
argument registers.
`flip6(1, 20, 3, 30, 2, 40)` calls `mix6(40, 2, 30, 3, 20, 1)`:
`40 − 2·2 + 30·3 − 3·4 + 20·5 − 1·6` = `40 − 4 + 90 − 12 + 100 − 6` = 208.
```maxon
function mix6(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer) returns Integer
	return a - b * 2 + c * 3 - d * 4 + e * 5 - f * 6
end 'mix6'

function flip6(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer) returns Integer
	return mix6(f, b: e, c: d, d: c, e: b, f: a)
end 'flip6'

function main() returns ExitCode
	let r = flip6(1, b: 20, c: 3, d: 30, e: 2, f: 40)
	if r == 208 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: six-args-all-live-after-the-call -->
All SIX parameters are passed (reversed) to the call AND read again AFTER it. A value live
across a call is confined to the five CALLEE-SAVED registers — and there are SIX of them. Hall's
condition fails, and the splitter must BRACKET one: a `storeSlotReg` at its DEF (which, for a
parameter, is its ENTRY CAPTURE) and a `loadRegSlot` after the call. That is a spill whose def
is a capture rather than an ordinary op — a case `register-spill.md` never reaches, since its
`six-values-live-across-call` spills locals. The spilled parameter is still needed as an
ARGUMENT before the call, so its live range splits at the peak: register up to its argument move,
memory across the call, a fresh reload after.
`keepAll(1..6)`: `digits6(6,5,4,3,2,1) = 654321`, plus the originals re-encoded as `123456`, so
`654321 + 123456 = 777777`.
```maxon
function digits6(p1 Integer, p2 Integer, p3 Integer, p4 Integer, p5 Integer, p6 Integer) returns Integer
	return p1 * 100000 + p2 * 10000 + p3 * 1000 + p4 * 100 + p5 * 10 + p6
end 'digits6'

function keepAll(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer) returns Integer
	let r = digits6(f, p2: e, p3: d, p4: c, p5: b, p6: a)
	return r + a * 100000 + b * 10000 + c * 1000 + d * 100 + e * 10 + f
end 'keepAll'

function main() returns ExitCode
	let v = keepAll(1, b: 2, c: 3, d: 4, e: 5, f: 6)
	if v == 777777 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: reverse-six-params-across-an-idiv -->
THE COLLISION, in one function. An `idiv` runs BEFORE the reversed call and all six parameters
are live across it, so on top of the cross-parameter forbid every one of them is ALSO forbidden
`{RAX, RDX}`. That takes parameter `c` (incoming **RAX**) and parameter `b` (incoming **RDX**)
off their own argument registers entirely — their forbidden sets now cover all six argument
registers, and they must be captured out to non-argument ones. The reversed setup then WRITES
`RAX` and `RDX` as argument slots 2 and 1. The quotient is read after the call, so it is confined
to callee-saved.
`divPermute(1..6)`: `sum = 21`, `q = 21 / 2 = 10` (2·10 = 20); `digits6(6,5,4,3,2,1) = 654321`;
`654321 + 10 = 654331`.
```maxon
function digits6(p1 Integer, p2 Integer, p3 Integer, p4 Integer, p5 Integer, p6 Integer) returns Integer
	return p1 * 100000 + p2 * 10000 + p3 * 1000 + p4 * 100 + p5 * 10 + p6
end 'digits6'

function divPermute(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer) returns Integer
	let sum = a + b + c + d + e + f
	let q = sum / 2
	let r = digits6(f, p2: e, p3: d, p4: c, p5: b, p6: a)
	return r + q
end 'divPermute'

function main() returns ExitCode
	let v = divPermute(1, b: 2, c: 3, d: 4, e: 5, f: 6)
	if v == 654331 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: recursive-permuting-six-params -->
RECURSION through a six-parameter function that PERMUTES its arguments at every level: the first
five rotate left and the sixth is the depth counter. The same six-register parallel copy runs on
capture and on setup at every level, and the mapping is a 5-cycle (`p1←p2, p2←p3, p3←p4, p4←p5,
p5←p1`), so `p1` (incoming `RCX`) is live across four setup moves and must leave the argument
file entirely, while `p2`..`p5` each die exactly one move before their own register is overwritten.
`rot(1,2,3,4,5, n:3)` → `rot(2,3,4,5,1, n:2)` → `rot(3,4,5,1,2, n:1)` → `rot(4,5,1,2,3, n:0)` →
base: `4·10000 + 5·1000 + 1·100 + 2·10 + 3` = 45123.
```maxon
function rot(p1 Integer, p2 Integer, p3 Integer, p4 Integer, p5 Integer, n Integer) returns Integer
	if n <= 0 'base'
		return p1 * 10000 + p2 * 1000 + p3 * 100 + p4 * 10 + p5
	end 'base'
	return rot(p2, p2: p3, p3: p4, p4: p5, p5: p1, n: n - 1)
end 'rot'

function main() returns ExitCode
	let r = rot(1, p2: 2, p3: 3, p4: 4, p5: 5, n: 3)
	if r == 45123 'ok'
		return 0
	end 'ok'
	return 99
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
