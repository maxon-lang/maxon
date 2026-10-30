---
feature: builtins-byte-order
status: stable
keywords: [builtins, __Builtins, byte order, endian, readBigEndian, byte swap, intrinsics]
category: system
---

# `__Builtins` byte-order intrinsics: the cases that pin emitted code

The cases of `specs/builtins-byte-order.md` whose subject is the emitted code. Each pins the Target IR its program compiles to.

## Tests

<!-- test: builtins-byte-order.a-big-endian-word-read-is-one-load-and-one-byte-swap -->
<!-- tier: stdlib -->
A four-byte big-endian read is a 32-bit load, a byte swap and no call: the memory access that moves the word and
the instruction that reverses its bytes are all the intrinsic costs once the bounds test has passed.
```maxon
function main() returns ExitCode
	var data = ByteArray.create()

	for byte in [1, 2, 3, 4, 5, 6, 7, 8] 'fill'
		data.push(byte)
	end 'fill'

	let word = try __Builtins.readBigEndian(data, 2, 4) otherwise panic("a read inside the array failed")
	print("{word as Count}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
50595078
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
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r12 + 0], rax
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.movRegImm32 r13, 0
    x64.jmp forhdr
  fill:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseIndexScale.word64 rdx, [rax + r13*8 + 0]
  __im_cont:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#10
  __im_bounds#11:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegReg rax, rcx
    x64.jcc aboveEqual, __im_slow#10
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#10
  __im_viewed:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#10
  __im_store:
    x64.loadRegBaseDisp.word64 rsi, [rbx + 24]
    x64.cmpRegImm32 rsi, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.storeBaseIndexScaleReg.word64 [rcx + rax*8 + 0], rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.jmp forstep
  __im_stride:
    x64.cmpRegImm32 rsi, 1
    x64.jcc notEqual, __im_slow#10
  __im_byte#16:
    x64.leaRegRegReg rcx, rcx, rax
    x64.storeBaseDispReg.byte [rcx + 0], rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.jmp forstep
  __im_slow#10:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
  forstep:
    x64.leaRegRegImm32 r13, r13, 1
  forhdr:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegReg r13, rax
    x64.jcc less, fill
  forexit:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegImm32 rdx, 2
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 2
    x64.jcc belowEqual, __im_slow#19
  __im_bounds#20:
    x64.leaRegRegImm32 rax, rax, -2
    x64.cmpRegImm32 rax, 4
    x64.jcc less, __im_slow#19
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#19
  __im_byte#22:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word32 rax, [rax + 2]
    x64.byteSwapReg.4 rax, rax
    x64.movRegReg rbx, rax
  tryok:
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
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
  __il_body:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __write_stdout
  __il_cont:
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
  __im_slow#19:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_word_get_be4
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  critsplit:
    x64.movRegReg rbx, r8
    x64.jmp tryok
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at builtins-byte-order.a-big-endian-word-read-is-one-load-and-one-byte-swap.test:9: a read inside the array failed\x0a"
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
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r12 + 0], rax
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.movRegImm32 r13, 0
    x64.jmp forhdr
  fill:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseIndexScale.word64 rdx, [rax + r13*8 + 0]
  __im_cont:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#10
  __im_bounds#11:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegReg rax, rcx
    x64.jcc aboveEqual, __im_slow#10
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#10
  __im_viewed:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#10
  __im_store:
    x64.loadRegBaseDisp.word64 rsi, [rbx + 24]
    x64.cmpRegImm32 rsi, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.storeBaseIndexScaleReg.word64 [rcx + rax*8 + 0], rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.jmp forstep
  __im_stride:
    x64.cmpRegImm32 rsi, 1
    x64.jcc notEqual, __im_slow#10
  __im_byte#16:
    x64.leaRegRegReg rcx, rcx, rax
    x64.storeBaseDispReg.byte [rcx + 0], rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.jmp forstep
  __im_slow#10:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
  forstep:
    x64.leaRegRegImm32 r13, r13, 1
  forhdr:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegReg r13, rax
    x64.jcc less, fill
  forexit:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegImm32 rdx, 2
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 2
    x64.jcc belowEqual, __im_slow#19
  __im_bounds#20:
    x64.leaRegRegImm32 rax, rax, -2
    x64.cmpRegImm32 rax, 4
    x64.jcc less, __im_slow#19
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#19
  __im_byte#22:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word32 rax, [rax + 2]
    x64.byteSwapReg.4 rax, rax
    x64.movRegReg rbx, rax
  tryok:
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
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
  __il_body:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __write_stdout
  __il_cont:
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
  __im_slow#19:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_word_get_be4
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  critsplit:
    x64.movRegReg rbx, r8
    x64.jmp tryok
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at builtins-byte-order.a-big-endian-word-read-is-one-load-and-one-byte-swap.test:9: a read inside the array failed\x0a"
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
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movImm x0, 8
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.movImm x0, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
    arm64.movImm x0, 8
    arm64.storeBaseDispReg.word64 [x20 + 24], x0
    arm64.movImm x21, 0
    arm64.b forhdr
  fill:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseIndexScale.word64 x1, [x0 + x21*8 + 0]
  __im_cont:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow#10
  __im_bounds#11:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow#10
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow#10
  __im_viewed:
    arm64.sub x3, x2, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#10
  __im_store:
    arm64.loadRegBaseDisp.word64 x3, [x19 + 24]
    arm64.cmp x3, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.b forstep
  __im_stride:
    arm64.cmp x3, 1
    arm64.b.ne __im_slow#10
  __im_byte#16:
    arm64.add x2, x2, x0
    arm64.storeBaseDispReg.byte [x2 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.b forstep
  __im_slow#10:
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x21, x0
    arm64.b.lt fill
  forexit:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movImm x1, 2
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 2
    arm64.b.ls __im_slow#19
  __im_bounds#20:
    arm64.sub x0, x0, 2
    arm64.cmp x0, 4
    arm64.b.lt __im_slow#19
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#19
  __im_byte#22:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x0, x0, 2
    arm64.loadRegBaseDisp.word32 x0, [x0 + 0]
    arm64.rev.4 x20, x0
  tryok:
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.add x24, x20, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x20, x26, x20
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x0, x20, x23
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
  __il_body:
    arm64.movRegReg x0, x25
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
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
  __im_slow#19:
    arm64.movRegReg x0, x19
    arm64.bl __managed_word_get_be4
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  critsplit:
    arm64.movRegReg x20, x0
    arm64.b tryok
  tryerr:
    arm64.leaRdata x0, __str_blob_3  ; "panic at builtins-byte-order.a-big-endian-word-read-is-one-load-and-one-byte-swap.test:9: a read inside the array failed\x0a"
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
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movImm x0, 8
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.movImm x0, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
    arm64.movImm x0, 8
    arm64.storeBaseDispReg.word64 [x20 + 24], x0
    arm64.movImm x21, 0
    arm64.b forhdr
  fill:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseIndexScale.word64 x1, [x0 + x21*8 + 0]
  __im_cont:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow#10
  __im_bounds#11:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow#10
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow#10
  __im_viewed:
    arm64.sub x3, x2, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#10
  __im_store:
    arm64.loadRegBaseDisp.word64 x3, [x19 + 24]
    arm64.cmp x3, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.b forstep
  __im_stride:
    arm64.cmp x3, 1
    arm64.b.ne __im_slow#10
  __im_byte#16:
    arm64.add x2, x2, x0
    arm64.storeBaseDispReg.byte [x2 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.b forstep
  __im_slow#10:
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x21, x0
    arm64.b.lt fill
  forexit:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movImm x1, 2
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 2
    arm64.b.ls __im_slow#19
  __im_bounds#20:
    arm64.sub x0, x0, 2
    arm64.cmp x0, 4
    arm64.b.lt __im_slow#19
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#19
  __im_byte#22:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.add x0, x0, 2
    arm64.loadRegBaseDisp.word32 x0, [x0 + 0]
    arm64.rev.4 x20, x0
  tryok:
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.add x24, x20, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x20, x26, x20
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x0, x20, x23
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
  __il_body:
    arm64.movRegReg x0, x25
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
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
  __im_slow#19:
    arm64.movRegReg x0, x19
    arm64.bl __managed_word_get_be4
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  critsplit:
    arm64.movRegReg x20, x0
    arm64.b tryok
  tryerr:
    arm64.leaRdata x0, __str_blob_3  ; "panic at builtins-byte-order.a-big-endian-word-read-is-one-load-and-one-byte-swap.test:9: a read inside the array failed\x0a"
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
    arm64.epilogue 80
    arm64.ret
}
```
