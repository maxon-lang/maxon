---
feature: codegen-internals
status: selfhosted
keywords: [rdata, cow, managed-memory, strings, stack-probing, signedness, width, i32, f32]
category: dev
---

# codegen-internals: the cases that pin emitted code

The cases of `specs/codegen-internals.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: rdata-constant-array-uses-rdata -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	return try arr.get(1) otherwise 0
end 'main'
```
```exitcode
20
```
```RequiredRdata
i64[] 10, 20, 30
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
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow
    x64.jmp __im_load
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.leaRegRegImm32 rax, rax, 8
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  tryok:
    x64.movRegReg rbx, rax
  trycont:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg rbx, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at rdata-constant-array-uses-rdata.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
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
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow
    x64.jmp __im_load
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.leaRegRegImm32 rax, rax, 8
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  tryok:
    x64.movRegReg rbx, rax
  trycont:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at rdata-constant-array-uses-rdata.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 8
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 1
    arm64.b.ls __im_slow
    arm64.b __im_load
  tryerr:
    arm64.movImm x19, 0
    arm64.b trycont
  __im_load:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 8
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
  tryok:
    arm64.movRegReg x19, x1
  trycont:
    arm64.bl __managed_decref
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at rdata-constant-array-uses-rdata.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 8
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 1
    arm64.b.ls __im_slow
    arm64.b __im_load
  tryerr:
    arm64.movImm x19, 0
    arm64.b trycont
  __im_load:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 8
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
  tryok:
    arm64.movRegReg x19, x1
  trycont:
    arm64.bl __managed_decref
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at rdata-constant-array-uses-rdata.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
}
```

<!-- test: rdata-bool-array-bit-packed -->
```maxon
function main() returns ExitCode
	let arr = [true, false, true, false]
	let v0 = try arr.get(0) otherwise false
	let v1 = try arr.get(1) otherwise true
	let v2 = try arr.get(2) otherwise false
	let v3 = try arr.get(3) otherwise true
	var sum = 0
	if v0 'c0'
		sum = sum + 1
	end 'c0'
	if v1 'c1'
		sum = sum + 1
	end 'c1'
	if v2 'c2'
		sum = sum + 1
	end 'c2'
	if v3 'c3'
		sum = sum + 1
	end 'c3'
	return sum
end 'main'
```
```exitcode
2
```
```RequiredRdata
i8[] 5
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
    x64.pushReg r14
    x64.prologue 32
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [rbx + 0], rax
    x64.movRegImm32 rax, 4
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm rax, 18446744073709551615
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
    x64.movRegImm32 r13, 1
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
    x64.movRegImm32 r14, 0
    x64.jmp trycont#9
  tryok#7:
    x64.movRegReg r14, r8
  trycont#9:
    x64.movRegImm32 rdx, 3
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#10
  tryerr#11:
    x64.movRegImm32 rax, 1
    x64.jmp trycont#12
  tryok#10:
    x64.movRegReg rax, r8
  trycont#12:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, ifcont#14
  c0:
    x64.movRegImm32 rcx, 1
  ifcont#14:
    x64.cmpRegImm32 r13, 0
    x64.jcc equal, ifcont#16
  c1:
    x64.leaRegRegImm32 rcx, rcx, 1
  ifcont#16:
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, ifcont#18
  c2:
    x64.leaRegRegImm32 rcx, rcx, 1
  ifcont#18:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit
  c3:
    x64.leaRegRegImm32 r12, rcx, 1
    x64.jmp ifcont#20
  critsplit:
    x64.movRegReg r12, rcx
  ifcont#20:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
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
    x64.pushReg r14
    x64.prologue 32
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [rbx + 0], rax
    x64.movRegImm32 rax, 4
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm rax, 18446744073709551615
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
    x64.movRegImm32 r13, 1
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
    x64.movRegImm32 r14, 0
    x64.jmp trycont#9
  tryok#7:
    x64.movRegReg r14, r8
  trycont#9:
    x64.movRegImm32 rdx, 3
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#10
  tryerr#11:
    x64.movRegImm32 rax, 1
    x64.jmp trycont#12
  tryok#10:
    x64.movRegReg rax, r8
  trycont#12:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, ifcont#14
  c0:
    x64.movRegImm32 rcx, 1
  ifcont#14:
    x64.cmpRegImm32 r13, 0
    x64.jcc equal, ifcont#16
  c1:
    x64.leaRegRegImm32 rcx, rcx, 1
  ifcont#16:
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, ifcont#18
  c2:
    x64.leaRegRegImm32 rcx, rcx, 1
  ifcont#18:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit
  c3:
    x64.leaRegRegImm32 r12, rcx, 1
    x64.jmp ifcont#20
  critsplit:
    x64.movRegReg r12, rcx
  ifcont#20:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r14
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
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.movImm x0, 4
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 18446744073709551615
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
    arm64.movImm x21, 1
    arm64.b trycont#6
  tryok#4:
    arm64.movRegReg x21, x0
  trycont#6:
    arm64.movImm x1, 2
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok#7
  tryerr#8:
    arm64.movImm x22, 0
    arm64.b trycont#9
  tryok#7:
    arm64.movRegReg x22, x0
  trycont#9:
    arm64.movImm x1, 3
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq trycont#12
  tryerr#11:
    arm64.movImm x0, 1
  trycont#12:
    arm64.movImm x1, 0
    arm64.cbz x20, ifcont#14
  c0:
    arm64.movImm x1, 1
  ifcont#14:
    arm64.cbz x21, ifcont#16
  c1:
    arm64.add x1, x1, 1
  ifcont#16:
    arm64.cbz x22, ifcont#18
  c2:
    arm64.add x1, x1, 1
  ifcont#18:
    arm64.cbz x0, critsplit
  c3:
    arm64.add x20, x1, 1
    arm64.b ifcont#20
  critsplit:
    arm64.movRegReg x20, x1
  ifcont#20:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
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
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.movImm x0, 4
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 18446744073709551615
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
    arm64.movImm x21, 1
    arm64.b trycont#6
  tryok#4:
    arm64.movRegReg x21, x0
  trycont#6:
    arm64.movImm x1, 2
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok#7
  tryerr#8:
    arm64.movImm x22, 0
    arm64.b trycont#9
  tryok#7:
    arm64.movRegReg x22, x0
  trycont#9:
    arm64.movImm x1, 3
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq trycont#12
  tryerr#11:
    arm64.movImm x0, 1
  trycont#12:
    arm64.movImm x1, 0
    arm64.cbz x20, ifcont#14
  c0:
    arm64.movImm x1, 1
  ifcont#14:
    arm64.cbz x21, ifcont#16
  c1:
    arm64.add x1, x1, 1
  ifcont#16:
    arm64.cbz x22, ifcont#18
  c2:
    arm64.add x1, x1, 1
  ifcont#18:
    arm64.cbz x0, critsplit
  c3:
    arm64.add x20, x1, 1
    arm64.b ifcont#20
  critsplit:
    arm64.movRegReg x20, x1
  ifcont#20:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: rdata-byte-array-uses-i8 -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let arr = [10 as Byte, 20 as Byte, 30 as Byte]
	let v0 = try arr.get(0) otherwise 0 as Byte
	let v1 = try arr.get(1) otherwise 0 as Byte
	let v2 = try arr.get(2) otherwise 0 as Byte
	return v0 + v1 + v2
end 'main'
```
```exitcode
60
```
```RequiredRdata
i8[] 10, 20, 30
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
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride#18
  __im_word#16:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#15
  __im_load#20:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.jmp tryok#1
  __im_stride#18:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#15
  __im_byte#17:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#15
  __im_load#21:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok#1
  __im_slow#15:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit#38
  tryerr#2:
    x64.movRegImm32 r12, 0
    x64.jmp trycont#3
  critsplit#38:
    x64.movRegReg rax, r8
  tryok#1:
    x64.movRegReg r12, rax
  trycont#3:
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride#26
  __im_word#24:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow#23
  __im_load#28:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegImm32 rax, rax, 8
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.jmp tryok#4
  __im_stride#26:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#23
  __im_byte#25:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow#23
  __im_load#29:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok#4
  __im_slow#23:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit#39
  tryerr#5:
    x64.movRegImm32 r13, 0
    x64.jmp trycont#6
  critsplit#39:
    x64.movRegReg rax, r8
  tryok#4:
    x64.movRegReg r13, rax
  trycont#6:
    x64.movRegImm32 rdx, 2
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride#34
  __im_word#32:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 2
    x64.jcc belowEqual, __im_slow#31
  __im_load#36:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegImm32 rax, rax, 16
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.jmp trycont#9
  __im_stride#34:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#31
  __im_byte#33:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 2
    x64.jcc belowEqual, __im_slow#31
  __im_load#37:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegImm32 rax, rax, 2
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp trycont#9
  __im_slow#31:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit#40
  tryerr#8:
    x64.movRegImm32 rax, 0
    x64.jmp trycont#9
  critsplit#40:
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
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at rdata-byte-array-uses-i8.test:10: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride#18
  __im_word#16:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#15
  __im_load#20:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.jmp tryok#1
  __im_stride#18:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#15
  __im_byte#17:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#15
  __im_load#21:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok#1
  __im_slow#15:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit#38
  tryerr#2:
    x64.movRegImm32 r12, 0
    x64.jmp trycont#3
  critsplit#38:
    x64.movRegReg rax, r8
  tryok#1:
    x64.movRegReg r12, rax
  trycont#3:
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride#26
  __im_word#24:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow#23
  __im_load#28:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegImm32 rax, rax, 8
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.jmp tryok#4
  __im_stride#26:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#23
  __im_byte#25:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow#23
  __im_load#29:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok#4
  __im_slow#23:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit#39
  tryerr#5:
    x64.movRegImm32 r13, 0
    x64.jmp trycont#6
  critsplit#39:
    x64.movRegReg rax, r8
  tryok#4:
    x64.movRegReg r13, rax
  trycont#6:
    x64.movRegImm32 rdx, 2
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride#34
  __im_word#32:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 2
    x64.jcc belowEqual, __im_slow#31
  __im_load#36:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegImm32 rax, rax, 16
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.jmp trycont#9
  __im_stride#34:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#31
  __im_byte#33:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 2
    x64.jcc belowEqual, __im_slow#31
  __im_load#37:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegImm32 rax, rax, 2
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp trycont#9
  __im_slow#31:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit#40
  tryerr#8:
    x64.movRegImm32 rax, 0
    x64.jmp trycont#9
  critsplit#40:
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
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at rdata-byte-array-uses-i8.test:10: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride#18
  __im_word#16:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow#15
  __im_load#20:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b tryok#1
  __im_stride#18:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#15
  __im_byte#17:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow#15
  __im_load#21:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok#1
  __im_slow#15:
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
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride#26
  __im_word#24:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 1
    arm64.b.ls __im_slow#23
  __im_load#28:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x0, x0, 8
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b tryok#4
  __im_stride#26:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#23
  __im_byte#25:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 1
    arm64.b.ls __im_slow#23
  __im_load#29:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x0, x0, 1
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok#4
  __im_slow#23:
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
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride#34
  __im_word#32:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 2
    arm64.b.ls __im_slow#31
  __im_load#36:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x0, x0, 16
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b trycont#9
  __im_stride#34:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#31
  __im_byte#33:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 2
    arm64.b.ls __im_slow#31
  __im_load#37:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x0, x0, 2
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b trycont#9
  __im_slow#31:
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
    arm64.leaRdata x0, __str_blob_2  ; "panic at rdata-byte-array-uses-i8.test:10: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride#18
  __im_word#16:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow#15
  __im_load#20:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b tryok#1
  __im_stride#18:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#15
  __im_byte#17:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow#15
  __im_load#21:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok#1
  __im_slow#15:
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
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride#26
  __im_word#24:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 1
    arm64.b.ls __im_slow#23
  __im_load#28:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x0, x0, 8
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b tryok#4
  __im_stride#26:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#23
  __im_byte#25:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 1
    arm64.b.ls __im_slow#23
  __im_load#29:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x0, x0, 1
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok#4
  __im_slow#23:
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
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride#34
  __im_word#32:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 2
    arm64.b.ls __im_slow#31
  __im_load#36:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x0, x0, 16
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b trycont#9
  __im_stride#34:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#31
  __im_byte#33:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 2
    arm64.b.ls __im_slow#31
  __im_load#37:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x0, x0, 2
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b trycont#9
  __im_slow#31:
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
    arm64.leaRdata x0, __str_blob_2  ; "panic at rdata-byte-array-uses-i8.test:10: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: rdata-typealias-byte-array-uses-i8 -->
```maxon

typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let arr = ByteArray from [10, 20, 30]
	let v0 = try arr.get(0) otherwise 0 as Byte
	let v1 = try arr.get(1) otherwise 0 as Byte
	let v2 = try arr.get(2) otherwise 0 as Byte
	return v0 + v1 + v2
end 'main'
```
```exitcode
60
```
```RequiredRdata
i8[] 10, 20, 30
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
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride#18
  __im_word#16:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#15
  __im_load#20:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.jmp tryok#1
  __im_stride#18:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#15
  __im_byte#17:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#15
  __im_load#21:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok#1
  __im_slow#15:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit#38
  tryerr#2:
    x64.movRegImm32 r12, 0
    x64.jmp trycont#3
  critsplit#38:
    x64.movRegReg rax, r8
  tryok#1:
    x64.movRegReg r12, rax
  trycont#3:
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride#26
  __im_word#24:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow#23
  __im_load#28:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegImm32 rax, rax, 8
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.jmp tryok#4
  __im_stride#26:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#23
  __im_byte#25:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow#23
  __im_load#29:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok#4
  __im_slow#23:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit#39
  tryerr#5:
    x64.movRegImm32 r13, 0
    x64.jmp trycont#6
  critsplit#39:
    x64.movRegReg rax, r8
  tryok#4:
    x64.movRegReg r13, rax
  trycont#6:
    x64.movRegImm32 rdx, 2
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride#34
  __im_word#32:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 2
    x64.jcc belowEqual, __im_slow#31
  __im_load#36:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegImm32 rax, rax, 16
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.jmp trycont#9
  __im_stride#34:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#31
  __im_byte#33:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 2
    x64.jcc belowEqual, __im_slow#31
  __im_load#37:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegImm32 rax, rax, 2
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp trycont#9
  __im_slow#31:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit#40
  tryerr#8:
    x64.movRegImm32 rax, 0
    x64.jmp trycont#9
  critsplit#40:
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
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at rdata-typealias-byte-array-uses-i8.test:11: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride#18
  __im_word#16:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#15
  __im_load#20:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.jmp tryok#1
  __im_stride#18:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#15
  __im_byte#17:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#15
  __im_load#21:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok#1
  __im_slow#15:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit#38
  tryerr#2:
    x64.movRegImm32 r12, 0
    x64.jmp trycont#3
  critsplit#38:
    x64.movRegReg rax, r8
  tryok#1:
    x64.movRegReg r12, rax
  trycont#3:
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride#26
  __im_word#24:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow#23
  __im_load#28:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegImm32 rax, rax, 8
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.jmp tryok#4
  __im_stride#26:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#23
  __im_byte#25:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow#23
  __im_load#29:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok#4
  __im_slow#23:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit#39
  tryerr#5:
    x64.movRegImm32 r13, 0
    x64.jmp trycont#6
  critsplit#39:
    x64.movRegReg rax, r8
  tryok#4:
    x64.movRegReg r13, rax
  trycont#6:
    x64.movRegImm32 rdx, 2
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride#34
  __im_word#32:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 2
    x64.jcc belowEqual, __im_slow#31
  __im_load#36:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegImm32 rax, rax, 16
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.jmp trycont#9
  __im_stride#34:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#31
  __im_byte#33:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 2
    x64.jcc belowEqual, __im_slow#31
  __im_load#37:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegImm32 rax, rax, 2
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp trycont#9
  __im_slow#31:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit#40
  tryerr#8:
    x64.movRegImm32 rax, 0
    x64.jmp trycont#9
  critsplit#40:
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
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at rdata-typealias-byte-array-uses-i8.test:11: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride#18
  __im_word#16:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow#15
  __im_load#20:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b tryok#1
  __im_stride#18:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#15
  __im_byte#17:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow#15
  __im_load#21:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok#1
  __im_slow#15:
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
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride#26
  __im_word#24:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 1
    arm64.b.ls __im_slow#23
  __im_load#28:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x0, x0, 8
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b tryok#4
  __im_stride#26:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#23
  __im_byte#25:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 1
    arm64.b.ls __im_slow#23
  __im_load#29:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x0, x0, 1
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok#4
  __im_slow#23:
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
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride#34
  __im_word#32:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 2
    arm64.b.ls __im_slow#31
  __im_load#36:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x0, x0, 16
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b trycont#9
  __im_stride#34:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#31
  __im_byte#33:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 2
    arm64.b.ls __im_slow#31
  __im_load#37:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x0, x0, 2
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b trycont#9
  __im_slow#31:
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
    arm64.leaRdata x0, __str_blob_2  ; "panic at rdata-typealias-byte-array-uses-i8.test:11: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride#18
  __im_word#16:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow#15
  __im_load#20:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b tryok#1
  __im_stride#18:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#15
  __im_byte#17:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow#15
  __im_load#21:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok#1
  __im_slow#15:
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
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride#26
  __im_word#24:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 1
    arm64.b.ls __im_slow#23
  __im_load#28:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x0, x0, 8
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b tryok#4
  __im_stride#26:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#23
  __im_byte#25:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 1
    arm64.b.ls __im_slow#23
  __im_load#29:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x0, x0, 1
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok#4
  __im_slow#23:
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
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride#34
  __im_word#32:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 2
    arm64.b.ls __im_slow#31
  __im_load#36:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x0, x0, 16
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b trycont#9
  __im_stride#34:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#31
  __im_byte#33:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 2
    arm64.b.ls __im_slow#31
  __im_load#37:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x0, x0, 2
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b trycont#9
  __im_slow#31:
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
    arm64.leaRdata x0, __str_blob_2  ; "panic at rdata-typealias-byte-array-uses-i8.test:11: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: rdata-cow-mutation-copies-to-heap -->
```maxon
function main() returns ExitCode
	var arr = [42]
	try arr.set(0, value: 77) otherwise panic("test invariant: set OOB")
	return try arr.get(0) otherwise 0
end 'main'
```
```exitcode
77
```
```RequiredRdata
i64 42
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
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 77
    x64.loadRegBaseDisp.word64 rcx, [r8 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc belowEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.storeBaseDispReg.word64 [rcx + 0], rax
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
  trycont:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg rbx, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_set
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, __im_load
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at rdata-cow-mutation-copies-to-heap.test:4: test invariant: set OOB\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at rdata-cow-mutation-copies-to-heap.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
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
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 77
    x64.loadRegBaseDisp.word64 rcx, [r8 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc belowEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.storeBaseDispReg.word64 [rcx + 0], rax
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
  trycont:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_set
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, __im_load
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at rdata-cow-mutation-copies-to-heap.test:4: test invariant: set OOB\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at rdata-cow-mutation-copies-to-heap.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 8
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 0
    arm64.movImm x2, 77
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 0
    arm64.b.ls __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x4, x3, 24
    arm64.loadRegBaseDisp.word64 x4, [x4 + 16]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.storeBaseDispReg.word64 [x3 + 0], x2
  __im_load:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x19, [x1 + 0]
  trycont:
    arm64.bl __managed_decref
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_set
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.eq __im_load
  tryerr:
    arm64.leaRdata x0, __str_blob_1  ; "panic at rdata-cow-mutation-copies-to-heap.test:4: test invariant: set OOB\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at rdata-cow-mutation-copies-to-heap.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 8
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 0
    arm64.movImm x2, 77
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 0
    arm64.b.ls __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x4, x3, 24
    arm64.loadRegBaseDisp.word64 x4, [x4 + 16]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.storeBaseDispReg.word64 [x3 + 0], x2
  __im_load:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x19, [x1 + 0]
  trycont:
    arm64.bl __managed_decref
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_set
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.eq __im_load
  tryerr:
    arm64.leaRdata x0, __str_blob_1  ; "panic at rdata-cow-mutation-copies-to-heap.test:4: test invariant: set OOB\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at rdata-cow-mutation-copies-to-heap.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
}
```

<!-- test: rdata-cow-multiple-mutations -->
```maxon
function main() returns ExitCode
	var arr = [1, 2, 3]
	try arr.set(0, value: 10) otherwise panic("test invariant: set OOB")
	try arr.set(1, value: 20) otherwise panic("test invariant: set OOB")
	try arr.set(2, value: 30) otherwise panic("test invariant: set OOB")
	var sum = 0
	sum = sum + (try arr.get(0) otherwise 0)
	sum = sum + (try arr.get(1) otherwise 0)
	sum = sum + (try arr.get(2) otherwise 0)
	return sum
end 'main'
```
```exitcode
60
```
```RequiredRdata
i64[] 1, 2, 3
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
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 10
    x64.loadRegBaseDisp.word64 rcx, [r8 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#21
  __im_bounds#22:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc belowEqual, __im_slow#21
  __im_buffer#23:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#21
  __im_viewed:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#21
  __im_store#25:
    x64.storeBaseDispReg.word64 [rcx + 0], rax
  tryok#1:
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 20
  __im_bounds#28:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 8]
    x64.cmpRegImm32 rcx, 1
    x64.jcc belowEqual, __im_slow#27
  __im_buffer#29:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 0]
  __im_store#31:
    x64.leaRegRegImm32 rcx, rcx, 8
    x64.storeBaseDispReg.word64 [rcx + 0], rax
  tryok#3:
    x64.movRegImm32 rdx, 2
    x64.movRegImm32 rax, 30
  __im_bounds#34:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 8]
    x64.cmpRegImm32 rcx, 2
    x64.jcc belowEqual, __im_slow#33
  __im_buffer#35:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 0]
  __im_store#37:
    x64.leaRegRegImm32 rcx, rcx, 16
    x64.storeBaseDispReg.word64 [rcx + 0], rax
  __im_load#40:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  __im_load#43:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 0]
    x64.leaRegRegImm32 rcx, rcx, 8
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
  trycont#12:
    x64.leaRegRegReg rax, rax, rcx
  __im_load#46:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 0]
    x64.leaRegRegImm32 rcx, rcx, 16
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
  trycont#15:
    x64.leaRegRegReg rbx, rax, rcx
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg rbx, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#21:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_set
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#1
  tryerr#2:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at rdata-cow-multiple-mutations.test:4: test invariant: set OOB\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#27:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_set
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#3
  tryerr#4:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at rdata-cow-multiple-mutations.test:5: test invariant: set OOB\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#33:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_set
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, __im_load#40
  tryerr#6:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at rdata-cow-multiple-mutations.test:6: test invariant: set OOB\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at rdata-cow-multiple-mutations.test:11: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
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
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 10
    x64.loadRegBaseDisp.word64 rcx, [r8 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#21
  __im_bounds#22:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc belowEqual, __im_slow#21
  __im_buffer#23:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#21
  __im_viewed:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#21
  __im_store#25:
    x64.storeBaseDispReg.word64 [rcx + 0], rax
  tryok#1:
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 20
  __im_bounds#28:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 8]
    x64.cmpRegImm32 rcx, 1
    x64.jcc belowEqual, __im_slow#27
  __im_buffer#29:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 0]
  __im_store#31:
    x64.leaRegRegImm32 rcx, rcx, 8
    x64.storeBaseDispReg.word64 [rcx + 0], rax
  tryok#3:
    x64.movRegImm32 rdx, 2
    x64.movRegImm32 rax, 30
  __im_bounds#34:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 8]
    x64.cmpRegImm32 rcx, 2
    x64.jcc belowEqual, __im_slow#33
  __im_buffer#35:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 0]
  __im_store#37:
    x64.leaRegRegImm32 rcx, rcx, 16
    x64.storeBaseDispReg.word64 [rcx + 0], rax
  __im_load#40:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  __im_load#43:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 0]
    x64.leaRegRegImm32 rcx, rcx, 8
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
  trycont#12:
    x64.leaRegRegReg rax, rax, rcx
  __im_load#46:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 0]
    x64.leaRegRegImm32 rcx, rcx, 16
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
  trycont#15:
    x64.leaRegRegReg rbx, rax, rcx
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#21:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_set
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#1
  tryerr#2:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at rdata-cow-multiple-mutations.test:4: test invariant: set OOB\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#27:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_set
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#3
  tryerr#4:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at rdata-cow-multiple-mutations.test:5: test invariant: set OOB\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#33:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_set
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, __im_load#40
  tryerr#6:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at rdata-cow-multiple-mutations.test:6: test invariant: set OOB\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at rdata-cow-multiple-mutations.test:11: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 8
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 0
    arm64.movImm x2, 10
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow#21
  __im_bounds#22:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 0
    arm64.b.ls __im_slow#21
  __im_buffer#23:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow#21
  __im_viewed:
    arm64.sub x4, x3, 24
    arm64.loadRegBaseDisp.word64 x4, [x4 + 16]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow#21
  __im_store#25:
    arm64.storeBaseDispReg.word64 [x3 + 0], x2
  tryok#1:
    arm64.movImm x1, 1
    arm64.movImm x2, 20
  __im_bounds#28:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 1
    arm64.b.ls __im_slow#27
  __im_buffer#29:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
  __im_store#31:
    arm64.add x1, x1, 8
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
  tryok#3:
    arm64.movImm x1, 2
    arm64.movImm x2, 30
  __im_bounds#34:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 2
    arm64.b.ls __im_slow#33
  __im_buffer#35:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
  __im_store#37:
    arm64.add x1, x1, 16
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
  __im_load#40:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
  __im_load#43:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.add x2, x2, 8
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
  trycont#12:
    arm64.add x1, x1, x2
  __im_load#46:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.add x2, x2, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
  trycont#15:
    arm64.add x19, x1, x2
    arm64.bl __managed_decref
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow#21:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_set
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.eq tryok#1
  tryerr#2:
    arm64.leaRdata x0, __str_blob_1  ; "panic at rdata-cow-multiple-mutations.test:4: test invariant: set OOB\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow#27:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_set
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.eq tryok#3
  tryerr#4:
    arm64.leaRdata x0, __str_blob_2  ; "panic at rdata-cow-multiple-mutations.test:5: test invariant: set OOB\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow#33:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_set
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.eq __im_load#40
  tryerr#6:
    arm64.leaRdata x0, __str_blob_3  ; "panic at rdata-cow-multiple-mutations.test:6: test invariant: set OOB\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_5  ; "panic at rdata-cow-multiple-mutations.test:11: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 8
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 0
    arm64.movImm x2, 10
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow#21
  __im_bounds#22:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 0
    arm64.b.ls __im_slow#21
  __im_buffer#23:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow#21
  __im_viewed:
    arm64.sub x4, x3, 24
    arm64.loadRegBaseDisp.word64 x4, [x4 + 16]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow#21
  __im_store#25:
    arm64.storeBaseDispReg.word64 [x3 + 0], x2
  tryok#1:
    arm64.movImm x1, 1
    arm64.movImm x2, 20
  __im_bounds#28:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 1
    arm64.b.ls __im_slow#27
  __im_buffer#29:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
  __im_store#31:
    arm64.add x1, x1, 8
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
  tryok#3:
    arm64.movImm x1, 2
    arm64.movImm x2, 30
  __im_bounds#34:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 2
    arm64.b.ls __im_slow#33
  __im_buffer#35:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
  __im_store#37:
    arm64.add x1, x1, 16
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
  __im_load#40:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
  __im_load#43:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.add x2, x2, 8
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
  trycont#12:
    arm64.add x1, x1, x2
  __im_load#46:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.add x2, x2, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
  trycont#15:
    arm64.add x19, x1, x2
    arm64.bl __managed_decref
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow#21:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_set
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.eq tryok#1
  tryerr#2:
    arm64.leaRdata x0, __str_blob_1  ; "panic at rdata-cow-multiple-mutations.test:4: test invariant: set OOB\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow#27:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_set
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.eq tryok#3
  tryerr#4:
    arm64.leaRdata x0, __str_blob_2  ; "panic at rdata-cow-multiple-mutations.test:5: test invariant: set OOB\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow#33:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_set
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.eq __im_load#40
  tryerr#6:
    arm64.leaRdata x0, __str_blob_3  ; "panic at rdata-cow-multiple-mutations.test:6: test invariant: set OOB\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_5  ; "panic at rdata-cow-multiple-mutations.test:11: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
}
```

<!-- test: rdata-global-let-array-uses-rdata -->
```maxon
let globalArr = [10, 20, 30]

function main() returns ExitCode
	return try globalArr.get(1) otherwise 0
end 'main'
```
```exitcode
20
```
```RequiredRdata
i64[] 10, 20, 30
```

```TargetIr:x64-windows
data {
  __slab_state@0 = i64 0
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
    x64.leaRegRdata rcx, [rip + __barr_rec_1]
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow
    x64.jmp __im_load
  tryerr:
    x64.movRegImm32 r8, 0
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.leaRegRegImm32 rax, rax, 8
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.movRegReg r8, rax
  trycont:
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
  __im_slow:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp trycont
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at rdata-global-let-array-uses-rdata.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegRdata rcx, [rip + __barr_rec_1]
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow
    x64.jmp __im_load
  tryerr:
    x64.movRegImm32 r8, 0
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.leaRegRegImm32 rax, rax, 8
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.movRegReg r8, rax
  trycont:
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp trycont
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at rdata-global-let-array-uses-rdata.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __slab_state@0 = i64 0
  __mrt_program_started@8 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaRdata x0, __barr_rec_1
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 1
    arm64.b.ls __im_slow
    arm64.b __im_load
  tryerr:
    arm64.movImm x0, 0
    arm64.b trycont
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x0, x0, 8
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  trycont:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __im_slow:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b trycont
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at rdata-global-let-array-uses-rdata.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaRdata x0, __barr_rec_1
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 1
    arm64.b.ls __im_slow
    arm64.b __im_load
  tryerr:
    arm64.movImm x0, 0
    arm64.b trycont
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x0, x0, 8
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  trycont:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __im_slow:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b trycont
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at rdata-global-let-array-uses-rdata.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: rdata-global-var-array-cow -->
```maxon
var globalArr = [1, 2, 3]

function main() returns ExitCode
	try globalArr.set(0, value: 42) otherwise panic("test invariant: set OOB")
	return try globalArr.get(0) otherwise 0
end 'main'
```
```exitcode
42
```
```RequiredRdata
i64[] 1, 2, 3
```

```TargetIr:x64-windows
data {
  __data_globalArr@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_console_probe_stdin@32 = i8 0
  __mrt_console_probe_stdout@33 = i8 0
  __mrt_console_probe_stderr@34 = i8 0
  __mrt_program_started@35 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_globalArr
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 42
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_slow#10
  __im_bounds:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc belowEqual, __im_slow#10
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_slow#10
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -24
    x64.loadRegBaseDisp.word64 rdi, [rdi + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_slow#10
  __im_store:
    x64.storeBaseDispReg.word64 [rsi + 0], rax
  tryok:
    x64.leaRegGlobal rax, __data_globalArr
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#16
    x64.jmp __im_load
  tryerr#4:
    x64.movRegImm32 r8, 0
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.movRegReg r8, rax
  trycont:
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
  __im_slow#10:
    x64.callDirect __managed_set
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr#2:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at rdata-global-var-array-cow.test:5: test invariant: set OOB\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow#16:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#4
    x64.jmp trycont
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at rdata-global-var-array-cow.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_globalArr@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_globalArr
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 42
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_slow#10
  __im_bounds:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc belowEqual, __im_slow#10
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_slow#10
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -24
    x64.loadRegBaseDisp.word64 rdi, [rdi + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_slow#10
  __im_store:
    x64.storeBaseDispReg.word64 [rsi + 0], rax
  tryok:
    x64.leaRegGlobal rax, __data_globalArr
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#16
    x64.jmp __im_load
  tryerr#4:
    x64.movRegImm32 r8, 0
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.movRegReg r8, rax
  trycont:
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow#10:
    x64.callDirect __managed_set
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr#2:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at rdata-global-var-array-cow.test:5: test invariant: set OOB\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow#16:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#4
    x64.jmp trycont
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at rdata-global-var-array-cow.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_globalArr@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_globalArr
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.movImm x1, 0
    arm64.movImm x2, 42
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow#10
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 0
    arm64.b.ls __im_slow#10
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow#10
  __im_viewed:
    arm64.sub x4, x3, 24
    arm64.loadRegBaseDisp.word64 x4, [x4 + 16]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow#10
  __im_store:
    arm64.storeBaseDispReg.word64 [x3 + 0], x2
  tryok:
    arm64.leaGlobal x0, __data_globalArr
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 0
    arm64.b.ls __im_slow#16
    arm64.b __im_load
  tryerr#4:
    arm64.movImm x0, 0
    arm64.b trycont
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  trycont:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __im_slow#10:
    arm64.bl __managed_set
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr#2:
    arm64.leaRdata x0, __str_blob_1  ; "panic at rdata-global-var-array-cow.test:5: test invariant: set OOB\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __im_slow#16:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#4
    arm64.b trycont
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at rdata-global-var-array-cow.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_globalArr@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_globalArr
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.movImm x1, 0
    arm64.movImm x2, 42
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow#10
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 0
    arm64.b.ls __im_slow#10
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow#10
  __im_viewed:
    arm64.sub x4, x3, 24
    arm64.loadRegBaseDisp.word64 x4, [x4 + 16]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow#10
  __im_store:
    arm64.storeBaseDispReg.word64 [x3 + 0], x2
  tryok:
    arm64.leaGlobal x0, __data_globalArr
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 0
    arm64.b.ls __im_slow#16
    arm64.b __im_load
  tryerr#4:
    arm64.movImm x0, 0
    arm64.b trycont
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  trycont:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __im_slow#10:
    arm64.bl __managed_set
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr#2:
    arm64.leaRdata x0, __str_blob_1  ; "panic at rdata-global-var-array-cow.test:5: test invariant: set OOB\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __im_slow#16:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#4
    arm64.b trycont
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at rdata-global-var-array-cow.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: rdata-global-var-array-cow-preserves-original -->
```maxon
typealias Integer = int(i64.min to i64.max)

var globalArr = [10, 20, 30]

function readFirst() returns Integer
	return try globalArr.get(0) otherwise 0
end 'readFirst'

function main() returns ExitCode
	try globalArr.set(0, value: 99) otherwise panic("test invariant: set OOB")
	return readFirst()
end 'main'
```
```exitcode
99
```
```RequiredRdata
i64[] 10, 20, 30
```

```TargetIr:x64-windows
data {
  __data_globalArr@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_console_probe_stdin@32 = i8 0
  __mrt_console_probe_stdout@33 = i8 0
  __mrt_console_probe_stderr@34 = i8 0
  __mrt_program_started@35 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_globalArr
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 99
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_slow#7
  __im_bounds:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc belowEqual, __im_slow#7
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_slow#7
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -24
    x64.loadRegBaseDisp.word64 rdi, [rdi + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_slow#7
  __im_store:
    x64.storeBaseDispReg.word64 [rsi + 0], rax
  __il_body:
    x64.leaRegGlobal rax, __data_globalArr
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#18
    x64.jmp __im_load
  tryerr#15:
    x64.movRegImm32 r8, 0
    x64.jmp __il_cont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.movRegReg r8, rax
  __il_cont:
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
  __im_slow#7:
    x64.callDirect __managed_set
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, __il_body
  tryerr#2:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at rdata-global-var-array-cow-preserves-original.test:11: test invariant: set OOB\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow#18:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#15
    x64.jmp __il_cont
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at rdata-global-var-array-cow-preserves-original.test:12: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_globalArr@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_globalArr
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 99
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_slow#7
  __im_bounds:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc belowEqual, __im_slow#7
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_slow#7
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -24
    x64.loadRegBaseDisp.word64 rdi, [rdi + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_slow#7
  __im_store:
    x64.storeBaseDispReg.word64 [rsi + 0], rax
  __il_body:
    x64.leaRegGlobal rax, __data_globalArr
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#18
    x64.jmp __im_load
  tryerr#15:
    x64.movRegImm32 r8, 0
    x64.jmp __il_cont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.movRegReg r8, rax
  __il_cont:
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow#7:
    x64.callDirect __managed_set
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, __il_body
  tryerr#2:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at rdata-global-var-array-cow-preserves-original.test:11: test invariant: set OOB\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow#18:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#15
    x64.jmp __il_cont
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at rdata-global-var-array-cow-preserves-original.test:12: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_globalArr@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_globalArr
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.movImm x1, 0
    arm64.movImm x2, 99
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow#7
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 0
    arm64.b.ls __im_slow#7
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow#7
  __im_viewed:
    arm64.sub x4, x3, 24
    arm64.loadRegBaseDisp.word64 x4, [x4 + 16]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow#7
  __im_store:
    arm64.storeBaseDispReg.word64 [x3 + 0], x2
  __il_body:
    arm64.leaGlobal x0, __data_globalArr
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 0
    arm64.b.ls __im_slow#18
    arm64.b __im_load
  tryerr#15:
    arm64.movImm x0, 0
    arm64.b __il_cont
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __im_slow#7:
    arm64.bl __managed_set
    arm64.cmp x9, 0
    arm64.b.eq __il_body
  tryerr#2:
    arm64.leaRdata x0, __str_blob_1  ; "panic at rdata-global-var-array-cow-preserves-original.test:11: test invariant: set OOB\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __im_slow#18:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#15
    arm64.b __il_cont
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at rdata-global-var-array-cow-preserves-original.test:12: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_globalArr@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_globalArr
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.movImm x1, 0
    arm64.movImm x2, 99
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow#7
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 0
    arm64.b.ls __im_slow#7
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow#7
  __im_viewed:
    arm64.sub x4, x3, 24
    arm64.loadRegBaseDisp.word64 x4, [x4 + 16]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow#7
  __im_store:
    arm64.storeBaseDispReg.word64 [x3 + 0], x2
  __il_body:
    arm64.leaGlobal x0, __data_globalArr
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 0
    arm64.b.ls __im_slow#18
    arm64.b __im_load
  tryerr#15:
    arm64.movImm x0, 0
    arm64.b __il_cont
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __im_slow#7:
    arm64.bl __managed_set
    arm64.cmp x9, 0
    arm64.b.eq __il_body
  tryerr#2:
    arm64.leaRdata x0, __str_blob_1  ; "panic at rdata-global-var-array-cow-preserves-original.test:11: test invariant: set OOB\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __im_slow#18:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#15
    arm64.b __il_cont
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at rdata-global-var-array-cow-preserves-original.test:12: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: managed-string-heap-string-generates-cleanup -->
```maxon
function main() returns ExitCode
	let s = "this is a heap allocated string!"
	return s.byteLength()
end 'main'
```
```exitcode
32
```
```RequiredRdata
utf8 "this is a heap allocated string!\0"
```

```TargetIr:x64-windows
data {
  __slab_state@0 = i64 0
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
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "this is a heap allocated string!"
  __il_body:
    x64.loadRegBaseDisp.word64 r8, [rax + 8]
  __il_cont:
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
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at managed-string-heap-string-generates-cleanup.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "this is a heap allocated string!"
  __il_body:
    x64.loadRegBaseDisp.word64 r8, [rax + 8]
  __il_cont:
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
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at managed-string-heap-string-generates-cleanup.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __slab_state@0 = i64 0
  __mrt_program_started@8 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaRdata x0, __str_rec_1  ; "this is a heap allocated string!"
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at managed-string-heap-string-generates-cleanup.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaRdata x0, __str_rec_1  ; "this is a heap allocated string!"
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at managed-string-heap-string-generates-cleanup.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: managed-string-reassignment-handles-old-value -->
```maxon
function main() returns ExitCode
	var s = "first heap allocated value!!"
	s = "second heap allocated here!!"
	return s.byteLength()
end 'main'
```
```exitcode
28
```
```RequiredRdata
utf8 "first heap allocated value!!\0"
utf8 "second heap allocated here!!\0"
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "first heap allocated value!!"
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.leaRegRegImm32 rcx, r12, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.leaRegRegImm32 r14, r13, 56
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r14, r12
    x64.storeBaseDispReg.word64 [r13 + 0], r14
    x64.storeBaseDispReg.word64 [r13 + 8], r12
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r13 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r13 + 24], rax
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "second heap allocated here!!"
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.leaRegRegImm32 rcx, r12, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.leaRegRegImm32 r15, r14, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r15, r12
    x64.storeBaseDispReg.word64 [r14 + 0], r15
    x64.storeBaseDispReg.word64 [r14 + 8], r12
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r14 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r14 + 24], rax
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
  __il_body:
    x64.loadRegBaseDisp.word64 rbx, [r14 + 8]
  __il_cont:
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg rbx, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at managed-string-reassignment-handles-old-value.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "first heap allocated value!!"
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.leaRegRegImm32 rcx, r12, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.leaRegRegImm32 r14, r13, 56
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r14, r12
    x64.storeBaseDispReg.word64 [r13 + 0], r14
    x64.storeBaseDispReg.word64 [r13 + 8], r12
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r13 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r13 + 24], rax
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "second heap allocated here!!"
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.leaRegRegImm32 rcx, r12, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.leaRegRegImm32 r15, r14, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, r15, r12
    x64.storeBaseDispReg.word64 [r14 + 0], r15
    x64.storeBaseDispReg.word64 [r14 + 8], r12
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r14 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r14 + 24], rax
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
  __il_body:
    x64.loadRegBaseDisp.word64 rbx, [r14 + 8]
  __il_cont:
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at managed-string-reassignment-handles-old-value.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
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
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x0, __str_rec_2  ; "first heap allocated value!!"
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x0 + 8]
    arm64.add x0, x20, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.add x22, x21, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x0, x22, x20
    arm64.storeBaseDispReg.word64 [x21 + 0], x22
    arm64.storeBaseDispReg.word64 [x21 + 8], x20
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x21 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x21 + 24], x0
    arm64.leaRdata x0, __str_rec_3  ; "second heap allocated here!!"
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x0 + 8]
    arm64.add x0, x20, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.add x23, x22, 56
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x0, x23, x20
    arm64.storeBaseDispReg.word64 [x22 + 0], x23
    arm64.storeBaseDispReg.word64 [x22 + 8], x20
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x22 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x22 + 24], x0
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  __il_body:
    arm64.loadRegBaseDisp.word64 x19, [x22 + 8]
  __il_cont:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_5  ; "panic at managed-string-reassignment-handles-old-value.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
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
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x0, __str_rec_2  ; "first heap allocated value!!"
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x0 + 8]
    arm64.add x0, x20, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.add x22, x21, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x0, x22, x20
    arm64.storeBaseDispReg.word64 [x21 + 0], x22
    arm64.storeBaseDispReg.word64 [x21 + 8], x20
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x21 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x21 + 24], x0
    arm64.leaRdata x0, __str_rec_3  ; "second heap allocated here!!"
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x0 + 8]
    arm64.add x0, x20, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.add x23, x22, 56
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x0, x23, x20
    arm64.storeBaseDispReg.word64 [x22 + 0], x23
    arm64.storeBaseDispReg.word64 [x22 + 8], x20
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x22 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x22 + 24], x0
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
  __il_body:
    arm64.loadRegBaseDisp.word64 x19, [x22 + 8]
  __il_cont:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x22
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_5  ; "panic at managed-string-reassignment-handles-old-value.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
}
```

<!-- test: managed-string-print-heap-string -->
```maxon
function main() returns ExitCode
	let s = "heap allocated string here!!"
	return s.byteLength()
end 'main'
```
```exitcode
28
```
```RequiredRdata
utf8 "heap allocated string here!!\0"
```

```TargetIr:x64-windows
data {
  __slab_state@0 = i64 0
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
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "heap allocated string here!!"
  __il_body:
    x64.loadRegBaseDisp.word64 r8, [rax + 8]
  __il_cont:
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
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at managed-string-print-heap-string.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "heap allocated string here!!"
  __il_body:
    x64.loadRegBaseDisp.word64 r8, [rax + 8]
  __il_cont:
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
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at managed-string-print-heap-string.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __slab_state@0 = i64 0
  __mrt_program_started@8 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaRdata x0, __str_rec_1  ; "heap allocated string here!!"
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at managed-string-print-heap-string.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaRdata x0, __str_rec_1  ; "heap allocated string here!!"
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at managed-string-print-heap-string.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: managed-string-short-string-sso -->
```maxon
function main() returns ExitCode
	let s = "short"
	return s.byteLength()
end 'main'
```
```exitcode
5
```
```RequiredRdata
utf8 "short\0"
```

```TargetIr:x64-windows
data {
  __slab_state@0 = i64 0
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
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "short"
  __il_body:
    x64.loadRegBaseDisp.word64 r8, [rax + 8]
  __il_cont:
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
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at managed-string-short-string-sso.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "short"
  __il_body:
    x64.loadRegBaseDisp.word64 r8, [rax + 8]
  __il_cont:
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
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at managed-string-short-string-sso.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __slab_state@0 = i64 0
  __mrt_program_started@8 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaRdata x0, __str_rec_1  ; "short"
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at managed-string-short-string-sso.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaRdata x0, __str_rec_1  ; "short"
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at managed-string-short-string-sso.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: managed-string-literal-deduplication -->
```maxon
function main() returns ExitCode
	let a = "hello world"
	let b = "hello world"
	let c = "hello world"
	return a.byteLength() + b.byteLength() + c.byteLength()
end 'main'
```
```exitcode
33
```
```RequiredRdata
utf8 "hello world\0"
```

```TargetIr:x64-windows
data {
  __slab_state@0 = i64 0
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
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "hello world"
    x64.leaRegRdata rcx, [rip + __str_rec_1]  ; "hello world"
    x64.leaRegRdata rdx, [rip + __str_rec_1]  ; "hello world"
  __il_body#7:
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
  __il_body#8:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
  __il_cont#5:
    x64.leaRegRegReg rax, rax, rcx
  __il_body#9:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 8]
  __il_cont#4:
    x64.leaRegRegReg r8, rax, rcx
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
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at managed-string-literal-deduplication.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "hello world"
    x64.leaRegRdata rcx, [rip + __str_rec_1]  ; "hello world"
    x64.leaRegRdata rdx, [rip + __str_rec_1]  ; "hello world"
  __il_body#7:
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
  __il_body#8:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
  __il_cont#5:
    x64.leaRegRegReg rax, rax, rcx
  __il_body#9:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 8]
  __il_cont#4:
    x64.leaRegRegReg r8, rax, rcx
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
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at managed-string-literal-deduplication.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __slab_state@0 = i64 0
  __mrt_program_started@8 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaRdata x0, __str_rec_1  ; "hello world"
    arm64.leaRdata x1, __str_rec_1  ; "hello world"
    arm64.leaRdata x2, __str_rec_1  ; "hello world"
  __il_body#7:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_body#8:
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
  __il_cont#5:
    arm64.add x0, x0, x1
  __il_body#9:
    arm64.loadRegBaseDisp.word64 x1, [x2 + 8]
  __il_cont#4:
    arm64.add x0, x0, x1
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at managed-string-literal-deduplication.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaRdata x0, __str_rec_1  ; "hello world"
    arm64.leaRdata x1, __str_rec_1  ; "hello world"
    arm64.leaRdata x2, __str_rec_1  ; "hello world"
  __il_body#7:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_body#8:
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
  __il_cont#5:
    arm64.add x0, x0, x1
  __il_body#9:
    arm64.loadRegBaseDisp.word64 x1, [x2 + 8]
  __il_cont#4:
    arm64.add x0, x0, x1
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at managed-string-literal-deduplication.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```
