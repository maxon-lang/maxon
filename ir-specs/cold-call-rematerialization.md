---
feature: cold-call-rematerialization
status: experimental
keywords: [register-allocator, cold-block, cold-call, rematerialization, spill, frame, copy-on-write, element-store]
category: register-allocator
---

# A constant live across a cold call is re-emitted after it, not saved and reloaded

A cold run brackets the registers its call clobbers. A register that holds a constant (`movRegImm`,
`arm64MovImm`) needs no slot: its reload is the constant's own instruction.

## Tests

<!-- test: stored-constants-are-rematerialized-after-the-detach-call -->
`stamp` and `main` store constants into a fixed array. The guard chain's cold `__im_detach` block calls
`__managed_cow_detach` while a constant is live in a register. The constant is re-emitted after the call:
no `storeSlotReg` writes it and no `loadRegSlot` reads it. In `stamp` only the array record, which is no
constant, is saved and reloaded. In `main` the index and the stored value are both constants, so the block
saves nothing and the frame holds no slot.
```maxon
typealias Word = bits(64)
typealias Planes = FixedArray with 4 Word

typealias Stamper = function(Planes)

function stamp(planes Planes)
	planes.set(1, value: 74565)
	planes.set(2, value: 30)
end 'stamp'

var stamper = stamp as Stamper

function main() returns ExitCode
	var planes = Planes.create()
	planes.set(0, value: 1)
	stamper(planes)

	if planes.get(0) == 1 and planes.get(1) == 74565 and planes.get(2) == 30 'stored'
		return 0
	end 'stored'

	return 1
end 'main'
```
```exitcode
0
```

```TargetIr:x64-windows
data {
  __data_stamper@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_console_probe_stdin@32 = i8 0
  __mrt_console_probe_stdout@33 = i8 0
  __mrt_console_probe_stderr@34 = i8 0
  __mrt_program_started@35 = i8 0
}

func @stamp {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 48
    x64.movRegImm32 rax, 74565
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rsi, rdx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach
  __im_detached#6:
    x64.storeBaseDispReg.word64 [rdx + 8], rax
  __im_cont#1:
    x64.movRegImm32 rax, 30
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
  __im_detached#11:
    x64.storeBaseDispReg.word64 [rcx + 16], rax
  __im_cont#7:
    x64.epilogue 48
    x64.popReg rbp
    x64.ret
  __im_detach:
    x64.storeSlotReg slot0, rcx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 74565
    x64.loadRegSlot rcx, slot0
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.jmp __im_detached#6
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_6]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 48
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
    x64.prologue 40
    x64.movRegImm32 rax, 4
    x64.leaRegRdata rdx, [rip + __layout_FixedArray_N4_Word]
  __il_body:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 72]
    x64.loadRegBaseDisp.word64 r9, [rdx + 40]
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
    x64.movRegReg rbx, r8
  __il_cont:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 1
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rsi, [rbx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rsi, rdx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach
  __im_detached:
    x64.storeBaseIndexScaleReg.word64 [rdx + rax*8 + 0], rcx
  __im_cont#8:
    x64.leaRegGlobal rax, __data_stamper
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callReg rax
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  __im_cont#14:
    x64.cmpRegImm32 rax, 1
    x64.movRegImm32 rax, 0
    x64.jcc notEqual, scmerge#2
  andrhs#1:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
  __im_cont#15:
    x64.cmpRegImm32 rax, 74565
    x64.setccReg equal, rax
  scmerge#2:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit
  andrhs#3:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 16]
  __im_cont#16:
    x64.cmpRegImm32 rax, 30
    x64.setccReg equal, rax
    x64.jmp scmerge#4
  critsplit:
    x64.movRegReg rax, rcx
  scmerge#4:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  stored:
    x64.movRegImm32 r12, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_detach:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 1
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.jmp __im_detached
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_6]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
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
  __data_stamper@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @stamp {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 48
    x64.movRegImm32 rax, 74565
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rsi, rdx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach
  __im_detached#6:
    x64.storeBaseDispReg.word64 [rdx + 8], rax
  __im_cont#1:
    x64.movRegImm32 rax, 30
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
  __im_detached#11:
    x64.storeBaseDispReg.word64 [rcx + 16], rax
  __im_cont#7:
    x64.epilogue 48
    x64.popReg rbp
    x64.ret
  __im_detach:
    x64.storeSlotReg slot0, rcx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 74565
    x64.loadRegSlot rcx, slot0
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.jmp __im_detached#6
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_6]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 48
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
    x64.prologue 40
    x64.movRegImm32 rax, 4
    x64.leaRegRdata rdx, [rip + __layout_FixedArray_N4_Word]
  __il_body:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 72]
    x64.loadRegBaseDisp.word64 r9, [rdx + 40]
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
    x64.movRegReg rbx, r8
  __il_cont:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 1
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rsi, [rbx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rsi, rdx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach
  __im_detached:
    x64.storeBaseIndexScaleReg.word64 [rdx + rax*8 + 0], rcx
  __im_cont#8:
    x64.leaRegGlobal rax, __data_stamper
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callReg rax
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  __im_cont#14:
    x64.cmpRegImm32 rax, 1
    x64.movRegImm32 rax, 0
    x64.jcc notEqual, scmerge#2
  andrhs#1:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
  __im_cont#15:
    x64.cmpRegImm32 rax, 74565
    x64.setccReg equal, rax
  scmerge#2:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit
  andrhs#3:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 16]
  __im_cont#16:
    x64.cmpRegImm32 rax, 30
    x64.setccReg equal, rax
    x64.jmp scmerge#4
  critsplit:
    x64.movRegReg rax, rcx
  scmerge#4:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  stored:
    x64.movRegImm32 r12, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_detach:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 1
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.jmp __im_detached
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_6]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
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
  __data_stamper@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @stamp {
  entry:
    arm64.prologue 32
    arm64.movImm x1, 74565
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach
  __im_detached#6:
    arm64.storeBaseDispReg.word64 [x2 + 8], x1
  __im_cont#1:
    arm64.movImm x1, 30
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  __im_detached#11:
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
  __im_cont#7:
    arm64.epilogue 32
    arm64.ret
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.movImm x1, 74565
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.b __im_detached#6
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 32
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 4
    arm64.leaRdata x1, __layout_FixedArray_N4_Word
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 72]
    arm64.loadRegBaseDisp.word64 x3, [x1 + 40]
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
    arm64.movRegReg x19, x0
  __il_cont:
    arm64.movImm x0, 0
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x3, [x19 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach
  __im_detached:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
  __im_cont#8:
    arm64.leaGlobal x0, __data_stamper
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x2, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.blr x2
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  __im_cont#14:
    arm64.cmp x0, 1
    arm64.movImm x0, 0
    arm64.b.ne scmerge#2
  andrhs#1:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __im_cont#15:
    arm64.movImm x16, 74565
    arm64.cmp x0, x16
    arm64.cset x0, eq
  scmerge#2:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit
  andrhs#3:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 16]
  __im_cont#16:
    arm64.cmp x0, 30
    arm64.cset x0, eq
    arm64.b scmerge#4
  critsplit:
    arm64.movRegReg x0, x1
  scmerge#4:
    arm64.cbz x0, ifcont
  stored:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __im_detach:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.movImm x0, 0
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.b __im_detached
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
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
  __data_stamper@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_hwcap_atomics@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @stamp {
  entry:
    arm64.prologue 32
    arm64.movImm x1, 74565
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach
  __im_detached#6:
    arm64.storeBaseDispReg.word64 [x2 + 8], x1
  __im_cont#1:
    arm64.movImm x1, 30
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  __im_detached#11:
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
  __im_cont#7:
    arm64.epilogue 32
    arm64.ret
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.movImm x1, 74565
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.b __im_detached#6
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 32
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 4
    arm64.leaRdata x1, __layout_FixedArray_N4_Word
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 72]
    arm64.loadRegBaseDisp.word64 x3, [x1 + 40]
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
    arm64.movRegReg x19, x0
  __il_cont:
    arm64.movImm x0, 0
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x3, [x19 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach
  __im_detached:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
  __im_cont#8:
    arm64.leaGlobal x0, __data_stamper
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x2, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.blr x2
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  __im_cont#14:
    arm64.cmp x0, 1
    arm64.movImm x0, 0
    arm64.b.ne scmerge#2
  andrhs#1:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __im_cont#15:
    arm64.movImm x16, 74565
    arm64.cmp x0, x16
    arm64.cset x0, eq
  scmerge#2:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit
  andrhs#3:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 16]
  __im_cont#16:
    arm64.cmp x0, 30
    arm64.cset x0, eq
    arm64.b scmerge#4
  critsplit:
    arm64.movRegReg x0, x1
  scmerge#4:
    arm64.cbz x0, ifcont
  stored:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __im_detach:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.movImm x0, 0
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.b __im_detached
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: a-64-bit-constant-is-rebuilt-in-full-after-the-detach-call -->
`stamp` stores `0x123456789ABCDEF0` into an array a view shares, so the detach arm runs. The constant is
rebuilt after the call by the same instruction that built it: `movRegImm` on x64, `movImm` on arm64
(four 16-bit pieces), with no slot for it.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function stamp(arr WordArray)
	try arr.set(1, value: 0x123456789ABCDEF0) otherwise panic("set: index 1 of a three-element array")
end 'stamp'

function main() returns ExitCode
	var arr = WordArray from [10, 20, 30]
	let view = try arr.slice(0, endIndex: 3) otherwise panic("slice: 0..3 of a three-element array")
	stamp(arr)

	let stored = try arr.get(1) otherwise panic("get: index 1 of a three-element array")
	let kept = try view.get(1) otherwise panic("get: index 1 of a three-element view")

	if stored == 0x123456789ABCDEF0 and kept == 20 'stored'
		return 0
	end 'stored'

	return 1
end 'main'
```
```exitcode
0
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
    x64.prologue 48
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
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 3
    x64.leaRegRdata r9, [rip + __layout_Array_Word]
  __il_body#20:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_slice_opaque
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#21
  tryerr#22:
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rcx, 0
    x64.jmp __il_cont#19
  tryok#21:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.cmpRegImm32 rax, -4
    x64.jcc equal, adoptclone
  exitterminal#23:
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r8
  __il_cont#19:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, tryerr#2
  __il_body#25:
    x64.movRegImm32 rdx, 1
    x64.movRegImm rax, 1311768467463790320
    x64.loadRegBaseDisp.word64 rsi, [rbx + 8]
    x64.cmpRegImm32 rsi, 1
    x64.jcc belowEqual, __im_slow#29
  __im_buffer:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rsi, [rbx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rsi, rdx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach
  __im_detached:
    x64.storeBaseDispReg.word64 [rdx + 8], rax
  __il_cont#24:
    x64.movRegImm32 rax, 1
  __im_load#15:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rdx + rax*8 + 0]
  tryok#3:
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegImm32 rsi, 1
    x64.jcc belowEqual, __im_slow#17
  __im_load#18:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
  tryok#5:
    x64.movRegImm rsi, 1311768467463790320
    x64.cmpRegReg rax, rsi
    x64.movRegImm32 rax, 0
    x64.jcc notEqual, scmerge
  andrhs:
    x64.cmpRegImm32 rdx, 20
    x64.setccReg equal, rax
  scmerge:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  stored:
    x64.movRegImm32 r12, 0
    x64.callDirect __managed_decref
    x64.jmp exitdrop
  ifcont:
    x64.movRegImm32 r12, 1
    x64.callDirect __managed_decref
  exitdrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitterminal#10:
    x64.movRegReg r8, r12
    x64.epilogue 48
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  adoptclone:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_clone
    x64.jmp exitterminal#23
  __im_slow#29:
    x64.storeSlotReg slot0, rcx
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rcx, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#27
    x64.jmp __il_cont#24
  __im_detach:
    x64.storeSlotReg slot0, rcx
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm rax, 1311768467463790320
    x64.loadRegSlot rcx, slot0
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.jmp __im_detached
  __im_slow#17:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, rcx
    x64.callDirect __managed_get
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot rcx, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#6
  critsplit:
    x64.movRegReg rdx, r8
    x64.jmp tryok#5
  tryerr#2:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at a-64-bit-constant-is-rebuilt-in-full-after-the-detach-call.test:11: slice: 0..3 of a three-element array\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 48
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#27:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-64-bit-constant-is-rebuilt-in-full-after-the-detach-call.test:6: set: index 1 of a three-element array\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 48
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_8]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 48
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#6:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-64-bit-constant-is-rebuilt-in-full-after-the-detach-call.test:15: get: index 1 of a three-element view\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 48
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
    x64.prologue 48
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
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 3
    x64.leaRegRdata r9, [rip + __layout_Array_Word]
  __il_body#20:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_slice_opaque
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#21
  tryerr#22:
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rcx, 0
    x64.jmp __il_cont#19
  tryok#21:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.cmpRegImm32 rax, -4
    x64.jcc equal, adoptclone
  exitterminal#23:
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r8
  __il_cont#19:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, tryerr#2
  __il_body#25:
    x64.movRegImm32 rdx, 1
    x64.movRegImm rax, 1311768467463790320
    x64.loadRegBaseDisp.word64 rsi, [rbx + 8]
    x64.cmpRegImm32 rsi, 1
    x64.jcc belowEqual, __im_slow#29
  __im_buffer:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rsi, [rbx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rsi, rdx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach
  __im_detached:
    x64.storeBaseDispReg.word64 [rdx + 8], rax
  __il_cont#24:
    x64.movRegImm32 rax, 1
  __im_load#15:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rdx + rax*8 + 0]
  tryok#3:
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegImm32 rsi, 1
    x64.jcc belowEqual, __im_slow#17
  __im_load#18:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
  tryok#5:
    x64.movRegImm rsi, 1311768467463790320
    x64.cmpRegReg rax, rsi
    x64.movRegImm32 rax, 0
    x64.jcc notEqual, scmerge
  andrhs:
    x64.cmpRegImm32 rdx, 20
    x64.setccReg equal, rax
  scmerge:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  stored:
    x64.movRegImm32 r12, 0
    x64.callDirect __managed_decref
    x64.jmp exitdrop
  ifcont:
    x64.movRegImm32 r12, 1
    x64.callDirect __managed_decref
  exitdrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitterminal#10:
    x64.movRegReg r8, r12
    x64.epilogue 48
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  adoptclone:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_clone
    x64.jmp exitterminal#23
  __im_slow#29:
    x64.storeSlotReg slot0, rcx
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rcx, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#27
    x64.jmp __il_cont#24
  __im_detach:
    x64.storeSlotReg slot0, rcx
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm rax, 1311768467463790320
    x64.loadRegSlot rcx, slot0
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.jmp __im_detached
  __im_slow#17:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, rcx
    x64.callDirect __managed_get
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot rcx, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#6
  critsplit:
    x64.movRegReg rdx, r8
    x64.jmp tryok#5
  tryerr#2:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at a-64-bit-constant-is-rebuilt-in-full-after-the-detach-call.test:11: slice: 0..3 of a three-element array\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 48
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#27:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-64-bit-constant-is-rebuilt-in-full-after-the-detach-call.test:6: set: index 1 of a three-element array\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 48
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_8]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 48
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#6:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-64-bit-constant-is-rebuilt-in-full-after-the-detach-call.test:15: get: index 1 of a three-element view\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 48
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
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
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
    arm64.movImm x0, 8
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movImm x1, 0
    arm64.movImm x2, 3
    arm64.leaRdata x3, __layout_Array_Word
  __il_body#20:
    arm64.movRegReg x0, x19
    arm64.bl __managed_slice_opaque
    arm64.cmp x9, 0
    arm64.b.eq tryok#21
  tryerr#22:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.xchgRegReg x0, x1
    arm64.b __il_cont#19
  tryok#21:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.cmp x1, -4
    arm64.b.eq adoptclone
  exitterminal#23:
    arm64.movImm x1, 0
  __il_cont#19:
    arm64.cmp x1, 0
    arm64.b.ne tryerr#2
  __il_body#25:
    arm64.movImm x1, 1
    arm64.movImm x2, 1311768467463790320
    arm64.loadRegBaseDisp.word64 x3, [x19 + 8]
    arm64.cmp x3, 1
    arm64.b.ls __im_slow#29
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x3, [x19 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x3, x1, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach
  __im_detached:
    arm64.storeBaseDispReg.word64 [x1 + 8], x2
  __il_cont#24:
    arm64.movImm x1, 1
  __im_load#15:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.loadRegBaseIndexScale.word64 x1, [x2 + x1*8 + 0]
  tryok#3:
    arm64.movImm x2, 1
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 1
    arm64.b.ls __im_slow#17
  __im_load#18:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
  tryok#5:
    arm64.movImm x3, 1311768467463790320
    arm64.cmp x1, x3
    arm64.movImm x1, 0
    arm64.b.ne scmerge
  andrhs:
    arm64.cmp x2, 20
    arm64.cset x1, eq
  scmerge:
    arm64.cbz x1, ifcont
  stored:
    arm64.movImm x20, 0
    arm64.bl __managed_decref
    arm64.b exitdrop
  ifcont:
    arm64.movImm x20, 1
    arm64.bl __managed_decref
  exitdrop:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal#10:
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.epilogue 48
    arm64.ret
  adoptclone:
    arm64.bl __managed_clone
    arm64.b exitterminal#23
  __im_slow#29:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_index_refused
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#27
    arm64.b __il_cont#24
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.movImm x2, 1311768467463790320
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.b __im_detached
  __im_slow#17:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x1, x2
    arm64.bl __managed_get
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#6
    arm64.b tryok#5
  tryerr#2:
    arm64.leaRdata x0, __str_blob_2  ; "panic at a-64-bit-constant-is-rebuilt-in-full-after-the-detach-call.test:11: slice: 0..3 of a three-element array\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.epilogue 48
    arm64.ret
  tryerr#27:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-64-bit-constant-is-rebuilt-in-full-after-the-detach-call.test:6: set: index 1 of a three-element array\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.epilogue 48
    arm64.ret
  __im_missing:
    arm64.leaRdata x0, __str_blob_8  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.epilogue 48
    arm64.ret
  tryerr#6:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-64-bit-constant-is-rebuilt-in-full-after-the-detach-call.test:15: get: index 1 of a three-element view\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
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
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
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
    arm64.movImm x0, 8
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movImm x1, 0
    arm64.movImm x2, 3
    arm64.leaRdata x3, __layout_Array_Word
  __il_body#20:
    arm64.movRegReg x0, x19
    arm64.bl __managed_slice_opaque
    arm64.cmp x9, 0
    arm64.b.eq tryok#21
  tryerr#22:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.xchgRegReg x0, x1
    arm64.b __il_cont#19
  tryok#21:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.cmp x1, -4
    arm64.b.eq adoptclone
  exitterminal#23:
    arm64.movImm x1, 0
  __il_cont#19:
    arm64.cmp x1, 0
    arm64.b.ne tryerr#2
  __il_body#25:
    arm64.movImm x1, 1
    arm64.movImm x2, 1311768467463790320
    arm64.loadRegBaseDisp.word64 x3, [x19 + 8]
    arm64.cmp x3, 1
    arm64.b.ls __im_slow#29
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x3, [x19 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x3, x1, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach
  __im_detached:
    arm64.storeBaseDispReg.word64 [x1 + 8], x2
  __il_cont#24:
    arm64.movImm x1, 1
  __im_load#15:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.loadRegBaseIndexScale.word64 x1, [x2 + x1*8 + 0]
  tryok#3:
    arm64.movImm x2, 1
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 1
    arm64.b.ls __im_slow#17
  __im_load#18:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
  tryok#5:
    arm64.movImm x3, 1311768467463790320
    arm64.cmp x1, x3
    arm64.movImm x1, 0
    arm64.b.ne scmerge
  andrhs:
    arm64.cmp x2, 20
    arm64.cset x1, eq
  scmerge:
    arm64.cbz x1, ifcont
  stored:
    arm64.movImm x20, 0
    arm64.bl __managed_decref
    arm64.b exitdrop
  ifcont:
    arm64.movImm x20, 1
    arm64.bl __managed_decref
  exitdrop:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal#10:
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.epilogue 48
    arm64.ret
  adoptclone:
    arm64.bl __managed_clone
    arm64.b exitterminal#23
  __im_slow#29:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_index_refused
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#27
    arm64.b __il_cont#24
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.movImm x2, 1311768467463790320
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.b __im_detached
  __im_slow#17:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x1, x2
    arm64.bl __managed_get
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#6
    arm64.b tryok#5
  tryerr#2:
    arm64.leaRdata x0, __str_blob_2  ; "panic at a-64-bit-constant-is-rebuilt-in-full-after-the-detach-call.test:11: slice: 0..3 of a three-element array\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.epilogue 48
    arm64.ret
  tryerr#27:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-64-bit-constant-is-rebuilt-in-full-after-the-detach-call.test:6: set: index 1 of a three-element array\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.epilogue 48
    arm64.ret
  __im_missing:
    arm64.leaRdata x0, __str_blob_8  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.epilogue 48
    arm64.ret
  tryerr#6:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-64-bit-constant-is-rebuilt-in-full-after-the-detach-call.test:15: get: index 1 of a three-element view\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.epilogue 48
    arm64.ret
}
```
