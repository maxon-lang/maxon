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
