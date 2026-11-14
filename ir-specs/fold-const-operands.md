---
feature: fold-const-operands
status: experimental
keywords: [optimizer, codegen, immediate, identity, fold, runtime]
category: codegen
---

# Folding Constant Operands: the cases that pin emitted code

The cases of `specs/fold-const-operands.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: the-emitted-runtime-is-folded-too -->
`__managed_fill` and `__managed_get` — two bodies the compiler installs after the pipeline, so the
pipeline's own run of the pass never saw them. Every `movRegImm32 <reg>, <k>` immediately followed
by a `cmpRegReg` against that register is an unfolded compare; the `TargetIr` pin shows `cmpRegImm32`
instead, and no `movRegImm32` feeding a compare at all.

`refill` is what reaches both: it fills a window through `__managed_fill` and the `get` reads one
slot back out.

```maxon
function main() returns ExitCode
	let a = FullRangeIntArray from [1, 2, 3]
	a.refill(6, value: 7)
	return (try a.get(5) otherwise 9) as ExitCode
end 'main'
```
```exitcode
7
```
```RequiredRuntime
__managed_fill
__managed_get
```

```TargetIr:x64-windows
data {
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_console_probe_stdin@48 = i8 0
  __mrt_console_probe_stdout@49 = i8 0
  __mrt_console_probe_stderr@50 = i8 0
  __mrt_program_started@51 = i8 0
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
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [rbx + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegImm32 r12, 6
    x64.movRegImm32 r13, 7
  __il_body#12:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __managed_resize
    x64.movRegImm32 r14, 0
  __il_body#14:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.movRegReg rax, r12
    x64.movRegReg r9, r13
    x64.callDirect __managed_fill
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#15
  tryerr#16:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at Array.maxon:441: Array.fillPublished: fill OOB \xe2\x80\x94 the window is inside the length the caller just published\x0a"
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
  tryok#15:
    x64.xorRegImm32 r8, r8, 1
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, forhdr
    x64.jmp __il_cont
  fill:
    x64.loadRegBaseDisp.word64 rax, [rbx + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __im_slow#29
  __im_buffer:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_slow#29
  __im_owned:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rcx, rax, -16
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, __im_detach
  __im_detached:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegReg r14, rcx
    x64.jcc aboveEqual, __im_slow#29
  __im_store:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 24]
    x64.cmpRegImm32 rcx, 8
    x64.jcc notEqual, __im_stride#37
  __im_word:
    x64.storeBaseIndexScaleReg.word64 [rax + r14*8 + 0], r13
    x64.jmp forstep
  __im_stride#37:
    x64.cmpRegImm32 rcx, 1
    x64.jcc equal, __im_byte
  __im_stride#39:
    x64.cmpRegImm32 rcx, -1
    x64.jcc notEqual, __im_stride#41
  __im_bit:
    x64.movRegReg rcx, r14
    x64.shrRegImm8 rcx, r14, 3
    x64.movRegReg rdx, r14
    x64.andRegImm32 rdx, r14, 7
    x64.movRegImm32 rsi, 1
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegBaseDisp.byte rdi, [rax + 0]
    x64.movRegReg rcx, rdx
    x64.shlRegCl rsi, rsi
    x64.xorRegImm32 rsi, rsi, -1
    x64.andRegReg rdi, rdi, rsi
    x64.movRegReg rsi, r13
    x64.andRegImm32 rsi, r13, 1
    x64.movRegReg rcx, rdx
    x64.shlRegCl rsi, rsi
    x64.orRegReg rdi, rdi, rsi
    x64.storeBaseDispReg.byte [rax + 0], rdi
    x64.jmp forstep
  __im_stride#41:
    x64.cmpRegImm32 rcx, 2
    x64.jcc equal, __im_bytes2
  __im_stride#43:
    x64.cmpRegImm32 rcx, 4
    x64.jcc equal, __im_bytes4
  __im_stride#45:
    x64.cmpRegImm32 rcx, -2
    x64.jcc notEqual, __im_stride#47
  __im_crumb:
    x64.imulRegRegImm32 rcx, r14, 2
    x64.movRegReg rdx, rcx
    x64.shrRegImm8 rdx, rcx, 3
    x64.movRegReg rsi, rcx
    x64.andRegImm32 rsi, rcx, 7
    x64.movRegImm32 rdi, 3
    x64.leaRegRegReg rax, rax, rdx
    x64.loadRegBaseDisp.byte rdx, [rax + 0]
    x64.movRegReg rcx, rsi
    x64.shlRegCl rdi, rdi
    x64.xorRegImm32 rdi, rdi, -1
    x64.andRegReg rdx, rdx, rdi
    x64.movRegReg rdi, r13
    x64.andRegImm32 rdi, r13, 3
    x64.movRegReg rcx, rsi
    x64.shlRegCl rdi, rdi
    x64.orRegReg rdx, rdx, rdi
    x64.storeBaseDispReg.byte [rax + 0], rdx
    x64.jmp forstep
  __im_stride#47:
    x64.cmpRegImm32 rcx, -4
    x64.jcc notEqual, __im_stride#49
  __im_nibble:
    x64.imulRegRegImm32 rcx, r14, 4
    x64.movRegReg rdx, rcx
    x64.shrRegImm8 rdx, rcx, 3
    x64.movRegReg rsi, rcx
    x64.andRegImm32 rsi, rcx, 7
    x64.movRegImm32 rdi, 15
    x64.leaRegRegReg rax, rax, rdx
    x64.loadRegBaseDisp.byte rdx, [rax + 0]
    x64.movRegReg rcx, rsi
    x64.shlRegCl rdi, rdi
    x64.xorRegImm32 rdi, rdi, -1
    x64.andRegReg rdx, rdx, rdi
    x64.movRegReg rdi, r13
    x64.andRegImm32 rdi, r13, 15
    x64.movRegReg rcx, rsi
    x64.shlRegCl rdi, rdi
    x64.orRegReg rdx, rdx, rdi
    x64.storeBaseDispReg.byte [rax + 0], rdx
    x64.jmp forstep
  __im_stride#49:
    x64.movRegImm rdx, 4294967297
    x64.cmpRegReg rcx, rdx
    x64.jcc notEqual, __im_stride#50
  __im_byte:
    x64.leaRegRegReg rax, rax, r14
    x64.storeBaseDispReg.byte [rax + 0], r13
    x64.jmp forstep
  __im_stride#50:
    x64.movRegImm rdx, 4294967298
    x64.cmpRegReg rcx, rdx
    x64.jcc notEqual, __im_stride#51
  __im_bytes2:
    x64.storeBaseIndexScaleReg.word16 [rax + r14*2 + 0], r13
    x64.jmp forstep
  __im_stride#51:
    x64.movRegImm rdx, 4294967300
    x64.cmpRegReg rcx, rdx
    x64.jcc notEqual, __im_slow#29
  __im_bytes4:
    x64.storeBaseIndexScaleReg.word32 [rax + r14*4 + 0], r13
    x64.jmp forstep
  __im_slow#29:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.movRegReg rax, r13
    x64.callDirect __managed_mem_set
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, forstep
  tryerr#23:
    x64.leaRegRdata rax, [rip + __str_rec_6]  ; "panic at Array.maxon:447: "
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.leaRegRdata rax, [rip + __str_rec_7]  ; "Array.fillPublished: set OOB at "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot7, rcx
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r15
    x64.callDirect __uint_to_string
    x64.storeSlotReg slot6, r8
    x64.leaRegRdata rax, [rip + __str_rec_8]  ; " \xe2\x80\x94 the window is inside the published length"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot5, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot4, rax
    x64.leaRegRdata rcx, [rip + __str_rec_9]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.storeSlotReg slot3, rdx
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.storeSlotReg slot2, rcx
    x64.leaRegRegReg rdx, r12, r13
    x64.leaRegRegReg rdx, rdx, r8
    x64.leaRegRegReg rax, rdx, rax
    x64.leaRegRegReg rax, rax, rcx
    x64.storeSlotReg slot0, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot1, r8
    x64.leaRegRegImm32 r14, r8, 56
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r14, r12
    x64.loadRegSlot rdx, slot7
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r13
    x64.loadRegSlot rax, slot6
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot6
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot4
    x64.loadRegSlot rdx, slot5
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot4
    x64.leaRegRegReg rcx, rbx, rax
    x64.loadRegSlot rax, slot2
    x64.loadRegSlot rdx, slot3
    x64.callDirect __str_copy
    x64.loadRegSlot rbx, slot1
    x64.storeBaseDispReg.word64 [rbx + 0], r14
    x64.loadRegSlot rax, slot0
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
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
  forstep:
    x64.leaRegRegImm32 r14, r14, 1
  forhdr:
    x64.cmpRegImm32 r14, 6
    x64.jcc below, fill
  __il_cont:
    x64.movRegImm32 rdx, 5
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 5
    x64.jcc belowEqual, __im_slow#9
    x64.jmp __im_load
  tryerr#2:
    x64.movRegImm32 r12, 9
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rax + rdx*8 + 0]
  tryok#1:
    x64.movRegReg r12, rax
  trycont:
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r12, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_detach:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jmp __im_detached
  __im_slow#9:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#2
  critsplit:
    x64.movRegReg rax, r8
    x64.jmp tryok#1
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_11]  ; "panic at the-emitted-runtime-is-folded-too.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
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

func @__managed_get {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegReg rax, rcx
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.cmpRegReg rdx, rcx
    x64.jcc below, load
  oob:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 1
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  load:
    x64.loadRegBaseDisp.word64 rsi, [rax + 0]
    x64.loadRegBaseDisp.word64 rcx, [rax + 24]
    x64.cmpRegImm32 rcx, 8
    x64.jcc notEqual, ld#7
  ldword:
    x64.loadRegBaseIndexScale.word64 r8, [rsi + rdx*8 + 0]
    x64.jmp gchkdtor
  ld#7:
    x64.cmpRegImm32 rcx, 1
    x64.jcc notEqual, ld#9
  ldbyte:
    x64.leaRegRegReg rcx, rsi, rdx
    x64.loadRegBaseDisp.byte r8, [rcx + 0]
    x64.jmp gchkdtor
  ld#9:
    x64.cmpRegImm32 rcx, -1
    x64.jcc notEqual, ld#11
  ldbit:
    x64.movRegReg rdi, rdx
    x64.shrRegImm8 rdi, rdx, 3
    x64.movRegReg rcx, rdx
    x64.andRegImm32 rcx, rdx, 7
    x64.leaRegRegReg rdx, rsi, rdi
    x64.loadRegBaseDisp.byte rdx, [rdx + 0]
    x64.shrRegCl rdx, rdx
    x64.andRegImm32 rdx, rdx, 1
    x64.movRegReg r8, rdx
    x64.jmp gchkdtor
  ld#11:
    x64.cmpRegImm32 rcx, 2
    x64.jcc notEqual, ld#13
  ldbytes2:
    x64.loadRegBaseIndexScale.word16 r8, [rsi + rdx*2 + 0]
    x64.jmp gchkdtor
  ld#13:
    x64.cmpRegImm32 rcx, 4
    x64.jcc notEqual, ld#15
  ldbytes4:
    x64.loadRegBaseIndexScale.word32 r8, [rsi + rdx*4 + 0]
    x64.jmp gchkdtor
  ld#15:
    x64.cmpRegImm32 rcx, -2
    x64.jcc notEqual, ld#17
  ldcrumb:
    x64.imulRegRegImm32 rdx, rdx, 2
    x64.movRegReg rdi, rdx
    x64.shrRegImm8 rdi, rdx, 3
    x64.movRegReg rcx, rdx
    x64.andRegImm32 rcx, rdx, 7
    x64.leaRegRegReg rdx, rsi, rdi
    x64.loadRegBaseDisp.byte rdx, [rdx + 0]
    x64.shrRegCl rdx, rdx
    x64.andRegImm32 rdx, rdx, 3
    x64.movRegReg r8, rdx
    x64.jmp gchkdtor
  ld#17:
    x64.cmpRegImm32 rcx, -4
    x64.jcc notEqual, ld#19
  ldnibble:
    x64.imulRegRegImm32 rdx, rdx, 4
    x64.movRegReg rdi, rdx
    x64.shrRegImm8 rdi, rdx, 3
    x64.movRegReg rcx, rdx
    x64.andRegImm32 rcx, rdx, 7
    x64.leaRegRegReg rdx, rsi, rdi
    x64.loadRegBaseDisp.byte rdx, [rdx + 0]
    x64.shrRegCl rdx, rdx
    x64.andRegImm32 rdx, rdx, 15
    x64.movRegReg r8, rdx
    x64.jmp gchkdtor
  ld#19:
    x64.movRegImm rdi, 4294967297
    x64.cmpRegReg rcx, rdi
    x64.jcc notEqual, ld#21
  ldsbyte:
    x64.leaRegRegReg rcx, rsi, rdx
    x64.loadRegBaseDisp.byte rcx, [rcx + 0]
    x64.shlRegImm8 rcx, rcx, 56
    x64.sarRegImm8 rcx, rcx, 56
    x64.movRegReg r8, rcx
    x64.jmp gchkdtor
  ld#21:
    x64.movRegImm rdi, 4294967298
    x64.cmpRegReg rcx, rdi
    x64.jcc notEqual, ld#23
  ldsbytes2:
    x64.loadRegBaseIndexScale.word16 rcx, [rsi + rdx*2 + 0]
    x64.shlRegImm8 rcx, rcx, 48
    x64.sarRegImm8 rcx, rcx, 48
    x64.movRegReg r8, rcx
    x64.jmp gchkdtor
  ld#23:
    x64.movRegImm rdi, 4294967300
    x64.cmpRegReg rcx, rdi
    x64.jcc notEqual, ld#5
  ldsbytes4:
    x64.loadRegBaseIndexScale.word32 rcx, [rsi + rdx*4 + 0]
    x64.shlRegImm8 rcx, rcx, 32
    x64.sarRegImm8 rcx, rcx, 32
    x64.movRegReg r8, rcx
  gchkdtor:
    x64.loadRegBaseDisp.word64 rax, [rax + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, gok
  gchknull:
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, gok
  gempty:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 2
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  gok:
    x64.movRegImm32 r10, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ld#5:
    x64.leaRegRdata rcx, [rip + __fatal_msg_14]  ; "fatal error: an array record carries an element size no element access serves\x0a"
    x64.movRegImm32 rdx, 78
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @__managed_fill {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, badrange
  chkcount:
    x64.cmpRegImm32 rax, 0
    x64.jcc less, badrange
  chkhigh:
    x64.leaRegRegReg rax, rdx, rax
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegReg rax, rsi
    x64.jcc belowEqual, classify
  badrange:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 1
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  classify:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 40]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, prepare
  declined:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  prepare:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, flddetach
  fldbuf:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, flddone
  fldshared:
    x64.leaRegRegImm32 rsi, rsi, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, flddetach
  flddone:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 24]
    x64.cmpRegImm32 rcx, 8
    x64.jcc notEqual, fi#14
  fih#29:
    x64.cmpRegReg rdx, rax
    x64.jcc greaterEqual, applied
  fib#30:
    x64.storeBaseIndexScaleReg.word64 [rsi + rdx*8 + 0], r9
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.jmp fih#29
  fi#14:
    x64.cmpRegImm32 rcx, 1
    x64.jcc equal, fih#32
  fi#16:
    x64.cmpRegImm32 rcx, -1
    x64.jcc notEqual, fi#18
  fih#35:
    x64.cmpRegReg rdx, rax
    x64.jcc greaterEqual, applied
  fib#36:
    x64.movRegReg rcx, rdx
    x64.shrRegImm8 rcx, rdx, 3
    x64.movRegReg rdi, rdx
    x64.andRegImm32 rdi, rdx, 7
    x64.movRegImm32 r8, 1
    x64.leaRegRegReg r10, rsi, rcx
    x64.loadRegBaseDisp.byte r11, [r10 + 0]
    x64.movRegReg rcx, rdi
    x64.shlRegCl r8, r8
    x64.xorRegImm32 r8, r8, -1
    x64.andRegReg r11, r11, r8
    x64.movRegReg r8, r9
    x64.andRegImm32 r8, r9, 1
    x64.movRegReg rcx, rdi
    x64.shlRegCl r8, r8
    x64.orRegReg r11, r11, r8
    x64.storeBaseDispReg.byte [r10 + 0], r11
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.jmp fih#35
  fi#18:
    x64.cmpRegImm32 rcx, 2
    x64.jcc equal, fih#38
  fi#20:
    x64.cmpRegImm32 rcx, 4
    x64.jcc equal, fih#41
  fi#22:
    x64.cmpRegImm32 rcx, -2
    x64.jcc notEqual, fi#24
  fih#44:
    x64.cmpRegReg rdx, rax
    x64.jcc greaterEqual, applied
  fib#45:
    x64.imulRegRegImm32 rcx, rdx, 2
    x64.movRegReg rdi, rcx
    x64.shrRegImm8 rdi, rcx, 3
    x64.movRegReg r8, rcx
    x64.andRegImm32 r8, rcx, 7
    x64.movRegImm32 r10, 3
    x64.leaRegRegReg rdi, rsi, rdi
    x64.loadRegBaseDisp.byte r11, [rdi + 0]
    x64.movRegReg rcx, r8
    x64.shlRegCl r10, r10
    x64.xorRegImm32 r10, r10, -1
    x64.andRegReg r11, r11, r10
    x64.movRegReg r10, r9
    x64.andRegImm32 r10, r9, 3
    x64.movRegReg rcx, r8
    x64.shlRegCl r10, r10
    x64.orRegReg r11, r11, r10
    x64.storeBaseDispReg.byte [rdi + 0], r11
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.jmp fih#44
  fi#24:
    x64.cmpRegImm32 rcx, -4
    x64.jcc notEqual, fi#26
  fih#47:
    x64.cmpRegReg rdx, rax
    x64.jcc greaterEqual, applied
  fib#48:
    x64.imulRegRegImm32 rcx, rdx, 4
    x64.movRegReg rdi, rcx
    x64.shrRegImm8 rdi, rcx, 3
    x64.movRegReg r8, rcx
    x64.andRegImm32 r8, rcx, 7
    x64.movRegImm32 r10, 15
    x64.leaRegRegReg rdi, rsi, rdi
    x64.loadRegBaseDisp.byte r11, [rdi + 0]
    x64.movRegReg rcx, r8
    x64.shlRegCl r10, r10
    x64.xorRegImm32 r10, r10, -1
    x64.andRegReg r11, r11, r10
    x64.movRegReg r10, r9
    x64.andRegImm32 r10, r9, 15
    x64.movRegReg rcx, r8
    x64.shlRegCl r10, r10
    x64.orRegReg r11, r11, r10
    x64.storeBaseDispReg.byte [rdi + 0], r11
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.jmp fih#47
  fi#26:
    x64.movRegImm rdi, 4294967297
    x64.cmpRegReg rcx, rdi
    x64.jcc notEqual, fi#27
  fih#32:
    x64.cmpRegReg rdx, rax
    x64.jcc greaterEqual, applied
  fib#33:
    x64.leaRegRegReg rcx, rsi, rdx
    x64.storeBaseDispReg.byte [rcx + 0], r9
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.jmp fih#32
  fi#27:
    x64.movRegImm rdi, 4294967298
    x64.cmpRegReg rcx, rdi
    x64.jcc notEqual, fi#28
  fih#38:
    x64.cmpRegReg rdx, rax
    x64.jcc greaterEqual, applied
  fib#39:
    x64.storeBaseIndexScaleReg.word16 [rsi + rdx*2 + 0], r9
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.jmp fih#38
  fi#28:
    x64.movRegImm rdi, 4294967300
    x64.cmpRegReg rcx, rdi
    x64.jcc notEqual, fi#12
  fih#41:
    x64.cmpRegReg rdx, rax
    x64.jcc less, fib#42
  applied:
    x64.movRegImm32 r8, 1
    x64.movRegImm32 r10, 0
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  fib#42:
    x64.storeBaseIndexScaleReg.word32 [rsi + rdx*4 + 0], r9
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.jmp fih#41
  flddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, r9
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot r9, slot3
    x64.jmp flddone
  fi#12:
    x64.leaRegRdata rcx, [rip + __fatal_msg_14]  ; "fatal error: an array record carries an element size no element access serves\x0a"
    x64.movRegImm32 rdx, 78
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 64
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
    x64.prologue 104
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [rbx + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegImm32 r12, 6
    x64.movRegImm32 r13, 7
  __il_body#12:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __managed_resize
    x64.movRegImm32 r14, 0
  __il_body#14:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.movRegReg rax, r12
    x64.movRegReg r9, r13
    x64.callDirect __managed_fill
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#15
  tryerr#16:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at Array.maxon:441: Array.fillPublished: fill OOB \xe2\x80\x94 the window is inside the length the caller just published\x0a"
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
  tryok#15:
    x64.xorRegImm32 r8, r8, 1
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, forhdr
    x64.jmp __il_cont
  fill:
    x64.loadRegBaseDisp.word64 rax, [rbx + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __im_slow#29
  __im_buffer:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_slow#29
  __im_owned:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rcx, rax, -16
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, __im_detach
  __im_detached:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegReg r14, rcx
    x64.jcc aboveEqual, __im_slow#29
  __im_store:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 24]
    x64.cmpRegImm32 rcx, 8
    x64.jcc notEqual, __im_stride#37
  __im_word:
    x64.storeBaseIndexScaleReg.word64 [rax + r14*8 + 0], r13
    x64.jmp forstep
  __im_stride#37:
    x64.cmpRegImm32 rcx, 1
    x64.jcc equal, __im_byte
  __im_stride#39:
    x64.cmpRegImm32 rcx, -1
    x64.jcc notEqual, __im_stride#41
  __im_bit:
    x64.movRegReg rcx, r14
    x64.shrRegImm8 rcx, r14, 3
    x64.movRegReg rdx, r14
    x64.andRegImm32 rdx, r14, 7
    x64.movRegImm32 rsi, 1
    x64.leaRegRegReg rax, rax, rcx
    x64.loadRegBaseDisp.byte rdi, [rax + 0]
    x64.movRegReg rcx, rdx
    x64.shlRegCl rsi, rsi
    x64.xorRegImm32 rsi, rsi, -1
    x64.andRegReg rdi, rdi, rsi
    x64.movRegReg rsi, r13
    x64.andRegImm32 rsi, r13, 1
    x64.movRegReg rcx, rdx
    x64.shlRegCl rsi, rsi
    x64.orRegReg rdi, rdi, rsi
    x64.storeBaseDispReg.byte [rax + 0], rdi
    x64.jmp forstep
  __im_stride#41:
    x64.cmpRegImm32 rcx, 2
    x64.jcc equal, __im_bytes2
  __im_stride#43:
    x64.cmpRegImm32 rcx, 4
    x64.jcc equal, __im_bytes4
  __im_stride#45:
    x64.cmpRegImm32 rcx, -2
    x64.jcc notEqual, __im_stride#47
  __im_crumb:
    x64.imulRegRegImm32 rcx, r14, 2
    x64.movRegReg rdx, rcx
    x64.shrRegImm8 rdx, rcx, 3
    x64.movRegReg rsi, rcx
    x64.andRegImm32 rsi, rcx, 7
    x64.movRegImm32 rdi, 3
    x64.leaRegRegReg rax, rax, rdx
    x64.loadRegBaseDisp.byte rdx, [rax + 0]
    x64.movRegReg rcx, rsi
    x64.shlRegCl rdi, rdi
    x64.xorRegImm32 rdi, rdi, -1
    x64.andRegReg rdx, rdx, rdi
    x64.movRegReg rdi, r13
    x64.andRegImm32 rdi, r13, 3
    x64.movRegReg rcx, rsi
    x64.shlRegCl rdi, rdi
    x64.orRegReg rdx, rdx, rdi
    x64.storeBaseDispReg.byte [rax + 0], rdx
    x64.jmp forstep
  __im_stride#47:
    x64.cmpRegImm32 rcx, -4
    x64.jcc notEqual, __im_stride#49
  __im_nibble:
    x64.imulRegRegImm32 rcx, r14, 4
    x64.movRegReg rdx, rcx
    x64.shrRegImm8 rdx, rcx, 3
    x64.movRegReg rsi, rcx
    x64.andRegImm32 rsi, rcx, 7
    x64.movRegImm32 rdi, 15
    x64.leaRegRegReg rax, rax, rdx
    x64.loadRegBaseDisp.byte rdx, [rax + 0]
    x64.movRegReg rcx, rsi
    x64.shlRegCl rdi, rdi
    x64.xorRegImm32 rdi, rdi, -1
    x64.andRegReg rdx, rdx, rdi
    x64.movRegReg rdi, r13
    x64.andRegImm32 rdi, r13, 15
    x64.movRegReg rcx, rsi
    x64.shlRegCl rdi, rdi
    x64.orRegReg rdx, rdx, rdi
    x64.storeBaseDispReg.byte [rax + 0], rdx
    x64.jmp forstep
  __im_stride#49:
    x64.movRegImm rdx, 4294967297
    x64.cmpRegReg rcx, rdx
    x64.jcc notEqual, __im_stride#50
  __im_byte:
    x64.leaRegRegReg rax, rax, r14
    x64.storeBaseDispReg.byte [rax + 0], r13
    x64.jmp forstep
  __im_stride#50:
    x64.movRegImm rdx, 4294967298
    x64.cmpRegReg rcx, rdx
    x64.jcc notEqual, __im_stride#51
  __im_bytes2:
    x64.storeBaseIndexScaleReg.word16 [rax + r14*2 + 0], r13
    x64.jmp forstep
  __im_stride#51:
    x64.movRegImm rdx, 4294967300
    x64.cmpRegReg rcx, rdx
    x64.jcc notEqual, __im_slow#29
  __im_bytes4:
    x64.storeBaseIndexScaleReg.word32 [rax + r14*4 + 0], r13
    x64.jmp forstep
  __im_slow#29:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.movRegReg rax, r13
    x64.callDirect __managed_mem_set
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, forstep
  tryerr#23:
    x64.leaRegRdata rax, [rip + __str_rec_6]  ; "panic at Array.maxon:447: "
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.leaRegRdata rax, [rip + __str_rec_7]  ; "Array.fillPublished: set OOB at "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot7, rcx
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r15
    x64.callDirect __uint_to_string
    x64.storeSlotReg slot6, r8
    x64.leaRegRdata rax, [rip + __str_rec_8]  ; " \xe2\x80\x94 the window is inside the published length"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot5, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot4, rax
    x64.leaRegRdata rcx, [rip + __str_rec_9]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.storeSlotReg slot3, rdx
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.storeSlotReg slot2, rcx
    x64.leaRegRegReg rdx, r12, r13
    x64.leaRegRegReg rdx, rdx, r8
    x64.leaRegRegReg rax, rdx, rax
    x64.leaRegRegReg rax, rax, rcx
    x64.storeSlotReg slot0, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot1, r8
    x64.leaRegRegImm32 r14, r8, 56
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r14, r12
    x64.loadRegSlot rdx, slot7
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r13
    x64.loadRegSlot rax, slot6
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot6
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot4
    x64.loadRegSlot rdx, slot5
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot4
    x64.leaRegRegReg rcx, rbx, rax
    x64.loadRegSlot rax, slot2
    x64.loadRegSlot rdx, slot3
    x64.callDirect __str_copy
    x64.loadRegSlot rbx, slot1
    x64.storeBaseDispReg.word64 [rbx + 0], r14
    x64.loadRegSlot rax, slot0
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
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
  forstep:
    x64.leaRegRegImm32 r14, r14, 1
  forhdr:
    x64.cmpRegImm32 r14, 6
    x64.jcc below, fill
  __il_cont:
    x64.movRegImm32 rdx, 5
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 5
    x64.jcc belowEqual, __im_slow#9
    x64.jmp __im_load
  tryerr#2:
    x64.movRegImm32 r12, 9
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rax + rdx*8 + 0]
  tryok#1:
    x64.movRegReg r12, rax
  trycont:
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r12, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_detach:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jmp __im_detached
  __im_slow#9:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#2
  critsplit:
    x64.movRegReg rax, r8
    x64.jmp tryok#1
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_11]  ; "panic at the-emitted-runtime-is-folded-too.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
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

func @__managed_get {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegReg rax, rcx
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.cmpRegReg rdx, rcx
    x64.jcc below, load
  oob:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 1
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  load:
    x64.loadRegBaseDisp.word64 rsi, [rax + 0]
    x64.loadRegBaseDisp.word64 rcx, [rax + 24]
    x64.cmpRegImm32 rcx, 8
    x64.jcc notEqual, ld#7
  ldword:
    x64.loadRegBaseIndexScale.word64 r8, [rsi + rdx*8 + 0]
    x64.jmp gchkdtor
  ld#7:
    x64.cmpRegImm32 rcx, 1
    x64.jcc notEqual, ld#9
  ldbyte:
    x64.leaRegRegReg rcx, rsi, rdx
    x64.loadRegBaseDisp.byte r8, [rcx + 0]
    x64.jmp gchkdtor
  ld#9:
    x64.cmpRegImm32 rcx, -1
    x64.jcc notEqual, ld#11
  ldbit:
    x64.movRegReg rdi, rdx
    x64.shrRegImm8 rdi, rdx, 3
    x64.movRegReg rcx, rdx
    x64.andRegImm32 rcx, rdx, 7
    x64.leaRegRegReg rdx, rsi, rdi
    x64.loadRegBaseDisp.byte rdx, [rdx + 0]
    x64.shrRegCl rdx, rdx
    x64.andRegImm32 rdx, rdx, 1
    x64.movRegReg r8, rdx
    x64.jmp gchkdtor
  ld#11:
    x64.cmpRegImm32 rcx, 2
    x64.jcc notEqual, ld#13
  ldbytes2:
    x64.loadRegBaseIndexScale.word16 r8, [rsi + rdx*2 + 0]
    x64.jmp gchkdtor
  ld#13:
    x64.cmpRegImm32 rcx, 4
    x64.jcc notEqual, ld#15
  ldbytes4:
    x64.loadRegBaseIndexScale.word32 r8, [rsi + rdx*4 + 0]
    x64.jmp gchkdtor
  ld#15:
    x64.cmpRegImm32 rcx, -2
    x64.jcc notEqual, ld#17
  ldcrumb:
    x64.imulRegRegImm32 rdx, rdx, 2
    x64.movRegReg rdi, rdx
    x64.shrRegImm8 rdi, rdx, 3
    x64.movRegReg rcx, rdx
    x64.andRegImm32 rcx, rdx, 7
    x64.leaRegRegReg rdx, rsi, rdi
    x64.loadRegBaseDisp.byte rdx, [rdx + 0]
    x64.shrRegCl rdx, rdx
    x64.andRegImm32 rdx, rdx, 3
    x64.movRegReg r8, rdx
    x64.jmp gchkdtor
  ld#17:
    x64.cmpRegImm32 rcx, -4
    x64.jcc notEqual, ld#19
  ldnibble:
    x64.imulRegRegImm32 rdx, rdx, 4
    x64.movRegReg rdi, rdx
    x64.shrRegImm8 rdi, rdx, 3
    x64.movRegReg rcx, rdx
    x64.andRegImm32 rcx, rdx, 7
    x64.leaRegRegReg rdx, rsi, rdi
    x64.loadRegBaseDisp.byte rdx, [rdx + 0]
    x64.shrRegCl rdx, rdx
    x64.andRegImm32 rdx, rdx, 15
    x64.movRegReg r8, rdx
    x64.jmp gchkdtor
  ld#19:
    x64.movRegImm rdi, 4294967297
    x64.cmpRegReg rcx, rdi
    x64.jcc notEqual, ld#21
  ldsbyte:
    x64.leaRegRegReg rcx, rsi, rdx
    x64.loadRegBaseDisp.byte rcx, [rcx + 0]
    x64.shlRegImm8 rcx, rcx, 56
    x64.sarRegImm8 rcx, rcx, 56
    x64.movRegReg r8, rcx
    x64.jmp gchkdtor
  ld#21:
    x64.movRegImm rdi, 4294967298
    x64.cmpRegReg rcx, rdi
    x64.jcc notEqual, ld#23
  ldsbytes2:
    x64.loadRegBaseIndexScale.word16 rcx, [rsi + rdx*2 + 0]
    x64.shlRegImm8 rcx, rcx, 48
    x64.sarRegImm8 rcx, rcx, 48
    x64.movRegReg r8, rcx
    x64.jmp gchkdtor
  ld#23:
    x64.movRegImm rdi, 4294967300
    x64.cmpRegReg rcx, rdi
    x64.jcc notEqual, ld#5
  ldsbytes4:
    x64.loadRegBaseIndexScale.word32 rcx, [rsi + rdx*4 + 0]
    x64.shlRegImm8 rcx, rcx, 32
    x64.sarRegImm8 rcx, rcx, 32
    x64.movRegReg r8, rcx
  gchkdtor:
    x64.loadRegBaseDisp.word64 rax, [rax + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, gok
  gchknull:
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, gok
  gempty:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 2
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  gok:
    x64.movRegImm32 r10, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ld#5:
    x64.leaRegRdata rcx, [rip + __fatal_msg_14]  ; "fatal error: an array record carries an element size no element access serves\x0a"
    x64.movRegImm32 rdx, 78
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @__managed_fill {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, badrange
  chkcount:
    x64.cmpRegImm32 rax, 0
    x64.jcc less, badrange
  chkhigh:
    x64.leaRegRegReg rax, rdx, rax
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.cmpRegReg rax, rsi
    x64.jcc belowEqual, classify
  badrange:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 1
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  classify:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 40]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, prepare
  declined:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  prepare:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, flddetach
  fldbuf:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, flddone
  fldshared:
    x64.leaRegRegImm32 rsi, rsi, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, flddetach
  flddone:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 24]
    x64.cmpRegImm32 rcx, 8
    x64.jcc notEqual, fi#14
  fih#29:
    x64.cmpRegReg rdx, rax
    x64.jcc greaterEqual, applied
  fib#30:
    x64.storeBaseIndexScaleReg.word64 [rsi + rdx*8 + 0], r9
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.jmp fih#29
  fi#14:
    x64.cmpRegImm32 rcx, 1
    x64.jcc equal, fih#32
  fi#16:
    x64.cmpRegImm32 rcx, -1
    x64.jcc notEqual, fi#18
  fih#35:
    x64.cmpRegReg rdx, rax
    x64.jcc greaterEqual, applied
  fib#36:
    x64.movRegReg rcx, rdx
    x64.shrRegImm8 rcx, rdx, 3
    x64.movRegReg rdi, rdx
    x64.andRegImm32 rdi, rdx, 7
    x64.movRegImm32 r8, 1
    x64.leaRegRegReg r10, rsi, rcx
    x64.loadRegBaseDisp.byte r11, [r10 + 0]
    x64.movRegReg rcx, rdi
    x64.shlRegCl r8, r8
    x64.xorRegImm32 r8, r8, -1
    x64.andRegReg r11, r11, r8
    x64.movRegReg r8, r9
    x64.andRegImm32 r8, r9, 1
    x64.movRegReg rcx, rdi
    x64.shlRegCl r8, r8
    x64.orRegReg r11, r11, r8
    x64.storeBaseDispReg.byte [r10 + 0], r11
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.jmp fih#35
  fi#18:
    x64.cmpRegImm32 rcx, 2
    x64.jcc equal, fih#38
  fi#20:
    x64.cmpRegImm32 rcx, 4
    x64.jcc equal, fih#41
  fi#22:
    x64.cmpRegImm32 rcx, -2
    x64.jcc notEqual, fi#24
  fih#44:
    x64.cmpRegReg rdx, rax
    x64.jcc greaterEqual, applied
  fib#45:
    x64.imulRegRegImm32 rcx, rdx, 2
    x64.movRegReg rdi, rcx
    x64.shrRegImm8 rdi, rcx, 3
    x64.movRegReg r8, rcx
    x64.andRegImm32 r8, rcx, 7
    x64.movRegImm32 r10, 3
    x64.leaRegRegReg rdi, rsi, rdi
    x64.loadRegBaseDisp.byte r11, [rdi + 0]
    x64.movRegReg rcx, r8
    x64.shlRegCl r10, r10
    x64.xorRegImm32 r10, r10, -1
    x64.andRegReg r11, r11, r10
    x64.movRegReg r10, r9
    x64.andRegImm32 r10, r9, 3
    x64.movRegReg rcx, r8
    x64.shlRegCl r10, r10
    x64.orRegReg r11, r11, r10
    x64.storeBaseDispReg.byte [rdi + 0], r11
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.jmp fih#44
  fi#24:
    x64.cmpRegImm32 rcx, -4
    x64.jcc notEqual, fi#26
  fih#47:
    x64.cmpRegReg rdx, rax
    x64.jcc greaterEqual, applied
  fib#48:
    x64.imulRegRegImm32 rcx, rdx, 4
    x64.movRegReg rdi, rcx
    x64.shrRegImm8 rdi, rcx, 3
    x64.movRegReg r8, rcx
    x64.andRegImm32 r8, rcx, 7
    x64.movRegImm32 r10, 15
    x64.leaRegRegReg rdi, rsi, rdi
    x64.loadRegBaseDisp.byte r11, [rdi + 0]
    x64.movRegReg rcx, r8
    x64.shlRegCl r10, r10
    x64.xorRegImm32 r10, r10, -1
    x64.andRegReg r11, r11, r10
    x64.movRegReg r10, r9
    x64.andRegImm32 r10, r9, 15
    x64.movRegReg rcx, r8
    x64.shlRegCl r10, r10
    x64.orRegReg r11, r11, r10
    x64.storeBaseDispReg.byte [rdi + 0], r11
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.jmp fih#47
  fi#26:
    x64.movRegImm rdi, 4294967297
    x64.cmpRegReg rcx, rdi
    x64.jcc notEqual, fi#27
  fih#32:
    x64.cmpRegReg rdx, rax
    x64.jcc greaterEqual, applied
  fib#33:
    x64.leaRegRegReg rcx, rsi, rdx
    x64.storeBaseDispReg.byte [rcx + 0], r9
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.jmp fih#32
  fi#27:
    x64.movRegImm rdi, 4294967298
    x64.cmpRegReg rcx, rdi
    x64.jcc notEqual, fi#28
  fih#38:
    x64.cmpRegReg rdx, rax
    x64.jcc greaterEqual, applied
  fib#39:
    x64.storeBaseIndexScaleReg.word16 [rsi + rdx*2 + 0], r9
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.jmp fih#38
  fi#28:
    x64.movRegImm rdi, 4294967300
    x64.cmpRegReg rcx, rdi
    x64.jcc notEqual, fi#12
  fih#41:
    x64.cmpRegReg rdx, rax
    x64.jcc less, fib#42
  applied:
    x64.movRegImm32 r8, 1
    x64.movRegImm32 r10, 0
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  fib#42:
    x64.storeBaseIndexScaleReg.word32 [rsi + rdx*4 + 0], r9
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.jmp fih#41
  flddetach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, r9
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot r9, slot3
    x64.jmp flddone
  fi#12:
    x64.leaRegRdata rcx, [rip + __fatal_msg_14]  ; "fatal error: an array record carries an element size no element access serves\x0a"
    x64.movRegImm32 rdx, 78
    x64.movRegImm32 rax, 70
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 64
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
    arm64.prologue 128
    arm64.storeSlotReg slot12, x28
    arm64.storeSlotReg slot11, x27
    arm64.storeSlotReg slot10, x26
    arm64.storeSlotReg slot9, x25
    arm64.storeSlotReg slot8, x24
    arm64.storeSlotReg slot7, x23
    arm64.storeSlotReg slot6, x22
    arm64.storeSlotReg slot5, x21
    arm64.storeSlotReg slot4, x20
    arm64.storeSlotReg slot3, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.movImm x0, 3
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 8
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movImm x20, 6
    arm64.movImm x21, 7
  __il_body#12:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __managed_resize
    arm64.movImm x22, 0
  __il_body#14:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x20
    arm64.movRegReg x3, x21
    arm64.bl __managed_fill
    arm64.cmp x9, 0
    arm64.b.eq tryok#15
  tryerr#16:
    arm64.leaRdata x0, __str_blob_5  ; "panic at Array.maxon:441: Array.fillPublished: fill OOB \xe2\x80\x94 the window is inside the length the caller just published\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.loadRegSlot x24, slot8
    arm64.loadRegSlot x25, slot9
    arm64.loadRegSlot x26, slot10
    arm64.loadRegSlot x27, slot11
    arm64.loadRegSlot x28, slot12
    arm64.epilogue 128
    arm64.ret
  tryok#15:
    arm64.eor x0, x0, 1
    arm64.cbnz x0, forhdr
    arm64.b __il_cont
  fill:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 40]
    arm64.cmp x0, 0
    arm64.b.ne __im_slow#29
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#29
  __im_owned:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 16]
    arm64.cmp x1, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x1, x0, 16
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.cmp x1, 0
    arm64.b.ne __im_detach
  __im_detached:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 16]
    arm64.cmp x22, x1
    arm64.b.hs __im_slow#29
  __im_store:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 24]
    arm64.cmp x1, 8
    arm64.b.ne __im_stride#37
  __im_word:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x22*8 + 0], x21
    arm64.b forstep
  __im_stride#37:
    arm64.cmp x1, 1
    arm64.b.eq __im_byte
  __im_stride#39:
    arm64.cmp x1, -1
    arm64.b.ne __im_stride#41
  __im_bit:
    arm64.lsr x1, x22, 3
    arm64.and x2, x22, 7
    arm64.movImm x3, 1
    arm64.add x0, x0, x1
    arm64.loadRegBaseDisp.byte x1, [x0 + 0]
    arm64.lslv x3, x3, x2
    arm64.movImm x16, 18446744073709551615
    arm64.eor x3, x3, x16
    arm64.and x1, x1, x3
    arm64.and x3, x21, 1
    arm64.lslv x2, x3, x2
    arm64.orr x1, x1, x2
    arm64.storeBaseDispReg.byte [x0 + 0], x1
    arm64.b forstep
  __im_stride#41:
    arm64.cmp x1, 2
    arm64.b.eq __im_bytes2
  __im_stride#43:
    arm64.cmp x1, 4
    arm64.b.eq __im_bytes4
  __im_stride#45:
    arm64.cmp x1, -2
    arm64.b.ne __im_stride#47
  __im_crumb:
    arm64.lsl x1, x22, 1
    arm64.lsr x2, x1, 3
    arm64.and x1, x1, 7
    arm64.movImm x3, 3
    arm64.add x0, x0, x2
    arm64.loadRegBaseDisp.byte x2, [x0 + 0]
    arm64.lslv x3, x3, x1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x3, x3, x16
    arm64.and x2, x2, x3
    arm64.and x3, x21, 3
    arm64.lslv x1, x3, x1
    arm64.orr x1, x2, x1
    arm64.storeBaseDispReg.byte [x0 + 0], x1
    arm64.b forstep
  __im_stride#47:
    arm64.cmp x1, -4
    arm64.b.ne __im_stride#49
  __im_nibble:
    arm64.lsl x1, x22, 2
    arm64.lsr x2, x1, 3
    arm64.and x1, x1, 7
    arm64.movImm x3, 15
    arm64.add x0, x0, x2
    arm64.loadRegBaseDisp.byte x2, [x0 + 0]
    arm64.lslv x3, x3, x1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x3, x3, x16
    arm64.and x2, x2, x3
    arm64.and x3, x21, 15
    arm64.lslv x1, x3, x1
    arm64.orr x1, x2, x1
    arm64.storeBaseDispReg.byte [x0 + 0], x1
    arm64.b forstep
  __im_stride#49:
    arm64.movImm x2, 4294967297
    arm64.cmp x1, x2
    arm64.b.ne __im_stride#50
  __im_byte:
    arm64.add x0, x0, x22
    arm64.storeBaseDispReg.byte [x0 + 0], x21
    arm64.b forstep
  __im_stride#50:
    arm64.movImm x2, 4294967298
    arm64.cmp x1, x2
    arm64.b.ne __im_stride#51
  __im_bytes2:
    arm64.storeBaseIndexScaleReg.word16 [x0 + x22*2 + 0], x21
    arm64.b forstep
  __im_stride#51:
    arm64.movImm x2, 4294967300
    arm64.cmp x1, x2
    arm64.b.ne __im_slow#29
  __im_bytes4:
    arm64.storeBaseIndexScaleReg.word32 [x0 + x22*4 + 0], x21
    arm64.b forstep
  __im_slow#29:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x21
    arm64.bl __managed_mem_set
    arm64.cmp x9, 0
    arm64.b.eq forstep
  tryerr#23:
    arm64.leaRdata x0, __str_rec_6  ; "panic at Array.maxon:447: "
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x0 + 8]
    arm64.leaRdata x0, __str_rec_7  ; "Array.fillPublished: set OOB at "
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x24
    arm64.bl __uint_to_string
    arm64.movRegReg x22, x0
    arm64.leaRdata x0, __str_rec_8  ; " \xe2\x80\x94 the window is inside the published length"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.leaRdata x0, __str_rec_9  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot2, x0
    arm64.add x1, x20, x23
    arm64.add x1, x1, x22
    arm64.add x1, x1, x26
    arm64.add x0, x1, x0
    arm64.storeSlotReg slot0, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot1, x0
    arm64.add x28, x0, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x28, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x19, x19, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x19, x19, x22
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x0, x19, x26
    arm64.loadRegSlot x2, slot2
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x19, slot1
    arm64.storeBaseDispReg.word64 [x19 + 0], x28
    arm64.loadRegSlot x0, slot0
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movRegReg x0, x24
    arm64.bl __mm_decref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.loadRegSlot x24, slot8
    arm64.loadRegSlot x25, slot9
    arm64.loadRegSlot x26, slot10
    arm64.loadRegSlot x27, slot11
    arm64.loadRegSlot x28, slot12
    arm64.epilogue 128
    arm64.ret
  forstep:
    arm64.add x22, x22, 1
  forhdr:
    arm64.cmp x22, 6
    arm64.b.lo fill
  __il_cont:
    arm64.movImm x1, 5
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 5
    arm64.b.ls __im_slow#9
    arm64.b __im_load
  tryerr#2:
    arm64.movImm x20, 9
    arm64.b trycont
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
  tryok#1:
    arm64.movRegReg x20, x0
  trycont:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.loadRegSlot x24, slot8
    arm64.loadRegSlot x25, slot9
    arm64.loadRegSlot x26, slot10
    arm64.loadRegSlot x27, slot11
    arm64.loadRegSlot x28, slot12
    arm64.epilogue 128
    arm64.ret
  __im_detach:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.b __im_detached
  __im_slow#9:
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#2
    arm64.b tryok#1
  __rc_panic:
    arm64.leaRdata x0, __str_blob_11  ; "panic at the-emitted-runtime-is-folded-too.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.loadRegSlot x24, slot8
    arm64.loadRegSlot x25, slot9
    arm64.loadRegSlot x26, slot10
    arm64.loadRegSlot x27, slot11
    arm64.loadRegSlot x28, slot12
    arm64.epilogue 128
    arm64.ret
}

func @__managed_get {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x1, x2
    arm64.b.lo load
  oob:
    arm64.movImm x0, 0
    arm64.movImm x9, 1
    arm64.epilogue 16
    arm64.ret
  load:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 24]
    arm64.cmp x3, 8
    arm64.b.ne ld#7
  ldword:
    arm64.loadRegBaseIndexScale.word64 x1, [x2 + x1*8 + 0]
    arm64.b gchkdtor
  ld#7:
    arm64.cmp x3, 1
    arm64.b.ne ld#9
  ldbyte:
    arm64.add x1, x2, x1
    arm64.loadRegBaseDisp.byte x1, [x1 + 0]
    arm64.b gchkdtor
  ld#9:
    arm64.cmp x3, -1
    arm64.b.ne ld#11
  ldbit:
    arm64.lsr x3, x1, 3
    arm64.and x1, x1, 7
    arm64.add x2, x2, x3
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
    arm64.lsrv x1, x2, x1
    arm64.and x1, x1, 1
    arm64.b gchkdtor
  ld#11:
    arm64.cmp x3, 2
    arm64.b.ne ld#13
  ldbytes2:
    arm64.loadRegBaseIndexScale.word16 x1, [x2 + x1*2 + 0]
    arm64.b gchkdtor
  ld#13:
    arm64.cmp x3, 4
    arm64.b.ne ld#15
  ldbytes4:
    arm64.loadRegBaseIndexScale.word32 x1, [x2 + x1*4 + 0]
    arm64.b gchkdtor
  ld#15:
    arm64.cmp x3, -2
    arm64.b.ne ld#17
  ldcrumb:
    arm64.lsl x1, x1, 1
    arm64.lsr x3, x1, 3
    arm64.and x1, x1, 7
    arm64.add x2, x2, x3
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
    arm64.lsrv x1, x2, x1
    arm64.and x1, x1, 3
    arm64.b gchkdtor
  ld#17:
    arm64.cmp x3, -4
    arm64.b.ne ld#19
  ldnibble:
    arm64.lsl x1, x1, 2
    arm64.lsr x3, x1, 3
    arm64.and x1, x1, 7
    arm64.add x2, x2, x3
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
    arm64.lsrv x1, x2, x1
    arm64.and x1, x1, 15
    arm64.b gchkdtor
  ld#19:
    arm64.movImm x4, 4294967297
    arm64.cmp x3, x4
    arm64.b.ne ld#21
  ldsbyte:
    arm64.add x1, x2, x1
    arm64.loadRegBaseDisp.byte x1, [x1 + 0]
    arm64.lsl x1, x1, 56
    arm64.asr x1, x1, 56
    arm64.b gchkdtor
  ld#21:
    arm64.movImm x4, 4294967298
    arm64.cmp x3, x4
    arm64.b.ne ld#23
  ldsbytes2:
    arm64.loadRegBaseIndexScale.word16 x1, [x2 + x1*2 + 0]
    arm64.lsl x1, x1, 48
    arm64.asr x1, x1, 48
    arm64.b gchkdtor
  ld#23:
    arm64.movImm x4, 4294967300
    arm64.cmp x3, x4
    arm64.b.ne ld#5
  ldsbytes4:
    arm64.loadRegBaseIndexScale.word32 x1, [x2 + x1*4 + 0]
    arm64.lsl x1, x1, 32
    arm64.asr x1, x1, 32
  gchkdtor:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 40]
    arm64.cmp x0, 0
    arm64.b.eq gok
  gchknull:
    arm64.cmp x1, 0
    arm64.b.ne gok
  gempty:
    arm64.movImm x0, 0
    arm64.movImm x9, 2
    arm64.epilogue 16
    arm64.ret
  gok:
    arm64.movImm x9, 0
    arm64.movRegReg x0, x1
    arm64.epilogue 16
    arm64.ret
  ld#5:
    arm64.leaRdata x0, __fatal_msg_14  ; "fatal error: an array record carries an element size no element access serves\x0a"
    arm64.movImm x1, 78
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.epilogue 16
    arm64.ret
}

func @__managed_fill {
  entry:
    arm64.prologue 48
    arm64.cmp x1, 0
    arm64.b.lt badrange
  chkcount:
    arm64.cmp x2, 0
    arm64.b.lt badrange
  chkhigh:
    arm64.add x2, x1, x2
    arm64.loadRegBaseDisp.word64 x4, [x0 + 8]
    arm64.cmp x2, x4
    arm64.b.ls classify
  badrange:
    arm64.movImm x0, 0
    arm64.movImm x9, 1
    arm64.epilogue 48
    arm64.ret
  classify:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 40]
    arm64.cmp x4, 0
    arm64.b.eq prepare
  declined:
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.epilogue 48
    arm64.ret
  prepare:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt flddetach
  fldbuf:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.cmp x4, 0
    arm64.b.eq flddone
  fldshared:
    arm64.sub x4, x4, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne flddetach
  flddone:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 24]
    arm64.cmp x0, 8
    arm64.b.ne fi#14
  fih#29:
    arm64.cmp x1, x2
    arm64.b.ge applied
  fib#30:
    arm64.storeBaseIndexScaleReg.word64 [x4 + x1*8 + 0], x3
    arm64.add x1, x1, 1
    arm64.b fih#29
  fi#14:
    arm64.cmp x0, 1
    arm64.b.eq fih#32
  fi#16:
    arm64.cmp x0, -1
    arm64.b.ne fi#18
  fih#35:
    arm64.cmp x1, x2
    arm64.b.ge applied
  fib#36:
    arm64.lsr x0, x1, 3
    arm64.and x5, x1, 7
    arm64.movImm x6, 1
    arm64.add x0, x4, x0
    arm64.loadRegBaseDisp.byte x7, [x0 + 0]
    arm64.lslv x6, x6, x5
    arm64.movImm x16, 18446744073709551615
    arm64.eor x6, x6, x16
    arm64.and x6, x7, x6
    arm64.and x7, x3, 1
    arm64.lslv x5, x7, x5
    arm64.orr x5, x6, x5
    arm64.storeBaseDispReg.byte [x0 + 0], x5
    arm64.add x1, x1, 1
    arm64.b fih#35
  fi#18:
    arm64.cmp x0, 2
    arm64.b.eq fih#38
  fi#20:
    arm64.cmp x0, 4
    arm64.b.eq fih#41
  fi#22:
    arm64.cmp x0, -2
    arm64.b.ne fi#24
  fih#44:
    arm64.cmp x1, x2
    arm64.b.ge applied
  fib#45:
    arm64.lsl x0, x1, 1
    arm64.lsr x5, x0, 3
    arm64.and x0, x0, 7
    arm64.movImm x6, 3
    arm64.add x5, x4, x5
    arm64.loadRegBaseDisp.byte x7, [x5 + 0]
    arm64.lslv x6, x6, x0
    arm64.movImm x16, 18446744073709551615
    arm64.eor x6, x6, x16
    arm64.and x6, x7, x6
    arm64.and x7, x3, 3
    arm64.lslv x0, x7, x0
    arm64.orr x0, x6, x0
    arm64.storeBaseDispReg.byte [x5 + 0], x0
    arm64.add x1, x1, 1
    arm64.b fih#44
  fi#24:
    arm64.cmp x0, -4
    arm64.b.ne fi#26
  fih#47:
    arm64.cmp x1, x2
    arm64.b.ge applied
  fib#48:
    arm64.lsl x0, x1, 2
    arm64.lsr x5, x0, 3
    arm64.and x0, x0, 7
    arm64.movImm x6, 15
    arm64.add x5, x4, x5
    arm64.loadRegBaseDisp.byte x7, [x5 + 0]
    arm64.lslv x6, x6, x0
    arm64.movImm x16, 18446744073709551615
    arm64.eor x6, x6, x16
    arm64.and x6, x7, x6
    arm64.and x7, x3, 15
    arm64.lslv x0, x7, x0
    arm64.orr x0, x6, x0
    arm64.storeBaseDispReg.byte [x5 + 0], x0
    arm64.add x1, x1, 1
    arm64.b fih#47
  fi#26:
    arm64.movImm x5, 4294967297
    arm64.cmp x0, x5
    arm64.b.ne fi#27
  fih#32:
    arm64.cmp x1, x2
    arm64.b.ge applied
  fib#33:
    arm64.add x0, x4, x1
    arm64.storeBaseDispReg.byte [x0 + 0], x3
    arm64.add x1, x1, 1
    arm64.b fih#32
  fi#27:
    arm64.movImm x5, 4294967298
    arm64.cmp x0, x5
    arm64.b.ne fi#28
  fih#38:
    arm64.cmp x1, x2
    arm64.b.ge applied
  fib#39:
    arm64.storeBaseIndexScaleReg.word16 [x4 + x1*2 + 0], x3
    arm64.add x1, x1, 1
    arm64.b fih#38
  fi#28:
    arm64.movImm x5, 4294967300
    arm64.cmp x0, x5
    arm64.b.ne fi#12
  fih#41:
    arm64.cmp x1, x2
    arm64.b.lt fib#42
  applied:
    arm64.movImm x0, 1
    arm64.movImm x9, 0
    arm64.epilogue 48
    arm64.ret
  fib#42:
    arm64.storeBaseIndexScaleReg.word32 [x4 + x1*4 + 0], x3
    arm64.add x1, x1, 1
    arm64.b fih#41
  flddetach:
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
    arm64.b flddone
  fi#12:
    arm64.leaRdata x0, __fatal_msg_14  ; "fatal error: an array record carries an element size no element access serves\x0a"
    arm64.movImm x1, 78
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.movImm x9, 0
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

func @main {
  entry:
    arm64.prologue 128
    arm64.storeSlotReg slot12, x28
    arm64.storeSlotReg slot11, x27
    arm64.storeSlotReg slot10, x26
    arm64.storeSlotReg slot9, x25
    arm64.storeSlotReg slot8, x24
    arm64.storeSlotReg slot7, x23
    arm64.storeSlotReg slot6, x22
    arm64.storeSlotReg slot5, x21
    arm64.storeSlotReg slot4, x20
    arm64.storeSlotReg slot3, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.movImm x0, 3
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 8
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movImm x20, 6
    arm64.movImm x21, 7
  __il_body#12:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __managed_resize
    arm64.movImm x22, 0
  __il_body#14:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x20
    arm64.movRegReg x3, x21
    arm64.bl __managed_fill
    arm64.cmp x9, 0
    arm64.b.eq tryok#15
  tryerr#16:
    arm64.leaRdata x0, __str_blob_5  ; "panic at Array.maxon:441: Array.fillPublished: fill OOB \xe2\x80\x94 the window is inside the length the caller just published\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.loadRegSlot x24, slot8
    arm64.loadRegSlot x25, slot9
    arm64.loadRegSlot x26, slot10
    arm64.loadRegSlot x27, slot11
    arm64.loadRegSlot x28, slot12
    arm64.epilogue 128
    arm64.ret
  tryok#15:
    arm64.eor x0, x0, 1
    arm64.cbnz x0, forhdr
    arm64.b __il_cont
  fill:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 40]
    arm64.cmp x0, 0
    arm64.b.ne __im_slow#29
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#29
  __im_owned:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 16]
    arm64.cmp x1, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x1, x0, 16
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.cmp x1, 0
    arm64.b.ne __im_detach
  __im_detached:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 16]
    arm64.cmp x22, x1
    arm64.b.hs __im_slow#29
  __im_store:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 24]
    arm64.cmp x1, 8
    arm64.b.ne __im_stride#37
  __im_word:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x22*8 + 0], x21
    arm64.b forstep
  __im_stride#37:
    arm64.cmp x1, 1
    arm64.b.eq __im_byte
  __im_stride#39:
    arm64.cmp x1, -1
    arm64.b.ne __im_stride#41
  __im_bit:
    arm64.lsr x1, x22, 3
    arm64.and x2, x22, 7
    arm64.movImm x3, 1
    arm64.add x0, x0, x1
    arm64.loadRegBaseDisp.byte x1, [x0 + 0]
    arm64.lslv x3, x3, x2
    arm64.movImm x16, 18446744073709551615
    arm64.eor x3, x3, x16
    arm64.and x1, x1, x3
    arm64.and x3, x21, 1
    arm64.lslv x2, x3, x2
    arm64.orr x1, x1, x2
    arm64.storeBaseDispReg.byte [x0 + 0], x1
    arm64.b forstep
  __im_stride#41:
    arm64.cmp x1, 2
    arm64.b.eq __im_bytes2
  __im_stride#43:
    arm64.cmp x1, 4
    arm64.b.eq __im_bytes4
  __im_stride#45:
    arm64.cmp x1, -2
    arm64.b.ne __im_stride#47
  __im_crumb:
    arm64.lsl x1, x22, 1
    arm64.lsr x2, x1, 3
    arm64.and x1, x1, 7
    arm64.movImm x3, 3
    arm64.add x0, x0, x2
    arm64.loadRegBaseDisp.byte x2, [x0 + 0]
    arm64.lslv x3, x3, x1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x3, x3, x16
    arm64.and x2, x2, x3
    arm64.and x3, x21, 3
    arm64.lslv x1, x3, x1
    arm64.orr x1, x2, x1
    arm64.storeBaseDispReg.byte [x0 + 0], x1
    arm64.b forstep
  __im_stride#47:
    arm64.cmp x1, -4
    arm64.b.ne __im_stride#49
  __im_nibble:
    arm64.lsl x1, x22, 2
    arm64.lsr x2, x1, 3
    arm64.and x1, x1, 7
    arm64.movImm x3, 15
    arm64.add x0, x0, x2
    arm64.loadRegBaseDisp.byte x2, [x0 + 0]
    arm64.lslv x3, x3, x1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x3, x3, x16
    arm64.and x2, x2, x3
    arm64.and x3, x21, 15
    arm64.lslv x1, x3, x1
    arm64.orr x1, x2, x1
    arm64.storeBaseDispReg.byte [x0 + 0], x1
    arm64.b forstep
  __im_stride#49:
    arm64.movImm x2, 4294967297
    arm64.cmp x1, x2
    arm64.b.ne __im_stride#50
  __im_byte:
    arm64.add x0, x0, x22
    arm64.storeBaseDispReg.byte [x0 + 0], x21
    arm64.b forstep
  __im_stride#50:
    arm64.movImm x2, 4294967298
    arm64.cmp x1, x2
    arm64.b.ne __im_stride#51
  __im_bytes2:
    arm64.storeBaseIndexScaleReg.word16 [x0 + x22*2 + 0], x21
    arm64.b forstep
  __im_stride#51:
    arm64.movImm x2, 4294967300
    arm64.cmp x1, x2
    arm64.b.ne __im_slow#29
  __im_bytes4:
    arm64.storeBaseIndexScaleReg.word32 [x0 + x22*4 + 0], x21
    arm64.b forstep
  __im_slow#29:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x21
    arm64.bl __managed_mem_set
    arm64.cmp x9, 0
    arm64.b.eq forstep
  tryerr#23:
    arm64.leaRdata x0, __str_rec_6  ; "panic at Array.maxon:447: "
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x0 + 8]
    arm64.leaRdata x0, __str_rec_7  ; "Array.fillPublished: set OOB at "
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x24
    arm64.bl __uint_to_string
    arm64.movRegReg x22, x0
    arm64.leaRdata x0, __str_rec_8  ; " \xe2\x80\x94 the window is inside the published length"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.leaRdata x0, __str_rec_9  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot2, x0
    arm64.add x1, x20, x23
    arm64.add x1, x1, x22
    arm64.add x1, x1, x26
    arm64.add x0, x1, x0
    arm64.storeSlotReg slot0, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot1, x0
    arm64.add x28, x0, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x28, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x19, x19, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x19, x19, x22
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x0, x19, x26
    arm64.loadRegSlot x2, slot2
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x19, slot1
    arm64.storeBaseDispReg.word64 [x19 + 0], x28
    arm64.loadRegSlot x0, slot0
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movRegReg x0, x24
    arm64.bl __mm_decref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.loadRegSlot x24, slot8
    arm64.loadRegSlot x25, slot9
    arm64.loadRegSlot x26, slot10
    arm64.loadRegSlot x27, slot11
    arm64.loadRegSlot x28, slot12
    arm64.epilogue 128
    arm64.ret
  forstep:
    arm64.add x22, x22, 1
  forhdr:
    arm64.cmp x22, 6
    arm64.b.lo fill
  __il_cont:
    arm64.movImm x1, 5
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 5
    arm64.b.ls __im_slow#9
    arm64.b __im_load
  tryerr#2:
    arm64.movImm x20, 9
    arm64.b trycont
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
  tryok#1:
    arm64.movRegReg x20, x0
  trycont:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.loadRegSlot x24, slot8
    arm64.loadRegSlot x25, slot9
    arm64.loadRegSlot x26, slot10
    arm64.loadRegSlot x27, slot11
    arm64.loadRegSlot x28, slot12
    arm64.epilogue 128
    arm64.ret
  __im_detach:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.b __im_detached
  __im_slow#9:
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#2
    arm64.b tryok#1
  __rc_panic:
    arm64.leaRdata x0, __str_blob_11  ; "panic at the-emitted-runtime-is-folded-too.test:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot3
    arm64.loadRegSlot x20, slot4
    arm64.loadRegSlot x21, slot5
    arm64.loadRegSlot x22, slot6
    arm64.loadRegSlot x23, slot7
    arm64.loadRegSlot x24, slot8
    arm64.loadRegSlot x25, slot9
    arm64.loadRegSlot x26, slot10
    arm64.loadRegSlot x27, slot11
    arm64.loadRegSlot x28, slot12
    arm64.epilogue 128
    arm64.ret
}

func @__managed_get {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x1, x2
    arm64.b.lo load
  oob:
    arm64.movImm x0, 0
    arm64.movImm x9, 1
    arm64.epilogue 16
    arm64.ret
  load:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 24]
    arm64.cmp x3, 8
    arm64.b.ne ld#7
  ldword:
    arm64.loadRegBaseIndexScale.word64 x1, [x2 + x1*8 + 0]
    arm64.b gchkdtor
  ld#7:
    arm64.cmp x3, 1
    arm64.b.ne ld#9
  ldbyte:
    arm64.add x1, x2, x1
    arm64.loadRegBaseDisp.byte x1, [x1 + 0]
    arm64.b gchkdtor
  ld#9:
    arm64.cmp x3, -1
    arm64.b.ne ld#11
  ldbit:
    arm64.lsr x3, x1, 3
    arm64.and x1, x1, 7
    arm64.add x2, x2, x3
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
    arm64.lsrv x1, x2, x1
    arm64.and x1, x1, 1
    arm64.b gchkdtor
  ld#11:
    arm64.cmp x3, 2
    arm64.b.ne ld#13
  ldbytes2:
    arm64.loadRegBaseIndexScale.word16 x1, [x2 + x1*2 + 0]
    arm64.b gchkdtor
  ld#13:
    arm64.cmp x3, 4
    arm64.b.ne ld#15
  ldbytes4:
    arm64.loadRegBaseIndexScale.word32 x1, [x2 + x1*4 + 0]
    arm64.b gchkdtor
  ld#15:
    arm64.cmp x3, -2
    arm64.b.ne ld#17
  ldcrumb:
    arm64.lsl x1, x1, 1
    arm64.lsr x3, x1, 3
    arm64.and x1, x1, 7
    arm64.add x2, x2, x3
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
    arm64.lsrv x1, x2, x1
    arm64.and x1, x1, 3
    arm64.b gchkdtor
  ld#17:
    arm64.cmp x3, -4
    arm64.b.ne ld#19
  ldnibble:
    arm64.lsl x1, x1, 2
    arm64.lsr x3, x1, 3
    arm64.and x1, x1, 7
    arm64.add x2, x2, x3
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
    arm64.lsrv x1, x2, x1
    arm64.and x1, x1, 15
    arm64.b gchkdtor
  ld#19:
    arm64.movImm x4, 4294967297
    arm64.cmp x3, x4
    arm64.b.ne ld#21
  ldsbyte:
    arm64.add x1, x2, x1
    arm64.loadRegBaseDisp.byte x1, [x1 + 0]
    arm64.lsl x1, x1, 56
    arm64.asr x1, x1, 56
    arm64.b gchkdtor
  ld#21:
    arm64.movImm x4, 4294967298
    arm64.cmp x3, x4
    arm64.b.ne ld#23
  ldsbytes2:
    arm64.loadRegBaseIndexScale.word16 x1, [x2 + x1*2 + 0]
    arm64.lsl x1, x1, 48
    arm64.asr x1, x1, 48
    arm64.b gchkdtor
  ld#23:
    arm64.movImm x4, 4294967300
    arm64.cmp x3, x4
    arm64.b.ne ld#5
  ldsbytes4:
    arm64.loadRegBaseIndexScale.word32 x1, [x2 + x1*4 + 0]
    arm64.lsl x1, x1, 32
    arm64.asr x1, x1, 32
  gchkdtor:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 40]
    arm64.cmp x0, 0
    arm64.b.eq gok
  gchknull:
    arm64.cmp x1, 0
    arm64.b.ne gok
  gempty:
    arm64.movImm x0, 0
    arm64.movImm x9, 2
    arm64.epilogue 16
    arm64.ret
  gok:
    arm64.movImm x9, 0
    arm64.movRegReg x0, x1
    arm64.epilogue 16
    arm64.ret
  ld#5:
    arm64.leaRdata x0, __fatal_msg_14  ; "fatal error: an array record carries an element size no element access serves\x0a"
    arm64.movImm x1, 78
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.epilogue 16
    arm64.ret
}

func @__managed_fill {
  entry:
    arm64.prologue 48
    arm64.cmp x1, 0
    arm64.b.lt badrange
  chkcount:
    arm64.cmp x2, 0
    arm64.b.lt badrange
  chkhigh:
    arm64.add x2, x1, x2
    arm64.loadRegBaseDisp.word64 x4, [x0 + 8]
    arm64.cmp x2, x4
    arm64.b.ls classify
  badrange:
    arm64.movImm x0, 0
    arm64.movImm x9, 1
    arm64.epilogue 48
    arm64.ret
  classify:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 40]
    arm64.cmp x4, 0
    arm64.b.eq prepare
  declined:
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.epilogue 48
    arm64.ret
  prepare:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt flddetach
  fldbuf:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.cmp x4, 0
    arm64.b.eq flddone
  fldshared:
    arm64.sub x4, x4, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne flddetach
  flddone:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 24]
    arm64.cmp x0, 8
    arm64.b.ne fi#14
  fih#29:
    arm64.cmp x1, x2
    arm64.b.ge applied
  fib#30:
    arm64.storeBaseIndexScaleReg.word64 [x4 + x1*8 + 0], x3
    arm64.add x1, x1, 1
    arm64.b fih#29
  fi#14:
    arm64.cmp x0, 1
    arm64.b.eq fih#32
  fi#16:
    arm64.cmp x0, -1
    arm64.b.ne fi#18
  fih#35:
    arm64.cmp x1, x2
    arm64.b.ge applied
  fib#36:
    arm64.lsr x0, x1, 3
    arm64.and x5, x1, 7
    arm64.movImm x6, 1
    arm64.add x0, x4, x0
    arm64.loadRegBaseDisp.byte x7, [x0 + 0]
    arm64.lslv x6, x6, x5
    arm64.movImm x16, 18446744073709551615
    arm64.eor x6, x6, x16
    arm64.and x6, x7, x6
    arm64.and x7, x3, 1
    arm64.lslv x5, x7, x5
    arm64.orr x5, x6, x5
    arm64.storeBaseDispReg.byte [x0 + 0], x5
    arm64.add x1, x1, 1
    arm64.b fih#35
  fi#18:
    arm64.cmp x0, 2
    arm64.b.eq fih#38
  fi#20:
    arm64.cmp x0, 4
    arm64.b.eq fih#41
  fi#22:
    arm64.cmp x0, -2
    arm64.b.ne fi#24
  fih#44:
    arm64.cmp x1, x2
    arm64.b.ge applied
  fib#45:
    arm64.lsl x0, x1, 1
    arm64.lsr x5, x0, 3
    arm64.and x0, x0, 7
    arm64.movImm x6, 3
    arm64.add x5, x4, x5
    arm64.loadRegBaseDisp.byte x7, [x5 + 0]
    arm64.lslv x6, x6, x0
    arm64.movImm x16, 18446744073709551615
    arm64.eor x6, x6, x16
    arm64.and x6, x7, x6
    arm64.and x7, x3, 3
    arm64.lslv x0, x7, x0
    arm64.orr x0, x6, x0
    arm64.storeBaseDispReg.byte [x5 + 0], x0
    arm64.add x1, x1, 1
    arm64.b fih#44
  fi#24:
    arm64.cmp x0, -4
    arm64.b.ne fi#26
  fih#47:
    arm64.cmp x1, x2
    arm64.b.ge applied
  fib#48:
    arm64.lsl x0, x1, 2
    arm64.lsr x5, x0, 3
    arm64.and x0, x0, 7
    arm64.movImm x6, 15
    arm64.add x5, x4, x5
    arm64.loadRegBaseDisp.byte x7, [x5 + 0]
    arm64.lslv x6, x6, x0
    arm64.movImm x16, 18446744073709551615
    arm64.eor x6, x6, x16
    arm64.and x6, x7, x6
    arm64.and x7, x3, 15
    arm64.lslv x0, x7, x0
    arm64.orr x0, x6, x0
    arm64.storeBaseDispReg.byte [x5 + 0], x0
    arm64.add x1, x1, 1
    arm64.b fih#47
  fi#26:
    arm64.movImm x5, 4294967297
    arm64.cmp x0, x5
    arm64.b.ne fi#27
  fih#32:
    arm64.cmp x1, x2
    arm64.b.ge applied
  fib#33:
    arm64.add x0, x4, x1
    arm64.storeBaseDispReg.byte [x0 + 0], x3
    arm64.add x1, x1, 1
    arm64.b fih#32
  fi#27:
    arm64.movImm x5, 4294967298
    arm64.cmp x0, x5
    arm64.b.ne fi#28
  fih#38:
    arm64.cmp x1, x2
    arm64.b.ge applied
  fib#39:
    arm64.storeBaseIndexScaleReg.word16 [x4 + x1*2 + 0], x3
    arm64.add x1, x1, 1
    arm64.b fih#38
  fi#28:
    arm64.movImm x5, 4294967300
    arm64.cmp x0, x5
    arm64.b.ne fi#12
  fih#41:
    arm64.cmp x1, x2
    arm64.b.lt fib#42
  applied:
    arm64.movImm x0, 1
    arm64.movImm x9, 0
    arm64.epilogue 48
    arm64.ret
  fib#42:
    arm64.storeBaseIndexScaleReg.word32 [x4 + x1*4 + 0], x3
    arm64.add x1, x1, 1
    arm64.b fih#41
  flddetach:
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
    arm64.b flddone
  fi#12:
    arm64.leaRdata x0, __fatal_msg_14  ; "fatal error: an array record carries an element size no element access serves\x0a"
    arm64.movImm x1, 78
    arm64.movImm x2, 70
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.epilogue 48
    arm64.ret
}
```
