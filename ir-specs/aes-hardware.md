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
    x64.prologue 80
    x64.loadRegBaseDisp.word64 rdi, [rax + 8]
    x64.cmpRegImm32 rdi, 352
    x64.jcc less, abbad
  scbounded:
    x64.cmpRegImm32 rdi, 480
    x64.jcc greater, abbad
  scstepped:
    x64.movRegReg r8, rdi
    x64.andRegImm32 r8, rdi, 63
    x64.cmpRegImm32 r8, 32
    x64.jcc notEqual, abbad
  aboutput:
    x64.loadRegBaseDisp.word64 r8, [rcx + 8]
    x64.cmpRegReg rdx, r8
    x64.jcc aboveEqual, abbad
  abo:
    x64.subRegReg r8, r8, rdx
    x64.cmpRegImm32 r8, 16
    x64.jcc less, abbad
  abinput:
    x64.loadRegBaseDisp.word64 r8, [r9 + 8]
    x64.cmpRegReg rsi, r8
    x64.jcc aboveEqual, abbad
  abi:
    x64.subRegReg r8, r8, rsi
    x64.cmpRegImm32 r8, 16
    x64.jcc less, abbad
  abcrypt:
    x64.loadRegBaseDisp.word64 r8, [rcx + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc less, abddetach
  abdbuf:
    x64.loadRegBaseDisp.word64 r8, [rcx + 0]
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, abddone
  abdshared:
    x64.leaRegRegImm32 r8, r8, -16
    x64.loadRegBaseDisp.word64 r8, [r8 + 8]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, abddetach
  abddone:
    x64.shrRegImm8 rdi, rdi, 5
    x64.leaRegRegImm32 rdi, rdi, -1
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.loadRegBaseDisp.word64 r9, [r9 + 0]
    x64.leaRegRegReg r8, rcx, rdx
    x64.leaRegRegReg r9, r9, rsi
    x64.movRegReg rcx, rdi
    x64.movRegReg rdx, rax
    x64.callDirect mrt_aes_encrypt_block
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
  abddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, rdi
    x64.storeSlotReg slot5, r9
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot rdi, slot4
    x64.loadRegSlot r9, slot5
    x64.jmp abddone
  abbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 80
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
    x64.prologue 80
    x64.loadRegBaseDisp.word64 rdi, [rax + 8]
    x64.cmpRegImm32 rdi, 352
    x64.jcc less, abbad
  scbounded:
    x64.cmpRegImm32 rdi, 480
    x64.jcc greater, abbad
  scstepped:
    x64.movRegReg r8, rdi
    x64.andRegImm32 r8, rdi, 63
    x64.cmpRegImm32 r8, 32
    x64.jcc notEqual, abbad
  aboutput:
    x64.loadRegBaseDisp.word64 r8, [rcx + 8]
    x64.cmpRegReg rdx, r8
    x64.jcc aboveEqual, abbad
  abo:
    x64.subRegReg r8, r8, rdx
    x64.cmpRegImm32 r8, 16
    x64.jcc less, abbad
  abinput:
    x64.loadRegBaseDisp.word64 r8, [r9 + 8]
    x64.cmpRegReg rsi, r8
    x64.jcc aboveEqual, abbad
  abi:
    x64.subRegReg r8, r8, rsi
    x64.cmpRegImm32 r8, 16
    x64.jcc less, abbad
  abcrypt:
    x64.loadRegBaseDisp.word64 r8, [rcx + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc less, abddetach
  abdbuf:
    x64.loadRegBaseDisp.word64 r8, [rcx + 0]
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, abddone
  abdshared:
    x64.leaRegRegImm32 r8, r8, -16
    x64.loadRegBaseDisp.word64 r8, [r8 + 8]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, abddetach
  abddone:
    x64.shrRegImm8 rdi, rdi, 5
    x64.leaRegRegImm32 rdi, rdi, -1
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.loadRegBaseDisp.word64 r9, [r9 + 0]
    x64.leaRegRegReg r8, rcx, rdx
    x64.leaRegRegReg r9, r9, rsi
    x64.movRegReg rcx, rdi
    x64.movRegReg rdx, rax
    x64.callDirect mrt_aes_encrypt_block
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
  abddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, rdi
    x64.storeSlotReg slot5, r9
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot rdi, slot4
    x64.loadRegSlot r9, slot5
    x64.jmp abddone
  abbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 80
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
    arm64.loadRegBaseDisp.word64 x5, [x2 + 8]
    arm64.cmp x5, 352
    arm64.b.lt abbad
  scbounded:
    arm64.cmp x5, 480
    arm64.b.gt abbad
  scstepped:
    arm64.and x6, x5, 63
    arm64.cmp x6, 32
    arm64.b.ne abbad
  aboutput:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 8]
    arm64.cmp x1, x6
    arm64.b.hs abbad
  abo:
    arm64.sub x6, x6, x1
    arm64.cmp x6, 16
    arm64.b.lt abbad
  abinput:
    arm64.loadRegBaseDisp.word64 x6, [x3 + 8]
    arm64.cmp x4, x6
    arm64.b.hs abbad
  abi:
    arm64.sub x6, x6, x4
    arm64.cmp x6, 16
    arm64.b.lt abbad
  abcrypt:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 16]
    arm64.cmp x6, 0
    arm64.b.lt abddetach
  abdbuf:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 0]
    arm64.cmp x6, 0
    arm64.b.eq abddone
  abdshared:
    arm64.sub x6, x6, 16
    arm64.loadRegBaseDisp.word64 x6, [x6 + 8]
    arm64.cmp x6, 0
    arm64.b.ne abddetach
  abddone:
    arm64.lsr x5, x5, 5
    arm64.sub x5, x5, 1
    arm64.loadRegBaseDisp.word64 x6, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.add x2, x0, x1
    arm64.add x3, x3, x4
    arm64.movRegReg x0, x5
    arm64.movRegReg x1, x6
    arm64.bl mrt_aes_encrypt_block
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
  abddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.storeSlotReg slot5, x5
    arm64.bl __managed_cow_detach
    arm64.movRegReg x6, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.loadRegSlot x5, slot5
    arm64.b abddone
  abbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
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
    arm64.loadRegBaseDisp.word64 x5, [x2 + 8]
    arm64.cmp x5, 352
    arm64.b.lt abbad
  scbounded:
    arm64.cmp x5, 480
    arm64.b.gt abbad
  scstepped:
    arm64.and x6, x5, 63
    arm64.cmp x6, 32
    arm64.b.ne abbad
  aboutput:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 8]
    arm64.cmp x1, x6
    arm64.b.hs abbad
  abo:
    arm64.sub x6, x6, x1
    arm64.cmp x6, 16
    arm64.b.lt abbad
  abinput:
    arm64.loadRegBaseDisp.word64 x6, [x3 + 8]
    arm64.cmp x4, x6
    arm64.b.hs abbad
  abi:
    arm64.sub x6, x6, x4
    arm64.cmp x6, 16
    arm64.b.lt abbad
  abcrypt:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 16]
    arm64.cmp x6, 0
    arm64.b.lt abddetach
  abdbuf:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 0]
    arm64.cmp x6, 0
    arm64.b.eq abddone
  abdshared:
    arm64.sub x6, x6, 16
    arm64.loadRegBaseDisp.word64 x6, [x6 + 8]
    arm64.cmp x6, 0
    arm64.b.ne abddetach
  abddone:
    arm64.lsr x5, x5, 5
    arm64.sub x5, x5, 1
    arm64.loadRegBaseDisp.word64 x6, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.add x2, x0, x1
    arm64.add x3, x3, x4
    arm64.movRegReg x0, x5
    arm64.movRegReg x1, x6
    arm64.bl mrt_aes_encrypt_block
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
  abddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.storeSlotReg slot5, x5
    arm64.bl __managed_cow_detach
    arm64.movRegReg x6, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.loadRegSlot x5, slot5
    arm64.b abddone
  abbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
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

<!-- test: aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry -->
`Aes.encryptBlockInto` and `Aes.decryptBlockInto` dispatch on the backing once, and on the hardware backing test the
block's length and the output's span once before one call to the checked entry of their direction: no layer between the
method and the entry, no count held on the key schedule or on the software cipher, and a frame that saves no callee
register. A block the output cannot hold takes one call off that path, which repairs the output and does the whole
operation.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var wideKey = ByteArray.create()
	var block = ByteArray.create()
	key.resize(16)
	wideKey.resize(32)
	block.resize(16)
	let narrow = try Aes.create(key) otherwise panic("key")
	let wide = try Aes.create(wideKey) otherwise panic("key")
	try narrow.encryptBlockInto(block, output: block, at: 0) otherwise panic("block")
	try wide.encryptBlockInto(block, output: block, at: 0) otherwise panic("block")
	try wide.decryptBlockInto(block, output: block, at: 0) otherwise panic("block")
	try narrow.decryptBlockInto(block, output: block, at: 0) otherwise panic("block")
	print("{try block.get(0) otherwise 0}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```
```RequiredRuntime
Aes.encryptBlockInto
Aes.decryptBlockInto
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
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 32
    x64.movRegReg rcx, r12
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, r13
    x64.callDirect __managed_resize
    x64.movRegReg rcx, rbx
    x64.callDirect Aes.create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot3, rbx
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#2
  tryok#1:
    x64.movRegReg rcx, r12
    x64.callDirect Aes.create
    x64.movRegReg r12, r8
    x64.storeSlotReg slot4, r12
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#4
  tryok#3:
    x64.movRegImm32 r9, 0
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r13
    x64.callDirect Aes.encryptBlockInto
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#6
  tryok#5:
    x64.movRegImm32 r9, 0
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r13
    x64.callDirect Aes.encryptBlockInto
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#8
  tryok#7:
    x64.movRegImm32 r9, 0
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r13
    x64.callDirect Aes.decryptBlockInto
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#10
  tryok#9:
    x64.movRegImm32 r9, 0
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r13
    x64.callDirect Aes.decryptBlockInto
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#12
  tryok#11:
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
    x64.jmp tryok#13
  __im_stride:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok#13
  __im_slow:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit
  tryerr#14:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  critsplit:
    x64.movRegReg rax, r8
  tryok#13:
    x64.movRegReg rbx, rax
  trycont:
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot5, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_12]  ; "\x0a"
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
    x64.callDirect __destruct_Aes
  exitdrop#17:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __destruct_Aes
  exitdrop#18:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __managed_decref
  exitdrop#19:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __managed_decref
  exitdrop#20:
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
  tryerr#2:
    x64.leaRegRdata rcx, [rip + __str_blob_6]  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:9: key\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#4:
    x64.leaRegRdata rcx, [rip + __str_blob_7]  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:10: key\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#6:
    x64.leaRegRdata rcx, [rip + __str_blob_8]  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:11: block\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#8:
    x64.leaRegRdata rcx, [rip + __str_blob_9]  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:12: block\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#10:
    x64.leaRegRdata rcx, [rip + __str_blob_10]  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:13: block\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#12:
    x64.leaRegRdata rcx, [rip + __str_blob_11]  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:14: block\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @Aes.encryptBlockInto {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegReg rdi, rdx
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, matchnext
  matcharm#1:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.movRegImm32 rsi, 0
    x64.movRegReg rdx, rdi
    x64.callDirect AesBitsliced.cryptBlockInto
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#3
    x64.jmp matchcont
  matchnext:
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, matchdead
  matcharm#5:
    x64.loadRegBaseDisp.word64 r8, [rcx + 8]
  __il_body#12:
    x64.loadRegBaseDisp.word64 rcx, [rdi + 8]
    x64.cmpRegImm32 rcx, 16
    x64.movRegImm32 rcx, 1
    x64.jcc notEqual, scmerge
  __il_body#20:
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.leaRegRegImm32 rdx, r9, 16
    x64.cmpRegReg rcx, rdx
    x64.setccReg below, rcx
  scmerge:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, ifcont
  refit:
    x64.movRegImm32 rsi, 0
    x64.movRegReg rcx, r8
    x64.movRegReg rdx, rdi
    x64.callDirect aesHardwareCryptRefitted
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#17
  tryok:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont
  ifcont:
    x64.movRegImm32 rsi, 0
    x64.movRegReg rcx, rax
    x64.movRegReg rdx, r9
    x64.movRegReg rax, r8
    x64.movRegReg r9, rdi
    x64.callDirect __cpufeat_aes_cipher_encrypt_block
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont
  tryerr#17:
    x64.movRegReg rax, r10
  __il_cont:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, tryerr#7
    x64.jmp matchcont
  tryerr#3:
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  tryerr#7:
    x64.movRegReg r10, rax
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  matchcont:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  matchdead:
}

func @Aes.decryptBlockInto {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegReg rdi, rdx
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, matchnext
  matcharm#1:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.movRegImm32 rsi, 1
    x64.movRegReg rdx, rdi
    x64.callDirect AesBitsliced.cryptBlockInto
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#3
    x64.jmp matchcont
  matchnext:
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, matchdead
  matcharm#5:
    x64.loadRegBaseDisp.word64 r8, [rcx + 8]
  __il_body#12:
    x64.loadRegBaseDisp.word64 rcx, [rdi + 8]
    x64.cmpRegImm32 rcx, 16
    x64.movRegImm32 rcx, 1
    x64.jcc notEqual, scmerge
  __il_body#20:
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.leaRegRegImm32 rdx, r9, 16
    x64.cmpRegReg rcx, rdx
    x64.setccReg below, rcx
  scmerge:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, ifcont
  refit:
    x64.movRegImm32 rsi, 1
    x64.movRegReg rcx, r8
    x64.movRegReg rdx, rdi
    x64.callDirect aesHardwareCryptRefitted
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#17
  tryok:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont
  ifcont:
    x64.movRegImm32 rsi, 0
    x64.movRegReg rcx, rax
    x64.movRegReg rdx, r9
    x64.movRegReg rax, r8
    x64.movRegReg r9, rdi
    x64.callDirect __cpufeat_aes_cipher_decrypt_block
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont
  tryerr#17:
    x64.movRegReg rax, r10
  __il_cont:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, tryerr#7
    x64.jmp matchcont
  tryerr#3:
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  tryerr#7:
    x64.movRegReg r10, rax
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  matchcont:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  matchdead:
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
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 32
    x64.movRegReg rcx, r12
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, r13
    x64.callDirect __managed_resize
    x64.movRegReg rcx, rbx
    x64.callDirect Aes.create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot3, rbx
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#2
  tryok#1:
    x64.movRegReg rcx, r12
    x64.callDirect Aes.create
    x64.movRegReg r12, r8
    x64.storeSlotReg slot4, r12
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#4
  tryok#3:
    x64.movRegImm32 r9, 0
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r13
    x64.callDirect Aes.encryptBlockInto
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#6
  tryok#5:
    x64.movRegImm32 r9, 0
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r13
    x64.callDirect Aes.encryptBlockInto
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#8
  tryok#7:
    x64.movRegImm32 r9, 0
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r13
    x64.callDirect Aes.decryptBlockInto
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#10
  tryok#9:
    x64.movRegImm32 r9, 0
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r13
    x64.callDirect Aes.decryptBlockInto
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#12
  tryok#11:
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
    x64.jmp tryok#13
  __im_stride:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok#13
  __im_slow:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit
  tryerr#14:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  critsplit:
    x64.movRegReg rax, r8
  tryok#13:
    x64.movRegReg rbx, rax
  trycont:
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot5, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_12]  ; "\x0a"
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
    x64.callDirect __destruct_Aes
  exitdrop#17:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __destruct_Aes
  exitdrop#18:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __managed_decref
  exitdrop#19:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __managed_decref
  exitdrop#20:
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
  tryerr#2:
    x64.leaRegRdata rcx, [rip + __str_blob_6]  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:9: key\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#4:
    x64.leaRegRdata rcx, [rip + __str_blob_7]  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:10: key\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#6:
    x64.leaRegRdata rcx, [rip + __str_blob_8]  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:11: block\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#8:
    x64.leaRegRdata rcx, [rip + __str_blob_9]  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:12: block\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#10:
    x64.leaRegRdata rcx, [rip + __str_blob_10]  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:13: block\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#12:
    x64.leaRegRdata rcx, [rip + __str_blob_11]  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:14: block\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @Aes.encryptBlockInto {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegReg rdi, rdx
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, matchnext
  matcharm#1:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.movRegImm32 rsi, 0
    x64.movRegReg rdx, rdi
    x64.callDirect AesBitsliced.cryptBlockInto
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#3
    x64.jmp matchcont
  matchnext:
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, matchdead
  matcharm#5:
    x64.loadRegBaseDisp.word64 r8, [rcx + 8]
  __il_body#12:
    x64.loadRegBaseDisp.word64 rcx, [rdi + 8]
    x64.cmpRegImm32 rcx, 16
    x64.movRegImm32 rcx, 1
    x64.jcc notEqual, scmerge
  __il_body#20:
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.leaRegRegImm32 rdx, r9, 16
    x64.cmpRegReg rcx, rdx
    x64.setccReg below, rcx
  scmerge:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, ifcont
  refit:
    x64.movRegImm32 rsi, 0
    x64.movRegReg rcx, r8
    x64.movRegReg rdx, rdi
    x64.callDirect aesHardwareCryptRefitted
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#17
  tryok:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont
  ifcont:
    x64.movRegImm32 rsi, 0
    x64.movRegReg rcx, rax
    x64.movRegReg rdx, r9
    x64.movRegReg rax, r8
    x64.movRegReg r9, rdi
    x64.callDirect __cpufeat_aes_cipher_encrypt_block
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont
  tryerr#17:
    x64.movRegReg rax, r10
  __il_cont:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, tryerr#7
    x64.jmp matchcont
  tryerr#3:
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  tryerr#7:
    x64.movRegReg r10, rax
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  matchcont:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  matchdead:
}

func @Aes.decryptBlockInto {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegReg rdi, rdx
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, matchnext
  matcharm#1:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.movRegImm32 rsi, 1
    x64.movRegReg rdx, rdi
    x64.callDirect AesBitsliced.cryptBlockInto
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#3
    x64.jmp matchcont
  matchnext:
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, matchdead
  matcharm#5:
    x64.loadRegBaseDisp.word64 r8, [rcx + 8]
  __il_body#12:
    x64.loadRegBaseDisp.word64 rcx, [rdi + 8]
    x64.cmpRegImm32 rcx, 16
    x64.movRegImm32 rcx, 1
    x64.jcc notEqual, scmerge
  __il_body#20:
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.leaRegRegImm32 rdx, r9, 16
    x64.cmpRegReg rcx, rdx
    x64.setccReg below, rcx
  scmerge:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, ifcont
  refit:
    x64.movRegImm32 rsi, 1
    x64.movRegReg rcx, r8
    x64.movRegReg rdx, rdi
    x64.callDirect aesHardwareCryptRefitted
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#17
  tryok:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont
  ifcont:
    x64.movRegImm32 rsi, 0
    x64.movRegReg rcx, rax
    x64.movRegReg rdx, r9
    x64.movRegReg rax, r8
    x64.movRegReg r9, rdi
    x64.callDirect __cpufeat_aes_cipher_decrypt_block
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont
  tryerr#17:
    x64.movRegReg rax, r10
  __il_cont:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, tryerr#7
    x64.jmp matchcont
  tryerr#3:
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  tryerr#7:
    x64.movRegReg r10, rax
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  matchcont:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  matchdead:
}
```
```TargetIr:arm64-macos
data {
  __mrt_cpu_aes_usable@0 = i64 1
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
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
    arm64.movImm x1, 16
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x1, 32
    arm64.movRegReg x0, x20
    arm64.bl __managed_resize
    arm64.movImm x1, 16
    arm64.movRegReg x0, x21
    arm64.bl __managed_resize
    arm64.movRegReg x0, x19
    arm64.bl Aes.create
    arm64.movRegReg x19, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#2
  tryok#1:
    arm64.movRegReg x0, x20
    arm64.bl Aes.create
    arm64.movRegReg x20, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#4
  tryok#3:
    arm64.movImm x3, 0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x21
    arm64.bl Aes.encryptBlockInto
    arm64.cmp x9, 0
    arm64.b.ne tryerr#6
  tryok#5:
    arm64.movImm x3, 0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x21
    arm64.bl Aes.encryptBlockInto
    arm64.cmp x9, 0
    arm64.b.ne tryerr#8
  tryok#7:
    arm64.movImm x3, 0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x21
    arm64.bl Aes.decryptBlockInto
    arm64.cmp x9, 0
    arm64.b.ne tryerr#10
  tryok#9:
    arm64.movImm x3, 0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x21
    arm64.bl Aes.decryptBlockInto
    arm64.cmp x9, 0
    arm64.b.ne tryerr#12
  tryok#11:
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
    arm64.b tryok#13
  __im_stride:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok#13
  __im_slow:
    arm64.movRegReg x0, x21
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok#13
  tryerr#14:
    arm64.movImm x22, 0
    arm64.b trycont
  tryok#13:
    arm64.movRegReg x22, x0
  trycont:
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.bl __uint_to_string
    arm64.movRegReg x22, x0
    arm64.leaRdata x0, __str_rec_12  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x22, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x22, x28, x22
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x22, x25
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
    arm64.movImm x22, 0
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Aes
  exitdrop#17:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Aes
  exitdrop#18:
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitdrop#19:
    arm64.loadRegSlot x0, slot1
    arm64.bl __managed_decref
  exitdrop#20:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x22
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
  tryerr#2:
    arm64.leaRdata x0, __str_blob_6  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:9: key\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
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
  tryerr#4:
    arm64.leaRdata x0, __str_blob_7  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:10: key\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
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
  tryerr#6:
    arm64.leaRdata x0, __str_blob_8  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:11: block\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
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
  tryerr#8:
    arm64.leaRdata x0, __str_blob_9  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:12: block\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
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
  tryerr#10:
    arm64.leaRdata x0, __str_blob_10  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:13: block\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
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
  tryerr#12:
    arm64.leaRdata x0, __str_blob_11  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:14: block\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
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

func @Aes.encryptBlockInto {
  entry:
    arm64.prologue 16
    arm64.movRegReg x5, x1
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 0
    arm64.b.ne matchnext
  matcharm#1:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.movImm x4, 0
    arm64.movRegReg x1, x5
    arm64.bl AesBitsliced.cryptBlockInto
    arm64.cmp x9, 0
    arm64.b.ne tryerr#3
    arm64.b matchcont
  matchnext:
    arm64.cmp x1, 1
    arm64.b.ne matchdead
  matcharm#5:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 8]
  __il_body#12:
    arm64.loadRegBaseDisp.word64 x0, [x5 + 8]
    arm64.cmp x0, 16
    arm64.movImm x0, 1
    arm64.b.ne scmerge
  __il_body#20:
    arm64.loadRegBaseDisp.word64 x0, [x2 + 8]
    arm64.add x1, x3, 16
    arm64.cmp x0, x1
    arm64.cset x0, lo
  scmerge:
    arm64.cbz x0, ifcont
  refit:
    arm64.movImm x4, 0
    arm64.movRegReg x0, x6
    arm64.movRegReg x1, x5
    arm64.bl aesHardwareCryptRefitted
    arm64.cmp x9, 0
    arm64.b.ne tryerr#17
  tryok:
    arm64.movImm x0, 0
    arm64.b __il_cont
  ifcont:
    arm64.movImm x4, 0
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x3
    arm64.movRegReg x2, x6
    arm64.movRegReg x3, x5
    arm64.bl __cpufeat_aes_cipher_encrypt_block
    arm64.movImm x0, 0
    arm64.b __il_cont
  tryerr#17:
    arm64.movRegReg x0, x9
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.ne tryerr#7
    arm64.b matchcont
  tryerr#3:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  tryerr#7:
    arm64.movRegReg x9, x0
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  matchcont:
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.epilogue 16
    arm64.ret
  matchdead:
}

func @Aes.decryptBlockInto {
  entry:
    arm64.prologue 16
    arm64.movRegReg x5, x1
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 0
    arm64.b.ne matchnext
  matcharm#1:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.movImm x4, 1
    arm64.movRegReg x1, x5
    arm64.bl AesBitsliced.cryptBlockInto
    arm64.cmp x9, 0
    arm64.b.ne tryerr#3
    arm64.b matchcont
  matchnext:
    arm64.cmp x1, 1
    arm64.b.ne matchdead
  matcharm#5:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 8]
  __il_body#12:
    arm64.loadRegBaseDisp.word64 x0, [x5 + 8]
    arm64.cmp x0, 16
    arm64.movImm x0, 1
    arm64.b.ne scmerge
  __il_body#20:
    arm64.loadRegBaseDisp.word64 x0, [x2 + 8]
    arm64.add x1, x3, 16
    arm64.cmp x0, x1
    arm64.cset x0, lo
  scmerge:
    arm64.cbz x0, ifcont
  refit:
    arm64.movImm x4, 1
    arm64.movRegReg x0, x6
    arm64.movRegReg x1, x5
    arm64.bl aesHardwareCryptRefitted
    arm64.cmp x9, 0
    arm64.b.ne tryerr#17
  tryok:
    arm64.movImm x0, 0
    arm64.b __il_cont
  ifcont:
    arm64.movImm x4, 0
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x3
    arm64.movRegReg x2, x6
    arm64.movRegReg x3, x5
    arm64.bl __cpufeat_aes_cipher_decrypt_block
    arm64.movImm x0, 0
    arm64.b __il_cont
  tryerr#17:
    arm64.movRegReg x0, x9
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.ne tryerr#7
    arm64.b matchcont
  tryerr#3:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  tryerr#7:
    arm64.movRegReg x9, x0
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  matchcont:
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.epilogue 16
    arm64.ret
  matchdead:
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
    arm64.movImm x1, 16
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x1, 32
    arm64.movRegReg x0, x20
    arm64.bl __managed_resize
    arm64.movImm x1, 16
    arm64.movRegReg x0, x21
    arm64.bl __managed_resize
    arm64.movRegReg x0, x19
    arm64.bl Aes.create
    arm64.movRegReg x19, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#2
  tryok#1:
    arm64.movRegReg x0, x20
    arm64.bl Aes.create
    arm64.movRegReg x20, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#4
  tryok#3:
    arm64.movImm x3, 0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x21
    arm64.bl Aes.encryptBlockInto
    arm64.cmp x9, 0
    arm64.b.ne tryerr#6
  tryok#5:
    arm64.movImm x3, 0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x21
    arm64.bl Aes.encryptBlockInto
    arm64.cmp x9, 0
    arm64.b.ne tryerr#8
  tryok#7:
    arm64.movImm x3, 0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x21
    arm64.bl Aes.decryptBlockInto
    arm64.cmp x9, 0
    arm64.b.ne tryerr#10
  tryok#9:
    arm64.movImm x3, 0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x21
    arm64.bl Aes.decryptBlockInto
    arm64.cmp x9, 0
    arm64.b.ne tryerr#12
  tryok#11:
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
    arm64.b tryok#13
  __im_stride:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok#13
  __im_slow:
    arm64.movRegReg x0, x21
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok#13
  tryerr#14:
    arm64.movImm x22, 0
    arm64.b trycont
  tryok#13:
    arm64.movRegReg x22, x0
  trycont:
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.bl __uint_to_string
    arm64.movRegReg x22, x0
    arm64.leaRdata x0, __str_rec_12  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x22, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x22, x28, x22
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x22, x25
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
    arm64.movImm x22, 0
    arm64.movRegReg x0, x20
    arm64.bl __destruct_Aes
  exitdrop#17:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_Aes
  exitdrop#18:
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitdrop#19:
    arm64.loadRegSlot x0, slot1
    arm64.bl __managed_decref
  exitdrop#20:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x22
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
  tryerr#2:
    arm64.leaRdata x0, __str_blob_6  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:9: key\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
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
  tryerr#4:
    arm64.leaRdata x0, __str_blob_7  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:10: key\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
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
  tryerr#6:
    arm64.leaRdata x0, __str_blob_8  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:11: block\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
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
  tryerr#8:
    arm64.leaRdata x0, __str_blob_9  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:12: block\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
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
  tryerr#10:
    arm64.leaRdata x0, __str_blob_10  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:13: block\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
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
  tryerr#12:
    arm64.leaRdata x0, __str_blob_11  ; "panic at aes-hardware.a-single-block-call-of-aes-is-one-frame-and-one-checked-entry.test:14: block\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
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

func @Aes.encryptBlockInto {
  entry:
    arm64.prologue 16
    arm64.movRegReg x5, x1
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 0
    arm64.b.ne matchnext
  matcharm#1:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.movImm x4, 0
    arm64.movRegReg x1, x5
    arm64.bl AesBitsliced.cryptBlockInto
    arm64.cmp x9, 0
    arm64.b.ne tryerr#3
    arm64.b matchcont
  matchnext:
    arm64.cmp x1, 1
    arm64.b.ne matchdead
  matcharm#5:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 8]
  __il_body#12:
    arm64.loadRegBaseDisp.word64 x0, [x5 + 8]
    arm64.cmp x0, 16
    arm64.movImm x0, 1
    arm64.b.ne scmerge
  __il_body#20:
    arm64.loadRegBaseDisp.word64 x0, [x2 + 8]
    arm64.add x1, x3, 16
    arm64.cmp x0, x1
    arm64.cset x0, lo
  scmerge:
    arm64.cbz x0, ifcont
  refit:
    arm64.movImm x4, 0
    arm64.movRegReg x0, x6
    arm64.movRegReg x1, x5
    arm64.bl aesHardwareCryptRefitted
    arm64.cmp x9, 0
    arm64.b.ne tryerr#17
  tryok:
    arm64.movImm x0, 0
    arm64.b __il_cont
  ifcont:
    arm64.movImm x4, 0
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x3
    arm64.movRegReg x2, x6
    arm64.movRegReg x3, x5
    arm64.bl __cpufeat_aes_cipher_encrypt_block
    arm64.movImm x0, 0
    arm64.b __il_cont
  tryerr#17:
    arm64.movRegReg x0, x9
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.ne tryerr#7
    arm64.b matchcont
  tryerr#3:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  tryerr#7:
    arm64.movRegReg x9, x0
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  matchcont:
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.epilogue 16
    arm64.ret
  matchdead:
}

func @Aes.decryptBlockInto {
  entry:
    arm64.prologue 16
    arm64.movRegReg x5, x1
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 0
    arm64.b.ne matchnext
  matcharm#1:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.movImm x4, 1
    arm64.movRegReg x1, x5
    arm64.bl AesBitsliced.cryptBlockInto
    arm64.cmp x9, 0
    arm64.b.ne tryerr#3
    arm64.b matchcont
  matchnext:
    arm64.cmp x1, 1
    arm64.b.ne matchdead
  matcharm#5:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 8]
  __il_body#12:
    arm64.loadRegBaseDisp.word64 x0, [x5 + 8]
    arm64.cmp x0, 16
    arm64.movImm x0, 1
    arm64.b.ne scmerge
  __il_body#20:
    arm64.loadRegBaseDisp.word64 x0, [x2 + 8]
    arm64.add x1, x3, 16
    arm64.cmp x0, x1
    arm64.cset x0, lo
  scmerge:
    arm64.cbz x0, ifcont
  refit:
    arm64.movImm x4, 1
    arm64.movRegReg x0, x6
    arm64.movRegReg x1, x5
    arm64.bl aesHardwareCryptRefitted
    arm64.cmp x9, 0
    arm64.b.ne tryerr#17
  tryok:
    arm64.movImm x0, 0
    arm64.b __il_cont
  ifcont:
    arm64.movImm x4, 0
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x3
    arm64.movRegReg x2, x6
    arm64.movRegReg x3, x5
    arm64.bl __cpufeat_aes_cipher_decrypt_block
    arm64.movImm x0, 0
    arm64.b __il_cont
  tryerr#17:
    arm64.movRegReg x0, x9
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.ne tryerr#7
    arm64.b matchcont
  tryerr#3:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  tryerr#7:
    arm64.movRegReg x9, x0
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  matchcont:
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.epilogue 16
    arm64.ret
  matchdead:
}
```

<!-- test: aes-hardware.a-program-that-only-encrypts-leaves-the-software-cipher-a-call-and-the-block-frame-lean -->
When a program calls `encryptBlockInto` alone, the software cipher's entry has one site, and splicing its loops and calls
into `Aes.encryptBlockInto` would make that function save callee registers on every path, the hardware one included. The
software arm stays a call: the frame below saves no register.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var block = ByteArray.create()
	key.resize(16)
	block.resize(16)
	let aes = try Aes.create(key) otherwise panic("key")
	try aes.encryptBlockInto(block, output: block, at: 0) otherwise panic("block")
	print("{try block.get(0) otherwise 0}\n")

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
Aes.encryptBlockInto
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
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, r12
    x64.callDirect __managed_resize
    x64.movRegReg rcx, rbx
    x64.callDirect Aes.create
    x64.storeSlotReg slot2, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#2
  tryok#1:
    x64.movRegImm32 r9, 0
    x64.movRegReg rcx, r8
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r12
    x64.callDirect Aes.encryptBlockInto
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#4
  tryok#3:
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
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.jmp tryok#5
  __im_stride:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok#5
  __im_slow:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit
  tryerr#6:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  critsplit:
    x64.movRegReg rax, r8
  tryok#5:
    x64.movRegReg rbx, rax
  trycont:
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot3, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_8]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot4, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot5, r8
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
    x64.loadRegSlot rax, slot5
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot4
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot3
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot5
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot5
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot2
    x64.callDirect __destruct_Aes
  exitdrop#9:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __managed_decref
  exitdrop#10:
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
  tryerr#2:
    x64.leaRegRdata rcx, [rip + __str_blob_6]  ; "panic at aes-hardware.a-program-that-only-encrypts-leaves-the-software-cipher-a-call-and-the-block-frame-lean.test:7: key\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#4:
    x64.leaRegRdata rcx, [rip + __str_blob_7]  ; "panic at aes-hardware.a-program-that-only-encrypts-leaves-the-software-cipher-a-call-and-the-block-frame-lean.test:8: block\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @Aes.encryptBlockInto {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegReg rdi, rdx
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, matchnext
  matcharm#1:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.movRegImm32 rsi, 0
    x64.movRegReg rdx, rdi
    x64.callDirect AesBitsliced.cryptBlockInto
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#3
    x64.jmp matchcont
  matchnext:
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, matchdead
  matcharm#5:
    x64.loadRegBaseDisp.word64 r8, [rcx + 8]
  __il_body#12:
    x64.loadRegBaseDisp.word64 rcx, [rdi + 8]
    x64.cmpRegImm32 rcx, 16
    x64.movRegImm32 rcx, 1
    x64.jcc notEqual, scmerge
  __il_body#20:
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.leaRegRegImm32 rdx, r9, 16
    x64.cmpRegReg rcx, rdx
    x64.setccReg below, rcx
  scmerge:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, ifcont
  refit:
    x64.movRegImm32 rsi, 0
    x64.movRegReg rcx, r8
    x64.movRegReg rdx, rdi
    x64.callDirect aesHardwareCryptRefitted
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#17
  tryok:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont
  ifcont:
    x64.movRegImm32 rsi, 0
    x64.movRegReg rcx, rax
    x64.movRegReg rdx, r9
    x64.movRegReg rax, r8
    x64.movRegReg r9, rdi
    x64.callDirect __cpufeat_aes_cipher_encrypt_block
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont
  tryerr#17:
    x64.movRegReg rax, r10
  __il_cont:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, tryerr#7
    x64.jmp matchcont
  tryerr#3:
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  tryerr#7:
    x64.movRegReg r10, rax
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  matchcont:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  matchdead:
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
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, r12
    x64.callDirect __managed_resize
    x64.movRegReg rcx, rbx
    x64.callDirect Aes.create
    x64.storeSlotReg slot2, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#2
  tryok#1:
    x64.movRegImm32 r9, 0
    x64.movRegReg rcx, r8
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r12
    x64.callDirect Aes.encryptBlockInto
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#4
  tryok#3:
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
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.jmp tryok#5
  __im_stride:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok#5
  __im_slow:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, critsplit
  tryerr#6:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  critsplit:
    x64.movRegReg rax, r8
  tryok#5:
    x64.movRegReg rbx, rax
  trycont:
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot3, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_8]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot4, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot5, r8
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
    x64.loadRegSlot rax, slot5
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot4
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot3
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot5
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot5
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot2
    x64.callDirect __destruct_Aes
  exitdrop#9:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __managed_decref
  exitdrop#10:
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
  tryerr#2:
    x64.leaRegRdata rcx, [rip + __str_blob_6]  ; "panic at aes-hardware.a-program-that-only-encrypts-leaves-the-software-cipher-a-call-and-the-block-frame-lean.test:7: key\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#4:
    x64.leaRegRdata rcx, [rip + __str_blob_7]  ; "panic at aes-hardware.a-program-that-only-encrypts-leaves-the-software-cipher-a-call-and-the-block-frame-lean.test:8: block\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @Aes.encryptBlockInto {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegReg rdi, rdx
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, matchnext
  matcharm#1:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.movRegImm32 rsi, 0
    x64.movRegReg rdx, rdi
    x64.callDirect AesBitsliced.cryptBlockInto
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#3
    x64.jmp matchcont
  matchnext:
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, matchdead
  matcharm#5:
    x64.loadRegBaseDisp.word64 r8, [rcx + 8]
  __il_body#12:
    x64.loadRegBaseDisp.word64 rcx, [rdi + 8]
    x64.cmpRegImm32 rcx, 16
    x64.movRegImm32 rcx, 1
    x64.jcc notEqual, scmerge
  __il_body#20:
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.leaRegRegImm32 rdx, r9, 16
    x64.cmpRegReg rcx, rdx
    x64.setccReg below, rcx
  scmerge:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, ifcont
  refit:
    x64.movRegImm32 rsi, 0
    x64.movRegReg rcx, r8
    x64.movRegReg rdx, rdi
    x64.callDirect aesHardwareCryptRefitted
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#17
  tryok:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont
  ifcont:
    x64.movRegImm32 rsi, 0
    x64.movRegReg rcx, rax
    x64.movRegReg rdx, r9
    x64.movRegReg rax, r8
    x64.movRegReg r9, rdi
    x64.callDirect __cpufeat_aes_cipher_encrypt_block
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont
  tryerr#17:
    x64.movRegReg rax, r10
  __il_cont:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, tryerr#7
    x64.jmp matchcont
  tryerr#3:
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  tryerr#7:
    x64.movRegReg r10, rax
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  matchcont:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  matchdead:
}
```
```TargetIr:arm64-macos
data {
  __mrt_cpu_aes_usable@0 = i64 1
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    arm64.prologue 128
    arm64.storeSlotReg slot13, x28
    arm64.storeSlotReg slot12, x27
    arm64.storeSlotReg slot11, x26
    arm64.storeSlotReg slot10, x25
    arm64.storeSlotReg slot9, x24
    arm64.storeSlotReg slot8, x23
    arm64.storeSlotReg slot7, x22
    arm64.storeSlotReg slot6, x21
    arm64.storeSlotReg slot5, x20
    arm64.storeSlotReg slot4, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x1, 16
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x1, 16
    arm64.movRegReg x0, x20
    arm64.bl __managed_resize
  __il_body#29:
    arm64.bl __cpufeat_aes_usable
  __il_cont#28:
    arm64.cbz x0, __il_body#174
  __il_body#166:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 16
    arm64.movImm x0, 0
    arm64.b.eq scmerge#168
  andrhs#167:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 24
    arm64.cset x0, ne
  scmerge#168:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#202
  andrhs#169:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 32
    arm64.cset x0, ne
    arm64.b scmerge#170
  critsplit#202:
    arm64.movRegReg x0, x1
  scmerge#170:
    arm64.cbz x0, ifcont#172
  keyLength#171:
    arm64.movImm x0, 1
    arm64.b __il_cont#165
  ifcont#172:
    arm64.movImm x0, 0
  __il_cont#165:
    arm64.cmp x0, 0
    arm64.b.ne tryerr#2
  __il_body#31:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.lsr x0, x0, 2
    arm64.add x21, x0, 6
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.add x0, x21, 1
    arm64.lsl x1, x0, 5
    arm64.movRegReg x0, x22
    arm64.bl __managed_resize
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x19
    arm64.bl __cpufeat_aes_cipher_expand_key
  __il_cont#30:
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x21 + 0], x0
    arm64.storeBaseDispReg.word64 [x21 + 8], x22
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x21
    arm64.movRegReg x21, x0
    arm64.b tryok#1
  __il_body#174:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 16
    arm64.movImm x0, 0
    arm64.b.eq scmerge#176
  andrhs#175:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 24
    arm64.cset x0, ne
  scmerge#176:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#204
  andrhs#177:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 32
    arm64.cset x0, ne
    arm64.b scmerge#178
  critsplit#204:
    arm64.movRegReg x0, x1
  scmerge#178:
    arm64.cbz x0, ifcont#180
  keyLength#179:
    arm64.movImm x0, 1
    arm64.b __il_cont#173
  ifcont#180:
    arm64.movImm x0, 0
  __il_cont#173:
    arm64.cmp x0, 0
    arm64.b.eq tryok#34
  tryerr#35:
    arm64.movImm x21, 0
    arm64.b __il_cont#32
  tryok#34:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.lsr x0, x0, 2
    arm64.add x21, x0, 6
    arm64.cmp x21, 10
    arm64.b.lt __rc_panic#37
  __rc_chk#38:
    arm64.cmp x21, 14
    arm64.b.gt __rc_panic#37
  __il_body#129:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.lsr x22, x0, 2
    arm64.add x0, x21, 1
    arm64.lsl x23, x0, 2
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x23
    arm64.bl Array.reserve
    arm64.movImm x0, 0
    arm64.b forhdr#106
  keyWords:
    arm64.lsl x1, x0, 2
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#131
  __il_body#182:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#186
  __im_bounds#187:
    arm64.sub x2, x2, x1
    arm64.cmp x2, 4
    arm64.b.lt __im_slow#186
  __im_load#188:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 24]
    arm64.cmp x2, 1
    arm64.b.ne __im_slow#186
  __im_byte#189:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.add x1, x2, x1
    arm64.loadRegBaseDisp.word32 x1, [x1 + 0]
  __il_cont#181:
    arm64.loadRegBaseDisp.word64 x2, [x24 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x24 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow#152
  __im_bounds#153:
    arm64.loadRegBaseDisp.word64 x3, [x24 + 16]
    arm64.cmp x2, x3
    arm64.b.hs __im_slow#152
  __im_buffer#154:
    arm64.loadRegBaseDisp.word64 x3, [x24 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow#152
  __im_viewed#155:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow#152
  __im_store#156:
    arm64.storeBaseIndexScaleReg.word64 [x3 + x2*8 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x24 + 8], x1
  forstep#108:
    arm64.add x0, x0, 1
  forhdr#106:
    arm64.cmp x0, x22
    arm64.b.lo keyWords
  forexit#109:
    arm64.sub x1, x22, 1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x0, x22, x16
    arm64.and x0, x0, 1
    arm64.eor x2, x22, 1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x2, x2, x16
    arm64.and x2, x2, x1
    arm64.orr x0, x0, x2
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#133
  __rc_ok#132:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 8]
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#158
  __im_load#159:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
  tryok#110:
    arm64.movImm x25, 0
    arm64.movImm x26, 0
    arm64.movRegReg x27, x26
    arm64.movRegReg x26, x25
    arm64.movRegReg x25, x22
    arm64.b forhdr#112
  eachWord:
    arm64.cmp x26, 0
    arm64.b.ne ifelse
  rotate:
    arm64.leaRdata x1, __barr_rec_28
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.cmp x27, x2
    arm64.b.hs __im_slow#143
  __im_load#144:
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.loadRegBaseIndexScale.word64 x28, [x1 + x27*8 + 0]
  __il_body#164:
    arm64.lsr x1, x0, 8
    arm64.lsl x0, x0, 24
    arm64.orr x0, x1, x0
    arm64.movImm x1, 4294967295
    arm64.and x0, x0, x1
  __il_cont#163:
    arm64.bl aesSubWord
    arm64.eor x0, x0, x28
    arm64.b ifcont#124
  ifelse:
    arm64.cmp x22, 8
    arm64.movImm x1, 0
    arm64.b.ne scmerge#121
  andrhs#120:
    arm64.cmp x26, 4
    arm64.cset x1, eq
  scmerge#121:
    arm64.cbz x1, ifcont#124
  longKey:
    arm64.bl aesSubWord
  ifcont#124:
    arm64.sub x1, x25, x22
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#135
  __rc_ok#134:
    arm64.loadRegBaseDisp.word64 x2, [x24 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#161
  __im_load#162:
    arm64.loadRegBaseDisp.word64 x2, [x24 + 0]
    arm64.loadRegBaseIndexScale.word64 x1, [x2 + x1*8 + 0]
  tryok#125:
    arm64.eor x1, x0, x1
    arm64.loadRegBaseDisp.word64 x0, [x24 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x24 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow#146
  __im_bounds#147:
    arm64.loadRegBaseDisp.word64 x2, [x24 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow#146
  __im_buffer#148:
    arm64.loadRegBaseDisp.word64 x2, [x24 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow#146
  __im_viewed#149:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#146
  __im_store#150:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x24 + 8], x0
  __im_cont#145:
    arm64.add x0, x26, 1
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#137
  __rc_chk#138:
    arm64.cmp x0, 8
    arm64.b.gt __rc_panic#137
  __rc_ok#136:
    arm64.cmp x0, x22
    arm64.b.ne critsplit#197
  wrap:
    arm64.movImm x0, 0
    arm64.add x2, x27, 1
    arm64.cmp x2, 0
    arm64.b.lt __rc_panic#140
  __rc_chk#141:
    arm64.cmp x2, 10
    arm64.b.gt __rc_panic#140
    arm64.b forstep#114
  critsplit#197:
    arm64.movRegReg x2, x27
  forstep#114:
    arm64.add x25, x25, 1
    arm64.movRegReg x26, x0
    arm64.movRegReg x0, x1
    arm64.movRegReg x27, x2
  forhdr#112:
    arm64.cmp x25, x23
    arm64.b.lo eachWord
  __il_cont#105:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.movImm x0, 8
    arm64.leaRdata x1, __layout_FixedArray_N8_AesWord
    arm64.bl FixedArray.create
    arm64.movRegReg x23, x0
    arm64.add x0, x21, 1
    arm64.lsl x1, x0, 3
    arm64.movRegReg x0, x22
    arm64.bl Array.reserve
    arm64.movImm x25, 0
    arm64.add x26, x21, 1
    arm64.b forhdr#41
  eachRound:
    arm64.lsl x1, x25, 2
  __rc_ok#62:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 8]
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#81
  __im_load#82:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
  tryok#45:
    arm64.add x2, x1, 1
  __rc_ok#64:
    arm64.loadRegBaseDisp.word64 x3, [x24 + 8]
    arm64.cmp x2, x3
    arm64.b.hs __im_slow#84
  __im_load#85:
    arm64.loadRegBaseDisp.word64 x3, [x24 + 0]
    arm64.loadRegBaseIndexScale.word64 x2, [x3 + x2*8 + 0]
  tryok#47:
    arm64.add x3, x1, 2
  __rc_ok#66:
    arm64.loadRegBaseDisp.word64 x4, [x24 + 8]
    arm64.cmp x3, x4
    arm64.b.hs __im_slow#87
  __im_load#88:
    arm64.loadRegBaseDisp.word64 x4, [x24 + 0]
    arm64.loadRegBaseIndexScale.word64 x3, [x4 + x3*8 + 0]
  tryok#49:
    arm64.add x1, x1, 3
  __rc_ok#68:
    arm64.loadRegBaseDisp.word64 x4, [x24 + 8]
    arm64.cmp x1, x4
    arm64.b.hs __im_slow#90
  __im_load#91:
    arm64.loadRegBaseDisp.word64 x4, [x24 + 0]
    arm64.loadRegBaseIndexScale.word64 x4, [x4 + x1*8 + 0]
  tryok#51:
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.movRegReg x3, x4
    arm64.bl aesPackColumns
    arm64.movRegReg x27, x0
    arm64.loadRegBaseDisp.word64 x2, [x27 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x27 + 8]
    arm64.movImm x1, 0
    arm64.b forhdr#53
  eachSlot:
    arm64.loadRegBaseDisp.word64 x0, [x23 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#71
  __im_bounds#72:
    arm64.loadRegBaseDisp.word64 x0, [x23 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#71
  __im_viewed#73:
    arm64.sub x4, x0, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow#71
  __im_store#74:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x1*8 + 0], x2
  __im_cont#70:
    arm64.add x4, x1, 4
    arm64.loadRegBaseDisp.word64 x0, [x23 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#76
  __im_bounds#77:
    arm64.loadRegBaseDisp.word64 x0, [x23 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#76
  __im_viewed#78:
    arm64.sub x5, x0, 16
    arm64.loadRegBaseDisp.word64 x5, [x5 + 8]
    arm64.cmp x5, 0
    arm64.b.ne __im_slow#76
  __im_store#79:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x4*8 + 0], x3
  forstep#55:
    arm64.add x1, x1, 1
  forhdr#53:
    arm64.cmp x1, 4
    arm64.b.lt eachSlot
  forexit#56:
    arm64.movRegReg x0, x23
    arm64.bl aesOrtho
  __il_body#93:
    arm64.movImm x0, 0
    arm64.b forhdr#94
  eachPlane:
    arm64.loadRegBaseDisp.word64 x1, [x23 + 0]
    arm64.loadRegBaseIndexScale.word64 x1, [x1 + x0*8 + 0]
  __im_cont#98:
    arm64.loadRegBaseDisp.word64 x2, [x22 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x22 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow#100
  __im_bounds#101:
    arm64.loadRegBaseDisp.word64 x3, [x22 + 16]
    arm64.cmp x2, x3
    arm64.b.hs __im_slow#100
  __im_buffer#102:
    arm64.loadRegBaseDisp.word64 x3, [x22 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow#100
  __im_viewed#103:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow#100
  __im_store#104:
    arm64.storeBaseIndexScaleReg.word64 [x3 + x2*8 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x22 + 8], x1
  forstep#96:
    arm64.add x0, x0, 1
  forhdr#94:
    arm64.cmp x0, 8
    arm64.b.lo eachPlane
  __il_cont#92:
    arm64.movRegReg x0, x27
    arm64.bl __mm_decref
  forstep#43:
    arm64.add x25, x25, 1
  forhdr#41:
    arm64.cmp x25, x26
    arm64.b.lo eachRound
  forexit#44:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
  exitdrop#58:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  __il_cont#39:
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x22
    arm64.storeBaseDispReg.word64 [x0 + 8], x21
    arm64.movImm x1, 0
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x1
  __il_cont#32:
    arm64.cmp x0, 0
    arm64.b.ne tryerr#2
  tryok#26:
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x22 + 0], x0
    arm64.storeBaseDispReg.word64 [x22 + 8], x21
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x22
    arm64.movRegReg x21, x0
  tryok#1:
    arm64.movImm x3, 0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x20
    arm64.bl Aes.encryptBlockInto
    arm64.cmp x9, 0
    arm64.b.ne tryerr#4
  tryok#3:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow#12
  __im_load#13:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b tryok#5
  __im_stride:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#12
  __im_byte#15:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok#5
  __im_slow#12:
    arm64.movRegReg x0, x20
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok#5
  tryerr#6:
    arm64.movImm x22, 0
    arm64.b trycont
  tryok#5:
    arm64.movRegReg x22, x0
  trycont:
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.bl __uint_to_string
    arm64.movRegReg x22, x0
    arm64.leaRdata x0, __str_rec_8  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x22, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x22, x28, x22
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x22, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x23
    arm64.bl __mm_decref
  __il_body#19:
    arm64.movRegReg x0, x27
    arm64.bl __write_stdout
  __il_cont#18:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x22, 0
    arm64.movRegReg x0, x21
    arm64.bl __destruct_Aes
  exitdrop#9:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitdrop#10:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x22
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  __im_slow#186:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_word_get_le4
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#184
    arm64.b __il_cont#181
  __im_slow#152:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x24
    arm64.bl __managed_push
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b forstep#108
  __im_slow#158:
    arm64.movRegReg x0, x24
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#111
    arm64.b tryok#110
  __im_slow#143:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x27
    arm64.bl __managed_get
    arm64.movRegReg x28, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#118
    arm64.b __il_body#164
  __im_slow#161:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x24
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#126
    arm64.b tryok#125
  __im_slow#146:
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x24
    arm64.bl __managed_push
    arm64.loadRegSlot x1, slot1
    arm64.b __im_cont#145
  __im_slow#81:
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x24
    arm64.bl __managed_get
    arm64.loadRegSlot x1, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#46
    arm64.b tryok#45
  __im_slow#84:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x2
    arm64.bl __managed_get
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#48
    arm64.b tryok#47
  __im_slow#87:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x3
    arm64.bl __managed_get
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.cmp x9, 0
    arm64.b.ne tryerr#50
    arm64.b tryok#49
  __im_slow#90:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.movRegReg x0, x24
    arm64.bl __managed_get
    arm64.movRegReg x4, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.cmp x9, 0
    arm64.b.ne tryerr#52
    arm64.b tryok#51
  __im_slow#71:
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.movRegReg x0, x23
    arm64.bl __managed_set_unchecked
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.b __im_cont#70
  __im_slow#76:
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x4
    arm64.movRegReg x2, x3
    arm64.bl __managed_set_unchecked
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.b forstep#55
  __im_slow#100:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x22
    arm64.bl __managed_push
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b forstep#96
  __rc_panic#37:
    arm64.leaRdata x0, __str_blob_81  ; "panic at AesBitsliced.maxon:713: Range check failed: value outside typealias 'AesRoundCount'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  __rc_panic#131:
    arm64.leaRdata x0, __str_blob_69  ; "panic at AesBitsliced.maxon:601: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#184:
    arm64.leaRdata x0, __str_blob_25  ; "panic at AesBitsliced.maxon:122: aes: a word inside an array the caller sized is missing\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  __rc_panic#133:
    arm64.leaRdata x0, __str_blob_70  ; "panic at AesBitsliced.maxon:604: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#111:
    arm64.leaRdata x0, __str_blob_27  ; "panic at AesBitsliced.maxon:604: aes: key word index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#118:
    arm64.leaRdata x0, __str_blob_29  ; "panic at AesBitsliced.maxon:612: aes: round constant index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  __rc_panic#135:
    arm64.leaRdata x0, __str_blob_71  ; "panic at AesBitsliced.maxon:618: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#126:
    arm64.leaRdata x0, __str_blob_30  ; "panic at AesBitsliced.maxon:618: aes: key word index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  __rc_panic#137:
    arm64.leaRdata x0, __str_blob_72  ; "panic at AesBitsliced.maxon:621: Range check failed: value outside typealias 'AesKeySlot'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  __rc_panic#140:
    arm64.leaRdata x0, __str_blob_73  ; "panic at AesBitsliced.maxon:625: Range check failed: value outside typealias 'AesConstantIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#46:
    arm64.leaRdata x0, __str_blob_31  ; "panic at AesBitsliced.maxon:646: aes: key word index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#48:
    arm64.leaRdata x0, __str_blob_32  ; "panic at AesBitsliced.maxon:647: aes: key word index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#50:
    arm64.leaRdata x0, __str_blob_33  ; "panic at AesBitsliced.maxon:648: aes: key word index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#52:
    arm64.leaRdata x0, __str_blob_34  ; "panic at AesBitsliced.maxon:649: aes: key word index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#2:
    arm64.leaRdata x0, __str_blob_6  ; "panic at aes-hardware.a-program-that-only-encrypts-leaves-the-software-cipher-a-call-and-the-block-frame-lean.test:7: key\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#4:
    arm64.leaRdata x0, __str_blob_7  ; "panic at aes-hardware.a-program-that-only-encrypts-leaves-the-software-cipher-a-call-and-the-block-frame-lean.test:8: block\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
}

func @Aes.encryptBlockInto {
  entry:
    arm64.prologue 16
    arm64.movRegReg x5, x1
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 0
    arm64.b.ne matchnext
  matcharm#1:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.movImm x4, 0
    arm64.movRegReg x1, x5
    arm64.bl AesBitsliced.cryptBlockInto
    arm64.cmp x9, 0
    arm64.b.ne tryerr#3
    arm64.b matchcont
  matchnext:
    arm64.cmp x1, 1
    arm64.b.ne matchdead
  matcharm#5:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 8]
  __il_body#12:
    arm64.loadRegBaseDisp.word64 x0, [x5 + 8]
    arm64.cmp x0, 16
    arm64.movImm x0, 1
    arm64.b.ne scmerge
  __il_body#20:
    arm64.loadRegBaseDisp.word64 x0, [x2 + 8]
    arm64.add x1, x3, 16
    arm64.cmp x0, x1
    arm64.cset x0, lo
  scmerge:
    arm64.cbz x0, ifcont
  refit:
    arm64.movImm x4, 0
    arm64.movRegReg x0, x6
    arm64.movRegReg x1, x5
    arm64.bl aesHardwareCryptRefitted
    arm64.cmp x9, 0
    arm64.b.ne tryerr#17
  tryok:
    arm64.movImm x0, 0
    arm64.b __il_cont
  ifcont:
    arm64.movImm x4, 0
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x3
    arm64.movRegReg x2, x6
    arm64.movRegReg x3, x5
    arm64.bl __cpufeat_aes_cipher_encrypt_block
    arm64.movImm x0, 0
    arm64.b __il_cont
  tryerr#17:
    arm64.movRegReg x0, x9
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.ne tryerr#7
    arm64.b matchcont
  tryerr#3:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  tryerr#7:
    arm64.movRegReg x9, x0
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  matchcont:
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.epilogue 16
    arm64.ret
  matchdead:
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
    arm64.prologue 128
    arm64.storeSlotReg slot13, x28
    arm64.storeSlotReg slot12, x27
    arm64.storeSlotReg slot11, x26
    arm64.storeSlotReg slot10, x25
    arm64.storeSlotReg slot9, x24
    arm64.storeSlotReg slot8, x23
    arm64.storeSlotReg slot7, x22
    arm64.storeSlotReg slot6, x21
    arm64.storeSlotReg slot5, x20
    arm64.storeSlotReg slot4, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x1, 16
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x1, 16
    arm64.movRegReg x0, x20
    arm64.bl __managed_resize
  __il_body#29:
    arm64.bl __cpufeat_aes_usable
  __il_cont#28:
    arm64.cbz x0, __il_body#174
  __il_body#166:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 16
    arm64.movImm x0, 0
    arm64.b.eq scmerge#168
  andrhs#167:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 24
    arm64.cset x0, ne
  scmerge#168:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#202
  andrhs#169:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 32
    arm64.cset x0, ne
    arm64.b scmerge#170
  critsplit#202:
    arm64.movRegReg x0, x1
  scmerge#170:
    arm64.cbz x0, ifcont#172
  keyLength#171:
    arm64.movImm x0, 1
    arm64.b __il_cont#165
  ifcont#172:
    arm64.movImm x0, 0
  __il_cont#165:
    arm64.cmp x0, 0
    arm64.b.ne tryerr#2
  __il_body#31:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.lsr x0, x0, 2
    arm64.add x21, x0, 6
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.add x0, x21, 1
    arm64.lsl x1, x0, 5
    arm64.movRegReg x0, x22
    arm64.bl __managed_resize
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x19
    arm64.bl __cpufeat_aes_cipher_expand_key
  __il_cont#30:
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x21 + 0], x0
    arm64.storeBaseDispReg.word64 [x21 + 8], x22
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x21
    arm64.movRegReg x21, x0
    arm64.b tryok#1
  __il_body#174:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 16
    arm64.movImm x0, 0
    arm64.b.eq scmerge#176
  andrhs#175:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 24
    arm64.cset x0, ne
  scmerge#176:
    arm64.movImm x1, 0
    arm64.cbz x0, critsplit#204
  andrhs#177:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 32
    arm64.cset x0, ne
    arm64.b scmerge#178
  critsplit#204:
    arm64.movRegReg x0, x1
  scmerge#178:
    arm64.cbz x0, ifcont#180
  keyLength#179:
    arm64.movImm x0, 1
    arm64.b __il_cont#173
  ifcont#180:
    arm64.movImm x0, 0
  __il_cont#173:
    arm64.cmp x0, 0
    arm64.b.eq tryok#34
  tryerr#35:
    arm64.movImm x21, 0
    arm64.b __il_cont#32
  tryok#34:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.lsr x0, x0, 2
    arm64.add x21, x0, 6
    arm64.cmp x21, 10
    arm64.b.lt __rc_panic#37
  __rc_chk#38:
    arm64.cmp x21, 14
    arm64.b.gt __rc_panic#37
  __il_body#129:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.lsr x22, x0, 2
    arm64.add x0, x21, 1
    arm64.lsl x23, x0, 2
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x23
    arm64.bl Array.reserve
    arm64.movImm x0, 0
    arm64.b forhdr#106
  keyWords:
    arm64.lsl x1, x0, 2
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#131
  __il_body#182:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#186
  __im_bounds#187:
    arm64.sub x2, x2, x1
    arm64.cmp x2, 4
    arm64.b.lt __im_slow#186
  __im_load#188:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 24]
    arm64.cmp x2, 1
    arm64.b.ne __im_slow#186
  __im_byte#189:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.add x1, x2, x1
    arm64.loadRegBaseDisp.word32 x1, [x1 + 0]
  __il_cont#181:
    arm64.loadRegBaseDisp.word64 x2, [x24 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x24 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow#152
  __im_bounds#153:
    arm64.loadRegBaseDisp.word64 x3, [x24 + 16]
    arm64.cmp x2, x3
    arm64.b.hs __im_slow#152
  __im_buffer#154:
    arm64.loadRegBaseDisp.word64 x3, [x24 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow#152
  __im_viewed#155:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow#152
  __im_store#156:
    arm64.storeBaseIndexScaleReg.word64 [x3 + x2*8 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x24 + 8], x1
  forstep#108:
    arm64.add x0, x0, 1
  forhdr#106:
    arm64.cmp x0, x22
    arm64.b.lo keyWords
  forexit#109:
    arm64.sub x1, x22, 1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x0, x22, x16
    arm64.and x0, x0, 1
    arm64.eor x2, x22, 1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x2, x2, x16
    arm64.and x2, x2, x1
    arm64.orr x0, x0, x2
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#133
  __rc_ok#132:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 8]
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#158
  __im_load#159:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
  tryok#110:
    arm64.movImm x25, 0
    arm64.movImm x26, 0
    arm64.movRegReg x27, x26
    arm64.movRegReg x26, x25
    arm64.movRegReg x25, x22
    arm64.b forhdr#112
  eachWord:
    arm64.cmp x26, 0
    arm64.b.ne ifelse
  rotate:
    arm64.leaRdata x1, __barr_rec_28
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.cmp x27, x2
    arm64.b.hs __im_slow#143
  __im_load#144:
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.loadRegBaseIndexScale.word64 x28, [x1 + x27*8 + 0]
  __il_body#164:
    arm64.lsr x1, x0, 8
    arm64.lsl x0, x0, 24
    arm64.orr x0, x1, x0
    arm64.movImm x1, 4294967295
    arm64.and x0, x0, x1
  __il_cont#163:
    arm64.bl aesSubWord
    arm64.eor x0, x0, x28
    arm64.b ifcont#124
  ifelse:
    arm64.cmp x22, 8
    arm64.movImm x1, 0
    arm64.b.ne scmerge#121
  andrhs#120:
    arm64.cmp x26, 4
    arm64.cset x1, eq
  scmerge#121:
    arm64.cbz x1, ifcont#124
  longKey:
    arm64.bl aesSubWord
  ifcont#124:
    arm64.sub x1, x25, x22
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#135
  __rc_ok#134:
    arm64.loadRegBaseDisp.word64 x2, [x24 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#161
  __im_load#162:
    arm64.loadRegBaseDisp.word64 x2, [x24 + 0]
    arm64.loadRegBaseIndexScale.word64 x1, [x2 + x1*8 + 0]
  tryok#125:
    arm64.eor x1, x0, x1
    arm64.loadRegBaseDisp.word64 x0, [x24 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x24 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow#146
  __im_bounds#147:
    arm64.loadRegBaseDisp.word64 x2, [x24 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow#146
  __im_buffer#148:
    arm64.loadRegBaseDisp.word64 x2, [x24 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow#146
  __im_viewed#149:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#146
  __im_store#150:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x24 + 8], x0
  __im_cont#145:
    arm64.add x0, x26, 1
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#137
  __rc_chk#138:
    arm64.cmp x0, 8
    arm64.b.gt __rc_panic#137
  __rc_ok#136:
    arm64.cmp x0, x22
    arm64.b.ne critsplit#197
  wrap:
    arm64.movImm x0, 0
    arm64.add x2, x27, 1
    arm64.cmp x2, 0
    arm64.b.lt __rc_panic#140
  __rc_chk#141:
    arm64.cmp x2, 10
    arm64.b.gt __rc_panic#140
    arm64.b forstep#114
  critsplit#197:
    arm64.movRegReg x2, x27
  forstep#114:
    arm64.add x25, x25, 1
    arm64.movRegReg x26, x0
    arm64.movRegReg x0, x1
    arm64.movRegReg x27, x2
  forhdr#112:
    arm64.cmp x25, x23
    arm64.b.lo eachWord
  __il_cont#105:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.movImm x0, 8
    arm64.leaRdata x1, __layout_FixedArray_N8_AesWord
    arm64.bl FixedArray.create
    arm64.movRegReg x23, x0
    arm64.add x0, x21, 1
    arm64.lsl x1, x0, 3
    arm64.movRegReg x0, x22
    arm64.bl Array.reserve
    arm64.movImm x25, 0
    arm64.add x26, x21, 1
    arm64.b forhdr#41
  eachRound:
    arm64.lsl x1, x25, 2
  __rc_ok#62:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 8]
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#81
  __im_load#82:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
  tryok#45:
    arm64.add x2, x1, 1
  __rc_ok#64:
    arm64.loadRegBaseDisp.word64 x3, [x24 + 8]
    arm64.cmp x2, x3
    arm64.b.hs __im_slow#84
  __im_load#85:
    arm64.loadRegBaseDisp.word64 x3, [x24 + 0]
    arm64.loadRegBaseIndexScale.word64 x2, [x3 + x2*8 + 0]
  tryok#47:
    arm64.add x3, x1, 2
  __rc_ok#66:
    arm64.loadRegBaseDisp.word64 x4, [x24 + 8]
    arm64.cmp x3, x4
    arm64.b.hs __im_slow#87
  __im_load#88:
    arm64.loadRegBaseDisp.word64 x4, [x24 + 0]
    arm64.loadRegBaseIndexScale.word64 x3, [x4 + x3*8 + 0]
  tryok#49:
    arm64.add x1, x1, 3
  __rc_ok#68:
    arm64.loadRegBaseDisp.word64 x4, [x24 + 8]
    arm64.cmp x1, x4
    arm64.b.hs __im_slow#90
  __im_load#91:
    arm64.loadRegBaseDisp.word64 x4, [x24 + 0]
    arm64.loadRegBaseIndexScale.word64 x4, [x4 + x1*8 + 0]
  tryok#51:
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.movRegReg x3, x4
    arm64.bl aesPackColumns
    arm64.movRegReg x27, x0
    arm64.loadRegBaseDisp.word64 x2, [x27 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x27 + 8]
    arm64.movImm x1, 0
    arm64.b forhdr#53
  eachSlot:
    arm64.loadRegBaseDisp.word64 x0, [x23 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#71
  __im_bounds#72:
    arm64.loadRegBaseDisp.word64 x0, [x23 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#71
  __im_viewed#73:
    arm64.sub x4, x0, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow#71
  __im_store#74:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x1*8 + 0], x2
  __im_cont#70:
    arm64.add x4, x1, 4
    arm64.loadRegBaseDisp.word64 x0, [x23 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#76
  __im_bounds#77:
    arm64.loadRegBaseDisp.word64 x0, [x23 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#76
  __im_viewed#78:
    arm64.sub x5, x0, 16
    arm64.loadRegBaseDisp.word64 x5, [x5 + 8]
    arm64.cmp x5, 0
    arm64.b.ne __im_slow#76
  __im_store#79:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x4*8 + 0], x3
  forstep#55:
    arm64.add x1, x1, 1
  forhdr#53:
    arm64.cmp x1, 4
    arm64.b.lt eachSlot
  forexit#56:
    arm64.movRegReg x0, x23
    arm64.bl aesOrtho
  __il_body#93:
    arm64.movImm x0, 0
    arm64.b forhdr#94
  eachPlane:
    arm64.loadRegBaseDisp.word64 x1, [x23 + 0]
    arm64.loadRegBaseIndexScale.word64 x1, [x1 + x0*8 + 0]
  __im_cont#98:
    arm64.loadRegBaseDisp.word64 x2, [x22 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x22 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow#100
  __im_bounds#101:
    arm64.loadRegBaseDisp.word64 x3, [x22 + 16]
    arm64.cmp x2, x3
    arm64.b.hs __im_slow#100
  __im_buffer#102:
    arm64.loadRegBaseDisp.word64 x3, [x22 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow#100
  __im_viewed#103:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow#100
  __im_store#104:
    arm64.storeBaseIndexScaleReg.word64 [x3 + x2*8 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x22 + 8], x1
  forstep#96:
    arm64.add x0, x0, 1
  forhdr#94:
    arm64.cmp x0, 8
    arm64.b.lo eachPlane
  __il_cont#92:
    arm64.movRegReg x0, x27
    arm64.bl __mm_decref
  forstep#43:
    arm64.add x25, x25, 1
  forhdr#41:
    arm64.cmp x25, x26
    arm64.b.lo eachRound
  forexit#44:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
  exitdrop#58:
    arm64.movRegReg x0, x24
    arm64.bl __managed_decref
  __il_cont#39:
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x22
    arm64.storeBaseDispReg.word64 [x0 + 8], x21
    arm64.movImm x1, 0
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x1
  __il_cont#32:
    arm64.cmp x0, 0
    arm64.b.ne tryerr#2
  tryok#26:
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x22 + 0], x0
    arm64.storeBaseDispReg.word64 [x22 + 8], x21
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x22
    arm64.movRegReg x21, x0
  tryok#1:
    arm64.movImm x3, 0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x20
    arm64.bl Aes.encryptBlockInto
    arm64.cmp x9, 0
    arm64.b.ne tryerr#4
  tryok#3:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow#12
  __im_load#13:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b tryok#5
  __im_stride:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#12
  __im_byte#15:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok#5
  __im_slow#12:
    arm64.movRegReg x0, x20
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.eq tryok#5
  tryerr#6:
    arm64.movImm x22, 0
    arm64.b trycont
  tryok#5:
    arm64.movRegReg x22, x0
  trycont:
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.bl __uint_to_string
    arm64.movRegReg x22, x0
    arm64.leaRdata x0, __str_rec_8  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x22, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x22, x28, x22
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x22, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x23
    arm64.bl __mm_decref
  __il_body#19:
    arm64.movRegReg x0, x27
    arm64.bl __write_stdout
  __il_cont#18:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x22, 0
    arm64.movRegReg x0, x21
    arm64.bl __destruct_Aes
  exitdrop#9:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitdrop#10:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x22
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  __im_slow#186:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_word_get_le4
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#184
    arm64.b __il_cont#181
  __im_slow#152:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x24
    arm64.bl __managed_push
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b forstep#108
  __im_slow#158:
    arm64.movRegReg x0, x24
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#111
    arm64.b tryok#110
  __im_slow#143:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x27
    arm64.bl __managed_get
    arm64.movRegReg x28, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#118
    arm64.b __il_body#164
  __im_slow#161:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x24
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#126
    arm64.b tryok#125
  __im_slow#146:
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x24
    arm64.bl __managed_push
    arm64.loadRegSlot x1, slot1
    arm64.b __im_cont#145
  __im_slow#81:
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x24
    arm64.bl __managed_get
    arm64.loadRegSlot x1, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#46
    arm64.b tryok#45
  __im_slow#84:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x2
    arm64.bl __managed_get
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#48
    arm64.b tryok#47
  __im_slow#87:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x3
    arm64.bl __managed_get
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.cmp x9, 0
    arm64.b.ne tryerr#50
    arm64.b tryok#49
  __im_slow#90:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.movRegReg x0, x24
    arm64.bl __managed_get
    arm64.movRegReg x4, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.cmp x9, 0
    arm64.b.ne tryerr#52
    arm64.b tryok#51
  __im_slow#71:
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.movRegReg x0, x23
    arm64.bl __managed_set_unchecked
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.b __im_cont#70
  __im_slow#76:
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x4
    arm64.movRegReg x2, x3
    arm64.bl __managed_set_unchecked
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.b forstep#55
  __im_slow#100:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x22
    arm64.bl __managed_push
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b forstep#96
  __rc_panic#37:
    arm64.leaRdata x0, __str_blob_81  ; "panic at AesBitsliced.maxon:713: Range check failed: value outside typealias 'AesRoundCount'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  __rc_panic#131:
    arm64.leaRdata x0, __str_blob_69  ; "panic at AesBitsliced.maxon:601: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#184:
    arm64.leaRdata x0, __str_blob_25  ; "panic at AesBitsliced.maxon:122: aes: a word inside an array the caller sized is missing\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  __rc_panic#133:
    arm64.leaRdata x0, __str_blob_70  ; "panic at AesBitsliced.maxon:604: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#111:
    arm64.leaRdata x0, __str_blob_27  ; "panic at AesBitsliced.maxon:604: aes: key word index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#118:
    arm64.leaRdata x0, __str_blob_29  ; "panic at AesBitsliced.maxon:612: aes: round constant index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  __rc_panic#135:
    arm64.leaRdata x0, __str_blob_71  ; "panic at AesBitsliced.maxon:618: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#126:
    arm64.leaRdata x0, __str_blob_30  ; "panic at AesBitsliced.maxon:618: aes: key word index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  __rc_panic#137:
    arm64.leaRdata x0, __str_blob_72  ; "panic at AesBitsliced.maxon:621: Range check failed: value outside typealias 'AesKeySlot'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  __rc_panic#140:
    arm64.leaRdata x0, __str_blob_73  ; "panic at AesBitsliced.maxon:625: Range check failed: value outside typealias 'AesConstantIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#46:
    arm64.leaRdata x0, __str_blob_31  ; "panic at AesBitsliced.maxon:646: aes: key word index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#48:
    arm64.leaRdata x0, __str_blob_32  ; "panic at AesBitsliced.maxon:647: aes: key word index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#50:
    arm64.leaRdata x0, __str_blob_33  ; "panic at AesBitsliced.maxon:648: aes: key word index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#52:
    arm64.leaRdata x0, __str_blob_34  ; "panic at AesBitsliced.maxon:649: aes: key word index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#2:
    arm64.leaRdata x0, __str_blob_6  ; "panic at aes-hardware.a-program-that-only-encrypts-leaves-the-software-cipher-a-call-and-the-block-frame-lean.test:7: key\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
  tryerr#4:
    arm64.leaRdata x0, __str_blob_7  ; "panic at aes-hardware.a-program-that-only-encrypts-leaves-the-software-cipher-a-call-and-the-block-frame-lean.test:8: block\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.loadRegSlot x24, slot9
    arm64.loadRegSlot x25, slot10
    arm64.loadRegSlot x26, slot11
    arm64.loadRegSlot x27, slot12
    arm64.loadRegSlot x28, slot13
    arm64.epilogue 128
    arm64.ret
}

func @Aes.encryptBlockInto {
  entry:
    arm64.prologue 16
    arm64.movRegReg x5, x1
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 0
    arm64.b.ne matchnext
  matcharm#1:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.movImm x4, 0
    arm64.movRegReg x1, x5
    arm64.bl AesBitsliced.cryptBlockInto
    arm64.cmp x9, 0
    arm64.b.ne tryerr#3
    arm64.b matchcont
  matchnext:
    arm64.cmp x1, 1
    arm64.b.ne matchdead
  matcharm#5:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 8]
  __il_body#12:
    arm64.loadRegBaseDisp.word64 x0, [x5 + 8]
    arm64.cmp x0, 16
    arm64.movImm x0, 1
    arm64.b.ne scmerge
  __il_body#20:
    arm64.loadRegBaseDisp.word64 x0, [x2 + 8]
    arm64.add x1, x3, 16
    arm64.cmp x0, x1
    arm64.cset x0, lo
  scmerge:
    arm64.cbz x0, ifcont
  refit:
    arm64.movImm x4, 0
    arm64.movRegReg x0, x6
    arm64.movRegReg x1, x5
    arm64.bl aesHardwareCryptRefitted
    arm64.cmp x9, 0
    arm64.b.ne tryerr#17
  tryok:
    arm64.movImm x0, 0
    arm64.b __il_cont
  ifcont:
    arm64.movImm x4, 0
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x3
    arm64.movRegReg x2, x6
    arm64.movRegReg x3, x5
    arm64.bl __cpufeat_aes_cipher_encrypt_block
    arm64.movImm x0, 0
    arm64.b __il_cont
  tryerr#17:
    arm64.movRegReg x0, x9
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.ne tryerr#7
    arm64.b matchcont
  tryerr#3:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  tryerr#7:
    arm64.movRegReg x9, x0
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  matchcont:
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.epilogue 16
    arm64.ret
  matchdead:
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
    x64.prologue 80
    x64.loadRegBaseDisp.word64 r8, [rax + 8]
    x64.cmpRegImm32 r8, 352
    x64.jcc less, cxbad
  scbounded:
    x64.cmpRegImm32 r8, 480
    x64.jcc greater, cxbad
  scstepped:
    x64.movRegReg r10, r8
    x64.andRegImm32 r10, r8, 63
    x64.cmpRegImm32 r10, 32
    x64.jcc notEqual, cxbad
  cxcounter:
    x64.loadRegBaseDisp.word64 r10, [r9 + 8]
    x64.cmpRegImm32 r10, 16
    x64.jcc notEqual, cxbad
  cxdispatch:
    x64.cmpRegImm32 rdi, 1
    x64.jcc notEqual, cxprobe1
  cxarm1:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 8]
    x64.cmpRegReg rdx, rdi
    x64.jcc aboveEqual, cxbad
  cxspan1:
    x64.subRegReg rdi, rdi, rdx
    x64.cmpRegImm32 rdi, 16
    x64.jcc less, cxbad
  cxcrypt1:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, cx1ddetach
  cx1dbuf:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, cx1ddone
  cx1dshared:
    x64.leaRegRegImm32 rdi, rdi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, cx1ddetach
  cx1ddone:
    x64.shrRegImm8 r8, r8, 5
    x64.leaRegRegImm32 rdi, r8, -1
    x64.shlRegImm8 rsi, rsi, 8
    x64.orRegReg rsi, rsi, rdi
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 r8, [r9 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.leaRegRegReg r9, rcx, rdx
    x64.movRegReg rcx, rsi
    x64.movRegReg rdx, rax
    x64.callDirect mrt_aes_ctr_blocks_1
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
  cxprobe1:
    x64.cmpRegImm32 rdi, 2
    x64.jcc notEqual, cxprobe2
  cxarm2:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 8]
    x64.cmpRegReg rdx, rdi
    x64.jcc aboveEqual, cxbad
  cxspan2:
    x64.subRegReg rdi, rdi, rdx
    x64.cmpRegImm32 rdi, 32
    x64.jcc less, cxbad
  cxcrypt2:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, cx2ddetach
  cx2dbuf:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, cx2ddone
  cx2dshared:
    x64.leaRegRegImm32 rdi, rdi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, cx2ddetach
  cx2ddone:
    x64.shrRegImm8 r8, r8, 5
    x64.leaRegRegImm32 rdi, r8, -1
    x64.shlRegImm8 rsi, rsi, 8
    x64.orRegReg rsi, rsi, rdi
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 r8, [r9 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.leaRegRegReg r9, rcx, rdx
    x64.movRegReg rcx, rsi
    x64.movRegReg rdx, rax
    x64.callDirect mrt_aes_ctr_blocks_2
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
  cxprobe2:
    x64.cmpRegImm32 rdi, 4
    x64.jcc notEqual, cxprobe4
  cxarm4:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 8]
    x64.cmpRegReg rdx, rdi
    x64.jcc aboveEqual, cxbad
  cxspan4:
    x64.subRegReg rdi, rdi, rdx
    x64.cmpRegImm32 rdi, 64
    x64.jcc less, cxbad
  cxcrypt4:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, cx4ddetach
  cx4dbuf:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, cx4ddone
  cx4dshared:
    x64.leaRegRegImm32 rdi, rdi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, cx4ddetach
  cx4ddone:
    x64.shrRegImm8 r8, r8, 5
    x64.leaRegRegImm32 rdi, r8, -1
    x64.shlRegImm8 rsi, rsi, 8
    x64.orRegReg rsi, rsi, rdi
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 r8, [r9 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.leaRegRegReg r9, rcx, rdx
    x64.movRegReg rcx, rsi
    x64.movRegReg rdx, rax
    x64.callDirect mrt_aes_ctr_blocks_4
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
  cxprobe4:
    x64.cmpRegImm32 rdi, 8
    x64.jcc notEqual, cxbad
  cxarm8:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 8]
    x64.cmpRegReg rdx, rdi
    x64.jcc aboveEqual, cxbad
  cxspan8:
    x64.subRegReg rdi, rdi, rdx
    x64.cmpRegImm32 rdi, 128
    x64.jcc less, cxbad
  cxcrypt8:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, cx8ddetach
  cx8dbuf:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, cx8ddone
  cx8dshared:
    x64.leaRegRegImm32 rdi, rdi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, cx8ddetach
  cx8ddone:
    x64.shrRegImm8 r8, r8, 5
    x64.leaRegRegImm32 rdi, r8, -1
    x64.shlRegImm8 rsi, rsi, 8
    x64.orRegReg rsi, rsi, rdi
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 r8, [r9 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.leaRegRegReg r9, rcx, rdx
    x64.movRegReg rcx, rsi
    x64.movRegReg rdx, rax
    x64.callDirect mrt_aes_ctr_blocks_8
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
  cx1ddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, r8
    x64.storeSlotReg slot5, r9
    x64.callDirect __managed_cow_detach
    x64.movRegReg rdi, r8
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot r8, slot4
    x64.loadRegSlot r9, slot5
    x64.jmp cx1ddone
  cx2ddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, r8
    x64.storeSlotReg slot5, r9
    x64.callDirect __managed_cow_detach
    x64.movRegReg rdi, r8
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot r8, slot4
    x64.loadRegSlot r9, slot5
    x64.jmp cx2ddone
  cx4ddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, r8
    x64.storeSlotReg slot5, r9
    x64.callDirect __managed_cow_detach
    x64.movRegReg rdi, r8
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot r8, slot4
    x64.loadRegSlot r9, slot5
    x64.jmp cx4ddone
  cx8ddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, r8
    x64.storeSlotReg slot5, r9
    x64.callDirect __managed_cow_detach
    x64.movRegReg rdi, r8
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot r8, slot4
    x64.loadRegSlot r9, slot5
    x64.jmp cx8ddone
  cxbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 80
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
    x64.prologue 80
    x64.loadRegBaseDisp.word64 r8, [rax + 8]
    x64.cmpRegImm32 r8, 352
    x64.jcc less, cxbad
  scbounded:
    x64.cmpRegImm32 r8, 480
    x64.jcc greater, cxbad
  scstepped:
    x64.movRegReg r10, r8
    x64.andRegImm32 r10, r8, 63
    x64.cmpRegImm32 r10, 32
    x64.jcc notEqual, cxbad
  cxcounter:
    x64.loadRegBaseDisp.word64 r10, [r9 + 8]
    x64.cmpRegImm32 r10, 16
    x64.jcc notEqual, cxbad
  cxdispatch:
    x64.cmpRegImm32 rdi, 1
    x64.jcc notEqual, cxprobe1
  cxarm1:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 8]
    x64.cmpRegReg rdx, rdi
    x64.jcc aboveEqual, cxbad
  cxspan1:
    x64.subRegReg rdi, rdi, rdx
    x64.cmpRegImm32 rdi, 16
    x64.jcc less, cxbad
  cxcrypt1:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, cx1ddetach
  cx1dbuf:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, cx1ddone
  cx1dshared:
    x64.leaRegRegImm32 rdi, rdi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, cx1ddetach
  cx1ddone:
    x64.shrRegImm8 r8, r8, 5
    x64.leaRegRegImm32 rdi, r8, -1
    x64.shlRegImm8 rsi, rsi, 8
    x64.orRegReg rsi, rsi, rdi
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 r8, [r9 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.leaRegRegReg r9, rcx, rdx
    x64.movRegReg rcx, rsi
    x64.movRegReg rdx, rax
    x64.callDirect mrt_aes_ctr_blocks_1
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
  cxprobe1:
    x64.cmpRegImm32 rdi, 2
    x64.jcc notEqual, cxprobe2
  cxarm2:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 8]
    x64.cmpRegReg rdx, rdi
    x64.jcc aboveEqual, cxbad
  cxspan2:
    x64.subRegReg rdi, rdi, rdx
    x64.cmpRegImm32 rdi, 32
    x64.jcc less, cxbad
  cxcrypt2:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, cx2ddetach
  cx2dbuf:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, cx2ddone
  cx2dshared:
    x64.leaRegRegImm32 rdi, rdi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, cx2ddetach
  cx2ddone:
    x64.shrRegImm8 r8, r8, 5
    x64.leaRegRegImm32 rdi, r8, -1
    x64.shlRegImm8 rsi, rsi, 8
    x64.orRegReg rsi, rsi, rdi
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 r8, [r9 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.leaRegRegReg r9, rcx, rdx
    x64.movRegReg rcx, rsi
    x64.movRegReg rdx, rax
    x64.callDirect mrt_aes_ctr_blocks_2
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
  cxprobe2:
    x64.cmpRegImm32 rdi, 4
    x64.jcc notEqual, cxprobe4
  cxarm4:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 8]
    x64.cmpRegReg rdx, rdi
    x64.jcc aboveEqual, cxbad
  cxspan4:
    x64.subRegReg rdi, rdi, rdx
    x64.cmpRegImm32 rdi, 64
    x64.jcc less, cxbad
  cxcrypt4:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, cx4ddetach
  cx4dbuf:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, cx4ddone
  cx4dshared:
    x64.leaRegRegImm32 rdi, rdi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, cx4ddetach
  cx4ddone:
    x64.shrRegImm8 r8, r8, 5
    x64.leaRegRegImm32 rdi, r8, -1
    x64.shlRegImm8 rsi, rsi, 8
    x64.orRegReg rsi, rsi, rdi
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 r8, [r9 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.leaRegRegReg r9, rcx, rdx
    x64.movRegReg rcx, rsi
    x64.movRegReg rdx, rax
    x64.callDirect mrt_aes_ctr_blocks_4
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
  cxprobe4:
    x64.cmpRegImm32 rdi, 8
    x64.jcc notEqual, cxbad
  cxarm8:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 8]
    x64.cmpRegReg rdx, rdi
    x64.jcc aboveEqual, cxbad
  cxspan8:
    x64.subRegReg rdi, rdi, rdx
    x64.cmpRegImm32 rdi, 128
    x64.jcc less, cxbad
  cxcrypt8:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, cx8ddetach
  cx8dbuf:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, cx8ddone
  cx8dshared:
    x64.leaRegRegImm32 rdi, rdi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, cx8ddetach
  cx8ddone:
    x64.shrRegImm8 r8, r8, 5
    x64.leaRegRegImm32 rdi, r8, -1
    x64.shlRegImm8 rsi, rsi, 8
    x64.orRegReg rsi, rsi, rdi
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 r8, [r9 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.leaRegRegReg r9, rcx, rdx
    x64.movRegReg rcx, rsi
    x64.movRegReg rdx, rax
    x64.callDirect mrt_aes_ctr_blocks_8
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
  cx1ddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, r8
    x64.storeSlotReg slot5, r9
    x64.callDirect __managed_cow_detach
    x64.movRegReg rdi, r8
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot r8, slot4
    x64.loadRegSlot r9, slot5
    x64.jmp cx1ddone
  cx2ddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, r8
    x64.storeSlotReg slot5, r9
    x64.callDirect __managed_cow_detach
    x64.movRegReg rdi, r8
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot r8, slot4
    x64.loadRegSlot r9, slot5
    x64.jmp cx2ddone
  cx4ddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, r8
    x64.storeSlotReg slot5, r9
    x64.callDirect __managed_cow_detach
    x64.movRegReg rdi, r8
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot r8, slot4
    x64.loadRegSlot r9, slot5
    x64.jmp cx4ddone
  cx8ddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, r8
    x64.storeSlotReg slot5, r9
    x64.callDirect __managed_cow_detach
    x64.movRegReg rdi, r8
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot r8, slot4
    x64.loadRegSlot r9, slot5
    x64.jmp cx8ddone
  cxbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 80
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
    arm64.loadRegBaseDisp.word64 x6, [x2 + 8]
    arm64.cmp x6, 352
    arm64.b.lt cxbad
  scbounded:
    arm64.cmp x6, 480
    arm64.b.gt cxbad
  scstepped:
    arm64.and x7, x6, 63
    arm64.cmp x7, 32
    arm64.b.ne cxbad
  cxcounter:
    arm64.loadRegBaseDisp.word64 x7, [x3 + 8]
    arm64.cmp x7, 16
    arm64.b.ne cxbad
  cxdispatch:
    arm64.cmp x5, 1
    arm64.b.ne cxprobe1
  cxarm1:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.cmp x1, x5
    arm64.b.hs cxbad
  cxspan1:
    arm64.sub x5, x5, x1
    arm64.cmp x5, 16
    arm64.b.lt cxbad
  cxcrypt1:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 16]
    arm64.cmp x5, 0
    arm64.b.lt cx1ddetach
  cx1dbuf:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 0]
    arm64.cmp x5, 0
    arm64.b.eq cx1ddone
  cx1dshared:
    arm64.sub x5, x5, 16
    arm64.loadRegBaseDisp.word64 x5, [x5 + 8]
    arm64.cmp x5, 0
    arm64.b.ne cx1ddetach
  cx1ddone:
    arm64.lsr x5, x6, 5
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x5, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x3, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x5
    arm64.bl mrt_aes_ctr_blocks_1
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
  cxprobe1:
    arm64.cmp x5, 2
    arm64.b.ne cxprobe2
  cxarm2:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.cmp x1, x5
    arm64.b.hs cxbad
  cxspan2:
    arm64.sub x5, x5, x1
    arm64.cmp x5, 32
    arm64.b.lt cxbad
  cxcrypt2:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 16]
    arm64.cmp x5, 0
    arm64.b.lt cx2ddetach
  cx2dbuf:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 0]
    arm64.cmp x5, 0
    arm64.b.eq cx2ddone
  cx2dshared:
    arm64.sub x5, x5, 16
    arm64.loadRegBaseDisp.word64 x5, [x5 + 8]
    arm64.cmp x5, 0
    arm64.b.ne cx2ddetach
  cx2ddone:
    arm64.lsr x5, x6, 5
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x5, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x3, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x5
    arm64.bl mrt_aes_ctr_blocks_2
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
  cxprobe2:
    arm64.cmp x5, 4
    arm64.b.ne cxprobe4
  cxarm4:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.cmp x1, x5
    arm64.b.hs cxbad
  cxspan4:
    arm64.sub x5, x5, x1
    arm64.cmp x5, 64
    arm64.b.lt cxbad
  cxcrypt4:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 16]
    arm64.cmp x5, 0
    arm64.b.lt cx4ddetach
  cx4dbuf:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 0]
    arm64.cmp x5, 0
    arm64.b.eq cx4ddone
  cx4dshared:
    arm64.sub x5, x5, 16
    arm64.loadRegBaseDisp.word64 x5, [x5 + 8]
    arm64.cmp x5, 0
    arm64.b.ne cx4ddetach
  cx4ddone:
    arm64.lsr x5, x6, 5
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x5, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x3, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x5
    arm64.bl mrt_aes_ctr_blocks_4
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
  cxprobe4:
    arm64.cmp x5, 8
    arm64.b.ne cxbad
  cxarm8:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.cmp x1, x5
    arm64.b.hs cxbad
  cxspan8:
    arm64.sub x5, x5, x1
    arm64.cmp x5, 128
    arm64.b.lt cxbad
  cxcrypt8:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 16]
    arm64.cmp x5, 0
    arm64.b.lt cx8ddetach
  cx8dbuf:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 0]
    arm64.cmp x5, 0
    arm64.b.eq cx8ddone
  cx8dshared:
    arm64.sub x5, x5, 16
    arm64.loadRegBaseDisp.word64 x5, [x5 + 8]
    arm64.cmp x5, 0
    arm64.b.ne cx8ddetach
  cx8ddone:
    arm64.lsr x5, x6, 5
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x5, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x3, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x5
    arm64.bl mrt_aes_ctr_blocks_8
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
  cx1ddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.storeSlotReg slot5, x6
    arm64.bl __managed_cow_detach
    arm64.movRegReg x5, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.loadRegSlot x6, slot5
    arm64.b cx1ddone
  cx2ddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.storeSlotReg slot5, x6
    arm64.bl __managed_cow_detach
    arm64.movRegReg x5, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.loadRegSlot x6, slot5
    arm64.b cx2ddone
  cx4ddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.storeSlotReg slot5, x6
    arm64.bl __managed_cow_detach
    arm64.movRegReg x5, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.loadRegSlot x6, slot5
    arm64.b cx4ddone
  cx8ddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.storeSlotReg slot5, x6
    arm64.bl __managed_cow_detach
    arm64.movRegReg x5, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.loadRegSlot x6, slot5
    arm64.b cx8ddone
  cxbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
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
    arm64.loadRegBaseDisp.word64 x6, [x2 + 8]
    arm64.cmp x6, 352
    arm64.b.lt cxbad
  scbounded:
    arm64.cmp x6, 480
    arm64.b.gt cxbad
  scstepped:
    arm64.and x7, x6, 63
    arm64.cmp x7, 32
    arm64.b.ne cxbad
  cxcounter:
    arm64.loadRegBaseDisp.word64 x7, [x3 + 8]
    arm64.cmp x7, 16
    arm64.b.ne cxbad
  cxdispatch:
    arm64.cmp x5, 1
    arm64.b.ne cxprobe1
  cxarm1:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.cmp x1, x5
    arm64.b.hs cxbad
  cxspan1:
    arm64.sub x5, x5, x1
    arm64.cmp x5, 16
    arm64.b.lt cxbad
  cxcrypt1:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 16]
    arm64.cmp x5, 0
    arm64.b.lt cx1ddetach
  cx1dbuf:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 0]
    arm64.cmp x5, 0
    arm64.b.eq cx1ddone
  cx1dshared:
    arm64.sub x5, x5, 16
    arm64.loadRegBaseDisp.word64 x5, [x5 + 8]
    arm64.cmp x5, 0
    arm64.b.ne cx1ddetach
  cx1ddone:
    arm64.lsr x5, x6, 5
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x5, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x3, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x5
    arm64.bl mrt_aes_ctr_blocks_1
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
  cxprobe1:
    arm64.cmp x5, 2
    arm64.b.ne cxprobe2
  cxarm2:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.cmp x1, x5
    arm64.b.hs cxbad
  cxspan2:
    arm64.sub x5, x5, x1
    arm64.cmp x5, 32
    arm64.b.lt cxbad
  cxcrypt2:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 16]
    arm64.cmp x5, 0
    arm64.b.lt cx2ddetach
  cx2dbuf:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 0]
    arm64.cmp x5, 0
    arm64.b.eq cx2ddone
  cx2dshared:
    arm64.sub x5, x5, 16
    arm64.loadRegBaseDisp.word64 x5, [x5 + 8]
    arm64.cmp x5, 0
    arm64.b.ne cx2ddetach
  cx2ddone:
    arm64.lsr x5, x6, 5
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x5, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x3, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x5
    arm64.bl mrt_aes_ctr_blocks_2
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
  cxprobe2:
    arm64.cmp x5, 4
    arm64.b.ne cxprobe4
  cxarm4:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.cmp x1, x5
    arm64.b.hs cxbad
  cxspan4:
    arm64.sub x5, x5, x1
    arm64.cmp x5, 64
    arm64.b.lt cxbad
  cxcrypt4:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 16]
    arm64.cmp x5, 0
    arm64.b.lt cx4ddetach
  cx4dbuf:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 0]
    arm64.cmp x5, 0
    arm64.b.eq cx4ddone
  cx4dshared:
    arm64.sub x5, x5, 16
    arm64.loadRegBaseDisp.word64 x5, [x5 + 8]
    arm64.cmp x5, 0
    arm64.b.ne cx4ddetach
  cx4ddone:
    arm64.lsr x5, x6, 5
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x5, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x3, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x5
    arm64.bl mrt_aes_ctr_blocks_4
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
  cxprobe4:
    arm64.cmp x5, 8
    arm64.b.ne cxbad
  cxarm8:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.cmp x1, x5
    arm64.b.hs cxbad
  cxspan8:
    arm64.sub x5, x5, x1
    arm64.cmp x5, 128
    arm64.b.lt cxbad
  cxcrypt8:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 16]
    arm64.cmp x5, 0
    arm64.b.lt cx8ddetach
  cx8dbuf:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 0]
    arm64.cmp x5, 0
    arm64.b.eq cx8ddone
  cx8dshared:
    arm64.sub x5, x5, 16
    arm64.loadRegBaseDisp.word64 x5, [x5 + 8]
    arm64.cmp x5, 0
    arm64.b.ne cx8ddetach
  cx8ddone:
    arm64.lsr x5, x6, 5
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x5, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x3, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x5
    arm64.bl mrt_aes_ctr_blocks_8
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
  cx1ddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.storeSlotReg slot5, x6
    arm64.bl __managed_cow_detach
    arm64.movRegReg x5, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.loadRegSlot x6, slot5
    arm64.b cx1ddone
  cx2ddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.storeSlotReg slot5, x6
    arm64.bl __managed_cow_detach
    arm64.movRegReg x5, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.loadRegSlot x6, slot5
    arm64.b cx2ddone
  cx4ddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.storeSlotReg slot5, x6
    arm64.bl __managed_cow_detach
    arm64.movRegReg x5, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.loadRegSlot x6, slot5
    arm64.b cx4ddone
  cx8ddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.storeSlotReg slot5, x6
    arm64.bl __managed_cow_detach
    arm64.movRegReg x5, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.loadRegSlot x6, slot5
    arm64.b cx8ddone
  cxbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
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

<!-- test: aes-hardware.a-galois-mode-call-is-one-checked-entry-and-its-routine-calls -->
<!-- tier: stdlib -->
`__Builtins.aesGcmInit` becomes a call to a checked runtime entry (`__cpufeat_aes_cipher_gcm_init`) that makes one call to
the hand-assembled `mrt_aes_gcm_init` routine. `aesGcmSeal` and `aesGcmOpen` each become a call to one checked entry
(`__cpufeat_aes_cipher_gcm_seal`, `__cpufeat_aes_cipher_gcm_open`) that tests the key block's size, the nonce's length and the
spans and overlap of the two arrays once, keeps the message block in its own frame, and makes the calls to the
`mrt_aes_encrypt_block`, `mrt_aes_gcm_hash`, `mrt_aes_gcm_encrypt` or `mrt_aes_gcm_decrypt` and `mrt_aes_gcm_finish`
routines with the control word or length, the key block, the message block and the data. The entries' bodies are the Target IR
below; the routines are hex dumps of the assembler's own bytes, still unlinked.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var schedule = ByteArray.create()
	var keyBlock = ByteArray.create()
	var nonce = ByteArray.create()
	var additional = ByteArray.create()
	var plain = ByteArray.create()
	var sealed = ByteArray.create()
	key.resize(16)
	schedule.resize(352)
	keyBlock.resize(432)
	nonce.resize(12)
	additional.resize(32)
	plain.resize(64)
	sealed.resize(80)
	__Builtins.aesExpandKey(schedule, key)
	__Builtins.aesGcmInit(keyBlock, schedule)
	__Builtins.aesGcmSeal(sealed, 0, keyBlock, nonce, additional, plain)
	let accepted = __Builtins.aesGcmOpen(plain, 0, keyBlock, nonce, additional, sealed)
	print("{accepted} {sealed.count()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
true 80
```
```RequiredRuntime
__cpufeat_aes_cipher_gcm_init
__cpufeat_aes_cipher_gcm_seal
__cpufeat_aes_cipher_gcm_open
mrt_aes_gcm_init
mrt_aes_encrypt_block
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
    x64.prologue 152
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
    x64.storeSlotReg slot4, r8
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.storeSlotReg slot5, r8
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r15, r8
    x64.storeSlotReg slot6, r15
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 352
    x64.movRegReg rcx, r12
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 432
    x64.movRegReg rcx, r13
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 12
    x64.movRegReg rcx, r14
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 32
    x64.loadRegSlot rcx, slot4
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 64
    x64.loadRegSlot rcx, slot5
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 80
    x64.movRegReg rcx, r15
    x64.callDirect __managed_resize
    x64.loadRegSlot rdx, slot0
    x64.movRegReg rcx, r12
    x64.callDirect __cpufeat_aes_cipher_expand_key
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r12
    x64.callDirect __cpufeat_aes_cipher_gcm_init
    x64.movRegImm32 rdx, 0
    x64.loadRegSlot rdi, slot5
    x64.loadRegSlot rsi, slot4
    x64.movRegReg rcx, r15
    x64.movRegReg rax, r13
    x64.movRegReg r9, r14
    x64.callDirect __cpufeat_aes_cipher_gcm_seal
    x64.movRegImm32 rdx, 0
    x64.loadRegSlot rcx, slot5
    x64.loadRegSlot rsi, slot4
    x64.movRegReg rax, r13
    x64.movRegReg r9, r14
    x64.movRegReg rdi, r15
    x64.callDirect __cpufeat_aes_cipher_gcm_open
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 5
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot7, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __bool_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot13, rcx
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.loadRegBaseDisp.word64 r14, [r15 + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r15
    x64.callDirect __uint_to_string
    x64.storeSlotReg slot12, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot11, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot10, rax
    x64.leaRegRegReg rcx, rbx, r13
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot8, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot9, r8
    x64.leaRegRegImm32 r14, r8, 56
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r14, rbx
    x64.loadRegSlot rdx, slot13
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r13
    x64.loadRegSlot rax, slot12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot12
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot10
    x64.loadRegSlot rdx, slot11
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot10
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot9
    x64.storeBaseDispReg.word64 [rax + 0], r14
    x64.loadRegSlot rcx, slot8
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot7
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot9
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot9
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot6
    x64.callDirect __managed_decref
  exitdrop#2:
    x64.loadRegSlot rcx, slot5
    x64.callDirect __managed_decref
  exitdrop#3:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __managed_decref
  exitdrop#4:
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
    x64.epilogue 152
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
    x64.prologue 64
    x64.loadRegBaseDisp.word64 rax, [rdx + 8]
    x64.cmpRegImm32 rax, 352
    x64.jcc less, gibad
  scbounded:
    x64.cmpRegImm32 rax, 480
    x64.jcc greater, gibad
  scstepped:
    x64.movRegReg rsi, rax
    x64.andRegImm32 rsi, rax, 63
    x64.cmpRegImm32 rsi, 32
    x64.jcc notEqual, gibad
  gikeyblock:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.movRegReg rdi, rax
    x64.shrRegImm8 rdi, rax, 1
    x64.leaRegRegImm32 rdi, rdi, 256
    x64.cmpRegReg rsi, rdi
    x64.jcc notEqual, gibad
  gicrypt:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, giddetach
  gidbuf:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, giddone
  gidshared:
    x64.leaRegRegImm32 rsi, rsi, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, giddetach
  giddone:
    x64.shrRegImm8 rax, rax, 5
    x64.leaRegRegImm32 rax, rax, -1
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.loadRegBaseDisp.word64 r8, [rdx + 0]
    x64.movRegImm32 r9, 0
    x64.movRegReg rcx, rax
    x64.movRegReg rdx, rsi
    x64.callDirect mrt_aes_gcm_init
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  giddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.jmp giddone
  gibad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_seal {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 168
    x64.loadRegBaseDisp.word64 r8, [rax + 8]
    x64.cmpRegImm32 r8, 432
    x64.jcc less, gpbad
  kbbounded:
    x64.cmpRegImm32 r8, 496
    x64.jcc greater, gpbad
  kbstepped:
    x64.movRegReg r10, r8
    x64.andRegImm32 r10, r8, 31
    x64.cmpRegImm32 r10, 16
    x64.jcc notEqual, gpbad
  gpnonce:
    x64.loadRegBaseDisp.word64 r10, [r9 + 8]
    x64.cmpRegImm32 r10, 12
    x64.jcc notEqual, gpbad
  gpsource:
    x64.loadRegBaseDisp.word64 rbx, [rdi + 8]
  gpmeasure:
    x64.movRegImm r10, 68719476704
    x64.cmpRegReg rbx, r10
    x64.jcc above, gpbad
  gpspan:
    x64.leaRegRegImm32 r10, rbx, 16
    x64.loadRegBaseDisp.word64 r11, [rcx + 8]
    x64.cmpRegReg rdx, r11
    x64.jcc above, gpbad
  gpinside:
    x64.subRegReg r11, r11, rdx
    x64.cmpRegReg r11, r10
    x64.jcc below, gpbad
  gpdetach:
    x64.loadRegBaseDisp.word64 r11, [rcx + 16]
    x64.cmpRegImm32 r11, 0
    x64.jcc less, gpddetach
  gpdbuf:
    x64.loadRegBaseDisp.word64 r11, [rcx + 0]
    x64.cmpRegImm32 r11, 0
    x64.jcc equal, gpddone
  gpdshared:
    x64.leaRegRegImm32 r11, r11, -16
    x64.loadRegBaseDisp.word64 r11, [r11 + 8]
    x64.cmpRegImm32 r11, 0
    x64.jcc notEqual, gpddetach
  gpddone:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdi, [rdi + 0]
    x64.leaRegRegReg rcx, rcx, rdx
    x64.storeSlotReg slot7, rcx
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, gpoverlap
  gpwritten:
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, gpoverlap
  gpapart:
    x64.cmpRegReg rcx, rdi
    x64.jcc equal, gpoverlap
  gpbelow:
    x64.leaRegRegReg rdx, rcx, r10
    x64.cmpRegReg rdx, rdi
    x64.jcc belowEqual, gpoverlap
  gpabove:
    x64.leaRegRegReg rdx, rdi, rbx
    x64.cmpRegReg rdx, rcx
    x64.jcc above, gpbad
  gpoverlap:
    x64.leaRegRegImm32 rcx, r8, -256
    x64.shrRegImm8 rcx, rcx, 4
    x64.leaRegRegImm32 rcx, rcx, -1
    x64.storeSlotReg slot8, rcx
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [r9 + 0]
    x64.loadRegBaseDisp.word64 r13, [rsi + 0]
    x64.loadRegBaseDisp.word64 r14, [rsi + 8]
    x64.leaRegSlot r15, slot0
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [r15 + 0], rdx
    x64.storeBaseDispReg.word64 [r15 + 8], rdx
    x64.loadRegBaseDisp.word64 rdx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 4]
    x64.storeBaseDispReg.word64 [r15 + 16], rdx
    x64.storeBaseDispReg.word64 [r15 + 20], rax
    x64.movRegImm32 rax, 16777216
    x64.storeBaseDispReg.word64 [r15 + 28], rax
    x64.storeBaseDispReg.word64 [r15 + 48], rdi
    x64.leaRegRegImm32 rdx, r12, 256
    x64.leaRegRegImm32 r9, r15, 16
    x64.leaRegRegImm32 r8, r15, 32
    x64.callDirect mrt_aes_encrypt_block
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, ghdone
  ghrun:
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg r8, r15
    x64.movRegReg r9, r13
    x64.callDirect mrt_aes_gcm_hash
  ghdone:
    x64.movRegReg rax, rbx
    x64.shlRegImm8 rax, rbx, 8
    x64.loadRegSlot rcx, slot8
    x64.orRegReg rax, rax, rcx
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, gcdone
  gcrun:
    x64.loadRegSlot r9, slot7
    x64.movRegReg rcx, rax
    x64.movRegReg rdx, r12
    x64.movRegReg r8, r15
    x64.callDirect mrt_aes_gcm_encrypt
  gcdone:
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg r8, r15
    x64.movRegReg r9, rbx
    x64.callDirect mrt_aes_gcm_finish
    x64.loadRegBaseDisp.word64 rax, [r15 + 0]
    x64.loadRegBaseDisp.word64 rcx, [r15 + 8]
    x64.loadRegSlot rdx, slot7
    x64.leaRegRegReg rdx, rdx, rbx
    x64.storeBaseDispReg.word64 [rdx + 0], rax
    x64.storeBaseDispReg.word64 [rdx + 8], rcx
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r15 + 0], rax
    x64.storeBaseDispReg.word64 [r15 + 8], rax
    x64.storeBaseDispReg.word64 [r15 + 16], rax
    x64.storeBaseDispReg.word64 [r15 + 24], rax
    x64.storeBaseDispReg.word64 [r15 + 32], rax
    x64.storeBaseDispReg.word64 [r15 + 40], rax
    x64.storeBaseDispReg.word64 [r15 + 48], rax
    x64.epilogue 168
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  gpddetach:
    x64.storeSlotReg slot9, rax
    x64.storeSlotReg slot10, rcx
    x64.storeSlotReg slot11, rdx
    x64.storeSlotReg slot12, rsi
    x64.storeSlotReg slot13, rdi
    x64.storeSlotReg slot14, r8
    x64.storeSlotReg slot15, r9
    x64.storeSlotReg slot16, r10
    x64.callDirect __managed_cow_detach
    x64.movRegReg r11, r8
    x64.loadRegSlot rax, slot9
    x64.loadRegSlot rcx, slot10
    x64.loadRegSlot rdx, slot11
    x64.loadRegSlot rsi, slot12
    x64.loadRegSlot rdi, slot13
    x64.loadRegSlot r8, slot14
    x64.loadRegSlot r9, slot15
    x64.loadRegSlot r10, slot16
    x64.jmp gpddone
  gpbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 168
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_open {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 184
    x64.loadRegBaseDisp.word64 r8, [rax + 8]
    x64.cmpRegImm32 r8, 432
    x64.jcc less, gpbad
  kbbounded:
    x64.cmpRegImm32 r8, 496
    x64.jcc greater, gpbad
  kbstepped:
    x64.movRegReg r10, r8
    x64.andRegImm32 r10, r8, 31
    x64.cmpRegImm32 r10, 16
    x64.jcc notEqual, gpbad
  gpnonce:
    x64.loadRegBaseDisp.word64 r10, [r9 + 8]
    x64.cmpRegImm32 r10, 12
    x64.jcc notEqual, gpbad
  gpsource:
    x64.loadRegBaseDisp.word64 r10, [rdi + 8]
    x64.cmpRegImm32 r10, 16
    x64.jcc less, gpbad
  gpmeasure:
    x64.leaRegRegImm32 rbx, r10, -16
    x64.movRegImm r11, 68719476704
    x64.cmpRegReg rbx, r11
    x64.jcc above, gpbad
  gpspan:
    x64.loadRegBaseDisp.word64 r11, [rcx + 8]
    x64.cmpRegReg rdx, r11
    x64.jcc above, gpbad
  gpinside:
    x64.subRegReg r11, r11, rdx
    x64.cmpRegReg r11, rbx
    x64.jcc below, gpbad
  gpdetach:
    x64.loadRegBaseDisp.word64 r11, [rcx + 16]
    x64.cmpRegImm32 r11, 0
    x64.jcc less, gpddetach
  gpdbuf:
    x64.loadRegBaseDisp.word64 r11, [rcx + 0]
    x64.cmpRegImm32 r11, 0
    x64.jcc equal, gpddone
  gpdshared:
    x64.leaRegRegImm32 r11, r11, -16
    x64.loadRegBaseDisp.word64 r11, [r11 + 8]
    x64.cmpRegImm32 r11, 0
    x64.jcc notEqual, gpddetach
  gpddone:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdi, [rdi + 0]
    x64.storeSlotReg slot7, rdi
    x64.leaRegRegReg rcx, rcx, rdx
    x64.storeSlotReg slot8, rcx
  gpwritten:
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, gpoverlap
  gpapart:
    x64.cmpRegReg rcx, rdi
    x64.jcc equal, gpoverlap
  gpbelow:
    x64.leaRegRegReg rdx, rcx, rbx
    x64.cmpRegReg rdx, rdi
    x64.jcc belowEqual, gpoverlap
  gpabove:
    x64.leaRegRegReg rdx, rdi, r10
    x64.cmpRegReg rdx, rcx
    x64.jcc above, gpbad
  gpoverlap:
    x64.leaRegRegImm32 rcx, r8, -256
    x64.shrRegImm8 rcx, rcx, 4
    x64.leaRegRegImm32 rcx, rcx, -1
    x64.storeSlotReg slot9, rcx
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [r9 + 0]
    x64.loadRegBaseDisp.word64 r13, [rsi + 0]
    x64.loadRegBaseDisp.word64 r14, [rsi + 8]
    x64.leaRegSlot r15, slot0
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [r15 + 0], rdx
    x64.storeBaseDispReg.word64 [r15 + 8], rdx
    x64.loadRegBaseDisp.word64 rdx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 4]
    x64.storeBaseDispReg.word64 [r15 + 16], rdx
    x64.storeBaseDispReg.word64 [r15 + 20], rax
    x64.movRegImm32 rax, 16777216
    x64.storeBaseDispReg.word64 [r15 + 28], rax
    x64.storeBaseDispReg.word64 [r15 + 48], rdi
    x64.leaRegRegImm32 rdx, r12, 256
    x64.leaRegRegImm32 r9, r15, 16
    x64.leaRegRegImm32 r8, r15, 32
    x64.callDirect mrt_aes_encrypt_block
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, ghdone
  ghrun:
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg r8, r15
    x64.movRegReg r9, r13
    x64.callDirect mrt_aes_gcm_hash
  ghdone:
    x64.movRegReg rax, rbx
    x64.shlRegImm8 rax, rbx, 8
    x64.loadRegSlot rcx, slot9
    x64.orRegReg rax, rax, rcx
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, gcdone
  gcrun:
    x64.loadRegSlot r9, slot8
    x64.movRegReg rcx, rax
    x64.movRegReg rdx, r12
    x64.movRegReg r8, r15
    x64.callDirect mrt_aes_gcm_decrypt
  gcdone:
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg r8, r15
    x64.movRegReg r9, rbx
    x64.callDirect mrt_aes_gcm_finish
    x64.loadRegBaseDisp.word64 rax, [r15 + 0]
    x64.loadRegBaseDisp.word64 rcx, [r15 + 8]
    x64.loadRegSlot rdx, slot7
    x64.leaRegRegReg rdx, rdx, rbx
    x64.loadRegBaseDisp.word64 rsi, [rdx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.xorRegReg rax, rax, rsi
    x64.xorRegReg rcx, rcx, rdx
    x64.orRegReg rax, rax, rcx
    x64.movRegImm32 rcx, 0
    x64.storeBaseDispReg.word64 [r15 + 0], rcx
    x64.storeBaseDispReg.word64 [r15 + 8], rcx
    x64.storeBaseDispReg.word64 [r15 + 16], rcx
    x64.storeBaseDispReg.word64 [r15 + 24], rcx
    x64.storeBaseDispReg.word64 [r15 + 32], rcx
    x64.storeBaseDispReg.word64 [r15 + 40], rcx
    x64.storeBaseDispReg.word64 [r15 + 48], rcx
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, gprejected
  gpaccepted:
    x64.movRegImm32 r8, 1
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  gprejected:
    x64.movRegImm32 r8, 0
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  gpddetach:
    x64.storeSlotReg slot10, rax
    x64.storeSlotReg slot11, rcx
    x64.storeSlotReg slot12, rdx
    x64.storeSlotReg slot13, rsi
    x64.storeSlotReg slot14, rdi
    x64.storeSlotReg slot15, r8
    x64.storeSlotReg slot16, r9
    x64.storeSlotReg slot17, r10
    x64.callDirect __managed_cow_detach
    x64.movRegReg r11, r8
    x64.loadRegSlot rax, slot10
    x64.loadRegSlot rcx, slot11
    x64.loadRegSlot rdx, slot12
    x64.loadRegSlot rsi, slot13
    x64.loadRegSlot rdi, slot14
    x64.loadRegSlot r8, slot15
    x64.loadRegSlot r9, slot16
    x64.loadRegSlot r10, slot17
    x64.jmp gpddone
  gpbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 184
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

chunk @mrt_aes_gcm_init {
    0000: 55 48 89 e5 48 83 ec 20 4c 8d 8a 00 01 00 00 4c
    0010: 8d 51 01 f3 41 0f 6f 00 f3 41 0f 7f 01 49 81 c0
    0020: 10 00 00 00 49 81 c1 10 00 00 00 49 81 ea 01 00
    0030: 00 00 0f 85 db ff ff ff 48 8d 05 d9 01 00 00 f3
    0040: 0f 6f 82 00 01 00 00 f3 0f 6f a2 10 01 00 00 66
    0050: 0f 38 dc c4 f3 0f 6f a2 20 01 00 00 66 0f 38 dc
    0060: c4 f3 0f 6f a2 30 01 00 00 66 0f 38 dc c4 f3 0f
    0070: 6f a2 40 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 50
    0080: 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 60 01 00 00
    0090: 66 0f 38 dc c4 f3 0f 6f a2 70 01 00 00 66 0f 38
    00a0: dc c4 f3 0f 6f a2 80 01 00 00 66 0f 38 dc c4 f3
    00b0: 0f 6f a2 90 01 00 00 66 0f 38 dc c4 f3 0f 6f a2
    00c0: a0 01 00 00 48 81 f9 0c 00 00 00 0f 82 3a 00 00
    00d0: 00 66 0f 38 dc c4 f3 0f 6f a2 b0 01 00 00 66 0f
    00e0: 38 dc c4 f3 0f 6f a2 c0 01 00 00 0f 84 1a 00 00
    00f0: 00 66 0f 38 dc c4 f3 0f 6f a2 d0 01 00 00 66 0f
    0100: 38 dc c4 f3 0f 6f a2 e0 01 00 00 66 0f 38 dd c4
    0110: f3 0f 6f 28 66 0f 38 00 c5 f3 0f 6f 48 10 66 0f
    0120: 70 e0 ff 0f 28 e8 66 0f 72 e4 1f 66 0f db e1 66
    0130: 0f 72 d5 1f 66 0f 73 fd 04 66 0f 72 f0 01 66 0f
    0140: ef c4 66 0f ef c5 f3 0f 7f 82 e0 00 00 00 66 0f
    0150: 70 e0 4e 66 0f ef e0 f3 0f 7f a2 f0 00 00 00 4c
    0160: 8d 92 c0 00 00 00 45 31 db 49 81 c3 07 00 00 00
    0170: f3 0f 6f 8a e0 00 00 00 0f 28 d1 f3 0f 6f 9a f0
    0180: 00 00 00 66 0f 3a 44 c8 00 66 0f 3a 44 d0 11 66
    0190: 0f 70 e0 4e 66 0f ef e0 66 0f 3a 44 dc 00 66 0f
    01a0: ef d9 66 0f ef da 0f 28 e3 66 0f 73 db 08 66 0f
    01b0: 73 fc 08 66 0f ef d3 66 0f ef cc f3 0f 6f 60 10
    01c0: 66 0f 3a 44 e1 01 66 0f 70 c9 4e 66 0f ef cc f3
    01d0: 0f 6f 60 10 66 0f 3a 44 e1 01 66 0f 70 c9 4e 66
    01e0: 0f ef cc 66 0f ef ca 0f 28 c1 f3 41 0f 7f 02 66
    01f0: 0f 70 e0 4e 66 0f ef e0 f3 41 0f 7f 62 10 49 81
    0200: eb 01 00 00 00 4d 8d 52 e0 0f 85 61 ff ff ff 31
    0210: c0 48 83 c4 20 5d c3 00 0f 0e 0d 0c 0b 0a 09 08
    0220: 07 06 05 04 03 02 01 00 01 00 00 00 00 00 00 00
    0230: 00 00 00 00 00 00 00 c2 ff ff ff ff ff ff ff ff
    0240: ff ff ff ff ff ff ff ff 00 00 00 00 00 00 00 00
    0250: 00 00 00 00 00 00 00 00 00
}

chunk @mrt_aes_gcm_hash {
    0000: 55 48 89 e5 48 81 ec d0 00 00 00 48 8d 05 90 05
    0010: 00 00 f3 41 0f 6f 08 48 85 c9 0f 84 72 05 00 00
    0020: 48 81 f9 80 00 00 00 0f 82 7a 03 00 00 f3 0f 7f
    0030: b5 60 ff ff ff f3 0f 7f bd 70 ff ff ff f3 44 0f
    0040: 7f 45 80 f3 44 0f 7f 4d 90 f3 44 0f 7f 55 a0 f3
    0050: 44 0f 7f 5d b0 f3 44 0f 7f 65 c0 f3 44 0f 7f 6d
    0060: d0 f3 44 0f 7f 75 e0 f3 44 0f 7f 7d f0 f3 44 0f
    0070: 6f 38 f3 44 0f 6f 70 10 44 0f 28 c1 44 0f 28 dc
    0080: 66 45 0f ef c9 66 45 0f ef d2 48 81 f9 80 00 00
    0090: 00 0f 82 cc 02 00 00 48 81 e9 80 00 00 00 f3 41
    00a0: 0f 6f 01 f3 41 0f 6f 49 10 f3 41 0f 6f 51 20 f3
    00b0: 41 0f 6f 59 30 f3 41 0f 6f 61 40 f3 41 0f 6f 69
    00c0: 50 f3 41 0f 6f 71 60 f3 41 0f 6f 79 70 49 81 c1
    00d0: 80 00 00 00 66 41 0f 38 00 c7 66 41 0f 38 00 cf
    00e0: 66 41 0f 38 00 d7 66 41 0f 38 00 df 66 41 0f 38
    00f0: 00 e7 66 41 0f 38 00 ef 66 41 0f 38 00 f7 66 41
    0100: 0f 38 00 ff 66 41 0f ef c0 f3 44 0f 6f 02 f3 44
    0110: 0f 6f 52 10 45 0f 28 c8 66 44 0f 70 e0 4e 66 44
    0120: 0f ef e0 66 45 0f 3a 44 d4 00 66 44 0f 3a 44 c0
    0130: 00 66 44 0f 3a 44 c8 11 f3 44 0f 6f 62 20 45 0f
    0140: 28 ec 66 44 0f 3a 44 e1 00 66 45 0f ef c4 66 44
    0150: 0f 3a 44 e9 11 66 45 0f ef cd 66 44 0f 70 e1 4e
    0160: 66 41 0f ef cc f3 44 0f 6f 62 30 66 44 0f 3a 44
    0170: e1 00 66 45 0f ef d4 f3 44 0f 6f 62 40 45 0f 28
    0180: ec 66 44 0f 3a 44 e2 00 66 45 0f ef c4 66 44 0f
    0190: 3a 44 ea 11 66 45 0f ef cd 66 44 0f 70 e2 4e 66
    01a0: 41 0f ef d4 f3 44 0f 6f 62 50 66 44 0f 3a 44 e2
    01b0: 00 66 45 0f ef d4 f3 44 0f 6f 62 60 45 0f 28 ec
    01c0: 66 44 0f 3a 44 e3 00 66 45 0f ef c4 66 44 0f 3a
    01d0: 44 eb 11 66 45 0f ef cd 66 44 0f 70 e3 4e 66 41
    01e0: 0f ef dc f3 44 0f 6f 62 70 66 44 0f 3a 44 e3 00
    01f0: 66 45 0f ef d4 f3 44 0f 6f a2 80 00 00 00 45 0f
    0200: 28 ec 66 44 0f 3a 44 e4 00 66 45 0f ef c4 66 44
    0210: 0f 3a 44 ec 11 66 45 0f ef cd 66 44 0f 70 e4 4e
    0220: 66 41 0f ef e4 f3 44 0f 6f a2 90 00 00 00 66 44
    0230: 0f 3a 44 e4 00 66 45 0f ef d4 f3 44 0f 6f a2 a0
    0240: 00 00 00 45 0f 28 ec 66 44 0f 3a 44 e5 00 66 45
    0250: 0f ef c4 66 44 0f 3a 44 ed 11 66 45 0f ef cd 66
    0260: 44 0f 70 e5 4e 66 41 0f ef ec f3 44 0f 6f a2 b0
    0270: 00 00 00 66 44 0f 3a 44 e5 00 66 45 0f ef d4 f3
    0280: 44 0f 6f a2 c0 00 00 00 45 0f 28 ec 66 44 0f 3a
    0290: 44 e6 00 66 45 0f ef c4 66 44 0f 3a 44 ee 11 66
    02a0: 45 0f ef cd 66 44 0f 70 e6 4e 66 41 0f ef f4 f3
    02b0: 44 0f 6f a2 d0 00 00 00 66 44 0f 3a 44 e6 00 66
    02c0: 45 0f ef d4 f3 44 0f 6f a2 e0 00 00 00 45 0f 28
    02d0: ec 66 44 0f 3a 44 e7 00 66 45 0f ef c4 66 44 0f
    02e0: 3a 44 ef 11 66 45 0f ef cd 66 44 0f 70 e7 4e 66
    02f0: 41 0f ef fc f3 44 0f 6f a2 f0 00 00 00 66 44 0f
    0300: 3a 44 e7 00 66 45 0f ef d4 66 45 0f ef d0 66 45
    0310: 0f ef d1 45 0f 28 da 66 41 0f 73 da 08 66 41 0f
    0320: 73 fb 08 66 45 0f ef ca 66 45 0f ef c3 45 0f 28
    0330: de 66 45 0f 3a 44 d8 01 66 45 0f 70 c0 4e 66 45
    0340: 0f ef c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45
    0350: 0f 70 c0 4e 66 45 0f ef c3 66 45 0f ef c1 e9 27
    0360: fd ff ff 41 0f 28 c8 f3 0f 6f b5 60 ff ff ff f3
    0370: 0f 6f bd 70 ff ff ff f3 44 0f 6f 45 80 f3 44 0f
    0380: 6f 4d 90 f3 44 0f 6f 55 a0 f3 44 0f 6f 5d b0 f3
    0390: 44 0f 6f 65 c0 f3 44 0f 6f 6d d0 f3 44 0f 6f 75
    03a0: e0 f3 44 0f 6f 7d f0 48 81 f9 10 00 00 00 0f 82
    03b0: 9c 00 00 00 48 81 e9 10 00 00 00 f3 41 0f 6f 01
    03c0: f3 0f 6f 28 66 0f 38 00 c5 66 0f ef c1 f3 0f 6f
    03d0: 8a e0 00 00 00 0f 28 d1 f3 0f 6f 9a f0 00 00 00
    03e0: 66 0f 3a 44 c8 00 66 0f 3a 44 d0 11 66 0f 70 e0
    03f0: 4e 66 0f ef e0 66 0f 3a 44 dc 00 66 0f ef d9 66
    0400: 0f ef da 0f 28 e3 66 0f 73 db 08 66 0f 73 fc 08
    0410: 66 0f ef d3 66 0f ef cc f3 0f 6f 60 10 66 0f 3a
    0420: 44 e1 01 66 0f 70 c9 4e 66 0f ef cc f3 0f 6f 60
    0430: 10 66 0f 3a 44 e1 01 66 0f 70 c9 4e 66 0f ef cc
    0440: 66 0f ef ca 49 81 c1 10 00 00 00 e9 57 ff ff ff
    0450: 48 85 c9 0f 84 39 01 00 00 48 8d b5 50 ff ff ff
    0460: 49 89 ca 49 81 e2 08 00 00 00 0f 84 14 00 00 00
    0470: 4d 8b 11 4c 89 16 49 81 c1 08 00 00 00 48 81 c6
    0480: 08 00 00 00 49 89 ca 49 81 e2 04 00 00 00 0f 84
    0490: 14 00 00 00 45 8b 11 44 89 16 49 81 c1 04 00 00
    04a0: 00 48 81 c6 04 00 00 00 49 89 ca 49 81 e2 02 00
    04b0: 00 00 0f 84 16 00 00 00 4d 0f b7 11 66 44 89 16
    04c0: 49 81 c1 02 00 00 00 48 81 c6 02 00 00 00 49 89
    04d0: ca 49 81 e2 01 00 00 00 0f 84 15 00 00 00 4d 0f
    04e0: b6 11 44 88 16 49 81 c1 01 00 00 00 48 81 c6 01
    04f0: 00 00 00 f3 0f 6f 85 50 ff ff ff 48 8d 35 a0 00
    0500: 00 00 48 29 ce f3 0f 6f 6e 30 66 0f db c5 f3 0f
    0510: 6f 28 66 0f 38 00 c5 66 0f ef c1 f3 0f 6f 8a e0
    0520: 00 00 00 0f 28 d1 f3 0f 6f 9a f0 00 00 00 66 0f
    0530: 3a 44 c8 00 66 0f 3a 44 d0 11 66 0f 70 e0 4e 66
    0540: 0f ef e0 66 0f 3a 44 dc 00 66 0f ef d9 66 0f ef
    0550: da 0f 28 e3 66 0f 73 db 08 66 0f 73 fc 08 66 0f
    0560: ef d3 66 0f ef cc f3 0f 6f 60 10 66 0f 3a 44 e1
    0570: 01 66 0f 70 c9 4e 66 0f ef cc f3 0f 6f 60 10 66
    0580: 0f 3a 44 e1 01 66 0f 70 c9 4e 66 0f ef cc 66 0f
    0590: ef ca f3 41 0f 7f 08 31 c0 48 81 c4 d0 00 00 00
    05a0: 5d c3 0f 0e 0d 0c 0b 0a 09 08 07 06 05 04 03 02
    05b0: 01 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00
    05c0: 00 c2 ff ff ff ff ff ff ff ff ff ff ff ff ff ff
    05d0: ff ff 00 00 00 00 00 00 00 00 00 00 00 00 00 00
    05e0: 00 00 00
}

chunk @mrt_aes_gcm_encrypt {
    0000: 55 48 89 e5 48 81 ec e0 01 00 00 49 89 ca 49 c1
    0010: ea 08 49 89 cb 49 81 e3 ff 00 00 00 48 8d 0d 6b
    0020: 16 00 00 f3 41 0f 6f 08 f3 41 0f 6f 40 10 41 8b
    0030: 40 1c f3 0f 6f a2 00 01 00 00 8b ba 0c 01 00 00
    0040: 0f c8 0f cf 49 8d 72 0f 48 c1 ee 04 48 01 c6 0f
    0050: ce 41 89 70 1c 66 0f ef e0 f3 0f 7f a5 c8 fe ff
    0060: ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    0070: d4 fe ff ff 4c 89 85 58 ff ff ff 49 8b 70 30 4d
    0080: 89 c8 49 29 f0 49 89 f1 49 81 fa 80 00 00 00 0f
    0090: 82 f5 10 00 00 f3 0f 7f b5 60 ff ff ff f3 0f 7f
    00a0: bd 70 ff ff ff f3 44 0f 7f 45 80 f3 44 0f 7f 4d
    00b0: 90 f3 44 0f 7f 55 a0 f3 44 0f 7f 5d b0 f3 44 0f
    00c0: 7f 65 c0 f3 44 0f 7f 6d d0 f3 44 0f 7f 75 e0 f3
    00d0: 44 0f 7f 7d f0 f3 44 0f 6f 39 f3 44 0f 6f 71 10
    00e0: 44 0f 28 c1 44 0f 28 dc 66 45 0f ef c9 66 45 0f
    00f0: ef d2 49 81 ea 80 00 00 00 f3 44 0f 7f 9d d8 fe
    0100: ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0110: b5 e4 fe ff ff f3 44 0f 7f 9d e8 fe ff ff 48 81
    0120: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 f4 fe ff
    0130: ff f3 44 0f 7f 9d f8 fe ff ff 48 81 c0 01 00 00
    0140: 00 89 c6 31 fe 0f ce 89 b5 04 ff ff ff f3 44 0f
    0150: 7f 9d 08 ff ff ff 48 81 c0 01 00 00 00 89 c6 31
    0160: fe 0f ce 89 b5 14 ff ff ff f3 44 0f 7f 9d 18 ff
    0170: ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0180: b5 24 ff ff ff f3 44 0f 7f 9d 28 ff ff ff 48 81
    0190: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 34 ff ff
    01a0: ff f3 44 0f 7f 9d 38 ff ff ff 48 81 c0 01 00 00
    01b0: 00 89 c6 31 fe 0f ce 89 b5 44 ff ff ff f3 0f 6f
    01c0: 85 c8 fe ff ff f3 0f 6f 8d d8 fe ff ff f3 0f 6f
    01d0: 95 e8 fe ff ff f3 0f 6f 9d f8 fe ff ff f3 0f 6f
    01e0: a5 08 ff ff ff f3 0f 6f ad 18 ff ff ff f3 0f 6f
    01f0: b5 28 ff ff ff f3 0f 6f bd 38 ff ff ff f3 44 0f
    0200: 6f 9a 10 01 00 00 66 41 0f 38 dc c3 66 41 0f 38
    0210: dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41
    0220: 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3
    0230: 66 41 0f 38 dc fb 48 81 c0 01 00 00 00 89 c6 31
    0240: fe 0f ce 89 b5 d4 fe ff ff f3 44 0f 6f 9a 20 01
    0250: 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41
    0260: 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3
    0270: 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38
    0280: dc fb 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0290: b5 e4 fe ff ff f3 44 0f 6f 9a 30 01 00 00 66 41
    02a0: 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3
    02b0: 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38
    02c0: dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 48 81
    02d0: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 f4 fe ff
    02e0: ff f3 44 0f 6f 9a 40 01 00 00 66 41 0f 38 dc c3
    02f0: 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38
    0300: dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41
    0310: 0f 38 dc f3 66 41 0f 38 dc fb 48 81 c0 01 00 00
    0320: 00 89 c6 31 fe 0f ce 89 b5 04 ff ff ff f3 44 0f
    0330: 6f 9a 50 01 00 00 66 41 0f 38 dc c3 66 41 0f 38
    0340: dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41
    0350: 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3
    0360: 66 41 0f 38 dc fb 48 81 c0 01 00 00 00 89 c6 31
    0370: fe 0f ce 89 b5 14 ff ff ff f3 44 0f 6f 9a 60 01
    0380: 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41
    0390: 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3
    03a0: 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38
    03b0: dc fb 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    03c0: b5 24 ff ff ff f3 44 0f 6f 9a 70 01 00 00 66 41
    03d0: 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3
    03e0: 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38
    03f0: dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 48 81
    0400: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 34 ff ff
    0410: ff f3 44 0f 6f 9a 80 01 00 00 66 41 0f 38 dc c3
    0420: 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38
    0430: dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41
    0440: 0f 38 dc f3 66 41 0f 38 dc fb 48 81 c0 01 00 00
    0450: 00 89 c6 31 fe 0f ce 89 b5 44 ff ff ff f3 44 0f
    0460: 6f 9a 90 01 00 00 66 41 0f 38 dc c3 66 41 0f 38
    0470: dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41
    0480: 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3
    0490: 66 41 0f 38 dc fb f3 44 0f 6f 9a a0 01 00 00 49
    04a0: 81 fb 0c 00 00 00 0f 82 ea 00 00 00 66 41 0f 38
    04b0: dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41
    04c0: 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb
    04d0: 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f
    04e0: 9a b0 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc
    04f0: cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f
    0500: 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66
    0510: 41 0f 38 dc fb f3 44 0f 6f 9a c0 01 00 00 0f 84
    0520: 72 00 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb
    0530: 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38
    0540: dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41
    0550: 0f 38 dc fb f3 44 0f 6f 9a d0 01 00 00 66 41 0f
    0560: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    0570: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    0580: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f
    0590: 6f 9a e0 01 00 00 66 41 0f 38 dd c3 66 41 0f 38
    05a0: dd cb 66 41 0f 38 dd d3 66 41 0f 38 dd db 66 41
    05b0: 0f 38 dd e3 66 41 0f 38 dd eb 66 41 0f 38 dd f3
    05c0: 66 41 0f 38 dd fb f3 45 0f 6f 19 66 41 0f ef c3
    05d0: f3 45 0f 6f 59 10 66 41 0f ef cb f3 45 0f 6f 59
    05e0: 20 66 41 0f ef d3 f3 45 0f 6f 59 30 66 41 0f ef
    05f0: db f3 45 0f 6f 59 40 66 41 0f ef e3 f3 45 0f 6f
    0600: 59 50 66 41 0f ef eb f3 45 0f 6f 59 60 66 41 0f
    0610: ef f3 f3 45 0f 6f 59 70 66 41 0f ef fb f3 43 0f
    0620: 7f 04 01 66 41 0f 38 00 c7 66 41 0f ef c0 f3 43
    0630: 0f 7f 4c 01 10 66 41 0f 38 00 cf f3 43 0f 7f 54
    0640: 01 20 66 41 0f 38 00 d7 f3 43 0f 7f 5c 01 30 66
    0650: 41 0f 38 00 df f3 43 0f 7f 64 01 40 66 41 0f 38
    0660: 00 e7 f3 43 0f 7f 6c 01 50 66 41 0f 38 00 ef f3
    0670: 43 0f 7f 74 01 60 66 41 0f 38 00 f7 f3 43 0f 7f
    0680: 7c 01 70 66 41 0f 38 00 ff f3 0f 7f 85 48 fe ff
    0690: ff f3 0f 7f 8d 58 fe ff ff f3 0f 7f 95 68 fe ff
    06a0: ff f3 0f 7f 9d 78 fe ff ff f3 0f 7f a5 88 fe ff
    06b0: ff f3 0f 7f ad 98 fe ff ff f3 0f 7f b5 a8 fe ff
    06c0: ff f3 0f 7f bd b8 fe ff ff 49 81 c1 80 00 00 00
    06d0: 49 81 fa 80 00 00 00 0f 82 bc 07 00 00 49 81 ea
    06e0: 80 00 00 00 f3 0f 6f 85 c8 fe ff ff f3 0f 6f 8d
    06f0: d8 fe ff ff f3 0f 6f 95 e8 fe ff ff f3 0f 6f 9d
    0700: f8 fe ff ff f3 0f 6f a5 08 ff ff ff f3 0f 6f ad
    0710: 18 ff ff ff f3 0f 6f b5 28 ff ff ff f3 0f 6f bd
    0720: 38 ff ff ff f3 44 0f 6f 9d 48 fe ff ff f3 44 0f
    0730: 6f 02 f3 44 0f 6f 52 10 45 0f 28 c8 66 45 0f 70
    0740: e3 4e 66 45 0f ef e3 66 45 0f 3a 44 d4 00 66 45
    0750: 0f 3a 44 c3 00 66 45 0f 3a 44 cb 11 f3 44 0f 6f
    0760: 9a 10 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc
    0770: cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f
    0780: 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66
    0790: 41 0f 38 dc fb f3 44 0f 6f 9d 58 fe ff ff f3 44
    07a0: 0f 6f 62 20 45 0f 28 ec 66 45 0f 3a 44 e3 00 66
    07b0: 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd
    07c0: 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f 62
    07d0: 30 66 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81 c0
    07e0: 01 00 00 00 89 c6 31 fe 0f ce 89 b5 d4 fe ff ff
    07f0: f3 44 0f 6f 9a 20 01 00 00 66 41 0f 38 dc c3 66
    0800: 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc
    0810: db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f
    0820: 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9d 68 fe
    0830: ff ff f3 44 0f 6f 62 40 45 0f 28 ec 66 45 0f 3a
    0840: 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66
    0850: 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3
    0860: 44 0f 6f 62 50 66 45 0f 3a 44 e3 00 66 45 0f ef
    0870: d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    0880: e4 fe ff ff f3 44 0f 6f 9a 30 01 00 00 66 41 0f
    0890: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    08a0: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    08b0: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f
    08c0: 6f 9d 78 fe ff ff f3 44 0f 6f 62 60 45 0f 28 ec
    08d0: 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a
    08e0: 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45
    08f0: 0f ef dc f3 44 0f 6f 62 70 66 45 0f 3a 44 e3 00
    0900: 66 45 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe
    0910: 0f ce 89 b5 f4 fe ff ff f3 44 0f 6f 9a 40 01 00
    0920: 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f
    0930: 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66
    0940: 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc
    0950: fb f3 44 0f 6f 9d 88 fe ff ff f3 44 0f 6f a2 80
    0960: 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45
    0970: 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66
    0980: 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 90
    0990: 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 48
    09a0: 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 04 ff
    09b0: ff ff f3 44 0f 6f 9a 50 01 00 00 66 41 0f 38 dc
    09c0: c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f
    09d0: 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66
    09e0: 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9d
    09f0: 98 fe ff ff f3 44 0f 6f a2 a0 00 00 00 45 0f 28
    0a00: ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f
    0a10: 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66
    0a20: 45 0f ef dc f3 44 0f 6f a2 b0 00 00 00 66 45 0f
    0a30: 3a 44 e3 00 66 45 0f ef d4 48 81 c0 01 00 00 00
    0a40: 89 c6 31 fe 0f ce 89 b5 14 ff ff ff f3 44 0f 6f
    0a50: 9a 60 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc
    0a60: cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f
    0a70: 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66
    0a80: 41 0f 38 dc fb f3 44 0f 6f 9d a8 fe ff ff f3 44
    0a90: 0f 6f a2 c0 00 00 00 45 0f 28 ec 66 45 0f 3a 44
    0aa0: e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45
    0ab0: 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44
    0ac0: 0f 6f a2 d0 00 00 00 66 45 0f 3a 44 e3 00 66 45
    0ad0: 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce
    0ae0: 89 b5 24 ff ff ff f3 44 0f 6f 9a 70 01 00 00 66
    0af0: 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc
    0b00: d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f
    0b10: 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3
    0b20: 44 0f 6f 9d b8 fe ff ff f3 44 0f 6f a2 e0 00 00
    0b30: 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef
    0b40: c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f
    0b50: 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 f0 00 00
    0b60: 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81 c0
    0b70: 01 00 00 00 89 c6 31 fe 0f ce 89 b5 34 ff ff ff
    0b80: f3 44 0f 6f 9a 80 01 00 00 66 41 0f 38 dc c3 66
    0b90: 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc
    0ba0: db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f
    0bb0: 38 dc f3 66 41 0f 38 dc fb 48 81 c0 01 00 00 00
    0bc0: 89 c6 31 fe 0f ce 89 b5 44 ff ff ff 66 45 0f ef
    0bd0: d0 66 45 0f ef d1 45 0f 28 da 66 41 0f 73 da 08
    0be0: 66 41 0f 73 fb 08 66 45 0f ef ca 66 45 0f ef c3
    0bf0: 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45 0f 70 c0
    0c00: 4e 66 45 0f ef c3 f3 44 0f 6f 9a 90 01 00 00 66
    0c10: 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc
    0c20: d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f
    0c30: 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 45
    0c40: 0f 28 de 66 45 0f 3a 44 d8 01 66 45 0f 70 c0 4e
    0c50: 66 45 0f ef c3 66 45 0f ef c1 f3 44 0f 6f 9a a0
    0c60: 01 00 00 49 81 fb 0c 00 00 00 0f 82 ea 00 00 00
    0c70: 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38
    0c80: dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41
    0c90: 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb
    0ca0: f3 44 0f 6f 9a b0 01 00 00 66 41 0f 38 dc c3 66
    0cb0: 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc
    0cc0: db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f
    0cd0: 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9a c0 01
    0ce0: 00 00 0f 84 72 00 00 00 66 41 0f 38 dc c3 66 41
    0cf0: 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db
    0d00: 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38
    0d10: dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9a d0 01 00
    0d20: 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f
    0d30: 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66
    0d40: 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc
    0d50: fb f3 44 0f 6f 9a e0 01 00 00 66 41 0f 38 dd c3
    0d60: 66 41 0f 38 dd cb 66 41 0f 38 dd d3 66 41 0f 38
    0d70: dd db 66 41 0f 38 dd e3 66 41 0f 38 dd eb 66 41
    0d80: 0f 38 dd f3 66 41 0f 38 dd fb f3 45 0f 6f 19 66
    0d90: 41 0f ef c3 f3 45 0f 6f 59 10 66 41 0f ef cb f3
    0da0: 45 0f 6f 59 20 66 41 0f ef d3 f3 45 0f 6f 59 30
    0db0: 66 41 0f ef db f3 45 0f 6f 59 40 66 41 0f ef e3
    0dc0: f3 45 0f 6f 59 50 66 41 0f ef eb f3 45 0f 6f 59
    0dd0: 60 66 41 0f ef f3 f3 45 0f 6f 59 70 66 41 0f ef
    0de0: fb f3 43 0f 7f 04 01 66 41 0f 38 00 c7 66 41 0f
    0df0: ef c0 f3 43 0f 7f 4c 01 10 66 41 0f 38 00 cf f3
    0e00: 43 0f 7f 54 01 20 66 41 0f 38 00 d7 f3 43 0f 7f
    0e10: 5c 01 30 66 41 0f 38 00 df f3 43 0f 7f 64 01 40
    0e20: 66 41 0f 38 00 e7 f3 43 0f 7f 6c 01 50 66 41 0f
    0e30: 38 00 ef f3 43 0f 7f 74 01 60 66 41 0f 38 00 f7
    0e40: f3 43 0f 7f 7c 01 70 66 41 0f 38 00 ff f3 0f 7f
    0e50: 85 48 fe ff ff f3 0f 7f 8d 58 fe ff ff f3 0f 7f
    0e60: 95 68 fe ff ff f3 0f 7f 9d 78 fe ff ff f3 0f 7f
    0e70: a5 88 fe ff ff f3 0f 7f ad 98 fe ff ff f3 0f 7f
    0e80: b5 a8 fe ff ff f3 0f 7f bd b8 fe ff ff 49 81 c1
    0e90: 80 00 00 00 e9 37 f8 ff ff f3 44 0f 6f 9d 48 fe
    0ea0: ff ff f3 44 0f 6f 02 f3 44 0f 6f 52 10 45 0f 28
    0eb0: c8 66 45 0f 70 e3 4e 66 45 0f ef e3 66 45 0f 3a
    0ec0: 44 d4 00 66 45 0f 3a 44 c3 00 66 45 0f 3a 44 cb
    0ed0: 11 f3 44 0f 6f 9d 58 fe ff ff f3 44 0f 6f 62 20
    0ee0: 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4
    0ef0: 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70
    0f00: e3 4e 66 45 0f ef dc f3 44 0f 6f 62 30 66 45 0f
    0f10: 3a 44 e3 00 66 45 0f ef d4 f3 44 0f 6f 9d 68 fe
    0f20: ff ff f3 44 0f 6f 62 40 45 0f 28 ec 66 45 0f 3a
    0f30: 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66
    0f40: 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3
    0f50: 44 0f 6f 62 50 66 45 0f 3a 44 e3 00 66 45 0f ef
    0f60: d4 f3 44 0f 6f 9d 78 fe ff ff f3 44 0f 6f 62 60
    0f70: 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4
    0f80: 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70
    0f90: e3 4e 66 45 0f ef dc f3 44 0f 6f 62 70 66 45 0f
    0fa0: 3a 44 e3 00 66 45 0f ef d4 f3 44 0f 6f 9d 88 fe
    0fb0: ff ff f3 44 0f 6f a2 80 00 00 00 45 0f 28 ec 66
    0fc0: 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44
    0fd0: eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f
    0fe0: ef dc f3 44 0f 6f a2 90 00 00 00 66 45 0f 3a 44
    0ff0: e3 00 66 45 0f ef d4 f3 44 0f 6f 9d 98 fe ff ff
    1000: f3 44 0f 6f a2 a0 00 00 00 45 0f 28 ec 66 45 0f
    1010: 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11
    1020: 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc
    1030: f3 44 0f 6f a2 b0 00 00 00 66 45 0f 3a 44 e3 00
    1040: 66 45 0f ef d4 f3 44 0f 6f 9d a8 fe ff ff f3 44
    1050: 0f 6f a2 c0 00 00 00 45 0f 28 ec 66 45 0f 3a 44
    1060: e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45
    1070: 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44
    1080: 0f 6f a2 d0 00 00 00 66 45 0f 3a 44 e3 00 66 45
    1090: 0f ef d4 f3 44 0f 6f 9d b8 fe ff ff f3 44 0f 6f
    10a0: a2 e0 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00
    10b0: 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef
    10c0: cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f
    10d0: a2 f0 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef
    10e0: d4 66 45 0f ef d0 66 45 0f ef d1 45 0f 28 da 66
    10f0: 41 0f 73 da 08 66 41 0f 73 fb 08 66 45 0f ef ca
    1100: 66 45 0f ef c3 45 0f 28 de 66 45 0f 3a 44 d8 01
    1110: 66 45 0f 70 c0 4e 66 45 0f ef c3 45 0f 28 de 66
    1120: 45 0f 3a 44 d8 01 66 45 0f 70 c0 4e 66 45 0f ef
    1130: c3 66 45 0f ef c1 41 0f 28 c8 f3 0f 6f b5 60 ff
    1140: ff ff f3 0f 6f bd 70 ff ff ff f3 44 0f 6f 45 80
    1150: f3 44 0f 6f 4d 90 f3 44 0f 6f 55 a0 f3 44 0f 6f
    1160: 5d b0 f3 44 0f 6f 65 c0 f3 44 0f 6f 6d d0 f3 44
    1170: 0f 6f 75 e0 f3 44 0f 6f 7d f0 4d 85 d2 0f 84 f4
    1180: 04 00 00 48 81 e8 07 00 00 00 49 81 fa 10 00 00
    1190: 00 0f 82 0a 02 00 00 31 c9 49 81 ea 10 00 00 00
    11a0: f3 0f 6f 85 c8 fe ff ff 48 81 c0 01 00 00 00 89
    11b0: c6 31 fe 0f ce 89 b5 d4 fe ff ff f3 0f 6f a2 10
    11c0: 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 20 01 00 00
    11d0: 66 0f 38 dc c4 f3 0f 6f a2 30 01 00 00 66 0f 38
    11e0: dc c4 f3 0f 6f a2 40 01 00 00 66 0f 38 dc c4 f3
    11f0: 0f 6f a2 50 01 00 00 66 0f 38 dc c4 f3 0f 6f a2
    1200: 60 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 70 01 00
    1210: 00 66 0f 38 dc c4 f3 0f 6f a2 80 01 00 00 66 0f
    1220: 38 dc c4 f3 0f 6f a2 90 01 00 00 66 0f 38 dc c4
    1230: f3 0f 6f a2 a0 01 00 00 49 81 fb 0c 00 00 00 0f
    1240: 82 3a 00 00 00 66 0f 38 dc c4 f3 0f 6f a2 b0 01
    1250: 00 00 66 0f 38 dc c4 f3 0f 6f a2 c0 01 00 00 0f
    1260: 84 1a 00 00 00 66 0f 38 dc c4 f3 0f 6f a2 d0 01
    1270: 00 00 66 0f 38 dc c4 f3 0f 6f a2 e0 01 00 00 66
    1280: 0f 38 dd c4 f3 41 0f 6f 29 66 0f ef c5 f3 43 0f
    1290: 7f 04 01 48 8d 35 f4 03 00 00 f3 0f 6f 2e 66 0f
    12a0: 38 00 c5 f3 0f 7f 84 0d 48 fe ff ff 49 81 c1 10
    12b0: 00 00 00 48 81 c1 10 00 00 00 49 81 fa 10 00 00
    12c0: 00 0f 83 d2 fe ff ff 48 01 c9 31 f6 48 81 c6 00
    12d0: 01 00 00 48 29 ce 31 c9 f3 0f 6f 85 48 fe ff ff
    12e0: 66 0f ef c1 f3 0f 7f 85 48 fe ff ff 66 0f ef c9
    12f0: 66 0f ef d2 66 0f ef db f3 0f 6f 84 0d 48 fe ff
    1300: ff f3 0f 6f 2c 32 0f 28 e5 66 0f 3a 44 e0 00 66
    1310: 0f ef cc 66 0f 3a 44 e8 11 66 0f ef d5 66 0f 70
    1320: e0 4e 66 0f ef e0 f3 0f 6f 6c 32 10 66 0f 3a 44
    1330: ec 00 66 0f ef dd 48 81 c1 10 00 00 00 48 81 c6
    1340: 20 00 00 00 48 81 fe 00 01 00 00 0f 82 a7 ff ff
    1350: ff 48 8d 0d 36 03 00 00 66 0f ef d9 66 0f ef da
    1360: 0f 28 e3 66 0f 73 db 08 66 0f 73 fc 08 66 0f ef
    1370: d3 66 0f ef cc f3 0f 6f 61 10 66 0f 3a 44 e1 01
    1380: 66 0f 70 c9 4e 66 0f ef cc f3 0f 6f 61 10 66 0f
    1390: 3a 44 e1 01 66 0f 70 c9 4e 66 0f ef cc 66 0f ef
    13a0: ca 4d 85 d2 0f 84 cd 02 00 00 48 8d 8d 48 ff ff
    13b0: ff 4c 89 d6 48 81 e6 08 00 00 00 0f 84 14 00 00
    13c0: 00 49 8b 31 48 89 31 49 81 c1 08 00 00 00 48 81
    13d0: c1 08 00 00 00 4c 89 d6 48 81 e6 04 00 00 00 0f
    13e0: 84 13 00 00 00 41 8b 31 89 31 49 81 c1 04 00 00
    13f0: 00 48 81 c1 04 00 00 00 4c 89 d6 48 81 e6 02 00
    1400: 00 00 0f 84 15 00 00 00 49 0f b7 31 66 89 31 49
    1410: 81 c1 02 00 00 00 48 81 c1 02 00 00 00 4c 89 d6
    1420: 48 81 e6 01 00 00 00 0f 84 15 00 00 00 49 0f b6
    1430: 31 40 88 31 49 81 c1 01 00 00 00 48 81 c1 01 00
    1440: 00 00 f3 0f 6f ad 48 ff ff ff 4d 29 d1 f3 0f 6f
    1450: 85 c8 fe ff ff 48 81 c0 01 00 00 00 89 c6 31 fe
    1460: 0f ce 89 b5 d4 fe ff ff f3 0f 6f a2 10 01 00 00
    1470: 66 0f 38 dc c4 f3 0f 6f a2 20 01 00 00 66 0f 38
    1480: dc c4 f3 0f 6f a2 30 01 00 00 66 0f 38 dc c4 f3
    1490: 0f 6f a2 40 01 00 00 66 0f 38 dc c4 f3 0f 6f a2
    14a0: 50 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 60 01 00
    14b0: 00 66 0f 38 dc c4 f3 0f 6f a2 70 01 00 00 66 0f
    14c0: 38 dc c4 f3 0f 6f a2 80 01 00 00 66 0f 38 dc c4
    14d0: f3 0f 6f a2 90 01 00 00 66 0f 38 dc c4 f3 0f 6f
    14e0: a2 a0 01 00 00 49 81 fb 0c 00 00 00 0f 82 3a 00
    14f0: 00 00 66 0f 38 dc c4 f3 0f 6f a2 b0 01 00 00 66
    1500: 0f 38 dc c4 f3 0f 6f a2 c0 01 00 00 0f 84 1a 00
    1510: 00 00 66 0f 38 dc c4 f3 0f 6f a2 d0 01 00 00 66
    1520: 0f 38 dc c4 f3 0f 6f a2 e0 01 00 00 66 0f 38 dd
    1530: c4 66 0f ef c5 4d 01 c1 f3 0f 7f 85 48 ff ff ff
    1540: 48 8d 8d 48 ff ff ff 4c 89 d6 48 81 e6 08 00 00
    1550: 00 0f 84 14 00 00 00 48 8b 31 49 89 31 48 81 c1
    1560: 08 00 00 00 49 81 c1 08 00 00 00 4c 89 d6 48 81
    1570: e6 04 00 00 00 0f 84 13 00 00 00 8b 31 41 89 31
    1580: 48 81 c1 04 00 00 00 49 81 c1 04 00 00 00 4c 89
    1590: d6 48 81 e6 02 00 00 00 0f 84 16 00 00 00 48 0f
    15a0: b7 31 66 41 89 31 48 81 c1 02 00 00 00 49 81 c1
    15b0: 02 00 00 00 4c 89 d6 48 81 e6 01 00 00 00 0f 84
    15c0: 15 00 00 00 48 0f b6 31 41 88 31 48 81 c1 01 00
    15d0: 00 00 49 81 c1 01 00 00 00 48 8d 35 ae 00 00 00
    15e0: 4c 29 d6 f3 0f 6f 6e 30 66 0f db c5 48 8d 0d 9b
    15f0: 00 00 00 f3 0f 6f 29 66 0f 38 00 c5 66 0f ef c1
    1600: f3 0f 6f 8a e0 00 00 00 0f 28 d1 f3 0f 6f 9a f0
    1610: 00 00 00 66 0f 3a 44 c8 00 66 0f 3a 44 d0 11 66
    1620: 0f 70 e0 4e 66 0f ef e0 66 0f 3a 44 dc 00 66 0f
    1630: ef d9 66 0f ef da 0f 28 e3 66 0f 73 db 08 66 0f
    1640: 73 fc 08 66 0f ef d3 66 0f ef cc f3 0f 6f 61 10
    1650: 66 0f 3a 44 e1 01 66 0f 70 c9 4e 66 0f ef cc f3
    1660: 0f 6f 61 10 66 0f 3a 44 e1 01 66 0f 70 c9 4e 66
    1670: 0f ef cc 66 0f ef ca 4c 8b 85 58 ff ff ff f3 41
    1680: 0f 7f 08 31 c0 48 81 c4 e0 01 00 00 5d c3 0f 0e
    1690: 0d 0c 0b 0a 09 08 07 06 05 04 03 02 01 00 01 00
    16a0: 00 00 00 00 00 00 00 00 00 00 00 00 00 c2 ff ff
    16b0: ff ff ff ff ff ff ff ff ff ff ff ff ff ff 00 00
    16c0: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
}

chunk @mrt_aes_gcm_decrypt {
    0000: 55 48 89 e5 48 81 ec d0 01 00 00 49 89 ca 49 c1
    0010: ea 08 49 89 cb 49 81 e3 ff 00 00 00 48 8d 0d 5f
    0020: 0e 00 00 f3 41 0f 6f 08 f3 41 0f 6f 40 10 41 8b
    0030: 40 1c f3 0f 6f a2 00 01 00 00 8b ba 0c 01 00 00
    0040: 0f c8 0f cf 49 8d 72 0f 48 c1 ee 04 48 01 c6 0f
    0050: ce 41 89 70 1c 66 0f ef e0 f3 0f 7f a5 58 fe ff
    0060: ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    0070: 64 fe ff ff 4c 89 85 58 ff ff ff 49 8b 70 30 4d
    0080: 89 c8 49 29 f0 49 89 f1 49 81 fa 80 00 00 00 0f
    0090: 82 dc 08 00 00 f3 0f 7f b5 60 ff ff ff f3 0f 7f
    00a0: bd 70 ff ff ff f3 44 0f 7f 45 80 f3 44 0f 7f 4d
    00b0: 90 f3 44 0f 7f 55 a0 f3 44 0f 7f 5d b0 f3 44 0f
    00c0: 7f 65 c0 f3 44 0f 7f 6d d0 f3 44 0f 7f 75 e0 f3
    00d0: 44 0f 7f 7d f0 f3 44 0f 6f 39 f3 44 0f 6f 71 10
    00e0: 44 0f 28 c1 44 0f 28 dc 66 45 0f ef c9 66 45 0f
    00f0: ef d2 f3 44 0f 7f 9d 68 fe ff ff 48 81 c0 01 00
    0100: 00 00 89 c6 31 fe 0f ce 89 b5 74 fe ff ff f3 44
    0110: 0f 7f 9d 78 fe ff ff 48 81 c0 01 00 00 00 89 c6
    0120: 31 fe 0f ce 89 b5 84 fe ff ff f3 44 0f 7f 9d 88
    0130: fe ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce
    0140: 89 b5 94 fe ff ff f3 44 0f 7f 9d 98 fe ff ff 48
    0150: 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 a4 fe
    0160: ff ff f3 44 0f 7f 9d a8 fe ff ff 48 81 c0 01 00
    0170: 00 00 89 c6 31 fe 0f ce 89 b5 b4 fe ff ff f3 44
    0180: 0f 7f 9d b8 fe ff ff 48 81 c0 01 00 00 00 89 c6
    0190: 31 fe 0f ce 89 b5 c4 fe ff ff f3 44 0f 7f 9d c8
    01a0: fe ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce
    01b0: 89 b5 d4 fe ff ff 49 81 fa 80 00 00 00 0f 82 63
    01c0: 07 00 00 49 81 ea 80 00 00 00 f3 0f 6f 85 58 fe
    01d0: ff ff f3 0f 6f 8d 68 fe ff ff f3 0f 6f 95 78 fe
    01e0: ff ff f3 0f 6f 9d 88 fe ff ff f3 0f 6f a5 98 fe
    01f0: ff ff f3 0f 6f ad a8 fe ff ff f3 0f 6f b5 b8 fe
    0200: ff ff f3 0f 6f bd c8 fe ff ff f3 45 0f 6f 19 66
    0210: 45 0f 38 00 df 66 45 0f ef d8 f3 44 0f 6f 02 f3
    0220: 44 0f 6f 52 10 45 0f 28 c8 66 45 0f 70 e3 4e 66
    0230: 45 0f ef e3 66 45 0f 3a 44 d4 00 66 45 0f 3a 44
    0240: c3 00 66 45 0f 3a 44 cb 11 f3 44 0f 6f 9a 10 01
    0250: 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41
    0260: 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3
    0270: 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38
    0280: dc fb f3 45 0f 6f 59 10 66 45 0f 38 00 df f3 44
    0290: 0f 6f 62 20 45 0f 28 ec 66 45 0f 3a 44 e3 00 66
    02a0: 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd
    02b0: 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f 62
    02c0: 30 66 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81 c0
    02d0: 01 00 00 00 89 c6 31 fe 0f ce 89 b5 64 fe ff ff
    02e0: f3 44 0f 6f 9a 20 01 00 00 66 41 0f 38 dc c3 66
    02f0: 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc
    0300: db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f
    0310: 38 dc f3 66 41 0f 38 dc fb f3 45 0f 6f 59 20 66
    0320: 45 0f 38 00 df f3 44 0f 6f 62 40 45 0f 28 ec 66
    0330: 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44
    0340: eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f
    0350: ef dc f3 44 0f 6f 62 50 66 45 0f 3a 44 e3 00 66
    0360: 45 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f
    0370: ce 89 b5 74 fe ff ff f3 44 0f 6f 9a 30 01 00 00
    0380: 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38
    0390: dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41
    03a0: 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb
    03b0: f3 45 0f 6f 59 30 66 45 0f 38 00 df f3 44 0f 6f
    03c0: 62 60 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f
    03d0: ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45
    03e0: 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f 62 70 66
    03f0: 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81 c0 01 00
    0400: 00 00 89 c6 31 fe 0f ce 89 b5 84 fe ff ff f3 44
    0410: 0f 6f 9a 40 01 00 00 66 41 0f 38 dc c3 66 41 0f
    0420: 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66
    0430: 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc
    0440: f3 66 41 0f 38 dc fb f3 45 0f 6f 59 40 66 45 0f
    0450: 38 00 df f3 44 0f 6f a2 80 00 00 00 45 0f 28 ec
    0460: 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a
    0470: 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45
    0480: 0f ef dc f3 44 0f 6f a2 90 00 00 00 66 45 0f 3a
    0490: 44 e3 00 66 45 0f ef d4 48 81 c0 01 00 00 00 89
    04a0: c6 31 fe 0f ce 89 b5 94 fe ff ff f3 44 0f 6f 9a
    04b0: 50 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb
    04c0: 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38
    04d0: dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41
    04e0: 0f 38 dc fb f3 45 0f 6f 59 50 66 45 0f 38 00 df
    04f0: f3 44 0f 6f a2 a0 00 00 00 45 0f 28 ec 66 45 0f
    0500: 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11
    0510: 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc
    0520: f3 44 0f 6f a2 b0 00 00 00 66 45 0f 3a 44 e3 00
    0530: 66 45 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe
    0540: 0f ce 89 b5 a4 fe ff ff f3 44 0f 6f 9a 60 01 00
    0550: 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f
    0560: 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66
    0570: 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc
    0580: fb f3 45 0f 6f 59 60 66 45 0f 38 00 df f3 44 0f
    0590: 6f a2 c0 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3
    05a0: 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f
    05b0: ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f
    05c0: 6f a2 d0 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f
    05d0: ef d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    05e0: b5 b4 fe ff ff f3 44 0f 6f 9a 70 01 00 00 66 41
    05f0: 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3
    0600: 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38
    0610: dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 45
    0620: 0f 6f 59 70 66 45 0f 38 00 df f3 44 0f 6f a2 e0
    0630: 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45
    0640: 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66
    0650: 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 f0
    0660: 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 48
    0670: 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 c4 fe
    0680: ff ff f3 44 0f 6f 9a 80 01 00 00 66 41 0f 38 dc
    0690: c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f
    06a0: 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66
    06b0: 41 0f 38 dc f3 66 41 0f 38 dc fb 48 81 c0 01 00
    06c0: 00 00 89 c6 31 fe 0f ce 89 b5 d4 fe ff ff 66 45
    06d0: 0f ef d0 66 45 0f ef d1 45 0f 28 da 66 41 0f 73
    06e0: da 08 66 41 0f 73 fb 08 66 45 0f ef ca 66 45 0f
    06f0: ef c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45 0f
    0700: 70 c0 4e 66 45 0f ef c3 f3 44 0f 6f 9a 90 01 00
    0710: 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f
    0720: 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66
    0730: 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc
    0740: fb 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45 0f 70
    0750: c0 4e 66 45 0f ef c3 66 45 0f ef c1 f3 44 0f 6f
    0760: 9a a0 01 00 00 49 81 fb 0c 00 00 00 0f 82 ea 00
    0770: 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41
    0780: 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3
    0790: 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38
    07a0: dc fb f3 44 0f 6f 9a b0 01 00 00 66 41 0f 38 dc
    07b0: c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f
    07c0: 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66
    07d0: 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9a
    07e0: c0 01 00 00 0f 84 72 00 00 00 66 41 0f 38 dc c3
    07f0: 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38
    0800: dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41
    0810: 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9a d0
    0820: 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66
    0830: 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc
    0840: e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f
    0850: 38 dc fb f3 44 0f 6f 9a e0 01 00 00 66 41 0f 38
    0860: dd c3 66 41 0f 38 dd cb 66 41 0f 38 dd d3 66 41
    0870: 0f 38 dd db 66 41 0f 38 dd e3 66 41 0f 38 dd eb
    0880: 66 41 0f 38 dd f3 66 41 0f 38 dd fb f3 45 0f 6f
    0890: 19 66 41 0f ef c3 f3 45 0f 6f 59 10 66 41 0f ef
    08a0: cb f3 45 0f 6f 59 20 66 41 0f ef d3 f3 45 0f 6f
    08b0: 59 30 66 41 0f ef db f3 45 0f 6f 59 40 66 41 0f
    08c0: ef e3 f3 45 0f 6f 59 50 66 41 0f ef eb f3 45 0f
    08d0: 6f 59 60 66 41 0f ef f3 f3 45 0f 6f 59 70 66 41
    08e0: 0f ef fb f3 43 0f 7f 04 01 f3 43 0f 7f 4c 01 10
    08f0: f3 43 0f 7f 54 01 20 f3 43 0f 7f 5c 01 30 f3 43
    0900: 0f 7f 64 01 40 f3 43 0f 7f 6c 01 50 f3 43 0f 7f
    0910: 74 01 60 f3 43 0f 7f 7c 01 70 49 81 c1 80 00 00
    0920: 00 e9 90 f8 ff ff 41 0f 28 c8 f3 0f 6f b5 60 ff
    0930: ff ff f3 0f 6f bd 70 ff ff ff f3 44 0f 6f 45 80
    0940: f3 44 0f 6f 4d 90 f3 44 0f 6f 55 a0 f3 44 0f 6f
    0950: 5d b0 f3 44 0f 6f 65 c0 f3 44 0f 6f 6d d0 f3 44
    0960: 0f 6f 75 e0 f3 44 0f 6f 7d f0 48 81 e8 07 00 00
    0970: 00 49 81 fa 10 00 00 00 0f 82 0f 02 00 00 31 c9
    0980: 49 81 ea 10 00 00 00 f3 41 0f 6f 01 48 8d 35 ef
    0990: 04 00 00 f3 0f 6f 2e 66 0f 38 00 c5 f3 0f 7f 84
    09a0: 0d e8 fe ff ff f3 0f 6f 85 58 fe ff ff 48 81 c0
    09b0: 01 00 00 00 89 c6 31 fe 0f ce 89 b5 64 fe ff ff
    09c0: f3 0f 6f a2 10 01 00 00 66 0f 38 dc c4 f3 0f 6f
    09d0: a2 20 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 30 01
    09e0: 00 00 66 0f 38 dc c4 f3 0f 6f a2 40 01 00 00 66
    09f0: 0f 38 dc c4 f3 0f 6f a2 50 01 00 00 66 0f 38 dc
    0a00: c4 f3 0f 6f a2 60 01 00 00 66 0f 38 dc c4 f3 0f
    0a10: 6f a2 70 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 80
    0a20: 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 90 01 00 00
    0a30: 66 0f 38 dc c4 f3 0f 6f a2 a0 01 00 00 49 81 fb
    0a40: 0c 00 00 00 0f 82 3a 00 00 00 66 0f 38 dc c4 f3
    0a50: 0f 6f a2 b0 01 00 00 66 0f 38 dc c4 f3 0f 6f a2
    0a60: c0 01 00 00 0f 84 1a 00 00 00 66 0f 38 dc c4 f3
    0a70: 0f 6f a2 d0 01 00 00 66 0f 38 dc c4 f3 0f 6f a2
    0a80: e0 01 00 00 66 0f 38 dd c4 f3 41 0f 6f 29 66 0f
    0a90: ef c5 f3 43 0f 7f 04 01 49 81 c1 10 00 00 00 48
    0aa0: 81 c1 10 00 00 00 49 81 fa 10 00 00 00 0f 83 cd
    0ab0: fe ff ff 48 01 c9 31 f6 48 81 c6 00 01 00 00 48
    0ac0: 29 ce 31 c9 f3 0f 6f 85 e8 fe ff ff 66 0f ef c1
    0ad0: f3 0f 7f 85 e8 fe ff ff 66 0f ef c9 66 0f ef d2
    0ae0: 66 0f ef db f3 0f 6f 84 0d e8 fe ff ff f3 0f 6f
    0af0: 2c 32 0f 28 e5 66 0f 3a 44 e0 00 66 0f ef cc 66
    0b00: 0f 3a 44 e8 11 66 0f ef d5 66 0f 70 e0 4e 66 0f
    0b10: ef e0 f3 0f 6f 6c 32 10 66 0f 3a 44 ec 00 66 0f
    0b20: ef dd 48 81 c1 10 00 00 00 48 81 c6 20 00 00 00
    0b30: 48 81 fe 00 01 00 00 0f 82 a7 ff ff ff 48 8d 0d
    0b40: 3e 03 00 00 66 0f ef d9 66 0f ef da 0f 28 e3 66
    0b50: 0f 73 db 08 66 0f 73 fc 08 66 0f ef d3 66 0f ef
    0b60: cc f3 0f 6f 61 10 66 0f 3a 44 e1 01 66 0f 70 c9
    0b70: 4e 66 0f ef cc f3 0f 6f 61 10 66 0f 3a 44 e1 01
    0b80: 66 0f 70 c9 4e 66 0f ef cc 66 0f ef ca 4d 85 d2
    0b90: 0f 84 d5 02 00 00 48 8d 8d d8 fe ff ff 4c 89 d6
    0ba0: 48 81 e6 08 00 00 00 0f 84 14 00 00 00 49 8b 31
    0bb0: 48 89 31 49 81 c1 08 00 00 00 48 81 c1 08 00 00
    0bc0: 00 4c 89 d6 48 81 e6 04 00 00 00 0f 84 13 00 00
    0bd0: 00 41 8b 31 89 31 49 81 c1 04 00 00 00 48 81 c1
    0be0: 04 00 00 00 4c 89 d6 48 81 e6 02 00 00 00 0f 84
    0bf0: 15 00 00 00 49 0f b7 31 66 89 31 49 81 c1 02 00
    0c00: 00 00 48 81 c1 02 00 00 00 4c 89 d6 48 81 e6 01
    0c10: 00 00 00 0f 84 15 00 00 00 49 0f b6 31 40 88 31
    0c20: 49 81 c1 01 00 00 00 48 81 c1 01 00 00 00 f3 0f
    0c30: 6f 85 d8 fe ff ff 4d 29 d1 48 8d 35 42 02 00 00
    0c40: 4c 29 d6 f3 0f 6f 6e 30 66 0f db c5 48 8d 0d 2f
    0c50: 02 00 00 f3 0f 6f 29 66 0f 38 00 c5 66 0f ef c1
    0c60: f3 0f 6f 8a e0 00 00 00 0f 28 d1 f3 0f 6f 9a f0
    0c70: 00 00 00 66 0f 3a 44 c8 00 66 0f 3a 44 d0 11 66
    0c80: 0f 70 e0 4e 66 0f ef e0 66 0f 3a 44 dc 00 66 0f
    0c90: ef d9 66 0f ef da 0f 28 e3 66 0f 73 db 08 66 0f
    0ca0: 73 fc 08 66 0f ef d3 66 0f ef cc f3 0f 6f 61 10
    0cb0: 66 0f 3a 44 e1 01 66 0f 70 c9 4e 66 0f ef cc f3
    0cc0: 0f 6f 61 10 66 0f 3a 44 e1 01 66 0f 70 c9 4e 66
    0cd0: 0f ef cc 66 0f ef ca f3 0f 6f 85 58 fe ff ff 48
    0ce0: 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 64 fe
    0cf0: ff ff f3 0f 6f a2 10 01 00 00 66 0f 38 dc c4 f3
    0d00: 0f 6f a2 20 01 00 00 66 0f 38 dc c4 f3 0f 6f a2
    0d10: 30 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 40 01 00
    0d20: 00 66 0f 38 dc c4 f3 0f 6f a2 50 01 00 00 66 0f
    0d30: 38 dc c4 f3 0f 6f a2 60 01 00 00 66 0f 38 dc c4
    0d40: f3 0f 6f a2 70 01 00 00 66 0f 38 dc c4 f3 0f 6f
    0d50: a2 80 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 90 01
    0d60: 00 00 66 0f 38 dc c4 f3 0f 6f a2 a0 01 00 00 49
    0d70: 81 fb 0c 00 00 00 0f 82 3a 00 00 00 66 0f 38 dc
    0d80: c4 f3 0f 6f a2 b0 01 00 00 66 0f 38 dc c4 f3 0f
    0d90: 6f a2 c0 01 00 00 0f 84 1a 00 00 00 66 0f 38 dc
    0da0: c4 f3 0f 6f a2 d0 01 00 00 66 0f 38 dc c4 f3 0f
    0db0: 6f a2 e0 01 00 00 66 0f 38 dd c4 f3 0f 6f ad d8
    0dc0: fe ff ff 66 0f ef c5 4d 01 c1 f3 0f 7f 85 d8 fe
    0dd0: ff ff 48 8d 8d d8 fe ff ff 4c 89 d6 48 81 e6 08
    0de0: 00 00 00 0f 84 14 00 00 00 48 8b 31 49 89 31 48
    0df0: 81 c1 08 00 00 00 49 81 c1 08 00 00 00 4c 89 d6
    0e00: 48 81 e6 04 00 00 00 0f 84 13 00 00 00 8b 31 41
    0e10: 89 31 48 81 c1 04 00 00 00 49 81 c1 04 00 00 00
    0e20: 4c 89 d6 48 81 e6 02 00 00 00 0f 84 16 00 00 00
    0e30: 48 0f b7 31 66 41 89 31 48 81 c1 02 00 00 00 49
    0e40: 81 c1 02 00 00 00 4c 89 d6 48 81 e6 01 00 00 00
    0e50: 0f 84 15 00 00 00 48 0f b6 31 41 88 31 48 81 c1
    0e60: 01 00 00 00 49 81 c1 01 00 00 00 4c 8b 85 58 ff
    0e70: ff ff f3 41 0f 7f 08 31 c0 48 81 c4 d0 01 00 00
    0e80: 5d c3 0f 0e 0d 0c 0b 0a 09 08 07 06 05 04 03 02
    0e90: 01 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00
    0ea0: 00 c2 ff ff ff ff ff ff ff ff ff ff ff ff ff ff
    0eb0: ff ff 00 00 00 00 00 00 00 00 00 00 00 00 00 00
    0ec0: 00 00 00
}

chunk @mrt_aes_gcm_finish {
    0000: 55 48 89 e5 48 83 ec 20 48 8d 05 b5 00 00 00 f3
    0010: 41 0f 6f 08 f3 41 0f 6f 68 20 49 c1 e1 03 48 c1
    0020: e1 03 66 49 0f 6e c1 66 48 0f 3a 22 c1 01 66 0f
    0030: ef c1 f3 0f 6f 8a e0 00 00 00 0f 28 d1 f3 0f 6f
    0040: 9a f0 00 00 00 66 0f 3a 44 c8 00 66 0f 3a 44 d0
    0050: 11 66 0f 70 e0 4e 66 0f ef e0 66 0f 3a 44 dc 00
    0060: 66 0f ef d9 66 0f ef da 0f 28 e3 66 0f 73 db 08
    0070: 66 0f 73 fc 08 66 0f ef d3 66 0f ef cc f3 0f 6f
    0080: 60 10 66 0f 3a 44 e1 01 66 0f 70 c9 4e 66 0f ef
    0090: cc f3 0f 6f 60 10 66 0f 3a 44 e1 01 66 0f 70 c9
    00a0: 4e 66 0f ef cc 66 0f ef ca f3 0f 6f 20 66 0f 38
    00b0: 00 cc 66 0f ef cd f3 41 0f 7f 08 31 c0 48 83 c4
    00c0: 20 5d c3 00 0f 0e 0d 0c 0b 0a 09 08 07 06 05 04
    00d0: 03 02 01 00 01 00 00 00 00 00 00 00 00 00 00 00
    00e0: 00 00 00 c2 ff ff ff ff ff ff ff ff ff ff ff ff
    00f0: ff ff ff ff 00 00 00 00 00 00 00 00 00 00 00 00
    0100: 00 00 00 00 00
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
    x64.prologue 152
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
    x64.storeSlotReg slot4, r8
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.storeSlotReg slot5, r8
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r15, r8
    x64.storeSlotReg slot6, r15
    x64.movRegImm32 rdx, 16
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 352
    x64.movRegReg rcx, r12
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 432
    x64.movRegReg rcx, r13
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 12
    x64.movRegReg rcx, r14
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 32
    x64.loadRegSlot rcx, slot4
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 64
    x64.loadRegSlot rcx, slot5
    x64.callDirect __managed_resize
    x64.movRegImm32 rdx, 80
    x64.movRegReg rcx, r15
    x64.callDirect __managed_resize
    x64.loadRegSlot rdx, slot0
    x64.movRegReg rcx, r12
    x64.callDirect __cpufeat_aes_cipher_expand_key
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r12
    x64.callDirect __cpufeat_aes_cipher_gcm_init
    x64.movRegImm32 rdx, 0
    x64.loadRegSlot rdi, slot5
    x64.loadRegSlot rsi, slot4
    x64.movRegReg rcx, r15
    x64.movRegReg rax, r13
    x64.movRegReg r9, r14
    x64.callDirect __cpufeat_aes_cipher_gcm_seal
    x64.movRegImm32 rdx, 0
    x64.loadRegSlot rcx, slot5
    x64.loadRegSlot rsi, slot4
    x64.movRegReg rax, r13
    x64.movRegReg r9, r14
    x64.movRegReg rdi, r15
    x64.callDirect __cpufeat_aes_cipher_gcm_open
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 5
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot7, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __bool_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot13, rcx
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.loadRegBaseDisp.word64 r14, [r15 + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r15
    x64.callDirect __uint_to_string
    x64.storeSlotReg slot12, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot11, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot10, rax
    x64.leaRegRegReg rcx, rbx, r13
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot8, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot9, r8
    x64.leaRegRegImm32 r14, r8, 56
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r14, rbx
    x64.loadRegSlot rdx, slot13
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r13
    x64.loadRegSlot rax, slot12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot12
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot10
    x64.loadRegSlot rdx, slot11
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot10
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot9
    x64.storeBaseDispReg.word64 [rax + 0], r14
    x64.loadRegSlot rcx, slot8
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot7
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot9
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot9
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot6
    x64.callDirect __managed_decref
  exitdrop#2:
    x64.loadRegSlot rcx, slot5
    x64.callDirect __managed_decref
  exitdrop#3:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __managed_decref
  exitdrop#4:
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
    x64.epilogue 152
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
    x64.prologue 64
    x64.loadRegBaseDisp.word64 rax, [rdx + 8]
    x64.cmpRegImm32 rax, 352
    x64.jcc less, gibad
  scbounded:
    x64.cmpRegImm32 rax, 480
    x64.jcc greater, gibad
  scstepped:
    x64.movRegReg rsi, rax
    x64.andRegImm32 rsi, rax, 63
    x64.cmpRegImm32 rsi, 32
    x64.jcc notEqual, gibad
  gikeyblock:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.movRegReg rdi, rax
    x64.shrRegImm8 rdi, rax, 1
    x64.leaRegRegImm32 rdi, rdi, 256
    x64.cmpRegReg rsi, rdi
    x64.jcc notEqual, gibad
  gicrypt:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, giddetach
  gidbuf:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, giddone
  gidshared:
    x64.leaRegRegImm32 rsi, rsi, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, giddetach
  giddone:
    x64.shrRegImm8 rax, rax, 5
    x64.leaRegRegImm32 rax, rax, -1
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.loadRegBaseDisp.word64 r8, [rdx + 0]
    x64.movRegImm32 r9, 0
    x64.movRegReg rcx, rax
    x64.movRegReg rdx, rsi
    x64.callDirect mrt_aes_gcm_init
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  giddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.jmp giddone
  gibad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_seal {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 168
    x64.loadRegBaseDisp.word64 r8, [rax + 8]
    x64.cmpRegImm32 r8, 432
    x64.jcc less, gpbad
  kbbounded:
    x64.cmpRegImm32 r8, 496
    x64.jcc greater, gpbad
  kbstepped:
    x64.movRegReg r10, r8
    x64.andRegImm32 r10, r8, 31
    x64.cmpRegImm32 r10, 16
    x64.jcc notEqual, gpbad
  gpnonce:
    x64.loadRegBaseDisp.word64 r10, [r9 + 8]
    x64.cmpRegImm32 r10, 12
    x64.jcc notEqual, gpbad
  gpsource:
    x64.loadRegBaseDisp.word64 rbx, [rdi + 8]
  gpmeasure:
    x64.movRegImm r10, 68719476704
    x64.cmpRegReg rbx, r10
    x64.jcc above, gpbad
  gpspan:
    x64.leaRegRegImm32 r10, rbx, 16
    x64.loadRegBaseDisp.word64 r11, [rcx + 8]
    x64.cmpRegReg rdx, r11
    x64.jcc above, gpbad
  gpinside:
    x64.subRegReg r11, r11, rdx
    x64.cmpRegReg r11, r10
    x64.jcc below, gpbad
  gpdetach:
    x64.loadRegBaseDisp.word64 r11, [rcx + 16]
    x64.cmpRegImm32 r11, 0
    x64.jcc less, gpddetach
  gpdbuf:
    x64.loadRegBaseDisp.word64 r11, [rcx + 0]
    x64.cmpRegImm32 r11, 0
    x64.jcc equal, gpddone
  gpdshared:
    x64.leaRegRegImm32 r11, r11, -16
    x64.loadRegBaseDisp.word64 r11, [r11 + 8]
    x64.cmpRegImm32 r11, 0
    x64.jcc notEqual, gpddetach
  gpddone:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdi, [rdi + 0]
    x64.leaRegRegReg rcx, rcx, rdx
    x64.storeSlotReg slot7, rcx
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, gpoverlap
  gpwritten:
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, gpoverlap
  gpapart:
    x64.cmpRegReg rcx, rdi
    x64.jcc equal, gpoverlap
  gpbelow:
    x64.leaRegRegReg rdx, rcx, r10
    x64.cmpRegReg rdx, rdi
    x64.jcc belowEqual, gpoverlap
  gpabove:
    x64.leaRegRegReg rdx, rdi, rbx
    x64.cmpRegReg rdx, rcx
    x64.jcc above, gpbad
  gpoverlap:
    x64.leaRegRegImm32 rcx, r8, -256
    x64.shrRegImm8 rcx, rcx, 4
    x64.leaRegRegImm32 rcx, rcx, -1
    x64.storeSlotReg slot8, rcx
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [r9 + 0]
    x64.loadRegBaseDisp.word64 r13, [rsi + 0]
    x64.loadRegBaseDisp.word64 r14, [rsi + 8]
    x64.leaRegSlot r15, slot0
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [r15 + 0], rdx
    x64.storeBaseDispReg.word64 [r15 + 8], rdx
    x64.loadRegBaseDisp.word64 rdx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 4]
    x64.storeBaseDispReg.word64 [r15 + 16], rdx
    x64.storeBaseDispReg.word64 [r15 + 20], rax
    x64.movRegImm32 rax, 16777216
    x64.storeBaseDispReg.word64 [r15 + 28], rax
    x64.storeBaseDispReg.word64 [r15 + 48], rdi
    x64.leaRegRegImm32 rdx, r12, 256
    x64.leaRegRegImm32 r9, r15, 16
    x64.leaRegRegImm32 r8, r15, 32
    x64.callDirect mrt_aes_encrypt_block
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, ghdone
  ghrun:
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg r8, r15
    x64.movRegReg r9, r13
    x64.callDirect mrt_aes_gcm_hash
  ghdone:
    x64.movRegReg rax, rbx
    x64.shlRegImm8 rax, rbx, 8
    x64.loadRegSlot rcx, slot8
    x64.orRegReg rax, rax, rcx
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, gcdone
  gcrun:
    x64.loadRegSlot r9, slot7
    x64.movRegReg rcx, rax
    x64.movRegReg rdx, r12
    x64.movRegReg r8, r15
    x64.callDirect mrt_aes_gcm_encrypt
  gcdone:
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg r8, r15
    x64.movRegReg r9, rbx
    x64.callDirect mrt_aes_gcm_finish
    x64.loadRegBaseDisp.word64 rax, [r15 + 0]
    x64.loadRegBaseDisp.word64 rcx, [r15 + 8]
    x64.loadRegSlot rdx, slot7
    x64.leaRegRegReg rdx, rdx, rbx
    x64.storeBaseDispReg.word64 [rdx + 0], rax
    x64.storeBaseDispReg.word64 [rdx + 8], rcx
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r15 + 0], rax
    x64.storeBaseDispReg.word64 [r15 + 8], rax
    x64.storeBaseDispReg.word64 [r15 + 16], rax
    x64.storeBaseDispReg.word64 [r15 + 24], rax
    x64.storeBaseDispReg.word64 [r15 + 32], rax
    x64.storeBaseDispReg.word64 [r15 + 40], rax
    x64.storeBaseDispReg.word64 [r15 + 48], rax
    x64.epilogue 168
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  gpddetach:
    x64.storeSlotReg slot9, rax
    x64.storeSlotReg slot10, rcx
    x64.storeSlotReg slot11, rdx
    x64.storeSlotReg slot12, rsi
    x64.storeSlotReg slot13, rdi
    x64.storeSlotReg slot14, r8
    x64.storeSlotReg slot15, r9
    x64.storeSlotReg slot16, r10
    x64.callDirect __managed_cow_detach
    x64.movRegReg r11, r8
    x64.loadRegSlot rax, slot9
    x64.loadRegSlot rcx, slot10
    x64.loadRegSlot rdx, slot11
    x64.loadRegSlot rsi, slot12
    x64.loadRegSlot rdi, slot13
    x64.loadRegSlot r8, slot14
    x64.loadRegSlot r9, slot15
    x64.loadRegSlot r10, slot16
    x64.jmp gpddone
  gpbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 168
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_open {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 184
    x64.loadRegBaseDisp.word64 r8, [rax + 8]
    x64.cmpRegImm32 r8, 432
    x64.jcc less, gpbad
  kbbounded:
    x64.cmpRegImm32 r8, 496
    x64.jcc greater, gpbad
  kbstepped:
    x64.movRegReg r10, r8
    x64.andRegImm32 r10, r8, 31
    x64.cmpRegImm32 r10, 16
    x64.jcc notEqual, gpbad
  gpnonce:
    x64.loadRegBaseDisp.word64 r10, [r9 + 8]
    x64.cmpRegImm32 r10, 12
    x64.jcc notEqual, gpbad
  gpsource:
    x64.loadRegBaseDisp.word64 r10, [rdi + 8]
    x64.cmpRegImm32 r10, 16
    x64.jcc less, gpbad
  gpmeasure:
    x64.leaRegRegImm32 rbx, r10, -16
    x64.movRegImm r11, 68719476704
    x64.cmpRegReg rbx, r11
    x64.jcc above, gpbad
  gpspan:
    x64.loadRegBaseDisp.word64 r11, [rcx + 8]
    x64.cmpRegReg rdx, r11
    x64.jcc above, gpbad
  gpinside:
    x64.subRegReg r11, r11, rdx
    x64.cmpRegReg r11, rbx
    x64.jcc below, gpbad
  gpdetach:
    x64.loadRegBaseDisp.word64 r11, [rcx + 16]
    x64.cmpRegImm32 r11, 0
    x64.jcc less, gpddetach
  gpdbuf:
    x64.loadRegBaseDisp.word64 r11, [rcx + 0]
    x64.cmpRegImm32 r11, 0
    x64.jcc equal, gpddone
  gpdshared:
    x64.leaRegRegImm32 r11, r11, -16
    x64.loadRegBaseDisp.word64 r11, [r11 + 8]
    x64.cmpRegImm32 r11, 0
    x64.jcc notEqual, gpddetach
  gpddone:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdi, [rdi + 0]
    x64.storeSlotReg slot7, rdi
    x64.leaRegRegReg rcx, rcx, rdx
    x64.storeSlotReg slot8, rcx
  gpwritten:
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, gpoverlap
  gpapart:
    x64.cmpRegReg rcx, rdi
    x64.jcc equal, gpoverlap
  gpbelow:
    x64.leaRegRegReg rdx, rcx, rbx
    x64.cmpRegReg rdx, rdi
    x64.jcc belowEqual, gpoverlap
  gpabove:
    x64.leaRegRegReg rdx, rdi, r10
    x64.cmpRegReg rdx, rcx
    x64.jcc above, gpbad
  gpoverlap:
    x64.leaRegRegImm32 rcx, r8, -256
    x64.shrRegImm8 rcx, rcx, 4
    x64.leaRegRegImm32 rcx, rcx, -1
    x64.storeSlotReg slot9, rcx
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [r9 + 0]
    x64.loadRegBaseDisp.word64 r13, [rsi + 0]
    x64.loadRegBaseDisp.word64 r14, [rsi + 8]
    x64.leaRegSlot r15, slot0
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [r15 + 0], rdx
    x64.storeBaseDispReg.word64 [r15 + 8], rdx
    x64.loadRegBaseDisp.word64 rdx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 4]
    x64.storeBaseDispReg.word64 [r15 + 16], rdx
    x64.storeBaseDispReg.word64 [r15 + 20], rax
    x64.movRegImm32 rax, 16777216
    x64.storeBaseDispReg.word64 [r15 + 28], rax
    x64.storeBaseDispReg.word64 [r15 + 48], rdi
    x64.leaRegRegImm32 rdx, r12, 256
    x64.leaRegRegImm32 r9, r15, 16
    x64.leaRegRegImm32 r8, r15, 32
    x64.callDirect mrt_aes_encrypt_block
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, ghdone
  ghrun:
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg r8, r15
    x64.movRegReg r9, r13
    x64.callDirect mrt_aes_gcm_hash
  ghdone:
    x64.movRegReg rax, rbx
    x64.shlRegImm8 rax, rbx, 8
    x64.loadRegSlot rcx, slot9
    x64.orRegReg rax, rax, rcx
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, gcdone
  gcrun:
    x64.loadRegSlot r9, slot8
    x64.movRegReg rcx, rax
    x64.movRegReg rdx, r12
    x64.movRegReg r8, r15
    x64.callDirect mrt_aes_gcm_decrypt
  gcdone:
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg r8, r15
    x64.movRegReg r9, rbx
    x64.callDirect mrt_aes_gcm_finish
    x64.loadRegBaseDisp.word64 rax, [r15 + 0]
    x64.loadRegBaseDisp.word64 rcx, [r15 + 8]
    x64.loadRegSlot rdx, slot7
    x64.leaRegRegReg rdx, rdx, rbx
    x64.loadRegBaseDisp.word64 rsi, [rdx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.xorRegReg rax, rax, rsi
    x64.xorRegReg rcx, rcx, rdx
    x64.orRegReg rax, rax, rcx
    x64.movRegImm32 rcx, 0
    x64.storeBaseDispReg.word64 [r15 + 0], rcx
    x64.storeBaseDispReg.word64 [r15 + 8], rcx
    x64.storeBaseDispReg.word64 [r15 + 16], rcx
    x64.storeBaseDispReg.word64 [r15 + 24], rcx
    x64.storeBaseDispReg.word64 [r15 + 32], rcx
    x64.storeBaseDispReg.word64 [r15 + 40], rcx
    x64.storeBaseDispReg.word64 [r15 + 48], rcx
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, gprejected
  gpaccepted:
    x64.movRegImm32 r8, 1
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  gprejected:
    x64.movRegImm32 r8, 0
    x64.epilogue 184
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  gpddetach:
    x64.storeSlotReg slot10, rax
    x64.storeSlotReg slot11, rcx
    x64.storeSlotReg slot12, rdx
    x64.storeSlotReg slot13, rsi
    x64.storeSlotReg slot14, rdi
    x64.storeSlotReg slot15, r8
    x64.storeSlotReg slot16, r9
    x64.storeSlotReg slot17, r10
    x64.callDirect __managed_cow_detach
    x64.movRegReg r11, r8
    x64.loadRegSlot rax, slot10
    x64.loadRegSlot rcx, slot11
    x64.loadRegSlot rdx, slot12
    x64.loadRegSlot rsi, slot13
    x64.loadRegSlot rdi, slot14
    x64.loadRegSlot r8, slot15
    x64.loadRegSlot r9, slot16
    x64.loadRegSlot r10, slot17
    x64.jmp gpddone
  gpbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 184
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

chunk @mrt_aes_gcm_init {
    0000: 55 48 89 e5 48 83 ec 20 4c 8d 8a 00 01 00 00 4c
    0010: 8d 51 01 f3 41 0f 6f 00 f3 41 0f 7f 01 49 81 c0
    0020: 10 00 00 00 49 81 c1 10 00 00 00 49 81 ea 01 00
    0030: 00 00 0f 85 db ff ff ff 48 8d 05 d9 01 00 00 f3
    0040: 0f 6f 82 00 01 00 00 f3 0f 6f a2 10 01 00 00 66
    0050: 0f 38 dc c4 f3 0f 6f a2 20 01 00 00 66 0f 38 dc
    0060: c4 f3 0f 6f a2 30 01 00 00 66 0f 38 dc c4 f3 0f
    0070: 6f a2 40 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 50
    0080: 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 60 01 00 00
    0090: 66 0f 38 dc c4 f3 0f 6f a2 70 01 00 00 66 0f 38
    00a0: dc c4 f3 0f 6f a2 80 01 00 00 66 0f 38 dc c4 f3
    00b0: 0f 6f a2 90 01 00 00 66 0f 38 dc c4 f3 0f 6f a2
    00c0: a0 01 00 00 48 81 f9 0c 00 00 00 0f 82 3a 00 00
    00d0: 00 66 0f 38 dc c4 f3 0f 6f a2 b0 01 00 00 66 0f
    00e0: 38 dc c4 f3 0f 6f a2 c0 01 00 00 0f 84 1a 00 00
    00f0: 00 66 0f 38 dc c4 f3 0f 6f a2 d0 01 00 00 66 0f
    0100: 38 dc c4 f3 0f 6f a2 e0 01 00 00 66 0f 38 dd c4
    0110: f3 0f 6f 28 66 0f 38 00 c5 f3 0f 6f 48 10 66 0f
    0120: 70 e0 ff 0f 28 e8 66 0f 72 e4 1f 66 0f db e1 66
    0130: 0f 72 d5 1f 66 0f 73 fd 04 66 0f 72 f0 01 66 0f
    0140: ef c4 66 0f ef c5 f3 0f 7f 82 e0 00 00 00 66 0f
    0150: 70 e0 4e 66 0f ef e0 f3 0f 7f a2 f0 00 00 00 4c
    0160: 8d 92 c0 00 00 00 45 31 db 49 81 c3 07 00 00 00
    0170: f3 0f 6f 8a e0 00 00 00 0f 28 d1 f3 0f 6f 9a f0
    0180: 00 00 00 66 0f 3a 44 c8 00 66 0f 3a 44 d0 11 66
    0190: 0f 70 e0 4e 66 0f ef e0 66 0f 3a 44 dc 00 66 0f
    01a0: ef d9 66 0f ef da 0f 28 e3 66 0f 73 db 08 66 0f
    01b0: 73 fc 08 66 0f ef d3 66 0f ef cc f3 0f 6f 60 10
    01c0: 66 0f 3a 44 e1 01 66 0f 70 c9 4e 66 0f ef cc f3
    01d0: 0f 6f 60 10 66 0f 3a 44 e1 01 66 0f 70 c9 4e 66
    01e0: 0f ef cc 66 0f ef ca 0f 28 c1 f3 41 0f 7f 02 66
    01f0: 0f 70 e0 4e 66 0f ef e0 f3 41 0f 7f 62 10 49 81
    0200: eb 01 00 00 00 4d 8d 52 e0 0f 85 61 ff ff ff 31
    0210: c0 48 83 c4 20 5d c3 00 0f 0e 0d 0c 0b 0a 09 08
    0220: 07 06 05 04 03 02 01 00 01 00 00 00 00 00 00 00
    0230: 00 00 00 00 00 00 00 c2 ff ff ff ff ff ff ff ff
    0240: ff ff ff ff ff ff ff ff 00 00 00 00 00 00 00 00
    0250: 00 00 00 00 00 00 00 00 00
}

chunk @mrt_aes_gcm_hash {
    0000: 55 48 89 e5 48 81 ec d0 00 00 00 48 8d 05 90 05
    0010: 00 00 f3 41 0f 6f 08 48 85 c9 0f 84 72 05 00 00
    0020: 48 81 f9 80 00 00 00 0f 82 7a 03 00 00 f3 0f 7f
    0030: b5 60 ff ff ff f3 0f 7f bd 70 ff ff ff f3 44 0f
    0040: 7f 45 80 f3 44 0f 7f 4d 90 f3 44 0f 7f 55 a0 f3
    0050: 44 0f 7f 5d b0 f3 44 0f 7f 65 c0 f3 44 0f 7f 6d
    0060: d0 f3 44 0f 7f 75 e0 f3 44 0f 7f 7d f0 f3 44 0f
    0070: 6f 38 f3 44 0f 6f 70 10 44 0f 28 c1 44 0f 28 dc
    0080: 66 45 0f ef c9 66 45 0f ef d2 48 81 f9 80 00 00
    0090: 00 0f 82 cc 02 00 00 48 81 e9 80 00 00 00 f3 41
    00a0: 0f 6f 01 f3 41 0f 6f 49 10 f3 41 0f 6f 51 20 f3
    00b0: 41 0f 6f 59 30 f3 41 0f 6f 61 40 f3 41 0f 6f 69
    00c0: 50 f3 41 0f 6f 71 60 f3 41 0f 6f 79 70 49 81 c1
    00d0: 80 00 00 00 66 41 0f 38 00 c7 66 41 0f 38 00 cf
    00e0: 66 41 0f 38 00 d7 66 41 0f 38 00 df 66 41 0f 38
    00f0: 00 e7 66 41 0f 38 00 ef 66 41 0f 38 00 f7 66 41
    0100: 0f 38 00 ff 66 41 0f ef c0 f3 44 0f 6f 02 f3 44
    0110: 0f 6f 52 10 45 0f 28 c8 66 44 0f 70 e0 4e 66 44
    0120: 0f ef e0 66 45 0f 3a 44 d4 00 66 44 0f 3a 44 c0
    0130: 00 66 44 0f 3a 44 c8 11 f3 44 0f 6f 62 20 45 0f
    0140: 28 ec 66 44 0f 3a 44 e1 00 66 45 0f ef c4 66 44
    0150: 0f 3a 44 e9 11 66 45 0f ef cd 66 44 0f 70 e1 4e
    0160: 66 41 0f ef cc f3 44 0f 6f 62 30 66 44 0f 3a 44
    0170: e1 00 66 45 0f ef d4 f3 44 0f 6f 62 40 45 0f 28
    0180: ec 66 44 0f 3a 44 e2 00 66 45 0f ef c4 66 44 0f
    0190: 3a 44 ea 11 66 45 0f ef cd 66 44 0f 70 e2 4e 66
    01a0: 41 0f ef d4 f3 44 0f 6f 62 50 66 44 0f 3a 44 e2
    01b0: 00 66 45 0f ef d4 f3 44 0f 6f 62 60 45 0f 28 ec
    01c0: 66 44 0f 3a 44 e3 00 66 45 0f ef c4 66 44 0f 3a
    01d0: 44 eb 11 66 45 0f ef cd 66 44 0f 70 e3 4e 66 41
    01e0: 0f ef dc f3 44 0f 6f 62 70 66 44 0f 3a 44 e3 00
    01f0: 66 45 0f ef d4 f3 44 0f 6f a2 80 00 00 00 45 0f
    0200: 28 ec 66 44 0f 3a 44 e4 00 66 45 0f ef c4 66 44
    0210: 0f 3a 44 ec 11 66 45 0f ef cd 66 44 0f 70 e4 4e
    0220: 66 41 0f ef e4 f3 44 0f 6f a2 90 00 00 00 66 44
    0230: 0f 3a 44 e4 00 66 45 0f ef d4 f3 44 0f 6f a2 a0
    0240: 00 00 00 45 0f 28 ec 66 44 0f 3a 44 e5 00 66 45
    0250: 0f ef c4 66 44 0f 3a 44 ed 11 66 45 0f ef cd 66
    0260: 44 0f 70 e5 4e 66 41 0f ef ec f3 44 0f 6f a2 b0
    0270: 00 00 00 66 44 0f 3a 44 e5 00 66 45 0f ef d4 f3
    0280: 44 0f 6f a2 c0 00 00 00 45 0f 28 ec 66 44 0f 3a
    0290: 44 e6 00 66 45 0f ef c4 66 44 0f 3a 44 ee 11 66
    02a0: 45 0f ef cd 66 44 0f 70 e6 4e 66 41 0f ef f4 f3
    02b0: 44 0f 6f a2 d0 00 00 00 66 44 0f 3a 44 e6 00 66
    02c0: 45 0f ef d4 f3 44 0f 6f a2 e0 00 00 00 45 0f 28
    02d0: ec 66 44 0f 3a 44 e7 00 66 45 0f ef c4 66 44 0f
    02e0: 3a 44 ef 11 66 45 0f ef cd 66 44 0f 70 e7 4e 66
    02f0: 41 0f ef fc f3 44 0f 6f a2 f0 00 00 00 66 44 0f
    0300: 3a 44 e7 00 66 45 0f ef d4 66 45 0f ef d0 66 45
    0310: 0f ef d1 45 0f 28 da 66 41 0f 73 da 08 66 41 0f
    0320: 73 fb 08 66 45 0f ef ca 66 45 0f ef c3 45 0f 28
    0330: de 66 45 0f 3a 44 d8 01 66 45 0f 70 c0 4e 66 45
    0340: 0f ef c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45
    0350: 0f 70 c0 4e 66 45 0f ef c3 66 45 0f ef c1 e9 27
    0360: fd ff ff 41 0f 28 c8 f3 0f 6f b5 60 ff ff ff f3
    0370: 0f 6f bd 70 ff ff ff f3 44 0f 6f 45 80 f3 44 0f
    0380: 6f 4d 90 f3 44 0f 6f 55 a0 f3 44 0f 6f 5d b0 f3
    0390: 44 0f 6f 65 c0 f3 44 0f 6f 6d d0 f3 44 0f 6f 75
    03a0: e0 f3 44 0f 6f 7d f0 48 81 f9 10 00 00 00 0f 82
    03b0: 9c 00 00 00 48 81 e9 10 00 00 00 f3 41 0f 6f 01
    03c0: f3 0f 6f 28 66 0f 38 00 c5 66 0f ef c1 f3 0f 6f
    03d0: 8a e0 00 00 00 0f 28 d1 f3 0f 6f 9a f0 00 00 00
    03e0: 66 0f 3a 44 c8 00 66 0f 3a 44 d0 11 66 0f 70 e0
    03f0: 4e 66 0f ef e0 66 0f 3a 44 dc 00 66 0f ef d9 66
    0400: 0f ef da 0f 28 e3 66 0f 73 db 08 66 0f 73 fc 08
    0410: 66 0f ef d3 66 0f ef cc f3 0f 6f 60 10 66 0f 3a
    0420: 44 e1 01 66 0f 70 c9 4e 66 0f ef cc f3 0f 6f 60
    0430: 10 66 0f 3a 44 e1 01 66 0f 70 c9 4e 66 0f ef cc
    0440: 66 0f ef ca 49 81 c1 10 00 00 00 e9 57 ff ff ff
    0450: 48 85 c9 0f 84 39 01 00 00 48 8d b5 50 ff ff ff
    0460: 49 89 ca 49 81 e2 08 00 00 00 0f 84 14 00 00 00
    0470: 4d 8b 11 4c 89 16 49 81 c1 08 00 00 00 48 81 c6
    0480: 08 00 00 00 49 89 ca 49 81 e2 04 00 00 00 0f 84
    0490: 14 00 00 00 45 8b 11 44 89 16 49 81 c1 04 00 00
    04a0: 00 48 81 c6 04 00 00 00 49 89 ca 49 81 e2 02 00
    04b0: 00 00 0f 84 16 00 00 00 4d 0f b7 11 66 44 89 16
    04c0: 49 81 c1 02 00 00 00 48 81 c6 02 00 00 00 49 89
    04d0: ca 49 81 e2 01 00 00 00 0f 84 15 00 00 00 4d 0f
    04e0: b6 11 44 88 16 49 81 c1 01 00 00 00 48 81 c6 01
    04f0: 00 00 00 f3 0f 6f 85 50 ff ff ff 48 8d 35 a0 00
    0500: 00 00 48 29 ce f3 0f 6f 6e 30 66 0f db c5 f3 0f
    0510: 6f 28 66 0f 38 00 c5 66 0f ef c1 f3 0f 6f 8a e0
    0520: 00 00 00 0f 28 d1 f3 0f 6f 9a f0 00 00 00 66 0f
    0530: 3a 44 c8 00 66 0f 3a 44 d0 11 66 0f 70 e0 4e 66
    0540: 0f ef e0 66 0f 3a 44 dc 00 66 0f ef d9 66 0f ef
    0550: da 0f 28 e3 66 0f 73 db 08 66 0f 73 fc 08 66 0f
    0560: ef d3 66 0f ef cc f3 0f 6f 60 10 66 0f 3a 44 e1
    0570: 01 66 0f 70 c9 4e 66 0f ef cc f3 0f 6f 60 10 66
    0580: 0f 3a 44 e1 01 66 0f 70 c9 4e 66 0f ef cc 66 0f
    0590: ef ca f3 41 0f 7f 08 31 c0 48 81 c4 d0 00 00 00
    05a0: 5d c3 0f 0e 0d 0c 0b 0a 09 08 07 06 05 04 03 02
    05b0: 01 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00
    05c0: 00 c2 ff ff ff ff ff ff ff ff ff ff ff ff ff ff
    05d0: ff ff 00 00 00 00 00 00 00 00 00 00 00 00 00 00
    05e0: 00 00 00
}

chunk @mrt_aes_gcm_encrypt {
    0000: 55 48 89 e5 48 81 ec e0 01 00 00 49 89 ca 49 c1
    0010: ea 08 49 89 cb 49 81 e3 ff 00 00 00 48 8d 0d 6b
    0020: 16 00 00 f3 41 0f 6f 08 f3 41 0f 6f 40 10 41 8b
    0030: 40 1c f3 0f 6f a2 00 01 00 00 8b ba 0c 01 00 00
    0040: 0f c8 0f cf 49 8d 72 0f 48 c1 ee 04 48 01 c6 0f
    0050: ce 41 89 70 1c 66 0f ef e0 f3 0f 7f a5 c8 fe ff
    0060: ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    0070: d4 fe ff ff 4c 89 85 58 ff ff ff 49 8b 70 30 4d
    0080: 89 c8 49 29 f0 49 89 f1 49 81 fa 80 00 00 00 0f
    0090: 82 f5 10 00 00 f3 0f 7f b5 60 ff ff ff f3 0f 7f
    00a0: bd 70 ff ff ff f3 44 0f 7f 45 80 f3 44 0f 7f 4d
    00b0: 90 f3 44 0f 7f 55 a0 f3 44 0f 7f 5d b0 f3 44 0f
    00c0: 7f 65 c0 f3 44 0f 7f 6d d0 f3 44 0f 7f 75 e0 f3
    00d0: 44 0f 7f 7d f0 f3 44 0f 6f 39 f3 44 0f 6f 71 10
    00e0: 44 0f 28 c1 44 0f 28 dc 66 45 0f ef c9 66 45 0f
    00f0: ef d2 49 81 ea 80 00 00 00 f3 44 0f 7f 9d d8 fe
    0100: ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0110: b5 e4 fe ff ff f3 44 0f 7f 9d e8 fe ff ff 48 81
    0120: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 f4 fe ff
    0130: ff f3 44 0f 7f 9d f8 fe ff ff 48 81 c0 01 00 00
    0140: 00 89 c6 31 fe 0f ce 89 b5 04 ff ff ff f3 44 0f
    0150: 7f 9d 08 ff ff ff 48 81 c0 01 00 00 00 89 c6 31
    0160: fe 0f ce 89 b5 14 ff ff ff f3 44 0f 7f 9d 18 ff
    0170: ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0180: b5 24 ff ff ff f3 44 0f 7f 9d 28 ff ff ff 48 81
    0190: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 34 ff ff
    01a0: ff f3 44 0f 7f 9d 38 ff ff ff 48 81 c0 01 00 00
    01b0: 00 89 c6 31 fe 0f ce 89 b5 44 ff ff ff f3 0f 6f
    01c0: 85 c8 fe ff ff f3 0f 6f 8d d8 fe ff ff f3 0f 6f
    01d0: 95 e8 fe ff ff f3 0f 6f 9d f8 fe ff ff f3 0f 6f
    01e0: a5 08 ff ff ff f3 0f 6f ad 18 ff ff ff f3 0f 6f
    01f0: b5 28 ff ff ff f3 0f 6f bd 38 ff ff ff f3 44 0f
    0200: 6f 9a 10 01 00 00 66 41 0f 38 dc c3 66 41 0f 38
    0210: dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41
    0220: 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3
    0230: 66 41 0f 38 dc fb 48 81 c0 01 00 00 00 89 c6 31
    0240: fe 0f ce 89 b5 d4 fe ff ff f3 44 0f 6f 9a 20 01
    0250: 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41
    0260: 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3
    0270: 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38
    0280: dc fb 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0290: b5 e4 fe ff ff f3 44 0f 6f 9a 30 01 00 00 66 41
    02a0: 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3
    02b0: 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38
    02c0: dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 48 81
    02d0: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 f4 fe ff
    02e0: ff f3 44 0f 6f 9a 40 01 00 00 66 41 0f 38 dc c3
    02f0: 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38
    0300: dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41
    0310: 0f 38 dc f3 66 41 0f 38 dc fb 48 81 c0 01 00 00
    0320: 00 89 c6 31 fe 0f ce 89 b5 04 ff ff ff f3 44 0f
    0330: 6f 9a 50 01 00 00 66 41 0f 38 dc c3 66 41 0f 38
    0340: dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41
    0350: 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3
    0360: 66 41 0f 38 dc fb 48 81 c0 01 00 00 00 89 c6 31
    0370: fe 0f ce 89 b5 14 ff ff ff f3 44 0f 6f 9a 60 01
    0380: 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41
    0390: 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3
    03a0: 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38
    03b0: dc fb 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    03c0: b5 24 ff ff ff f3 44 0f 6f 9a 70 01 00 00 66 41
    03d0: 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3
    03e0: 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38
    03f0: dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 48 81
    0400: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 34 ff ff
    0410: ff f3 44 0f 6f 9a 80 01 00 00 66 41 0f 38 dc c3
    0420: 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38
    0430: dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41
    0440: 0f 38 dc f3 66 41 0f 38 dc fb 48 81 c0 01 00 00
    0450: 00 89 c6 31 fe 0f ce 89 b5 44 ff ff ff f3 44 0f
    0460: 6f 9a 90 01 00 00 66 41 0f 38 dc c3 66 41 0f 38
    0470: dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41
    0480: 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3
    0490: 66 41 0f 38 dc fb f3 44 0f 6f 9a a0 01 00 00 49
    04a0: 81 fb 0c 00 00 00 0f 82 ea 00 00 00 66 41 0f 38
    04b0: dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41
    04c0: 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb
    04d0: 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f
    04e0: 9a b0 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc
    04f0: cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f
    0500: 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66
    0510: 41 0f 38 dc fb f3 44 0f 6f 9a c0 01 00 00 0f 84
    0520: 72 00 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb
    0530: 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38
    0540: dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41
    0550: 0f 38 dc fb f3 44 0f 6f 9a d0 01 00 00 66 41 0f
    0560: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    0570: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    0580: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f
    0590: 6f 9a e0 01 00 00 66 41 0f 38 dd c3 66 41 0f 38
    05a0: dd cb 66 41 0f 38 dd d3 66 41 0f 38 dd db 66 41
    05b0: 0f 38 dd e3 66 41 0f 38 dd eb 66 41 0f 38 dd f3
    05c0: 66 41 0f 38 dd fb f3 45 0f 6f 19 66 41 0f ef c3
    05d0: f3 45 0f 6f 59 10 66 41 0f ef cb f3 45 0f 6f 59
    05e0: 20 66 41 0f ef d3 f3 45 0f 6f 59 30 66 41 0f ef
    05f0: db f3 45 0f 6f 59 40 66 41 0f ef e3 f3 45 0f 6f
    0600: 59 50 66 41 0f ef eb f3 45 0f 6f 59 60 66 41 0f
    0610: ef f3 f3 45 0f 6f 59 70 66 41 0f ef fb f3 43 0f
    0620: 7f 04 01 66 41 0f 38 00 c7 66 41 0f ef c0 f3 43
    0630: 0f 7f 4c 01 10 66 41 0f 38 00 cf f3 43 0f 7f 54
    0640: 01 20 66 41 0f 38 00 d7 f3 43 0f 7f 5c 01 30 66
    0650: 41 0f 38 00 df f3 43 0f 7f 64 01 40 66 41 0f 38
    0660: 00 e7 f3 43 0f 7f 6c 01 50 66 41 0f 38 00 ef f3
    0670: 43 0f 7f 74 01 60 66 41 0f 38 00 f7 f3 43 0f 7f
    0680: 7c 01 70 66 41 0f 38 00 ff f3 0f 7f 85 48 fe ff
    0690: ff f3 0f 7f 8d 58 fe ff ff f3 0f 7f 95 68 fe ff
    06a0: ff f3 0f 7f 9d 78 fe ff ff f3 0f 7f a5 88 fe ff
    06b0: ff f3 0f 7f ad 98 fe ff ff f3 0f 7f b5 a8 fe ff
    06c0: ff f3 0f 7f bd b8 fe ff ff 49 81 c1 80 00 00 00
    06d0: 49 81 fa 80 00 00 00 0f 82 bc 07 00 00 49 81 ea
    06e0: 80 00 00 00 f3 0f 6f 85 c8 fe ff ff f3 0f 6f 8d
    06f0: d8 fe ff ff f3 0f 6f 95 e8 fe ff ff f3 0f 6f 9d
    0700: f8 fe ff ff f3 0f 6f a5 08 ff ff ff f3 0f 6f ad
    0710: 18 ff ff ff f3 0f 6f b5 28 ff ff ff f3 0f 6f bd
    0720: 38 ff ff ff f3 44 0f 6f 9d 48 fe ff ff f3 44 0f
    0730: 6f 02 f3 44 0f 6f 52 10 45 0f 28 c8 66 45 0f 70
    0740: e3 4e 66 45 0f ef e3 66 45 0f 3a 44 d4 00 66 45
    0750: 0f 3a 44 c3 00 66 45 0f 3a 44 cb 11 f3 44 0f 6f
    0760: 9a 10 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc
    0770: cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f
    0780: 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66
    0790: 41 0f 38 dc fb f3 44 0f 6f 9d 58 fe ff ff f3 44
    07a0: 0f 6f 62 20 45 0f 28 ec 66 45 0f 3a 44 e3 00 66
    07b0: 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd
    07c0: 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f 62
    07d0: 30 66 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81 c0
    07e0: 01 00 00 00 89 c6 31 fe 0f ce 89 b5 d4 fe ff ff
    07f0: f3 44 0f 6f 9a 20 01 00 00 66 41 0f 38 dc c3 66
    0800: 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc
    0810: db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f
    0820: 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9d 68 fe
    0830: ff ff f3 44 0f 6f 62 40 45 0f 28 ec 66 45 0f 3a
    0840: 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66
    0850: 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3
    0860: 44 0f 6f 62 50 66 45 0f 3a 44 e3 00 66 45 0f ef
    0870: d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    0880: e4 fe ff ff f3 44 0f 6f 9a 30 01 00 00 66 41 0f
    0890: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    08a0: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    08b0: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f
    08c0: 6f 9d 78 fe ff ff f3 44 0f 6f 62 60 45 0f 28 ec
    08d0: 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a
    08e0: 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45
    08f0: 0f ef dc f3 44 0f 6f 62 70 66 45 0f 3a 44 e3 00
    0900: 66 45 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe
    0910: 0f ce 89 b5 f4 fe ff ff f3 44 0f 6f 9a 40 01 00
    0920: 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f
    0930: 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66
    0940: 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc
    0950: fb f3 44 0f 6f 9d 88 fe ff ff f3 44 0f 6f a2 80
    0960: 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45
    0970: 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66
    0980: 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 90
    0990: 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 48
    09a0: 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 04 ff
    09b0: ff ff f3 44 0f 6f 9a 50 01 00 00 66 41 0f 38 dc
    09c0: c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f
    09d0: 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66
    09e0: 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9d
    09f0: 98 fe ff ff f3 44 0f 6f a2 a0 00 00 00 45 0f 28
    0a00: ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f
    0a10: 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66
    0a20: 45 0f ef dc f3 44 0f 6f a2 b0 00 00 00 66 45 0f
    0a30: 3a 44 e3 00 66 45 0f ef d4 48 81 c0 01 00 00 00
    0a40: 89 c6 31 fe 0f ce 89 b5 14 ff ff ff f3 44 0f 6f
    0a50: 9a 60 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc
    0a60: cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f
    0a70: 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66
    0a80: 41 0f 38 dc fb f3 44 0f 6f 9d a8 fe ff ff f3 44
    0a90: 0f 6f a2 c0 00 00 00 45 0f 28 ec 66 45 0f 3a 44
    0aa0: e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45
    0ab0: 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44
    0ac0: 0f 6f a2 d0 00 00 00 66 45 0f 3a 44 e3 00 66 45
    0ad0: 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce
    0ae0: 89 b5 24 ff ff ff f3 44 0f 6f 9a 70 01 00 00 66
    0af0: 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc
    0b00: d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f
    0b10: 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3
    0b20: 44 0f 6f 9d b8 fe ff ff f3 44 0f 6f a2 e0 00 00
    0b30: 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef
    0b40: c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f
    0b50: 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 f0 00 00
    0b60: 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81 c0
    0b70: 01 00 00 00 89 c6 31 fe 0f ce 89 b5 34 ff ff ff
    0b80: f3 44 0f 6f 9a 80 01 00 00 66 41 0f 38 dc c3 66
    0b90: 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc
    0ba0: db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f
    0bb0: 38 dc f3 66 41 0f 38 dc fb 48 81 c0 01 00 00 00
    0bc0: 89 c6 31 fe 0f ce 89 b5 44 ff ff ff 66 45 0f ef
    0bd0: d0 66 45 0f ef d1 45 0f 28 da 66 41 0f 73 da 08
    0be0: 66 41 0f 73 fb 08 66 45 0f ef ca 66 45 0f ef c3
    0bf0: 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45 0f 70 c0
    0c00: 4e 66 45 0f ef c3 f3 44 0f 6f 9a 90 01 00 00 66
    0c10: 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc
    0c20: d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f
    0c30: 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 45
    0c40: 0f 28 de 66 45 0f 3a 44 d8 01 66 45 0f 70 c0 4e
    0c50: 66 45 0f ef c3 66 45 0f ef c1 f3 44 0f 6f 9a a0
    0c60: 01 00 00 49 81 fb 0c 00 00 00 0f 82 ea 00 00 00
    0c70: 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38
    0c80: dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41
    0c90: 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb
    0ca0: f3 44 0f 6f 9a b0 01 00 00 66 41 0f 38 dc c3 66
    0cb0: 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc
    0cc0: db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f
    0cd0: 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9a c0 01
    0ce0: 00 00 0f 84 72 00 00 00 66 41 0f 38 dc c3 66 41
    0cf0: 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db
    0d00: 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38
    0d10: dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9a d0 01 00
    0d20: 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f
    0d30: 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66
    0d40: 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc
    0d50: fb f3 44 0f 6f 9a e0 01 00 00 66 41 0f 38 dd c3
    0d60: 66 41 0f 38 dd cb 66 41 0f 38 dd d3 66 41 0f 38
    0d70: dd db 66 41 0f 38 dd e3 66 41 0f 38 dd eb 66 41
    0d80: 0f 38 dd f3 66 41 0f 38 dd fb f3 45 0f 6f 19 66
    0d90: 41 0f ef c3 f3 45 0f 6f 59 10 66 41 0f ef cb f3
    0da0: 45 0f 6f 59 20 66 41 0f ef d3 f3 45 0f 6f 59 30
    0db0: 66 41 0f ef db f3 45 0f 6f 59 40 66 41 0f ef e3
    0dc0: f3 45 0f 6f 59 50 66 41 0f ef eb f3 45 0f 6f 59
    0dd0: 60 66 41 0f ef f3 f3 45 0f 6f 59 70 66 41 0f ef
    0de0: fb f3 43 0f 7f 04 01 66 41 0f 38 00 c7 66 41 0f
    0df0: ef c0 f3 43 0f 7f 4c 01 10 66 41 0f 38 00 cf f3
    0e00: 43 0f 7f 54 01 20 66 41 0f 38 00 d7 f3 43 0f 7f
    0e10: 5c 01 30 66 41 0f 38 00 df f3 43 0f 7f 64 01 40
    0e20: 66 41 0f 38 00 e7 f3 43 0f 7f 6c 01 50 66 41 0f
    0e30: 38 00 ef f3 43 0f 7f 74 01 60 66 41 0f 38 00 f7
    0e40: f3 43 0f 7f 7c 01 70 66 41 0f 38 00 ff f3 0f 7f
    0e50: 85 48 fe ff ff f3 0f 7f 8d 58 fe ff ff f3 0f 7f
    0e60: 95 68 fe ff ff f3 0f 7f 9d 78 fe ff ff f3 0f 7f
    0e70: a5 88 fe ff ff f3 0f 7f ad 98 fe ff ff f3 0f 7f
    0e80: b5 a8 fe ff ff f3 0f 7f bd b8 fe ff ff 49 81 c1
    0e90: 80 00 00 00 e9 37 f8 ff ff f3 44 0f 6f 9d 48 fe
    0ea0: ff ff f3 44 0f 6f 02 f3 44 0f 6f 52 10 45 0f 28
    0eb0: c8 66 45 0f 70 e3 4e 66 45 0f ef e3 66 45 0f 3a
    0ec0: 44 d4 00 66 45 0f 3a 44 c3 00 66 45 0f 3a 44 cb
    0ed0: 11 f3 44 0f 6f 9d 58 fe ff ff f3 44 0f 6f 62 20
    0ee0: 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4
    0ef0: 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70
    0f00: e3 4e 66 45 0f ef dc f3 44 0f 6f 62 30 66 45 0f
    0f10: 3a 44 e3 00 66 45 0f ef d4 f3 44 0f 6f 9d 68 fe
    0f20: ff ff f3 44 0f 6f 62 40 45 0f 28 ec 66 45 0f 3a
    0f30: 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66
    0f40: 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3
    0f50: 44 0f 6f 62 50 66 45 0f 3a 44 e3 00 66 45 0f ef
    0f60: d4 f3 44 0f 6f 9d 78 fe ff ff f3 44 0f 6f 62 60
    0f70: 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4
    0f80: 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70
    0f90: e3 4e 66 45 0f ef dc f3 44 0f 6f 62 70 66 45 0f
    0fa0: 3a 44 e3 00 66 45 0f ef d4 f3 44 0f 6f 9d 88 fe
    0fb0: ff ff f3 44 0f 6f a2 80 00 00 00 45 0f 28 ec 66
    0fc0: 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44
    0fd0: eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f
    0fe0: ef dc f3 44 0f 6f a2 90 00 00 00 66 45 0f 3a 44
    0ff0: e3 00 66 45 0f ef d4 f3 44 0f 6f 9d 98 fe ff ff
    1000: f3 44 0f 6f a2 a0 00 00 00 45 0f 28 ec 66 45 0f
    1010: 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11
    1020: 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc
    1030: f3 44 0f 6f a2 b0 00 00 00 66 45 0f 3a 44 e3 00
    1040: 66 45 0f ef d4 f3 44 0f 6f 9d a8 fe ff ff f3 44
    1050: 0f 6f a2 c0 00 00 00 45 0f 28 ec 66 45 0f 3a 44
    1060: e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45
    1070: 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44
    1080: 0f 6f a2 d0 00 00 00 66 45 0f 3a 44 e3 00 66 45
    1090: 0f ef d4 f3 44 0f 6f 9d b8 fe ff ff f3 44 0f 6f
    10a0: a2 e0 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00
    10b0: 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef
    10c0: cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f
    10d0: a2 f0 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef
    10e0: d4 66 45 0f ef d0 66 45 0f ef d1 45 0f 28 da 66
    10f0: 41 0f 73 da 08 66 41 0f 73 fb 08 66 45 0f ef ca
    1100: 66 45 0f ef c3 45 0f 28 de 66 45 0f 3a 44 d8 01
    1110: 66 45 0f 70 c0 4e 66 45 0f ef c3 45 0f 28 de 66
    1120: 45 0f 3a 44 d8 01 66 45 0f 70 c0 4e 66 45 0f ef
    1130: c3 66 45 0f ef c1 41 0f 28 c8 f3 0f 6f b5 60 ff
    1140: ff ff f3 0f 6f bd 70 ff ff ff f3 44 0f 6f 45 80
    1150: f3 44 0f 6f 4d 90 f3 44 0f 6f 55 a0 f3 44 0f 6f
    1160: 5d b0 f3 44 0f 6f 65 c0 f3 44 0f 6f 6d d0 f3 44
    1170: 0f 6f 75 e0 f3 44 0f 6f 7d f0 4d 85 d2 0f 84 f4
    1180: 04 00 00 48 81 e8 07 00 00 00 49 81 fa 10 00 00
    1190: 00 0f 82 0a 02 00 00 31 c9 49 81 ea 10 00 00 00
    11a0: f3 0f 6f 85 c8 fe ff ff 48 81 c0 01 00 00 00 89
    11b0: c6 31 fe 0f ce 89 b5 d4 fe ff ff f3 0f 6f a2 10
    11c0: 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 20 01 00 00
    11d0: 66 0f 38 dc c4 f3 0f 6f a2 30 01 00 00 66 0f 38
    11e0: dc c4 f3 0f 6f a2 40 01 00 00 66 0f 38 dc c4 f3
    11f0: 0f 6f a2 50 01 00 00 66 0f 38 dc c4 f3 0f 6f a2
    1200: 60 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 70 01 00
    1210: 00 66 0f 38 dc c4 f3 0f 6f a2 80 01 00 00 66 0f
    1220: 38 dc c4 f3 0f 6f a2 90 01 00 00 66 0f 38 dc c4
    1230: f3 0f 6f a2 a0 01 00 00 49 81 fb 0c 00 00 00 0f
    1240: 82 3a 00 00 00 66 0f 38 dc c4 f3 0f 6f a2 b0 01
    1250: 00 00 66 0f 38 dc c4 f3 0f 6f a2 c0 01 00 00 0f
    1260: 84 1a 00 00 00 66 0f 38 dc c4 f3 0f 6f a2 d0 01
    1270: 00 00 66 0f 38 dc c4 f3 0f 6f a2 e0 01 00 00 66
    1280: 0f 38 dd c4 f3 41 0f 6f 29 66 0f ef c5 f3 43 0f
    1290: 7f 04 01 48 8d 35 f4 03 00 00 f3 0f 6f 2e 66 0f
    12a0: 38 00 c5 f3 0f 7f 84 0d 48 fe ff ff 49 81 c1 10
    12b0: 00 00 00 48 81 c1 10 00 00 00 49 81 fa 10 00 00
    12c0: 00 0f 83 d2 fe ff ff 48 01 c9 31 f6 48 81 c6 00
    12d0: 01 00 00 48 29 ce 31 c9 f3 0f 6f 85 48 fe ff ff
    12e0: 66 0f ef c1 f3 0f 7f 85 48 fe ff ff 66 0f ef c9
    12f0: 66 0f ef d2 66 0f ef db f3 0f 6f 84 0d 48 fe ff
    1300: ff f3 0f 6f 2c 32 0f 28 e5 66 0f 3a 44 e0 00 66
    1310: 0f ef cc 66 0f 3a 44 e8 11 66 0f ef d5 66 0f 70
    1320: e0 4e 66 0f ef e0 f3 0f 6f 6c 32 10 66 0f 3a 44
    1330: ec 00 66 0f ef dd 48 81 c1 10 00 00 00 48 81 c6
    1340: 20 00 00 00 48 81 fe 00 01 00 00 0f 82 a7 ff ff
    1350: ff 48 8d 0d 36 03 00 00 66 0f ef d9 66 0f ef da
    1360: 0f 28 e3 66 0f 73 db 08 66 0f 73 fc 08 66 0f ef
    1370: d3 66 0f ef cc f3 0f 6f 61 10 66 0f 3a 44 e1 01
    1380: 66 0f 70 c9 4e 66 0f ef cc f3 0f 6f 61 10 66 0f
    1390: 3a 44 e1 01 66 0f 70 c9 4e 66 0f ef cc 66 0f ef
    13a0: ca 4d 85 d2 0f 84 cd 02 00 00 48 8d 8d 48 ff ff
    13b0: ff 4c 89 d6 48 81 e6 08 00 00 00 0f 84 14 00 00
    13c0: 00 49 8b 31 48 89 31 49 81 c1 08 00 00 00 48 81
    13d0: c1 08 00 00 00 4c 89 d6 48 81 e6 04 00 00 00 0f
    13e0: 84 13 00 00 00 41 8b 31 89 31 49 81 c1 04 00 00
    13f0: 00 48 81 c1 04 00 00 00 4c 89 d6 48 81 e6 02 00
    1400: 00 00 0f 84 15 00 00 00 49 0f b7 31 66 89 31 49
    1410: 81 c1 02 00 00 00 48 81 c1 02 00 00 00 4c 89 d6
    1420: 48 81 e6 01 00 00 00 0f 84 15 00 00 00 49 0f b6
    1430: 31 40 88 31 49 81 c1 01 00 00 00 48 81 c1 01 00
    1440: 00 00 f3 0f 6f ad 48 ff ff ff 4d 29 d1 f3 0f 6f
    1450: 85 c8 fe ff ff 48 81 c0 01 00 00 00 89 c6 31 fe
    1460: 0f ce 89 b5 d4 fe ff ff f3 0f 6f a2 10 01 00 00
    1470: 66 0f 38 dc c4 f3 0f 6f a2 20 01 00 00 66 0f 38
    1480: dc c4 f3 0f 6f a2 30 01 00 00 66 0f 38 dc c4 f3
    1490: 0f 6f a2 40 01 00 00 66 0f 38 dc c4 f3 0f 6f a2
    14a0: 50 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 60 01 00
    14b0: 00 66 0f 38 dc c4 f3 0f 6f a2 70 01 00 00 66 0f
    14c0: 38 dc c4 f3 0f 6f a2 80 01 00 00 66 0f 38 dc c4
    14d0: f3 0f 6f a2 90 01 00 00 66 0f 38 dc c4 f3 0f 6f
    14e0: a2 a0 01 00 00 49 81 fb 0c 00 00 00 0f 82 3a 00
    14f0: 00 00 66 0f 38 dc c4 f3 0f 6f a2 b0 01 00 00 66
    1500: 0f 38 dc c4 f3 0f 6f a2 c0 01 00 00 0f 84 1a 00
    1510: 00 00 66 0f 38 dc c4 f3 0f 6f a2 d0 01 00 00 66
    1520: 0f 38 dc c4 f3 0f 6f a2 e0 01 00 00 66 0f 38 dd
    1530: c4 66 0f ef c5 4d 01 c1 f3 0f 7f 85 48 ff ff ff
    1540: 48 8d 8d 48 ff ff ff 4c 89 d6 48 81 e6 08 00 00
    1550: 00 0f 84 14 00 00 00 48 8b 31 49 89 31 48 81 c1
    1560: 08 00 00 00 49 81 c1 08 00 00 00 4c 89 d6 48 81
    1570: e6 04 00 00 00 0f 84 13 00 00 00 8b 31 41 89 31
    1580: 48 81 c1 04 00 00 00 49 81 c1 04 00 00 00 4c 89
    1590: d6 48 81 e6 02 00 00 00 0f 84 16 00 00 00 48 0f
    15a0: b7 31 66 41 89 31 48 81 c1 02 00 00 00 49 81 c1
    15b0: 02 00 00 00 4c 89 d6 48 81 e6 01 00 00 00 0f 84
    15c0: 15 00 00 00 48 0f b6 31 41 88 31 48 81 c1 01 00
    15d0: 00 00 49 81 c1 01 00 00 00 48 8d 35 ae 00 00 00
    15e0: 4c 29 d6 f3 0f 6f 6e 30 66 0f db c5 48 8d 0d 9b
    15f0: 00 00 00 f3 0f 6f 29 66 0f 38 00 c5 66 0f ef c1
    1600: f3 0f 6f 8a e0 00 00 00 0f 28 d1 f3 0f 6f 9a f0
    1610: 00 00 00 66 0f 3a 44 c8 00 66 0f 3a 44 d0 11 66
    1620: 0f 70 e0 4e 66 0f ef e0 66 0f 3a 44 dc 00 66 0f
    1630: ef d9 66 0f ef da 0f 28 e3 66 0f 73 db 08 66 0f
    1640: 73 fc 08 66 0f ef d3 66 0f ef cc f3 0f 6f 61 10
    1650: 66 0f 3a 44 e1 01 66 0f 70 c9 4e 66 0f ef cc f3
    1660: 0f 6f 61 10 66 0f 3a 44 e1 01 66 0f 70 c9 4e 66
    1670: 0f ef cc 66 0f ef ca 4c 8b 85 58 ff ff ff f3 41
    1680: 0f 7f 08 31 c0 48 81 c4 e0 01 00 00 5d c3 0f 0e
    1690: 0d 0c 0b 0a 09 08 07 06 05 04 03 02 01 00 01 00
    16a0: 00 00 00 00 00 00 00 00 00 00 00 00 00 c2 ff ff
    16b0: ff ff ff ff ff ff ff ff ff ff ff ff ff ff 00 00
    16c0: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
}

chunk @mrt_aes_gcm_decrypt {
    0000: 55 48 89 e5 48 81 ec d0 01 00 00 49 89 ca 49 c1
    0010: ea 08 49 89 cb 49 81 e3 ff 00 00 00 48 8d 0d 5f
    0020: 0e 00 00 f3 41 0f 6f 08 f3 41 0f 6f 40 10 41 8b
    0030: 40 1c f3 0f 6f a2 00 01 00 00 8b ba 0c 01 00 00
    0040: 0f c8 0f cf 49 8d 72 0f 48 c1 ee 04 48 01 c6 0f
    0050: ce 41 89 70 1c 66 0f ef e0 f3 0f 7f a5 58 fe ff
    0060: ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    0070: 64 fe ff ff 4c 89 85 58 ff ff ff 49 8b 70 30 4d
    0080: 89 c8 49 29 f0 49 89 f1 49 81 fa 80 00 00 00 0f
    0090: 82 dc 08 00 00 f3 0f 7f b5 60 ff ff ff f3 0f 7f
    00a0: bd 70 ff ff ff f3 44 0f 7f 45 80 f3 44 0f 7f 4d
    00b0: 90 f3 44 0f 7f 55 a0 f3 44 0f 7f 5d b0 f3 44 0f
    00c0: 7f 65 c0 f3 44 0f 7f 6d d0 f3 44 0f 7f 75 e0 f3
    00d0: 44 0f 7f 7d f0 f3 44 0f 6f 39 f3 44 0f 6f 71 10
    00e0: 44 0f 28 c1 44 0f 28 dc 66 45 0f ef c9 66 45 0f
    00f0: ef d2 f3 44 0f 7f 9d 68 fe ff ff 48 81 c0 01 00
    0100: 00 00 89 c6 31 fe 0f ce 89 b5 74 fe ff ff f3 44
    0110: 0f 7f 9d 78 fe ff ff 48 81 c0 01 00 00 00 89 c6
    0120: 31 fe 0f ce 89 b5 84 fe ff ff f3 44 0f 7f 9d 88
    0130: fe ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce
    0140: 89 b5 94 fe ff ff f3 44 0f 7f 9d 98 fe ff ff 48
    0150: 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 a4 fe
    0160: ff ff f3 44 0f 7f 9d a8 fe ff ff 48 81 c0 01 00
    0170: 00 00 89 c6 31 fe 0f ce 89 b5 b4 fe ff ff f3 44
    0180: 0f 7f 9d b8 fe ff ff 48 81 c0 01 00 00 00 89 c6
    0190: 31 fe 0f ce 89 b5 c4 fe ff ff f3 44 0f 7f 9d c8
    01a0: fe ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce
    01b0: 89 b5 d4 fe ff ff 49 81 fa 80 00 00 00 0f 82 63
    01c0: 07 00 00 49 81 ea 80 00 00 00 f3 0f 6f 85 58 fe
    01d0: ff ff f3 0f 6f 8d 68 fe ff ff f3 0f 6f 95 78 fe
    01e0: ff ff f3 0f 6f 9d 88 fe ff ff f3 0f 6f a5 98 fe
    01f0: ff ff f3 0f 6f ad a8 fe ff ff f3 0f 6f b5 b8 fe
    0200: ff ff f3 0f 6f bd c8 fe ff ff f3 45 0f 6f 19 66
    0210: 45 0f 38 00 df 66 45 0f ef d8 f3 44 0f 6f 02 f3
    0220: 44 0f 6f 52 10 45 0f 28 c8 66 45 0f 70 e3 4e 66
    0230: 45 0f ef e3 66 45 0f 3a 44 d4 00 66 45 0f 3a 44
    0240: c3 00 66 45 0f 3a 44 cb 11 f3 44 0f 6f 9a 10 01
    0250: 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41
    0260: 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3
    0270: 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38
    0280: dc fb f3 45 0f 6f 59 10 66 45 0f 38 00 df f3 44
    0290: 0f 6f 62 20 45 0f 28 ec 66 45 0f 3a 44 e3 00 66
    02a0: 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd
    02b0: 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f 62
    02c0: 30 66 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81 c0
    02d0: 01 00 00 00 89 c6 31 fe 0f ce 89 b5 64 fe ff ff
    02e0: f3 44 0f 6f 9a 20 01 00 00 66 41 0f 38 dc c3 66
    02f0: 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc
    0300: db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f
    0310: 38 dc f3 66 41 0f 38 dc fb f3 45 0f 6f 59 20 66
    0320: 45 0f 38 00 df f3 44 0f 6f 62 40 45 0f 28 ec 66
    0330: 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44
    0340: eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f
    0350: ef dc f3 44 0f 6f 62 50 66 45 0f 3a 44 e3 00 66
    0360: 45 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f
    0370: ce 89 b5 74 fe ff ff f3 44 0f 6f 9a 30 01 00 00
    0380: 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38
    0390: dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41
    03a0: 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb
    03b0: f3 45 0f 6f 59 30 66 45 0f 38 00 df f3 44 0f 6f
    03c0: 62 60 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f
    03d0: ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45
    03e0: 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f 62 70 66
    03f0: 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81 c0 01 00
    0400: 00 00 89 c6 31 fe 0f ce 89 b5 84 fe ff ff f3 44
    0410: 0f 6f 9a 40 01 00 00 66 41 0f 38 dc c3 66 41 0f
    0420: 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66
    0430: 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc
    0440: f3 66 41 0f 38 dc fb f3 45 0f 6f 59 40 66 45 0f
    0450: 38 00 df f3 44 0f 6f a2 80 00 00 00 45 0f 28 ec
    0460: 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a
    0470: 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45
    0480: 0f ef dc f3 44 0f 6f a2 90 00 00 00 66 45 0f 3a
    0490: 44 e3 00 66 45 0f ef d4 48 81 c0 01 00 00 00 89
    04a0: c6 31 fe 0f ce 89 b5 94 fe ff ff f3 44 0f 6f 9a
    04b0: 50 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb
    04c0: 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38
    04d0: dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41
    04e0: 0f 38 dc fb f3 45 0f 6f 59 50 66 45 0f 38 00 df
    04f0: f3 44 0f 6f a2 a0 00 00 00 45 0f 28 ec 66 45 0f
    0500: 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11
    0510: 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc
    0520: f3 44 0f 6f a2 b0 00 00 00 66 45 0f 3a 44 e3 00
    0530: 66 45 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe
    0540: 0f ce 89 b5 a4 fe ff ff f3 44 0f 6f 9a 60 01 00
    0550: 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f
    0560: 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66
    0570: 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc
    0580: fb f3 45 0f 6f 59 60 66 45 0f 38 00 df f3 44 0f
    0590: 6f a2 c0 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3
    05a0: 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f
    05b0: ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f
    05c0: 6f a2 d0 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f
    05d0: ef d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    05e0: b5 b4 fe ff ff f3 44 0f 6f 9a 70 01 00 00 66 41
    05f0: 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3
    0600: 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38
    0610: dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 45
    0620: 0f 6f 59 70 66 45 0f 38 00 df f3 44 0f 6f a2 e0
    0630: 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45
    0640: 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66
    0650: 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 f0
    0660: 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 48
    0670: 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 c4 fe
    0680: ff ff f3 44 0f 6f 9a 80 01 00 00 66 41 0f 38 dc
    0690: c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f
    06a0: 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66
    06b0: 41 0f 38 dc f3 66 41 0f 38 dc fb 48 81 c0 01 00
    06c0: 00 00 89 c6 31 fe 0f ce 89 b5 d4 fe ff ff 66 45
    06d0: 0f ef d0 66 45 0f ef d1 45 0f 28 da 66 41 0f 73
    06e0: da 08 66 41 0f 73 fb 08 66 45 0f ef ca 66 45 0f
    06f0: ef c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45 0f
    0700: 70 c0 4e 66 45 0f ef c3 f3 44 0f 6f 9a 90 01 00
    0710: 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f
    0720: 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66
    0730: 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc
    0740: fb 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45 0f 70
    0750: c0 4e 66 45 0f ef c3 66 45 0f ef c1 f3 44 0f 6f
    0760: 9a a0 01 00 00 49 81 fb 0c 00 00 00 0f 82 ea 00
    0770: 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41
    0780: 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3
    0790: 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38
    07a0: dc fb f3 44 0f 6f 9a b0 01 00 00 66 41 0f 38 dc
    07b0: c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f
    07c0: 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66
    07d0: 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9a
    07e0: c0 01 00 00 0f 84 72 00 00 00 66 41 0f 38 dc c3
    07f0: 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38
    0800: dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41
    0810: 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9a d0
    0820: 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66
    0830: 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc
    0840: e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f
    0850: 38 dc fb f3 44 0f 6f 9a e0 01 00 00 66 41 0f 38
    0860: dd c3 66 41 0f 38 dd cb 66 41 0f 38 dd d3 66 41
    0870: 0f 38 dd db 66 41 0f 38 dd e3 66 41 0f 38 dd eb
    0880: 66 41 0f 38 dd f3 66 41 0f 38 dd fb f3 45 0f 6f
    0890: 19 66 41 0f ef c3 f3 45 0f 6f 59 10 66 41 0f ef
    08a0: cb f3 45 0f 6f 59 20 66 41 0f ef d3 f3 45 0f 6f
    08b0: 59 30 66 41 0f ef db f3 45 0f 6f 59 40 66 41 0f
    08c0: ef e3 f3 45 0f 6f 59 50 66 41 0f ef eb f3 45 0f
    08d0: 6f 59 60 66 41 0f ef f3 f3 45 0f 6f 59 70 66 41
    08e0: 0f ef fb f3 43 0f 7f 04 01 f3 43 0f 7f 4c 01 10
    08f0: f3 43 0f 7f 54 01 20 f3 43 0f 7f 5c 01 30 f3 43
    0900: 0f 7f 64 01 40 f3 43 0f 7f 6c 01 50 f3 43 0f 7f
    0910: 74 01 60 f3 43 0f 7f 7c 01 70 49 81 c1 80 00 00
    0920: 00 e9 90 f8 ff ff 41 0f 28 c8 f3 0f 6f b5 60 ff
    0930: ff ff f3 0f 6f bd 70 ff ff ff f3 44 0f 6f 45 80
    0940: f3 44 0f 6f 4d 90 f3 44 0f 6f 55 a0 f3 44 0f 6f
    0950: 5d b0 f3 44 0f 6f 65 c0 f3 44 0f 6f 6d d0 f3 44
    0960: 0f 6f 75 e0 f3 44 0f 6f 7d f0 48 81 e8 07 00 00
    0970: 00 49 81 fa 10 00 00 00 0f 82 0f 02 00 00 31 c9
    0980: 49 81 ea 10 00 00 00 f3 41 0f 6f 01 48 8d 35 ef
    0990: 04 00 00 f3 0f 6f 2e 66 0f 38 00 c5 f3 0f 7f 84
    09a0: 0d e8 fe ff ff f3 0f 6f 85 58 fe ff ff 48 81 c0
    09b0: 01 00 00 00 89 c6 31 fe 0f ce 89 b5 64 fe ff ff
    09c0: f3 0f 6f a2 10 01 00 00 66 0f 38 dc c4 f3 0f 6f
    09d0: a2 20 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 30 01
    09e0: 00 00 66 0f 38 dc c4 f3 0f 6f a2 40 01 00 00 66
    09f0: 0f 38 dc c4 f3 0f 6f a2 50 01 00 00 66 0f 38 dc
    0a00: c4 f3 0f 6f a2 60 01 00 00 66 0f 38 dc c4 f3 0f
    0a10: 6f a2 70 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 80
    0a20: 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 90 01 00 00
    0a30: 66 0f 38 dc c4 f3 0f 6f a2 a0 01 00 00 49 81 fb
    0a40: 0c 00 00 00 0f 82 3a 00 00 00 66 0f 38 dc c4 f3
    0a50: 0f 6f a2 b0 01 00 00 66 0f 38 dc c4 f3 0f 6f a2
    0a60: c0 01 00 00 0f 84 1a 00 00 00 66 0f 38 dc c4 f3
    0a70: 0f 6f a2 d0 01 00 00 66 0f 38 dc c4 f3 0f 6f a2
    0a80: e0 01 00 00 66 0f 38 dd c4 f3 41 0f 6f 29 66 0f
    0a90: ef c5 f3 43 0f 7f 04 01 49 81 c1 10 00 00 00 48
    0aa0: 81 c1 10 00 00 00 49 81 fa 10 00 00 00 0f 83 cd
    0ab0: fe ff ff 48 01 c9 31 f6 48 81 c6 00 01 00 00 48
    0ac0: 29 ce 31 c9 f3 0f 6f 85 e8 fe ff ff 66 0f ef c1
    0ad0: f3 0f 7f 85 e8 fe ff ff 66 0f ef c9 66 0f ef d2
    0ae0: 66 0f ef db f3 0f 6f 84 0d e8 fe ff ff f3 0f 6f
    0af0: 2c 32 0f 28 e5 66 0f 3a 44 e0 00 66 0f ef cc 66
    0b00: 0f 3a 44 e8 11 66 0f ef d5 66 0f 70 e0 4e 66 0f
    0b10: ef e0 f3 0f 6f 6c 32 10 66 0f 3a 44 ec 00 66 0f
    0b20: ef dd 48 81 c1 10 00 00 00 48 81 c6 20 00 00 00
    0b30: 48 81 fe 00 01 00 00 0f 82 a7 ff ff ff 48 8d 0d
    0b40: 3e 03 00 00 66 0f ef d9 66 0f ef da 0f 28 e3 66
    0b50: 0f 73 db 08 66 0f 73 fc 08 66 0f ef d3 66 0f ef
    0b60: cc f3 0f 6f 61 10 66 0f 3a 44 e1 01 66 0f 70 c9
    0b70: 4e 66 0f ef cc f3 0f 6f 61 10 66 0f 3a 44 e1 01
    0b80: 66 0f 70 c9 4e 66 0f ef cc 66 0f ef ca 4d 85 d2
    0b90: 0f 84 d5 02 00 00 48 8d 8d d8 fe ff ff 4c 89 d6
    0ba0: 48 81 e6 08 00 00 00 0f 84 14 00 00 00 49 8b 31
    0bb0: 48 89 31 49 81 c1 08 00 00 00 48 81 c1 08 00 00
    0bc0: 00 4c 89 d6 48 81 e6 04 00 00 00 0f 84 13 00 00
    0bd0: 00 41 8b 31 89 31 49 81 c1 04 00 00 00 48 81 c1
    0be0: 04 00 00 00 4c 89 d6 48 81 e6 02 00 00 00 0f 84
    0bf0: 15 00 00 00 49 0f b7 31 66 89 31 49 81 c1 02 00
    0c00: 00 00 48 81 c1 02 00 00 00 4c 89 d6 48 81 e6 01
    0c10: 00 00 00 0f 84 15 00 00 00 49 0f b6 31 40 88 31
    0c20: 49 81 c1 01 00 00 00 48 81 c1 01 00 00 00 f3 0f
    0c30: 6f 85 d8 fe ff ff 4d 29 d1 48 8d 35 42 02 00 00
    0c40: 4c 29 d6 f3 0f 6f 6e 30 66 0f db c5 48 8d 0d 2f
    0c50: 02 00 00 f3 0f 6f 29 66 0f 38 00 c5 66 0f ef c1
    0c60: f3 0f 6f 8a e0 00 00 00 0f 28 d1 f3 0f 6f 9a f0
    0c70: 00 00 00 66 0f 3a 44 c8 00 66 0f 3a 44 d0 11 66
    0c80: 0f 70 e0 4e 66 0f ef e0 66 0f 3a 44 dc 00 66 0f
    0c90: ef d9 66 0f ef da 0f 28 e3 66 0f 73 db 08 66 0f
    0ca0: 73 fc 08 66 0f ef d3 66 0f ef cc f3 0f 6f 61 10
    0cb0: 66 0f 3a 44 e1 01 66 0f 70 c9 4e 66 0f ef cc f3
    0cc0: 0f 6f 61 10 66 0f 3a 44 e1 01 66 0f 70 c9 4e 66
    0cd0: 0f ef cc 66 0f ef ca f3 0f 6f 85 58 fe ff ff 48
    0ce0: 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 64 fe
    0cf0: ff ff f3 0f 6f a2 10 01 00 00 66 0f 38 dc c4 f3
    0d00: 0f 6f a2 20 01 00 00 66 0f 38 dc c4 f3 0f 6f a2
    0d10: 30 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 40 01 00
    0d20: 00 66 0f 38 dc c4 f3 0f 6f a2 50 01 00 00 66 0f
    0d30: 38 dc c4 f3 0f 6f a2 60 01 00 00 66 0f 38 dc c4
    0d40: f3 0f 6f a2 70 01 00 00 66 0f 38 dc c4 f3 0f 6f
    0d50: a2 80 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 90 01
    0d60: 00 00 66 0f 38 dc c4 f3 0f 6f a2 a0 01 00 00 49
    0d70: 81 fb 0c 00 00 00 0f 82 3a 00 00 00 66 0f 38 dc
    0d80: c4 f3 0f 6f a2 b0 01 00 00 66 0f 38 dc c4 f3 0f
    0d90: 6f a2 c0 01 00 00 0f 84 1a 00 00 00 66 0f 38 dc
    0da0: c4 f3 0f 6f a2 d0 01 00 00 66 0f 38 dc c4 f3 0f
    0db0: 6f a2 e0 01 00 00 66 0f 38 dd c4 f3 0f 6f ad d8
    0dc0: fe ff ff 66 0f ef c5 4d 01 c1 f3 0f 7f 85 d8 fe
    0dd0: ff ff 48 8d 8d d8 fe ff ff 4c 89 d6 48 81 e6 08
    0de0: 00 00 00 0f 84 14 00 00 00 48 8b 31 49 89 31 48
    0df0: 81 c1 08 00 00 00 49 81 c1 08 00 00 00 4c 89 d6
    0e00: 48 81 e6 04 00 00 00 0f 84 13 00 00 00 8b 31 41
    0e10: 89 31 48 81 c1 04 00 00 00 49 81 c1 04 00 00 00
    0e20: 4c 89 d6 48 81 e6 02 00 00 00 0f 84 16 00 00 00
    0e30: 48 0f b7 31 66 41 89 31 48 81 c1 02 00 00 00 49
    0e40: 81 c1 02 00 00 00 4c 89 d6 48 81 e6 01 00 00 00
    0e50: 0f 84 15 00 00 00 48 0f b6 31 41 88 31 48 81 c1
    0e60: 01 00 00 00 49 81 c1 01 00 00 00 4c 8b 85 58 ff
    0e70: ff ff f3 41 0f 7f 08 31 c0 48 81 c4 d0 01 00 00
    0e80: 5d c3 0f 0e 0d 0c 0b 0a 09 08 07 06 05 04 03 02
    0e90: 01 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00
    0ea0: 00 c2 ff ff ff ff ff ff ff ff ff ff ff ff ff ff
    0eb0: ff ff 00 00 00 00 00 00 00 00 00 00 00 00 00 00
    0ec0: 00 00 00
}

chunk @mrt_aes_gcm_finish {
    0000: 55 48 89 e5 48 83 ec 20 48 8d 05 b5 00 00 00 f3
    0010: 41 0f 6f 08 f3 41 0f 6f 68 20 49 c1 e1 03 48 c1
    0020: e1 03 66 49 0f 6e c1 66 48 0f 3a 22 c1 01 66 0f
    0030: ef c1 f3 0f 6f 8a e0 00 00 00 0f 28 d1 f3 0f 6f
    0040: 9a f0 00 00 00 66 0f 3a 44 c8 00 66 0f 3a 44 d0
    0050: 11 66 0f 70 e0 4e 66 0f ef e0 66 0f 3a 44 dc 00
    0060: 66 0f ef d9 66 0f ef da 0f 28 e3 66 0f 73 db 08
    0070: 66 0f 73 fc 08 66 0f ef d3 66 0f ef cc f3 0f 6f
    0080: 60 10 66 0f 3a 44 e1 01 66 0f 70 c9 4e 66 0f ef
    0090: cc f3 0f 6f 60 10 66 0f 3a 44 e1 01 66 0f 70 c9
    00a0: 4e 66 0f ef cc 66 0f ef ca f3 0f 6f 20 66 0f 38
    00b0: 00 cc 66 0f ef cd f3 41 0f 7f 08 31 c0 48 83 c4
    00c0: 20 5d c3 00 0f 0e 0d 0c 0b 0a 09 08 07 06 05 04
    00d0: 03 02 01 00 01 00 00 00 00 00 00 00 00 00 00 00
    00e0: 00 00 00 c2 ff ff ff ff ff ff ff ff ff ff ff ff
    00f0: ff ff ff ff 00 00 00 00 00 00 00 00 00 00 00 00
    0100: 00 00 00 00 00
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
    arm64.prologue 176
    arm64.storeSlotReg slot18, x28
    arm64.storeSlotReg slot17, x27
    arm64.storeSlotReg slot16, x26
    arm64.storeSlotReg slot15, x25
    arm64.storeSlotReg slot14, x24
    arm64.storeSlotReg slot13, x23
    arm64.storeSlotReg slot12, x22
    arm64.storeSlotReg slot11, x21
    arm64.storeSlotReg slot10, x20
    arm64.storeSlotReg slot9, x19
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
    arm64.storeSlotReg slot2, x21
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.storeSlotReg slot3, x22
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x23, x0
    arm64.storeSlotReg slot4, x23
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x24, x0
    arm64.storeSlotReg slot5, x24
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x25, x0
    arm64.storeSlotReg slot6, x25
    arm64.movImm x1, 16
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x1, 352
    arm64.movRegReg x0, x20
    arm64.bl __managed_resize
    arm64.movImm x1, 432
    arm64.movRegReg x0, x21
    arm64.bl __managed_resize
    arm64.movImm x1, 12
    arm64.movRegReg x0, x22
    arm64.bl __managed_resize
    arm64.movImm x1, 32
    arm64.movRegReg x0, x23
    arm64.bl __managed_resize
    arm64.movImm x1, 64
    arm64.movRegReg x0, x24
    arm64.bl __managed_resize
    arm64.movImm x1, 80
    arm64.movRegReg x0, x25
    arm64.bl __managed_resize
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __cpufeat_aes_cipher_expand_key
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x20
    arm64.bl __cpufeat_aes_cipher_gcm_init
    arm64.movImm x1, 0
    arm64.movRegReg x0, x25
    arm64.movRegReg x2, x21
    arm64.movRegReg x3, x22
    arm64.movRegReg x4, x23
    arm64.movRegReg x5, x24
    arm64.bl __cpufeat_aes_cipher_gcm_seal
    arm64.movImm x1, 0
    arm64.movRegReg x0, x24
    arm64.movRegReg x2, x21
    arm64.movRegReg x3, x22
    arm64.movRegReg x4, x23
    arm64.movRegReg x5, x25
    arm64.bl __cpufeat_aes_cipher_gcm_open
    arm64.movRegReg x19, x0
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot7, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __bool_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_2  ; " "
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x23, [x25 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x24
    arm64.bl __uint_to_string
    arm64.movRegReg x23, x0
    arm64.leaRdata x0, __str_rec_3  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.add x0, x19, x22
    arm64.add x0, x0, x23
    arm64.add x0, x0, x26
    arm64.storeSlotReg slot8, x0
    arm64.add x0, x0, 57
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
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x19, x19, x22
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x19, x19, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x0, x19, x26
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.loadRegSlot x0, slot8
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.loadRegSlot x0, slot7
    arm64.bl __mm_decref
    arm64.movRegReg x0, x24
    arm64.bl __mm_decref
  __il_body:
    arm64.movRegReg x0, x27
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.loadRegSlot x0, slot6
    arm64.bl __managed_decref
  exitdrop#2:
    arm64.loadRegSlot x0, slot5
    arm64.bl __managed_decref
  exitdrop#3:
    arm64.loadRegSlot x0, slot4
    arm64.bl __managed_decref
  exitdrop#4:
    arm64.loadRegSlot x0, slot3
    arm64.bl __managed_decref
  exitdrop#5:
    arm64.loadRegSlot x0, slot2
    arm64.bl __managed_decref
  exitdrop#6:
    arm64.loadRegSlot x0, slot1
    arm64.bl __managed_decref
  exitdrop#7:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot9
    arm64.loadRegSlot x20, slot10
    arm64.loadRegSlot x21, slot11
    arm64.loadRegSlot x22, slot12
    arm64.loadRegSlot x23, slot13
    arm64.loadRegSlot x24, slot14
    arm64.loadRegSlot x25, slot15
    arm64.loadRegSlot x26, slot16
    arm64.loadRegSlot x27, slot17
    arm64.loadRegSlot x28, slot18
    arm64.epilogue 176
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_init {
  entry:
    arm64.prologue 48
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.cmp x2, 352
    arm64.b.lt gibad
  scbounded:
    arm64.cmp x2, 480
    arm64.b.gt gibad
  scstepped:
    arm64.and x3, x2, 63
    arm64.cmp x3, 32
    arm64.b.ne gibad
  gikeyblock:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.lsr x4, x2, 1
    arm64.add x4, x4, 256
    arm64.cmp x3, x4
    arm64.b.ne gibad
  gicrypt:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt giddetach
  gidbuf:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq giddone
  gidshared:
    arm64.sub x3, x3, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne giddetach
  giddone:
    arm64.lsr x2, x2, 5
    arm64.sub x4, x2, 1
    arm64.loadRegBaseDisp.word64 x5, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.movImm x3, 0
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x5
    arm64.bl mrt_aes_gcm_init
    arm64.movRegReg x1, x0
    arm64.epilogue 48
    arm64.ret
  giddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.bl __managed_cow_detach
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.b giddone
  gibad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.epilogue 48
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_seal {
  entry:
    arm64.prologue 192
    arm64.storeSlotReg slot21, x25
    arm64.storeSlotReg slot20, x24
    arm64.storeSlotReg slot19, x23
    arm64.storeSlotReg slot18, x22
    arm64.storeSlotReg slot17, x21
    arm64.storeSlotReg slot16, x20
    arm64.storeSlotReg slot15, x19
    arm64.loadRegBaseDisp.word64 x6, [x2 + 8]
    arm64.cmp x6, 432
    arm64.b.lt gpbad
  kbbounded:
    arm64.cmp x6, 496
    arm64.b.gt gpbad
  kbstepped:
    arm64.and x7, x6, 31
    arm64.cmp x7, 16
    arm64.b.ne gpbad
  gpnonce:
    arm64.loadRegBaseDisp.word64 x7, [x3 + 8]
    arm64.cmp x7, 12
    arm64.b.ne gpbad
  gpsource:
    arm64.loadRegBaseDisp.word64 x19, [x5 + 8]
  gpmeasure:
    arm64.movImm x7, 68719476704
    arm64.cmp x19, x7
    arm64.b.hi gpbad
  gpspan:
    arm64.add x7, x19, 16
    arm64.loadRegBaseDisp.word64 x8, [x0 + 8]
    arm64.cmp x1, x8
    arm64.b.hi gpbad
  gpinside:
    arm64.sub x8, x8, x1
    arm64.cmp x8, x7
    arm64.b.lo gpbad
  gpdetach:
    arm64.loadRegBaseDisp.word64 x8, [x0 + 16]
    arm64.cmp x8, 0
    arm64.b.lt gpddetach
  gpdbuf:
    arm64.loadRegBaseDisp.word64 x8, [x0 + 0]
    arm64.cmp x8, 0
    arm64.b.eq gpddone
  gpdshared:
    arm64.sub x8, x8, 16
    arm64.loadRegBaseDisp.word64 x8, [x8 + 8]
    arm64.cmp x8, 0
    arm64.b.ne gpddetach
  gpddone:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x5, [x5 + 0]
    arm64.add x20, x0, x1
    arm64.cmp x19, 0
    arm64.b.eq gpoverlap
  gpwritten:
    arm64.cmp x7, 0
    arm64.b.eq gpoverlap
  gpapart:
    arm64.cmp x20, x5
    arm64.b.eq gpoverlap
  gpbelow:
    arm64.add x0, x20, x7
    arm64.cmp x0, x5
    arm64.b.ls gpoverlap
  gpabove:
    arm64.add x0, x5, x19
    arm64.cmp x0, x20
    arm64.b.hi gpbad
  gpoverlap:
    arm64.sub x0, x6, 256
    arm64.lsr x0, x0, 4
    arm64.sub x21, x0, 1
    arm64.loadRegBaseDisp.word64 x22, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x4 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x4 + 8]
    arm64.leaRegSlot x25, slot0
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x25 + 0], x1
    arm64.storeBaseDispReg.word64 [x25 + 8], x1
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x16, x0, 4
    arm64.loadRegBaseDisp.word64 x0, [x16 + 0]
    arm64.storeBaseDispReg.word64 [x25 + 16], x1
    arm64.add x16, x25, 20
    arm64.storeBaseDispReg.word64 [x16 + 0], x0
    arm64.movImm x0, 16777216
    arm64.add x16, x25, 28
    arm64.storeBaseDispReg.word64 [x16 + 0], x0
    arm64.storeBaseDispReg.word64 [x25 + 48], x5
    arm64.add x1, x22, 256
    arm64.add x3, x25, 16
    arm64.add x2, x25, 32
    arm64.movRegReg x0, x21
    arm64.bl mrt_aes_encrypt_block
    arm64.cmp x24, 0
    arm64.b.eq ghdone
  ghrun:
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x25
    arm64.movRegReg x3, x23
    arm64.bl mrt_aes_gcm_hash
  ghdone:
    arm64.lsl x0, x19, 8
    arm64.orr x0, x0, x21
    arm64.cmp x19, 0
    arm64.b.eq gcdone
  gcrun:
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x25
    arm64.movRegReg x3, x20
    arm64.bl mrt_aes_gcm_encrypt
  gcdone:
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x25
    arm64.movRegReg x3, x19
    arm64.bl mrt_aes_gcm_finish
    arm64.movRegReg x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x25 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x25 + 8]
    arm64.add x3, x20, x19
    arm64.storeBaseDispReg.word64 [x3 + 0], x1
    arm64.storeBaseDispReg.word64 [x3 + 8], x2
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x25 + 0], x1
    arm64.storeBaseDispReg.word64 [x25 + 8], x1
    arm64.storeBaseDispReg.word64 [x25 + 16], x1
    arm64.storeBaseDispReg.word64 [x25 + 24], x1
    arm64.storeBaseDispReg.word64 [x25 + 32], x1
    arm64.storeBaseDispReg.word64 [x25 + 40], x1
    arm64.storeBaseDispReg.word64 [x25 + 48], x1
    arm64.loadRegSlot x19, slot15
    arm64.loadRegSlot x20, slot16
    arm64.loadRegSlot x21, slot17
    arm64.loadRegSlot x22, slot18
    arm64.loadRegSlot x23, slot19
    arm64.loadRegSlot x24, slot20
    arm64.loadRegSlot x25, slot21
    arm64.epilogue 192
    arm64.ret
  gpddetach:
    arm64.storeSlotReg slot7, x0
    arm64.storeSlotReg slot8, x1
    arm64.storeSlotReg slot9, x2
    arm64.storeSlotReg slot10, x3
    arm64.storeSlotReg slot11, x4
    arm64.storeSlotReg slot12, x5
    arm64.storeSlotReg slot13, x6
    arm64.storeSlotReg slot14, x7
    arm64.bl __managed_cow_detach
    arm64.movRegReg x8, x0
    arm64.loadRegSlot x0, slot7
    arm64.loadRegSlot x1, slot8
    arm64.loadRegSlot x2, slot9
    arm64.loadRegSlot x3, slot10
    arm64.loadRegSlot x4, slot11
    arm64.loadRegSlot x5, slot12
    arm64.loadRegSlot x6, slot13
    arm64.loadRegSlot x7, slot14
    arm64.b gpddone
  gpbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot15
    arm64.loadRegSlot x20, slot16
    arm64.loadRegSlot x21, slot17
    arm64.loadRegSlot x22, slot18
    arm64.loadRegSlot x23, slot19
    arm64.loadRegSlot x24, slot20
    arm64.loadRegSlot x25, slot21
    arm64.epilogue 192
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_open {
  entry:
    arm64.prologue 208
    arm64.storeSlotReg slot22, x26
    arm64.storeSlotReg slot21, x25
    arm64.storeSlotReg slot20, x24
    arm64.storeSlotReg slot19, x23
    arm64.storeSlotReg slot18, x22
    arm64.storeSlotReg slot17, x21
    arm64.storeSlotReg slot16, x20
    arm64.storeSlotReg slot15, x19
    arm64.loadRegBaseDisp.word64 x6, [x2 + 8]
    arm64.cmp x6, 432
    arm64.b.lt gpbad
  kbbounded:
    arm64.cmp x6, 496
    arm64.b.gt gpbad
  kbstepped:
    arm64.and x7, x6, 31
    arm64.cmp x7, 16
    arm64.b.ne gpbad
  gpnonce:
    arm64.loadRegBaseDisp.word64 x7, [x3 + 8]
    arm64.cmp x7, 12
    arm64.b.ne gpbad
  gpsource:
    arm64.loadRegBaseDisp.word64 x7, [x5 + 8]
    arm64.cmp x7, 16
    arm64.b.lt gpbad
  gpmeasure:
    arm64.sub x19, x7, 16
    arm64.movImm x8, 68719476704
    arm64.cmp x19, x8
    arm64.b.hi gpbad
  gpspan:
    arm64.loadRegBaseDisp.word64 x8, [x0 + 8]
    arm64.cmp x1, x8
    arm64.b.hi gpbad
  gpinside:
    arm64.sub x8, x8, x1
    arm64.cmp x8, x19
    arm64.b.lo gpbad
  gpdetach:
    arm64.loadRegBaseDisp.word64 x8, [x0 + 16]
    arm64.cmp x8, 0
    arm64.b.lt gpddetach
  gpdbuf:
    arm64.loadRegBaseDisp.word64 x8, [x0 + 0]
    arm64.cmp x8, 0
    arm64.b.eq gpddone
  gpdshared:
    arm64.sub x8, x8, 16
    arm64.loadRegBaseDisp.word64 x8, [x8 + 8]
    arm64.cmp x8, 0
    arm64.b.ne gpddetach
  gpddone:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x5 + 0]
    arm64.add x21, x0, x1
  gpwritten:
    arm64.cmp x19, 0
    arm64.b.eq gpoverlap
  gpapart:
    arm64.cmp x21, x20
    arm64.b.eq gpoverlap
  gpbelow:
    arm64.add x0, x21, x19
    arm64.cmp x0, x20
    arm64.b.ls gpoverlap
  gpabove:
    arm64.add x0, x20, x7
    arm64.cmp x0, x21
    arm64.b.hi gpbad
  gpoverlap:
    arm64.sub x0, x6, 256
    arm64.lsr x0, x0, 4
    arm64.sub x22, x0, 1
    arm64.loadRegBaseDisp.word64 x23, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x4 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x4 + 8]
    arm64.leaRegSlot x26, slot0
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x26 + 0], x1
    arm64.storeBaseDispReg.word64 [x26 + 8], x1
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x16, x0, 4
    arm64.loadRegBaseDisp.word64 x0, [x16 + 0]
    arm64.storeBaseDispReg.word64 [x26 + 16], x1
    arm64.add x16, x26, 20
    arm64.storeBaseDispReg.word64 [x16 + 0], x0
    arm64.movImm x0, 16777216
    arm64.add x16, x26, 28
    arm64.storeBaseDispReg.word64 [x16 + 0], x0
    arm64.storeBaseDispReg.word64 [x26 + 48], x20
    arm64.add x1, x23, 256
    arm64.add x3, x26, 16
    arm64.add x2, x26, 32
    arm64.movRegReg x0, x22
    arm64.bl mrt_aes_encrypt_block
    arm64.cmp x25, 0
    arm64.b.eq ghdone
  ghrun:
    arm64.movRegReg x0, x25
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x26
    arm64.movRegReg x3, x24
    arm64.bl mrt_aes_gcm_hash
  ghdone:
    arm64.lsl x0, x19, 8
    arm64.orr x0, x0, x22
    arm64.cmp x19, 0
    arm64.b.eq gcdone
  gcrun:
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x26
    arm64.movRegReg x3, x21
    arm64.bl mrt_aes_gcm_decrypt
  gcdone:
    arm64.movRegReg x0, x25
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x26
    arm64.movRegReg x3, x19
    arm64.bl mrt_aes_gcm_finish
    arm64.loadRegBaseDisp.word64 x0, [x26 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x26 + 8]
    arm64.add x2, x20, x19
    arm64.loadRegBaseDisp.word64 x3, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.eor x0, x0, x3
    arm64.eor x1, x1, x2
    arm64.orr x0, x0, x1
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x26 + 0], x1
    arm64.storeBaseDispReg.word64 [x26 + 8], x1
    arm64.storeBaseDispReg.word64 [x26 + 16], x1
    arm64.storeBaseDispReg.word64 [x26 + 24], x1
    arm64.storeBaseDispReg.word64 [x26 + 32], x1
    arm64.storeBaseDispReg.word64 [x26 + 40], x1
    arm64.storeBaseDispReg.word64 [x26 + 48], x1
    arm64.cmp x0, 0
    arm64.b.ne gprejected
  gpaccepted:
    arm64.movImm x0, 1
    arm64.loadRegSlot x19, slot15
    arm64.loadRegSlot x20, slot16
    arm64.loadRegSlot x21, slot17
    arm64.loadRegSlot x22, slot18
    arm64.loadRegSlot x23, slot19
    arm64.loadRegSlot x24, slot20
    arm64.loadRegSlot x25, slot21
    arm64.loadRegSlot x26, slot22
    arm64.epilogue 208
    arm64.ret
  gprejected:
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot15
    arm64.loadRegSlot x20, slot16
    arm64.loadRegSlot x21, slot17
    arm64.loadRegSlot x22, slot18
    arm64.loadRegSlot x23, slot19
    arm64.loadRegSlot x24, slot20
    arm64.loadRegSlot x25, slot21
    arm64.loadRegSlot x26, slot22
    arm64.epilogue 208
    arm64.ret
  gpddetach:
    arm64.storeSlotReg slot7, x0
    arm64.storeSlotReg slot8, x1
    arm64.storeSlotReg slot9, x2
    arm64.storeSlotReg slot10, x3
    arm64.storeSlotReg slot11, x4
    arm64.storeSlotReg slot12, x5
    arm64.storeSlotReg slot13, x6
    arm64.storeSlotReg slot14, x7
    arm64.bl __managed_cow_detach
    arm64.movRegReg x8, x0
    arm64.loadRegSlot x0, slot7
    arm64.loadRegSlot x1, slot8
    arm64.loadRegSlot x2, slot9
    arm64.loadRegSlot x3, slot10
    arm64.loadRegSlot x4, slot11
    arm64.loadRegSlot x5, slot12
    arm64.loadRegSlot x6, slot13
    arm64.loadRegSlot x7, slot14
    arm64.b gpddone
  gpbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot15
    arm64.loadRegSlot x20, slot16
    arm64.loadRegSlot x21, slot17
    arm64.loadRegSlot x22, slot18
    arm64.loadRegSlot x23, slot19
    arm64.loadRegSlot x24, slot20
    arm64.loadRegSlot x25, slot21
    arm64.loadRegSlot x26, slot22
    arm64.epilogue 208
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
    0010: f7 1c 18 4e 18 1f 38 6e 50 00 c0 3d e0 18 00 b4
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
    0250: 40 07 00 b4 e9 03 03 aa ef 03 00 aa eb 03 1f aa
    0260: ec 03 1f aa 1f 20 00 f1 83 00 00 54 2b 01 40 f9
    0270: 29 21 00 91 ef 21 00 d1 e7 03 1f aa ed 03 1f aa
    0280: ee 01 7e 92 ce 00 00 b4 2e 01 40 b9 ce 21 cd 9a
    0290: e7 00 0e aa 29 11 00 91 ad 81 00 91 ee 01 7f 92
    02a0: ce 00 00 b4 2e 01 40 79 ce 21 cd 9a e7 00 0e aa
    02b0: 29 09 00 91 ad 41 00 91 ee 01 40 92 ce 00 00 b4
    02c0: 2e 01 40 39 ce 21 cd 9a e7 00 0e aa 29 05 00 91
    02d0: ad 21 00 91 1f 20 00 f1 ec 20 8c 9a 6b 21 87 9a
    02e0: 60 1d 08 4e 80 1d 18 4e 00 08 20 4e 00 1c 30 6e
    02f0: 13 40 00 6e 13 1c 33 6e 11 e0 f4 0e 10 e0 f4 4e
    0300: 72 e2 f5 0e 52 1e 30 6e 52 1e 31 6e 53 42 18 6e
    0310: 12 43 12 6e 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e
    0320: 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e
    0330: 31 42 11 6e 10 1e 31 6e 50 00 80 3d e0 03 1f aa
    0340: c0 03 5f d6
}

chunk @mrt_aes_gcm_encrypt {
    0000: 04 fc 48 d3 e5 1f 80 d2 05 00 05 8a 28 00 04 91
    0010: 47 18 80 d2 e7 1c 48 d3 f7 1c 08 4e 27 00 80 d2
    0020: f7 1c 18 4e 18 1f 38 6e 50 00 c0 3d 31 1e 31 6e
    0030: 52 1e 32 6e 5a 04 c0 3d 39 1f 39 6e 27 00 80 d2
    0040: f9 1c 1c 4e 47 1c 40 b9 e7 08 c0 5a 86 3c 00 91
    0050: c6 fc 44 d3 e7 00 06 8b e7 08 c0 5a 47 1c 00 b9
    0060: 5a 0b 20 6e 5a 87 b9 4e ea 03 03 aa 43 18 40 f9
    0070: 9f 00 02 f1 c3 30 00 54 84 00 02 d1 40 0b 20 6e
    0080: 5a 87 b9 4e 41 0b 20 6e 5a 87 b9 4e 42 0b 20 6e
    0090: 5a 87 b9 4e 43 0b 20 6e 5a 87 b9 4e 44 0b 20 6e
    00a0: 5a 87 b9 4e 45 0b 20 6e 5a 87 b9 4e 46 0b 20 6e
    00b0: 5a 87 b9 4e 47 0b 20 6e 5a 87 b9 4e e6 03 08 aa
    00c0: bf 30 00 f1 03 09 00 54 80 04 00 54 db 00 c0 3d
    00d0: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    00e0: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    00f0: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0100: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0110: db 04 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0120: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0130: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0140: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0150: e7 68 28 4e c6 80 00 91 db 00 c0 3d 60 4b 28 4e
    0160: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0170: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0180: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0190: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 04 c0 3d
    01a0: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    01b0: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    01c0: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    01d0: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    01e0: c6 80 00 91 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    01f0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0200: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0210: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0220: 67 4b 28 4e e7 68 28 4e db 04 c0 3d 60 4b 28 4e
    0230: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0240: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0250: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0260: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 08 c0 3d
    0270: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0280: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0290: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    02a0: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    02b0: db 0c c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    02c0: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    02d0: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    02e0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    02f0: e7 68 28 4e db 10 c0 3d 60 4b 28 4e 00 68 28 4e
    0300: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0310: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0320: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0330: 67 4b 28 4e e7 68 28 4e db 14 c0 3d 60 4b 28 4e
    0340: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0350: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0360: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0370: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 18 c0 3d
    0380: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0390: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    03a0: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    03b0: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    03c0: db 1c c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    03d0: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    03e0: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    03f0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0400: e7 68 28 4e db 20 c0 3d 60 4b 28 4e 00 68 28 4e
    0410: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0420: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0430: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0440: 67 4b 28 4e e7 68 28 4e db 24 c0 3d 60 4b 28 4e
    0450: 61 4b 28 4e 62 4b 28 4e 63 4b 28 4e 64 4b 28 4e
    0460: 65 4b 28 4e 66 4b 28 4e 67 4b 28 4e db 28 c0 3d
    0470: 00 1c 3b 6e 21 1c 3b 6e 42 1c 3b 6e 63 1c 3b 6e
    0480: 84 1c 3b 6e a5 1c 3b 6e c6 1c 3b 6e e7 1c 3b 6e
    0490: 74 00 c0 3d 75 04 c0 3d 00 1c 34 6e 21 1c 35 6e
    04a0: 40 01 80 3d 41 05 80 3d 74 08 c0 3d 75 0c c0 3d
    04b0: 42 1c 34 6e 63 1c 35 6e 42 09 80 3d 43 0d 80 3d
    04c0: 74 10 c0 3d 75 14 c0 3d 84 1c 34 6e a5 1c 35 6e
    04d0: 44 11 80 3d 45 15 80 3d 74 18 c0 3d 75 1c c0 3d
    04e0: c6 1c 34 6e e7 1c 35 6e 46 19 80 3d 47 1d 80 3d
    04f0: 34 00 c0 3d 35 04 c0 3d 00 08 20 4e 00 1c 30 6e
    0500: 13 40 00 6e 13 1c 33 6e 11 e0 f4 0e 10 e0 f4 4e
    0510: 72 e2 f5 0e 34 08 c0 3d 35 0c c0 3d 21 08 20 4e
    0520: 33 40 01 6e 33 1c 33 6e 36 e0 f4 0e 31 1e 36 6e
    0530: 36 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0540: 34 10 c0 3d 35 14 c0 3d 42 08 20 4e 53 40 02 6e
    0550: 53 1c 33 6e 56 e0 f4 0e 31 1e 36 6e 56 e0 f4 4e
    0560: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 34 18 c0 3d
    0570: 35 1c c0 3d 63 08 20 4e 73 40 03 6e 73 1c 33 6e
    0580: 76 e0 f4 0e 31 1e 36 6e 76 e0 f4 4e 10 1e 36 6e
    0590: 76 e2 f5 0e 52 1e 36 6e 34 20 c0 3d 35 24 c0 3d
    05a0: 84 08 20 4e 93 40 04 6e 93 1c 33 6e 96 e0 f4 0e
    05b0: 31 1e 36 6e 96 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e
    05c0: 52 1e 36 6e 34 28 c0 3d 35 2c c0 3d a5 08 20 4e
    05d0: b3 40 05 6e b3 1c 33 6e b6 e0 f4 0e 31 1e 36 6e
    05e0: b6 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    05f0: 34 30 c0 3d 35 34 c0 3d c6 08 20 4e d3 40 06 6e
    0600: d3 1c 33 6e d6 e0 f4 0e 31 1e 36 6e d6 e0 f4 4e
    0610: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 34 38 c0 3d
    0620: 35 3c c0 3d e7 08 20 4e f3 40 07 6e f3 1c 33 6e
    0630: f6 e0 f4 0e 31 1e 36 6e f6 e0 f4 4e 10 1e 36 6e
    0640: 76 e2 f5 0e 52 1e 36 6e 52 1e 30 6e 52 1e 31 6e
    0650: 53 42 18 6e 12 43 12 6e 10 1e 32 6e 31 1e 33 6e
    0660: 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e
    0670: 31 1e 33 6e 31 42 11 6e 10 1e 31 6e 63 00 02 91
    0680: 4a 01 02 91 9f 00 02 f1 82 cf ff 54 44 20 00 b4
    0690: 9f 40 00 f1 c3 0c 00 54 e9 03 0a aa ec 03 1f aa
    06a0: 84 40 00 d1 7c 00 c0 3d 40 0b 20 6e 5a 87 b9 4e
    06b0: e6 03 08 aa bf 30 00 f1 03 02 00 54 00 01 00 54
    06c0: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    06d0: 60 4b 28 4e 00 68 28 4e c6 80 00 91 db 00 c0 3d
    06e0: 60 4b 28 4e 00 68 28 4e db 04 c0 3d 60 4b 28 4e
    06f0: 00 68 28 4e c6 80 00 91 db 00 c0 3d 60 4b 28 4e
    0700: 00 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    0710: db 08 c0 3d 60 4b 28 4e 00 68 28 4e db 0c c0 3d
    0720: 60 4b 28 4e 00 68 28 4e db 10 c0 3d 60 4b 28 4e
    0730: 00 68 28 4e db 14 c0 3d 60 4b 28 4e 00 68 28 4e
    0740: db 18 c0 3d 60 4b 28 4e 00 68 28 4e db 1c c0 3d
    0750: 60 4b 28 4e 00 68 28 4e db 20 c0 3d 60 4b 28 4e
    0760: 00 68 28 4e db 24 c0 3d 60 4b 28 4e db 28 c0 3d
    0770: 00 1c 3b 6e 00 1c 3c 6e 40 01 80 3d 63 40 00 91
    0780: 4a 41 00 91 8c 41 00 91 9f 40 00 f1 a2 f8 ff 54
    0790: 01 1e b0 4e 10 1e 30 6e 31 1e 31 6e 52 1e 32 6e
    07a0: 8e f9 7f d3 0d 20 80 d2 ad 01 0e cb ad 01 01 8b
    07b0: 20 01 c0 3d 00 08 20 4e 00 1c 21 6e 21 1c 21 6e
    07c0: b4 01 c0 3d b5 05 c0 3d 13 40 00 6e 13 1c 33 6e
    07d0: 16 e0 f4 0e 31 1e 36 6e 16 e0 f4 4e 10 1e 36 6e
    07e0: 76 e2 f5 0e 52 1e 36 6e 29 41 00 91 ad 81 00 91
    07f0: 8c 41 00 d1 ec fd ff b5 52 1e 30 6e 52 1e 31 6e
    0800: 53 42 18 6e 12 43 12 6e 10 1e 32 6e 31 1e 33 6e
    0810: 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e
    0820: 31 1e 33 6e 31 42 11 6e 10 1e 31 6e 34 38 c0 3d
    0830: 35 3c c0 3d 04 13 00 b4 e9 03 03 aa ef 03 04 aa
    0840: eb 03 1f aa ec 03 1f aa 9f 20 00 f1 83 00 00 54
    0850: 2b 01 40 f9 29 21 00 91 ef 21 00 d1 e7 03 1f aa
    0860: ed 03 1f aa ee 01 7e 92 ce 00 00 b4 2e 01 40 b9
    0870: ce 21 cd 9a e7 00 0e aa 29 11 00 91 ad 81 00 91
    0880: ee 01 7f 92 ce 00 00 b4 2e 01 40 79 ce 21 cd 9a
    0890: e7 00 0e aa 29 09 00 91 ad 41 00 91 ee 01 40 92
    08a0: ce 00 00 b4 2e 01 40 39 ce 21 cd 9a e7 00 0e aa
    08b0: 29 05 00 91 ad 21 00 91 9f 20 00 f1 ec 20 8c 9a
    08c0: 6b 21 87 9a 7c 1d 08 4e 9c 1d 18 4e 40 0b 20 6e
    08d0: 5a 87 b9 4e e6 03 08 aa bf 30 00 f1 03 02 00 54
    08e0: 00 01 00 54 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    08f0: db 04 c0 3d 60 4b 28 4e 00 68 28 4e c6 80 00 91
    0900: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    0910: 60 4b 28 4e 00 68 28 4e c6 80 00 91 db 00 c0 3d
    0920: 60 4b 28 4e 00 68 28 4e db 04 c0 3d 60 4b 28 4e
    0930: 00 68 28 4e db 08 c0 3d 60 4b 28 4e 00 68 28 4e
    0940: db 0c c0 3d 60 4b 28 4e 00 68 28 4e db 10 c0 3d
    0950: 60 4b 28 4e 00 68 28 4e db 14 c0 3d 60 4b 28 4e
    0960: 00 68 28 4e db 18 c0 3d 60 4b 28 4e 00 68 28 4e
    0970: db 1c c0 3d 60 4b 28 4e 00 68 28 4e db 20 c0 3d
    0980: 60 4b 28 4e 00 68 28 4e db 24 c0 3d 60 4b 28 4e
    0990: db 28 c0 3d 00 1c 3b 6e 00 1c 3c 6e e9 03 0a aa
    09a0: ef 03 04 aa 0b 3c 08 4e 0c 3c 18 4e 9f 20 00 f1
    09b0: a3 00 00 54 2b 01 00 f9 29 21 00 91 ef 21 00 d1
    09c0: eb 03 0c aa ee 01 7e 92 8e 00 00 b4 2b 01 00 b9
    09d0: 29 11 00 91 6b fd 60 d3 ee 01 7f 92 8e 00 00 b4
    09e0: 2b 01 00 79 29 09 00 91 6b fd 50 d3 ee 01 40 92
    09f0: 8e 00 00 b4 2b 01 00 39 29 05 00 91 6b fd 48 d3
    0a00: 0b 3c 08 4e 0c 3c 18 4e 8e 08 40 92 ce f1 7d d3
    0a10: 2d 00 80 d2 ad 21 ce 9a ad 05 00 d1 ee 03 1f aa
    0a20: ce 05 00 d1 ef 03 1f aa 9f 20 00 f1 ce 21 8d 9a
    0a30: af 21 8f 9a 6b 01 0e 8a 8c 01 0f 8a 60 1d 08 4e
    0a40: 80 1d 18 4e 00 08 20 4e 00 1c 30 6e 13 40 00 6e
    0a50: 13 1c 33 6e 11 e0 f4 0e 10 e0 f4 4e 72 e2 f5 0e
    0a60: 52 1e 30 6e 52 1e 31 6e 53 42 18 6e 12 43 12 6e
    0a70: 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e
    0a80: 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e
    0a90: 10 1e 31 6e 50 00 80 3d e0 03 1f aa c0 03 5f d6
}

chunk @mrt_aes_gcm_decrypt {
    0000: 04 fc 48 d3 e5 1f 80 d2 05 00 05 8a 28 00 04 91
    0010: 47 18 80 d2 e7 1c 48 d3 f7 1c 08 4e 27 00 80 d2
    0020: f7 1c 18 4e 18 1f 38 6e 50 00 c0 3d 31 1e 31 6e
    0030: 52 1e 32 6e 5a 04 c0 3d 39 1f 39 6e 27 00 80 d2
    0040: f9 1c 1c 4e 47 1c 40 b9 e7 08 c0 5a 86 3c 00 91
    0050: c6 fc 44 d3 e7 00 06 8b e7 08 c0 5a 47 1c 00 b9
    0060: 5a 0b 20 6e 5a 87 b9 4e ea 03 03 aa 43 18 40 f9
    0070: 9f 00 02 f1 c3 31 00 54 84 00 02 d1 40 0b 20 6e
    0080: 5a 87 b9 4e 41 0b 20 6e 5a 87 b9 4e 42 0b 20 6e
    0090: 5a 87 b9 4e 43 0b 20 6e 5a 87 b9 4e 44 0b 20 6e
    00a0: 5a 87 b9 4e 45 0b 20 6e 5a 87 b9 4e 46 0b 20 6e
    00b0: 5a 87 b9 4e 47 0b 20 6e 5a 87 b9 4e e6 03 08 aa
    00c0: bf 30 00 f1 03 09 00 54 80 04 00 54 db 00 c0 3d
    00d0: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    00e0: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    00f0: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0100: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0110: db 04 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0120: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0130: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0140: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0150: e7 68 28 4e c6 80 00 91 db 00 c0 3d 60 4b 28 4e
    0160: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0170: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0180: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0190: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 04 c0 3d
    01a0: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    01b0: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    01c0: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    01d0: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    01e0: c6 80 00 91 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    01f0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0200: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0210: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0220: 67 4b 28 4e e7 68 28 4e db 04 c0 3d 60 4b 28 4e
    0230: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0240: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0250: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0260: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 08 c0 3d
    0270: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0280: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0290: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    02a0: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    02b0: db 0c c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    02c0: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    02d0: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    02e0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    02f0: e7 68 28 4e db 10 c0 3d 60 4b 28 4e 00 68 28 4e
    0300: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0310: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0320: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0330: 67 4b 28 4e e7 68 28 4e db 14 c0 3d 60 4b 28 4e
    0340: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0350: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0360: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0370: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 18 c0 3d
    0380: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0390: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    03a0: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    03b0: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    03c0: db 1c c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    03d0: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    03e0: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    03f0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0400: e7 68 28 4e db 20 c0 3d 60 4b 28 4e 00 68 28 4e
    0410: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0420: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0430: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0440: 67 4b 28 4e e7 68 28 4e db 24 c0 3d 60 4b 28 4e
    0450: 61 4b 28 4e 62 4b 28 4e 63 4b 28 4e 64 4b 28 4e
    0460: 65 4b 28 4e 66 4b 28 4e 67 4b 28 4e db 28 c0 3d
    0470: 00 1c 3b 6e 21 1c 3b 6e 42 1c 3b 6e 63 1c 3b 6e
    0480: 84 1c 3b 6e a5 1c 3b 6e c6 1c 3b 6e e7 1c 3b 6e
    0490: 7d 00 c0 3d 34 00 c0 3d 35 04 c0 3d bd 0b 20 4e
    04a0: bd 1f 30 6e b3 43 1d 6e b3 1f 33 6e b1 e3 f4 0e
    04b0: b0 e3 f4 4e 72 e2 f5 0e 7d 04 c0 3d 34 08 c0 3d
    04c0: 35 0c c0 3d bd 0b 20 4e b3 43 1d 6e b3 1f 33 6e
    04d0: b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e 10 1e 36 6e
    04e0: 76 e2 f5 0e 52 1e 36 6e 7d 08 c0 3d 34 10 c0 3d
    04f0: 35 14 c0 3d bd 0b 20 4e b3 43 1d 6e b3 1f 33 6e
    0500: b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e 10 1e 36 6e
    0510: 76 e2 f5 0e 52 1e 36 6e 7d 0c c0 3d 34 18 c0 3d
    0520: 35 1c c0 3d bd 0b 20 4e b3 43 1d 6e b3 1f 33 6e
    0530: b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e 10 1e 36 6e
    0540: 76 e2 f5 0e 52 1e 36 6e 7d 10 c0 3d 34 20 c0 3d
    0550: 35 24 c0 3d bd 0b 20 4e b3 43 1d 6e b3 1f 33 6e
    0560: b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e 10 1e 36 6e
    0570: 76 e2 f5 0e 52 1e 36 6e 7d 14 c0 3d 34 28 c0 3d
    0580: 35 2c c0 3d bd 0b 20 4e b3 43 1d 6e b3 1f 33 6e
    0590: b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e 10 1e 36 6e
    05a0: 76 e2 f5 0e 52 1e 36 6e 7d 18 c0 3d 34 30 c0 3d
    05b0: 35 34 c0 3d bd 0b 20 4e b3 43 1d 6e b3 1f 33 6e
    05c0: b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e 10 1e 36 6e
    05d0: 76 e2 f5 0e 52 1e 36 6e 7d 1c c0 3d 34 38 c0 3d
    05e0: 35 3c c0 3d bd 0b 20 4e b3 43 1d 6e b3 1f 33 6e
    05f0: b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e 10 1e 36 6e
    0600: 76 e2 f5 0e 52 1e 36 6e 52 1e 30 6e 52 1e 31 6e
    0610: 53 42 18 6e 12 43 12 6e 10 1e 32 6e 31 1e 33 6e
    0620: 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e
    0630: 31 1e 33 6e 31 42 11 6e 10 1e 31 6e 7c 00 c0 3d
    0640: 00 1c 3c 6e 40 01 80 3d 7c 04 c0 3d 21 1c 3c 6e
    0650: 41 05 80 3d 7c 08 c0 3d 42 1c 3c 6e 42 09 80 3d
    0660: 7c 0c c0 3d 63 1c 3c 6e 43 0d 80 3d 7c 10 c0 3d
    0670: 84 1c 3c 6e 44 11 80 3d 7c 14 c0 3d a5 1c 3c 6e
    0680: 45 15 80 3d 7c 18 c0 3d c6 1c 3c 6e 46 19 80 3d
    0690: 7c 1c c0 3d e7 1c 3c 6e 47 1d 80 3d 63 00 02 91
    06a0: 4a 01 02 91 9f 00 02 f1 82 ce ff 54 44 1e 00 b4
    06b0: 9f 40 00 f1 e3 0c 00 54 e9 03 03 aa 8c fc 44 d3
    06c0: 8c ed 7c d3 01 1e b0 4e 10 1e 30 6e 31 1e 31 6e
    06d0: 52 1e 32 6e 8e f9 7f d3 0d 20 80 d2 ad 01 0e cb
    06e0: ad 01 01 8b 20 01 c0 3d 00 08 20 4e 00 1c 21 6e
    06f0: 21 1c 21 6e b4 01 c0 3d b5 05 c0 3d 13 40 00 6e
    0700: 13 1c 33 6e 16 e0 f4 0e 31 1e 36 6e 16 e0 f4 4e
    0710: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 29 41 00 91
    0720: ad 81 00 91 8c 41 00 d1 ec fd ff b5 52 1e 30 6e
    0730: 52 1e 31 6e 53 42 18 6e 12 43 12 6e 10 1e 32 6e
    0740: 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e
    0750: 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e 10 1e 31 6e
    0760: 84 40 00 d1 7c 00 c0 3d 40 0b 20 6e 5a 87 b9 4e
    0770: e6 03 08 aa bf 30 00 f1 03 02 00 54 00 01 00 54
    0780: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    0790: 60 4b 28 4e 00 68 28 4e c6 80 00 91 db 00 c0 3d
    07a0: 60 4b 28 4e 00 68 28 4e db 04 c0 3d 60 4b 28 4e
    07b0: 00 68 28 4e c6 80 00 91 db 00 c0 3d 60 4b 28 4e
    07c0: 00 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    07d0: db 08 c0 3d 60 4b 28 4e 00 68 28 4e db 0c c0 3d
    07e0: 60 4b 28 4e 00 68 28 4e db 10 c0 3d 60 4b 28 4e
    07f0: 00 68 28 4e db 14 c0 3d 60 4b 28 4e 00 68 28 4e
    0800: db 18 c0 3d 60 4b 28 4e 00 68 28 4e db 1c c0 3d
    0810: 60 4b 28 4e 00 68 28 4e db 20 c0 3d 60 4b 28 4e
    0820: 00 68 28 4e db 24 c0 3d 60 4b 28 4e db 28 c0 3d
    0830: 00 1c 3b 6e 00 1c 3c 6e 40 01 80 3d 63 40 00 91
    0840: 4a 41 00 91 8c 41 00 91 9f 40 00 f1 a2 f8 ff 54
    0850: 34 38 c0 3d 35 3c c0 3d e4 10 00 b4 e9 03 03 aa
    0860: ef 03 04 aa eb 03 1f aa ec 03 1f aa 9f 20 00 f1
    0870: 83 00 00 54 2b 01 40 f9 29 21 00 91 ef 21 00 d1
    0880: e7 03 1f aa ed 03 1f aa ee 01 7e 92 ce 00 00 b4
    0890: 2e 01 40 b9 ce 21 cd 9a e7 00 0e aa 29 11 00 91
    08a0: ad 81 00 91 ee 01 7f 92 ce 00 00 b4 2e 01 40 79
    08b0: ce 21 cd 9a e7 00 0e aa 29 09 00 91 ad 41 00 91
    08c0: ee 01 40 92 ce 00 00 b4 2e 01 40 39 ce 21 cd 9a
    08d0: e7 00 0e aa 29 05 00 91 ad 21 00 91 9f 20 00 f1
    08e0: ec 20 8c 9a 6b 21 87 9a 7c 1d 08 4e 9c 1d 18 4e
    08f0: 40 0b 20 6e 5a 87 b9 4e e6 03 08 aa bf 30 00 f1
    0900: 03 02 00 54 00 01 00 54 db 00 c0 3d 60 4b 28 4e
    0910: 00 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    0920: c6 80 00 91 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    0930: db 04 c0 3d 60 4b 28 4e 00 68 28 4e c6 80 00 91
    0940: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    0950: 60 4b 28 4e 00 68 28 4e db 08 c0 3d 60 4b 28 4e
    0960: 00 68 28 4e db 0c c0 3d 60 4b 28 4e 00 68 28 4e
    0970: db 10 c0 3d 60 4b 28 4e 00 68 28 4e db 14 c0 3d
    0980: 60 4b 28 4e 00 68 28 4e db 18 c0 3d 60 4b 28 4e
    0990: 00 68 28 4e db 1c c0 3d 60 4b 28 4e 00 68 28 4e
    09a0: db 20 c0 3d 60 4b 28 4e 00 68 28 4e db 24 c0 3d
    09b0: 60 4b 28 4e db 28 c0 3d 00 1c 3b 6e 00 1c 3c 6e
    09c0: e9 03 0a aa ef 03 04 aa 0b 3c 08 4e 0c 3c 18 4e
    09d0: 9f 20 00 f1 a3 00 00 54 2b 01 00 f9 29 21 00 91
    09e0: ef 21 00 d1 eb 03 0c aa ee 01 7e 92 8e 00 00 b4
    09f0: 2b 01 00 b9 29 11 00 91 6b fd 60 d3 ee 01 7f 92
    0a00: 8e 00 00 b4 2b 01 00 79 29 09 00 91 6b fd 50 d3
    0a10: ee 01 40 92 8e 00 00 b4 2b 01 00 39 29 05 00 91
    0a20: 6b fd 48 d3 9c 0b 20 4e 9c 1f 30 6e 93 43 1c 6e
    0a30: 93 1f 33 6e 91 e3 f4 0e 90 e3 f4 4e 72 e2 f5 0e
    0a40: 52 1e 30 6e 52 1e 31 6e 53 42 18 6e 12 43 12 6e
    0a50: 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e
    0a60: 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e
    0a70: 10 1e 31 6e 50 00 80 3d e0 03 1f aa c0 03 5f d6
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
    arm64.prologue 176
    arm64.storeSlotReg slot18, x28
    arm64.storeSlotReg slot17, x27
    arm64.storeSlotReg slot16, x26
    arm64.storeSlotReg slot15, x25
    arm64.storeSlotReg slot14, x24
    arm64.storeSlotReg slot13, x23
    arm64.storeSlotReg slot12, x22
    arm64.storeSlotReg slot11, x21
    arm64.storeSlotReg slot10, x20
    arm64.storeSlotReg slot9, x19
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
    arm64.storeSlotReg slot2, x21
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x22, x0
    arm64.storeSlotReg slot3, x22
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x23, x0
    arm64.storeSlotReg slot4, x23
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x24, x0
    arm64.storeSlotReg slot5, x24
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x25, x0
    arm64.storeSlotReg slot6, x25
    arm64.movImm x1, 16
    arm64.movRegReg x0, x19
    arm64.bl __managed_resize
    arm64.movImm x1, 352
    arm64.movRegReg x0, x20
    arm64.bl __managed_resize
    arm64.movImm x1, 432
    arm64.movRegReg x0, x21
    arm64.bl __managed_resize
    arm64.movImm x1, 12
    arm64.movRegReg x0, x22
    arm64.bl __managed_resize
    arm64.movImm x1, 32
    arm64.movRegReg x0, x23
    arm64.bl __managed_resize
    arm64.movImm x1, 64
    arm64.movRegReg x0, x24
    arm64.bl __managed_resize
    arm64.movImm x1, 80
    arm64.movRegReg x0, x25
    arm64.bl __managed_resize
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __cpufeat_aes_cipher_expand_key
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x20
    arm64.bl __cpufeat_aes_cipher_gcm_init
    arm64.movImm x1, 0
    arm64.movRegReg x0, x25
    arm64.movRegReg x2, x21
    arm64.movRegReg x3, x22
    arm64.movRegReg x4, x23
    arm64.movRegReg x5, x24
    arm64.bl __cpufeat_aes_cipher_gcm_seal
    arm64.movImm x1, 0
    arm64.movRegReg x0, x24
    arm64.movRegReg x2, x21
    arm64.movRegReg x3, x22
    arm64.movRegReg x4, x23
    arm64.movRegReg x5, x25
    arm64.bl __cpufeat_aes_cipher_gcm_open
    arm64.movRegReg x19, x0
    arm64.movImm x0, 5
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot7, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __bool_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_2  ; " "
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x23, [x25 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x24
    arm64.bl __uint_to_string
    arm64.movRegReg x23, x0
    arm64.leaRdata x0, __str_rec_3  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.add x0, x19, x22
    arm64.add x0, x0, x23
    arm64.add x0, x0, x26
    arm64.storeSlotReg slot8, x0
    arm64.add x0, x0, 57
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
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x19, x19, x22
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x19, x19, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x0, x19, x26
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.loadRegSlot x0, slot8
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.loadRegSlot x0, slot7
    arm64.bl __mm_decref
    arm64.movRegReg x0, x24
    arm64.bl __mm_decref
  __il_body:
    arm64.movRegReg x0, x27
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.loadRegSlot x0, slot6
    arm64.bl __managed_decref
  exitdrop#2:
    arm64.loadRegSlot x0, slot5
    arm64.bl __managed_decref
  exitdrop#3:
    arm64.loadRegSlot x0, slot4
    arm64.bl __managed_decref
  exitdrop#4:
    arm64.loadRegSlot x0, slot3
    arm64.bl __managed_decref
  exitdrop#5:
    arm64.loadRegSlot x0, slot2
    arm64.bl __managed_decref
  exitdrop#6:
    arm64.loadRegSlot x0, slot1
    arm64.bl __managed_decref
  exitdrop#7:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot9
    arm64.loadRegSlot x20, slot10
    arm64.loadRegSlot x21, slot11
    arm64.loadRegSlot x22, slot12
    arm64.loadRegSlot x23, slot13
    arm64.loadRegSlot x24, slot14
    arm64.loadRegSlot x25, slot15
    arm64.loadRegSlot x26, slot16
    arm64.loadRegSlot x27, slot17
    arm64.loadRegSlot x28, slot18
    arm64.epilogue 176
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_init {
  entry:
    arm64.prologue 48
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.cmp x2, 352
    arm64.b.lt gibad
  scbounded:
    arm64.cmp x2, 480
    arm64.b.gt gibad
  scstepped:
    arm64.and x3, x2, 63
    arm64.cmp x3, 32
    arm64.b.ne gibad
  gikeyblock:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.lsr x4, x2, 1
    arm64.add x4, x4, 256
    arm64.cmp x3, x4
    arm64.b.ne gibad
  gicrypt:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt giddetach
  gidbuf:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq giddone
  gidshared:
    arm64.sub x3, x3, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne giddetach
  giddone:
    arm64.lsr x2, x2, 5
    arm64.sub x4, x2, 1
    arm64.loadRegBaseDisp.word64 x5, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.movImm x3, 0
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x5
    arm64.bl mrt_aes_gcm_init
    arm64.movRegReg x1, x0
    arm64.epilogue 48
    arm64.ret
  giddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.bl __managed_cow_detach
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.b giddone
  gibad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.epilogue 48
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_seal {
  entry:
    arm64.prologue 192
    arm64.storeSlotReg slot21, x25
    arm64.storeSlotReg slot20, x24
    arm64.storeSlotReg slot19, x23
    arm64.storeSlotReg slot18, x22
    arm64.storeSlotReg slot17, x21
    arm64.storeSlotReg slot16, x20
    arm64.storeSlotReg slot15, x19
    arm64.loadRegBaseDisp.word64 x6, [x2 + 8]
    arm64.cmp x6, 432
    arm64.b.lt gpbad
  kbbounded:
    arm64.cmp x6, 496
    arm64.b.gt gpbad
  kbstepped:
    arm64.and x7, x6, 31
    arm64.cmp x7, 16
    arm64.b.ne gpbad
  gpnonce:
    arm64.loadRegBaseDisp.word64 x7, [x3 + 8]
    arm64.cmp x7, 12
    arm64.b.ne gpbad
  gpsource:
    arm64.loadRegBaseDisp.word64 x19, [x5 + 8]
  gpmeasure:
    arm64.movImm x7, 68719476704
    arm64.cmp x19, x7
    arm64.b.hi gpbad
  gpspan:
    arm64.add x7, x19, 16
    arm64.loadRegBaseDisp.word64 x8, [x0 + 8]
    arm64.cmp x1, x8
    arm64.b.hi gpbad
  gpinside:
    arm64.sub x8, x8, x1
    arm64.cmp x8, x7
    arm64.b.lo gpbad
  gpdetach:
    arm64.loadRegBaseDisp.word64 x8, [x0 + 16]
    arm64.cmp x8, 0
    arm64.b.lt gpddetach
  gpdbuf:
    arm64.loadRegBaseDisp.word64 x8, [x0 + 0]
    arm64.cmp x8, 0
    arm64.b.eq gpddone
  gpdshared:
    arm64.sub x8, x8, 16
    arm64.loadRegBaseDisp.word64 x8, [x8 + 8]
    arm64.cmp x8, 0
    arm64.b.ne gpddetach
  gpddone:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x5, [x5 + 0]
    arm64.add x20, x0, x1
    arm64.cmp x19, 0
    arm64.b.eq gpoverlap
  gpwritten:
    arm64.cmp x7, 0
    arm64.b.eq gpoverlap
  gpapart:
    arm64.cmp x20, x5
    arm64.b.eq gpoverlap
  gpbelow:
    arm64.add x0, x20, x7
    arm64.cmp x0, x5
    arm64.b.ls gpoverlap
  gpabove:
    arm64.add x0, x5, x19
    arm64.cmp x0, x20
    arm64.b.hi gpbad
  gpoverlap:
    arm64.sub x0, x6, 256
    arm64.lsr x0, x0, 4
    arm64.sub x21, x0, 1
    arm64.loadRegBaseDisp.word64 x22, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x4 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x4 + 8]
    arm64.leaRegSlot x25, slot0
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x25 + 0], x1
    arm64.storeBaseDispReg.word64 [x25 + 8], x1
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x16, x0, 4
    arm64.loadRegBaseDisp.word64 x0, [x16 + 0]
    arm64.storeBaseDispReg.word64 [x25 + 16], x1
    arm64.add x16, x25, 20
    arm64.storeBaseDispReg.word64 [x16 + 0], x0
    arm64.movImm x0, 16777216
    arm64.add x16, x25, 28
    arm64.storeBaseDispReg.word64 [x16 + 0], x0
    arm64.storeBaseDispReg.word64 [x25 + 48], x5
    arm64.add x1, x22, 256
    arm64.add x3, x25, 16
    arm64.add x2, x25, 32
    arm64.movRegReg x0, x21
    arm64.bl mrt_aes_encrypt_block
    arm64.cmp x24, 0
    arm64.b.eq ghdone
  ghrun:
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x25
    arm64.movRegReg x3, x23
    arm64.bl mrt_aes_gcm_hash
  ghdone:
    arm64.lsl x0, x19, 8
    arm64.orr x0, x0, x21
    arm64.cmp x19, 0
    arm64.b.eq gcdone
  gcrun:
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x25
    arm64.movRegReg x3, x20
    arm64.bl mrt_aes_gcm_encrypt
  gcdone:
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x25
    arm64.movRegReg x3, x19
    arm64.bl mrt_aes_gcm_finish
    arm64.movRegReg x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x25 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x25 + 8]
    arm64.add x3, x20, x19
    arm64.storeBaseDispReg.word64 [x3 + 0], x1
    arm64.storeBaseDispReg.word64 [x3 + 8], x2
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x25 + 0], x1
    arm64.storeBaseDispReg.word64 [x25 + 8], x1
    arm64.storeBaseDispReg.word64 [x25 + 16], x1
    arm64.storeBaseDispReg.word64 [x25 + 24], x1
    arm64.storeBaseDispReg.word64 [x25 + 32], x1
    arm64.storeBaseDispReg.word64 [x25 + 40], x1
    arm64.storeBaseDispReg.word64 [x25 + 48], x1
    arm64.loadRegSlot x19, slot15
    arm64.loadRegSlot x20, slot16
    arm64.loadRegSlot x21, slot17
    arm64.loadRegSlot x22, slot18
    arm64.loadRegSlot x23, slot19
    arm64.loadRegSlot x24, slot20
    arm64.loadRegSlot x25, slot21
    arm64.epilogue 192
    arm64.ret
  gpddetach:
    arm64.storeSlotReg slot7, x0
    arm64.storeSlotReg slot8, x1
    arm64.storeSlotReg slot9, x2
    arm64.storeSlotReg slot10, x3
    arm64.storeSlotReg slot11, x4
    arm64.storeSlotReg slot12, x5
    arm64.storeSlotReg slot13, x6
    arm64.storeSlotReg slot14, x7
    arm64.bl __managed_cow_detach
    arm64.movRegReg x8, x0
    arm64.loadRegSlot x0, slot7
    arm64.loadRegSlot x1, slot8
    arm64.loadRegSlot x2, slot9
    arm64.loadRegSlot x3, slot10
    arm64.loadRegSlot x4, slot11
    arm64.loadRegSlot x5, slot12
    arm64.loadRegSlot x6, slot13
    arm64.loadRegSlot x7, slot14
    arm64.b gpddone
  gpbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot15
    arm64.loadRegSlot x20, slot16
    arm64.loadRegSlot x21, slot17
    arm64.loadRegSlot x22, slot18
    arm64.loadRegSlot x23, slot19
    arm64.loadRegSlot x24, slot20
    arm64.loadRegSlot x25, slot21
    arm64.epilogue 192
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_open {
  entry:
    arm64.prologue 208
    arm64.storeSlotReg slot22, x26
    arm64.storeSlotReg slot21, x25
    arm64.storeSlotReg slot20, x24
    arm64.storeSlotReg slot19, x23
    arm64.storeSlotReg slot18, x22
    arm64.storeSlotReg slot17, x21
    arm64.storeSlotReg slot16, x20
    arm64.storeSlotReg slot15, x19
    arm64.loadRegBaseDisp.word64 x6, [x2 + 8]
    arm64.cmp x6, 432
    arm64.b.lt gpbad
  kbbounded:
    arm64.cmp x6, 496
    arm64.b.gt gpbad
  kbstepped:
    arm64.and x7, x6, 31
    arm64.cmp x7, 16
    arm64.b.ne gpbad
  gpnonce:
    arm64.loadRegBaseDisp.word64 x7, [x3 + 8]
    arm64.cmp x7, 12
    arm64.b.ne gpbad
  gpsource:
    arm64.loadRegBaseDisp.word64 x7, [x5 + 8]
    arm64.cmp x7, 16
    arm64.b.lt gpbad
  gpmeasure:
    arm64.sub x19, x7, 16
    arm64.movImm x8, 68719476704
    arm64.cmp x19, x8
    arm64.b.hi gpbad
  gpspan:
    arm64.loadRegBaseDisp.word64 x8, [x0 + 8]
    arm64.cmp x1, x8
    arm64.b.hi gpbad
  gpinside:
    arm64.sub x8, x8, x1
    arm64.cmp x8, x19
    arm64.b.lo gpbad
  gpdetach:
    arm64.loadRegBaseDisp.word64 x8, [x0 + 16]
    arm64.cmp x8, 0
    arm64.b.lt gpddetach
  gpdbuf:
    arm64.loadRegBaseDisp.word64 x8, [x0 + 0]
    arm64.cmp x8, 0
    arm64.b.eq gpddone
  gpdshared:
    arm64.sub x8, x8, 16
    arm64.loadRegBaseDisp.word64 x8, [x8 + 8]
    arm64.cmp x8, 0
    arm64.b.ne gpddetach
  gpddone:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x5 + 0]
    arm64.add x21, x0, x1
  gpwritten:
    arm64.cmp x19, 0
    arm64.b.eq gpoverlap
  gpapart:
    arm64.cmp x21, x20
    arm64.b.eq gpoverlap
  gpbelow:
    arm64.add x0, x21, x19
    arm64.cmp x0, x20
    arm64.b.ls gpoverlap
  gpabove:
    arm64.add x0, x20, x7
    arm64.cmp x0, x21
    arm64.b.hi gpbad
  gpoverlap:
    arm64.sub x0, x6, 256
    arm64.lsr x0, x0, 4
    arm64.sub x22, x0, 1
    arm64.loadRegBaseDisp.word64 x23, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x4 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x4 + 8]
    arm64.leaRegSlot x26, slot0
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x26 + 0], x1
    arm64.storeBaseDispReg.word64 [x26 + 8], x1
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x16, x0, 4
    arm64.loadRegBaseDisp.word64 x0, [x16 + 0]
    arm64.storeBaseDispReg.word64 [x26 + 16], x1
    arm64.add x16, x26, 20
    arm64.storeBaseDispReg.word64 [x16 + 0], x0
    arm64.movImm x0, 16777216
    arm64.add x16, x26, 28
    arm64.storeBaseDispReg.word64 [x16 + 0], x0
    arm64.storeBaseDispReg.word64 [x26 + 48], x20
    arm64.add x1, x23, 256
    arm64.add x3, x26, 16
    arm64.add x2, x26, 32
    arm64.movRegReg x0, x22
    arm64.bl mrt_aes_encrypt_block
    arm64.cmp x25, 0
    arm64.b.eq ghdone
  ghrun:
    arm64.movRegReg x0, x25
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x26
    arm64.movRegReg x3, x24
    arm64.bl mrt_aes_gcm_hash
  ghdone:
    arm64.lsl x0, x19, 8
    arm64.orr x0, x0, x22
    arm64.cmp x19, 0
    arm64.b.eq gcdone
  gcrun:
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x26
    arm64.movRegReg x3, x21
    arm64.bl mrt_aes_gcm_decrypt
  gcdone:
    arm64.movRegReg x0, x25
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x26
    arm64.movRegReg x3, x19
    arm64.bl mrt_aes_gcm_finish
    arm64.loadRegBaseDisp.word64 x0, [x26 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x26 + 8]
    arm64.add x2, x20, x19
    arm64.loadRegBaseDisp.word64 x3, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.eor x0, x0, x3
    arm64.eor x1, x1, x2
    arm64.orr x0, x0, x1
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x26 + 0], x1
    arm64.storeBaseDispReg.word64 [x26 + 8], x1
    arm64.storeBaseDispReg.word64 [x26 + 16], x1
    arm64.storeBaseDispReg.word64 [x26 + 24], x1
    arm64.storeBaseDispReg.word64 [x26 + 32], x1
    arm64.storeBaseDispReg.word64 [x26 + 40], x1
    arm64.storeBaseDispReg.word64 [x26 + 48], x1
    arm64.cmp x0, 0
    arm64.b.ne gprejected
  gpaccepted:
    arm64.movImm x0, 1
    arm64.loadRegSlot x19, slot15
    arm64.loadRegSlot x20, slot16
    arm64.loadRegSlot x21, slot17
    arm64.loadRegSlot x22, slot18
    arm64.loadRegSlot x23, slot19
    arm64.loadRegSlot x24, slot20
    arm64.loadRegSlot x25, slot21
    arm64.loadRegSlot x26, slot22
    arm64.epilogue 208
    arm64.ret
  gprejected:
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot15
    arm64.loadRegSlot x20, slot16
    arm64.loadRegSlot x21, slot17
    arm64.loadRegSlot x22, slot18
    arm64.loadRegSlot x23, slot19
    arm64.loadRegSlot x24, slot20
    arm64.loadRegSlot x25, slot21
    arm64.loadRegSlot x26, slot22
    arm64.epilogue 208
    arm64.ret
  gpddetach:
    arm64.storeSlotReg slot7, x0
    arm64.storeSlotReg slot8, x1
    arm64.storeSlotReg slot9, x2
    arm64.storeSlotReg slot10, x3
    arm64.storeSlotReg slot11, x4
    arm64.storeSlotReg slot12, x5
    arm64.storeSlotReg slot13, x6
    arm64.storeSlotReg slot14, x7
    arm64.bl __managed_cow_detach
    arm64.movRegReg x8, x0
    arm64.loadRegSlot x0, slot7
    arm64.loadRegSlot x1, slot8
    arm64.loadRegSlot x2, slot9
    arm64.loadRegSlot x3, slot10
    arm64.loadRegSlot x4, slot11
    arm64.loadRegSlot x5, slot12
    arm64.loadRegSlot x6, slot13
    arm64.loadRegSlot x7, slot14
    arm64.b gpddone
  gpbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot15
    arm64.loadRegSlot x20, slot16
    arm64.loadRegSlot x21, slot17
    arm64.loadRegSlot x22, slot18
    arm64.loadRegSlot x23, slot19
    arm64.loadRegSlot x24, slot20
    arm64.loadRegSlot x25, slot21
    arm64.loadRegSlot x26, slot22
    arm64.epilogue 208
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
    0010: f7 1c 18 4e 18 1f 38 6e 50 00 c0 3d e0 18 00 b4
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
    0250: 40 07 00 b4 e9 03 03 aa ef 03 00 aa eb 03 1f aa
    0260: ec 03 1f aa 1f 20 00 f1 83 00 00 54 2b 01 40 f9
    0270: 29 21 00 91 ef 21 00 d1 e7 03 1f aa ed 03 1f aa
    0280: ee 01 7e 92 ce 00 00 b4 2e 01 40 b9 ce 21 cd 9a
    0290: e7 00 0e aa 29 11 00 91 ad 81 00 91 ee 01 7f 92
    02a0: ce 00 00 b4 2e 01 40 79 ce 21 cd 9a e7 00 0e aa
    02b0: 29 09 00 91 ad 41 00 91 ee 01 40 92 ce 00 00 b4
    02c0: 2e 01 40 39 ce 21 cd 9a e7 00 0e aa 29 05 00 91
    02d0: ad 21 00 91 1f 20 00 f1 ec 20 8c 9a 6b 21 87 9a
    02e0: 60 1d 08 4e 80 1d 18 4e 00 08 20 4e 00 1c 30 6e
    02f0: 13 40 00 6e 13 1c 33 6e 11 e0 f4 0e 10 e0 f4 4e
    0300: 72 e2 f5 0e 52 1e 30 6e 52 1e 31 6e 53 42 18 6e
    0310: 12 43 12 6e 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e
    0320: 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e
    0330: 31 42 11 6e 10 1e 31 6e 50 00 80 3d e0 03 1f aa
    0340: c0 03 5f d6
}

chunk @mrt_aes_gcm_encrypt {
    0000: 04 fc 48 d3 e5 1f 80 d2 05 00 05 8a 28 00 04 91
    0010: 47 18 80 d2 e7 1c 48 d3 f7 1c 08 4e 27 00 80 d2
    0020: f7 1c 18 4e 18 1f 38 6e 50 00 c0 3d 31 1e 31 6e
    0030: 52 1e 32 6e 5a 04 c0 3d 39 1f 39 6e 27 00 80 d2
    0040: f9 1c 1c 4e 47 1c 40 b9 e7 08 c0 5a 86 3c 00 91
    0050: c6 fc 44 d3 e7 00 06 8b e7 08 c0 5a 47 1c 00 b9
    0060: 5a 0b 20 6e 5a 87 b9 4e ea 03 03 aa 43 18 40 f9
    0070: 9f 00 02 f1 c3 30 00 54 84 00 02 d1 40 0b 20 6e
    0080: 5a 87 b9 4e 41 0b 20 6e 5a 87 b9 4e 42 0b 20 6e
    0090: 5a 87 b9 4e 43 0b 20 6e 5a 87 b9 4e 44 0b 20 6e
    00a0: 5a 87 b9 4e 45 0b 20 6e 5a 87 b9 4e 46 0b 20 6e
    00b0: 5a 87 b9 4e 47 0b 20 6e 5a 87 b9 4e e6 03 08 aa
    00c0: bf 30 00 f1 03 09 00 54 80 04 00 54 db 00 c0 3d
    00d0: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    00e0: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    00f0: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0100: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0110: db 04 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0120: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0130: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0140: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0150: e7 68 28 4e c6 80 00 91 db 00 c0 3d 60 4b 28 4e
    0160: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0170: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0180: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0190: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 04 c0 3d
    01a0: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    01b0: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    01c0: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    01d0: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    01e0: c6 80 00 91 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    01f0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0200: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0210: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0220: 67 4b 28 4e e7 68 28 4e db 04 c0 3d 60 4b 28 4e
    0230: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0240: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0250: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0260: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 08 c0 3d
    0270: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0280: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0290: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    02a0: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    02b0: db 0c c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    02c0: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    02d0: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    02e0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    02f0: e7 68 28 4e db 10 c0 3d 60 4b 28 4e 00 68 28 4e
    0300: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0310: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0320: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0330: 67 4b 28 4e e7 68 28 4e db 14 c0 3d 60 4b 28 4e
    0340: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0350: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0360: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0370: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 18 c0 3d
    0380: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0390: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    03a0: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    03b0: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    03c0: db 1c c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    03d0: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    03e0: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    03f0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0400: e7 68 28 4e db 20 c0 3d 60 4b 28 4e 00 68 28 4e
    0410: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0420: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0430: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0440: 67 4b 28 4e e7 68 28 4e db 24 c0 3d 60 4b 28 4e
    0450: 61 4b 28 4e 62 4b 28 4e 63 4b 28 4e 64 4b 28 4e
    0460: 65 4b 28 4e 66 4b 28 4e 67 4b 28 4e db 28 c0 3d
    0470: 00 1c 3b 6e 21 1c 3b 6e 42 1c 3b 6e 63 1c 3b 6e
    0480: 84 1c 3b 6e a5 1c 3b 6e c6 1c 3b 6e e7 1c 3b 6e
    0490: 74 00 c0 3d 75 04 c0 3d 00 1c 34 6e 21 1c 35 6e
    04a0: 40 01 80 3d 41 05 80 3d 74 08 c0 3d 75 0c c0 3d
    04b0: 42 1c 34 6e 63 1c 35 6e 42 09 80 3d 43 0d 80 3d
    04c0: 74 10 c0 3d 75 14 c0 3d 84 1c 34 6e a5 1c 35 6e
    04d0: 44 11 80 3d 45 15 80 3d 74 18 c0 3d 75 1c c0 3d
    04e0: c6 1c 34 6e e7 1c 35 6e 46 19 80 3d 47 1d 80 3d
    04f0: 34 00 c0 3d 35 04 c0 3d 00 08 20 4e 00 1c 30 6e
    0500: 13 40 00 6e 13 1c 33 6e 11 e0 f4 0e 10 e0 f4 4e
    0510: 72 e2 f5 0e 34 08 c0 3d 35 0c c0 3d 21 08 20 4e
    0520: 33 40 01 6e 33 1c 33 6e 36 e0 f4 0e 31 1e 36 6e
    0530: 36 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0540: 34 10 c0 3d 35 14 c0 3d 42 08 20 4e 53 40 02 6e
    0550: 53 1c 33 6e 56 e0 f4 0e 31 1e 36 6e 56 e0 f4 4e
    0560: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 34 18 c0 3d
    0570: 35 1c c0 3d 63 08 20 4e 73 40 03 6e 73 1c 33 6e
    0580: 76 e0 f4 0e 31 1e 36 6e 76 e0 f4 4e 10 1e 36 6e
    0590: 76 e2 f5 0e 52 1e 36 6e 34 20 c0 3d 35 24 c0 3d
    05a0: 84 08 20 4e 93 40 04 6e 93 1c 33 6e 96 e0 f4 0e
    05b0: 31 1e 36 6e 96 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e
    05c0: 52 1e 36 6e 34 28 c0 3d 35 2c c0 3d a5 08 20 4e
    05d0: b3 40 05 6e b3 1c 33 6e b6 e0 f4 0e 31 1e 36 6e
    05e0: b6 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    05f0: 34 30 c0 3d 35 34 c0 3d c6 08 20 4e d3 40 06 6e
    0600: d3 1c 33 6e d6 e0 f4 0e 31 1e 36 6e d6 e0 f4 4e
    0610: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 34 38 c0 3d
    0620: 35 3c c0 3d e7 08 20 4e f3 40 07 6e f3 1c 33 6e
    0630: f6 e0 f4 0e 31 1e 36 6e f6 e0 f4 4e 10 1e 36 6e
    0640: 76 e2 f5 0e 52 1e 36 6e 52 1e 30 6e 52 1e 31 6e
    0650: 53 42 18 6e 12 43 12 6e 10 1e 32 6e 31 1e 33 6e
    0660: 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e
    0670: 31 1e 33 6e 31 42 11 6e 10 1e 31 6e 63 00 02 91
    0680: 4a 01 02 91 9f 00 02 f1 82 cf ff 54 44 20 00 b4
    0690: 9f 40 00 f1 c3 0c 00 54 e9 03 0a aa ec 03 1f aa
    06a0: 84 40 00 d1 7c 00 c0 3d 40 0b 20 6e 5a 87 b9 4e
    06b0: e6 03 08 aa bf 30 00 f1 03 02 00 54 00 01 00 54
    06c0: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    06d0: 60 4b 28 4e 00 68 28 4e c6 80 00 91 db 00 c0 3d
    06e0: 60 4b 28 4e 00 68 28 4e db 04 c0 3d 60 4b 28 4e
    06f0: 00 68 28 4e c6 80 00 91 db 00 c0 3d 60 4b 28 4e
    0700: 00 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    0710: db 08 c0 3d 60 4b 28 4e 00 68 28 4e db 0c c0 3d
    0720: 60 4b 28 4e 00 68 28 4e db 10 c0 3d 60 4b 28 4e
    0730: 00 68 28 4e db 14 c0 3d 60 4b 28 4e 00 68 28 4e
    0740: db 18 c0 3d 60 4b 28 4e 00 68 28 4e db 1c c0 3d
    0750: 60 4b 28 4e 00 68 28 4e db 20 c0 3d 60 4b 28 4e
    0760: 00 68 28 4e db 24 c0 3d 60 4b 28 4e db 28 c0 3d
    0770: 00 1c 3b 6e 00 1c 3c 6e 40 01 80 3d 63 40 00 91
    0780: 4a 41 00 91 8c 41 00 91 9f 40 00 f1 a2 f8 ff 54
    0790: 01 1e b0 4e 10 1e 30 6e 31 1e 31 6e 52 1e 32 6e
    07a0: 8e f9 7f d3 0d 20 80 d2 ad 01 0e cb ad 01 01 8b
    07b0: 20 01 c0 3d 00 08 20 4e 00 1c 21 6e 21 1c 21 6e
    07c0: b4 01 c0 3d b5 05 c0 3d 13 40 00 6e 13 1c 33 6e
    07d0: 16 e0 f4 0e 31 1e 36 6e 16 e0 f4 4e 10 1e 36 6e
    07e0: 76 e2 f5 0e 52 1e 36 6e 29 41 00 91 ad 81 00 91
    07f0: 8c 41 00 d1 ec fd ff b5 52 1e 30 6e 52 1e 31 6e
    0800: 53 42 18 6e 12 43 12 6e 10 1e 32 6e 31 1e 33 6e
    0810: 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e
    0820: 31 1e 33 6e 31 42 11 6e 10 1e 31 6e 34 38 c0 3d
    0830: 35 3c c0 3d 04 13 00 b4 e9 03 03 aa ef 03 04 aa
    0840: eb 03 1f aa ec 03 1f aa 9f 20 00 f1 83 00 00 54
    0850: 2b 01 40 f9 29 21 00 91 ef 21 00 d1 e7 03 1f aa
    0860: ed 03 1f aa ee 01 7e 92 ce 00 00 b4 2e 01 40 b9
    0870: ce 21 cd 9a e7 00 0e aa 29 11 00 91 ad 81 00 91
    0880: ee 01 7f 92 ce 00 00 b4 2e 01 40 79 ce 21 cd 9a
    0890: e7 00 0e aa 29 09 00 91 ad 41 00 91 ee 01 40 92
    08a0: ce 00 00 b4 2e 01 40 39 ce 21 cd 9a e7 00 0e aa
    08b0: 29 05 00 91 ad 21 00 91 9f 20 00 f1 ec 20 8c 9a
    08c0: 6b 21 87 9a 7c 1d 08 4e 9c 1d 18 4e 40 0b 20 6e
    08d0: 5a 87 b9 4e e6 03 08 aa bf 30 00 f1 03 02 00 54
    08e0: 00 01 00 54 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    08f0: db 04 c0 3d 60 4b 28 4e 00 68 28 4e c6 80 00 91
    0900: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    0910: 60 4b 28 4e 00 68 28 4e c6 80 00 91 db 00 c0 3d
    0920: 60 4b 28 4e 00 68 28 4e db 04 c0 3d 60 4b 28 4e
    0930: 00 68 28 4e db 08 c0 3d 60 4b 28 4e 00 68 28 4e
    0940: db 0c c0 3d 60 4b 28 4e 00 68 28 4e db 10 c0 3d
    0950: 60 4b 28 4e 00 68 28 4e db 14 c0 3d 60 4b 28 4e
    0960: 00 68 28 4e db 18 c0 3d 60 4b 28 4e 00 68 28 4e
    0970: db 1c c0 3d 60 4b 28 4e 00 68 28 4e db 20 c0 3d
    0980: 60 4b 28 4e 00 68 28 4e db 24 c0 3d 60 4b 28 4e
    0990: db 28 c0 3d 00 1c 3b 6e 00 1c 3c 6e e9 03 0a aa
    09a0: ef 03 04 aa 0b 3c 08 4e 0c 3c 18 4e 9f 20 00 f1
    09b0: a3 00 00 54 2b 01 00 f9 29 21 00 91 ef 21 00 d1
    09c0: eb 03 0c aa ee 01 7e 92 8e 00 00 b4 2b 01 00 b9
    09d0: 29 11 00 91 6b fd 60 d3 ee 01 7f 92 8e 00 00 b4
    09e0: 2b 01 00 79 29 09 00 91 6b fd 50 d3 ee 01 40 92
    09f0: 8e 00 00 b4 2b 01 00 39 29 05 00 91 6b fd 48 d3
    0a00: 0b 3c 08 4e 0c 3c 18 4e 8e 08 40 92 ce f1 7d d3
    0a10: 2d 00 80 d2 ad 21 ce 9a ad 05 00 d1 ee 03 1f aa
    0a20: ce 05 00 d1 ef 03 1f aa 9f 20 00 f1 ce 21 8d 9a
    0a30: af 21 8f 9a 6b 01 0e 8a 8c 01 0f 8a 60 1d 08 4e
    0a40: 80 1d 18 4e 00 08 20 4e 00 1c 30 6e 13 40 00 6e
    0a50: 13 1c 33 6e 11 e0 f4 0e 10 e0 f4 4e 72 e2 f5 0e
    0a60: 52 1e 30 6e 52 1e 31 6e 53 42 18 6e 12 43 12 6e
    0a70: 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e
    0a80: 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e
    0a90: 10 1e 31 6e 50 00 80 3d e0 03 1f aa c0 03 5f d6
}

chunk @mrt_aes_gcm_decrypt {
    0000: 04 fc 48 d3 e5 1f 80 d2 05 00 05 8a 28 00 04 91
    0010: 47 18 80 d2 e7 1c 48 d3 f7 1c 08 4e 27 00 80 d2
    0020: f7 1c 18 4e 18 1f 38 6e 50 00 c0 3d 31 1e 31 6e
    0030: 52 1e 32 6e 5a 04 c0 3d 39 1f 39 6e 27 00 80 d2
    0040: f9 1c 1c 4e 47 1c 40 b9 e7 08 c0 5a 86 3c 00 91
    0050: c6 fc 44 d3 e7 00 06 8b e7 08 c0 5a 47 1c 00 b9
    0060: 5a 0b 20 6e 5a 87 b9 4e ea 03 03 aa 43 18 40 f9
    0070: 9f 00 02 f1 c3 31 00 54 84 00 02 d1 40 0b 20 6e
    0080: 5a 87 b9 4e 41 0b 20 6e 5a 87 b9 4e 42 0b 20 6e
    0090: 5a 87 b9 4e 43 0b 20 6e 5a 87 b9 4e 44 0b 20 6e
    00a0: 5a 87 b9 4e 45 0b 20 6e 5a 87 b9 4e 46 0b 20 6e
    00b0: 5a 87 b9 4e 47 0b 20 6e 5a 87 b9 4e e6 03 08 aa
    00c0: bf 30 00 f1 03 09 00 54 80 04 00 54 db 00 c0 3d
    00d0: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    00e0: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    00f0: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0100: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0110: db 04 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0120: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0130: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0140: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0150: e7 68 28 4e c6 80 00 91 db 00 c0 3d 60 4b 28 4e
    0160: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0170: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0180: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0190: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 04 c0 3d
    01a0: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    01b0: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    01c0: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    01d0: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    01e0: c6 80 00 91 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    01f0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0200: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0210: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0220: 67 4b 28 4e e7 68 28 4e db 04 c0 3d 60 4b 28 4e
    0230: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0240: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0250: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0260: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 08 c0 3d
    0270: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0280: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0290: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    02a0: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    02b0: db 0c c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    02c0: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    02d0: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    02e0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    02f0: e7 68 28 4e db 10 c0 3d 60 4b 28 4e 00 68 28 4e
    0300: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0310: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0320: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0330: 67 4b 28 4e e7 68 28 4e db 14 c0 3d 60 4b 28 4e
    0340: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0350: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0360: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0370: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 18 c0 3d
    0380: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0390: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    03a0: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    03b0: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    03c0: db 1c c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    03d0: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    03e0: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    03f0: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0400: e7 68 28 4e db 20 c0 3d 60 4b 28 4e 00 68 28 4e
    0410: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0420: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0430: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0440: 67 4b 28 4e e7 68 28 4e db 24 c0 3d 60 4b 28 4e
    0450: 61 4b 28 4e 62 4b 28 4e 63 4b 28 4e 64 4b 28 4e
    0460: 65 4b 28 4e 66 4b 28 4e 67 4b 28 4e db 28 c0 3d
    0470: 00 1c 3b 6e 21 1c 3b 6e 42 1c 3b 6e 63 1c 3b 6e
    0480: 84 1c 3b 6e a5 1c 3b 6e c6 1c 3b 6e e7 1c 3b 6e
    0490: 7d 00 c0 3d 34 00 c0 3d 35 04 c0 3d bd 0b 20 4e
    04a0: bd 1f 30 6e b3 43 1d 6e b3 1f 33 6e b1 e3 f4 0e
    04b0: b0 e3 f4 4e 72 e2 f5 0e 7d 04 c0 3d 34 08 c0 3d
    04c0: 35 0c c0 3d bd 0b 20 4e b3 43 1d 6e b3 1f 33 6e
    04d0: b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e 10 1e 36 6e
    04e0: 76 e2 f5 0e 52 1e 36 6e 7d 08 c0 3d 34 10 c0 3d
    04f0: 35 14 c0 3d bd 0b 20 4e b3 43 1d 6e b3 1f 33 6e
    0500: b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e 10 1e 36 6e
    0510: 76 e2 f5 0e 52 1e 36 6e 7d 0c c0 3d 34 18 c0 3d
    0520: 35 1c c0 3d bd 0b 20 4e b3 43 1d 6e b3 1f 33 6e
    0530: b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e 10 1e 36 6e
    0540: 76 e2 f5 0e 52 1e 36 6e 7d 10 c0 3d 34 20 c0 3d
    0550: 35 24 c0 3d bd 0b 20 4e b3 43 1d 6e b3 1f 33 6e
    0560: b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e 10 1e 36 6e
    0570: 76 e2 f5 0e 52 1e 36 6e 7d 14 c0 3d 34 28 c0 3d
    0580: 35 2c c0 3d bd 0b 20 4e b3 43 1d 6e b3 1f 33 6e
    0590: b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e 10 1e 36 6e
    05a0: 76 e2 f5 0e 52 1e 36 6e 7d 18 c0 3d 34 30 c0 3d
    05b0: 35 34 c0 3d bd 0b 20 4e b3 43 1d 6e b3 1f 33 6e
    05c0: b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e 10 1e 36 6e
    05d0: 76 e2 f5 0e 52 1e 36 6e 7d 1c c0 3d 34 38 c0 3d
    05e0: 35 3c c0 3d bd 0b 20 4e b3 43 1d 6e b3 1f 33 6e
    05f0: b6 e3 f4 0e 31 1e 36 6e b6 e3 f4 4e 10 1e 36 6e
    0600: 76 e2 f5 0e 52 1e 36 6e 52 1e 30 6e 52 1e 31 6e
    0610: 53 42 18 6e 12 43 12 6e 10 1e 32 6e 31 1e 33 6e
    0620: 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e
    0630: 31 1e 33 6e 31 42 11 6e 10 1e 31 6e 7c 00 c0 3d
    0640: 00 1c 3c 6e 40 01 80 3d 7c 04 c0 3d 21 1c 3c 6e
    0650: 41 05 80 3d 7c 08 c0 3d 42 1c 3c 6e 42 09 80 3d
    0660: 7c 0c c0 3d 63 1c 3c 6e 43 0d 80 3d 7c 10 c0 3d
    0670: 84 1c 3c 6e 44 11 80 3d 7c 14 c0 3d a5 1c 3c 6e
    0680: 45 15 80 3d 7c 18 c0 3d c6 1c 3c 6e 46 19 80 3d
    0690: 7c 1c c0 3d e7 1c 3c 6e 47 1d 80 3d 63 00 02 91
    06a0: 4a 01 02 91 9f 00 02 f1 82 ce ff 54 44 1e 00 b4
    06b0: 9f 40 00 f1 e3 0c 00 54 e9 03 03 aa 8c fc 44 d3
    06c0: 8c ed 7c d3 01 1e b0 4e 10 1e 30 6e 31 1e 31 6e
    06d0: 52 1e 32 6e 8e f9 7f d3 0d 20 80 d2 ad 01 0e cb
    06e0: ad 01 01 8b 20 01 c0 3d 00 08 20 4e 00 1c 21 6e
    06f0: 21 1c 21 6e b4 01 c0 3d b5 05 c0 3d 13 40 00 6e
    0700: 13 1c 33 6e 16 e0 f4 0e 31 1e 36 6e 16 e0 f4 4e
    0710: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 29 41 00 91
    0720: ad 81 00 91 8c 41 00 d1 ec fd ff b5 52 1e 30 6e
    0730: 52 1e 31 6e 53 42 18 6e 12 43 12 6e 10 1e 32 6e
    0740: 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e
    0750: 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e 10 1e 31 6e
    0760: 84 40 00 d1 7c 00 c0 3d 40 0b 20 6e 5a 87 b9 4e
    0770: e6 03 08 aa bf 30 00 f1 03 02 00 54 00 01 00 54
    0780: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    0790: 60 4b 28 4e 00 68 28 4e c6 80 00 91 db 00 c0 3d
    07a0: 60 4b 28 4e 00 68 28 4e db 04 c0 3d 60 4b 28 4e
    07b0: 00 68 28 4e c6 80 00 91 db 00 c0 3d 60 4b 28 4e
    07c0: 00 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    07d0: db 08 c0 3d 60 4b 28 4e 00 68 28 4e db 0c c0 3d
    07e0: 60 4b 28 4e 00 68 28 4e db 10 c0 3d 60 4b 28 4e
    07f0: 00 68 28 4e db 14 c0 3d 60 4b 28 4e 00 68 28 4e
    0800: db 18 c0 3d 60 4b 28 4e 00 68 28 4e db 1c c0 3d
    0810: 60 4b 28 4e 00 68 28 4e db 20 c0 3d 60 4b 28 4e
    0820: 00 68 28 4e db 24 c0 3d 60 4b 28 4e db 28 c0 3d
    0830: 00 1c 3b 6e 00 1c 3c 6e 40 01 80 3d 63 40 00 91
    0840: 4a 41 00 91 8c 41 00 91 9f 40 00 f1 a2 f8 ff 54
    0850: 34 38 c0 3d 35 3c c0 3d e4 10 00 b4 e9 03 03 aa
    0860: ef 03 04 aa eb 03 1f aa ec 03 1f aa 9f 20 00 f1
    0870: 83 00 00 54 2b 01 40 f9 29 21 00 91 ef 21 00 d1
    0880: e7 03 1f aa ed 03 1f aa ee 01 7e 92 ce 00 00 b4
    0890: 2e 01 40 b9 ce 21 cd 9a e7 00 0e aa 29 11 00 91
    08a0: ad 81 00 91 ee 01 7f 92 ce 00 00 b4 2e 01 40 79
    08b0: ce 21 cd 9a e7 00 0e aa 29 09 00 91 ad 41 00 91
    08c0: ee 01 40 92 ce 00 00 b4 2e 01 40 39 ce 21 cd 9a
    08d0: e7 00 0e aa 29 05 00 91 ad 21 00 91 9f 20 00 f1
    08e0: ec 20 8c 9a 6b 21 87 9a 7c 1d 08 4e 9c 1d 18 4e
    08f0: 40 0b 20 6e 5a 87 b9 4e e6 03 08 aa bf 30 00 f1
    0900: 03 02 00 54 00 01 00 54 db 00 c0 3d 60 4b 28 4e
    0910: 00 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    0920: c6 80 00 91 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    0930: db 04 c0 3d 60 4b 28 4e 00 68 28 4e c6 80 00 91
    0940: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    0950: 60 4b 28 4e 00 68 28 4e db 08 c0 3d 60 4b 28 4e
    0960: 00 68 28 4e db 0c c0 3d 60 4b 28 4e 00 68 28 4e
    0970: db 10 c0 3d 60 4b 28 4e 00 68 28 4e db 14 c0 3d
    0980: 60 4b 28 4e 00 68 28 4e db 18 c0 3d 60 4b 28 4e
    0990: 00 68 28 4e db 1c c0 3d 60 4b 28 4e 00 68 28 4e
    09a0: db 20 c0 3d 60 4b 28 4e 00 68 28 4e db 24 c0 3d
    09b0: 60 4b 28 4e db 28 c0 3d 00 1c 3b 6e 00 1c 3c 6e
    09c0: e9 03 0a aa ef 03 04 aa 0b 3c 08 4e 0c 3c 18 4e
    09d0: 9f 20 00 f1 a3 00 00 54 2b 01 00 f9 29 21 00 91
    09e0: ef 21 00 d1 eb 03 0c aa ee 01 7e 92 8e 00 00 b4
    09f0: 2b 01 00 b9 29 11 00 91 6b fd 60 d3 ee 01 7f 92
    0a00: 8e 00 00 b4 2b 01 00 79 29 09 00 91 6b fd 50 d3
    0a10: ee 01 40 92 8e 00 00 b4 2b 01 00 39 29 05 00 91
    0a20: 6b fd 48 d3 9c 0b 20 4e 9c 1f 30 6e 93 43 1c 6e
    0a30: 93 1f 33 6e 91 e3 f4 0e 90 e3 f4 4e 72 e2 f5 0e
    0a40: 52 1e 30 6e 52 1e 31 6e 53 42 18 6e 12 43 12 6e
    0a50: 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e
    0a60: 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e
    0a70: 10 1e 31 6e 50 00 80 3d e0 03 1f aa c0 03 5f d6
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
