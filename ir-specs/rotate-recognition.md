---
feature: rotate-recognition
status: selfhosted
keywords: [optimizer, codegen, rotate, shift, bits, rol, ror]
category: codegen
---

# Rotate Recognition: the cases that pin emitted code

The cases of `specs/rotate.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: a-constant-count-rotate-is-one-instruction -->
`(word shl 8) or (word shr 56)` and `(word shr 3) or (word shl 61)` over a `bits(64)` value, whose counts sum to 64. Each pair is one rotate and the `TargetIr` pin shows no separate shift and no `orRegReg`.

The word is carried round a loop, so neither rotate is folded away by the caller's constants.

```maxon
typealias Word = bits(64)
typealias Turns = int(0 to 1000)

function rotateLeftByEight(word Word) returns Word
	return (word shl 8) or (word shr 56)
end 'rotateLeftByEight'

function rotateRightByThree(word Word) returns Word
	return (word shr 3) or (word shl 61)
end 'rotateRightByThree'

function main() returns ExitCode
	var word = 81985529216486895 as Word
	var turns = 0 as Turns

	while turns < 7 'spin'
		word = rotateLeftByEight(word)
		word = rotateRightByThree(word)
		turns = turns + 1
	end 'spin'

	return (word and 127) as ExitCode
end 'main'
```
```exitcode
60
```

```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.movRegImm rax, 81985529216486895
    x64.movRegImm32 rcx, 0
    x64.jmp whilehdr
  __il_body#9:
    x64.rolRegImm8 rax, rax, 8
  __il_body#10:
    x64.rolRegImm8 rax, rax, 61
  __il_cont:
    x64.leaRegRegImm32 rcx, rcx, 1
  whilehdr:
    x64.cmpRegImm32 rcx, 7
    x64.jcc less, __il_body#9
  whileexit:
    x64.movRegReg r8, rax
    x64.andRegImm32 r8, rax, 127
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

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.movRegImm rax, 81985529216486895
    x64.movRegImm32 rcx, 0
    x64.jmp whilehdr
  __il_body#9:
    x64.rolRegImm8 rax, rax, 8
  __il_body#10:
    x64.rolRegImm8 rax, rax, 61
  __il_cont:
    x64.leaRegRegImm32 rcx, rcx, 1
  whilehdr:
    x64.cmpRegImm32 rcx, 7
    x64.jcc less, __il_body#9
  whileexit:
    x64.movRegReg r8, rax
    x64.andRegImm32 r8, rax, 127
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 81985529216486895
    arm64.movImm x1, 0
    arm64.b whilehdr
  __il_body#9:
    arm64.ror x0, x0, 56
  __il_body#10:
    arm64.ror x0, x0, 3
  __il_cont:
    arm64.add x1, x1, 1
  whilehdr:
    arm64.cmp x1, 7
    arm64.b.lt __il_body#9
  whileexit:
    arm64.and x0, x0, 127
    arm64.epilogue 16
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 81985529216486895
    arm64.movImm x1, 0
    arm64.b whilehdr
  __il_body#9:
    arm64.ror x0, x0, 56
  __il_body#10:
    arm64.ror x0, x0, 3
  __il_cont:
    arm64.add x1, x1, 1
  whilehdr:
    arm64.cmp x1, 7
    arm64.b.lt __il_body#9
  whileexit:
    arm64.and x0, x0, 127
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: a-proven-variable-count-rotate-is-one-instruction -->
`(word shl n) or (word shr (64 - n))` and its mirror, where `n` is declared `int(1 to 63)`, so both counts are proven inside the window the shift instruction takes as written. Each pair is one rotate by a register count, and the pin shows `rolRegCl` and `rorRegCl` on x64.

```maxon
typealias Word = bits(64)
typealias Turns = int(0 to 1000)
typealias RotateCount = int(1 to 63)

function rotateLeftByCount(word Word, n RotateCount) returns Word
	return (word shl n) or (word shr (64 - n))
end 'rotateLeftByCount'

function rotateRightByCount(word Word, n RotateCount) returns Word
	return (word shr n) or (word shl (64 - n))
end 'rotateRightByCount'

function main() returns ExitCode
	var word = 81985529216486895 as Word
	var turns = 0 as Turns

	while turns < 7 'spin'
		let count = ((turns * 5) mod 63 + 1) as RotateCount
		word = rotateLeftByCount(word, n: count)
		word = rotateRightByCount(word, n: count)
		turns = turns + 1
	end 'spin'

	return (word and 127) as ExitCode
end 'main'
```
```exitcode
111
```

```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm rsi, 81985529216486895
    x64.movRegImm32 rdi, 0
    x64.jmp whilehdr
  spin:
    x64.imulRegRegImm32 rcx, rdi, 5
    x64.movRegImm rax, 292805461487453201
    x64.mulHighReg rcx
    x64.movRegReg rax, rcx
    x64.subRegReg rax, rcx, rdx
    x64.shrRegImm8 rax, rax, 1
    x64.leaRegRegReg rax, rax, rdx
    x64.shrRegImm8 rax, rax, 5
    x64.imulRegRegImm32 rax, rax, 63
    x64.subRegReg rcx, rcx, rax
    x64.leaRegRegImm32 rax, rcx, 1
    x64.cmpRegImm32 rax, 1
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rax, 63
    x64.jcc greater, __rc_panic
  __rc_ok#13:
    x64.movRegReg rcx, rax
    x64.rolRegCl rsi, rsi
  __rc_ok#17:
    x64.movRegReg rcx, rax
    x64.rorRegCl rsi, rsi
  __il_cont:
    x64.leaRegRegImm32 rdi, rdi, 1
  whilehdr:
    x64.cmpRegImm32 rdi, 7
    x64.jcc less, spin
  whileexit:
    x64.movRegReg r8, rsi
    x64.andRegImm32 r8, rsi, 127
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at a-proven-variable-count-rotate-is-one-instruction.test:19: Range check failed: value outside typealias 'RotateCount'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
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

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm rsi, 81985529216486895
    x64.movRegImm32 rdi, 0
    x64.jmp whilehdr
  spin:
    x64.imulRegRegImm32 rcx, rdi, 5
    x64.movRegImm rax, 292805461487453201
    x64.mulHighReg rcx
    x64.movRegReg rax, rcx
    x64.subRegReg rax, rcx, rdx
    x64.shrRegImm8 rax, rax, 1
    x64.leaRegRegReg rax, rax, rdx
    x64.shrRegImm8 rax, rax, 5
    x64.imulRegRegImm32 rax, rax, 63
    x64.subRegReg rcx, rcx, rax
    x64.leaRegRegImm32 rax, rcx, 1
    x64.cmpRegImm32 rax, 1
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rax, 63
    x64.jcc greater, __rc_panic
  __rc_ok#13:
    x64.movRegReg rcx, rax
    x64.rolRegCl rsi, rsi
  __rc_ok#17:
    x64.movRegReg rcx, rax
    x64.rorRegCl rsi, rsi
  __il_cont:
    x64.leaRegRegImm32 rdi, rdi, 1
  whilehdr:
    x64.cmpRegImm32 rdi, 7
    x64.jcc less, spin
  whileexit:
    x64.movRegReg r8, rsi
    x64.andRegImm32 r8, rsi, 127
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at a-proven-variable-count-rotate-is-one-instruction.test:19: Range check failed: value outside typealias 'RotateCount'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 81985529216486895
    arm64.movImm x1, 0
    arm64.b whilehdr
  spin:
    arm64.arm64AddLsl x2, x1, x1, lsl #2
    arm64.movImm x3, 292805461487453201
    arm64.umulh x3, x3, x2
    arm64.sub x4, x2, x3
    arm64.lsr x4, x4, 1
    arm64.add x3, x4, x3
    arm64.lsr x3, x3, 5
    arm64.movImm x16, 63
    arm64.mul x3, x3, x16
    arm64.sub x2, x2, x3
    arm64.add x2, x2, 1
    arm64.cmp x2, 1
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x2, 63
    arm64.b.gt __rc_panic
  __rc_ok#13:
    arm64.neg x3, x2
    arm64.rorv x0, x0, x3
  __rc_ok#17:
    arm64.rorv x0, x0, x2
  __il_cont:
    arm64.add x1, x1, 1
  whilehdr:
    arm64.cmp x1, 7
    arm64.b.lt spin
  whileexit:
    arm64.and x0, x0, 127
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at a-proven-variable-count-rotate-is-one-instruction.test:19: Range check failed: value outside typealias 'RotateCount'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 81985529216486895
    arm64.movImm x1, 0
    arm64.b whilehdr
  spin:
    arm64.arm64AddLsl x2, x1, x1, lsl #2
    arm64.movImm x3, 292805461487453201
    arm64.umulh x3, x3, x2
    arm64.sub x4, x2, x3
    arm64.lsr x4, x4, 1
    arm64.add x3, x4, x3
    arm64.lsr x3, x3, 5
    arm64.movImm x16, 63
    arm64.mul x3, x3, x16
    arm64.sub x2, x2, x3
    arm64.add x2, x2, 1
    arm64.cmp x2, 1
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x2, 63
    arm64.b.gt __rc_panic
  __rc_ok#13:
    arm64.neg x3, x2
    arm64.rorv x0, x0, x3
  __rc_ok#17:
    arm64.rorv x0, x0, x2
  __il_cont:
    arm64.add x1, x1, 1
  whilehdr:
    arm64.cmp x1, 7
    arm64.b.lt spin
  whileexit:
    arm64.and x0, x0, 127
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at a-proven-variable-count-rotate-is-one-instruction.test:19: Range check failed: value outside typealias 'RotateCount'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```
