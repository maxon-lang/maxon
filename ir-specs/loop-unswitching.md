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
	var a = WordArray.create()
	a.resize(5)
	var log = HalfArray.create()
	fillAndLog(a, log: log, n: 5)
	var b = WordArray.create()
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
  __il_body#37:
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#38
  each#39:
    x64.imulRegRegImm32 rax, r13, 3
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegReg r13, rcx
    x64.jcc aboveEqual, __im_slow#45
  __im_buffer#46:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_missing#47
  __im_owned#49:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __im_detach#48
  __im_viewed#50:
    x64.leaRegRegImm32 rdx, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_detach#48
  __im_detached#51:
    x64.storeBaseIndexScaleReg.word64 [rcx + r13*8 + 0], rax
  tryok:
    x64.movRegImm32 r14, 8
  __il_body#53:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_capacity
    x64.cmpRegImm32 r8, 0
    x64.movRegImm32 rax, 1
    x64.jcc less, scmerge
  __rc_ok:
    x64.cmpRegImm32 r8, 8
    x64.setccReg below, rax
  scmerge:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, forstep#40
  grow:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __managed_mem_grow
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, forstep#40
  tryerr#58:
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
  forstep#40:
    x64.leaRegRegImm32 r13, r13, 1
  forhdr#38:
    x64.cmpRegImm32 r13, 5
    x64.jcc below, each#39
  __il_cont#36:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 5
    x64.movRegReg rcx, r13
    x64.callDirect __managed_resize
  __il_body#19:
    x64.movRegImm32 rdx, 0
  __us_test#73:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, forhdr#20
  __us_test#74:
    x64.loadRegBaseDisp.word64 rsi, [r13 + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, forhdr#20
  __us_test#75:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, forhdr#20
    x64.jmp forhdr#62
  each#21:
    x64.imulRegRegImm32 rax, rdx, 3
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rcx, [r13 + 8]
    x64.cmpRegReg rdx, rcx
    x64.jcc aboveEqual, __im_slow#27
  __im_buffer#28:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_missing#29
  __im_owned#31:
    x64.loadRegBaseDisp.word64 rsi, [r13 + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach#30
  __im_viewed#32:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach#30
  __im_detached#33:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rax
  forstep#22:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#20:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#21
    x64.jmp __il_cont#18
  each#72:
    x64.imulRegRegImm32 rsi, rdx, 3
    x64.leaRegRegImm32 rsi, rsi, 1
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#66
  __im_detached#67:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rsi
  forstep#63:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#62:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#72
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
    x64.jcc equal, __il_body#35
  sums:
    x64.movRegImm32 r14, 1
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.jmp exitdrop#11
  __il_body#35:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_capacity
  __il_cont#34:
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
  __im_slow#45:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#43
    x64.jmp tryok
  __im_detach#48:
    x64.storeSlotReg slot0, rax
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.jmp __im_detached#51
  __im_slow#27:
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rdx, slot1
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#25
    x64.jmp forstep#22
  __im_detach#30:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rdx, slot1
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.jmp __im_detached#33
  __im_slow#66:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot2, rcx
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot2
    x64.loadRegSlot rdx, slot1
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#25
    x64.jmp forstep#63
  tryerr#43:
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
  __im_missing#47:
    x64.leaRegRdata rcx, [rip + __str_blob_7]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing#29:
    x64.leaRegRdata rcx, [rip + __str_blob_7]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
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
  __il_body#37:
    arm64.movImm x21, 0
    arm64.b forhdr#38
  each#39:
    arm64.arm64AddLsl x0, x21, x21, lsl #1
    arm64.add x0, x0, 1
    arm64.loadRegBaseDisp.word64 x1, [x19 + 8]
    arm64.cmp x21, x1
    arm64.b.hs __im_slow#45
  __im_buffer#46:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_missing#47
  __im_owned#49:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_detach#48
  __im_viewed#50:
    arm64.sub x2, x1, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne __im_detach#48
  __im_detached#51:
    arm64.storeBaseIndexScaleReg.word64 [x1 + x21*8 + 0], x0
  tryok:
    arm64.movImm x22, 8
  __il_body#53:
    arm64.movRegReg x0, x20
    arm64.bl __managed_capacity
    arm64.cmp x0, 0
    arm64.movImm x1, 1
    arm64.b.lt critsplit
  __rc_ok:
    arm64.cmp x0, 8
    arm64.cset x0, lo
    arm64.b scmerge
  critsplit:
    arm64.movRegReg x0, x1
  scmerge:
    arm64.cbz x0, forstep#40
  grow:
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x22
    arm64.bl __managed_mem_grow
    arm64.cmp x9, 0
    arm64.b.eq forstep#40
  tryerr#58:
    arm64.leaRdata x0, __str_blob_2  ; "panic at Array.maxon:329: Array.reserve: the requested capacity exceeds what the buffer can hold\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  forstep#40:
    arm64.add x21, x21, 1
  forhdr#38:
    arm64.cmp x21, 5
    arm64.b.lo each#39
  __il_cont#36:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x21, x0
    arm64.movImm x1, 5
    arm64.movRegReg x0, x21
    arm64.bl __managed_resize
  __il_body#19:
    arm64.movImm x1, 0
  __us_test#73:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.cmp x2, 0
    arm64.b.eq forhdr#20
  __us_test#74:
    arm64.loadRegBaseDisp.word64 x3, [x21 + 16]
    arm64.cmp x3, 0
    arm64.b.lt forhdr#20
  __us_test#75:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne forhdr#20
    arm64.b forhdr#62
  each#21:
    arm64.arm64AddLsl x0, x1, x1, lsl #1
    arm64.add x0, x0, 1
    arm64.loadRegBaseDisp.word64 x2, [x21 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#27
  __im_buffer#28:
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_missing#29
  __im_owned#31:
    arm64.loadRegBaseDisp.word64 x3, [x21 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach#30
  __im_viewed#32:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach#30
  __im_detached#33:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x1*8 + 0], x0
  forstep#22:
    arm64.add x1, x1, 1
  forhdr#20:
    arm64.cmp x1, 5
    arm64.b.lo each#21
    arm64.b __il_cont#18
  each#72:
    arm64.arm64AddLsl x3, x1, x1, lsl #1
    arm64.add x3, x3, 1
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#66
  __im_detached#67:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x1*8 + 0], x3
  forstep#63:
    arm64.add x1, x1, 1
  forhdr#62:
    arm64.cmp x1, 5
    arm64.b.lo each#72
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
    arm64.b.eq __il_body#35
  sums:
    arm64.movImm x22, 1
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.b exitdrop#11
  __il_body#35:
    arm64.movRegReg x0, x20
    arm64.bl __managed_capacity
  __il_cont#34:
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
  __im_slow#45:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr#43
    arm64.b tryok
  __im_detach#48:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.b __im_detached#51
  __im_slow#27:
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x21
    arm64.bl __managed_index_refused
    arm64.loadRegSlot x1, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#25
    arm64.b forstep#22
  __im_detach#30:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x21
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.b __im_detached#33
  __im_slow#66:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.movRegReg x0, x21
    arm64.bl __managed_index_refused
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.cmp x9, 0
    arm64.b.ne tryerr#25
    arm64.b forstep#63
  tryerr#43:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-loop-calling-an-unexpanded-managed-entry-stays-unversioned.test:9: fillAndLog: i < n = a.count()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_missing#47:
    arm64.leaRdata x0, __str_blob_7  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_missing#29:
    arm64.leaRdata x0, __str_blob_7  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
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
  __il_body#37:
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#38
  each#39:
    x64.imulRegRegImm32 rax, r13, 3
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegReg r13, rcx
    x64.jcc aboveEqual, __im_slow#45
  __im_buffer#46:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_missing#47
  __im_owned#49:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __im_detach#48
  __im_viewed#50:
    x64.leaRegRegImm32 rdx, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_detach#48
  __im_detached#51:
    x64.storeBaseIndexScaleReg.word64 [rcx + r13*8 + 0], rax
  tryok:
    x64.movRegImm32 r14, 8
  __il_body#53:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_capacity
    x64.cmpRegImm32 r8, 0
    x64.movRegImm32 rax, 1
    x64.jcc less, scmerge
  __rc_ok:
    x64.cmpRegImm32 r8, 8
    x64.setccReg below, rax
  scmerge:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, forstep#40
  grow:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __managed_mem_grow
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, forstep#40
  tryerr#58:
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
  forstep#40:
    x64.leaRegRegImm32 r13, r13, 1
  forhdr#38:
    x64.cmpRegImm32 r13, 5
    x64.jcc below, each#39
  __il_cont#36:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 5
    x64.movRegReg rcx, r13
    x64.callDirect __managed_resize
  __il_body#19:
    x64.movRegImm32 rdx, 0
  __us_test#73:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, forhdr#20
  __us_test#74:
    x64.loadRegBaseDisp.word64 rsi, [r13 + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, forhdr#20
  __us_test#75:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, forhdr#20
    x64.jmp forhdr#62
  each#21:
    x64.imulRegRegImm32 rax, rdx, 3
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rcx, [r13 + 8]
    x64.cmpRegReg rdx, rcx
    x64.jcc aboveEqual, __im_slow#27
  __im_buffer#28:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_missing#29
  __im_owned#31:
    x64.loadRegBaseDisp.word64 rsi, [r13 + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach#30
  __im_viewed#32:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach#30
  __im_detached#33:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rax
  forstep#22:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#20:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#21
    x64.jmp __il_cont#18
  each#72:
    x64.imulRegRegImm32 rsi, rdx, 3
    x64.leaRegRegImm32 rsi, rsi, 1
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#66
  __im_detached#67:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rsi
  forstep#63:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#62:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#72
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
    x64.jcc equal, __il_body#35
  sums:
    x64.movRegImm32 r14, 1
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.jmp exitdrop#11
  __il_body#35:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_capacity
  __il_cont#34:
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
  __im_slow#45:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#43
    x64.jmp tryok
  __im_detach#48:
    x64.storeSlotReg slot0, rax
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.jmp __im_detached#51
  __im_slow#27:
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rdx, slot1
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#25
    x64.jmp forstep#22
  __im_detach#30:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rdx, slot1
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.jmp __im_detached#33
  __im_slow#66:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot2, rcx
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot2
    x64.loadRegSlot rdx, slot1
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#25
    x64.jmp forstep#63
  tryerr#43:
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
  __im_missing#47:
    x64.leaRegRdata rcx, [rip + __str_blob_7]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing#29:
    x64.leaRegRdata rcx, [rip + __str_blob_7]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
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
  __il_body#37:
    arm64.movImm x21, 0
    arm64.b forhdr#38
  each#39:
    arm64.arm64AddLsl x0, x21, x21, lsl #1
    arm64.add x0, x0, 1
    arm64.loadRegBaseDisp.word64 x1, [x19 + 8]
    arm64.cmp x21, x1
    arm64.b.hs __im_slow#45
  __im_buffer#46:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_missing#47
  __im_owned#49:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_detach#48
  __im_viewed#50:
    arm64.sub x2, x1, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne __im_detach#48
  __im_detached#51:
    arm64.storeBaseIndexScaleReg.word64 [x1 + x21*8 + 0], x0
  tryok:
    arm64.movImm x22, 8
  __il_body#53:
    arm64.movRegReg x0, x20
    arm64.bl __managed_capacity
    arm64.cmp x0, 0
    arm64.movImm x1, 1
    arm64.b.lt critsplit
  __rc_ok:
    arm64.cmp x0, 8
    arm64.cset x0, lo
    arm64.b scmerge
  critsplit:
    arm64.movRegReg x0, x1
  scmerge:
    arm64.cbz x0, forstep#40
  grow:
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x22
    arm64.bl __managed_mem_grow
    arm64.cmp x9, 0
    arm64.b.eq forstep#40
  tryerr#58:
    arm64.leaRdata x0, __str_blob_2  ; "panic at Array.maxon:329: Array.reserve: the requested capacity exceeds what the buffer can hold\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  forstep#40:
    arm64.add x21, x21, 1
  forhdr#38:
    arm64.cmp x21, 5
    arm64.b.lo each#39
  __il_cont#36:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x21, x0
    arm64.movImm x1, 5
    arm64.movRegReg x0, x21
    arm64.bl __managed_resize
  __il_body#19:
    arm64.movImm x1, 0
  __us_test#73:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.cmp x2, 0
    arm64.b.eq forhdr#20
  __us_test#74:
    arm64.loadRegBaseDisp.word64 x3, [x21 + 16]
    arm64.cmp x3, 0
    arm64.b.lt forhdr#20
  __us_test#75:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne forhdr#20
    arm64.b forhdr#62
  each#21:
    arm64.arm64AddLsl x0, x1, x1, lsl #1
    arm64.add x0, x0, 1
    arm64.loadRegBaseDisp.word64 x2, [x21 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#27
  __im_buffer#28:
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_missing#29
  __im_owned#31:
    arm64.loadRegBaseDisp.word64 x3, [x21 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach#30
  __im_viewed#32:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach#30
  __im_detached#33:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x1*8 + 0], x0
  forstep#22:
    arm64.add x1, x1, 1
  forhdr#20:
    arm64.cmp x1, 5
    arm64.b.lo each#21
    arm64.b __il_cont#18
  each#72:
    arm64.arm64AddLsl x3, x1, x1, lsl #1
    arm64.add x3, x3, 1
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#66
  __im_detached#67:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x1*8 + 0], x3
  forstep#63:
    arm64.add x1, x1, 1
  forhdr#62:
    arm64.cmp x1, 5
    arm64.b.lo each#72
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
    arm64.b.eq __il_body#35
  sums:
    arm64.movImm x22, 1
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.b exitdrop#11
  __il_body#35:
    arm64.movRegReg x0, x20
    arm64.bl __managed_capacity
  __il_cont#34:
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
  __im_slow#45:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr#43
    arm64.b tryok
  __im_detach#48:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.b __im_detached#51
  __im_slow#27:
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x21
    arm64.bl __managed_index_refused
    arm64.loadRegSlot x1, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#25
    arm64.b forstep#22
  __im_detach#30:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x21
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.b __im_detached#33
  __im_slow#66:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.movRegReg x0, x21
    arm64.bl __managed_index_refused
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.cmp x9, 0
    arm64.b.ne tryerr#25
    arm64.b forstep#63
  tryerr#43:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-loop-calling-an-unexpanded-managed-entry-stays-unversioned.test:9: fillAndLog: i < n = a.count()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_missing#47:
    arm64.leaRdata x0, __str_blob_7  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_missing#29:
    arm64.leaRdata x0, __str_blob_7  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
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
		var scratch = HalfArray.create()
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
	var a = WordArray.create()
	a.resize(5)
	let logged = fillAndLog(a, n: 5)
	var b = WordArray.create()
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
    x64.prologue 64
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
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegReg r13, rcx
    x64.jcc aboveEqual, __im_slow#27
  __im_buffer#28:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_missing#29
  __im_owned#31:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __im_detach#30
  __im_viewed#32:
    x64.leaRegRegImm32 rdx, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_detach#30
  __im_detached#33:
    x64.storeBaseIndexScaleReg.word64 [rcx + r13*8 + 0], rax
  tryok:
    x64.movRegImm32 rcx, 2
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r14, r8
    x64.movRegImm32 rdx, 7
    x64.movRegReg rcx, r14
    x64.callDirect __managed_push
    x64.loadRegBaseDisp.word64 rax, [r14 + 8]
    x64.leaRegRegReg r12, r12, rax
    x64.movRegReg rcx, r14
    x64.callDirect __managed_decref
  forstep#22:
    x64.leaRegRegImm32 r13, r13, 1
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
  __il_body#34:
    x64.movRegImm32 rdx, 0
  __us_test#60:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, forhdr#35
  __us_test#61:
    x64.loadRegBaseDisp.word64 rsi, [r13 + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, forhdr#35
  __us_test#62:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, forhdr#35
    x64.jmp forhdr#49
  each#36:
    x64.imulRegRegImm32 rax, rdx, 3
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rcx, [r13 + 8]
    x64.cmpRegReg rdx, rcx
    x64.jcc aboveEqual, __im_slow#42
  __im_buffer#43:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_missing#44
  __im_owned#46:
    x64.loadRegBaseDisp.word64 rsi, [r13 + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach#45
  __im_viewed#47:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach#45
  __im_detached#48:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rax
  forstep#37:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#35:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#36
    x64.jmp __il_cont#17
  each#59:
    x64.imulRegRegImm32 rsi, rdx, 3
    x64.leaRegRegImm32 rsi, rsi, 1
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#53
  __im_detached#54:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rsi
  forstep#50:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#49:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#59
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
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#27:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#25
    x64.jmp tryok
  __im_detach#30:
    x64.storeSlotReg slot0, rax
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.jmp __im_detached#33
  __im_slow#42:
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rdx, slot1
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#40
    x64.jmp forstep#37
  __im_detach#45:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rdx, slot1
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.jmp __im_detached#48
  __im_slow#53:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot2, rcx
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot2
    x64.loadRegSlot rdx, slot1
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#40
    x64.jmp forstep#50
  tryerr#25:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:11: fillAndLog: i < n = a.count()\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing#29:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing#44:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#40:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:22: fillOnly: i < n = a.count()\x0a"
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
  __il_body#19:
    x64.movRegImm32 r12, 0
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#20
  each#21:
    x64.imulRegRegImm32 rax, r13, 3
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegReg r13, rcx
    x64.jcc aboveEqual, __im_slow#27
  __im_buffer#28:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_missing#29
  __im_owned#31:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __im_detach#30
  __im_viewed#32:
    x64.leaRegRegImm32 rdx, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_detach#30
  __im_detached#33:
    x64.storeBaseIndexScaleReg.word64 [rcx + r13*8 + 0], rax
  tryok:
    x64.movRegImm32 rcx, 2
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r14, r8
    x64.movRegImm32 rdx, 7
    x64.movRegReg rcx, r14
    x64.callDirect __managed_push
    x64.loadRegBaseDisp.word64 rax, [r14 + 8]
    x64.leaRegRegReg r12, r12, rax
    x64.movRegReg rcx, r14
    x64.callDirect __managed_decref
  forstep#22:
    x64.leaRegRegImm32 r13, r13, 1
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
  __il_body#34:
    x64.movRegImm32 rdx, 0
  __us_test#60:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, forhdr#35
  __us_test#61:
    x64.loadRegBaseDisp.word64 rsi, [r13 + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, forhdr#35
  __us_test#62:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, forhdr#35
    x64.jmp forhdr#49
  each#36:
    x64.imulRegRegImm32 rax, rdx, 3
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rcx, [r13 + 8]
    x64.cmpRegReg rdx, rcx
    x64.jcc aboveEqual, __im_slow#42
  __im_buffer#43:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_missing#44
  __im_owned#46:
    x64.loadRegBaseDisp.word64 rsi, [r13 + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach#45
  __im_viewed#47:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach#45
  __im_detached#48:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rax
  forstep#37:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#35:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#36
    x64.jmp __il_cont#17
  each#59:
    x64.imulRegRegImm32 rsi, rdx, 3
    x64.leaRegRegImm32 rsi, rsi, 1
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#53
  __im_detached#54:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rsi
  forstep#50:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#49:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#59
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
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#27:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#25
    x64.jmp tryok
  __im_detach#30:
    x64.storeSlotReg slot0, rax
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.jmp __im_detached#33
  __im_slow#42:
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rdx, slot1
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#40
    x64.jmp forstep#37
  __im_detach#45:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rdx, slot1
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.jmp __im_detached#48
  __im_slow#53:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot2, rcx
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_index_refused
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot2
    x64.loadRegSlot rdx, slot1
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#40
    x64.jmp forstep#50
  tryerr#25:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:11: fillAndLog: i < n = a.count()\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing#29:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing#44:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#40:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:22: fillOnly: i < n = a.count()\x0a"
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
  __il_body#19:
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b forhdr#20
  each#21:
    arm64.arm64AddLsl x0, x21, x21, lsl #1
    arm64.add x0, x0, 1
    arm64.loadRegBaseDisp.word64 x1, [x19 + 8]
    arm64.cmp x21, x1
    arm64.b.hs __im_slow#27
  __im_buffer#28:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_missing#29
  __im_owned#31:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_detach#30
  __im_viewed#32:
    arm64.sub x2, x1, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne __im_detach#30
  __im_detached#33:
    arm64.storeBaseIndexScaleReg.word64 [x1 + x21*8 + 0], x0
  tryok:
    arm64.movImm x0, 2
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.movImm x1, 7
    arm64.movRegReg x0, x22
    arm64.bl __managed_push
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.add x20, x20, x0
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  forstep#22:
    arm64.add x21, x21, 1
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
  __il_body#34:
    arm64.movImm x1, 0
  __us_test#60:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.cmp x2, 0
    arm64.b.eq forhdr#35
  __us_test#61:
    arm64.loadRegBaseDisp.word64 x3, [x21 + 16]
    arm64.cmp x3, 0
    arm64.b.lt forhdr#35
  __us_test#62:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne forhdr#35
    arm64.b forhdr#49
  each#36:
    arm64.arm64AddLsl x0, x1, x1, lsl #1
    arm64.add x0, x0, 1
    arm64.loadRegBaseDisp.word64 x2, [x21 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#42
  __im_buffer#43:
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_missing#44
  __im_owned#46:
    arm64.loadRegBaseDisp.word64 x3, [x21 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach#45
  __im_viewed#47:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach#45
  __im_detached#48:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x1*8 + 0], x0
  forstep#37:
    arm64.add x1, x1, 1
  forhdr#35:
    arm64.cmp x1, 5
    arm64.b.lo each#36
    arm64.b __il_cont#17
  each#59:
    arm64.arm64AddLsl x3, x1, x1, lsl #1
    arm64.add x3, x3, 1
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#53
  __im_detached#54:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x1*8 + 0], x3
  forstep#50:
    arm64.add x1, x1, 1
  forhdr#49:
    arm64.cmp x1, 5
    arm64.b.lo each#59
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
    arm64.epilogue 80
    arm64.ret
  __im_slow#27:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr#25
    arm64.b tryok
  __im_detach#30:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.b __im_detached#33
  __im_slow#42:
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x21
    arm64.bl __managed_index_refused
    arm64.loadRegSlot x1, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#40
    arm64.b forstep#37
  __im_detach#45:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x21
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.b __im_detached#48
  __im_slow#53:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.movRegReg x0, x21
    arm64.bl __managed_index_refused
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.cmp x9, 0
    arm64.b.ne tryerr#40
    arm64.b forstep#50
  tryerr#25:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:11: fillAndLog: i < n = a.count()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_missing#29:
    arm64.leaRdata x0, __str_blob_5  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_missing#44:
    arm64.leaRdata x0, __str_blob_5  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  tryerr#40:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:22: fillOnly: i < n = a.count()\x0a"
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
  __il_body#19:
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b forhdr#20
  each#21:
    arm64.arm64AddLsl x0, x21, x21, lsl #1
    arm64.add x0, x0, 1
    arm64.loadRegBaseDisp.word64 x1, [x19 + 8]
    arm64.cmp x21, x1
    arm64.b.hs __im_slow#27
  __im_buffer#28:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_missing#29
  __im_owned#31:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_detach#30
  __im_viewed#32:
    arm64.sub x2, x1, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne __im_detach#30
  __im_detached#33:
    arm64.storeBaseIndexScaleReg.word64 [x1 + x21*8 + 0], x0
  tryok:
    arm64.movImm x0, 2
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.movImm x1, 7
    arm64.movRegReg x0, x22
    arm64.bl __managed_push
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.add x20, x20, x0
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  forstep#22:
    arm64.add x21, x21, 1
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
  __il_body#34:
    arm64.movImm x1, 0
  __us_test#60:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.cmp x2, 0
    arm64.b.eq forhdr#35
  __us_test#61:
    arm64.loadRegBaseDisp.word64 x3, [x21 + 16]
    arm64.cmp x3, 0
    arm64.b.lt forhdr#35
  __us_test#62:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne forhdr#35
    arm64.b forhdr#49
  each#36:
    arm64.arm64AddLsl x0, x1, x1, lsl #1
    arm64.add x0, x0, 1
    arm64.loadRegBaseDisp.word64 x2, [x21 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#42
  __im_buffer#43:
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_missing#44
  __im_owned#46:
    arm64.loadRegBaseDisp.word64 x3, [x21 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach#45
  __im_viewed#47:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach#45
  __im_detached#48:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x1*8 + 0], x0
  forstep#37:
    arm64.add x1, x1, 1
  forhdr#35:
    arm64.cmp x1, 5
    arm64.b.lo each#36
    arm64.b __il_cont#17
  each#59:
    arm64.arm64AddLsl x3, x1, x1, lsl #1
    arm64.add x3, x3, 1
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#53
  __im_detached#54:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x1*8 + 0], x3
  forstep#50:
    arm64.add x1, x1, 1
  forhdr#49:
    arm64.cmp x1, 5
    arm64.b.lo each#59
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
    arm64.epilogue 80
    arm64.ret
  __im_slow#27:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr#25
    arm64.b tryok
  __im_detach#30:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.b __im_detached#33
  __im_slow#42:
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x21
    arm64.bl __managed_index_refused
    arm64.loadRegSlot x1, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#40
    arm64.b forstep#37
  __im_detach#45:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x21
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.b __im_detached#48
  __im_slow#53:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.movRegReg x0, x21
    arm64.bl __managed_index_refused
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.cmp x9, 0
    arm64.b.ne tryerr#40
    arm64.b forstep#50
  tryerr#25:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:11: fillAndLog: i < n = a.count()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_missing#29:
    arm64.leaRdata x0, __str_blob_5  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  __im_missing#44:
    arm64.leaRdata x0, __str_blob_5  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  tryerr#40:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-loop-pushing-onto-a-record-it-just-created-stays-unversioned.test:22: fillOnly: i < n = a.count()\x0a"
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
