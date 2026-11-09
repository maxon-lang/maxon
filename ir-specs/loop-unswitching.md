---
feature: loop-unswitching
status: experimental
keywords: [optimizer, codegen, loop, unswitching, versioning, callee-table, managed-memory]
category: codegen
---

# A guard the loop asks once: the cases that pin emitted code

The cases of `specs/loop-unswitching.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: a-loop-calling-an-unexpanded-managed-entry-stays-unversioned -->
Control for the callee table, on the one shape where nothing else can refuse the loop. `log.reserve` grows
through `__managed_mem_grow`, a managed entry `InlineManagedPrimitives` has no expansion for, so it stays the
call the lowering wrote: no inline length store, no guard chain, and the call's header effect
(`managedCalleeHeaderEffect`) is the only fact about it the unswitcher has. `fillAndLog` also holds
`a.set`, whose three shape guards are the thing to version, so a callee table that declared `__managed_mem_grow` to
write nothing would version this loop. The pin holds `fillAndLog`'s loop once, calling
`__managed_mem_grow`, and `fillOnly`'s loop - the same body without the reserve - twice, behind `__us_test`
blocks. A `__us_test` block over `fillAndLog`'s loop, or one copy of `fillOnly`'s, is the failure.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word
typealias Half = int(0 to 65535)
typealias HalfArray = Array with Half

function fillAndLog(a WordArray, log HalfArray, n Count)
	for i in 0 upto n 'each'
		try a.set(i, value: (i * 3 + 1) as Word) otherwise panic("fillAndLog: i < n = a.count()")
		log.reserve(8)
	end 'each'
end 'fillAndLog'

function fillOnly(a WordArray, n Count)
	for i in 0 upto n 'each'
		try a.set(i, value: (i * 3 + 1) as Word) otherwise panic("fillOnly: i < n = a.count()")
	end 'each'
end 'fillOnly'

function main() returns ExitCode
	var a = WordArray.create(0)
	a.resize(5)
	var log = HalfArray.create(0)
	fillAndLog(a, log: log, n: 5)
	var b = WordArray.create(0)
	b.resize(5)
	fillOnly(b, n: 5)
	var seen = 0 as Word
	for v in a 'checkA'
		seen = seen + v
	end 'checkA'
	for v in b 'checkB'
		seen = seen + v
	end 'checkB'
	if seen != 70 'sums'
		return 1
	end 'sums'
	if log.capacity() < 8 'reserved'
		return 2
	end 'reserved'
	return 0
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
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 64
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 5
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rcx, 2
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
  __il_body#41:
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#42
  each#43:
    x64.imulRegRegImm32 rax, r13, 3
  __rc_ok#48:
    x64.leaRegRegImm32 rcx, rax, 1
    x64.cmpRegReg rcx, rax
    x64.jcc below, __rc_panic#51
  __rc_ok#50:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r13, rax
    x64.jcc aboveEqual, __im_slow#53
  __im_buffer#54:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_missing#55
  __im_owned#57:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __im_detach#56
  __im_viewed#58:
    x64.leaRegRegImm32 rdx, rax, -16
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_detach#56
  __im_detached#59:
    x64.storeBaseIndexScaleReg.word64 [rax + r13*8 + 0], rcx
  tryok:
    x64.movRegImm32 r14, 8
  __il_body#61:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_capacity
    x64.cmpRegImm32 r8, 0
    x64.movRegImm32 rax, 1
    x64.jcc less, scmerge
  __rc_ok#68:
    x64.cmpRegImm32 r8, 8
    x64.setccReg below, rax
  scmerge:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, forstep#44
  grow:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __managed_mem_grow
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, forstep#44
  tryerr#66:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at Array.maxon:329: Array.reserve: the requested capacity exceeds what the buffer can hold\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  forstep#44:
    x64.leaRegRegImm32 r13, r13, 1
  forhdr#42:
    x64.cmpRegImm32 r13, 5
    x64.jcc below, each#43
  __il_cont#40:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 5
    x64.movRegReg rcx, r13
    x64.callDirect __managed_resize
  __il_body#19:
    x64.movRegImm32 rdx, 0
  __us_test#83:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, forhdr#20
  __us_test#84:
    x64.loadRegBaseDisp.word64 rsi, [r13 + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, forhdr#20
  __us_test#85:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, forhdr#20
    x64.jmp forhdr#70
  each#21:
    x64.imulRegRegImm32 rax, rdx, 3
  __rc_ok#26:
    x64.leaRegRegImm32 rcx, rax, 1
    x64.cmpRegReg rcx, rax
    x64.jcc below, __rc_panic#29
  __rc_ok#28:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#31
  __im_buffer#32:
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_missing#33
  __im_owned#35:
    x64.loadRegBaseDisp.word64 rsi, [r13 + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach#34
  __im_viewed#36:
    x64.leaRegRegImm32 rsi, rax, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach#34
  __im_detached#37:
    x64.storeBaseIndexScaleReg.word64 [rax + rdx*8 + 0], rcx
  forstep#22:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#20:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#21
    x64.jmp __il_cont#18
  each#82:
    x64.imulRegRegImm32 rsi, rdx, 3
  __rc_ok#81:
    x64.leaRegRegImm32 rdi, rsi, 1
    x64.cmpRegReg rdi, rsi
    x64.jcc below, __rc_panic#29
  __rc_ok#80:
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#74
  __im_detached#75:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rdi
  forstep#71:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#70:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#82
  __il_cont#18:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 0
    x64.loadRegBaseDisp.word64 rdx, [rbx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rbx + 0]
    x64.jmp forhdr#1
  checkA:
    x64.loadRegBaseIndexScale.word64 rdi, [rsi + rcx*8 + 0]
  __im_cont#16:
    x64.leaRegRegReg rax, rax, rdi
  forstep#3:
    x64.leaRegRegImm32 rcx, rcx, 1
  forhdr#1:
    x64.cmpRegReg rcx, rdx
    x64.jcc less, checkA
  forexit#4:
    x64.movRegImm32 rcx, 0
    x64.loadRegBaseDisp.word64 rdx, [r13 + 8]
    x64.loadRegBaseDisp.word64 rsi, [r13 + 0]
    x64.jmp forhdr#5
  checkB:
    x64.loadRegBaseIndexScale.word64 rdi, [rsi + rcx*8 + 0]
  __im_cont#17:
    x64.leaRegRegReg rax, rax, rdi
  forstep#7:
    x64.leaRegRegImm32 rcx, rcx, 1
  forhdr#5:
    x64.cmpRegReg rcx, rdx
    x64.jcc less, checkB
  forexit#8:
    x64.cmpRegImm32 rax, 70
    x64.jcc equal, __il_body#39
  sums:
    x64.movRegImm32 r14, 1
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.jmp exitdrop#11
  __il_body#39:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_capacity
  __il_cont#38:
    x64.cmpRegImm32 r8, 8
    x64.jcc greaterEqual, ifcont
  reserved:
    x64.movRegImm32 r14, 2
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.jmp exitdrop#11
  ifcont:
    x64.movRegImm32 r14, 0
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
  exitdrop#11:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitdrop#12:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, r14
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#53:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#47
    x64.jmp tryok
  __im_detach#56:
    x64.storeSlotReg slot0, rcx
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rcx, slot0
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jmp __im_detached#59
  __im_slow#31:
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rdx, slot1
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#25
    x64.jmp forstep#22
  __im_detach#34:
    x64.storeSlotReg slot0, rcx
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rcx, slot0
    x64.loadRegSlot rdx, slot1
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.jmp __im_detached#37
  __im_slow#74:
    x64.storeSlotReg slot2, rax
    x64.storeSlotReg slot0, rcx
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rax, slot2
    x64.loadRegSlot rcx, slot0
    x64.loadRegSlot rdx, slot1
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#25
    x64.jmp forstep#71
  __rc_panic#51:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-loop-calling-an-unexpanded-managed-entry-stays-unversioned.test:9: Range check failed: value outside typealias 'Count'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#47:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-loop-calling-an-unexpanded-managed-entry-stays-unversioned.test:9: fillAndLog: i < n = a.count()\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing#55:
    x64.leaRegRdata rcx, [rip + __str_blob_9]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing#33:
    x64.leaRegRdata rcx, [rip + __str_blob_9]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#29:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at a-loop-calling-an-unexpanded-managed-entry-stays-unversioned.test:16: Range check failed: value outside typealias 'Count'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#25:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-loop-calling-an-unexpanded-managed-entry-stays-unversioned.test:16: fillOnly: i < n = a.count()\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
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
    arm64.prologue 80
    arm64.storeSlotReg slot6, x22
    arm64.storeSlotReg slot5, x21
    arm64.storeSlotReg slot4, x20
    arm64.storeSlotReg slot3, x19
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 5
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x0, 2
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
  __il_body#41:
    arm64.movImm x21, 0
    arm64.b forhdr#42
  each#43:
    arm64.arm64AddLsl x0, x21, x21, lsl #1
  __rc_ok#48:
    arm64.add x1, x0, 1
    arm64.cmp x1, x0
    arm64.b.lo __rc_panic#51
  __rc_ok#50:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x21, x0
    arm64.b.hs __im_slow#53
  __im_buffer#54:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_missing#55
  __im_owned#57:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_detach#56
  __im_viewed#58:
    arm64.sub x2, x0, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne __im_detach#56
  __im_detached#59:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x21*8 + 0], x1
  tryok:
    arm64.movImm x22, 8
  __il_body#61:
    arm64.movRegReg x0, x20
    arm64.bl __managed_capacity
    arm64.cmp x0, 0
    arm64.movImm x1, 1
    arm64.b.lt critsplit
  __rc_ok#68:
    arm64.cmp x0, 8
    arm64.cset x0, lo
    arm64.b scmerge
  critsplit:
    arm64.movRegReg x0, x1
  scmerge:
    arm64.cbz x0, forstep#44
  grow:
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x22
    arm64.bl __managed_mem_grow
    arm64.cmp x9, 0
    arm64.b.eq forstep#44
  tryerr#66:
    arm64.leaRdata x0, __str_blob_2  ; "panic at Array.maxon:329: Array.reserve: the requested capacity exceeds what the buffer can hold\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  forstep#44:
    arm64.add x21, x21, 1
  forhdr#42:
    arm64.cmp x21, 5
    arm64.b.lo each#43
  __il_cont#40:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x21, x0
    arm64.movImm x1, 5
    arm64.movRegReg x0, x21
    arm64.bl __managed_resize
  __il_body#19:
    arm64.movImm x1, 0
  __us_test#83:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.cmp x2, 0
    arm64.b.eq forhdr#20
  __us_test#84:
    arm64.loadRegBaseDisp.word64 x3, [x21 + 16]
    arm64.cmp x3, 0
    arm64.b.lt forhdr#20
  __us_test#85:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne forhdr#20
    arm64.b forhdr#70
  each#21:
    arm64.arm64AddLsl x0, x1, x1, lsl #1
  __rc_ok#26:
    arm64.add x2, x0, 1
    arm64.cmp x2, x0
    arm64.b.lo __rc_panic#29
  __rc_ok#28:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#31
  __im_buffer#32:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_missing#33
  __im_owned#35:
    arm64.loadRegBaseDisp.word64 x3, [x21 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach#34
  __im_viewed#36:
    arm64.sub x3, x0, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach#34
  __im_detached#37:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x1*8 + 0], x2
  forstep#22:
    arm64.add x1, x1, 1
  forhdr#20:
    arm64.cmp x1, 5
    arm64.b.lo each#21
    arm64.b __il_cont#18
  each#82:
    arm64.arm64AddLsl x3, x1, x1, lsl #1
  __rc_ok#81:
    arm64.add x4, x3, 1
    arm64.cmp x4, x3
    arm64.b.lo __rc_panic#29
  __rc_ok#80:
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#74
  __im_detached#75:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x1*8 + 0], x4
  forstep#71:
    arm64.add x1, x1, 1
  forhdr#70:
    arm64.cmp x1, 5
    arm64.b.lo each#82
  __il_cont#18:
    arm64.movImm x0, 0
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.b forhdr#1
  checkA:
    arm64.loadRegBaseIndexScale.word64 x4, [x3 + x1*8 + 0]
  __im_cont#16:
    arm64.add x0, x0, x4
  forstep#3:
    arm64.add x1, x1, 1
  forhdr#1:
    arm64.cmp x1, x2
    arm64.b.lt checkA
  forexit#4:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x21 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x21 + 0]
    arm64.b forhdr#5
  checkB:
    arm64.loadRegBaseIndexScale.word64 x4, [x3 + x1*8 + 0]
  __im_cont#17:
    arm64.add x0, x0, x4
  forstep#7:
    arm64.add x1, x1, 1
  forhdr#5:
    arm64.cmp x1, x2
    arm64.b.lt checkB
  forexit#8:
    arm64.cmp x0, 70
    arm64.b.eq __il_body#39
  sums:
    arm64.movImm x22, 1
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.b exitdrop#11
  __il_body#39:
    arm64.movRegReg x0, x20
    arm64.bl __managed_capacity
  __il_cont#38:
    arm64.cmp x0, 8
    arm64.b.ge ifcont
  reserved:
    arm64.movImm x22, 2
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.b exitdrop#11
  ifcont:
    arm64.movImm x22, 0
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitdrop#11:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitdrop#12:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x22
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_slow#53:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr#47
    arm64.b tryok
  __im_detach#56:
    arm64.storeSlotReg slot0, x1
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.loadRegSlot x1, slot0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.b __im_detached#59
  __im_slow#31:
    arm64.storeSlotReg slot0, x1
    arm64.movRegReg x0, x21
    arm64.bl __managed_index_refused
    arm64.loadRegSlot x1, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#25
    arm64.b forstep#22
  __im_detach#34:
    arm64.storeSlotReg slot0, x1
    arm64.storeSlotReg slot1, x2
    arm64.movRegReg x0, x21
    arm64.bl __managed_cow_detach
    arm64.loadRegSlot x1, slot0
    arm64.loadRegSlot x2, slot1
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.b __im_detached#37
  __im_slow#74:
    arm64.storeSlotReg slot2, x0
    arm64.storeSlotReg slot0, x1
    arm64.storeSlotReg slot1, x2
    arm64.movRegReg x0, x21
    arm64.bl __managed_index_refused
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot2
    arm64.loadRegSlot x1, slot0
    arm64.loadRegSlot x2, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#25
    arm64.b forstep#71
  __rc_panic#51:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-loop-calling-an-unexpanded-managed-entry-stays-unversioned.test:9: Range check failed: value outside typealias 'Count'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  tryerr#47:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-loop-calling-an-unexpanded-managed-entry-stays-unversioned.test:9: fillAndLog: i < n = a.count()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_missing#55:
    arm64.leaRdata x0, __str_blob_9  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_missing#33:
    arm64.leaRdata x0, __str_blob_9  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __rc_panic#29:
    arm64.leaRdata x0, __str_blob_5  ; "panic at a-loop-calling-an-unexpanded-managed-entry-stays-unversioned.test:16: Range check failed: value outside typealias 'Count'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  tryerr#25:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-loop-calling-an-unexpanded-managed-entry-stays-unversioned.test:16: fillOnly: i < n = a.count()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
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
    x64.prologue 64
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 5
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rcx, 2
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
  __il_body#41:
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#42
  each#43:
    x64.imulRegRegImm32 rax, r13, 3
  __rc_ok#48:
    x64.leaRegRegImm32 rcx, rax, 1
    x64.cmpRegReg rcx, rax
    x64.jcc below, __rc_panic#51
  __rc_ok#50:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r13, rax
    x64.jcc aboveEqual, __im_slow#53
  __im_buffer#54:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_missing#55
  __im_owned#57:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __im_detach#56
  __im_viewed#58:
    x64.leaRegRegImm32 rdx, rax, -16
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_detach#56
  __im_detached#59:
    x64.storeBaseIndexScaleReg.word64 [rax + r13*8 + 0], rcx
  tryok:
    x64.movRegImm32 r14, 8
  __il_body#61:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_capacity
    x64.cmpRegImm32 r8, 0
    x64.movRegImm32 rax, 1
    x64.jcc less, scmerge
  __rc_ok#68:
    x64.cmpRegImm32 r8, 8
    x64.setccReg below, rax
  scmerge:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, forstep#44
  grow:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __managed_mem_grow
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, forstep#44
  tryerr#66:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at Array.maxon:329: Array.reserve: the requested capacity exceeds what the buffer can hold\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  forstep#44:
    x64.leaRegRegImm32 r13, r13, 1
  forhdr#42:
    x64.cmpRegImm32 r13, 5
    x64.jcc below, each#43
  __il_cont#40:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 5
    x64.movRegReg rcx, r13
    x64.callDirect __managed_resize
  __il_body#19:
    x64.movRegImm32 rdx, 0
  __us_test#83:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, forhdr#20
  __us_test#84:
    x64.loadRegBaseDisp.word64 rsi, [r13 + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, forhdr#20
  __us_test#85:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, forhdr#20
    x64.jmp forhdr#70
  each#21:
    x64.imulRegRegImm32 rax, rdx, 3
  __rc_ok#26:
    x64.leaRegRegImm32 rcx, rax, 1
    x64.cmpRegReg rcx, rax
    x64.jcc below, __rc_panic#29
  __rc_ok#28:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#31
  __im_buffer#32:
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_missing#33
  __im_owned#35:
    x64.loadRegBaseDisp.word64 rsi, [r13 + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach#34
  __im_viewed#36:
    x64.leaRegRegImm32 rsi, rax, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach#34
  __im_detached#37:
    x64.storeBaseIndexScaleReg.word64 [rax + rdx*8 + 0], rcx
  forstep#22:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#20:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#21
    x64.jmp __il_cont#18
  each#82:
    x64.imulRegRegImm32 rsi, rdx, 3
  __rc_ok#81:
    x64.leaRegRegImm32 rdi, rsi, 1
    x64.cmpRegReg rdi, rsi
    x64.jcc below, __rc_panic#29
  __rc_ok#80:
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#74
  __im_detached#75:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rdi
  forstep#71:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#70:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#82
  __il_cont#18:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 0
    x64.loadRegBaseDisp.word64 rdx, [rbx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rbx + 0]
    x64.jmp forhdr#1
  checkA:
    x64.loadRegBaseIndexScale.word64 rdi, [rsi + rcx*8 + 0]
  __im_cont#16:
    x64.leaRegRegReg rax, rax, rdi
  forstep#3:
    x64.leaRegRegImm32 rcx, rcx, 1
  forhdr#1:
    x64.cmpRegReg rcx, rdx
    x64.jcc less, checkA
  forexit#4:
    x64.movRegImm32 rcx, 0
    x64.loadRegBaseDisp.word64 rdx, [r13 + 8]
    x64.loadRegBaseDisp.word64 rsi, [r13 + 0]
    x64.jmp forhdr#5
  checkB:
    x64.loadRegBaseIndexScale.word64 rdi, [rsi + rcx*8 + 0]
  __im_cont#17:
    x64.leaRegRegReg rax, rax, rdi
  forstep#7:
    x64.leaRegRegImm32 rcx, rcx, 1
  forhdr#5:
    x64.cmpRegReg rcx, rdx
    x64.jcc less, checkB
  forexit#8:
    x64.cmpRegImm32 rax, 70
    x64.jcc equal, __il_body#39
  sums:
    x64.movRegImm32 r14, 1
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.jmp exitdrop#11
  __il_body#39:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_capacity
  __il_cont#38:
    x64.cmpRegImm32 r8, 8
    x64.jcc greaterEqual, ifcont
  reserved:
    x64.movRegImm32 r14, 2
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.jmp exitdrop#11
  ifcont:
    x64.movRegImm32 r14, 0
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
  exitdrop#11:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitdrop#12:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, r14
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#53:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#47
    x64.jmp tryok
  __im_detach#56:
    x64.storeSlotReg slot0, rcx
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rcx, slot0
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jmp __im_detached#59
  __im_slow#31:
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rdx, slot1
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#25
    x64.jmp forstep#22
  __im_detach#34:
    x64.storeSlotReg slot0, rcx
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rcx, slot0
    x64.loadRegSlot rdx, slot1
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.jmp __im_detached#37
  __im_slow#74:
    x64.storeSlotReg slot2, rax
    x64.storeSlotReg slot0, rcx
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rax, slot2
    x64.loadRegSlot rcx, slot0
    x64.loadRegSlot rdx, slot1
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#25
    x64.jmp forstep#71
  __rc_panic#51:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-loop-calling-an-unexpanded-managed-entry-stays-unversioned.test:9: Range check failed: value outside typealias 'Count'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#47:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-loop-calling-an-unexpanded-managed-entry-stays-unversioned.test:9: fillAndLog: i < n = a.count()\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing#55:
    x64.leaRegRdata rcx, [rip + __str_blob_9]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing#33:
    x64.leaRegRdata rcx, [rip + __str_blob_9]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#29:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at a-loop-calling-an-unexpanded-managed-entry-stays-unversioned.test:16: Range check failed: value outside typealias 'Count'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#25:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-loop-calling-an-unexpanded-managed-entry-stays-unversioned.test:16: fillOnly: i < n = a.count()\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
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
    arm64.prologue 80
    arm64.storeSlotReg slot6, x22
    arm64.storeSlotReg slot5, x21
    arm64.storeSlotReg slot4, x20
    arm64.storeSlotReg slot3, x19
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 5
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x0, 2
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
  __il_body#41:
    arm64.movImm x21, 0
    arm64.b forhdr#42
  each#43:
    arm64.arm64AddLsl x0, x21, x21, lsl #1
  __rc_ok#48:
    arm64.add x1, x0, 1
    arm64.cmp x1, x0
    arm64.b.lo __rc_panic#51
  __rc_ok#50:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x21, x0
    arm64.b.hs __im_slow#53
  __im_buffer#54:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_missing#55
  __im_owned#57:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_detach#56
  __im_viewed#58:
    arm64.sub x2, x0, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne __im_detach#56
  __im_detached#59:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x21*8 + 0], x1
  tryok:
    arm64.movImm x22, 8
  __il_body#61:
    arm64.movRegReg x0, x20
    arm64.bl __managed_capacity
    arm64.cmp x0, 0
    arm64.movImm x1, 1
    arm64.b.lt critsplit
  __rc_ok#68:
    arm64.cmp x0, 8
    arm64.cset x0, lo
    arm64.b scmerge
  critsplit:
    arm64.movRegReg x0, x1
  scmerge:
    arm64.cbz x0, forstep#44
  grow:
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x22
    arm64.bl __managed_mem_grow
    arm64.cmp x9, 0
    arm64.b.eq forstep#44
  tryerr#66:
    arm64.leaRdata x0, __str_blob_2  ; "panic at Array.maxon:329: Array.reserve: the requested capacity exceeds what the buffer can hold\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  forstep#44:
    arm64.add x21, x21, 1
  forhdr#42:
    arm64.cmp x21, 5
    arm64.b.lo each#43
  __il_cont#40:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x21, x0
    arm64.movImm x1, 5
    arm64.movRegReg x0, x21
    arm64.bl __managed_resize
  __il_body#19:
    arm64.movImm x1, 0
  __us_test#83:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.cmp x2, 0
    arm64.b.eq forhdr#20
  __us_test#84:
    arm64.loadRegBaseDisp.word64 x3, [x21 + 16]
    arm64.cmp x3, 0
    arm64.b.lt forhdr#20
  __us_test#85:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne forhdr#20
    arm64.b forhdr#70
  each#21:
    arm64.arm64AddLsl x0, x1, x1, lsl #1
  __rc_ok#26:
    arm64.add x2, x0, 1
    arm64.cmp x2, x0
    arm64.b.lo __rc_panic#29
  __rc_ok#28:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#31
  __im_buffer#32:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_missing#33
  __im_owned#35:
    arm64.loadRegBaseDisp.word64 x3, [x21 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach#34
  __im_viewed#36:
    arm64.sub x3, x0, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach#34
  __im_detached#37:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x1*8 + 0], x2
  forstep#22:
    arm64.add x1, x1, 1
  forhdr#20:
    arm64.cmp x1, 5
    arm64.b.lo each#21
    arm64.b __il_cont#18
  each#82:
    arm64.arm64AddLsl x3, x1, x1, lsl #1
  __rc_ok#81:
    arm64.add x4, x3, 1
    arm64.cmp x4, x3
    arm64.b.lo __rc_panic#29
  __rc_ok#80:
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#74
  __im_detached#75:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x1*8 + 0], x4
  forstep#71:
    arm64.add x1, x1, 1
  forhdr#70:
    arm64.cmp x1, 5
    arm64.b.lo each#82
  __il_cont#18:
    arm64.movImm x0, 0
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.b forhdr#1
  checkA:
    arm64.loadRegBaseIndexScale.word64 x4, [x3 + x1*8 + 0]
  __im_cont#16:
    arm64.add x0, x0, x4
  forstep#3:
    arm64.add x1, x1, 1
  forhdr#1:
    arm64.cmp x1, x2
    arm64.b.lt checkA
  forexit#4:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x21 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x21 + 0]
    arm64.b forhdr#5
  checkB:
    arm64.loadRegBaseIndexScale.word64 x4, [x3 + x1*8 + 0]
  __im_cont#17:
    arm64.add x0, x0, x4
  forstep#7:
    arm64.add x1, x1, 1
  forhdr#5:
    arm64.cmp x1, x2
    arm64.b.lt checkB
  forexit#8:
    arm64.cmp x0, 70
    arm64.b.eq __il_body#39
  sums:
    arm64.movImm x22, 1
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.b exitdrop#11
  __il_body#39:
    arm64.movRegReg x0, x20
    arm64.bl __managed_capacity
  __il_cont#38:
    arm64.cmp x0, 8
    arm64.b.ge ifcont
  reserved:
    arm64.movImm x22, 2
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.b exitdrop#11
  ifcont:
    arm64.movImm x22, 0
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitdrop#11:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitdrop#12:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x22
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_slow#53:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr#47
    arm64.b tryok
  __im_detach#56:
    arm64.storeSlotReg slot0, x1
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.loadRegSlot x1, slot0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.b __im_detached#59
  __im_slow#31:
    arm64.storeSlotReg slot0, x1
    arm64.movRegReg x0, x21
    arm64.bl __managed_index_refused
    arm64.loadRegSlot x1, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#25
    arm64.b forstep#22
  __im_detach#34:
    arm64.storeSlotReg slot0, x1
    arm64.storeSlotReg slot1, x2
    arm64.movRegReg x0, x21
    arm64.bl __managed_cow_detach
    arm64.loadRegSlot x1, slot0
    arm64.loadRegSlot x2, slot1
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.b __im_detached#37
  __im_slow#74:
    arm64.storeSlotReg slot2, x0
    arm64.storeSlotReg slot0, x1
    arm64.storeSlotReg slot1, x2
    arm64.movRegReg x0, x21
    arm64.bl __managed_index_refused
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot2
    arm64.loadRegSlot x1, slot0
    arm64.loadRegSlot x2, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#25
    arm64.b forstep#71
  __rc_panic#51:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-loop-calling-an-unexpanded-managed-entry-stays-unversioned.test:9: Range check failed: value outside typealias 'Count'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  tryerr#47:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-loop-calling-an-unexpanded-managed-entry-stays-unversioned.test:9: fillAndLog: i < n = a.count()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_missing#55:
    arm64.leaRdata x0, __str_blob_9  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_missing#33:
    arm64.leaRdata x0, __str_blob_9  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __rc_panic#29:
    arm64.leaRdata x0, __str_blob_5  ; "panic at a-loop-calling-an-unexpanded-managed-entry-stays-unversioned.test:16: Range check failed: value outside typealias 'Count'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  tryerr#25:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-loop-calling-an-unexpanded-managed-entry-stays-unversioned.test:16: fillOnly: i < n = a.count()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
}
```

<!-- test: a-loop-pushing-onto-a-record-it-just-created-stays-unversioned -->
Control for the callee table's `__managed_push` entry. A `push` onto a record the loop body created in the same block chain is left a call (`freshRecords`: the record has no buffer yet, so its first push would take the slow arm anyway), so `__managed_push` stays in the loop with only `managedCalleeHeaderEffect` to say what it does to a record. `fillAndLog` also holds `a.set`, whose three shape guards are the thing to version, so a callee table that declared `__managed_push` to write nothing would let the unswitcher version this loop; the loop also holds the create and the drop of the fresh record, whose effect the table does not state either. The pin holds `fillAndLog`'s loop once, calling `__managed_push`, and `fillOnly`'s loop - the same body without the scratch record - twice, behind `__us_test` blocks. A `__us_test` block over `fillAndLog`'s loop, or one copy of `fillOnly`'s, is the failure.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word
typealias Half = int(0 to 65535)
typealias HalfArray = Array with Half

function fillAndLog(a WordArray, n Count) returns Count
	var logged = 0 as Count

	for i in 0 upto n 'each'
		try a.set(i, value: (i * 3 + 1) as Word) otherwise panic("fillAndLog: i < n = a.count()")
		var scratch = HalfArray.create(0)
		scratch.push(7)
		logged = logged + scratch.count()
	end 'each'

	return logged
end 'fillAndLog'

function fillOnly(a WordArray, n Count)
	for i in 0 upto n 'each'
		try a.set(i, value: (i * 3 + 1) as Word) otherwise panic("fillOnly: i < n = a.count()")
	end 'each'
end 'fillOnly'

function main() returns ExitCode
	var a = WordArray.create(0)
	a.resize(5)
	let logged = fillAndLog(a, n: 5)
	var b = WordArray.create(0)
	b.resize(5)
	fillOnly(b, n: 5)
	var seen = 0 as Word
	for v in a 'checkA'
		seen = seen + v
	end 'checkA'
	for v in b 'checkB'
		seen = seen + v
	end 'checkB'
	if seen != 70 'sums'
		return 1
	end 'sums'
	if logged != 5 'logged'
		return 2
	end 'logged'
	return 0
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
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 56
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 5
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
  __il_body#19:
    x64.movRegImm32 r12, 0
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#20
  each#21:
    x64.imulRegRegImm32 rax, r13, 3
  __rc_ok#26:
    x64.leaRegRegImm32 rcx, rax, 1
    x64.cmpRegReg rcx, rax
    x64.jcc below, __rc_panic#29
  __rc_ok#28:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r13, rax
    x64.jcc aboveEqual, __im_slow#33
  __im_buffer#34:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_missing#35
  __im_owned#37:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __im_detach#36
  __im_viewed#38:
    x64.leaRegRegImm32 rdx, rax, -16
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_detach#36
  __im_detached#39:
    x64.storeBaseIndexScaleReg.word64 [rax + r13*8 + 0], rcx
  tryok:
    x64.movRegImm32 rcx, 2
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r14, r8
    x64.movRegImm32 rdx, 7
    x64.movRegReg rcx, r14
    x64.callDirect __managed_push
    x64.loadRegBaseDisp.word64 rax, [r14 + 8]
    x64.leaRegRegReg r15, r12, rax
    x64.cmpRegReg r15, r12
    x64.jcc below, __rc_panic#31
  __rc_ok#30:
    x64.movRegReg rcx, r14
    x64.callDirect __managed_decref
  forstep#22:
    x64.leaRegRegImm32 r13, r13, 1
    x64.movRegReg r12, r15
  forhdr#20:
    x64.cmpRegImm32 r13, 5
    x64.jcc below, each#21
  __il_cont#18:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 5
    x64.movRegReg rcx, r13
    x64.callDirect __managed_resize
  __il_body#40:
    x64.movRegImm32 rdx, 0
  __us_test#72:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, forhdr#41
  __us_test#73:
    x64.loadRegBaseDisp.word64 rsi, [r13 + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, forhdr#41
  __us_test#74:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, forhdr#41
    x64.jmp forhdr#59
  each#42:
    x64.imulRegRegImm32 rax, rdx, 3
  __rc_ok#47:
    x64.leaRegRegImm32 rcx, rax, 1
    x64.cmpRegReg rcx, rax
    x64.jcc below, __rc_panic#50
  __rc_ok#49:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#52
  __im_buffer#53:
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_missing#54
  __im_owned#56:
    x64.loadRegBaseDisp.word64 rsi, [r13 + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach#55
  __im_viewed#57:
    x64.leaRegRegImm32 rsi, rax, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach#55
  __im_detached#58:
    x64.storeBaseIndexScaleReg.word64 [rax + rdx*8 + 0], rcx
  forstep#43:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#41:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#42
    x64.jmp __il_cont#17
  each#71:
    x64.imulRegRegImm32 rsi, rdx, 3
  __rc_ok#70:
    x64.leaRegRegImm32 rdi, rsi, 1
    x64.cmpRegReg rdi, rsi
    x64.jcc below, __rc_panic#50
  __rc_ok#69:
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#63
  __im_detached#64:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rdi
  forstep#60:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#59:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#71
  __il_cont#17:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 0
    x64.loadRegBaseDisp.word64 rdx, [rbx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rbx + 0]
    x64.jmp forhdr#1
  checkA:
    x64.loadRegBaseIndexScale.word64 rdi, [rsi + rcx*8 + 0]
  __im_cont#15:
    x64.leaRegRegReg rax, rax, rdi
  forstep#3:
    x64.leaRegRegImm32 rcx, rcx, 1
  forhdr#1:
    x64.cmpRegReg rcx, rdx
    x64.jcc less, checkA
  forexit#4:
    x64.movRegImm32 rcx, 0
    x64.loadRegBaseDisp.word64 rdx, [r13 + 8]
    x64.loadRegBaseDisp.word64 rsi, [r13 + 0]
    x64.jmp forhdr#5
  checkB:
    x64.loadRegBaseIndexScale.word64 rdi, [rsi + rcx*8 + 0]
  __im_cont#16:
    x64.leaRegRegReg rax, rax, rdi
  forstep#7:
    x64.leaRegRegImm32 rcx, rcx, 1
  forhdr#5:
    x64.cmpRegReg rcx, rdx
    x64.jcc less, checkB
  forexit#8:
    x64.cmpRegImm32 rax, 70
    x64.jcc equal, ifcont#12
  sums:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.jmp exitdrop
  ifcont#12:
    x64.cmpRegImm32 r12, 5
    x64.jcc equal, ifcont#14
  logged:
    x64.movRegImm32 r12, 2
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.jmp exitdrop
  ifcont#14:
    x64.movRegImm32 r12, 0
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
  exitdrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, r12
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#33:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#25
    x64.jmp tryok
  __im_detach#36:
    x64.storeSlotReg slot0, rcx
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rcx, slot0
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jmp __im_detached#39
  __im_slow#52:
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rdx, slot1
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#46
    x64.jmp forstep#43
  __im_detach#55:
    x64.storeSlotReg slot0, rcx
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rcx, slot0
    x64.loadRegSlot rdx, slot1
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.jmp __im_detached#58
  __im_slow#63:
    x64.storeSlotReg slot2, rax
    x64.storeSlotReg slot0, rcx
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rax, slot2
    x64.loadRegSlot rcx, slot0
    x64.loadRegSlot rdx, slot1
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#46
    x64.jmp forstep#60
  __rc_panic#29:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:11: Range check failed: value outside typealias 'Count'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#25:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:11: fillAndLog: i < n = a.count()\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing#35:
    x64.leaRegRdata rcx, [rip + __str_blob_8]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#31:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:14: Range check failed: value outside typealias 'Count'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing#54:
    x64.leaRegRdata rcx, [rip + __str_blob_8]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#50:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:22: Range check failed: value outside typealias 'Count'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#46:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:22: fillOnly: i < n = a.count()\x0a"
    x64.callDirect mrt_panic
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
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 56
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 5
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
  __il_body#19:
    x64.movRegImm32 r12, 0
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#20
  each#21:
    x64.imulRegRegImm32 rax, r13, 3
  __rc_ok#26:
    x64.leaRegRegImm32 rcx, rax, 1
    x64.cmpRegReg rcx, rax
    x64.jcc below, __rc_panic#29
  __rc_ok#28:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r13, rax
    x64.jcc aboveEqual, __im_slow#33
  __im_buffer#34:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_missing#35
  __im_owned#37:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __im_detach#36
  __im_viewed#38:
    x64.leaRegRegImm32 rdx, rax, -16
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_detach#36
  __im_detached#39:
    x64.storeBaseIndexScaleReg.word64 [rax + r13*8 + 0], rcx
  tryok:
    x64.movRegImm32 rcx, 2
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r14, r8
    x64.movRegImm32 rdx, 7
    x64.movRegReg rcx, r14
    x64.callDirect __managed_push
    x64.loadRegBaseDisp.word64 rax, [r14 + 8]
    x64.leaRegRegReg r15, r12, rax
    x64.cmpRegReg r15, r12
    x64.jcc below, __rc_panic#31
  __rc_ok#30:
    x64.movRegReg rcx, r14
    x64.callDirect __managed_decref
  forstep#22:
    x64.leaRegRegImm32 r13, r13, 1
    x64.movRegReg r12, r15
  forhdr#20:
    x64.cmpRegImm32 r13, 5
    x64.jcc below, each#21
  __il_cont#18:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 5
    x64.movRegReg rcx, r13
    x64.callDirect __managed_resize
  __il_body#40:
    x64.movRegImm32 rdx, 0
  __us_test#72:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, forhdr#41
  __us_test#73:
    x64.loadRegBaseDisp.word64 rsi, [r13 + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, forhdr#41
  __us_test#74:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, forhdr#41
    x64.jmp forhdr#59
  each#42:
    x64.imulRegRegImm32 rax, rdx, 3
  __rc_ok#47:
    x64.leaRegRegImm32 rcx, rax, 1
    x64.cmpRegReg rcx, rax
    x64.jcc below, __rc_panic#50
  __rc_ok#49:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#52
  __im_buffer#53:
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_missing#54
  __im_owned#56:
    x64.loadRegBaseDisp.word64 rsi, [r13 + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach#55
  __im_viewed#57:
    x64.leaRegRegImm32 rsi, rax, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach#55
  __im_detached#58:
    x64.storeBaseIndexScaleReg.word64 [rax + rdx*8 + 0], rcx
  forstep#43:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#41:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#42
    x64.jmp __il_cont#17
  each#71:
    x64.imulRegRegImm32 rsi, rdx, 3
  __rc_ok#70:
    x64.leaRegRegImm32 rdi, rsi, 1
    x64.cmpRegReg rdi, rsi
    x64.jcc below, __rc_panic#50
  __rc_ok#69:
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#63
  __im_detached#64:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rdi
  forstep#60:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#59:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#71
  __il_cont#17:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 0
    x64.loadRegBaseDisp.word64 rdx, [rbx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rbx + 0]
    x64.jmp forhdr#1
  checkA:
    x64.loadRegBaseIndexScale.word64 rdi, [rsi + rcx*8 + 0]
  __im_cont#15:
    x64.leaRegRegReg rax, rax, rdi
  forstep#3:
    x64.leaRegRegImm32 rcx, rcx, 1
  forhdr#1:
    x64.cmpRegReg rcx, rdx
    x64.jcc less, checkA
  forexit#4:
    x64.movRegImm32 rcx, 0
    x64.loadRegBaseDisp.word64 rdx, [r13 + 8]
    x64.loadRegBaseDisp.word64 rsi, [r13 + 0]
    x64.jmp forhdr#5
  checkB:
    x64.loadRegBaseIndexScale.word64 rdi, [rsi + rcx*8 + 0]
  __im_cont#16:
    x64.leaRegRegReg rax, rax, rdi
  forstep#7:
    x64.leaRegRegImm32 rcx, rcx, 1
  forhdr#5:
    x64.cmpRegReg rcx, rdx
    x64.jcc less, checkB
  forexit#8:
    x64.cmpRegImm32 rax, 70
    x64.jcc equal, ifcont#12
  sums:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.jmp exitdrop
  ifcont#12:
    x64.cmpRegImm32 r12, 5
    x64.jcc equal, ifcont#14
  logged:
    x64.movRegImm32 r12, 2
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.jmp exitdrop
  ifcont#14:
    x64.movRegImm32 r12, 0
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
  exitdrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, r12
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#33:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#25
    x64.jmp tryok
  __im_detach#36:
    x64.storeSlotReg slot0, rcx
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rcx, slot0
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jmp __im_detached#39
  __im_slow#52:
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rdx, slot1
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#46
    x64.jmp forstep#43
  __im_detach#55:
    x64.storeSlotReg slot0, rcx
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rcx, slot0
    x64.loadRegSlot rdx, slot1
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.jmp __im_detached#58
  __im_slow#63:
    x64.storeSlotReg slot2, rax
    x64.storeSlotReg slot0, rcx
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rax, slot2
    x64.loadRegSlot rcx, slot0
    x64.loadRegSlot rdx, slot1
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#46
    x64.jmp forstep#60
  __rc_panic#29:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:11: Range check failed: value outside typealias 'Count'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#25:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:11: fillAndLog: i < n = a.count()\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing#35:
    x64.leaRegRdata rcx, [rip + __str_blob_8]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#31:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:14: Range check failed: value outside typealias 'Count'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing#54:
    x64.leaRegRdata rcx, [rip + __str_blob_8]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#50:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:22: Range check failed: value outside typealias 'Count'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#46:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:22: fillOnly: i < n = a.count()\x0a"
    x64.callDirect mrt_panic
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
    arm64.prologue 80
    arm64.storeSlotReg slot7, x23
    arm64.storeSlotReg slot6, x22
    arm64.storeSlotReg slot5, x21
    arm64.storeSlotReg slot4, x20
    arm64.storeSlotReg slot3, x19
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 5
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
  __il_body#19:
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b forhdr#20
  each#21:
    arm64.arm64AddLsl x0, x21, x21, lsl #1
  __rc_ok#26:
    arm64.add x1, x0, 1
    arm64.cmp x1, x0
    arm64.b.lo __rc_panic#29
  __rc_ok#28:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x21, x0
    arm64.b.hs __im_slow#33
  __im_buffer#34:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_missing#35
  __im_owned#37:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_detach#36
  __im_viewed#38:
    arm64.sub x2, x0, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne __im_detach#36
  __im_detached#39:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x21*8 + 0], x1
  tryok:
    arm64.movImm x0, 2
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.movImm x1, 7
    arm64.movRegReg x0, x22
    arm64.bl __managed_push
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.add x23, x20, x0
    arm64.cmp x23, x20
    arm64.b.lo __rc_panic#31
  __rc_ok#30:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  forstep#22:
    arm64.add x21, x21, 1
    arm64.movRegReg x20, x23
  forhdr#20:
    arm64.cmp x21, 5
    arm64.b.lo each#21
  __il_cont#18:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x21, x0
    arm64.movImm x1, 5
    arm64.movRegReg x0, x21
    arm64.bl __managed_resize
  __il_body#40:
    arm64.movImm x1, 0
  __us_test#72:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.cmp x2, 0
    arm64.b.eq forhdr#41
  __us_test#73:
    arm64.loadRegBaseDisp.word64 x3, [x21 + 16]
    arm64.cmp x3, 0
    arm64.b.lt forhdr#41
  __us_test#74:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne forhdr#41
    arm64.b forhdr#59
  each#42:
    arm64.arm64AddLsl x0, x1, x1, lsl #1
  __rc_ok#47:
    arm64.add x2, x0, 1
    arm64.cmp x2, x0
    arm64.b.lo __rc_panic#50
  __rc_ok#49:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#52
  __im_buffer#53:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_missing#54
  __im_owned#56:
    arm64.loadRegBaseDisp.word64 x3, [x21 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach#55
  __im_viewed#57:
    arm64.sub x3, x0, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach#55
  __im_detached#58:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x1*8 + 0], x2
  forstep#43:
    arm64.add x1, x1, 1
  forhdr#41:
    arm64.cmp x1, 5
    arm64.b.lo each#42
    arm64.b __il_cont#17
  each#71:
    arm64.arm64AddLsl x3, x1, x1, lsl #1
  __rc_ok#70:
    arm64.add x4, x3, 1
    arm64.cmp x4, x3
    arm64.b.lo __rc_panic#50
  __rc_ok#69:
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#63
  __im_detached#64:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x1*8 + 0], x4
  forstep#60:
    arm64.add x1, x1, 1
  forhdr#59:
    arm64.cmp x1, 5
    arm64.b.lo each#71
  __il_cont#17:
    arm64.movImm x0, 0
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.b forhdr#1
  checkA:
    arm64.loadRegBaseIndexScale.word64 x4, [x3 + x1*8 + 0]
  __im_cont#15:
    arm64.add x0, x0, x4
  forstep#3:
    arm64.add x1, x1, 1
  forhdr#1:
    arm64.cmp x1, x2
    arm64.b.lt checkA
  forexit#4:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x21 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x21 + 0]
    arm64.b forhdr#5
  checkB:
    arm64.loadRegBaseIndexScale.word64 x4, [x3 + x1*8 + 0]
  __im_cont#16:
    arm64.add x0, x0, x4
  forstep#7:
    arm64.add x1, x1, 1
  forhdr#5:
    arm64.cmp x1, x2
    arm64.b.lt checkB
  forexit#8:
    arm64.cmp x0, 70
    arm64.b.eq ifcont#12
  sums:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.b exitdrop
  ifcont#12:
    arm64.cmp x20, 5
    arm64.b.eq ifcont#14
  logged:
    arm64.movImm x20, 2
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.b exitdrop
  ifcont#14:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitdrop:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.ret
  __im_slow#33:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr#25
    arm64.b tryok
  __im_detach#36:
    arm64.storeSlotReg slot0, x1
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.loadRegSlot x1, slot0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.b __im_detached#39
  __im_slow#52:
    arm64.storeSlotReg slot0, x1
    arm64.movRegReg x0, x21
    arm64.bl __managed_index_refused
    arm64.loadRegSlot x1, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#46
    arm64.b forstep#43
  __im_detach#55:
    arm64.storeSlotReg slot0, x1
    arm64.storeSlotReg slot1, x2
    arm64.movRegReg x0, x21
    arm64.bl __managed_cow_detach
    arm64.loadRegSlot x1, slot0
    arm64.loadRegSlot x2, slot1
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.b __im_detached#58
  __im_slow#63:
    arm64.storeSlotReg slot2, x0
    arm64.storeSlotReg slot0, x1
    arm64.storeSlotReg slot1, x2
    arm64.movRegReg x0, x21
    arm64.bl __managed_index_refused
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot2
    arm64.loadRegSlot x1, slot0
    arm64.loadRegSlot x2, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#46
    arm64.b forstep#60
  __rc_panic#29:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:11: Range check failed: value outside typealias 'Count'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.ret
  tryerr#25:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:11: fillAndLog: i < n = a.count()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.ret
  __im_missing#35:
    arm64.leaRdata x0, __str_blob_8  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.ret
  __rc_panic#31:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:14: Range check failed: value outside typealias 'Count'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.ret
  __im_missing#54:
    arm64.leaRdata x0, __str_blob_8  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.ret
  __rc_panic#50:
    arm64.leaRdata x0, __str_blob_5  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:22: Range check failed: value outside typealias 'Count'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.ret
  tryerr#46:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:22: fillOnly: i < n = a.count()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
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
    arm64.prologue 80
    arm64.storeSlotReg slot7, x23
    arm64.storeSlotReg slot6, x22
    arm64.storeSlotReg slot5, x21
    arm64.storeSlotReg slot4, x20
    arm64.storeSlotReg slot3, x19
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 5
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
  __il_body#19:
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b forhdr#20
  each#21:
    arm64.arm64AddLsl x0, x21, x21, lsl #1
  __rc_ok#26:
    arm64.add x1, x0, 1
    arm64.cmp x1, x0
    arm64.b.lo __rc_panic#29
  __rc_ok#28:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x21, x0
    arm64.b.hs __im_slow#33
  __im_buffer#34:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_missing#35
  __im_owned#37:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_detach#36
  __im_viewed#38:
    arm64.sub x2, x0, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne __im_detach#36
  __im_detached#39:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x21*8 + 0], x1
  tryok:
    arm64.movImm x0, 2
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.movImm x1, 7
    arm64.movRegReg x0, x22
    arm64.bl __managed_push
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.add x23, x20, x0
    arm64.cmp x23, x20
    arm64.b.lo __rc_panic#31
  __rc_ok#30:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  forstep#22:
    arm64.add x21, x21, 1
    arm64.movRegReg x20, x23
  forhdr#20:
    arm64.cmp x21, 5
    arm64.b.lo each#21
  __il_cont#18:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x21, x0
    arm64.movImm x1, 5
    arm64.movRegReg x0, x21
    arm64.bl __managed_resize
  __il_body#40:
    arm64.movImm x1, 0
  __us_test#72:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.cmp x2, 0
    arm64.b.eq forhdr#41
  __us_test#73:
    arm64.loadRegBaseDisp.word64 x3, [x21 + 16]
    arm64.cmp x3, 0
    arm64.b.lt forhdr#41
  __us_test#74:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne forhdr#41
    arm64.b forhdr#59
  each#42:
    arm64.arm64AddLsl x0, x1, x1, lsl #1
  __rc_ok#47:
    arm64.add x2, x0, 1
    arm64.cmp x2, x0
    arm64.b.lo __rc_panic#50
  __rc_ok#49:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#52
  __im_buffer#53:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_missing#54
  __im_owned#56:
    arm64.loadRegBaseDisp.word64 x3, [x21 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach#55
  __im_viewed#57:
    arm64.sub x3, x0, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach#55
  __im_detached#58:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x1*8 + 0], x2
  forstep#43:
    arm64.add x1, x1, 1
  forhdr#41:
    arm64.cmp x1, 5
    arm64.b.lo each#42
    arm64.b __il_cont#17
  each#71:
    arm64.arm64AddLsl x3, x1, x1, lsl #1
  __rc_ok#70:
    arm64.add x4, x3, 1
    arm64.cmp x4, x3
    arm64.b.lo __rc_panic#50
  __rc_ok#69:
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#63
  __im_detached#64:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x1*8 + 0], x4
  forstep#60:
    arm64.add x1, x1, 1
  forhdr#59:
    arm64.cmp x1, 5
    arm64.b.lo each#71
  __il_cont#17:
    arm64.movImm x0, 0
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.b forhdr#1
  checkA:
    arm64.loadRegBaseIndexScale.word64 x4, [x3 + x1*8 + 0]
  __im_cont#15:
    arm64.add x0, x0, x4
  forstep#3:
    arm64.add x1, x1, 1
  forhdr#1:
    arm64.cmp x1, x2
    arm64.b.lt checkA
  forexit#4:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x21 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x21 + 0]
    arm64.b forhdr#5
  checkB:
    arm64.loadRegBaseIndexScale.word64 x4, [x3 + x1*8 + 0]
  __im_cont#16:
    arm64.add x0, x0, x4
  forstep#7:
    arm64.add x1, x1, 1
  forhdr#5:
    arm64.cmp x1, x2
    arm64.b.lt checkB
  forexit#8:
    arm64.cmp x0, 70
    arm64.b.eq ifcont#12
  sums:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.b exitdrop
  ifcont#12:
    arm64.cmp x20, 5
    arm64.b.eq ifcont#14
  logged:
    arm64.movImm x20, 2
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.b exitdrop
  ifcont#14:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitdrop:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.ret
  __im_slow#33:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr#25
    arm64.b tryok
  __im_detach#36:
    arm64.storeSlotReg slot0, x1
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.loadRegSlot x1, slot0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.b __im_detached#39
  __im_slow#52:
    arm64.storeSlotReg slot0, x1
    arm64.movRegReg x0, x21
    arm64.bl __managed_index_refused
    arm64.loadRegSlot x1, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#46
    arm64.b forstep#43
  __im_detach#55:
    arm64.storeSlotReg slot0, x1
    arm64.storeSlotReg slot1, x2
    arm64.movRegReg x0, x21
    arm64.bl __managed_cow_detach
    arm64.loadRegSlot x1, slot0
    arm64.loadRegSlot x2, slot1
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.b __im_detached#58
  __im_slow#63:
    arm64.storeSlotReg slot2, x0
    arm64.storeSlotReg slot0, x1
    arm64.storeSlotReg slot1, x2
    arm64.movRegReg x0, x21
    arm64.bl __managed_index_refused
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot2
    arm64.loadRegSlot x1, slot0
    arm64.loadRegSlot x2, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#46
    arm64.b forstep#60
  __rc_panic#29:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:11: Range check failed: value outside typealias 'Count'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.ret
  tryerr#25:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:11: fillAndLog: i < n = a.count()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.ret
  __im_missing#35:
    arm64.leaRdata x0, __str_blob_8  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.ret
  __rc_panic#31:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:14: Range check failed: value outside typealias 'Count'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.ret
  __im_missing#54:
    arm64.leaRdata x0, __str_blob_8  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.ret
  __rc_panic#50:
    arm64.leaRdata x0, __str_blob_5  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:22: Range check failed: value outside typealias 'Count'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.ret
  tryerr#46:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:22: fillOnly: i < n = a.count()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.epilogue 80
    arm64.ret
}
```
