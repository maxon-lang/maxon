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
	var a = [1, 2, 3]
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
    x64.leaRegRdata r14, [rip + __layout_Array_int]
  __il_body#11:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __managed_resize
    x64.movRegImm32 r15, 0
  __il_body#13:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.movRegReg rax, r12
    x64.movRegReg r9, r13
    x64.callDirect __managed_fill
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#14
  tryerr#15:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at Array.maxon:444: Array.fillPublished: fill OOB \xe2\x80\x94 the window is inside the length the caller just published\x0a"
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
  tryok#14:
    x64.xorRegImm32 r8, r8, 1
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, forhdr
    x64.jmp ifcont
  fill:
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r13
    x64.callDirect __retain_type_param
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rax, [rbx + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __im_slow#25
  __im_owned#29:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __im_slow#25
  __im_bounds#30:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegReg r15, rax
    x64.jcc aboveEqual, __im_slow#25
  __im_buffer#31:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_slow#25
  __im_viewed#32:
    x64.leaRegRegImm32 rcx, rax, -24
    x64.loadRegBaseDisp.word64 rcx, [rcx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, __im_slow#25
  __im_store#33:
    x64.storeBaseIndexScaleReg.word64 [rax + r15*8 + 0], r8
    x64.jmp forstep
  __im_stride:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#25
  __im_byte:
    x64.loadRegBaseDisp.word64 rax, [rbx + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __im_slow#25
  __im_owned#34:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __im_slow#25
  __im_bounds#35:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegReg r15, rax
    x64.jcc aboveEqual, __im_slow#25
  __im_buffer#36:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_slow#25
  __im_viewed#37:
    x64.leaRegRegImm32 rcx, rax, -24
    x64.loadRegBaseDisp.word64 rcx, [rcx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, __im_slow#25
  __im_store#38:
    x64.leaRegRegReg rax, rax, r15
    x64.storeBaseDispReg.byte [rax + 0], r8
    x64.jmp forstep
  __im_slow#25:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.movRegReg rax, r8
    x64.callDirect __managed_mem_set
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, forstep
  tryerr#22:
    x64.leaRegRdata rax, [rip + __str_rec_6]  ; "panic at Array.maxon:450: "
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.leaRegRdata rax, [rip + __str_rec_7]  ; "Array.fillPublished: set OOB at "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot7, rcx
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r14
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
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, r12
    x64.loadRegSlot rdx, slot7
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r13
    x64.loadRegSlot rax, slot6
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot6
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot4
    x64.loadRegSlot rdx, slot5
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot4
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot2
    x64.loadRegSlot rdx, slot3
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot2
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rbx, slot1
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot0
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, r14
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
    x64.leaRegRegImm32 r15, r15, 1
  forhdr:
    x64.cmpRegImm32 r15, 6
    x64.jcc below, fill
  ifcont:
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r13
    x64.callDirect __drop_type_param
  __il_cont:
    x64.movRegImm32 rdx, 5
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 5
    x64.jcc belowEqual, __im_slow#8
    x64.jmp __im_load
  tryerr#2:
    x64.movRegImm32 r12, 9
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rax + rdx*8 + 0]
    x64.movRegReg r8, rax
  tryok#1:
    x64.movRegReg r12, r8
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
  __im_slow#8:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#2
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
    x64.movRegReg rax, rcx
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.cmpRegReg rdx, rcx
    x64.jcc below, load
  oob:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 1
    x64.popReg rbp
    x64.ret
  load:
    x64.loadRegBaseDisp.word64 rsi, [rax + 0]
    x64.loadRegBaseDisp.word64 rcx, [rax + 24]
    x64.cmpRegImm32 rcx, 8
    x64.jcc notEqual, esbyte
  ldword:
    x64.imulRegReg rdx, rdx, rcx
    x64.leaRegRegReg rcx, rsi, rdx
    x64.loadRegBaseDisp.word64 r8, [rcx + 0]
    x64.jmp gchkdtor
  esbyte:
    x64.cmpRegImm32 rcx, 1
    x64.jcc notEqual, __il_body#17
  ldbyte:
    x64.imulRegReg rdx, rdx, rcx
    x64.leaRegRegReg rcx, rsi, rdx
    x64.loadRegBaseDisp.byte r8, [rcx + 0]
    x64.jmp gchkdtor
  __il_body#17:
    x64.movRegReg rdi, rcx
    x64.sarRegImm8 rdi, rcx, 63
    x64.movRegReg r8, rdi
    x64.xorRegImm32 r8, rdi, -1
    x64.movRegImm32 r9, 0
    x64.subRegReg r9, r9, rcx
    x64.shlRegImm8 rcx, rcx, 3
    x64.andRegReg r9, r9, rdi
    x64.andRegReg rcx, rcx, r8
    x64.orRegReg r9, r9, rcx
  __il_cont#16:
    x64.imulRegReg rdx, rdx, r9
    x64.movRegReg rdi, rdx
    x64.shrRegImm8 rdi, rdx, 3
    x64.andRegImm32 rdx, rdx, 7
    x64.movRegImm32 rcx, 64
    x64.subRegReg rcx, rcx, r9
    x64.movRegImm r8, 18446744073709551615
    x64.shrRegCl r8, r8
    x64.leaRegRegReg r9, rdx, r9
  __il_body#18:
    x64.leaRegRegImm32 r9, r9, 7
    x64.shrRegImm8 r9, r9, 3
  __il_cont#15:
    x64.leaRegRegReg rsi, rsi, rdi
    x64.movRegImm32 rdi, 0
    x64.movRegImm32 r10, 0
  ldeh:
    x64.cmpRegReg rdi, r9
    x64.jcc less, ldeb
  ldxtr:
    x64.movRegReg rcx, rdx
    x64.shrRegCl r10, r10
    x64.andRegReg r10, r10, r8
    x64.movRegReg r8, r10
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
    x64.popReg rbp
    x64.ret
  gok:
    x64.movRegImm32 r10, 0
    x64.popReg rbp
    x64.ret
  ldeb:
    x64.leaRegRegReg rcx, rsi, rdi
    x64.loadRegBaseDisp.byte r11, [rcx + 0]
    x64.imulRegRegImm32 rcx, rdi, 8
    x64.shlRegCl r11, r11
    x64.orRegReg r11, r11, r10
    x64.leaRegRegImm32 rdi, rdi, 1
    x64.movRegReg r10, r11
    x64.jmp ldeh
}

func @__managed_fill {
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
    x64.movRegReg r13, r9
    x64.cmpRegImm32 r12, 0
    x64.jcc less, badrange
  chkcount:
    x64.cmpRegImm32 rax, 0
    x64.jcc less, badrange
  chkhigh:
    x64.leaRegRegReg r14, r12, rax
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r14, rax
    x64.jcc belowEqual, classify
  badrange:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 1
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  classify:
    x64.loadRegBaseDisp.word64 rax, [rbx + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, prepare
  declined:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  prepare:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rbx + 24]
    x64.cmpRegImm32 rdx, 8
    x64.jcc notEqual, esbyte
  fillword:
    x64.movRegReg rcx, r12
  fiwh:
    x64.cmpRegReg rcx, r14
    x64.jcc greaterEqual, applied
  fiwb:
    x64.movRegReg rsi, rcx
    x64.imulRegReg rsi, rcx, rdx
    x64.leaRegRegReg rsi, rax, rsi
    x64.storeBaseDispReg.word64 [rsi + 0], r13
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.jmp fiwh
  esbyte:
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, fillgen
  fillbyte:
    x64.movRegReg rcx, r12
  fibh:
    x64.cmpRegReg rcx, r14
    x64.jcc greaterEqual, applied
  fibb:
    x64.movRegReg rsi, rcx
    x64.imulRegReg rsi, rcx, rdx
    x64.leaRegRegReg rsi, rax, rsi
    x64.storeBaseDispReg.byte [rsi + 0], r13
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.jmp fibh
  fillgen:
    x64.movRegReg rsi, r12
  figh:
    x64.cmpRegReg rsi, r14
    x64.jcc less, figb
  applied:
    x64.movRegImm32 r8, 1
    x64.movRegImm32 r10, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  figb:
    x64.cmpRegImm32 rdx, 0
    x64.jcc greaterEqual, stby
  __il_body#29:
    x64.movRegReg rcx, rdx
    x64.sarRegImm8 rcx, rdx, 63
    x64.movRegReg rdi, rcx
    x64.xorRegImm32 rdi, rcx, -1
    x64.movRegImm32 r8, 0
    x64.subRegReg r8, r8, rdx
    x64.movRegReg r9, rdx
    x64.shlRegImm8 r9, rdx, 3
    x64.andRegReg r8, r8, rcx
    x64.andRegReg r9, r9, rdi
    x64.orRegReg r8, r8, r9
  __il_cont#28:
    x64.movRegReg rcx, rsi
    x64.imulRegReg rcx, rsi, r8
    x64.movRegReg rdi, rcx
    x64.shrRegImm8 rdi, rcx, 3
    x64.movRegReg r9, rcx
    x64.andRegImm32 r9, rcx, 7
    x64.movRegImm32 rcx, 64
    x64.subRegReg rcx, rcx, r8
    x64.movRegImm r10, 18446744073709551615
    x64.shrRegCl r10, r10
    x64.leaRegRegReg r8, r9, r8
  __il_body#30:
    x64.leaRegRegImm32 rcx, r8, 7
    x64.shrRegImm8 rcx, rcx, 3
  __il_cont#27:
    x64.leaRegRegReg rdi, rax, rdi
    x64.loadRegBaseDisp.byte r8, [rdi + 0]
    x64.movRegReg rcx, r9
    x64.movRegReg r11, r10
    x64.shlRegCl r11, r10
    x64.xorRegImm32 r11, r11, -1
    x64.andRegReg r8, r8, r11
    x64.movRegReg r11, r13
    x64.andRegReg r11, r13, r10
    x64.movRegReg rcx, r9
    x64.shlRegCl r11, r11
    x64.orRegReg r8, r8, r11
    x64.storeBaseDispReg.byte [rdi + 0], r8
    x64.jmp figstep
  stby:
    x64.movRegReg rcx, rsi
    x64.imulRegReg rcx, rsi, rdx
    x64.leaRegRegReg rdi, rax, rcx
    x64.movRegImm32 r8, 0
  steh:
    x64.cmpRegReg r8, rdx
    x64.jcc less, steb
  figstep:
    x64.leaRegRegImm32 rsi, rsi, 1
    x64.jmp figh
  steb:
    x64.imulRegRegImm32 rcx, r8, 8
    x64.movRegReg r9, r13
    x64.shrRegCl r9, r13
    x64.leaRegRegReg r10, rdi, r8
    x64.storeBaseDispReg.byte [r10 + 0], r9
    x64.leaRegRegImm32 r8, r8, 1
    x64.jmp steh
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
    x64.leaRegRdata r14, [rip + __layout_Array_int]
  __il_body#11:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __managed_resize
    x64.movRegImm32 r15, 0
  __il_body#13:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.movRegReg rax, r12
    x64.movRegReg r9, r13
    x64.callDirect __managed_fill
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#14
  tryerr#15:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at Array.maxon:444: Array.fillPublished: fill OOB \xe2\x80\x94 the window is inside the length the caller just published\x0a"
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
  tryok#14:
    x64.xorRegImm32 r8, r8, 1
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, forhdr
    x64.jmp ifcont
  fill:
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r13
    x64.callDirect __retain_type_param
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rax, [rbx + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __im_slow#25
  __im_owned#29:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __im_slow#25
  __im_bounds#30:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegReg r15, rax
    x64.jcc aboveEqual, __im_slow#25
  __im_buffer#31:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_slow#25
  __im_viewed#32:
    x64.leaRegRegImm32 rcx, rax, -24
    x64.loadRegBaseDisp.word64 rcx, [rcx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, __im_slow#25
  __im_store#33:
    x64.storeBaseIndexScaleReg.word64 [rax + r15*8 + 0], r8
    x64.jmp forstep
  __im_stride:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#25
  __im_byte:
    x64.loadRegBaseDisp.word64 rax, [rbx + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __im_slow#25
  __im_owned#34:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __im_slow#25
  __im_bounds#35:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegReg r15, rax
    x64.jcc aboveEqual, __im_slow#25
  __im_buffer#36:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_slow#25
  __im_viewed#37:
    x64.leaRegRegImm32 rcx, rax, -24
    x64.loadRegBaseDisp.word64 rcx, [rcx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, __im_slow#25
  __im_store#38:
    x64.leaRegRegReg rax, rax, r15
    x64.storeBaseDispReg.byte [rax + 0], r8
    x64.jmp forstep
  __im_slow#25:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.movRegReg rax, r8
    x64.callDirect __managed_mem_set
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, forstep
  tryerr#22:
    x64.leaRegRdata rax, [rip + __str_rec_6]  ; "panic at Array.maxon:450: "
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.leaRegRdata rax, [rip + __str_rec_7]  ; "Array.fillPublished: set OOB at "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot7, rcx
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r14
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
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, r12
    x64.loadRegSlot rdx, slot7
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r13
    x64.loadRegSlot rax, slot6
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot6
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot4
    x64.loadRegSlot rdx, slot5
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot4
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot2
    x64.loadRegSlot rdx, slot3
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot2
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rbx, slot1
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot0
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, r14
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
    x64.leaRegRegImm32 r15, r15, 1
  forhdr:
    x64.cmpRegImm32 r15, 6
    x64.jcc below, fill
  ifcont:
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r13
    x64.callDirect __drop_type_param
  __il_cont:
    x64.movRegImm32 rdx, 5
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 5
    x64.jcc belowEqual, __im_slow#8
    x64.jmp __im_load
  tryerr#2:
    x64.movRegImm32 r12, 9
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rax + rdx*8 + 0]
    x64.movRegReg r8, rax
  tryok#1:
    x64.movRegReg r12, r8
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
  __im_slow#8:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#2
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
    x64.movRegReg rax, rcx
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.cmpRegReg rdx, rcx
    x64.jcc below, load
  oob:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 1
    x64.popReg rbp
    x64.ret
  load:
    x64.loadRegBaseDisp.word64 rsi, [rax + 0]
    x64.loadRegBaseDisp.word64 rcx, [rax + 24]
    x64.cmpRegImm32 rcx, 8
    x64.jcc notEqual, esbyte
  ldword:
    x64.imulRegReg rdx, rdx, rcx
    x64.leaRegRegReg rcx, rsi, rdx
    x64.loadRegBaseDisp.word64 r8, [rcx + 0]
    x64.jmp gchkdtor
  esbyte:
    x64.cmpRegImm32 rcx, 1
    x64.jcc notEqual, __il_body#17
  ldbyte:
    x64.imulRegReg rdx, rdx, rcx
    x64.leaRegRegReg rcx, rsi, rdx
    x64.loadRegBaseDisp.byte r8, [rcx + 0]
    x64.jmp gchkdtor
  __il_body#17:
    x64.movRegReg rdi, rcx
    x64.sarRegImm8 rdi, rcx, 63
    x64.movRegReg r8, rdi
    x64.xorRegImm32 r8, rdi, -1
    x64.movRegImm32 r9, 0
    x64.subRegReg r9, r9, rcx
    x64.shlRegImm8 rcx, rcx, 3
    x64.andRegReg r9, r9, rdi
    x64.andRegReg rcx, rcx, r8
    x64.orRegReg r9, r9, rcx
  __il_cont#16:
    x64.imulRegReg rdx, rdx, r9
    x64.movRegReg rdi, rdx
    x64.shrRegImm8 rdi, rdx, 3
    x64.andRegImm32 rdx, rdx, 7
    x64.movRegImm32 rcx, 64
    x64.subRegReg rcx, rcx, r9
    x64.movRegImm r8, 18446744073709551615
    x64.shrRegCl r8, r8
    x64.leaRegRegReg r9, rdx, r9
  __il_body#18:
    x64.leaRegRegImm32 r9, r9, 7
    x64.shrRegImm8 r9, r9, 3
  __il_cont#15:
    x64.leaRegRegReg rsi, rsi, rdi
    x64.movRegImm32 rdi, 0
    x64.movRegImm32 r10, 0
  ldeh:
    x64.cmpRegReg rdi, r9
    x64.jcc less, ldeb
  ldxtr:
    x64.movRegReg rcx, rdx
    x64.shrRegCl r10, r10
    x64.andRegReg r10, r10, r8
    x64.movRegReg r8, r10
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
    x64.popReg rbp
    x64.ret
  gok:
    x64.movRegImm32 r10, 0
    x64.popReg rbp
    x64.ret
  ldeb:
    x64.leaRegRegReg rcx, rsi, rdi
    x64.loadRegBaseDisp.byte r11, [rcx + 0]
    x64.imulRegRegImm32 rcx, rdi, 8
    x64.shlRegCl r11, r11
    x64.orRegReg r11, r11, r10
    x64.leaRegRegImm32 rdi, rdi, 1
    x64.movRegReg r10, r11
    x64.jmp ldeh
}

func @__managed_fill {
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
    x64.movRegReg r13, r9
    x64.cmpRegImm32 r12, 0
    x64.jcc less, badrange
  chkcount:
    x64.cmpRegImm32 rax, 0
    x64.jcc less, badrange
  chkhigh:
    x64.leaRegRegReg r14, r12, rax
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegReg r14, rax
    x64.jcc belowEqual, classify
  badrange:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 1
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  classify:
    x64.loadRegBaseDisp.word64 rax, [rbx + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, prepare
  declined:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  prepare:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rbx + 24]
    x64.cmpRegImm32 rdx, 8
    x64.jcc notEqual, esbyte
  fillword:
    x64.movRegReg rcx, r12
  fiwh:
    x64.cmpRegReg rcx, r14
    x64.jcc greaterEqual, applied
  fiwb:
    x64.movRegReg rsi, rcx
    x64.imulRegReg rsi, rcx, rdx
    x64.leaRegRegReg rsi, rax, rsi
    x64.storeBaseDispReg.word64 [rsi + 0], r13
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.jmp fiwh
  esbyte:
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, fillgen
  fillbyte:
    x64.movRegReg rcx, r12
  fibh:
    x64.cmpRegReg rcx, r14
    x64.jcc greaterEqual, applied
  fibb:
    x64.movRegReg rsi, rcx
    x64.imulRegReg rsi, rcx, rdx
    x64.leaRegRegReg rsi, rax, rsi
    x64.storeBaseDispReg.byte [rsi + 0], r13
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.jmp fibh
  fillgen:
    x64.movRegReg rsi, r12
  figh:
    x64.cmpRegReg rsi, r14
    x64.jcc less, figb
  applied:
    x64.movRegImm32 r8, 1
    x64.movRegImm32 r10, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  figb:
    x64.cmpRegImm32 rdx, 0
    x64.jcc greaterEqual, stby
  __il_body#29:
    x64.movRegReg rcx, rdx
    x64.sarRegImm8 rcx, rdx, 63
    x64.movRegReg rdi, rcx
    x64.xorRegImm32 rdi, rcx, -1
    x64.movRegImm32 r8, 0
    x64.subRegReg r8, r8, rdx
    x64.movRegReg r9, rdx
    x64.shlRegImm8 r9, rdx, 3
    x64.andRegReg r8, r8, rcx
    x64.andRegReg r9, r9, rdi
    x64.orRegReg r8, r8, r9
  __il_cont#28:
    x64.movRegReg rcx, rsi
    x64.imulRegReg rcx, rsi, r8
    x64.movRegReg rdi, rcx
    x64.shrRegImm8 rdi, rcx, 3
    x64.movRegReg r9, rcx
    x64.andRegImm32 r9, rcx, 7
    x64.movRegImm32 rcx, 64
    x64.subRegReg rcx, rcx, r8
    x64.movRegImm r10, 18446744073709551615
    x64.shrRegCl r10, r10
    x64.leaRegRegReg r8, r9, r8
  __il_body#30:
    x64.leaRegRegImm32 rcx, r8, 7
    x64.shrRegImm8 rcx, rcx, 3
  __il_cont#27:
    x64.leaRegRegReg rdi, rax, rdi
    x64.loadRegBaseDisp.byte r8, [rdi + 0]
    x64.movRegReg rcx, r9
    x64.movRegReg r11, r10
    x64.shlRegCl r11, r10
    x64.xorRegImm32 r11, r11, -1
    x64.andRegReg r8, r8, r11
    x64.movRegReg r11, r13
    x64.andRegReg r11, r13, r10
    x64.movRegReg rcx, r9
    x64.shlRegCl r11, r11
    x64.orRegReg r8, r8, r11
    x64.storeBaseDispReg.byte [rdi + 0], r8
    x64.jmp figstep
  stby:
    x64.movRegReg rcx, rsi
    x64.imulRegReg rcx, rsi, rdx
    x64.leaRegRegReg rdi, rax, rcx
    x64.movRegImm32 r8, 0
  steh:
    x64.cmpRegReg r8, rdx
    x64.jcc less, steb
  figstep:
    x64.leaRegRegImm32 rsi, rsi, 1
    x64.jmp figh
  steb:
    x64.imulRegRegImm32 rcx, r8, 8
    x64.movRegReg r9, r13
    x64.shrRegCl r9, r13
    x64.leaRegRegReg r10, rdi, r8
    x64.storeBaseDispReg.byte [r10 + 0], r9
    x64.leaRegRegImm32 r8, r8, 1
    x64.jmp steh
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
    arm64.leaRdata x22, __layout_Array_int
  __il_body#11:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __managed_resize
    arm64.movImm x23, 0
  __il_body#13:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x20
    arm64.movRegReg x3, x21
    arm64.bl __managed_fill
    arm64.cmp x9, 0
    arm64.b.eq tryok#14
  tryerr#15:
    arm64.leaRdata x0, __str_blob_5  ; "panic at Array.maxon:444: Array.fillPublished: fill OOB \xe2\x80\x94 the window is inside the length the caller just published\x0a"
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
  tryok#14:
    arm64.eor x0, x0, 1
    arm64.cbnz x0, forhdr
    arm64.b ifcont
  fill:
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x21
    arm64.bl __retain_type_param
    arm64.movRegReg x2, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 40]
    arm64.cmp x0, 0
    arm64.b.ne __im_slow#25
  __im_owned#29:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#25
  __im_bounds#30:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x23, x0
    arm64.b.hs __im_slow#25
  __im_buffer#31:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#25
  __im_viewed#32:
    arm64.sub x1, x0, 24
    arm64.loadRegBaseDisp.word64 x1, [x1 + 16]
    arm64.cmp x1, 0
    arm64.b.ne __im_slow#25
  __im_store#33:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x23*8 + 0], x2
    arm64.b forstep
  __im_stride:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#25
  __im_byte:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 40]
    arm64.cmp x0, 0
    arm64.b.ne __im_slow#25
  __im_owned#34:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#25
  __im_bounds#35:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x23, x0
    arm64.b.hs __im_slow#25
  __im_buffer#36:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#25
  __im_viewed#37:
    arm64.sub x1, x0, 24
    arm64.loadRegBaseDisp.word64 x1, [x1 + 16]
    arm64.cmp x1, 0
    arm64.b.ne __im_slow#25
  __im_store#38:
    arm64.add x0, x0, x23
    arm64.storeBaseDispReg.byte [x0 + 0], x2
    arm64.b forstep
  __im_slow#25:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __managed_mem_set
    arm64.cmp x9, 0
    arm64.b.eq forstep
  tryerr#22:
    arm64.leaRdata x0, __str_rec_6  ; "panic at Array.maxon:450: "
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x0 + 8]
    arm64.leaRdata x0, __str_rec_7  ; "Array.fillPublished: set OOB at "
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x24
    arm64.bl __uint_to_string
    arm64.movRegReg x23, x0
    arm64.leaRdata x0, __str_rec_8  ; " \xe2\x80\x94 the window is inside the published length"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.leaRdata x0, __str_rec_9  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot2, x0
    arm64.add x1, x20, x22
    arm64.add x1, x1, x23
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
    arm64.add x19, x19, x26
    arm64.loadRegSlot x2, slot2
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot2
    arm64.add x0, x19, x0
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
    arm64.add x23, x23, 1
  forhdr:
    arm64.cmp x23, 6
    arm64.b.lo fill
  ifcont:
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x21
    arm64.bl __drop_type_param
  __il_cont:
    arm64.movImm x1, 5
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 5
    arm64.b.ls __im_slow#8
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
  __im_slow#8:
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
    arm64.b.ne esbyte
  ldword:
    arm64.mul x1, x1, x3
    arm64.add x1, x2, x1
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.b gchkdtor
  esbyte:
    arm64.cmp x3, 1
    arm64.b.ne __il_body#17
  ldbyte:
    arm64.mul x1, x1, x3
    arm64.add x1, x2, x1
    arm64.loadRegBaseDisp.byte x1, [x1 + 0]
    arm64.b gchkdtor
  __il_body#17:
    arm64.asr x4, x3, 63
    arm64.movImm x16, 18446744073709551615
    arm64.eor x5, x4, x16
    arm64.movImm x6, 0
    arm64.sub x6, x6, x3
    arm64.lsl x3, x3, 3
    arm64.and x4, x6, x4
    arm64.and x3, x3, x5
    arm64.orr x3, x4, x3
  __il_cont#16:
    arm64.mul x1, x1, x3
    arm64.lsr x4, x1, 3
    arm64.and x1, x1, 7
    arm64.movImm x5, 64
    arm64.sub x5, x5, x3
    arm64.movImm x6, 18446744073709551615
    arm64.lsrv x5, x6, x5
    arm64.add x3, x1, x3
  __il_body#18:
    arm64.add x3, x3, 7
    arm64.lsr x3, x3, 3
  __il_cont#15:
    arm64.add x2, x2, x4
    arm64.movImm x4, 0
    arm64.movImm x6, 0
  ldeh:
    arm64.cmp x4, x3
    arm64.b.lt ldeb
  ldxtr:
    arm64.lsrv x1, x6, x1
    arm64.and x1, x1, x5
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
  ldeb:
    arm64.add x7, x2, x4
    arm64.loadRegBaseDisp.byte x7, [x7 + 0]
    arm64.lsl x8, x4, 3
    arm64.lslv x7, x7, x8
    arm64.orr x6, x6, x7
    arm64.add x4, x4, 1
    arm64.b ldeh
}

func @__managed_fill {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x3
    arm64.cmp x20, 0
    arm64.b.lt badrange
  chkcount:
    arm64.cmp x2, 0
    arm64.b.lt badrange
  chkhigh:
    arm64.add x22, x20, x2
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x22, x0
    arm64.b.ls classify
  badrange:
    arm64.movImm x0, 0
    arm64.movImm x9, 1
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  classify:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 40]
    arm64.cmp x0, 0
    arm64.b.eq prepare
  declined:
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  prepare:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x19 + 24]
    arm64.cmp x1, 8
    arm64.b.ne esbyte
  fillword:
    arm64.movRegReg x2, x20
  fiwh:
    arm64.cmp x2, x22
    arm64.b.ge applied
  fiwb:
    arm64.mul x3, x2, x1
    arm64.add x3, x0, x3
    arm64.storeBaseDispReg.word64 [x3 + 0], x21
    arm64.add x2, x2, 1
    arm64.b fiwh
  esbyte:
    arm64.cmp x1, 1
    arm64.b.ne fillgen
  fillbyte:
    arm64.movRegReg x2, x20
  fibh:
    arm64.cmp x2, x22
    arm64.b.ge applied
  fibb:
    arm64.mul x3, x2, x1
    arm64.add x3, x0, x3
    arm64.storeBaseDispReg.byte [x3 + 0], x21
    arm64.add x2, x2, 1
    arm64.b fibh
  fillgen:
    arm64.movRegReg x2, x20
  figh:
    arm64.cmp x2, x22
    arm64.b.lt figb
  applied:
    arm64.movImm x0, 1
    arm64.movImm x9, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  figb:
    arm64.cmp x1, 0
    arm64.b.ge stby
  __il_body#29:
    arm64.asr x3, x1, 63
    arm64.movImm x16, 18446744073709551615
    arm64.eor x4, x3, x16
    arm64.movImm x5, 0
    arm64.sub x5, x5, x1
    arm64.lsl x6, x1, 3
    arm64.and x3, x5, x3
    arm64.and x4, x6, x4
    arm64.orr x3, x3, x4
  __il_cont#28:
    arm64.mul x4, x2, x3
    arm64.lsr x5, x4, 3
    arm64.and x4, x4, 7
    arm64.movImm x6, 64
    arm64.sub x6, x6, x3
    arm64.movImm x7, 18446744073709551615
    arm64.lsrv x6, x7, x6
    arm64.add x3, x4, x3
  __il_body#30:
    arm64.add x3, x3, 7
    arm64.lsr x3, x3, 3
  __il_cont#27:
    arm64.add x3, x0, x5
    arm64.loadRegBaseDisp.byte x5, [x3 + 0]
    arm64.lslv x7, x6, x4
    arm64.movImm x16, 18446744073709551615
    arm64.eor x7, x7, x16
    arm64.and x5, x5, x7
    arm64.and x6, x21, x6
    arm64.lslv x4, x6, x4
    arm64.orr x4, x5, x4
    arm64.storeBaseDispReg.byte [x3 + 0], x4
    arm64.b figstep
  stby:
    arm64.mul x3, x2, x1
    arm64.add x3, x0, x3
    arm64.movImm x4, 0
  steh:
    arm64.cmp x4, x1
    arm64.b.lt steb
  figstep:
    arm64.add x2, x2, 1
    arm64.b figh
  steb:
    arm64.lsl x5, x4, 3
    arm64.lsrv x5, x21, x5
    arm64.add x6, x3, x4
    arm64.storeBaseDispReg.byte [x6 + 0], x5
    arm64.add x4, x4, 1
    arm64.b steh
}
```

```TargetIr:arm64-linux
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
    arm64.leaRdata x22, __layout_Array_int
  __il_body#11:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __managed_resize
    arm64.movImm x23, 0
  __il_body#13:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x20
    arm64.movRegReg x3, x21
    arm64.bl __managed_fill
    arm64.cmp x9, 0
    arm64.b.eq tryok#14
  tryerr#15:
    arm64.leaRdata x0, __str_blob_5  ; "panic at Array.maxon:444: Array.fillPublished: fill OOB \xe2\x80\x94 the window is inside the length the caller just published\x0a"
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
  tryok#14:
    arm64.eor x0, x0, 1
    arm64.cbnz x0, forhdr
    arm64.b ifcont
  fill:
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x21
    arm64.bl __retain_type_param
    arm64.movRegReg x2, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 40]
    arm64.cmp x0, 0
    arm64.b.ne __im_slow#25
  __im_owned#29:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#25
  __im_bounds#30:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x23, x0
    arm64.b.hs __im_slow#25
  __im_buffer#31:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#25
  __im_viewed#32:
    arm64.sub x1, x0, 24
    arm64.loadRegBaseDisp.word64 x1, [x1 + 16]
    arm64.cmp x1, 0
    arm64.b.ne __im_slow#25
  __im_store#33:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x23*8 + 0], x2
    arm64.b forstep
  __im_stride:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#25
  __im_byte:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 40]
    arm64.cmp x0, 0
    arm64.b.ne __im_slow#25
  __im_owned#34:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#25
  __im_bounds#35:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x23, x0
    arm64.b.hs __im_slow#25
  __im_buffer#36:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#25
  __im_viewed#37:
    arm64.sub x1, x0, 24
    arm64.loadRegBaseDisp.word64 x1, [x1 + 16]
    arm64.cmp x1, 0
    arm64.b.ne __im_slow#25
  __im_store#38:
    arm64.add x0, x0, x23
    arm64.storeBaseDispReg.byte [x0 + 0], x2
    arm64.b forstep
  __im_slow#25:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __managed_mem_set
    arm64.cmp x9, 0
    arm64.b.eq forstep
  tryerr#22:
    arm64.leaRdata x0, __str_rec_6  ; "panic at Array.maxon:450: "
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x0 + 8]
    arm64.leaRdata x0, __str_rec_7  ; "Array.fillPublished: set OOB at "
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x24
    arm64.bl __uint_to_string
    arm64.movRegReg x23, x0
    arm64.leaRdata x0, __str_rec_8  ; " \xe2\x80\x94 the window is inside the published length"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.leaRdata x0, __str_rec_9  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot2, x0
    arm64.add x1, x20, x22
    arm64.add x1, x1, x23
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
    arm64.add x19, x19, x26
    arm64.loadRegSlot x2, slot2
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot2
    arm64.add x0, x19, x0
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
    arm64.add x23, x23, 1
  forhdr:
    arm64.cmp x23, 6
    arm64.b.lo fill
  ifcont:
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x21
    arm64.bl __drop_type_param
  __il_cont:
    arm64.movImm x1, 5
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 5
    arm64.b.ls __im_slow#8
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
  __im_slow#8:
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
    arm64.b.ne esbyte
  ldword:
    arm64.mul x1, x1, x3
    arm64.add x1, x2, x1
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.b gchkdtor
  esbyte:
    arm64.cmp x3, 1
    arm64.b.ne __il_body#17
  ldbyte:
    arm64.mul x1, x1, x3
    arm64.add x1, x2, x1
    arm64.loadRegBaseDisp.byte x1, [x1 + 0]
    arm64.b gchkdtor
  __il_body#17:
    arm64.asr x4, x3, 63
    arm64.movImm x16, 18446744073709551615
    arm64.eor x5, x4, x16
    arm64.movImm x6, 0
    arm64.sub x6, x6, x3
    arm64.lsl x3, x3, 3
    arm64.and x4, x6, x4
    arm64.and x3, x3, x5
    arm64.orr x3, x4, x3
  __il_cont#16:
    arm64.mul x1, x1, x3
    arm64.lsr x4, x1, 3
    arm64.and x1, x1, 7
    arm64.movImm x5, 64
    arm64.sub x5, x5, x3
    arm64.movImm x6, 18446744073709551615
    arm64.lsrv x5, x6, x5
    arm64.add x3, x1, x3
  __il_body#18:
    arm64.add x3, x3, 7
    arm64.lsr x3, x3, 3
  __il_cont#15:
    arm64.add x2, x2, x4
    arm64.movImm x4, 0
    arm64.movImm x6, 0
  ldeh:
    arm64.cmp x4, x3
    arm64.b.lt ldeb
  ldxtr:
    arm64.lsrv x1, x6, x1
    arm64.and x1, x1, x5
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
  ldeb:
    arm64.add x7, x2, x4
    arm64.loadRegBaseDisp.byte x7, [x7 + 0]
    arm64.lsl x8, x4, 3
    arm64.lslv x7, x7, x8
    arm64.orr x6, x6, x7
    arm64.add x4, x4, 1
    arm64.b ldeh
}

func @__managed_fill {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x3
    arm64.cmp x20, 0
    arm64.b.lt badrange
  chkcount:
    arm64.cmp x2, 0
    arm64.b.lt badrange
  chkhigh:
    arm64.add x22, x20, x2
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x22, x0
    arm64.b.ls classify
  badrange:
    arm64.movImm x0, 0
    arm64.movImm x9, 1
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  classify:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 40]
    arm64.cmp x0, 0
    arm64.b.eq prepare
  declined:
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  prepare:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x19 + 24]
    arm64.cmp x1, 8
    arm64.b.ne esbyte
  fillword:
    arm64.movRegReg x2, x20
  fiwh:
    arm64.cmp x2, x22
    arm64.b.ge applied
  fiwb:
    arm64.mul x3, x2, x1
    arm64.add x3, x0, x3
    arm64.storeBaseDispReg.word64 [x3 + 0], x21
    arm64.add x2, x2, 1
    arm64.b fiwh
  esbyte:
    arm64.cmp x1, 1
    arm64.b.ne fillgen
  fillbyte:
    arm64.movRegReg x2, x20
  fibh:
    arm64.cmp x2, x22
    arm64.b.ge applied
  fibb:
    arm64.mul x3, x2, x1
    arm64.add x3, x0, x3
    arm64.storeBaseDispReg.byte [x3 + 0], x21
    arm64.add x2, x2, 1
    arm64.b fibh
  fillgen:
    arm64.movRegReg x2, x20
  figh:
    arm64.cmp x2, x22
    arm64.b.lt figb
  applied:
    arm64.movImm x0, 1
    arm64.movImm x9, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  figb:
    arm64.cmp x1, 0
    arm64.b.ge stby
  __il_body#29:
    arm64.asr x3, x1, 63
    arm64.movImm x16, 18446744073709551615
    arm64.eor x4, x3, x16
    arm64.movImm x5, 0
    arm64.sub x5, x5, x1
    arm64.lsl x6, x1, 3
    arm64.and x3, x5, x3
    arm64.and x4, x6, x4
    arm64.orr x3, x3, x4
  __il_cont#28:
    arm64.mul x4, x2, x3
    arm64.lsr x5, x4, 3
    arm64.and x4, x4, 7
    arm64.movImm x6, 64
    arm64.sub x6, x6, x3
    arm64.movImm x7, 18446744073709551615
    arm64.lsrv x6, x7, x6
    arm64.add x3, x4, x3
  __il_body#30:
    arm64.add x3, x3, 7
    arm64.lsr x3, x3, 3
  __il_cont#27:
    arm64.add x3, x0, x5
    arm64.loadRegBaseDisp.byte x5, [x3 + 0]
    arm64.lslv x7, x6, x4
    arm64.movImm x16, 18446744073709551615
    arm64.eor x7, x7, x16
    arm64.and x5, x5, x7
    arm64.and x6, x21, x6
    arm64.lslv x4, x6, x4
    arm64.orr x4, x5, x4
    arm64.storeBaseDispReg.byte [x3 + 0], x4
    arm64.b figstep
  stby:
    arm64.mul x3, x2, x1
    arm64.add x3, x0, x3
    arm64.movImm x4, 0
  steh:
    arm64.cmp x4, x1
    arm64.b.lt steb
  figstep:
    arm64.add x2, x2, 1
    arm64.b figh
  steb:
    arm64.lsl x5, x4, 3
    arm64.lsrv x5, x21, x5
    arm64.add x6, x3, x4
    arm64.storeBaseDispReg.byte [x6 + 0], x5
    arm64.add x4, x4, 1
    arm64.b steh
}
```
