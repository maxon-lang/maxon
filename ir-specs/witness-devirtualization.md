---
feature: witness-devirtualization
status: experimental
keywords: [witness, interface, generics, devirtualization, inliner, iterator, codegen]
category: codegen
---

# Witness Dispatch Resolved at a Constant Table: the cases that pin emitted code

The cases of `specs/witness-devirtualization.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

The generic body itself stays one shared function that dispatches through its tables; what these pins show is
the copy spliced into a caller whose tables are constant, with every call through them resolved.

## Tests

<!-- test: a-with-iterator-loop-over-an-array-calls-no-witness -->
`for (iter, value) in values.withIterator()` walks the array through `WithIterIterator`, whose `current` and
`advance` reach the array's iterator through the witness table the generic body receives. Once the body is
spliced into a caller that table is a constant of the caller, so each slot is a known function: the
witness call becomes a direct call and the array iterator's `current` and `advance` splice into the loop.
The pin below is `indexedSum` and `weightedSum` with no `callMem`, no call to `WithIterIterator.current` or
`WithIterIterator.advance`, no call to the array iterator's `current` or `advance` or to the adapter that
stands in front of it, and no `__witness_` table read in the loop. Two loops, each in a function `main` calls
twice, so neither `WithIterIterator` method nor either function is spliced for being called once. Prints
through the exit code: 16 and 5 for the sums with the index, 26 and 10 for the doubled ones, 57.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function indexedSum(values IntArray) returns Integer
	var total = 0 as Integer

	for (iter, value) in values.withIterator() 'each'
		total = total + value + (iter.index() as Integer)
	end 'each'

	return total
end 'indexedSum'

function weightedSum(values IntArray) returns Integer
	var total = 0 as Integer

	for (iter, value) in values.withIterator() 'each'
		total = total + value * 2 + (iter.index() as Integer)
	end 'each'

	return total
end 'weightedSum'

function main() returns ExitCode
	let first = IntArray from [1, 2, 3, 4]
	let second = IntArray from [5]

	return (indexedSum(first) + indexedSum(second) + weightedSum(first) + weightedSum(second)) as ExitCode
end 'main'
```
```exitcode
57
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

func @indexedSum {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
    x64.movRegImm32 rbx, 0
    x64.leaRegRdata rdx, [rip + __layout_Array_Integer]
    x64.callDirect Array.withIterator
    x64.movRegReg r12, r8
    x64.movRegImm32 rax, 0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, forhdr
    x64.jmp forempty
  __il_body#9:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#20:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.leaRegRdata rdx, [rip + __str_rec_6]  ; "panic at Array.maxon:497: "
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdi, [rsi + 8]
    x64.cmpRegReg rax, rdi
    x64.jcc aboveEqual, __im_slow#22
  __im_load:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 24]
    x64.cmpRegImm32 rdi, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rdi + rax*8 + 0]
    x64.jmp __im_loaded
  __im_stride:
    x64.cmpRegImm32 rdi, 1
    x64.jcc notEqual, __im_slow#22
  __im_byte:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 0]
    x64.leaRegRegReg rax, rdi, rax
    x64.loadRegBaseDisp.byte rax, [rax + 0]
  __im_loaded:
    x64.loadRegBaseDisp.word64 rsi, [rsi + 40]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, critsplit#37
  __im_empty:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#38
  __im_slow#22:
    x64.callDirect __managed_cursor_current
    x64.jmp __il_cont#8
  critsplit#38:
    x64.movRegReg r8, rax
    x64.jmp __il_cont#8
  critsplit#37:
    x64.movRegReg r8, rax
  __il_cont#8:
    x64.leaRegRegReg rax, rbx, r8
  __il_body#10:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic
  __il_cont#7:
    x64.leaRegRegReg rbx, rax, rcx
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_ArrayIterator_Integer
  __il_body#14:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#30:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegReg rax, rdx
    x64.jcc aboveEqual, __im_slow#34
    x64.jmp __im_move
  tryerr:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#29
  __im_move:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  tryok#31:
    x64.movRegImm32 rax, 0
  __il_cont#29:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, forhdr
  tryok#15:
    x64.movRegImm32 rax, 0
  forhdr:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __il_body#9
  forexit:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Integer_Integer
    x64.movRegReg r8, rbx
    x64.jmp forjoin
  forempty:
    x64.movRegReg r8, rbx
  forjoin:
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#34:
    x64.callDirect __managed_cursor_advance
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok#31
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_36]  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @weightedSum {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
    x64.movRegImm32 rbx, 0
    x64.leaRegRdata rdx, [rip + __layout_Array_Integer]
    x64.callDirect Array.withIterator
    x64.movRegReg r12, r8
    x64.movRegImm32 rax, 0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, forhdr
    x64.jmp forempty
  __il_body#9:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#20:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.leaRegRdata rdx, [rip + __str_rec_6]  ; "panic at Array.maxon:497: "
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdi, [rsi + 8]
    x64.cmpRegReg rax, rdi
    x64.jcc aboveEqual, __im_slow#22
  __im_load:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 24]
    x64.cmpRegImm32 rdi, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rdi + rax*8 + 0]
    x64.jmp __im_loaded
  __im_stride:
    x64.cmpRegImm32 rdi, 1
    x64.jcc notEqual, __im_slow#22
  __im_byte:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 0]
    x64.leaRegRegReg rax, rdi, rax
    x64.loadRegBaseDisp.byte rax, [rax + 0]
  __im_loaded:
    x64.loadRegBaseDisp.word64 rsi, [rsi + 40]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, critsplit#37
  __im_empty:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#38
  __im_slow#22:
    x64.callDirect __managed_cursor_current
    x64.jmp __il_cont#8
  critsplit#38:
    x64.movRegReg r8, rax
    x64.jmp __il_cont#8
  critsplit#37:
    x64.movRegReg r8, rax
  __il_cont#8:
    x64.leaRegBaseIndexScale rax, [rbx + r8*2]
  __il_body#10:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic
  __il_cont#7:
    x64.leaRegRegReg rbx, rax, rcx
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_ArrayIterator_Integer
  __il_body#14:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#30:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegReg rax, rdx
    x64.jcc aboveEqual, __im_slow#34
    x64.jmp __im_move
  tryerr:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#29
  __im_move:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  tryok#31:
    x64.movRegImm32 rax, 0
  __il_cont#29:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, forhdr
  tryok#15:
    x64.movRegImm32 rax, 0
  forhdr:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __il_body#9
  forexit:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Integer_Integer
    x64.movRegReg r8, rbx
    x64.jmp forjoin
  forempty:
    x64.movRegReg r8, rbx
  forjoin:
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#34:
    x64.callDirect __managed_cursor_advance
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok#31
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_36]  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
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
    x64.pushReg r13
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [rbx + 0], rax
    x64.movRegImm32 rax, 4
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __barr_blob_1]
    x64.storeBaseDispReg.word64 [r12 + 0], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.movRegReg rcx, rbx
    x64.callDirect indexedSum
    x64.movRegReg r13, r8
    x64.movRegReg rcx, r12
    x64.callDirect indexedSum
    x64.leaRegRegReg r13, r13, r8
    x64.movRegReg rcx, rbx
    x64.callDirect weightedSum
    x64.leaRegRegReg r13, r13, r8
    x64.movRegReg rcx, r12
    x64.callDirect weightedSum
    x64.leaRegRegReg r13, r13, r8
    x64.cmpRegImm32 r13, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r13, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitdrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, r13
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_35]  ; "panic at a-with-iterator-loop-over-an-array-calls-no-witness.test:29: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
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
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_program_started@40 = i8 0
}

func @indexedSum {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
    x64.movRegImm32 rbx, 0
    x64.leaRegRdata rdx, [rip + __layout_Array_Integer]
    x64.callDirect Array.withIterator
    x64.movRegReg r12, r8
    x64.movRegImm32 rax, 0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, forhdr
    x64.jmp forempty
  __il_body#9:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#20:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.leaRegRdata rdx, [rip + __str_rec_6]  ; "panic at Array.maxon:497: "
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdi, [rsi + 8]
    x64.cmpRegReg rax, rdi
    x64.jcc aboveEqual, __im_slow#22
  __im_load:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 24]
    x64.cmpRegImm32 rdi, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rdi + rax*8 + 0]
    x64.jmp __im_loaded
  __im_stride:
    x64.cmpRegImm32 rdi, 1
    x64.jcc notEqual, __im_slow#22
  __im_byte:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 0]
    x64.leaRegRegReg rax, rdi, rax
    x64.loadRegBaseDisp.byte rax, [rax + 0]
  __im_loaded:
    x64.loadRegBaseDisp.word64 rsi, [rsi + 40]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, critsplit#37
  __im_empty:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#38
  __im_slow#22:
    x64.callDirect __managed_cursor_current
    x64.jmp __il_cont#8
  critsplit#38:
    x64.movRegReg r8, rax
    x64.jmp __il_cont#8
  critsplit#37:
    x64.movRegReg r8, rax
  __il_cont#8:
    x64.leaRegRegReg rax, rbx, r8
  __il_body#10:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic
  __il_cont#7:
    x64.leaRegRegReg rbx, rax, rcx
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_ArrayIterator_Integer
  __il_body#14:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#30:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegReg rax, rdx
    x64.jcc aboveEqual, __im_slow#34
    x64.jmp __im_move
  tryerr:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#29
  __im_move:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  tryok#31:
    x64.movRegImm32 rax, 0
  __il_cont#29:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, forhdr
  tryok#15:
    x64.movRegImm32 rax, 0
  forhdr:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __il_body#9
  forexit:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Integer_Integer
    x64.movRegReg r8, rbx
    x64.jmp forjoin
  forempty:
    x64.movRegReg r8, rbx
  forjoin:
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#34:
    x64.callDirect __managed_cursor_advance
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok#31
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_36]  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @weightedSum {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
    x64.movRegImm32 rbx, 0
    x64.leaRegRdata rdx, [rip + __layout_Array_Integer]
    x64.callDirect Array.withIterator
    x64.movRegReg r12, r8
    x64.movRegImm32 rax, 0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, forhdr
    x64.jmp forempty
  __il_body#9:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#20:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.leaRegRdata rdx, [rip + __str_rec_6]  ; "panic at Array.maxon:497: "
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdi, [rsi + 8]
    x64.cmpRegReg rax, rdi
    x64.jcc aboveEqual, __im_slow#22
  __im_load:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 24]
    x64.cmpRegImm32 rdi, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rdi + rax*8 + 0]
    x64.jmp __im_loaded
  __im_stride:
    x64.cmpRegImm32 rdi, 1
    x64.jcc notEqual, __im_slow#22
  __im_byte:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 0]
    x64.leaRegRegReg rax, rdi, rax
    x64.loadRegBaseDisp.byte rax, [rax + 0]
  __im_loaded:
    x64.loadRegBaseDisp.word64 rsi, [rsi + 40]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, critsplit#37
  __im_empty:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#38
  __im_slow#22:
    x64.callDirect __managed_cursor_current
    x64.jmp __il_cont#8
  critsplit#38:
    x64.movRegReg r8, rax
    x64.jmp __il_cont#8
  critsplit#37:
    x64.movRegReg r8, rax
  __il_cont#8:
    x64.leaRegBaseIndexScale rax, [rbx + r8*2]
  __il_body#10:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic
  __il_cont#7:
    x64.leaRegRegReg rbx, rax, rcx
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_ArrayIterator_Integer
  __il_body#14:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#30:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegReg rax, rdx
    x64.jcc aboveEqual, __im_slow#34
    x64.jmp __im_move
  tryerr:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#29
  __im_move:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  tryok#31:
    x64.movRegImm32 rax, 0
  __il_cont#29:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, forhdr
  tryok#15:
    x64.movRegImm32 rax, 0
  forhdr:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __il_body#9
  forexit:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Integer_Integer
    x64.movRegReg r8, rbx
    x64.jmp forjoin
  forempty:
    x64.movRegReg r8, rbx
  forjoin:
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#34:
    x64.callDirect __managed_cursor_advance
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok#31
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_36]  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
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
    x64.pushReg r13
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [rbx + 0], rax
    x64.movRegImm32 rax, 4
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __barr_blob_1]
    x64.storeBaseDispReg.word64 [r12 + 0], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.movRegReg rcx, rbx
    x64.callDirect indexedSum
    x64.movRegReg r13, r8
    x64.movRegReg rcx, r12
    x64.callDirect indexedSum
    x64.leaRegRegReg r13, r13, r8
    x64.movRegReg rcx, rbx
    x64.callDirect weightedSum
    x64.leaRegRegReg r13, r13, r8
    x64.movRegReg rcx, r12
    x64.callDirect weightedSum
    x64.leaRegRegReg r13, r13, r8
    x64.cmpRegImm32 r13, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r13, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitdrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, r13
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_35]  ; "panic at a-with-iterator-loop-over-an-array-calls-no-witness.test:29: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
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
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @indexedSum {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 0
    arm64.leaRdata x1, __layout_Array_Integer
    arm64.bl Array.withIterator
    arm64.movRegReg x20, x0
    arm64.movImm x0, 0
    arm64.cmp x9, 0
    arm64.b.eq forhdr
    arm64.b forempty
  __il_body#9:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#20:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaRdata x1, __str_rec_6  ; "panic at Array.maxon:497: "
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x4, [x3 + 8]
    arm64.cmp x2, x4
    arm64.b.hs __im_slow#22
  __im_load:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 24]
    arm64.cmp x4, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.loadRegBaseIndexScale.word64 x2, [x4 + x2*8 + 0]
    arm64.b __im_loaded
  __im_stride:
    arm64.cmp x4, 1
    arm64.b.ne __im_slow#22
  __im_byte:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.add x2, x4, x2
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
  __im_loaded:
    arm64.loadRegBaseDisp.word64 x3, [x3 + 40]
    arm64.cmp x3, 0
    arm64.b.eq critsplit#37
  __im_empty:
    arm64.cmp x2, 0
    arm64.b.ne critsplit#38
  __im_slow#22:
    arm64.bl __managed_cursor_current
    arm64.b __il_cont#8
  critsplit#38:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#8
  critsplit#37:
    arm64.movRegReg x0, x2
  __il_cont#8:
    arm64.add x0, x19, x0
  __il_body#10:
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic
  __il_cont#7:
    arm64.add x19, x0, x1
    arm64.movRegReg x0, x21
    arm64.bl __destruct_ArrayIterator_Integer
  __il_body#14:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#30:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#34
    arm64.b __im_move
  tryerr:
    arm64.movImm x0, 1
    arm64.b __il_cont#29
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#31:
    arm64.movImm x0, 0
  __il_cont#29:
    arm64.cmp x0, 0
    arm64.b.ne forhdr
  tryok#15:
    arm64.movImm x0, 0
  forhdr:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#9
  forexit:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Integer_Integer
    arm64.movRegReg x0, x19
    arm64.b forjoin
  forempty:
    arm64.movRegReg x0, x19
  forjoin:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __im_slow#34:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok#31
  __rc_panic:
    arm64.leaRdata x0, __str_blob_36  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @weightedSum {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 0
    arm64.leaRdata x1, __layout_Array_Integer
    arm64.bl Array.withIterator
    arm64.movRegReg x20, x0
    arm64.movImm x0, 0
    arm64.cmp x9, 0
    arm64.b.eq forhdr
    arm64.b forempty
  __il_body#9:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#20:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaRdata x1, __str_rec_6  ; "panic at Array.maxon:497: "
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x4, [x3 + 8]
    arm64.cmp x2, x4
    arm64.b.hs __im_slow#22
  __im_load:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 24]
    arm64.cmp x4, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.loadRegBaseIndexScale.word64 x2, [x4 + x2*8 + 0]
    arm64.b __im_loaded
  __im_stride:
    arm64.cmp x4, 1
    arm64.b.ne __im_slow#22
  __im_byte:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.add x2, x4, x2
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
  __im_loaded:
    arm64.loadRegBaseDisp.word64 x3, [x3 + 40]
    arm64.cmp x3, 0
    arm64.b.eq critsplit#37
  __im_empty:
    arm64.cmp x2, 0
    arm64.b.ne critsplit#38
  __im_slow#22:
    arm64.bl __managed_cursor_current
    arm64.b __il_cont#8
  critsplit#38:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#8
  critsplit#37:
    arm64.movRegReg x0, x2
  __il_cont#8:
    arm64.arm64AddLsl x0, x19, x0, lsl #1
  __il_body#10:
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic
  __il_cont#7:
    arm64.add x19, x0, x1
    arm64.movRegReg x0, x21
    arm64.bl __destruct_ArrayIterator_Integer
  __il_body#14:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#30:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#34
    arm64.b __im_move
  tryerr:
    arm64.movImm x0, 1
    arm64.b __il_cont#29
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#31:
    arm64.movImm x0, 0
  __il_cont#29:
    arm64.cmp x0, 0
    arm64.b.ne forhdr
  tryok#15:
    arm64.movImm x0, 0
  forhdr:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#9
  forexit:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Integer_Integer
    arm64.movRegReg x0, x19
    arm64.b forjoin
  forempty:
    arm64.movRegReg x0, x19
  forjoin:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __im_slow#34:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok#31
  __rc_panic:
    arm64.leaRdata x0, __str_blob_36  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.movImm x0, 4
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 8
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __barr_blob_1
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.movImm x0, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
    arm64.movImm x0, 8
    arm64.storeBaseDispReg.word64 [x20 + 24], x0
    arm64.movRegReg x0, x19
    arm64.bl indexedSum
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl indexedSum
    arm64.add x21, x21, x0
    arm64.movRegReg x0, x19
    arm64.bl weightedSum
    arm64.add x21, x21, x0
    arm64.movRegReg x0, x20
    arm64.bl weightedSum
    arm64.add x21, x21, x0
    arm64.cmp x21, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x21, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitdrop:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x21
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_35  ; "panic at a-with-iterator-loop-over-an-array-calls-no-witness.test:29: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
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

func @indexedSum {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 0
    arm64.leaRdata x1, __layout_Array_Integer
    arm64.bl Array.withIterator
    arm64.movRegReg x20, x0
    arm64.movImm x0, 0
    arm64.cmp x9, 0
    arm64.b.eq forhdr
    arm64.b forempty
  __il_body#9:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#20:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaRdata x1, __str_rec_6  ; "panic at Array.maxon:497: "
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x4, [x3 + 8]
    arm64.cmp x2, x4
    arm64.b.hs __im_slow#22
  __im_load:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 24]
    arm64.cmp x4, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.loadRegBaseIndexScale.word64 x2, [x4 + x2*8 + 0]
    arm64.b __im_loaded
  __im_stride:
    arm64.cmp x4, 1
    arm64.b.ne __im_slow#22
  __im_byte:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.add x2, x4, x2
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
  __im_loaded:
    arm64.loadRegBaseDisp.word64 x3, [x3 + 40]
    arm64.cmp x3, 0
    arm64.b.eq critsplit#37
  __im_empty:
    arm64.cmp x2, 0
    arm64.b.ne critsplit#38
  __im_slow#22:
    arm64.bl __managed_cursor_current
    arm64.b __il_cont#8
  critsplit#38:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#8
  critsplit#37:
    arm64.movRegReg x0, x2
  __il_cont#8:
    arm64.add x0, x19, x0
  __il_body#10:
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic
  __il_cont#7:
    arm64.add x19, x0, x1
    arm64.movRegReg x0, x21
    arm64.bl __destruct_ArrayIterator_Integer
  __il_body#14:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#30:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#34
    arm64.b __im_move
  tryerr:
    arm64.movImm x0, 1
    arm64.b __il_cont#29
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#31:
    arm64.movImm x0, 0
  __il_cont#29:
    arm64.cmp x0, 0
    arm64.b.ne forhdr
  tryok#15:
    arm64.movImm x0, 0
  forhdr:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#9
  forexit:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Integer_Integer
    arm64.movRegReg x0, x19
    arm64.b forjoin
  forempty:
    arm64.movRegReg x0, x19
  forjoin:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __im_slow#34:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok#31
  __rc_panic:
    arm64.leaRdata x0, __str_blob_36  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @weightedSum {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 0
    arm64.leaRdata x1, __layout_Array_Integer
    arm64.bl Array.withIterator
    arm64.movRegReg x20, x0
    arm64.movImm x0, 0
    arm64.cmp x9, 0
    arm64.b.eq forhdr
    arm64.b forempty
  __il_body#9:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#20:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaRdata x1, __str_rec_6  ; "panic at Array.maxon:497: "
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x4, [x3 + 8]
    arm64.cmp x2, x4
    arm64.b.hs __im_slow#22
  __im_load:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 24]
    arm64.cmp x4, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.loadRegBaseIndexScale.word64 x2, [x4 + x2*8 + 0]
    arm64.b __im_loaded
  __im_stride:
    arm64.cmp x4, 1
    arm64.b.ne __im_slow#22
  __im_byte:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.add x2, x4, x2
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
  __im_loaded:
    arm64.loadRegBaseDisp.word64 x3, [x3 + 40]
    arm64.cmp x3, 0
    arm64.b.eq critsplit#37
  __im_empty:
    arm64.cmp x2, 0
    arm64.b.ne critsplit#38
  __im_slow#22:
    arm64.bl __managed_cursor_current
    arm64.b __il_cont#8
  critsplit#38:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#8
  critsplit#37:
    arm64.movRegReg x0, x2
  __il_cont#8:
    arm64.arm64AddLsl x0, x19, x0, lsl #1
  __il_body#10:
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic
  __il_cont#7:
    arm64.add x19, x0, x1
    arm64.movRegReg x0, x21
    arm64.bl __destruct_ArrayIterator_Integer
  __il_body#14:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#30:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#34
    arm64.b __im_move
  tryerr:
    arm64.movImm x0, 1
    arm64.b __il_cont#29
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#31:
    arm64.movImm x0, 0
  __il_cont#29:
    arm64.cmp x0, 0
    arm64.b.ne forhdr
  tryok#15:
    arm64.movImm x0, 0
  forhdr:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#9
  forexit:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Integer_Integer
    arm64.movRegReg x0, x19
    arm64.b forjoin
  forempty:
    arm64.movRegReg x0, x19
  forjoin:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __im_slow#34:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok#31
  __rc_panic:
    arm64.leaRdata x0, __str_blob_36  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.movImm x0, 4
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 8
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __barr_blob_1
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.movImm x0, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
    arm64.movImm x0, 8
    arm64.storeBaseDispReg.word64 [x20 + 24], x0
    arm64.movRegReg x0, x19
    arm64.bl indexedSum
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl indexedSum
    arm64.add x21, x21, x0
    arm64.movRegReg x0, x19
    arm64.bl weightedSum
    arm64.add x21, x21, x0
    arm64.movRegReg x0, x20
    arm64.bl weightedSum
    arm64.add x21, x21, x0
    arm64.cmp x21, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x21, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitdrop:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x21
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_35  ; "panic at a-with-iterator-loop-over-an-array-calls-no-witness.test:29: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```
