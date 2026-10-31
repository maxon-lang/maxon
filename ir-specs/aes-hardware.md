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
  abbounded:
    x64.cmpRegImm32 r15, 480
    x64.jcc greater, abbad
  abstepped:
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
    0000: 55 48 89 e5 48 83 ec 20 f3 0f 6f 0a f3 41 0f 6f
    0010: 01 48 81 c2 10 00 00 00 66 0f ef c1 48 81 e9 0c
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
  abbounded:
    x64.cmpRegImm32 r15, 480
    x64.jcc greater, abbad
  abstepped:
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
    0000: 55 48 89 e5 48 83 ec 20 f3 0f 6f 0a f3 41 0f 6f
    0010: 01 48 81 c2 10 00 00 00 66 0f ef c1 48 81 e9 0c
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
  abbounded:
    arm64.cmp x24, 480
    arm64.b.gt abbad
  abstepped:
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
  abbounded:
    arm64.cmp x24, 480
    arm64.b.gt abbad
  abstepped:
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
