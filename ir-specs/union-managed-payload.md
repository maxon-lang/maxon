---
feature: union-managed-payload
status: experimental
keywords: [union, payload, match, hold, retain, release, refcount, codegen]
category: ownership
---

# Union managed payloads: the cases that pin emitted code

The cases of `specs/union-managed-payload.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: a-read-only-payload-bound-for-one-call-costs-no-count -->
`Reader.stampInto` matches a union held in a `let` field and binds each case's `ByteArray` payload for one call that
writes a byte of another array. Nothing the call can reach releases the box, the reader or the payload, so the
bindings are borrowed: in the pin, `matcharm#19` and `matcharm#20` each load the payload and call `stamp`, with no
`__managed_incref`, `__managed_retain` or `__managed_decref` around the call. The next case is the same match over a
callee that can release its owner, and keeps both.
```maxon
union Backing
	small(bytes ByteArray)
	large(bytes ByteArray)
end 'Backing'

function stamp(source ByteArray, output ByteArray)
	try output.set(0, value: source.count() as Byte) otherwise panic("the output holds a byte")
end 'stamp'

type Reader
	let backing as Backing

	static function create(backing Backing) returns Reader
		return Self{backing: backing}
	end 'create'

	function stampInto(output ByteArray)
		match backing 'backing'
			small(bytes) then stamp(bytes, output: output)
			large(bytes) then stamp(bytes, output: output)
		end 'backing'
	end 'stampInto'
end 'Reader'

function main() returns ExitCode
	var payload = ByteArray.create()
	payload.resize(9)
	let reader = Reader.create(Backing.large(payload))
	var output = ByteArray.create()
	output.resize(1)
	reader.stampInto(output)

	return try output.get(0) otherwise 0
end 'main'
```
```exitcode
9
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

func @stamp {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rax, 0
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rsi, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc belowEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rdi, rcx, -24
    x64.loadRegBaseDisp.word64 rdi, [rdi + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.loadRegBaseDisp.word64 rdi, [rdx + 24]
    x64.cmpRegImm32 rdi, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.storeBaseDispReg.word64 [rcx + 0], rsi
    x64.jmp tryok
  __im_stride:
    x64.cmpRegImm32 rdi, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.storeBaseDispReg.byte [rcx + 0], rsi
    x64.jmp tryok
  __im_slow:
    x64.movRegReg rcx, rdx
    x64.movRegReg rdx, rax
    x64.movRegReg rax, rsi
    x64.callDirect __managed_set
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  tryok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at a-read-only-payload-bound-for-one-call-costs-no-count.test:8: Range check failed: value outside typealias 'Byte'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-read-only-payload-bound-for-one-call-costs-no-count.test:8: the output holds a byte\x0a"
    x64.callDirect mrt_panic
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
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 9
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 0], rax
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_retain
    x64.storeBaseDispReg.word64 [r12 + 8], r8
  __il_body#16:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.movRegReg rcx, r12
    x64.callDirect __mm_incref
    x64.storeBaseDispReg.word64 [r13 + 0], r12
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_Backing
  __il_cont#15:
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, r12
    x64.callDirect __managed_resize
  __il_body#18:
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, matchnext
  matcharm#19:
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.movRegReg rdx, r12
    x64.callDirect stamp
    x64.jmp __il_cont#14
  matchnext:
    x64.cmpRegImm32 rcx, 1
    x64.jcc notEqual, matchdead
  matcharm#20:
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.movRegReg rdx, r12
    x64.callDirect stamp
  __il_cont#14:
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r12 + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rax + rdx*8 + 0]
    x64.jmp tryok
  __im_stride:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok
  __im_slow:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit
  tryerr:
    x64.movRegImm32 r14, 0
    x64.jmp trycont
  critsplit:
    x64.movRegReg rax, r8
  tryok:
    x64.movRegReg r14, rax
  trycont:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitdrop#5:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_Reader
  exitdrop#6:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, r14
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  matchdead:
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

func @stamp {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rax, 0
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rsi, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc belowEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rdi, rcx, -24
    x64.loadRegBaseDisp.word64 rdi, [rdi + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.loadRegBaseDisp.word64 rdi, [rdx + 24]
    x64.cmpRegImm32 rdi, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.storeBaseDispReg.word64 [rcx + 0], rsi
    x64.jmp tryok
  __im_stride:
    x64.cmpRegImm32 rdi, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.storeBaseDispReg.byte [rcx + 0], rsi
    x64.jmp tryok
  __im_slow:
    x64.movRegReg rcx, rdx
    x64.movRegReg rdx, rax
    x64.movRegReg rax, rsi
    x64.callDirect __managed_set
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  tryok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at a-read-only-payload-bound-for-one-call-costs-no-count.test:8: Range check failed: value outside typealias 'Byte'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-read-only-payload-bound-for-one-call-costs-no-count.test:8: the output holds a byte\x0a"
    x64.callDirect mrt_panic
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
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 9
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 0], rax
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_retain
    x64.storeBaseDispReg.word64 [r12 + 8], r8
  __il_body#16:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.movRegReg rcx, r12
    x64.callDirect __mm_incref
    x64.storeBaseDispReg.word64 [r13 + 0], r12
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_Backing
  __il_cont#15:
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, r12
    x64.callDirect __managed_resize
  __il_body#18:
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, matchnext
  matcharm#19:
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.movRegReg rdx, r12
    x64.callDirect stamp
    x64.jmp __il_cont#14
  matchnext:
    x64.cmpRegImm32 rcx, 1
    x64.jcc notEqual, matchdead
  matcharm#20:
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.movRegReg rdx, r12
    x64.callDirect stamp
  __il_cont#14:
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r12 + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rax + rdx*8 + 0]
    x64.jmp tryok
  __im_stride:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok
  __im_slow:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit
  tryerr:
    x64.movRegImm32 r14, 0
    x64.jmp trycont
  critsplit:
    x64.movRegReg rax, r8
  tryok:
    x64.movRegReg r14, rax
  trycont:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitdrop#5:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_Reader
  exitdrop#6:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, r14
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  matchdead:
}
```
```TargetIr:arm64-macos
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @stamp {
  entry:
    arm64.prologue 16
    arm64.movImm x2, 0
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x3, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x4, x0, 24
    arm64.loadRegBaseDisp.word64 x4, [x4 + 16]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.loadRegBaseDisp.word64 x4, [x1 + 24]
    arm64.cmp x4, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.storeBaseDispReg.word64 [x0 + 0], x3
    arm64.b tryok
  __im_stride:
    arm64.cmp x4, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.storeBaseDispReg.byte [x0 + 0], x3
    arm64.b tryok
  __im_slow:
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.bl __managed_set
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  tryok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at a-read-only-payload-bound-for-one-call-costs-no-count.test:8: Range check failed: value outside typealias 'Byte'\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 16
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-read-only-payload-bound-for-one-call-costs-no-count.test:8: the output holds a byte\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 9
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_retain
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
  __il_body#16:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x21 + 0], x20
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Backing
  __il_cont#15:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x1, 1
    arm64.movRegReg x0, x20
    arm64.bl __managed_resize
  __il_body#18:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 0
    arm64.b.ne matchnext
  matcharm#19:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.movRegReg x1, x20
    arm64.bl stamp
    arm64.b __il_cont#14
  matchnext:
    arm64.cmp x1, 1
    arm64.b.ne matchdead
  matcharm#20:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.movRegReg x1, x20
    arm64.bl stamp
  __il_cont#14:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
    arm64.b tryok
  __im_stride:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok
  __im_slow:
    arm64.movRegReg x0, x20
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.movImm x22, 0
    arm64.b trycont
  tryok:
    arm64.movRegReg x22, x0
  trycont:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitdrop#5:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_Reader
  exitdrop#6:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x22
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  matchdead:
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

func @stamp {
  entry:
    arm64.prologue 16
    arm64.movImm x2, 0
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x3, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x4, x0, 24
    arm64.loadRegBaseDisp.word64 x4, [x4 + 16]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.loadRegBaseDisp.word64 x4, [x1 + 24]
    arm64.cmp x4, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.storeBaseDispReg.word64 [x0 + 0], x3
    arm64.b tryok
  __im_stride:
    arm64.cmp x4, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.storeBaseDispReg.byte [x0 + 0], x3
    arm64.b tryok
  __im_slow:
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.bl __managed_set
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  tryok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at a-read-only-payload-bound-for-one-call-costs-no-count.test:8: Range check failed: value outside typealias 'Byte'\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 16
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-read-only-payload-bound-for-one-call-costs-no-count.test:8: the output holds a byte\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 9
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_retain
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
  __il_body#16:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x21 + 0], x20
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Backing
  __il_cont#15:
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x1, 1
    arm64.movRegReg x0, x20
    arm64.bl __managed_resize
  __il_body#18:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 0
    arm64.b.ne matchnext
  matcharm#19:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.movRegReg x1, x20
    arm64.bl stamp
    arm64.b __il_cont#14
  matchnext:
    arm64.cmp x1, 1
    arm64.b.ne matchdead
  matcharm#20:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.movRegReg x1, x20
    arm64.bl stamp
  __il_cont#14:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
    arm64.b tryok
  __im_stride:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok
  __im_slow:
    arm64.movRegReg x0, x20
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.movImm x22, 0
    arm64.b trycont
  tryok:
    arm64.movRegReg x22, x0
  trycont:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitdrop#5:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_Reader
  exitdrop#6:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x22
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  matchdead:
}
```

<!-- test: a-read-only-payload-keeps-its-count-across-a-call-that-can-release-its-owner -->
The control for the case above. `Holder.measure` binds the payload of a union in a `let` field for one call, and that
call replaces the global that owns the holder, which releases the box the payload was read out of. The binding keeps
its count across the call: in the pin, `matcharm#7` retains the payload with `__managed_retain` before the replacement
and the arm's exit releases it with `__managed_decref`.
```maxon
union Slot
	filled(bytes ByteArray)
	empty
end 'Slot'

type Holder
	let slot as Slot

	static function create(slot Slot) returns Holder
		return Self{slot: slot}
	end 'create'

	function measure() returns Count
		return match slot 'slot'
			filled(bytes) gives replaceThenCount(bytes)
			empty gives 0
		end 'slot'
	end 'measure'
end 'Holder'

var theHolder = Holder.create(Slot.empty)

function replaceThenCount(bytes ByteArray) returns Count
	theHolder = Holder.create(Slot.empty)

	return bytes.count()
end 'replaceThenCount'

function main() returns ExitCode
	var payload = ByteArray.create()
	payload.resize(9)
	theHolder = Holder.create(Slot.filled(payload))

	return theHolder.measure()
end 'main'
```
```exitcode
9
```
```TargetIr:x64-windows
data {
  __data_theHolder@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_console_probe_stdin@32 = i8 0
  __mrt_console_probe_stdout@33 = i8 0
  __mrt_console_probe_stderr@34 = i8 0
  __mrt_program_started@35 = i8 0
}

func @Holder.create {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_incref
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_Slot
    x64.movRegReg r8, r12
    x64.epilogue 32
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
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 9
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r12 + 0], rax
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_retain
    x64.storeBaseDispReg.word64 [r12 + 8], r8
    x64.movRegReg rcx, r12
    x64.callDirect Holder.create
    x64.movRegReg r12, r8
    x64.leaRegGlobal rax, __data_theHolder
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect __destruct_Holder
    x64.leaRegGlobal rax, __data_theHolder
    x64.storeBaseDispReg.word64 [rax + 0], r12
    x64.leaRegGlobal rax, __data_theHolder
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.leaRegFunc rdx, [rip + __clone_Holder]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_own_deep
    x64.movRegReg r13, r8
  __il_body#6:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, matchnext
  matcharm#7:
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.movRegReg rcx, r12
    x64.callDirect __managed_retain
    x64.movRegReg r14, r8
  __il_body#13:
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegReg rcx, r8
    x64.callDirect Holder.create
    x64.movRegReg r15, r8
    x64.leaRegGlobal rax, __data_theHolder
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect __destruct_Holder
    x64.leaRegGlobal rax, __data_theHolder
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegBaseDisp.word64 r12, [r12 + 8]
  __il_cont#12:
    x64.movRegReg rcx, r14
    x64.callDirect __managed_decref
    x64.jmp __il_cont#5
  matchnext:
    x64.cmpRegImm32 rcx, 1
    x64.jcc notEqual, matchdead
  matcharm#8:
    x64.movRegImm32 r12, 0
  __il_cont#5:
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r12, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_Holder
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
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
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-read-only-payload-keeps-its-count-across-a-call-that-can-release-its-owner.test:35: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __data_theHolder@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @Holder.create {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_incref
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_Slot
    x64.movRegReg r8, r12
    x64.epilogue 32
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
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 9
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r12 + 0], rax
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_retain
    x64.storeBaseDispReg.word64 [r12 + 8], r8
    x64.movRegReg rcx, r12
    x64.callDirect Holder.create
    x64.movRegReg r12, r8
    x64.leaRegGlobal rax, __data_theHolder
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect __destruct_Holder
    x64.leaRegGlobal rax, __data_theHolder
    x64.storeBaseDispReg.word64 [rax + 0], r12
    x64.leaRegGlobal rax, __data_theHolder
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.leaRegFunc rdx, [rip + __clone_Holder]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_own_deep
    x64.movRegReg r13, r8
  __il_body#6:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, matchnext
  matcharm#7:
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.movRegReg rcx, r12
    x64.callDirect __managed_retain
    x64.movRegReg r14, r8
  __il_body#13:
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegReg rcx, r8
    x64.callDirect Holder.create
    x64.movRegReg r15, r8
    x64.leaRegGlobal rax, __data_theHolder
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect __destruct_Holder
    x64.leaRegGlobal rax, __data_theHolder
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegBaseDisp.word64 r12, [r12 + 8]
  __il_cont#12:
    x64.movRegReg rcx, r14
    x64.callDirect __managed_decref
    x64.jmp __il_cont#5
  matchnext:
    x64.cmpRegImm32 rcx, 1
    x64.jcc notEqual, matchdead
  matcharm#8:
    x64.movRegImm32 r12, 0
  __il_cont#5:
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r12, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_Holder
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
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
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-read-only-payload-keeps-its-count-across-a-call-that-can-release-its-owner.test:35: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __data_theHolder@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @Holder.create {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Slot
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 9
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_retain
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.movRegReg x0, x20
    arm64.bl Holder.create
    arm64.movRegReg x20, x0
    arm64.leaGlobal x0, __data_theHolder
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl __destruct_Holder
    arm64.leaGlobal x0, __data_theHolder
    arm64.storeBaseDispReg.word64 [x0 + 0], x20
    arm64.leaGlobal x0, __data_theHolder
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.leaFuncAddr x1, __clone_Holder
    arm64.movRegReg x0, x20
    arm64.bl __mm_own_deep
    arm64.movRegReg x21, x0
  __il_body#6:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 0
    arm64.b.ne matchnext
  matcharm#7:
    arm64.loadRegBaseDisp.word64 x20, [x0 + 8]
    arm64.movRegReg x0, x20
    arm64.bl __managed_retain
    arm64.movRegReg x22, x0
  __il_body#13:
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.bl Holder.create
    arm64.movRegReg x23, x0
    arm64.leaGlobal x0, __data_theHolder
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl __destruct_Holder
    arm64.leaGlobal x0, __data_theHolder
    arm64.storeBaseDispReg.word64 [x0 + 0], x23
    arm64.loadRegBaseDisp.word64 x20, [x20 + 8]
  __il_cont#12:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
    arm64.b __il_cont#5
  matchnext:
    arm64.cmp x1, 1
    arm64.b.ne matchdead
  matcharm#8:
    arm64.movImm x20, 0
  __il_cont#5:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_Holder
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  matchdead:
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-read-only-payload-keeps-its-count-across-a-call-that-can-release-its-owner.test:35: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __data_theHolder@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_hwcap_atomics@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @Holder.create {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Slot
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 9
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_retain
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.movRegReg x0, x20
    arm64.bl Holder.create
    arm64.movRegReg x20, x0
    arm64.leaGlobal x0, __data_theHolder
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl __destruct_Holder
    arm64.leaGlobal x0, __data_theHolder
    arm64.storeBaseDispReg.word64 [x0 + 0], x20
    arm64.leaGlobal x0, __data_theHolder
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.leaFuncAddr x1, __clone_Holder
    arm64.movRegReg x0, x20
    arm64.bl __mm_own_deep
    arm64.movRegReg x21, x0
  __il_body#6:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 0
    arm64.b.ne matchnext
  matcharm#7:
    arm64.loadRegBaseDisp.word64 x20, [x0 + 8]
    arm64.movRegReg x0, x20
    arm64.bl __managed_retain
    arm64.movRegReg x22, x0
  __il_body#13:
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.bl Holder.create
    arm64.movRegReg x23, x0
    arm64.leaGlobal x0, __data_theHolder
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl __destruct_Holder
    arm64.leaGlobal x0, __data_theHolder
    arm64.storeBaseDispReg.word64 [x0 + 0], x23
    arm64.loadRegBaseDisp.word64 x20, [x20 + 8]
  __il_cont#12:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
    arm64.b __il_cont#5
  matchnext:
    arm64.cmp x1, 1
    arm64.b.ne matchdead
  matcharm#8:
    arm64.movImm x20, 0
  __il_cont#5:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_Holder
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  matchdead:
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-read-only-payload-keeps-its-count-across-a-call-that-can-release-its-owner.test:35: Range check failed: value outside typealias 'ExitCode'\x0a"
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

<!-- test: a-read-only-payload-keeps-its-count-across-a-call-that-overwrites-the-field-that-holds-it -->
The same control, with the owner overwritten in place instead of replaced. `Holder.measure` matches the union in a `var`
field and binds the payload for one call, and that call assigns the field, which releases the box the payload was read
out of. The store is the only thing in the call that can release it, and it is a store of a union, so the binding keeps
its count: in the pin, `matcharm#11` retains the payload with `__managed_retain` before the call and the arm's exit
releases it with `__managed_decref`.
```maxon
union Slot
	filled(bytes ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create(slot Slot) returns Holder
		return Self{slot: slot}
	end 'create'

	function measure() returns Count
		return match slot 'slot'
			filled(bytes) gives overwriteThenCount(self, bytes: bytes)
			empty gives 0
		end 'slot'
	end 'measure'
end 'Holder'

function overwriteThenCount(holder Holder, bytes ByteArray) returns Count
	holder.slot = Slot.empty

	return bytes.count()
end 'overwriteThenCount'

function main() returns ExitCode
	var payload = ByteArray.create()
	payload.resize(9)
	var holder = Holder.create(Slot.filled(payload))

	return holder.measure()
end 'main'
```
```exitcode
9
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
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 9
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r12 + 0], rax
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_retain
    x64.storeBaseDispReg.word64 [r12 + 8], r8
  __il_body#7:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.movRegReg rcx, r12
    x64.callDirect __mm_incref
    x64.storeBaseDispReg.word64 [r13 + 0], r12
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_Slot
  __il_body#10:
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, matchnext
  matcharm#11:
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.movRegReg rcx, r12
    x64.callDirect __managed_retain
    x64.movRegReg r14, r8
  __il_body#17:
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r15 + 0], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r15 + 8], rax
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.callDirect __destruct_Slot
    x64.storeBaseDispReg.word64 [r13 + 0], r15
    x64.loadRegBaseDisp.word64 r12, [r12 + 8]
  __il_cont#16:
    x64.movRegReg rcx, r14
    x64.callDirect __managed_decref
    x64.jmp __il_cont#9
  matchnext:
    x64.cmpRegImm32 rcx, 1
    x64.jcc notEqual, matchdead
  matcharm#12:
    x64.movRegImm32 r12, 0
  __il_cont#9:
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r12, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_Holder
  exitdrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, r12
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
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-read-only-payload-keeps-its-count-across-a-call-that-overwrites-the-field-that-holds-it.test:33: Range check failed: value outside typealias 'ExitCod"...
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
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 9
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r12 + 0], rax
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_retain
    x64.storeBaseDispReg.word64 [r12 + 8], r8
  __il_body#7:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.movRegReg rcx, r12
    x64.callDirect __mm_incref
    x64.storeBaseDispReg.word64 [r13 + 0], r12
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_Slot
  __il_body#10:
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, matchnext
  matcharm#11:
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.movRegReg rcx, r12
    x64.callDirect __managed_retain
    x64.movRegReg r14, r8
  __il_body#17:
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r15 + 0], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r15 + 8], rax
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.callDirect __destruct_Slot
    x64.storeBaseDispReg.word64 [r13 + 0], r15
    x64.loadRegBaseDisp.word64 r12, [r12 + 8]
  __il_cont#16:
    x64.movRegReg rcx, r14
    x64.callDirect __managed_decref
    x64.jmp __il_cont#9
  matchnext:
    x64.cmpRegImm32 rcx, 1
    x64.jcc notEqual, matchdead
  matcharm#12:
    x64.movRegImm32 r12, 0
  __il_cont#9:
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r12, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_Holder
  exitdrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, r12
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
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-read-only-payload-keeps-its-count-across-a-call-that-overwrites-the-field-that-holds-it.test:33: Range check failed: value outside typealias 'ExitCod"...
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
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 9
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_retain
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
  __il_body#7:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x21 + 0], x20
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Slot
  __il_body#10:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 0
    arm64.b.ne matchnext
  matcharm#11:
    arm64.loadRegBaseDisp.word64 x20, [x0 + 8]
    arm64.movRegReg x0, x20
    arm64.bl __managed_retain
    arm64.movRegReg x22, x0
  __il_body#17:
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x23 + 0], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x23 + 8], x0
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.bl __destruct_Slot
    arm64.storeBaseDispReg.word64 [x21 + 0], x23
    arm64.loadRegBaseDisp.word64 x20, [x20 + 8]
  __il_cont#16:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
    arm64.b __il_cont#9
  matchnext:
    arm64.cmp x1, 1
    arm64.b.ne matchdead
  matcharm#12:
    arm64.movImm x20, 0
  __il_cont#9:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_Holder
  exitdrop:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  matchdead:
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-read-only-payload-keeps-its-count-across-a-call-that-overwrites-the-field-that-holds-it.test:33: Range check failed: value outside typealias 'ExitCod"...
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
  __mrt_hwcap_atomics@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @main {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 9
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_retain
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
  __il_body#7:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x21 + 0], x20
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Slot
  __il_body#10:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 0
    arm64.b.ne matchnext
  matcharm#11:
    arm64.loadRegBaseDisp.word64 x20, [x0 + 8]
    arm64.movRegReg x0, x20
    arm64.bl __managed_retain
    arm64.movRegReg x22, x0
  __il_body#17:
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x23 + 0], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x23 + 8], x0
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.bl __destruct_Slot
    arm64.storeBaseDispReg.word64 [x21 + 0], x23
    arm64.loadRegBaseDisp.word64 x20, [x20 + 8]
  __il_cont#16:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
    arm64.b __il_cont#9
  matchnext:
    arm64.cmp x1, 1
    arm64.b.ne matchdead
  matcharm#12:
    arm64.movImm x20, 0
  __il_cont#9:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_Holder
  exitdrop:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  matchdead:
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-read-only-payload-keeps-its-count-across-a-call-that-overwrites-the-field-that-holds-it.test:33: Range check failed: value outside typealias 'ExitCod"...
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
