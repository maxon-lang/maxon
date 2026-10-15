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
    x64.leaRegRdata rbx, [rip + __layout_Map_Count$0_1000000_Count$0_1000000]
    x64.leaRegRdata rax, [rip + __witness_int.Hashable]
    x64.leaRegRdata rax, [rip + __witness_int.Equatable]
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
    x64.pushReg r15
    x64.prologue 40
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
    x64.movRegImm32 rcx, 3
  __il_body#42:
    x64.loadRegBaseDisp.byte rax, [r14 + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#44
  sharedView#43:
    x64.loadRegBaseDisp.word64 rax, [r14 + 8]
    x64.jmp __il_cont#41
  ifcont#44:
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
  __il_cont#41:
    x64.loadRegBaseDisp.word64 rbx, [rax + 8]
    x64.leaRegFunc rax, [rip + __clone_Map_Count$0_1000000_Count$0_1000000]
    x64.leaRegRdata r12, [rip + __layout_Map_Count$0_1000000_Count$0_1000000]
    x64.leaRegRdata r13, [rip + __witness_int.Hashable]
    x64.leaRegRdata r15, [rip + __witness_int.Equatable]
  __il_body#16:
    x64.callMem [r13 + 48]
    x64.movRegReg rcx, rbx
    x64.movRegImm32 rdx, 3
    x64.movRegReg rax, r8
    x64.movRegReg r9, r12
    x64.movRegReg rsi, r13
    x64.movRegReg rdi, r15
    x64.callDirect Map.findSlot
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, __il_body#20
  tryerr#18:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#15
  __il_body#20:
    x64.movRegImm rax, 9223372036854775808
    x64.cmpRegReg r8, rax
    x64.jcc above, __rc_panic
  __rc_ok:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.loadRegBaseDisp.word64 rax, [rcx + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegReg r8, rax
    x64.jcc aboveEqual, __im_slow
  __im_load#31:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rax + r8*8 + 0]
    x64.jmp __il_body#46
  __im_stride:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegReg r8, rax
    x64.jcc aboveEqual, __im_slow
  __im_load#32:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.leaRegRegReg rax, rax, r8
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp __il_body#46
  __im_slow:
    x64.movRegReg rdx, r8
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#22
  critsplit#56:
    x64.movRegReg rax, r8
  __il_body#46:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, matchnext#51
  matcharm#48:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#15
  matchnext#51:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, matchnext#52
  matcharm#47:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#15
  matchnext#52:
    x64.cmpRegImm32 rax, 2
    x64.jcc notEqual, matchdead
  matcharm#49:
    x64.movRegImm32 rax, 0
  __il_cont#15:
    x64.movRegImm32 rcx, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#55
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
    x64.loadRegBaseDisp.byte rax, [rax + 16]
    x64.jmp scmerge
  critsplit#55:
    x64.movRegReg rax, rcx
  scmerge:
    x64.movRegImm32 rcx, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#54
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
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegImm32 rax, 3
    x64.setccReg above, rax
    x64.jmp __il_cont#6
  critsplit#54:
    x64.movRegReg rax, rcx
  __il_cont#6:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#3
  asked:
    x64.movRegImm32 rbx, 1
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_Holder
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg r15
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
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  matchdead:
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_49]  ; "panic at Map.maxon:340: Range check failed: value outside typealias 'TableSlotIndex'\x0a"
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
  tryerr#22:
    x64.leaRegRdata rcx, [rip + __str_blob_21]  ; "panic at Map.maxon:341: Map.slotHoldsKey: states.get OOB \xe2\x80\x94 slot from findSlot is always in-bounds\x0a"
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
	let kept = shelf.holding()
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
  __il_body#12:
    x64.movRegImm32 rcx, 1
    x64.callDirect Note.create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
  __il_body#13:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
  __il_cont#10:
    x64.callDirect __mm_retain
    x64.movRegReg rbx, r8
  __il_body#14:
    x64.movRegImm32 rcx, 2
    x64.callDirect Note.create
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __mm_decref
    x64.storeBaseDispReg.word64 [r12 + 0], r13
  __il_cont#9:
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
