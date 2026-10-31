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
    x64.leaRegRegImm32 r8, r8, -24
    x64.loadRegBaseDisp.word64 r8, [r8 + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, abddetach
  abddone:
    x64.movRegReg r8, rdi
    x64.shrRegImm8 r8, rdi, 5
    x64.leaRegRegImm32 r8, r8, -1
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.shrRegImm8 rdi, rdi, 1
    x64.leaRegRegReg rdi, rax, rdi
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdi, [r9 + 0]
    x64.leaRegRegReg r9, rcx, rdx
    x64.leaRegRegReg rsi, rdi, rsi
    x64.movRegReg rcx, r8
    x64.movRegReg rdx, rax
    x64.movRegReg r8, r9
    x64.movRegReg r9, rsi
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
    x64.leaRegRegImm32 r8, r8, -24
    x64.loadRegBaseDisp.word64 r8, [r8 + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, abddetach
  abddone:
    x64.movRegReg r8, rdi
    x64.shrRegImm8 r8, rdi, 5
    x64.leaRegRegImm32 r8, r8, -1
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.shrRegImm8 rdi, rdi, 1
    x64.leaRegRegReg rdi, rax, rdi
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdi, [r9 + 0]
    x64.leaRegRegReg r9, rcx, rdx
    x64.leaRegRegReg rsi, rdi, rsi
    x64.movRegReg rcx, r8
    x64.movRegReg rdx, rax
    x64.movRegReg r8, r9
    x64.movRegReg r9, rsi
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
    arm64.sub x6, x6, 24
    arm64.loadRegBaseDisp.word64 x6, [x6 + 16]
    arm64.cmp x6, 0
    arm64.b.ne abddetach
  abddone:
    arm64.lsr x6, x5, 5
    arm64.sub x6, x6, 1
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.lsr x5, x5, 1
    arm64.add x5, x2, x5
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.add x5, x0, x1
    arm64.add x3, x3, x4
    arm64.movRegReg x0, x6
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x5
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
    arm64.sub x6, x6, 24
    arm64.loadRegBaseDisp.word64 x6, [x6 + 16]
    arm64.cmp x6, 0
    arm64.b.ne abddetach
  abddone:
    arm64.lsr x6, x5, 5
    arm64.sub x6, x6, 1
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.lsr x5, x5, 1
    arm64.add x5, x2, x5
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.add x5, x0, x1
    arm64.add x3, x3, x4
    arm64.movRegReg x0, x6
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x5
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
    x64.leaRegRegImm32 rdi, rdi, -24
    x64.loadRegBaseDisp.word64 rdi, [rdi + 16]
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
    x64.leaRegRegImm32 rdi, rdi, -24
    x64.loadRegBaseDisp.word64 rdi, [rdi + 16]
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
    x64.leaRegRegImm32 rdi, rdi, -24
    x64.loadRegBaseDisp.word64 rdi, [rdi + 16]
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
    x64.leaRegRegImm32 rdi, rdi, -24
    x64.loadRegBaseDisp.word64 rdi, [rdi + 16]
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
    x64.leaRegRegImm32 rdi, rdi, -24
    x64.loadRegBaseDisp.word64 rdi, [rdi + 16]
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
    x64.leaRegRegImm32 rdi, rdi, -24
    x64.loadRegBaseDisp.word64 rdi, [rdi + 16]
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
    x64.leaRegRegImm32 rdi, rdi, -24
    x64.loadRegBaseDisp.word64 rdi, [rdi + 16]
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
    x64.leaRegRegImm32 rdi, rdi, -24
    x64.loadRegBaseDisp.word64 rdi, [rdi + 16]
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
    arm64.sub x5, x5, 24
    arm64.loadRegBaseDisp.word64 x5, [x5 + 16]
    arm64.cmp x5, 0
    arm64.b.ne cx1ddetach
  cx1ddone:
    arm64.lsr x5, x6, 5
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x5, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.movRegReg x3, x5
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
    arm64.sub x5, x5, 24
    arm64.loadRegBaseDisp.word64 x5, [x5 + 16]
    arm64.cmp x5, 0
    arm64.b.ne cx2ddetach
  cx2ddone:
    arm64.lsr x5, x6, 5
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x5, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.movRegReg x3, x5
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
    arm64.sub x5, x5, 24
    arm64.loadRegBaseDisp.word64 x5, [x5 + 16]
    arm64.cmp x5, 0
    arm64.b.ne cx4ddetach
  cx4ddone:
    arm64.lsr x5, x6, 5
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x5, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.movRegReg x3, x5
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
    arm64.sub x5, x5, 24
    arm64.loadRegBaseDisp.word64 x5, [x5 + 16]
    arm64.cmp x5, 0
    arm64.b.ne cx8ddetach
  cx8ddone:
    arm64.lsr x5, x6, 5
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x5, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.movRegReg x3, x5
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
    arm64.sub x5, x5, 24
    arm64.loadRegBaseDisp.word64 x5, [x5 + 16]
    arm64.cmp x5, 0
    arm64.b.ne cx1ddetach
  cx1ddone:
    arm64.lsr x5, x6, 5
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x5, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.movRegReg x3, x5
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
    arm64.sub x5, x5, 24
    arm64.loadRegBaseDisp.word64 x5, [x5 + 16]
    arm64.cmp x5, 0
    arm64.b.ne cx2ddetach
  cx2ddone:
    arm64.lsr x5, x6, 5
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x5, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.movRegReg x3, x5
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
    arm64.sub x5, x5, 24
    arm64.loadRegBaseDisp.word64 x5, [x5 + 16]
    arm64.cmp x5, 0
    arm64.b.ne cx4ddetach
  cx4ddone:
    arm64.lsr x5, x6, 5
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x5, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.movRegReg x3, x5
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
    arm64.sub x5, x5, 24
    arm64.loadRegBaseDisp.word64 x5, [x5 + 16]
    arm64.cmp x5, 0
    arm64.b.ne cx8ddetach
  cx8ddone:
    arm64.lsr x5, x6, 5
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x5, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.movRegReg x3, x5
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
    x64.leaRegRegImm32 rsi, rsi, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
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

func @__cpufeat_aes_cipher_gcm_hash {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 80
    x64.loadRegBaseDisp.word64 rdi, [rdx + 8]
    x64.cmpRegImm32 rdi, 432
    x64.jcc less, ghbad
  kbbounded:
    x64.cmpRegImm32 rdi, 496
    x64.jcc greater, ghbad
  kbstepped:
    x64.andRegImm32 rdi, rdi, 31
    x64.cmpRegImm32 rdi, 16
    x64.jcc notEqual, ghbad
  gkmessage:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 8]
    x64.cmpRegImm32 rdi, 48
    x64.jcc notEqual, ghbad
  ghspan:
    x64.loadRegBaseDisp.word64 rdi, [rax + 8]
    x64.cmpRegReg r9, rdi
    x64.jcc above, ghbad
  ghinside:
    x64.subRegReg rdi, rdi, r9
    x64.cmpRegReg rdi, rsi
    x64.jcc below, ghbad
  ghcrypt:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, ghddetach
  ghdbuf:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, ghddone
  ghdshared:
    x64.leaRegRegImm32 rdi, rdi, -24
    x64.loadRegBaseDisp.word64 rdi, [rdi + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, ghddetach
  ghddone:
    x64.loadRegBaseDisp.word64 rdx, [rdx + 0]
    x64.loadRegBaseDisp.word64 r8, [rcx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegReg r9, rax, r9
    x64.movRegReg rcx, rsi
    x64.callDirect mrt_aes_gcm_hash
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
  ghddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, r9
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot r9, slot4
    x64.jmp ghddone
  ghbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_encrypt {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 80
    x64.loadRegBaseDisp.word64 rdi, [rax + 8]
    x64.cmpRegImm32 rdi, 432
    x64.jcc less, gcbad
  kbbounded:
    x64.cmpRegImm32 rdi, 496
    x64.jcc greater, gcbad
  kbstepped:
    x64.movRegReg r8, rdi
    x64.andRegImm32 r8, rdi, 31
    x64.cmpRegImm32 r8, 16
    x64.jcc notEqual, gcbad
  gkmessage:
    x64.loadRegBaseDisp.word64 r8, [r9 + 8]
    x64.cmpRegImm32 r8, 48
    x64.jcc notEqual, gcbad
  gcspan:
    x64.loadRegBaseDisp.word64 r8, [rcx + 8]
    x64.cmpRegReg rdx, r8
    x64.jcc above, gcbad
  gcinside:
    x64.subRegReg r8, r8, rdx
    x64.cmpRegReg r8, rsi
    x64.jcc below, gcbad
  gccrypt:
    x64.loadRegBaseDisp.word64 r8, [rcx + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc less, gbddetach
  gbdbuf:
    x64.loadRegBaseDisp.word64 r8, [rcx + 0]
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, gbddone
  gbdshared:
    x64.leaRegRegImm32 r8, r8, -24
    x64.loadRegBaseDisp.word64 r8, [r8 + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, gbddetach
  gbddone:
    x64.loadRegBaseDisp.word64 r8, [r9 + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc less, gmddetach
  gmdbuf:
    x64.loadRegBaseDisp.word64 r8, [r9 + 0]
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, gmddone
  gmdshared:
    x64.leaRegRegImm32 r8, r8, -24
    x64.loadRegBaseDisp.word64 r8, [r8 + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, gmddetach
  gmddone:
    x64.leaRegRegImm32 rdi, rdi, -256
    x64.shrRegImm8 rdi, rdi, 4
    x64.leaRegRegImm32 rdi, rdi, -1
    x64.shlRegImm8 rsi, rsi, 8
    x64.orRegReg rsi, rsi, rdi
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 r8, [r9 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.leaRegRegReg r9, rcx, rdx
    x64.movRegReg rcx, rsi
    x64.movRegReg rdx, rax
    x64.callDirect mrt_aes_gcm_encrypt
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
  gbddetach:
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
    x64.jmp gbddone
  gmddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, rdi
    x64.storeSlotReg slot5, r9
    x64.movRegReg rcx, r9
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot rdi, slot4
    x64.loadRegSlot r9, slot5
    x64.jmp gmddone
  gcbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_decrypt {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 80
    x64.loadRegBaseDisp.word64 rdi, [rax + 8]
    x64.cmpRegImm32 rdi, 432
    x64.jcc less, gcbad
  kbbounded:
    x64.cmpRegImm32 rdi, 496
    x64.jcc greater, gcbad
  kbstepped:
    x64.movRegReg r8, rdi
    x64.andRegImm32 r8, rdi, 31
    x64.cmpRegImm32 r8, 16
    x64.jcc notEqual, gcbad
  gkmessage:
    x64.loadRegBaseDisp.word64 r8, [r9 + 8]
    x64.cmpRegImm32 r8, 48
    x64.jcc notEqual, gcbad
  gcspan:
    x64.loadRegBaseDisp.word64 r8, [rcx + 8]
    x64.cmpRegReg rdx, r8
    x64.jcc above, gcbad
  gcinside:
    x64.subRegReg r8, r8, rdx
    x64.cmpRegReg r8, rsi
    x64.jcc below, gcbad
  gccrypt:
    x64.loadRegBaseDisp.word64 r8, [rcx + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc less, gbddetach
  gbdbuf:
    x64.loadRegBaseDisp.word64 r8, [rcx + 0]
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, gbddone
  gbdshared:
    x64.leaRegRegImm32 r8, r8, -24
    x64.loadRegBaseDisp.word64 r8, [r8 + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, gbddetach
  gbddone:
    x64.loadRegBaseDisp.word64 r8, [r9 + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc less, gmddetach
  gmdbuf:
    x64.loadRegBaseDisp.word64 r8, [r9 + 0]
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, gmddone
  gmdshared:
    x64.leaRegRegImm32 r8, r8, -24
    x64.loadRegBaseDisp.word64 r8, [r8 + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, gmddetach
  gmddone:
    x64.leaRegRegImm32 rdi, rdi, -256
    x64.shrRegImm8 rdi, rdi, 4
    x64.leaRegRegImm32 rdi, rdi, -1
    x64.shlRegImm8 rsi, rsi, 8
    x64.orRegReg rsi, rsi, rdi
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 r8, [r9 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.leaRegRegReg r9, rcx, rdx
    x64.movRegReg rcx, rsi
    x64.movRegReg rdx, rax
    x64.callDirect mrt_aes_gcm_decrypt
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
  gbddetach:
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
    x64.jmp gbddone
  gmddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, rdi
    x64.storeSlotReg slot5, r9
    x64.movRegReg rcx, r9
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot rdi, slot4
    x64.loadRegSlot r9, slot5
    x64.jmp gmddone
  gcbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_finish {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.loadRegBaseDisp.word64 rsi, [rdx + 8]
    x64.cmpRegImm32 rsi, 432
    x64.jcc less, gfbad
  kbbounded:
    x64.cmpRegImm32 rsi, 496
    x64.jcc greater, gfbad
  kbstepped:
    x64.andRegImm32 rsi, rsi, 31
    x64.cmpRegImm32 rsi, 16
    x64.jcc notEqual, gfbad
  gkmessage:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegImm32 rsi, 48
    x64.jcc notEqual, gfbad
  gfcrypt:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, gfddetach
  gfdbuf:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, gfddone
  gfdshared:
    x64.leaRegRegImm32 rsi, rsi, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, gfddetach
  gfddone:
    x64.loadRegBaseDisp.word64 rdx, [rdx + 0]
    x64.loadRegBaseDisp.word64 r8, [rcx + 0]
    x64.movRegReg rcx, rax
    x64.callDirect mrt_aes_gcm_finish
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  gfddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, r9
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot r9, slot3
    x64.jmp gfddone
  gfbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
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
    0000: 55 48 89 e5 48 81 ec d0 01 00 00 49 89 ca 49 c1
    0010: ea 08 49 89 cb 49 81 e3 ff 00 00 00 48 8d 0d 3d
    0020: 16 00 00 f3 41 0f 6f 08 f3 41 0f 6f 40 10 41 8b
    0030: 40 1c f3 0f 6f a2 00 01 00 00 8b ba 0c 01 00 00
    0040: 0f c8 0f cf 49 8d 72 0f 48 c1 ee 04 48 01 c6 0f
    0050: ce 41 89 70 1c 66 0f ef e0 f3 0f 7f a5 d0 fe ff
    0060: ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    0070: dc fe ff ff 49 81 fa 80 00 00 00 0f 82 e5 10 00
    0080: 00 f3 0f 7f b5 60 ff ff ff f3 0f 7f bd 70 ff ff
    0090: ff f3 44 0f 7f 45 80 f3 44 0f 7f 4d 90 f3 44 0f
    00a0: 7f 55 a0 f3 44 0f 7f 5d b0 f3 44 0f 7f 65 c0 f3
    00b0: 44 0f 7f 6d d0 f3 44 0f 7f 75 e0 f3 44 0f 7f 7d
    00c0: f0 f3 44 0f 6f 39 f3 44 0f 6f 71 10 44 0f 28 c1
    00d0: 44 0f 28 dc 66 45 0f ef c9 66 45 0f ef d2 49 81
    00e0: ea 80 00 00 00 f3 44 0f 7f 9d e0 fe ff ff 48 81
    00f0: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 ec fe ff
    0100: ff f3 44 0f 7f 9d f0 fe ff ff 48 81 c0 01 00 00
    0110: 00 89 c6 31 fe 0f ce 89 b5 fc fe ff ff f3 44 0f
    0120: 7f 9d 00 ff ff ff 48 81 c0 01 00 00 00 89 c6 31
    0130: fe 0f ce 89 b5 0c ff ff ff f3 44 0f 7f 9d 10 ff
    0140: ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0150: b5 1c ff ff ff f3 44 0f 7f 9d 20 ff ff ff 48 81
    0160: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 2c ff ff
    0170: ff f3 44 0f 7f 9d 30 ff ff ff 48 81 c0 01 00 00
    0180: 00 89 c6 31 fe 0f ce 89 b5 3c ff ff ff f3 44 0f
    0190: 7f 9d 40 ff ff ff 48 81 c0 01 00 00 00 89 c6 31
    01a0: fe 0f ce 89 b5 4c ff ff ff f3 0f 6f 85 d0 fe ff
    01b0: ff f3 0f 6f 8d e0 fe ff ff f3 0f 6f 95 f0 fe ff
    01c0: ff f3 0f 6f 9d 00 ff ff ff f3 0f 6f a5 10 ff ff
    01d0: ff f3 0f 6f ad 20 ff ff ff f3 0f 6f b5 30 ff ff
    01e0: ff f3 0f 6f bd 40 ff ff ff f3 44 0f 6f 9a 10 01
    01f0: 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41
    0200: 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3
    0210: 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38
    0220: dc fb 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0230: b5 dc fe ff ff f3 44 0f 6f 9a 20 01 00 00 66 41
    0240: 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3
    0250: 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38
    0260: dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 48 81
    0270: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 ec fe ff
    0280: ff f3 44 0f 6f 9a 30 01 00 00 66 41 0f 38 dc c3
    0290: 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38
    02a0: dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41
    02b0: 0f 38 dc f3 66 41 0f 38 dc fb 48 81 c0 01 00 00
    02c0: 00 89 c6 31 fe 0f ce 89 b5 fc fe ff ff f3 44 0f
    02d0: 6f 9a 40 01 00 00 66 41 0f 38 dc c3 66 41 0f 38
    02e0: dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41
    02f0: 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3
    0300: 66 41 0f 38 dc fb 48 81 c0 01 00 00 00 89 c6 31
    0310: fe 0f ce 89 b5 0c ff ff ff f3 44 0f 6f 9a 50 01
    0320: 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41
    0330: 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3
    0340: 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38
    0350: dc fb 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0360: b5 1c ff ff ff f3 44 0f 6f 9a 60 01 00 00 66 41
    0370: 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3
    0380: 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38
    0390: dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 48 81
    03a0: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 2c ff ff
    03b0: ff f3 44 0f 6f 9a 70 01 00 00 66 41 0f 38 dc c3
    03c0: 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38
    03d0: dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41
    03e0: 0f 38 dc f3 66 41 0f 38 dc fb 48 81 c0 01 00 00
    03f0: 00 89 c6 31 fe 0f ce 89 b5 3c ff ff ff f3 44 0f
    0400: 6f 9a 80 01 00 00 66 41 0f 38 dc c3 66 41 0f 38
    0410: dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41
    0420: 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3
    0430: 66 41 0f 38 dc fb 48 81 c0 01 00 00 00 89 c6 31
    0440: fe 0f ce 89 b5 4c ff ff ff f3 44 0f 6f 9a 90 01
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
    0670: 85 50 fe ff ff f3 0f 7f 8d 60 fe ff ff f3 0f 7f
    0680: 95 70 fe ff ff f3 0f 7f 9d 80 fe ff ff f3 0f 7f
    0690: a5 90 fe ff ff f3 0f 7f ad a0 fe ff ff f3 0f 7f
    06a0: b5 b0 fe ff ff f3 0f 7f bd c0 fe ff ff 49 81 c1
    06b0: 80 00 00 00 49 81 fa 80 00 00 00 0f 82 b4 07 00
    06c0: 00 49 81 ea 80 00 00 00 f3 0f 6f 85 d0 fe ff ff
    06d0: f3 0f 6f 8d e0 fe ff ff f3 0f 6f 95 f0 fe ff ff
    06e0: f3 0f 6f 9d 00 ff ff ff f3 0f 6f a5 10 ff ff ff
    06f0: f3 0f 6f ad 20 ff ff ff f3 0f 6f b5 30 ff ff ff
    0700: f3 0f 6f bd 40 ff ff ff f3 44 0f 6f 9d 50 fe ff
    0710: ff f3 44 0f 6f 02 f3 44 0f 6f 52 10 45 0f 28 c8
    0720: 66 45 0f 70 e3 4e 66 45 0f ef e3 66 45 0f 3a 44
    0730: d4 00 66 45 0f 3a 44 c3 00 66 45 0f 3a 44 cb 11
    0740: f3 44 0f 6f 9a 10 01 00 00 66 41 0f 38 dc c3 66
    0750: 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc
    0760: db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f
    0770: 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9d 60 fe
    0780: ff ff f3 44 0f 6f 62 20 45 0f 28 ec 66 45 0f 3a
    0790: 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66
    07a0: 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3
    07b0: 44 0f 6f 62 30 66 45 0f 3a 44 e3 00 66 45 0f ef
    07c0: d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    07d0: dc fe ff ff f3 44 0f 6f 9a 20 01 00 00 66 41 0f
    07e0: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    07f0: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    0800: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f
    0810: 6f 9d 70 fe ff ff f3 44 0f 6f 62 40 45 0f 28 ec
    0820: 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a
    0830: 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45
    0840: 0f ef dc f3 44 0f 6f 62 50 66 45 0f 3a 44 e3 00
    0850: 66 45 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe
    0860: 0f ce 89 b5 ec fe ff ff f3 44 0f 6f 9a 30 01 00
    0870: 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f
    0880: 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66
    0890: 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc
    08a0: fb f3 44 0f 6f 9d 80 fe ff ff f3 44 0f 6f 62 60
    08b0: 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4
    08c0: 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70
    08d0: e3 4e 66 45 0f ef dc f3 44 0f 6f 62 70 66 45 0f
    08e0: 3a 44 e3 00 66 45 0f ef d4 48 81 c0 01 00 00 00
    08f0: 89 c6 31 fe 0f ce 89 b5 fc fe ff ff f3 44 0f 6f
    0900: 9a 40 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc
    0910: cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f
    0920: 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66
    0930: 41 0f 38 dc fb f3 44 0f 6f 9d 90 fe ff ff f3 44
    0940: 0f 6f a2 80 00 00 00 45 0f 28 ec 66 45 0f 3a 44
    0950: e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45
    0960: 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44
    0970: 0f 6f a2 90 00 00 00 66 45 0f 3a 44 e3 00 66 45
    0980: 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce
    0990: 89 b5 0c ff ff ff f3 44 0f 6f 9a 50 01 00 00 66
    09a0: 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc
    09b0: d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f
    09c0: 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3
    09d0: 44 0f 6f 9d a0 fe ff ff f3 44 0f 6f a2 a0 00 00
    09e0: 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef
    09f0: c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f
    0a00: 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 b0 00 00
    0a10: 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81 c0
    0a20: 01 00 00 00 89 c6 31 fe 0f ce 89 b5 1c ff ff ff
    0a30: f3 44 0f 6f 9a 60 01 00 00 66 41 0f 38 dc c3 66
    0a40: 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc
    0a50: db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f
    0a60: 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9d b0 fe
    0a70: ff ff f3 44 0f 6f a2 c0 00 00 00 45 0f 28 ec 66
    0a80: 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44
    0a90: eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f
    0aa0: ef dc f3 44 0f 6f a2 d0 00 00 00 66 45 0f 3a 44
    0ab0: e3 00 66 45 0f ef d4 48 81 c0 01 00 00 00 89 c6
    0ac0: 31 fe 0f ce 89 b5 2c ff ff ff f3 44 0f 6f 9a 70
    0ad0: 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66
    0ae0: 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc
    0af0: e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f
    0b00: 38 dc fb f3 44 0f 6f 9d c0 fe ff ff f3 44 0f 6f
    0b10: a2 e0 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00
    0b20: 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef
    0b30: cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f
    0b40: a2 f0 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef
    0b50: d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    0b60: 3c ff ff ff f3 44 0f 6f 9a 80 01 00 00 66 41 0f
    0b70: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    0b80: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    0b90: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 48 81 c0
    0ba0: 01 00 00 00 89 c6 31 fe 0f ce 89 b5 4c ff ff ff
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
    0e20: 7f 79 70 66 41 0f 38 00 ff f3 0f 7f 85 50 fe ff
    0e30: ff f3 0f 7f 8d 60 fe ff ff f3 0f 7f 95 70 fe ff
    0e40: ff f3 0f 7f 9d 80 fe ff ff f3 0f 7f a5 90 fe ff
    0e50: ff f3 0f 7f ad a0 fe ff ff f3 0f 7f b5 b0 fe ff
    0e60: ff f3 0f 7f bd c0 fe ff ff 49 81 c1 80 00 00 00
    0e70: e9 3f f8 ff ff f3 44 0f 6f 9d 50 fe ff ff f3 44
    0e80: 0f 6f 02 f3 44 0f 6f 52 10 45 0f 28 c8 66 45 0f
    0e90: 70 e3 4e 66 45 0f ef e3 66 45 0f 3a 44 d4 00 66
    0ea0: 45 0f 3a 44 c3 00 66 45 0f 3a 44 cb 11 f3 44 0f
    0eb0: 6f 9d 60 fe ff ff f3 44 0f 6f 62 20 45 0f 28 ec
    0ec0: 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a
    0ed0: 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45
    0ee0: 0f ef dc f3 44 0f 6f 62 30 66 45 0f 3a 44 e3 00
    0ef0: 66 45 0f ef d4 f3 44 0f 6f 9d 70 fe ff ff f3 44
    0f00: 0f 6f 62 40 45 0f 28 ec 66 45 0f 3a 44 e3 00 66
    0f10: 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd
    0f20: 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f 62
    0f30: 50 66 45 0f 3a 44 e3 00 66 45 0f ef d4 f3 44 0f
    0f40: 6f 9d 80 fe ff ff f3 44 0f 6f 62 60 45 0f 28 ec
    0f50: 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a
    0f60: 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45
    0f70: 0f ef dc f3 44 0f 6f 62 70 66 45 0f 3a 44 e3 00
    0f80: 66 45 0f ef d4 f3 44 0f 6f 9d 90 fe ff ff f3 44
    0f90: 0f 6f a2 80 00 00 00 45 0f 28 ec 66 45 0f 3a 44
    0fa0: e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45
    0fb0: 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44
    0fc0: 0f 6f a2 90 00 00 00 66 45 0f 3a 44 e3 00 66 45
    0fd0: 0f ef d4 f3 44 0f 6f 9d a0 fe ff ff f3 44 0f 6f
    0fe0: a2 a0 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00
    0ff0: 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef
    1000: cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f
    1010: a2 b0 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef
    1020: d4 f3 44 0f 6f 9d b0 fe ff ff f3 44 0f 6f a2 c0
    1030: 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45
    1040: 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66
    1050: 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 d0
    1060: 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 f3
    1070: 44 0f 6f 9d c0 fe ff ff f3 44 0f 6f a2 e0 00 00
    1080: 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef
    1090: c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f
    10a0: 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 f0 00 00
    10b0: 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 66 45 0f
    10c0: ef d0 66 45 0f ef d1 45 0f 28 da 66 41 0f 73 da
    10d0: 08 66 41 0f 73 fb 08 66 45 0f ef ca 66 45 0f ef
    10e0: c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45 0f 70
    10f0: c0 4e 66 45 0f ef c3 45 0f 28 de 66 45 0f 3a 44
    1100: d8 01 66 45 0f 70 c0 4e 66 45 0f ef c3 66 45 0f
    1110: ef c1 41 0f 28 c8 f3 0f 6f b5 60 ff ff ff f3 0f
    1120: 6f bd 70 ff ff ff f3 44 0f 6f 45 80 f3 44 0f 6f
    1130: 4d 90 f3 44 0f 6f 55 a0 f3 44 0f 6f 5d b0 f3 44
    1140: 0f 6f 65 c0 f3 44 0f 6f 6d d0 f3 44 0f 6f 75 e0
    1150: f3 44 0f 6f 7d f0 4d 85 d2 0f 84 f0 04 00 00 48
    1160: 81 e8 07 00 00 00 49 81 fa 10 00 00 00 0f 82 09
    1170: 02 00 00 31 c9 49 81 ea 10 00 00 00 f3 0f 6f 85
    1180: d0 fe ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f
    1190: ce 89 b5 dc fe ff ff f3 0f 6f a2 10 01 00 00 66
    11a0: 0f 38 dc c4 f3 0f 6f a2 20 01 00 00 66 0f 38 dc
    11b0: c4 f3 0f 6f a2 30 01 00 00 66 0f 38 dc c4 f3 0f
    11c0: 6f a2 40 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 50
    11d0: 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 60 01 00 00
    11e0: 66 0f 38 dc c4 f3 0f 6f a2 70 01 00 00 66 0f 38
    11f0: dc c4 f3 0f 6f a2 80 01 00 00 66 0f 38 dc c4 f3
    1200: 0f 6f a2 90 01 00 00 66 0f 38 dc c4 f3 0f 6f a2
    1210: a0 01 00 00 49 81 fb 0c 00 00 00 0f 82 3a 00 00
    1220: 00 66 0f 38 dc c4 f3 0f 6f a2 b0 01 00 00 66 0f
    1230: 38 dc c4 f3 0f 6f a2 c0 01 00 00 0f 84 1a 00 00
    1240: 00 66 0f 38 dc c4 f3 0f 6f a2 d0 01 00 00 66 0f
    1250: 38 dc c4 f3 0f 6f a2 e0 01 00 00 66 0f 38 dd c4
    1260: f3 41 0f 6f 29 66 0f ef c5 f3 41 0f 7f 01 48 8d
    1270: 35 eb 03 00 00 f3 0f 6f 2e 66 0f 38 00 c5 f3 0f
    1280: 7f 84 0d 50 fe ff ff 49 81 c1 10 00 00 00 48 81
    1290: c1 10 00 00 00 49 81 fa 10 00 00 00 0f 83 d3 fe
    12a0: ff ff 48 01 c9 31 f6 48 81 c6 00 01 00 00 48 29
    12b0: ce 31 c9 f3 0f 6f 85 50 fe ff ff 66 0f ef c1 f3
    12c0: 0f 7f 85 50 fe ff ff 66 0f ef c9 66 0f ef d2 66
    12d0: 0f ef db f3 0f 6f 84 0d 50 fe ff ff f3 0f 6f 2c
    12e0: 32 0f 28 e5 66 0f 3a 44 e0 00 66 0f ef cc 66 0f
    12f0: 3a 44 e8 11 66 0f ef d5 66 0f 70 e0 4e 66 0f ef
    1300: e0 f3 0f 6f 6c 32 10 66 0f 3a 44 ec 00 66 0f ef
    1310: dd 48 81 c1 10 00 00 00 48 81 c6 20 00 00 00 48
    1320: 81 fe 00 01 00 00 0f 82 a7 ff ff ff 48 8d 0d 2d
    1330: 03 00 00 66 0f ef d9 66 0f ef da 0f 28 e3 66 0f
    1340: 73 db 08 66 0f 73 fc 08 66 0f ef d3 66 0f ef cc
    1350: f3 0f 6f 61 10 66 0f 3a 44 e1 01 66 0f 70 c9 4e
    1360: 66 0f ef cc f3 0f 6f 61 10 66 0f 3a 44 e1 01 66
    1370: 0f 70 c9 4e 66 0f ef cc 66 0f ef ca 4d 85 d2 0f
    1380: 84 ca 02 00 00 48 8d 8d 50 ff ff ff 4c 89 d6 48
    1390: 81 e6 08 00 00 00 0f 84 14 00 00 00 49 8b 31 48
    13a0: 89 31 49 81 c1 08 00 00 00 48 81 c1 08 00 00 00
    13b0: 4c 89 d6 48 81 e6 04 00 00 00 0f 84 13 00 00 00
    13c0: 41 8b 31 89 31 49 81 c1 04 00 00 00 48 81 c1 04
    13d0: 00 00 00 4c 89 d6 48 81 e6 02 00 00 00 0f 84 15
    13e0: 00 00 00 49 0f b7 31 66 89 31 49 81 c1 02 00 00
    13f0: 00 48 81 c1 02 00 00 00 4c 89 d6 48 81 e6 01 00
    1400: 00 00 0f 84 15 00 00 00 49 0f b6 31 40 88 31 49
    1410: 81 c1 01 00 00 00 48 81 c1 01 00 00 00 f3 0f 6f
    1420: ad 50 ff ff ff 4d 29 d1 f3 0f 6f 85 d0 fe ff ff
    1430: 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 dc
    1440: fe ff ff f3 0f 6f a2 10 01 00 00 66 0f 38 dc c4
    1450: f3 0f 6f a2 20 01 00 00 66 0f 38 dc c4 f3 0f 6f
    1460: a2 30 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 40 01
    1470: 00 00 66 0f 38 dc c4 f3 0f 6f a2 50 01 00 00 66
    1480: 0f 38 dc c4 f3 0f 6f a2 60 01 00 00 66 0f 38 dc
    1490: c4 f3 0f 6f a2 70 01 00 00 66 0f 38 dc c4 f3 0f
    14a0: 6f a2 80 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 90
    14b0: 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 a0 01 00 00
    14c0: 49 81 fb 0c 00 00 00 0f 82 3a 00 00 00 66 0f 38
    14d0: dc c4 f3 0f 6f a2 b0 01 00 00 66 0f 38 dc c4 f3
    14e0: 0f 6f a2 c0 01 00 00 0f 84 1a 00 00 00 66 0f 38
    14f0: dc c4 f3 0f 6f a2 d0 01 00 00 66 0f 38 dc c4 f3
    1500: 0f 6f a2 e0 01 00 00 66 0f 38 dd c4 66 0f ef c5
    1510: f3 0f 7f 85 50 ff ff ff 48 8d 8d 50 ff ff ff 4c
    1520: 89 d6 48 81 e6 08 00 00 00 0f 84 14 00 00 00 48
    1530: 8b 31 49 89 31 48 81 c1 08 00 00 00 49 81 c1 08
    1540: 00 00 00 4c 89 d6 48 81 e6 04 00 00 00 0f 84 13
    1550: 00 00 00 8b 31 41 89 31 48 81 c1 04 00 00 00 49
    1560: 81 c1 04 00 00 00 4c 89 d6 48 81 e6 02 00 00 00
    1570: 0f 84 16 00 00 00 48 0f b7 31 66 41 89 31 48 81
    1580: c1 02 00 00 00 49 81 c1 02 00 00 00 4c 89 d6 48
    1590: 81 e6 01 00 00 00 0f 84 15 00 00 00 48 0f b6 31
    15a0: 41 88 31 48 81 c1 01 00 00 00 49 81 c1 01 00 00
    15b0: 00 48 8d 35 a8 00 00 00 4c 29 d6 f3 0f 6f 6e 30
    15c0: 66 0f db c5 48 8d 0d 95 00 00 00 f3 0f 6f 29 66
    15d0: 0f 38 00 c5 66 0f ef c1 f3 0f 6f 8a e0 00 00 00
    15e0: 0f 28 d1 f3 0f 6f 9a f0 00 00 00 66 0f 3a 44 c8
    15f0: 00 66 0f 3a 44 d0 11 66 0f 70 e0 4e 66 0f ef e0
    1600: 66 0f 3a 44 dc 00 66 0f ef d9 66 0f ef da 0f 28
    1610: e3 66 0f 73 db 08 66 0f 73 fc 08 66 0f ef d3 66
    1620: 0f ef cc f3 0f 6f 61 10 66 0f 3a 44 e1 01 66 0f
    1630: 70 c9 4e 66 0f ef cc f3 0f 6f 61 10 66 0f 3a 44
    1640: e1 01 66 0f 70 c9 4e 66 0f ef cc 66 0f ef ca f3
    1650: 41 0f 7f 08 31 c0 48 81 c4 d0 01 00 00 5d c3 00
    1660: 0f 0e 0d 0c 0b 0a 09 08 07 06 05 04 03 02 01 00
    1670: 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 c2
    1680: ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff
    1690: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
    16a0: 00
}

chunk @mrt_aes_gcm_decrypt {
    0000: 55 48 89 e5 48 81 ec c0 01 00 00 49 89 ca 49 c1
    0010: ea 08 49 89 cb 49 81 e3 ff 00 00 00 48 8d 0d 39
    0020: 0e 00 00 f3 41 0f 6f 08 f3 41 0f 6f 40 10 41 8b
    0030: 40 1c f3 0f 6f a2 00 01 00 00 8b ba 0c 01 00 00
    0040: 0f c8 0f cf 49 8d 72 0f 48 c1 ee 04 48 01 c6 0f
    0050: ce 41 89 70 1c 66 0f ef e0 f3 0f 7f a5 60 fe ff
    0060: ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    0070: 6c fe ff ff 49 81 fa 80 00 00 00 0f 82 d4 08 00
    0080: 00 f3 0f 7f b5 60 ff ff ff f3 0f 7f bd 70 ff ff
    0090: ff f3 44 0f 7f 45 80 f3 44 0f 7f 4d 90 f3 44 0f
    00a0: 7f 55 a0 f3 44 0f 7f 5d b0 f3 44 0f 7f 65 c0 f3
    00b0: 44 0f 7f 6d d0 f3 44 0f 7f 75 e0 f3 44 0f 7f 7d
    00c0: f0 f3 44 0f 6f 39 f3 44 0f 6f 71 10 44 0f 28 c1
    00d0: 44 0f 28 dc 66 45 0f ef c9 66 45 0f ef d2 f3 44
    00e0: 0f 7f 9d 70 fe ff ff 48 81 c0 01 00 00 00 89 c6
    00f0: 31 fe 0f ce 89 b5 7c fe ff ff f3 44 0f 7f 9d 80
    0100: fe ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce
    0110: 89 b5 8c fe ff ff f3 44 0f 7f 9d 90 fe ff ff 48
    0120: 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 9c fe
    0130: ff ff f3 44 0f 7f 9d a0 fe ff ff 48 81 c0 01 00
    0140: 00 00 89 c6 31 fe 0f ce 89 b5 ac fe ff ff f3 44
    0150: 0f 7f 9d b0 fe ff ff 48 81 c0 01 00 00 00 89 c6
    0160: 31 fe 0f ce 89 b5 bc fe ff ff f3 44 0f 7f 9d c0
    0170: fe ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce
    0180: 89 b5 cc fe ff ff f3 44 0f 7f 9d d0 fe ff ff 48
    0190: 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 dc fe
    01a0: ff ff 49 81 fa 80 00 00 00 0f 82 5b 07 00 00 49
    01b0: 81 ea 80 00 00 00 f3 0f 6f 85 60 fe ff ff f3 0f
    01c0: 6f 8d 70 fe ff ff f3 0f 6f 95 80 fe ff ff f3 0f
    01d0: 6f 9d 90 fe ff ff f3 0f 6f a5 a0 fe ff ff f3 0f
    01e0: 6f ad b0 fe ff ff f3 0f 6f b5 c0 fe ff ff f3 0f
    01f0: 6f bd d0 fe ff ff f3 45 0f 6f 19 66 45 0f 38 00
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
    02c0: 89 c6 31 fe 0f ce 89 b5 6c fe ff ff f3 44 0f 6f
    02d0: 9a 20 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc
    02e0: cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f
    02f0: 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66
    0300: 41 0f 38 dc fb f3 45 0f 6f 59 20 66 45 0f 38 00
    0310: df f3 44 0f 6f 62 40 45 0f 28 ec 66 45 0f 3a 44
    0320: e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45
    0330: 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44
    0340: 0f 6f 62 50 66 45 0f 3a 44 e3 00 66 45 0f ef d4
    0350: 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 7c
    0360: fe ff ff f3 44 0f 6f 9a 30 01 00 00 66 41 0f 38
    0370: dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41
    0380: 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb
    0390: 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 45 0f 6f
    03a0: 59 30 66 45 0f 38 00 df f3 44 0f 6f 62 60 45 0f
    03b0: 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45
    03c0: 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e
    03d0: 66 45 0f ef dc f3 44 0f 6f 62 70 66 45 0f 3a 44
    03e0: e3 00 66 45 0f ef d4 48 81 c0 01 00 00 00 89 c6
    03f0: 31 fe 0f ce 89 b5 8c fe ff ff f3 44 0f 6f 9a 40
    0400: 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66
    0410: 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc
    0420: e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f
    0430: 38 dc fb f3 45 0f 6f 59 40 66 45 0f 38 00 df f3
    0440: 44 0f 6f a2 80 00 00 00 45 0f 28 ec 66 45 0f 3a
    0450: 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66
    0460: 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3
    0470: 44 0f 6f a2 90 00 00 00 66 45 0f 3a 44 e3 00 66
    0480: 45 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f
    0490: ce 89 b5 9c fe ff ff f3 44 0f 6f 9a 50 01 00 00
    04a0: 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38
    04b0: dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41
    04c0: 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb
    04d0: f3 45 0f 6f 59 50 66 45 0f 38 00 df f3 44 0f 6f
    04e0: a2 a0 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00
    04f0: 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef
    0500: cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f
    0510: a2 b0 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef
    0520: d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    0530: ac fe ff ff f3 44 0f 6f 9a 60 01 00 00 66 41 0f
    0540: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    0550: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    0560: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 45 0f
    0570: 6f 59 60 66 45 0f 38 00 df f3 44 0f 6f a2 c0 00
    0580: 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f
    0590: ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45
    05a0: 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 d0 00
    05b0: 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81
    05c0: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 bc fe ff
    05d0: ff f3 44 0f 6f 9a 70 01 00 00 66 41 0f 38 dc c3
    05e0: 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38
    05f0: dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41
    0600: 0f 38 dc f3 66 41 0f 38 dc fb f3 45 0f 6f 59 70
    0610: 66 45 0f 38 00 df f3 44 0f 6f a2 e0 00 00 00 45
    0620: 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66
    0630: 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3
    0640: 4e 66 45 0f ef dc f3 44 0f 6f a2 f0 00 00 00 66
    0650: 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81 c0 01 00
    0660: 00 00 89 c6 31 fe 0f ce 89 b5 cc fe ff ff f3 44
    0670: 0f 6f 9a 80 01 00 00 66 41 0f 38 dc c3 66 41 0f
    0680: 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66
    0690: 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc
    06a0: f3 66 41 0f 38 dc fb 48 81 c0 01 00 00 00 89 c6
    06b0: 31 fe 0f ce 89 b5 dc fe ff ff 66 45 0f ef d0 66
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
    0900: c1 80 00 00 00 e9 98 f8 ff ff 41 0f 28 c8 f3 0f
    0910: 6f b5 60 ff ff ff f3 0f 6f bd 70 ff ff ff f3 44
    0920: 0f 6f 45 80 f3 44 0f 6f 4d 90 f3 44 0f 6f 55 a0
    0930: f3 44 0f 6f 5d b0 f3 44 0f 6f 65 c0 f3 44 0f 6f
    0940: 6d d0 f3 44 0f 6f 75 e0 f3 44 0f 6f 7d f0 48 81
    0950: e8 07 00 00 00 49 81 fa 10 00 00 00 0f 82 0e 02
    0960: 00 00 31 c9 49 81 ea 10 00 00 00 f3 41 0f 6f 01
    0970: 48 8d 35 e5 04 00 00 f3 0f 6f 2e 66 0f 38 00 c5
    0980: f3 0f 7f 84 0d f0 fe ff ff f3 0f 6f 85 60 fe ff
    0990: ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    09a0: 6c fe ff ff f3 0f 6f a2 10 01 00 00 66 0f 38 dc
    09b0: c4 f3 0f 6f a2 20 01 00 00 66 0f 38 dc c4 f3 0f
    09c0: 6f a2 30 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 40
    09d0: 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 50 01 00 00
    09e0: 66 0f 38 dc c4 f3 0f 6f a2 60 01 00 00 66 0f 38
    09f0: dc c4 f3 0f 6f a2 70 01 00 00 66 0f 38 dc c4 f3
    0a00: 0f 6f a2 80 01 00 00 66 0f 38 dc c4 f3 0f 6f a2
    0a10: 90 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 a0 01 00
    0a20: 00 49 81 fb 0c 00 00 00 0f 82 3a 00 00 00 66 0f
    0a30: 38 dc c4 f3 0f 6f a2 b0 01 00 00 66 0f 38 dc c4
    0a40: f3 0f 6f a2 c0 01 00 00 0f 84 1a 00 00 00 66 0f
    0a50: 38 dc c4 f3 0f 6f a2 d0 01 00 00 66 0f 38 dc c4
    0a60: f3 0f 6f a2 e0 01 00 00 66 0f 38 dd c4 f3 41 0f
    0a70: 6f 29 66 0f ef c5 f3 41 0f 7f 01 49 81 c1 10 00
    0a80: 00 00 48 81 c1 10 00 00 00 49 81 fa 10 00 00 00
    0a90: 0f 83 ce fe ff ff 48 01 c9 31 f6 48 81 c6 00 01
    0aa0: 00 00 48 29 ce 31 c9 f3 0f 6f 85 f0 fe ff ff 66
    0ab0: 0f ef c1 f3 0f 7f 85 f0 fe ff ff 66 0f ef c9 66
    0ac0: 0f ef d2 66 0f ef db f3 0f 6f 84 0d f0 fe ff ff
    0ad0: f3 0f 6f 2c 32 0f 28 e5 66 0f 3a 44 e0 00 66 0f
    0ae0: ef cc 66 0f 3a 44 e8 11 66 0f ef d5 66 0f 70 e0
    0af0: 4e 66 0f ef e0 f3 0f 6f 6c 32 10 66 0f 3a 44 ec
    0b00: 00 66 0f ef dd 48 81 c1 10 00 00 00 48 81 c6 20
    0b10: 00 00 00 48 81 fe 00 01 00 00 0f 82 a7 ff ff ff
    0b20: 48 8d 0d 35 03 00 00 66 0f ef d9 66 0f ef da 0f
    0b30: 28 e3 66 0f 73 db 08 66 0f 73 fc 08 66 0f ef d3
    0b40: 66 0f ef cc f3 0f 6f 61 10 66 0f 3a 44 e1 01 66
    0b50: 0f 70 c9 4e 66 0f ef cc f3 0f 6f 61 10 66 0f 3a
    0b60: 44 e1 01 66 0f 70 c9 4e 66 0f ef cc 66 0f ef ca
    0b70: 4d 85 d2 0f 84 d2 02 00 00 48 8d 8d e0 fe ff ff
    0b80: 4c 89 d6 48 81 e6 08 00 00 00 0f 84 14 00 00 00
    0b90: 49 8b 31 48 89 31 49 81 c1 08 00 00 00 48 81 c1
    0ba0: 08 00 00 00 4c 89 d6 48 81 e6 04 00 00 00 0f 84
    0bb0: 13 00 00 00 41 8b 31 89 31 49 81 c1 04 00 00 00
    0bc0: 48 81 c1 04 00 00 00 4c 89 d6 48 81 e6 02 00 00
    0bd0: 00 0f 84 15 00 00 00 49 0f b7 31 66 89 31 49 81
    0be0: c1 02 00 00 00 48 81 c1 02 00 00 00 4c 89 d6 48
    0bf0: 81 e6 01 00 00 00 0f 84 15 00 00 00 49 0f b6 31
    0c00: 40 88 31 49 81 c1 01 00 00 00 48 81 c1 01 00 00
    0c10: 00 f3 0f 6f 85 e0 fe ff ff 4d 29 d1 48 8d 35 39
    0c20: 02 00 00 4c 29 d6 f3 0f 6f 6e 30 66 0f db c5 48
    0c30: 8d 0d 26 02 00 00 f3 0f 6f 29 66 0f 38 00 c5 66
    0c40: 0f ef c1 f3 0f 6f 8a e0 00 00 00 0f 28 d1 f3 0f
    0c50: 6f 9a f0 00 00 00 66 0f 3a 44 c8 00 66 0f 3a 44
    0c60: d0 11 66 0f 70 e0 4e 66 0f ef e0 66 0f 3a 44 dc
    0c70: 00 66 0f ef d9 66 0f ef da 0f 28 e3 66 0f 73 db
    0c80: 08 66 0f 73 fc 08 66 0f ef d3 66 0f ef cc f3 0f
    0c90: 6f 61 10 66 0f 3a 44 e1 01 66 0f 70 c9 4e 66 0f
    0ca0: ef cc f3 0f 6f 61 10 66 0f 3a 44 e1 01 66 0f 70
    0cb0: c9 4e 66 0f ef cc 66 0f ef ca f3 0f 6f 85 60 fe
    0cc0: ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0cd0: b5 6c fe ff ff f3 0f 6f a2 10 01 00 00 66 0f 38
    0ce0: dc c4 f3 0f 6f a2 20 01 00 00 66 0f 38 dc c4 f3
    0cf0: 0f 6f a2 30 01 00 00 66 0f 38 dc c4 f3 0f 6f a2
    0d00: 40 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 50 01 00
    0d10: 00 66 0f 38 dc c4 f3 0f 6f a2 60 01 00 00 66 0f
    0d20: 38 dc c4 f3 0f 6f a2 70 01 00 00 66 0f 38 dc c4
    0d30: f3 0f 6f a2 80 01 00 00 66 0f 38 dc c4 f3 0f 6f
    0d40: a2 90 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 a0 01
    0d50: 00 00 49 81 fb 0c 00 00 00 0f 82 3a 00 00 00 66
    0d60: 0f 38 dc c4 f3 0f 6f a2 b0 01 00 00 66 0f 38 dc
    0d70: c4 f3 0f 6f a2 c0 01 00 00 0f 84 1a 00 00 00 66
    0d80: 0f 38 dc c4 f3 0f 6f a2 d0 01 00 00 66 0f 38 dc
    0d90: c4 f3 0f 6f a2 e0 01 00 00 66 0f 38 dd c4 f3 0f
    0da0: 6f ad e0 fe ff ff 66 0f ef c5 f3 0f 7f 85 e0 fe
    0db0: ff ff 48 8d 8d e0 fe ff ff 4c 89 d6 48 81 e6 08
    0dc0: 00 00 00 0f 84 14 00 00 00 48 8b 31 49 89 31 48
    0dd0: 81 c1 08 00 00 00 49 81 c1 08 00 00 00 4c 89 d6
    0de0: 48 81 e6 04 00 00 00 0f 84 13 00 00 00 8b 31 41
    0df0: 89 31 48 81 c1 04 00 00 00 49 81 c1 04 00 00 00
    0e00: 4c 89 d6 48 81 e6 02 00 00 00 0f 84 16 00 00 00
    0e10: 48 0f b7 31 66 41 89 31 48 81 c1 02 00 00 00 49
    0e20: 81 c1 02 00 00 00 4c 89 d6 48 81 e6 01 00 00 00
    0e30: 0f 84 15 00 00 00 48 0f b6 31 41 88 31 48 81 c1
    0e40: 01 00 00 00 49 81 c1 01 00 00 00 f3 41 0f 7f 08
    0e50: 31 c0 48 81 c4 c0 01 00 00 5d c3 00 0f 0e 0d 0c
    0e60: 0b 0a 09 08 07 06 05 04 03 02 01 00 01 00 00 00
    0e70: 00 00 00 00 00 00 00 00 00 00 00 c2 ff ff ff ff
    0e80: ff ff ff ff ff ff ff ff ff ff ff ff 00 00 00 00
    0e90: 00 00 00 00 00 00 00 00 00 00 00 00 00
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
    x64.leaRegRegImm32 rsi, rsi, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
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

func @__cpufeat_aes_cipher_gcm_hash {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 80
    x64.loadRegBaseDisp.word64 rdi, [rdx + 8]
    x64.cmpRegImm32 rdi, 432
    x64.jcc less, ghbad
  kbbounded:
    x64.cmpRegImm32 rdi, 496
    x64.jcc greater, ghbad
  kbstepped:
    x64.andRegImm32 rdi, rdi, 31
    x64.cmpRegImm32 rdi, 16
    x64.jcc notEqual, ghbad
  gkmessage:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 8]
    x64.cmpRegImm32 rdi, 48
    x64.jcc notEqual, ghbad
  ghspan:
    x64.loadRegBaseDisp.word64 rdi, [rax + 8]
    x64.cmpRegReg r9, rdi
    x64.jcc above, ghbad
  ghinside:
    x64.subRegReg rdi, rdi, r9
    x64.cmpRegReg rdi, rsi
    x64.jcc below, ghbad
  ghcrypt:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, ghddetach
  ghdbuf:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, ghddone
  ghdshared:
    x64.leaRegRegImm32 rdi, rdi, -24
    x64.loadRegBaseDisp.word64 rdi, [rdi + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, ghddetach
  ghddone:
    x64.loadRegBaseDisp.word64 rdx, [rdx + 0]
    x64.loadRegBaseDisp.word64 r8, [rcx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegReg r9, rax, r9
    x64.movRegReg rcx, rsi
    x64.callDirect mrt_aes_gcm_hash
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
  ghddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, r9
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot r9, slot4
    x64.jmp ghddone
  ghbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_encrypt {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 80
    x64.loadRegBaseDisp.word64 rdi, [rax + 8]
    x64.cmpRegImm32 rdi, 432
    x64.jcc less, gcbad
  kbbounded:
    x64.cmpRegImm32 rdi, 496
    x64.jcc greater, gcbad
  kbstepped:
    x64.movRegReg r8, rdi
    x64.andRegImm32 r8, rdi, 31
    x64.cmpRegImm32 r8, 16
    x64.jcc notEqual, gcbad
  gkmessage:
    x64.loadRegBaseDisp.word64 r8, [r9 + 8]
    x64.cmpRegImm32 r8, 48
    x64.jcc notEqual, gcbad
  gcspan:
    x64.loadRegBaseDisp.word64 r8, [rcx + 8]
    x64.cmpRegReg rdx, r8
    x64.jcc above, gcbad
  gcinside:
    x64.subRegReg r8, r8, rdx
    x64.cmpRegReg r8, rsi
    x64.jcc below, gcbad
  gccrypt:
    x64.loadRegBaseDisp.word64 r8, [rcx + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc less, gbddetach
  gbdbuf:
    x64.loadRegBaseDisp.word64 r8, [rcx + 0]
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, gbddone
  gbdshared:
    x64.leaRegRegImm32 r8, r8, -24
    x64.loadRegBaseDisp.word64 r8, [r8 + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, gbddetach
  gbddone:
    x64.loadRegBaseDisp.word64 r8, [r9 + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc less, gmddetach
  gmdbuf:
    x64.loadRegBaseDisp.word64 r8, [r9 + 0]
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, gmddone
  gmdshared:
    x64.leaRegRegImm32 r8, r8, -24
    x64.loadRegBaseDisp.word64 r8, [r8 + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, gmddetach
  gmddone:
    x64.leaRegRegImm32 rdi, rdi, -256
    x64.shrRegImm8 rdi, rdi, 4
    x64.leaRegRegImm32 rdi, rdi, -1
    x64.shlRegImm8 rsi, rsi, 8
    x64.orRegReg rsi, rsi, rdi
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 r8, [r9 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.leaRegRegReg r9, rcx, rdx
    x64.movRegReg rcx, rsi
    x64.movRegReg rdx, rax
    x64.callDirect mrt_aes_gcm_encrypt
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
  gbddetach:
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
    x64.jmp gbddone
  gmddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, rdi
    x64.storeSlotReg slot5, r9
    x64.movRegReg rcx, r9
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot rdi, slot4
    x64.loadRegSlot r9, slot5
    x64.jmp gmddone
  gcbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_decrypt {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 80
    x64.loadRegBaseDisp.word64 rdi, [rax + 8]
    x64.cmpRegImm32 rdi, 432
    x64.jcc less, gcbad
  kbbounded:
    x64.cmpRegImm32 rdi, 496
    x64.jcc greater, gcbad
  kbstepped:
    x64.movRegReg r8, rdi
    x64.andRegImm32 r8, rdi, 31
    x64.cmpRegImm32 r8, 16
    x64.jcc notEqual, gcbad
  gkmessage:
    x64.loadRegBaseDisp.word64 r8, [r9 + 8]
    x64.cmpRegImm32 r8, 48
    x64.jcc notEqual, gcbad
  gcspan:
    x64.loadRegBaseDisp.word64 r8, [rcx + 8]
    x64.cmpRegReg rdx, r8
    x64.jcc above, gcbad
  gcinside:
    x64.subRegReg r8, r8, rdx
    x64.cmpRegReg r8, rsi
    x64.jcc below, gcbad
  gccrypt:
    x64.loadRegBaseDisp.word64 r8, [rcx + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc less, gbddetach
  gbdbuf:
    x64.loadRegBaseDisp.word64 r8, [rcx + 0]
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, gbddone
  gbdshared:
    x64.leaRegRegImm32 r8, r8, -24
    x64.loadRegBaseDisp.word64 r8, [r8 + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, gbddetach
  gbddone:
    x64.loadRegBaseDisp.word64 r8, [r9 + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc less, gmddetach
  gmdbuf:
    x64.loadRegBaseDisp.word64 r8, [r9 + 0]
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, gmddone
  gmdshared:
    x64.leaRegRegImm32 r8, r8, -24
    x64.loadRegBaseDisp.word64 r8, [r8 + 16]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, gmddetach
  gmddone:
    x64.leaRegRegImm32 rdi, rdi, -256
    x64.shrRegImm8 rdi, rdi, 4
    x64.leaRegRegImm32 rdi, rdi, -1
    x64.shlRegImm8 rsi, rsi, 8
    x64.orRegReg rsi, rsi, rdi
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 r8, [r9 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.leaRegRegReg r9, rcx, rdx
    x64.movRegReg rcx, rsi
    x64.movRegReg rdx, rax
    x64.callDirect mrt_aes_gcm_decrypt
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
  gbddetach:
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
    x64.jmp gbddone
  gmddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, rdi
    x64.storeSlotReg slot5, r9
    x64.movRegReg rcx, r9
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot rdi, slot4
    x64.loadRegSlot r9, slot5
    x64.jmp gmddone
  gcbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 80
    x64.popReg rbp
    x64.ret
}

func @__cpufeat_aes_cipher_gcm_finish {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.loadRegBaseDisp.word64 rsi, [rdx + 8]
    x64.cmpRegImm32 rsi, 432
    x64.jcc less, gfbad
  kbbounded:
    x64.cmpRegImm32 rsi, 496
    x64.jcc greater, gfbad
  kbstepped:
    x64.andRegImm32 rsi, rsi, 31
    x64.cmpRegImm32 rsi, 16
    x64.jcc notEqual, gfbad
  gkmessage:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegImm32 rsi, 48
    x64.jcc notEqual, gfbad
  gfcrypt:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, gfddetach
  gfdbuf:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, gfddone
  gfdshared:
    x64.leaRegRegImm32 rsi, rsi, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, gfddetach
  gfddone:
    x64.loadRegBaseDisp.word64 rdx, [rdx + 0]
    x64.loadRegBaseDisp.word64 r8, [rcx + 0]
    x64.movRegReg rcx, rax
    x64.callDirect mrt_aes_gcm_finish
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  gfddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, r9
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot r9, slot3
    x64.jmp gfddone
  gfbad:
    x64.leaRegRdata rcx, [rip + __fatal_msg_13]  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    x64.movRegImm32 rdx, 89
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
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
    0000: 55 48 89 e5 48 81 ec d0 01 00 00 49 89 ca 49 c1
    0010: ea 08 49 89 cb 49 81 e3 ff 00 00 00 48 8d 0d 3d
    0020: 16 00 00 f3 41 0f 6f 08 f3 41 0f 6f 40 10 41 8b
    0030: 40 1c f3 0f 6f a2 00 01 00 00 8b ba 0c 01 00 00
    0040: 0f c8 0f cf 49 8d 72 0f 48 c1 ee 04 48 01 c6 0f
    0050: ce 41 89 70 1c 66 0f ef e0 f3 0f 7f a5 d0 fe ff
    0060: ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    0070: dc fe ff ff 49 81 fa 80 00 00 00 0f 82 e5 10 00
    0080: 00 f3 0f 7f b5 60 ff ff ff f3 0f 7f bd 70 ff ff
    0090: ff f3 44 0f 7f 45 80 f3 44 0f 7f 4d 90 f3 44 0f
    00a0: 7f 55 a0 f3 44 0f 7f 5d b0 f3 44 0f 7f 65 c0 f3
    00b0: 44 0f 7f 6d d0 f3 44 0f 7f 75 e0 f3 44 0f 7f 7d
    00c0: f0 f3 44 0f 6f 39 f3 44 0f 6f 71 10 44 0f 28 c1
    00d0: 44 0f 28 dc 66 45 0f ef c9 66 45 0f ef d2 49 81
    00e0: ea 80 00 00 00 f3 44 0f 7f 9d e0 fe ff ff 48 81
    00f0: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 ec fe ff
    0100: ff f3 44 0f 7f 9d f0 fe ff ff 48 81 c0 01 00 00
    0110: 00 89 c6 31 fe 0f ce 89 b5 fc fe ff ff f3 44 0f
    0120: 7f 9d 00 ff ff ff 48 81 c0 01 00 00 00 89 c6 31
    0130: fe 0f ce 89 b5 0c ff ff ff f3 44 0f 7f 9d 10 ff
    0140: ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0150: b5 1c ff ff ff f3 44 0f 7f 9d 20 ff ff ff 48 81
    0160: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 2c ff ff
    0170: ff f3 44 0f 7f 9d 30 ff ff ff 48 81 c0 01 00 00
    0180: 00 89 c6 31 fe 0f ce 89 b5 3c ff ff ff f3 44 0f
    0190: 7f 9d 40 ff ff ff 48 81 c0 01 00 00 00 89 c6 31
    01a0: fe 0f ce 89 b5 4c ff ff ff f3 0f 6f 85 d0 fe ff
    01b0: ff f3 0f 6f 8d e0 fe ff ff f3 0f 6f 95 f0 fe ff
    01c0: ff f3 0f 6f 9d 00 ff ff ff f3 0f 6f a5 10 ff ff
    01d0: ff f3 0f 6f ad 20 ff ff ff f3 0f 6f b5 30 ff ff
    01e0: ff f3 0f 6f bd 40 ff ff ff f3 44 0f 6f 9a 10 01
    01f0: 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41
    0200: 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3
    0210: 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38
    0220: dc fb 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0230: b5 dc fe ff ff f3 44 0f 6f 9a 20 01 00 00 66 41
    0240: 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3
    0250: 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38
    0260: dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 48 81
    0270: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 ec fe ff
    0280: ff f3 44 0f 6f 9a 30 01 00 00 66 41 0f 38 dc c3
    0290: 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38
    02a0: dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41
    02b0: 0f 38 dc f3 66 41 0f 38 dc fb 48 81 c0 01 00 00
    02c0: 00 89 c6 31 fe 0f ce 89 b5 fc fe ff ff f3 44 0f
    02d0: 6f 9a 40 01 00 00 66 41 0f 38 dc c3 66 41 0f 38
    02e0: dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41
    02f0: 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3
    0300: 66 41 0f 38 dc fb 48 81 c0 01 00 00 00 89 c6 31
    0310: fe 0f ce 89 b5 0c ff ff ff f3 44 0f 6f 9a 50 01
    0320: 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41
    0330: 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3
    0340: 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38
    0350: dc fb 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0360: b5 1c ff ff ff f3 44 0f 6f 9a 60 01 00 00 66 41
    0370: 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3
    0380: 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38
    0390: dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 48 81
    03a0: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 2c ff ff
    03b0: ff f3 44 0f 6f 9a 70 01 00 00 66 41 0f 38 dc c3
    03c0: 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38
    03d0: dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41
    03e0: 0f 38 dc f3 66 41 0f 38 dc fb 48 81 c0 01 00 00
    03f0: 00 89 c6 31 fe 0f ce 89 b5 3c ff ff ff f3 44 0f
    0400: 6f 9a 80 01 00 00 66 41 0f 38 dc c3 66 41 0f 38
    0410: dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41
    0420: 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3
    0430: 66 41 0f 38 dc fb 48 81 c0 01 00 00 00 89 c6 31
    0440: fe 0f ce 89 b5 4c ff ff ff f3 44 0f 6f 9a 90 01
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
    0670: 85 50 fe ff ff f3 0f 7f 8d 60 fe ff ff f3 0f 7f
    0680: 95 70 fe ff ff f3 0f 7f 9d 80 fe ff ff f3 0f 7f
    0690: a5 90 fe ff ff f3 0f 7f ad a0 fe ff ff f3 0f 7f
    06a0: b5 b0 fe ff ff f3 0f 7f bd c0 fe ff ff 49 81 c1
    06b0: 80 00 00 00 49 81 fa 80 00 00 00 0f 82 b4 07 00
    06c0: 00 49 81 ea 80 00 00 00 f3 0f 6f 85 d0 fe ff ff
    06d0: f3 0f 6f 8d e0 fe ff ff f3 0f 6f 95 f0 fe ff ff
    06e0: f3 0f 6f 9d 00 ff ff ff f3 0f 6f a5 10 ff ff ff
    06f0: f3 0f 6f ad 20 ff ff ff f3 0f 6f b5 30 ff ff ff
    0700: f3 0f 6f bd 40 ff ff ff f3 44 0f 6f 9d 50 fe ff
    0710: ff f3 44 0f 6f 02 f3 44 0f 6f 52 10 45 0f 28 c8
    0720: 66 45 0f 70 e3 4e 66 45 0f ef e3 66 45 0f 3a 44
    0730: d4 00 66 45 0f 3a 44 c3 00 66 45 0f 3a 44 cb 11
    0740: f3 44 0f 6f 9a 10 01 00 00 66 41 0f 38 dc c3 66
    0750: 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc
    0760: db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f
    0770: 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9d 60 fe
    0780: ff ff f3 44 0f 6f 62 20 45 0f 28 ec 66 45 0f 3a
    0790: 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66
    07a0: 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3
    07b0: 44 0f 6f 62 30 66 45 0f 3a 44 e3 00 66 45 0f ef
    07c0: d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    07d0: dc fe ff ff f3 44 0f 6f 9a 20 01 00 00 66 41 0f
    07e0: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    07f0: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    0800: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 44 0f
    0810: 6f 9d 70 fe ff ff f3 44 0f 6f 62 40 45 0f 28 ec
    0820: 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a
    0830: 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45
    0840: 0f ef dc f3 44 0f 6f 62 50 66 45 0f 3a 44 e3 00
    0850: 66 45 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe
    0860: 0f ce 89 b5 ec fe ff ff f3 44 0f 6f 9a 30 01 00
    0870: 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f
    0880: 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66
    0890: 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc
    08a0: fb f3 44 0f 6f 9d 80 fe ff ff f3 44 0f 6f 62 60
    08b0: 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4
    08c0: 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70
    08d0: e3 4e 66 45 0f ef dc f3 44 0f 6f 62 70 66 45 0f
    08e0: 3a 44 e3 00 66 45 0f ef d4 48 81 c0 01 00 00 00
    08f0: 89 c6 31 fe 0f ce 89 b5 fc fe ff ff f3 44 0f 6f
    0900: 9a 40 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc
    0910: cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f
    0920: 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66
    0930: 41 0f 38 dc fb f3 44 0f 6f 9d 90 fe ff ff f3 44
    0940: 0f 6f a2 80 00 00 00 45 0f 28 ec 66 45 0f 3a 44
    0950: e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45
    0960: 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44
    0970: 0f 6f a2 90 00 00 00 66 45 0f 3a 44 e3 00 66 45
    0980: 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce
    0990: 89 b5 0c ff ff ff f3 44 0f 6f 9a 50 01 00 00 66
    09a0: 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc
    09b0: d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f
    09c0: 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3
    09d0: 44 0f 6f 9d a0 fe ff ff f3 44 0f 6f a2 a0 00 00
    09e0: 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef
    09f0: c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f
    0a00: 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 b0 00 00
    0a10: 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81 c0
    0a20: 01 00 00 00 89 c6 31 fe 0f ce 89 b5 1c ff ff ff
    0a30: f3 44 0f 6f 9a 60 01 00 00 66 41 0f 38 dc c3 66
    0a40: 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc
    0a50: db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f
    0a60: 38 dc f3 66 41 0f 38 dc fb f3 44 0f 6f 9d b0 fe
    0a70: ff ff f3 44 0f 6f a2 c0 00 00 00 45 0f 28 ec 66
    0a80: 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44
    0a90: eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f
    0aa0: ef dc f3 44 0f 6f a2 d0 00 00 00 66 45 0f 3a 44
    0ab0: e3 00 66 45 0f ef d4 48 81 c0 01 00 00 00 89 c6
    0ac0: 31 fe 0f ce 89 b5 2c ff ff ff f3 44 0f 6f 9a 70
    0ad0: 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66
    0ae0: 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc
    0af0: e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f
    0b00: 38 dc fb f3 44 0f 6f 9d c0 fe ff ff f3 44 0f 6f
    0b10: a2 e0 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00
    0b20: 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef
    0b30: cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f
    0b40: a2 f0 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef
    0b50: d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    0b60: 3c ff ff ff f3 44 0f 6f 9a 80 01 00 00 66 41 0f
    0b70: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    0b80: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    0b90: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb 48 81 c0
    0ba0: 01 00 00 00 89 c6 31 fe 0f ce 89 b5 4c ff ff ff
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
    0e20: 7f 79 70 66 41 0f 38 00 ff f3 0f 7f 85 50 fe ff
    0e30: ff f3 0f 7f 8d 60 fe ff ff f3 0f 7f 95 70 fe ff
    0e40: ff f3 0f 7f 9d 80 fe ff ff f3 0f 7f a5 90 fe ff
    0e50: ff f3 0f 7f ad a0 fe ff ff f3 0f 7f b5 b0 fe ff
    0e60: ff f3 0f 7f bd c0 fe ff ff 49 81 c1 80 00 00 00
    0e70: e9 3f f8 ff ff f3 44 0f 6f 9d 50 fe ff ff f3 44
    0e80: 0f 6f 02 f3 44 0f 6f 52 10 45 0f 28 c8 66 45 0f
    0e90: 70 e3 4e 66 45 0f ef e3 66 45 0f 3a 44 d4 00 66
    0ea0: 45 0f 3a 44 c3 00 66 45 0f 3a 44 cb 11 f3 44 0f
    0eb0: 6f 9d 60 fe ff ff f3 44 0f 6f 62 20 45 0f 28 ec
    0ec0: 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a
    0ed0: 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45
    0ee0: 0f ef dc f3 44 0f 6f 62 30 66 45 0f 3a 44 e3 00
    0ef0: 66 45 0f ef d4 f3 44 0f 6f 9d 70 fe ff ff f3 44
    0f00: 0f 6f 62 40 45 0f 28 ec 66 45 0f 3a 44 e3 00 66
    0f10: 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd
    0f20: 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f 62
    0f30: 50 66 45 0f 3a 44 e3 00 66 45 0f ef d4 f3 44 0f
    0f40: 6f 9d 80 fe ff ff f3 44 0f 6f 62 60 45 0f 28 ec
    0f50: 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45 0f 3a
    0f60: 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e 66 45
    0f70: 0f ef dc f3 44 0f 6f 62 70 66 45 0f 3a 44 e3 00
    0f80: 66 45 0f ef d4 f3 44 0f 6f 9d 90 fe ff ff f3 44
    0f90: 0f 6f a2 80 00 00 00 45 0f 28 ec 66 45 0f 3a 44
    0fa0: e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45
    0fb0: 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44
    0fc0: 0f 6f a2 90 00 00 00 66 45 0f 3a 44 e3 00 66 45
    0fd0: 0f ef d4 f3 44 0f 6f 9d a0 fe ff ff f3 44 0f 6f
    0fe0: a2 a0 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00
    0ff0: 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef
    1000: cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f
    1010: a2 b0 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef
    1020: d4 f3 44 0f 6f 9d b0 fe ff ff f3 44 0f 6f a2 c0
    1030: 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45
    1040: 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66
    1050: 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 d0
    1060: 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 f3
    1070: 44 0f 6f 9d c0 fe ff ff f3 44 0f 6f a2 e0 00 00
    1080: 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef
    1090: c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f
    10a0: 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 f0 00 00
    10b0: 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 66 45 0f
    10c0: ef d0 66 45 0f ef d1 45 0f 28 da 66 41 0f 73 da
    10d0: 08 66 41 0f 73 fb 08 66 45 0f ef ca 66 45 0f ef
    10e0: c3 45 0f 28 de 66 45 0f 3a 44 d8 01 66 45 0f 70
    10f0: c0 4e 66 45 0f ef c3 45 0f 28 de 66 45 0f 3a 44
    1100: d8 01 66 45 0f 70 c0 4e 66 45 0f ef c3 66 45 0f
    1110: ef c1 41 0f 28 c8 f3 0f 6f b5 60 ff ff ff f3 0f
    1120: 6f bd 70 ff ff ff f3 44 0f 6f 45 80 f3 44 0f 6f
    1130: 4d 90 f3 44 0f 6f 55 a0 f3 44 0f 6f 5d b0 f3 44
    1140: 0f 6f 65 c0 f3 44 0f 6f 6d d0 f3 44 0f 6f 75 e0
    1150: f3 44 0f 6f 7d f0 4d 85 d2 0f 84 f0 04 00 00 48
    1160: 81 e8 07 00 00 00 49 81 fa 10 00 00 00 0f 82 09
    1170: 02 00 00 31 c9 49 81 ea 10 00 00 00 f3 0f 6f 85
    1180: d0 fe ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f
    1190: ce 89 b5 dc fe ff ff f3 0f 6f a2 10 01 00 00 66
    11a0: 0f 38 dc c4 f3 0f 6f a2 20 01 00 00 66 0f 38 dc
    11b0: c4 f3 0f 6f a2 30 01 00 00 66 0f 38 dc c4 f3 0f
    11c0: 6f a2 40 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 50
    11d0: 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 60 01 00 00
    11e0: 66 0f 38 dc c4 f3 0f 6f a2 70 01 00 00 66 0f 38
    11f0: dc c4 f3 0f 6f a2 80 01 00 00 66 0f 38 dc c4 f3
    1200: 0f 6f a2 90 01 00 00 66 0f 38 dc c4 f3 0f 6f a2
    1210: a0 01 00 00 49 81 fb 0c 00 00 00 0f 82 3a 00 00
    1220: 00 66 0f 38 dc c4 f3 0f 6f a2 b0 01 00 00 66 0f
    1230: 38 dc c4 f3 0f 6f a2 c0 01 00 00 0f 84 1a 00 00
    1240: 00 66 0f 38 dc c4 f3 0f 6f a2 d0 01 00 00 66 0f
    1250: 38 dc c4 f3 0f 6f a2 e0 01 00 00 66 0f 38 dd c4
    1260: f3 41 0f 6f 29 66 0f ef c5 f3 41 0f 7f 01 48 8d
    1270: 35 eb 03 00 00 f3 0f 6f 2e 66 0f 38 00 c5 f3 0f
    1280: 7f 84 0d 50 fe ff ff 49 81 c1 10 00 00 00 48 81
    1290: c1 10 00 00 00 49 81 fa 10 00 00 00 0f 83 d3 fe
    12a0: ff ff 48 01 c9 31 f6 48 81 c6 00 01 00 00 48 29
    12b0: ce 31 c9 f3 0f 6f 85 50 fe ff ff 66 0f ef c1 f3
    12c0: 0f 7f 85 50 fe ff ff 66 0f ef c9 66 0f ef d2 66
    12d0: 0f ef db f3 0f 6f 84 0d 50 fe ff ff f3 0f 6f 2c
    12e0: 32 0f 28 e5 66 0f 3a 44 e0 00 66 0f ef cc 66 0f
    12f0: 3a 44 e8 11 66 0f ef d5 66 0f 70 e0 4e 66 0f ef
    1300: e0 f3 0f 6f 6c 32 10 66 0f 3a 44 ec 00 66 0f ef
    1310: dd 48 81 c1 10 00 00 00 48 81 c6 20 00 00 00 48
    1320: 81 fe 00 01 00 00 0f 82 a7 ff ff ff 48 8d 0d 2d
    1330: 03 00 00 66 0f ef d9 66 0f ef da 0f 28 e3 66 0f
    1340: 73 db 08 66 0f 73 fc 08 66 0f ef d3 66 0f ef cc
    1350: f3 0f 6f 61 10 66 0f 3a 44 e1 01 66 0f 70 c9 4e
    1360: 66 0f ef cc f3 0f 6f 61 10 66 0f 3a 44 e1 01 66
    1370: 0f 70 c9 4e 66 0f ef cc 66 0f ef ca 4d 85 d2 0f
    1380: 84 ca 02 00 00 48 8d 8d 50 ff ff ff 4c 89 d6 48
    1390: 81 e6 08 00 00 00 0f 84 14 00 00 00 49 8b 31 48
    13a0: 89 31 49 81 c1 08 00 00 00 48 81 c1 08 00 00 00
    13b0: 4c 89 d6 48 81 e6 04 00 00 00 0f 84 13 00 00 00
    13c0: 41 8b 31 89 31 49 81 c1 04 00 00 00 48 81 c1 04
    13d0: 00 00 00 4c 89 d6 48 81 e6 02 00 00 00 0f 84 15
    13e0: 00 00 00 49 0f b7 31 66 89 31 49 81 c1 02 00 00
    13f0: 00 48 81 c1 02 00 00 00 4c 89 d6 48 81 e6 01 00
    1400: 00 00 0f 84 15 00 00 00 49 0f b6 31 40 88 31 49
    1410: 81 c1 01 00 00 00 48 81 c1 01 00 00 00 f3 0f 6f
    1420: ad 50 ff ff ff 4d 29 d1 f3 0f 6f 85 d0 fe ff ff
    1430: 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 dc
    1440: fe ff ff f3 0f 6f a2 10 01 00 00 66 0f 38 dc c4
    1450: f3 0f 6f a2 20 01 00 00 66 0f 38 dc c4 f3 0f 6f
    1460: a2 30 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 40 01
    1470: 00 00 66 0f 38 dc c4 f3 0f 6f a2 50 01 00 00 66
    1480: 0f 38 dc c4 f3 0f 6f a2 60 01 00 00 66 0f 38 dc
    1490: c4 f3 0f 6f a2 70 01 00 00 66 0f 38 dc c4 f3 0f
    14a0: 6f a2 80 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 90
    14b0: 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 a0 01 00 00
    14c0: 49 81 fb 0c 00 00 00 0f 82 3a 00 00 00 66 0f 38
    14d0: dc c4 f3 0f 6f a2 b0 01 00 00 66 0f 38 dc c4 f3
    14e0: 0f 6f a2 c0 01 00 00 0f 84 1a 00 00 00 66 0f 38
    14f0: dc c4 f3 0f 6f a2 d0 01 00 00 66 0f 38 dc c4 f3
    1500: 0f 6f a2 e0 01 00 00 66 0f 38 dd c4 66 0f ef c5
    1510: f3 0f 7f 85 50 ff ff ff 48 8d 8d 50 ff ff ff 4c
    1520: 89 d6 48 81 e6 08 00 00 00 0f 84 14 00 00 00 48
    1530: 8b 31 49 89 31 48 81 c1 08 00 00 00 49 81 c1 08
    1540: 00 00 00 4c 89 d6 48 81 e6 04 00 00 00 0f 84 13
    1550: 00 00 00 8b 31 41 89 31 48 81 c1 04 00 00 00 49
    1560: 81 c1 04 00 00 00 4c 89 d6 48 81 e6 02 00 00 00
    1570: 0f 84 16 00 00 00 48 0f b7 31 66 41 89 31 48 81
    1580: c1 02 00 00 00 49 81 c1 02 00 00 00 4c 89 d6 48
    1590: 81 e6 01 00 00 00 0f 84 15 00 00 00 48 0f b6 31
    15a0: 41 88 31 48 81 c1 01 00 00 00 49 81 c1 01 00 00
    15b0: 00 48 8d 35 a8 00 00 00 4c 29 d6 f3 0f 6f 6e 30
    15c0: 66 0f db c5 48 8d 0d 95 00 00 00 f3 0f 6f 29 66
    15d0: 0f 38 00 c5 66 0f ef c1 f3 0f 6f 8a e0 00 00 00
    15e0: 0f 28 d1 f3 0f 6f 9a f0 00 00 00 66 0f 3a 44 c8
    15f0: 00 66 0f 3a 44 d0 11 66 0f 70 e0 4e 66 0f ef e0
    1600: 66 0f 3a 44 dc 00 66 0f ef d9 66 0f ef da 0f 28
    1610: e3 66 0f 73 db 08 66 0f 73 fc 08 66 0f ef d3 66
    1620: 0f ef cc f3 0f 6f 61 10 66 0f 3a 44 e1 01 66 0f
    1630: 70 c9 4e 66 0f ef cc f3 0f 6f 61 10 66 0f 3a 44
    1640: e1 01 66 0f 70 c9 4e 66 0f ef cc 66 0f ef ca f3
    1650: 41 0f 7f 08 31 c0 48 81 c4 d0 01 00 00 5d c3 00
    1660: 0f 0e 0d 0c 0b 0a 09 08 07 06 05 04 03 02 01 00
    1670: 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 c2
    1680: ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff ff
    1690: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
    16a0: 00
}

chunk @mrt_aes_gcm_decrypt {
    0000: 55 48 89 e5 48 81 ec c0 01 00 00 49 89 ca 49 c1
    0010: ea 08 49 89 cb 49 81 e3 ff 00 00 00 48 8d 0d 39
    0020: 0e 00 00 f3 41 0f 6f 08 f3 41 0f 6f 40 10 41 8b
    0030: 40 1c f3 0f 6f a2 00 01 00 00 8b ba 0c 01 00 00
    0040: 0f c8 0f cf 49 8d 72 0f 48 c1 ee 04 48 01 c6 0f
    0050: ce 41 89 70 1c 66 0f ef e0 f3 0f 7f a5 60 fe ff
    0060: ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    0070: 6c fe ff ff 49 81 fa 80 00 00 00 0f 82 d4 08 00
    0080: 00 f3 0f 7f b5 60 ff ff ff f3 0f 7f bd 70 ff ff
    0090: ff f3 44 0f 7f 45 80 f3 44 0f 7f 4d 90 f3 44 0f
    00a0: 7f 55 a0 f3 44 0f 7f 5d b0 f3 44 0f 7f 65 c0 f3
    00b0: 44 0f 7f 6d d0 f3 44 0f 7f 75 e0 f3 44 0f 7f 7d
    00c0: f0 f3 44 0f 6f 39 f3 44 0f 6f 71 10 44 0f 28 c1
    00d0: 44 0f 28 dc 66 45 0f ef c9 66 45 0f ef d2 f3 44
    00e0: 0f 7f 9d 70 fe ff ff 48 81 c0 01 00 00 00 89 c6
    00f0: 31 fe 0f ce 89 b5 7c fe ff ff f3 44 0f 7f 9d 80
    0100: fe ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce
    0110: 89 b5 8c fe ff ff f3 44 0f 7f 9d 90 fe ff ff 48
    0120: 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 9c fe
    0130: ff ff f3 44 0f 7f 9d a0 fe ff ff 48 81 c0 01 00
    0140: 00 00 89 c6 31 fe 0f ce 89 b5 ac fe ff ff f3 44
    0150: 0f 7f 9d b0 fe ff ff 48 81 c0 01 00 00 00 89 c6
    0160: 31 fe 0f ce 89 b5 bc fe ff ff f3 44 0f 7f 9d c0
    0170: fe ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce
    0180: 89 b5 cc fe ff ff f3 44 0f 7f 9d d0 fe ff ff 48
    0190: 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 dc fe
    01a0: ff ff 49 81 fa 80 00 00 00 0f 82 5b 07 00 00 49
    01b0: 81 ea 80 00 00 00 f3 0f 6f 85 60 fe ff ff f3 0f
    01c0: 6f 8d 70 fe ff ff f3 0f 6f 95 80 fe ff ff f3 0f
    01d0: 6f 9d 90 fe ff ff f3 0f 6f a5 a0 fe ff ff f3 0f
    01e0: 6f ad b0 fe ff ff f3 0f 6f b5 c0 fe ff ff f3 0f
    01f0: 6f bd d0 fe ff ff f3 45 0f 6f 19 66 45 0f 38 00
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
    02c0: 89 c6 31 fe 0f ce 89 b5 6c fe ff ff f3 44 0f 6f
    02d0: 9a 20 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc
    02e0: cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f
    02f0: 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66
    0300: 41 0f 38 dc fb f3 45 0f 6f 59 20 66 45 0f 38 00
    0310: df f3 44 0f 6f 62 40 45 0f 28 ec 66 45 0f 3a 44
    0320: e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45
    0330: 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44
    0340: 0f 6f 62 50 66 45 0f 3a 44 e3 00 66 45 0f ef d4
    0350: 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 7c
    0360: fe ff ff f3 44 0f 6f 9a 30 01 00 00 66 41 0f 38
    0370: dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41
    0380: 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb
    0390: 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 45 0f 6f
    03a0: 59 30 66 45 0f 38 00 df f3 44 0f 6f 62 60 45 0f
    03b0: 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66 45
    03c0: 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3 4e
    03d0: 66 45 0f ef dc f3 44 0f 6f 62 70 66 45 0f 3a 44
    03e0: e3 00 66 45 0f ef d4 48 81 c0 01 00 00 00 89 c6
    03f0: 31 fe 0f ce 89 b5 8c fe ff ff f3 44 0f 6f 9a 40
    0400: 01 00 00 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66
    0410: 41 0f 38 dc d3 66 41 0f 38 dc db 66 41 0f 38 dc
    0420: e3 66 41 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f
    0430: 38 dc fb f3 45 0f 6f 59 40 66 45 0f 38 00 df f3
    0440: 44 0f 6f a2 80 00 00 00 45 0f 28 ec 66 45 0f 3a
    0450: 44 e3 00 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66
    0460: 45 0f ef cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3
    0470: 44 0f 6f a2 90 00 00 00 66 45 0f 3a 44 e3 00 66
    0480: 45 0f ef d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f
    0490: ce 89 b5 9c fe ff ff f3 44 0f 6f 9a 50 01 00 00
    04a0: 66 41 0f 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38
    04b0: dc d3 66 41 0f 38 dc db 66 41 0f 38 dc e3 66 41
    04c0: 0f 38 dc eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb
    04d0: f3 45 0f 6f 59 50 66 45 0f 38 00 df f3 44 0f 6f
    04e0: a2 a0 00 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00
    04f0: 66 45 0f ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef
    0500: cd 66 45 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f
    0510: a2 b0 00 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef
    0520: d4 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    0530: ac fe ff ff f3 44 0f 6f 9a 60 01 00 00 66 41 0f
    0540: 38 dc c3 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66
    0550: 41 0f 38 dc db 66 41 0f 38 dc e3 66 41 0f 38 dc
    0560: eb 66 41 0f 38 dc f3 66 41 0f 38 dc fb f3 45 0f
    0570: 6f 59 60 66 45 0f 38 00 df f3 44 0f 6f a2 c0 00
    0580: 00 00 45 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f
    0590: ef c4 66 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45
    05a0: 0f 70 e3 4e 66 45 0f ef dc f3 44 0f 6f a2 d0 00
    05b0: 00 00 66 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81
    05c0: c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5 bc fe ff
    05d0: ff f3 44 0f 6f 9a 70 01 00 00 66 41 0f 38 dc c3
    05e0: 66 41 0f 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38
    05f0: dc db 66 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41
    0600: 0f 38 dc f3 66 41 0f 38 dc fb f3 45 0f 6f 59 70
    0610: 66 45 0f 38 00 df f3 44 0f 6f a2 e0 00 00 00 45
    0620: 0f 28 ec 66 45 0f 3a 44 e3 00 66 45 0f ef c4 66
    0630: 45 0f 3a 44 eb 11 66 45 0f ef cd 66 45 0f 70 e3
    0640: 4e 66 45 0f ef dc f3 44 0f 6f a2 f0 00 00 00 66
    0650: 45 0f 3a 44 e3 00 66 45 0f ef d4 48 81 c0 01 00
    0660: 00 00 89 c6 31 fe 0f ce 89 b5 cc fe ff ff f3 44
    0670: 0f 6f 9a 80 01 00 00 66 41 0f 38 dc c3 66 41 0f
    0680: 38 dc cb 66 41 0f 38 dc d3 66 41 0f 38 dc db 66
    0690: 41 0f 38 dc e3 66 41 0f 38 dc eb 66 41 0f 38 dc
    06a0: f3 66 41 0f 38 dc fb 48 81 c0 01 00 00 00 89 c6
    06b0: 31 fe 0f ce 89 b5 dc fe ff ff 66 45 0f ef d0 66
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
    0900: c1 80 00 00 00 e9 98 f8 ff ff 41 0f 28 c8 f3 0f
    0910: 6f b5 60 ff ff ff f3 0f 6f bd 70 ff ff ff f3 44
    0920: 0f 6f 45 80 f3 44 0f 6f 4d 90 f3 44 0f 6f 55 a0
    0930: f3 44 0f 6f 5d b0 f3 44 0f 6f 65 c0 f3 44 0f 6f
    0940: 6d d0 f3 44 0f 6f 75 e0 f3 44 0f 6f 7d f0 48 81
    0950: e8 07 00 00 00 49 81 fa 10 00 00 00 0f 82 0e 02
    0960: 00 00 31 c9 49 81 ea 10 00 00 00 f3 41 0f 6f 01
    0970: 48 8d 35 e5 04 00 00 f3 0f 6f 2e 66 0f 38 00 c5
    0980: f3 0f 7f 84 0d f0 fe ff ff f3 0f 6f 85 60 fe ff
    0990: ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89 b5
    09a0: 6c fe ff ff f3 0f 6f a2 10 01 00 00 66 0f 38 dc
    09b0: c4 f3 0f 6f a2 20 01 00 00 66 0f 38 dc c4 f3 0f
    09c0: 6f a2 30 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 40
    09d0: 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 50 01 00 00
    09e0: 66 0f 38 dc c4 f3 0f 6f a2 60 01 00 00 66 0f 38
    09f0: dc c4 f3 0f 6f a2 70 01 00 00 66 0f 38 dc c4 f3
    0a00: 0f 6f a2 80 01 00 00 66 0f 38 dc c4 f3 0f 6f a2
    0a10: 90 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 a0 01 00
    0a20: 00 49 81 fb 0c 00 00 00 0f 82 3a 00 00 00 66 0f
    0a30: 38 dc c4 f3 0f 6f a2 b0 01 00 00 66 0f 38 dc c4
    0a40: f3 0f 6f a2 c0 01 00 00 0f 84 1a 00 00 00 66 0f
    0a50: 38 dc c4 f3 0f 6f a2 d0 01 00 00 66 0f 38 dc c4
    0a60: f3 0f 6f a2 e0 01 00 00 66 0f 38 dd c4 f3 41 0f
    0a70: 6f 29 66 0f ef c5 f3 41 0f 7f 01 49 81 c1 10 00
    0a80: 00 00 48 81 c1 10 00 00 00 49 81 fa 10 00 00 00
    0a90: 0f 83 ce fe ff ff 48 01 c9 31 f6 48 81 c6 00 01
    0aa0: 00 00 48 29 ce 31 c9 f3 0f 6f 85 f0 fe ff ff 66
    0ab0: 0f ef c1 f3 0f 7f 85 f0 fe ff ff 66 0f ef c9 66
    0ac0: 0f ef d2 66 0f ef db f3 0f 6f 84 0d f0 fe ff ff
    0ad0: f3 0f 6f 2c 32 0f 28 e5 66 0f 3a 44 e0 00 66 0f
    0ae0: ef cc 66 0f 3a 44 e8 11 66 0f ef d5 66 0f 70 e0
    0af0: 4e 66 0f ef e0 f3 0f 6f 6c 32 10 66 0f 3a 44 ec
    0b00: 00 66 0f ef dd 48 81 c1 10 00 00 00 48 81 c6 20
    0b10: 00 00 00 48 81 fe 00 01 00 00 0f 82 a7 ff ff ff
    0b20: 48 8d 0d 35 03 00 00 66 0f ef d9 66 0f ef da 0f
    0b30: 28 e3 66 0f 73 db 08 66 0f 73 fc 08 66 0f ef d3
    0b40: 66 0f ef cc f3 0f 6f 61 10 66 0f 3a 44 e1 01 66
    0b50: 0f 70 c9 4e 66 0f ef cc f3 0f 6f 61 10 66 0f 3a
    0b60: 44 e1 01 66 0f 70 c9 4e 66 0f ef cc 66 0f ef ca
    0b70: 4d 85 d2 0f 84 d2 02 00 00 48 8d 8d e0 fe ff ff
    0b80: 4c 89 d6 48 81 e6 08 00 00 00 0f 84 14 00 00 00
    0b90: 49 8b 31 48 89 31 49 81 c1 08 00 00 00 48 81 c1
    0ba0: 08 00 00 00 4c 89 d6 48 81 e6 04 00 00 00 0f 84
    0bb0: 13 00 00 00 41 8b 31 89 31 49 81 c1 04 00 00 00
    0bc0: 48 81 c1 04 00 00 00 4c 89 d6 48 81 e6 02 00 00
    0bd0: 00 0f 84 15 00 00 00 49 0f b7 31 66 89 31 49 81
    0be0: c1 02 00 00 00 48 81 c1 02 00 00 00 4c 89 d6 48
    0bf0: 81 e6 01 00 00 00 0f 84 15 00 00 00 49 0f b6 31
    0c00: 40 88 31 49 81 c1 01 00 00 00 48 81 c1 01 00 00
    0c10: 00 f3 0f 6f 85 e0 fe ff ff 4d 29 d1 48 8d 35 39
    0c20: 02 00 00 4c 29 d6 f3 0f 6f 6e 30 66 0f db c5 48
    0c30: 8d 0d 26 02 00 00 f3 0f 6f 29 66 0f 38 00 c5 66
    0c40: 0f ef c1 f3 0f 6f 8a e0 00 00 00 0f 28 d1 f3 0f
    0c50: 6f 9a f0 00 00 00 66 0f 3a 44 c8 00 66 0f 3a 44
    0c60: d0 11 66 0f 70 e0 4e 66 0f ef e0 66 0f 3a 44 dc
    0c70: 00 66 0f ef d9 66 0f ef da 0f 28 e3 66 0f 73 db
    0c80: 08 66 0f 73 fc 08 66 0f ef d3 66 0f ef cc f3 0f
    0c90: 6f 61 10 66 0f 3a 44 e1 01 66 0f 70 c9 4e 66 0f
    0ca0: ef cc f3 0f 6f 61 10 66 0f 3a 44 e1 01 66 0f 70
    0cb0: c9 4e 66 0f ef cc 66 0f ef ca f3 0f 6f 85 60 fe
    0cc0: ff ff 48 81 c0 01 00 00 00 89 c6 31 fe 0f ce 89
    0cd0: b5 6c fe ff ff f3 0f 6f a2 10 01 00 00 66 0f 38
    0ce0: dc c4 f3 0f 6f a2 20 01 00 00 66 0f 38 dc c4 f3
    0cf0: 0f 6f a2 30 01 00 00 66 0f 38 dc c4 f3 0f 6f a2
    0d00: 40 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 50 01 00
    0d10: 00 66 0f 38 dc c4 f3 0f 6f a2 60 01 00 00 66 0f
    0d20: 38 dc c4 f3 0f 6f a2 70 01 00 00 66 0f 38 dc c4
    0d30: f3 0f 6f a2 80 01 00 00 66 0f 38 dc c4 f3 0f 6f
    0d40: a2 90 01 00 00 66 0f 38 dc c4 f3 0f 6f a2 a0 01
    0d50: 00 00 49 81 fb 0c 00 00 00 0f 82 3a 00 00 00 66
    0d60: 0f 38 dc c4 f3 0f 6f a2 b0 01 00 00 66 0f 38 dc
    0d70: c4 f3 0f 6f a2 c0 01 00 00 0f 84 1a 00 00 00 66
    0d80: 0f 38 dc c4 f3 0f 6f a2 d0 01 00 00 66 0f 38 dc
    0d90: c4 f3 0f 6f a2 e0 01 00 00 66 0f 38 dd c4 f3 0f
    0da0: 6f ad e0 fe ff ff 66 0f ef c5 f3 0f 7f 85 e0 fe
    0db0: ff ff 48 8d 8d e0 fe ff ff 4c 89 d6 48 81 e6 08
    0dc0: 00 00 00 0f 84 14 00 00 00 48 8b 31 49 89 31 48
    0dd0: 81 c1 08 00 00 00 49 81 c1 08 00 00 00 4c 89 d6
    0de0: 48 81 e6 04 00 00 00 0f 84 13 00 00 00 8b 31 41
    0df0: 89 31 48 81 c1 04 00 00 00 49 81 c1 04 00 00 00
    0e00: 4c 89 d6 48 81 e6 02 00 00 00 0f 84 16 00 00 00
    0e10: 48 0f b7 31 66 41 89 31 48 81 c1 02 00 00 00 49
    0e20: 81 c1 02 00 00 00 4c 89 d6 48 81 e6 01 00 00 00
    0e30: 0f 84 15 00 00 00 48 0f b6 31 41 88 31 48 81 c1
    0e40: 01 00 00 00 49 81 c1 01 00 00 00 f3 41 0f 7f 08
    0e50: 31 c0 48 81 c4 c0 01 00 00 5d c3 00 0f 0e 0d 0c
    0e60: 0b 0a 09 08 07 06 05 04 03 02 01 00 01 00 00 00
    0e70: 00 00 00 00 00 00 00 00 00 00 00 c2 ff ff ff ff
    0e80: ff ff ff ff ff ff ff ff ff ff ff ff 00 00 00 00
    0e90: 00 00 00 00 00 00 00 00 00 00 00 00 00
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
    arm64.sub x3, x3, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne giddetach
  giddone:
    arm64.lsr x2, x2, 5
    arm64.sub x2, x2, 1
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x4, [x1 + 0]
    arm64.movImm x5, 0
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x3
    arm64.movRegReg x2, x4
    arm64.movRegReg x3, x5
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

func @__cpufeat_aes_cipher_gcm_hash {
  entry:
    arm64.prologue 64
    arm64.loadRegBaseDisp.word64 x5, [x1 + 8]
    arm64.cmp x5, 432
    arm64.b.lt ghbad
  kbbounded:
    arm64.cmp x5, 496
    arm64.b.gt ghbad
  kbstepped:
    arm64.and x5, x5, 31
    arm64.cmp x5, 16
    arm64.b.ne ghbad
  gkmessage:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.cmp x5, 48
    arm64.b.ne ghbad
  ghspan:
    arm64.loadRegBaseDisp.word64 x5, [x2 + 8]
    arm64.cmp x3, x5
    arm64.b.hi ghbad
  ghinside:
    arm64.sub x5, x5, x3
    arm64.cmp x5, x4
    arm64.b.lo ghbad
  ghcrypt:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 16]
    arm64.cmp x5, 0
    arm64.b.lt ghddetach
  ghdbuf:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 0]
    arm64.cmp x5, 0
    arm64.b.eq ghddone
  ghdshared:
    arm64.sub x5, x5, 24
    arm64.loadRegBaseDisp.word64 x5, [x5 + 16]
    arm64.cmp x5, 0
    arm64.b.ne ghddetach
  ghddone:
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.loadRegBaseDisp.word64 x5, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x2 + 0]
    arm64.add x3, x0, x3
    arm64.movRegReg x0, x4
    arm64.movRegReg x2, x5
    arm64.bl mrt_aes_gcm_hash
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
  ghddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.bl __managed_cow_detach
    arm64.movRegReg x5, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.b ghddone
  ghbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_encrypt {
  entry:
    arm64.prologue 64
    arm64.loadRegBaseDisp.word64 x5, [x2 + 8]
    arm64.cmp x5, 432
    arm64.b.lt gcbad
  kbbounded:
    arm64.cmp x5, 496
    arm64.b.gt gcbad
  kbstepped:
    arm64.and x6, x5, 31
    arm64.cmp x6, 16
    arm64.b.ne gcbad
  gkmessage:
    arm64.loadRegBaseDisp.word64 x6, [x3 + 8]
    arm64.cmp x6, 48
    arm64.b.ne gcbad
  gcspan:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 8]
    arm64.cmp x1, x6
    arm64.b.hi gcbad
  gcinside:
    arm64.sub x6, x6, x1
    arm64.cmp x6, x4
    arm64.b.lo gcbad
  gccrypt:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 16]
    arm64.cmp x6, 0
    arm64.b.lt gbddetach
  gbdbuf:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 0]
    arm64.cmp x6, 0
    arm64.b.eq gbddone
  gbdshared:
    arm64.sub x6, x6, 24
    arm64.loadRegBaseDisp.word64 x6, [x6 + 16]
    arm64.cmp x6, 0
    arm64.b.ne gbddetach
  gbddone:
    arm64.loadRegBaseDisp.word64 x6, [x3 + 16]
    arm64.cmp x6, 0
    arm64.b.lt gmddetach
  gmdbuf:
    arm64.loadRegBaseDisp.word64 x6, [x3 + 0]
    arm64.cmp x6, 0
    arm64.b.eq gmddone
  gmdshared:
    arm64.sub x6, x6, 24
    arm64.loadRegBaseDisp.word64 x6, [x6 + 16]
    arm64.cmp x6, 0
    arm64.b.ne gmddetach
  gmddone:
    arm64.sub x5, x5, 256
    arm64.lsr x5, x5, 4
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x5, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.movRegReg x3, x5
    arm64.bl mrt_aes_gcm_encrypt
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
  gbddetach:
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
    arm64.b gbddone
  gmddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.storeSlotReg slot5, x5
    arm64.movRegReg x0, x3
    arm64.bl __managed_cow_detach
    arm64.movRegReg x6, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.loadRegSlot x5, slot5
    arm64.b gmddone
  gcbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_decrypt {
  entry:
    arm64.prologue 64
    arm64.loadRegBaseDisp.word64 x5, [x2 + 8]
    arm64.cmp x5, 432
    arm64.b.lt gcbad
  kbbounded:
    arm64.cmp x5, 496
    arm64.b.gt gcbad
  kbstepped:
    arm64.and x6, x5, 31
    arm64.cmp x6, 16
    arm64.b.ne gcbad
  gkmessage:
    arm64.loadRegBaseDisp.word64 x6, [x3 + 8]
    arm64.cmp x6, 48
    arm64.b.ne gcbad
  gcspan:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 8]
    arm64.cmp x1, x6
    arm64.b.hi gcbad
  gcinside:
    arm64.sub x6, x6, x1
    arm64.cmp x6, x4
    arm64.b.lo gcbad
  gccrypt:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 16]
    arm64.cmp x6, 0
    arm64.b.lt gbddetach
  gbdbuf:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 0]
    arm64.cmp x6, 0
    arm64.b.eq gbddone
  gbdshared:
    arm64.sub x6, x6, 24
    arm64.loadRegBaseDisp.word64 x6, [x6 + 16]
    arm64.cmp x6, 0
    arm64.b.ne gbddetach
  gbddone:
    arm64.loadRegBaseDisp.word64 x6, [x3 + 16]
    arm64.cmp x6, 0
    arm64.b.lt gmddetach
  gmdbuf:
    arm64.loadRegBaseDisp.word64 x6, [x3 + 0]
    arm64.cmp x6, 0
    arm64.b.eq gmddone
  gmdshared:
    arm64.sub x6, x6, 24
    arm64.loadRegBaseDisp.word64 x6, [x6 + 16]
    arm64.cmp x6, 0
    arm64.b.ne gmddetach
  gmddone:
    arm64.sub x5, x5, 256
    arm64.lsr x5, x5, 4
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x5, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.movRegReg x3, x5
    arm64.bl mrt_aes_gcm_decrypt
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
  gbddetach:
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
    arm64.b gbddone
  gmddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.storeSlotReg slot5, x5
    arm64.movRegReg x0, x3
    arm64.bl __managed_cow_detach
    arm64.movRegReg x6, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.loadRegSlot x5, slot5
    arm64.b gmddone
  gcbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_finish {
  entry:
    arm64.prologue 48
    arm64.loadRegBaseDisp.word64 x4, [x1 + 8]
    arm64.cmp x4, 432
    arm64.b.lt gfbad
  kbbounded:
    arm64.cmp x4, 496
    arm64.b.gt gfbad
  kbstepped:
    arm64.and x4, x4, 31
    arm64.cmp x4, 16
    arm64.b.ne gfbad
  gkmessage:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 8]
    arm64.cmp x4, 48
    arm64.b.ne gfbad
  gfcrypt:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt gfddetach
  gfdbuf:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.cmp x4, 0
    arm64.b.eq gfddone
  gfdshared:
    arm64.sub x4, x4, 24
    arm64.loadRegBaseDisp.word64 x4, [x4 + 16]
    arm64.cmp x4, 0
    arm64.b.ne gfddetach
  gfddone:
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.movRegReg x0, x2
    arm64.movRegReg x2, x4
    arm64.bl mrt_aes_gcm_finish
    arm64.movRegReg x1, x0
    arm64.epilogue 48
    arm64.ret
  gfddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.bl __managed_cow_detach
    arm64.movRegReg x4, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.b gfddone
  gfbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
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
    0040: f9 1c 1c 4e 47 1c 40 b9 e7 08 c0 5a 8a 3c 00 91
    0050: 4a fd 44 d3 e7 00 0a 8b e7 08 c0 5a 47 1c 00 b9
    0060: 5a 0b 20 6e 5a 87 b9 4e 9f 00 02 f1 a3 30 00 54
    0070: 84 00 02 d1 40 0b 20 6e 5a 87 b9 4e 41 0b 20 6e
    0080: 5a 87 b9 4e 42 0b 20 6e 5a 87 b9 4e 43 0b 20 6e
    0090: 5a 87 b9 4e 44 0b 20 6e 5a 87 b9 4e 45 0b 20 6e
    00a0: 5a 87 b9 4e 46 0b 20 6e 5a 87 b9 4e 47 0b 20 6e
    00b0: 5a 87 b9 4e e6 03 08 aa bf 30 00 f1 03 09 00 54
    00c0: 80 04 00 54 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    00d0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    00e0: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    00f0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0100: 67 4b 28 4e e7 68 28 4e db 04 c0 3d 60 4b 28 4e
    0110: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0120: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0130: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0140: c6 68 28 4e 67 4b 28 4e e7 68 28 4e c6 80 00 91
    0150: db 00 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0160: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0170: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0180: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0190: e7 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    01a0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    01b0: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    01c0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    01d0: 67 4b 28 4e e7 68 28 4e c6 80 00 91 db 00 c0 3d
    01e0: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    01f0: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0200: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0210: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0220: db 04 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0230: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0240: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0250: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0260: e7 68 28 4e db 08 c0 3d 60 4b 28 4e 00 68 28 4e
    0270: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0280: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0290: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    02a0: 67 4b 28 4e e7 68 28 4e db 0c c0 3d 60 4b 28 4e
    02b0: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    02c0: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    02d0: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    02e0: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 10 c0 3d
    02f0: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0300: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0310: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0320: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0330: db 14 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0340: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0350: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0360: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0370: e7 68 28 4e db 18 c0 3d 60 4b 28 4e 00 68 28 4e
    0380: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0390: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    03a0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    03b0: 67 4b 28 4e e7 68 28 4e db 1c c0 3d 60 4b 28 4e
    03c0: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    03d0: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    03e0: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    03f0: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 20 c0 3d
    0400: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0410: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0420: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0430: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0440: db 24 c0 3d 60 4b 28 4e 61 4b 28 4e 62 4b 28 4e
    0450: 63 4b 28 4e 64 4b 28 4e 65 4b 28 4e 66 4b 28 4e
    0460: 67 4b 28 4e db 28 c0 3d 00 1c 3b 6e 21 1c 3b 6e
    0470: 42 1c 3b 6e 63 1c 3b 6e 84 1c 3b 6e a5 1c 3b 6e
    0480: c6 1c 3b 6e e7 1c 3b 6e 74 00 c0 3d 75 04 c0 3d
    0490: 00 1c 34 6e 21 1c 35 6e 60 00 80 3d 61 04 80 3d
    04a0: 74 08 c0 3d 75 0c c0 3d 42 1c 34 6e 63 1c 35 6e
    04b0: 62 08 80 3d 63 0c 80 3d 74 10 c0 3d 75 14 c0 3d
    04c0: 84 1c 34 6e a5 1c 35 6e 64 10 80 3d 65 14 80 3d
    04d0: 74 18 c0 3d 75 1c c0 3d c6 1c 34 6e e7 1c 35 6e
    04e0: 66 18 80 3d 67 1c 80 3d 34 00 c0 3d 35 04 c0 3d
    04f0: 00 08 20 4e 00 1c 30 6e 13 40 00 6e 13 1c 33 6e
    0500: 11 e0 f4 0e 10 e0 f4 4e 72 e2 f5 0e 34 08 c0 3d
    0510: 35 0c c0 3d 21 08 20 4e 33 40 01 6e 33 1c 33 6e
    0520: 36 e0 f4 0e 31 1e 36 6e 36 e0 f4 4e 10 1e 36 6e
    0530: 76 e2 f5 0e 52 1e 36 6e 34 10 c0 3d 35 14 c0 3d
    0540: 42 08 20 4e 53 40 02 6e 53 1c 33 6e 56 e0 f4 0e
    0550: 31 1e 36 6e 56 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e
    0560: 52 1e 36 6e 34 18 c0 3d 35 1c c0 3d 63 08 20 4e
    0570: 73 40 03 6e 73 1c 33 6e 76 e0 f4 0e 31 1e 36 6e
    0580: 76 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0590: 34 20 c0 3d 35 24 c0 3d 84 08 20 4e 93 40 04 6e
    05a0: 93 1c 33 6e 96 e0 f4 0e 31 1e 36 6e 96 e0 f4 4e
    05b0: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 34 28 c0 3d
    05c0: 35 2c c0 3d a5 08 20 4e b3 40 05 6e b3 1c 33 6e
    05d0: b6 e0 f4 0e 31 1e 36 6e b6 e0 f4 4e 10 1e 36 6e
    05e0: 76 e2 f5 0e 52 1e 36 6e 34 30 c0 3d 35 34 c0 3d
    05f0: c6 08 20 4e d3 40 06 6e d3 1c 33 6e d6 e0 f4 0e
    0600: 31 1e 36 6e d6 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e
    0610: 52 1e 36 6e 34 38 c0 3d 35 3c c0 3d e7 08 20 4e
    0620: f3 40 07 6e f3 1c 33 6e f6 e0 f4 0e 31 1e 36 6e
    0630: f6 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0640: 52 1e 30 6e 52 1e 31 6e 53 42 18 6e 12 43 12 6e
    0650: 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e
    0660: 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e
    0670: 10 1e 31 6e 63 00 02 91 9f 00 02 f1 a2 cf ff 54
    0680: 24 20 00 b4 9f 40 00 f1 a3 0c 00 54 e9 03 03 aa
    0690: ec 03 1f aa 84 40 00 d1 7c 00 c0 3d 40 0b 20 6e
    06a0: 5a 87 b9 4e e6 03 08 aa bf 30 00 f1 03 02 00 54
    06b0: 00 01 00 54 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    06c0: db 04 c0 3d 60 4b 28 4e 00 68 28 4e c6 80 00 91
    06d0: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    06e0: 60 4b 28 4e 00 68 28 4e c6 80 00 91 db 00 c0 3d
    06f0: 60 4b 28 4e 00 68 28 4e db 04 c0 3d 60 4b 28 4e
    0700: 00 68 28 4e db 08 c0 3d 60 4b 28 4e 00 68 28 4e
    0710: db 0c c0 3d 60 4b 28 4e 00 68 28 4e db 10 c0 3d
    0720: 60 4b 28 4e 00 68 28 4e db 14 c0 3d 60 4b 28 4e
    0730: 00 68 28 4e db 18 c0 3d 60 4b 28 4e 00 68 28 4e
    0740: db 1c c0 3d 60 4b 28 4e 00 68 28 4e db 20 c0 3d
    0750: 60 4b 28 4e 00 68 28 4e db 24 c0 3d 60 4b 28 4e
    0760: db 28 c0 3d 00 1c 3b 6e 00 1c 3c 6e 60 00 80 3d
    0770: 63 40 00 91 8c 41 00 91 9f 40 00 f1 c2 f8 ff 54
    0780: 01 1e b0 4e 10 1e 30 6e 31 1e 31 6e 52 1e 32 6e
    0790: 8e f9 7f d3 0d 20 80 d2 ad 01 0e cb ad 01 01 8b
    07a0: 20 01 c0 3d 00 08 20 4e 00 1c 21 6e 21 1c 21 6e
    07b0: b4 01 c0 3d b5 05 c0 3d 13 40 00 6e 13 1c 33 6e
    07c0: 16 e0 f4 0e 31 1e 36 6e 16 e0 f4 4e 10 1e 36 6e
    07d0: 76 e2 f5 0e 52 1e 36 6e 29 41 00 91 ad 81 00 91
    07e0: 8c 41 00 d1 ec fd ff b5 52 1e 30 6e 52 1e 31 6e
    07f0: 53 42 18 6e 12 43 12 6e 10 1e 32 6e 31 1e 33 6e
    0800: 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e
    0810: 31 1e 33 6e 31 42 11 6e 10 1e 31 6e 34 38 c0 3d
    0820: 35 3c c0 3d 04 13 00 b4 e9 03 03 aa ef 03 04 aa
    0830: eb 03 1f aa ec 03 1f aa 9f 20 00 f1 83 00 00 54
    0840: 2b 01 40 f9 29 21 00 91 ef 21 00 d1 e7 03 1f aa
    0850: ed 03 1f aa ee 01 7e 92 ce 00 00 b4 2e 01 40 b9
    0860: ce 21 cd 9a e7 00 0e aa 29 11 00 91 ad 81 00 91
    0870: ee 01 7f 92 ce 00 00 b4 2e 01 40 79 ce 21 cd 9a
    0880: e7 00 0e aa 29 09 00 91 ad 41 00 91 ee 01 40 92
    0890: ce 00 00 b4 2e 01 40 39 ce 21 cd 9a e7 00 0e aa
    08a0: 29 05 00 91 ad 21 00 91 9f 20 00 f1 ec 20 8c 9a
    08b0: 6b 21 87 9a 7c 1d 08 4e 9c 1d 18 4e 40 0b 20 6e
    08c0: 5a 87 b9 4e e6 03 08 aa bf 30 00 f1 03 02 00 54
    08d0: 00 01 00 54 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    08e0: db 04 c0 3d 60 4b 28 4e 00 68 28 4e c6 80 00 91
    08f0: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    0900: 60 4b 28 4e 00 68 28 4e c6 80 00 91 db 00 c0 3d
    0910: 60 4b 28 4e 00 68 28 4e db 04 c0 3d 60 4b 28 4e
    0920: 00 68 28 4e db 08 c0 3d 60 4b 28 4e 00 68 28 4e
    0930: db 0c c0 3d 60 4b 28 4e 00 68 28 4e db 10 c0 3d
    0940: 60 4b 28 4e 00 68 28 4e db 14 c0 3d 60 4b 28 4e
    0950: 00 68 28 4e db 18 c0 3d 60 4b 28 4e 00 68 28 4e
    0960: db 1c c0 3d 60 4b 28 4e 00 68 28 4e db 20 c0 3d
    0970: 60 4b 28 4e 00 68 28 4e db 24 c0 3d 60 4b 28 4e
    0980: db 28 c0 3d 00 1c 3b 6e 00 1c 3c 6e e9 03 03 aa
    0990: ef 03 04 aa 0b 3c 08 4e 0c 3c 18 4e 9f 20 00 f1
    09a0: a3 00 00 54 2b 01 00 f9 29 21 00 91 ef 21 00 d1
    09b0: eb 03 0c aa ee 01 7e 92 8e 00 00 b4 2b 01 00 b9
    09c0: 29 11 00 91 6b fd 60 d3 ee 01 7f 92 8e 00 00 b4
    09d0: 2b 01 00 79 29 09 00 91 6b fd 50 d3 ee 01 40 92
    09e0: 8e 00 00 b4 2b 01 00 39 29 05 00 91 6b fd 48 d3
    09f0: 0b 3c 08 4e 0c 3c 18 4e 8e 08 40 92 ce f1 7d d3
    0a00: 2d 00 80 d2 ad 21 ce 9a ad 05 00 d1 ee 03 1f aa
    0a10: ce 05 00 d1 ef 03 1f aa 9f 20 00 f1 ce 21 8d 9a
    0a20: af 21 8f 9a 6b 01 0e 8a 8c 01 0f 8a 60 1d 08 4e
    0a30: 80 1d 18 4e 00 08 20 4e 00 1c 30 6e 13 40 00 6e
    0a40: 13 1c 33 6e 11 e0 f4 0e 10 e0 f4 4e 72 e2 f5 0e
    0a50: 52 1e 30 6e 52 1e 31 6e 53 42 18 6e 12 43 12 6e
    0a60: 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e
    0a70: 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e
    0a80: 10 1e 31 6e 50 00 80 3d e0 03 1f aa c0 03 5f d6
}

chunk @mrt_aes_gcm_decrypt {
    0000: 04 fc 48 d3 e5 1f 80 d2 05 00 05 8a 28 00 04 91
    0010: 47 18 80 d2 e7 1c 48 d3 f7 1c 08 4e 27 00 80 d2
    0020: f7 1c 18 4e 18 1f 38 6e 50 00 c0 3d 31 1e 31 6e
    0030: 52 1e 32 6e 5a 04 c0 3d 39 1f 39 6e 27 00 80 d2
    0040: f9 1c 1c 4e 47 1c 40 b9 e7 08 c0 5a 8a 3c 00 91
    0050: 4a fd 44 d3 e7 00 0a 8b e7 08 c0 5a 47 1c 00 b9
    0060: 5a 0b 20 6e 5a 87 b9 4e 9f 00 02 f1 a3 31 00 54
    0070: 84 00 02 d1 40 0b 20 6e 5a 87 b9 4e 41 0b 20 6e
    0080: 5a 87 b9 4e 42 0b 20 6e 5a 87 b9 4e 43 0b 20 6e
    0090: 5a 87 b9 4e 44 0b 20 6e 5a 87 b9 4e 45 0b 20 6e
    00a0: 5a 87 b9 4e 46 0b 20 6e 5a 87 b9 4e 47 0b 20 6e
    00b0: 5a 87 b9 4e e6 03 08 aa bf 30 00 f1 03 09 00 54
    00c0: 80 04 00 54 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    00d0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    00e0: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    00f0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0100: 67 4b 28 4e e7 68 28 4e db 04 c0 3d 60 4b 28 4e
    0110: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0120: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0130: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0140: c6 68 28 4e 67 4b 28 4e e7 68 28 4e c6 80 00 91
    0150: db 00 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0160: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0170: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0180: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0190: e7 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    01a0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    01b0: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    01c0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    01d0: 67 4b 28 4e e7 68 28 4e c6 80 00 91 db 00 c0 3d
    01e0: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    01f0: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0200: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0210: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0220: db 04 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0230: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0240: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0250: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0260: e7 68 28 4e db 08 c0 3d 60 4b 28 4e 00 68 28 4e
    0270: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0280: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0290: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    02a0: 67 4b 28 4e e7 68 28 4e db 0c c0 3d 60 4b 28 4e
    02b0: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    02c0: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    02d0: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    02e0: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 10 c0 3d
    02f0: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0300: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0310: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0320: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0330: db 14 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0340: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0350: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0360: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0370: e7 68 28 4e db 18 c0 3d 60 4b 28 4e 00 68 28 4e
    0380: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0390: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    03a0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    03b0: 67 4b 28 4e e7 68 28 4e db 1c c0 3d 60 4b 28 4e
    03c0: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    03d0: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    03e0: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    03f0: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 20 c0 3d
    0400: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0410: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0420: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0430: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0440: db 24 c0 3d 60 4b 28 4e 61 4b 28 4e 62 4b 28 4e
    0450: 63 4b 28 4e 64 4b 28 4e 65 4b 28 4e 66 4b 28 4e
    0460: 67 4b 28 4e db 28 c0 3d 00 1c 3b 6e 21 1c 3b 6e
    0470: 42 1c 3b 6e 63 1c 3b 6e 84 1c 3b 6e a5 1c 3b 6e
    0480: c6 1c 3b 6e e7 1c 3b 6e 7d 00 c0 3d 34 00 c0 3d
    0490: 35 04 c0 3d bd 0b 20 4e bd 1f 30 6e b3 43 1d 6e
    04a0: b3 1f 33 6e b1 e3 f4 0e b0 e3 f4 4e 72 e2 f5 0e
    04b0: 7d 04 c0 3d 34 08 c0 3d 35 0c c0 3d bd 0b 20 4e
    04c0: b3 43 1d 6e b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e
    04d0: b6 e3 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    04e0: 7d 08 c0 3d 34 10 c0 3d 35 14 c0 3d bd 0b 20 4e
    04f0: b3 43 1d 6e b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e
    0500: b6 e3 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0510: 7d 0c c0 3d 34 18 c0 3d 35 1c c0 3d bd 0b 20 4e
    0520: b3 43 1d 6e b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e
    0530: b6 e3 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0540: 7d 10 c0 3d 34 20 c0 3d 35 24 c0 3d bd 0b 20 4e
    0550: b3 43 1d 6e b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e
    0560: b6 e3 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0570: 7d 14 c0 3d 34 28 c0 3d 35 2c c0 3d bd 0b 20 4e
    0580: b3 43 1d 6e b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e
    0590: b6 e3 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    05a0: 7d 18 c0 3d 34 30 c0 3d 35 34 c0 3d bd 0b 20 4e
    05b0: b3 43 1d 6e b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e
    05c0: b6 e3 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    05d0: 7d 1c c0 3d 34 38 c0 3d 35 3c c0 3d bd 0b 20 4e
    05e0: b3 43 1d 6e b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e
    05f0: b6 e3 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0600: 52 1e 30 6e 52 1e 31 6e 53 42 18 6e 12 43 12 6e
    0610: 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e
    0620: 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e
    0630: 10 1e 31 6e 7c 00 c0 3d 00 1c 3c 6e 60 00 80 3d
    0640: 7c 04 c0 3d 21 1c 3c 6e 61 04 80 3d 7c 08 c0 3d
    0650: 42 1c 3c 6e 62 08 80 3d 7c 0c c0 3d 63 1c 3c 6e
    0660: 63 0c 80 3d 7c 10 c0 3d 84 1c 3c 6e 64 10 80 3d
    0670: 7c 14 c0 3d a5 1c 3c 6e 65 14 80 3d 7c 18 c0 3d
    0680: c6 1c 3c 6e 66 18 80 3d 7c 1c c0 3d e7 1c 3c 6e
    0690: 67 1c 80 3d 63 00 02 91 9f 00 02 f1 a2 ce ff 54
    06a0: 24 1e 00 b4 9f 40 00 f1 c3 0c 00 54 e9 03 03 aa
    06b0: 8c fc 44 d3 8c ed 7c d3 01 1e b0 4e 10 1e 30 6e
    06c0: 31 1e 31 6e 52 1e 32 6e 8e f9 7f d3 0d 20 80 d2
    06d0: ad 01 0e cb ad 01 01 8b 20 01 c0 3d 00 08 20 4e
    06e0: 00 1c 21 6e 21 1c 21 6e b4 01 c0 3d b5 05 c0 3d
    06f0: 13 40 00 6e 13 1c 33 6e 16 e0 f4 0e 31 1e 36 6e
    0700: 16 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0710: 29 41 00 91 ad 81 00 91 8c 41 00 d1 ec fd ff b5
    0720: 52 1e 30 6e 52 1e 31 6e 53 42 18 6e 12 43 12 6e
    0730: 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e
    0740: 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e
    0750: 10 1e 31 6e 84 40 00 d1 7c 00 c0 3d 40 0b 20 6e
    0760: 5a 87 b9 4e e6 03 08 aa bf 30 00 f1 03 02 00 54
    0770: 00 01 00 54 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    0780: db 04 c0 3d 60 4b 28 4e 00 68 28 4e c6 80 00 91
    0790: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    07a0: 60 4b 28 4e 00 68 28 4e c6 80 00 91 db 00 c0 3d
    07b0: 60 4b 28 4e 00 68 28 4e db 04 c0 3d 60 4b 28 4e
    07c0: 00 68 28 4e db 08 c0 3d 60 4b 28 4e 00 68 28 4e
    07d0: db 0c c0 3d 60 4b 28 4e 00 68 28 4e db 10 c0 3d
    07e0: 60 4b 28 4e 00 68 28 4e db 14 c0 3d 60 4b 28 4e
    07f0: 00 68 28 4e db 18 c0 3d 60 4b 28 4e 00 68 28 4e
    0800: db 1c c0 3d 60 4b 28 4e 00 68 28 4e db 20 c0 3d
    0810: 60 4b 28 4e 00 68 28 4e db 24 c0 3d 60 4b 28 4e
    0820: db 28 c0 3d 00 1c 3b 6e 00 1c 3c 6e 60 00 80 3d
    0830: 63 40 00 91 8c 41 00 91 9f 40 00 f1 c2 f8 ff 54
    0840: 34 38 c0 3d 35 3c c0 3d e4 10 00 b4 e9 03 03 aa
    0850: ef 03 04 aa eb 03 1f aa ec 03 1f aa 9f 20 00 f1
    0860: 83 00 00 54 2b 01 40 f9 29 21 00 91 ef 21 00 d1
    0870: e7 03 1f aa ed 03 1f aa ee 01 7e 92 ce 00 00 b4
    0880: 2e 01 40 b9 ce 21 cd 9a e7 00 0e aa 29 11 00 91
    0890: ad 81 00 91 ee 01 7f 92 ce 00 00 b4 2e 01 40 79
    08a0: ce 21 cd 9a e7 00 0e aa 29 09 00 91 ad 41 00 91
    08b0: ee 01 40 92 ce 00 00 b4 2e 01 40 39 ce 21 cd 9a
    08c0: e7 00 0e aa 29 05 00 91 ad 21 00 91 9f 20 00 f1
    08d0: ec 20 8c 9a 6b 21 87 9a 7c 1d 08 4e 9c 1d 18 4e
    08e0: 40 0b 20 6e 5a 87 b9 4e e6 03 08 aa bf 30 00 f1
    08f0: 03 02 00 54 00 01 00 54 db 00 c0 3d 60 4b 28 4e
    0900: 00 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    0910: c6 80 00 91 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    0920: db 04 c0 3d 60 4b 28 4e 00 68 28 4e c6 80 00 91
    0930: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    0940: 60 4b 28 4e 00 68 28 4e db 08 c0 3d 60 4b 28 4e
    0950: 00 68 28 4e db 0c c0 3d 60 4b 28 4e 00 68 28 4e
    0960: db 10 c0 3d 60 4b 28 4e 00 68 28 4e db 14 c0 3d
    0970: 60 4b 28 4e 00 68 28 4e db 18 c0 3d 60 4b 28 4e
    0980: 00 68 28 4e db 1c c0 3d 60 4b 28 4e 00 68 28 4e
    0990: db 20 c0 3d 60 4b 28 4e 00 68 28 4e db 24 c0 3d
    09a0: 60 4b 28 4e db 28 c0 3d 00 1c 3b 6e 00 1c 3c 6e
    09b0: e9 03 03 aa ef 03 04 aa 0b 3c 08 4e 0c 3c 18 4e
    09c0: 9f 20 00 f1 a3 00 00 54 2b 01 00 f9 29 21 00 91
    09d0: ef 21 00 d1 eb 03 0c aa ee 01 7e 92 8e 00 00 b4
    09e0: 2b 01 00 b9 29 11 00 91 6b fd 60 d3 ee 01 7f 92
    09f0: 8e 00 00 b4 2b 01 00 79 29 09 00 91 6b fd 50 d3
    0a00: ee 01 40 92 8e 00 00 b4 2b 01 00 39 29 05 00 91
    0a10: 6b fd 48 d3 9c 0b 20 4e 9c 1f 30 6e 93 43 1c 6e
    0a20: 93 1f 33 6e 91 e3 f4 0e 90 e3 f4 4e 72 e2 f5 0e
    0a30: 52 1e 30 6e 52 1e 31 6e 53 42 18 6e 12 43 12 6e
    0a40: 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e
    0a50: 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e
    0a60: 10 1e 31 6e 50 00 80 3d e0 03 1f aa c0 03 5f d6
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
    arm64.sub x3, x3, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne giddetach
  giddone:
    arm64.lsr x2, x2, 5
    arm64.sub x2, x2, 1
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x4, [x1 + 0]
    arm64.movImm x5, 0
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x3
    arm64.movRegReg x2, x4
    arm64.movRegReg x3, x5
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

func @__cpufeat_aes_cipher_gcm_hash {
  entry:
    arm64.prologue 64
    arm64.loadRegBaseDisp.word64 x5, [x1 + 8]
    arm64.cmp x5, 432
    arm64.b.lt ghbad
  kbbounded:
    arm64.cmp x5, 496
    arm64.b.gt ghbad
  kbstepped:
    arm64.and x5, x5, 31
    arm64.cmp x5, 16
    arm64.b.ne ghbad
  gkmessage:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 8]
    arm64.cmp x5, 48
    arm64.b.ne ghbad
  ghspan:
    arm64.loadRegBaseDisp.word64 x5, [x2 + 8]
    arm64.cmp x3, x5
    arm64.b.hi ghbad
  ghinside:
    arm64.sub x5, x5, x3
    arm64.cmp x5, x4
    arm64.b.lo ghbad
  ghcrypt:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 16]
    arm64.cmp x5, 0
    arm64.b.lt ghddetach
  ghdbuf:
    arm64.loadRegBaseDisp.word64 x5, [x0 + 0]
    arm64.cmp x5, 0
    arm64.b.eq ghddone
  ghdshared:
    arm64.sub x5, x5, 24
    arm64.loadRegBaseDisp.word64 x5, [x5 + 16]
    arm64.cmp x5, 0
    arm64.b.ne ghddetach
  ghddone:
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.loadRegBaseDisp.word64 x5, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x2 + 0]
    arm64.add x3, x0, x3
    arm64.movRegReg x0, x4
    arm64.movRegReg x2, x5
    arm64.bl mrt_aes_gcm_hash
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
  ghddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.bl __managed_cow_detach
    arm64.movRegReg x5, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.b ghddone
  ghbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_encrypt {
  entry:
    arm64.prologue 64
    arm64.loadRegBaseDisp.word64 x5, [x2 + 8]
    arm64.cmp x5, 432
    arm64.b.lt gcbad
  kbbounded:
    arm64.cmp x5, 496
    arm64.b.gt gcbad
  kbstepped:
    arm64.and x6, x5, 31
    arm64.cmp x6, 16
    arm64.b.ne gcbad
  gkmessage:
    arm64.loadRegBaseDisp.word64 x6, [x3 + 8]
    arm64.cmp x6, 48
    arm64.b.ne gcbad
  gcspan:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 8]
    arm64.cmp x1, x6
    arm64.b.hi gcbad
  gcinside:
    arm64.sub x6, x6, x1
    arm64.cmp x6, x4
    arm64.b.lo gcbad
  gccrypt:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 16]
    arm64.cmp x6, 0
    arm64.b.lt gbddetach
  gbdbuf:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 0]
    arm64.cmp x6, 0
    arm64.b.eq gbddone
  gbdshared:
    arm64.sub x6, x6, 24
    arm64.loadRegBaseDisp.word64 x6, [x6 + 16]
    arm64.cmp x6, 0
    arm64.b.ne gbddetach
  gbddone:
    arm64.loadRegBaseDisp.word64 x6, [x3 + 16]
    arm64.cmp x6, 0
    arm64.b.lt gmddetach
  gmdbuf:
    arm64.loadRegBaseDisp.word64 x6, [x3 + 0]
    arm64.cmp x6, 0
    arm64.b.eq gmddone
  gmdshared:
    arm64.sub x6, x6, 24
    arm64.loadRegBaseDisp.word64 x6, [x6 + 16]
    arm64.cmp x6, 0
    arm64.b.ne gmddetach
  gmddone:
    arm64.sub x5, x5, 256
    arm64.lsr x5, x5, 4
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x5, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.movRegReg x3, x5
    arm64.bl mrt_aes_gcm_encrypt
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
  gbddetach:
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
    arm64.b gbddone
  gmddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.storeSlotReg slot5, x5
    arm64.movRegReg x0, x3
    arm64.bl __managed_cow_detach
    arm64.movRegReg x6, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.loadRegSlot x5, slot5
    arm64.b gmddone
  gcbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_decrypt {
  entry:
    arm64.prologue 64
    arm64.loadRegBaseDisp.word64 x5, [x2 + 8]
    arm64.cmp x5, 432
    arm64.b.lt gcbad
  kbbounded:
    arm64.cmp x5, 496
    arm64.b.gt gcbad
  kbstepped:
    arm64.and x6, x5, 31
    arm64.cmp x6, 16
    arm64.b.ne gcbad
  gkmessage:
    arm64.loadRegBaseDisp.word64 x6, [x3 + 8]
    arm64.cmp x6, 48
    arm64.b.ne gcbad
  gcspan:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 8]
    arm64.cmp x1, x6
    arm64.b.hi gcbad
  gcinside:
    arm64.sub x6, x6, x1
    arm64.cmp x6, x4
    arm64.b.lo gcbad
  gccrypt:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 16]
    arm64.cmp x6, 0
    arm64.b.lt gbddetach
  gbdbuf:
    arm64.loadRegBaseDisp.word64 x6, [x0 + 0]
    arm64.cmp x6, 0
    arm64.b.eq gbddone
  gbdshared:
    arm64.sub x6, x6, 24
    arm64.loadRegBaseDisp.word64 x6, [x6 + 16]
    arm64.cmp x6, 0
    arm64.b.ne gbddetach
  gbddone:
    arm64.loadRegBaseDisp.word64 x6, [x3 + 16]
    arm64.cmp x6, 0
    arm64.b.lt gmddetach
  gmdbuf:
    arm64.loadRegBaseDisp.word64 x6, [x3 + 0]
    arm64.cmp x6, 0
    arm64.b.eq gmddone
  gmdshared:
    arm64.sub x6, x6, 24
    arm64.loadRegBaseDisp.word64 x6, [x6 + 16]
    arm64.cmp x6, 0
    arm64.b.ne gmddetach
  gmddone:
    arm64.sub x5, x5, 256
    arm64.lsr x5, x5, 4
    arm64.sub x5, x5, 1
    arm64.lsl x4, x4, 8
    arm64.orr x4, x4, x5
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x5, x0, x1
    arm64.movRegReg x0, x4
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.movRegReg x3, x5
    arm64.bl mrt_aes_gcm_decrypt
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
  gbddetach:
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
    arm64.b gbddone
  gmddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.storeSlotReg slot5, x5
    arm64.movRegReg x0, x3
    arm64.bl __managed_cow_detach
    arm64.movRegReg x6, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.loadRegSlot x5, slot5
    arm64.b gmddone
  gcbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.epilogue 64
    arm64.ret
}

func @__cpufeat_aes_cipher_gcm_finish {
  entry:
    arm64.prologue 48
    arm64.loadRegBaseDisp.word64 x4, [x1 + 8]
    arm64.cmp x4, 432
    arm64.b.lt gfbad
  kbbounded:
    arm64.cmp x4, 496
    arm64.b.gt gfbad
  kbstepped:
    arm64.and x4, x4, 31
    arm64.cmp x4, 16
    arm64.b.ne gfbad
  gkmessage:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 8]
    arm64.cmp x4, 48
    arm64.b.ne gfbad
  gfcrypt:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt gfddetach
  gfdbuf:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.cmp x4, 0
    arm64.b.eq gfddone
  gfdshared:
    arm64.sub x4, x4, 24
    arm64.loadRegBaseDisp.word64 x4, [x4 + 16]
    arm64.cmp x4, 0
    arm64.b.ne gfddetach
  gfddone:
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.movRegReg x0, x2
    arm64.movRegReg x2, x4
    arm64.bl mrt_aes_gcm_finish
    arm64.movRegReg x1, x0
    arm64.epilogue 48
    arm64.ret
  gfddetach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.bl __managed_cow_detach
    arm64.movRegReg x4, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.b gfddone
  gfbad:
    arm64.leaRdata x0, __fatal_msg_13  ; "fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size\x0a"
    arm64.movImm x1, 89
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
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
    0040: f9 1c 1c 4e 47 1c 40 b9 e7 08 c0 5a 8a 3c 00 91
    0050: 4a fd 44 d3 e7 00 0a 8b e7 08 c0 5a 47 1c 00 b9
    0060: 5a 0b 20 6e 5a 87 b9 4e 9f 00 02 f1 a3 30 00 54
    0070: 84 00 02 d1 40 0b 20 6e 5a 87 b9 4e 41 0b 20 6e
    0080: 5a 87 b9 4e 42 0b 20 6e 5a 87 b9 4e 43 0b 20 6e
    0090: 5a 87 b9 4e 44 0b 20 6e 5a 87 b9 4e 45 0b 20 6e
    00a0: 5a 87 b9 4e 46 0b 20 6e 5a 87 b9 4e 47 0b 20 6e
    00b0: 5a 87 b9 4e e6 03 08 aa bf 30 00 f1 03 09 00 54
    00c0: 80 04 00 54 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    00d0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    00e0: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    00f0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0100: 67 4b 28 4e e7 68 28 4e db 04 c0 3d 60 4b 28 4e
    0110: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0120: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0130: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0140: c6 68 28 4e 67 4b 28 4e e7 68 28 4e c6 80 00 91
    0150: db 00 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0160: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0170: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0180: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0190: e7 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    01a0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    01b0: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    01c0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    01d0: 67 4b 28 4e e7 68 28 4e c6 80 00 91 db 00 c0 3d
    01e0: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    01f0: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0200: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0210: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0220: db 04 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0230: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0240: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0250: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0260: e7 68 28 4e db 08 c0 3d 60 4b 28 4e 00 68 28 4e
    0270: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0280: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0290: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    02a0: 67 4b 28 4e e7 68 28 4e db 0c c0 3d 60 4b 28 4e
    02b0: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    02c0: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    02d0: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    02e0: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 10 c0 3d
    02f0: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0300: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0310: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0320: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0330: db 14 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0340: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0350: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0360: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0370: e7 68 28 4e db 18 c0 3d 60 4b 28 4e 00 68 28 4e
    0380: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0390: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    03a0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    03b0: 67 4b 28 4e e7 68 28 4e db 1c c0 3d 60 4b 28 4e
    03c0: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    03d0: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    03e0: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    03f0: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 20 c0 3d
    0400: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0410: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0420: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0430: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0440: db 24 c0 3d 60 4b 28 4e 61 4b 28 4e 62 4b 28 4e
    0450: 63 4b 28 4e 64 4b 28 4e 65 4b 28 4e 66 4b 28 4e
    0460: 67 4b 28 4e db 28 c0 3d 00 1c 3b 6e 21 1c 3b 6e
    0470: 42 1c 3b 6e 63 1c 3b 6e 84 1c 3b 6e a5 1c 3b 6e
    0480: c6 1c 3b 6e e7 1c 3b 6e 74 00 c0 3d 75 04 c0 3d
    0490: 00 1c 34 6e 21 1c 35 6e 60 00 80 3d 61 04 80 3d
    04a0: 74 08 c0 3d 75 0c c0 3d 42 1c 34 6e 63 1c 35 6e
    04b0: 62 08 80 3d 63 0c 80 3d 74 10 c0 3d 75 14 c0 3d
    04c0: 84 1c 34 6e a5 1c 35 6e 64 10 80 3d 65 14 80 3d
    04d0: 74 18 c0 3d 75 1c c0 3d c6 1c 34 6e e7 1c 35 6e
    04e0: 66 18 80 3d 67 1c 80 3d 34 00 c0 3d 35 04 c0 3d
    04f0: 00 08 20 4e 00 1c 30 6e 13 40 00 6e 13 1c 33 6e
    0500: 11 e0 f4 0e 10 e0 f4 4e 72 e2 f5 0e 34 08 c0 3d
    0510: 35 0c c0 3d 21 08 20 4e 33 40 01 6e 33 1c 33 6e
    0520: 36 e0 f4 0e 31 1e 36 6e 36 e0 f4 4e 10 1e 36 6e
    0530: 76 e2 f5 0e 52 1e 36 6e 34 10 c0 3d 35 14 c0 3d
    0540: 42 08 20 4e 53 40 02 6e 53 1c 33 6e 56 e0 f4 0e
    0550: 31 1e 36 6e 56 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e
    0560: 52 1e 36 6e 34 18 c0 3d 35 1c c0 3d 63 08 20 4e
    0570: 73 40 03 6e 73 1c 33 6e 76 e0 f4 0e 31 1e 36 6e
    0580: 76 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0590: 34 20 c0 3d 35 24 c0 3d 84 08 20 4e 93 40 04 6e
    05a0: 93 1c 33 6e 96 e0 f4 0e 31 1e 36 6e 96 e0 f4 4e
    05b0: 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e 34 28 c0 3d
    05c0: 35 2c c0 3d a5 08 20 4e b3 40 05 6e b3 1c 33 6e
    05d0: b6 e0 f4 0e 31 1e 36 6e b6 e0 f4 4e 10 1e 36 6e
    05e0: 76 e2 f5 0e 52 1e 36 6e 34 30 c0 3d 35 34 c0 3d
    05f0: c6 08 20 4e d3 40 06 6e d3 1c 33 6e d6 e0 f4 0e
    0600: 31 1e 36 6e d6 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e
    0610: 52 1e 36 6e 34 38 c0 3d 35 3c c0 3d e7 08 20 4e
    0620: f3 40 07 6e f3 1c 33 6e f6 e0 f4 0e 31 1e 36 6e
    0630: f6 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0640: 52 1e 30 6e 52 1e 31 6e 53 42 18 6e 12 43 12 6e
    0650: 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e
    0660: 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e
    0670: 10 1e 31 6e 63 00 02 91 9f 00 02 f1 a2 cf ff 54
    0680: 24 20 00 b4 9f 40 00 f1 a3 0c 00 54 e9 03 03 aa
    0690: ec 03 1f aa 84 40 00 d1 7c 00 c0 3d 40 0b 20 6e
    06a0: 5a 87 b9 4e e6 03 08 aa bf 30 00 f1 03 02 00 54
    06b0: 00 01 00 54 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    06c0: db 04 c0 3d 60 4b 28 4e 00 68 28 4e c6 80 00 91
    06d0: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    06e0: 60 4b 28 4e 00 68 28 4e c6 80 00 91 db 00 c0 3d
    06f0: 60 4b 28 4e 00 68 28 4e db 04 c0 3d 60 4b 28 4e
    0700: 00 68 28 4e db 08 c0 3d 60 4b 28 4e 00 68 28 4e
    0710: db 0c c0 3d 60 4b 28 4e 00 68 28 4e db 10 c0 3d
    0720: 60 4b 28 4e 00 68 28 4e db 14 c0 3d 60 4b 28 4e
    0730: 00 68 28 4e db 18 c0 3d 60 4b 28 4e 00 68 28 4e
    0740: db 1c c0 3d 60 4b 28 4e 00 68 28 4e db 20 c0 3d
    0750: 60 4b 28 4e 00 68 28 4e db 24 c0 3d 60 4b 28 4e
    0760: db 28 c0 3d 00 1c 3b 6e 00 1c 3c 6e 60 00 80 3d
    0770: 63 40 00 91 8c 41 00 91 9f 40 00 f1 c2 f8 ff 54
    0780: 01 1e b0 4e 10 1e 30 6e 31 1e 31 6e 52 1e 32 6e
    0790: 8e f9 7f d3 0d 20 80 d2 ad 01 0e cb ad 01 01 8b
    07a0: 20 01 c0 3d 00 08 20 4e 00 1c 21 6e 21 1c 21 6e
    07b0: b4 01 c0 3d b5 05 c0 3d 13 40 00 6e 13 1c 33 6e
    07c0: 16 e0 f4 0e 31 1e 36 6e 16 e0 f4 4e 10 1e 36 6e
    07d0: 76 e2 f5 0e 52 1e 36 6e 29 41 00 91 ad 81 00 91
    07e0: 8c 41 00 d1 ec fd ff b5 52 1e 30 6e 52 1e 31 6e
    07f0: 53 42 18 6e 12 43 12 6e 10 1e 32 6e 31 1e 33 6e
    0800: 13 e2 f7 0e 10 42 10 6e 10 1e 33 6e 13 e2 f7 0e
    0810: 31 1e 33 6e 31 42 11 6e 10 1e 31 6e 34 38 c0 3d
    0820: 35 3c c0 3d 04 13 00 b4 e9 03 03 aa ef 03 04 aa
    0830: eb 03 1f aa ec 03 1f aa 9f 20 00 f1 83 00 00 54
    0840: 2b 01 40 f9 29 21 00 91 ef 21 00 d1 e7 03 1f aa
    0850: ed 03 1f aa ee 01 7e 92 ce 00 00 b4 2e 01 40 b9
    0860: ce 21 cd 9a e7 00 0e aa 29 11 00 91 ad 81 00 91
    0870: ee 01 7f 92 ce 00 00 b4 2e 01 40 79 ce 21 cd 9a
    0880: e7 00 0e aa 29 09 00 91 ad 41 00 91 ee 01 40 92
    0890: ce 00 00 b4 2e 01 40 39 ce 21 cd 9a e7 00 0e aa
    08a0: 29 05 00 91 ad 21 00 91 9f 20 00 f1 ec 20 8c 9a
    08b0: 6b 21 87 9a 7c 1d 08 4e 9c 1d 18 4e 40 0b 20 6e
    08c0: 5a 87 b9 4e e6 03 08 aa bf 30 00 f1 03 02 00 54
    08d0: 00 01 00 54 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    08e0: db 04 c0 3d 60 4b 28 4e 00 68 28 4e c6 80 00 91
    08f0: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    0900: 60 4b 28 4e 00 68 28 4e c6 80 00 91 db 00 c0 3d
    0910: 60 4b 28 4e 00 68 28 4e db 04 c0 3d 60 4b 28 4e
    0920: 00 68 28 4e db 08 c0 3d 60 4b 28 4e 00 68 28 4e
    0930: db 0c c0 3d 60 4b 28 4e 00 68 28 4e db 10 c0 3d
    0940: 60 4b 28 4e 00 68 28 4e db 14 c0 3d 60 4b 28 4e
    0950: 00 68 28 4e db 18 c0 3d 60 4b 28 4e 00 68 28 4e
    0960: db 1c c0 3d 60 4b 28 4e 00 68 28 4e db 20 c0 3d
    0970: 60 4b 28 4e 00 68 28 4e db 24 c0 3d 60 4b 28 4e
    0980: db 28 c0 3d 00 1c 3b 6e 00 1c 3c 6e e9 03 03 aa
    0990: ef 03 04 aa 0b 3c 08 4e 0c 3c 18 4e 9f 20 00 f1
    09a0: a3 00 00 54 2b 01 00 f9 29 21 00 91 ef 21 00 d1
    09b0: eb 03 0c aa ee 01 7e 92 8e 00 00 b4 2b 01 00 b9
    09c0: 29 11 00 91 6b fd 60 d3 ee 01 7f 92 8e 00 00 b4
    09d0: 2b 01 00 79 29 09 00 91 6b fd 50 d3 ee 01 40 92
    09e0: 8e 00 00 b4 2b 01 00 39 29 05 00 91 6b fd 48 d3
    09f0: 0b 3c 08 4e 0c 3c 18 4e 8e 08 40 92 ce f1 7d d3
    0a00: 2d 00 80 d2 ad 21 ce 9a ad 05 00 d1 ee 03 1f aa
    0a10: ce 05 00 d1 ef 03 1f aa 9f 20 00 f1 ce 21 8d 9a
    0a20: af 21 8f 9a 6b 01 0e 8a 8c 01 0f 8a 60 1d 08 4e
    0a30: 80 1d 18 4e 00 08 20 4e 00 1c 30 6e 13 40 00 6e
    0a40: 13 1c 33 6e 11 e0 f4 0e 10 e0 f4 4e 72 e2 f5 0e
    0a50: 52 1e 30 6e 52 1e 31 6e 53 42 18 6e 12 43 12 6e
    0a60: 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e
    0a70: 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e
    0a80: 10 1e 31 6e 50 00 80 3d e0 03 1f aa c0 03 5f d6
}

chunk @mrt_aes_gcm_decrypt {
    0000: 04 fc 48 d3 e5 1f 80 d2 05 00 05 8a 28 00 04 91
    0010: 47 18 80 d2 e7 1c 48 d3 f7 1c 08 4e 27 00 80 d2
    0020: f7 1c 18 4e 18 1f 38 6e 50 00 c0 3d 31 1e 31 6e
    0030: 52 1e 32 6e 5a 04 c0 3d 39 1f 39 6e 27 00 80 d2
    0040: f9 1c 1c 4e 47 1c 40 b9 e7 08 c0 5a 8a 3c 00 91
    0050: 4a fd 44 d3 e7 00 0a 8b e7 08 c0 5a 47 1c 00 b9
    0060: 5a 0b 20 6e 5a 87 b9 4e 9f 00 02 f1 a3 31 00 54
    0070: 84 00 02 d1 40 0b 20 6e 5a 87 b9 4e 41 0b 20 6e
    0080: 5a 87 b9 4e 42 0b 20 6e 5a 87 b9 4e 43 0b 20 6e
    0090: 5a 87 b9 4e 44 0b 20 6e 5a 87 b9 4e 45 0b 20 6e
    00a0: 5a 87 b9 4e 46 0b 20 6e 5a 87 b9 4e 47 0b 20 6e
    00b0: 5a 87 b9 4e e6 03 08 aa bf 30 00 f1 03 09 00 54
    00c0: 80 04 00 54 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    00d0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    00e0: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    00f0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    0100: 67 4b 28 4e e7 68 28 4e db 04 c0 3d 60 4b 28 4e
    0110: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    0120: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    0130: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    0140: c6 68 28 4e 67 4b 28 4e e7 68 28 4e c6 80 00 91
    0150: db 00 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0160: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0170: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0180: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0190: e7 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    01a0: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    01b0: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    01c0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    01d0: 67 4b 28 4e e7 68 28 4e c6 80 00 91 db 00 c0 3d
    01e0: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    01f0: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0200: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0210: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0220: db 04 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0230: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0240: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0250: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0260: e7 68 28 4e db 08 c0 3d 60 4b 28 4e 00 68 28 4e
    0270: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0280: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    0290: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    02a0: 67 4b 28 4e e7 68 28 4e db 0c c0 3d 60 4b 28 4e
    02b0: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    02c0: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    02d0: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    02e0: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 10 c0 3d
    02f0: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0300: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0310: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0320: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0330: db 14 c0 3d 60 4b 28 4e 00 68 28 4e 61 4b 28 4e
    0340: 21 68 28 4e 62 4b 28 4e 42 68 28 4e 63 4b 28 4e
    0350: 63 68 28 4e 64 4b 28 4e 84 68 28 4e 65 4b 28 4e
    0360: a5 68 28 4e 66 4b 28 4e c6 68 28 4e 67 4b 28 4e
    0370: e7 68 28 4e db 18 c0 3d 60 4b 28 4e 00 68 28 4e
    0380: 61 4b 28 4e 21 68 28 4e 62 4b 28 4e 42 68 28 4e
    0390: 63 4b 28 4e 63 68 28 4e 64 4b 28 4e 84 68 28 4e
    03a0: 65 4b 28 4e a5 68 28 4e 66 4b 28 4e c6 68 28 4e
    03b0: 67 4b 28 4e e7 68 28 4e db 1c c0 3d 60 4b 28 4e
    03c0: 00 68 28 4e 61 4b 28 4e 21 68 28 4e 62 4b 28 4e
    03d0: 42 68 28 4e 63 4b 28 4e 63 68 28 4e 64 4b 28 4e
    03e0: 84 68 28 4e 65 4b 28 4e a5 68 28 4e 66 4b 28 4e
    03f0: c6 68 28 4e 67 4b 28 4e e7 68 28 4e db 20 c0 3d
    0400: 60 4b 28 4e 00 68 28 4e 61 4b 28 4e 21 68 28 4e
    0410: 62 4b 28 4e 42 68 28 4e 63 4b 28 4e 63 68 28 4e
    0420: 64 4b 28 4e 84 68 28 4e 65 4b 28 4e a5 68 28 4e
    0430: 66 4b 28 4e c6 68 28 4e 67 4b 28 4e e7 68 28 4e
    0440: db 24 c0 3d 60 4b 28 4e 61 4b 28 4e 62 4b 28 4e
    0450: 63 4b 28 4e 64 4b 28 4e 65 4b 28 4e 66 4b 28 4e
    0460: 67 4b 28 4e db 28 c0 3d 00 1c 3b 6e 21 1c 3b 6e
    0470: 42 1c 3b 6e 63 1c 3b 6e 84 1c 3b 6e a5 1c 3b 6e
    0480: c6 1c 3b 6e e7 1c 3b 6e 7d 00 c0 3d 34 00 c0 3d
    0490: 35 04 c0 3d bd 0b 20 4e bd 1f 30 6e b3 43 1d 6e
    04a0: b3 1f 33 6e b1 e3 f4 0e b0 e3 f4 4e 72 e2 f5 0e
    04b0: 7d 04 c0 3d 34 08 c0 3d 35 0c c0 3d bd 0b 20 4e
    04c0: b3 43 1d 6e b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e
    04d0: b6 e3 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    04e0: 7d 08 c0 3d 34 10 c0 3d 35 14 c0 3d bd 0b 20 4e
    04f0: b3 43 1d 6e b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e
    0500: b6 e3 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0510: 7d 0c c0 3d 34 18 c0 3d 35 1c c0 3d bd 0b 20 4e
    0520: b3 43 1d 6e b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e
    0530: b6 e3 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0540: 7d 10 c0 3d 34 20 c0 3d 35 24 c0 3d bd 0b 20 4e
    0550: b3 43 1d 6e b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e
    0560: b6 e3 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0570: 7d 14 c0 3d 34 28 c0 3d 35 2c c0 3d bd 0b 20 4e
    0580: b3 43 1d 6e b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e
    0590: b6 e3 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    05a0: 7d 18 c0 3d 34 30 c0 3d 35 34 c0 3d bd 0b 20 4e
    05b0: b3 43 1d 6e b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e
    05c0: b6 e3 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    05d0: 7d 1c c0 3d 34 38 c0 3d 35 3c c0 3d bd 0b 20 4e
    05e0: b3 43 1d 6e b3 1f 33 6e b6 e3 f4 0e 31 1e 36 6e
    05f0: b6 e3 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0600: 52 1e 30 6e 52 1e 31 6e 53 42 18 6e 12 43 12 6e
    0610: 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e
    0620: 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e
    0630: 10 1e 31 6e 7c 00 c0 3d 00 1c 3c 6e 60 00 80 3d
    0640: 7c 04 c0 3d 21 1c 3c 6e 61 04 80 3d 7c 08 c0 3d
    0650: 42 1c 3c 6e 62 08 80 3d 7c 0c c0 3d 63 1c 3c 6e
    0660: 63 0c 80 3d 7c 10 c0 3d 84 1c 3c 6e 64 10 80 3d
    0670: 7c 14 c0 3d a5 1c 3c 6e 65 14 80 3d 7c 18 c0 3d
    0680: c6 1c 3c 6e 66 18 80 3d 7c 1c c0 3d e7 1c 3c 6e
    0690: 67 1c 80 3d 63 00 02 91 9f 00 02 f1 a2 ce ff 54
    06a0: 24 1e 00 b4 9f 40 00 f1 c3 0c 00 54 e9 03 03 aa
    06b0: 8c fc 44 d3 8c ed 7c d3 01 1e b0 4e 10 1e 30 6e
    06c0: 31 1e 31 6e 52 1e 32 6e 8e f9 7f d3 0d 20 80 d2
    06d0: ad 01 0e cb ad 01 01 8b 20 01 c0 3d 00 08 20 4e
    06e0: 00 1c 21 6e 21 1c 21 6e b4 01 c0 3d b5 05 c0 3d
    06f0: 13 40 00 6e 13 1c 33 6e 16 e0 f4 0e 31 1e 36 6e
    0700: 16 e0 f4 4e 10 1e 36 6e 76 e2 f5 0e 52 1e 36 6e
    0710: 29 41 00 91 ad 81 00 91 8c 41 00 d1 ec fd ff b5
    0720: 52 1e 30 6e 52 1e 31 6e 53 42 18 6e 12 43 12 6e
    0730: 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e
    0740: 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e
    0750: 10 1e 31 6e 84 40 00 d1 7c 00 c0 3d 40 0b 20 6e
    0760: 5a 87 b9 4e e6 03 08 aa bf 30 00 f1 03 02 00 54
    0770: 00 01 00 54 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    0780: db 04 c0 3d 60 4b 28 4e 00 68 28 4e c6 80 00 91
    0790: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    07a0: 60 4b 28 4e 00 68 28 4e c6 80 00 91 db 00 c0 3d
    07b0: 60 4b 28 4e 00 68 28 4e db 04 c0 3d 60 4b 28 4e
    07c0: 00 68 28 4e db 08 c0 3d 60 4b 28 4e 00 68 28 4e
    07d0: db 0c c0 3d 60 4b 28 4e 00 68 28 4e db 10 c0 3d
    07e0: 60 4b 28 4e 00 68 28 4e db 14 c0 3d 60 4b 28 4e
    07f0: 00 68 28 4e db 18 c0 3d 60 4b 28 4e 00 68 28 4e
    0800: db 1c c0 3d 60 4b 28 4e 00 68 28 4e db 20 c0 3d
    0810: 60 4b 28 4e 00 68 28 4e db 24 c0 3d 60 4b 28 4e
    0820: db 28 c0 3d 00 1c 3b 6e 00 1c 3c 6e 60 00 80 3d
    0830: 63 40 00 91 8c 41 00 91 9f 40 00 f1 c2 f8 ff 54
    0840: 34 38 c0 3d 35 3c c0 3d e4 10 00 b4 e9 03 03 aa
    0850: ef 03 04 aa eb 03 1f aa ec 03 1f aa 9f 20 00 f1
    0860: 83 00 00 54 2b 01 40 f9 29 21 00 91 ef 21 00 d1
    0870: e7 03 1f aa ed 03 1f aa ee 01 7e 92 ce 00 00 b4
    0880: 2e 01 40 b9 ce 21 cd 9a e7 00 0e aa 29 11 00 91
    0890: ad 81 00 91 ee 01 7f 92 ce 00 00 b4 2e 01 40 79
    08a0: ce 21 cd 9a e7 00 0e aa 29 09 00 91 ad 41 00 91
    08b0: ee 01 40 92 ce 00 00 b4 2e 01 40 39 ce 21 cd 9a
    08c0: e7 00 0e aa 29 05 00 91 ad 21 00 91 9f 20 00 f1
    08d0: ec 20 8c 9a 6b 21 87 9a 7c 1d 08 4e 9c 1d 18 4e
    08e0: 40 0b 20 6e 5a 87 b9 4e e6 03 08 aa bf 30 00 f1
    08f0: 03 02 00 54 00 01 00 54 db 00 c0 3d 60 4b 28 4e
    0900: 00 68 28 4e db 04 c0 3d 60 4b 28 4e 00 68 28 4e
    0910: c6 80 00 91 db 00 c0 3d 60 4b 28 4e 00 68 28 4e
    0920: db 04 c0 3d 60 4b 28 4e 00 68 28 4e c6 80 00 91
    0930: db 00 c0 3d 60 4b 28 4e 00 68 28 4e db 04 c0 3d
    0940: 60 4b 28 4e 00 68 28 4e db 08 c0 3d 60 4b 28 4e
    0950: 00 68 28 4e db 0c c0 3d 60 4b 28 4e 00 68 28 4e
    0960: db 10 c0 3d 60 4b 28 4e 00 68 28 4e db 14 c0 3d
    0970: 60 4b 28 4e 00 68 28 4e db 18 c0 3d 60 4b 28 4e
    0980: 00 68 28 4e db 1c c0 3d 60 4b 28 4e 00 68 28 4e
    0990: db 20 c0 3d 60 4b 28 4e 00 68 28 4e db 24 c0 3d
    09a0: 60 4b 28 4e db 28 c0 3d 00 1c 3b 6e 00 1c 3c 6e
    09b0: e9 03 03 aa ef 03 04 aa 0b 3c 08 4e 0c 3c 18 4e
    09c0: 9f 20 00 f1 a3 00 00 54 2b 01 00 f9 29 21 00 91
    09d0: ef 21 00 d1 eb 03 0c aa ee 01 7e 92 8e 00 00 b4
    09e0: 2b 01 00 b9 29 11 00 91 6b fd 60 d3 ee 01 7f 92
    09f0: 8e 00 00 b4 2b 01 00 79 29 09 00 91 6b fd 50 d3
    0a00: ee 01 40 92 8e 00 00 b4 2b 01 00 39 29 05 00 91
    0a10: 6b fd 48 d3 9c 0b 20 4e 9c 1f 30 6e 93 43 1c 6e
    0a20: 93 1f 33 6e 91 e3 f4 0e 90 e3 f4 4e 72 e2 f5 0e
    0a30: 52 1e 30 6e 52 1e 31 6e 53 42 18 6e 12 43 12 6e
    0a40: 10 1e 32 6e 31 1e 33 6e 13 e2 f7 0e 10 42 10 6e
    0a50: 10 1e 33 6e 13 e2 f7 0e 31 1e 33 6e 31 42 11 6e
    0a60: 10 1e 31 6e 50 00 80 3d e0 03 1f aa c0 03 5f d6
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
