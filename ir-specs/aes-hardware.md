---
feature: aes-hardware
status: stable
keywords: [builtins, __Builtins, aesEncryptBlock, AES-NI, AESE, hand-assembled chunk, intrinsics]
category: system
---

# `__Builtins.aesEncryptBlock`: the cases that pin emitted code

The cases of `specs/aes-hardware.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, and
the hand-assembled routine the block call reaches as the bytes the assembler wrote.

## Tests

<!-- test: aes-hardware.a-block-call-is-one-checked-entry-and-one-routine-call -->
<!-- tier: stdlib -->
`__Builtins.aesEncryptBlock` becomes a call to the checked runtime entry `__cpufeat_aes_cipher_encrypt_block`, which
tests the schedule's size and both blocks' spans once and makes one call to the hand-assembled `mrt_aes_encrypt_block`.
The entry's body is the Target IR below; the routine is a hex dump of the assembler's own bytes, still unlinked.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var schedule = ByteArray.create()
	var block = ByteArray.create()
	var sealed = ByteArray.create()
	key.resize(16)
	schedule.resize(352)
	block.resize(16)
	sealed.resize(16)
	__Builtins.aesExpandKey(schedule, key)
	__Builtins.aesEncryptBlock(sealed, 0, schedule, block, 0)
	print("{try sealed.get(0) otherwise 0}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
102
```
```RequiredRuntime
__cpufeat_aes_cipher_encrypt_block
mrt_aes_encrypt_block
```

```TargetIr:x64-windows
data {
  __mrt_cpu_aes_usable@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_console_probe_stdin@32 = i8 0
  __mrt_console_probe_stdout@33 = i8 0
  __mrt_console_probe_stderr@34 = i8 0
  __mrt_program_started@35 = i8 0
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
    x64.prologue 88
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r14, r8
    x64.storeSlotReg slot3, r14
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 352
    x64.movRegReg rcx, r12
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, r13
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, r14
    x64.callDirect __managed_resize
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __cpufeat_aes_cipher_expand_key
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rsi, 0
    x64.movRegReg rcx, r14
    x64.movRegReg rax, r12
    x64.movRegReg r9, r13
    x64.callDirect __cpufeat_aes_cipher_encrypt_block
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [r14 + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r14 + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.jmp tryok
  __im_stride:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok
  __im_slow:
    x64.movRegReg rcx, r14
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  critsplit:
    x64.movRegReg rax, r8
  tryok:
    x64.movRegReg rbx, rax
  trycont:
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot4, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot5, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot6, r8
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
    x64.loadRegSlot rax, slot6
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot5
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot4
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot6
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot6
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot3
    x64.callDirect __managed_decref
  exitdrop#5:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __managed_decref
  exitdrop#6:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __managed_decref
  exitdrop#7:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_encrypt_block {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.movRegReg r13, rax
    x64.movRegReg r14, r9
    x64.storeSlotReg slot0, rsi
    x64.loadRegBaseDisp.word64 r15, [r13 + 8]
    x64.cmpRegImm32 r15, 352
    x64.jcc less, abbad
  scbounded:
    x64.cmpRegImm32 r15, 480
    x64.jcc greater, abbad
  scstepped:
    x64.movRegReg rax, r15
    x64.andRegImm32 rax, r15, 63
    x64.cmpRegImm32 rax, 32
    x64.jcc notEqual, abbad
  aboutput:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r12, rax
    x64.jcc aboveEqual, abbad
  abo:
    x64.subRegReg rax, rax, r12
    x64.cmpRegImm32 rax, 16
    x64.jcc less, abbad
  abinput:
    x64.loadRegBaseDisp.word64 rax, [r14 + 8]
    x64.cmpRegReg rsi, rax
    x64.jcc aboveEqual, abbad
  abi:
    x64.subRegReg rax, rax, rsi
    x64.cmpRegImm32 rax, 16
    x64.jcc less, abbad
  abcrypt:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegReg rax, r15
    x64.shrRegImm8 rax, r15, 5
    x64.leaRegRegImm32 rcx, rax, -1
    x64.loadRegBaseDisp.word64 rdx, [r13 + 0]
    x64.shrRegImm8 r15, r15, 1
    x64.leaRegRegReg rax, rdx, r15
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rsi, [r14 + 0]
    x64.leaRegRegReg r8, rax, r12
    x64.loadRegSlot rax, slot0
    x64.leaRegRegReg r9, rsi, rax
    x64.callDirect mrt_aes_encrypt_block
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  abbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

chunk @mrt_aes_encrypt_block {
    0000: 55 48 89 e5 48 83 ec 20 f3 41 0f 6f 01 f3 0f 6f
    0010: 0a 66 0f ef c1 48 81 c2 10 00 00 00 48 81 f9 0c
    0020: 00 00 00 0f 84 20 00 00 00 0f 82 34 00 00 00 f3
    0030: 0f 6f 0a 66 0f 38 dc c1 f3 0f 6f 4a 10 66 0f 38
    0040: dc c1 48 81 c2 20 00 00 00 f3 0f 6f 0a 66 0f 38
    0050: dc c1 f3 0f 6f 4a 10 66 0f 38 dc c1 48 81 c2 20
    0060: 00 00 00 f3 0f 6f 0a 66 0f 38 dc c1 f3 0f 6f 4a
    0070: 10 66 0f 38 dc c1 f3 0f 6f 4a 20 66 0f 38 dc c1
    0080: f3 0f 6f 4a 30 66 0f 38 dc c1 f3 0f 6f 4a 40 66
    0090: 0f 38 dc c1 f3 0f 6f 4a 50 66 0f 38 dc c1 f3 0f
    00a0: 6f 4a 60 66 0f 38 dc c1 f3 0f 6f 4a 70 66 0f 38
    00b0: dc c1 f3 0f 6f 8a 80 00 00 00 66 0f 38 dc c1 f3
    00c0: 0f 6f 8a 90 00 00 00 66 0f 38 dd c1 f3 41 0f 7f
    00d0: 00 31 c0 48 83 c4 20 5d c3
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_cpu_aes_usable@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
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
    x64.prologue 88
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r14, r8
    x64.storeSlotReg slot3, r14
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 352
    x64.movRegReg rcx, r12
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, r13
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, r14
    x64.callDirect __managed_resize
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __cpufeat_aes_cipher_expand_key
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rsi, 0
    x64.movRegReg rcx, r14
    x64.movRegReg rax, r12
    x64.movRegReg r9, r13
    x64.callDirect __cpufeat_aes_cipher_encrypt_block
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [r14 + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r14 + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.jmp tryok
  __im_stride:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok
  __im_slow:
    x64.movRegReg rcx, r14
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  critsplit:
    x64.movRegReg rax, r8
  tryok:
    x64.movRegReg rbx, rax
  trycont:
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot4, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot5, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot6, r8
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
    x64.loadRegSlot rax, slot6
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot5
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot4
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot6
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot6
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot3
    x64.callDirect __managed_decref
  exitdrop#5:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __managed_decref
  exitdrop#6:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __managed_decref
  exitdrop#7:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_encrypt_block {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.movRegReg r13, rax
    x64.movRegReg r14, r9
    x64.storeSlotReg slot0, rsi
    x64.loadRegBaseDisp.word64 r15, [r13 + 8]
    x64.cmpRegImm32 r15, 352
    x64.jcc less, abbad
  scbounded:
    x64.cmpRegImm32 r15, 480
    x64.jcc greater, abbad
  scstepped:
    x64.movRegReg rax, r15
    x64.andRegImm32 rax, r15, 63
    x64.cmpRegImm32 rax, 32
    x64.jcc notEqual, abbad
  aboutput:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r12, rax
    x64.jcc aboveEqual, abbad
  abo:
    x64.subRegReg rax, rax, r12
    x64.cmpRegImm32 rax, 16
    x64.jcc less, abbad
  abinput:
    x64.loadRegBaseDisp.word64 rax, [r14 + 8]
    x64.cmpRegReg rsi, rax
    x64.jcc aboveEqual, abbad
  abi:
    x64.subRegReg rax, rax, rsi
    x64.cmpRegImm32 rax, 16
    x64.jcc less, abbad
  abcrypt:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegReg rax, r15
    x64.shrRegImm8 rax, r15, 5
    x64.leaRegRegImm32 rcx, rax, -1
    x64.loadRegBaseDisp.word64 rdx, [r13 + 0]
    x64.shrRegImm8 r15, r15, 1
    x64.leaRegRegReg rax, rdx, r15
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rsi, [r14 + 0]
    x64.leaRegRegReg r8, rax, r12
    x64.loadRegSlot rax, slot0
    x64.leaRegRegReg r9, rsi, rax
    x64.callDirect mrt_aes_encrypt_block
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  abbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

chunk @mrt_aes_encrypt_block {
    0000: 55 48 89 e5 48 83 ec 20 f3 41 0f 6f 01 f3 0f 6f
    0010: 0a 66 0f ef c1 48 81 c2 10 00 00 00 48 81 f9 0c
    0020: 00 00 00 0f 84 20 00 00 00 0f 82 34 00 00 00 f3
    0030: 0f 6f 0a 66 0f 38 dc c1 f3 0f 6f 4a 10 66 0f 38
    0040: dc c1 48 81 c2 20 00 00 00 f3 0f 6f 0a 66 0f 38
    0050: dc c1 f3 0f 6f 4a 10 66 0f 38 dc c1 48 81 c2 20
    0060: 00 00 00 f3 0f 6f 0a 66 0f 38 dc c1 f3 0f 6f 4a
    0070: 10 66 0f 38 dc c1 f3 0f 6f 4a 20 66 0f 38 dc c1
    0080: f3 0f 6f 4a 30 66 0f 38 dc c1 f3 0f 6f 4a 40 66
    0090: 0f 38 dc c1 f3 0f 6f 4a 50 66 0f 38 dc c1 f3 0f
    00a0: 6f 4a 60 66 0f 38 dc c1 f3 0f 6f 4a 70 66 0f 38
    00b0: dc c1 f3 0f 6f 8a 80 00 00 00 66 0f 38 dc c1 f3
    00c0: 0f 6f 8a 90 00 00 00 66 0f 38 dd c1 f3 41 0f 7f
    00d0: 00 31 c0 48 83 c4 20 5d c3
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
    arm64.prologue 112
    arm64.storeSlotReg slot10, x28
    arm64.storeSlotReg slot9, x27
    arm64.storeSlotReg slot8, x26
    arm64.storeSlotReg slot7, x25
    arm64.storeSlotReg slot6, x24
    arm64.storeSlotReg slot5, x23
    arm64.storeSlotReg slot4, x22
    arm64.storeSlotReg slot3, x21
    arm64.storeSlotReg slot2, x20
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x21, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.movImm x1, 16
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x1, 352
    arm64.movRegReg x0, x20
    arm64.bl __managed_resize
    arm64.movImm x1, 16
    arm64.movRegReg x0, x21
    arm64.bl __managed_resize
    arm64.movImm x1, 16
    arm64.movRegReg x0, x22
    arm64.bl __managed_resize
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __cpufeat_aes_cipher_expand_key
    arm64.movImm x1, 0
    arm64.movImm x4, 0
    arm64.movRegReg x0, x22
    arm64.movRegReg x2, x20
    arm64.movRegReg x3, x21
    arm64.bl __cpufeat_aes_cipher_encrypt_block
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b tryok
  __im_stride:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok
  __im_slow:
    arm64.movRegReg x0, x22
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.movImm x19, 0
    arm64.b trycont
  tryok:
    arm64.movRegReg x19, x0
  trycont:
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_1  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x19, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x28, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x19, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x23
    arm64.bl __mm_decref
  __il_body:
    arm64.movRegReg x0, x27
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitdrop#5:
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitdrop#6:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitdrop#7:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
}

func @__cpufeat_aes_cipher_encrypt_block {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x2
    arm64.movRegReg x22, x3
    arm64.movRegReg x23, x4
    arm64.loadRegBaseDisp.word64 x24, [x21 + 8]
    arm64.cmp x24, 352
    arm64.b.lt abbad
  scbounded:
    arm64.cmp x24, 480
    arm64.b.gt abbad
  scstepped:
    arm64.and x0, x24, 63
    arm64.cmp x0, 32
    arm64.b.ne abbad
  aboutput:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x20, x0
    arm64.b.hs abbad
  abo:
    arm64.sub x0, x0, x20
    arm64.cmp x0, 16
    arm64.b.lt abbad
  abinput:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.cmp x23, x0
    arm64.b.hs abbad
  abi:
    arm64.sub x0, x0, x23
    arm64.cmp x0, 16
    arm64.b.lt abbad
  abcrypt:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.lsr x0, x24, 5
    arm64.sub x0, x0, 1
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.lsr x2, x24, 1
    arm64.add x2, x1, x2
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x22 + 0]
    arm64.add x2, x2, x20
    arm64.add x3, x3, x23
    arm64.bl mrt_aes_encrypt_block
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
  abbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
}

chunk @mrt_aes_encrypt_block {
    0000: 60 00 c0 3d 1f 30 00 f1 03 02 00 54 00 01 00 54
    0010: 21 00 c0 3d 20 48 28 4e 00 68 28 4e 21 04 c0 3d
    0020: 20 48 28 4e 00 68 28 4e 21 80 00 91 21 00 c0 3d
    0030: 20 48 28 4e 00 68 28 4e 21 04 c0 3d 20 48 28 4e
    0040: 00 68 28 4e 21 80 00 91 21 00 c0 3d 20 48 28 4e
    0050: 00 68 28 4e 21 04 c0 3d 20 48 28 4e 00 68 28 4e
    0060: 21 08 c0 3d 20 48 28 4e 00 68 28 4e 21 0c c0 3d
    0070: 20 48 28 4e 00 68 28 4e 21 10 c0 3d 20 48 28 4e
    0080: 00 68 28 4e 21 14 c0 3d 20 48 28 4e 00 68 28 4e
    0090: 21 18 c0 3d 20 48 28 4e 00 68 28 4e 21 1c c0 3d
    00a0: 20 48 28 4e 00 68 28 4e 21 20 c0 3d 20 48 28 4e
    00b0: 00 68 28 4e 21 24 c0 3d 20 48 28 4e 21 28 c0 3d
    00c0: 00 1c 21 6e 40 00 80 3d e0 03 1f aa c0 03 5f d6
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __mrt_cpu_aes_usable@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @main {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot10, x28
    arm64.storeSlotReg slot9, x27
    arm64.storeSlotReg slot8, x26
    arm64.storeSlotReg slot7, x25
    arm64.storeSlotReg slot6, x24
    arm64.storeSlotReg slot5, x23
    arm64.storeSlotReg slot4, x22
    arm64.storeSlotReg slot3, x21
    arm64.storeSlotReg slot2, x20
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x21, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.movImm x1, 16
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x1, 352
    arm64.movRegReg x0, x20
    arm64.bl __managed_resize
    arm64.movImm x1, 16
    arm64.movRegReg x0, x21
    arm64.bl __managed_resize
    arm64.movImm x1, 16
    arm64.movRegReg x0, x22
    arm64.bl __managed_resize
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __cpufeat_aes_cipher_expand_key
    arm64.movImm x1, 0
    arm64.movImm x4, 0
    arm64.movRegReg x0, x22
    arm64.movRegReg x2, x20
    arm64.movRegReg x3, x21
    arm64.bl __cpufeat_aes_cipher_encrypt_block
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b tryok
  __im_stride:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok
  __im_slow:
    arm64.movRegReg x0, x22
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.movImm x19, 0
    arm64.b trycont
  tryok:
    arm64.movRegReg x19, x0
  trycont:
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_1  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x19, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x28, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x19, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x23
    arm64.bl __mm_decref
  __il_body:
    arm64.movRegReg x0, x27
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitdrop#5:
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitdrop#6:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitdrop#7:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
}

func @__cpufeat_aes_cipher_encrypt_block {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x2
    arm64.movRegReg x22, x3
    arm64.movRegReg x23, x4
    arm64.loadRegBaseDisp.word64 x24, [x21 + 8]
    arm64.cmp x24, 352
    arm64.b.lt abbad
  scbounded:
    arm64.cmp x24, 480
    arm64.b.gt abbad
  scstepped:
    arm64.and x0, x24, 63
    arm64.cmp x0, 32
    arm64.b.ne abbad
  aboutput:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x20, x0
    arm64.b.hs abbad
  abo:
    arm64.sub x0, x0, x20
    arm64.cmp x0, 16
    arm64.b.lt abbad
  abinput:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.cmp x23, x0
    arm64.b.hs abbad
  abi:
    arm64.sub x0, x0, x23
    arm64.cmp x0, 16
    arm64.b.lt abbad
  abcrypt:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.lsr x0, x24, 5
    arm64.sub x0, x0, 1
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.lsr x2, x24, 1
    arm64.add x2, x1, x2
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x22 + 0]
    arm64.add x2, x2, x20
    arm64.add x3, x3, x23
    arm64.bl mrt_aes_encrypt_block
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
  abbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
}

chunk @mrt_aes_encrypt_block {
    0000: 60 00 c0 3d 1f 30 00 f1 03 02 00 54 00 01 00 54
    0010: 21 00 c0 3d 20 48 28 4e 00 68 28 4e 21 04 c0 3d
    0020: 20 48 28 4e 00 68 28 4e 21 80 00 91 21 00 c0 3d
    0030: 20 48 28 4e 00 68 28 4e 21 04 c0 3d 20 48 28 4e
    0040: 00 68 28 4e 21 80 00 91 21 00 c0 3d 20 48 28 4e
    0050: 00 68 28 4e 21 04 c0 3d 20 48 28 4e 00 68 28 4e
    0060: 21 08 c0 3d 20 48 28 4e 00 68 28 4e 21 0c c0 3d
    0070: 20 48 28 4e 00 68 28 4e 21 10 c0 3d 20 48 28 4e
    0080: 00 68 28 4e 21 14 c0 3d 20 48 28 4e 00 68 28 4e
    0090: 21 18 c0 3d 20 48 28 4e 00 68 28 4e 21 1c c0 3d
    00a0: 20 48 28 4e 00 68 28 4e 21 20 c0 3d 20 48 28 4e
    00b0: 00 68 28 4e 21 24 c0 3d 20 48 28 4e 21 28 c0 3d
    00c0: 00 1c 21 6e 40 00 80 3d e0 03 1f aa c0 03 5f d6
}
```

<!-- test: aes-hardware.a-counter-mode-call-is-one-checked-entry-and-one-routine-call -->
<!-- tier: stdlib -->
`__Builtins.aesCtrXor` becomes a call to the checked runtime entry `__cpufeat_aes_cipher_ctr_xor`, which tests the
schedule's size, the counter block's length and the group's span once, picks the routine for the group size and makes
one call to it with the control word (round count and counter offset), the round keys, the counter block and the
buffer. The entry's body is the Target IR below; the four-block routine is a hex dump of the assembler's own bytes,
still unlinked.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var schedule = ByteArray.create()
	var buffer = ByteArray.create()
	var counterBlock = ByteArray.create()
	key.resize(16)
	schedule.resize(352)
	buffer.resize(64)
	counterBlock.resize(16)
	__Builtins.aesExpandKey(schedule, key)
	__Builtins.aesCtrXor(buffer, 0, schedule, counterBlock, 0, 4)
	print("{try buffer.get(0) otherwise 0}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
102
```
```RequiredRuntime
__cpufeat_aes_cipher_ctr_xor
mrt_aes_ctr_blocks_4
```

```TargetIr:x64-windows
data {
  __mrt_cpu_aes_usable@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_console_probe_stdin@32 = i8 0
  __mrt_console_probe_stdout@33 = i8 0
  __mrt_console_probe_stderr@34 = i8 0
  __mrt_program_started@35 = i8 0
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
    x64.prologue 88
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r14, r8
    x64.storeSlotReg slot3, r14
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 352
    x64.movRegReg rcx, r12
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 64
    x64.movRegReg rcx, r13
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, r14
    x64.callDirect __managed_resize
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __cpufeat_aes_cipher_expand_key
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rsi, 0
    x64.movRegImm32 rdi, 4
    x64.movRegReg rcx, r13
    x64.movRegReg rax, r12
    x64.movRegReg r9, r14
    x64.callDirect __cpufeat_aes_cipher_ctr_xor
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r13 + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.jmp tryok
  __im_stride:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok
  __im_slow:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  critsplit:
    x64.movRegReg rax, r8
  tryok:
    x64.movRegReg rbx, rax
  trycont:
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot4, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot5, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot6, r8
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
    x64.loadRegSlot rax, slot6
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot5
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot4
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot6
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot6
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot3
    x64.callDirect __managed_decref
  exitdrop#5:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __managed_decref
  exitdrop#6:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __managed_decref
  exitdrop#7:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_ctr_xor {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 72
    x64.movRegReg rbx, rcx
    x64.storeSlotReg slot1, rbx
    x64.movRegReg r12, rdx
    x64.storeSlotReg slot0, r12
    x64.storeSlotReg slot3, rax
    x64.movRegReg r13, r9
    x64.storeSlotReg slot2, r13
    x64.movRegReg r14, rsi
    x64.loadRegBaseDisp.word64 r15, [rax + 8]
    x64.cmpRegImm32 r15, 352
    x64.jcc less, cxbad
  scbounded:
    x64.cmpRegImm32 r15, 480
    x64.jcc greater, cxbad
  scstepped:
    x64.movRegReg rax, r15
    x64.andRegImm32 rax, r15, 63
    x64.cmpRegImm32 rax, 32
    x64.jcc notEqual, cxbad
  cxcounter:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.cmpRegImm32 rax, 16
    x64.jcc notEqual, cxbad
  cxdispatch:
    x64.cmpRegImm32 rdi, 1
    x64.jcc notEqual, cxprobe1
  cxarm1:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r12, rax
    x64.jcc aboveEqual, cxbad
  cxspan1:
    x64.subRegReg rax, rax, r12
    x64.cmpRegImm32 rax, 16
    x64.jcc less, cxbad
  cxcrypt1:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.shrRegImm8 r15, r15, 5
    x64.leaRegRegImm32 rax, r15, -1
    x64.shlRegImm8 r14, r14, 8
    x64.movRegReg rcx, r14
    x64.orRegReg rcx, r14, rax
    x64.loadRegSlot rax, slot3
    x64.loadRegBaseDisp.word64 rdx, [rax + 0]
    x64.loadRegBaseDisp.word64 r8, [r13 + 0]
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegReg r9, rax, r12
    x64.callDirect mrt_aes_ctr_blocks_1
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  cxprobe1:
    x64.cmpRegImm32 rdi, 2
    x64.jcc notEqual, cxprobe2
  cxarm2:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r12, rax
    x64.jcc aboveEqual, cxbad
  cxspan2:
    x64.subRegReg rax, rax, r12
    x64.cmpRegImm32 rax, 32
    x64.jcc less, cxbad
  cxcrypt2:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.shrRegImm8 r15, r15, 5
    x64.leaRegRegImm32 rax, r15, -1
    x64.shlRegImm8 r14, r14, 8
    x64.movRegReg rcx, r14
    x64.orRegReg rcx, r14, rax
    x64.loadRegSlot rax, slot3
    x64.loadRegBaseDisp.word64 rdx, [rax + 0]
    x64.loadRegSlot rax, slot2
    x64.loadRegBaseDisp.word64 r8, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegReg r9, rax, r12
    x64.callDirect mrt_aes_ctr_blocks_2
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  cxprobe2:
    x64.cmpRegImm32 rdi, 4
    x64.jcc notEqual, cxprobe4
  cxarm4:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r12, rax
    x64.jcc aboveEqual, cxbad
  cxspan4:
    x64.subRegReg rax, rax, r12
    x64.cmpRegImm32 rax, 64
    x64.jcc less, cxbad
  cxcrypt4:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.shrRegImm8 r15, r15, 5
    x64.leaRegRegImm32 rax, r15, -1
    x64.shlRegImm8 r14, r14, 8
    x64.movRegReg rcx, r14
    x64.orRegReg rcx, r14, rax
    x64.loadRegSlot rax, slot3
    x64.loadRegBaseDisp.word64 rdx, [rax + 0]
    x64.loadRegSlot rax, slot2
    x64.loadRegBaseDisp.word64 r8, [rax + 0]
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegReg r9, rax, r12
    x64.callDirect mrt_aes_ctr_blocks_4
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  cxprobe4:
    x64.cmpRegImm32 rdi, 8
    x64.jcc notEqual, cxbad
  cxarm8:
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegReg r12, rax
    x64.jcc aboveEqual, cxbad
  cxspan8:
    x64.subRegReg rax, rax, r12
    x64.cmpRegImm32 rax, 128
    x64.jcc less, cxbad
  cxcrypt8:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __managed_cow_detach
    x64.shrRegImm8 r15, r15, 5
    x64.leaRegRegImm32 rax, r15, -1
    x64.shlRegImm8 r14, r14, 8
    x64.movRegReg rcx, r14
    x64.orRegReg rcx, r14, rax
    x64.loadRegSlot rax, slot3
    x64.loadRegBaseDisp.word64 rdx, [rax + 0]
    x64.loadRegSlot rax, slot2
    x64.loadRegBaseDisp.word64 r8, [rax + 0]
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegSlot rsi, slot0
    x64.leaRegRegReg r9, rax, rsi
    x64.callDirect mrt_aes_ctr_blocks_8
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  cxbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

chunk @mrt_aes_ctr_blocks_4 {
    0000: 55 48 89 e5 48 83 ec 20 49 89 cb 49 81 e3 ff 00
    0010: 00 00 48 c1 e9 08 f3 41 0f 6f 28 41 8b 40 0c 0f
    0020: c8 48 01 c8 0f 28 c5 41 89 c0 41 0f c8 66 41 0f
    0030: 3a 22 c0 03 0f 28 cd 41 89 c0 49 81 c0 01 00 00
    0040: 00 41 0f c8 66 41 0f 3a 22 c8 03 0f 28 d5 41 89
    0050: c0 49 81 c0 02 00 00 00 41 0f c8 66 41 0f 3a 22
    0060: d0 03 0f 28 dd 41 89 c0 49 81 c0 03 00 00 00 41
    0070: 0f c8 66 41 0f 3a 22 d8 03 49 89 d2 f3 41 0f 6f
    0080: 22 66 0f ef c4 66 0f ef cc 66 0f ef d4 66 0f ef
    0090: dc 49 81 c2 10 00 00 00 49 81 fb 0c 00 00 00 0f
    00a0: 84 40 00 00 00 0f 82 74 00 00 00 f3 41 0f 6f 22
    00b0: 66 0f 38 dc c4 66 0f 38 dc cc 66 0f 38 dc d4 66
    00c0: 0f 38 dc dc f3 41 0f 6f 62 10 66 0f 38 dc c4 66
    00d0: 0f 38 dc cc 66 0f 38 dc d4 66 0f 38 dc dc 49 81
    00e0: c2 20 00 00 00 f3 41 0f 6f 22 66 0f 38 dc c4 66
    00f0: 0f 38 dc cc 66 0f 38 dc d4 66 0f 38 dc dc f3 41
    0100: 0f 6f 62 10 66 0f 38 dc c4 66 0f 38 dc cc 66 0f
    0110: 38 dc d4 66 0f 38 dc dc 49 81 c2 20 00 00 00 f3
    0120: 41 0f 6f 22 66 0f 38 dc c4 66 0f 38 dc cc 66 0f
    0130: 38 dc d4 66 0f 38 dc dc f3 41 0f 6f 62 10 66 0f
    0140: 38 dc c4 66 0f 38 dc cc 66 0f 38 dc d4 66 0f 38
    0150: dc dc f3 41 0f 6f 62 20 66 0f 38 dc c4 66 0f 38
    0160: dc cc 66 0f 38 dc d4 66 0f 38 dc dc f3 41 0f 6f
    0170: 62 30 66 0f 38 dc c4 66 0f 38 dc cc 66 0f 38 dc
    0180: d4 66 0f 38 dc dc f3 41 0f 6f 62 40 66 0f 38 dc
    0190: c4 66 0f 38 dc cc 66 0f 38 dc d4 66 0f 38 dc dc
    01a0: f3 41 0f 6f 62 50 66 0f 38 dc c4 66 0f 38 dc cc
    01b0: 66 0f 38 dc d4 66 0f 38 dc dc f3 41 0f 6f 62 60
    01c0: 66 0f 38 dc c4 66 0f 38 dc cc 66 0f 38 dc d4 66
    01d0: 0f 38 dc dc f3 41 0f 6f 62 70 66 0f 38 dc c4 66
    01e0: 0f 38 dc cc 66 0f 38 dc d4 66 0f 38 dc dc f3 41
    01f0: 0f 6f a2 80 00 00 00 66 0f 38 dc c4 66 0f 38 dc
    0200: cc 66 0f 38 dc d4 66 0f 38 dc dc f3 41 0f 6f a2
    0210: 90 00 00 00 66 0f 38 dd c4 66 0f 38 dd cc 66 0f
    0220: 38 dd d4 66 0f 38 dd dc f3 41 0f 6f 21 66 0f ef
    0230: e0 f3 41 0f 7f 21 f3 41 0f 6f 61 10 66 0f ef e1
    0240: f3 41 0f 7f 61 10 f3 41 0f 6f 61 20 66 0f ef e2
    0250: f3 41 0f 7f 61 20 f3 41 0f 6f 61 30 66 0f ef e3
    0260: f3 41 0f 7f 61 30 48 81 c0 04 00 00 00 49 81 c1
    0270: 40 00 00 00 31 c0 48 83 c4 20 5d c3
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_cpu_aes_usable@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
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
    x64.prologue 88
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r14, r8
    x64.storeSlotReg slot3, r14
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 352
    x64.movRegReg rcx, r12
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 64
    x64.movRegReg rcx, r13
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, r14
    x64.callDirect __managed_resize
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __cpufeat_aes_cipher_expand_key
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rsi, 0
    x64.movRegImm32 rdi, 4
    x64.movRegReg rcx, r13
    x64.movRegReg rax, r12
    x64.movRegReg r9, r14
    x64.callDirect __cpufeat_aes_cipher_ctr_xor
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r13 + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.jmp tryok
  __im_stride:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok
  __im_slow:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  critsplit:
    x64.movRegReg rax, r8
  tryok:
    x64.movRegReg rbx, rax
  trycont:
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot4, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot5, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot6, r8
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
    x64.loadRegSlot rax, slot6
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot5
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot4
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot6
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot6
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot3
    x64.callDirect __managed_decref
  exitdrop#5:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __managed_decref
  exitdrop#6:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __managed_decref
  exitdrop#7:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_ctr_xor {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 72
    x64.movRegReg rbx, rcx
    x64.storeSlotReg slot1, rbx
    x64.movRegReg r12, rdx
    x64.storeSlotReg slot0, r12
    x64.storeSlotReg slot3, rax
    x64.movRegReg r13, r9
    x64.storeSlotReg slot2, r13
    x64.movRegReg r14, rsi
    x64.loadRegBaseDisp.word64 r15, [rax + 8]
    x64.cmpRegImm32 r15, 352
    x64.jcc less, cxbad
  scbounded:
    x64.cmpRegImm32 r15, 480
    x64.jcc greater, cxbad
  scstepped:
    x64.movRegReg rax, r15
    x64.andRegImm32 rax, r15, 63
    x64.cmpRegImm32 rax, 32
    x64.jcc notEqual, cxbad
  cxcounter:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.cmpRegImm32 rax, 16
    x64.jcc notEqual, cxbad
  cxdispatch:
    x64.cmpRegImm32 rdi, 1
    x64.jcc notEqual, cxprobe1
  cxarm1:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r12, rax
    x64.jcc aboveEqual, cxbad
  cxspan1:
    x64.subRegReg rax, rax, r12
    x64.cmpRegImm32 rax, 16
    x64.jcc less, cxbad
  cxcrypt1:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.shrRegImm8 r15, r15, 5
    x64.leaRegRegImm32 rax, r15, -1
    x64.shlRegImm8 r14, r14, 8
    x64.movRegReg rcx, r14
    x64.orRegReg rcx, r14, rax
    x64.loadRegSlot rax, slot3
    x64.loadRegBaseDisp.word64 rdx, [rax + 0]
    x64.loadRegBaseDisp.word64 r8, [r13 + 0]
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegReg r9, rax, r12
    x64.callDirect mrt_aes_ctr_blocks_1
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  cxprobe1:
    x64.cmpRegImm32 rdi, 2
    x64.jcc notEqual, cxprobe2
  cxarm2:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r12, rax
    x64.jcc aboveEqual, cxbad
  cxspan2:
    x64.subRegReg rax, rax, r12
    x64.cmpRegImm32 rax, 32
    x64.jcc less, cxbad
  cxcrypt2:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.shrRegImm8 r15, r15, 5
    x64.leaRegRegImm32 rax, r15, -1
    x64.shlRegImm8 r14, r14, 8
    x64.movRegReg rcx, r14
    x64.orRegReg rcx, r14, rax
    x64.loadRegSlot rax, slot3
    x64.loadRegBaseDisp.word64 rdx, [rax + 0]
    x64.loadRegSlot rax, slot2
    x64.loadRegBaseDisp.word64 r8, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.leaRegRegReg r9, rax, r12
    x64.callDirect mrt_aes_ctr_blocks_2
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  cxprobe2:
    x64.cmpRegImm32 rdi, 4
    x64.jcc notEqual, cxprobe4
  cxarm4:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r12, rax
    x64.jcc aboveEqual, cxbad
  cxspan4:
    x64.subRegReg rax, rax, r12
    x64.cmpRegImm32 rax, 64
    x64.jcc less, cxbad
  cxcrypt4:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.shrRegImm8 r15, r15, 5
    x64.leaRegRegImm32 rax, r15, -1
    x64.shlRegImm8 r14, r14, 8
    x64.movRegReg rcx, r14
    x64.orRegReg rcx, r14, rax
    x64.loadRegSlot rax, slot3
    x64.loadRegBaseDisp.word64 rdx, [rax + 0]
    x64.loadRegSlot rax, slot2
    x64.loadRegBaseDisp.word64 r8, [rax + 0]
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegReg r9, rax, r12
    x64.callDirect mrt_aes_ctr_blocks_4
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  cxprobe4:
    x64.cmpRegImm32 rdi, 8
    x64.jcc notEqual, cxbad
  cxarm8:
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegReg r12, rax
    x64.jcc aboveEqual, cxbad
  cxspan8:
    x64.subRegReg rax, rax, r12
    x64.cmpRegImm32 rax, 128
    x64.jcc less, cxbad
  cxcrypt8:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __managed_cow_detach
    x64.shrRegImm8 r15, r15, 5
    x64.leaRegRegImm32 rax, r15, -1
    x64.shlRegImm8 r14, r14, 8
    x64.movRegReg rcx, r14
    x64.orRegReg rcx, r14, rax
    x64.loadRegSlot rax, slot3
    x64.loadRegBaseDisp.word64 rdx, [rax + 0]
    x64.loadRegSlot rax, slot2
    x64.loadRegBaseDisp.word64 r8, [rax + 0]
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegSlot rsi, slot0
    x64.leaRegRegReg r9, rax, rsi
    x64.callDirect mrt_aes_ctr_blocks_8
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  cxbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

chunk @mrt_aes_ctr_blocks_4 {
    0000: 55 48 89 e5 48 83 ec 20 49 89 cb 49 81 e3 ff 00
    0010: 00 00 48 c1 e9 08 f3 41 0f 6f 28 41 8b 40 0c 0f
    0020: c8 48 01 c8 0f 28 c5 41 89 c0 41 0f c8 66 41 0f
    0030: 3a 22 c0 03 0f 28 cd 41 89 c0 49 81 c0 01 00 00
    0040: 00 41 0f c8 66 41 0f 3a 22 c8 03 0f 28 d5 41 89
    0050: c0 49 81 c0 02 00 00 00 41 0f c8 66 41 0f 3a 22
    0060: d0 03 0f 28 dd 41 89 c0 49 81 c0 03 00 00 00 41
    0070: 0f c8 66 41 0f 3a 22 d8 03 49 89 d2 f3 41 0f 6f
    0080: 22 66 0f ef c4 66 0f ef cc 66 0f ef d4 66 0f ef
    0090: dc 49 81 c2 10 00 00 00 49 81 fb 0c 00 00 00 0f
    00a0: 84 40 00 00 00 0f 82 74 00 00 00 f3 41 0f 6f 22
    00b0: 66 0f 38 dc c4 66 0f 38 dc cc 66 0f 38 dc d4 66
    00c0: 0f 38 dc dc f3 41 0f 6f 62 10 66 0f 38 dc c4 66
    00d0: 0f 38 dc cc 66 0f 38 dc d4 66 0f 38 dc dc 49 81
    00e0: c2 20 00 00 00 f3 41 0f 6f 22 66 0f 38 dc c4 66
    00f0: 0f 38 dc cc 66 0f 38 dc d4 66 0f 38 dc dc f3 41
    0100: 0f 6f 62 10 66 0f 38 dc c4 66 0f 38 dc cc 66 0f
    0110: 38 dc d4 66 0f 38 dc dc 49 81 c2 20 00 00 00 f3
    0120: 41 0f 6f 22 66 0f 38 dc c4 66 0f 38 dc cc 66 0f
    0130: 38 dc d4 66 0f 38 dc dc f3 41 0f 6f 62 10 66 0f
    0140: 38 dc c4 66 0f 38 dc cc 66 0f 38 dc d4 66 0f 38
    0150: dc dc f3 41 0f 6f 62 20 66 0f 38 dc c4 66 0f 38
    0160: dc cc 66 0f 38 dc d4 66 0f 38 dc dc f3 41 0f 6f
    0170: 62 30 66 0f 38 dc c4 66 0f 38 dc cc 66 0f 38 dc
    0180: d4 66 0f 38 dc dc f3 41 0f 6f 62 40 66 0f 38 dc
    0190: c4 66 0f 38 dc cc 66 0f 38 dc d4 66 0f 38 dc dc
    01a0: f3 41 0f 6f 62 50 66 0f 38 dc c4 66 0f 38 dc cc
    01b0: 66 0f 38 dc d4 66 0f 38 dc dc f3 41 0f 6f 62 60
    01c0: 66 0f 38 dc c4 66 0f 38 dc cc 66 0f 38 dc d4 66
    01d0: 0f 38 dc dc f3 41 0f 6f 62 70 66 0f 38 dc c4 66
    01e0: 0f 38 dc cc 66 0f 38 dc d4 66 0f 38 dc dc f3 41
    01f0: 0f 6f a2 80 00 00 00 66 0f 38 dc c4 66 0f 38 dc
    0200: cc 66 0f 38 dc d4 66 0f 38 dc dc f3 41 0f 6f a2
    0210: 90 00 00 00 66 0f 38 dd c4 66 0f 38 dd cc 66 0f
    0220: 38 dd d4 66 0f 38 dd dc f3 41 0f 6f 21 66 0f ef
    0230: e0 f3 41 0f 7f 21 f3 41 0f 6f 61 10 66 0f ef e1
    0240: f3 41 0f 7f 61 10 f3 41 0f 6f 61 20 66 0f ef e2
    0250: f3 41 0f 7f 61 20 f3 41 0f 6f 61 30 66 0f ef e3
    0260: f3 41 0f 7f 61 30 48 81 c0 04 00 00 00 49 81 c1
    0270: 40 00 00 00 31 c0 48 83 c4 20 5d c3
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
    arm64.prologue 112
    arm64.storeSlotReg slot10, x28
    arm64.storeSlotReg slot9, x27
    arm64.storeSlotReg slot8, x26
    arm64.storeSlotReg slot7, x25
    arm64.storeSlotReg slot6, x24
    arm64.storeSlotReg slot5, x23
    arm64.storeSlotReg slot4, x22
    arm64.storeSlotReg slot3, x21
    arm64.storeSlotReg slot2, x20
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x21, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.movImm x1, 16
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x1, 352
    arm64.movRegReg x0, x20
    arm64.bl __managed_resize
    arm64.movImm x1, 64
    arm64.movRegReg x0, x21
    arm64.bl __managed_resize
    arm64.movImm x1, 16
    arm64.movRegReg x0, x22
    arm64.bl __managed_resize
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __cpufeat_aes_cipher_expand_key
    arm64.movImm x1, 0
    arm64.movImm x4, 0
    arm64.movImm x5, 4
    arm64.movRegReg x0, x21
    arm64.movRegReg x2, x20
    arm64.movRegReg x3, x22
    arm64.bl __cpufeat_aes_cipher_ctr_xor
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b tryok
  __im_stride:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok
  __im_slow:
    arm64.movRegReg x0, x21
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.movImm x19, 0
    arm64.b trycont
  tryok:
    arm64.movRegReg x19, x0
  trycont:
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_1  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x19, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x28, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x19, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x23
    arm64.bl __mm_decref
  __il_body:
    arm64.movRegReg x0, x27
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitdrop#5:
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitdrop#6:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitdrop#7:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
}

func @__cpufeat_aes_cipher_ctr_xor {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x2
    arm64.movRegReg x22, x3
    arm64.movRegReg x23, x4
    arm64.loadRegBaseDisp.word64 x24, [x21 + 8]
    arm64.cmp x24, 352
    arm64.b.lt cxbad
  scbounded:
    arm64.cmp x24, 480
    arm64.b.gt cxbad
  scstepped:
    arm64.and x0, x24, 63
    arm64.cmp x0, 32
    arm64.b.ne cxbad
  cxcounter:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.cmp x0, 16
    arm64.b.ne cxbad
  cxdispatch:
    arm64.cmp x5, 1
    arm64.b.ne cxprobe1
  cxarm1:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x20, x0
    arm64.b.hs cxbad
  cxspan1:
    arm64.sub x0, x0, x20
    arm64.cmp x0, 16
    arm64.b.lt cxbad
  cxcrypt1:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.lsr x0, x24, 5
    arm64.sub x0, x0, 1
    arm64.lsl x1, x23, 8
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.add x3, x3, x20
    arm64.bl mrt_aes_ctr_blocks_1
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
  cxprobe1:
    arm64.cmp x5, 2
    arm64.b.ne cxprobe2
  cxarm2:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x20, x0
    arm64.b.hs cxbad
  cxspan2:
    arm64.sub x0, x0, x20
    arm64.cmp x0, 32
    arm64.b.lt cxbad
  cxcrypt2:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.lsr x0, x24, 5
    arm64.sub x0, x0, 1
    arm64.lsl x1, x23, 8
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.add x3, x3, x20
    arm64.bl mrt_aes_ctr_blocks_2
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
  cxprobe2:
    arm64.cmp x5, 4
    arm64.b.ne cxprobe4
  cxarm4:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x20, x0
    arm64.b.hs cxbad
  cxspan4:
    arm64.sub x0, x0, x20
    arm64.cmp x0, 64
    arm64.b.lt cxbad
  cxcrypt4:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.lsr x0, x24, 5
    arm64.sub x0, x0, 1
    arm64.lsl x1, x23, 8
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.add x3, x3, x20
    arm64.bl mrt_aes_ctr_blocks_4
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
  cxprobe4:
    arm64.cmp x5, 8
    arm64.b.ne cxbad
  cxarm8:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x20, x0
    arm64.b.hs cxbad
  cxspan8:
    arm64.sub x0, x0, x20
    arm64.cmp x0, 128
    arm64.b.lt cxbad
  cxcrypt8:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.lsr x0, x24, 5
    arm64.sub x0, x0, 1
    arm64.lsl x1, x23, 8
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.add x3, x3, x20
    arm64.bl mrt_aes_ctr_blocks_8
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
  cxbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
}

chunk @mrt_aes_ctr_blocks_4 {
    0000: e4 1f 80 d2 04 00 04 8a 00 fc 48 d3 45 0c 40 b9
    0010: a5 08 c0 5a a5 00 00 8b 40 00 c0 3d a7 00 00 91
    0020: e7 08 c0 5a e0 1c 1c 4e 41 00 c0 3d a7 04 00 91
    0030: e7 08 c0 5a e1 1c 1c 4e 42 00 c0 3d a7 08 00 91
    0040: e7 08 c0 5a e2 1c 1c 4e 43 00 c0 3d a7 0c 00 91
    0050: e7 08 c0 5a e3 1c 1c 4e e6 03 01 aa 9f 30 00 f1
    0060: 03 05 00 54 80 02 00 54 d0 00 c0 3d 00 4a 28 4e
    0070: 00 68 28 4e 01 4a 28 4e 21 68 28 4e 02 4a 28 4e
    0080: 42 68 28 4e 03 4a 28 4e 63 68 28 4e d0 04 c0 3d
    0090: 00 4a 28 4e 00 68 28 4e 01 4a 28 4e 21 68 28 4e
    00a0: 02 4a 28 4e 42 68 28 4e 03 4a 28 4e 63 68 28 4e
    00b0: c6 80 00 91 d0 00 c0 3d 00 4a 28 4e 00 68 28 4e
    00c0: 01 4a 28 4e 21 68 28 4e 02 4a 28 4e 42 68 28 4e
    00d0: 03 4a 28 4e 63 68 28 4e d0 04 c0 3d 00 4a 28 4e
    00e0: 00 68 28 4e 01 4a 28 4e 21 68 28 4e 02 4a 28 4e
    00f0: 42 68 28 4e 03 4a 28 4e 63 68 28 4e c6 80 00 91
    0100: d0 00 c0 3d 00 4a 28 4e 00 68 28 4e 01 4a 28 4e
    0110: 21 68 28 4e 02 4a 28 4e 42 68 28 4e 03 4a 28 4e
    0120: 63 68 28 4e d0 04 c0 3d 00 4a 28 4e 00 68 28 4e
    0130: 01 4a 28 4e 21 68 28 4e 02 4a 28 4e 42 68 28 4e
    0140: 03 4a 28 4e 63 68 28 4e d0 08 c0 3d 00 4a 28 4e
    0150: 00 68 28 4e 01 4a 28 4e 21 68 28 4e 02 4a 28 4e
    0160: 42 68 28 4e 03 4a 28 4e 63 68 28 4e d0 0c c0 3d
    0170: 00 4a 28 4e 00 68 28 4e 01 4a 28 4e 21 68 28 4e
    0180: 02 4a 28 4e 42 68 28 4e 03 4a 28 4e 63 68 28 4e
    0190: d0 10 c0 3d 00 4a 28 4e 00 68 28 4e 01 4a 28 4e
    01a0: 21 68 28 4e 02 4a 28 4e 42 68 28 4e 03 4a 28 4e
    01b0: 63 68 28 4e d0 14 c0 3d 00 4a 28 4e 00 68 28 4e
    01c0: 01 4a 28 4e 21 68 28 4e 02 4a 28 4e 42 68 28 4e
    01d0: 03 4a 28 4e 63 68 28 4e d0 18 c0 3d 00 4a 28 4e
    01e0: 00 68 28 4e 01 4a 28 4e 21 68 28 4e 02 4a 28 4e
    01f0: 42 68 28 4e 03 4a 28 4e 63 68 28 4e d0 1c c0 3d
    0200: 00 4a 28 4e 00 68 28 4e 01 4a 28 4e 21 68 28 4e
    0210: 02 4a 28 4e 42 68 28 4e 03 4a 28 4e 63 68 28 4e
    0220: d0 20 c0 3d 00 4a 28 4e 00 68 28 4e 01 4a 28 4e
    0230: 21 68 28 4e 02 4a 28 4e 42 68 28 4e 03 4a 28 4e
    0240: 63 68 28 4e d0 24 c0 3d 00 4a 28 4e 01 4a 28 4e
    0250: 02 4a 28 4e 03 4a 28 4e d0 28 c0 3d 00 1c 30 6e
    0260: 21 1c 30 6e 42 1c 30 6e 63 1c 30 6e 71 00 c0 3d
    0270: 31 1e 20 6e 71 00 80 3d 71 04 c0 3d 31 1e 21 6e
    0280: 71 04 80 3d 71 08 c0 3d 31 1e 22 6e 71 08 80 3d
    0290: 71 0c c0 3d 31 1e 23 6e 71 0c 80 3d e0 03 1f aa
    02a0: c0 03 5f d6
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __mrt_cpu_aes_usable@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @main {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot10, x28
    arm64.storeSlotReg slot9, x27
    arm64.storeSlotReg slot8, x26
    arm64.storeSlotReg slot7, x25
    arm64.storeSlotReg slot6, x24
    arm64.storeSlotReg slot5, x23
    arm64.storeSlotReg slot4, x22
    arm64.storeSlotReg slot3, x21
    arm64.storeSlotReg slot2, x20
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x21, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.movImm x1, 16
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x1, 352
    arm64.movRegReg x0, x20
    arm64.bl __managed_resize
    arm64.movImm x1, 64
    arm64.movRegReg x0, x21
    arm64.bl __managed_resize
    arm64.movImm x1, 16
    arm64.movRegReg x0, x22
    arm64.bl __managed_resize
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __cpufeat_aes_cipher_expand_key
    arm64.movImm x1, 0
    arm64.movImm x4, 0
    arm64.movImm x5, 4
    arm64.movRegReg x0, x21
    arm64.movRegReg x2, x20
    arm64.movRegReg x3, x22
    arm64.bl __cpufeat_aes_cipher_ctr_xor
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b tryok
  __im_stride:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok
  __im_slow:
    arm64.movRegReg x0, x21
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.movImm x19, 0
    arm64.b trycont
  tryok:
    arm64.movRegReg x19, x0
  trycont:
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_1  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x19, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x28, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x19, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x23
    arm64.bl __mm_decref
  __il_body:
    arm64.movRegReg x0, x27
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitdrop#5:
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitdrop#6:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitdrop#7:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
}

func @__cpufeat_aes_cipher_ctr_xor {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x2
    arm64.movRegReg x22, x3
    arm64.movRegReg x23, x4
    arm64.loadRegBaseDisp.word64 x24, [x21 + 8]
    arm64.cmp x24, 352
    arm64.b.lt cxbad
  scbounded:
    arm64.cmp x24, 480
    arm64.b.gt cxbad
  scstepped:
    arm64.and x0, x24, 63
    arm64.cmp x0, 32
    arm64.b.ne cxbad
  cxcounter:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.cmp x0, 16
    arm64.b.ne cxbad
  cxdispatch:
    arm64.cmp x5, 1
    arm64.b.ne cxprobe1
  cxarm1:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x20, x0
    arm64.b.hs cxbad
  cxspan1:
    arm64.sub x0, x0, x20
    arm64.cmp x0, 16
    arm64.b.lt cxbad
  cxcrypt1:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.lsr x0, x24, 5
    arm64.sub x0, x0, 1
    arm64.lsl x1, x23, 8
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.add x3, x3, x20
    arm64.bl mrt_aes_ctr_blocks_1
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
  cxprobe1:
    arm64.cmp x5, 2
    arm64.b.ne cxprobe2
  cxarm2:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x20, x0
    arm64.b.hs cxbad
  cxspan2:
    arm64.sub x0, x0, x20
    arm64.cmp x0, 32
    arm64.b.lt cxbad
  cxcrypt2:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.lsr x0, x24, 5
    arm64.sub x0, x0, 1
    arm64.lsl x1, x23, 8
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.add x3, x3, x20
    arm64.bl mrt_aes_ctr_blocks_2
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
  cxprobe2:
    arm64.cmp x5, 4
    arm64.b.ne cxprobe4
  cxarm4:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x20, x0
    arm64.b.hs cxbad
  cxspan4:
    arm64.sub x0, x0, x20
    arm64.cmp x0, 64
    arm64.b.lt cxbad
  cxcrypt4:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.lsr x0, x24, 5
    arm64.sub x0, x0, 1
    arm64.lsl x1, x23, 8
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.add x3, x3, x20
    arm64.bl mrt_aes_ctr_blocks_4
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
  cxprobe4:
    arm64.cmp x5, 8
    arm64.b.ne cxbad
  cxarm8:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x20, x0
    arm64.b.hs cxbad
  cxspan8:
    arm64.sub x0, x0, x20
    arm64.cmp x0, 128
    arm64.b.lt cxbad
  cxcrypt8:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.lsr x0, x24, 5
    arm64.sub x0, x0, 1
    arm64.lsl x1, x23, 8
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.add x3, x3, x20
    arm64.bl mrt_aes_ctr_blocks_8
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
  cxbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
}

chunk @mrt_aes_ctr_blocks_4 {
    0000: e4 1f 80 d2 04 00 04 8a 00 fc 48 d3 45 0c 40 b9
    0010: a5 08 c0 5a a5 00 00 8b 40 00 c0 3d a7 00 00 91
    0020: e7 08 c0 5a e0 1c 1c 4e 41 00 c0 3d a7 04 00 91
    0030: e7 08 c0 5a e1 1c 1c 4e 42 00 c0 3d a7 08 00 91
    0040: e7 08 c0 5a e2 1c 1c 4e 43 00 c0 3d a7 0c 00 91
    0050: e7 08 c0 5a e3 1c 1c 4e e6 03 01 aa 9f 30 00 f1
    0060: 03 05 00 54 80 02 00 54 d0 00 c0 3d 00 4a 28 4e
    0070: 00 68 28 4e 01 4a 28 4e 21 68 28 4e 02 4a 28 4e
    0080: 42 68 28 4e 03 4a 28 4e 63 68 28 4e d0 04 c0 3d
    0090: 00 4a 28 4e 00 68 28 4e 01 4a 28 4e 21 68 28 4e
    00a0: 02 4a 28 4e 42 68 28 4e 03 4a 28 4e 63 68 28 4e
    00b0: c6 80 00 91 d0 00 c0 3d 00 4a 28 4e 00 68 28 4e
    00c0: 01 4a 28 4e 21 68 28 4e 02 4a 28 4e 42 68 28 4e
    00d0: 03 4a 28 4e 63 68 28 4e d0 04 c0 3d 00 4a 28 4e
    00e0: 00 68 28 4e 01 4a 28 4e 21 68 28 4e 02 4a 28 4e
    00f0: 42 68 28 4e 03 4a 28 4e 63 68 28 4e c6 80 00 91
    0100: d0 00 c0 3d 00 4a 28 4e 00 68 28 4e 01 4a 28 4e
    0110: 21 68 28 4e 02 4a 28 4e 42 68 28 4e 03 4a 28 4e
    0120: 63 68 28 4e d0 04 c0 3d 00 4a 28 4e 00 68 28 4e
    0130: 01 4a 28 4e 21 68 28 4e 02 4a 28 4e 42 68 28 4e
    0140: 03 4a 28 4e 63 68 28 4e d0 08 c0 3d 00 4a 28 4e
    0150: 00 68 28 4e 01 4a 28 4e 21 68 28 4e 02 4a 28 4e
    0160: 42 68 28 4e 03 4a 28 4e 63 68 28 4e d0 0c c0 3d
    0170: 00 4a 28 4e 00 68 28 4e 01 4a 28 4e 21 68 28 4e
    0180: 02 4a 28 4e 42 68 28 4e 03 4a 28 4e 63 68 28 4e
    0190: d0 10 c0 3d 00 4a 28 4e 00 68 28 4e 01 4a 28 4e
    01a0: 21 68 28 4e 02 4a 28 4e 42 68 28 4e 03 4a 28 4e
    01b0: 63 68 28 4e d0 14 c0 3d 00 4a 28 4e 00 68 28 4e
    01c0: 01 4a 28 4e 21 68 28 4e 02 4a 28 4e 42 68 28 4e
    01d0: 03 4a 28 4e 63 68 28 4e d0 18 c0 3d 00 4a 28 4e
    01e0: 00 68 28 4e 01 4a 28 4e 21 68 28 4e 02 4a 28 4e
    01f0: 42 68 28 4e 03 4a 28 4e 63 68 28 4e d0 1c c0 3d
    0200: 00 4a 28 4e 00 68 28 4e 01 4a 28 4e 21 68 28 4e
    0210: 02 4a 28 4e 42 68 28 4e 03 4a 28 4e 63 68 28 4e
    0220: d0 20 c0 3d 00 4a 28 4e 00 68 28 4e 01 4a 28 4e
    0230: 21 68 28 4e 02 4a 28 4e 42 68 28 4e 03 4a 28 4e
    0240: 63 68 28 4e d0 24 c0 3d 00 4a 28 4e 01 4a 28 4e
    0250: 02 4a 28 4e 03 4a 28 4e d0 28 c0 3d 00 1c 30 6e
    0260: 21 1c 30 6e 42 1c 30 6e 63 1c 30 6e 71 00 c0 3d
    0270: 31 1e 20 6e 71 00 80 3d 71 04 c0 3d 31 1e 21 6e
    0280: 71 04 80 3d 71 08 c0 3d 31 1e 22 6e 71 08 80 3d
    0290: 71 0c c0 3d 31 1e 23 6e 71 0c 80 3d e0 03 1f aa
    02a0: c0 03 5f d6
}
```

<!-- test: aes-hardware.a-galois-mode-call-is-one-checked-entry-and-one-routine-call -->
<!-- tier: stdlib -->
`__Builtins.aesGcmInit`, `aesGcmHash`, `aesGcmEncrypt`, `aesGcmDecrypt` and `aesGcmFinish` each become a call to a checked runtime
entry (`__cpufeat_aes_cipher_gcm_*`), which tests the key block's size, the message block's size and the span once and makes
one call to the hand-assembled `mrt_aes_gcm_*` routine with the control word or length, the key block, the message block and the
data. The entries' bodies are the Target IR below; the routines are hex dumps of the assembler's own bytes, still unlinked.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var schedule = ByteArray.create()
	var keyBlock = ByteArray.create()
	var message = ByteArray.create()
	var buffer = ByteArray.create()
	key.resize(16)
	schedule.resize(352)
	keyBlock.resize(432)
	message.resize(48)
	buffer.resize(64)
	__Builtins.aesExpandKey(schedule, key)
	__Builtins.aesGcmInit(keyBlock, schedule)
	__Builtins.aesGcmHash(message, keyBlock, buffer, 0, 32)
	__Builtins.aesGcmEncrypt(buffer, 0, keyBlock, message, 64)
	__Builtins.aesGcmDecrypt(buffer, 0, keyBlock, message, 64)
	__Builtins.aesGcmFinish(message, keyBlock, 32, 64)
	print("{message.count()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
48
```
```RequiredRuntime
__cpufeat_aes_cipher_gcm_init
__cpufeat_aes_cipher_gcm_hash
__cpufeat_aes_cipher_gcm_encrypt
__cpufeat_aes_cipher_gcm_decrypt
__cpufeat_aes_cipher_gcm_finish
mrt_aes_gcm_init
mrt_aes_gcm_hash
mrt_aes_gcm_encrypt
mrt_aes_gcm_decrypt
mrt_aes_gcm_finish
```

```TargetIr:x64-windows
data {
  __mrt_cpu_aes_usable@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_console_probe_stdin@32 = i8 0
  __mrt_console_probe_stdout@33 = i8 0
  __mrt_console_probe_stderr@34 = i8 0
  __mrt_program_started@35 = i8 0
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
    x64.prologue 104
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r14, r8
    x64.storeSlotReg slot3, r14
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r15, r8
    x64.storeSlotReg slot4, r15
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 352
    x64.movRegReg rcx, r12
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 432
    x64.movRegReg rcx, r13
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 48
    x64.movRegReg rcx, r14
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 64
    x64.movRegReg rcx, r15
    x64.callDirect __managed_resize
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __cpufeat_aes_cipher_expand_key
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r12
    x64.callDirect __cpufeat_aes_cipher_gcm_init
    x64.movRegImm32 r9, 0
    x64.movRegImm32 rsi, 32
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r15
    x64.callDirect __cpufeat_aes_cipher_gcm_hash
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rsi, 64
    x64.movRegReg rcx, r15
    x64.movRegReg rax, r13
    x64.movRegReg r9, r14
    x64.callDirect __cpufeat_aes_cipher_gcm_encrypt
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rsi, 64
    x64.movRegReg rcx, r15
    x64.movRegReg rax, r13
    x64.movRegReg r9, r14
    x64.callDirect __cpufeat_aes_cipher_gcm_decrypt
    x64.movRegImm32 rax, 32
    x64.movRegImm32 r9, 64
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r13
    x64.callDirect __cpufeat_aes_cipher_gcm_finish
    x64.loadRegBaseDisp.word64 rbx, [r14 + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot5, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot6, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot7, r8
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
    x64.loadRegSlot rax, slot7
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot6
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot5
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot7
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot7
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot4
    x64.callDirect __managed_decref
  exitdrop#2:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __managed_decref
  exitdrop#3:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __managed_decref
  exitdrop#4:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __managed_decref
  exitdrop#5:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_init {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.loadRegBaseDisp.word64 r13, [r12 + 8]
    x64.cmpRegImm32 r13, 352
    x64.jcc less, gibad
  scbounded:
    x64.cmpRegImm32 r13, 480
    x64.jcc greater, gibad
  scstepped:
    x64.movRegReg rax, r13
    x64.andRegImm32 rax, r13, 63
    x64.cmpRegImm32 rax, 32
    x64.jcc notEqual, gibad
  gikeyblock:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.movRegReg rcx, r13
    x64.shrRegImm8 rcx, r13, 1
    x64.leaRegRegImm32 rcx, rcx, 256
    x64.cmpRegReg rax, rcx
    x64.jcc notEqual, gibad
  gicrypt:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.shrRegImm8 r13, r13, 5
    x64.leaRegRegImm32 rcx, r13, -1
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.loadRegBaseDisp.word64 r8, [r12 + 0]
    x64.movRegImm32 r9, 0
    x64.callDirect mrt_aes_gcm_init
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  gibad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_hash {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.movRegReg r13, rax
    x64.movRegReg r14, r9
    x64.movRegReg r15, rsi
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegImm32 rax, 432
    x64.jcc less, ghbad
  kbbounded:
    x64.cmpRegImm32 rax, 496
    x64.jcc greater, ghbad
  kbstepped:
    x64.andRegImm32 rax, rax, 31
    x64.cmpRegImm32 rax, 16
    x64.jcc notEqual, ghbad
  gkmessage:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 48
    x64.jcc notEqual, ghbad
  ghspan:
    x64.movRegReg rax, r15
    x64.andRegImm32 rax, r15, 15
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, ghbad
  ghaligned:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.cmpRegReg r14, rax
    x64.jcc above, ghbad
  ghinside:
    x64.subRegReg rax, rax, r14
    x64.cmpRegReg rax, r15
    x64.jcc below, ghbad
  ghcrypt:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.loadRegBaseDisp.word64 rdx, [r12 + 0]
    x64.loadRegBaseDisp.word64 r8, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.leaRegRegReg r9, rax, r14
    x64.movRegReg rcx, r15
    x64.callDirect mrt_aes_gcm_hash
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ghbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_encrypt {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.storeSlotReg slot0, rdx
    x64.movRegReg r12, rax
    x64.movRegReg r13, r9
    x64.movRegReg r14, rsi
    x64.loadRegBaseDisp.word64 r15, [r12 + 8]
    x64.cmpRegImm32 r15, 432
    x64.jcc less, gcbad
  kbbounded:
    x64.cmpRegImm32 r15, 496
    x64.jcc greater, gcbad
  kbstepped:
    x64.movRegReg rax, r15
    x64.andRegImm32 rax, r15, 31
    x64.cmpRegImm32 rax, 16
    x64.jcc notEqual, gcbad
  gkmessage:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.cmpRegImm32 rax, 48
    x64.jcc notEqual, gcbad
  gcspan:
    x64.movRegReg rax, r14
    x64.andRegImm32 rax, r14, 15
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, gcbad
  gcaligned:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc above, gcbad
  gcinside:
    x64.subRegReg rax, rax, rdx
    x64.cmpRegReg rax, r14
    x64.jcc below, gcbad
  gccrypt:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cow_detach
    x64.leaRegRegImm32 rax, r15, -256
    x64.shrRegImm8 rax, rax, 4
    x64.leaRegRegImm32 rax, rax, -1
    x64.shlRegImm8 r14, r14, 8
    x64.movRegReg rcx, r14
    x64.orRegReg rcx, r14, rax
    x64.loadRegBaseDisp.word64 rdx, [r12 + 0]
    x64.loadRegBaseDisp.word64 r8, [r13 + 0]
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegSlot rsi, slot0
    x64.leaRegRegReg r9, rax, rsi
    x64.callDirect mrt_aes_gcm_encrypt
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  gcbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_decrypt {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.storeSlotReg slot0, rdx
    x64.movRegReg r12, rax
    x64.movRegReg r13, r9
    x64.movRegReg r14, rsi
    x64.loadRegBaseDisp.word64 r15, [r12 + 8]
    x64.cmpRegImm32 r15, 432
    x64.jcc less, gcbad
  kbbounded:
    x64.cmpRegImm32 r15, 496
    x64.jcc greater, gcbad
  kbstepped:
    x64.movRegReg rax, r15
    x64.andRegImm32 rax, r15, 31
    x64.cmpRegImm32 rax, 16
    x64.jcc notEqual, gcbad
  gkmessage:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.cmpRegImm32 rax, 48
    x64.jcc notEqual, gcbad
  gcspan:
    x64.movRegReg rax, r14
    x64.andRegImm32 rax, r14, 15
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, gcbad
  gcaligned:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc above, gcbad
  gcinside:
    x64.subRegReg rax, rax, rdx
    x64.cmpRegReg rax, r14
    x64.jcc below, gcbad
  gccrypt:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cow_detach
    x64.leaRegRegImm32 rax, r15, -256
    x64.shrRegImm8 rax, rax, 4
    x64.leaRegRegImm32 rax, rax, -1
    x64.shlRegImm8 r14, r14, 8
    x64.movRegReg rcx, r14
    x64.orRegReg rcx, r14, rax
    x64.loadRegBaseDisp.word64 rdx, [r12 + 0]
    x64.loadRegBaseDisp.word64 r8, [r13 + 0]
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegSlot rsi, slot0
    x64.leaRegRegReg r9, rax, rsi
    x64.callDirect mrt_aes_gcm_decrypt
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  gcbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_finish {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.movRegReg r13, rax
    x64.movRegReg r14, r9
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegImm32 rax, 432
    x64.jcc less, gfbad
  kbbounded:
    x64.cmpRegImm32 rax, 496
    x64.jcc greater, gfbad
  kbstepped:
    x64.andRegImm32 rax, rax, 31
    x64.cmpRegImm32 rax, 16
    x64.jcc notEqual, gfbad
  gkmessage:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 48
    x64.jcc notEqual, gfbad
  gfcrypt:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.loadRegBaseDisp.word64 rdx, [r12 + 0]
    x64.loadRegBaseDisp.word64 r8, [rbx + 0]
    x64.movRegReg rcx, r13
    x64.movRegReg r9, r14
    x64.callDirect mrt_aes_gcm_finish
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  gfbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

chunk @mrt_aes_gcm_init {
    0000: 55 48 89 e5 48 81 ec c0 00 00 00 f3 0f 7f b5 60
    0010: ff ff ff f3 0f 7f bd 70 ff ff ff f3 44 0f 7f 45
    0020: 80 f3 44 0f 7f 4d 90 f3 44 0f 7f 55 a0 f3 44 0f
    0030: 7f 5d b0 f3 44 0f 7f 65 c0 f3 44 0f 7f 6d d0 f3
    0040: 44 0f 7f 75 e0 f3 44 0f 7f 7d f0 4c 8d 8a 00 01
    0050: 00 00 4c 8d 51 01 f3 41 0f 6f 00 f3 41 0f 7f 01
    0060: 49 81 c0 10 00 00 00 49 81 c1 10 00 00 00 49 81
    0070: ea 01 00 00 00 0f 85 db ff ff ff 48 8d 05 62 02
    0080: 00 00 f3 44 0f 6f 38 48 8d 05 68 02 00 00 f3 44
    0090: 0f 6f 30 f3 0f 6f 82 00 01 00 00 f3 44 0f 6f 9a
    00a0: 10 01 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a 20
    00b0: 01 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a 30 01
    00c0: 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a 40 01 00
    00d0: 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a 50 01 00 00
    00e0: 66 41 0f 38 dc c3 f3 44 0f 6f 9a 60 01 00 00 66
    00f0: 41 0f 38 dc c3 f3 44 0f 6f 9a 70 01 00 00 66 41
    0100: 0f 38 dc c3 f3 44 0f 6f 9a 80 01 00 00 66 41 0f
    0110: 38 dc c3 f3 44 0f 6f 9a 90 01 00 00 66 41 0f 38
    0120: dc c3 f3 44 0f 6f 9a a0 01 00 00 48 81 f9 0c 00
    0130: 00 00 0f 82 42 00 00 00 66 41 0f 38 dc c3 f3 44
    0140: 0f 6f 9a b0 01 00 00 66 41 0f 38 dc c3 f3 44 0f
    0150: 6f 9a c0 01 00 00 0f 84 1e 00 00 00 66 41 0f 38
    0160: dc c3 f3 44 0f 6f 9a d0 01 00 00 66 41 0f 38 dc
    0170: c3 f3 44 0f 6f 9a e0 01 00 00 66 41 0f 38 dd c3
    0180: 66 41 0f 38 00 c7 66 44 0f 70 d8 ff 44 0f 28 e0
    0190: 66 41 0f 72 e3 1f 66 45 0f db de 66 41 0f 72 d4
    01a0: 1f 66 41 0f 73 fc 04 66 0f 72 f0 01 66 41 0f ef
    01b0: c3 66 41 0f ef c4 f3 0f 7f 82 e0 00 00 00 66 44
    01c0: 0f 70 d8 4e 66 44 0f ef d8 f3 44 0f 7f 9a f0 00
    01d0: 00 00 4c 8d 92 c0 00 00 00 45 31 db 49 81 c3 07
    01e0: 00 00 00 f3 44 0f 6f 82 e0 00 00 00 45 0f 28 c8
    01f0: f3 44 0f 6f 92 f0 00 00 00 66 44 0f 3a 44 c0 00
    0200: 66 44 0f 3a 44 c8 11 66 44 0f 70 d8 4e 66 44 0f
    0210: ef d8 66 45 0f 3a 44 d3 00 66 45 0f ef d0 66 45
    0220: 0f ef d1 45 0f 28 da 66 41 0f 73 da 08 66 41 0f
    0230: 73 fb 08 66 45 0f ef ca 66 45 0f ef c3 45 0f 28
    0240: de 66 45 0f 3a 44 d8 01 66 45 0f 70 c0 4e 66 45
    0250: 0f ef c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45
    0260: 0f 70 c0 4e 66 45 0f ef c3 66 45 0f ef c1 41 0f
    0270: 28 c0 f3 41 0f 7f 02 66 44 0f 70 d8 4e 66 44 0f
    0280: ef d8 f3 45 0f 7f 5a 10 49 81 eb 01 00 00 00 4d
    0290: 8d 52 e0 0f 85 4a ff ff ff f3 0f 6f b5 60 ff ff
    02a0: ff f3 0f 6f bd 70 ff ff ff f3 44 0f 6f 45 80 f3
    02b0: 44 0f 6f 4d 90 f3 44 0f 6f 55 a0 f3 44 0f 6f 5d
    02c0: b0 f3 44 0f 6f 65 c0 f3 44 0f 6f 6d d0 f3 44 0f
    02d0: 6f 75 e0 f3 44 0f 6f 7d f0 31 c0 48 81 c4 c0 00
    02e0: 00 00 5d c3 0f 0e 0d 0c 0b 0a 09 08 07 06 05 04
    02f0: 03 02 01 00 00 00 01 00 00 00 00 00 00 00 00 00
    0300: 00 00 00 00 00 c2 00
}

chunk @mrt_aes_gcm_hash {
    0000: 55 48 89 e5 48 81 ec c0 00 00 00 f3 0f 7f b5 60
    0010: ff ff ff f3 0f 7f bd 70 ff ff ff f3 44 0f 7f 45
    0020: 80 f3 44 0f 7f 4d 90 f3 44 0f 7f 55 a0 f3 44 0f
    0030: 7f 5d b0 f3 44 0f 7f 65 c0 f3 44 0f 7f 6d d0 f3
    0040: 44 0f 7f 75 e0 f3 44 0f 7f 7d f0 48 8d 05 04 04
    0050: 00 00 f3 44 0f 6f 38 48 8d 05 0a 04 00 00 f3 44
    0060: 0f 6f 30 f3 45 0f 6f 00 48 85 c9 0f 84 94 03 00
    0070: 00 48 81 f9 80 00 00 00 0f 82 cc 02 00 00 48 81
    0080: e9 80 00 00 00 f3 41 0f 6f 01 f3 41 0f 6f 49 10
    0090: f3 41 0f 6f 51 20 f3 41 0f 6f 59 30 f3 41 0f 6f
    00a0: 61 40 f3 41 0f 6f 69 50 f3 41 0f 6f 71 60 f3 41
    00b0: 0f 6f 79 70 49 81 c1 80 00 00 00 66 41 0f 38 00
    00c0: c7 66 41 0f 38 00 cf 66 41 0f 38 00 d7 66 41 0f
    00d0: 38 00 df 66 41 0f 38 00 e7 66 41 0f 38 00 ef 66
    00e0: 41 0f 38 00 f7 66 41 0f 38 00 ff 66 41 0f ef c0
    00f0: f3 44 0f 6f 02 f3 44 0f 6f 52 10 45 0f 28 c8 66
    0100: 44 0f 70 e0 4e 66 44 0f ef e0 66 45 0f 3a 44 d4
    0110: 00 66 44 0f 3a 44 c0 00 66 44 0f 3a 44 c8 11 f3
    0120: 44 0f 6f 62 20 45 0f 28 ec 66 44 0f 3a 44 e1 00
    0130: 66 45 0f ef c4 66 44 0f 3a 44 e9 11 66 45 0f ef
    0140: cd 66 44 0f 70 e1 4e 66 41 0f ef cc f3 44 0f 6f
    0150: 62 30 66 44 0f 3a 44 e1 00 66 45 0f ef d4 f3 44
    0160: 0f 6f 62 40 45 0f 28 ec 66 44 0f 3a 44 e2 00 66
    0170: 45 0f ef c4 66 44 0f 3a 44 ea 11 66 45 0f ef cd
    0180: 66 44 0f 70 e2 4e 66 41 0f ef d4 f3 44 0f 6f 62
    0190: 50 66 44 0f 3a 44 e2 00 66 45 0f ef d4 f3 44 0f
    01a0: 6f 62 60 45 0f 28 ec 66 44 0f 3a 44 e3 00 66 45
    01b0: 0f ef c4 66 44 0f 3a 44 eb 11 66 45 0f ef cd 66
    01c0: 44 0f 70 e3 4e 66 41 0f ef dc f3 44 0f 6f 62 70
    01d0: 66 44 0f 3a 44 e3 00 66 45 0f ef d4 f3 44 0f 6f
    01e0: a2 80 00 00 00 45 0f 28 ec 66 44 0f 3a 44 e4 00
    01f0: 66 45 0f ef c4 66 44 0f 3a 44 ec 11 66 45 0f ef
    0200: cd 66 44 0f 70 e4 4e 66 41 0f ef e4 f3 44 0f 6f
    0210: a2 90 00 00 00 66 44 0f 3a 44 e4 00 66 45 0f ef
    0220: d4 f3 44 0f 6f a2 a0 00 00 00 45 0f 28 ec 66 44
    0230: 0f 3a 44 e5 00 66 45 0f ef c4 66 44 0f 3a 44 ed
    0240: 11 66 45 0f ef cd 66 44 0f 70 e5 4e 66 41 0f ef
    0250: ec f3 44 0f 6f a2 b0 00 00 00 66 44 0f 3a 44 e5
    0260: 00 66 45 0f ef d4 f3 44 0f 6f a2 c0 00 00 00 45
    0270: 0f 28 ec 66 44 0f 3a 44 e6 00 66 45 0f ef c4 66
    0280: 44 0f 3a 44 ee 11 66 45 0f ef cd 66 44 0f 70 e6
    0290: 4e 66 41 0f ef f4 f3 44 0f 6f a2 d0 00 00 00 66
    02a0: 44 0f 3a 44 e6 00 66 45 0f ef d4 f3 44 0f 6f a2
    02b0: e0 00 00 00 45 0f 28 ec 66 44 0f 3a 44 e7 00 66
    02c0: 45 0f ef c4 66 44 0f 3a 44 ef 11 66 45 0f ef cd
    02d0: 66 44 0f 70 e7 4e 66 41 0f ef fc f3 44 0f 6f a2
    02e0: f0 00 00 00 66 44 0f 3a 44 e7 00 66 45 0f ef d4
    02f0: 66 45 0f ef d0 66 45 0f ef d1 45 0f 28 da 66 41
    0300: 0f 73 da 08 66 41 0f 73 fb 08 66 45 0f ef ca 66
    0310: 45 0f ef c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66
    0320: 45 0f 70 c0 4e 66 45 0f ef c3 45 0f 28 de 66 45
    0330: 0f 3a 44 d8 01 66 45 0f 70 c0 4e 66 45 0f ef c3
    0340: 66 45 0f ef c1 e9 27 fd ff ff 48 81 f9 10 00 00
    0350: 00 0f 82 ae 00 00 00 48 81 e9 10 00 00 00 f3 41
    0360: 0f 6f 01 66 41 0f 38 00 c7 66 41 0f ef c0 f3 44
    0370: 0f 6f 82 e0 00 00 00 45 0f 28 c8 f3 44 0f 6f 92
    0380: f0 00 00 00 66 44 0f 3a 44 c0 00 66 44 0f 3a 44
    0390: c8 11 66 44 0f 70 d8 4e 66 44 0f ef d8 66 45 0f
    03a0: 3a 44 d3 00 66 45 0f ef d0 66 45 0f ef d1 45 0f
    03b0: 28 da 66 41 0f 73 da 08 66 41 0f 73 fb 08 66 45
    03c0: 0f ef ca 66 45 0f ef c3 45 0f 28 de 66 45 0f 3a
    03d0: 44 d8 01 66 45 0f 70 c0 4e 66 45 0f ef c3 45 0f
    03e0: 28 de 66 45 0f 3a 44 d8 01 66 45 0f 70 c0 4e 66
    03f0: 45 0f ef c3 66 45 0f ef c1 49 81 c1 10 00 00 00
    0400: e9 45 ff ff ff f3 45 0f 7f 00 f3 0f 6f b5 60 ff
    0410: ff ff f3 0f 6f bd 70 ff ff ff f3 44 0f 6f 45 80
    0420: f3 44 0f 6f 4d 90 f3 44 0f 6f 55 a0 f3 44 0f 6f
    0430: 5d b0 f3 44 0f 6f 65 c0 f3 44 0f 6f 6d d0 f3 44
    0440: 0f 6f 75 e0 f3 44 0f 6f 7d f0 31 c0 48 81 c4 c0
    0450: 00 00 00 5d c3 00 0f 0e 0d 0c 0b 0a 09 08 07 06
    0460: 05 04 03 02 01 00 00 00 01 00 00 00 00 00 00 00
    0470: 00 00 00 00 00 00 00 c2 00
}

chunk @mrt_aes_gcm_encrypt {
    0000: 55 48 89 e5 48 81 ec c0 01 00 00 f3 0f 7f b5 60
    0010: ff ff ff f3 0f 7f bd 70 ff ff ff f3 44 0f 7f 45
    0020: 80 f3 44 0f 7f 4d 90 f3 44 0f 7f 55 a0 f3 44 0f
    0030: 7f 5d b0 f3 44 0f 7f 65 c0 f3 44 0f 7f 6d d0 f3
    0040: 44 0f 7f 75 e0 f3 44 0f 7f 7d f0 49 89 ca 49 c1
    0050: ea 08 49 89 cb 49 81 e3 ff 00 00 00 48 8d 0d c7
    0060: 12 00 00 f3 44 0f 6f 39 48 8d 0d cd 12 00 00 f3
    0070: 44 0f 6f 31 f3 45 0f 6f 00 66 45 0f ef c9 66 45
    0080: 0f ef d2 f3 41 0f 6f 40 10 41 8b 40 1c f3 44 0f
    0090: 6f 9a 00 01 00 00 8b ba 0c 01 00 00 0f c8 0f cf
    00a0: 4c 89 d6 48 c1 ee 04 48 01 c6 0f ce 41 89 70 1c
    00b0: 66 44 0f ef d8 f3 44 0f 7f 9d e0 fe ff ff 48 81
    00c0: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 ec fe ff
    00d0: ff 49 81 fa 80 00 00 00 0f 82 44 10 00 00 49 81
    00e0: ea 80 00 00 00 f3 44 0f 7f 9d f0 fe ff ff 48 81
    00f0: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 fc fe ff
    0100: ff f3 44 0f 7f 9d 00 ff ff ff 48 81 c0 01 00 00
    0110: 00 89 c6 31 fe 0f ce 89 b5 0c ff ff ff f3 44 0f
    0120: 7f 9d 10 ff ff ff 48 81 c0 01 00 00 00 89 c6 31
    0130: fe 0f ce 89 b5 1c ff ff ff f3 44 0f 7f 9d 20 ff
    0140: ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0150: b5 2c ff ff ff f3 44 0f 7f 9d 30 ff ff ff 48 81
    0160: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 3c ff ff
    0170: ff f3 44 0f 7f 9d 40 ff ff ff 48 81 c0 01 00 00
    0180: 00 89 c6 31 fe 0f ce 89 b5 4c ff ff ff f3 44 0f
    0190: 7f 9d 50 ff ff ff 48 81 c0 01 00 00 00 89 c6 31
    01a0: fe 0f ce 89 b5 5c ff ff ff f3 0f 6f 85 e0 fe ff
    01b0: ff f3 0f 6f 8d f0 fe ff ff f3 0f 6f 95 00 ff ff
    01c0: ff f3 0f 6f 9d 10 ff ff ff f3 0f 6f a5 20 ff ff
    01d0: ff f3 0f 6f ad 30 ff ff ff f3 0f 6f b5 40 ff ff
    01e0: ff f3 0f 6f bd 50 ff ff ff f3 44 0f 6f 9a 10 01
    01f0: 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41
    0200: 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3
    0210: 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38
    0220: dc fb 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0230: b5 ec fe ff ff f3 44 0f 6f 9a 20 01 00 00 66 41
    0240: 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3
    0250: 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38
    0260: dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 48 81
    0270: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 fc fe ff
    0280: ff f3 44 0f 6f 9a 30 01 00 00 66 41 0f 38 dc c3
    0290: 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38
    02a0: dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41
    02b0: 0f 38 dc f3 66 41 0f 38 dc fb 48 81 c0 01 00 00
    02c0: 00 89 c6 31 fe 0f ce 89 b5 0c ff ff ff f3 44 0f
    02d0: 6f 9a 40 01 00 00 66 41 0f 38 dc c3 66 41 0f 38
    02e0: dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41
    02f0: 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3
    0300: 66 41 0f 38 dc fb 48 81 c0 01 00 00 00 89 c6 31
    0310: fe 0f ce 89 b5 1c ff ff ff f3 44 0f 6f 9a 50 01
    0320: 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41
    0330: 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3
    0340: 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38
    0350: dc fb 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0360: b5 2c ff ff ff f3 44 0f 6f 9a 60 01 00 00 66 41
    0370: 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3
    0380: 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38
    0390: dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 48 81
    03a0: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 3c ff ff
    03b0: ff f3 44 0f 6f 9a 70 01 00 00 66 41 0f 38 dc c3
    03c0: 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38
    03d0: dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41
    03e0: 0f 38 dc f3 66 41 0f 38 dc fb 48 81 c0 01 00 00
    03f0: 00 89 c6 31 fe 0f ce 89 b5 4c ff ff ff f3 44 0f
    0400: 6f 9a 80 01 00 00 66 41 0f 38 dc c3 66 41 0f 38
    0410: dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41
    0420: 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3
    0430: 66 41 0f 38 dc fb 48 81 c0 01 00 00 00 89 c6 31
    0440: fe 0f ce 89 b5 5c ff ff ff f3 44 0f 6f 9a 90 01
    0450: 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41
    0460: 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3
    0470: 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38
    0480: dc fb f3 44 0f 6f 9a a0 01 00 00 49 81 fb 0c 00
    0490: 00 00 0f 82 ea 00 00 00 66 41 0f 38 dc c3 66 41
    04a0: 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db
    04b0: 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38
    04c0: dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9a b0 01 00
    04d0: 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f
    04e0: 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66
    04f0: 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc
    0500: fb f3 44 0f 6f 9a c0 01 00 00 0f 84 72 00 00 00
    0510: 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38
    0520: dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41
    0530: 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb
    0540: f3 44 0f 6f 9a d0 01 00 00 66 41 0f 38 dc c3 66
    0550: 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc
    0560: db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f
    0570: 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9a e0 01
    0580: 00 00 66 41 0f 38 dd c3 66 41 0f 38 dd cb 66 41
    0590: 0f 38 dd d3 66 41 0f 38 dd db 66 41 0f 38 dd e3
    05a0: 66 41 0f 38 dd eb 66 41 0f 38 dd f3 66 41 0f 38
    05b0: dd fb f3 45 0f 6f 19 66 41 0f ef c3 f3 45 0f 6f
    05c0: 59 10 66 41 0f ef cb f3 45 0f 6f 59 20 66 41 0f
    05d0: ef d3 f3 45 0f 6f 59 30 66 41 0f ef db f3 45 0f
    05e0: 6f 59 40 66 41 0f ef e3 f3 45 0f 6f 59 50 66 41
    05f0: 0f ef eb f3 45 0f 6f 59 60 66 41 0f ef f3 f3 45
    0600: 0f 6f 59 70 66 41 0f ef fb f3 41 0f 7f 01 66 41
    0610: 0f 38 00 c7 66 41 0f ef c0 f3 41 0f 7f 49 10 66
    0620: 41 0f 38 00 cf f3 41 0f 7f 51 20 66 41 0f 38 00
    0630: d7 f3 41 0f 7f 59 30 66 41 0f 38 00 df f3 41 0f
    0640: 7f 61 40 66 41 0f 38 00 e7 f3 41 0f 7f 69 50 66
    0650: 41 0f 38 00 ef f3 41 0f 7f 71 60 66 41 0f 38 00
    0660: f7 f3 41 0f 7f 79 70 66 41 0f 38 00 ff f3 0f 7f
    0670: 85 60 fe ff ff f3 0f 7f 8d 70 fe ff ff f3 0f 7f
    0680: 95 80 fe ff ff f3 0f 7f 9d 90 fe ff ff f3 0f 7f
    0690: a5 a0 fe ff ff f3 0f 7f ad b0 fe ff ff f3 0f 7f
    06a0: b5 c0 fe ff ff f3 0f 7f bd d0 fe ff ff 49 81 c1
    06b0: 80 00 00 00 49 81 fa 80 00 00 00 0f 82 b4 07 00
    06c0: 00 49 81 ea 80 00 00 00 f3 0f 6f 85 e0 fe ff ff
    06d0: f3 0f 6f 8d f0 fe ff ff f3 0f 6f 95 00 ff ff ff
    06e0: f3 0f 6f 9d 10 ff ff ff f3 0f 6f a5 20 ff ff ff
    06f0: f3 0f 6f ad 30 ff ff ff f3 0f 6f b5 40 ff ff ff
    0700: f3 0f 6f bd 50 ff ff ff f3 44 0f 6f 9d 60 fe ff
    0710: ff f3 44 0f 6f 02 f3 44 0f 6f 52 10 45 0f 28 c8
    0720: 66 45 0f 70 e3 4e 66 45 0f ef e3 66 45 0f 3a 44
    0730: d4 00 66 45 0f 3a 44 c3 00 66 45 0f 3a 44 cb 11
    0740: f3 44 0f 6f 9a 10 01 00 00 66 41 0f 38 dc c3 66
    0750: 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc
    0760: db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f
    0770: 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9d 70 fe
    0780: ff ff f3 44 0f 6f 62 20 45 0f 28 ec 66 45 0f 3a
    0790: 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66
    07a0: 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3
    07b0: 44 0f 6f 62 30 66 45 0f 3a 44 e3 00 66 45 0f ef
    07c0: d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    07d0: ec fe ff ff f3 44 0f 6f 9a 20 01 00 00 66 41 0f
    07e0: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    07f0: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    0800: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f
    0810: 6f 9d 80 fe ff ff f3 44 0f 6f 62 40 45 0f 28 ec
    0820: 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a
    0830: 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45
    0840: 0f ef dc f3 44 0f 6f 62 50 66 45 0f 3a 44 e3 00
    0850: 66 45 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe
    0860: 0f ce 89 b5 fc fe ff ff f3 44 0f 6f 9a 30 01 00
    0870: 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f
    0880: 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66
    0890: 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc
    08a0: fb f3 44 0f 6f 9d 90 fe ff ff f3 44 0f 6f 62 60
    08b0: 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4
    08c0: 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70
    08d0: e3 4e 66 45 0f ef dc f3 44 0f 6f 62 70 66 45 0f
    08e0: 3a 44 e3 00 66 45 0f ef d4 48 81 c0 01 00 00 00
    08f0: 89 c6 31 fe 0f ce 89 b5 0c ff ff ff f3 44 0f 6f
    0900: 9a 40 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc
    0910: cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f
    0920: 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66
    0930: 41 0f 38 dc fb f3 44 0f 6f 9d a0 fe ff ff f3 44
    0940: 0f 6f a2 80 00 00 00 45 0f 28 ec 66 45 0f 3a 44
    0950: e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45
    0960: 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44
    0970: 0f 6f a2 90 00 00 00 66 45 0f 3a 44 e3 00 66 45
    0980: 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce
    0990: 89 b5 1c ff ff ff f3 44 0f 6f 9a 50 01 00 00 66
    09a0: 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc
    09b0: d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f
    09c0: 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3
    09d0: 44 0f 6f 9d b0 fe ff ff f3 44 0f 6f a2 a0 00 00
    09e0: 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef
    09f0: c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f
    0a00: 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 b0 00 00
    0a10: 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81 c0
    0a20: 01 00 00 00 89 c6 31 fe 0f ce 89 b5 2c ff ff ff
    0a30: f3 44 0f 6f 9a 60 01 00 00 66 41 0f 38 dc c3 66
    0a40: 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc
    0a50: db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f
    0a60: 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9d c0 fe
    0a70: ff ff f3 44 0f 6f a2 c0 00 00 00 45 0f 28 ec 66
    0a80: 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44
    0a90: eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f
    0aa0: ef dc f3 44 0f 6f a2 d0 00 00 00 66 45 0f 3a 44
    0ab0: e3 00 66 45 0f ef d4 48 81 c0 01 00 00 00 89 c6
    0ac0: 31 fe 0f ce 89 b5 3c ff ff ff f3 44 0f 6f 9a 70
    0ad0: 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66
    0ae0: 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc
    0af0: e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f
    0b00: 38 dc fb f3 44 0f 6f 9d d0 fe ff ff f3 44 0f 6f
    0b10: a2 e0 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00
    0b20: 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef
    0b30: cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f
    0b40: a2 f0 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef
    0b50: d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    0b60: 4c ff ff ff f3 44 0f 6f 9a 80 01 00 00 66 41 0f
    0b70: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    0b80: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    0b90: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 48 81 c0
    0ba0: 01 00 00 00 89 c6 31 fe 0f ce 89 b5 5c ff ff ff
    0bb0: 66 45 0f ef d0 66 45 0f ef d1 45 0f 28 da 66 41
    0bc0: 0f 73 da 08 66 41 0f 73 fb 08 66 45 0f ef ca 66
    0bd0: 45 0f ef c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66
    0be0: 45 0f 70 c0 4e 66 45 0f ef c3 f3 44 0f 6f 9a 90
    0bf0: 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66
    0c00: 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc
    0c10: e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f
    0c20: 38 dc fb 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45
    0c30: 0f 70 c0 4e 66 45 0f ef c3 66 45 0f ef c1 f3 44
    0c40: 0f 6f 9a a0 01 00 00 49 81 fb 0c 00 00 00 0f 82
    0c50: ea 00 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb
    0c60: 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38
    0c70: dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41
    0c80: 0f 38 dc fb f3 44 0f 6f 9a b0 01 00 00 66 41 0f
    0c90: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    0ca0: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    0cb0: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f
    0cc0: 6f 9a c0 01 00 00 0f 84 72 00 00 00 66 41 0f 38
    0cd0: dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41
    0ce0: 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb
    0cf0: 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f
    0d00: 9a d0 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc
    0d10: cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f
    0d20: 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66
    0d30: 41 0f 38 dc fb f3 44 0f 6f 9a e0 01 00 00 66 41
    0d40: 0f 38 dd c3 66 41 0f 38 dd cb 66 41 0f 38 dd d3
    0d50: 66 41 0f 38 dd db 66 41 0f 38 dd e3 66 41 0f 38
    0d60: dd eb 66 41 0f 38 dd f3 66 41 0f 38 dd fb f3 45
    0d70: 0f 6f 19 66 41 0f ef c3 f3 45 0f 6f 59 10 66 41
    0d80: 0f ef cb f3 45 0f 6f 59 20 66 41 0f ef d3 f3 45
    0d90: 0f 6f 59 30 66 41 0f ef db f3 45 0f 6f 59 40 66
    0da0: 41 0f ef e3 f3 45 0f 6f 59 50 66 41 0f ef eb f3
    0db0: 45 0f 6f 59 60 66 41 0f ef f3 f3 45 0f 6f 59 70
    0dc0: 66 41 0f ef fb f3 41 0f 7f 01 66 41 0f 38 00 c7
    0dd0: 66 41 0f ef c0 f3 41 0f 7f 49 10 66 41 0f 38 00
    0de0: cf f3 41 0f 7f 51 20 66 41 0f 38 00 d7 f3 41 0f
    0df0: 7f 59 30 66 41 0f 38 00 df f3 41 0f 7f 61 40 66
    0e00: 41 0f 38 00 e7 f3 41 0f 7f 69 50 66 41 0f 38 00
    0e10: ef f3 41 0f 7f 71 60 66 41 0f 38 00 f7 f3 41 0f
    0e20: 7f 79 70 66 41 0f 38 00 ff f3 0f 7f 85 60 fe ff
    0e30: ff f3 0f 7f 8d 70 fe ff ff f3 0f 7f 95 80 fe ff
    0e40: ff f3 0f 7f 9d 90 fe ff ff f3 0f 7f a5 a0 fe ff
    0e50: ff f3 0f 7f ad b0 fe ff ff f3 0f 7f b5 c0 fe ff
    0e60: ff f3 0f 7f bd d0 fe ff ff 49 81 c1 80 00 00 00
    0e70: e9 3f f8 ff ff f3 44 0f 6f 9d 60 fe ff ff f3 44
    0e80: 0f 6f 02 f3 44 0f 6f 52 10 45 0f 28 c8 66 45 0f
    0e90: 70 e3 4e 66 45 0f ef e3 66 45 0f 3a 44 d4 00 66
    0ea0: 45 0f 3a 44 c3 00 66 45 0f 3a 44 cb 11 f3 44 0f
    0eb0: 6f 9d 70 fe ff ff f3 44 0f 6f 62 20 45 0f 28 ec
    0ec0: 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a
    0ed0: 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45
    0ee0: 0f ef dc f3 44 0f 6f 62 30 66 45 0f 3a 44 e3 00
    0ef0: 66 45 0f ef d4 f3 44 0f 6f 9d 80 fe ff ff f3 44
    0f00: 0f 6f 62 40 45 0f 28 ec 66 45 0f 3a 44 e3 00 66
    0f10: 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd
    0f20: 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f 62
    0f30: 50 66 45 0f 3a 44 e3 00 66 45 0f ef d4 f3 44 0f
    0f40: 6f 9d 90 fe ff ff f3 44 0f 6f 62 60 45 0f 28 ec
    0f50: 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a
    0f60: 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45
    0f70: 0f ef dc f3 44 0f 6f 62 70 66 45 0f 3a 44 e3 00
    0f80: 66 45 0f ef d4 f3 44 0f 6f 9d a0 fe ff ff f3 44
    0f90: 0f 6f a2 80 00 00 00 45 0f 28 ec 66 45 0f 3a 44
    0fa0: e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45
    0fb0: 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44
    0fc0: 0f 6f a2 90 00 00 00 66 45 0f 3a 44 e3 00 66 45
    0fd0: 0f ef d4 f3 44 0f 6f 9d b0 fe ff ff f3 44 0f 6f
    0fe0: a2 a0 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00
    0ff0: 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef
    1000: cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f
    1010: a2 b0 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef
    1020: d4 f3 44 0f 6f 9d c0 fe ff ff f3 44 0f 6f a2 c0
    1030: 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45
    1040: 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66
    1050: 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 d0
    1060: 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 f3
    1070: 44 0f 6f 9d d0 fe ff ff f3 44 0f 6f a2 e0 00 00
    1080: 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef
    1090: c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f
    10a0: 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 f0 00 00
    10b0: 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 66 45 0f
    10c0: ef d0 66 45 0f ef d1 45 0f 28 da 66 41 0f 73 da
    10d0: 08 66 41 0f 73 fb 08 66 45 0f ef ca 66 45 0f ef
    10e0: c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45 0f 70
    10f0: c0 4e 66 45 0f ef c3 45 0f 28 de 66 45 0f 3a 44
    1100: d8 01 66 45 0f 70 c0 4e 66 45 0f ef c3 66 45 0f
    1110: ef c1 4d 85 d2 0f 84 be 01 00 00 48 81 e8 07 00
    1120: 00 00 f3 0f 6f 8a 10 01 00 00 f3 0f 6f 92 20 01
    1130: 00 00 f3 0f 6f 9a 30 01 00 00 f3 0f 6f a2 40 01
    1140: 00 00 f3 0f 6f aa 50 01 00 00 f3 0f 6f b2 60 01
    1150: 00 00 f3 0f 6f ba 70 01 00 00 49 81 fa 10 00 00
    1160: 00 0f 82 72 01 00 00 49 81 ea 10 00 00 00 f3 0f
    1170: 6f 85 e0 fe ff ff 48 81 c0 01 00 00 00 89 c6 31
    1180: fe 0f ce 89 b5 ec fe ff ff 66 0f 38 dc c1 66 0f
    1190: 38 dc c2 66 0f 38 dc c3 66 0f 38 dc c4 66 0f 38
    11a0: dc c5 66 0f 38 dc c6 66 0f 38 dc c7 f3 44 0f 6f
    11b0: 9a 80 01 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a
    11c0: 90 01 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a a0
    11d0: 01 00 00 49 81 fb 0c 00 00 00 0f 82 42 00 00 00
    11e0: 66 41 0f 38 dc c3 f3 44 0f 6f 9a b0 01 00 00 66
    11f0: 41 0f 38 dc c3 f3 44 0f 6f 9a c0 01 00 00 0f 84
    1200: 1e 00 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a d0
    1210: 01 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a e0 01
    1220: 00 00 66 41 0f 38 dd c3 f3 45 0f 6f 19 66 41 0f
    1230: ef c3 f3 41 0f 7f 01 66 41 0f 38 00 c7 66 41 0f
    1240: ef c0 f3 44 0f 6f 82 e0 00 00 00 45 0f 28 c8 f3
    1250: 44 0f 6f 92 f0 00 00 00 66 44 0f 3a 44 c0 00 66
    1260: 44 0f 3a 44 c8 11 66 44 0f 70 d8 4e 66 44 0f ef
    1270: d8 66 45 0f 3a 44 d3 00 66 45 0f ef d0 66 45 0f
    1280: ef d1 45 0f 28 da 66 41 0f 73 da 08 66 41 0f 73
    1290: fb 08 66 45 0f ef ca 66 45 0f ef c3 45 0f 28 de
    12a0: 66 45 0f 3a 44 d8 01 66 45 0f 70 c0 4e 66 45 0f
    12b0: ef c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45 0f
    12c0: 70 c0 4e 66 45 0f ef c3 66 45 0f ef c1 49 81 c1
    12d0: 10 00 00 00 e9 81 fe ff ff f3 45 0f 7f 00 f3 0f
    12e0: 6f b5 60 ff ff ff f3 0f 6f bd 70 ff ff ff f3 44
    12f0: 0f 6f 45 80 f3 44 0f 6f 4d 90 f3 44 0f 6f 55 a0
    1300: f3 44 0f 6f 5d b0 f3 44 0f 6f 65 c0 f3 44 0f 6f
    1310: 6d d0 f3 44 0f 6f 75 e0 f3 44 0f 6f 7d f0 31 c0
    1320: 48 81 c4 c0 01 00 00 5d c3 00 0f 0e 0d 0c 0b 0a
    1330: 09 08 07 06 05 04 03 02 01 00 00 00 01 00 00 00
    1340: 00 00 00 00 00 00 00 00 00 00 00 c2 00
}

chunk @mrt_aes_gcm_decrypt {
    0000: 55 48 89 e5 48 81 ec 40 01 00 00 f3 0f 7f b5 60
    0010: ff ff ff f3 0f 7f bd 70 ff ff ff f3 44 0f 7f 45
    0020: 80 f3 44 0f 7f 4d 90 f3 44 0f 7f 55 a0 f3 44 0f
    0030: 7f 5d b0 f3 44 0f 7f 65 c0 f3 44 0f 7f 6d d0 f3
    0040: 44 0f 7f 75 e0 f3 44 0f 7f 7d f0 49 89 ca 49 c1
    0050: ea 08 49 89 cb 49 81 e3 ff 00 00 00 48 8d 0d b9
    0060: 0a 00 00 f3 44 0f 6f 39 48 8d 0d bf 0a 00 00 f3
    0070: 44 0f 6f 31 f3 45 0f 6f 00 66 45 0f ef c9 66 45
    0080: 0f ef d2 f3 41 0f 6f 40 10 41 8b 40 1c f3 44 0f
    0090: 6f 9a 00 01 00 00 8b ba 0c 01 00 00 0f c8 0f cf
    00a0: 4c 89 d6 48 c1 ee 04 48 01 c6 0f ce 41 89 70 1c
    00b0: 66 44 0f ef d8 f3 44 0f 7f 9d e0 fe ff ff 48 81
    00c0: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 ec fe ff
    00d0: ff 49 81 fa 80 00 00 00 0f 82 33 08 00 00 f3 44
    00e0: 0f 7f 9d f0 fe ff ff 48 81 c0 01 00 00 00 89 c6
    00f0: 31 fe 0f ce 89 b5 fc fe ff ff f3 44 0f 7f 9d 00
    0100: ff ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce
    0110: 89 b5 0c ff ff ff f3 44 0f 7f 9d 10 ff ff ff 48
    0120: 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 1c ff
    0130: ff ff f3 44 0f 7f 9d 20 ff ff ff 48 81 c0 01 00
    0140: 00 00 89 c6 31 fe 0f ce 89 b5 2c ff ff ff f3 44
    0150: 0f 7f 9d 30 ff ff ff 48 81 c0 01 00 00 00 89 c6
    0160: 31 fe 0f ce 89 b5 3c ff ff ff f3 44 0f 7f 9d 40
    0170: ff ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce
    0180: 89 b5 4c ff ff ff f3 44 0f 7f 9d 50 ff ff ff 48
    0190: 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 5c ff
    01a0: ff ff 49 81 fa 80 00 00 00 0f 82 5b 07 00 00 49
    01b0: 81 ea 80 00 00 00 f3 0f 6f 85 e0 fe ff ff f3 0f
    01c0: 6f 8d f0 fe ff ff f3 0f 6f 95 00 ff ff ff f3 0f
    01d0: 6f 9d 10 ff ff ff f3 0f 6f a5 20 ff ff ff f3 0f
    01e0: 6f ad 30 ff ff ff f3 0f 6f b5 40 ff ff ff f3 0f
    01f0: 6f bd 50 ff ff ff f3 45 0f 6f 19 66 45 0f 38 00
    0200: df 66 45 0f ef d8 f3 44 0f 6f 02 f3 44 0f 6f 52
    0210: 10 45 0f 28 c8 66 45 0f 70 e3 4e 66 45 0f ef e3
    0220: 66 45 0f 3a 44 d4 00 66 45 0f 3a 44 c3 00 66 45
    0230: 0f 3a 44 cb 11 f3 44 0f 6f 9a 10 01 00 00 66 41
    0240: 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3
    0250: 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38
    0260: dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 45
    0270: 0f 6f 59 10 66 45 0f 38 00 df f3 44 0f 6f 62 20
    0280: 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4
    0290: 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70
    02a0: e3 4e 66 45 0f ef dc f3 44 0f 6f 62 30 66 45 0f
    02b0: 3a 44 e3 00 66 45 0f ef d4 48 81 c0 01 00 00 00
    02c0: 89 c6 31 fe 0f ce 89 b5 ec fe ff ff f3 44 0f 6f
    02d0: 9a 20 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc
    02e0: cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f
    02f0: 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66
    0300: 41 0f 38 dc fb f3 45 0f 6f 59 20 66 45 0f 38 00
    0310: df f3 44 0f 6f 62 40 45 0f 28 ec 66 45 0f 3a 44
    0320: e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45
    0330: 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44
    0340: 0f 6f 62 50 66 45 0f 3a 44 e3 00 66 45 0f ef d4
    0350: 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 fc
    0360: fe ff ff f3 44 0f 6f 9a 30 01 00 00 66 41 0f 38
    0370: dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41
    0380: 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb
    0390: 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 45 0f 6f
    03a0: 59 30 66 45 0f 38 00 df f3 44 0f 6f 62 60 45 0f
    03b0: 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45
    03c0: 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e
    03d0: 66 45 0f ef dc f3 44 0f 6f 62 70 66 45 0f 3a 44
    03e0: e3 00 66 45 0f ef d4 48 81 c0 01 00 00 00 89 c6
    03f0: 31 fe 0f ce 89 b5 0c ff ff ff f3 44 0f 6f 9a 40
    0400: 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66
    0410: 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc
    0420: e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f
    0430: 38 dc fb f3 45 0f 6f 59 40 66 45 0f 38 00 df f3
    0440: 44 0f 6f a2 80 00 00 00 45 0f 28 ec 66 45 0f 3a
    0450: 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66
    0460: 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3
    0470: 44 0f 6f a2 90 00 00 00 66 45 0f 3a 44 e3 00 66
    0480: 45 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f
    0490: ce 89 b5 1c ff ff ff f3 44 0f 6f 9a 50 01 00 00
    04a0: 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38
    04b0: dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41
    04c0: 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb
    04d0: f3 45 0f 6f 59 50 66 45 0f 38 00 df f3 44 0f 6f
    04e0: a2 a0 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00
    04f0: 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef
    0500: cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f
    0510: a2 b0 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef
    0520: d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    0530: 2c ff ff ff f3 44 0f 6f 9a 60 01 00 00 66 41 0f
    0540: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    0550: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    0560: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 45 0f
    0570: 6f 59 60 66 45 0f 38 00 df f3 44 0f 6f a2 c0 00
    0580: 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f
    0590: ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45
    05a0: 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 d0 00
    05b0: 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81
    05c0: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 3c ff ff
    05d0: ff f3 44 0f 6f 9a 70 01 00 00 66 41 0f 38 dc c3
    05e0: 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38
    05f0: dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41
    0600: 0f 38 dc f3 66 41 0f 38 dc fb f3 45 0f 6f 59 70
    0610: 66 45 0f 38 00 df f3 44 0f 6f a2 e0 00 00 00 45
    0620: 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66
    0630: 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3
    0640: 4e 66 45 0f ef dc f3 44 0f 6f a2 f0 00 00 00 66
    0650: 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81 c0 01 00
    0660: 00 00 89 c6 31 fe 0f ce 89 b5 4c ff ff ff f3 44
    0670: 0f 6f 9a 80 01 00 00 66 41 0f 38 dc c3 66 41 0f
    0680: 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66
    0690: 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc
    06a0: f3 66 41 0f 38 dc fb 48 81 c0 01 00 00 00 89 c6
    06b0: 31 fe 0f ce 89 b5 5c ff ff ff 66 45 0f ef d0 66
    06c0: 45 0f ef d1 45 0f 28 da 66 41 0f 73 da 08 66 41
    06d0: 0f 73 fb 08 66 45 0f ef ca 66 45 0f ef c3 45 0f
    06e0: 28 de 66 45 0f 3a 44 d8 01 66 45 0f 70 c0 4e 66
    06f0: 45 0f ef c3 f3 44 0f 6f 9a 90 01 00 00 66 41 0f
    0700: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    0710: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    0720: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 45 0f 28
    0730: de 66 45 0f 3a 44 d8 01 66 45 0f 70 c0 4e 66 45
    0740: 0f ef c3 66 45 0f ef c1 f3 44 0f 6f 9a a0 01 00
    0750: 00 49 81 fb 0c 00 00 00 0f 82 ea 00 00 00 66 41
    0760: 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3
    0770: 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38
    0780: dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44
    0790: 0f 6f 9a b0 01 00 00 66 41 0f 38 dc c3 66 41 0f
    07a0: 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66
    07b0: 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc
    07c0: f3 66 41 0f 38 dc fb f3 44 0f 6f 9a c0 01 00 00
    07d0: 0f 84 72 00 00 00 66 41 0f 38 dc c3 66 41 0f 38
    07e0: dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41
    07f0: 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3
    0800: 66 41 0f 38 dc fb f3 44 0f 6f 9a d0 01 00 00 66
    0810: 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc
    0820: d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f
    0830: 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3
    0840: 44 0f 6f 9a e0 01 00 00 66 41 0f 38 dd c3 66 41
    0850: 0f 38 dd cb 66 41 0f 38 dd d3 66 41 0f 38 dd db
    0860: 66 41 0f 38 dd e3 66 41 0f 38 dd eb 66 41 0f 38
    0870: dd f3 66 41 0f 38 dd fb f3 45 0f 6f 19 66 41 0f
    0880: ef c3 f3 45 0f 6f 59 10 66 41 0f ef cb f3 45 0f
    0890: 6f 59 20 66 41 0f ef d3 f3 45 0f 6f 59 30 66 41
    08a0: 0f ef db f3 45 0f 6f 59 40 66 41 0f ef e3 f3 45
    08b0: 0f 6f 59 50 66 41 0f ef eb f3 45 0f 6f 59 60 66
    08c0: 41 0f ef f3 f3 45 0f 6f 59 70 66 41 0f ef fb f3
    08d0: 41 0f 7f 01 f3 41 0f 7f 49 10 f3 41 0f 7f 51 20
    08e0: f3 41 0f 7f 59 30 f3 41 0f 7f 61 40 f3 41 0f 7f
    08f0: 69 50 f3 41 0f 7f 71 60 f3 41 0f 7f 79 70 49 81
    0900: c1 80 00 00 00 e9 98 f8 ff ff 48 81 e8 07 00 00
    0910: 00 f3 0f 6f 8a 10 01 00 00 f3 0f 6f 92 20 01 00
    0920: 00 f3 0f 6f 9a 30 01 00 00 f3 0f 6f a2 40 01 00
    0930: 00 f3 0f 6f aa 50 01 00 00 f3 0f 6f b2 60 01 00
    0940: 00 f3 0f 6f ba 70 01 00 00 49 81 fa 10 00 00 00
    0950: 0f 82 76 01 00 00 49 81 ea 10 00 00 00 f3 41 0f
    0960: 6f 01 44 0f 28 e0 66 41 0f 38 00 c7 66 41 0f ef
    0970: c0 f3 44 0f 6f 82 e0 00 00 00 45 0f 28 c8 f3 44
    0980: 0f 6f 92 f0 00 00 00 66 44 0f 3a 44 c0 00 66 44
    0990: 0f 3a 44 c8 11 66 44 0f 70 d8 4e 66 44 0f ef d8
    09a0: 66 45 0f 3a 44 d3 00 66 45 0f ef d0 66 45 0f ef
    09b0: d1 45 0f 28 da 66 41 0f 73 da 08 66 41 0f 73 fb
    09c0: 08 66 45 0f ef ca 66 45 0f ef c3 45 0f 28 de 66
    09d0: 45 0f 3a 44 d8 01 66 45 0f 70 c0 4e 66 45 0f ef
    09e0: c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45 0f 70
    09f0: c0 4e 66 45 0f ef c3 66 45 0f ef c1 f3 0f 6f 85
    0a00: e0 fe ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f
    0a10: ce 89 b5 ec fe ff ff 66 0f 38 dc c1 66 0f 38 dc
    0a20: c2 66 0f 38 dc c3 66 0f 38 dc c4 66 0f 38 dc c5
    0a30: 66 0f 38 dc c6 66 0f 38 dc c7 f3 44 0f 6f 9a 80
    0a40: 01 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a 90 01
    0a50: 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a a0 01 00
    0a60: 00 49 81 fb 0c 00 00 00 0f 82 42 00 00 00 66 41
    0a70: 0f 38 dc c3 f3 44 0f 6f 9a b0 01 00 00 66 41 0f
    0a80: 38 dc c3 f3 44 0f 6f 9a c0 01 00 00 0f 84 1e 00
    0a90: 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a d0 01 00
    0aa0: 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a e0 01 00 00
    0ab0: 66 41 0f 38 dd c3 66 41 0f ef c4 f3 41 0f 7f 01
    0ac0: 49 81 c1 10 00 00 00 e9 7d fe ff ff f3 45 0f 7f
    0ad0: 00 f3 0f 6f b5 60 ff ff ff f3 0f 6f bd 70 ff ff
    0ae0: ff f3 44 0f 6f 45 80 f3 44 0f 6f 4d 90 f3 44 0f
    0af0: 6f 55 a0 f3 44 0f 6f 5d b0 f3 44 0f 6f 65 c0 f3
    0b00: 44 0f 6f 6d d0 f3 44 0f 6f 75 e0 f3 44 0f 6f 7d
    0b10: f0 31 c0 48 81 c4 40 01 00 00 5d c3 0f 0e 0d 0c
    0b20: 0b 0a 09 08 07 06 05 04 03 02 01 00 00 00 01 00
    0b30: 00 00 00 00 00 00 00 00 00 00 00 00 00 c2 00
}

chunk @mrt_aes_gcm_finish {
    0000: 55 48 89 e5 48 81 ec c0 00 00 00 f3 0f 7f b5 60
    0010: ff ff ff f3 0f 7f bd 70 ff ff ff f3 44 0f 7f 45
    0020: 80 f3 44 0f 7f 4d 90 f3 44 0f 7f 55 a0 f3 44 0f
    0030: 7f 5d b0 f3 44 0f 7f 65 c0 f3 44 0f 7f 6d d0 f3
    0040: 44 0f 7f 75 e0 f3 44 0f 7f 7d f0 48 8d 05 1c 01
    0050: 00 00 f3 44 0f 6f 38 48 8d 05 22 01 00 00 f3 44
    0060: 0f 6f 30 f3 45 0f 6f 00 f3 45 0f 6f 68 20 49 c1
    0070: e1 03 48 c1 e1 03 66 49 0f 6e c1 66 48 0f 3a 22
    0080: c1 01 66 41 0f ef c0 f3 44 0f 6f 82 e0 00 00 00
    0090: 45 0f 28 c8 f3 44 0f 6f 92 f0 00 00 00 66 44 0f
    00a0: 3a 44 c0 00 66 44 0f 3a 44 c8 11 66 44 0f 70 d8
    00b0: 4e 66 44 0f ef d8 66 45 0f 3a 44 d3 00 66 45 0f
    00c0: ef d0 66 45 0f ef d1 45 0f 28 da 66 41 0f 73 da
    00d0: 08 66 41 0f 73 fb 08 66 45 0f ef ca 66 45 0f ef
    00e0: c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45 0f 70
    00f0: c0 4e 66 45 0f ef c3 45 0f 28 de 66 45 0f 3a 44
    0100: d8 01 66 45 0f 70 c0 4e 66 45 0f ef c3 66 45 0f
    0110: ef c1 66 45 0f 38 00 c7 66 45 0f ef c5 f3 45 0f
    0120: 7f 00 f3 0f 6f b5 60 ff ff ff f3 0f 6f bd 70 ff
    0130: ff ff f3 44 0f 6f 45 80 f3 44 0f 6f 4d 90 f3 44
    0140: 0f 6f 55 a0 f3 44 0f 6f 5d b0 f3 44 0f 6f 65 c0
    0150: f3 44 0f 6f 6d d0 f3 44 0f 6f 75 e0 f3 44 0f 6f
    0160: 7d f0 31 c0 48 81 c4 c0 00 00 00 5d c3 00 0f 0e
    0170: 0d 0c 0b 0a 09 08 07 06 05 04 03 02 01 00 00 00
    0180: 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 c2
    0190: 00
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_cpu_aes_usable@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
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
    x64.prologue 104
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r14, r8
    x64.storeSlotReg slot3, r14
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r15, r8
    x64.storeSlotReg slot4, r15
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 352
    x64.movRegReg rcx, r12
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 432
    x64.movRegReg rcx, r13
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 48
    x64.movRegReg rcx, r14
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 64
    x64.movRegReg rcx, r15
    x64.callDirect __managed_resize
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __cpufeat_aes_cipher_expand_key
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r12
    x64.callDirect __cpufeat_aes_cipher_gcm_init
    x64.movRegImm32 r9, 0
    x64.movRegImm32 rsi, 32
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r15
    x64.callDirect __cpufeat_aes_cipher_gcm_hash
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rsi, 64
    x64.movRegReg rcx, r15
    x64.movRegReg rax, r13
    x64.movRegReg r9, r14
    x64.callDirect __cpufeat_aes_cipher_gcm_encrypt
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rsi, 64
    x64.movRegReg rcx, r15
    x64.movRegReg rax, r13
    x64.movRegReg r9, r14
    x64.callDirect __cpufeat_aes_cipher_gcm_decrypt
    x64.movRegImm32 rax, 32
    x64.movRegImm32 r9, 64
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r13
    x64.callDirect __cpufeat_aes_cipher_gcm_finish
    x64.loadRegBaseDisp.word64 rbx, [r14 + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot5, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot6, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot7, r8
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
    x64.loadRegSlot rax, slot7
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot6
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot5
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot7
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot7
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot4
    x64.callDirect __managed_decref
  exitdrop#2:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __managed_decref
  exitdrop#3:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __managed_decref
  exitdrop#4:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __managed_decref
  exitdrop#5:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_init {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.loadRegBaseDisp.word64 r13, [r12 + 8]
    x64.cmpRegImm32 r13, 352
    x64.jcc less, gibad
  scbounded:
    x64.cmpRegImm32 r13, 480
    x64.jcc greater, gibad
  scstepped:
    x64.movRegReg rax, r13
    x64.andRegImm32 rax, r13, 63
    x64.cmpRegImm32 rax, 32
    x64.jcc notEqual, gibad
  gikeyblock:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.movRegReg rcx, r13
    x64.shrRegImm8 rcx, r13, 1
    x64.leaRegRegImm32 rcx, rcx, 256
    x64.cmpRegReg rax, rcx
    x64.jcc notEqual, gibad
  gicrypt:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.shrRegImm8 r13, r13, 5
    x64.leaRegRegImm32 rcx, r13, -1
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.loadRegBaseDisp.word64 r8, [r12 + 0]
    x64.movRegImm32 r9, 0
    x64.callDirect mrt_aes_gcm_init
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  gibad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_hash {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.movRegReg r13, rax
    x64.movRegReg r14, r9
    x64.movRegReg r15, rsi
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegImm32 rax, 432
    x64.jcc less, ghbad
  kbbounded:
    x64.cmpRegImm32 rax, 496
    x64.jcc greater, ghbad
  kbstepped:
    x64.andRegImm32 rax, rax, 31
    x64.cmpRegImm32 rax, 16
    x64.jcc notEqual, ghbad
  gkmessage:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 48
    x64.jcc notEqual, ghbad
  ghspan:
    x64.movRegReg rax, r15
    x64.andRegImm32 rax, r15, 15
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, ghbad
  ghaligned:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.cmpRegReg r14, rax
    x64.jcc above, ghbad
  ghinside:
    x64.subRegReg rax, rax, r14
    x64.cmpRegReg rax, r15
    x64.jcc below, ghbad
  ghcrypt:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.loadRegBaseDisp.word64 rdx, [r12 + 0]
    x64.loadRegBaseDisp.word64 r8, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.leaRegRegReg r9, rax, r14
    x64.movRegReg rcx, r15
    x64.callDirect mrt_aes_gcm_hash
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ghbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_encrypt {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.storeSlotReg slot0, rdx
    x64.movRegReg r12, rax
    x64.movRegReg r13, r9
    x64.movRegReg r14, rsi
    x64.loadRegBaseDisp.word64 r15, [r12 + 8]
    x64.cmpRegImm32 r15, 432
    x64.jcc less, gcbad
  kbbounded:
    x64.cmpRegImm32 r15, 496
    x64.jcc greater, gcbad
  kbstepped:
    x64.movRegReg rax, r15
    x64.andRegImm32 rax, r15, 31
    x64.cmpRegImm32 rax, 16
    x64.jcc notEqual, gcbad
  gkmessage:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.cmpRegImm32 rax, 48
    x64.jcc notEqual, gcbad
  gcspan:
    x64.movRegReg rax, r14
    x64.andRegImm32 rax, r14, 15
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, gcbad
  gcaligned:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc above, gcbad
  gcinside:
    x64.subRegReg rax, rax, rdx
    x64.cmpRegReg rax, r14
    x64.jcc below, gcbad
  gccrypt:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cow_detach
    x64.leaRegRegImm32 rax, r15, -256
    x64.shrRegImm8 rax, rax, 4
    x64.leaRegRegImm32 rax, rax, -1
    x64.shlRegImm8 r14, r14, 8
    x64.movRegReg rcx, r14
    x64.orRegReg rcx, r14, rax
    x64.loadRegBaseDisp.word64 rdx, [r12 + 0]
    x64.loadRegBaseDisp.word64 r8, [r13 + 0]
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegSlot rsi, slot0
    x64.leaRegRegReg r9, rax, rsi
    x64.callDirect mrt_aes_gcm_encrypt
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  gcbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_decrypt {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.storeSlotReg slot0, rdx
    x64.movRegReg r12, rax
    x64.movRegReg r13, r9
    x64.movRegReg r14, rsi
    x64.loadRegBaseDisp.word64 r15, [r12 + 8]
    x64.cmpRegImm32 r15, 432
    x64.jcc less, gcbad
  kbbounded:
    x64.cmpRegImm32 r15, 496
    x64.jcc greater, gcbad
  kbstepped:
    x64.movRegReg rax, r15
    x64.andRegImm32 rax, r15, 31
    x64.cmpRegImm32 rax, 16
    x64.jcc notEqual, gcbad
  gkmessage:
    x64.loadRegBaseDisp.word64 rax, [r13 + 8]
    x64.cmpRegImm32 rax, 48
    x64.jcc notEqual, gcbad
  gcspan:
    x64.movRegReg rax, r14
    x64.andRegImm32 rax, r14, 15
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, gcbad
  gcaligned:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc above, gcbad
  gcinside:
    x64.subRegReg rax, rax, rdx
    x64.cmpRegReg rax, r14
    x64.jcc below, gcbad
  gccrypt:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cow_detach
    x64.leaRegRegImm32 rax, r15, -256
    x64.shrRegImm8 rax, rax, 4
    x64.leaRegRegImm32 rax, rax, -1
    x64.shlRegImm8 r14, r14, 8
    x64.movRegReg rcx, r14
    x64.orRegReg rcx, r14, rax
    x64.loadRegBaseDisp.word64 rdx, [r12 + 0]
    x64.loadRegBaseDisp.word64 r8, [r13 + 0]
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegSlot rsi, slot0
    x64.leaRegRegReg r9, rax, rsi
    x64.callDirect mrt_aes_gcm_decrypt
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  gcbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_finish {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.movRegReg r13, rax
    x64.movRegReg r14, r9
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegImm32 rax, 432
    x64.jcc less, gfbad
  kbbounded:
    x64.cmpRegImm32 rax, 496
    x64.jcc greater, gfbad
  kbstepped:
    x64.andRegImm32 rax, rax, 31
    x64.cmpRegImm32 rax, 16
    x64.jcc notEqual, gfbad
  gkmessage:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 48
    x64.jcc notEqual, gfbad
  gfcrypt:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.loadRegBaseDisp.word64 rdx, [r12 + 0]
    x64.loadRegBaseDisp.word64 r8, [rbx + 0]
    x64.movRegReg rcx, r13
    x64.movRegReg r9, r14
    x64.callDirect mrt_aes_gcm_finish
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  gfbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

chunk @mrt_aes_gcm_init {
    0000: 55 48 89 e5 48 81 ec c0 00 00 00 f3 0f 7f b5 60
    0010: ff ff ff f3 0f 7f bd 70 ff ff ff f3 44 0f 7f 45
    0020: 80 f3 44 0f 7f 4d 90 f3 44 0f 7f 55 a0 f3 44 0f
    0030: 7f 5d b0 f3 44 0f 7f 65 c0 f3 44 0f 7f 6d d0 f3
    0040: 44 0f 7f 75 e0 f3 44 0f 7f 7d f0 4c 8d 8a 00 01
    0050: 00 00 4c 8d 51 01 f3 41 0f 6f 00 f3 41 0f 7f 01
    0060: 49 81 c0 10 00 00 00 49 81 c1 10 00 00 00 49 81
    0070: ea 01 00 00 00 0f 85 db ff ff ff 48 8d 05 62 02
    0080: 00 00 f3 44 0f 6f 38 48 8d 05 68 02 00 00 f3 44
    0090: 0f 6f 30 f3 0f 6f 82 00 01 00 00 f3 44 0f 6f 9a
    00a0: 10 01 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a 20
    00b0: 01 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a 30 01
    00c0: 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a 40 01 00
    00d0: 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a 50 01 00 00
    00e0: 66 41 0f 38 dc c3 f3 44 0f 6f 9a 60 01 00 00 66
    00f0: 41 0f 38 dc c3 f3 44 0f 6f 9a 70 01 00 00 66 41
    0100: 0f 38 dc c3 f3 44 0f 6f 9a 80 01 00 00 66 41 0f
    0110: 38 dc c3 f3 44 0f 6f 9a 90 01 00 00 66 41 0f 38
    0120: dc c3 f3 44 0f 6f 9a a0 01 00 00 48 81 f9 0c 00
    0130: 00 00 0f 82 42 00 00 00 66 41 0f 38 dc c3 f3 44
    0140: 0f 6f 9a b0 01 00 00 66 41 0f 38 dc c3 f3 44 0f
    0150: 6f 9a c0 01 00 00 0f 84 1e 00 00 00 66 41 0f 38
    0160: dc c3 f3 44 0f 6f 9a d0 01 00 00 66 41 0f 38 dc
    0170: c3 f3 44 0f 6f 9a e0 01 00 00 66 41 0f 38 dd c3
    0180: 66 41 0f 38 00 c7 66 44 0f 70 d8 ff 44 0f 28 e0
    0190: 66 41 0f 72 e3 1f 66 45 0f db de 66 41 0f 72 d4
    01a0: 1f 66 41 0f 73 fc 04 66 0f 72 f0 01 66 41 0f ef
    01b0: c3 66 41 0f ef c4 f3 0f 7f 82 e0 00 00 00 66 44
    01c0: 0f 70 d8 4e 66 44 0f ef d8 f3 44 0f 7f 9a f0 00
    01d0: 00 00 4c 8d 92 c0 00 00 00 45 31 db 49 81 c3 07
    01e0: 00 00 00 f3 44 0f 6f 82 e0 00 00 00 45 0f 28 c8
    01f0: f3 44 0f 6f 92 f0 00 00 00 66 44 0f 3a 44 c0 00
    0200: 66 44 0f 3a 44 c8 11 66 44 0f 70 d8 4e 66 44 0f
    0210: ef d8 66 45 0f 3a 44 d3 00 66 45 0f ef d0 66 45
    0220: 0f ef d1 45 0f 28 da 66 41 0f 73 da 08 66 41 0f
    0230: 73 fb 08 66 45 0f ef ca 66 45 0f ef c3 45 0f 28
    0240: de 66 45 0f 3a 44 d8 01 66 45 0f 70 c0 4e 66 45
    0250: 0f ef c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45
    0260: 0f 70 c0 4e 66 45 0f ef c3 66 45 0f ef c1 41 0f
    0270: 28 c0 f3 41 0f 7f 02 66 44 0f 70 d8 4e 66 44 0f
    0280: ef d8 f3 45 0f 7f 5a 10 49 81 eb 01 00 00 00 4d
    0290: 8d 52 e0 0f 85 4a ff ff ff f3 0f 6f b5 60 ff ff
    02a0: ff f3 0f 6f bd 70 ff ff ff f3 44 0f 6f 45 80 f3
    02b0: 44 0f 6f 4d 90 f3 44 0f 6f 55 a0 f3 44 0f 6f 5d
    02c0: b0 f3 44 0f 6f 65 c0 f3 44 0f 6f 6d d0 f3 44 0f
    02d0: 6f 75 e0 f3 44 0f 6f 7d f0 31 c0 48 81 c4 c0 00
    02e0: 00 00 5d c3 0f 0e 0d 0c 0b 0a 09 08 07 06 05 04
    02f0: 03 02 01 00 00 00 01 00 00 00 00 00 00 00 00 00
    0300: 00 00 00 00 00 c2 00
}

chunk @mrt_aes_gcm_hash {
    0000: 55 48 89 e5 48 81 ec c0 00 00 00 f3 0f 7f b5 60
    0010: ff ff ff f3 0f 7f bd 70 ff ff ff f3 44 0f 7f 45
    0020: 80 f3 44 0f 7f 4d 90 f3 44 0f 7f 55 a0 f3 44 0f
    0030: 7f 5d b0 f3 44 0f 7f 65 c0 f3 44 0f 7f 6d d0 f3
    0040: 44 0f 7f 75 e0 f3 44 0f 7f 7d f0 48 8d 05 04 04
    0050: 00 00 f3 44 0f 6f 38 48 8d 05 0a 04 00 00 f3 44
    0060: 0f 6f 30 f3 45 0f 6f 00 48 85 c9 0f 84 94 03 00
    0070: 00 48 81 f9 80 00 00 00 0f 82 cc 02 00 00 48 81
    0080: e9 80 00 00 00 f3 41 0f 6f 01 f3 41 0f 6f 49 10
    0090: f3 41 0f 6f 51 20 f3 41 0f 6f 59 30 f3 41 0f 6f
    00a0: 61 40 f3 41 0f 6f 69 50 f3 41 0f 6f 71 60 f3 41
    00b0: 0f 6f 79 70 49 81 c1 80 00 00 00 66 41 0f 38 00
    00c0: c7 66 41 0f 38 00 cf 66 41 0f 38 00 d7 66 41 0f
    00d0: 38 00 df 66 41 0f 38 00 e7 66 41 0f 38 00 ef 66
    00e0: 41 0f 38 00 f7 66 41 0f 38 00 ff 66 41 0f ef c0
    00f0: f3 44 0f 6f 02 f3 44 0f 6f 52 10 45 0f 28 c8 66
    0100: 44 0f 70 e0 4e 66 44 0f ef e0 66 45 0f 3a 44 d4
    0110: 00 66 44 0f 3a 44 c0 00 66 44 0f 3a 44 c8 11 f3
    0120: 44 0f 6f 62 20 45 0f 28 ec 66 44 0f 3a 44 e1 00
    0130: 66 45 0f ef c4 66 44 0f 3a 44 e9 11 66 45 0f ef
    0140: cd 66 44 0f 70 e1 4e 66 41 0f ef cc f3 44 0f 6f
    0150: 62 30 66 44 0f 3a 44 e1 00 66 45 0f ef d4 f3 44
    0160: 0f 6f 62 40 45 0f 28 ec 66 44 0f 3a 44 e2 00 66
    0170: 45 0f ef c4 66 44 0f 3a 44 ea 11 66 45 0f ef cd
    0180: 66 44 0f 70 e2 4e 66 41 0f ef d4 f3 44 0f 6f 62
    0190: 50 66 44 0f 3a 44 e2 00 66 45 0f ef d4 f3 44 0f
    01a0: 6f 62 60 45 0f 28 ec 66 44 0f 3a 44 e3 00 66 45
    01b0: 0f ef c4 66 44 0f 3a 44 eb 11 66 45 0f ef cd 66
    01c0: 44 0f 70 e3 4e 66 41 0f ef dc f3 44 0f 6f 62 70
    01d0: 66 44 0f 3a 44 e3 00 66 45 0f ef d4 f3 44 0f 6f
    01e0: a2 80 00 00 00 45 0f 28 ec 66 44 0f 3a 44 e4 00
    01f0: 66 45 0f ef c4 66 44 0f 3a 44 ec 11 66 45 0f ef
    0200: cd 66 44 0f 70 e4 4e 66 41 0f ef e4 f3 44 0f 6f
    0210: a2 90 00 00 00 66 44 0f 3a 44 e4 00 66 45 0f ef
    0220: d4 f3 44 0f 6f a2 a0 00 00 00 45 0f 28 ec 66 44
    0230: 0f 3a 44 e5 00 66 45 0f ef c4 66 44 0f 3a 44 ed
    0240: 11 66 45 0f ef cd 66 44 0f 70 e5 4e 66 41 0f ef
    0250: ec f3 44 0f 6f a2 b0 00 00 00 66 44 0f 3a 44 e5
    0260: 00 66 45 0f ef d4 f3 44 0f 6f a2 c0 00 00 00 45
    0270: 0f 28 ec 66 44 0f 3a 44 e6 00 66 45 0f ef c4 66
    0280: 44 0f 3a 44 ee 11 66 45 0f ef cd 66 44 0f 70 e6
    0290: 4e 66 41 0f ef f4 f3 44 0f 6f a2 d0 00 00 00 66
    02a0: 44 0f 3a 44 e6 00 66 45 0f ef d4 f3 44 0f 6f a2
    02b0: e0 00 00 00 45 0f 28 ec 66 44 0f 3a 44 e7 00 66
    02c0: 45 0f ef c4 66 44 0f 3a 44 ef 11 66 45 0f ef cd
    02d0: 66 44 0f 70 e7 4e 66 41 0f ef fc f3 44 0f 6f a2
    02e0: f0 00 00 00 66 44 0f 3a 44 e7 00 66 45 0f ef d4
    02f0: 66 45 0f ef d0 66 45 0f ef d1 45 0f 28 da 66 41
    0300: 0f 73 da 08 66 41 0f 73 fb 08 66 45 0f ef ca 66
    0310: 45 0f ef c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66
    0320: 45 0f 70 c0 4e 66 45 0f ef c3 45 0f 28 de 66 45
    0330: 0f 3a 44 d8 01 66 45 0f 70 c0 4e 66 45 0f ef c3
    0340: 66 45 0f ef c1 e9 27 fd ff ff 48 81 f9 10 00 00
    0350: 00 0f 82 ae 00 00 00 48 81 e9 10 00 00 00 f3 41
    0360: 0f 6f 01 66 41 0f 38 00 c7 66 41 0f ef c0 f3 44
    0370: 0f 6f 82 e0 00 00 00 45 0f 28 c8 f3 44 0f 6f 92
    0380: f0 00 00 00 66 44 0f 3a 44 c0 00 66 44 0f 3a 44
    0390: c8 11 66 44 0f 70 d8 4e 66 44 0f ef d8 66 45 0f
    03a0: 3a 44 d3 00 66 45 0f ef d0 66 45 0f ef d1 45 0f
    03b0: 28 da 66 41 0f 73 da 08 66 41 0f 73 fb 08 66 45
    03c0: 0f ef ca 66 45 0f ef c3 45 0f 28 de 66 45 0f 3a
    03d0: 44 d8 01 66 45 0f 70 c0 4e 66 45 0f ef c3 45 0f
    03e0: 28 de 66 45 0f 3a 44 d8 01 66 45 0f 70 c0 4e 66
    03f0: 45 0f ef c3 66 45 0f ef c1 49 81 c1 10 00 00 00
    0400: e9 45 ff ff ff f3 45 0f 7f 00 f3 0f 6f b5 60 ff
    0410: ff ff f3 0f 6f bd 70 ff ff ff f3 44 0f 6f 45 80
    0420: f3 44 0f 6f 4d 90 f3 44 0f 6f 55 a0 f3 44 0f 6f
    0430: 5d b0 f3 44 0f 6f 65 c0 f3 44 0f 6f 6d d0 f3 44
    0440: 0f 6f 75 e0 f3 44 0f 6f 7d f0 31 c0 48 81 c4 c0
    0450: 00 00 00 5d c3 00 0f 0e 0d 0c 0b 0a 09 08 07 06
    0460: 05 04 03 02 01 00 00 00 01 00 00 00 00 00 00 00
    0470: 00 00 00 00 00 00 00 c2 00
}

chunk @mrt_aes_gcm_encrypt {
    0000: 55 48 89 e5 48 81 ec c0 01 00 00 f3 0f 7f b5 60
    0010: ff ff ff f3 0f 7f bd 70 ff ff ff f3 44 0f 7f 45
    0020: 80 f3 44 0f 7f 4d 90 f3 44 0f 7f 55 a0 f3 44 0f
    0030: 7f 5d b0 f3 44 0f 7f 65 c0 f3 44 0f 7f 6d d0 f3
    0040: 44 0f 7f 75 e0 f3 44 0f 7f 7d f0 49 89 ca 49 c1
    0050: ea 08 49 89 cb 49 81 e3 ff 00 00 00 48 8d 0d c7
    0060: 12 00 00 f3 44 0f 6f 39 48 8d 0d cd 12 00 00 f3
    0070: 44 0f 6f 31 f3 45 0f 6f 00 66 45 0f ef c9 66 45
    0080: 0f ef d2 f3 41 0f 6f 40 10 41 8b 40 1c f3 44 0f
    0090: 6f 9a 00 01 00 00 8b ba 0c 01 00 00 0f c8 0f cf
    00a0: 4c 89 d6 48 c1 ee 04 48 01 c6 0f ce 41 89 70 1c
    00b0: 66 44 0f ef d8 f3 44 0f 7f 9d e0 fe ff ff 48 81
    00c0: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 ec fe ff
    00d0: ff 49 81 fa 80 00 00 00 0f 82 44 10 00 00 49 81
    00e0: ea 80 00 00 00 f3 44 0f 7f 9d f0 fe ff ff 48 81
    00f0: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 fc fe ff
    0100: ff f3 44 0f 7f 9d 00 ff ff ff 48 81 c0 01 00 00
    0110: 00 89 c6 31 fe 0f ce 89 b5 0c ff ff ff f3 44 0f
    0120: 7f 9d 10 ff ff ff 48 81 c0 01 00 00 00 89 c6 31
    0130: fe 0f ce 89 b5 1c ff ff ff f3 44 0f 7f 9d 20 ff
    0140: ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0150: b5 2c ff ff ff f3 44 0f 7f 9d 30 ff ff ff 48 81
    0160: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 3c ff ff
    0170: ff f3 44 0f 7f 9d 40 ff ff ff 48 81 c0 01 00 00
    0180: 00 89 c6 31 fe 0f ce 89 b5 4c ff ff ff f3 44 0f
    0190: 7f 9d 50 ff ff ff 48 81 c0 01 00 00 00 89 c6 31
    01a0: fe 0f ce 89 b5 5c ff ff ff f3 0f 6f 85 e0 fe ff
    01b0: ff f3 0f 6f 8d f0 fe ff ff f3 0f 6f 95 00 ff ff
    01c0: ff f3 0f 6f 9d 10 ff ff ff f3 0f 6f a5 20 ff ff
    01d0: ff f3 0f 6f ad 30 ff ff ff f3 0f 6f b5 40 ff ff
    01e0: ff f3 0f 6f bd 50 ff ff ff f3 44 0f 6f 9a 10 01
    01f0: 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41
    0200: 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3
    0210: 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38
    0220: dc fb 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0230: b5 ec fe ff ff f3 44 0f 6f 9a 20 01 00 00 66 41
    0240: 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3
    0250: 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38
    0260: dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 48 81
    0270: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 fc fe ff
    0280: ff f3 44 0f 6f 9a 30 01 00 00 66 41 0f 38 dc c3
    0290: 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38
    02a0: dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41
    02b0: 0f 38 dc f3 66 41 0f 38 dc fb 48 81 c0 01 00 00
    02c0: 00 89 c6 31 fe 0f ce 89 b5 0c ff ff ff f3 44 0f
    02d0: 6f 9a 40 01 00 00 66 41 0f 38 dc c3 66 41 0f 38
    02e0: dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41
    02f0: 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3
    0300: 66 41 0f 38 dc fb 48 81 c0 01 00 00 00 89 c6 31
    0310: fe 0f ce 89 b5 1c ff ff ff f3 44 0f 6f 9a 50 01
    0320: 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41
    0330: 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3
    0340: 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38
    0350: dc fb 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0360: b5 2c ff ff ff f3 44 0f 6f 9a 60 01 00 00 66 41
    0370: 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3
    0380: 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38
    0390: dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 48 81
    03a0: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 3c ff ff
    03b0: ff f3 44 0f 6f 9a 70 01 00 00 66 41 0f 38 dc c3
    03c0: 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38
    03d0: dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41
    03e0: 0f 38 dc f3 66 41 0f 38 dc fb 48 81 c0 01 00 00
    03f0: 00 89 c6 31 fe 0f ce 89 b5 4c ff ff ff f3 44 0f
    0400: 6f 9a 80 01 00 00 66 41 0f 38 dc c3 66 41 0f 38
    0410: dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41
    0420: 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3
    0430: 66 41 0f 38 dc fb 48 81 c0 01 00 00 00 89 c6 31
    0440: fe 0f ce 89 b5 5c ff ff ff f3 44 0f 6f 9a 90 01
    0450: 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41
    0460: 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3
    0470: 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38
    0480: dc fb f3 44 0f 6f 9a a0 01 00 00 49 81 fb 0c 00
    0490: 00 00 0f 82 ea 00 00 00 66 41 0f 38 dc c3 66 41
    04a0: 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db
    04b0: 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38
    04c0: dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9a b0 01 00
    04d0: 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f
    04e0: 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66
    04f0: 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc
    0500: fb f3 44 0f 6f 9a c0 01 00 00 0f 84 72 00 00 00
    0510: 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38
    0520: dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41
    0530: 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb
    0540: f3 44 0f 6f 9a d0 01 00 00 66 41 0f 38 dc c3 66
    0550: 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc
    0560: db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f
    0570: 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9a e0 01
    0580: 00 00 66 41 0f 38 dd c3 66 41 0f 38 dd cb 66 41
    0590: 0f 38 dd d3 66 41 0f 38 dd db 66 41 0f 38 dd e3
    05a0: 66 41 0f 38 dd eb 66 41 0f 38 dd f3 66 41 0f 38
    05b0: dd fb f3 45 0f 6f 19 66 41 0f ef c3 f3 45 0f 6f
    05c0: 59 10 66 41 0f ef cb f3 45 0f 6f 59 20 66 41 0f
    05d0: ef d3 f3 45 0f 6f 59 30 66 41 0f ef db f3 45 0f
    05e0: 6f 59 40 66 41 0f ef e3 f3 45 0f 6f 59 50 66 41
    05f0: 0f ef eb f3 45 0f 6f 59 60 66 41 0f ef f3 f3 45
    0600: 0f 6f 59 70 66 41 0f ef fb f3 41 0f 7f 01 66 41
    0610: 0f 38 00 c7 66 41 0f ef c0 f3 41 0f 7f 49 10 66
    0620: 41 0f 38 00 cf f3 41 0f 7f 51 20 66 41 0f 38 00
    0630: d7 f3 41 0f 7f 59 30 66 41 0f 38 00 df f3 41 0f
    0640: 7f 61 40 66 41 0f 38 00 e7 f3 41 0f 7f 69 50 66
    0650: 41 0f 38 00 ef f3 41 0f 7f 71 60 66 41 0f 38 00
    0660: f7 f3 41 0f 7f 79 70 66 41 0f 38 00 ff f3 0f 7f
    0670: 85 60 fe ff ff f3 0f 7f 8d 70 fe ff ff f3 0f 7f
    0680: 95 80 fe ff ff f3 0f 7f 9d 90 fe ff ff f3 0f 7f
    0690: a5 a0 fe ff ff f3 0f 7f ad b0 fe ff ff f3 0f 7f
    06a0: b5 c0 fe ff ff f3 0f 7f bd d0 fe ff ff 49 81 c1
    06b0: 80 00 00 00 49 81 fa 80 00 00 00 0f 82 b4 07 00
    06c0: 00 49 81 ea 80 00 00 00 f3 0f 6f 85 e0 fe ff ff
    06d0: f3 0f 6f 8d f0 fe ff ff f3 0f 6f 95 00 ff ff ff
    06e0: f3 0f 6f 9d 10 ff ff ff f3 0f 6f a5 20 ff ff ff
    06f0: f3 0f 6f ad 30 ff ff ff f3 0f 6f b5 40 ff ff ff
    0700: f3 0f 6f bd 50 ff ff ff f3 44 0f 6f 9d 60 fe ff
    0710: ff f3 44 0f 6f 02 f3 44 0f 6f 52 10 45 0f 28 c8
    0720: 66 45 0f 70 e3 4e 66 45 0f ef e3 66 45 0f 3a 44
    0730: d4 00 66 45 0f 3a 44 c3 00 66 45 0f 3a 44 cb 11
    0740: f3 44 0f 6f 9a 10 01 00 00 66 41 0f 38 dc c3 66
    0750: 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc
    0760: db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f
    0770: 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9d 70 fe
    0780: ff ff f3 44 0f 6f 62 20 45 0f 28 ec 66 45 0f 3a
    0790: 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66
    07a0: 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3
    07b0: 44 0f 6f 62 30 66 45 0f 3a 44 e3 00 66 45 0f ef
    07c0: d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    07d0: ec fe ff ff f3 44 0f 6f 9a 20 01 00 00 66 41 0f
    07e0: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    07f0: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    0800: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f
    0810: 6f 9d 80 fe ff ff f3 44 0f 6f 62 40 45 0f 28 ec
    0820: 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a
    0830: 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45
    0840: 0f ef dc f3 44 0f 6f 62 50 66 45 0f 3a 44 e3 00
    0850: 66 45 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe
    0860: 0f ce 89 b5 fc fe ff ff f3 44 0f 6f 9a 30 01 00
    0870: 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f
    0880: 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66
    0890: 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc
    08a0: fb f3 44 0f 6f 9d 90 fe ff ff f3 44 0f 6f 62 60
    08b0: 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4
    08c0: 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70
    08d0: e3 4e 66 45 0f ef dc f3 44 0f 6f 62 70 66 45 0f
    08e0: 3a 44 e3 00 66 45 0f ef d4 48 81 c0 01 00 00 00
    08f0: 89 c6 31 fe 0f ce 89 b5 0c ff ff ff f3 44 0f 6f
    0900: 9a 40 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc
    0910: cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f
    0920: 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66
    0930: 41 0f 38 dc fb f3 44 0f 6f 9d a0 fe ff ff f3 44
    0940: 0f 6f a2 80 00 00 00 45 0f 28 ec 66 45 0f 3a 44
    0950: e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45
    0960: 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44
    0970: 0f 6f a2 90 00 00 00 66 45 0f 3a 44 e3 00 66 45
    0980: 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce
    0990: 89 b5 1c ff ff ff f3 44 0f 6f 9a 50 01 00 00 66
    09a0: 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc
    09b0: d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f
    09c0: 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3
    09d0: 44 0f 6f 9d b0 fe ff ff f3 44 0f 6f a2 a0 00 00
    09e0: 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef
    09f0: c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f
    0a00: 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 b0 00 00
    0a10: 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81 c0
    0a20: 01 00 00 00 89 c6 31 fe 0f ce 89 b5 2c ff ff ff
    0a30: f3 44 0f 6f 9a 60 01 00 00 66 41 0f 38 dc c3 66
    0a40: 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc
    0a50: db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f
    0a60: 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9d c0 fe
    0a70: ff ff f3 44 0f 6f a2 c0 00 00 00 45 0f 28 ec 66
    0a80: 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44
    0a90: eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f
    0aa0: ef dc f3 44 0f 6f a2 d0 00 00 00 66 45 0f 3a 44
    0ab0: e3 00 66 45 0f ef d4 48 81 c0 01 00 00 00 89 c6
    0ac0: 31 fe 0f ce 89 b5 3c ff ff ff f3 44 0f 6f 9a 70
    0ad0: 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66
    0ae0: 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc
    0af0: e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f
    0b00: 38 dc fb f3 44 0f 6f 9d d0 fe ff ff f3 44 0f 6f
    0b10: a2 e0 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00
    0b20: 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef
    0b30: cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f
    0b40: a2 f0 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef
    0b50: d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    0b60: 4c ff ff ff f3 44 0f 6f 9a 80 01 00 00 66 41 0f
    0b70: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    0b80: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    0b90: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 48 81 c0
    0ba0: 01 00 00 00 89 c6 31 fe 0f ce 89 b5 5c ff ff ff
    0bb0: 66 45 0f ef d0 66 45 0f ef d1 45 0f 28 da 66 41
    0bc0: 0f 73 da 08 66 41 0f 73 fb 08 66 45 0f ef ca 66
    0bd0: 45 0f ef c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66
    0be0: 45 0f 70 c0 4e 66 45 0f ef c3 f3 44 0f 6f 9a 90
    0bf0: 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66
    0c00: 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc
    0c10: e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f
    0c20: 38 dc fb 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45
    0c30: 0f 70 c0 4e 66 45 0f ef c3 66 45 0f ef c1 f3 44
    0c40: 0f 6f 9a a0 01 00 00 49 81 fb 0c 00 00 00 0f 82
    0c50: ea 00 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb
    0c60: 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38
    0c70: dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41
    0c80: 0f 38 dc fb f3 44 0f 6f 9a b0 01 00 00 66 41 0f
    0c90: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    0ca0: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    0cb0: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f
    0cc0: 6f 9a c0 01 00 00 0f 84 72 00 00 00 66 41 0f 38
    0cd0: dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41
    0ce0: 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb
    0cf0: 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f
    0d00: 9a d0 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc
    0d10: cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f
    0d20: 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66
    0d30: 41 0f 38 dc fb f3 44 0f 6f 9a e0 01 00 00 66 41
    0d40: 0f 38 dd c3 66 41 0f 38 dd cb 66 41 0f 38 dd d3
    0d50: 66 41 0f 38 dd db 66 41 0f 38 dd e3 66 41 0f 38
    0d60: dd eb 66 41 0f 38 dd f3 66 41 0f 38 dd fb f3 45
    0d70: 0f 6f 19 66 41 0f ef c3 f3 45 0f 6f 59 10 66 41
    0d80: 0f ef cb f3 45 0f 6f 59 20 66 41 0f ef d3 f3 45
    0d90: 0f 6f 59 30 66 41 0f ef db f3 45 0f 6f 59 40 66
    0da0: 41 0f ef e3 f3 45 0f 6f 59 50 66 41 0f ef eb f3
    0db0: 45 0f 6f 59 60 66 41 0f ef f3 f3 45 0f 6f 59 70
    0dc0: 66 41 0f ef fb f3 41 0f 7f 01 66 41 0f 38 00 c7
    0dd0: 66 41 0f ef c0 f3 41 0f 7f 49 10 66 41 0f 38 00
    0de0: cf f3 41 0f 7f 51 20 66 41 0f 38 00 d7 f3 41 0f
    0df0: 7f 59 30 66 41 0f 38 00 df f3 41 0f 7f 61 40 66
    0e00: 41 0f 38 00 e7 f3 41 0f 7f 69 50 66 41 0f 38 00
    0e10: ef f3 41 0f 7f 71 60 66 41 0f 38 00 f7 f3 41 0f
    0e20: 7f 79 70 66 41 0f 38 00 ff f3 0f 7f 85 60 fe ff
    0e30: ff f3 0f 7f 8d 70 fe ff ff f3 0f 7f 95 80 fe ff
    0e40: ff f3 0f 7f 9d 90 fe ff ff f3 0f 7f a5 a0 fe ff
    0e50: ff f3 0f 7f ad b0 fe ff ff f3 0f 7f b5 c0 fe ff
    0e60: ff f3 0f 7f bd d0 fe ff ff 49 81 c1 80 00 00 00
    0e70: e9 3f f8 ff ff f3 44 0f 6f 9d 60 fe ff ff f3 44
    0e80: 0f 6f 02 f3 44 0f 6f 52 10 45 0f 28 c8 66 45 0f
    0e90: 70 e3 4e 66 45 0f ef e3 66 45 0f 3a 44 d4 00 66
    0ea0: 45 0f 3a 44 c3 00 66 45 0f 3a 44 cb 11 f3 44 0f
    0eb0: 6f 9d 70 fe ff ff f3 44 0f 6f 62 20 45 0f 28 ec
    0ec0: 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a
    0ed0: 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45
    0ee0: 0f ef dc f3 44 0f 6f 62 30 66 45 0f 3a 44 e3 00
    0ef0: 66 45 0f ef d4 f3 44 0f 6f 9d 80 fe ff ff f3 44
    0f00: 0f 6f 62 40 45 0f 28 ec 66 45 0f 3a 44 e3 00 66
    0f10: 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd
    0f20: 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f 62
    0f30: 50 66 45 0f 3a 44 e3 00 66 45 0f ef d4 f3 44 0f
    0f40: 6f 9d 90 fe ff ff f3 44 0f 6f 62 60 45 0f 28 ec
    0f50: 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a
    0f60: 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45
    0f70: 0f ef dc f3 44 0f 6f 62 70 66 45 0f 3a 44 e3 00
    0f80: 66 45 0f ef d4 f3 44 0f 6f 9d a0 fe ff ff f3 44
    0f90: 0f 6f a2 80 00 00 00 45 0f 28 ec 66 45 0f 3a 44
    0fa0: e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45
    0fb0: 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44
    0fc0: 0f 6f a2 90 00 00 00 66 45 0f 3a 44 e3 00 66 45
    0fd0: 0f ef d4 f3 44 0f 6f 9d b0 fe ff ff f3 44 0f 6f
    0fe0: a2 a0 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00
    0ff0: 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef
    1000: cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f
    1010: a2 b0 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef
    1020: d4 f3 44 0f 6f 9d c0 fe ff ff f3 44 0f 6f a2 c0
    1030: 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45
    1040: 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66
    1050: 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 d0
    1060: 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 f3
    1070: 44 0f 6f 9d d0 fe ff ff f3 44 0f 6f a2 e0 00 00
    1080: 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef
    1090: c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f
    10a0: 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 f0 00 00
    10b0: 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 66 45 0f
    10c0: ef d0 66 45 0f ef d1 45 0f 28 da 66 41 0f 73 da
    10d0: 08 66 41 0f 73 fb 08 66 45 0f ef ca 66 45 0f ef
    10e0: c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45 0f 70
    10f0: c0 4e 66 45 0f ef c3 45 0f 28 de 66 45 0f 3a 44
    1100: d8 01 66 45 0f 70 c0 4e 66 45 0f ef c3 66 45 0f
    1110: ef c1 4d 85 d2 0f 84 be 01 00 00 48 81 e8 07 00
    1120: 00 00 f3 0f 6f 8a 10 01 00 00 f3 0f 6f 92 20 01
    1130: 00 00 f3 0f 6f 9a 30 01 00 00 f3 0f 6f a2 40 01
    1140: 00 00 f3 0f 6f aa 50 01 00 00 f3 0f 6f b2 60 01
    1150: 00 00 f3 0f 6f ba 70 01 00 00 49 81 fa 10 00 00
    1160: 00 0f 82 72 01 00 00 49 81 ea 10 00 00 00 f3 0f
    1170: 6f 85 e0 fe ff ff 48 81 c0 01 00 00 00 89 c6 31
    1180: fe 0f ce 89 b5 ec fe ff ff 66 0f 38 dc c1 66 0f
    1190: 38 dc c2 66 0f 38 dc c3 66 0f 38 dc c4 66 0f 38
    11a0: dc c5 66 0f 38 dc c6 66 0f 38 dc c7 f3 44 0f 6f
    11b0: 9a 80 01 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a
    11c0: 90 01 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a a0
    11d0: 01 00 00 49 81 fb 0c 00 00 00 0f 82 42 00 00 00
    11e0: 66 41 0f 38 dc c3 f3 44 0f 6f 9a b0 01 00 00 66
    11f0: 41 0f 38 dc c3 f3 44 0f 6f 9a c0 01 00 00 0f 84
    1200: 1e 00 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a d0
    1210: 01 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a e0 01
    1220: 00 00 66 41 0f 38 dd c3 f3 45 0f 6f 19 66 41 0f
    1230: ef c3 f3 41 0f 7f 01 66 41 0f 38 00 c7 66 41 0f
    1240: ef c0 f3 44 0f 6f 82 e0 00 00 00 45 0f 28 c8 f3
    1250: 44 0f 6f 92 f0 00 00 00 66 44 0f 3a 44 c0 00 66
    1260: 44 0f 3a 44 c8 11 66 44 0f 70 d8 4e 66 44 0f ef
    1270: d8 66 45 0f 3a 44 d3 00 66 45 0f ef d0 66 45 0f
    1280: ef d1 45 0f 28 da 66 41 0f 73 da 08 66 41 0f 73
    1290: fb 08 66 45 0f ef ca 66 45 0f ef c3 45 0f 28 de
    12a0: 66 45 0f 3a 44 d8 01 66 45 0f 70 c0 4e 66 45 0f
    12b0: ef c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45 0f
    12c0: 70 c0 4e 66 45 0f ef c3 66 45 0f ef c1 49 81 c1
    12d0: 10 00 00 00 e9 81 fe ff ff f3 45 0f 7f 00 f3 0f
    12e0: 6f b5 60 ff ff ff f3 0f 6f bd 70 ff ff ff f3 44
    12f0: 0f 6f 45 80 f3 44 0f 6f 4d 90 f3 44 0f 6f 55 a0
    1300: f3 44 0f 6f 5d b0 f3 44 0f 6f 65 c0 f3 44 0f 6f
    1310: 6d d0 f3 44 0f 6f 75 e0 f3 44 0f 6f 7d f0 31 c0
    1320: 48 81 c4 c0 01 00 00 5d c3 00 0f 0e 0d 0c 0b 0a
    1330: 09 08 07 06 05 04 03 02 01 00 00 00 01 00 00 00
    1340: 00 00 00 00 00 00 00 00 00 00 00 c2 00
}

chunk @mrt_aes_gcm_decrypt {
    0000: 55 48 89 e5 48 81 ec 40 01 00 00 f3 0f 7f b5 60
    0010: ff ff ff f3 0f 7f bd 70 ff ff ff f3 44 0f 7f 45
    0020: 80 f3 44 0f 7f 4d 90 f3 44 0f 7f 55 a0 f3 44 0f
    0030: 7f 5d b0 f3 44 0f 7f 65 c0 f3 44 0f 7f 6d d0 f3
    0040: 44 0f 7f 75 e0 f3 44 0f 7f 7d f0 49 89 ca 49 c1
    0050: ea 08 49 89 cb 49 81 e3 ff 00 00 00 48 8d 0d b9
    0060: 0a 00 00 f3 44 0f 6f 39 48 8d 0d bf 0a 00 00 f3
    0070: 44 0f 6f 31 f3 45 0f 6f 00 66 45 0f ef c9 66 45
    0080: 0f ef d2 f3 41 0f 6f 40 10 41 8b 40 1c f3 44 0f
    0090: 6f 9a 00 01 00 00 8b ba 0c 01 00 00 0f c8 0f cf
    00a0: 4c 89 d6 48 c1 ee 04 48 01 c6 0f ce 41 89 70 1c
    00b0: 66 44 0f ef d8 f3 44 0f 7f 9d e0 fe ff ff 48 81
    00c0: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 ec fe ff
    00d0: ff 49 81 fa 80 00 00 00 0f 82 33 08 00 00 f3 44
    00e0: 0f 7f 9d f0 fe ff ff 48 81 c0 01 00 00 00 89 c6
    00f0: 31 fe 0f ce 89 b5 fc fe ff ff f3 44 0f 7f 9d 00
    0100: ff ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce
    0110: 89 b5 0c ff ff ff f3 44 0f 7f 9d 10 ff ff ff 48
    0120: 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 1c ff
    0130: ff ff f3 44 0f 7f 9d 20 ff ff ff 48 81 c0 01 00
    0140: 00 00 89 c6 31 fe 0f ce 89 b5 2c ff ff ff f3 44
    0150: 0f 7f 9d 30 ff ff ff 48 81 c0 01 00 00 00 89 c6
    0160: 31 fe 0f ce 89 b5 3c ff ff ff f3 44 0f 7f 9d 40
    0170: ff ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce
    0180: 89 b5 4c ff ff ff f3 44 0f 7f 9d 50 ff ff ff 48
    0190: 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 5c ff
    01a0: ff ff 49 81 fa 80 00 00 00 0f 82 5b 07 00 00 49
    01b0: 81 ea 80 00 00 00 f3 0f 6f 85 e0 fe ff ff f3 0f
    01c0: 6f 8d f0 fe ff ff f3 0f 6f 95 00 ff ff ff f3 0f
    01d0: 6f 9d 10 ff ff ff f3 0f 6f a5 20 ff ff ff f3 0f
    01e0: 6f ad 30 ff ff ff f3 0f 6f b5 40 ff ff ff f3 0f
    01f0: 6f bd 50 ff ff ff f3 45 0f 6f 19 66 45 0f 38 00
    0200: df 66 45 0f ef d8 f3 44 0f 6f 02 f3 44 0f 6f 52
    0210: 10 45 0f 28 c8 66 45 0f 70 e3 4e 66 45 0f ef e3
    0220: 66 45 0f 3a 44 d4 00 66 45 0f 3a 44 c3 00 66 45
    0230: 0f 3a 44 cb 11 f3 44 0f 6f 9a 10 01 00 00 66 41
    0240: 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3
    0250: 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38
    0260: dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 45
    0270: 0f 6f 59 10 66 45 0f 38 00 df f3 44 0f 6f 62 20
    0280: 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4
    0290: 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70
    02a0: e3 4e 66 45 0f ef dc f3 44 0f 6f 62 30 66 45 0f
    02b0: 3a 44 e3 00 66 45 0f ef d4 48 81 c0 01 00 00 00
    02c0: 89 c6 31 fe 0f ce 89 b5 ec fe ff ff f3 44 0f 6f
    02d0: 9a 20 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc
    02e0: cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f
    02f0: 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66
    0300: 41 0f 38 dc fb f3 45 0f 6f 59 20 66 45 0f 38 00
    0310: df f3 44 0f 6f 62 40 45 0f 28 ec 66 45 0f 3a 44
    0320: e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45
    0330: 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44
    0340: 0f 6f 62 50 66 45 0f 3a 44 e3 00 66 45 0f ef d4
    0350: 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 fc
    0360: fe ff ff f3 44 0f 6f 9a 30 01 00 00 66 41 0f 38
    0370: dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41
    0380: 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb
    0390: 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 45 0f 6f
    03a0: 59 30 66 45 0f 38 00 df f3 44 0f 6f 62 60 45 0f
    03b0: 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45
    03c0: 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e
    03d0: 66 45 0f ef dc f3 44 0f 6f 62 70 66 45 0f 3a 44
    03e0: e3 00 66 45 0f ef d4 48 81 c0 01 00 00 00 89 c6
    03f0: 31 fe 0f ce 89 b5 0c ff ff ff f3 44 0f 6f 9a 40
    0400: 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66
    0410: 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc
    0420: e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f
    0430: 38 dc fb f3 45 0f 6f 59 40 66 45 0f 38 00 df f3
    0440: 44 0f 6f a2 80 00 00 00 45 0f 28 ec 66 45 0f 3a
    0450: 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66
    0460: 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3
    0470: 44 0f 6f a2 90 00 00 00 66 45 0f 3a 44 e3 00 66
    0480: 45 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f
    0490: ce 89 b5 1c ff ff ff f3 44 0f 6f 9a 50 01 00 00
    04a0: 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38
    04b0: dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41
    04c0: 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb
    04d0: f3 45 0f 6f 59 50 66 45 0f 38 00 df f3 44 0f 6f
    04e0: a2 a0 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00
    04f0: 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef
    0500: cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f
    0510: a2 b0 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef
    0520: d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    0530: 2c ff ff ff f3 44 0f 6f 9a 60 01 00 00 66 41 0f
    0540: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    0550: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    0560: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 45 0f
    0570: 6f 59 60 66 45 0f 38 00 df f3 44 0f 6f a2 c0 00
    0580: 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f
    0590: ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45
    05a0: 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 d0 00
    05b0: 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81
    05c0: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 3c ff ff
    05d0: ff f3 44 0f 6f 9a 70 01 00 00 66 41 0f 38 dc c3
    05e0: 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38
    05f0: dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41
    0600: 0f 38 dc f3 66 41 0f 38 dc fb f3 45 0f 6f 59 70
    0610: 66 45 0f 38 00 df f3 44 0f 6f a2 e0 00 00 00 45
    0620: 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66
    0630: 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3
    0640: 4e 66 45 0f ef dc f3 44 0f 6f a2 f0 00 00 00 66
    0650: 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81 c0 01 00
    0660: 00 00 89 c6 31 fe 0f ce 89 b5 4c ff ff ff f3 44
    0670: 0f 6f 9a 80 01 00 00 66 41 0f 38 dc c3 66 41 0f
    0680: 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66
    0690: 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc
    06a0: f3 66 41 0f 38 dc fb 48 81 c0 01 00 00 00 89 c6
    06b0: 31 fe 0f ce 89 b5 5c ff ff ff 66 45 0f ef d0 66
    06c0: 45 0f ef d1 45 0f 28 da 66 41 0f 73 da 08 66 41
    06d0: 0f 73 fb 08 66 45 0f ef ca 66 45 0f ef c3 45 0f
    06e0: 28 de 66 45 0f 3a 44 d8 01 66 45 0f 70 c0 4e 66
    06f0: 45 0f ef c3 f3 44 0f 6f 9a 90 01 00 00 66 41 0f
    0700: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    0710: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    0720: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 45 0f 28
    0730: de 66 45 0f 3a 44 d8 01 66 45 0f 70 c0 4e 66 45
    0740: 0f ef c3 66 45 0f ef c1 f3 44 0f 6f 9a a0 01 00
    0750: 00 49 81 fb 0c 00 00 00 0f 82 ea 00 00 00 66 41
    0760: 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3
    0770: 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38
    0780: dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44
    0790: 0f 6f 9a b0 01 00 00 66 41 0f 38 dc c3 66 41 0f
    07a0: 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66
    07b0: 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc
    07c0: f3 66 41 0f 38 dc fb f3 44 0f 6f 9a c0 01 00 00
    07d0: 0f 84 72 00 00 00 66 41 0f 38 dc c3 66 41 0f 38
    07e0: dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41
    07f0: 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3
    0800: 66 41 0f 38 dc fb f3 44 0f 6f 9a d0 01 00 00 66
    0810: 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc
    0820: d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f
    0830: 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3
    0840: 44 0f 6f 9a e0 01 00 00 66 41 0f 38 dd c3 66 41
    0850: 0f 38 dd cb 66 41 0f 38 dd d3 66 41 0f 38 dd db
    0860: 66 41 0f 38 dd e3 66 41 0f 38 dd eb 66 41 0f 38
    0870: dd f3 66 41 0f 38 dd fb f3 45 0f 6f 19 66 41 0f
    0880: ef c3 f3 45 0f 6f 59 10 66 41 0f ef cb f3 45 0f
    0890: 6f 59 20 66 41 0f ef d3 f3 45 0f 6f 59 30 66 41
    08a0: 0f ef db f3 45 0f 6f 59 40 66 41 0f ef e3 f3 45
    08b0: 0f 6f 59 50 66 41 0f ef eb f3 45 0f 6f 59 60 66
    08c0: 41 0f ef f3 f3 45 0f 6f 59 70 66 41 0f ef fb f3
    08d0: 41 0f 7f 01 f3 41 0f 7f 49 10 f3 41 0f 7f 51 20
    08e0: f3 41 0f 7f 59 30 f3 41 0f 7f 61 40 f3 41 0f 7f
    08f0: 69 50 f3 41 0f 7f 71 60 f3 41 0f 7f 79 70 49 81
    0900: c1 80 00 00 00 e9 98 f8 ff ff 48 81 e8 07 00 00
    0910: 00 f3 0f 6f 8a 10 01 00 00 f3 0f 6f 92 20 01 00
    0920: 00 f3 0f 6f 9a 30 01 00 00 f3 0f 6f a2 40 01 00
    0930: 00 f3 0f 6f aa 50 01 00 00 f3 0f 6f b2 60 01 00
    0940: 00 f3 0f 6f ba 70 01 00 00 49 81 fa 10 00 00 00
    0950: 0f 82 76 01 00 00 49 81 ea 10 00 00 00 f3 41 0f
    0960: 6f 01 44 0f 28 e0 66 41 0f 38 00 c7 66 41 0f ef
    0970: c0 f3 44 0f 6f 82 e0 00 00 00 45 0f 28 c8 f3 44
    0980: 0f 6f 92 f0 00 00 00 66 44 0f 3a 44 c0 00 66 44
    0990: 0f 3a 44 c8 11 66 44 0f 70 d8 4e 66 44 0f ef d8
    09a0: 66 45 0f 3a 44 d3 00 66 45 0f ef d0 66 45 0f ef
    09b0: d1 45 0f 28 da 66 41 0f 73 da 08 66 41 0f 73 fb
    09c0: 08 66 45 0f ef ca 66 45 0f ef c3 45 0f 28 de 66
    09d0: 45 0f 3a 44 d8 01 66 45 0f 70 c0 4e 66 45 0f ef
    09e0: c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45 0f 70
    09f0: c0 4e 66 45 0f ef c3 66 45 0f ef c1 f3 0f 6f 85
    0a00: e0 fe ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f
    0a10: ce 89 b5 ec fe ff ff 66 0f 38 dc c1 66 0f 38 dc
    0a20: c2 66 0f 38 dc c3 66 0f 38 dc c4 66 0f 38 dc c5
    0a30: 66 0f 38 dc c6 66 0f 38 dc c7 f3 44 0f 6f 9a 80
    0a40: 01 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a 90 01
    0a50: 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a a0 01 00
    0a60: 00 49 81 fb 0c 00 00 00 0f 82 42 00 00 00 66 41
    0a70: 0f 38 dc c3 f3 44 0f 6f 9a b0 01 00 00 66 41 0f
    0a80: 38 dc c3 f3 44 0f 6f 9a c0 01 00 00 0f 84 1e 00
    0a90: 00 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a d0 01 00
    0aa0: 00 66 41 0f 38 dc c3 f3 44 0f 6f 9a e0 01 00 00
    0ab0: 66 41 0f 38 dd c3 66 41 0f ef c4 f3 41 0f 7f 01
    0ac0: 49 81 c1 10 00 00 00 e9 7d fe ff ff f3 45 0f 7f
    0ad0: 00 f3 0f 6f b5 60 ff ff ff f3 0f 6f bd 70 ff ff
    0ae0: ff f3 44 0f 6f 45 80 f3 44 0f 6f 4d 90 f3 44 0f
    0af0: 6f 55 a0 f3 44 0f 6f 5d b0 f3 44 0f 6f 65 c0 f3
    0b00: 44 0f 6f 6d d0 f3 44 0f 6f 75 e0 f3 44 0f 6f 7d
    0b10: f0 31 c0 48 81 c4 40 01 00 00 5d c3 0f 0e 0d 0c
    0b20: 0b 0a 09 08 07 06 05 04 03 02 01 00 00 00 01 00
    0b30: 00 00 00 00 00 00 00 00 00 00 00 00 00 c2 00
}

chunk @mrt_aes_gcm_finish {
    0000: 55 48 89 e5 48 81 ec c0 00 00 00 f3 0f 7f b5 60
    0010: ff ff ff f3 0f 7f bd 70 ff ff ff f3 44 0f 7f 45
    0020: 80 f3 44 0f 7f 4d 90 f3 44 0f 7f 55 a0 f3 44 0f
    0030: 7f 5d b0 f3 44 0f 7f 65 c0 f3 44 0f 7f 6d d0 f3
    0040: 44 0f 7f 75 e0 f3 44 0f 7f 7d f0 48 8d 05 1c 01
    0050: 00 00 f3 44 0f 6f 38 48 8d 05 22 01 00 00 f3 44
    0060: 0f 6f 30 f3 45 0f 6f 00 f3 45 0f 6f 68 20 49 c1
    0070: e1 03 48 c1 e1 03 66 49 0f 6e c1 66 48 0f 3a 22
    0080: c1 01 66 41 0f ef c0 f3 44 0f 6f 82 e0 00 00 00
    0090: 45 0f 28 c8 f3 44 0f 6f 92 f0 00 00 00 66 44 0f
    00a0: 3a 44 c0 00 66 44 0f 3a 44 c8 11 66 44 0f 70 d8
    00b0: 4e 66 44 0f ef d8 66 45 0f 3a 44 d3 00 66 45 0f
    00c0: ef d0 66 45 0f ef d1 45 0f 28 da 66 41 0f 73 da
    00d0: 08 66 41 0f 73 fb 08 66 45 0f ef ca 66 45 0f ef
    00e0: c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45 0f 70
    00f0: c0 4e 66 45 0f ef c3 45 0f 28 de 66 45 0f 3a 44
    0100: d8 01 66 45 0f 70 c0 4e 66 45 0f ef c3 66 45 0f
    0110: ef c1 66 45 0f 38 00 c7 66 45 0f ef c5 f3 45 0f
    0120: 7f 00 f3 0f 6f b5 60 ff ff ff f3 0f 6f bd 70 ff
    0130: ff ff f3 44 0f 6f 45 80 f3 44 0f 6f 4d 90 f3 44
    0140: 0f 6f 55 a0 f3 44 0f 6f 5d b0 f3 44 0f 6f 65 c0
    0150: f3 44 0f 6f 6d d0 f3 44 0f 6f 75 e0 f3 44 0f 6f
    0160: 7d f0 31 c0 48 81 c4 c0 00 00 00 5d c3 00 0f 0e
    0170: 0d 0c 0b 0a 09 08 07 06 05 04 03 02 01 00 00 00
    0180: 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 c2
    0190: 00
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
    arm64.prologue 112
    arm64.storeSlotReg slot11, x28
    arm64.storeSlotReg slot10, x27
    arm64.storeSlotReg slot9, x26
    arm64.storeSlotReg slot8, x25
    arm64.storeSlotReg slot7, x24
    arm64.storeSlotReg slot6, x23
    arm64.storeSlotReg slot5, x22
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot1, x20
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x21, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x23, x0
    arm64.movImm x1, 16
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x1, 352
    arm64.movRegReg x0, x20
    arm64.bl __managed_resize
    arm64.movImm x1, 432
    arm64.movRegReg x0, x21
    arm64.bl __managed_resize
    arm64.movImm x1, 48
    arm64.movRegReg x0, x22
    arm64.bl __managed_resize
    arm64.movImm x1, 64
    arm64.movRegReg x0, x23
    arm64.bl __managed_resize
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __cpufeat_aes_cipher_expand_key
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x20
    arm64.bl __cpufeat_aes_cipher_gcm_init
    arm64.movImm x3, 0
    arm64.movImm x4, 32
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x23
    arm64.bl __cpufeat_aes_cipher_gcm_hash
    arm64.movImm x1, 0
    arm64.movImm x4, 64
    arm64.movRegReg x0, x23
    arm64.movRegReg x2, x21
    arm64.movRegReg x3, x22
    arm64.bl __cpufeat_aes_cipher_gcm_encrypt
    arm64.movImm x1, 0
    arm64.movImm x4, 64
    arm64.movRegReg x0, x23
    arm64.movRegReg x2, x21
    arm64.movRegReg x3, x22
    arm64.bl __cpufeat_aes_cipher_gcm_decrypt
    arm64.movImm x2, 32
    arm64.movImm x3, 64
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x21
    arm64.bl __cpufeat_aes_cipher_gcm_finish
    arm64.loadRegBaseDisp.word64 x19, [x22 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_1  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x19, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x28, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x19, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
  __il_body:
    arm64.movRegReg x0, x27
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
  exitdrop#2:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitdrop#3:
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitdrop#4:
    arm64.loadRegSlot x0, slot1
    arm64.bl __managed_decref
  exitdrop#5:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.loadRegSlot x23, slot6
    arm64.loadRegSlot x24, slot7
    arm64.loadRegSlot x25, slot8
    arm64.loadRegSlot x26, slot9
    arm64.loadRegSlot x27, slot10
    arm64.loadRegSlot x28, slot11
    arm64.epilogue 112
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_init {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.loadRegBaseDisp.word64 x21, [x20 + 8]
    arm64.cmp x21, 352
    arm64.b.lt gibad
  scbounded:
    arm64.cmp x21, 480
    arm64.b.gt gibad
  scstepped:
    arm64.and x0, x21, 63
    arm64.cmp x0, 32
    arm64.b.ne gibad
  gikeyblock:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.lsr x1, x21, 1
    arm64.add x1, x1, 256
    arm64.cmp x0, x1
    arm64.b.ne gibad
  gicrypt:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.lsr x0, x21, 5
    arm64.sub x0, x0, 1
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x20 + 0]
    arm64.movImm x3, 0
    arm64.bl mrt_aes_gcm_init
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  gibad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_hash {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x2
    arm64.movRegReg x22, x3
    arm64.movRegReg x23, x4
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x0, 432
    arm64.b.lt ghbad
  kbbounded:
    arm64.cmp x0, 496
    arm64.b.gt ghbad
  kbstepped:
    arm64.and x0, x0, 31
    arm64.cmp x0, 16
    arm64.b.ne ghbad
  gkmessage:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 48
    arm64.b.ne ghbad
  ghspan:
    arm64.and x0, x23, 15
    arm64.cmp x0, 0
    arm64.b.ne ghbad
  ghaligned:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.cmp x22, x0
    arm64.b.hi ghbad
  ghinside:
    arm64.sub x0, x0, x22
    arm64.cmp x0, x23
    arm64.b.lo ghbad
  ghcrypt:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.loadRegBaseDisp.word64 x1, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.add x3, x0, x22
    arm64.movRegReg x0, x23
    arm64.bl mrt_aes_gcm_hash
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  ghbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_encrypt {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x2
    arm64.movRegReg x22, x3
    arm64.movRegReg x23, x4
    arm64.loadRegBaseDisp.word64 x24, [x21 + 8]
    arm64.cmp x24, 432
    arm64.b.lt gcbad
  kbbounded:
    arm64.cmp x24, 496
    arm64.b.gt gcbad
  kbstepped:
    arm64.and x0, x24, 31
    arm64.cmp x0, 16
    arm64.b.ne gcbad
  gkmessage:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.cmp x0, 48
    arm64.b.ne gcbad
  gcspan:
    arm64.and x0, x23, 15
    arm64.cmp x0, 0
    arm64.b.ne gcbad
  gcaligned:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x20, x0
    arm64.b.hi gcbad
  gcinside:
    arm64.sub x0, x0, x20
    arm64.cmp x0, x23
    arm64.b.lo gcbad
  gccrypt:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x0, x22
    arm64.bl __managed_cow_detach
    arm64.sub x0, x24, 256
    arm64.lsr x0, x0, 4
    arm64.sub x0, x0, 1
    arm64.lsl x1, x23, 8
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.add x3, x3, x20
    arm64.bl mrt_aes_gcm_encrypt
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
  gcbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_decrypt {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x2
    arm64.movRegReg x22, x3
    arm64.movRegReg x23, x4
    arm64.loadRegBaseDisp.word64 x24, [x21 + 8]
    arm64.cmp x24, 432
    arm64.b.lt gcbad
  kbbounded:
    arm64.cmp x24, 496
    arm64.b.gt gcbad
  kbstepped:
    arm64.and x0, x24, 31
    arm64.cmp x0, 16
    arm64.b.ne gcbad
  gkmessage:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.cmp x0, 48
    arm64.b.ne gcbad
  gcspan:
    arm64.and x0, x23, 15
    arm64.cmp x0, 0
    arm64.b.ne gcbad
  gcaligned:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x20, x0
    arm64.b.hi gcbad
  gcinside:
    arm64.sub x0, x0, x20
    arm64.cmp x0, x23
    arm64.b.lo gcbad
  gccrypt:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x0, x22
    arm64.bl __managed_cow_detach
    arm64.sub x0, x24, 256
    arm64.lsr x0, x0, 4
    arm64.sub x0, x0, 1
    arm64.lsl x1, x23, 8
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.add x3, x3, x20
    arm64.bl mrt_aes_gcm_decrypt
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
  gcbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_finish {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x2
    arm64.movRegReg x22, x3
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x0, 432
    arm64.b.lt gfbad
  kbbounded:
    arm64.cmp x0, 496
    arm64.b.gt gfbad
  kbstepped:
    arm64.and x0, x0, 31
    arm64.cmp x0, 16
    arm64.b.ne gfbad
  gkmessage:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 48
    arm64.b.ne gfbad
  gfcrypt:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.loadRegBaseDisp.word64 x1, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.movRegReg x0, x21
    arm64.movRegReg x3, x22
    arm64.bl mrt_aes_gcm_finish
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  gfbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}

chunk @mrt_aes_gcm_init {
    0000: 47 18 80 d2 e7 1c 48 d3 f7 1c 08 4e 27 00 80 d2
    0010: f7 1c 18 4e 18 1f 38 6e 28 00 04 91 0a 04 00 91
    0020: 40 00 c0 3d 00 01 80 3d 42 40 00 91 08 41 00 91
    0030: 4a 05 00 d1 6a ff ff b5 26 00 04 91 00 1c 20 6e
    0040: 1f 30 00 f1 03 02 00 54 00 01 00 54 db 00 c0 3d
    0050: 60 4b 28 4e 00 68 28 4e db 04 c0 3d 60 4b 28 4e
    0060: 00 68 28 4e c6 80 00 91 db 00 c0 3d 60 4b 28 4e
    0070: 00 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    0080: c6 80 00 91 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    0090: db 04 c0 3d 60 4b 28 4e 00 68 28 4e db 08 c0 3d
    00a0: 60 4b 28 4e 00 68 28 4e db 0c c0 3d 60 4b 28 4e
    00b0: 00 68 28 4e db 10 c0 3d 60 4b 28 4e 00 68 28 4e
    00c0: db 14 c0 3d 60 4b 28 4e 00 68 28 4e db 18 c0 3d
    00d0: 60 4b 28 4e 00 68 28 4e db 1c c0 3d 60 4b 28 4e
    00e0: 00 68 28 4e db 20 c0 3d 60 4b 28 4e 00 68 28 4e
    00f0: db 24 c0 3d 60 4b 28 4e db 28 c0 3d 00 1c 3b 6e
    0100: 00 08 20 4e 07 3c 08 4e e7 fc 7f 93 f4 1c 08 4e
    0110: f4 1c 18 4e 94 1e 37 4e 15 04 41 6f b5 42 18 6e
    0120: 00 54 41 4f 00 1c 34 6e 00 1c 35 6e 20 38 80 3d
    0130: 13 40 00 6e 73 1e 20 6e 33 3c 80 3d 28 00 03 91
    0140: e4 00 80 d2 84 04 00 d1 34 38 c0 3d 35 3c c0 3d
    0150: 13 40 00 6e 13 1c 33 6e 11 e0 f4 0e 10 e0 f4 4e
    0160: 72 e2 f5 0e 52 1e 30 6e 52 1e 31 6e 53 42 18 6e
    0170: 12 43 12 6e 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e
    0180: 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e
    0190: 31 42 11 6e 10 1e 31 6e 00 1e b0 4e 00 01 80 3d
    01a0: 13 40 00 6e 73 1e 20 6e 13 05 80 3d 08 81 00 d1
    01b0: a4 fc ff b5 e0 03 1f aa c0 03 5f d6
}

chunk @mrt_aes_gcm_hash {
    0000: 47 18 80 d2 e7 1c 48 d3 f7 1c 08 4e 27 00 80 d2
    0010: f7 1c 18 4e 18 1f 38 6e 50 00 c0 3d a0 11 00 b4
    0020: 1f 00 02 f1 e3 0d 00 54 00 00 02 d1 60 00 c0 3d
    0030: 61 04 c0 3d 62 08 c0 3d 63 0c c0 3d 64 10 c0 3d
    0040: 65 14 c0 3d 66 18 c0 3d 67 1c c0 3d 63 00 02 91
    0050: 34 00 c0 3d 35 04 c0 3d 00 08 20 4e 00 1c 30 6e
    0060: 13 40 00 6e 13 1c 33 6e 11 e0 f4 0e 10 e0 f4 4e
    0070: 72 e2 f5 0e 34 08 c0 3d 35 0c c0 3d 21 08 20 4e
    0080: 33 40 01 6e 33 1c 33 6e 36 e0 f4 0e 31 1e 36 6e
    0090: 36 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    00a0: 34 10 c0 3d 35 14 c0 3d 42 08 20 4e 53 40 02 6e
    00b0: 53 1c 33 6e 56 e0 f4 0e 31 1e 36 6e 56 e0 f4 4e
    00c0: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 34 18 c0 3d
    00d0: 35 1c c0 3d 63 08 20 4e 73 40 03 6e 73 1c 33 6e
    00e0: 76 e0 f4 0e 31 1e 36 6e 76 e0 f4 4e 10 1e 36 6e
    00f0: 76 e2 f5 0e 52 1e 36 6e 34 20 c0 3d 35 24 c0 3d
    0100: 84 08 20 4e 93 40 04 6e 93 1c 33 6e 96 e0 f4 0e
    0110: 31 1e 36 6e 96 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e
    0120: 52 1e 36 6e 34 28 c0 3d 35 2c c0 3d a5 08 20 4e
    0130: b3 40 05 6e b3 1c 33 6e b6 e0 f4 0e 31 1e 36 6e
    0140: b6 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0150: 34 30 c0 3d 35 34 c0 3d c6 08 20 4e d3 40 06 6e
    0160: d3 1c 33 6e d6 e0 f4 0e 31 1e 36 6e d6 e0 f4 4e
    0170: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 34 38 c0 3d
    0180: 35 3c c0 3d e7 08 20 4e f3 40 07 6e f3 1c 33 6e
    0190: f6 e0 f4 0e 31 1e 36 6e f6 e0 f4 4e 10 1e 36 6e
    01a0: 76 e2 f5 0e 52 1e 36 6e 52 1e 30 6e 52 1e 31 6e
    01b0: 53 42 18 6e 12 43 12 6e 10 1e 32 6e 31 1e 33 6e
    01c0: 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e
    01d0: 31 1e 33 6e 31 42 11 6e 10 1e 31 6e 91 ff ff 17
    01e0: 34 38 c0 3d 35 3c c0 3d 1f 40 00 f1 23 03 00 54
    01f0: 00 40 00 d1 60 00 c0 3d 63 40 00 91 00 08 20 4e
    0200: 00 1c 30 6e 13 40 00 6e 13 1c 33 6e 11 e0 f4 0e
    0210: 10 e0 f4 4e 72 e2 f5 0e 52 1e 30 6e 52 1e 31 6e
    0220: 53 42 18 6e 12 43 12 6e 10 1e 32 6e 31 1e 33 6e
    0230: 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e
    0240: 31 1e 33 6e 31 42 11 6e 10 1e 31 6e e7 ff ff 17
    0250: 50 00 80 3d e0 03 1f aa c0 03 5f d6
}

chunk @mrt_aes_gcm_encrypt {
    0000: 04 fc 48 d3 e5 1f 80 d2 05 00 05 8a 28 00 04 91
    0010: 47 18 80 d2 e7 1c 48 d3 f7 1c 08 4e 27 00 80 d2
    0020: f7 1c 18 4e 18 1f 38 6e 50 00 c0 3d 31 1e 31 6e
    0030: 52 1e 32 6e 5a 04 c0 3d 39 1f 39 6e 27 00 80 d2
    0040: f9 1c 1c 4e 47 1c 40 b9 e7 08 c0 5a 8a fc 44 d3
    0050: e7 00 0a 8b e7 08 c0 5a 47 1c 00 b9 5a 0b 20 6e
    0060: 5a 87 b9 4e 9f 00 02 f1 a3 30 00 54 84 00 02 d1
    0070: 40 0b 20 6e 5a 87 b9 4e 41 0b 20 6e 5a 87 b9 4e
    0080: 42 0b 20 6e 5a 87 b9 4e 43 0b 20 6e 5a 87 b9 4e
    0090: 44 0b 20 6e 5a 87 b9 4e 45 0b 20 6e 5a 87 b9 4e
    00a0: 46 0b 20 6e 5a 87 b9 4e 47 0b 20 6e 5a 87 b9 4e
    00b0: e6 03 08 aa bf 30 00 f1 03 09 00 54 80 04 00 54
    00c0: db 00 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    00d0: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    00e0: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    00f0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0100: e7 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    0110: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0120: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0130: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0140: 67 4b 28 4e e7 68 28 4e c6 80 00 91 db 00 c0 3d
    0150: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0160: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0170: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0180: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0190: db 04 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    01a0: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    01b0: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    01c0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    01d0: e7 68 28 4e c6 80 00 91 db 00 c0 3d 60 4b 28 4e
    01e0: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    01f0: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0200: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0210: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 04 c0 3d
    0220: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0230: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0240: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0250: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0260: db 08 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0270: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0280: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0290: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    02a0: e7 68 28 4e db 0c c0 3d 60 4b 28 4e 00 68 28 4e
    02b0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    02c0: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    02d0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    02e0: 67 4b 28 4e e7 68 28 4e db 10 c0 3d 60 4b 28 4e
    02f0: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0300: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0310: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0320: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 14 c0 3d
    0330: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0340: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0350: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0360: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0370: db 18 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0380: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0390: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    03a0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    03b0: e7 68 28 4e db 1c c0 3d 60 4b 28 4e 00 68 28 4e
    03c0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    03d0: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    03e0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    03f0: 67 4b 28 4e e7 68 28 4e db 20 c0 3d 60 4b 28 4e
    0400: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0410: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0420: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0430: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 24 c0 3d
    0440: 60 4b 28 4e 61 4b 28 4e 62 4b 28 4e 63 4b 28 4e
    0450: 64 4b 28 4e 65 4b 28 4e 66 4b 28 4e 67 4b 28 4e
    0460: db 28 c0 3d 00 1c 3b 6e 21 1c 3b 6e 42 1c 3b 6e
    0470: 63 1c 3b 6e 84 1c 3b 6e a5 1c 3b 6e c6 1c 3b 6e
    0480: e7 1c 3b 6e 74 00 c0 3d 75 04 c0 3d 00 1c 34 6e
    0490: 21 1c 35 6e 60 00 80 3d 61 04 80 3d 74 08 c0 3d
    04a0: 75 0c c0 3d 42 1c 34 6e 63 1c 35 6e 62 08 80 3d
    04b0: 63 0c 80 3d 74 10 c0 3d 75 14 c0 3d 84 1c 34 6e
    04c0: a5 1c 35 6e 64 10 80 3d 65 14 80 3d 74 18 c0 3d
    04d0: 75 1c c0 3d c6 1c 34 6e e7 1c 35 6e 66 18 80 3d
    04e0: 67 1c 80 3d 34 00 c0 3d 35 04 c0 3d 00 08 20 4e
    04f0: 00 1c 30 6e 13 40 00 6e 13 1c 33 6e 11 e0 f4 0e
    0500: 10 e0 f4 4e 72 e2 f5 0e 34 08 c0 3d 35 0c c0 3d
    0510: 21 08 20 4e 33 40 01 6e 33 1c 33 6e 36 e0 f4 0e
    0520: 31 1e 36 6e 36 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e
    0530: 52 1e 36 6e 34 10 c0 3d 35 14 c0 3d 42 08 20 4e
    0540: 53 40 02 6e 53 1c 33 6e 56 e0 f4 0e 31 1e 36 6e
    0550: 56 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0560: 34 18 c0 3d 35 1c c0 3d 63 08 20 4e 73 40 03 6e
    0570: 73 1c 33 6e 76 e0 f4 0e 31 1e 36 6e 76 e0 f4 4e
    0580: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 34 20 c0 3d
    0590: 35 24 c0 3d 84 08 20 4e 93 40 04 6e 93 1c 33 6e
    05a0: 96 e0 f4 0e 31 1e 36 6e 96 e0 f4 4e 10 1e 36 6e
    05b0: 76 e2 f5 0e 52 1e 36 6e 34 28 c0 3d 35 2c c0 3d
    05c0: a5 08 20 4e b3 40 05 6e b3 1c 33 6e b6 e0 f4 0e
    05d0: 31 1e 36 6e b6 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e
    05e0: 52 1e 36 6e 34 30 c0 3d 35 34 c0 3d c6 08 20 4e
    05f0: d3 40 06 6e d3 1c 33 6e d6 e0 f4 0e 31 1e 36 6e
    0600: d6 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0610: 34 38 c0 3d 35 3c c0 3d e7 08 20 4e f3 40 07 6e
    0620: f3 1c 33 6e f6 e0 f4 0e 31 1e 36 6e f6 e0 f4 4e
    0630: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 52 1e 30 6e
    0640: 52 1e 31 6e 53 42 18 6e 12 43 12 6e 10 1e 32 6e
    0650: 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e
    0660: 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e 10 1e 31 6e
    0670: 63 00 02 91 9f 00 02 f1 a2 cf ff 54 44 0a 00 b4
    0680: 34 38 c0 3d 35 3c c0 3d 9f 40 00 f1 c3 09 00 54
    0690: 84 40 00 d1 7c 00 c0 3d 40 0b 20 6e 5a 87 b9 4e
    06a0: e6 03 08 aa bf 30 00 f1 03 02 00 54 00 01 00 54
    06b0: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    06c0: 60 4b 28 4e 00 68 28 4e c6 80 00 91 db 00 c0 3d
    06d0: 60 4b 28 4e 00 68 28 4e db 04 c0 3d 60 4b 28 4e
    06e0: 00 68 28 4e c6 80 00 91 db 00 c0 3d 60 4b 28 4e
    06f0: 00 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    0700: db 08 c0 3d 60 4b 28 4e 00 68 28 4e db 0c c0 3d
    0710: 60 4b 28 4e 00 68 28 4e db 10 c0 3d 60 4b 28 4e
    0720: 00 68 28 4e db 14 c0 3d 60 4b 28 4e 00 68 28 4e
    0730: db 18 c0 3d 60 4b 28 4e 00 68 28 4e db 1c c0 3d
    0740: 60 4b 28 4e 00 68 28 4e db 20 c0 3d 60 4b 28 4e
    0750: 00 68 28 4e db 24 c0 3d 60 4b 28 4e db 28 c0 3d
    0760: 00 1c 3b 6e 00 1c 3c 6e 60 00 80 3d 00 08 20 4e
    0770: 00 1c 30 6e 13 40 00 6e 13 1c 33 6e 11 e0 f4 0e
    0780: 10 e0 f4 4e 72 e2 f5 0e 52 1e 30 6e 52 1e 31 6e
    0790: 53 42 18 6e 12 43 12 6e 10 1e 32 6e 31 1e 33 6e
    07a0: 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e
    07b0: 31 1e 33 6e 31 42 11 6e 10 1e 31 6e 63 40 00 91
    07c0: b2 ff ff 17 50 00 80 3d e0 03 1f aa c0 03 5f d6
}

chunk @mrt_aes_gcm_decrypt {
    0000: 04 fc 48 d3 e5 1f 80 d2 05 00 05 8a 28 00 04 91
    0010: 47 18 80 d2 e7 1c 48 d3 f7 1c 08 4e 27 00 80 d2
    0020: f7 1c 18 4e 18 1f 38 6e 50 00 c0 3d 31 1e 31 6e
    0030: 52 1e 32 6e 5a 04 c0 3d 39 1f 39 6e 27 00 80 d2
    0040: f9 1c 1c 4e 47 1c 40 b9 e7 08 c0 5a 8a fc 44 d3
    0050: e7 00 0a 8b e7 08 c0 5a 47 1c 00 b9 5a 0b 20 6e
    0060: 5a 87 b9 4e 9f 00 02 f1 a3 31 00 54 84 00 02 d1
    0070: 40 0b 20 6e 5a 87 b9 4e 41 0b 20 6e 5a 87 b9 4e
    0080: 42 0b 20 6e 5a 87 b9 4e 43 0b 20 6e 5a 87 b9 4e
    0090: 44 0b 20 6e 5a 87 b9 4e 45 0b 20 6e 5a 87 b9 4e
    00a0: 46 0b 20 6e 5a 87 b9 4e 47 0b 20 6e 5a 87 b9 4e
    00b0: e6 03 08 aa bf 30 00 f1 03 09 00 54 80 04 00 54
    00c0: db 00 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    00d0: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    00e0: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    00f0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0100: e7 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    0110: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0120: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0130: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0140: 67 4b 28 4e e7 68 28 4e c6 80 00 91 db 00 c0 3d
    0150: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0160: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0170: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0180: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0190: db 04 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    01a0: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    01b0: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    01c0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    01d0: e7 68 28 4e c6 80 00 91 db 00 c0 3d 60 4b 28 4e
    01e0: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    01f0: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0200: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0210: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 04 c0 3d
    0220: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0230: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0240: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0250: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0260: db 08 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0270: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0280: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0290: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    02a0: e7 68 28 4e db 0c c0 3d 60 4b 28 4e 00 68 28 4e
    02b0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    02c0: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    02d0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    02e0: 67 4b 28 4e e7 68 28 4e db 10 c0 3d 60 4b 28 4e
    02f0: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0300: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0310: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0320: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 14 c0 3d
    0330: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0340: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0350: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0360: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0370: db 18 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0380: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0390: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    03a0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    03b0: e7 68 28 4e db 1c c0 3d 60 4b 28 4e 00 68 28 4e
    03c0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    03d0: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    03e0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    03f0: 67 4b 28 4e e7 68 28 4e db 20 c0 3d 60 4b 28 4e
    0400: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0410: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0420: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0430: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 24 c0 3d
    0440: 60 4b 28 4e 61 4b 28 4e 62 4b 28 4e 63 4b 28 4e
    0450: 64 4b 28 4e 65 4b 28 4e 66 4b 28 4e 67 4b 28 4e
    0460: db 28 c0 3d 00 1c 3b 6e 21 1c 3b 6e 42 1c 3b 6e
    0470: 63 1c 3b 6e 84 1c 3b 6e a5 1c 3b 6e c6 1c 3b 6e
    0480: e7 1c 3b 6e 7d 00 c0 3d 34 00 c0 3d 35 04 c0 3d
    0490: bd 0b 20 4e bd 1f 30 6e b3 43 1d 6e b3 1f 33 6e
    04a0: b1 e3 f4 0e b0 e3 f4 4e 72 e2 f5 0e 7d 04 c0 3d
    04b0: 34 08 c0 3d 35 0c c0 3d bd 0b 20 4e b3 43 1d 6e
    04c0: b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e
    04d0: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 7d 08 c0 3d
    04e0: 34 10 c0 3d 35 14 c0 3d bd 0b 20 4e b3 43 1d 6e
    04f0: b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e
    0500: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 7d 0c c0 3d
    0510: 34 18 c0 3d 35 1c c0 3d bd 0b 20 4e b3 43 1d 6e
    0520: b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e
    0530: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 7d 10 c0 3d
    0540: 34 20 c0 3d 35 24 c0 3d bd 0b 20 4e b3 43 1d 6e
    0550: b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e
    0560: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 7d 14 c0 3d
    0570: 34 28 c0 3d 35 2c c0 3d bd 0b 20 4e b3 43 1d 6e
    0580: b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e
    0590: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 7d 18 c0 3d
    05a0: 34 30 c0 3d 35 34 c0 3d bd 0b 20 4e b3 43 1d 6e
    05b0: b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e
    05c0: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 7d 1c c0 3d
    05d0: 34 38 c0 3d 35 3c c0 3d bd 0b 20 4e b3 43 1d 6e
    05e0: b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e
    05f0: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 52 1e 30 6e
    0600: 52 1e 31 6e 53 42 18 6e 12 43 12 6e 10 1e 32 6e
    0610: 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e
    0620: 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e 10 1e 31 6e
    0630: 7c 00 c0 3d 00 1c 3c 6e 60 00 80 3d 7c 04 c0 3d
    0640: 21 1c 3c 6e 61 04 80 3d 7c 08 c0 3d 42 1c 3c 6e
    0650: 62 08 80 3d 7c 0c c0 3d 63 1c 3c 6e 63 0c 80 3d
    0660: 7c 10 c0 3d 84 1c 3c 6e 64 10 80 3d 7c 14 c0 3d
    0670: a5 1c 3c 6e 65 14 80 3d 7c 18 c0 3d c6 1c 3c 6e
    0680: 66 18 80 3d 7c 1c c0 3d e7 1c 3c 6e 67 1c 80 3d
    0690: 63 00 02 91 9f 00 02 f1 a2 ce ff 54 44 0a 00 b4
    06a0: 34 38 c0 3d 35 3c c0 3d 9f 40 00 f1 c3 09 00 54
    06b0: 84 40 00 d1 7c 00 c0 3d 40 0b 20 6e 5a 87 b9 4e
    06c0: e6 03 08 aa bf 30 00 f1 03 02 00 54 00 01 00 54
    06d0: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    06e0: 60 4b 28 4e 00 68 28 4e c6 80 00 91 db 00 c0 3d
    06f0: 60 4b 28 4e 00 68 28 4e db 04 c0 3d 60 4b 28 4e
    0700: 00 68 28 4e c6 80 00 91 db 00 c0 3d 60 4b 28 4e
    0710: 00 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    0720: db 08 c0 3d 60 4b 28 4e 00 68 28 4e db 0c c0 3d
    0730: 60 4b 28 4e 00 68 28 4e db 10 c0 3d 60 4b 28 4e
    0740: 00 68 28 4e db 14 c0 3d 60 4b 28 4e 00 68 28 4e
    0750: db 18 c0 3d 60 4b 28 4e 00 68 28 4e db 1c c0 3d
    0760: 60 4b 28 4e 00 68 28 4e db 20 c0 3d 60 4b 28 4e
    0770: 00 68 28 4e db 24 c0 3d 60 4b 28 4e db 28 c0 3d
    0780: 00 1c 3b 6e 00 1c 3c 6e 60 00 80 3d 9c 0b 20 4e
    0790: 9c 1f 30 6e 93 43 1c 6e 93 1f 33 6e 91 e3 f4 0e
    07a0: 90 e3 f4 4e 72 e2 f5 0e 52 1e 30 6e 52 1e 31 6e
    07b0: 53 42 18 6e 12 43 12 6e 10 1e 32 6e 31 1e 33 6e
    07c0: 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e
    07d0: 31 1e 33 6e 31 42 11 6e 10 1e 31 6e 63 40 00 91
    07e0: b2 ff ff 17 50 00 80 3d e0 03 1f aa c0 03 5f d6
}

chunk @mrt_aes_gcm_finish {
    0000: 47 18 80 d2 e7 1c 48 d3 f7 1c 08 4e 27 00 80 d2
    0010: f7 1c 18 4e 18 1f 38 6e 50 00 c0 3d 41 08 c0 3d
    0020: 00 f0 7d d3 63 f0 7d d3 00 1c 08 4e 60 1c 18 4e
    0030: 34 38 c0 3d 35 3c c0 3d 00 1c 30 6e 13 40 00 6e
    0040: 13 1c 33 6e 11 e0 f4 0e 10 e0 f4 4e 72 e2 f5 0e
    0050: 52 1e 30 6e 52 1e 31 6e 53 42 18 6e 12 43 12 6e
    0060: 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e
    0070: 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e
    0080: 10 1e 31 6e 10 0a 20 4e 10 1e 21 6e 50 00 80 3d
    0090: e0 03 1f aa c0 03 5f d6
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __mrt_cpu_aes_usable@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @main {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot11, x28
    arm64.storeSlotReg slot10, x27
    arm64.storeSlotReg slot9, x26
    arm64.storeSlotReg slot8, x25
    arm64.storeSlotReg slot7, x24
    arm64.storeSlotReg slot6, x23
    arm64.storeSlotReg slot5, x22
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot1, x20
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x21, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x23, x0
    arm64.movImm x1, 16
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x1, 352
    arm64.movRegReg x0, x20
    arm64.bl __managed_resize
    arm64.movImm x1, 432
    arm64.movRegReg x0, x21
    arm64.bl __managed_resize
    arm64.movImm x1, 48
    arm64.movRegReg x0, x22
    arm64.bl __managed_resize
    arm64.movImm x1, 64
    arm64.movRegReg x0, x23
    arm64.bl __managed_resize
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __cpufeat_aes_cipher_expand_key
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x20
    arm64.bl __cpufeat_aes_cipher_gcm_init
    arm64.movImm x3, 0
    arm64.movImm x4, 32
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x23
    arm64.bl __cpufeat_aes_cipher_gcm_hash
    arm64.movImm x1, 0
    arm64.movImm x4, 64
    arm64.movRegReg x0, x23
    arm64.movRegReg x2, x21
    arm64.movRegReg x3, x22
    arm64.bl __cpufeat_aes_cipher_gcm_encrypt
    arm64.movImm x1, 0
    arm64.movImm x4, 64
    arm64.movRegReg x0, x23
    arm64.movRegReg x2, x21
    arm64.movRegReg x3, x22
    arm64.bl __cpufeat_aes_cipher_gcm_decrypt
    arm64.movImm x2, 32
    arm64.movImm x3, 64
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x21
    arm64.bl __cpufeat_aes_cipher_gcm_finish
    arm64.loadRegBaseDisp.word64 x19, [x22 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_1  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x19, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x28, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x19, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
  __il_body:
    arm64.movRegReg x0, x27
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
  exitdrop#2:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitdrop#3:
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitdrop#4:
    arm64.loadRegSlot x0, slot1
    arm64.bl __managed_decref
  exitdrop#5:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.loadRegSlot x23, slot6
    arm64.loadRegSlot x24, slot7
    arm64.loadRegSlot x25, slot8
    arm64.loadRegSlot x26, slot9
    arm64.loadRegSlot x27, slot10
    arm64.loadRegSlot x28, slot11
    arm64.epilogue 112
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_init {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.loadRegBaseDisp.word64 x21, [x20 + 8]
    arm64.cmp x21, 352
    arm64.b.lt gibad
  scbounded:
    arm64.cmp x21, 480
    arm64.b.gt gibad
  scstepped:
    arm64.and x0, x21, 63
    arm64.cmp x0, 32
    arm64.b.ne gibad
  gikeyblock:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.lsr x1, x21, 1
    arm64.add x1, x1, 256
    arm64.cmp x0, x1
    arm64.b.ne gibad
  gicrypt:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.lsr x0, x21, 5
    arm64.sub x0, x0, 1
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x20 + 0]
    arm64.movImm x3, 0
    arm64.bl mrt_aes_gcm_init
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  gibad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_hash {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x2
    arm64.movRegReg x22, x3
    arm64.movRegReg x23, x4
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x0, 432
    arm64.b.lt ghbad
  kbbounded:
    arm64.cmp x0, 496
    arm64.b.gt ghbad
  kbstepped:
    arm64.and x0, x0, 31
    arm64.cmp x0, 16
    arm64.b.ne ghbad
  gkmessage:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 48
    arm64.b.ne ghbad
  ghspan:
    arm64.and x0, x23, 15
    arm64.cmp x0, 0
    arm64.b.ne ghbad
  ghaligned:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 8]
    arm64.cmp x22, x0
    arm64.b.hi ghbad
  ghinside:
    arm64.sub x0, x0, x22
    arm64.cmp x0, x23
    arm64.b.lo ghbad
  ghcrypt:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.loadRegBaseDisp.word64 x1, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.add x3, x0, x22
    arm64.movRegReg x0, x23
    arm64.bl mrt_aes_gcm_hash
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  ghbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_encrypt {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x2
    arm64.movRegReg x22, x3
    arm64.movRegReg x23, x4
    arm64.loadRegBaseDisp.word64 x24, [x21 + 8]
    arm64.cmp x24, 432
    arm64.b.lt gcbad
  kbbounded:
    arm64.cmp x24, 496
    arm64.b.gt gcbad
  kbstepped:
    arm64.and x0, x24, 31
    arm64.cmp x0, 16
    arm64.b.ne gcbad
  gkmessage:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.cmp x0, 48
    arm64.b.ne gcbad
  gcspan:
    arm64.and x0, x23, 15
    arm64.cmp x0, 0
    arm64.b.ne gcbad
  gcaligned:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x20, x0
    arm64.b.hi gcbad
  gcinside:
    arm64.sub x0, x0, x20
    arm64.cmp x0, x23
    arm64.b.lo gcbad
  gccrypt:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x0, x22
    arm64.bl __managed_cow_detach
    arm64.sub x0, x24, 256
    arm64.lsr x0, x0, 4
    arm64.sub x0, x0, 1
    arm64.lsl x1, x23, 8
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.add x3, x3, x20
    arm64.bl mrt_aes_gcm_encrypt
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
  gcbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_decrypt {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x2
    arm64.movRegReg x22, x3
    arm64.movRegReg x23, x4
    arm64.loadRegBaseDisp.word64 x24, [x21 + 8]
    arm64.cmp x24, 432
    arm64.b.lt gcbad
  kbbounded:
    arm64.cmp x24, 496
    arm64.b.gt gcbad
  kbstepped:
    arm64.and x0, x24, 31
    arm64.cmp x0, 16
    arm64.b.ne gcbad
  gkmessage:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 8]
    arm64.cmp x0, 48
    arm64.b.ne gcbad
  gcspan:
    arm64.and x0, x23, 15
    arm64.cmp x0, 0
    arm64.b.ne gcbad
  gcaligned:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x20, x0
    arm64.b.hi gcbad
  gcinside:
    arm64.sub x0, x0, x20
    arm64.cmp x0, x23
    arm64.b.lo gcbad
  gccrypt:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x0, x22
    arm64.bl __managed_cow_detach
    arm64.sub x0, x24, 256
    arm64.lsr x0, x0, 4
    arm64.sub x0, x0, 1
    arm64.lsl x1, x23, 8
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.add x3, x3, x20
    arm64.bl mrt_aes_gcm_decrypt
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
  gcbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.epilogue 64
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_finish {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x2
    arm64.movRegReg x22, x3
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x0, 432
    arm64.b.lt gfbad
  kbbounded:
    arm64.cmp x0, 496
    arm64.b.gt gfbad
  kbstepped:
    arm64.and x0, x0, 31
    arm64.cmp x0, 16
    arm64.b.ne gfbad
  gkmessage:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 48
    arm64.b.ne gfbad
  gfcrypt:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.loadRegBaseDisp.word64 x1, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.movRegReg x0, x21
    arm64.movRegReg x3, x22
    arm64.bl mrt_aes_gcm_finish
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  gfbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}

chunk @mrt_aes_gcm_init {
    0000: 47 18 80 d2 e7 1c 48 d3 f7 1c 08 4e 27 00 80 d2
    0010: f7 1c 18 4e 18 1f 38 6e 28 00 04 91 0a 04 00 91
    0020: 40 00 c0 3d 00 01 80 3d 42 40 00 91 08 41 00 91
    0030: 4a 05 00 d1 6a ff ff b5 26 00 04 91 00 1c 20 6e
    0040: 1f 30 00 f1 03 02 00 54 00 01 00 54 db 00 c0 3d
    0050: 60 4b 28 4e 00 68 28 4e db 04 c0 3d 60 4b 28 4e
    0060: 00 68 28 4e c6 80 00 91 db 00 c0 3d 60 4b 28 4e
    0070: 00 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    0080: c6 80 00 91 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    0090: db 04 c0 3d 60 4b 28 4e 00 68 28 4e db 08 c0 3d
    00a0: 60 4b 28 4e 00 68 28 4e db 0c c0 3d 60 4b 28 4e
    00b0: 00 68 28 4e db 10 c0 3d 60 4b 28 4e 00 68 28 4e
    00c0: db 14 c0 3d 60 4b 28 4e 00 68 28 4e db 18 c0 3d
    00d0: 60 4b 28 4e 00 68 28 4e db 1c c0 3d 60 4b 28 4e
    00e0: 00 68 28 4e db 20 c0 3d 60 4b 28 4e 00 68 28 4e
    00f0: db 24 c0 3d 60 4b 28 4e db 28 c0 3d 00 1c 3b 6e
    0100: 00 08 20 4e 07 3c 08 4e e7 fc 7f 93 f4 1c 08 4e
    0110: f4 1c 18 4e 94 1e 37 4e 15 04 41 6f b5 42 18 6e
    0120: 00 54 41 4f 00 1c 34 6e 00 1c 35 6e 20 38 80 3d
    0130: 13 40 00 6e 73 1e 20 6e 33 3c 80 3d 28 00 03 91
    0140: e4 00 80 d2 84 04 00 d1 34 38 c0 3d 35 3c c0 3d
    0150: 13 40 00 6e 13 1c 33 6e 11 e0 f4 0e 10 e0 f4 4e
    0160: 72 e2 f5 0e 52 1e 30 6e 52 1e 31 6e 53 42 18 6e
    0170: 12 43 12 6e 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e
    0180: 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e
    0190: 31 42 11 6e 10 1e 31 6e 00 1e b0 4e 00 01 80 3d
    01a0: 13 40 00 6e 73 1e 20 6e 13 05 80 3d 08 81 00 d1
    01b0: a4 fc ff b5 e0 03 1f aa c0 03 5f d6
}

chunk @mrt_aes_gcm_hash {
    0000: 47 18 80 d2 e7 1c 48 d3 f7 1c 08 4e 27 00 80 d2
    0010: f7 1c 18 4e 18 1f 38 6e 50 00 c0 3d a0 11 00 b4
    0020: 1f 00 02 f1 e3 0d 00 54 00 00 02 d1 60 00 c0 3d
    0030: 61 04 c0 3d 62 08 c0 3d 63 0c c0 3d 64 10 c0 3d
    0040: 65 14 c0 3d 66 18 c0 3d 67 1c c0 3d 63 00 02 91
    0050: 34 00 c0 3d 35 04 c0 3d 00 08 20 4e 00 1c 30 6e
    0060: 13 40 00 6e 13 1c 33 6e 11 e0 f4 0e 10 e0 f4 4e
    0070: 72 e2 f5 0e 34 08 c0 3d 35 0c c0 3d 21 08 20 4e
    0080: 33 40 01 6e 33 1c 33 6e 36 e0 f4 0e 31 1e 36 6e
    0090: 36 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    00a0: 34 10 c0 3d 35 14 c0 3d 42 08 20 4e 53 40 02 6e
    00b0: 53 1c 33 6e 56 e0 f4 0e 31 1e 36 6e 56 e0 f4 4e
    00c0: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 34 18 c0 3d
    00d0: 35 1c c0 3d 63 08 20 4e 73 40 03 6e 73 1c 33 6e
    00e0: 76 e0 f4 0e 31 1e 36 6e 76 e0 f4 4e 10 1e 36 6e
    00f0: 76 e2 f5 0e 52 1e 36 6e 34 20 c0 3d 35 24 c0 3d
    0100: 84 08 20 4e 93 40 04 6e 93 1c 33 6e 96 e0 f4 0e
    0110: 31 1e 36 6e 96 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e
    0120: 52 1e 36 6e 34 28 c0 3d 35 2c c0 3d a5 08 20 4e
    0130: b3 40 05 6e b3 1c 33 6e b6 e0 f4 0e 31 1e 36 6e
    0140: b6 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0150: 34 30 c0 3d 35 34 c0 3d c6 08 20 4e d3 40 06 6e
    0160: d3 1c 33 6e d6 e0 f4 0e 31 1e 36 6e d6 e0 f4 4e
    0170: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 34 38 c0 3d
    0180: 35 3c c0 3d e7 08 20 4e f3 40 07 6e f3 1c 33 6e
    0190: f6 e0 f4 0e 31 1e 36 6e f6 e0 f4 4e 10 1e 36 6e
    01a0: 76 e2 f5 0e 52 1e 36 6e 52 1e 30 6e 52 1e 31 6e
    01b0: 53 42 18 6e 12 43 12 6e 10 1e 32 6e 31 1e 33 6e
    01c0: 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e
    01d0: 31 1e 33 6e 31 42 11 6e 10 1e 31 6e 91 ff ff 17
    01e0: 34 38 c0 3d 35 3c c0 3d 1f 40 00 f1 23 03 00 54
    01f0: 00 40 00 d1 60 00 c0 3d 63 40 00 91 00 08 20 4e
    0200: 00 1c 30 6e 13 40 00 6e 13 1c 33 6e 11 e0 f4 0e
    0210: 10 e0 f4 4e 72 e2 f5 0e 52 1e 30 6e 52 1e 31 6e
    0220: 53 42 18 6e 12 43 12 6e 10 1e 32 6e 31 1e 33 6e
    0230: 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e
    0240: 31 1e 33 6e 31 42 11 6e 10 1e 31 6e e7 ff ff 17
    0250: 50 00 80 3d e0 03 1f aa c0 03 5f d6
}

chunk @mrt_aes_gcm_encrypt {
    0000: 04 fc 48 d3 e5 1f 80 d2 05 00 05 8a 28 00 04 91
    0010: 47 18 80 d2 e7 1c 48 d3 f7 1c 08 4e 27 00 80 d2
    0020: f7 1c 18 4e 18 1f 38 6e 50 00 c0 3d 31 1e 31 6e
    0030: 52 1e 32 6e 5a 04 c0 3d 39 1f 39 6e 27 00 80 d2
    0040: f9 1c 1c 4e 47 1c 40 b9 e7 08 c0 5a 8a fc 44 d3
    0050: e7 00 0a 8b e7 08 c0 5a 47 1c 00 b9 5a 0b 20 6e
    0060: 5a 87 b9 4e 9f 00 02 f1 a3 30 00 54 84 00 02 d1
    0070: 40 0b 20 6e 5a 87 b9 4e 41 0b 20 6e 5a 87 b9 4e
    0080: 42 0b 20 6e 5a 87 b9 4e 43 0b 20 6e 5a 87 b9 4e
    0090: 44 0b 20 6e 5a 87 b9 4e 45 0b 20 6e 5a 87 b9 4e
    00a0: 46 0b 20 6e 5a 87 b9 4e 47 0b 20 6e 5a 87 b9 4e
    00b0: e6 03 08 aa bf 30 00 f1 03 09 00 54 80 04 00 54
    00c0: db 00 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    00d0: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    00e0: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    00f0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0100: e7 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    0110: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0120: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0130: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0140: 67 4b 28 4e e7 68 28 4e c6 80 00 91 db 00 c0 3d
    0150: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0160: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0170: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0180: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0190: db 04 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    01a0: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    01b0: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    01c0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    01d0: e7 68 28 4e c6 80 00 91 db 00 c0 3d 60 4b 28 4e
    01e0: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    01f0: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0200: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0210: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 04 c0 3d
    0220: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0230: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0240: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0250: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0260: db 08 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0270: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0280: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0290: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    02a0: e7 68 28 4e db 0c c0 3d 60 4b 28 4e 00 68 28 4e
    02b0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    02c0: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    02d0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    02e0: 67 4b 28 4e e7 68 28 4e db 10 c0 3d 60 4b 28 4e
    02f0: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0300: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0310: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0320: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 14 c0 3d
    0330: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0340: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0350: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0360: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0370: db 18 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0380: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0390: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    03a0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    03b0: e7 68 28 4e db 1c c0 3d 60 4b 28 4e 00 68 28 4e
    03c0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    03d0: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    03e0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    03f0: 67 4b 28 4e e7 68 28 4e db 20 c0 3d 60 4b 28 4e
    0400: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0410: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0420: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0430: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 24 c0 3d
    0440: 60 4b 28 4e 61 4b 28 4e 62 4b 28 4e 63 4b 28 4e
    0450: 64 4b 28 4e 65 4b 28 4e 66 4b 28 4e 67 4b 28 4e
    0460: db 28 c0 3d 00 1c 3b 6e 21 1c 3b 6e 42 1c 3b 6e
    0470: 63 1c 3b 6e 84 1c 3b 6e a5 1c 3b 6e c6 1c 3b 6e
    0480: e7 1c 3b 6e 74 00 c0 3d 75 04 c0 3d 00 1c 34 6e
    0490: 21 1c 35 6e 60 00 80 3d 61 04 80 3d 74 08 c0 3d
    04a0: 75 0c c0 3d 42 1c 34 6e 63 1c 35 6e 62 08 80 3d
    04b0: 63 0c 80 3d 74 10 c0 3d 75 14 c0 3d 84 1c 34 6e
    04c0: a5 1c 35 6e 64 10 80 3d 65 14 80 3d 74 18 c0 3d
    04d0: 75 1c c0 3d c6 1c 34 6e e7 1c 35 6e 66 18 80 3d
    04e0: 67 1c 80 3d 34 00 c0 3d 35 04 c0 3d 00 08 20 4e
    04f0: 00 1c 30 6e 13 40 00 6e 13 1c 33 6e 11 e0 f4 0e
    0500: 10 e0 f4 4e 72 e2 f5 0e 34 08 c0 3d 35 0c c0 3d
    0510: 21 08 20 4e 33 40 01 6e 33 1c 33 6e 36 e0 f4 0e
    0520: 31 1e 36 6e 36 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e
    0530: 52 1e 36 6e 34 10 c0 3d 35 14 c0 3d 42 08 20 4e
    0540: 53 40 02 6e 53 1c 33 6e 56 e0 f4 0e 31 1e 36 6e
    0550: 56 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0560: 34 18 c0 3d 35 1c c0 3d 63 08 20 4e 73 40 03 6e
    0570: 73 1c 33 6e 76 e0 f4 0e 31 1e 36 6e 76 e0 f4 4e
    0580: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 34 20 c0 3d
    0590: 35 24 c0 3d 84 08 20 4e 93 40 04 6e 93 1c 33 6e
    05a0: 96 e0 f4 0e 31 1e 36 6e 96 e0 f4 4e 10 1e 36 6e
    05b0: 76 e2 f5 0e 52 1e 36 6e 34 28 c0 3d 35 2c c0 3d
    05c0: a5 08 20 4e b3 40 05 6e b3 1c 33 6e b6 e0 f4 0e
    05d0: 31 1e 36 6e b6 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e
    05e0: 52 1e 36 6e 34 30 c0 3d 35 34 c0 3d c6 08 20 4e
    05f0: d3 40 06 6e d3 1c 33 6e d6 e0 f4 0e 31 1e 36 6e
    0600: d6 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0610: 34 38 c0 3d 35 3c c0 3d e7 08 20 4e f3 40 07 6e
    0620: f3 1c 33 6e f6 e0 f4 0e 31 1e 36 6e f6 e0 f4 4e
    0630: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 52 1e 30 6e
    0640: 52 1e 31 6e 53 42 18 6e 12 43 12 6e 10 1e 32 6e
    0650: 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e
    0660: 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e 10 1e 31 6e
    0670: 63 00 02 91 9f 00 02 f1 a2 cf ff 54 44 0a 00 b4
    0680: 34 38 c0 3d 35 3c c0 3d 9f 40 00 f1 c3 09 00 54
    0690: 84 40 00 d1 7c 00 c0 3d 40 0b 20 6e 5a 87 b9 4e
    06a0: e6 03 08 aa bf 30 00 f1 03 02 00 54 00 01 00 54
    06b0: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    06c0: 60 4b 28 4e 00 68 28 4e c6 80 00 91 db 00 c0 3d
    06d0: 60 4b 28 4e 00 68 28 4e db 04 c0 3d 60 4b 28 4e
    06e0: 00 68 28 4e c6 80 00 91 db 00 c0 3d 60 4b 28 4e
    06f0: 00 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    0700: db 08 c0 3d 60 4b 28 4e 00 68 28 4e db 0c c0 3d
    0710: 60 4b 28 4e 00 68 28 4e db 10 c0 3d 60 4b 28 4e
    0720: 00 68 28 4e db 14 c0 3d 60 4b 28 4e 00 68 28 4e
    0730: db 18 c0 3d 60 4b 28 4e 00 68 28 4e db 1c c0 3d
    0740: 60 4b 28 4e 00 68 28 4e db 20 c0 3d 60 4b 28 4e
    0750: 00 68 28 4e db 24 c0 3d 60 4b 28 4e db 28 c0 3d
    0760: 00 1c 3b 6e 00 1c 3c 6e 60 00 80 3d 00 08 20 4e
    0770: 00 1c 30 6e 13 40 00 6e 13 1c 33 6e 11 e0 f4 0e
    0780: 10 e0 f4 4e 72 e2 f5 0e 52 1e 30 6e 52 1e 31 6e
    0790: 53 42 18 6e 12 43 12 6e 10 1e 32 6e 31 1e 33 6e
    07a0: 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e
    07b0: 31 1e 33 6e 31 42 11 6e 10 1e 31 6e 63 40 00 91
    07c0: b2 ff ff 17 50 00 80 3d e0 03 1f aa c0 03 5f d6
}

chunk @mrt_aes_gcm_decrypt {
    0000: 04 fc 48 d3 e5 1f 80 d2 05 00 05 8a 28 00 04 91
    0010: 47 18 80 d2 e7 1c 48 d3 f7 1c 08 4e 27 00 80 d2
    0020: f7 1c 18 4e 18 1f 38 6e 50 00 c0 3d 31 1e 31 6e
    0030: 52 1e 32 6e 5a 04 c0 3d 39 1f 39 6e 27 00 80 d2
    0040: f9 1c 1c 4e 47 1c 40 b9 e7 08 c0 5a 8a fc 44 d3
    0050: e7 00 0a 8b e7 08 c0 5a 47 1c 00 b9 5a 0b 20 6e
    0060: 5a 87 b9 4e 9f 00 02 f1 a3 31 00 54 84 00 02 d1
    0070: 40 0b 20 6e 5a 87 b9 4e 41 0b 20 6e 5a 87 b9 4e
    0080: 42 0b 20 6e 5a 87 b9 4e 43 0b 20 6e 5a 87 b9 4e
    0090: 44 0b 20 6e 5a 87 b9 4e 45 0b 20 6e 5a 87 b9 4e
    00a0: 46 0b 20 6e 5a 87 b9 4e 47 0b 20 6e 5a 87 b9 4e
    00b0: e6 03 08 aa bf 30 00 f1 03 09 00 54 80 04 00 54
    00c0: db 00 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    00d0: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    00e0: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    00f0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0100: e7 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    0110: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0120: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0130: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0140: 67 4b 28 4e e7 68 28 4e c6 80 00 91 db 00 c0 3d
    0150: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0160: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0170: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0180: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0190: db 04 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    01a0: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    01b0: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    01c0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    01d0: e7 68 28 4e c6 80 00 91 db 00 c0 3d 60 4b 28 4e
    01e0: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    01f0: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0200: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0210: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 04 c0 3d
    0220: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0230: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0240: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0250: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0260: db 08 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0270: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0280: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0290: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    02a0: e7 68 28 4e db 0c c0 3d 60 4b 28 4e 00 68 28 4e
    02b0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    02c0: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    02d0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    02e0: 67 4b 28 4e e7 68 28 4e db 10 c0 3d 60 4b 28 4e
    02f0: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0300: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0310: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0320: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 14 c0 3d
    0330: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0340: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0350: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0360: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0370: db 18 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0380: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0390: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    03a0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    03b0: e7 68 28 4e db 1c c0 3d 60 4b 28 4e 00 68 28 4e
    03c0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    03d0: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    03e0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    03f0: 67 4b 28 4e e7 68 28 4e db 20 c0 3d 60 4b 28 4e
    0400: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0410: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0420: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0430: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 24 c0 3d
    0440: 60 4b 28 4e 61 4b 28 4e 62 4b 28 4e 63 4b 28 4e
    0450: 64 4b 28 4e 65 4b 28 4e 66 4b 28 4e 67 4b 28 4e
    0460: db 28 c0 3d 00 1c 3b 6e 21 1c 3b 6e 42 1c 3b 6e
    0470: 63 1c 3b 6e 84 1c 3b 6e a5 1c 3b 6e c6 1c 3b 6e
    0480: e7 1c 3b 6e 7d 00 c0 3d 34 00 c0 3d 35 04 c0 3d
    0490: bd 0b 20 4e bd 1f 30 6e b3 43 1d 6e b3 1f 33 6e
    04a0: b1 e3 f4 0e b0 e3 f4 4e 72 e2 f5 0e 7d 04 c0 3d
    04b0: 34 08 c0 3d 35 0c c0 3d bd 0b 20 4e b3 43 1d 6e
    04c0: b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e
    04d0: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 7d 08 c0 3d
    04e0: 34 10 c0 3d 35 14 c0 3d bd 0b 20 4e b3 43 1d 6e
    04f0: b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e
    0500: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 7d 0c c0 3d
    0510: 34 18 c0 3d 35 1c c0 3d bd 0b 20 4e b3 43 1d 6e
    0520: b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e
    0530: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 7d 10 c0 3d
    0540: 34 20 c0 3d 35 24 c0 3d bd 0b 20 4e b3 43 1d 6e
    0550: b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e
    0560: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 7d 14 c0 3d
    0570: 34 28 c0 3d 35 2c c0 3d bd 0b 20 4e b3 43 1d 6e
    0580: b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e
    0590: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 7d 18 c0 3d
    05a0: 34 30 c0 3d 35 34 c0 3d bd 0b 20 4e b3 43 1d 6e
    05b0: b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e
    05c0: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 7d 1c c0 3d
    05d0: 34 38 c0 3d 35 3c c0 3d bd 0b 20 4e b3 43 1d 6e
    05e0: b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e
    05f0: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 52 1e 30 6e
    0600: 52 1e 31 6e 53 42 18 6e 12 43 12 6e 10 1e 32 6e
    0610: 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e
    0620: 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e 10 1e 31 6e
    0630: 7c 00 c0 3d 00 1c 3c 6e 60 00 80 3d 7c 04 c0 3d
    0640: 21 1c 3c 6e 61 04 80 3d 7c 08 c0 3d 42 1c 3c 6e
    0650: 62 08 80 3d 7c 0c c0 3d 63 1c 3c 6e 63 0c 80 3d
    0660: 7c 10 c0 3d 84 1c 3c 6e 64 10 80 3d 7c 14 c0 3d
    0670: a5 1c 3c 6e 65 14 80 3d 7c 18 c0 3d c6 1c 3c 6e
    0680: 66 18 80 3d 7c 1c c0 3d e7 1c 3c 6e 67 1c 80 3d
    0690: 63 00 02 91 9f 00 02 f1 a2 ce ff 54 44 0a 00 b4
    06a0: 34 38 c0 3d 35 3c c0 3d 9f 40 00 f1 c3 09 00 54
    06b0: 84 40 00 d1 7c 00 c0 3d 40 0b 20 6e 5a 87 b9 4e
    06c0: e6 03 08 aa bf 30 00 f1 03 02 00 54 00 01 00 54
    06d0: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    06e0: 60 4b 28 4e 00 68 28 4e c6 80 00 91 db 00 c0 3d
    06f0: 60 4b 28 4e 00 68 28 4e db 04 c0 3d 60 4b 28 4e
    0700: 00 68 28 4e c6 80 00 91 db 00 c0 3d 60 4b 28 4e
    0710: 00 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    0720: db 08 c0 3d 60 4b 28 4e 00 68 28 4e db 0c c0 3d
    0730: 60 4b 28 4e 00 68 28 4e db 10 c0 3d 60 4b 28 4e
    0740: 00 68 28 4e db 14 c0 3d 60 4b 28 4e 00 68 28 4e
    0750: db 18 c0 3d 60 4b 28 4e 00 68 28 4e db 1c c0 3d
    0760: 60 4b 28 4e 00 68 28 4e db 20 c0 3d 60 4b 28 4e
    0770: 00 68 28 4e db 24 c0 3d 60 4b 28 4e db 28 c0 3d
    0780: 00 1c 3b 6e 00 1c 3c 6e 60 00 80 3d 9c 0b 20 4e
    0790: 9c 1f 30 6e 93 43 1c 6e 93 1f 33 6e 91 e3 f4 0e
    07a0: 90 e3 f4 4e 72 e2 f5 0e 52 1e 30 6e 52 1e 31 6e
    07b0: 53 42 18 6e 12 43 12 6e 10 1e 32 6e 31 1e 33 6e
    07c0: 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e
    07d0: 31 1e 33 6e 31 42 11 6e 10 1e 31 6e 63 40 00 91
    07e0: b2 ff ff 17 50 00 80 3d e0 03 1f aa c0 03 5f d6
}

chunk @mrt_aes_gcm_finish {
    0000: 47 18 80 d2 e7 1c 48 d3 f7 1c 08 4e 27 00 80 d2
    0010: f7 1c 18 4e 18 1f 38 6e 50 00 c0 3d 41 08 c0 3d
    0020: 00 f0 7d d3 63 f0 7d d3 00 1c 08 4e 60 1c 18 4e
    0030: 34 38 c0 3d 35 3c c0 3d 00 1c 30 6e 13 40 00 6e
    0040: 13 1c 33 6e 11 e0 f4 0e 10 e0 f4 4e 72 e2 f5 0e
    0050: 52 1e 30 6e 52 1e 31 6e 53 42 18 6e 12 43 12 6e
    0060: 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e
    0070: 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e
    0080: 10 1e 31 6e 10 0a 20 4e 10 1e 21 6e 50 00 80 3d
    0090: e0 03 1f aa c0 03 5f d6
}
```
