---
feature: stride-arms
status: stable
keywords: [array, element-size, stride, inline, managed-memory, codegen, packed, bool]
category: codegen
---

# Stride arms: the cases that pin emitted code

Each case pins the Target IR its program compiles to, on every native lane that runs it.

## Tests


<!-- test: a-16-bit-element-is-read-and-written-through-one-arm -->
An `Array with Half` has stride 2, so a checked `get` and a checked `set` are one 16-bit load and one 16-bit store each, with no stride compare and no call.
```maxon
typealias Half = int(0 to 65535)
typealias Cells = Array with Half
typealias Reader = function(Cells, ElementIndex) returns Half
typealias Writer = function(mutable Cells, ElementIndex, Half)

function read(cells Cells, at ElementIndex) returns Half
	return try cells.get(at) otherwise panic("read: index out of range")
end 'read'

function write(cells mutable Cells, at ElementIndex, value Half)
	try cells.set(at, value: value) otherwise panic("write: index out of range")
end 'write'

var reader = read as Reader
var writer = write as Writer

function main() returns ExitCode
	var cells = Cells.create(0)
	cells.push(40000)
	cells.push(40000)
	writer(cells, 1, 40000)

	if reader(cells, 1) != 40000 'differs'
		return 1
	end 'differs'

	return 0
end 'main'
```
```exitcode
0
```
```TargetIr:x64-windows
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_console_probe_stdin@40 = i8 0
  __mrt_console_probe_stdout@41 = i8 0
  __mrt_console_probe_stderr@42 = i8 0
  __mrt_program_started@43 = i8 0
}

func @read {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseIndexScale.word16 r8, [rax + rdx*2 + 0]
  tryok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-16-bit-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @write {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rax, 65535
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegReg rdx, rsi
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_detach
  critsplit:
    x64.movRegReg rcx, rsi
  __im_detached:
    x64.storeBaseIndexScaleReg.word16 [rcx + rdx*2 + 0], rax
  tryok:
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
  __im_detach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.jmp __im_detached
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-16-bit-element-is-read-and-written-through-one-arm.test:11: Range check failed: value outside typealias 'Half'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-16-bit-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
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
    x64.prologue 40
    x64.movRegImm32 rcx, 2
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 40000
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 40000
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.leaRegGlobal rax, __data_writer
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 40000
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg r9, r12
    x64.callReg rsi
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.leaRegGlobal rax, __data_reader
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callReg rsi
    x64.cmpRegImm32 r8, 40000
    x64.setccReg notEqual, r12
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, ifcont
  differs:
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
  ifcont:
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
}
```
```TargetIr:x64-linux
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @read {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseIndexScale.word16 r8, [rax + rdx*2 + 0]
  tryok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-16-bit-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @write {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rax, 65535
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegReg rdx, rsi
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_detach
  critsplit:
    x64.movRegReg rcx, rsi
  __im_detached:
    x64.storeBaseIndexScaleReg.word16 [rcx + rdx*2 + 0], rax
  tryok:
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
  __im_detach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.jmp __im_detached
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-16-bit-element-is-read-and-written-through-one-arm.test:11: Range check failed: value outside typealias 'Half'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-16-bit-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
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
    x64.prologue 40
    x64.movRegImm32 rcx, 2
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 40000
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 40000
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.leaRegGlobal rax, __data_writer
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 40000
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg r9, r12
    x64.callReg rsi
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.leaRegGlobal rax, __data_reader
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callReg rsi
    x64.cmpRegImm32 r8, 40000
    x64.setccReg notEqual, r12
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, ifcont
  differs:
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
  ifcont:
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
}
```
```TargetIr:arm64-macos
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_program_started@40 = i8 0
}

func @read {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseIndexScale.word16 x0, [x0 + x1*2 + 0]
  tryok:
    arm64.epilogue 16
    arm64.ret
  __im_slow:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-16-bit-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @write {
  entry:
    arm64.prologue 48
    arm64.cmp x2, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.movImm x16, 65535
    arm64.cmp x2, x16
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x1, x3
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_detach
  critsplit:
    arm64.movRegReg x0, x3
  __im_detached:
    arm64.storeBaseIndexScaleReg.word16 [x0 + x1*2 + 0], x2
  tryok:
    arm64.epilogue 48
    arm64.ret
  __im_slow:
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.bl __managed_cow_detach
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b __im_detached
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-16-bit-element-is-read-and-written-through-one-arm.test:11: Range check failed: value outside typealias 'Half'\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-16-bit-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 2
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 40000
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 40000
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.leaGlobal x0, __data_writer
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.movImm x2, 40000
    arm64.loadRegBaseDisp.word64 x4, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x3, x20
    arm64.blr x4
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.leaGlobal x0, __data_reader
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x3, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x20
    arm64.blr x3
    arm64.movImm x16, 40000
    arm64.cmp x0, x16
    arm64.cset x20, ne
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.cbz x20, ifcont
  differs:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __mrt_hwcap_atomics@32 = i64 0
  __slab_arena_list@40 = i64 0
  __slab_arena_map_l1@48 = i64 0
  __slab_state@56 = i64 0
  __mrt_program_started@64 = i8 0
}

func @read {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseIndexScale.word16 x0, [x0 + x1*2 + 0]
  tryok:
    arm64.epilogue 16
    arm64.ret
  __im_slow:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-16-bit-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @write {
  entry:
    arm64.prologue 48
    arm64.cmp x2, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.movImm x16, 65535
    arm64.cmp x2, x16
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x1, x3
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_detach
  critsplit:
    arm64.movRegReg x0, x3
  __im_detached:
    arm64.storeBaseIndexScaleReg.word16 [x0 + x1*2 + 0], x2
  tryok:
    arm64.epilogue 48
    arm64.ret
  __im_slow:
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.bl __managed_cow_detach
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b __im_detached
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-16-bit-element-is-read-and-written-through-one-arm.test:11: Range check failed: value outside typealias 'Half'\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-16-bit-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 2
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 40000
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 40000
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.leaGlobal x0, __data_writer
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.movImm x2, 40000
    arm64.loadRegBaseDisp.word64 x4, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x3, x20
    arm64.blr x4
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.leaGlobal x0, __data_reader
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x3, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x20
    arm64.blr x3
    arm64.movImm x16, 40000
    arm64.cmp x0, x16
    arm64.cset x20, ne
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.cbz x20, ifcont
  differs:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```


<!-- test: a-32-bit-element-is-read-and-written-through-one-arm -->
An `Array with Wide` has stride 4, so a checked `get` and a checked `set` are one 32-bit load and one 32-bit store each.
```maxon
typealias Wide = int(0 to u32.max)
typealias Cells = Array with Wide
typealias Reader = function(Cells, ElementIndex) returns Wide
typealias Writer = function(mutable Cells, ElementIndex, Wide)

function read(cells Cells, at ElementIndex) returns Wide
	return try cells.get(at) otherwise panic("read: index out of range")
end 'read'

function write(cells mutable Cells, at ElementIndex, value Wide)
	try cells.set(at, value: value) otherwise panic("write: index out of range")
end 'write'

var reader = read as Reader
var writer = write as Writer

function main() returns ExitCode
	var cells = Cells.create(0)
	cells.push(3000000000)
	cells.push(3000000000)
	writer(cells, 1, 3000000000)

	if reader(cells, 1) != 3000000000 'differs'
		return 1
	end 'differs'

	return 0
end 'main'
```
```exitcode
0
```
```TargetIr:x64-windows
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_console_probe_stdin@40 = i8 0
  __mrt_console_probe_stdout@41 = i8 0
  __mrt_console_probe_stderr@42 = i8 0
  __mrt_program_started@43 = i8 0
}

func @read {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseIndexScale.word32 r8, [rax + rdx*4 + 0]
  tryok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-32-bit-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @write {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rsi, 4294967295
    x64.cmpRegReg rax, rsi
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegReg rdx, rsi
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_detach
  critsplit:
    x64.movRegReg rcx, rsi
  __im_detached:
    x64.storeBaseIndexScaleReg.word32 [rcx + rdx*4 + 0], rax
  tryok:
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
  __im_detach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.jmp __im_detached
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-32-bit-element-is-read-and-written-through-one-arm.test:11: Range check failed: value outside typealias 'Wide'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-32-bit-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
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
    x64.prologue 40
    x64.movRegImm32 rcx, 4
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 3000000000
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 3000000000
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.leaRegGlobal rax, __data_writer
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 3000000000
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg r9, r12
    x64.callReg rsi
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.leaRegGlobal rax, __data_reader
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callReg rsi
    x64.movRegImm32 rax, 3000000000
    x64.cmpRegReg r8, rax
    x64.setccReg notEqual, r12
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, ifcont
  differs:
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
  ifcont:
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
}
```
```TargetIr:x64-linux
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @read {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseIndexScale.word32 r8, [rax + rdx*4 + 0]
  tryok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-32-bit-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @write {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rsi, 4294967295
    x64.cmpRegReg rax, rsi
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegReg rdx, rsi
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_detach
  critsplit:
    x64.movRegReg rcx, rsi
  __im_detached:
    x64.storeBaseIndexScaleReg.word32 [rcx + rdx*4 + 0], rax
  tryok:
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
  __im_detach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.jmp __im_detached
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-32-bit-element-is-read-and-written-through-one-arm.test:11: Range check failed: value outside typealias 'Wide'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-32-bit-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
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
    x64.prologue 40
    x64.movRegImm32 rcx, 4
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 3000000000
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 3000000000
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.leaRegGlobal rax, __data_writer
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 3000000000
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg r9, r12
    x64.callReg rsi
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.leaRegGlobal rax, __data_reader
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callReg rsi
    x64.movRegImm32 rax, 3000000000
    x64.cmpRegReg r8, rax
    x64.setccReg notEqual, r12
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, ifcont
  differs:
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
  ifcont:
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
}
```
```TargetIr:arm64-macos
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_program_started@40 = i8 0
}

func @read {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseIndexScale.word32 x0, [x0 + x1*4 + 0]
  tryok:
    arm64.epilogue 16
    arm64.ret
  __im_slow:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-32-bit-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @write {
  entry:
    arm64.prologue 48
    arm64.cmp x2, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.movImm x3, 4294967295
    arm64.cmp x2, x3
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x1, x3
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_detach
  critsplit:
    arm64.movRegReg x0, x3
  __im_detached:
    arm64.storeBaseIndexScaleReg.word32 [x0 + x1*4 + 0], x2
  tryok:
    arm64.epilogue 48
    arm64.ret
  __im_slow:
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.bl __managed_cow_detach
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b __im_detached
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-32-bit-element-is-read-and-written-through-one-arm.test:11: Range check failed: value outside typealias 'Wide'\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-32-bit-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 4
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 3000000000
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 3000000000
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.leaGlobal x0, __data_writer
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.movImm x2, 3000000000
    arm64.loadRegBaseDisp.word64 x4, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x3, x20
    arm64.blr x4
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.leaGlobal x0, __data_reader
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x3, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x20
    arm64.blr x3
    arm64.movImm x1, 3000000000
    arm64.cmp x0, x1
    arm64.cset x20, ne
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.cbz x20, ifcont
  differs:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __mrt_hwcap_atomics@32 = i64 0
  __slab_arena_list@40 = i64 0
  __slab_arena_map_l1@48 = i64 0
  __slab_state@56 = i64 0
  __mrt_program_started@64 = i8 0
}

func @read {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseIndexScale.word32 x0, [x0 + x1*4 + 0]
  tryok:
    arm64.epilogue 16
    arm64.ret
  __im_slow:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-32-bit-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @write {
  entry:
    arm64.prologue 48
    arm64.cmp x2, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.movImm x3, 4294967295
    arm64.cmp x2, x3
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x1, x3
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_detach
  critsplit:
    arm64.movRegReg x0, x3
  __im_detached:
    arm64.storeBaseIndexScaleReg.word32 [x0 + x1*4 + 0], x2
  tryok:
    arm64.epilogue 48
    arm64.ret
  __im_slow:
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.bl __managed_cow_detach
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b __im_detached
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-32-bit-element-is-read-and-written-through-one-arm.test:11: Range check failed: value outside typealias 'Wide'\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-32-bit-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 4
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 3000000000
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 3000000000
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.leaGlobal x0, __data_writer
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.movImm x2, 3000000000
    arm64.loadRegBaseDisp.word64 x4, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x3, x20
    arm64.blr x4
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.leaGlobal x0, __data_reader
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x3, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x20
    arm64.blr x3
    arm64.movImm x1, 3000000000
    arm64.cmp x0, x1
    arm64.cset x20, ne
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.cbz x20, ifcont
  differs:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```


<!-- test: a-bool-element-is-read-and-written-through-one-arm -->
An `Array with bool` packs one bit per element, so a checked `get` is one byte load, a shift and a mask, and a checked `set` is a read-modify-write of one byte, with no stride compare and no call.
```maxon

typealias Cells = Array with bool
typealias Reader = function(Cells, ElementIndex) returns bool
typealias Writer = function(mutable Cells, ElementIndex, bool)

function read(cells Cells, at ElementIndex) returns bool
	return try cells.get(at) otherwise panic("read: index out of range")
end 'read'

function write(cells mutable Cells, at ElementIndex, value bool)
	try cells.set(at, value: value) otherwise panic("write: index out of range")
end 'write'

var reader = read as Reader
var writer = write as Writer

function main() returns ExitCode
	var cells = Cells.create(0)
	cells.push(true)
	cells.push(true)
	writer(cells, 1, true)

	if reader(cells, 1) != true 'differs'
		return 1
	end 'differs'

	return 0
end 'main'
```
```exitcode
0
```
```TargetIr:x64-windows
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_console_probe_stdin@40 = i8 0
  __mrt_console_probe_stdout@41 = i8 0
  __mrt_console_probe_stderr@42 = i8 0
  __mrt_program_started@43 = i8 0
}

func @read {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.movRegReg rsi, rdx
    x64.shrRegImm8 rsi, rdx, 3
    x64.movRegReg rcx, rdx
    x64.andRegImm32 rcx, rdx, 7
    x64.leaRegRegReg rax, rax, rsi
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.shrRegCl rax, rax
    x64.andRegImm32 rax, rax, 1
    x64.movRegReg r8, rax
  tryok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-bool-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @write {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegReg rdx, rsi
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_detach
  critsplit:
    x64.movRegReg rcx, rsi
  __im_detached:
    x64.movRegReg rsi, rdx
    x64.shrRegImm8 rsi, rdx, 3
    x64.andRegImm32 rdx, rdx, 7
    x64.movRegImm32 rdi, 1
    x64.leaRegRegReg rsi, rcx, rsi
    x64.loadRegBaseDisp.byte r8, [rsi + 0]
    x64.movRegReg rcx, rdx
    x64.shlRegCl rdi, rdi
    x64.xorRegImm32 rdi, rdi, -1
    x64.andRegReg r8, r8, rdi
    x64.andRegImm32 rax, rax, 1
    x64.movRegReg rcx, rdx
    x64.shlRegCl rax, rax
    x64.orRegReg r8, r8, rax
    x64.storeBaseDispReg.byte [rsi + 0], r8
  tryok:
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
  __im_detach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.jmp __im_detached
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-bool-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
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
    x64.prologue 40
    x64.movRegImm rcx, 18446744073709551615
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.leaRegGlobal rax, __data_writer
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 1
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg r9, r12
    x64.callReg rsi
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.leaRegGlobal rax, __data_reader
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callReg rsi
    x64.cmpRegImm32 r8, 1
    x64.setccReg notEqual, r12
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, ifcont
  differs:
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
  ifcont:
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
}
```
```TargetIr:x64-linux
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @read {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.movRegReg rsi, rdx
    x64.shrRegImm8 rsi, rdx, 3
    x64.movRegReg rcx, rdx
    x64.andRegImm32 rcx, rdx, 7
    x64.leaRegRegReg rax, rax, rsi
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.shrRegCl rax, rax
    x64.andRegImm32 rax, rax, 1
    x64.movRegReg r8, rax
  tryok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-bool-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @write {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegReg rdx, rsi
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_detach
  critsplit:
    x64.movRegReg rcx, rsi
  __im_detached:
    x64.movRegReg rsi, rdx
    x64.shrRegImm8 rsi, rdx, 3
    x64.andRegImm32 rdx, rdx, 7
    x64.movRegImm32 rdi, 1
    x64.leaRegRegReg rsi, rcx, rsi
    x64.loadRegBaseDisp.byte r8, [rsi + 0]
    x64.movRegReg rcx, rdx
    x64.shlRegCl rdi, rdi
    x64.xorRegImm32 rdi, rdi, -1
    x64.andRegReg r8, r8, rdi
    x64.andRegImm32 rax, rax, 1
    x64.movRegReg rcx, rdx
    x64.shlRegCl rax, rax
    x64.orRegReg r8, r8, rax
    x64.storeBaseDispReg.byte [rsi + 0], r8
  tryok:
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
  __im_detach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.jmp __im_detached
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-bool-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
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
    x64.prologue 40
    x64.movRegImm rcx, 18446744073709551615
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.leaRegGlobal rax, __data_writer
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 1
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg r9, r12
    x64.callReg rsi
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.leaRegGlobal rax, __data_reader
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callReg rsi
    x64.cmpRegImm32 r8, 1
    x64.setccReg notEqual, r12
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, ifcont
  differs:
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
  ifcont:
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
}
```
```TargetIr:arm64-macos
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_program_started@40 = i8 0
}

func @read {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.lsr x2, x1, 3
    arm64.and x1, x1, 7
    arm64.add x0, x0, x2
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.lsrv x0, x0, x1
    arm64.and x0, x0, 1
  tryok:
    arm64.epilogue 16
    arm64.ret
  __im_slow:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-bool-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @write {
  entry:
    arm64.prologue 48
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x1, x3
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_detach
  critsplit:
    arm64.movRegReg x0, x3
  __im_detached:
    arm64.lsr x3, x1, 3
    arm64.and x1, x1, 7
    arm64.movImm x4, 1
    arm64.add x0, x0, x3
    arm64.loadRegBaseDisp.byte x3, [x0 + 0]
    arm64.lslv x4, x4, x1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x4, x4, x16
    arm64.and x3, x3, x4
    arm64.and x2, x2, 1
    arm64.lslv x1, x2, x1
    arm64.orr x1, x3, x1
    arm64.storeBaseDispReg.byte [x0 + 0], x1
  tryok:
    arm64.epilogue 48
    arm64.ret
  __im_slow:
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.bl __managed_cow_detach
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b __im_detached
  tryerr:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-bool-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  __im_missing:
    arm64.leaRdata x0, __str_blob_5  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 18446744073709551615
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.leaGlobal x0, __data_writer
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.movImm x2, 1
    arm64.loadRegBaseDisp.word64 x4, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x3, x20
    arm64.blr x4
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.leaGlobal x0, __data_reader
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x3, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x20
    arm64.blr x3
    arm64.cmp x0, 1
    arm64.cset x20, ne
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.cbz x20, ifcont
  differs:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __mrt_hwcap_atomics@32 = i64 0
  __slab_arena_list@40 = i64 0
  __slab_arena_map_l1@48 = i64 0
  __slab_state@56 = i64 0
  __mrt_program_started@64 = i8 0
}

func @read {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.lsr x2, x1, 3
    arm64.and x1, x1, 7
    arm64.add x0, x0, x2
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.lsrv x0, x0, x1
    arm64.and x0, x0, 1
  tryok:
    arm64.epilogue 16
    arm64.ret
  __im_slow:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-bool-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @write {
  entry:
    arm64.prologue 48
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x1, x3
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_detach
  critsplit:
    arm64.movRegReg x0, x3
  __im_detached:
    arm64.lsr x3, x1, 3
    arm64.and x1, x1, 7
    arm64.movImm x4, 1
    arm64.add x0, x0, x3
    arm64.loadRegBaseDisp.byte x3, [x0 + 0]
    arm64.lslv x4, x4, x1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x4, x4, x16
    arm64.and x3, x3, x4
    arm64.and x2, x2, 1
    arm64.lslv x1, x2, x1
    arm64.orr x1, x3, x1
    arm64.storeBaseDispReg.byte [x0 + 0], x1
  tryok:
    arm64.epilogue 48
    arm64.ret
  __im_slow:
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.bl __managed_cow_detach
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b __im_detached
  tryerr:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-bool-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  __im_missing:
    arm64.leaRdata x0, __str_blob_5  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 18446744073709551615
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.leaGlobal x0, __data_writer
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.movImm x2, 1
    arm64.loadRegBaseDisp.word64 x4, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x3, x20
    arm64.blr x4
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.leaGlobal x0, __data_reader
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x3, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x20
    arm64.blr x3
    arm64.cmp x0, 1
    arm64.cset x20, ne
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.cbz x20, ifcont
  differs:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```


<!-- test: a-2-bit-element-is-read-and-written-through-one-arm -->
An `Array with Crumb` packs two bits per element; the arm is the one-bit arm at a width of two.
```maxon
typealias Crumb = int(0 to 3)
typealias Cells = Array with Crumb
typealias Reader = function(Cells, ElementIndex) returns Crumb
typealias Writer = function(mutable Cells, ElementIndex, Crumb)

function read(cells Cells, at ElementIndex) returns Crumb
	return try cells.get(at) otherwise panic("read: index out of range")
end 'read'

function write(cells mutable Cells, at ElementIndex, value Crumb)
	try cells.set(at, value: value) otherwise panic("write: index out of range")
end 'write'

var reader = read as Reader
var writer = write as Writer

function main() returns ExitCode
	var cells = Cells.create(0)
	cells.push(2)
	cells.push(2)
	writer(cells, 1, 2)

	if reader(cells, 1) != 2 'differs'
		return 1
	end 'differs'

	return 0
end 'main'
```
```exitcode
0
```
```TargetIr:x64-windows
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_console_probe_stdin@40 = i8 0
  __mrt_console_probe_stdout@41 = i8 0
  __mrt_console_probe_stderr@42 = i8 0
  __mrt_program_started@43 = i8 0
}

func @read {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.imulRegRegImm32 rdx, rdx, 2
    x64.movRegReg rsi, rdx
    x64.shrRegImm8 rsi, rdx, 3
    x64.movRegReg rcx, rdx
    x64.andRegImm32 rcx, rdx, 7
    x64.leaRegRegReg rax, rax, rsi
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.shrRegCl rax, rax
    x64.andRegImm32 rax, rax, 3
    x64.movRegReg r8, rax
  tryok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-2-bit-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @write {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rax, 3
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegReg rdx, rsi
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_detach
  critsplit:
    x64.movRegReg rcx, rsi
  __im_detached:
    x64.imulRegRegImm32 rdx, rdx, 2
    x64.movRegReg rsi, rdx
    x64.shrRegImm8 rsi, rdx, 3
    x64.andRegImm32 rdx, rdx, 7
    x64.movRegImm32 rdi, 3
    x64.leaRegRegReg rsi, rcx, rsi
    x64.loadRegBaseDisp.byte r8, [rsi + 0]
    x64.movRegReg rcx, rdx
    x64.shlRegCl rdi, rdi
    x64.xorRegImm32 rdi, rdi, -1
    x64.andRegReg r8, r8, rdi
    x64.andRegImm32 rax, rax, 3
    x64.movRegReg rcx, rdx
    x64.shlRegCl rax, rax
    x64.orRegReg r8, r8, rax
    x64.storeBaseDispReg.byte [rsi + 0], r8
  tryok:
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
  __im_detach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.jmp __im_detached
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-2-bit-element-is-read-and-written-through-one-arm.test:11: Range check failed: value outside typealias 'Crumb'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-2-bit-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
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
    x64.prologue 40
    x64.movRegImm rcx, 18446744073709551614
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 2
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 2
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.leaRegGlobal rax, __data_writer
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 2
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg r9, r12
    x64.callReg rsi
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.leaRegGlobal rax, __data_reader
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callReg rsi
    x64.cmpRegImm32 r8, 2
    x64.setccReg notEqual, r12
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, ifcont
  differs:
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
  ifcont:
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
}
```
```TargetIr:x64-linux
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @read {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.imulRegRegImm32 rdx, rdx, 2
    x64.movRegReg rsi, rdx
    x64.shrRegImm8 rsi, rdx, 3
    x64.movRegReg rcx, rdx
    x64.andRegImm32 rcx, rdx, 7
    x64.leaRegRegReg rax, rax, rsi
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.shrRegCl rax, rax
    x64.andRegImm32 rax, rax, 3
    x64.movRegReg r8, rax
  tryok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-2-bit-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @write {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rax, 3
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegReg rdx, rsi
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_detach
  critsplit:
    x64.movRegReg rcx, rsi
  __im_detached:
    x64.imulRegRegImm32 rdx, rdx, 2
    x64.movRegReg rsi, rdx
    x64.shrRegImm8 rsi, rdx, 3
    x64.andRegImm32 rdx, rdx, 7
    x64.movRegImm32 rdi, 3
    x64.leaRegRegReg rsi, rcx, rsi
    x64.loadRegBaseDisp.byte r8, [rsi + 0]
    x64.movRegReg rcx, rdx
    x64.shlRegCl rdi, rdi
    x64.xorRegImm32 rdi, rdi, -1
    x64.andRegReg r8, r8, rdi
    x64.andRegImm32 rax, rax, 3
    x64.movRegReg rcx, rdx
    x64.shlRegCl rax, rax
    x64.orRegReg r8, r8, rax
    x64.storeBaseDispReg.byte [rsi + 0], r8
  tryok:
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
  __im_detach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.jmp __im_detached
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-2-bit-element-is-read-and-written-through-one-arm.test:11: Range check failed: value outside typealias 'Crumb'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-2-bit-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
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
    x64.prologue 40
    x64.movRegImm rcx, 18446744073709551614
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 2
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 2
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.leaRegGlobal rax, __data_writer
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 2
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg r9, r12
    x64.callReg rsi
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.leaRegGlobal rax, __data_reader
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callReg rsi
    x64.cmpRegImm32 r8, 2
    x64.setccReg notEqual, r12
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, ifcont
  differs:
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
  ifcont:
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
}
```
```TargetIr:arm64-macos
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_program_started@40 = i8 0
}

func @read {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.lsl x1, x1, 1
    arm64.lsr x2, x1, 3
    arm64.and x1, x1, 7
    arm64.add x0, x0, x2
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.lsrv x0, x0, x1
    arm64.and x0, x0, 3
  tryok:
    arm64.epilogue 16
    arm64.ret
  __im_slow:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-2-bit-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @write {
  entry:
    arm64.prologue 48
    arm64.cmp x2, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x2, 3
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x1, x3
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_detach
  critsplit:
    arm64.movRegReg x0, x3
  __im_detached:
    arm64.lsl x1, x1, 1
    arm64.lsr x3, x1, 3
    arm64.and x1, x1, 7
    arm64.movImm x4, 3
    arm64.add x0, x0, x3
    arm64.loadRegBaseDisp.byte x3, [x0 + 0]
    arm64.lslv x4, x4, x1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x4, x4, x16
    arm64.and x3, x3, x4
    arm64.and x2, x2, 3
    arm64.lslv x1, x2, x1
    arm64.orr x1, x3, x1
    arm64.storeBaseDispReg.byte [x0 + 0], x1
  tryok:
    arm64.epilogue 48
    arm64.ret
  __im_slow:
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.bl __managed_cow_detach
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b __im_detached
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-2-bit-element-is-read-and-written-through-one-arm.test:11: Range check failed: value outside typealias 'Crumb'\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-2-bit-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 18446744073709551614
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 2
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 2
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.leaGlobal x0, __data_writer
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.movImm x2, 2
    arm64.loadRegBaseDisp.word64 x4, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x3, x20
    arm64.blr x4
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.leaGlobal x0, __data_reader
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x3, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x20
    arm64.blr x3
    arm64.cmp x0, 2
    arm64.cset x20, ne
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.cbz x20, ifcont
  differs:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __mrt_hwcap_atomics@32 = i64 0
  __slab_arena_list@40 = i64 0
  __slab_arena_map_l1@48 = i64 0
  __slab_state@56 = i64 0
  __mrt_program_started@64 = i8 0
}

func @read {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.lsl x1, x1, 1
    arm64.lsr x2, x1, 3
    arm64.and x1, x1, 7
    arm64.add x0, x0, x2
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.lsrv x0, x0, x1
    arm64.and x0, x0, 3
  tryok:
    arm64.epilogue 16
    arm64.ret
  __im_slow:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-2-bit-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @write {
  entry:
    arm64.prologue 48
    arm64.cmp x2, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x2, 3
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x1, x3
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_detach
  critsplit:
    arm64.movRegReg x0, x3
  __im_detached:
    arm64.lsl x1, x1, 1
    arm64.lsr x3, x1, 3
    arm64.and x1, x1, 7
    arm64.movImm x4, 3
    arm64.add x0, x0, x3
    arm64.loadRegBaseDisp.byte x3, [x0 + 0]
    arm64.lslv x4, x4, x1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x4, x4, x16
    arm64.and x3, x3, x4
    arm64.and x2, x2, 3
    arm64.lslv x1, x2, x1
    arm64.orr x1, x3, x1
    arm64.storeBaseDispReg.byte [x0 + 0], x1
  tryok:
    arm64.epilogue 48
    arm64.ret
  __im_slow:
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.bl __managed_cow_detach
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b __im_detached
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-2-bit-element-is-read-and-written-through-one-arm.test:11: Range check failed: value outside typealias 'Crumb'\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-2-bit-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 18446744073709551614
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 2
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 2
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.leaGlobal x0, __data_writer
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.movImm x2, 2
    arm64.loadRegBaseDisp.word64 x4, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x3, x20
    arm64.blr x4
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.leaGlobal x0, __data_reader
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x3, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x20
    arm64.blr x3
    arm64.cmp x0, 2
    arm64.cset x20, ne
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.cbz x20, ifcont
  differs:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```


<!-- test: a-4-bit-element-is-read-and-written-through-one-arm -->
An `Array with Nibble` packs four bits per element; the arm is the one-bit arm at a width of four.
```maxon
typealias Nibble = int(0 to 15)
typealias Cells = Array with Nibble
typealias Reader = function(Cells, ElementIndex) returns Nibble
typealias Writer = function(mutable Cells, ElementIndex, Nibble)

function read(cells Cells, at ElementIndex) returns Nibble
	return try cells.get(at) otherwise panic("read: index out of range")
end 'read'

function write(cells mutable Cells, at ElementIndex, value Nibble)
	try cells.set(at, value: value) otherwise panic("write: index out of range")
end 'write'

var reader = read as Reader
var writer = write as Writer

function main() returns ExitCode
	var cells = Cells.create(0)
	cells.push(9)
	cells.push(9)
	writer(cells, 1, 9)

	if reader(cells, 1) != 9 'differs'
		return 1
	end 'differs'

	return 0
end 'main'
```
```exitcode
0
```
```TargetIr:x64-windows
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_console_probe_stdin@40 = i8 0
  __mrt_console_probe_stdout@41 = i8 0
  __mrt_console_probe_stderr@42 = i8 0
  __mrt_program_started@43 = i8 0
}

func @read {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.imulRegRegImm32 rdx, rdx, 4
    x64.movRegReg rsi, rdx
    x64.shrRegImm8 rsi, rdx, 3
    x64.movRegReg rcx, rdx
    x64.andRegImm32 rcx, rdx, 7
    x64.leaRegRegReg rax, rax, rsi
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.shrRegCl rax, rax
    x64.andRegImm32 rax, rax, 15
    x64.movRegReg r8, rax
  tryok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-4-bit-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @write {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rax, 15
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegReg rdx, rsi
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_detach
  critsplit:
    x64.movRegReg rcx, rsi
  __im_detached:
    x64.imulRegRegImm32 rdx, rdx, 4
    x64.movRegReg rsi, rdx
    x64.shrRegImm8 rsi, rdx, 3
    x64.andRegImm32 rdx, rdx, 7
    x64.movRegImm32 rdi, 15
    x64.leaRegRegReg rsi, rcx, rsi
    x64.loadRegBaseDisp.byte r8, [rsi + 0]
    x64.movRegReg rcx, rdx
    x64.shlRegCl rdi, rdi
    x64.xorRegImm32 rdi, rdi, -1
    x64.andRegReg r8, r8, rdi
    x64.andRegImm32 rax, rax, 15
    x64.movRegReg rcx, rdx
    x64.shlRegCl rax, rax
    x64.orRegReg r8, r8, rax
    x64.storeBaseDispReg.byte [rsi + 0], r8
  tryok:
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
  __im_detach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.jmp __im_detached
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-4-bit-element-is-read-and-written-through-one-arm.test:11: Range check failed: value outside typealias 'Nibble'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-4-bit-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
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
    x64.prologue 40
    x64.movRegImm rcx, 18446744073709551612
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 9
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 9
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.leaRegGlobal rax, __data_writer
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 9
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg r9, r12
    x64.callReg rsi
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.leaRegGlobal rax, __data_reader
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callReg rsi
    x64.cmpRegImm32 r8, 9
    x64.setccReg notEqual, r12
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, ifcont
  differs:
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
  ifcont:
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
}
```
```TargetIr:x64-linux
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @read {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.imulRegRegImm32 rdx, rdx, 4
    x64.movRegReg rsi, rdx
    x64.shrRegImm8 rsi, rdx, 3
    x64.movRegReg rcx, rdx
    x64.andRegImm32 rcx, rdx, 7
    x64.leaRegRegReg rax, rax, rsi
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.shrRegCl rax, rax
    x64.andRegImm32 rax, rax, 15
    x64.movRegReg r8, rax
  tryok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-4-bit-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @write {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rax, 15
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegReg rdx, rsi
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_detach
  critsplit:
    x64.movRegReg rcx, rsi
  __im_detached:
    x64.imulRegRegImm32 rdx, rdx, 4
    x64.movRegReg rsi, rdx
    x64.shrRegImm8 rsi, rdx, 3
    x64.andRegImm32 rdx, rdx, 7
    x64.movRegImm32 rdi, 15
    x64.leaRegRegReg rsi, rcx, rsi
    x64.loadRegBaseDisp.byte r8, [rsi + 0]
    x64.movRegReg rcx, rdx
    x64.shlRegCl rdi, rdi
    x64.xorRegImm32 rdi, rdi, -1
    x64.andRegReg r8, r8, rdi
    x64.andRegImm32 rax, rax, 15
    x64.movRegReg rcx, rdx
    x64.shlRegCl rax, rax
    x64.orRegReg r8, r8, rax
    x64.storeBaseDispReg.byte [rsi + 0], r8
  tryok:
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
  __im_detach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.jmp __im_detached
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-4-bit-element-is-read-and-written-through-one-arm.test:11: Range check failed: value outside typealias 'Nibble'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-4-bit-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
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
    x64.prologue 40
    x64.movRegImm rcx, 18446744073709551612
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 9
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 9
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.leaRegGlobal rax, __data_writer
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 9
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg r9, r12
    x64.callReg rsi
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.leaRegGlobal rax, __data_reader
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callReg rsi
    x64.cmpRegImm32 r8, 9
    x64.setccReg notEqual, r12
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, ifcont
  differs:
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
  ifcont:
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
}
```
```TargetIr:arm64-macos
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_program_started@40 = i8 0
}

func @read {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.lsl x1, x1, 2
    arm64.lsr x2, x1, 3
    arm64.and x1, x1, 7
    arm64.add x0, x0, x2
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.lsrv x0, x0, x1
    arm64.and x0, x0, 15
  tryok:
    arm64.epilogue 16
    arm64.ret
  __im_slow:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-4-bit-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @write {
  entry:
    arm64.prologue 48
    arm64.cmp x2, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x2, 15
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x1, x3
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_detach
  critsplit:
    arm64.movRegReg x0, x3
  __im_detached:
    arm64.lsl x1, x1, 2
    arm64.lsr x3, x1, 3
    arm64.and x1, x1, 7
    arm64.movImm x4, 15
    arm64.add x0, x0, x3
    arm64.loadRegBaseDisp.byte x3, [x0 + 0]
    arm64.lslv x4, x4, x1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x4, x4, x16
    arm64.and x3, x3, x4
    arm64.and x2, x2, 15
    arm64.lslv x1, x2, x1
    arm64.orr x1, x3, x1
    arm64.storeBaseDispReg.byte [x0 + 0], x1
  tryok:
    arm64.epilogue 48
    arm64.ret
  __im_slow:
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.bl __managed_cow_detach
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b __im_detached
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-4-bit-element-is-read-and-written-through-one-arm.test:11: Range check failed: value outside typealias 'Nibble'\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-4-bit-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 18446744073709551612
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 9
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 9
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.leaGlobal x0, __data_writer
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.movImm x2, 9
    arm64.loadRegBaseDisp.word64 x4, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x3, x20
    arm64.blr x4
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.leaGlobal x0, __data_reader
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x3, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x20
    arm64.blr x3
    arm64.cmp x0, 9
    arm64.cset x20, ne
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.cbz x20, ifcont
  differs:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __data_reader@0 = i64 0
  __data_writer@8 = i64 0
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __mrt_hwcap_atomics@32 = i64 0
  __slab_arena_list@40 = i64 0
  __slab_arena_map_l1@48 = i64 0
  __slab_state@56 = i64 0
  __mrt_program_started@64 = i8 0
}

func @read {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.lsl x1, x1, 2
    arm64.lsr x2, x1, 3
    arm64.and x1, x1, 7
    arm64.add x0, x0, x2
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.lsrv x0, x0, x1
    arm64.and x0, x0, 15
  tryok:
    arm64.epilogue 16
    arm64.ret
  __im_slow:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-4-bit-element-is-read-and-written-through-one-arm.test:8: read: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @write {
  entry:
    arm64.prologue 48
    arm64.cmp x2, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x2, 15
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x1, x3
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_detach
  critsplit:
    arm64.movRegReg x0, x3
  __im_detached:
    arm64.lsl x1, x1, 2
    arm64.lsr x3, x1, 3
    arm64.and x1, x1, 7
    arm64.movImm x4, 15
    arm64.add x0, x0, x3
    arm64.loadRegBaseDisp.byte x3, [x0 + 0]
    arm64.lslv x4, x4, x1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x4, x4, x16
    arm64.and x3, x3, x4
    arm64.and x2, x2, 15
    arm64.lslv x1, x2, x1
    arm64.orr x1, x3, x1
    arm64.storeBaseDispReg.byte [x0 + 0], x1
  tryok:
    arm64.epilogue 48
    arm64.ret
  __im_slow:
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.bl __managed_cow_detach
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b __im_detached
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-4-bit-element-is-read-and-written-through-one-arm.test:11: Range check failed: value outside typealias 'Nibble'\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-4-bit-element-is-read-and-written-through-one-arm.test:12: write: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 18446744073709551612
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 9
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 9
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.leaGlobal x0, __data_writer
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.movImm x2, 9
    arm64.loadRegBaseDisp.word64 x4, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x3, x20
    arm64.blr x4
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.leaGlobal x0, __data_reader
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x3, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x20
    arm64.blr x3
    arm64.cmp x0, 9
    arm64.cset x20, ne
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.cbz x20, ifcont
  differs:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```


<!-- test: a-shared-generic-body-forks-over-every-stride -->
A body compiled once for every instantiation does not know its element's stride, so a checked `get`, a checked `set` and a `push` in it compare the record's whole stride stamp against 8, 1, the packed -1, 2, 4, the packed -2 and -4, and last the three signed stamps (a 1-, 2- or 4-byte stride plus the signed flag), and each arm is one access. Only the three signed read arms sign-extend what they load, so the word and the unsigned byte arm pay nothing for the sign; a signed stamp on a store goes to the unsigned arm of its width. `Bag.at`, `Bag.put` and `Bag.add` carry the fork.
```maxon
typealias Half = int(0 to 65535)

type Bag uses Element
	typealias Items = Array with Element
	var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function add(value Element)
		items.push(value)
	end 'add'

	function at(i ElementIndex) returns Element
		return try items.get(i) otherwise panic("Bag.at: index out of range")
	end 'at'

	function put(i ElementIndex, value Element)
		try items.set(i, value: value) otherwise panic("Bag.put: index out of range")
	end 'put'
end 'Bag'

typealias HalfBag = Bag with Half

function main() returns ExitCode
	var bag = HalfBag.create()
	bag.add(40000)
	bag.add(7)
	bag.put(1, value: 9)

	if bag.at(0) != 40000 or bag.at(1) != 9 'differs'
		return 1
	end 'differs'

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

func @Bag.add {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseDisp.word64 rsi, [rax + 8]
    x64.loadRegBaseDisp.word64 rcx, [rax + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [rax + 16]
    x64.cmpRegReg rsi, rcx
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rdi, rcx, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.loadRegBaseDisp.word64 rdi, [rax + 24]
    x64.cmpRegImm32 rdi, 8
    x64.jcc notEqual, __im_stride#8
  __im_word:
    x64.storeBaseIndexScaleReg.word64 [rcx + rsi*8 + 0], rdx
    x64.leaRegRegImm32 rcx, rsi, 1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.jmp __im_cont
  __im_stride#8:
    x64.cmpRegImm32 rdi, 1
    x64.jcc equal, __im_byte
  __im_stride#10:
    x64.cmpRegImm32 rdi, -1
    x64.jcc notEqual, __im_stride#12
  __im_bit:
    x64.movRegReg rdi, rsi
    x64.shrRegImm8 rdi, rsi, 3
    x64.movRegReg r8, rsi
    x64.andRegImm32 r8, rsi, 7
    x64.movRegImm32 r9, 1
    x64.leaRegRegReg rdi, rcx, rdi
    x64.loadRegBaseDisp.byte r10, [rdi + 0]
    x64.movRegReg rcx, r8
    x64.shlRegCl r9, r9
    x64.xorRegImm32 r9, r9, -1
    x64.andRegReg r10, r10, r9
    x64.andRegImm32 rdx, rdx, 1
    x64.movRegReg rcx, r8
    x64.shlRegCl rdx, rdx
    x64.orRegReg r10, r10, rdx
    x64.storeBaseDispReg.byte [rdi + 0], r10
    x64.leaRegRegImm32 rdx, rsi, 1
    x64.storeBaseDispReg.word64 [rax + 8], rdx
    x64.jmp __im_cont
  __im_stride#12:
    x64.cmpRegImm32 rdi, 2
    x64.jcc equal, __im_bytes2
  __im_stride#14:
    x64.cmpRegImm32 rdi, 4
    x64.jcc equal, __im_bytes4
  __im_stride#16:
    x64.cmpRegImm32 rdi, -2
    x64.jcc notEqual, __im_stride#18
  __im_crumb:
    x64.imulRegRegImm32 rdi, rsi, 2
    x64.movRegReg r8, rdi
    x64.shrRegImm8 r8, rdi, 3
    x64.andRegImm32 rdi, rdi, 7
    x64.movRegImm32 r9, 3
    x64.leaRegRegReg r8, rcx, r8
    x64.loadRegBaseDisp.byte r10, [r8 + 0]
    x64.movRegReg rcx, rdi
    x64.shlRegCl r9, r9
    x64.xorRegImm32 r9, r9, -1
    x64.andRegReg r10, r10, r9
    x64.andRegImm32 rdx, rdx, 3
    x64.movRegReg rcx, rdi
    x64.shlRegCl rdx, rdx
    x64.orRegReg r10, r10, rdx
    x64.storeBaseDispReg.byte [r8 + 0], r10
    x64.leaRegRegImm32 rdx, rsi, 1
    x64.storeBaseDispReg.word64 [rax + 8], rdx
    x64.jmp __im_cont
  __im_stride#18:
    x64.cmpRegImm32 rdi, -4
    x64.jcc notEqual, __im_stride#20
  __im_nibble:
    x64.imulRegRegImm32 rdi, rsi, 4
    x64.movRegReg r8, rdi
    x64.shrRegImm8 r8, rdi, 3
    x64.andRegImm32 rdi, rdi, 7
    x64.movRegImm32 r9, 15
    x64.leaRegRegReg r8, rcx, r8
    x64.loadRegBaseDisp.byte r10, [r8 + 0]
    x64.movRegReg rcx, rdi
    x64.shlRegCl r9, r9
    x64.xorRegImm32 r9, r9, -1
    x64.andRegReg r10, r10, r9
    x64.andRegImm32 rdx, rdx, 15
    x64.movRegReg rcx, rdi
    x64.shlRegCl rdx, rdx
    x64.orRegReg r10, r10, rdx
    x64.storeBaseDispReg.byte [r8 + 0], r10
    x64.leaRegRegImm32 rdx, rsi, 1
    x64.storeBaseDispReg.word64 [rax + 8], rdx
    x64.jmp __im_cont
  __im_stride#20:
    x64.movRegImm r8, 4294967297
    x64.cmpRegReg rdi, r8
    x64.jcc notEqual, __im_stride#21
  __im_byte:
    x64.leaRegRegReg rcx, rcx, rsi
    x64.storeBaseDispReg.byte [rcx + 0], rdx
    x64.leaRegRegImm32 rcx, rsi, 1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.jmp __im_cont
  __im_stride#21:
    x64.movRegImm r8, 4294967298
    x64.cmpRegReg rdi, r8
    x64.jcc notEqual, __im_stride#22
  __im_bytes2:
    x64.storeBaseIndexScaleReg.word16 [rcx + rsi*2 + 0], rdx
    x64.leaRegRegImm32 rcx, rsi, 1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.jmp __im_cont
  __im_stride#22:
    x64.movRegImm r8, 4294967300
    x64.cmpRegReg rdi, r8
    x64.jcc notEqual, __im_slow
  __im_bytes4:
    x64.storeBaseIndexScaleReg.word32 [rcx + rsi*4 + 0], rdx
    x64.leaRegRegImm32 rcx, rsi, 1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.jmp __im_cont
  __im_slow:
    x64.movRegReg rcx, rax
    x64.callDirect __managed_push
  __im_cont:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @Bag.at {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rax
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.cmpRegReg rdx, rcx
    x64.jcc aboveEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rcx, [rax + 24]
    x64.cmpRegImm32 rcx, 8
    x64.jcc notEqual, __im_stride#7
  __im_word:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseIndexScale.word64 rcx, [rcx + rdx*8 + 0]
    x64.jmp __im_loaded
  __im_stride#7:
    x64.cmpRegImm32 rcx, 1
    x64.jcc notEqual, __im_stride#9
  __im_byte:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegBaseDisp.byte rcx, [rcx + 0]
    x64.jmp __im_loaded
  __im_stride#9:
    x64.cmpRegImm32 rcx, -1
    x64.jcc notEqual, __im_stride#11
  __im_bit:
    x64.loadRegBaseDisp.word64 rsi, [rax + 0]
    x64.movRegReg rdi, rdx
    x64.shrRegImm8 rdi, rdx, 3
    x64.movRegReg rcx, rdx
    x64.andRegImm32 rcx, rdx, 7
    x64.leaRegRegReg rsi, rsi, rdi
    x64.loadRegBaseDisp.byte rsi, [rsi + 0]
    x64.shrRegCl rsi, rsi
    x64.andRegImm32 rsi, rsi, 1
    x64.movRegReg rcx, rsi
    x64.jmp __im_loaded
  __im_stride#11:
    x64.cmpRegImm32 rcx, 2
    x64.jcc notEqual, __im_stride#13
  __im_bytes2:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseIndexScale.word16 rcx, [rcx + rdx*2 + 0]
    x64.jmp __im_loaded
  __im_stride#13:
    x64.cmpRegImm32 rcx, 4
    x64.jcc notEqual, __im_stride#15
  __im_bytes4:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseIndexScale.word32 rcx, [rcx + rdx*4 + 0]
    x64.jmp __im_loaded
  __im_stride#15:
    x64.cmpRegImm32 rcx, -2
    x64.jcc notEqual, __im_stride#17
  __im_crumb:
    x64.loadRegBaseDisp.word64 rsi, [rax + 0]
    x64.imulRegRegImm32 rdi, rdx, 2
    x64.movRegReg r8, rdi
    x64.shrRegImm8 r8, rdi, 3
    x64.movRegReg rcx, rdi
    x64.andRegImm32 rcx, rdi, 7
    x64.leaRegRegReg rsi, rsi, r8
    x64.loadRegBaseDisp.byte rsi, [rsi + 0]
    x64.shrRegCl rsi, rsi
    x64.andRegImm32 rsi, rsi, 3
    x64.movRegReg rcx, rsi
    x64.jmp __im_loaded
  __im_stride#17:
    x64.cmpRegImm32 rcx, -4
    x64.jcc notEqual, __im_stride#19
  __im_nibble:
    x64.loadRegBaseDisp.word64 rsi, [rax + 0]
    x64.imulRegRegImm32 rdi, rdx, 4
    x64.movRegReg r8, rdi
    x64.shrRegImm8 r8, rdi, 3
    x64.movRegReg rcx, rdi
    x64.andRegImm32 rcx, rdi, 7
    x64.leaRegRegReg rsi, rsi, r8
    x64.loadRegBaseDisp.byte rsi, [rsi + 0]
    x64.shrRegCl rsi, rsi
    x64.andRegImm32 rsi, rsi, 15
    x64.movRegReg rcx, rsi
    x64.jmp __im_loaded
  __im_stride#19:
    x64.movRegImm rsi, 4294967297
    x64.cmpRegReg rcx, rsi
    x64.jcc notEqual, __im_stride#21
  __im_sbyte:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegBaseDisp.byte rcx, [rcx + 0]
    x64.shlRegImm8 rcx, rcx, 56
    x64.sarRegImm8 rcx, rcx, 56
    x64.jmp __im_loaded
  __im_stride#21:
    x64.movRegImm rsi, 4294967298
    x64.cmpRegReg rcx, rsi
    x64.jcc notEqual, __im_stride#23
  __im_sbytes2:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseIndexScale.word16 rcx, [rcx + rdx*2 + 0]
    x64.shlRegImm8 rcx, rcx, 48
    x64.sarRegImm8 rcx, rcx, 48
    x64.jmp __im_loaded
  __im_stride#23:
    x64.movRegImm rsi, 4294967300
    x64.cmpRegReg rcx, rsi
    x64.jcc notEqual, __im_slow
  __im_sbytes4:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseIndexScale.word32 rcx, [rcx + rdx*4 + 0]
    x64.shlRegImm8 rcx, rcx, 32
    x64.sarRegImm8 rcx, rcx, 32
  __im_loaded:
    x64.movRegImm32 rsi, 0
    x64.loadRegBaseDisp.word64 rdi, [rax + 40]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, critsplit#27
  __im_empty:
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, critsplit#28
  __im_slow:
    x64.movRegReg rcx, rax
    x64.callDirect __managed_get
    x64.movRegReg rdx, r8
    x64.jmp __im_cont
  critsplit#28:
    x64.movRegReg rdx, rcx
    x64.movRegReg r10, rsi
    x64.jmp __im_cont
  critsplit#27:
    x64.movRegReg rdx, rcx
    x64.movRegReg r10, rsi
  __im_cont:
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  tryok:
    x64.movRegReg rcx, rbx
    x64.callDirect __retain_type_param
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-shared-generic-body-forks-over-every-stride.test:17: Bag.at: index out of range\x0a"
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
    x64.prologue 48
    x64.leaRegRdata rax, [rip + __layout_Bag_Half]
  __il_body#8:
    x64.loadRegBaseDisp.word64 rcx, [rax + 72]
    x64.loadRegBaseDisp.word64 rdx, [rax + 40]
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
  __il_cont#7:
    x64.movRegImm32 rdx, 40000
    x64.movRegReg rcx, r12
    x64.callDirect Bag.add
    x64.movRegImm32 rdx, 7
    x64.movRegReg rcx, r12
    x64.callDirect Bag.add
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 9
  __il_body#9:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 40]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegImm32 rsi, 1
    x64.jcc belowEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_detach
  __im_detached:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 24]
    x64.cmpRegImm32 rdi, 8
    x64.jcc notEqual, __im_stride#22
  __im_word:
    x64.storeBaseDispReg.word64 [rsi + 8], rax
    x64.jmp __il_cont#6
  __im_stride#22:
    x64.cmpRegImm32 rdi, 1
    x64.jcc equal, __im_byte
  __im_stride#24:
    x64.cmpRegImm32 rdi, -1
    x64.jcc notEqual, __im_stride#26
  __im_bit:
    x64.loadRegBaseDisp.byte rax, [rsi + 0]
    x64.andRegImm32 rax, rax, -3
    x64.orRegImm32 rax, rax, 2
    x64.storeBaseDispReg.byte [rsi + 0], rax
    x64.jmp __il_cont#6
  __im_stride#26:
    x64.cmpRegImm32 rdi, 2
    x64.jcc equal, __im_bytes2
  __im_stride#28:
    x64.cmpRegImm32 rdi, 4
    x64.jcc equal, __im_bytes4
  __im_stride#30:
    x64.cmpRegImm32 rdi, -2
    x64.jcc notEqual, __im_stride#32
  __im_crumb:
    x64.loadRegBaseDisp.byte rax, [rsi + 0]
    x64.andRegImm32 rax, rax, -13
    x64.orRegImm32 rax, rax, 4
    x64.storeBaseDispReg.byte [rsi + 0], rax
    x64.jmp __il_cont#6
  __im_stride#32:
    x64.cmpRegImm32 rdi, -4
    x64.jcc notEqual, __im_stride#34
  __im_nibble:
    x64.loadRegBaseDisp.byte rax, [rsi + 0]
    x64.andRegImm32 rax, rax, -241
    x64.orRegImm32 rax, rax, 144
    x64.storeBaseDispReg.byte [rsi + 0], rax
    x64.jmp __il_cont#6
  __im_stride#34:
    x64.movRegImm r8, 4294967297
    x64.cmpRegReg rdi, r8
    x64.jcc notEqual, __im_stride#35
  __im_byte:
    x64.storeBaseDispReg.byte [rsi + 1], rax
    x64.jmp __il_cont#6
  __im_stride#35:
    x64.movRegImm r8, 4294967298
    x64.cmpRegReg rdi, r8
    x64.jcc notEqual, __im_stride#36
  __im_bytes2:
    x64.storeBaseDispReg.word16 [rsi + 2], rax
    x64.jmp __il_cont#6
  __im_stride#36:
    x64.movRegImm r8, 4294967300
    x64.cmpRegReg rdi, r8
    x64.jcc notEqual, __im_slow
  __im_bytes4:
    x64.storeBaseDispReg.word32 [rsi + 4], rax
    x64.jmp __il_cont#6
  __im_slow:
    x64.callDirect __managed_set
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  __il_cont#6:
    x64.movRegImm32 rdx, 0
    x64.leaRegRdata rax, [rip + __layout_Bag_Half]
    x64.movRegReg rcx, r12
    x64.callDirect Bag.at
    x64.cmpRegImm32 r8, 40000
    x64.movRegImm32 rax, 1
    x64.jcc notEqual, scmerge
  orrhs:
    x64.movRegImm32 rdx, 1
    x64.leaRegRdata rax, [rip + __layout_Bag_Half]
    x64.movRegReg rcx, r12
    x64.callDirect Bag.at
    x64.cmpRegImm32 r8, 9
    x64.setccReg notEqual, rax
  scmerge:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  differs:
    x64.movRegImm32 rbx, 1
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_Bag_Half
    x64.movRegReg r8, rbx
    x64.epilogue 48
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_Bag_Half
    x64.movRegReg r8, rbx
    x64.epilogue 48
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_detach:
    x64.storeSlotReg slot0, rcx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 9
    x64.loadRegSlot rcx, slot0
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.jmp __im_detached
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 48
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-shared-generic-body-forks-over-every-stride.test:21: Bag.put: index out of range\x0a"
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

func @Bag.add {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseDisp.word64 rsi, [rax + 8]
    x64.loadRegBaseDisp.word64 rcx, [rax + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [rax + 16]
    x64.cmpRegReg rsi, rcx
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rdi, rcx, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.loadRegBaseDisp.word64 rdi, [rax + 24]
    x64.cmpRegImm32 rdi, 8
    x64.jcc notEqual, __im_stride#8
  __im_word:
    x64.storeBaseIndexScaleReg.word64 [rcx + rsi*8 + 0], rdx
    x64.leaRegRegImm32 rcx, rsi, 1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.jmp __im_cont
  __im_stride#8:
    x64.cmpRegImm32 rdi, 1
    x64.jcc equal, __im_byte
  __im_stride#10:
    x64.cmpRegImm32 rdi, -1
    x64.jcc notEqual, __im_stride#12
  __im_bit:
    x64.movRegReg rdi, rsi
    x64.shrRegImm8 rdi, rsi, 3
    x64.movRegReg r8, rsi
    x64.andRegImm32 r8, rsi, 7
    x64.movRegImm32 r9, 1
    x64.leaRegRegReg rdi, rcx, rdi
    x64.loadRegBaseDisp.byte r10, [rdi + 0]
    x64.movRegReg rcx, r8
    x64.shlRegCl r9, r9
    x64.xorRegImm32 r9, r9, -1
    x64.andRegReg r10, r10, r9
    x64.andRegImm32 rdx, rdx, 1
    x64.movRegReg rcx, r8
    x64.shlRegCl rdx, rdx
    x64.orRegReg r10, r10, rdx
    x64.storeBaseDispReg.byte [rdi + 0], r10
    x64.leaRegRegImm32 rdx, rsi, 1
    x64.storeBaseDispReg.word64 [rax + 8], rdx
    x64.jmp __im_cont
  __im_stride#12:
    x64.cmpRegImm32 rdi, 2
    x64.jcc equal, __im_bytes2
  __im_stride#14:
    x64.cmpRegImm32 rdi, 4
    x64.jcc equal, __im_bytes4
  __im_stride#16:
    x64.cmpRegImm32 rdi, -2
    x64.jcc notEqual, __im_stride#18
  __im_crumb:
    x64.imulRegRegImm32 rdi, rsi, 2
    x64.movRegReg r8, rdi
    x64.shrRegImm8 r8, rdi, 3
    x64.andRegImm32 rdi, rdi, 7
    x64.movRegImm32 r9, 3
    x64.leaRegRegReg r8, rcx, r8
    x64.loadRegBaseDisp.byte r10, [r8 + 0]
    x64.movRegReg rcx, rdi
    x64.shlRegCl r9, r9
    x64.xorRegImm32 r9, r9, -1
    x64.andRegReg r10, r10, r9
    x64.andRegImm32 rdx, rdx, 3
    x64.movRegReg rcx, rdi
    x64.shlRegCl rdx, rdx
    x64.orRegReg r10, r10, rdx
    x64.storeBaseDispReg.byte [r8 + 0], r10
    x64.leaRegRegImm32 rdx, rsi, 1
    x64.storeBaseDispReg.word64 [rax + 8], rdx
    x64.jmp __im_cont
  __im_stride#18:
    x64.cmpRegImm32 rdi, -4
    x64.jcc notEqual, __im_stride#20
  __im_nibble:
    x64.imulRegRegImm32 rdi, rsi, 4
    x64.movRegReg r8, rdi
    x64.shrRegImm8 r8, rdi, 3
    x64.andRegImm32 rdi, rdi, 7
    x64.movRegImm32 r9, 15
    x64.leaRegRegReg r8, rcx, r8
    x64.loadRegBaseDisp.byte r10, [r8 + 0]
    x64.movRegReg rcx, rdi
    x64.shlRegCl r9, r9
    x64.xorRegImm32 r9, r9, -1
    x64.andRegReg r10, r10, r9
    x64.andRegImm32 rdx, rdx, 15
    x64.movRegReg rcx, rdi
    x64.shlRegCl rdx, rdx
    x64.orRegReg r10, r10, rdx
    x64.storeBaseDispReg.byte [r8 + 0], r10
    x64.leaRegRegImm32 rdx, rsi, 1
    x64.storeBaseDispReg.word64 [rax + 8], rdx
    x64.jmp __im_cont
  __im_stride#20:
    x64.movRegImm r8, 4294967297
    x64.cmpRegReg rdi, r8
    x64.jcc notEqual, __im_stride#21
  __im_byte:
    x64.leaRegRegReg rcx, rcx, rsi
    x64.storeBaseDispReg.byte [rcx + 0], rdx
    x64.leaRegRegImm32 rcx, rsi, 1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.jmp __im_cont
  __im_stride#21:
    x64.movRegImm r8, 4294967298
    x64.cmpRegReg rdi, r8
    x64.jcc notEqual, __im_stride#22
  __im_bytes2:
    x64.storeBaseIndexScaleReg.word16 [rcx + rsi*2 + 0], rdx
    x64.leaRegRegImm32 rcx, rsi, 1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.jmp __im_cont
  __im_stride#22:
    x64.movRegImm r8, 4294967300
    x64.cmpRegReg rdi, r8
    x64.jcc notEqual, __im_slow
  __im_bytes4:
    x64.storeBaseIndexScaleReg.word32 [rcx + rsi*4 + 0], rdx
    x64.leaRegRegImm32 rcx, rsi, 1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.jmp __im_cont
  __im_slow:
    x64.movRegReg rcx, rax
    x64.callDirect __managed_push
  __im_cont:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @Bag.at {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rax
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.cmpRegReg rdx, rcx
    x64.jcc aboveEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rcx, [rax + 24]
    x64.cmpRegImm32 rcx, 8
    x64.jcc notEqual, __im_stride#7
  __im_word:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseIndexScale.word64 rcx, [rcx + rdx*8 + 0]
    x64.jmp __im_loaded
  __im_stride#7:
    x64.cmpRegImm32 rcx, 1
    x64.jcc notEqual, __im_stride#9
  __im_byte:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegBaseDisp.byte rcx, [rcx + 0]
    x64.jmp __im_loaded
  __im_stride#9:
    x64.cmpRegImm32 rcx, -1
    x64.jcc notEqual, __im_stride#11
  __im_bit:
    x64.loadRegBaseDisp.word64 rsi, [rax + 0]
    x64.movRegReg rdi, rdx
    x64.shrRegImm8 rdi, rdx, 3
    x64.movRegReg rcx, rdx
    x64.andRegImm32 rcx, rdx, 7
    x64.leaRegRegReg rsi, rsi, rdi
    x64.loadRegBaseDisp.byte rsi, [rsi + 0]
    x64.shrRegCl rsi, rsi
    x64.andRegImm32 rsi, rsi, 1
    x64.movRegReg rcx, rsi
    x64.jmp __im_loaded
  __im_stride#11:
    x64.cmpRegImm32 rcx, 2
    x64.jcc notEqual, __im_stride#13
  __im_bytes2:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseIndexScale.word16 rcx, [rcx + rdx*2 + 0]
    x64.jmp __im_loaded
  __im_stride#13:
    x64.cmpRegImm32 rcx, 4
    x64.jcc notEqual, __im_stride#15
  __im_bytes4:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseIndexScale.word32 rcx, [rcx + rdx*4 + 0]
    x64.jmp __im_loaded
  __im_stride#15:
    x64.cmpRegImm32 rcx, -2
    x64.jcc notEqual, __im_stride#17
  __im_crumb:
    x64.loadRegBaseDisp.word64 rsi, [rax + 0]
    x64.imulRegRegImm32 rdi, rdx, 2
    x64.movRegReg r8, rdi
    x64.shrRegImm8 r8, rdi, 3
    x64.movRegReg rcx, rdi
    x64.andRegImm32 rcx, rdi, 7
    x64.leaRegRegReg rsi, rsi, r8
    x64.loadRegBaseDisp.byte rsi, [rsi + 0]
    x64.shrRegCl rsi, rsi
    x64.andRegImm32 rsi, rsi, 3
    x64.movRegReg rcx, rsi
    x64.jmp __im_loaded
  __im_stride#17:
    x64.cmpRegImm32 rcx, -4
    x64.jcc notEqual, __im_stride#19
  __im_nibble:
    x64.loadRegBaseDisp.word64 rsi, [rax + 0]
    x64.imulRegRegImm32 rdi, rdx, 4
    x64.movRegReg r8, rdi
    x64.shrRegImm8 r8, rdi, 3
    x64.movRegReg rcx, rdi
    x64.andRegImm32 rcx, rdi, 7
    x64.leaRegRegReg rsi, rsi, r8
    x64.loadRegBaseDisp.byte rsi, [rsi + 0]
    x64.shrRegCl rsi, rsi
    x64.andRegImm32 rsi, rsi, 15
    x64.movRegReg rcx, rsi
    x64.jmp __im_loaded
  __im_stride#19:
    x64.movRegImm rsi, 4294967297
    x64.cmpRegReg rcx, rsi
    x64.jcc notEqual, __im_stride#21
  __im_sbyte:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.leaRegRegReg rcx, rcx, rdx
    x64.loadRegBaseDisp.byte rcx, [rcx + 0]
    x64.shlRegImm8 rcx, rcx, 56
    x64.sarRegImm8 rcx, rcx, 56
    x64.jmp __im_loaded
  __im_stride#21:
    x64.movRegImm rsi, 4294967298
    x64.cmpRegReg rcx, rsi
    x64.jcc notEqual, __im_stride#23
  __im_sbytes2:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseIndexScale.word16 rcx, [rcx + rdx*2 + 0]
    x64.shlRegImm8 rcx, rcx, 48
    x64.sarRegImm8 rcx, rcx, 48
    x64.jmp __im_loaded
  __im_stride#23:
    x64.movRegImm rsi, 4294967300
    x64.cmpRegReg rcx, rsi
    x64.jcc notEqual, __im_slow
  __im_sbytes4:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseIndexScale.word32 rcx, [rcx + rdx*4 + 0]
    x64.shlRegImm8 rcx, rcx, 32
    x64.sarRegImm8 rcx, rcx, 32
  __im_loaded:
    x64.movRegImm32 rsi, 0
    x64.loadRegBaseDisp.word64 rdi, [rax + 40]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, critsplit#27
  __im_empty:
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, critsplit#28
  __im_slow:
    x64.movRegReg rcx, rax
    x64.callDirect __managed_get
    x64.movRegReg rdx, r8
    x64.jmp __im_cont
  critsplit#28:
    x64.movRegReg rdx, rcx
    x64.movRegReg r10, rsi
    x64.jmp __im_cont
  critsplit#27:
    x64.movRegReg rdx, rcx
    x64.movRegReg r10, rsi
  __im_cont:
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  tryok:
    x64.movRegReg rcx, rbx
    x64.callDirect __retain_type_param
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-shared-generic-body-forks-over-every-stride.test:17: Bag.at: index out of range\x0a"
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
    x64.prologue 48
    x64.leaRegRdata rax, [rip + __layout_Bag_Half]
  __il_body#8:
    x64.loadRegBaseDisp.word64 rcx, [rax + 72]
    x64.loadRegBaseDisp.word64 rdx, [rax + 40]
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
  __il_cont#7:
    x64.movRegImm32 rdx, 40000
    x64.movRegReg rcx, r12
    x64.callDirect Bag.add
    x64.movRegImm32 rdx, 7
    x64.movRegReg rcx, r12
    x64.callDirect Bag.add
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 9
  __il_body#9:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 40]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegImm32 rsi, 1
    x64.jcc belowEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_detach
  __im_detached:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 24]
    x64.cmpRegImm32 rdi, 8
    x64.jcc notEqual, __im_stride#22
  __im_word:
    x64.storeBaseDispReg.word64 [rsi + 8], rax
    x64.jmp __il_cont#6
  __im_stride#22:
    x64.cmpRegImm32 rdi, 1
    x64.jcc equal, __im_byte
  __im_stride#24:
    x64.cmpRegImm32 rdi, -1
    x64.jcc notEqual, __im_stride#26
  __im_bit:
    x64.loadRegBaseDisp.byte rax, [rsi + 0]
    x64.andRegImm32 rax, rax, -3
    x64.orRegImm32 rax, rax, 2
    x64.storeBaseDispReg.byte [rsi + 0], rax
    x64.jmp __il_cont#6
  __im_stride#26:
    x64.cmpRegImm32 rdi, 2
    x64.jcc equal, __im_bytes2
  __im_stride#28:
    x64.cmpRegImm32 rdi, 4
    x64.jcc equal, __im_bytes4
  __im_stride#30:
    x64.cmpRegImm32 rdi, -2
    x64.jcc notEqual, __im_stride#32
  __im_crumb:
    x64.loadRegBaseDisp.byte rax, [rsi + 0]
    x64.andRegImm32 rax, rax, -13
    x64.orRegImm32 rax, rax, 4
    x64.storeBaseDispReg.byte [rsi + 0], rax
    x64.jmp __il_cont#6
  __im_stride#32:
    x64.cmpRegImm32 rdi, -4
    x64.jcc notEqual, __im_stride#34
  __im_nibble:
    x64.loadRegBaseDisp.byte rax, [rsi + 0]
    x64.andRegImm32 rax, rax, -241
    x64.orRegImm32 rax, rax, 144
    x64.storeBaseDispReg.byte [rsi + 0], rax
    x64.jmp __il_cont#6
  __im_stride#34:
    x64.movRegImm r8, 4294967297
    x64.cmpRegReg rdi, r8
    x64.jcc notEqual, __im_stride#35
  __im_byte:
    x64.storeBaseDispReg.byte [rsi + 1], rax
    x64.jmp __il_cont#6
  __im_stride#35:
    x64.movRegImm r8, 4294967298
    x64.cmpRegReg rdi, r8
    x64.jcc notEqual, __im_stride#36
  __im_bytes2:
    x64.storeBaseDispReg.word16 [rsi + 2], rax
    x64.jmp __il_cont#6
  __im_stride#36:
    x64.movRegImm r8, 4294967300
    x64.cmpRegReg rdi, r8
    x64.jcc notEqual, __im_slow
  __im_bytes4:
    x64.storeBaseDispReg.word32 [rsi + 4], rax
    x64.jmp __il_cont#6
  __im_slow:
    x64.callDirect __managed_set
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  __il_cont#6:
    x64.movRegImm32 rdx, 0
    x64.leaRegRdata rax, [rip + __layout_Bag_Half]
    x64.movRegReg rcx, r12
    x64.callDirect Bag.at
    x64.cmpRegImm32 r8, 40000
    x64.movRegImm32 rax, 1
    x64.jcc notEqual, scmerge
  orrhs:
    x64.movRegImm32 rdx, 1
    x64.leaRegRdata rax, [rip + __layout_Bag_Half]
    x64.movRegReg rcx, r12
    x64.callDirect Bag.at
    x64.cmpRegImm32 r8, 9
    x64.setccReg notEqual, rax
  scmerge:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  differs:
    x64.movRegImm32 rbx, 1
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_Bag_Half
    x64.movRegReg r8, rbx
    x64.epilogue 48
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_Bag_Half
    x64.movRegReg r8, rbx
    x64.epilogue 48
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_detach:
    x64.storeSlotReg slot0, rcx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 9
    x64.loadRegSlot rcx, slot0
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.jmp __im_detached
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 48
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-shared-generic-body-forks-over-every-stride.test:21: Bag.put: index out of range\x0a"
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

func @Bag.add {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x2, x3
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 24]
    arm64.cmp x4, 8
    arm64.b.ne __im_stride#8
  __im_word:
    arm64.storeBaseIndexScaleReg.word64 [x3 + x2*8 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.b __im_cont
  __im_stride#8:
    arm64.cmp x4, 1
    arm64.b.eq __im_byte
  __im_stride#10:
    arm64.cmp x4, -1
    arm64.b.ne __im_stride#12
  __im_bit:
    arm64.lsr x4, x2, 3
    arm64.and x5, x2, 7
    arm64.movImm x6, 1
    arm64.add x3, x3, x4
    arm64.loadRegBaseDisp.byte x4, [x3 + 0]
    arm64.lslv x6, x6, x5
    arm64.movImm x16, 18446744073709551615
    arm64.eor x6, x6, x16
    arm64.and x4, x4, x6
    arm64.and x1, x1, 1
    arm64.lslv x1, x1, x5
    arm64.orr x1, x4, x1
    arm64.storeBaseDispReg.byte [x3 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.b __im_cont
  __im_stride#12:
    arm64.cmp x4, 2
    arm64.b.eq __im_bytes2
  __im_stride#14:
    arm64.cmp x4, 4
    arm64.b.eq __im_bytes4
  __im_stride#16:
    arm64.cmp x4, -2
    arm64.b.ne __im_stride#18
  __im_crumb:
    arm64.lsl x4, x2, 1
    arm64.lsr x5, x4, 3
    arm64.and x4, x4, 7
    arm64.movImm x6, 3
    arm64.add x3, x3, x5
    arm64.loadRegBaseDisp.byte x5, [x3 + 0]
    arm64.lslv x6, x6, x4
    arm64.movImm x16, 18446744073709551615
    arm64.eor x6, x6, x16
    arm64.and x5, x5, x6
    arm64.and x1, x1, 3
    arm64.lslv x1, x1, x4
    arm64.orr x1, x5, x1
    arm64.storeBaseDispReg.byte [x3 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.b __im_cont
  __im_stride#18:
    arm64.cmp x4, -4
    arm64.b.ne __im_stride#20
  __im_nibble:
    arm64.lsl x4, x2, 2
    arm64.lsr x5, x4, 3
    arm64.and x4, x4, 7
    arm64.movImm x6, 15
    arm64.add x3, x3, x5
    arm64.loadRegBaseDisp.byte x5, [x3 + 0]
    arm64.lslv x6, x6, x4
    arm64.movImm x16, 18446744073709551615
    arm64.eor x6, x6, x16
    arm64.and x5, x5, x6
    arm64.and x1, x1, 15
    arm64.lslv x1, x1, x4
    arm64.orr x1, x5, x1
    arm64.storeBaseDispReg.byte [x3 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.b __im_cont
  __im_stride#20:
    arm64.movImm x5, 4294967297
    arm64.cmp x4, x5
    arm64.b.ne __im_stride#21
  __im_byte:
    arm64.add x3, x3, x2
    arm64.storeBaseDispReg.byte [x3 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.b __im_cont
  __im_stride#21:
    arm64.movImm x5, 4294967298
    arm64.cmp x4, x5
    arm64.b.ne __im_stride#22
  __im_bytes2:
    arm64.storeBaseIndexScaleReg.word16 [x3 + x2*2 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.b __im_cont
  __im_stride#22:
    arm64.movImm x5, 4294967300
    arm64.cmp x4, x5
    arm64.b.ne __im_slow
  __im_bytes4:
    arm64.storeBaseIndexScaleReg.word32 [x3 + x2*4 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.b __im_cont
  __im_slow:
    arm64.bl __managed_push
  __im_cont:
    arm64.epilogue 16
    arm64.ret
}

func @Bag.at {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x2
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 24]
    arm64.cmp x2, 8
    arm64.b.ne __im_stride#7
  __im_word:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseIndexScale.word64 x2, [x2 + x1*8 + 0]
    arm64.b __im_loaded
  __im_stride#7:
    arm64.cmp x2, 1
    arm64.b.ne __im_stride#9
  __im_byte:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.add x2, x2, x1
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
    arm64.b __im_loaded
  __im_stride#9:
    arm64.cmp x2, -1
    arm64.b.ne __im_stride#11
  __im_bit:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.lsr x3, x1, 3
    arm64.and x4, x1, 7
    arm64.add x2, x2, x3
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
    arm64.lsrv x2, x2, x4
    arm64.and x2, x2, 1
    arm64.b __im_loaded
  __im_stride#11:
    arm64.cmp x2, 2
    arm64.b.ne __im_stride#13
  __im_bytes2:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseIndexScale.word16 x2, [x2 + x1*2 + 0]
    arm64.b __im_loaded
  __im_stride#13:
    arm64.cmp x2, 4
    arm64.b.ne __im_stride#15
  __im_bytes4:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseIndexScale.word32 x2, [x2 + x1*4 + 0]
    arm64.b __im_loaded
  __im_stride#15:
    arm64.cmp x2, -2
    arm64.b.ne __im_stride#17
  __im_crumb:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.lsl x3, x1, 1
    arm64.lsr x4, x3, 3
    arm64.and x3, x3, 7
    arm64.add x2, x2, x4
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
    arm64.lsrv x2, x2, x3
    arm64.and x2, x2, 3
    arm64.b __im_loaded
  __im_stride#17:
    arm64.cmp x2, -4
    arm64.b.ne __im_stride#19
  __im_nibble:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.lsl x3, x1, 2
    arm64.lsr x4, x3, 3
    arm64.and x3, x3, 7
    arm64.add x2, x2, x4
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
    arm64.lsrv x2, x2, x3
    arm64.and x2, x2, 15
    arm64.b __im_loaded
  __im_stride#19:
    arm64.movImm x3, 4294967297
    arm64.cmp x2, x3
    arm64.b.ne __im_stride#21
  __im_sbyte:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.add x2, x2, x1
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
    arm64.lsl x2, x2, 56
    arm64.asr x2, x2, 56
    arm64.b __im_loaded
  __im_stride#21:
    arm64.movImm x3, 4294967298
    arm64.cmp x2, x3
    arm64.b.ne __im_stride#23
  __im_sbytes2:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseIndexScale.word16 x2, [x2 + x1*2 + 0]
    arm64.lsl x2, x2, 48
    arm64.asr x2, x2, 48
    arm64.b __im_loaded
  __im_stride#23:
    arm64.movImm x3, 4294967300
    arm64.cmp x2, x3
    arm64.b.ne __im_slow
  __im_sbytes4:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseIndexScale.word32 x2, [x2 + x1*4 + 0]
    arm64.lsl x2, x2, 32
    arm64.asr x2, x2, 32
  __im_loaded:
    arm64.movImm x3, 0
    arm64.loadRegBaseDisp.word64 x4, [x0 + 40]
    arm64.cmp x4, 0
    arm64.b.eq critsplit#27
  __im_empty:
    arm64.cmp x2, 0
    arm64.b.ne critsplit#28
  __im_slow:
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.b __im_cont
  critsplit#28:
    arm64.movRegReg x1, x2
    arm64.movRegReg x9, x3
    arm64.b __im_cont
  critsplit#27:
    arm64.movRegReg x1, x2
    arm64.movRegReg x9, x3
  __im_cont:
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  tryok:
    arm64.movRegReg x0, x19
    arm64.bl __retain_type_param
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-shared-generic-body-forks-over-every-stride.test:17: Bag.at: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x20
    arm64.storeSlotReg slot1, x19
    arm64.leaRdata x1, __layout_Bag_Half
  __il_body#8:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 72]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 40]
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
  __il_cont#7:
    arm64.movImm x1, 40000
    arm64.movRegReg x0, x20
    arm64.bl Bag.add
    arm64.movImm x1, 7
    arm64.movRegReg x0, x20
    arm64.bl Bag.add
    arm64.movImm x1, 1
    arm64.movImm x2, 9
  __il_body#9:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 40]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 1
    arm64.b.ls __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_detach
  __im_detached:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 24]
    arm64.cmp x4, 8
    arm64.b.ne __im_stride#22
  __im_word:
    arm64.storeBaseDispReg.word64 [x3 + 8], x2
    arm64.b __il_cont#6
  __im_stride#22:
    arm64.cmp x4, 1
    arm64.b.eq __im_byte
  __im_stride#24:
    arm64.cmp x4, -1
    arm64.b.ne __im_stride#26
  __im_bit:
    arm64.loadRegBaseDisp.byte x0, [x3 + 0]
    arm64.and x0, x0, -3
    arm64.orr x0, x0, 2
    arm64.storeBaseDispReg.byte [x3 + 0], x0
    arm64.b __il_cont#6
  __im_stride#26:
    arm64.cmp x4, 2
    arm64.b.eq __im_bytes2
  __im_stride#28:
    arm64.cmp x4, 4
    arm64.b.eq __im_bytes4
  __im_stride#30:
    arm64.cmp x4, -2
    arm64.b.ne __im_stride#32
  __im_crumb:
    arm64.loadRegBaseDisp.byte x0, [x3 + 0]
    arm64.and x0, x0, -13
    arm64.orr x0, x0, 4
    arm64.storeBaseDispReg.byte [x3 + 0], x0
    arm64.b __il_cont#6
  __im_stride#32:
    arm64.cmp x4, -4
    arm64.b.ne __im_stride#34
  __im_nibble:
    arm64.loadRegBaseDisp.byte x0, [x3 + 0]
    arm64.and x0, x0, -241
    arm64.movImm x16, 144
    arm64.orr x0, x0, x16
    arm64.storeBaseDispReg.byte [x3 + 0], x0
    arm64.b __il_cont#6
  __im_stride#34:
    arm64.movImm x5, 4294967297
    arm64.cmp x4, x5
    arm64.b.ne __im_stride#35
  __im_byte:
    arm64.storeBaseDispReg.byte [x3 + 1], x2
    arm64.b __il_cont#6
  __im_stride#35:
    arm64.movImm x5, 4294967298
    arm64.cmp x4, x5
    arm64.b.ne __im_stride#36
  __im_bytes2:
    arm64.storeBaseDispReg.word16 [x3 + 2], x2
    arm64.b __il_cont#6
  __im_stride#36:
    arm64.movImm x5, 4294967300
    arm64.cmp x4, x5
    arm64.b.ne __im_slow
  __im_bytes4:
    arm64.storeBaseDispReg.word32 [x3 + 4], x2
    arm64.b __il_cont#6
  __im_slow:
    arm64.bl __managed_set
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  __il_cont#6:
    arm64.movImm x1, 0
    arm64.leaRdata x2, __layout_Bag_Half
    arm64.movRegReg x0, x20
    arm64.bl Bag.at
    arm64.movImm x16, 40000
    arm64.cmp x0, x16
    arm64.movImm x0, 1
    arm64.b.ne scmerge
  orrhs:
    arm64.movImm x1, 1
    arm64.leaRdata x2, __layout_Bag_Half
    arm64.movRegReg x0, x20
    arm64.bl Bag.at
    arm64.cmp x0, 9
    arm64.cset x0, ne
  scmerge:
    arm64.cbz x0, ifcont
  differs:
    arm64.movImm x19, 1
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Bag_Half
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Bag_Half
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.epilogue 48
    arm64.ret
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_cow_detach
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.movImm x1, 1
    arm64.movImm x2, 9
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.b __im_detached
  __im_missing:
    arm64.leaRdata x0, __str_blob_5  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.epilogue 48
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-shared-generic-body-forks-over-every-stride.test:21: Bag.put: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
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

func @Bag.add {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x2, x3
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 24]
    arm64.cmp x4, 8
    arm64.b.ne __im_stride#8
  __im_word:
    arm64.storeBaseIndexScaleReg.word64 [x3 + x2*8 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.b __im_cont
  __im_stride#8:
    arm64.cmp x4, 1
    arm64.b.eq __im_byte
  __im_stride#10:
    arm64.cmp x4, -1
    arm64.b.ne __im_stride#12
  __im_bit:
    arm64.lsr x4, x2, 3
    arm64.and x5, x2, 7
    arm64.movImm x6, 1
    arm64.add x3, x3, x4
    arm64.loadRegBaseDisp.byte x4, [x3 + 0]
    arm64.lslv x6, x6, x5
    arm64.movImm x16, 18446744073709551615
    arm64.eor x6, x6, x16
    arm64.and x4, x4, x6
    arm64.and x1, x1, 1
    arm64.lslv x1, x1, x5
    arm64.orr x1, x4, x1
    arm64.storeBaseDispReg.byte [x3 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.b __im_cont
  __im_stride#12:
    arm64.cmp x4, 2
    arm64.b.eq __im_bytes2
  __im_stride#14:
    arm64.cmp x4, 4
    arm64.b.eq __im_bytes4
  __im_stride#16:
    arm64.cmp x4, -2
    arm64.b.ne __im_stride#18
  __im_crumb:
    arm64.lsl x4, x2, 1
    arm64.lsr x5, x4, 3
    arm64.and x4, x4, 7
    arm64.movImm x6, 3
    arm64.add x3, x3, x5
    arm64.loadRegBaseDisp.byte x5, [x3 + 0]
    arm64.lslv x6, x6, x4
    arm64.movImm x16, 18446744073709551615
    arm64.eor x6, x6, x16
    arm64.and x5, x5, x6
    arm64.and x1, x1, 3
    arm64.lslv x1, x1, x4
    arm64.orr x1, x5, x1
    arm64.storeBaseDispReg.byte [x3 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.b __im_cont
  __im_stride#18:
    arm64.cmp x4, -4
    arm64.b.ne __im_stride#20
  __im_nibble:
    arm64.lsl x4, x2, 2
    arm64.lsr x5, x4, 3
    arm64.and x4, x4, 7
    arm64.movImm x6, 15
    arm64.add x3, x3, x5
    arm64.loadRegBaseDisp.byte x5, [x3 + 0]
    arm64.lslv x6, x6, x4
    arm64.movImm x16, 18446744073709551615
    arm64.eor x6, x6, x16
    arm64.and x5, x5, x6
    arm64.and x1, x1, 15
    arm64.lslv x1, x1, x4
    arm64.orr x1, x5, x1
    arm64.storeBaseDispReg.byte [x3 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.b __im_cont
  __im_stride#20:
    arm64.movImm x5, 4294967297
    arm64.cmp x4, x5
    arm64.b.ne __im_stride#21
  __im_byte:
    arm64.add x3, x3, x2
    arm64.storeBaseDispReg.byte [x3 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.b __im_cont
  __im_stride#21:
    arm64.movImm x5, 4294967298
    arm64.cmp x4, x5
    arm64.b.ne __im_stride#22
  __im_bytes2:
    arm64.storeBaseIndexScaleReg.word16 [x3 + x2*2 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.b __im_cont
  __im_stride#22:
    arm64.movImm x5, 4294967300
    arm64.cmp x4, x5
    arm64.b.ne __im_slow
  __im_bytes4:
    arm64.storeBaseIndexScaleReg.word32 [x3 + x2*4 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.b __im_cont
  __im_slow:
    arm64.bl __managed_push
  __im_cont:
    arm64.epilogue 16
    arm64.ret
}

func @Bag.at {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x2
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 24]
    arm64.cmp x2, 8
    arm64.b.ne __im_stride#7
  __im_word:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseIndexScale.word64 x2, [x2 + x1*8 + 0]
    arm64.b __im_loaded
  __im_stride#7:
    arm64.cmp x2, 1
    arm64.b.ne __im_stride#9
  __im_byte:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.add x2, x2, x1
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
    arm64.b __im_loaded
  __im_stride#9:
    arm64.cmp x2, -1
    arm64.b.ne __im_stride#11
  __im_bit:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.lsr x3, x1, 3
    arm64.and x4, x1, 7
    arm64.add x2, x2, x3
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
    arm64.lsrv x2, x2, x4
    arm64.and x2, x2, 1
    arm64.b __im_loaded
  __im_stride#11:
    arm64.cmp x2, 2
    arm64.b.ne __im_stride#13
  __im_bytes2:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseIndexScale.word16 x2, [x2 + x1*2 + 0]
    arm64.b __im_loaded
  __im_stride#13:
    arm64.cmp x2, 4
    arm64.b.ne __im_stride#15
  __im_bytes4:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseIndexScale.word32 x2, [x2 + x1*4 + 0]
    arm64.b __im_loaded
  __im_stride#15:
    arm64.cmp x2, -2
    arm64.b.ne __im_stride#17
  __im_crumb:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.lsl x3, x1, 1
    arm64.lsr x4, x3, 3
    arm64.and x3, x3, 7
    arm64.add x2, x2, x4
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
    arm64.lsrv x2, x2, x3
    arm64.and x2, x2, 3
    arm64.b __im_loaded
  __im_stride#17:
    arm64.cmp x2, -4
    arm64.b.ne __im_stride#19
  __im_nibble:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.lsl x3, x1, 2
    arm64.lsr x4, x3, 3
    arm64.and x3, x3, 7
    arm64.add x2, x2, x4
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
    arm64.lsrv x2, x2, x3
    arm64.and x2, x2, 15
    arm64.b __im_loaded
  __im_stride#19:
    arm64.movImm x3, 4294967297
    arm64.cmp x2, x3
    arm64.b.ne __im_stride#21
  __im_sbyte:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.add x2, x2, x1
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
    arm64.lsl x2, x2, 56
    arm64.asr x2, x2, 56
    arm64.b __im_loaded
  __im_stride#21:
    arm64.movImm x3, 4294967298
    arm64.cmp x2, x3
    arm64.b.ne __im_stride#23
  __im_sbytes2:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseIndexScale.word16 x2, [x2 + x1*2 + 0]
    arm64.lsl x2, x2, 48
    arm64.asr x2, x2, 48
    arm64.b __im_loaded
  __im_stride#23:
    arm64.movImm x3, 4294967300
    arm64.cmp x2, x3
    arm64.b.ne __im_slow
  __im_sbytes4:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseIndexScale.word32 x2, [x2 + x1*4 + 0]
    arm64.lsl x2, x2, 32
    arm64.asr x2, x2, 32
  __im_loaded:
    arm64.movImm x3, 0
    arm64.loadRegBaseDisp.word64 x4, [x0 + 40]
    arm64.cmp x4, 0
    arm64.b.eq critsplit#27
  __im_empty:
    arm64.cmp x2, 0
    arm64.b.ne critsplit#28
  __im_slow:
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.b __im_cont
  critsplit#28:
    arm64.movRegReg x1, x2
    arm64.movRegReg x9, x3
    arm64.b __im_cont
  critsplit#27:
    arm64.movRegReg x1, x2
    arm64.movRegReg x9, x3
  __im_cont:
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  tryok:
    arm64.movRegReg x0, x19
    arm64.bl __retain_type_param
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-shared-generic-body-forks-over-every-stride.test:17: Bag.at: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x20
    arm64.storeSlotReg slot1, x19
    arm64.leaRdata x1, __layout_Bag_Half
  __il_body#8:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 72]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 40]
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
  __il_cont#7:
    arm64.movImm x1, 40000
    arm64.movRegReg x0, x20
    arm64.bl Bag.add
    arm64.movImm x1, 7
    arm64.movRegReg x0, x20
    arm64.bl Bag.add
    arm64.movImm x1, 1
    arm64.movImm x2, 9
  __il_body#9:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 40]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.cmp x3, 1
    arm64.b.ls __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_detach
  __im_detached:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 24]
    arm64.cmp x4, 8
    arm64.b.ne __im_stride#22
  __im_word:
    arm64.storeBaseDispReg.word64 [x3 + 8], x2
    arm64.b __il_cont#6
  __im_stride#22:
    arm64.cmp x4, 1
    arm64.b.eq __im_byte
  __im_stride#24:
    arm64.cmp x4, -1
    arm64.b.ne __im_stride#26
  __im_bit:
    arm64.loadRegBaseDisp.byte x0, [x3 + 0]
    arm64.and x0, x0, -3
    arm64.orr x0, x0, 2
    arm64.storeBaseDispReg.byte [x3 + 0], x0
    arm64.b __il_cont#6
  __im_stride#26:
    arm64.cmp x4, 2
    arm64.b.eq __im_bytes2
  __im_stride#28:
    arm64.cmp x4, 4
    arm64.b.eq __im_bytes4
  __im_stride#30:
    arm64.cmp x4, -2
    arm64.b.ne __im_stride#32
  __im_crumb:
    arm64.loadRegBaseDisp.byte x0, [x3 + 0]
    arm64.and x0, x0, -13
    arm64.orr x0, x0, 4
    arm64.storeBaseDispReg.byte [x3 + 0], x0
    arm64.b __il_cont#6
  __im_stride#32:
    arm64.cmp x4, -4
    arm64.b.ne __im_stride#34
  __im_nibble:
    arm64.loadRegBaseDisp.byte x0, [x3 + 0]
    arm64.and x0, x0, -241
    arm64.movImm x16, 144
    arm64.orr x0, x0, x16
    arm64.storeBaseDispReg.byte [x3 + 0], x0
    arm64.b __il_cont#6
  __im_stride#34:
    arm64.movImm x5, 4294967297
    arm64.cmp x4, x5
    arm64.b.ne __im_stride#35
  __im_byte:
    arm64.storeBaseDispReg.byte [x3 + 1], x2
    arm64.b __il_cont#6
  __im_stride#35:
    arm64.movImm x5, 4294967298
    arm64.cmp x4, x5
    arm64.b.ne __im_stride#36
  __im_bytes2:
    arm64.storeBaseDispReg.word16 [x3 + 2], x2
    arm64.b __il_cont#6
  __im_stride#36:
    arm64.movImm x5, 4294967300
    arm64.cmp x4, x5
    arm64.b.ne __im_slow
  __im_bytes4:
    arm64.storeBaseDispReg.word32 [x3 + 4], x2
    arm64.b __il_cont#6
  __im_slow:
    arm64.bl __managed_set
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  __il_cont#6:
    arm64.movImm x1, 0
    arm64.leaRdata x2, __layout_Bag_Half
    arm64.movRegReg x0, x20
    arm64.bl Bag.at
    arm64.movImm x16, 40000
    arm64.cmp x0, x16
    arm64.movImm x0, 1
    arm64.b.ne scmerge
  orrhs:
    arm64.movImm x1, 1
    arm64.leaRdata x2, __layout_Bag_Half
    arm64.movRegReg x0, x20
    arm64.bl Bag.at
    arm64.cmp x0, 9
    arm64.cset x0, ne
  scmerge:
    arm64.cbz x0, ifcont
  differs:
    arm64.movImm x19, 1
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Bag_Half
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Bag_Half
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.epilogue 48
    arm64.ret
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_cow_detach
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.movImm x1, 1
    arm64.movImm x2, 9
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.b __im_detached
  __im_missing:
    arm64.leaRdata x0, __str_blob_5  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.epilogue 48
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-shared-generic-body-forks-over-every-stride.test:21: Bag.put: index out of range\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.epilogue 48
    arm64.ret
}
```
