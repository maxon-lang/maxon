---
feature: returned-parameters
status: stable
keywords: [refcount, retain, release, parameter, return, ownership, fusion, codegen]
category: memory
---

# A function that returns its parameter: the cases that pin emitted code

The cases of `specs/returned-parameters.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: a-var-rebound-through-a-called-step-takes-no-reference -->
`step` returns its parameter and is called from two loops, so it stays a call. Each loop rebinds its `var`
to the result: the pin shows `step` handing its parameter back with no `__managed_retain`, and each loop
testing the record for an image once and carrying on, with no `__managed_retain` and no `__managed_decref`
per iteration. Prints `20 40`.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function fill(cells TallyArray, count Tally)
	for i in 0 upto count 'each'
		cells.push(i)
	end 'each'
end 'fill'

function step(cells TallyArray) returns TallyArray
	fill(cells, count: 2)

	return cells
end 'step'

function main() returns ExitCode
	var first = TallyArray.create()
	var second = TallyArray.create()

	for _ in 0 upto 10 'eachFirst'
		first = step(first)
	end 'eachFirst'

	for _ in 0 upto 20 'eachSecond'
		second = step(second)
	end 'eachSecond'

	print("{first.count()} {second.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
20 40
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

func @step {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 48
  __il_body:
    x64.movRegImm32 rdx, 0
    x64.jmp forhdr
  each:
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegReg rax, rsi
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.storeBaseIndexScaleReg.word64 [rsi + rax*8 + 0], rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  forstep:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr:
    x64.cmpRegImm32 rdx, 2
    x64.jcc below, each
  __il_cont:
    x64.movRegReg r8, rcx
    x64.epilogue 48
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, rcx
    x64.storeSlotReg slot1, rdx
    x64.callDirect __managed_push
    x64.loadRegSlot rcx, slot0
    x64.loadRegSlot rdx, slot1
    x64.jmp forstep
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
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#1
  eachFirst:
    x64.movRegReg rcx, rbx
    x64.callDirect step
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, -4
    x64.jcc equal, adoptclone#16
  critsplit#17:
    x64.movRegReg r8, rbx
  forstep#3:
    x64.leaRegRegImm32 r13, r13, 1
    x64.movRegReg rbx, r8
  forhdr#1:
    x64.storeSlotReg slot0, rbx
    x64.cmpRegImm32 r13, 10
    x64.jcc less, eachFirst
  forexit#4:
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#5
  eachSecond:
    x64.movRegReg rcx, r12
    x64.callDirect step
    x64.loadRegBaseDisp.word64 rax, [r12 + 16]
    x64.cmpRegImm32 rax, -4
    x64.jcc equal, adoptclone#14
  critsplit#18:
    x64.movRegReg r8, r12
  forstep#7:
    x64.leaRegRegImm32 r13, r13, 1
    x64.movRegReg r12, r8
  forhdr#5:
    x64.storeSlotReg slot1, r12
    x64.cmpRegImm32 r13, 20
    x64.jcc less, eachSecond
  forexit#8:
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot8, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.loadRegBaseDisp.word64 r12, [r12 + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __uint_to_string
    x64.storeSlotReg slot7, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rcx, rbx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, rbx
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r14
    x64.loadRegSlot rax, slot7
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot7
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot5
    x64.loadRegSlot rdx, slot6
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot4
    x64.storeBaseDispReg.word64 [rax + 0], r12
    x64.loadRegSlot rcx, slot3
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __write_stdout
  __il_cont:
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
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  adoptclone#16:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_clone
    x64.jmp forstep#3
  adoptclone#14:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_clone
    x64.jmp forstep#7
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

func @step {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 48
  __il_body:
    x64.movRegImm32 rdx, 0
    x64.jmp forhdr
  each:
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegReg rax, rsi
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.storeBaseIndexScaleReg.word64 [rsi + rax*8 + 0], rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  forstep:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr:
    x64.cmpRegImm32 rdx, 2
    x64.jcc below, each
  __il_cont:
    x64.movRegReg r8, rcx
    x64.epilogue 48
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, rcx
    x64.storeSlotReg slot1, rdx
    x64.callDirect __managed_push
    x64.loadRegSlot rcx, slot0
    x64.loadRegSlot rdx, slot1
    x64.jmp forstep
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
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#1
  eachFirst:
    x64.movRegReg rcx, rbx
    x64.callDirect step
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, -4
    x64.jcc equal, adoptclone#16
  critsplit#17:
    x64.movRegReg r8, rbx
  forstep#3:
    x64.leaRegRegImm32 r13, r13, 1
    x64.movRegReg rbx, r8
  forhdr#1:
    x64.storeSlotReg slot0, rbx
    x64.cmpRegImm32 r13, 10
    x64.jcc less, eachFirst
  forexit#4:
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#5
  eachSecond:
    x64.movRegReg rcx, r12
    x64.callDirect step
    x64.loadRegBaseDisp.word64 rax, [r12 + 16]
    x64.cmpRegImm32 rax, -4
    x64.jcc equal, adoptclone#14
  critsplit#18:
    x64.movRegReg r8, r12
  forstep#7:
    x64.leaRegRegImm32 r13, r13, 1
    x64.movRegReg r12, r8
  forhdr#5:
    x64.storeSlotReg slot1, r12
    x64.cmpRegImm32 r13, 20
    x64.jcc less, eachSecond
  forexit#8:
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot8, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.loadRegBaseDisp.word64 r12, [r12 + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __uint_to_string
    x64.storeSlotReg slot7, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rcx, rbx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, rbx
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r14
    x64.loadRegSlot rax, slot7
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot7
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot5
    x64.loadRegSlot rdx, slot6
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot4
    x64.storeBaseDispReg.word64 [rax + 0], r12
    x64.loadRegSlot rcx, slot3
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __write_stdout
  __il_cont:
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
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  adoptclone#16:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_clone
    x64.jmp forstep#3
  adoptclone#14:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_clone
    x64.jmp forstep#7
}
```
```TargetIr:arm64-macos
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @step {
  entry:
    arm64.prologue 32
  __il_body:
    arm64.movImm x1, 0
    arm64.b forhdr
  each:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x2, x3
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.storeBaseIndexScaleReg.word64 [x3 + x2*8 + 0], x1
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x2
  forstep:
    arm64.add x1, x1, 1
  forhdr:
    arm64.cmp x1, 2
    arm64.b.lo each
  __il_cont:
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.bl __managed_push
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.b forstep
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
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x21, 0
    arm64.b forhdr#1
  eachFirst:
    arm64.movRegReg x0, x19
    arm64.bl step
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, -4
    arm64.b.eq adoptclone#16
  critsplit#17:
    arm64.movRegReg x0, x19
  forstep#3:
    arm64.add x21, x21, 1
    arm64.movRegReg x19, x0
  forhdr#1:
    arm64.storeSlotReg slot0, x19
    arm64.cmp x21, 10
    arm64.b.lt eachFirst
  forexit#4:
    arm64.movImm x21, 0
    arm64.b forhdr#5
  eachSecond:
    arm64.movRegReg x0, x20
    arm64.bl step
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.cmp x0, -4
    arm64.b.eq adoptclone#14
  critsplit#18:
    arm64.movRegReg x0, x20
  forstep#7:
    arm64.add x21, x21, 1
    arm64.movRegReg x20, x0
  forhdr#5:
    arm64.storeSlotReg slot1, x20
    arm64.cmp x21, 20
    arm64.b.lt eachSecond
  forexit#8:
    arm64.loadRegBaseDisp.word64 x19, [x19 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot2, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_2  ; " "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x20, [x20 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_3  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.add x0, x19, x23
    arm64.add x0, x0, x20
    arm64.add x0, x0, x26
    arm64.storeSlotReg slot3, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x28, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x19, x19, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x19, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x0, x19, x26
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.loadRegSlot x0, slot3
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.loadRegSlot x0, slot2
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
    arm64.loadRegSlot x0, slot1
    arm64.bl __managed_decref
  exitdrop:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
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
  adoptclone#16:
    arm64.movRegReg x0, x19
    arm64.bl __managed_clone
    arm64.b forstep#3
  adoptclone#14:
    arm64.movRegReg x0, x20
    arm64.bl __managed_clone
    arm64.b forstep#7
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

func @step {
  entry:
    arm64.prologue 32
  __il_body:
    arm64.movImm x1, 0
    arm64.b forhdr
  each:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x2, x3
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.storeBaseIndexScaleReg.word64 [x3 + x2*8 + 0], x1
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x2
  forstep:
    arm64.add x1, x1, 1
  forhdr:
    arm64.cmp x1, 2
    arm64.b.lo each
  __il_cont:
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.bl __managed_push
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.b forstep
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
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x21, 0
    arm64.b forhdr#1
  eachFirst:
    arm64.movRegReg x0, x19
    arm64.bl step
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, -4
    arm64.b.eq adoptclone#16
  critsplit#17:
    arm64.movRegReg x0, x19
  forstep#3:
    arm64.add x21, x21, 1
    arm64.movRegReg x19, x0
  forhdr#1:
    arm64.storeSlotReg slot0, x19
    arm64.cmp x21, 10
    arm64.b.lt eachFirst
  forexit#4:
    arm64.movImm x21, 0
    arm64.b forhdr#5
  eachSecond:
    arm64.movRegReg x0, x20
    arm64.bl step
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.cmp x0, -4
    arm64.b.eq adoptclone#14
  critsplit#18:
    arm64.movRegReg x0, x20
  forstep#7:
    arm64.add x21, x21, 1
    arm64.movRegReg x20, x0
  forhdr#5:
    arm64.storeSlotReg slot1, x20
    arm64.cmp x21, 20
    arm64.b.lt eachSecond
  forexit#8:
    arm64.loadRegBaseDisp.word64 x19, [x19 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot2, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_2  ; " "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x20, [x20 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_3  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.add x0, x19, x23
    arm64.add x0, x0, x20
    arm64.add x0, x0, x26
    arm64.storeSlotReg slot3, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x28, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x19, x19, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x19, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x0, x19, x26
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.loadRegSlot x0, slot3
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.loadRegSlot x0, slot2
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
    arm64.loadRegSlot x0, slot1
    arm64.bl __managed_decref
  exitdrop:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
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
  adoptclone#16:
    arm64.movRegReg x0, x19
    arm64.bl __managed_clone
    arm64.b forstep#3
  adoptclone#14:
    arm64.movRegReg x0, x20
    arm64.bl __managed_clone
    arm64.b forstep#7
}
```

<!-- test: a-step-called-once-and-spliced-leaves-no-pair -->
`touch` is spliced into its one caller, so the retain its `return` takes and the release the loop owes
the record it overwrites sit in one function: the pin shows neither, and the image test in their place.
Prints `1000`.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function touch(cells TallyArray) returns TallyArray
	try cells.set(0, value: (try cells.get(0) otherwise panic("get")) + 1) otherwise panic("set")

	return cells
end 'touch'

function main() returns ExitCode
	var last = TallyArray.create()
	last.push(0)

	for _ in 0 upto 1000 'each'
		last = touch(last)
	end 'each'

	print("{try last.get(0) otherwise 0}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1000
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
    x64.prologue 88
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, rbx
    x64.jmp forhdr
  __il_body#13:
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rsi, 0
    x64.loadRegBaseDisp.word64 rdi, [rcx + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc belowEqual, __im_slow#19
  __im_load#20:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.loadRegBaseDisp.word64 rsi, [rsi + 0]
  tryok#14:
    x64.leaRegRegImm32 rsi, rsi, 1
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, __im_slow#22
  __im_buffer:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, __im_slow#22
  __im_viewed:
    x64.leaRegRegImm32 r8, rdi, -16
    x64.loadRegBaseDisp.word64 r8, [r8 + 8]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, __im_slow#22
  __im_store:
    x64.storeBaseDispReg.word64 [rdi + 0], rsi
  tryok#16:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 16]
    x64.cmpRegImm32 rdx, -4
    x64.jcc equal, adoptclone
  critsplit#32:
    x64.movRegReg r8, rcx
  forstep:
    x64.leaRegRegImm32 rax, rax, 1
    x64.movRegReg rcx, r8
  forhdr:
    x64.storeSlotReg slot0, rcx
    x64.cmpRegImm32 rax, 1000
    x64.jcc less, __il_body#13
  forexit:
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#10
    x64.jmp __im_load#11
  tryerr#6:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  __im_load#11:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  tryok#5:
    x64.movRegReg rbx, rax
  trycont:
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
  __il_body#28:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#19:
    x64.storeSlotReg slot4, rax
    x64.storeSlotReg slot5, rcx
    x64.storeSlotReg slot6, rdx
    x64.movRegReg rdx, rsi
    x64.callDirect __managed_get
    x64.loadRegSlot rax, slot4
    x64.loadRegSlot rcx, slot5
    x64.loadRegSlot rdx, slot6
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#15
  critsplit#33:
    x64.movRegReg rsi, r8
    x64.jmp tryok#14
  __im_slow#22:
    x64.storeSlotReg slot4, rax
    x64.storeSlotReg slot5, rcx
    x64.movRegReg rax, rsi
    x64.callDirect __managed_set
    x64.loadRegSlot rax, slot4
    x64.loadRegSlot rcx, slot5
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#17
    x64.jmp tryok#16
  adoptclone:
    x64.storeSlotReg slot4, rax
    x64.callDirect __managed_clone
    x64.loadRegSlot rax, slot4
    x64.jmp forstep
  __im_slow#10:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#6
  critsplit#31:
    x64.movRegReg rax, r8
    x64.jmp tryok#5
  tryerr#15:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-step-called-once-and-spliced-leaves-no-pair.test:6: get\x0a"
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
  tryerr#17:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at a-step-called-once-and-spliced-leaves-no-pair.test:6: set\x0a"
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
    x64.prologue 88
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, rbx
    x64.jmp forhdr
  __il_body#13:
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rsi, 0
    x64.loadRegBaseDisp.word64 rdi, [rcx + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc belowEqual, __im_slow#19
  __im_load#20:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.loadRegBaseDisp.word64 rsi, [rsi + 0]
  tryok#14:
    x64.leaRegRegImm32 rsi, rsi, 1
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, __im_slow#22
  __im_buffer:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, __im_slow#22
  __im_viewed:
    x64.leaRegRegImm32 r8, rdi, -16
    x64.loadRegBaseDisp.word64 r8, [r8 + 8]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, __im_slow#22
  __im_store:
    x64.storeBaseDispReg.word64 [rdi + 0], rsi
  tryok#16:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 16]
    x64.cmpRegImm32 rdx, -4
    x64.jcc equal, adoptclone
  critsplit#32:
    x64.movRegReg r8, rcx
  forstep:
    x64.leaRegRegImm32 rax, rax, 1
    x64.movRegReg rcx, r8
  forhdr:
    x64.storeSlotReg slot0, rcx
    x64.cmpRegImm32 rax, 1000
    x64.jcc less, __il_body#13
  forexit:
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#10
    x64.jmp __im_load#11
  tryerr#6:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  __im_load#11:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  tryok#5:
    x64.movRegReg rbx, rax
  trycont:
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
  __il_body#28:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#19:
    x64.storeSlotReg slot4, rax
    x64.storeSlotReg slot5, rcx
    x64.storeSlotReg slot6, rdx
    x64.movRegReg rdx, rsi
    x64.callDirect __managed_get
    x64.loadRegSlot rax, slot4
    x64.loadRegSlot rcx, slot5
    x64.loadRegSlot rdx, slot6
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#15
  critsplit#33:
    x64.movRegReg rsi, r8
    x64.jmp tryok#14
  __im_slow#22:
    x64.storeSlotReg slot4, rax
    x64.storeSlotReg slot5, rcx
    x64.movRegReg rax, rsi
    x64.callDirect __managed_set
    x64.loadRegSlot rax, slot4
    x64.loadRegSlot rcx, slot5
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#17
    x64.jmp tryok#16
  adoptclone:
    x64.storeSlotReg slot4, rax
    x64.callDirect __managed_clone
    x64.loadRegSlot rax, slot4
    x64.jmp forstep
  __im_slow#10:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#6
  critsplit#31:
    x64.movRegReg rax, r8
    x64.jmp tryok#5
  tryerr#15:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-step-called-once-and-spliced-leaves-no-pair.test:6: get\x0a"
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
  tryerr#17:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at a-step-called-once-and-spliced-leaves-no-pair.test:6: set\x0a"
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
    arm64.storeSlotReg slot9, x26
    arm64.storeSlotReg slot8, x25
    arm64.storeSlotReg slot7, x24
    arm64.storeSlotReg slot6, x23
    arm64.storeSlotReg slot5, x22
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x0, 0
    arm64.b forhdr
  __il_body#13:
    arm64.movImm x1, 0
    arm64.movImm x2, 0
    arm64.loadRegBaseDisp.word64 x3, [x19 + 8]
    arm64.cmp x3, 0
    arm64.b.ls __im_slow#19
  __im_load#20:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
  tryok#14:
    arm64.add x2, x2, 1
    arm64.loadRegBaseDisp.word64 x3, [x19 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow#22
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow#22
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow#22
  __im_store:
    arm64.storeBaseDispReg.word64 [x3 + 0], x2
  tryok#16:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 16]
    arm64.cmp x1, -4
    arm64.b.eq adoptclone
  critsplit:
    arm64.movRegReg x1, x19
  forstep:
    arm64.add x0, x0, 1
    arm64.movRegReg x19, x1
  forhdr:
    arm64.cmp x0, 1000
    arm64.b.lt __il_body#13
  forexit:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow#10
    arm64.b __im_load#11
  tryerr#6:
    arm64.movImm x20, 0
    arm64.b trycont
  __im_load#11:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  tryok#5:
    arm64.movRegReg x20, x0
  trycont:
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_3  ; "\x0a"
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
  __il_body#28:
    arm64.movRegReg x0, x25
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.loadRegSlot x23, slot6
    arm64.loadRegSlot x24, slot7
    arm64.loadRegSlot x25, slot8
    arm64.loadRegSlot x26, slot9
    arm64.epilogue 96
    arm64.ret
  __im_slow#19:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x2
    arm64.bl __managed_get
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#15
    arm64.b tryok#14
  __im_slow#22:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_set
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#17
    arm64.b tryok#16
  adoptclone:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_clone
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b forstep
  __im_slow#10:
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#6
    arm64.b tryok#5
  tryerr#15:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-step-called-once-and-spliced-leaves-no-pair.test:6: get\x0a"
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
    arm64.epilogue 96
    arm64.ret
  tryerr#17:
    arm64.leaRdata x0, __str_blob_2  ; "panic at a-step-called-once-and-spliced-leaves-no-pair.test:6: set\x0a"
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
    arm64.epilogue 96
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
    arm64.prologue 96
    arm64.storeSlotReg slot9, x26
    arm64.storeSlotReg slot8, x25
    arm64.storeSlotReg slot7, x24
    arm64.storeSlotReg slot6, x23
    arm64.storeSlotReg slot5, x22
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x0, 0
    arm64.b forhdr
  __il_body#13:
    arm64.movImm x1, 0
    arm64.movImm x2, 0
    arm64.loadRegBaseDisp.word64 x3, [x19 + 8]
    arm64.cmp x3, 0
    arm64.b.ls __im_slow#19
  __im_load#20:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
  tryok#14:
    arm64.add x2, x2, 1
    arm64.loadRegBaseDisp.word64 x3, [x19 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow#22
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow#22
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow#22
  __im_store:
    arm64.storeBaseDispReg.word64 [x3 + 0], x2
  tryok#16:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 16]
    arm64.cmp x1, -4
    arm64.b.eq adoptclone
  critsplit:
    arm64.movRegReg x1, x19
  forstep:
    arm64.add x0, x0, 1
    arm64.movRegReg x19, x1
  forhdr:
    arm64.cmp x0, 1000
    arm64.b.lt __il_body#13
  forexit:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow#10
    arm64.b __im_load#11
  tryerr#6:
    arm64.movImm x20, 0
    arm64.b trycont
  __im_load#11:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  tryok#5:
    arm64.movRegReg x20, x0
  trycont:
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_3  ; "\x0a"
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
  __il_body#28:
    arm64.movRegReg x0, x25
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.loadRegSlot x23, slot6
    arm64.loadRegSlot x24, slot7
    arm64.loadRegSlot x25, slot8
    arm64.loadRegSlot x26, slot9
    arm64.epilogue 96
    arm64.ret
  __im_slow#19:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x2
    arm64.bl __managed_get
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.cmp x9, 0
    arm64.b.ne tryerr#15
    arm64.b tryok#14
  __im_slow#22:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_set
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#17
    arm64.b tryok#16
  adoptclone:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_clone
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b forstep
  __im_slow#10:
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#6
    arm64.b tryok#5
  tryerr#15:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-step-called-once-and-spliced-leaves-no-pair.test:6: get\x0a"
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
    arm64.epilogue 96
    arm64.ret
  tryerr#17:
    arm64.leaRdata x0, __str_blob_2  ; "panic at a-step-called-once-and-spliced-leaves-no-pair.test:6: set\x0a"
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
    arm64.epilogue 96
    arm64.ret
}
```

<!-- test: a-match-merge-skips-the-release-on-the-arms-that-return-the-parameter -->
`pick` returns `keepCells(cells)` on two arms and a fresh record on the third, and the loop rebinds its
`var` to the merged result. The arms that return the parameter branch straight past the release of the old
record; the fresh arm reaches it. Prints `5`.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

enum Move
	keep
	fresh
	also
end 'Move'

function fill(cells TallyArray, count Tally)
	for i in 0 upto count 'each'
		cells.push(i)
	end 'each'
end 'fill'

function keepCells(cells TallyArray) returns TallyArray
	fill(cells, count: 2)

	return cells
end 'keepCells'

function freshCells() returns TallyArray
	var cells = TallyArray.create()
	fill(cells, count: 3)

	return cells
end 'freshCells'

function pick(move Move, cells TallyArray) returns TallyArray
	return match move 'arm'
		keep gives keepCells(cells)
		fresh gives freshCells()
		also gives keepCells(cells)
	end 'arm'
end 'pick'

function main() returns ExitCode
	var last = TallyArray.create()

	for move in Move.allCases 'each'
		last = pick(move, cells: last)
	end 'each'

	print("{last.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
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

func @fill {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.movRegImm32 rax, 0
    x64.jmp forhdr
  each:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegReg rsi, rdi
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 r8, rdi, -16
    x64.loadRegBaseDisp.word64 r8, [r8 + 8]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.storeBaseIndexScaleReg.word64 [rdi + rsi*8 + 0], rax
    x64.leaRegRegImm32 rsi, rsi, 1
    x64.storeBaseDispReg.word64 [rcx + 8], rsi
  forstep:
    x64.leaRegRegImm32 rax, rax, 1
  forhdr:
    x64.cmpRegReg rax, rdx
    x64.jcc below, each
  forexit:
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.movRegReg rdx, rax
    x64.callDirect __managed_push
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.jmp forstep
}

func @keepCells {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegImm32 rdx, 2
    x64.movRegReg rcx, rbx
    x64.callDirect fill
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @freshCells {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 3
    x64.movRegReg rcx, rbx
    x64.callDirect fill
    x64.movRegReg r8, rbx
    x64.epilogue 40
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 72
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegImm32 rdx, 0
    x64.movRegReg rcx, r12
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, r12
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 2
    x64.movRegReg rcx, r12
    x64.callDirect __managed_push
    x64.movRegImm32 r13, 0
    x64.jmp forhdr
  each:
    x64.loadRegBaseDisp.word64 rax, [r12 + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rax + r13*8 + 0]
    x64.jmp __il_body#14
  __im_stride:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.leaRegRegReg rax, rax, r13
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp __il_body#14
  __im_slow:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __managed_get_unchecked
    x64.movRegReg rax, r8
  __il_body#14:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, matchnext#19
  matcharm#15:
    x64.movRegReg rcx, rbx
    x64.callDirect keepCells
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, -4
    x64.jcc equal, adoptclone#25
  critsplit#26:
    x64.movRegReg r8, rbx
    x64.jmp forstep
  matchnext#19:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, matchnext#20
  matcharm#16:
    x64.callDirect freshCells
    x64.movRegReg r14, r8
  matchcont:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r14
    x64.jmp forstep
  matchnext#20:
    x64.cmpRegImm32 rax, 2
    x64.jcc notEqual, matchdead
  matcharm#17:
    x64.movRegReg rcx, rbx
    x64.callDirect keepCells
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, -4
    x64.jcc equal, adoptclone#23
  critsplit#27:
    x64.movRegReg r8, rbx
  forstep:
    x64.leaRegRegImm32 r13, r13, 1
    x64.movRegReg rbx, r8
  forhdr:
    x64.storeSlotReg slot0, rbx
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegReg r13, rax
    x64.jcc less, each
    x64.jmp forexit
  matchdead:
  forexit:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "\x0a"
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
  __il_body#12:
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
  adoptclone#25:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_clone
    x64.jmp forstep
  adoptclone#23:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_clone
    x64.jmp forstep
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

func @fill {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.movRegImm32 rax, 0
    x64.jmp forhdr
  each:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 8]
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegReg rsi, rdi
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 r8, rdi, -16
    x64.loadRegBaseDisp.word64 r8, [r8 + 8]
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.storeBaseIndexScaleReg.word64 [rdi + rsi*8 + 0], rax
    x64.leaRegRegImm32 rsi, rsi, 1
    x64.storeBaseDispReg.word64 [rcx + 8], rsi
  forstep:
    x64.leaRegRegImm32 rax, rax, 1
  forhdr:
    x64.cmpRegReg rax, rdx
    x64.jcc below, each
  forexit:
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.movRegReg rdx, rax
    x64.callDirect __managed_push
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.jmp forstep
}

func @keepCells {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegImm32 rdx, 2
    x64.movRegReg rcx, rbx
    x64.callDirect fill
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @freshCells {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 3
    x64.movRegReg rcx, rbx
    x64.callDirect fill
    x64.movRegReg r8, rbx
    x64.epilogue 40
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 72
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegImm32 rdx, 0
    x64.movRegReg rcx, r12
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, r12
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 2
    x64.movRegReg rcx, r12
    x64.callDirect __managed_push
    x64.movRegImm32 r13, 0
    x64.jmp forhdr
  each:
    x64.loadRegBaseDisp.word64 rax, [r12 + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rax + r13*8 + 0]
    x64.jmp __il_body#14
  __im_stride:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.leaRegRegReg rax, rax, r13
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp __il_body#14
  __im_slow:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __managed_get_unchecked
    x64.movRegReg rax, r8
  __il_body#14:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, matchnext#19
  matcharm#15:
    x64.movRegReg rcx, rbx
    x64.callDirect keepCells
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, -4
    x64.jcc equal, adoptclone#25
  critsplit#26:
    x64.movRegReg r8, rbx
    x64.jmp forstep
  matchnext#19:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, matchnext#20
  matcharm#16:
    x64.callDirect freshCells
    x64.movRegReg r14, r8
  matchcont:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r14
    x64.jmp forstep
  matchnext#20:
    x64.cmpRegImm32 rax, 2
    x64.jcc notEqual, matchdead
  matcharm#17:
    x64.movRegReg rcx, rbx
    x64.callDirect keepCells
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, -4
    x64.jcc equal, adoptclone#23
  critsplit#27:
    x64.movRegReg r8, rbx
  forstep:
    x64.leaRegRegImm32 r13, r13, 1
    x64.movRegReg rbx, r8
  forhdr:
    x64.storeSlotReg slot0, rbx
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegReg r13, rax
    x64.jcc less, each
    x64.jmp forexit
  matchdead:
  forexit:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "\x0a"
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
  __il_body#12:
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
  adoptclone#25:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_clone
    x64.jmp forstep
  adoptclone#23:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_clone
    x64.jmp forstep
}
```
```TargetIr:arm64-macos
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @fill {
  entry:
    arm64.prologue 48
    arm64.movImm x2, 0
    arm64.b forhdr
  each:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x3, x4
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.cmp x4, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x5, x4, 16
    arm64.loadRegBaseDisp.word64 x5, [x5 + 8]
    arm64.cmp x5, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.storeBaseIndexScaleReg.word64 [x4 + x3*8 + 0], x2
    arm64.add x3, x3, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x3
  forstep:
    arm64.add x2, x2, 1
  forhdr:
    arm64.cmp x2, x1
    arm64.b.lo each
  forexit:
    arm64.epilogue 48
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.movRegReg x1, x2
    arm64.bl __managed_push
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.b forstep
}

func @keepCells {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movImm x1, 2
    arm64.movRegReg x0, x19
    arm64.bl fill
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @freshCells {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 3
    arm64.movRegReg x0, x19
    arm64.bl fill
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
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
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x1, 0
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
    arm64.movImm x1, 1
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
    arm64.movImm x1, 2
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
    arm64.movImm x21, 0
    arm64.b forhdr
  each:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x21*8 + 0]
    arm64.b __il_body#14
  __im_stride:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.add x0, x0, x21
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b __il_body#14
  __im_slow:
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.bl __managed_get_unchecked
  __il_body#14:
    arm64.cmp x0, 0
    arm64.b.ne matchnext#19
  matcharm#15:
    arm64.movRegReg x0, x19
    arm64.bl keepCells
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, -4
    arm64.b.eq adoptclone#25
  critsplit#26:
    arm64.movRegReg x0, x19
    arm64.b forstep
  matchnext#19:
    arm64.cmp x0, 1
    arm64.b.ne matchnext#20
  matcharm#16:
    arm64.bl freshCells
    arm64.movRegReg x22, x0
  matchcont:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x22
    arm64.b forstep
  matchnext#20:
    arm64.cmp x0, 2
    arm64.b.ne matchdead
  matcharm#17:
    arm64.movRegReg x0, x19
    arm64.bl keepCells
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, -4
    arm64.b.eq adoptclone#23
  critsplit#27:
    arm64.movRegReg x0, x19
  forstep:
    arm64.add x21, x21, 1
    arm64.movRegReg x19, x0
  forhdr:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x21, x0
    arm64.b.lt each
    arm64.b forexit
  matchdead:
  forexit:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_1  ; "\x0a"
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
  __il_body#12:
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
  adoptclone#25:
    arm64.movRegReg x0, x19
    arm64.bl __managed_clone
    arm64.b forstep
  adoptclone#23:
    arm64.movRegReg x0, x19
    arm64.bl __managed_clone
    arm64.b forstep
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

func @fill {
  entry:
    arm64.prologue 48
    arm64.movImm x2, 0
    arm64.b forhdr
  each:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x3, x4
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 0]
    arm64.cmp x4, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x5, x4, 16
    arm64.loadRegBaseDisp.word64 x5, [x5 + 8]
    arm64.cmp x5, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.storeBaseIndexScaleReg.word64 [x4 + x3*8 + 0], x2
    arm64.add x3, x3, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x3
  forstep:
    arm64.add x2, x2, 1
  forhdr:
    arm64.cmp x2, x1
    arm64.b.lo each
  forexit:
    arm64.epilogue 48
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.movRegReg x1, x2
    arm64.bl __managed_push
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.b forstep
}

func @keepCells {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movImm x1, 2
    arm64.movRegReg x0, x19
    arm64.bl fill
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @freshCells {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 3
    arm64.movRegReg x0, x19
    arm64.bl fill
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
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
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x1, 0
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
    arm64.movImm x1, 1
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
    arm64.movImm x1, 2
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
    arm64.movImm x21, 0
    arm64.b forhdr
  each:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x21*8 + 0]
    arm64.b __il_body#14
  __im_stride:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.add x0, x0, x21
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b __il_body#14
  __im_slow:
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.bl __managed_get_unchecked
  __il_body#14:
    arm64.cmp x0, 0
    arm64.b.ne matchnext#19
  matcharm#15:
    arm64.movRegReg x0, x19
    arm64.bl keepCells
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, -4
    arm64.b.eq adoptclone#25
  critsplit#26:
    arm64.movRegReg x0, x19
    arm64.b forstep
  matchnext#19:
    arm64.cmp x0, 1
    arm64.b.ne matchnext#20
  matcharm#16:
    arm64.bl freshCells
    arm64.movRegReg x22, x0
  matchcont:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x22
    arm64.b forstep
  matchnext#20:
    arm64.cmp x0, 2
    arm64.b.ne matchdead
  matcharm#17:
    arm64.movRegReg x0, x19
    arm64.bl keepCells
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, -4
    arm64.b.eq adoptclone#23
  critsplit#27:
    arm64.movRegReg x0, x19
  forstep:
    arm64.add x21, x21, 1
    arm64.movRegReg x19, x0
  forhdr:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x21, x0
    arm64.b.lt each
    arm64.b forexit
  matchdead:
  forexit:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_1  ; "\x0a"
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
  __il_body#12:
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
  adoptclone#25:
    arm64.movRegReg x0, x19
    arm64.bl __managed_clone
    arm64.b forstep
  adoptclone#23:
    arm64.movRegReg x0, x19
    arm64.bl __managed_clone
    arm64.b forstep
}
```

<!-- test: a-step-reached-as-a-function-value-keeps-its-counted-return -->
`step` is also passed as a function value, so a caller that cannot see its body expects a counted
reference. It keeps returning one: the pin shows the `__managed_retain` in `step` and the pair in the loop.
Prints `3 3`.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally
typealias Step = function(TallyArray) returns TallyArray

function fill(cells TallyArray, count Tally)
	for i in 0 upto count 'each'
		cells.push(i)
	end 'each'
end 'fill'

function step(cells TallyArray) returns TallyArray
	fill(cells, count: 1)

	return cells
end 'step'

function apply(run Step, cells TallyArray) returns TallyArray
	return run(cells)
end 'apply'

function main() returns ExitCode
	var first = TallyArray.create()
	var second = TallyArray.create()

	for _ in 0 upto 3 'eachFirst'
		first = step(first)
	end 'eachFirst'

	for _ in 0 upto 3 'eachSecond'
		second = apply(step, cells: second)
	end 'eachSecond'

	print("{first.count()} {second.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 3
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

func @step {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 48
  __il_body:
    x64.movRegImm32 rdx, 0
    x64.jmp forhdr
  each:
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegReg rax, rsi
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.storeBaseIndexScaleReg.word64 [rsi + rax*8 + 0], rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  forstep:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr:
    x64.cmpRegImm32 rdx, 1
    x64.jcc below, each
  __il_cont:
    x64.callDirect __managed_retain
    x64.epilogue 48
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, rcx
    x64.storeSlotReg slot1, rdx
    x64.callDirect __managed_push
    x64.loadRegSlot rcx, slot0
    x64.loadRegSlot rdx, slot1
    x64.jmp forstep
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
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#1
  eachFirst:
    x64.movRegReg rcx, rbx
    x64.callDirect step
    x64.movRegReg r14, r8
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  forstep#3:
    x64.leaRegRegImm32 r13, r13, 1
    x64.movRegReg rbx, r14
  forhdr#1:
    x64.storeSlotReg slot0, rbx
    x64.cmpRegImm32 r13, 3
    x64.jcc less, eachFirst
  forexit#4:
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#5
  eachSecond:
    x64.leaRegRdata rdx, [rip + __fnrec_step]
  __il_body#12:
    x64.loadRegBaseDisp.word64 rax, [rdx + 0]
    x64.movRegReg rcx, r12
    x64.callReg rax
    x64.movRegReg r14, r8
  __il_cont#11:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  forstep#7:
    x64.leaRegRegImm32 r13, r13, 1
    x64.movRegReg r12, r14
  forhdr#5:
    x64.storeSlotReg slot1, r12
    x64.cmpRegImm32 r13, 3
    x64.jcc less, eachSecond
  forexit#8:
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot8, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.loadRegBaseDisp.word64 r12, [r12 + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __uint_to_string
    x64.storeSlotReg slot7, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rcx, rbx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, rbx
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r14
    x64.loadRegSlot rax, slot7
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot7
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot5
    x64.loadRegSlot rdx, slot6
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot4
    x64.storeBaseDispReg.word64 [rax + 0], r12
    x64.loadRegSlot rcx, slot3
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
  __il_body#14:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __write_stdout
  __il_cont#13:
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
    x64.epilogue 104
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

func @step {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 48
  __il_body:
    x64.movRegImm32 rdx, 0
    x64.jmp forhdr
  each:
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegReg rax, rsi
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.storeBaseIndexScaleReg.word64 [rsi + rax*8 + 0], rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  forstep:
    x64.leaRegRegImm32 rdx, rdx, 1
  forhdr:
    x64.cmpRegImm32 rdx, 1
    x64.jcc below, each
  __il_cont:
    x64.callDirect __managed_retain
    x64.epilogue 48
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, rcx
    x64.storeSlotReg slot1, rdx
    x64.callDirect __managed_push
    x64.loadRegSlot rcx, slot0
    x64.loadRegSlot rdx, slot1
    x64.jmp forstep
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
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#1
  eachFirst:
    x64.movRegReg rcx, rbx
    x64.callDirect step
    x64.movRegReg r14, r8
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  forstep#3:
    x64.leaRegRegImm32 r13, r13, 1
    x64.movRegReg rbx, r14
  forhdr#1:
    x64.storeSlotReg slot0, rbx
    x64.cmpRegImm32 r13, 3
    x64.jcc less, eachFirst
  forexit#4:
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#5
  eachSecond:
    x64.leaRegRdata rdx, [rip + __fnrec_step]
  __il_body#12:
    x64.loadRegBaseDisp.word64 rax, [rdx + 0]
    x64.movRegReg rcx, r12
    x64.callReg rax
    x64.movRegReg r14, r8
  __il_cont#11:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  forstep#7:
    x64.leaRegRegImm32 r13, r13, 1
    x64.movRegReg r12, r14
  forhdr#5:
    x64.storeSlotReg slot1, r12
    x64.cmpRegImm32 r13, 3
    x64.jcc less, eachSecond
  forexit#8:
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot8, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.loadRegBaseDisp.word64 r12, [r12 + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __uint_to_string
    x64.storeSlotReg slot7, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rcx, rbx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, rbx
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r14
    x64.loadRegSlot rax, slot7
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot7
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot5
    x64.loadRegSlot rdx, slot6
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot4
    x64.storeBaseDispReg.word64 [rax + 0], r12
    x64.loadRegSlot rcx, slot3
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
  __il_body#14:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __write_stdout
  __il_cont#13:
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
    x64.epilogue 104
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

func @step {
  entry:
    arm64.prologue 32
  __il_body:
    arm64.movImm x1, 0
    arm64.b forhdr
  each:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x2, x3
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.storeBaseIndexScaleReg.word64 [x3 + x2*8 + 0], x1
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x2
  forstep:
    arm64.add x1, x1, 1
  forhdr:
    arm64.cmp x1, 1
    arm64.b.lo each
  __il_cont:
    arm64.bl __managed_retain
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.bl __managed_push
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.b forstep
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
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x21, 0
    arm64.b forhdr#1
  eachFirst:
    arm64.movRegReg x0, x19
    arm64.bl step
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  forstep#3:
    arm64.add x21, x21, 1
    arm64.movRegReg x19, x22
  forhdr#1:
    arm64.storeSlotReg slot0, x19
    arm64.cmp x21, 3
    arm64.b.lt eachFirst
  forexit#4:
    arm64.movImm x21, 0
    arm64.b forhdr#5
  eachSecond:
    arm64.leaRdata x1, __fnrec_step
  __il_body#12:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.movRegReg x0, x20
    arm64.blr x2
    arm64.movRegReg x22, x0
  __il_cont#11:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  forstep#7:
    arm64.add x21, x21, 1
    arm64.movRegReg x20, x22
  forhdr#5:
    arm64.storeSlotReg slot1, x20
    arm64.cmp x21, 3
    arm64.b.lt eachSecond
  forexit#8:
    arm64.loadRegBaseDisp.word64 x19, [x19 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot2, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_2  ; " "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x20, [x20 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_3  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.add x0, x19, x23
    arm64.add x0, x0, x20
    arm64.add x0, x0, x26
    arm64.storeSlotReg slot3, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x28, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x19, x19, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x19, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x0, x19, x26
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.loadRegSlot x0, slot3
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.loadRegSlot x0, slot2
    arm64.bl __mm_decref
    arm64.movRegReg x0, x24
    arm64.bl __mm_decref
  __il_body#14:
    arm64.movRegReg x0, x27
    arm64.bl __write_stdout
  __il_cont#13:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.loadRegSlot x0, slot1
    arm64.bl __managed_decref
  exitdrop:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
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

func @step {
  entry:
    arm64.prologue 32
  __il_body:
    arm64.movImm x1, 0
    arm64.b forhdr
  each:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x2, x3
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.storeBaseIndexScaleReg.word64 [x3 + x2*8 + 0], x1
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x2
  forstep:
    arm64.add x1, x1, 1
  forhdr:
    arm64.cmp x1, 1
    arm64.b.lo each
  __il_cont:
    arm64.bl __managed_retain
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.bl __managed_push
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.b forstep
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
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x21, 0
    arm64.b forhdr#1
  eachFirst:
    arm64.movRegReg x0, x19
    arm64.bl step
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  forstep#3:
    arm64.add x21, x21, 1
    arm64.movRegReg x19, x22
  forhdr#1:
    arm64.storeSlotReg slot0, x19
    arm64.cmp x21, 3
    arm64.b.lt eachFirst
  forexit#4:
    arm64.movImm x21, 0
    arm64.b forhdr#5
  eachSecond:
    arm64.leaRdata x1, __fnrec_step
  __il_body#12:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.movRegReg x0, x20
    arm64.blr x2
    arm64.movRegReg x22, x0
  __il_cont#11:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  forstep#7:
    arm64.add x21, x21, 1
    arm64.movRegReg x20, x22
  forhdr#5:
    arm64.storeSlotReg slot1, x20
    arm64.cmp x21, 3
    arm64.b.lt eachSecond
  forexit#8:
    arm64.loadRegBaseDisp.word64 x19, [x19 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot2, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_2  ; " "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x20, [x20 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_3  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.add x0, x19, x23
    arm64.add x0, x0, x20
    arm64.add x0, x0, x26
    arm64.storeSlotReg slot3, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x28, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x19, x19, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x19, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x0, x19, x26
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.loadRegSlot x0, slot3
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.loadRegSlot x0, slot2
    arm64.bl __mm_decref
    arm64.movRegReg x0, x24
    arm64.bl __mm_decref
  __il_body#14:
    arm64.movRegReg x0, x27
    arm64.bl __write_stdout
  __il_cont#13:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.loadRegSlot x0, slot1
    arm64.bl __managed_decref
  exitdrop:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
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
```

<!-- test: a-box-rebound-through-a-called-step-takes-no-reference -->
The same shape over a struct record: `bump` returns its parameter and is called from two loops, and the
pin shows no `__mm_retain` in `bump` and none in either loop, and no `__mm_decref` per iteration. Prints
`20 40`.
```maxon
typealias Tally = int(0 to u64.max)

type Plain
	export var hits as Tally

	static function create() returns Plain
		return Self{hits: 0}
	end 'create'
end 'Plain'

function spin(plain Plain, laps Tally)
	for _ in 0 upto laps 'each'
		plain.hits = plain.hits + 1
	end 'each'
end 'spin'

function bump(plain Plain) returns Plain
	spin(plain, laps: 2)

	return plain
end 'bump'

function main() returns ExitCode
	var first = Plain.create()
	var second = Plain.create()

	for _ in 0 upto 10 'eachFirst'
		first = bump(first)
	end 'eachFirst'

	for _ in 0 upto 20 'eachSecond'
		second = bump(second)
	end 'eachSecond'

	print("{first.hits} {second.hits}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
20 40
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

func @Plain.create {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @bump {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
  __il_body:
    x64.movRegImm32 rax, 0
    x64.jmp forhdr
  each:
    x64.aluBaseDispImm32.add [rcx + 0], 1
  forstep:
    x64.leaRegRegImm32 rax, rax, 1
  forhdr:
    x64.cmpRegImm32 rax, 2
    x64.jcc below, each
  __il_cont:
    x64.movRegReg r8, rcx
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 104
    x64.callDirect Plain.create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.callDirect Plain.create
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#1
  eachFirst:
    x64.movRegReg rcx, rbx
    x64.callDirect bump
  forstep#3:
    x64.leaRegRegImm32 r13, r13, 1
  forhdr#1:
    x64.cmpRegImm32 r13, 10
    x64.jcc less, eachFirst
  forexit#4:
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#5
  eachSecond:
    x64.movRegReg rcx, r12
    x64.callDirect bump
  forstep#7:
    x64.leaRegRegImm32 r13, r13, 1
  forhdr#5:
    x64.cmpRegImm32 r13, 20
    x64.jcc less, eachSecond
  forexit#8:
    x64.loadRegBaseDisp.word64 rbx, [rbx + 0]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot8, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.loadRegBaseDisp.word64 r12, [r12 + 0]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __uint_to_string
    x64.storeSlotReg slot7, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rcx, rbx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, rbx
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r14
    x64.loadRegSlot rax, slot7
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot7
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot5
    x64.loadRegSlot rdx, slot6
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot4
    x64.storeBaseDispReg.word64 [rax + 0], r12
    x64.loadRegSlot rcx, slot3
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot1
    x64.callDirect __mm_decref
  exitdrop:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
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

func @Plain.create {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @bump {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
  __il_body:
    x64.movRegImm32 rax, 0
    x64.jmp forhdr
  each:
    x64.aluBaseDispImm32.add [rcx + 0], 1
  forstep:
    x64.leaRegRegImm32 rax, rax, 1
  forhdr:
    x64.cmpRegImm32 rax, 2
    x64.jcc below, each
  __il_cont:
    x64.movRegReg r8, rcx
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 104
    x64.callDirect Plain.create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.callDirect Plain.create
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#1
  eachFirst:
    x64.movRegReg rcx, rbx
    x64.callDirect bump
  forstep#3:
    x64.leaRegRegImm32 r13, r13, 1
  forhdr#1:
    x64.cmpRegImm32 r13, 10
    x64.jcc less, eachFirst
  forexit#4:
    x64.movRegImm32 r13, 0
    x64.jmp forhdr#5
  eachSecond:
    x64.movRegReg rcx, r12
    x64.callDirect bump
  forstep#7:
    x64.leaRegRegImm32 r13, r13, 1
  forhdr#5:
    x64.cmpRegImm32 r13, 20
    x64.jcc less, eachSecond
  forexit#8:
    x64.loadRegBaseDisp.word64 rbx, [rbx + 0]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot8, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.loadRegBaseDisp.word64 r12, [r12 + 0]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __uint_to_string
    x64.storeSlotReg slot7, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rcx, rbx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, rbx
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r14
    x64.loadRegSlot rax, slot7
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot7
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot5
    x64.loadRegSlot rdx, slot6
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot4
    x64.storeBaseDispReg.word64 [rax + 0], r12
    x64.loadRegSlot rcx, slot3
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot1
    x64.callDirect __mm_decref
  exitdrop:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
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
```
```TargetIr:arm64-macos
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @Plain.create {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @bump {
  entry:
    arm64.prologue 16
  __il_body:
    arm64.movImm x1, 0
    arm64.b forhdr
  each:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x2
  forstep:
    arm64.add x1, x1, 1
  forhdr:
    arm64.cmp x1, 2
    arm64.b.lo each
  __il_cont:
    arm64.epilogue 16
    arm64.ret
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
    arm64.bl Plain.create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.bl Plain.create
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot1, x20
    arm64.movImm x21, 0
    arm64.b forhdr#1
  eachFirst:
    arm64.movRegReg x0, x19
    arm64.bl bump
  forstep#3:
    arm64.add x21, x21, 1
  forhdr#1:
    arm64.cmp x21, 10
    arm64.b.lt eachFirst
  forexit#4:
    arm64.movImm x21, 0
    arm64.b forhdr#5
  eachSecond:
    arm64.movRegReg x0, x20
    arm64.bl bump
  forstep#7:
    arm64.add x21, x21, 1
  forhdr#5:
    arm64.cmp x21, 20
    arm64.b.lt eachSecond
  forexit#8:
    arm64.loadRegBaseDisp.word64 x19, [x19 + 0]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot2, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_2  ; " "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x20, [x20 + 0]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_3  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.add x0, x19, x23
    arm64.add x0, x0, x20
    arm64.add x0, x0, x26
    arm64.storeSlotReg slot3, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x28, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x19, x19, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x19, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x0, x19, x26
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.loadRegSlot x0, slot3
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.loadRegSlot x0, slot2
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
    arm64.loadRegSlot x0, slot1
    arm64.bl __mm_decref
  exitdrop:
    arm64.loadRegSlot x0, slot0
    arm64.bl __mm_decref
  exitterminal:
    arm64.movRegReg x0, x19
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

func @Plain.create {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @bump {
  entry:
    arm64.prologue 16
  __il_body:
    arm64.movImm x1, 0
    arm64.b forhdr
  each:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x2
  forstep:
    arm64.add x1, x1, 1
  forhdr:
    arm64.cmp x1, 2
    arm64.b.lo each
  __il_cont:
    arm64.epilogue 16
    arm64.ret
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
    arm64.bl Plain.create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.bl Plain.create
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot1, x20
    arm64.movImm x21, 0
    arm64.b forhdr#1
  eachFirst:
    arm64.movRegReg x0, x19
    arm64.bl bump
  forstep#3:
    arm64.add x21, x21, 1
  forhdr#1:
    arm64.cmp x21, 10
    arm64.b.lt eachFirst
  forexit#4:
    arm64.movImm x21, 0
    arm64.b forhdr#5
  eachSecond:
    arm64.movRegReg x0, x20
    arm64.bl bump
  forstep#7:
    arm64.add x21, x21, 1
  forhdr#5:
    arm64.cmp x21, 20
    arm64.b.lt eachSecond
  forexit#8:
    arm64.loadRegBaseDisp.word64 x19, [x19 + 0]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot2, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_2  ; " "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x20, [x20 + 0]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_3  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.add x0, x19, x23
    arm64.add x0, x0, x20
    arm64.add x0, x0, x26
    arm64.storeSlotReg slot3, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x28, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x19, x19, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x19, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x0, x19, x26
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.loadRegSlot x0, slot3
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.loadRegSlot x0, slot2
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
    arm64.loadRegSlot x0, slot1
    arm64.bl __mm_decref
  exitdrop:
    arm64.loadRegSlot x0, slot0
    arm64.bl __mm_decref
  exitterminal:
    arm64.movRegReg x0, x19
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
```
