---
feature: stack-arguments
status: stable
keywords: [function, parameters, calling-convention, stack, abi]
category: functions
---

# Stack Arguments

## Documentation

A calling convention passes the first few arguments in registers and the rest on the stack. shv2's
custom x64 ABI has **six** integer argument registers and **six** float ones, counted independently;
AAPCS64 has eight of each. An argument past its file's registers is written by the caller into an
outgoing stack slot and read back by the callee out of the caller's frame.

The rule that decides where an argument goes is stated once, in `abiFileSlotIndex` /
`abiArgIsOnStack` / `abiStackSlotsBefore`, and BOTH ends of a call consult it. That matters more than
it looks: a caller and a callee that disagree about one argument do not fail to compile, they compute
a different answer.

Two properties are specific to the stack half and are what these tests pin:

**The two register files share ONE stack area.** Registers are two independent counters — a float at
source position 1 following an integer is float-slot 0 — but there is only one outgoing region and
one 8-byte stride, so an overflowing float takes the next slot after an overflowing integer.

**A stack argument's placement cannot destroy an argument register.** Materializing a stack argument's
value takes a register the allocator picks, and an argument register that an earlier move already loaded
is not a *value* — nothing marks it live from that move to the call — so nothing about SSA stops a store
from silently overwriting one.

⭐⭐ **TWO INDEPENDENT CURES ANSWER THAT, AND EACH ONE ALONE IS SUFFICIENT — MEASURED, 2026-08-06
(BATCH29/X3), BY REMOVING THEM ONE AT A TIME AND THEN TOGETHER.** They are
`emitArgMovesByFloatMask`'s two-phase order (every stack store emitted before every register move, so no
argument register exists yet while the stores run) and `sweepEstablishedRegisters`'s `establishedAtDef`
forbid (a value defined while an argument register is pending may not be coloured onto it).

- **Two-phase order removed, forbid intact** — source-order emission, the pre-fix shape. The emitted code
  CHANGES (`movRegImm32 rax, 0` becomes `rcx, 0` in both `x64-stack-arg-disp32` cases) and every answer in
  this file and that one stays RIGHT. The forbid caught it.
- **Forbid removed, two-phase order intact** — the emitted code is BYTE-IDENTICAL, in both files. With the
  stores first the forbid never had anything to forbid.
- **BOTH removed** — three cases go red at once, with the exact historical symptom the routine's header
  records: `x64-stack-arg-disp32/twenty-second-param-at-rbp-128` returns **400 instead of 200**,
  `params-straddling-rbp-128-boundary` 210 instead of 150, and `every-argument-of-a-wide-call-is-distinct`
  below 272 instead of 253.

⇒ **The property IS pinned, by those three cases**, and no single-mechanism sabotage can show it — which is
why a case has to be seen red against BOTH cures removed before it can be believed to pin anything here.

⛔ **`a-stack-argument-store-does-not-clobber-an-argument-register` WAS DELETED IN THAT SAME MEASUREMENT
(BATCH29/X3), AND ITS SHELVING ROW'S TWO CLAIMS WERE BOTH WRONG.** The row said the property was *"now
pinned by nothing"* (it is pinned by the three cases above) and that *"there is no legal Maxon program with
this property"*, because seven of its eight parameters had to go unread and an unread parameter is `E3012`.
The second claim mistakes which side of the call the bug is on: the clobber is emitted by the CALLER, and
what the callee does with its parameters cannot move an instruction in it — so a version whose callee reads
all eight behind a guard and still returns only the third is legal, compiles, and was written and run. It
was then deleted anyway, for the reason the row never reached: **eight arguments do not create enough
register pressure for the clobber to be observable at all.** That case stayed GREEN with both cures
removed, in the same run where the three above went red. Restoring it at 22 arguments would make it red and
would also make it a second spelling of `every-argument-of-a-wide-call-is-distinct`.

A managed argument (a `String`, an `Array`) is passed by pointer like any other 8-byte value, so a
stack slot changes nothing about who owns it: the callee consumes it and drops it exactly as it would
one arriving in rcx. The tests that pass managed values through stack slots are therefore also leak
tests — the suite fails a program that ends with a live allocation.

**Targets.** These cases are calling-convention BEHAVIOUR rather than encoding detail, so they run on
EVERY target, unmarked — nothing here is x64-specific, and the answers below are the convention's, not
one ABI's.

What DOES differ per target is where the boundary falls, and that is the point rather than an
inconvenience: the capacity the shared rule is asked against is **six** per file on x64
(`x64RegisterArgCapacity`) and **eight** on AAPCS64 (`arm64RegisterArgCapacity`, x0–x7 / d0–d7), so the
seven- and eight-argument cases overflow on x64 and still fit in registers on arm64, while the
twenty-two-argument one overflows everywhere. `wasm32-wasi`'s parameters are plain locals with no
register file at all, so it covers the front-end half — the ABI slot count, the diagnostics — and
nothing about slots. Each target's golden pins where ITS boundary landed, which is exactly the
cross-target agreement worth pinning: the same source, the same answer, three different placements.

## Tests

<!-- test: seven-scalar-parameters -->
The seventh argument is the first one that does not fit the register file.
```maxon
typealias Integer = int(i64.min to i64.max)

function sum7(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer, g Integer) returns Integer
	return a + b + c + d + e + f + g
end 'sum7'

function main() returns ExitCode
	return sum7(1, b: 2, c: 4, d: 8, e: 16, f: 32, g: 64)
end 'main'
```
```exitcode
127
```

<!-- test: eight-scalar-parameters -->
Two stack slots, so the second one's displacement is exercised as well as the first's.
```maxon
typealias Integer = int(i64.min to i64.max)

function sum8(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer, g Integer, h Integer) returns Integer
	return a + b + c + d + e + f + g + h
end 'sum8'

function main() returns ExitCode
	return sum8(1, b: 2, c: 4, d: 8, e: 16, f: 32, g: 64, h: 128)
end 'main'
```
```exitcode
255
```

<!-- test: a-function-typed-parameter-costs-two-argument-slots -->
A `function`-typed parameter carries a hidden environment argument under the uniform closure ABI, so
six written parameters can already need seven slots. The count the ABI uses is the one that decides
which arguments go on the stack.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntFn = function(x Integer) returns Integer

function twice(x Integer) returns Integer
	return x * 2
end 'twice'

function apply(fn IntFn, a Integer, b Integer, c Integer, d Integer, e Integer) returns Integer
	return fn(a) + b + c + d + e
end 'apply'

function main() returns ExitCode
	return apply(twice, a: 10, b: 1, c: 2, d: 4, e: 8)
end 'main'
```
```exitcode
35
```

<!-- test: seven-float-parameters -->
The float file overflows on its own counter: seven floats need one stack slot even though no integer
argument exists. Both reference compilers refuse this case outright.
```maxon
function sumf7(a Real, b Real, c Real, d Real, e Real, f Real, g Real) returns Real
	return a + b + c + d + e + f + g
end 'sumf7'

function main() returns ExitCode
	let total = sumf7(1.0, b: 2.0, c: 4.0, d: 8.0, e: 16.0, f: 32.0, g: 64.0)
	if total == 127.0 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
typealias Real = float(f64.min to f64.max)
```
```exitcode
0
```

<!-- test: int-and-float-overflow-share-one-stack-area -->
Seven integers and seven floats: each file overflows once, and the two overflowing arguments must
land in DIFFERENT slots of the single merged stack area. If either file indexed the area by its own
counter, both would claim slot 0 and one would read the other's bits.
```maxon
typealias Integer = int(i64.min to i64.max)

function mix(i1 Integer, i2 Integer, i3 Integer, i4 Integer, i5 Integer, i6 Integer, i7 Integer, f1 Real, f2 Real, f3 Real, f4 Real, f5 Real, f6 Real, f7 Real) returns Integer
	if f1 + f2 + f3 + f4 + f5 + f6 + f7 == 127.0 'floatsOk'
		return i1 + i2 + i3 + i4 + i5 + i6 + i7
	end 'floatsOk'
	return 0
end 'mix'

function main() returns ExitCode
	return mix(1, i2: 2, i3: 4, i4: 8, i5: 16, i6: 32, i7: 64, f1: 1.0, f2: 2.0, f3: 4.0, f4: 8.0, f5: 16.0, f6: 32.0, f7: 64.0)
end 'main'
typealias Real = float(f64.min to f64.max)
```
```exitcode
127
```

<!-- test: a-stack-argument-survives-an-intervening-call -->
The seventh parameter is read AFTER a call, so its value must outlive a callee that clobbers every
caller-saved register — it is loaded once at entry and preserved like any other value.
```maxon
typealias Integer = int(i64.min to i64.max)

function noise(x Integer) returns Integer
	return x + 1
end 'noise'

function tail(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer, g Integer) returns Integer
	let churn = noise(noise(noise(a + b + c + d + e + f)))
	return g + churn
end 'tail'

function main() returns ExitCode
	return tail(1, b: 1, c: 1, d: 1, e: 1, f: 1, g: 60)
end 'main'
```
```exitcode
69
```

<!-- test: recursion-through-stack-arguments -->
A recursive callee is its own caller, so the outgoing region it writes and the incoming region it
reads are two different frames' worth of the same layout.
```maxon
typealias Integer = int(i64.min to i64.max)

function countdown(n Integer, b Integer, c Integer, d Integer, e Integer, f Integer, acc Integer) returns Integer
	if n == 0 'done'
		return acc
	end 'done'
	return countdown(n - 1, b: b, c: c, d: d, e: e, f: f, acc: acc + n)
end 'countdown'

function main() returns ExitCode
	return countdown(10, b: 0, c: 0, d: 0, e: 0, f: 0, acc: 0)
end 'main'
```
```exitcode
55
```

<!-- test: managed-string-arguments-in-stack-slots -->
Eight `String`s: two of them travel in stack slots. A managed value is an 8-byte pointer wherever it
rides, so the callee consumes and drops all eight the same way — the run also fails on a leak. The
byte lengths are 46 + 2 + 3 + 4 + 5 + 6 + 36 + 25 = 127, so a stack slot read from the wrong place
changes the answer rather than merely reordering it.
```maxon
typealias ByteTotal = int(0 to 100000)

function joinLengths(a String, b String, c String, d String, e String, f String, g String, h String) returns ByteTotal
	return a.byteLength() + b.byteLength() + c.byteLength() + d.byteLength() + e.byteLength() + f.byteLength() + g.byteLength() + h.byteLength()
end 'joinLengths'

function main() returns ExitCode
	return joinLengths("a string long enough to be heap allocated, one", b: "bb", c: "ccc", d: "dddd", e: "eeeee", f: "ffffff", g: "another heap allocated string, seven", h: "hhhhhhhhhhhhhhhhhhhhhhhhh")
end 'main'
```
```exitcode
127
```

<!-- test: managed-array-arguments-in-stack-slots -->
The same for `Array`: the seventh and eighth arguments are heap arrays reached through a stack slot,
and both are dropped by the callee.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function totalCount(a Integer, b Integer, c Integer, d Integer, e Integer, f IntArray, g IntArray, h IntArray) returns Integer
	return a + b + c + d + e + f.count() + g.count() + h.count()
end 'totalCount'

function main() returns ExitCode
	var first = IntArray.create()
	first.push(1)
	first.push(2)
	var second = IntArray.create()
	second.push(3)
	second.push(4)
	second.push(5)
	var third = IntArray.create()
	third.push(6)
	return totalCount(10, b: 20, c: 30, d: 40, e: 50, f: first, g: second, h: third)
end 'main'
```
```exitcode
156
```

<!-- test: argument-slots-are-capped-and-the-cap-is-stated -->
The remaining ceiling is the width of the per-argument float mask that routes each argument to its
register file, not a register count — and it is diagnosed rather than silently miscompiled.
```maxon
typealias Integer = int(i64.min to i64.max)

function tooWide(p1 Integer, p2 Integer, p3 Integer, p4 Integer, p5 Integer, p6 Integer, p7 Integer, p8 Integer, p9 Integer, p10 Integer, p11 Integer, p12 Integer, p13 Integer, p14 Integer, p15 Integer, p16 Integer, p17 Integer, p18 Integer, p19 Integer, p20 Integer, p21 Integer, p22 Integer, p23 Integer, p24 Integer, p25 Integer, p26 Integer, p27 Integer, p28 Integer, p29 Integer, p30 Integer, p31 Integer, p32 Integer, p33 Integer, p34 Integer, p35 Integer, p36 Integer, p37 Integer, p38 Integer, p39 Integer, p40 Integer, p41 Integer, p42 Integer, p43 Integer, p44 Integer, p45 Integer, p46 Integer, p47 Integer, p48 Integer, p49 Integer, p50 Integer, p51 Integer, p52 Integer, p53 Integer, p54 Integer, p55 Integer, p56 Integer, p57 Integer, p58 Integer, p59 Integer, p60 Integer, p61 Integer, p62 Integer, p63 Integer, p64 Integer, p65 Integer) returns Integer
	return p1 + p65
end 'tooWide'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/stack-arguments/argument-slots-are-capped-and-the-cap-is-stated.test:4:10: Unsupported: a function with 65 argument slots — more than the 64 a call can carry. A signature's slots are its declared parameters plus the hidden ones the ABI adds (a companion environment per function-typed parameter, a layout descriptor for a generic that reads `sizeof`, one witness per `where` constraint), and the limit is the width of the per-argument float mask that routes each one to its register file
```

<!-- test: a-spawn-keeps-the-lower-async-argument-ceiling -->
An `async` call's arguments do not travel in the calling convention at all — they ride the green
thread's inline argument region, which the hand-assembled trampoline reads back into the argument
registers — so a spawn has no stack-argument path even now that an ordinary call does. The lower
ceiling is diagnosed rather than overrunning the region.
```maxon
typealias Integer = int(i64.min to i64.max)

function wide7(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer, g Integer) returns bool
	return a + b + c + d + e + f + g > 0
end 'wide7'

function main() returns ExitCode
	let p = async wide7(1, b: 2, c: 4, d: 8, e: 16, f: 32, g: 64)
	if await p 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/stack-arguments/a-spawn-keeps-the-lower-async-argument-ceiling.test:9:16: Unsupported: `async wide7(…)` passes 7 arguments — more than the 6 a spawn can carry. A spawned call's arguments ride the green thread's inline argument region, which the hand-assembled trampoline reads back into the argument registers, so a spawn has no stack-argument path even though an ordinary call does. Call it directly, or pass fewer arguments
```

<!-- test: every-argument-of-a-wide-call-is-distinct -->
Twenty-two arguments, every one a DIFFERENT value, summed. The ported `x64-stack-arg-disp32` cases
pass zeros everywhere but the boundary, so they catch a wrong displacement at one slot; this one
catches a wrong slot ANYWHERE, because no two arguments can be swapped, duplicated or dropped without
moving the total.
```maxon
typealias Integer = int(i64.min to i64.max)

function sum22(a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer, a13 Integer, a14 Integer, a15 Integer, a16 Integer, a17 Integer, a18 Integer, a19 Integer, a20 Integer, a21 Integer, a22 Integer) returns Integer
	return a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15 + a16 + a17 + a18 + a19 + a20 + a21 + a22
end 'sum22'

function main() returns ExitCode
	return sum22(1, a2: 2, a3: 3, a4: 4, a5: 5, a6: 6, a7: 7, a8: 8, a9: 9, a10: 10, a11: 11, a12: 12, a13: 13, a14: 14, a15: 15, a16: 16, a17: 17, a18: 18, a19: 19, a20: 20, a21: 21, a22: 22)
end 'main'
```
```exitcode
253
```
