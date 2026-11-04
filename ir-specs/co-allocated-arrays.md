---
feature: co-allocated-arrays
status: stable
keywords: [array, fixed array, buffer, allocation, co-allocation, runtime, codegen]
category: memory
---

# Co-allocated array buffers: the cases that pin emitted code

Each case pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: a-fixed-array-creation-is-one-sized-create -->
Creating a `FixedArray` is one call to `__managed_create_fixed`, which hands back a record whose slots are
published; there is no `__managed_create` followed by a `__managed_resize`. `make` is reached through a function
value so that its body stays in the program.
```maxon
typealias Word = bits(64)
typealias Planes = FixedArray with 8 Word

typealias PlanesMaker = function() returns Planes

function make() returns Planes
	return Planes.create()
end 'make'

var maker = make as PlanesMaker

function main() returns ExitCode
	var planes = maker()

	if planes.get(0) == 0 'zero'
		return 0
	end 'zero'

	return 1
end 'main'
```
```exitcode
0
```
```TargetIr:x64-windows
data {
  __data_maker@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_console_probe_stdin@32 = i8 0
  __mrt_console_probe_stdout@33 = i8 0
  __mrt_console_probe_stderr@34 = i8 0
  __mrt_program_started@35 = i8 0
}

func @make {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rax, 8
    x64.leaRegRdata rdx, [rip + __layout_FixedArray_N8_Word]
  __il_body:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 72]
    x64.loadRegBaseDisp.word64 r9, [rdx + 40]
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
  __il_cont:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.leaRegGlobal rax, __data_maker
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_retain
    x64.movRegReg r12, r8
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.movRegReg rcx, rbx
    x64.callReg rax
    x64.movRegReg rbx, r8
    x64.movRegReg rcx, r12
    x64.callDirect __mm_decref
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  __im_cont:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, ifcont
  zero:
    x64.movRegImm32 r12, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:x64-linux
data {
  __data_maker@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @make {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rax, 8
    x64.leaRegRdata rdx, [rip + __layout_FixedArray_N8_Word]
  __il_body:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 72]
    x64.loadRegBaseDisp.word64 r9, [rdx + 40]
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
  __il_cont:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.leaRegGlobal rax, __data_maker
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_retain
    x64.movRegReg r12, r8
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.movRegReg rcx, rbx
    x64.callReg rax
    x64.movRegReg rbx, r8
    x64.movRegReg rcx, r12
    x64.callDirect __mm_decref
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  __im_cont:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, ifcont
  zero:
    x64.movRegImm32 r12, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-macos
data {
  __data_maker@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @make {
  entry:
    arm64.prologue 16
    arm64.movImm x2, 8
    arm64.leaRdata x1, __layout_FixedArray_N8_Word
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 72]
    arm64.loadRegBaseDisp.word64 x3, [x1 + 40]
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
  __il_cont:
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaGlobal x0, __data_maker
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.movRegReg x0, x19
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.movRegReg x0, x19
    arm64.blr x1
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  __im_cont:
    arm64.cmp x0, 0
    arm64.b.ne ifcont
  zero:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  ifcont:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __data_maker@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_hwcap_atomics@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @make {
  entry:
    arm64.prologue 16
    arm64.movImm x2, 8
    arm64.leaRdata x1, __layout_FixedArray_N8_Word
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 72]
    arm64.loadRegBaseDisp.word64 x3, [x1 + 40]
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
  __il_cont:
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaGlobal x0, __data_maker
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.movRegReg x0, x19
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.movRegReg x0, x19
    arm64.blr x1
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  __im_cont:
    arm64.cmp x0, 0
    arm64.b.ne ifcont
  zero:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  ifcont:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}
```

<!-- test: the-sized-create-allocates-the-record-and-a-small-buffer-together -->
`__managed_create_sized` allocates once for a record whose buffer fits the co-allocation limit: the record and the
buffer's refcount word and bytes are one `__mm_alloc`, and the second `__mm_alloc`, in the `seppublish` block, is
reached only by a buffer past the limit.
```maxon
typealias Word = bits(64)
typealias Planes = Array with Word

function main() returns ExitCode
	let planes = Planes.create(8)

	if (try planes.get(7) otherwise 1) == 0 'zero'
		return 0
	end 'zero'

	return 1
end 'main'
```
```exitcode
0
```
```RequiredRuntime
__managed_create_sized
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
    x64.movRegImm32 rax, 8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 r9, 0
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_sized
    x64.movRegImm32 rdx, 7
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.cmpRegImm32 rax, 7
    x64.jcc belowEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 56]
  tryok:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, ifcont
  zero:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 rbx, 1
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
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
    x64.jcc notEqual, ifcont
    x64.jmp tryok
}

func @__managed_create_sized {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 56
    x64.movRegReg rbx, rcx
    x64.storeSlotReg slot0, rax
    x64.movRegReg r12, r9
    x64.cmpRegImm32 rbx, 0
    x64.movRegImm32 r13, 48
    x64.jcc notEqual, chkcap
  badsize:
    x64.leaRegRdata rcx, [rip + __abort_msg_74]  ; "fatal error: runtime abort 74 (arrayElementSizeZero)\x0a"
    x64.movRegImm32 rdx, 53
    x64.movRegImm32 rax, 74
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  chkcap:
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, overflow
  chksign:
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, floorcap
  emptyarm:
    x64.movRegImm32 rax, 0
    x64.movRegReg r14, rax
    x64.jmp alloc
  floorcap:
    x64.cmpRegImm32 rdx, 4
    x64.jcc greaterEqual, takeask
  takemin:
    x64.movRegImm32 rcx, 4
    x64.jmp __il_body#21
  takeask:
    x64.movRegReg rcx, rdx
  __il_body#21:
    x64.movRegReg rax, rbx
    x64.sarRegImm8 rax, rbx, 63
    x64.movRegReg rdx, rax
    x64.xorRegImm32 rdx, rax, -1
    x64.movRegImm32 rsi, 0
    x64.subRegReg rsi, rsi, rbx
    x64.movRegReg rdi, rbx
    x64.shlRegImm8 rdi, rbx, 3
    x64.andRegReg rsi, rsi, rax
    x64.andRegReg rdi, rdi, rdx
    x64.orRegReg rsi, rsi, rdi
  __il_cont#20:
    x64.movRegReg rax, rcx
    x64.imulRegReg rax, rcx, rsi
  __il_body#22:
    x64.leaRegRegImm32 rdi, rax, 7
    x64.shrRegImm8 rdi, rdi, 3
  __il_cont#19:
    x64.movRegReg rdx, rcx
    x64.orRegReg rdx, rcx, rsi
    x64.shrRegImm8 rdx, rdx, 31
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, wide
  measure:
    x64.cmpRegImm32 rdi, 256
    x64.jcc greater, separm
  coarm:
    x64.leaRegRegImm32 r13, rdi, 56
    x64.movRegImm32 rax, 0
    x64.movRegReg r14, rcx
    x64.jmp alloc
  separm:
    x64.movRegReg rax, rdi
    x64.movRegReg r14, rcx
  alloc:
    x64.storeSlotReg slot1, rax
    x64.movRegImm32 rdx, 0
    x64.movRegReg rcx, r13
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.storeBaseDispReg.word64 [r15 + 24], rbx
    x64.storeBaseDispReg.word64 [r15 + 40], r12
    x64.cmpRegImm32 r13, 48
    x64.jcc lessEqual, chksep
  copublish:
    x64.leaRegRegImm32 rax, r15, 56
    x64.storeBaseDispReg.word64 [r15 + 0], rax
    x64.storeBaseDispReg.word64 [r15 + 16], r14
    x64.jmp done
  chksep:
    x64.loadRegSlot rax, slot1
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done
  seppublish:
    x64.movRegImm32 rdx, 0
    x64.loadRegSlot rcx, slot1
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r15 + 0], r8
    x64.storeBaseDispReg.word64 [r15 + 16], r14
  done:
    x64.loadRegSlot rax, slot0
    x64.storeBaseDispReg.word64 [r15 + 8], rax
    x64.movRegReg r8, r15
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  wide:
    x64.cqo
    x64.idivReg rsi
    x64.cmpRegReg rax, rcx
    x64.jcc equal, measure
  overflow:
    x64.leaRegRdata rcx, [rip + __abort_msg_71]  ; "fatal error: runtime abort 71 (arrayCapacityOverflow)\x0a"
    x64.movRegImm32 rdx, 54
    x64.movRegImm32 rax, 71
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 56
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
    x64.prologue 40
    x64.movRegImm32 rax, 8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 r9, 0
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_sized
    x64.movRegImm32 rdx, 7
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.cmpRegImm32 rax, 7
    x64.jcc belowEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 56]
  tryok:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, ifcont
  zero:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 rbx, 1
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
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
    x64.jcc notEqual, ifcont
    x64.jmp tryok
}

func @__managed_create_sized {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 56
    x64.movRegReg rbx, rcx
    x64.storeSlotReg slot0, rax
    x64.movRegReg r12, r9
    x64.cmpRegImm32 rbx, 0
    x64.movRegImm32 r13, 48
    x64.jcc notEqual, chkcap
  badsize:
    x64.leaRegRdata rcx, [rip + __abort_msg_74]  ; "fatal error: runtime abort 74 (arrayElementSizeZero)\x0a"
    x64.movRegImm32 rdx, 53
    x64.movRegImm32 rax, 74
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  chkcap:
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, overflow
  chksign:
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, floorcap
  emptyarm:
    x64.movRegImm32 rax, 0
    x64.movRegReg r14, rax
    x64.jmp alloc
  floorcap:
    x64.cmpRegImm32 rdx, 4
    x64.jcc greaterEqual, takeask
  takemin:
    x64.movRegImm32 rcx, 4
    x64.jmp __il_body#21
  takeask:
    x64.movRegReg rcx, rdx
  __il_body#21:
    x64.movRegReg rax, rbx
    x64.sarRegImm8 rax, rbx, 63
    x64.movRegReg rdx, rax
    x64.xorRegImm32 rdx, rax, -1
    x64.movRegImm32 rsi, 0
    x64.subRegReg rsi, rsi, rbx
    x64.movRegReg rdi, rbx
    x64.shlRegImm8 rdi, rbx, 3
    x64.andRegReg rsi, rsi, rax
    x64.andRegReg rdi, rdi, rdx
    x64.orRegReg rsi, rsi, rdi
  __il_cont#20:
    x64.movRegReg rax, rcx
    x64.imulRegReg rax, rcx, rsi
  __il_body#22:
    x64.leaRegRegImm32 rdi, rax, 7
    x64.shrRegImm8 rdi, rdi, 3
  __il_cont#19:
    x64.movRegReg rdx, rcx
    x64.orRegReg rdx, rcx, rsi
    x64.shrRegImm8 rdx, rdx, 31
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, wide
  measure:
    x64.cmpRegImm32 rdi, 256
    x64.jcc greater, separm
  coarm:
    x64.leaRegRegImm32 r13, rdi, 56
    x64.movRegImm32 rax, 0
    x64.movRegReg r14, rcx
    x64.jmp alloc
  separm:
    x64.movRegReg rax, rdi
    x64.movRegReg r14, rcx
  alloc:
    x64.storeSlotReg slot1, rax
    x64.movRegImm32 rdx, 0
    x64.movRegReg rcx, r13
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.storeBaseDispReg.word64 [r15 + 24], rbx
    x64.storeBaseDispReg.word64 [r15 + 40], r12
    x64.cmpRegImm32 r13, 48
    x64.jcc lessEqual, chksep
  copublish:
    x64.leaRegRegImm32 rax, r15, 56
    x64.storeBaseDispReg.word64 [r15 + 0], rax
    x64.storeBaseDispReg.word64 [r15 + 16], r14
    x64.jmp done
  chksep:
    x64.loadRegSlot rax, slot1
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done
  seppublish:
    x64.movRegImm32 rdx, 0
    x64.loadRegSlot rcx, slot1
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r15 + 0], r8
    x64.storeBaseDispReg.word64 [r15 + 16], r14
  done:
    x64.loadRegSlot rax, slot0
    x64.storeBaseDispReg.word64 [r15 + 8], rax
    x64.movRegReg r8, r15
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  wide:
    x64.cqo
    x64.idivReg rsi
    x64.cmpRegReg rax, rcx
    x64.jcc equal, measure
  overflow:
    x64.leaRegRdata rcx, [rip + __abort_msg_71]  ; "fatal error: runtime abort 71 (arrayCapacityOverflow)\x0a"
    x64.movRegImm32 rdx, 54
    x64.movRegImm32 rax, 71
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 56
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x2, 8
    arm64.movImm x0, 8
    arm64.movImm x3, 0
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_sized
    arm64.movImm x1, 7
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 7
    arm64.b.ls __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 56]
  tryok:
    arm64.cmp x1, 0
    arm64.b.ne ifcont
  zero:
    arm64.movImm x19, 0
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  ifcont:
    arm64.movImm x19, 1
    arm64.bl __managed_decref
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
    arm64.b.ne ifcont
    arm64.b tryok
}

func @__managed_create_sized {
  entry:
    arm64.prologue 80
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x2
    arm64.movRegReg x21, x3
    arm64.cmp x19, 0
    arm64.movImm x22, 48
    arm64.b.ne chkcap
  badsize:
    arm64.leaRdata x0, __abort_msg_74  ; "fatal error: runtime abort 74 (arrayElementSizeZero)\x0a"
    arm64.movImm x1, 53
    arm64.movImm x2, 74
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  chkcap:
    arm64.cmp x1, 0
    arm64.b.lt overflow
  chksign:
    arm64.cmp x1, 0
    arm64.b.ne floorcap
  emptyarm:
    arm64.movImm x23, 0
    arm64.movRegReg x24, x23
    arm64.b alloc
  floorcap:
    arm64.cmp x1, 4
    arm64.b.ge takeask
  takemin:
    arm64.movImm x0, 4
    arm64.b __il_body#21
  takeask:
    arm64.movRegReg x0, x1
  __il_body#21:
    arm64.asr x1, x19, 63
    arm64.movImm x16, 18446744073709551615
    arm64.eor x2, x1, x16
    arm64.movImm x3, 0
    arm64.sub x3, x3, x19
    arm64.lsl x4, x19, 3
    arm64.and x1, x3, x1
    arm64.and x2, x4, x2
    arm64.orr x1, x1, x2
  __il_cont#20:
    arm64.mul x2, x0, x1
  __il_body#22:
    arm64.add x3, x2, 7
    arm64.lsr x23, x3, 3
  __il_cont#19:
    arm64.orr x3, x0, x1
    arm64.lsr x3, x3, 31
    arm64.cmp x3, 0
    arm64.b.ne wide
  measure:
    arm64.cmp x23, 256
    arm64.b.gt separm
  coarm:
    arm64.add x22, x23, 56
    arm64.movImm x23, 0
    arm64.movRegReg x24, x0
    arm64.b alloc
  separm:
    arm64.movRegReg x24, x0
  alloc:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x22
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.storeBaseDispReg.word64 [x25 + 24], x19
    arm64.storeBaseDispReg.word64 [x25 + 40], x21
    arm64.cmp x22, 48
    arm64.b.le chksep
  copublish:
    arm64.add x0, x25, 56
    arm64.storeBaseDispReg.word64 [x25 + 0], x0
    arm64.storeBaseDispReg.word64 [x25 + 16], x24
    arm64.b done
  chksep:
    arm64.cmp x23, 0
    arm64.b.eq done
  seppublish:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x23
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x25 + 0], x0
    arm64.storeBaseDispReg.word64 [x25 + 16], x24
  done:
    arm64.storeBaseDispReg.word64 [x25 + 8], x20
    arm64.movRegReg x0, x25
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  wide:
    arm64.sdiv x1, x2, x1
    arm64.cmp x1, x0
    arm64.b.eq measure
  overflow:
    arm64.leaRdata x0, __abort_msg_71  ; "fatal error: runtime abort 71 (arrayCapacityOverflow)\x0a"
    arm64.movImm x1, 54
    arm64.movImm x2, 71
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x2, 8
    arm64.movImm x0, 8
    arm64.movImm x3, 0
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_sized
    arm64.movImm x1, 7
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 7
    arm64.b.ls __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 56]
  tryok:
    arm64.cmp x1, 0
    arm64.b.ne ifcont
  zero:
    arm64.movImm x19, 0
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  ifcont:
    arm64.movImm x19, 1
    arm64.bl __managed_decref
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
    arm64.b.ne ifcont
    arm64.b tryok
}

func @__managed_create_sized {
  entry:
    arm64.prologue 80
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x2
    arm64.movRegReg x21, x3
    arm64.cmp x19, 0
    arm64.movImm x22, 48
    arm64.b.ne chkcap
  badsize:
    arm64.leaRdata x0, __abort_msg_74  ; "fatal error: runtime abort 74 (arrayElementSizeZero)\x0a"
    arm64.movImm x1, 53
    arm64.movImm x2, 74
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  chkcap:
    arm64.cmp x1, 0
    arm64.b.lt overflow
  chksign:
    arm64.cmp x1, 0
    arm64.b.ne floorcap
  emptyarm:
    arm64.movImm x23, 0
    arm64.movRegReg x24, x23
    arm64.b alloc
  floorcap:
    arm64.cmp x1, 4
    arm64.b.ge takeask
  takemin:
    arm64.movImm x0, 4
    arm64.b __il_body#21
  takeask:
    arm64.movRegReg x0, x1
  __il_body#21:
    arm64.asr x1, x19, 63
    arm64.movImm x16, 18446744073709551615
    arm64.eor x2, x1, x16
    arm64.movImm x3, 0
    arm64.sub x3, x3, x19
    arm64.lsl x4, x19, 3
    arm64.and x1, x3, x1
    arm64.and x2, x4, x2
    arm64.orr x1, x1, x2
  __il_cont#20:
    arm64.mul x2, x0, x1
  __il_body#22:
    arm64.add x3, x2, 7
    arm64.lsr x23, x3, 3
  __il_cont#19:
    arm64.orr x3, x0, x1
    arm64.lsr x3, x3, 31
    arm64.cmp x3, 0
    arm64.b.ne wide
  measure:
    arm64.cmp x23, 256
    arm64.b.gt separm
  coarm:
    arm64.add x22, x23, 56
    arm64.movImm x23, 0
    arm64.movRegReg x24, x0
    arm64.b alloc
  separm:
    arm64.movRegReg x24, x0
  alloc:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x22
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.storeBaseDispReg.word64 [x25 + 24], x19
    arm64.storeBaseDispReg.word64 [x25 + 40], x21
    arm64.cmp x22, 48
    arm64.b.le chksep
  copublish:
    arm64.add x0, x25, 56
    arm64.storeBaseDispReg.word64 [x25 + 0], x0
    arm64.storeBaseDispReg.word64 [x25 + 16], x24
    arm64.b done
  chksep:
    arm64.cmp x23, 0
    arm64.b.eq done
  seppublish:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x23
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x25 + 0], x0
    arm64.storeBaseDispReg.word64 [x25 + 16], x24
  done:
    arm64.storeBaseDispReg.word64 [x25 + 8], x20
    arm64.movRegReg x0, x25
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  wide:
    arm64.sdiv x1, x2, x1
    arm64.cmp x1, x0
    arm64.b.eq measure
  overflow:
    arm64.leaRdata x0, __abort_msg_71  ; "fatal error: runtime abort 71 (arrayCapacityOverflow)\x0a"
    arm64.movImm x1, 54
    arm64.movImm x2, 71
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
}
```

<!-- test: the-fixed-create-places-the-record-and-its-buffer-together-at-any-size -->
`__managed_create_fixed` is `__managed_create_sized` without the co-allocation limit: a fixed array never grows, so
the record and the buffer's refcount word and bytes are always one `__mm_alloc`, and there is no `seppublish` block.
```maxon
typealias Word = bits(64)
typealias Planes = FixedArray with 8 Word

function main() returns ExitCode
	let planes = Planes.create()

	if planes.get(7) == 0 'zero'
		return 0
	end 'zero'

	return 1
end 'main'
```
```exitcode
0
```
```RequiredRuntime
__managed_create_fixed
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
    x64.movRegImm32 rax, 8
    x64.leaRegRdata rdx, [rip + __layout_FixedArray_N8_Word]
  __il_body:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 72]
    x64.loadRegBaseDisp.word64 r9, [rdx + 40]
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
  __il_cont:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 56]
  __im_cont:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, ifcont
  zero:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 rbx, 1
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__managed_create_fixed {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rax
    x64.movRegReg r13, r9
    x64.cmpRegImm32 rbx, 0
    x64.movRegImm32 r14, 48
    x64.jcc notEqual, chkcap
  badsize:
    x64.leaRegRdata rcx, [rip + __abort_msg_74]  ; "fatal error: runtime abort 74 (arrayElementSizeZero)\x0a"
    x64.movRegImm32 rdx, 53
    x64.movRegImm32 rax, 74
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  chkcap:
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, overflow
  chksign:
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, floorcap
  emptyarm:
    x64.movRegImm32 r15, 0
    x64.jmp alloc
  floorcap:
    x64.cmpRegImm32 rdx, 4
    x64.jcc greaterEqual, takeask
  takemin:
    x64.movRegImm32 rcx, 4
    x64.jmp __il_body#18
  takeask:
    x64.movRegReg rcx, rdx
  __il_body#18:
    x64.movRegReg rax, rbx
    x64.sarRegImm8 rax, rbx, 63
    x64.movRegReg rdx, rax
    x64.xorRegImm32 rdx, rax, -1
    x64.movRegImm32 rsi, 0
    x64.subRegReg rsi, rsi, rbx
    x64.movRegReg rdi, rbx
    x64.shlRegImm8 rdi, rbx, 3
    x64.andRegReg rsi, rsi, rax
    x64.andRegReg rdi, rdi, rdx
    x64.orRegReg rsi, rsi, rdi
  __il_cont#17:
    x64.movRegReg rax, rcx
    x64.imulRegReg rax, rcx, rsi
  __il_body#19:
    x64.leaRegRegImm32 rdi, rax, 7
    x64.shrRegImm8 rdi, rdi, 3
  __il_cont#16:
    x64.movRegReg rdx, rcx
    x64.orRegReg rdx, rcx, rsi
    x64.shrRegImm8 rdx, rdx, 31
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, wide
  coarm:
    x64.leaRegRegImm32 r14, rdi, 56
    x64.movRegReg r15, rcx
  alloc:
    x64.movRegImm32 rdx, 0
    x64.movRegReg rcx, r14
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 24], rbx
    x64.storeBaseDispReg.word64 [r8 + 40], r13
    x64.cmpRegImm32 r14, 48
    x64.jcc lessEqual, done
  copublish:
    x64.leaRegRegImm32 rax, r8, 56
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 16], r15
  done:
    x64.storeBaseDispReg.word64 [r8 + 8], r12
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  wide:
    x64.cqo
    x64.idivReg rsi
    x64.cmpRegReg rax, rcx
    x64.jcc equal, coarm
  overflow:
    x64.leaRegRdata rcx, [rip + __abort_msg_71]  ; "fatal error: runtime abort 71 (arrayCapacityOverflow)\x0a"
    x64.movRegImm32 rdx, 54
    x64.movRegImm32 rax, 71
    x64.callDirect __rt_abort
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
    x64.prologue 40
    x64.movRegImm32 rax, 8
    x64.leaRegRdata rdx, [rip + __layout_FixedArray_N8_Word]
  __il_body:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 72]
    x64.loadRegBaseDisp.word64 r9, [rdx + 40]
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
  __il_cont:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 56]
  __im_cont:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, ifcont
  zero:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 rbx, 1
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__managed_create_fixed {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rax
    x64.movRegReg r13, r9
    x64.cmpRegImm32 rbx, 0
    x64.movRegImm32 r14, 48
    x64.jcc notEqual, chkcap
  badsize:
    x64.leaRegRdata rcx, [rip + __abort_msg_74]  ; "fatal error: runtime abort 74 (arrayElementSizeZero)\x0a"
    x64.movRegImm32 rdx, 53
    x64.movRegImm32 rax, 74
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  chkcap:
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, overflow
  chksign:
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, floorcap
  emptyarm:
    x64.movRegImm32 r15, 0
    x64.jmp alloc
  floorcap:
    x64.cmpRegImm32 rdx, 4
    x64.jcc greaterEqual, takeask
  takemin:
    x64.movRegImm32 rcx, 4
    x64.jmp __il_body#18
  takeask:
    x64.movRegReg rcx, rdx
  __il_body#18:
    x64.movRegReg rax, rbx
    x64.sarRegImm8 rax, rbx, 63
    x64.movRegReg rdx, rax
    x64.xorRegImm32 rdx, rax, -1
    x64.movRegImm32 rsi, 0
    x64.subRegReg rsi, rsi, rbx
    x64.movRegReg rdi, rbx
    x64.shlRegImm8 rdi, rbx, 3
    x64.andRegReg rsi, rsi, rax
    x64.andRegReg rdi, rdi, rdx
    x64.orRegReg rsi, rsi, rdi
  __il_cont#17:
    x64.movRegReg rax, rcx
    x64.imulRegReg rax, rcx, rsi
  __il_body#19:
    x64.leaRegRegImm32 rdi, rax, 7
    x64.shrRegImm8 rdi, rdi, 3
  __il_cont#16:
    x64.movRegReg rdx, rcx
    x64.orRegReg rdx, rcx, rsi
    x64.shrRegImm8 rdx, rdx, 31
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, wide
  coarm:
    x64.leaRegRegImm32 r14, rdi, 56
    x64.movRegReg r15, rcx
  alloc:
    x64.movRegImm32 rdx, 0
    x64.movRegReg rcx, r14
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 24], rbx
    x64.storeBaseDispReg.word64 [r8 + 40], r13
    x64.cmpRegImm32 r14, 48
    x64.jcc lessEqual, done
  copublish:
    x64.leaRegRegImm32 rax, r8, 56
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 16], r15
  done:
    x64.storeBaseDispReg.word64 [r8 + 8], r12
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  wide:
    x64.cqo
    x64.idivReg rsi
    x64.cmpRegReg rax, rcx
    x64.jcc equal, coarm
  overflow:
    x64.leaRegRdata rcx, [rip + __abort_msg_71]  ; "fatal error: runtime abort 71 (arrayCapacityOverflow)\x0a"
    x64.movRegImm32 rdx, 54
    x64.movRegImm32 rax, 71
    x64.callDirect __rt_abort
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
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 8
    arm64.leaRdata x1, __layout_FixedArray_N8_Word
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 72]
    arm64.loadRegBaseDisp.word64 x3, [x1 + 40]
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
  __il_cont:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 56]
  __im_cont:
    arm64.cmp x1, 0
    arm64.b.ne ifcont
  zero:
    arm64.movImm x19, 0
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
  ifcont:
    arm64.movImm x19, 1
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__managed_create_fixed {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x2
    arm64.movRegReg x21, x3
    arm64.cmp x19, 0
    arm64.movImm x22, 48
    arm64.b.ne chkcap
  badsize:
    arm64.leaRdata x0, __abort_msg_74  ; "fatal error: runtime abort 74 (arrayElementSizeZero)\x0a"
    arm64.movImm x1, 53
    arm64.movImm x2, 74
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  chkcap:
    arm64.cmp x1, 0
    arm64.b.lt overflow
  chksign:
    arm64.cmp x1, 0
    arm64.b.ne floorcap
  emptyarm:
    arm64.movImm x23, 0
    arm64.b alloc
  floorcap:
    arm64.cmp x1, 4
    arm64.b.ge takeask
  takemin:
    arm64.movImm x0, 4
    arm64.b __il_body#18
  takeask:
    arm64.movRegReg x0, x1
  __il_body#18:
    arm64.asr x1, x19, 63
    arm64.movImm x16, 18446744073709551615
    arm64.eor x2, x1, x16
    arm64.movImm x3, 0
    arm64.sub x3, x3, x19
    arm64.lsl x4, x19, 3
    arm64.and x1, x3, x1
    arm64.and x2, x4, x2
    arm64.orr x1, x1, x2
  __il_cont#17:
    arm64.mul x2, x0, x1
  __il_body#19:
    arm64.add x3, x2, 7
    arm64.lsr x3, x3, 3
  __il_cont#16:
    arm64.orr x4, x0, x1
    arm64.lsr x4, x4, 31
    arm64.cmp x4, 0
    arm64.b.ne wide
  coarm:
    arm64.add x22, x3, 56
    arm64.movRegReg x23, x0
  alloc:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x22
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 24], x19
    arm64.storeBaseDispReg.word64 [x0 + 40], x21
    arm64.cmp x22, 48
    arm64.b.le done
  copublish:
    arm64.add x1, x0, 56
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.storeBaseDispReg.word64 [x0 + 16], x23
  done:
    arm64.storeBaseDispReg.word64 [x0 + 8], x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  wide:
    arm64.sdiv x1, x2, x1
    arm64.cmp x1, x0
    arm64.b.eq coarm
  overflow:
    arm64.leaRdata x0, __abort_msg_71  ; "fatal error: runtime abort 71 (arrayCapacityOverflow)\x0a"
    arm64.movImm x1, 54
    arm64.movImm x2, 71
    arm64.bl __rt_abort
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
  __mrt_hwcap_atomics@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 8
    arm64.leaRdata x1, __layout_FixedArray_N8_Word
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 72]
    arm64.loadRegBaseDisp.word64 x3, [x1 + 40]
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
  __il_cont:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 56]
  __im_cont:
    arm64.cmp x1, 0
    arm64.b.ne ifcont
  zero:
    arm64.movImm x19, 0
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
  ifcont:
    arm64.movImm x19, 1
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__managed_create_fixed {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x2
    arm64.movRegReg x21, x3
    arm64.cmp x19, 0
    arm64.movImm x22, 48
    arm64.b.ne chkcap
  badsize:
    arm64.leaRdata x0, __abort_msg_74  ; "fatal error: runtime abort 74 (arrayElementSizeZero)\x0a"
    arm64.movImm x1, 53
    arm64.movImm x2, 74
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  chkcap:
    arm64.cmp x1, 0
    arm64.b.lt overflow
  chksign:
    arm64.cmp x1, 0
    arm64.b.ne floorcap
  emptyarm:
    arm64.movImm x23, 0
    arm64.b alloc
  floorcap:
    arm64.cmp x1, 4
    arm64.b.ge takeask
  takemin:
    arm64.movImm x0, 4
    arm64.b __il_body#18
  takeask:
    arm64.movRegReg x0, x1
  __il_body#18:
    arm64.asr x1, x19, 63
    arm64.movImm x16, 18446744073709551615
    arm64.eor x2, x1, x16
    arm64.movImm x3, 0
    arm64.sub x3, x3, x19
    arm64.lsl x4, x19, 3
    arm64.and x1, x3, x1
    arm64.and x2, x4, x2
    arm64.orr x1, x1, x2
  __il_cont#17:
    arm64.mul x2, x0, x1
  __il_body#19:
    arm64.add x3, x2, 7
    arm64.lsr x3, x3, 3
  __il_cont#16:
    arm64.orr x4, x0, x1
    arm64.lsr x4, x4, 31
    arm64.cmp x4, 0
    arm64.b.ne wide
  coarm:
    arm64.add x22, x3, 56
    arm64.movRegReg x23, x0
  alloc:
    arm64.movImm x1, 0
    arm64.movRegReg x0, x22
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 24], x19
    arm64.storeBaseDispReg.word64 [x0 + 40], x21
    arm64.cmp x22, 48
    arm64.b.le done
  copublish:
    arm64.add x1, x0, 56
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.storeBaseDispReg.word64 [x0 + 16], x23
  done:
    arm64.storeBaseDispReg.word64 [x0 + 8], x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  wide:
    arm64.sdiv x1, x2, x1
    arm64.cmp x1, x0
    arm64.b.eq coarm
  overflow:
    arm64.leaRdata x0, __abort_msg_71  ; "fatal error: runtime abort 71 (arrayCapacityOverflow)\x0a"
    arm64.movImm x1, 54
    arm64.movImm x2, 71
    arm64.bl __rt_abort
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

<!-- test: a-sized-memory-create-is-one-sized-create -->
<!-- tier: stdlib -->
`__ManagedMemory.create(capacity, elementSize)` is the throwing check of its arguments and one call to
`__managed_create_sized`; it no longer calls `__managed_create` and `__managed_reserve`.
```maxon
function main() returns ExitCode
	var memory = try __ManagedMemory.create(16, 1) otherwise return 1

	try memory.setLength(16) otherwise return 2

	return memory.length() as ExitCode
end 'main'
```
```exitcode
16
```
```RequiredRuntime
__managed_mem_create
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
    x64.prologue 32
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 1
    x64.callDirect __managed_mem_create
    x64.movRegReg rbx, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#1
  tryerr#2:
    x64.movRegImm32 r8, 1
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#1:
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_set_length
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#3
  tryerr#4:
    x64.movRegImm32 r12, 2
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#3:
    x64.loadRegBaseDisp.word64 r12, [rbx + 8]
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
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-sized-memory-create-is-one-sized-create.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__managed_mem_create {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegReg rsi, rcx
    x64.cmpRegImm32 rdx, 1
    x64.jcc less, badalloc
  chkcount:
    x64.cmpRegImm32 rsi, 0
    x64.jcc greaterEqual, build
  badalloc:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 8
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  build:
    x64.movRegImm32 r9, 0
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, rdx
    x64.movRegReg rdx, rsi
    x64.callDirect __managed_create_sized
    x64.movRegImm32 r10, 0
    x64.epilogue 32
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
    x64.prologue 32
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 1
    x64.callDirect __managed_mem_create
    x64.movRegReg rbx, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#1
  tryerr#2:
    x64.movRegImm32 r8, 1
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#1:
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_set_length
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#3
  tryerr#4:
    x64.movRegImm32 r12, 2
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#3:
    x64.loadRegBaseDisp.word64 r12, [rbx + 8]
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r12, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-sized-memory-create-is-one-sized-create.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__managed_mem_create {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegReg rsi, rcx
    x64.cmpRegImm32 rdx, 1
    x64.jcc less, badalloc
  chkcount:
    x64.cmpRegImm32 rsi, 0
    x64.jcc greaterEqual, build
  badalloc:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 8
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  build:
    x64.movRegImm32 r9, 0
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, rdx
    x64.movRegReg rdx, rsi
    x64.callDirect __managed_create_sized
    x64.movRegImm32 r10, 0
    x64.epilogue 32
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
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 16
    arm64.movImm x1, 1
    arm64.bl __managed_mem_create
    arm64.movRegReg x19, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#1
  tryerr#2:
    arm64.movImm x0, 1
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  tryok#1:
    arm64.movImm x1, 16
    arm64.movRegReg x0, x19
    arm64.bl __managed_set_length
    arm64.cmp x9, 0
    arm64.b.eq tryok#3
  tryerr#4:
    arm64.movImm x20, 2
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  tryok#3:
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
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
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-sized-memory-create-is-one-sized-create.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

func @__managed_mem_create {
  entry:
    arm64.prologue 16
    arm64.movRegReg x4, x0
    arm64.cmp x1, 1
    arm64.b.lt badalloc
  chkcount:
    arm64.cmp x4, 0
    arm64.b.ge build
  badalloc:
    arm64.movImm x0, 0
    arm64.movImm x9, 8
    arm64.epilogue 16
    arm64.ret
  build:
    arm64.movImm x3, 0
    arm64.movImm x2, 0
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x4
    arm64.bl __managed_create_sized
    arm64.movImm x9, 0
    arm64.epilogue 16
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 16
    arm64.movImm x1, 1
    arm64.bl __managed_mem_create
    arm64.movRegReg x19, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#1
  tryerr#2:
    arm64.movImm x0, 1
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  tryok#1:
    arm64.movImm x1, 16
    arm64.movRegReg x0, x19
    arm64.bl __managed_set_length
    arm64.cmp x9, 0
    arm64.b.eq tryok#3
  tryerr#4:
    arm64.movImm x20, 2
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  tryok#3:
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
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
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-sized-memory-create-is-one-sized-create.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

func @__managed_mem_create {
  entry:
    arm64.prologue 16
    arm64.movRegReg x4, x0
    arm64.cmp x1, 1
    arm64.b.lt badalloc
  chkcount:
    arm64.cmp x4, 0
    arm64.b.ge build
  badalloc:
    arm64.movImm x0, 0
    arm64.movImm x9, 8
    arm64.epilogue 16
    arm64.ret
  build:
    arm64.movImm x3, 0
    arm64.movImm x2, 0
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x4
    arm64.bl __managed_create_sized
    arm64.movImm x9, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: a-length-publish-and-a-drop-on-a-plain-record-keep-no-registers-across-the-element-walk -->
<!-- tier: stdlib -->
`__managed_set_length` and `__managed_release` reach the managed-element destroy walk through a call, so the
frame of neither holds the walk's loop state: publishing a length on a record is a few loads, compares and one
store, and the last drop of a plain record is its owed-base probe and one free.
```maxon
function main() returns ExitCode
	var memory = try __ManagedMemory.create(16, 1) otherwise return 1

	try memory.setLength(16) otherwise return 2
	try memory.setLength(8) otherwise return 3

	return memory.length() as ExitCode
end 'main'
```
```exitcode
8
```
```RequiredRuntime
__managed_set_length
__managed_shrink_length
__managed_release
__managed_destroy_range
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
    x64.prologue 32
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 1
    x64.callDirect __managed_mem_create
    x64.movRegReg rbx, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#1
  tryerr#2:
    x64.movRegImm32 r8, 1
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#1:
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_set_length
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#3
  tryerr#4:
    x64.movRegImm32 r12, 2
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#3:
    x64.movRegImm32 rdx, 8
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_set_length
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#6
  tryerr#7:
    x64.movRegImm32 r12, 3
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#6:
    x64.loadRegBaseDisp.word64 r12, [rbx + 8]
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
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-length-publish-and-a-drop-on-a-plain-record-keep-no-registers-across-the-element-walk.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__managed_release {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [rbx + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, relbuf
  mwalk:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_destroy_range
  relbuf:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc greaterEqual, obase
  vbase:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 32]
    x64.jmp basejoin
  obase:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, basejoin
  obufprobe:
    x64.leaRegRegImm32 rax, rbx, 56
    x64.cmpRegReg rcx, rax
    x64.jcc notEqual, basejoin
  obaseco:
    x64.movRegImm32 rcx, 0
  basejoin:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, freerec
  rcstep:
    x64.callDirect __mm_decref
  freerec:
    x64.movRegReg rcx, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.tailJmp __mm_free
}

func @__managed_destroy_range {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.loadRegBaseDisp.word64 rbx, [rcx + 8]
    x64.loadRegBaseDisp.word64 r12, [rcx + 40]
    x64.loadRegBaseDisp.word64 r13, [rcx + 0]
    x64.loadRegBaseDisp.word64 r14, [rcx + 24]
    x64.movRegReg r15, rdx
  dtorh:
    x64.cmpRegReg r15, rbx
    x64.jcc less, dtorb
  dtordone:
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  dtorb:
    x64.movRegReg rax, r15
    x64.imulRegReg rax, r15, r14
    x64.leaRegRegReg rax, r13, rax
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, dtorstep
  dtordo:
    x64.callReg r12
  dtorstep:
    x64.leaRegRegImm32 r15, r15, 1
    x64.jmp dtorh
}

func @__managed_set_length {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 48
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, badlen
  chkhigh:
    x64.loadRegBaseDisp.word64 rax, [rcx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, slddetach
  sldbuf:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, slddone
  sldshared:
    x64.leaRegRegImm32 rax, rax, -16
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, slddetach
  slddone:
    x64.loadRegBaseDisp.word64 rax, [rcx + 16]
    x64.cmpRegReg rdx, rax
    x64.jcc belowEqual, pick
  badlen:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 4
    x64.epilogue 48
    x64.popReg rbp
    x64.ret
  pick:
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc greaterEqual, setlen
  dozero:
    x64.callDirect __managed_shrink_length
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 48
    x64.popReg rbp
    x64.ret
  setlen:
    x64.storeBaseDispReg.word64 [rcx + 8], rdx
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 48
    x64.popReg rbp
    x64.ret
  slddetach:
    x64.storeSlotReg slot0, rcx
    x64.storeSlotReg slot1, rdx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rcx, slot0
    x64.loadRegSlot rdx, slot1
    x64.jmp slddone
}

func @__managed_shrink_length {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.loadRegBaseDisp.word64 r13, [rbx + 8]
    x64.loadRegBaseDisp.word64 r14, [rbx + 24]
    x64.loadRegBaseDisp.word64 r15, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rbx + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, mcont
  mwalk:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __managed_destroy_range
  mcont:
    x64.movRegReg rax, r13
    x64.subRegReg rax, r13, r12
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg r9, r14
    x64.callDirect __managed_zero_elems
    x64.movRegReg rax, r8
    x64.storeBaseDispReg.word64 [rbx + 8], r12
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
    x64.prologue 32
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 1
    x64.callDirect __managed_mem_create
    x64.movRegReg rbx, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#1
  tryerr#2:
    x64.movRegImm32 r8, 1
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#1:
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_set_length
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#3
  tryerr#4:
    x64.movRegImm32 r12, 2
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#3:
    x64.movRegImm32 rdx, 8
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_set_length
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#6
  tryerr#7:
    x64.movRegImm32 r12, 3
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#6:
    x64.loadRegBaseDisp.word64 r12, [rbx + 8]
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r12, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-length-publish-and-a-drop-on-a-plain-record-keep-no-registers-across-the-element-walk.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__managed_release {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [rbx + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, relbuf
  mwalk:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_destroy_range
  relbuf:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc greaterEqual, obase
  vbase:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 32]
    x64.jmp basejoin
  obase:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, basejoin
  obufprobe:
    x64.leaRegRegImm32 rax, rbx, 56
    x64.cmpRegReg rcx, rax
    x64.jcc notEqual, basejoin
  obaseco:
    x64.movRegImm32 rcx, 0
  basejoin:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, freerec
  rcstep:
    x64.callDirect __mm_decref
  freerec:
    x64.movRegReg rcx, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.tailJmp __mm_free
}

func @__managed_destroy_range {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.loadRegBaseDisp.word64 rbx, [rcx + 8]
    x64.loadRegBaseDisp.word64 r12, [rcx + 40]
    x64.loadRegBaseDisp.word64 r13, [rcx + 0]
    x64.loadRegBaseDisp.word64 r14, [rcx + 24]
    x64.movRegReg r15, rdx
  dtorh:
    x64.cmpRegReg r15, rbx
    x64.jcc less, dtorb
  dtordone:
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  dtorb:
    x64.movRegReg rax, r15
    x64.imulRegReg rax, r15, r14
    x64.leaRegRegReg rax, r13, rax
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, dtorstep
  dtordo:
    x64.callReg r12
  dtorstep:
    x64.leaRegRegImm32 r15, r15, 1
    x64.jmp dtorh
}

func @__managed_set_length {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 48
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, badlen
  chkhigh:
    x64.loadRegBaseDisp.word64 rax, [rcx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, slddetach
  sldbuf:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, slddone
  sldshared:
    x64.leaRegRegImm32 rax, rax, -16
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, slddetach
  slddone:
    x64.loadRegBaseDisp.word64 rax, [rcx + 16]
    x64.cmpRegReg rdx, rax
    x64.jcc belowEqual, pick
  badlen:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 4
    x64.epilogue 48
    x64.popReg rbp
    x64.ret
  pick:
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc greaterEqual, setlen
  dozero:
    x64.callDirect __managed_shrink_length
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 48
    x64.popReg rbp
    x64.ret
  setlen:
    x64.storeBaseDispReg.word64 [rcx + 8], rdx
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 48
    x64.popReg rbp
    x64.ret
  slddetach:
    x64.storeSlotReg slot0, rcx
    x64.storeSlotReg slot1, rdx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rcx, slot0
    x64.loadRegSlot rdx, slot1
    x64.jmp slddone
}

func @__managed_shrink_length {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.loadRegBaseDisp.word64 r13, [rbx + 8]
    x64.loadRegBaseDisp.word64 r14, [rbx + 24]
    x64.loadRegBaseDisp.word64 r15, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rbx + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, mcont
  mwalk:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __managed_destroy_range
  mcont:
    x64.movRegReg rax, r13
    x64.subRegReg rax, r13, r12
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg r9, r14
    x64.callDirect __managed_zero_elems
    x64.movRegReg rax, r8
    x64.storeBaseDispReg.word64 [rbx + 8], r12
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 16
    arm64.movImm x1, 1
    arm64.bl __managed_mem_create
    arm64.movRegReg x19, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#1
  tryerr#2:
    arm64.movImm x0, 1
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  tryok#1:
    arm64.movImm x1, 16
    arm64.movRegReg x0, x19
    arm64.bl __managed_set_length
    arm64.cmp x9, 0
    arm64.b.eq tryok#3
  tryerr#4:
    arm64.movImm x20, 2
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  tryok#3:
    arm64.movImm x1, 8
    arm64.movRegReg x0, x19
    arm64.bl __managed_set_length
    arm64.cmp x9, 0
    arm64.b.eq tryok#6
  tryerr#7:
    arm64.movImm x20, 3
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  tryok#6:
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
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
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-length-publish-and-a-drop-on-a-plain-record-keep-no-registers-across-the-element-walk.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

func @__managed_release {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 40]
    arm64.cmp x0, 0
    arm64.b.eq relbuf
  mwalk:
    arm64.movRegReg x0, x19
    arm64.bl __managed_destroy_range
  relbuf:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, 0
    arm64.b.ge obase
  vbase:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 32]
    arm64.b basejoin
  obase:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq basejoin
  obufprobe:
    arm64.add x1, x19, 56
    arm64.cmp x0, x1
    arm64.b.ne basejoin
  obaseco:
    arm64.movImm x0, 0
  basejoin:
    arm64.cmp x0, 0
    arm64.b.eq freerec
  rcstep:
    arm64.bl __mm_decref
  freerec:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.tailBranch __mm_free
}

func @__managed_destroy_range {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.loadRegBaseDisp.word64 x19, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x20, [x0 + 40]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 24]
    arm64.movRegReg x23, x1
  dtorh:
    arm64.cmp x23, x19
    arm64.b.lt dtorb
  dtordone:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  dtorb:
    arm64.mul x0, x23, x22
    arm64.add x0, x21, x0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.b.eq dtorstep
  dtordo:
    arm64.blr x20
  dtorstep:
    arm64.add x23, x23, 1
    arm64.b dtorh
}

func @__managed_set_length {
  entry:
    arm64.prologue 32
    arm64.cmp x1, 0
    arm64.b.lt badlen
  chkhigh:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 16]
    arm64.cmp x2, 0
    arm64.b.lt slddetach
  sldbuf:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.cmp x2, 0
    arm64.b.eq slddone
  sldshared:
    arm64.sub x2, x2, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne slddetach
  slddone:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 16]
    arm64.cmp x1, x2
    arm64.b.ls pick
  badlen:
    arm64.movImm x0, 0
    arm64.movImm x9, 4
    arm64.epilogue 32
    arm64.ret
  pick:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x1, x2
    arm64.b.ge setlen
  dozero:
    arm64.bl __managed_shrink_length
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.epilogue 32
    arm64.ret
  setlen:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.epilogue 32
    arm64.ret
  slddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.b slddone
}

func @__managed_shrink_length {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.loadRegBaseDisp.word64 x21, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x22, [x19 + 24]
    arm64.loadRegBaseDisp.word64 x23, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x19 + 40]
    arm64.cmp x0, 0
    arm64.b.eq mcont
  mwalk:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __managed_destroy_range
  mcont:
    arm64.sub x2, x21, x20
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x20
    arm64.movRegReg x3, x22
    arm64.bl __managed_zero_elems
    arm64.movRegReg x1, x0
    arm64.storeBaseDispReg.word64 [x19 + 8], x20
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
  __mrt_hwcap_atomics@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 16
    arm64.movImm x1, 1
    arm64.bl __managed_mem_create
    arm64.movRegReg x19, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#1
  tryerr#2:
    arm64.movImm x0, 1
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  tryok#1:
    arm64.movImm x1, 16
    arm64.movRegReg x0, x19
    arm64.bl __managed_set_length
    arm64.cmp x9, 0
    arm64.b.eq tryok#3
  tryerr#4:
    arm64.movImm x20, 2
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  tryok#3:
    arm64.movImm x1, 8
    arm64.movRegReg x0, x19
    arm64.bl __managed_set_length
    arm64.cmp x9, 0
    arm64.b.eq tryok#6
  tryerr#7:
    arm64.movImm x20, 3
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  tryok#6:
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
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
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-length-publish-and-a-drop-on-a-plain-record-keep-no-registers-across-the-element-walk.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

func @__managed_release {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 40]
    arm64.cmp x0, 0
    arm64.b.eq relbuf
  mwalk:
    arm64.movRegReg x0, x19
    arm64.bl __managed_destroy_range
  relbuf:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, 0
    arm64.b.ge obase
  vbase:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 32]
    arm64.b basejoin
  obase:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq basejoin
  obufprobe:
    arm64.add x1, x19, 56
    arm64.cmp x0, x1
    arm64.b.ne basejoin
  obaseco:
    arm64.movImm x0, 0
  basejoin:
    arm64.cmp x0, 0
    arm64.b.eq freerec
  rcstep:
    arm64.bl __mm_decref
  freerec:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.tailBranch __mm_free
}

func @__managed_destroy_range {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.loadRegBaseDisp.word64 x19, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x20, [x0 + 40]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 24]
    arm64.movRegReg x23, x1
  dtorh:
    arm64.cmp x23, x19
    arm64.b.lt dtorb
  dtordone:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  dtorb:
    arm64.mul x0, x23, x22
    arm64.add x0, x21, x0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.b.eq dtorstep
  dtordo:
    arm64.blr x20
  dtorstep:
    arm64.add x23, x23, 1
    arm64.b dtorh
}

func @__managed_set_length {
  entry:
    arm64.prologue 32
    arm64.cmp x1, 0
    arm64.b.lt badlen
  chkhigh:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 16]
    arm64.cmp x2, 0
    arm64.b.lt slddetach
  sldbuf:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.cmp x2, 0
    arm64.b.eq slddone
  sldshared:
    arm64.sub x2, x2, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne slddetach
  slddone:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 16]
    arm64.cmp x1, x2
    arm64.b.ls pick
  badlen:
    arm64.movImm x0, 0
    arm64.movImm x9, 4
    arm64.epilogue 32
    arm64.ret
  pick:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x1, x2
    arm64.b.ge setlen
  dozero:
    arm64.bl __managed_shrink_length
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.epilogue 32
    arm64.ret
  setlen:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.epilogue 32
    arm64.ret
  slddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.b slddone
}

func @__managed_shrink_length {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.loadRegBaseDisp.word64 x21, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x22, [x19 + 24]
    arm64.loadRegBaseDisp.word64 x23, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x19 + 40]
    arm64.cmp x0, 0
    arm64.b.eq mcont
  mwalk:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __managed_destroy_range
  mcont:
    arm64.sub x2, x21, x20
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x20
    arm64.movRegReg x3, x22
    arm64.bl __managed_zero_elems
    arm64.movRegReg x1, x0
    arm64.storeBaseDispReg.word64 [x19 + 8], x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
}
```

<!-- test: a-byte-array-created-at-a-capacity-is-one-sized-create -->
`ByteArray.create(16)` is one call to `__managed_create_sized` with the capacity and the length both 16, and `zeroed: false` is the same call with length 0: neither goes through `__managed_mem_create`, `__managed_set_length` or a grow.
```maxon
function main() returns ExitCode
	let zeroed = ByteArray.create(16)
	var reserved = ByteArray.create(16, zeroed: false)
	reserved.push(3)

	return (zeroed.count() + reserved.count()) as ExitCode
end 'main'
```
```exitcode
17
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
    x64.movRegImm32 rax, 16
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 r9, 0
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_sized
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 16
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 r9, 0
    x64.callDirect __managed_create_sized
    x64.movRegReg r12, r8
    x64.movRegImm32 rdx, 3
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegReg rax, rcx
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.loadRegBaseDisp.word64 rsi, [r12 + 24]
    x64.cmpRegImm32 rsi, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.storeBaseIndexScaleReg.word64 [rcx + rax*8 + 0], rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.jmp __im_cont
  __im_stride:
    x64.cmpRegImm32 rsi, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.leaRegRegReg rcx, rcx, rax
    x64.storeBaseDispReg.byte [rcx + 0], rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.jmp __im_cont
  __im_slow:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_push
  __im_cont:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.loadRegBaseDisp.word64 rcx, [r12 + 8]
    x64.leaRegRegReg r13, rax, rcx
    x64.cmpRegImm32 r13, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r13, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitdrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, r13
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-byte-array-created-at-a-capacity-is-one-sized-create.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    x64.movRegImm32 rax, 16
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 r9, 0
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_sized
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 16
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 r9, 0
    x64.callDirect __managed_create_sized
    x64.movRegReg r12, r8
    x64.movRegImm32 rdx, 3
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegReg rax, rcx
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.loadRegBaseDisp.word64 rsi, [r12 + 24]
    x64.cmpRegImm32 rsi, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.storeBaseIndexScaleReg.word64 [rcx + rax*8 + 0], rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.jmp __im_cont
  __im_stride:
    x64.cmpRegImm32 rsi, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.leaRegRegReg rcx, rcx, rax
    x64.storeBaseDispReg.byte [rcx + 0], rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.jmp __im_cont
  __im_slow:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_push
  __im_cont:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.loadRegBaseDisp.word64 rcx, [r12 + 8]
    x64.leaRegRegReg r13, rax, rcx
    x64.cmpRegImm32 r13, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r13, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitdrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, r13
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-byte-array-created-at-a-capacity-is-one-sized-create.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    arm64.movImm x2, 16
    arm64.movImm x0, 1
    arm64.movImm x3, 0
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_sized
    arm64.movRegReg x19, x0
    arm64.movImm x1, 16
    arm64.movImm x2, 0
    arm64.movImm x0, 1
    arm64.movImm x3, 0
    arm64.bl __managed_create_sized
    arm64.movRegReg x20, x0
    arm64.movImm x1, 3
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x20 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.loadRegBaseDisp.word64 x3, [x20 + 24]
    arm64.cmp x3, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.b __im_cont
  __im_stride:
    arm64.cmp x3, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.add x2, x2, x0
    arm64.storeBaseDispReg.byte [x2 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.b __im_cont
  __im_slow:
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
  __im_cont:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x1, [x20 + 8]
    arm64.add x21, x0, x1
    arm64.cmp x21, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x21, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitdrop:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x21
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-byte-array-created-at-a-capacity-is-one-sized-create.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __mrt_hwcap_atomics@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 16
    arm64.movImm x0, 1
    arm64.movImm x3, 0
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_sized
    arm64.movRegReg x19, x0
    arm64.movImm x1, 16
    arm64.movImm x2, 0
    arm64.movImm x0, 1
    arm64.movImm x3, 0
    arm64.bl __managed_create_sized
    arm64.movRegReg x20, x0
    arm64.movImm x1, 3
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x20 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.loadRegBaseDisp.word64 x3, [x20 + 24]
    arm64.cmp x3, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.b __im_cont
  __im_stride:
    arm64.cmp x3, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.add x2, x2, x0
    arm64.storeBaseDispReg.byte [x2 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.b __im_cont
  __im_slow:
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
  __im_cont:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x1, [x20 + 8]
    arm64.add x21, x0, x1
    arm64.cmp x21, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x21, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitdrop:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x21
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-byte-array-created-at-a-capacity-is-one-sized-create.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: a-runtime-sized-creation-is-one-sized-create -->
`Cells.create(n)` over an element with no count in its type is one call to `__managed_create_fixed` with the capacity and the length both `n`; there is no `__managed_create` followed by a grow or a `setLength`. `make` is reached through a function value so that its body stays in the program.
```maxon
typealias Word = bits(64)
typealias Cells = FixedArray with Word

typealias CellsMaker = function(ElementIndex) returns Cells

function make(n ElementIndex) returns Cells
	return Cells.create(n)
end 'make'

var maker = make as CellsMaker

function main() returns ExitCode
	var cells = maker(6)

	return cells.count() as ExitCode
end 'main'
```
```exitcode
6
```
```TargetIr:x64-windows
data {
  __data_maker@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_console_probe_stdin@32 = i8 0
  __mrt_console_probe_stdout@33 = i8 0
  __mrt_console_probe_stderr@34 = i8 0
  __mrt_program_started@35 = i8 0
}

func @make {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegReg rax, rcx
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 r9, 0
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.leaRegGlobal rax, __data_maker
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_retain
    x64.movRegReg r12, r8
    x64.movRegImm32 rcx, 6
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.movRegReg rdx, rbx
    x64.callReg rax
    x64.movRegReg rbx, r8
    x64.movRegReg rcx, r12
    x64.callDirect __mm_decref
    x64.loadRegBaseDisp.word64 r12, [rbx + 8]
  __il_cont:
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
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-runtime-sized-creation-is-one-sized-create.test:16: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:x64-linux
data {
  __data_maker@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @make {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegReg rax, rcx
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 r9, 0
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.leaRegGlobal rax, __data_maker
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_retain
    x64.movRegReg r12, r8
    x64.movRegImm32 rcx, 6
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.movRegReg rdx, rbx
    x64.callReg rax
    x64.movRegReg rbx, r8
    x64.movRegReg rcx, r12
    x64.callDirect __mm_decref
    x64.loadRegBaseDisp.word64 r12, [rbx + 8]
  __il_cont:
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r12, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-runtime-sized-creation-is-one-sized-create.test:16: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-macos
data {
  __data_maker@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @make {
  entry:
    arm64.prologue 16
    arm64.movRegReg x2, x0
    arm64.movImm x0, 8
    arm64.movImm x3, 0
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaGlobal x0, __data_maker
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.movRegReg x0, x19
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.movImm x0, 6
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.movRegReg x1, x19
    arm64.blr x2
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
  __il_cont:
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
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-runtime-sized-creation-is-one-sized-create.test:16: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __data_maker@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_hwcap_atomics@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @make {
  entry:
    arm64.prologue 16
    arm64.movRegReg x2, x0
    arm64.movImm x0, 8
    arm64.movImm x3, 0
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaGlobal x0, __data_maker
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.movRegReg x0, x19
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.movImm x0, 6
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.movRegReg x1, x19
    arm64.blr x2
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
  __il_cont:
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
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-runtime-sized-creation-is-one-sized-create.test:16: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}
```

<!-- test: a-constant-length-creation-is-one-sized-create-at-the-constant -->
`Cells.create(8)` has the length 8 in its type, so the call is `__managed_create_fixed` with the constants 8 and 8, exactly as `Planes.create()` over a `FixedArray with 8` is. The result is returned as a `Cells`, which a sized array widens to.
```maxon
typealias Word = bits(64)
typealias Cells = FixedArray with Word

typealias CellsMaker = function() returns Cells

function make() returns Cells
	return Cells.create(8)
end 'make'

var maker = make as CellsMaker

function main() returns ExitCode
	var cells = maker()

	return cells.count() as ExitCode
end 'main'
```
```exitcode
8
```
```TargetIr:x64-windows
data {
  __data_maker@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_console_probe_stdin@32 = i8 0
  __mrt_console_probe_stdout@33 = i8 0
  __mrt_console_probe_stderr@34 = i8 0
  __mrt_program_started@35 = i8 0
}

func @make {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rax, 8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 r9, 0
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.leaRegGlobal rax, __data_maker
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_retain
    x64.movRegReg r12, r8
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.movRegReg rcx, rbx
    x64.callReg rax
    x64.movRegReg rbx, r8
    x64.movRegReg rcx, r12
    x64.callDirect __mm_decref
    x64.loadRegBaseDisp.word64 r12, [rbx + 8]
  __il_cont:
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
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-constant-length-creation-is-one-sized-create-at-the-constant.test:16: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:x64-linux
data {
  __data_maker@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @make {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rax, 8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 r9, 0
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.leaRegGlobal rax, __data_maker
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_retain
    x64.movRegReg r12, r8
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.movRegReg rcx, rbx
    x64.callReg rax
    x64.movRegReg rbx, r8
    x64.movRegReg rcx, r12
    x64.callDirect __mm_decref
    x64.loadRegBaseDisp.word64 r12, [rbx + 8]
  __il_cont:
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r12, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-constant-length-creation-is-one-sized-create-at-the-constant.test:16: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-macos
data {
  __data_maker@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @make {
  entry:
    arm64.prologue 16
    arm64.movImm x2, 8
    arm64.movImm x0, 8
    arm64.movImm x3, 0
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaGlobal x0, __data_maker
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.movRegReg x0, x19
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.movRegReg x0, x19
    arm64.blr x1
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
  __il_cont:
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
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-constant-length-creation-is-one-sized-create-at-the-constant.test:16: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __data_maker@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_hwcap_atomics@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @make {
  entry:
    arm64.prologue 16
    arm64.movImm x2, 8
    arm64.movImm x0, 8
    arm64.movImm x3, 0
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaGlobal x0, __data_maker
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.movRegReg x0, x19
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.movRegReg x0, x19
    arm64.blr x1
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
  __il_cont:
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
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-constant-length-creation-is-one-sized-create-at-the-constant.test:16: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}
```
