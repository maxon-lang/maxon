---
feature: static-variables
status: experimental
keywords: [static, var, global, mutable, module, type]
category: language
---

# Static Variables: the cases that pin emitted code

The cases of `specs/static-variables.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: data-section-bool-1byte -->
A single bool global occupies 1 byte in the .data section.

```maxon
var flag = true

function main() returns ExitCode
	if flag 'read'
		return 0
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
i8 1
```

```TargetIr:x64-windows
data {
  __data_flag@0 = i1 1
  __mrt_console_probe_stdin@1 = i8 0
  __mrt_console_probe_stdout@2 = i8 0
  __mrt_console_probe_stderr@3 = i8 0
  __mrt_program_started@4 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegGlobal rax, __data_flag
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  read:
    x64.movRegImm32 r8, 0
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_flag@0 = i1 1
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegGlobal rax, __data_flag
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  read:
    x64.movRegImm32 r8, 0
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_flag@0 = i1 1
  __mrt_program_started@1 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_flag
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.cbz x0, ifcont
  read:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_flag@0 = i1 1
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_flag
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.cbz x0, ifcont
  read:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: data-section-i64-8byte -->
A single i64 global occupies 8 bytes in the .data section.

```maxon
var counter = 42

function main() returns ExitCode
	return counter - 42
end 'main'
```
```exitcode
0
```
```RequiredData
i64 42
```

```TargetIr:x64-windows
data {
  __data_counter@0 = i64 42
  __mrt_console_probe_stdin@8 = i8 0
  __mrt_console_probe_stdout@9 = i8 0
  __mrt_console_probe_stderr@10 = i8 0
  __mrt_program_started@11 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_counter
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegImm32 r8, rax, -42
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at data-section-i64-8byte.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_counter@0 = i64 42
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_counter
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegImm32 r8, rax, -42
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at data-section-i64-8byte.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_counter@0 = i64 42
  __mrt_program_started@8 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_counter
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.sub x0, x0, 42
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at data-section-i64-8byte.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_counter@0 = i64 42
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_counter
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.sub x0, x0, 42
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at data-section-i64-8byte.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: data-section-f64-8byte -->
A single f64 global occupies 8 bytes in the .data section.

```maxon
var pi = 3.14

function main() returns ExitCode
	if pi > 3.0 'read'
		return 0
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
f64 3.14
```

```TargetIr:x64-windows
data {
  __data_pi@0 = f64 3.14
  __mrt_console_probe_stdin@8 = i8 0
  __mrt_console_probe_stdout@9 = i8 0
  __mrt_console_probe_stderr@10 = i8 0
  __mrt_program_started@11 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegGlobal rax, __data_pi
    x64.loadRegBaseDisp.word64 xmm0, [rax + 0]
    x64.movsdRegRip xmm1, [rip + __fconst_3]
    x64.ucomisdRegReg xmm0, xmm1
    x64.jcc belowEqual, ifcont
  read:
    x64.movRegImm32 r8, 0
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_pi@0 = f64 3.14
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegGlobal rax, __data_pi
    x64.loadRegBaseDisp.word64 xmm0, [rax + 0]
    x64.movsdRegRip xmm1, [rip + __fconst_3]
    x64.ucomisdRegReg xmm0, xmm1
    x64.jcc belowEqual, ifcont
  read:
    x64.movRegImm32 r8, 0
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_pi@0 = f64 3.14
  __mrt_program_started@8 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_pi
    arm64.loadRegBaseDisp.word64 d0, [x0 + 0]
    arm64.fpLoadRdata d1, [rdata + __fconst_3]
    arm64.fcmp d0, d1
    arm64.b.le ifcont
  read:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_pi@0 = f64 3.14
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_pi
    arm64.loadRegBaseDisp.word64 d0, [x0 + 0]
    arm64.fpLoadRdata d1, [rdata + __fconst_3]
    arm64.fcmp d0, d1
    arm64.b.le ifcont
  read:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: data-section-f64-8byte-folded -->
A FOLDED float initializer lays down the same 8 bytes as the literal: `3.0 + 0.14` produces `3.14`, the strongest proof the constant evaluator produced a NUMBER (folded with the host's f64) and not a summed bit pattern (byte-identical to the literal `3.14`).

```maxon
var pi = 3.0 + 0.14

function main() returns ExitCode
	if pi > 3.0 'read'
		return 0
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
f64 3.14
```

```TargetIr:x64-windows
data {
  __data_pi@0 = f64 3.14
  __mrt_console_probe_stdin@8 = i8 0
  __mrt_console_probe_stdout@9 = i8 0
  __mrt_console_probe_stderr@10 = i8 0
  __mrt_program_started@11 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegGlobal rax, __data_pi
    x64.loadRegBaseDisp.word64 xmm0, [rax + 0]
    x64.movsdRegRip xmm1, [rip + __fconst_3]
    x64.ucomisdRegReg xmm0, xmm1
    x64.jcc belowEqual, ifcont
  read:
    x64.movRegImm32 r8, 0
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_pi@0 = f64 3.14
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegGlobal rax, __data_pi
    x64.loadRegBaseDisp.word64 xmm0, [rax + 0]
    x64.movsdRegRip xmm1, [rip + __fconst_3]
    x64.ucomisdRegReg xmm0, xmm1
    x64.jcc belowEqual, ifcont
  read:
    x64.movRegImm32 r8, 0
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_pi@0 = f64 3.14
  __mrt_program_started@8 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_pi
    arm64.loadRegBaseDisp.word64 d0, [x0 + 0]
    arm64.fpLoadRdata d1, [rdata + __fconst_3]
    arm64.fcmp d0, d1
    arm64.b.le ifcont
  read:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_pi@0 = f64 3.14
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_pi
    arm64.loadRegBaseDisp.word64 d0, [x0 + 0]
    arm64.fpLoadRdata d1, [rdata + __fconst_3]
    arm64.fcmp d0, d1
    arm64.b.le ifcont
  read:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: data-section-bool-then-i64-sorted -->
A bool and i64 global: sorted largest-first, no padding needed.

```maxon
var flag = false
var counter = 42

function main() returns ExitCode
	if flag 'read'
		return 1
	end 'read'
	return counter - 42
end 'main'
```
```exitcode
0
```
```RequiredData
i64 42
i8 0
```

```TargetIr:x64-windows
data {
  __data_counter@0 = i64 42
  __data_flag@8 = i1 0
  __mrt_console_probe_stdin@9 = i8 0
  __mrt_console_probe_stdout@10 = i8 0
  __mrt_console_probe_stderr@11 = i8 0
  __mrt_program_started@12 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_flag
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  read:
    x64.movRegImm32 r8, 1
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.leaRegGlobal rax, __data_counter
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegImm32 r8, rax, -42
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at data-section-bool-then-i64-sorted.test:9: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_counter@0 = i64 42
  __data_flag@8 = i1 0
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_flag
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  read:
    x64.movRegImm32 r8, 1
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.leaRegGlobal rax, __data_counter
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegImm32 r8, rax, -42
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at data-section-bool-then-i64-sorted.test:9: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_counter@0 = i64 42
  __data_flag@8 = i1 0
  __mrt_program_started@9 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_flag
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.cbz x0, ifcont
  read:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.leaGlobal x0, __data_counter
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.sub x0, x0, 42
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at data-section-bool-then-i64-sorted.test:9: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_counter@0 = i64 42
  __data_flag@8 = i1 0
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_flag
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.cbz x0, ifcont
  read:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.leaGlobal x0, __data_counter
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.sub x0, x0, 42
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at data-section-bool-then-i64-sorted.test:9: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: data-section-bool-true-then-i64 -->
A true bool and i64: sorted largest-first, no padding needed.

```maxon
var flag = true
var counter = 99

function main() returns ExitCode
	if flag 'read'
		return counter - 99
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
i64 99
i8 1
```

```TargetIr:x64-windows
data {
  __data_counter@0 = i64 99
  __data_flag@8 = i1 1
  __mrt_console_probe_stdin@9 = i8 0
  __mrt_console_probe_stdout@10 = i8 0
  __mrt_console_probe_stderr@11 = i8 0
  __mrt_program_started@12 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_flag
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  read:
    x64.leaRegGlobal rax, __data_counter
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegImm32 r8, rax, -99
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at data-section-bool-true-then-i64.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_counter@0 = i64 99
  __data_flag@8 = i1 1
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_flag
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  read:
    x64.leaRegGlobal rax, __data_counter
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegImm32 r8, rax, -99
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at data-section-bool-true-then-i64.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_counter@0 = i64 99
  __data_flag@8 = i1 1
  __mrt_program_started@9 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_flag
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.cbz x0, ifcont
  read:
    arm64.leaGlobal x0, __data_counter
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.sub x0, x0, 99
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at data-section-bool-true-then-i64.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_counter@0 = i64 99
  __data_flag@8 = i1 1
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_flag
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.cbz x0, ifcont
  read:
    arm64.leaGlobal x0, __data_counter
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.sub x0, x0, 99
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at data-section-bool-true-then-i64.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: data-section-i64-then-bool -->
An i64 followed by a bool: no padding needed since bool has 1-byte alignment.

```maxon
var counter = 7
var flag = true

function main() returns ExitCode
	if flag 'read'
		return counter - 7
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
i64 7
i8 1
```

```TargetIr:x64-windows
data {
  __data_counter@0 = i64 7
  __data_flag@8 = i1 1
  __mrt_console_probe_stdin@9 = i8 0
  __mrt_console_probe_stdout@10 = i8 0
  __mrt_console_probe_stderr@11 = i8 0
  __mrt_program_started@12 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_flag
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  read:
    x64.leaRegGlobal rax, __data_counter
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegImm32 r8, rax, -7
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at data-section-i64-then-bool.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_counter@0 = i64 7
  __data_flag@8 = i1 1
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_flag
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  read:
    x64.leaRegGlobal rax, __data_counter
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegImm32 r8, rax, -7
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at data-section-i64-then-bool.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_counter@0 = i64 7
  __data_flag@8 = i1 1
  __mrt_program_started@9 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_flag
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.cbz x0, ifcont
  read:
    arm64.leaGlobal x0, __data_counter
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.sub x0, x0, 7
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at data-section-i64-then-bool.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_counter@0 = i64 7
  __data_flag@8 = i1 1
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_flag
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.cbz x0, ifcont
  read:
    arm64.leaGlobal x0, __data_counter
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.sub x0, x0, 7
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at data-section-i64-then-bool.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: data-section-multiple-bools -->
Multiple consecutive bools occupy 1 byte each with no padding.

```maxon
var a = true
var b = false
var c = true

function main() returns ExitCode
	if a and c and (b == false) 'read'
		return 0
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
i8 1
i8 0
i8 1
```

```TargetIr:x64-windows
data {
  __data_a@0 = i1 1
  __data_b@1 = i1 0
  __data_c@2 = i1 1
  __mrt_console_probe_stdin@3 = i8 0
  __mrt_console_probe_stdout@4 = i8 0
  __mrt_console_probe_stderr@5 = i8 0
  __mrt_program_started@6 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegGlobal rax, __data_a
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#7
  andrhs#1:
    x64.leaRegGlobal rax, __data_c
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp scmerge#2
  critsplit#7:
    x64.movRegReg rax, rcx
  scmerge#2:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#8
  andrhs#3:
    x64.leaRegGlobal rax, __data_b
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.setccReg equal, rax
    x64.jmp scmerge#4
  critsplit#8:
    x64.movRegReg rax, rcx
  scmerge#4:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  read:
    x64.movRegImm32 r8, 0
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_a@0 = i1 1
  __data_b@1 = i1 0
  __data_c@2 = i1 1
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegGlobal rax, __data_a
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#7
  andrhs#1:
    x64.leaRegGlobal rax, __data_c
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp scmerge#2
  critsplit#7:
    x64.movRegReg rax, rcx
  scmerge#2:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#8
  andrhs#3:
    x64.leaRegGlobal rax, __data_b
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.setccReg equal, rax
    x64.jmp scmerge#4
  critsplit#8:
    x64.movRegReg rax, rcx
  scmerge#4:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  read:
    x64.movRegImm32 r8, 0
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_a@0 = i1 1
  __data_b@1 = i1 0
  __data_c@2 = i1 1
  __mrt_program_started@3 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_a
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#7
  andrhs#1:
    arm64.leaGlobal x0, __data_c
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b scmerge#2
  critsplit#7:
    arm64.movRegReg x0, x1
  scmerge#2:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#8
  andrhs#3:
    arm64.leaGlobal x0, __data_b
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.cset x0, eq
    arm64.b scmerge#4
  critsplit#8:
    arm64.movRegReg x0, x1
  scmerge#4:
    arm64.cbz x0, ifcont
  read:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_a@0 = i1 1
  __data_b@1 = i1 0
  __data_c@2 = i1 1
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_a
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#7
  andrhs#1:
    arm64.leaGlobal x0, __data_c
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b scmerge#2
  critsplit#7:
    arm64.movRegReg x0, x1
  scmerge#2:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#8
  andrhs#3:
    arm64.leaGlobal x0, __data_b
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.cset x0, eq
    arm64.b scmerge#4
  critsplit#8:
    arm64.movRegReg x0, x1
  scmerge#4:
    arm64.cbz x0, ifcont
  read:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: data-section-mixed-types -->
Mixed bool, i64, f64 globals sorted largest-first, no padding.

```maxon
var flag = true
var count = 10
var ratio = 2.5

function main() returns ExitCode
	if flag and (count == 10) and (ratio > 2.0) 'read'
		return 0
	end 'read'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
i64 10
f64 2.5
i8 1
```

```TargetIr:x64-windows
data {
  __data_count@0 = i64 10
  __data_ratio@8 = f64 2.5
  __data_flag@16 = i1 1
  __mrt_console_probe_stdin@17 = i8 0
  __mrt_console_probe_stdout@18 = i8 0
  __mrt_console_probe_stderr@19 = i8 0
  __mrt_program_started@20 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegGlobal rax, __data_flag
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#7
  andrhs#1:
    x64.leaRegGlobal rax, __data_count
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 10
    x64.setccReg equal, rax
    x64.jmp scmerge#2
  critsplit#7:
    x64.movRegReg rax, rcx
  scmerge#2:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#8
  andrhs#3:
    x64.leaRegGlobal rax, __data_ratio
    x64.loadRegBaseDisp.word64 xmm0, [rax + 0]
    x64.movsdRegRip xmm1, [rip + __fconst_2]
    x64.ucomisdRegReg xmm0, xmm1
    x64.setccReg above, rax
    x64.jmp scmerge#4
  critsplit#8:
    x64.movRegReg rax, rcx
  scmerge#4:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  read:
    x64.movRegImm32 r8, 0
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_count@0 = i64 10
  __data_ratio@8 = f64 2.5
  __data_flag@16 = i1 1
  __mrt_envp@24 = i64 0
  __mrt_signal_stack_bytes@32 = i64 0
  __mrt_program_started@40 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegGlobal rax, __data_flag
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#7
  andrhs#1:
    x64.leaRegGlobal rax, __data_count
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 10
    x64.setccReg equal, rax
    x64.jmp scmerge#2
  critsplit#7:
    x64.movRegReg rax, rcx
  scmerge#2:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#8
  andrhs#3:
    x64.leaRegGlobal rax, __data_ratio
    x64.loadRegBaseDisp.word64 xmm0, [rax + 0]
    x64.movsdRegRip xmm1, [rip + __fconst_2]
    x64.ucomisdRegReg xmm0, xmm1
    x64.setccReg above, rax
    x64.jmp scmerge#4
  critsplit#8:
    x64.movRegReg rax, rcx
  scmerge#4:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  read:
    x64.movRegImm32 r8, 0
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_count@0 = i64 10
  __data_ratio@8 = f64 2.5
  __data_flag@16 = i1 1
  __mrt_program_started@17 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_flag
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#7
  andrhs#1:
    arm64.leaGlobal x0, __data_count
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 10
    arm64.cset x0, eq
    arm64.b scmerge#2
  critsplit#7:
    arm64.movRegReg x0, x1
  scmerge#2:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#8
  andrhs#3:
    arm64.leaGlobal x0, __data_ratio
    arm64.loadRegBaseDisp.word64 d0, [x0 + 0]
    arm64.fpLoadRdata d1, [rdata + __fconst_2]
    arm64.fcmp d0, d1
    arm64.cset x0, gt
    arm64.b scmerge#4
  critsplit#8:
    arm64.movRegReg x0, x1
  scmerge#4:
    arm64.cbz x0, ifcont
  read:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_count@0 = i64 10
  __data_ratio@8 = f64 2.5
  __data_flag@16 = i1 1
  __mrt_envp@24 = i64 0
  __mrt_signal_stack_bytes@32 = i64 0
  __mrt_program_started@40 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_flag
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#7
  andrhs#1:
    arm64.leaGlobal x0, __data_count
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 10
    arm64.cset x0, eq
    arm64.b scmerge#2
  critsplit#7:
    arm64.movRegReg x0, x1
  scmerge#2:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#8
  andrhs#3:
    arm64.leaGlobal x0, __data_ratio
    arm64.loadRegBaseDisp.word64 d0, [x0 + 0]
    arm64.fpLoadRdata d1, [rdata + __fconst_2]
    arm64.fcmp d0, d1
    arm64.cset x0, gt
    arm64.b scmerge#4
  critsplit#8:
    arm64.movRegReg x0, x1
  scmerge#4:
    arm64.cbz x0, ifcont
  read:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: data-section-runtime-word-after-a-bool-is-aligned -->
The runtime's words are laid out after the program's own globals, and each is still naturally aligned:
a 1-byte user global is followed by seven bytes of padding before the scheduler's 8-byte worker mark.
An arm64 exclusive load or store faults on an address that is not a multiple of its width.

```maxon
var flag = true

function main() returns ExitCode
	let workers = __Builtins.schedMaxActiveWorkers()

	if flag 'read'
		return workers - 1
	end 'read'

	return 1
end 'main'
```
```exitcode
0
```
```RequiredData
i8 1
i8 0
i8 0
i8 0
i8 0
i8 0
i8 0
i8 0
i64 1
```

```TargetIr:x64-windows
data {
  __data_flag@0 = i1 1
  __sched_max_active_workers@8 = i64 1
  __mrt_console_probe_stdin@16 = i8 0
  __mrt_console_probe_stdout@17 = i8 0
  __mrt_console_probe_stderr@18 = i8 0
  __mrt_program_started@19 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.callDirect __sched_max_active_workers
    x64.leaRegGlobal rax, __data_flag
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  read:
    x64.leaRegRegImm32 r8, r8, -1
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at data-section-runtime-word-after-a-bool-is-aligned.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_flag@0 = i1 1
  __sched_max_active_workers@8 = i64 1
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.callDirect __sched_max_active_workers
    x64.leaRegGlobal rax, __data_flag
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  read:
    x64.leaRegRegImm32 r8, r8, -1
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at data-section-runtime-word-after-a-bool-is-aligned.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_flag@0 = i1 1
  __sched_max_active_workers@8 = i64 1
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.bl __sched_max_active_workers
    arm64.leaGlobal x1, __data_flag
    arm64.loadRegBaseDisp.byte x1, [x1 + 0]
    arm64.cbz x1, ifcont
  read:
    arm64.sub x0, x0, 1
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at data-section-runtime-word-after-a-bool-is-aligned.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_flag@0 = i1 1
  __sched_max_active_workers@8 = i64 1
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.bl __sched_max_active_workers
    arm64.leaGlobal x1, __data_flag
    arm64.loadRegBaseDisp.byte x1, [x1 + 0]
    arm64.cbz x1, ifcont
  read:
    arm64.sub x0, x0, 1
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at data-section-runtime-word-after-a-bool-is-aligned.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```
