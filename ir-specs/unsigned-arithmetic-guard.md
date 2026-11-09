---
feature: unsigned-arithmetic-guard
status: active
keywords: [typealias, unsigned, overflow, range check, panic]
category: types
---

# Unsigned Arithmetic Guard: the cases that pin emitted code

The cases of `specs/range-check-panic.md` whose subject is the emitted code of `+`, `-` and `*` over a full-width unsigned alias. Each pins the Target IR its program compiles to, on every native lane.

## Tests

### The sum a counter's interval covers carries no check

<!-- test: unsigned-arithmetic-guard.a-counted-sum-carries-no-compare -->
`(i as Slot) + 1` over a counted loop's counter has a proven interval inside `Slot`, so the addition is a bare add: no compare, no panic block for the sum.

```maxon
typealias Slot = int(0 to u64.max)

function main() returns ExitCode
	var last = 0 as Slot
	for i in 0 upto 10 'each'
		last = (i as Slot) + 1
	end 'each'
	return last as ExitCode
end 'main'
```
```exitcode
10
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
    x64.movRegImm32 r8, 0
    x64.movRegImm32 rax, 0
    x64.jmp forhdr
  each:
    x64.leaRegRegImm32 rax, rax, 1
  forstep:
    x64.movRegReg r8, rax
  forhdr:
    x64.cmpRegImm32 rax, 10
    x64.jcc less, each
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at unsigned-arithmetic-guard.a-counted-sum-carries-no-compare.test:9: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    x64.movRegImm32 r8, 0
    x64.movRegImm32 rax, 0
    x64.jmp forhdr
  each:
    x64.leaRegRegImm32 rax, rax, 1
  forstep:
    x64.movRegReg r8, rax
  forhdr:
    x64.cmpRegImm32 rax, 10
    x64.jcc less, each
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at unsigned-arithmetic-guard.a-counted-sum-carries-no-compare.test:9: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
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
    arm64.movImm x0, 0
    arm64.movImm x1, 0
    arm64.b forhdr
  each:
    arm64.add x1, x1, 1
  forstep:
    arm64.movRegReg x0, x1
  forhdr:
    arm64.cmp x1, 10
    arm64.b.lt each
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at unsigned-arithmetic-guard.a-counted-sum-carries-no-compare.test:9: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```
```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 0
    arm64.movImm x1, 0
    arm64.b forhdr
  each:
    arm64.add x1, x1, 1
  forstep:
    arm64.movRegReg x0, x1
  forhdr:
    arm64.cmp x1, 10
    arm64.b.lt each
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at unsigned-arithmetic-guard.a-counted-sum-carries-no-compare.test:9: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

### The sum nothing covers carries one compare

<!-- test: unsigned-arithmetic-guard.an-unproven-sum-carries-one-compare -->
`n + 1` over a parameter that recursion keeps from being folded has no proof: the sum is compared with its left operand, unsigned, and a carry reaches the panic block.

```maxon
typealias Slot = int(0 to u64.max)

function again(n Slot, rest Slot) returns Slot
	if rest == 0 'done'
		return n
	end 'done'

	return again(n + 1, rest: rest shr 1)
end 'again'

function main() returns ExitCode
	return again(0, rest: 1000) as ExitCode
end 'main'
```
```exitcode
10
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @again {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, ifcont
  done:
    x64.movRegReg r8, rcx
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.leaRegRegImm32 rax, rcx, 1
    x64.cmpRegReg rax, rcx
    x64.jcc below, __rc_panic
  __rc_ok:
    x64.shrRegImm8 rdx, rdx, 1
    x64.movRegReg rcx, rax
    x64.callDirect again
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at unsigned-arithmetic-guard.an-unproven-sum-carries-one-compare.test:9: Range check failed: value outside typealias 'Slot'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 1000
    x64.callDirect again
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
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at unsigned-arithmetic-guard.an-unproven-sum-carries-one-compare.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
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

func @again {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, ifcont
  done:
    x64.movRegReg r8, rcx
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.leaRegRegImm32 rax, rcx, 1
    x64.cmpRegReg rax, rcx
    x64.jcc below, __rc_panic
  __rc_ok:
    x64.shrRegImm8 rdx, rdx, 1
    x64.movRegReg rcx, rax
    x64.callDirect again
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at unsigned-arithmetic-guard.an-unproven-sum-carries-one-compare.test:9: Range check failed: value outside typealias 'Slot'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 1000
    x64.callDirect again
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
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at unsigned-arithmetic-guard.an-unproven-sum-carries-one-compare.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @again {
  entry:
    arm64.prologue 16
    arm64.cmp x1, 0
    arm64.b.ne ifcont
  done:
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.add x2, x0, 1
    arm64.cmp x2, x0
    arm64.b.lo __rc_panic
  __rc_ok:
    arm64.lsr x1, x1, 1
    arm64.movRegReg x0, x2
    arm64.bl again
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at unsigned-arithmetic-guard.an-unproven-sum-carries-one-compare.test:9: Range check failed: value outside typealias 'Slot'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 0
    arm64.movImm x1, 1000
    arm64.bl again
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at unsigned-arithmetic-guard.an-unproven-sum-carries-one-compare.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```
```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @again {
  entry:
    arm64.prologue 16
    arm64.cmp x1, 0
    arm64.b.ne ifcont
  done:
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.add x2, x0, 1
    arm64.cmp x2, x0
    arm64.b.lo __rc_panic
  __rc_ok:
    arm64.lsr x1, x1, 1
    arm64.movRegReg x0, x2
    arm64.bl again
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at unsigned-arithmetic-guard.an-unproven-sum-carries-one-compare.test:9: Range check failed: value outside typealias 'Slot'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 0
    arm64.movImm x1, 1000
    arm64.bl again
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at unsigned-arithmetic-guard.an-unproven-sum-carries-one-compare.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

### The product nothing covers carries one high multiply and one compare

<!-- test: unsigned-arithmetic-guard.an-unproven-product-carries-one-high-multiply -->
`n * m` over two parameters that recursion keeps from being folded has no proof: the high word of the 128-bit product is formed by one multiply and compared with zero, unsigned, and a nonzero word reaches the panic block.

```maxon
typealias Slot = int(0 to u64.max)

function grow(n Slot, m Slot, rest Slot) returns Slot
	if rest == 0 'done'
		return n
	end 'done'

	return grow(n * m, m: m, rest: rest shr 1)
end 'grow'

function main() returns ExitCode
	return grow(1, m: 1, rest: 1000) as ExitCode
end 'main'
```
```exitcode
1
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @grow {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegReg rsi, rdx
    x64.movRegReg rdi, rax
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, ifcont
  done:
    x64.movRegReg r8, rcx
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegReg r8, rcx
    x64.imulRegReg r8, rcx, rsi
    x64.movRegReg rax, rcx
    x64.mulHighReg rsi
    x64.cmpRegImm32 rdx, 0
    x64.jcc above, __rc_panic
  __rc_ok:
    x64.movRegReg rax, rdi
    x64.shrRegImm8 rax, rdi, 1
    x64.movRegReg rcx, r8
    x64.movRegReg rdx, rsi
    x64.callDirect grow
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at unsigned-arithmetic-guard.an-unproven-product-carries-one-high-multiply.test:9: Range check failed: value outside typealias 'Slot'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 1000
    x64.callDirect grow
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
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at unsigned-arithmetic-guard.an-unproven-product-carries-one-high-multiply.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
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

func @grow {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegReg rsi, rdx
    x64.movRegReg rdi, rax
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, ifcont
  done:
    x64.movRegReg r8, rcx
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegReg r8, rcx
    x64.imulRegReg r8, rcx, rsi
    x64.movRegReg rax, rcx
    x64.mulHighReg rsi
    x64.cmpRegImm32 rdx, 0
    x64.jcc above, __rc_panic
  __rc_ok:
    x64.movRegReg rax, rdi
    x64.shrRegImm8 rax, rdi, 1
    x64.movRegReg rcx, r8
    x64.movRegReg rdx, rsi
    x64.callDirect grow
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at unsigned-arithmetic-guard.an-unproven-product-carries-one-high-multiply.test:9: Range check failed: value outside typealias 'Slot'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 1000
    x64.callDirect grow
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
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at unsigned-arithmetic-guard.an-unproven-product-carries-one-high-multiply.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @grow {
  entry:
    arm64.prologue 16
    arm64.cmp x2, 0
    arm64.b.ne ifcont
  done:
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.mul x3, x0, x1
    arm64.umulh x0, x0, x1
    arm64.cmp x0, 0
    arm64.b.hi __rc_panic
  __rc_ok:
    arm64.lsr x2, x2, 1
    arm64.movRegReg x0, x3
    arm64.bl grow
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at unsigned-arithmetic-guard.an-unproven-product-carries-one-high-multiply.test:9: Range check failed: value outside typealias 'Slot'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 1
    arm64.movImm x1, 1
    arm64.movImm x2, 1000
    arm64.bl grow
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at unsigned-arithmetic-guard.an-unproven-product-carries-one-high-multiply.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```
```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @grow {
  entry:
    arm64.prologue 16
    arm64.cmp x2, 0
    arm64.b.ne ifcont
  done:
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.mul x3, x0, x1
    arm64.umulh x0, x0, x1
    arm64.cmp x0, 0
    arm64.b.hi __rc_panic
  __rc_ok:
    arm64.lsr x2, x2, 1
    arm64.movRegReg x0, x3
    arm64.bl grow
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at unsigned-arithmetic-guard.an-unproven-product-carries-one-high-multiply.test:9: Range check failed: value outside typealias 'Slot'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 1
    arm64.movImm x1, 1
    arm64.movImm x2, 1000
    arm64.bl grow
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at unsigned-arithmetic-guard.an-unproven-product-carries-one-high-multiply.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```
