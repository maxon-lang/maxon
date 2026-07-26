---
feature: x64-stack-arg-disp32
status: stable
keywords: [function, parameters, calling-convention]
category: functions
---

# Many-Parameter Stack Argument Passing

## Documentation

shv2's x64 calling convention passes the first SIX integer parameters in registers
(`rcx, rdx, rax, r9, rsi, rdi`) and spills the remainder onto the caller's stack,
where the callee reads them at positive `[rbp + N]` displacements. The first stack
argument sits at `[rbp + 0x30]`: 16 bytes for the return address and the saved
`rbp`, then the 32-byte shadow space the outgoing region starts past
(`win64IncomingArgDisp`). The ELEVENTH stack argument — the 17th integer
parameter — therefore lands at exactly `[rbp + 128]`.

⚠ **Those numbers read SEVEN registers and `[rbp + 0x10]` until this rung, describing
v1's ABI, from which this spec was ported verbatim.** Corrected here against the
emitted code, where `sum22`'s parameter loads step `0x78(%rbp)` (disp8) then
`0x80(%rbp)` (disp32) — so the boundary parameter under shv2 is `a17`, not `a22`.
**The tests are unaffected and still pin exactly what they claim to**: each sums EVERY
parameter, so it straddles the boundary and catches a wrong load at any one of them,
whichever index happens to sit on it. (The same v1 arithmetic survives in a comment
inside `twenty-second-param-at-rbp-128`'s source. It is left alone deliberately —
editing it would rewrite a committed fragment for a comment — and rides the same
regeneration follow-up as the missing `x64-linux` goldens.)

A signed-byte (disp8) memory displacement only spans `-128..+127`, so a `+128`
displacement must be encoded with a 32-bit displacement (disp32). Encoding it as
a disp8 writes the byte `0x80`, which the CPU sign-extends to `-128`, silently
reading `[rbp - 128]` (a callee local slot) instead of the argument — a value
miscompile, not a crash. These tests pin correct value flow across, and past, the
disp8/disp32 boundary. Every parameter is summed so a corrupted load of any one
of them changes the result.

**Targets: an x64 case on its merits** — disp8-vs-disp32 is an x86 instruction-encoding fact, so arm64
cannot exhibit it and the `wasm32-wasi` lane (kept, already green) checks the sum rather than the
encoding. ⚠ Unlike `x64-large-frame-arg7`, **`x64-linux` DOES share this encoding and belongs here on the
merits** — it is gated out purely because this host cannot execute it, so its fragment was never
generated. **To widen**: `spec-test --target=x64-linux --update-required --filter=x64-stack-arg-disp32`
on a Linux host, commit `specs-shv2/fragments/x64-linux/x64-stack-arg-disp32/`, and add the target below.

## Tests

<!-- test: twenty-second-param-at-rbp-128 -->
<!-- targets: x64-windows, wasm32-wasi -->
```maxon

typealias Integer = int(i64.min to i64.max)

// 22 integer parameters: 1-7 arrive in registers, 8-22 on the stack. The 22nd
// (a22) sits at [rbp + 128] — the exact disp8/disp32 boundary.
function sum22(a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer, a13 Integer, a14 Integer, a15 Integer, a16 Integer, a17 Integer, a18 Integer, a19 Integer, a20 Integer, a21 Integer, a22 Integer) returns Integer
	return a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15 + a16 + a17 + a18 + a19 + a20 + a21 + a22
end 'sum22'

function main() returns ExitCode
	return sum22(0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 0, a21: 0, a22: 200)
end 'main'
```
```exitcode
200
```


<!-- test: params-straddling-rbp-128-boundary -->
<!-- targets: x64-windows, wasm32-wasi -->
```maxon

typealias Integer = int(i64.min to i64.max)

// Sum the three parameters straddling the boundary: a20 at [rbp+112], a21 at
// [rbp+120], a22 at [rbp+128]. A miscompiled a22 load corrupts the total.
function sum22(a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer, a13 Integer, a14 Integer, a15 Integer, a16 Integer, a17 Integer, a18 Integer, a19 Integer, a20 Integer, a21 Integer, a22 Integer) returns Integer
	return a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15 + a16 + a17 + a18 + a19 + a20 + a21 + a22
end 'sum22'

function main() returns ExitCode
	return sum22(0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 0, a18: 0, a19: 0, a20: 40, a21: 50, a22: 60)
end 'main'
```
```exitcode
150
```
