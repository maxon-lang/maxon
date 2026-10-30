---
feature: borrowed-field-getters
status: stable
keywords: [getter, borrow, field, receiver, refcount, hold, codegen, inliner]
category: memory
---

# Borrowed field getters: the cases that pin emitted code

The cases of `specs/borrowed-field-getters.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: a-two-way-getter-read-is-loads-and-a-branch -->
`view` returns one of two fields of its receiver, and `ask` reads three fields of its result in one
expression. The getter hands back no reference, so nothing counts the result: the pin below is `main` with
`ask` spliced in and `view` spliced into each of the three reads, each a load of the flag, a branch and a
load of the field, and with no `__mm_retain`, `__mm_own_deep` or `__destruct_Big` in it. A getter that returned a
counted record would leave a `callDirect` to `Holder.view` and a destructor call beside each read.
```maxon
typealias Count = int(0 to 1000000)
typealias CountArray = Array with Count
typealias CountMap = Map with (Count, Count)

type Big
	module var items = CountArray.create()
	module var lookup = CountMap.create()
	module var flag = false

	static function create() returns Big
		return Self{}
	end 'create'
end 'Big'

type Holder
	module var own as Big
	module let shared as Big
	module let reads as bool

	static function create() returns Holder
		return Self{own: Big.create(), shared: Big.create(), reads: false}
	end 'create'

	function view() returns Big
		if self.reads 'sharedView'
			return self.shared
		end 'sharedView'

		return self.own
	end 'view'

	function ask(key Count) returns bool
		return self.view().lookup.contains(key) or self.view().flag or self.view().items.count() > 3
	end 'ask'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create()

	if holder.ask(3) 'asked'
		return 1
	end 'asked'

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

func @Big.create {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
  __il_body#2:
    x64.movRegImm32 rcx, 4
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.storeSlotReg slot0, r8
  __il_body#4:
    x64.leaRegRdata rax, [rip + __witness_int.Hashable]
    x64.leaRegRdata rax, [rip + __witness_int.Equatable]
    x64.leaRegRdata rbx, [rip + __layout_Map_Count$0_1000000_Count$0_1000000]
  __il_body#11:
    x64.movRegImm32 rcx, 8
    x64.loadRegBaseDisp.word64 rdx, [rbx + 40]
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
  __il_body#12:
    x64.movRegImm32 rcx, 8
    x64.loadRegBaseDisp.word64 rdx, [rbx + 112]
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
  __il_body#13:
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r13, r8
  __il_body#14:
    x64.movRegImm32 rcx, 4
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r14, r8
  __il_cont#7:
    x64.movRegImm32 rcx, 56
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.storeBaseDispReg.word64 [r15 + 0], r12
    x64.storeBaseDispReg.word64 [r15 + 8], rbx
    x64.storeBaseDispReg.word64 [r15 + 16], r13
    x64.storeBaseDispReg.word64 [r15 + 24], r14
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r15 + 32], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r15 + 40], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r15 + 48], rax
  __il_cont#3:
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 rcx, 24
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.loadRegSlot rax, slot0
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 8], r15
    x64.storeBaseDispReg.byte [r8 + 16], rbx
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
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
  __il_body#5:
    x64.callDirect Big.create
    x64.movRegReg rbx, r8
    x64.callDirect Big.create
    x64.movRegReg r12, r8
    x64.movRegImm32 r13, 0
    x64.movRegImm32 rcx, 24
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.storeBaseDispReg.word64 [r14 + 0], rbx
    x64.storeBaseDispReg.word64 [r14 + 8], r12
    x64.storeBaseDispReg.byte [r14 + 16], r13
  __il_cont#4:
    x64.movRegImm32 rdx, 3
  __il_body#38:
    x64.loadRegBaseDisp.byte rax, [r14 + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#40
  sharedView#39:
    x64.loadRegBaseDisp.word64 rax, [r14 + 8]
    x64.jmp __il_cont#37
  ifcont#40:
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
  __il_cont#37:
    x64.loadRegBaseDisp.word64 rbx, [rax + 8]
    x64.leaRegRdata r9, [rip + __witness_int.Hashable]
    x64.leaRegRdata rsi, [rip + __witness_int.Equatable]
    x64.leaRegRdata rdi, [rip + __layout_Map_Count$0_1000000_Count$0_1000000]
  __il_body#42:
    x64.movRegImm32 rax, 3
  __il_cont#41:
    x64.movRegReg rcx, rbx
    x64.callDirect Map.findSlot
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, __il_body#17
  tryerr#15:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#12
  __il_body#17:
    x64.movRegImm rax, 9223372036854775808
    x64.cmpRegReg r8, rax
    x64.jcc above, __rc_panic
  __rc_ok:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegReg r8, rax
    x64.jcc aboveEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rax + r8*8 + 0]
    x64.jmp __il_body#44
  __im_stride:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.leaRegRegReg rax, rax, r8
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp __il_body#44
  __im_slow:
    x64.movRegReg rdx, r8
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#19
  critsplit#54:
    x64.movRegReg rax, r8
  __il_body#44:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, matchnext#49
  matcharm#46:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#12
  matchnext#49:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, matchnext#50
  matcharm#45:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#12
  matchnext#50:
    x64.cmpRegImm32 rax, 2
    x64.jcc notEqual, matchdead
  matcharm#47:
    x64.movRegImm32 rax, 0
  __il_cont#12:
    x64.movRegImm32 rcx, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#53
  __il_body#30:
    x64.loadRegBaseDisp.byte rax, [r14 + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#32
  sharedView#31:
    x64.loadRegBaseDisp.word64 rax, [r14 + 8]
    x64.jmp __il_cont#29
  ifcont#32:
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
  __il_cont#29:
    x64.loadRegBaseDisp.byte rax, [rax + 16]
    x64.jmp scmerge
  critsplit#53:
    x64.movRegReg rax, rcx
  scmerge:
    x64.movRegImm32 rcx, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#52
  __il_body#34:
    x64.loadRegBaseDisp.byte rax, [r14 + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#36
  sharedView#35:
    x64.loadRegBaseDisp.word64 rax, [r14 + 8]
    x64.jmp __il_cont#33
  ifcont#36:
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
  __il_cont#33:
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegImm32 rax, 3
    x64.setccReg above, rax
    x64.jmp __il_cont#6
  critsplit#52:
    x64.movRegReg rax, rcx
  __il_cont#6:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#3
  asked:
    x64.movRegImm32 rbx, 1
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_Holder
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont#3:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_Holder
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  matchdead:
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_52]  ; "panic at Map.maxon:329: Range check failed: value outside typealias 'TableSlotIndex'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#19:
    x64.leaRegRdata rcx, [rip + __str_blob_24]  ; "panic at Map.maxon:330: Map.slotHoldsKey: states.get OOB \xe2\x80\x94 slot from findSlot is always in-bounds\x0a"
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

func @Big.create {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
  __il_body#2:
    x64.movRegImm32 rcx, 4
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.storeSlotReg slot0, r8
  __il_body#4:
    x64.leaRegRdata rax, [rip + __witness_int.Hashable]
    x64.leaRegRdata rax, [rip + __witness_int.Equatable]
    x64.leaRegRdata rbx, [rip + __layout_Map_Count$0_1000000_Count$0_1000000]
  __il_body#11:
    x64.movRegImm32 rcx, 8
    x64.loadRegBaseDisp.word64 rdx, [rbx + 40]
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
  __il_body#12:
    x64.movRegImm32 rcx, 8
    x64.loadRegBaseDisp.word64 rdx, [rbx + 112]
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
  __il_body#13:
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r13, r8
  __il_body#14:
    x64.movRegImm32 rcx, 4
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r14, r8
  __il_cont#7:
    x64.movRegImm32 rcx, 56
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.storeBaseDispReg.word64 [r15 + 0], r12
    x64.storeBaseDispReg.word64 [r15 + 8], rbx
    x64.storeBaseDispReg.word64 [r15 + 16], r13
    x64.storeBaseDispReg.word64 [r15 + 24], r14
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r15 + 32], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r15 + 40], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r15 + 48], rax
  __il_cont#3:
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 rcx, 24
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.loadRegSlot rax, slot0
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 8], r15
    x64.storeBaseDispReg.byte [r8 + 16], rbx
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
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
  __il_body#5:
    x64.callDirect Big.create
    x64.movRegReg rbx, r8
    x64.callDirect Big.create
    x64.movRegReg r12, r8
    x64.movRegImm32 r13, 0
    x64.movRegImm32 rcx, 24
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.storeBaseDispReg.word64 [r14 + 0], rbx
    x64.storeBaseDispReg.word64 [r14 + 8], r12
    x64.storeBaseDispReg.byte [r14 + 16], r13
  __il_cont#4:
    x64.movRegImm32 rdx, 3
  __il_body#38:
    x64.loadRegBaseDisp.byte rax, [r14 + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#40
  sharedView#39:
    x64.loadRegBaseDisp.word64 rax, [r14 + 8]
    x64.jmp __il_cont#37
  ifcont#40:
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
  __il_cont#37:
    x64.loadRegBaseDisp.word64 rbx, [rax + 8]
    x64.leaRegRdata r9, [rip + __witness_int.Hashable]
    x64.leaRegRdata rsi, [rip + __witness_int.Equatable]
    x64.leaRegRdata rdi, [rip + __layout_Map_Count$0_1000000_Count$0_1000000]
  __il_body#42:
    x64.movRegImm32 rax, 3
  __il_cont#41:
    x64.movRegReg rcx, rbx
    x64.callDirect Map.findSlot
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, __il_body#17
  tryerr#15:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#12
  __il_body#17:
    x64.movRegImm rax, 9223372036854775808
    x64.cmpRegReg r8, rax
    x64.jcc above, __rc_panic
  __rc_ok:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegReg r8, rax
    x64.jcc aboveEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rax + r8*8 + 0]
    x64.jmp __il_body#44
  __im_stride:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.leaRegRegReg rax, rax, r8
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp __il_body#44
  __im_slow:
    x64.movRegReg rdx, r8
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#19
  critsplit#54:
    x64.movRegReg rax, r8
  __il_body#44:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, matchnext#49
  matcharm#46:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#12
  matchnext#49:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, matchnext#50
  matcharm#45:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#12
  matchnext#50:
    x64.cmpRegImm32 rax, 2
    x64.jcc notEqual, matchdead
  matcharm#47:
    x64.movRegImm32 rax, 0
  __il_cont#12:
    x64.movRegImm32 rcx, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#53
  __il_body#30:
    x64.loadRegBaseDisp.byte rax, [r14 + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#32
  sharedView#31:
    x64.loadRegBaseDisp.word64 rax, [r14 + 8]
    x64.jmp __il_cont#29
  ifcont#32:
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
  __il_cont#29:
    x64.loadRegBaseDisp.byte rax, [rax + 16]
    x64.jmp scmerge
  critsplit#53:
    x64.movRegReg rax, rcx
  scmerge:
    x64.movRegImm32 rcx, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#52
  __il_body#34:
    x64.loadRegBaseDisp.byte rax, [r14 + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#36
  sharedView#35:
    x64.loadRegBaseDisp.word64 rax, [r14 + 8]
    x64.jmp __il_cont#33
  ifcont#36:
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
  __il_cont#33:
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegImm32 rax, 3
    x64.setccReg above, rax
    x64.jmp __il_cont#6
  critsplit#52:
    x64.movRegReg rax, rcx
  __il_cont#6:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#3
  asked:
    x64.movRegImm32 rbx, 1
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_Holder
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont#3:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_Holder
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  matchdead:
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_52]  ; "panic at Map.maxon:329: Range check failed: value outside typealias 'TableSlotIndex'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#19:
    x64.leaRegRdata rcx, [rip + __str_blob_24]  ; "panic at Map.maxon:330: Map.slotHoldsKey: states.get OOB \xe2\x80\x94 slot from findSlot is always in-bounds\x0a"
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

func @Big.create {
  entry:
    arm64.prologue 96
    arm64.storeSlotReg slot8, x27
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#2:
    arm64.movImm x0, 4
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
  __il_body#4:
    arm64.leaRdata x0, __witness_int.Hashable
    arm64.leaRdata x0, __witness_int.Equatable
    arm64.leaRdata x20, __layout_Map_Count$0_1000000_Count$0_1000000
  __il_body#11:
    arm64.movImm x0, 8
    arm64.loadRegBaseDisp.word64 x1, [x20 + 40]
    arm64.bl __managed_create
    arm64.movRegReg x21, x0
  __il_body#12:
    arm64.movImm x0, 8
    arm64.loadRegBaseDisp.word64 x1, [x20 + 112]
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
  __il_body#13:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
  __il_body#14:
    arm64.movImm x0, 4
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x23, x0
  __il_cont#7:
    arm64.movImm x24, 0
    arm64.movImm x25, 0
    arm64.movImm x26, 0
    arm64.movImm x0, 56
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.storeBaseDispReg.word64 [x27 + 0], x21
    arm64.storeBaseDispReg.word64 [x27 + 8], x20
    arm64.storeBaseDispReg.word64 [x27 + 16], x22
    arm64.storeBaseDispReg.word64 [x27 + 24], x23
    arm64.storeBaseDispReg.word64 [x27 + 32], x24
    arm64.storeBaseDispReg.word64 [x27 + 40], x25
    arm64.storeBaseDispReg.word64 [x27 + 48], x26
  __il_cont#3:
    arm64.movImm x20, 0
    arm64.movImm x0, 24
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x19
    arm64.storeBaseDispReg.word64 [x0 + 8], x27
    arm64.storeBaseDispReg.byte [x0 + 16], x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.loadRegSlot x27, slot8
    arm64.epilogue 96
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#5:
    arm64.bl Big.create
    arm64.movRegReg x19, x0
    arm64.bl Big.create
    arm64.movRegReg x20, x0
    arm64.movImm x21, 0
    arm64.movImm x0, 24
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.storeBaseDispReg.word64 [x22 + 0], x19
    arm64.storeBaseDispReg.word64 [x22 + 8], x20
    arm64.storeBaseDispReg.byte [x22 + 16], x21
  __il_cont#4:
    arm64.movImm x1, 3
  __il_body#38:
    arm64.loadRegBaseDisp.byte x0, [x22 + 16]
    arm64.cbz x0, ifcont#40
  sharedView#39:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.b __il_cont#37
  ifcont#40:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
  __il_cont#37:
    arm64.loadRegBaseDisp.word64 x19, [x0 + 8]
    arm64.leaRdata x3, __witness_int.Hashable
    arm64.leaRdata x4, __witness_int.Equatable
    arm64.leaRdata x5, __layout_Map_Count$0_1000000_Count$0_1000000
  __il_body#42:
    arm64.movImm x2, 3
  __il_cont#41:
    arm64.movRegReg x0, x19
    arm64.bl Map.findSlot
    arm64.movRegReg x1, x0
    arm64.cmp x9, 0
    arm64.b.eq __il_body#17
  tryerr#15:
    arm64.movImm x0, 0
    arm64.b __il_cont#12
  __il_body#17:
    arm64.movImm x0, 9223372036854775808
    arm64.cmp x1, x0
    arm64.b.hi __rc_panic
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 24]
    arm64.cmp x2, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
    arm64.b __il_body#44
  __im_stride:
    arm64.cmp x2, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x0, x0, x1
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b __il_body#44
  __im_slow:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#19
  __il_body#44:
    arm64.cmp x0, 0
    arm64.b.ne matchnext#49
  matcharm#46:
    arm64.movImm x0, 0
    arm64.b __il_cont#12
  matchnext#49:
    arm64.cmp x0, 1
    arm64.b.ne matchnext#50
  matcharm#45:
    arm64.movImm x0, 1
    arm64.b __il_cont#12
  matchnext#50:
    arm64.cmp x0, 2
    arm64.b.ne matchdead
  matcharm#47:
    arm64.movImm x0, 0
  __il_cont#12:
    arm64.movImm x1, 1
    arm64.cbnz x0, critsplit#53
  __il_body#30:
    arm64.loadRegBaseDisp.byte x0, [x22 + 16]
    arm64.cbz x0, ifcont#32
  sharedView#31:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.b __il_cont#29
  ifcont#32:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
  __il_cont#29:
    arm64.loadRegBaseDisp.byte x0, [x0 + 16]
    arm64.b scmerge
  critsplit#53:
    arm64.movRegReg x0, x1
  scmerge:
    arm64.movImm x1, 1
    arm64.cbnz x0, critsplit#52
  __il_body#34:
    arm64.loadRegBaseDisp.byte x0, [x22 + 16]
    arm64.cbz x0, ifcont#36
  sharedView#35:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.b __il_cont#33
  ifcont#36:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
  __il_cont#33:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x0, 3
    arm64.cset x0, hi
    arm64.b __il_cont#6
  critsplit#52:
    arm64.movRegReg x0, x1
  __il_cont#6:
    arm64.cbz x0, ifcont#3
  asked:
    arm64.movImm x19, 1
    arm64.movRegReg x0, x22
    arm64.bl __destruct_Holder
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  ifcont#3:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x22
    arm64.bl __destruct_Holder
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  matchdead:
  __rc_panic:
    arm64.leaRdata x0, __str_blob_52  ; "panic at Map.maxon:329: Range check failed: value outside typealias 'TableSlotIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  tryerr#19:
    arm64.leaRdata x0, __str_blob_24  ; "panic at Map.maxon:330: Map.slotHoldsKey: states.get OOB \xe2\x80\x94 slot from findSlot is always in-bounds\x0a"
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

func @Big.create {
  entry:
    arm64.prologue 96
    arm64.storeSlotReg slot8, x27
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#2:
    arm64.movImm x0, 4
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
  __il_body#4:
    arm64.leaRdata x0, __witness_int.Hashable
    arm64.leaRdata x0, __witness_int.Equatable
    arm64.leaRdata x20, __layout_Map_Count$0_1000000_Count$0_1000000
  __il_body#11:
    arm64.movImm x0, 8
    arm64.loadRegBaseDisp.word64 x1, [x20 + 40]
    arm64.bl __managed_create
    arm64.movRegReg x21, x0
  __il_body#12:
    arm64.movImm x0, 8
    arm64.loadRegBaseDisp.word64 x1, [x20 + 112]
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
  __il_body#13:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
  __il_body#14:
    arm64.movImm x0, 4
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x23, x0
  __il_cont#7:
    arm64.movImm x24, 0
    arm64.movImm x25, 0
    arm64.movImm x26, 0
    arm64.movImm x0, 56
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.storeBaseDispReg.word64 [x27 + 0], x21
    arm64.storeBaseDispReg.word64 [x27 + 8], x20
    arm64.storeBaseDispReg.word64 [x27 + 16], x22
    arm64.storeBaseDispReg.word64 [x27 + 24], x23
    arm64.storeBaseDispReg.word64 [x27 + 32], x24
    arm64.storeBaseDispReg.word64 [x27 + 40], x25
    arm64.storeBaseDispReg.word64 [x27 + 48], x26
  __il_cont#3:
    arm64.movImm x20, 0
    arm64.movImm x0, 24
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x19
    arm64.storeBaseDispReg.word64 [x0 + 8], x27
    arm64.storeBaseDispReg.byte [x0 + 16], x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.loadRegSlot x27, slot8
    arm64.epilogue 96
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#5:
    arm64.bl Big.create
    arm64.movRegReg x19, x0
    arm64.bl Big.create
    arm64.movRegReg x20, x0
    arm64.movImm x21, 0
    arm64.movImm x0, 24
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.storeBaseDispReg.word64 [x22 + 0], x19
    arm64.storeBaseDispReg.word64 [x22 + 8], x20
    arm64.storeBaseDispReg.byte [x22 + 16], x21
  __il_cont#4:
    arm64.movImm x1, 3
  __il_body#38:
    arm64.loadRegBaseDisp.byte x0, [x22 + 16]
    arm64.cbz x0, ifcont#40
  sharedView#39:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.b __il_cont#37
  ifcont#40:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
  __il_cont#37:
    arm64.loadRegBaseDisp.word64 x19, [x0 + 8]
    arm64.leaRdata x3, __witness_int.Hashable
    arm64.leaRdata x4, __witness_int.Equatable
    arm64.leaRdata x5, __layout_Map_Count$0_1000000_Count$0_1000000
  __il_body#42:
    arm64.movImm x2, 3
  __il_cont#41:
    arm64.movRegReg x0, x19
    arm64.bl Map.findSlot
    arm64.movRegReg x1, x0
    arm64.cmp x9, 0
    arm64.b.eq __il_body#17
  tryerr#15:
    arm64.movImm x0, 0
    arm64.b __il_cont#12
  __il_body#17:
    arm64.movImm x0, 9223372036854775808
    arm64.cmp x1, x0
    arm64.b.hi __rc_panic
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 24]
    arm64.cmp x2, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
    arm64.b __il_body#44
  __im_stride:
    arm64.cmp x2, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x0, x0, x1
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b __il_body#44
  __im_slow:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#19
  __il_body#44:
    arm64.cmp x0, 0
    arm64.b.ne matchnext#49
  matcharm#46:
    arm64.movImm x0, 0
    arm64.b __il_cont#12
  matchnext#49:
    arm64.cmp x0, 1
    arm64.b.ne matchnext#50
  matcharm#45:
    arm64.movImm x0, 1
    arm64.b __il_cont#12
  matchnext#50:
    arm64.cmp x0, 2
    arm64.b.ne matchdead
  matcharm#47:
    arm64.movImm x0, 0
  __il_cont#12:
    arm64.movImm x1, 1
    arm64.cbnz x0, critsplit#53
  __il_body#30:
    arm64.loadRegBaseDisp.byte x0, [x22 + 16]
    arm64.cbz x0, ifcont#32
  sharedView#31:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.b __il_cont#29
  ifcont#32:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
  __il_cont#29:
    arm64.loadRegBaseDisp.byte x0, [x0 + 16]
    arm64.b scmerge
  critsplit#53:
    arm64.movRegReg x0, x1
  scmerge:
    arm64.movImm x1, 1
    arm64.cbnz x0, critsplit#52
  __il_body#34:
    arm64.loadRegBaseDisp.byte x0, [x22 + 16]
    arm64.cbz x0, ifcont#36
  sharedView#35:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.b __il_cont#33
  ifcont#36:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
  __il_cont#33:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x0, 3
    arm64.cset x0, hi
    arm64.b __il_cont#6
  critsplit#52:
    arm64.movRegReg x0, x1
  __il_cont#6:
    arm64.cbz x0, ifcont#3
  asked:
    arm64.movImm x19, 1
    arm64.movRegReg x0, x22
    arm64.bl __destruct_Holder
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  ifcont#3:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x22
    arm64.bl __destruct_Holder
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  matchdead:
  __rc_panic:
    arm64.leaRdata x0, __str_blob_52  ; "panic at Map.maxon:329: Range check failed: value outside typealias 'TableSlotIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  tryerr#19:
    arm64.leaRdata x0, __str_blob_24  ; "panic at Map.maxon:330: Map.slotHoldsKey: states.get OOB \xe2\x80\x94 slot from findSlot is always in-bounds\x0a"
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

<!-- test: a-getter-result-kept-across-a-store-to-its-field-is-counted -->
`kept` is bound to the getter's result and `retire` then stores a fresh record into the field the getter
read. Something in the binding's window replaces the field, so the binding takes a reference of its own: the
pin below holds the `__mm_retain` that reference is, and the release that ends it.
```maxon
typealias Count = int(0 to 1000000)

type Note
	module var weight as Count

	static function create(weight Count) returns Note
		return Note{weight: weight}
	end 'create'
end 'Note'

type Shelf
	var note as Note

	static function create() returns Shelf
		return Shelf{note: Note.create(1)}
	end 'create'

	function holding() returns Note
		return self.note
	end 'holding'

	function retire()
		self.note = Note.create(2)
	end 'retire'
end 'Shelf'

function main() returns ExitCode
	var shelf = Shelf.create()
	view kept = shelf.holding()
	shelf.retire()
	return 0 if kept.weight == 1 else 1
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

func @Note.create {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 1000000
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-getter-result-kept-across-a-store-to-its-field-is-counted.test:7: Range check failed: value outside typealias 'Count'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg rbx
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
  __il_body#9:
    x64.movRegImm32 rcx, 1
    x64.callDirect Note.create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
  __il_body#10:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
  __il_cont#7:
    x64.callDirect __mm_retain
    x64.movRegReg rbx, r8
  __il_body#11:
    x64.movRegImm32 rcx, 2
    x64.callDirect Note.create
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __mm_decref
    x64.storeBaseDispReg.word64 [r12 + 0], r13
  __il_cont#6:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, ternaryfalse
  ternarytrue:
    x64.movRegImm32 r13, 0
    x64.jmp matchcont
  ternaryfalse:
    x64.movRegImm32 r13, 1
  matchcont:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
  exitdrop:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_Shelf
  exitterminal:
    x64.movRegReg r8, r13
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

func @Note.create {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 1000000
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-getter-result-kept-across-a-store-to-its-field-is-counted.test:7: Range check failed: value outside typealias 'Count'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg rbx
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
  __il_body#9:
    x64.movRegImm32 rcx, 1
    x64.callDirect Note.create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
  __il_body#10:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
  __il_cont#7:
    x64.callDirect __mm_retain
    x64.movRegReg rbx, r8
  __il_body#11:
    x64.movRegImm32 rcx, 2
    x64.callDirect Note.create
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __mm_decref
    x64.storeBaseDispReg.word64 [r12 + 0], r13
  __il_cont#6:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, ternaryfalse
  ternarytrue:
    x64.movRegImm32 r13, 0
    x64.jmp matchcont
  ternaryfalse:
    x64.movRegImm32 r13, 1
  matchcont:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
  exitdrop:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_Shelf
  exitterminal:
    x64.movRegReg r8, r13
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

func @Note.create {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.movImm x16, 1000000
    arm64.cmp x19, x16
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-getter-result-kept-across-a-store-to-its-field-is-counted.test:7: Range check failed: value outside typealias 'Count'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#9:
    arm64.movImm x0, 1
    arm64.bl Note.create
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
  __il_body#10:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_cont#7:
    arm64.bl __mm_retain
    arm64.movRegReg x19, x0
  __il_body#11:
    arm64.movImm x0, 2
    arm64.bl Note.create
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mm_decref
    arm64.storeBaseDispReg.word64 [x20 + 0], x21
  __il_cont#6:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 1
    arm64.b.ne ternaryfalse
  ternarytrue:
    arm64.movImm x21, 0
    arm64.b matchcont
  ternaryfalse:
    arm64.movImm x21, 1
  matchcont:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
  exitdrop:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Shelf
  exitterminal:
    arm64.movRegReg x0, x21
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

func @Note.create {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.movImm x16, 1000000
    arm64.cmp x19, x16
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-getter-result-kept-across-a-store-to-its-field-is-counted.test:7: Range check failed: value outside typealias 'Count'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#9:
    arm64.movImm x0, 1
    arm64.bl Note.create
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
  __il_body#10:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_cont#7:
    arm64.bl __mm_retain
    arm64.movRegReg x19, x0
  __il_body#11:
    arm64.movImm x0, 2
    arm64.bl Note.create
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mm_decref
    arm64.storeBaseDispReg.word64 [x20 + 0], x21
  __il_cont#6:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 1
    arm64.b.ne ternaryfalse
  ternarytrue:
    arm64.movImm x21, 0
    arm64.b matchcont
  ternaryfalse:
    arm64.movImm x21, 1
  matchcont:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
  exitdrop:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Shelf
  exitterminal:
    arm64.movRegReg x0, x21
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```
