---
feature: x64-stack-arg-disp32
status: stable
keywords: [function, parameters, calling-convention]
category: functions
---

# Many-Parameter Stack Argument Passing

## Documentation

the compiler's x64 calling convention passes the first SIX integer parameters in registers
(`rcx, rdx, rax, r9, rsi, rdi`) and spills the remainder onto the caller's stack,
where the callee reads them at positive `[rbp + N]` displacements. The first stack
argument sits at `[rbp + 0x30]`: 16 bytes for the return address and the saved
`rbp`, then the 32-byte shadow space the outgoing region starts past
(`win64IncomingArgDisp`). The ELEVENTH stack argument — the 17th integer
parameter — therefore lands at exactly `[rbp + 128]`.

⚠ **The pins are the authority for those numbers**: `sum22` is kept out of line by its
`RequiredRuntime` block, and on both x64 lanes its `func @sum22` loads `a16` from `[rbp + 120]`
and `a17` from `[rbp + 128]`, so the boundary parameter is `a17`. The Target IR names a
displacement, not its encoding: the encoder writes `120` as a disp8 and `128` as a disp32, so a
wrong encoding shows in the exit code, not in the pin. **So each program sums EVERY parameter and
gives the boundary parameter a value of its own**: a load of `a17` through the wrong displacement
changes the result.

A signed-byte (disp8) memory displacement only spans `-128..+127`, so a `+128`
displacement must be encoded with a 32-bit displacement (disp32). Encoding it as
a disp8 writes the byte `0x80`, which the CPU sign-extends to `-128`, silently
reading `[rbp - 128]` instead of the argument — a value
miscompile, not a crash. These tests pin correct value flow across, and past, the
disp8/disp32 boundary.

**Targets: an x64 case on its merits** — disp8-vs-disp32 is an x86 instruction-encoding fact, so arm64
cannot exhibit it and the `wasm32-wasi` lane (kept, green) checks the sum rather than the encoding.
**`x64-linux` shares this encoding and calling convention and belongs here on the merits**, and
it runs: a Windows host cross-compiles x64-linux and runs the ELF under WSL2, which is exactly what the
cross-target gate does. ⚠ **A target absent because nobody minted its pin is not a restriction; it
is a gap wearing a restriction's syntax**, and here it would hide the one other native target that can
exhibit the bug this spec exists to catch.

## Tests

<!-- test: twenty-second-param-at-rbp-128 -->
<!-- unsupported-targets: arm64-macos, arm64-linux -->
⚠ **THE SUBJECT IS THE x64 ENCODING**: an operand past `rbp-128` needs a disp32 rather than a disp8. That
boundary is this ISA's; arm64 addresses a frame slot differently and the case would assert nothing there.
`a17`, the parameter at `[rbp + 128]`, carries 17 and `a22` the rest of the 200.
```maxon

typealias Integer = int(i64.min to i64.max)

function sum22(a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer, a13 Integer, a14 Integer, a15 Integer, a16 Integer, a17 Integer, a18 Integer, a19 Integer, a20 Integer, a21 Integer, a22 Integer) returns Integer
	return a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15 + a16 + a17 + a18 + a19 + a20 + a21 + a22
end 'sum22'

function main() returns ExitCode
	return sum22(0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 0, a17: 17, a18: 0, a19: 0, a20: 0, a21: 0, a22: 183)
end 'main'
```
```exitcode
200
```

```RequiredRuntime
sum22
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
}

func @sum22 {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 64
    x64.loadRegBaseDisp.word64 r8, [rbp + 48]
    x64.loadRegBaseDisp.word64 r10, [rbp + 56]
    x64.loadRegBaseDisp.word64 r11, [rbp + 64]
    x64.loadRegBaseDisp.word64 rbx, [rbp + 72]
    x64.loadRegBaseDisp.word64 r12, [rbp + 80]
    x64.loadRegBaseDisp.word64 r13, [rbp + 88]
    x64.loadRegBaseDisp.word64 r14, [rbp + 96]
    x64.loadRegBaseDisp.word64 r15, [rbp + 104]
    x64.storeSlotReg slot0, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 112]
    x64.storeSlotReg slot1, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 120]
    x64.storeSlotReg slot2, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 128]
    x64.storeSlotReg slot3, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 136]
    x64.storeSlotReg slot4, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 144]
    x64.storeSlotReg slot5, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 152]
    x64.storeSlotReg slot6, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 160]
    x64.storeSlotReg slot7, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 168]
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rcx, rax
    x64.leaRegRegReg rax, rax, r9
    x64.leaRegRegReg rax, rax, rsi
    x64.leaRegRegReg rax, rax, rdi
    x64.leaRegRegReg rax, rax, r8
    x64.leaRegRegReg rax, rax, r10
    x64.leaRegRegReg rax, rax, r11
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r14
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot2
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot3
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot4
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot5
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot6
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot7
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg r8, rax, r15
    x64.epilogue 64
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 168
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rsi, 0
    x64.movRegImm32 rdi, 0
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r9, 0
    x64.movRegImm32 r10, 0
    x64.movRegImm32 r11, 0
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 r12, 17
    x64.movRegImm32 r13, 0
    x64.movRegImm32 r14, 0
    x64.movRegImm32 r15, 0
    x64.storeBaseDispReg.word64 [rsp + 32], rax
    x64.storeBaseDispReg.word64 [rsp + 40], rcx
    x64.storeBaseDispReg.word64 [rsp + 48], rdx
    x64.storeBaseDispReg.word64 [rsp + 56], rsi
    x64.storeBaseDispReg.word64 [rsp + 64], rdi
    x64.storeBaseDispReg.word64 [rsp + 72], r8
    x64.storeBaseDispReg.word64 [rsp + 80], r9
    x64.storeBaseDispReg.word64 [rsp + 88], r10
    x64.storeBaseDispReg.word64 [rsp + 96], r11
    x64.storeBaseDispReg.word64 [rsp + 104], rbx
    x64.storeBaseDispReg.word64 [rsp + 112], r12
    x64.storeBaseDispReg.word64 [rsp + 120], r13
    x64.storeBaseDispReg.word64 [rsp + 128], r14
    x64.storeBaseDispReg.word64 [rsp + 136], r15
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 144], rax
    x64.movRegImm32 rax, 183
    x64.storeBaseDispReg.word64 [rsp + 152], rax
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r9, 0
    x64.movRegImm32 rsi, 0
    x64.movRegImm32 rdi, 0
    x64.callDirect sum22
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 168
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at twenty-second-param-at-rbp-128.test:10: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 168
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @sum22 {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 64
    x64.loadRegBaseDisp.word64 r8, [rbp + 48]
    x64.loadRegBaseDisp.word64 r10, [rbp + 56]
    x64.loadRegBaseDisp.word64 r11, [rbp + 64]
    x64.loadRegBaseDisp.word64 rbx, [rbp + 72]
    x64.loadRegBaseDisp.word64 r12, [rbp + 80]
    x64.loadRegBaseDisp.word64 r13, [rbp + 88]
    x64.loadRegBaseDisp.word64 r14, [rbp + 96]
    x64.loadRegBaseDisp.word64 r15, [rbp + 104]
    x64.storeSlotReg slot0, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 112]
    x64.storeSlotReg slot1, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 120]
    x64.storeSlotReg slot2, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 128]
    x64.storeSlotReg slot3, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 136]
    x64.storeSlotReg slot4, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 144]
    x64.storeSlotReg slot5, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 152]
    x64.storeSlotReg slot6, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 160]
    x64.storeSlotReg slot7, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 168]
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rcx, rax
    x64.leaRegRegReg rax, rax, r9
    x64.leaRegRegReg rax, rax, rsi
    x64.leaRegRegReg rax, rax, rdi
    x64.leaRegRegReg rax, rax, r8
    x64.leaRegRegReg rax, rax, r10
    x64.leaRegRegReg rax, rax, r11
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r14
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot2
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot3
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot4
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot5
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot6
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot7
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg r8, rax, r15
    x64.epilogue 64
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 168
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rsi, 0
    x64.movRegImm32 rdi, 0
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r9, 0
    x64.movRegImm32 r10, 0
    x64.movRegImm32 r11, 0
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 r12, 17
    x64.movRegImm32 r13, 0
    x64.movRegImm32 r14, 0
    x64.movRegImm32 r15, 0
    x64.storeBaseDispReg.word64 [rsp + 32], rax
    x64.storeBaseDispReg.word64 [rsp + 40], rcx
    x64.storeBaseDispReg.word64 [rsp + 48], rdx
    x64.storeBaseDispReg.word64 [rsp + 56], rsi
    x64.storeBaseDispReg.word64 [rsp + 64], rdi
    x64.storeBaseDispReg.word64 [rsp + 72], r8
    x64.storeBaseDispReg.word64 [rsp + 80], r9
    x64.storeBaseDispReg.word64 [rsp + 88], r10
    x64.storeBaseDispReg.word64 [rsp + 96], r11
    x64.storeBaseDispReg.word64 [rsp + 104], rbx
    x64.storeBaseDispReg.word64 [rsp + 112], r12
    x64.storeBaseDispReg.word64 [rsp + 120], r13
    x64.storeBaseDispReg.word64 [rsp + 128], r14
    x64.storeBaseDispReg.word64 [rsp + 136], r15
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 144], rax
    x64.movRegImm32 rax, 183
    x64.storeBaseDispReg.word64 [rsp + 152], rax
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r9, 0
    x64.movRegImm32 rsi, 0
    x64.movRegImm32 rdi, 0
    x64.callDirect sum22
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 168
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at twenty-second-param-at-rbp-128.test:10: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 168
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```


<!-- test: params-straddling-rbp-128-boundary -->
<!-- unsupported-targets: arm64-macos, arm64-linux -->
⚠ **THE SUBJECT IS THE x64 ENCODING**, as its sibling above: the disp8/disp32 boundary at `rbp-128` is
this ISA's, so arm64 would run the program without exercising the choice. The three nonzero
arguments are the parameters straddling the boundary: `a16` at `[rbp + 120]`, `a17` at `[rbp + 128]`
and `a18` at `[rbp + 136]`.
```maxon

typealias Integer = int(i64.min to i64.max)

function sum22(a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer, a13 Integer, a14 Integer, a15 Integer, a16 Integer, a17 Integer, a18 Integer, a19 Integer, a20 Integer, a21 Integer, a22 Integer) returns Integer
	return a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15 + a16 + a17 + a18 + a19 + a20 + a21 + a22
end 'sum22'

function main() returns ExitCode
	return sum22(0, a2: 0, a3: 0, a4: 0, a5: 0, a6: 0, a7: 0, a8: 0, a9: 0, a10: 0, a11: 0, a12: 0, a13: 0, a14: 0, a15: 0, a16: 40, a17: 50, a18: 60, a19: 0, a20: 0, a21: 0, a22: 0)
end 'main'
```
```exitcode
150
```

```RequiredRuntime
sum22
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
}

func @sum22 {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 64
    x64.loadRegBaseDisp.word64 r8, [rbp + 48]
    x64.loadRegBaseDisp.word64 r10, [rbp + 56]
    x64.loadRegBaseDisp.word64 r11, [rbp + 64]
    x64.loadRegBaseDisp.word64 rbx, [rbp + 72]
    x64.loadRegBaseDisp.word64 r12, [rbp + 80]
    x64.loadRegBaseDisp.word64 r13, [rbp + 88]
    x64.loadRegBaseDisp.word64 r14, [rbp + 96]
    x64.loadRegBaseDisp.word64 r15, [rbp + 104]
    x64.storeSlotReg slot0, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 112]
    x64.storeSlotReg slot1, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 120]
    x64.storeSlotReg slot2, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 128]
    x64.storeSlotReg slot3, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 136]
    x64.storeSlotReg slot4, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 144]
    x64.storeSlotReg slot5, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 152]
    x64.storeSlotReg slot6, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 160]
    x64.storeSlotReg slot7, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 168]
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rcx, rax
    x64.leaRegRegReg rax, rax, r9
    x64.leaRegRegReg rax, rax, rsi
    x64.leaRegRegReg rax, rax, rdi
    x64.leaRegRegReg rax, rax, r8
    x64.leaRegRegReg rax, rax, r10
    x64.leaRegRegReg rax, rax, r11
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r14
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot2
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot3
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot4
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot5
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot6
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot7
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg r8, rax, r15
    x64.epilogue 64
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 168
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rsi, 0
    x64.movRegImm32 rdi, 0
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r9, 0
    x64.movRegImm32 r10, 0
    x64.movRegImm32 r11, 0
    x64.movRegImm32 rbx, 40
    x64.movRegImm32 r12, 50
    x64.movRegImm32 r13, 60
    x64.movRegImm32 r14, 0
    x64.movRegImm32 r15, 0
    x64.storeBaseDispReg.word64 [rsp + 32], rax
    x64.storeBaseDispReg.word64 [rsp + 40], rcx
    x64.storeBaseDispReg.word64 [rsp + 48], rdx
    x64.storeBaseDispReg.word64 [rsp + 56], rsi
    x64.storeBaseDispReg.word64 [rsp + 64], rdi
    x64.storeBaseDispReg.word64 [rsp + 72], r8
    x64.storeBaseDispReg.word64 [rsp + 80], r9
    x64.storeBaseDispReg.word64 [rsp + 88], r10
    x64.storeBaseDispReg.word64 [rsp + 96], r11
    x64.storeBaseDispReg.word64 [rsp + 104], rbx
    x64.storeBaseDispReg.word64 [rsp + 112], r12
    x64.storeBaseDispReg.word64 [rsp + 120], r13
    x64.storeBaseDispReg.word64 [rsp + 128], r14
    x64.storeBaseDispReg.word64 [rsp + 136], r15
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 144], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 152], rax
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r9, 0
    x64.movRegImm32 rsi, 0
    x64.movRegImm32 rdi, 0
    x64.callDirect sum22
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 168
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at params-straddling-rbp-128-boundary.test:10: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 168
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @sum22 {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 64
    x64.loadRegBaseDisp.word64 r8, [rbp + 48]
    x64.loadRegBaseDisp.word64 r10, [rbp + 56]
    x64.loadRegBaseDisp.word64 r11, [rbp + 64]
    x64.loadRegBaseDisp.word64 rbx, [rbp + 72]
    x64.loadRegBaseDisp.word64 r12, [rbp + 80]
    x64.loadRegBaseDisp.word64 r13, [rbp + 88]
    x64.loadRegBaseDisp.word64 r14, [rbp + 96]
    x64.loadRegBaseDisp.word64 r15, [rbp + 104]
    x64.storeSlotReg slot0, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 112]
    x64.storeSlotReg slot1, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 120]
    x64.storeSlotReg slot2, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 128]
    x64.storeSlotReg slot3, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 136]
    x64.storeSlotReg slot4, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 144]
    x64.storeSlotReg slot5, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 152]
    x64.storeSlotReg slot6, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 160]
    x64.storeSlotReg slot7, r15
    x64.loadRegBaseDisp.word64 r15, [rbp + 168]
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rcx, rax
    x64.leaRegRegReg rax, rax, r9
    x64.leaRegRegReg rax, rax, rsi
    x64.leaRegRegReg rax, rax, rdi
    x64.leaRegRegReg rax, rax, r8
    x64.leaRegRegReg rax, rax, r10
    x64.leaRegRegReg rax, rax, r11
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r14
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot2
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot3
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot4
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot5
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot6
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegSlot rcx, slot7
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg r8, rax, r15
    x64.epilogue 64
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 168
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rsi, 0
    x64.movRegImm32 rdi, 0
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r9, 0
    x64.movRegImm32 r10, 0
    x64.movRegImm32 r11, 0
    x64.movRegImm32 rbx, 40
    x64.movRegImm32 r12, 50
    x64.movRegImm32 r13, 60
    x64.movRegImm32 r14, 0
    x64.movRegImm32 r15, 0
    x64.storeBaseDispReg.word64 [rsp + 32], rax
    x64.storeBaseDispReg.word64 [rsp + 40], rcx
    x64.storeBaseDispReg.word64 [rsp + 48], rdx
    x64.storeBaseDispReg.word64 [rsp + 56], rsi
    x64.storeBaseDispReg.word64 [rsp + 64], rdi
    x64.storeBaseDispReg.word64 [rsp + 72], r8
    x64.storeBaseDispReg.word64 [rsp + 80], r9
    x64.storeBaseDispReg.word64 [rsp + 88], r10
    x64.storeBaseDispReg.word64 [rsp + 96], r11
    x64.storeBaseDispReg.word64 [rsp + 104], rbx
    x64.storeBaseDispReg.word64 [rsp + 112], r12
    x64.storeBaseDispReg.word64 [rsp + 120], r13
    x64.storeBaseDispReg.word64 [rsp + 128], r14
    x64.storeBaseDispReg.word64 [rsp + 136], r15
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 144], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rsp + 152], rax
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r9, 0
    x64.movRegImm32 rsi, 0
    x64.movRegImm32 rdi, 0
    x64.callDirect sum22
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 168
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at params-straddling-rbp-128-boundary.test:10: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 168
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
