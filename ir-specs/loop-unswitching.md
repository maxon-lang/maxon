---
feature: loop-unswitching
status: experimental
keywords: [optimizer, codegen, loop, unswitching, versioning, callee-table, managed-memory]
category: codegen
---

# A guard the loop asks once: the cases that pin emitted code

The cases of `specs/loop-unswitching.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: a-loop-pushing-an-unexpanded-element-stays-unversioned -->
Control for the callee table, on the one shape where nothing else can refuse the loop. `HalfArray`'s
elements are two bytes wide, a stride `InlineManagedPrimitives` has no fast arm for, so `log.push` stays
the `__managed_push` call the lowering wrote: no inline length store, no guard chain, and the call's
header effect (`managedCalleeHeaderEffect`) is the only fact about it the unswitcher has. `fillAndLog`
also holds `a.set`, whose three shape guards are the thing to version, so a callee table that declared
`push` to write nothing would version this loop. The pin holds `fillAndLog`'s loop once, calling
`__managed_push`, and `fillOnly`'s loop - the same body without the push - twice, behind `__us_test`
blocks. A `__us_test` block over `fillAndLog`'s loop, or one copy of `fillOnly`'s, is the failure.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word
typealias Half = int(0 to 65535)
typealias HalfArray = Array with Half

function fillAndLog(a WordArray, log HalfArray, n Count)
	for i in 0 upto n 'each'
		try a.set(i, value: (i * 3 + 1) as Word) otherwise panic("fillAndLog: i < n = a.count()")
		log.push(7)
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
	if log.count() != 5 'logged'
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
    x64.movRegImm32 rcx, 2
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
  __il_body#20:
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#21
  each#22:
    x64.imulRegRegImm32 rax, r13, 3
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#28
  __im_bounds#29:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegReg r13, rcx
    x64.jcc aboveEqual, __im_slow#28
  __im_buffer#30:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#28
  __im_viewed#31:
    x64.leaRegRegImm32 rdx, rcx, -24
    x64.loadRegBaseDisp.word64 rdx, [rdx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_slow#28
  __im_store#32:
    x64.storeBaseIndexScaleReg.word64 [rcx + r13*8 + 0], rax
  tryok:
    x64.movRegImm32 rdx, 7
    x64.movRegReg rcx, r12
    x64.callDirect __managed_push
  forstep#23:
    x64.leaRegRegImm32 r13, r13, 1
  forhdr#21:
    x64.cmpRegImm32 r13, 5
    x64.jcc below, each#22
  __il_cont#19:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 5
    x64.movRegReg rcx, r13
    x64.callDirect __managed_resize
  __il_body#33:
    x64.movRegImm32 rdx, 0
  __us_test#56:
    x64.loadRegBaseDisp.word64 rax, [r13 + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, forhdr#34
  __us_test#57:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, forhdr#34
  __us_test#58:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, forhdr#34
    x64.jmp forhdr#46
  each#35:
    x64.imulRegRegImm32 rax, rdx, 3
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rcx, [r13 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#41
  __im_bounds#42:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 8]
    x64.cmpRegReg rdx, rcx
    x64.jcc aboveEqual, __im_slow#41
  __im_buffer#43:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#41
  __im_viewed#44:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#41
  __im_store#45:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rax
  forstep#36:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#34:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#35
    x64.jmp __il_cont#18
  each#55:
    x64.imulRegRegImm32 rsi, rdx, 3
    x64.leaRegRegImm32 rsi, rsi, 1
  __im_bounds#54:
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#50
  __im_store#51:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rsi
  forstep#47:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#46:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#55
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
    x64.jcc equal, ifcont#13
  sums:
    x64.movRegImm32 r14, 1
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.jmp exitdrop#11
  ifcont#13:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegImm32 rax, 5
    x64.jcc equal, ifcont#15
  logged:
    x64.movRegImm32 r14, 2
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.jmp exitdrop#11
  ifcont#15:
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
  __im_slow#28:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __managed_set
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#26
    x64.jmp tryok
  __im_slow#41:
    x64.storeSlotReg slot0, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_set
    x64.loadRegSlot rdx, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#39
    x64.jmp forstep#36
  __im_slow#50:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot2, rcx
    x64.storeSlotReg slot0, rdx
    x64.movRegReg rcx, r13
    x64.movRegReg rax, rsi
    x64.callDirect __managed_set
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot rcx, slot2
    x64.loadRegSlot rdx, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#39
    x64.jmp forstep#47
  tryerr#26:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-loop-pushing-an-unexpanded-element-stays-unversioned.test:9: fillAndLog: i < n = a.count()\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#39:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-loop-pushing-an-unexpanded-element-stays-unversioned.test:16: fillOnly: i < n = a.count()\x0a"
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
  __il_body#20:
    arm64.movImm x21, 0
    arm64.b forhdr#21
  each#22:
    arm64.arm64AddLsl x0, x21, x21, lsl #1
    arm64.add x2, x0, 1
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#28
  __im_bounds#29:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x21, x0
    arm64.b.hs __im_slow#28
  __im_buffer#30:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#28
  __im_viewed#31:
    arm64.sub x1, x0, 24
    arm64.loadRegBaseDisp.word64 x1, [x1 + 16]
    arm64.cmp x1, 0
    arm64.b.ne __im_slow#28
  __im_store#32:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x21*8 + 0], x2
  tryok:
    arm64.movImm x1, 7
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
  forstep#23:
    arm64.add x21, x21, 1
  forhdr#21:
    arm64.cmp x21, 5
    arm64.b.lo each#22
  __il_cont#19:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x21, x0
    arm64.movImm x1, 5
    arm64.movRegReg x0, x21
    arm64.bl __managed_resize
  __il_body#33:
    arm64.movImm x1, 0
  __us_test#56:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 16]
    arm64.cmp x0, 0
    arm64.b.lt forhdr#34
  __us_test#57:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.cmp x2, 0
    arm64.b.eq forhdr#34
  __us_test#58:
    arm64.sub x3, x2, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne forhdr#34
    arm64.b forhdr#46
  each#35:
    arm64.arm64AddLsl x0, x1, x1, lsl #1
    arm64.add x2, x0, 1
    arm64.loadRegBaseDisp.word64 x0, [x21 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#41
  __im_bounds#42:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#41
  __im_buffer#43:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#41
  __im_viewed#44:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#41
  __im_store#45:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x1*8 + 0], x2
  forstep#36:
    arm64.add x1, x1, 1
  forhdr#34:
    arm64.cmp x1, 5
    arm64.b.lo each#35
    arm64.b __il_cont#18
  each#55:
    arm64.arm64AddLsl x3, x1, x1, lsl #1
    arm64.add x3, x3, 1
  __im_bounds#54:
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#50
  __im_store#51:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x1*8 + 0], x3
  forstep#47:
    arm64.add x1, x1, 1
  forhdr#46:
    arm64.cmp x1, 5
    arm64.b.lo each#55
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
    arm64.b.eq ifcont#13
  sums:
    arm64.movImm x22, 1
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.b exitdrop#11
  ifcont#13:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x0, 5
    arm64.b.eq ifcont#15
  logged:
    arm64.movImm x22, 2
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.b exitdrop#11
  ifcont#15:
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
  __im_slow#28:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __managed_set
    arm64.cmp x9, 0
    arm64.b.ne tryerr#26
    arm64.b tryok
  __im_slow#41:
    arm64.storeSlotReg slot0, x1
    arm64.movRegReg x0, x21
    arm64.bl __managed_set
    arm64.loadRegSlot x1, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#39
    arm64.b forstep#36
  __im_slow#50:
    arm64.storeSlotReg slot1, x0
    arm64.storeSlotReg slot0, x1
    arm64.storeSlotReg slot2, x2
    arm64.movRegReg x0, x21
    arm64.movRegReg x2, x3
    arm64.bl __managed_set
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot1
    arm64.loadRegSlot x1, slot0
    arm64.loadRegSlot x2, slot2
    arm64.cmp x9, 0
    arm64.b.ne tryerr#39
    arm64.b forstep#47
  tryerr#26:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-loop-pushing-an-unexpanded-element-stays-unversioned.test:9: fillAndLog: i < n = a.count()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  tryerr#39:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-loop-pushing-an-unexpanded-element-stays-unversioned.test:16: fillOnly: i < n = a.count()\x0a"
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
  __il_body#20:
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#21
  each#22:
    x64.imulRegRegImm32 rax, r13, 3
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#28
  __im_bounds#29:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegReg r13, rcx
    x64.jcc aboveEqual, __im_slow#28
  __im_buffer#30:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#28
  __im_viewed#31:
    x64.leaRegRegImm32 rdx, rcx, -24
    x64.loadRegBaseDisp.word64 rdx, [rdx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_slow#28
  __im_store#32:
    x64.storeBaseIndexScaleReg.word64 [rcx + r13*8 + 0], rax
  tryok:
    x64.movRegImm32 rdx, 7
    x64.movRegReg rcx, r12
    x64.callDirect __managed_push
  forstep#23:
    x64.leaRegRegImm32 r13, r13, 1
  forhdr#21:
    x64.cmpRegImm32 r13, 5
    x64.jcc below, each#22
  __il_cont#19:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 5
    x64.movRegReg rcx, r13
    x64.callDirect __managed_resize
  __il_body#33:
    x64.movRegImm32 rdx, 0
  __us_test#56:
    x64.loadRegBaseDisp.word64 rax, [r13 + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, forhdr#34
  __us_test#57:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, forhdr#34
  __us_test#58:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, forhdr#34
    x64.jmp forhdr#46
  each#35:
    x64.imulRegRegImm32 rax, rdx, 3
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rcx, [r13 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#41
  __im_bounds#42:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 8]
    x64.cmpRegReg rdx, rcx
    x64.jcc aboveEqual, __im_slow#41
  __im_buffer#43:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#41
  __im_viewed#44:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#41
  __im_store#45:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rax
  forstep#36:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#34:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#35
    x64.jmp __il_cont#18
  each#55:
    x64.imulRegRegImm32 rsi, rdx, 3
    x64.leaRegRegImm32 rsi, rsi, 1
  __im_bounds#54:
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#50
  __im_store#51:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rsi
  forstep#47:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr#46:
    x64.cmpRegImm32 rdx, 5
    x64.jcc below, each#55
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
    x64.jcc equal, ifcont#13
  sums:
    x64.movRegImm32 r14, 1
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.jmp exitdrop#11
  ifcont#13:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegImm32 rax, 5
    x64.jcc equal, ifcont#15
  logged:
    x64.movRegImm32 r14, 2
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.jmp exitdrop#11
  ifcont#15:
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
  __im_slow#28:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __managed_set
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#26
    x64.jmp tryok
  __im_slow#41:
    x64.storeSlotReg slot0, rdx
    x64.movRegReg rcx, r13
    x64.callDirect __managed_set
    x64.loadRegSlot rdx, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#39
    x64.jmp forstep#36
  __im_slow#50:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot2, rcx
    x64.storeSlotReg slot0, rdx
    x64.movRegReg rcx, r13
    x64.movRegReg rax, rsi
    x64.callDirect __managed_set
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot rcx, slot2
    x64.loadRegSlot rdx, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#39
    x64.jmp forstep#47
  tryerr#26:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-loop-pushing-an-unexpanded-element-stays-unversioned.test:9: fillAndLog: i < n = a.count()\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 64
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#39:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-loop-pushing-an-unexpanded-element-stays-unversioned.test:16: fillOnly: i < n = a.count()\x0a"
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
  __il_body#20:
    arm64.movImm x21, 0
    arm64.b forhdr#21
  each#22:
    arm64.arm64AddLsl x0, x21, x21, lsl #1
    arm64.add x2, x0, 1
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#28
  __im_bounds#29:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x21, x0
    arm64.b.hs __im_slow#28
  __im_buffer#30:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#28
  __im_viewed#31:
    arm64.sub x1, x0, 24
    arm64.loadRegBaseDisp.word64 x1, [x1 + 16]
    arm64.cmp x1, 0
    arm64.b.ne __im_slow#28
  __im_store#32:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x21*8 + 0], x2
  tryok:
    arm64.movImm x1, 7
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
  forstep#23:
    arm64.add x21, x21, 1
  forhdr#21:
    arm64.cmp x21, 5
    arm64.b.lo each#22
  __il_cont#19:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x21, x0
    arm64.movImm x1, 5
    arm64.movRegReg x0, x21
    arm64.bl __managed_resize
  __il_body#33:
    arm64.movImm x1, 0
  __us_test#56:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 16]
    arm64.cmp x0, 0
    arm64.b.lt forhdr#34
  __us_test#57:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.cmp x2, 0
    arm64.b.eq forhdr#34
  __us_test#58:
    arm64.sub x3, x2, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne forhdr#34
    arm64.b forhdr#46
  each#35:
    arm64.arm64AddLsl x0, x1, x1, lsl #1
    arm64.add x2, x0, 1
    arm64.loadRegBaseDisp.word64 x0, [x21 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#41
  __im_bounds#42:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#41
  __im_buffer#43:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#41
  __im_viewed#44:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#41
  __im_store#45:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x1*8 + 0], x2
  forstep#36:
    arm64.add x1, x1, 1
  forhdr#34:
    arm64.cmp x1, 5
    arm64.b.lo each#35
    arm64.b __il_cont#18
  each#55:
    arm64.arm64AddLsl x3, x1, x1, lsl #1
    arm64.add x3, x3, 1
  __im_bounds#54:
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#50
  __im_store#51:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x1*8 + 0], x3
  forstep#47:
    arm64.add x1, x1, 1
  forhdr#46:
    arm64.cmp x1, 5
    arm64.b.lo each#55
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
    arm64.b.eq ifcont#13
  sums:
    arm64.movImm x22, 1
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.b exitdrop#11
  ifcont#13:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x0, 5
    arm64.b.eq ifcont#15
  logged:
    arm64.movImm x22, 2
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.b exitdrop#11
  ifcont#15:
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
  __im_slow#28:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __managed_set
    arm64.cmp x9, 0
    arm64.b.ne tryerr#26
    arm64.b tryok
  __im_slow#41:
    arm64.storeSlotReg slot0, x1
    arm64.movRegReg x0, x21
    arm64.bl __managed_set
    arm64.loadRegSlot x1, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#39
    arm64.b forstep#36
  __im_slow#50:
    arm64.storeSlotReg slot1, x0
    arm64.storeSlotReg slot0, x1
    arm64.storeSlotReg slot2, x2
    arm64.movRegReg x0, x21
    arm64.movRegReg x2, x3
    arm64.bl __managed_set
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot1
    arm64.loadRegSlot x1, slot0
    arm64.loadRegSlot x2, slot2
    arm64.cmp x9, 0
    arm64.b.ne tryerr#39
    arm64.b forstep#47
  tryerr#26:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-loop-pushing-an-unexpanded-element-stays-unversioned.test:9: fillAndLog: i < n = a.count()\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.epilogue 80
    arm64.ret
  tryerr#39:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-loop-pushing-an-unexpanded-element-stays-unversioned.test:16: fillOnly: i < n = a.count()\x0a"
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
