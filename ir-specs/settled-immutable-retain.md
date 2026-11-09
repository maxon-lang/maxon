---
feature: settled-immutable-retain
status: experimental
keywords: [retain, parameter, immutable, inference, ownership, codegen]
category: memory
---

# A parameter inference settles immutable-only takes the same reference as one declared `immutable`

## Documentation

An unmarked parameter whose callees keep it in `immutable` slots is `immutable`-only once the whole program has been
read. The reference its function takes on the way to such a callee is the one a parameter declared `immutable` takes:
the plain retain, which leaves an image record alone, and not the copying one.

## Tests

<!-- test: an-unmarked-parameter-forwarded-into-an-immutable-field-takes-the-plain-retain -->
`owned` writes no capability word, and `Layout.create` keeps `name` in an `immutable` field, so `name` settles
`immutable`-only and `owned` retains it with `__mm_retain`. Prints `3`.

```maxon
typealias Tally = int(0 to 1000000)

type Layout
	export let name as immutable ByteArray
	export var fields as Tally

	static function create(name ByteArray) returns Layout
		return Self{name: name, fields: 0}
	end 'create'
end 'Layout'

function owned(name ByteArray) returns Layout
	return Layout.create(name)
end 'owned'

let Shared = b"abc"

function main() returns ExitCode
	let layout = owned(Shared)
	print("{layout.name.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
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
    x64.prologue 72
    x64.leaRegRdata rcx, [rip + __barr_rec_2]
  __il_body#5:
    x64.callDirect __mm_retain
    x64.movRegReg rbx, r8
  __il_body#7:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_retain
    x64.movRegReg r12, r8
    x64.storeSlotReg slot0, r12
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  __il_cont#4:
    x64.loadRegBaseDisp.word64 rbx, [r12 + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot3, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, rbx
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, rbx, r14
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot2
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot1
    x64.callDirect __mm_decref
  __il_body#3:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __write_stdout
  __il_cont#2:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 72
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
    x64.prologue 72
    x64.leaRegRdata rcx, [rip + __barr_rec_2]
  __il_body#5:
    x64.callDirect __mm_retain
    x64.movRegReg rbx, r8
  __il_body#7:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_retain
    x64.movRegReg r12, r8
    x64.storeSlotReg slot0, r12
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  __il_cont#4:
    x64.loadRegBaseDisp.word64 rbx, [r12 + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot3, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, rbx
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, rbx, r14
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot2
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot1
    x64.callDirect __mm_decref
  __il_body#3:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __write_stdout
  __il_cont#2:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 72
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
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x0, __barr_rec_2
  __il_body#5:
    arm64.bl __mm_retain
    arm64.movRegReg x19, x0
  __il_body#7:
    arm64.movRegReg x0, x19
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  __il_cont#4:
    arm64.loadRegBaseDisp.word64 x19, [x20 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_3  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.add x24, x19, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x26, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x0, x19, x23
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
  __il_body#3:
    arm64.movRegReg x0, x25
    arm64.bl __write_stdout
  __il_cont#2:
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
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
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x0, __barr_rec_2
  __il_body#5:
    arm64.bl __mm_retain
    arm64.movRegReg x19, x0
  __il_body#7:
    arm64.movRegReg x0, x19
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  __il_cont#4:
    arm64.loadRegBaseDisp.word64 x19, [x20 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_3  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.add x24, x19, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x26, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x0, x19, x23
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
  __il_body#3:
    arm64.movRegReg x0, x25
    arm64.bl __write_stdout
  __il_cont#2:
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.epilogue 80
    arm64.ret
}
```

<!-- test: an-unmarked-parameter-forwarded-into-a-writable-field-keeps-the-copying-retain -->
`wrap` forwards `data` into a `var` field, so `data` does not settle `immutable`-only and `wrap` keeps
`__managed_retain`, which copies an image record. Prints `4`.

```maxon
typealias Tally = int(0 to 1000000)

type Slab
	export var data as ByteArray
	export var fields as Tally

	static function create(data ByteArray) returns Slab
		return Self{data: data, fields: 0}
	end 'create'
end 'Slab'

function wrap(data ByteArray) returns Slab
	return Slab.create(data)
end 'wrap'

function main() returns ExitCode
	var raw = "abc".toByteArray()
	var slab = wrap(raw)
	slab.data.push(100)
	print("{slab.data.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
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
    x64.prologue 72
    x64.leaRegRdata rcx, [rip + __str_rec_2]  ; "abc"
  __il_body#13:
    x64.movRegImm32 rdx, 0
  __il_body#18:
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
  __il_cont#17:
    x64.callDirect __managed_slice
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at String.maxon:149: String.toByteArray: slice 0..byteLength() is in bounds by construction\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.cmpRegImm32 rax, -4
    x64.jcc equal, adoptclone
  critsplit:
    x64.movRegReg rcx, r8
  adopted:
    x64.storeSlotReg slot0, rcx
  __il_body#19:
    x64.callDirect __managed_retain
    x64.movRegReg rbx, r8
  __il_body#21:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_retain
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  __il_cont#11:
    x64.movRegImm32 rdx, 100
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegReg rax, rcx
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.leaRegRegReg rcx, rcx, rax
    x64.storeBaseDispReg.byte [rcx + 0], rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [r12 + 8], rax
  __im_cont:
    x64.loadRegBaseDisp.word64 rbx, [r12 + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot2, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, rbx
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, rbx, r14
    x64.loadRegSlot rax, slot4
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot3
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
  __il_body#10:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __write_stdout
  __il_cont#9:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot1
    x64.callDirect __managed_decref
  exitdrop:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  adoptclone:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_clone
    x64.movRegReg rcx, r8
    x64.jmp adopted
  __im_slow:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_push
    x64.jmp __im_cont
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
    x64.prologue 72
    x64.leaRegRdata rcx, [rip + __str_rec_2]  ; "abc"
  __il_body#13:
    x64.movRegImm32 rdx, 0
  __il_body#18:
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
  __il_cont#17:
    x64.callDirect __managed_slice
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at String.maxon:149: String.toByteArray: slice 0..byteLength() is in bounds by construction\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.cmpRegImm32 rax, -4
    x64.jcc equal, adoptclone
  critsplit:
    x64.movRegReg rcx, r8
  adopted:
    x64.storeSlotReg slot0, rcx
  __il_body#19:
    x64.callDirect __managed_retain
    x64.movRegReg rbx, r8
  __il_body#21:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_retain
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  __il_cont#11:
    x64.movRegImm32 rdx, 100
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegReg rax, rcx
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.leaRegRegReg rcx, rcx, rax
    x64.storeBaseDispReg.byte [rcx + 0], rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [r12 + 8], rax
  __im_cont:
    x64.loadRegBaseDisp.word64 rbx, [r12 + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot2, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, rbx
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, rbx, r14
    x64.loadRegSlot rax, slot4
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot3
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
  __il_body#10:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __write_stdout
  __il_cont#9:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot1
    x64.callDirect __managed_decref
  exitdrop:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  adoptclone:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_clone
    x64.movRegReg rcx, r8
    x64.jmp adopted
  __im_slow:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_push
    x64.jmp __im_cont
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
    arm64.leaRdata x0, __str_rec_2  ; "abc"
  __il_body#13:
    arm64.movImm x1, 0
  __il_body#18:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
  __il_cont#17:
    arm64.bl __managed_slice
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.leaRdata x0, __str_blob_4  ; "panic at String.maxon:149: String.toByteArray: slice 0..byteLength() is in bounds by construction\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
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
  tryok:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.cmp x1, -4
    arm64.b.eq adoptclone
  critsplit:
    arm64.movRegReg x19, x0
  __il_body#19:
    arm64.movRegReg x0, x19
    arm64.bl __managed_retain
    arm64.movRegReg x20, x0
  __il_body#21:
    arm64.movRegReg x0, x20
    arm64.bl __managed_retain
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  __il_cont#11:
    arm64.movImm x1, 100
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x21 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x2, [x21 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.add x2, x2, x0
    arm64.storeBaseDispReg.byte [x2 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x21 + 8], x0
  __im_cont:
    arm64.loadRegBaseDisp.word64 x20, [x21 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x22
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_3  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.add x25, x20, x24
    arm64.add x0, x25, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x26, x0
    arm64.add x27, x26, 56
    arm64.movRegReg x0, x27
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x20, x27, x20
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.add x0, x20, x24
    arm64.storeBaseDispReg.word64 [x26 + 0], x27
    arm64.storeBaseDispReg.word64 [x26 + 8], x25
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x26 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x26 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
  __il_body#10:
    arm64.movRegReg x0, x26
    arm64.bl __write_stdout
  __il_cont#9:
    arm64.movRegReg x0, x26
    arm64.bl __str_decref
    arm64.movImm x20, 0
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
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
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.loadRegSlot x27, slot8
    arm64.epilogue 96
    arm64.ret
  adoptclone:
    arm64.bl __managed_clone
    arm64.movRegReg x19, x0
    arm64.b __il_body#19
  __im_slow:
    arm64.movRegReg x0, x21
    arm64.bl __managed_push
    arm64.b __im_cont
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
    arm64.leaRdata x0, __str_rec_2  ; "abc"
  __il_body#13:
    arm64.movImm x1, 0
  __il_body#18:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
  __il_cont#17:
    arm64.bl __managed_slice
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.leaRdata x0, __str_blob_4  ; "panic at String.maxon:149: String.toByteArray: slice 0..byteLength() is in bounds by construction\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
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
  tryok:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.cmp x1, -4
    arm64.b.eq adoptclone
  critsplit:
    arm64.movRegReg x19, x0
  __il_body#19:
    arm64.movRegReg x0, x19
    arm64.bl __managed_retain
    arm64.movRegReg x20, x0
  __il_body#21:
    arm64.movRegReg x0, x20
    arm64.bl __managed_retain
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  __il_cont#11:
    arm64.movImm x1, 100
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x21 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x2, [x21 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x2, [x21 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.add x2, x2, x0
    arm64.storeBaseDispReg.byte [x2 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x21 + 8], x0
  __im_cont:
    arm64.loadRegBaseDisp.word64 x20, [x21 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x22
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_3  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.add x25, x20, x24
    arm64.add x0, x25, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x26, x0
    arm64.add x27, x26, 56
    arm64.movRegReg x0, x27
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x20, x27, x20
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.add x0, x20, x24
    arm64.storeBaseDispReg.word64 [x26 + 0], x27
    arm64.storeBaseDispReg.word64 [x26 + 8], x25
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x26 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x26 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
  __il_body#10:
    arm64.movRegReg x0, x26
    arm64.bl __write_stdout
  __il_cont#9:
    arm64.movRegReg x0, x26
    arm64.bl __str_decref
    arm64.movImm x20, 0
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
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
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.loadRegSlot x27, slot8
    arm64.epilogue 96
    arm64.ret
  adoptclone:
    arm64.bl __managed_clone
    arm64.movRegReg x19, x0
    arm64.b __il_body#19
  __im_slow:
    arm64.movRegReg x0, x21
    arm64.bl __managed_push
    arm64.b __im_cont
}
```
