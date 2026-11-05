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
    x64.prologue 56
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm32 rax, 2
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#12
    x64.jmp __im_load#13
  tryerr#2:
    x64.movRegImm32 rax, 0
    x64.jmp trycont#3
  __im_load#13:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word16 rax, [rax + 0]
  trycont#3:
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rcx, [r8 + 8]
    x64.cmpRegImm32 rcx, 1
    x64.jcc belowEqual, __im_slow#15
    x64.jmp __im_load#16
  tryerr#5:
    x64.movRegImm32 rcx, 0
    x64.jmp trycont#6
  __im_load#16:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 0]
    x64.loadRegBaseDisp.word16 rcx, [rcx + 2]
  trycont#6:
    x64.movRegImm32 rdx, 2
    x64.loadRegBaseDisp.word64 rsi, [r8 + 8]
    x64.cmpRegImm32 rsi, 2
    x64.jcc belowEqual, __im_slow#18
    x64.jmp __im_load#19
  tryerr#8:
    x64.movRegImm32 rdx, 0
    x64.jmp trycont#9
  __im_load#19:
    x64.loadRegBaseDisp.word64 rdx, [r8 + 0]
    x64.loadRegBaseDisp.word16 rdx, [rdx + 4]
  trycont#9:
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg rbx, rax, rdx
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 56
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#12:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#2
    x64.jmp trycont#3
  __im_slow#15:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rcx, r8
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#5
    x64.jmp trycont#6
  __im_slow#18:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot2, rcx
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rdx, r8
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot rcx, slot2
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#8
    x64.jmp trycont#9
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
    x64.prologue 56
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm32 rax, 2
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#15
    x64.jmp __im_load#16
  tryerr#2:
    x64.movRegImm32 rax, 0
    x64.jmp trycont#3
  __im_load#16:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word16 rax, [rax + 0]
  trycont#3:
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rcx, [r8 + 8]
    x64.cmpRegImm32 rcx, 1
    x64.jcc belowEqual, __im_slow#18
    x64.jmp __im_load#19
  tryerr#5:
    x64.movRegImm32 rcx, 0
    x64.jmp trycont#6
  __im_load#19:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 0]
    x64.loadRegBaseDisp.word16 rcx, [rcx + 2]
  trycont#6:
    x64.movRegImm32 rdx, 2
    x64.loadRegBaseDisp.word64 rsi, [r8 + 8]
    x64.cmpRegImm32 rsi, 2
    x64.jcc belowEqual, __im_slow#21
    x64.jmp __im_load#22
  tryerr#8:
    x64.movRegImm32 rdx, 0
    x64.jmp trycont#9
  __im_load#22:
    x64.loadRegBaseDisp.word64 rdx, [r8 + 0]
    x64.loadRegBaseDisp.word16 rdx, [rdx + 4]
  trycont#9:
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRegReg rbx, rax, rdx
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 56
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#15:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#2
    x64.jmp trycont#3
  __im_slow#18:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rcx, r8
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#5
    x64.jmp trycont#6
  __im_slow#21:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot2, rcx
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rdx, r8
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot rcx, slot2
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#8
    x64.jmp trycont#9
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at short-type.u16-rdata.test:10: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
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
    arm64.storeSlotReg slot3, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 2
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 0
    arm64.b.ls __im_slow#15
    arm64.b __im_load#16
  tryerr#2:
    arm64.movImm x1, 0
    arm64.b trycont#3
  __im_load#16:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word16 x1, [x1 + 0]
  trycont#3:
    arm64.movImm x2, 1
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 1
    arm64.b.ls __im_slow#18
    arm64.b __im_load#19
  tryerr#5:
    arm64.movImm x2, 0
    arm64.b trycont#6
  __im_load#19:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word16 x2, [x2 + 2]
  trycont#6:
    arm64.movImm x3, 2
    arm64.loadRegBaseDisp.word64 x4, [x0 + 8]
    arm64.cmp x4, 2
    arm64.b.ls __im_slow#21
    arm64.b __im_load#22
  tryerr#8:
    arm64.movImm x3, 0
    arm64.b trycont#9
  __im_load#22:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.loadRegBaseDisp.word16 x3, [x3 + 4]
  trycont#9:
    arm64.add x1, x1, x2
    arm64.add x19, x1, x3
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot3
    arm64.epilogue 48
    arm64.ret
  __im_slow#15:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#2
    arm64.b trycont#3
  __im_slow#18:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x1, x2
    arm64.bl __managed_get
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#5
    arm64.b trycont#6
  __im_slow#21:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.movRegReg x1, x3
    arm64.bl __managed_get
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.cmp x9, 0
    arm64.b.ne tryerr#8
    arm64.b trycont#9
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at short-type.u16-rdata.test:10: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.epilogue 48
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 2
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 0
    arm64.b.ls __im_slow#15
    arm64.b __im_load#16
  tryerr#2:
    arm64.movImm x1, 0
    arm64.b trycont#3
  __im_load#16:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word16 x1, [x1 + 0]
  trycont#3:
    arm64.movImm x2, 1
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 1
    arm64.b.ls __im_slow#18
    arm64.b __im_load#19
  tryerr#5:
    arm64.movImm x2, 0
    arm64.b trycont#6
  __im_load#19:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word16 x2, [x2 + 2]
  trycont#6:
    arm64.movImm x3, 2
    arm64.loadRegBaseDisp.word64 x4, [x0 + 8]
    arm64.cmp x4, 2
    arm64.b.ls __im_slow#21
    arm64.b __im_load#22
  tryerr#8:
    arm64.movImm x3, 0
    arm64.b trycont#9
  __im_load#22:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.loadRegBaseDisp.word16 x3, [x3 + 4]
  trycont#9:
    arm64.add x1, x1, x2
    arm64.add x19, x1, x3
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot3
    arm64.epilogue 48
    arm64.ret
  __im_slow#15:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#2
    arm64.b trycont#3
  __im_slow#18:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x1, x2
    arm64.bl __managed_get
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#5
    arm64.b trycont#6
  __im_slow#21:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.movRegReg x1, x3
    arm64.bl __managed_get
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.cmp x9, 0
    arm64.b.ne tryerr#8
    arm64.b trycont#9
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at short-type.u16-rdata.test:10: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
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
  __mrt_hwcap_atomics@24 = i64 0
  __mrt_program_started@32 = i8 0
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
