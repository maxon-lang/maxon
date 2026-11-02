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
Creating a `FixedArray` is one call to `__managed_create_sized`, which hands back a record whose slots are
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
    x64.movRegImm32 rcx, 8
    x64.loadRegBaseDisp.word64 r9, [rdx + 40]
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_sized
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
    x64.movRegImm32 rcx, 8
    x64.loadRegBaseDisp.word64 r9, [rdx + 40]
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_sized
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
    arm64.movImm x0, 8
    arm64.loadRegBaseDisp.word64 x3, [x1 + 40]
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_sized
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
    arm64.movImm x0, 8
    arm64.loadRegBaseDisp.word64 x3, [x1 + 40]
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_sized
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
    x64.leaRegRdata rdx, [rip + __layout_FixedArray_N8_Word]
  __il_body:
    x64.movRegImm32 rcx, 8
    x64.loadRegBaseDisp.word64 r9, [rdx + 40]
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_sized
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
    x64.jmp __il_body#20
  takeask:
    x64.movRegReg rcx, rdx
  __il_body#20:
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
  __il_cont#19:
    x64.movRegReg rax, rcx
    x64.imulRegReg rax, rcx, rsi
  __il_body#21:
    x64.leaRegRegImm32 rdi, rax, 7
    x64.shrRegImm8 rdi, rdi, 3
  __il_cont#18:
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
    x64.leaRegRdata rdx, [rip + __layout_FixedArray_N8_Word]
  __il_body:
    x64.movRegImm32 rcx, 8
    x64.loadRegBaseDisp.word64 r9, [rdx + 40]
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_sized
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
    x64.jmp __il_body#20
  takeask:
    x64.movRegReg rcx, rdx
  __il_body#20:
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
  __il_cont#19:
    x64.movRegReg rax, rcx
    x64.imulRegReg rax, rcx, rsi
  __il_body#21:
    x64.leaRegRegImm32 rdi, rax, 7
    x64.shrRegImm8 rdi, rdi, 3
  __il_cont#18:
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
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 8
    arm64.leaRdata x1, __layout_FixedArray_N8_Word
  __il_body:
    arm64.movImm x0, 8
    arm64.loadRegBaseDisp.word64 x3, [x1 + 40]
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_sized
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
    arm64.b __il_body#20
  takeask:
    arm64.movRegReg x0, x1
  __il_body#20:
    arm64.asr x1, x19, 63
    arm64.movImm x16, 18446744073709551615
    arm64.eor x2, x1, x16
    arm64.movImm x3, 0
    arm64.sub x3, x3, x19
    arm64.lsl x4, x19, 3
    arm64.and x1, x3, x1
    arm64.and x2, x4, x2
    arm64.orr x1, x1, x2
  __il_cont#19:
    arm64.mul x2, x0, x1
  __il_body#21:
    arm64.add x3, x2, 7
    arm64.lsr x23, x3, 3
  __il_cont#18:
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
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 8
    arm64.leaRdata x1, __layout_FixedArray_N8_Word
  __il_body:
    arm64.movImm x0, 8
    arm64.loadRegBaseDisp.word64 x3, [x1 + 40]
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_sized
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
    arm64.b __il_body#20
  takeask:
    arm64.movRegReg x0, x1
  __il_body#20:
    arm64.asr x1, x19, 63
    arm64.movImm x16, 18446744073709551615
    arm64.eor x2, x1, x16
    arm64.movImm x3, 0
    arm64.sub x3, x3, x19
    arm64.lsl x4, x19, 3
    arm64.and x1, x3, x1
    arm64.and x2, x4, x2
    arm64.orr x1, x1, x2
  __il_cont#19:
    arm64.mul x2, x0, x1
  __il_body#21:
    arm64.add x3, x2, 7
    arm64.lsr x23, x3, 3
  __il_cont#18:
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
