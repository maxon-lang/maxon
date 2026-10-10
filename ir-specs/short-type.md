---
feature: short-type
status: active
keywords: [short, 16-bit, u16, i16, storage]
category: types
---

# Short (16-bit) Storage: the cases that pin emitted code

The cases of `specs/short-type.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

### U16 Constant Array Rdata

<!-- test: short-type.u16-rdata -->
Narrow array-element storage flows through the collection-from-array
syntax: when `U16Array = Array with U16`, the element type `U16`
(`int(0 to 65535)`) propagates through `ParseFromExpression` →
`EmitArrayLiteralElements` so each integer literal is range-checked at
compile time and re-tagged with the optimal storage kind (i16/u16). The
constant-folding pass then lifts the array into `.rdata` with the narrow
element width.

```maxon
typealias U16 = int(0 to 65535)
typealias U16Array = Array with U16

function main() returns ExitCode
	let arr = U16Array from [10, 20, 30]
	let a = try arr.get(0) otherwise 0
	let b = try arr.get(1) otherwise 0
	let c = try arr.get(2) otherwise 0
	return a + b + c
end 'main'
```
```exitcode
60
```
```RequiredRdata
u16[] 10, 20, 30
```

```TargetIr:x64-windows
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [rbx + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 2
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegImm32 rdx, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#1
  tryerr#2:
    x64.movRegImm32 r12, 0
    x64.jmp trycont#3
  tryok#1:
    x64.movRegReg r12, r8
  trycont#3:
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#4
  tryerr#5:
    x64.movRegImm32 r13, 0
    x64.jmp trycont#6
  tryok#4:
    x64.movRegReg r13, r8
  trycont#6:
    x64.movRegImm32 rdx, 2
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#7
  tryerr#8:
    x64.movRegImm32 rax, 0
    x64.jmp trycont#9
  tryok#7:
    x64.movRegReg rax, r8
  trycont#9:
    x64.leaRegRegReg rcx, r12, r13
    x64.leaRegRegReg r12, rcx, rax
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r12, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at short-type.u16-rdata.test:10: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
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
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_program_started@40 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [rbx + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 2
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegImm32 rdx, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#1
  tryerr#2:
    x64.movRegImm32 r12, 0
    x64.jmp trycont#3
  tryok#1:
    x64.movRegReg r12, r8
  trycont#3:
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#4
  tryerr#5:
    x64.movRegImm32 r13, 0
    x64.jmp trycont#6
  tryok#4:
    x64.movRegReg r13, r8
  trycont#6:
    x64.movRegImm32 rdx, 2
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#7
  tryerr#8:
    x64.movRegImm32 rax, 0
    x64.jmp trycont#9
  tryok#7:
    x64.movRegReg rax, r8
  trycont#9:
    x64.leaRegRegReg rcx, r12, r13
    x64.leaRegRegReg r12, rcx, rax
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r12, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at short-type.u16-rdata.test:10: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.movImm x0, 3
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 2
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movImm x1, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok#1
  tryerr#2:
    arm64.movImm x20, 0
    arm64.b trycont#3
  tryok#1:
    arm64.movRegReg x20, x0
  trycont#3:
    arm64.movImm x1, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok#4
  tryerr#5:
    arm64.movImm x21, 0
    arm64.b trycont#6
  tryok#4:
    arm64.movRegReg x21, x0
  trycont#6:
    arm64.movImm x1, 2
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq trycont#9
  tryerr#8:
    arm64.movImm x0, 0
  trycont#9:
    arm64.add x1, x20, x21
    arm64.add x20, x1, x0
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at short-type.u16-rdata.test:10: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_program_started@40 = i8 0
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.movImm x0, 3
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 2
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movImm x1, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok#1
  tryerr#2:
    arm64.movImm x20, 0
    arm64.b trycont#3
  tryok#1:
    arm64.movRegReg x20, x0
  trycont#3:
    arm64.movImm x1, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok#4
  tryerr#5:
    arm64.movImm x21, 0
    arm64.b trycont#6
  tryok#4:
    arm64.movRegReg x21, x0
  trycont#6:
    arm64.movImm x1, 2
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq trycont#9
  tryerr#8:
    arm64.movImm x0, 0
  trycont#9:
    arm64.add x1, x20, x21
    arm64.add x20, x1, x0
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at short-type.u16-rdata.test:10: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```

### U16 Global Variable

<!-- test: short-type.u16-global -->
```maxon
typealias U16 = int(0 to 65535)

var counter = 42 as U16

function main() returns ExitCode
	return counter
end 'main'
```
```exitcode
42
```
```RequiredData
u16 42
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
    x64.leaRegGlobal rax, __data_counter
    x64.loadRegBaseDisp.word64 r8, [rax + 0]
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
    x64.loadRegBaseDisp.word64 r8, [rax + 0]
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
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at short-type.u16-global.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at short-type.u16-global.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at short-type.u16-global.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```
