---
feature: element-store-detach
status: experimental
keywords: [optimizer, codegen, array, fixed array, copy-on-write, guard, store, inline, managed-memory]
category: codegen
---

# Consecutive element stores on one array: one guard chain, one store after the join

An inlined `set` asks whether the buffer needs a private copy. A guard that fails calls
`__managed_cow_detach` on a cold block and rejoins, so the stored value has ONE use, the store after the
join, and a store on a record whose earlier store already proved the shape asks nothing but whether the
buffer exists.

## Tests

<!-- test: proven-stores-on-one-fixed-array-share-one-guard-chain -->
`mix` stores four words computed from the two it read, at proven indices. The first store asks the three
shape guards; each later store asks only whether the buffer exists, then loads it and stores. No store has a
second consumer for its value, and no block holds a `__managed_set_unchecked` call.
```maxon
typealias Word = bits(64)
typealias Planes = FixedArray with 4 Word

typealias PlaneMixer = function(mutable Planes)

function mix(planes mutable Planes)
	let a = planes.get(0)
	let b = planes.get(1)

	planes.set(0, value: a xor b)
	planes.set(1, value: a xor 5)
	planes.set(2, value: b xor 6)
	planes.set(3, value: (a xor b) xor 7)
end 'mix'

type Mixers
	export var mixer as PlaneMixer

	static function create() returns Self
		return Self{mixer: mix as PlaneMixer}
	end 'create'
end 'Mixers'

function main() returns ExitCode
	let mixers = Mixers.create()
	let planes = Planes.create()
	planes.set(0, value: 1)
	planes.set(1, value: 2)
	mixers.mixer(planes)

	if planes.get(0) == 3 and planes.get(1) == 4 and planes.get(2) == 4 and planes.get(3) == 4 'stored'
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

func @mix {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  __im_cont#1:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
  __im_cont#2:
    x64.movRegReg rsi, rax
    x64.xorRegReg rsi, rax, rdx
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 r8, [rcx + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 r8, rdi, -16
    x64.loadRegBaseDisp.word64 r8, [r8 + 8]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, __im_detach
  __im_detached#8:
    x64.storeBaseDispReg.word64 [rdi + 0], rsi
  __im_cont#3:
    x64.xorRegImm32 rax, rax, 5
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
  __im_detached#13:
    x64.storeBaseDispReg.word64 [rdi + 8], rax
  __im_cont#9:
    x64.xorRegImm32 rdx, rdx, 6
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
  __im_detached#18:
    x64.storeBaseDispReg.word64 [rax + 16], rdx
  __im_cont#14:
    x64.xorRegImm32 rsi, rsi, 7
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
  __im_detached#23:
    x64.storeBaseDispReg.word64 [rax + 24], rsi
  __im_cont#19:
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_detach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.jmp __im_detached#8
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_6]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
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
    x64.prologue 32
  __il_body#28:
    x64.leaRegRdata rbx, [rip + __fnrec_mix]
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
  __il_cont#27:
    x64.movRegImm32 rax, 4
    x64.leaRegRdata rdx, [rip + __layout_FixedArray_N4_Word]
  __il_body#29:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 72]
    x64.loadRegBaseDisp.word64 r9, [rdx + 40]
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
    x64.movRegReg rbx, r8
  __il_cont#26:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 1
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __im_missing
  __im_owned#14:
    x64.loadRegBaseDisp.word64 rsi, [rbx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach#13
  __im_viewed#15:
    x64.leaRegRegImm32 rsi, rdx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach#13
  __im_detached#16:
    x64.storeBaseIndexScaleReg.word64 [rdx + rax*8 + 0], rcx
  __im_cont#11:
    x64.movRegImm32 rax, 2
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_missing
  __im_owned#19:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __im_detach#18
  __im_viewed#20:
    x64.leaRegRegImm32 rdx, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_detach#18
  __im_detached#21:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  __im_cont#17:
    x64.loadRegBaseDisp.word64 r13, [r12 + 0]
    x64.movRegReg rcx, r13
    x64.callDirect __mm_retain
    x64.movRegReg r14, r8
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callReg rax
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  __im_cont#22:
    x64.cmpRegImm32 rax, 3
    x64.movRegImm32 rax, 0
    x64.jcc notEqual, scmerge#2
  andrhs#1:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
  __im_cont#23:
    x64.cmpRegImm32 rax, 4
    x64.setccReg equal, rax
  scmerge#2:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#30
  andrhs#3:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 16]
  __im_cont#24:
    x64.cmpRegImm32 rax, 4
    x64.setccReg equal, rax
    x64.jmp scmerge#4
  critsplit#30:
    x64.movRegReg rax, rcx
  scmerge#4:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#31
  andrhs#5:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 24]
  __im_cont#25:
    x64.cmpRegImm32 rax, 4
    x64.setccReg equal, rax
    x64.jmp scmerge#6
  critsplit#31:
    x64.movRegReg rax, rcx
  scmerge#6:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  stored:
    x64.movRegImm32 r13, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.jmp exitdrop
  ifcont:
    x64.movRegImm32 r13, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitdrop:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_Mixers
  exitterminal:
    x64.movRegReg r8, r13
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_detach#13:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 1
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.jmp __im_detached#16
  __im_detach#18:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 2
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.jmp __im_detached#21
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_6]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
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

func @mix {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  __im_cont#1:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
  __im_cont#2:
    x64.movRegReg rsi, rax
    x64.xorRegReg rsi, rax, rdx
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 r8, [rcx + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 r8, rdi, -16
    x64.loadRegBaseDisp.word64 r8, [r8 + 8]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, __im_detach
  __im_detached#8:
    x64.storeBaseDispReg.word64 [rdi + 0], rsi
  __im_cont#3:
    x64.xorRegImm32 rax, rax, 5
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
  __im_detached#13:
    x64.storeBaseDispReg.word64 [rdi + 8], rax
  __im_cont#9:
    x64.xorRegImm32 rdx, rdx, 6
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
  __im_detached#18:
    x64.storeBaseDispReg.word64 [rax + 16], rdx
  __im_cont#14:
    x64.xorRegImm32 rsi, rsi, 7
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
  __im_detached#23:
    x64.storeBaseDispReg.word64 [rax + 24], rsi
  __im_cont#19:
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_detach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.jmp __im_detached#8
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_6]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
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
    x64.prologue 32
  __il_body#28:
    x64.leaRegRdata rbx, [rip + __fnrec_mix]
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
  __il_cont#27:
    x64.movRegImm32 rax, 4
    x64.leaRegRdata rdx, [rip + __layout_FixedArray_N4_Word]
  __il_body#29:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 72]
    x64.loadRegBaseDisp.word64 r9, [rdx + 40]
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
    x64.movRegReg rbx, r8
  __il_cont#26:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 1
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __im_missing
  __im_owned#14:
    x64.loadRegBaseDisp.word64 rsi, [rbx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach#13
  __im_viewed#15:
    x64.leaRegRegImm32 rsi, rdx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach#13
  __im_detached#16:
    x64.storeBaseIndexScaleReg.word64 [rdx + rax*8 + 0], rcx
  __im_cont#11:
    x64.movRegImm32 rax, 2
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_missing
  __im_owned#19:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __im_detach#18
  __im_viewed#20:
    x64.leaRegRegImm32 rdx, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_detach#18
  __im_detached#21:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  __im_cont#17:
    x64.loadRegBaseDisp.word64 r13, [r12 + 0]
    x64.movRegReg rcx, r13
    x64.callDirect __mm_retain
    x64.movRegReg r14, r8
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callReg rax
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  __im_cont#22:
    x64.cmpRegImm32 rax, 3
    x64.movRegImm32 rax, 0
    x64.jcc notEqual, scmerge#2
  andrhs#1:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
  __im_cont#23:
    x64.cmpRegImm32 rax, 4
    x64.setccReg equal, rax
  scmerge#2:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#30
  andrhs#3:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 16]
  __im_cont#24:
    x64.cmpRegImm32 rax, 4
    x64.setccReg equal, rax
    x64.jmp scmerge#4
  critsplit#30:
    x64.movRegReg rax, rcx
  scmerge#4:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#31
  andrhs#5:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 24]
  __im_cont#25:
    x64.cmpRegImm32 rax, 4
    x64.setccReg equal, rax
    x64.jmp scmerge#6
  critsplit#31:
    x64.movRegReg rax, rcx
  scmerge#6:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  stored:
    x64.movRegImm32 r13, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.jmp exitdrop
  ifcont:
    x64.movRegImm32 r13, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitdrop:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_Mixers
  exitterminal:
    x64.movRegReg r8, r13
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_detach#13:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 1
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.jmp __im_detached#16
  __im_detach#18:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 2
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.jmp __im_detached#21
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_6]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
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

func @mix {
  entry:
    arm64.prologue 48
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
  __im_cont#1:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
  __im_cont#2:
    arm64.eor x3, x1, x2
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.cmp x4, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 16]
    arm64.cmp x5, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x5, x4, 16
    arm64.loadRegBaseDisp.word64 x5, [x5 + 8]
    arm64.cmp x5, 0
    arm64.b.ne __im_detach
  __im_detached#8:
    arm64.storeBaseDispReg.word64 [x4 + 0], x3
  __im_cont#3:
    arm64.movImm x16, 5
    arm64.eor x1, x1, x16
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
  __im_detached#13:
    arm64.storeBaseDispReg.word64 [x4 + 8], x1
  __im_cont#9:
    arm64.eor x1, x2, 6
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
  __im_detached#18:
    arm64.storeBaseDispReg.word64 [x2 + 16], x1
  __im_cont#14:
    arm64.eor x1, x3, 7
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  __im_detached#23:
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
  __im_cont#19:
    arm64.epilogue 48
    arm64.ret
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.bl __managed_cow_detach
    arm64.movRegReg x4, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.b __im_detached#8
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#28:
    arm64.leaRdata x19, __fnrec_mix
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
  __il_cont#27:
    arm64.movImm x2, 4
    arm64.leaRdata x1, __layout_FixedArray_N4_Word
  __il_body#29:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 72]
    arm64.loadRegBaseDisp.word64 x3, [x1 + 40]
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
    arm64.movRegReg x19, x0
  __il_cont#26:
    arm64.movImm x0, 0
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_missing
  __im_owned#14:
    arm64.loadRegBaseDisp.word64 x3, [x19 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach#13
  __im_viewed#15:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach#13
  __im_detached#16:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
  __im_cont#11:
    arm64.movImm x0, 2
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_missing
  __im_owned#19:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_detach#18
  __im_viewed#20:
    arm64.sub x2, x1, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne __im_detach#18
  __im_detached#21:
    arm64.storeBaseDispReg.word64 [x1 + 8], x0
  __im_cont#17:
    arm64.loadRegBaseDisp.word64 x21, [x20 + 0]
    arm64.movRegReg x0, x21
    arm64.bl __mm_retain
    arm64.movRegReg x22, x0
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.blr x2
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  __im_cont#22:
    arm64.cmp x0, 3
    arm64.movImm x0, 0
    arm64.b.ne scmerge#2
  andrhs#1:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __im_cont#23:
    arm64.cmp x0, 4
    arm64.cset x0, eq
  scmerge#2:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#30
  andrhs#3:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 16]
  __im_cont#24:
    arm64.cmp x0, 4
    arm64.cset x0, eq
    arm64.b scmerge#4
  critsplit#30:
    arm64.movRegReg x0, x1
  scmerge#4:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#31
  andrhs#5:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 24]
  __im_cont#25:
    arm64.cmp x0, 4
    arm64.cset x0, eq
    arm64.b scmerge#6
  critsplit#31:
    arm64.movRegReg x0, x1
  scmerge#6:
    arm64.cbz x0, ifcont
  stored:
    arm64.movImm x21, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.b exitdrop
  ifcont:
    arm64.movImm x21, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitdrop:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Mixers
  exitterminal:
    arm64.movRegReg x0, x21
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  __im_detach#13:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.movImm x0, 0
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.b __im_detached#16
  __im_detach#18:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x1, x0
    arm64.movImm x0, 2
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.b __im_detached#21
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
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
  __mrt_hwcap_atomics@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @mix {
  entry:
    arm64.prologue 48
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
  __im_cont#1:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
  __im_cont#2:
    arm64.eor x3, x1, x2
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.cmp x4, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 16]
    arm64.cmp x5, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x5, x4, 16
    arm64.loadRegBaseDisp.word64 x5, [x5 + 8]
    arm64.cmp x5, 0
    arm64.b.ne __im_detach
  __im_detached#8:
    arm64.storeBaseDispReg.word64 [x4 + 0], x3
  __im_cont#3:
    arm64.movImm x16, 5
    arm64.eor x1, x1, x16
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
  __im_detached#13:
    arm64.storeBaseDispReg.word64 [x4 + 8], x1
  __im_cont#9:
    arm64.eor x1, x2, 6
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
  __im_detached#18:
    arm64.storeBaseDispReg.word64 [x2 + 16], x1
  __im_cont#14:
    arm64.eor x1, x3, 7
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  __im_detached#23:
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
  __im_cont#19:
    arm64.epilogue 48
    arm64.ret
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.bl __managed_cow_detach
    arm64.movRegReg x4, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.b __im_detached#8
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#28:
    arm64.leaRdata x19, __fnrec_mix
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
  __il_cont#27:
    arm64.movImm x2, 4
    arm64.leaRdata x1, __layout_FixedArray_N4_Word
  __il_body#29:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 72]
    arm64.loadRegBaseDisp.word64 x3, [x1 + 40]
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
    arm64.movRegReg x19, x0
  __il_cont#26:
    arm64.movImm x0, 0
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_missing
  __im_owned#14:
    arm64.loadRegBaseDisp.word64 x3, [x19 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach#13
  __im_viewed#15:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach#13
  __im_detached#16:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
  __im_cont#11:
    arm64.movImm x0, 2
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_missing
  __im_owned#19:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_detach#18
  __im_viewed#20:
    arm64.sub x2, x1, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne __im_detach#18
  __im_detached#21:
    arm64.storeBaseDispReg.word64 [x1 + 8], x0
  __im_cont#17:
    arm64.loadRegBaseDisp.word64 x21, [x20 + 0]
    arm64.movRegReg x0, x21
    arm64.bl __mm_retain
    arm64.movRegReg x22, x0
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.blr x2
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  __im_cont#22:
    arm64.cmp x0, 3
    arm64.movImm x0, 0
    arm64.b.ne scmerge#2
  andrhs#1:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __im_cont#23:
    arm64.cmp x0, 4
    arm64.cset x0, eq
  scmerge#2:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#30
  andrhs#3:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 16]
  __im_cont#24:
    arm64.cmp x0, 4
    arm64.cset x0, eq
    arm64.b scmerge#4
  critsplit#30:
    arm64.movRegReg x0, x1
  scmerge#4:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#31
  andrhs#5:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 24]
  __im_cont#25:
    arm64.cmp x0, 4
    arm64.cset x0, eq
    arm64.b scmerge#6
  critsplit#31:
    arm64.movRegReg x0, x1
  scmerge#6:
    arm64.cbz x0, ifcont
  stored:
    arm64.movImm x21, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.b exitdrop
  ifcont:
    arm64.movImm x21, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitdrop:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Mixers
  exitterminal:
    arm64.movRegReg x0, x21
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  __im_detach#13:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.movImm x0, 0
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.b __im_detached#16
  __im_detach#18:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x1, x0
    arm64.movImm x0, 2
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.b __im_detached#21
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: checked-stores-on-one-array-reach-the-runtime-only-out-of-range -->
Two checked stores at two indices. Each asks its own bound, whose refusal alone reaches
`__managed_index_refused`; the shape guards send a failing buffer to the detach and rejoin.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word
typealias Slot = int(0 to u64.max) implements ElementIndex

typealias Putter = function(mutable WordArray, Slot, Slot) returns bool

function put(words mutable WordArray, first Slot, second Slot) returns bool
	try words.set(first, value: 7) otherwise return false
	try words.set(second, value: 9) otherwise return false

	return true
end 'put'

type Putters
	export var putter as Putter

	static function create() returns Self
		return Self{putter: put as Putter}
	end 'create'
end 'Putters'

function main() returns ExitCode
	let putters = Putters.create()
	let words = WordArray from [0, 0, 0]

	if not putters.putter(words, 0, 2) 'refused'
		return 1
	end 'refused'

	if putters.putter(words, 1, 5) 'accepted'
		return 2
	end 'accepted'

	let first = try words.get(0) otherwise return 3
	let last = try words.get(2) otherwise return 4
	let middle = try words.get(1) otherwise return 5

	if first == 7 and last == 9 and middle == 7 'stored'
		return 0
	end 'stored'

	return 6
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

func @put {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.movRegImm32 rsi, 7
    x64.loadRegBaseDisp.word64 rdi, [rcx + 8]
    x64.cmpRegReg rdx, rdi
    x64.jcc aboveEqual, __im_slow#6
    x64.jmp __im_buffer#7
  tryerr#2:
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_buffer#7:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 r8, [rcx + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 r8, rdi, -16
    x64.loadRegBaseDisp.word64 r8, [r8 + 8]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, __im_detach
  __im_detached#12:
    x64.storeBaseIndexScaleReg.word64 [rdi + rdx*8 + 0], rsi
  tryok#1:
    x64.movRegImm32 rdx, 9
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegReg rax, rsi
    x64.jcc aboveEqual, __im_slow#14
    x64.jmp __im_buffer#15
  tryerr#4:
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_buffer#15:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
  __im_detached#19:
    x64.storeBaseIndexScaleReg.word64 [rcx + rax*8 + 0], rdx
  tryok#3:
    x64.movRegImm32 r8, 1
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_slow#6:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#2
    x64.jmp tryok#1
  __im_detach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.movRegImm32 rsi, 7
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.jmp __im_detached#12
  __im_slow#14:
    x64.movRegReg rdx, rax
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#4
    x64.jmp tryok#3
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
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
    x64.prologue 48
  __il_body:
    x64.leaRegRdata rbx, [rip + __fnrec_put]
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
  __il_cont:
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
    x64.loadRegBaseDisp.word64 r13, [r12 + 0]
    x64.movRegReg rcx, r13
    x64.callDirect __mm_retain
    x64.movRegReg r14, r8
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 2
    x64.loadRegBaseDisp.word64 rsi, [r13 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg r9, r13
    x64.callReg rsi
    x64.movRegReg r13, r8
    x64.xorRegImm32 r13, r8, 1
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
    x64.cmpRegImm32 r13, 0
    x64.jcc equal, ifcont#4
  refused:
    x64.movRegImm32 r13, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.jmp exitdrop
  ifcont#4:
    x64.loadRegBaseDisp.word64 r13, [r12 + 0]
    x64.movRegReg rcx, r13
    x64.callDirect __mm_retain
    x64.movRegReg r14, r8
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 5
    x64.loadRegBaseDisp.word64 rsi, [r13 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg r9, r13
    x64.callReg rsi
    x64.movRegReg r13, r8
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
    x64.cmpRegImm32 r13, 0
    x64.jcc equal, ifcont#6
  accepted:
    x64.movRegImm32 r13, 2
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.jmp exitdrop
  ifcont#6:
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#20
    x64.jmp __im_load#21
  tryerr#8:
    x64.movRegImm32 r13, 3
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.jmp exitdrop
  __im_load#21:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  tryok#7:
    x64.movRegImm32 rdx, 2
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegImm32 rcx, 2
    x64.jcc belowEqual, __im_slow#23
    x64.jmp __im_load#24
  tryerr#10:
    x64.movRegImm32 r13, 4
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.jmp exitdrop
  __im_load#24:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 16]
  __im_load#27:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
  tryok#11:
    x64.cmpRegImm32 rax, 7
    x64.movRegImm32 rax, 0
    x64.jcc notEqual, scmerge#14
  andrhs#13:
    x64.cmpRegImm32 rcx, 9
    x64.setccReg equal, rax
  scmerge#14:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#31
  andrhs#15:
    x64.cmpRegImm32 rdx, 7
    x64.setccReg equal, rax
    x64.jmp scmerge#16
  critsplit#31:
    x64.movRegReg rax, rcx
  scmerge#16:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#18
  stored:
    x64.movRegImm32 r13, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.jmp exitdrop
  ifcont#18:
    x64.movRegImm32 r13, 6
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitdrop:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_Putters
  exitterminal:
    x64.movRegReg r8, r13
    x64.epilogue 48
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#20:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#8
  critsplit#32:
    x64.movRegReg rax, r8
    x64.jmp tryok#7
  __im_slow#23:
    x64.storeSlotReg slot0, rax
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.loadRegSlot rax, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#10
  critsplit#33:
    x64.movRegReg rcx, r8
    x64.jmp __im_load#27
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

func @put {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.movRegImm32 rsi, 7
    x64.loadRegBaseDisp.word64 rdi, [rcx + 8]
    x64.cmpRegReg rdx, rdi
    x64.jcc aboveEqual, __im_slow#6
    x64.jmp __im_buffer#7
  tryerr#2:
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_buffer#7:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 r8, [rcx + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 r8, rdi, -16
    x64.loadRegBaseDisp.word64 r8, [r8 + 8]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, __im_detach
  __im_detached#12:
    x64.storeBaseIndexScaleReg.word64 [rdi + rdx*8 + 0], rsi
  tryok#1:
    x64.movRegImm32 rdx, 9
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegReg rax, rsi
    x64.jcc aboveEqual, __im_slow#14
    x64.jmp __im_buffer#15
  tryerr#4:
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_buffer#15:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
  __im_detached#19:
    x64.storeBaseIndexScaleReg.word64 [rcx + rax*8 + 0], rdx
  tryok#3:
    x64.movRegImm32 r8, 1
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_slow#6:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#2
    x64.jmp tryok#1
  __im_detach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.movRegImm32 rsi, 7
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.jmp __im_detached#12
  __im_slow#14:
    x64.movRegReg rdx, rax
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#4
    x64.jmp tryok#3
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
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
    x64.prologue 48
  __il_body:
    x64.leaRegRdata rbx, [rip + __fnrec_put]
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
  __il_cont:
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
    x64.loadRegBaseDisp.word64 r13, [r12 + 0]
    x64.movRegReg rcx, r13
    x64.callDirect __mm_retain
    x64.movRegReg r14, r8
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 2
    x64.loadRegBaseDisp.word64 rsi, [r13 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg r9, r13
    x64.callReg rsi
    x64.movRegReg r13, r8
    x64.xorRegImm32 r13, r8, 1
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
    x64.cmpRegImm32 r13, 0
    x64.jcc equal, ifcont#4
  refused:
    x64.movRegImm32 r13, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.jmp exitdrop
  ifcont#4:
    x64.loadRegBaseDisp.word64 r13, [r12 + 0]
    x64.movRegReg rcx, r13
    x64.callDirect __mm_retain
    x64.movRegReg r14, r8
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 5
    x64.loadRegBaseDisp.word64 rsi, [r13 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg r9, r13
    x64.callReg rsi
    x64.movRegReg r13, r8
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
    x64.cmpRegImm32 r13, 0
    x64.jcc equal, ifcont#6
  accepted:
    x64.movRegImm32 r13, 2
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.jmp exitdrop
  ifcont#6:
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#20
    x64.jmp __im_load#21
  tryerr#8:
    x64.movRegImm32 r13, 3
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.jmp exitdrop
  __im_load#21:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  tryok#7:
    x64.movRegImm32 rdx, 2
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegImm32 rcx, 2
    x64.jcc belowEqual, __im_slow#23
    x64.jmp __im_load#24
  tryerr#10:
    x64.movRegImm32 r13, 4
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.jmp exitdrop
  __im_load#24:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 16]
  __im_load#27:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
  tryok#11:
    x64.cmpRegImm32 rax, 7
    x64.movRegImm32 rax, 0
    x64.jcc notEqual, scmerge#14
  andrhs#13:
    x64.cmpRegImm32 rcx, 9
    x64.setccReg equal, rax
  scmerge#14:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#31
  andrhs#15:
    x64.cmpRegImm32 rdx, 7
    x64.setccReg equal, rax
    x64.jmp scmerge#16
  critsplit#31:
    x64.movRegReg rax, rcx
  scmerge#16:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#18
  stored:
    x64.movRegImm32 r13, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.jmp exitdrop
  ifcont#18:
    x64.movRegImm32 r13, 6
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitdrop:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_Putters
  exitterminal:
    x64.movRegReg r8, r13
    x64.epilogue 48
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#20:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#8
  critsplit#32:
    x64.movRegReg rax, r8
    x64.jmp tryok#7
  __im_slow#23:
    x64.storeSlotReg slot0, rax
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.loadRegSlot rax, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#10
  critsplit#33:
    x64.movRegReg rcx, r8
    x64.jmp __im_load#27
}
```
```TargetIr:arm64-macos
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @put {
  entry:
    arm64.prologue 48
    arm64.movImm x3, 7
    arm64.loadRegBaseDisp.word64 x4, [x0 + 8]
    arm64.cmp x1, x4
    arm64.b.hs __im_slow#6
    arm64.b __im_buffer#7
  tryerr#2:
    arm64.movImm x0, 0
    arm64.epilogue 48
    arm64.ret
  __im_buffer#7:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.cmp x4, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 16]
    arm64.cmp x5, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x5, x4, 16
    arm64.loadRegBaseDisp.word64 x5, [x5 + 8]
    arm64.cmp x5, 0
    arm64.b.ne __im_detach
  __im_detached#12:
    arm64.storeBaseIndexScaleReg.word64 [x4 + x1*8 + 0], x3
  tryok#1:
    arm64.movImm x1, 9
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x2, x3
    arm64.b.hs __im_slow#14
    arm64.b __im_buffer#15
  tryerr#4:
    arm64.movImm x0, 0
    arm64.epilogue 48
    arm64.ret
  __im_buffer#15:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  __im_detached#19:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x2*8 + 0], x1
  tryok#3:
    arm64.movImm x0, 1
    arm64.epilogue 48
    arm64.ret
  __im_slow#6:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x2
    arm64.bl __managed_index_refused
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x2, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#2
    arm64.b tryok#1
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot2, x1
    arm64.storeSlotReg slot1, x2
    arm64.bl __managed_cow_detach
    arm64.movRegReg x4, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot2
    arm64.loadRegSlot x2, slot1
    arm64.movImm x3, 7
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.b __im_detached#12
  __im_slow#14:
    arm64.movRegReg x1, x2
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr#4
    arm64.b tryok#3
  __im_missing:
    arm64.leaRdata x0, __str_blob_4  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x22
    arm64.storeSlotReg slot3, x21
    arm64.storeSlotReg slot2, x20
    arm64.storeSlotReg slot1, x19
  __il_body:
    arm64.leaRdata x19, __fnrec_put
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
  __il_cont:
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
    arm64.loadRegBaseDisp.word64 x21, [x20 + 0]
    arm64.movRegReg x0, x21
    arm64.bl __mm_retain
    arm64.movRegReg x22, x0
    arm64.movImm x1, 0
    arm64.movImm x2, 2
    arm64.loadRegBaseDisp.word64 x4, [x21 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x3, x21
    arm64.blr x4
    arm64.eor x21, x0, 1
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.cbz x21, ifcont#4
  refused:
    arm64.movImm x21, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.b exitdrop
  ifcont#4:
    arm64.loadRegBaseDisp.word64 x21, [x20 + 0]
    arm64.movRegReg x0, x21
    arm64.bl __mm_retain
    arm64.movRegReg x22, x0
    arm64.movImm x1, 1
    arm64.movImm x2, 5
    arm64.loadRegBaseDisp.word64 x4, [x21 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x3, x21
    arm64.blr x4
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.cbz x21, ifcont#6
  accepted:
    arm64.movImm x21, 2
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.b exitdrop
  ifcont#6:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow#20
    arm64.b __im_load#21
  tryerr#8:
    arm64.movImm x21, 3
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.b exitdrop
  __im_load#21:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  tryok#7:
    arm64.movImm x1, 2
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
    arm64.cmp x2, 2
    arm64.b.ls __im_slow#23
    arm64.b __im_load#24
  tryerr#10:
    arm64.movImm x21, 4
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.b exitdrop
  __im_load#24:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 16]
  __im_load#27:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
  tryok#11:
    arm64.cmp x0, 7
    arm64.movImm x0, 0
    arm64.b.ne scmerge#14
  andrhs#13:
    arm64.cmp x1, 9
    arm64.cset x0, eq
  scmerge#14:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit
  andrhs#15:
    arm64.cmp x2, 7
    arm64.cset x0, eq
    arm64.b scmerge#16
  critsplit:
    arm64.movRegReg x0, x1
  scmerge#16:
    arm64.cbz x0, ifcont#18
  stored:
    arm64.movImm x21, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.b exitdrop
  ifcont#18:
    arm64.movImm x21, 6
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitdrop:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Putters
  exitterminal:
    arm64.movRegReg x0, x21
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.epilogue 64
    arm64.ret
  __im_slow#20:
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#8
    arm64.b tryok#7
  __im_slow#23:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#10
    arm64.b __im_load#27
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

func @put {
  entry:
    arm64.prologue 48
    arm64.movImm x3, 7
    arm64.loadRegBaseDisp.word64 x4, [x0 + 8]
    arm64.cmp x1, x4
    arm64.b.hs __im_slow#6
    arm64.b __im_buffer#7
  tryerr#2:
    arm64.movImm x0, 0
    arm64.epilogue 48
    arm64.ret
  __im_buffer#7:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.cmp x4, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 16]
    arm64.cmp x5, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x5, x4, 16
    arm64.loadRegBaseDisp.word64 x5, [x5 + 8]
    arm64.cmp x5, 0
    arm64.b.ne __im_detach
  __im_detached#12:
    arm64.storeBaseIndexScaleReg.word64 [x4 + x1*8 + 0], x3
  tryok#1:
    arm64.movImm x1, 9
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x2, x3
    arm64.b.hs __im_slow#14
    arm64.b __im_buffer#15
  tryerr#4:
    arm64.movImm x0, 0
    arm64.epilogue 48
    arm64.ret
  __im_buffer#15:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  __im_detached#19:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x2*8 + 0], x1
  tryok#3:
    arm64.movImm x0, 1
    arm64.epilogue 48
    arm64.ret
  __im_slow#6:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x2
    arm64.bl __managed_index_refused
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x2, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#2
    arm64.b tryok#1
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot2, x1
    arm64.storeSlotReg slot1, x2
    arm64.bl __managed_cow_detach
    arm64.movRegReg x4, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot2
    arm64.loadRegSlot x2, slot1
    arm64.movImm x3, 7
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.b __im_detached#12
  __im_slow#14:
    arm64.movRegReg x1, x2
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr#4
    arm64.b tryok#3
  __im_missing:
    arm64.leaRdata x0, __str_blob_4  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x22
    arm64.storeSlotReg slot3, x21
    arm64.storeSlotReg slot2, x20
    arm64.storeSlotReg slot1, x19
  __il_body:
    arm64.leaRdata x19, __fnrec_put
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
  __il_cont:
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
    arm64.loadRegBaseDisp.word64 x21, [x20 + 0]
    arm64.movRegReg x0, x21
    arm64.bl __mm_retain
    arm64.movRegReg x22, x0
    arm64.movImm x1, 0
    arm64.movImm x2, 2
    arm64.loadRegBaseDisp.word64 x4, [x21 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x3, x21
    arm64.blr x4
    arm64.eor x21, x0, 1
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.cbz x21, ifcont#4
  refused:
    arm64.movImm x21, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.b exitdrop
  ifcont#4:
    arm64.loadRegBaseDisp.word64 x21, [x20 + 0]
    arm64.movRegReg x0, x21
    arm64.bl __mm_retain
    arm64.movRegReg x22, x0
    arm64.movImm x1, 1
    arm64.movImm x2, 5
    arm64.loadRegBaseDisp.word64 x4, [x21 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x3, x21
    arm64.blr x4
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.cbz x21, ifcont#6
  accepted:
    arm64.movImm x21, 2
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.b exitdrop
  ifcont#6:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow#20
    arm64.b __im_load#21
  tryerr#8:
    arm64.movImm x21, 3
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.b exitdrop
  __im_load#21:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  tryok#7:
    arm64.movImm x1, 2
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
    arm64.cmp x2, 2
    arm64.b.ls __im_slow#23
    arm64.b __im_load#24
  tryerr#10:
    arm64.movImm x21, 4
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.b exitdrop
  __im_load#24:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 16]
  __im_load#27:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
  tryok#11:
    arm64.cmp x0, 7
    arm64.movImm x0, 0
    arm64.b.ne scmerge#14
  andrhs#13:
    arm64.cmp x1, 9
    arm64.cset x0, eq
  scmerge#14:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit
  andrhs#15:
    arm64.cmp x2, 7
    arm64.cset x0, eq
    arm64.b scmerge#16
  critsplit:
    arm64.movRegReg x0, x1
  scmerge#16:
    arm64.cbz x0, ifcont#18
  stored:
    arm64.movImm x21, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.b exitdrop
  ifcont#18:
    arm64.movImm x21, 6
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitdrop:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Putters
  exitterminal:
    arm64.movRegReg x0, x21
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.epilogue 64
    arm64.ret
  __im_slow#20:
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#8
    arm64.b tryok#7
  __im_slow#23:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#10
    arm64.b __im_load#27
}
```
