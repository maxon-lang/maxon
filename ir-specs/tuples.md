---
feature: tuples
status: stable
keywords: [tuple, pair, return, registers, value-tuple-return, drop-cascade]
category: types
---

# Tuples: the cases that pin emitted code

The cases of `specs/tuples.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: a-pair-returned-through-an-exit-cascade-stays-in-registers -->
A function that owns a binding leaves through the shared cascade of drop blocks that ends in its one
terminal `ret`, so each `return` does not end in a `ret` of its own: it branches into the cascade
carrying its pair. The pair is still a VALUE. `pair` owns the String `label` and returns on two exits,
one inside an `if`, and both pairs come back in the two return registers, low half and high half, with
no `__mm_alloc` for a two-word record anywhere in `pair`; `main` reads each result's high half out of the
second return register. A heap record in the pin below is the convention failing to see through the
cascade. Prints `n=0`, then `3 1 0 0`.
```maxon
typealias Integer = int(i64.min to i64.max)

function pair(n Integer) returns (Integer, Integer)
	let label = "n={n}"

	if n > 0 'positive'
		return (n, 1)
	end 'positive'

	print("{label}\n")
	return (0, 0)
end 'pair'

function main() returns ExitCode
	let (a, b) = pair(3)
	let (c, d) = pair(0)
	print("{a} {b} {c} {d}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n=0
3 1 0 0
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

func @pair {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 88
    x64.movRegReg rbx, rcx
    x64.storeSlotReg slot1, rbx
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "n="
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __int_to_string
    x64.storeSlotReg slot5, r8
    x64.leaRegRegReg rax, r13, r8
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.leaRegRegImm32 r15, rbx, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r13
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
    x64.loadRegSlot rax, slot1
    x64.cmpRegImm32 rax, 0
    x64.jcc lessEqual, ifcont
  positive:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.loadRegSlot r8, slot1
    x64.movRegReg r10, r12
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.loadRegBaseDisp.word64 r12, [rbx + 0]
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
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
    x64.loadRegSlot rbx, slot4
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 r12, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __str_decref
    x64.movRegReg r8, rbx
    x64.movRegReg r10, r12
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 184
    x64.movRegImm32 rcx, 3
    x64.callDirect pair
    x64.movRegReg rbx, r8
    x64.movRegReg r12, r10
    x64.movRegImm32 rcx, 0
    x64.callDirect pair
    x64.movRegReg r13, r8
    x64.movRegReg r14, r10
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.storeSlotReg slot0, r15
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __int_to_string
    x64.storeSlotReg slot16, r8
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot11, rcx
    x64.loadRegBaseDisp.word64 rbx, [rax + 8]
    x64.storeSlotReg slot15, rbx
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot10, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot14, r8
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot9, rcx
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.storeSlotReg slot13, r12
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot8, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot12, r8
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.storeSlotReg slot3, r13
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot1, r8
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot5, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot17, rax
    x64.loadRegSlot r14, slot16
    x64.leaRegRegReg rax, r14, rbx
    x64.loadRegSlot rbx, slot14
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r12
    x64.loadRegSlot r12, slot12
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r8
    x64.loadRegSlot rcx, slot17
    x64.leaRegRegReg rax, rax, rcx
    x64.storeSlotReg slot6, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot7, r8
    x64.leaRegRegImm32 r13, r8, 56
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r15
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg r14, r13, r14
    x64.loadRegSlot rdx, slot11
    x64.loadRegSlot rax, slot15
    x64.movRegReg rcx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot15
    x64.leaRegRegReg r14, r14, rax
    x64.loadRegSlot rdx, slot10
    x64.movRegReg rcx, r14
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r14, rbx
    x64.loadRegSlot rdx, slot9
    x64.loadRegSlot rax, slot13
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot13
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r12
    x64.loadRegSlot rax, slot3
    x64.loadRegSlot rdx, slot4
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot3
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot rdx, slot2
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot1
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot5
    x64.loadRegSlot rax, slot17
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot17
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rbx, slot7
    x64.storeBaseDispReg.word64 [rbx + 0], r13
    x64.loadRegSlot rax, slot6
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot10
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot8
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
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

func @pair {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 88
    x64.movRegReg rbx, rcx
    x64.storeSlotReg slot1, rbx
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "n="
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __int_to_string
    x64.storeSlotReg slot5, r8
    x64.leaRegRegReg rax, r13, r8
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.leaRegRegImm32 r15, rbx, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r13
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
    x64.loadRegSlot rax, slot1
    x64.cmpRegImm32 rax, 0
    x64.jcc lessEqual, ifcont
  positive:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.loadRegSlot r8, slot1
    x64.movRegReg r10, r12
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.loadRegBaseDisp.word64 r12, [rbx + 0]
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
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
    x64.loadRegSlot rbx, slot4
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 r12, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __str_decref
    x64.movRegReg r8, rbx
    x64.movRegReg r10, r12
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 184
    x64.movRegImm32 rcx, 3
    x64.callDirect pair
    x64.movRegReg rbx, r8
    x64.movRegReg r12, r10
    x64.movRegImm32 rcx, 0
    x64.callDirect pair
    x64.movRegReg r13, r8
    x64.movRegReg r14, r10
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.storeSlotReg slot0, r15
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __int_to_string
    x64.storeSlotReg slot16, r8
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot11, rcx
    x64.loadRegBaseDisp.word64 rbx, [rax + 8]
    x64.storeSlotReg slot15, rbx
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot10, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot14, r8
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot9, rcx
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.storeSlotReg slot13, r12
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot8, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot12, r8
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.storeSlotReg slot3, r13
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot1, r8
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot5, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot17, rax
    x64.loadRegSlot r14, slot16
    x64.leaRegRegReg rax, r14, rbx
    x64.loadRegSlot rbx, slot14
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r12
    x64.loadRegSlot r12, slot12
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r8
    x64.loadRegSlot rcx, slot17
    x64.leaRegRegReg rax, rax, rcx
    x64.storeSlotReg slot6, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot7, r8
    x64.leaRegRegImm32 r13, r8, 56
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r15
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg r14, r13, r14
    x64.loadRegSlot rdx, slot11
    x64.loadRegSlot rax, slot15
    x64.movRegReg rcx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot15
    x64.leaRegRegReg r14, r14, rax
    x64.loadRegSlot rdx, slot10
    x64.movRegReg rcx, r14
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r14, rbx
    x64.loadRegSlot rdx, slot9
    x64.loadRegSlot rax, slot13
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot13
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r12
    x64.loadRegSlot rax, slot3
    x64.loadRegSlot rdx, slot4
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot3
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot rdx, slot2
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot1
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot5
    x64.loadRegSlot rax, slot17
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot17
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rbx, slot7
    x64.storeBaseDispReg.word64 [rbx + 0], r13
    x64.loadRegSlot rax, slot6
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot10
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot8
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
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
```
```TargetIr:arm64-macos
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @pair {
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
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_3  ; "n="
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.add x24, x21, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x20, x26, x21
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
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.cmp x19, 0
    arm64.b.le ifcont
  positive:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.movRegReg x9, x20
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
  ifcont:
    arm64.loadRegBaseDisp.word64 x19, [x25 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x25 + 8]
    arm64.leaRdata x0, __str_rec_4  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.add x23, x20, x22
    arm64.add x0, x23, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.add x26, x24, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x26, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x0, x19, x22
    arm64.storeBaseDispReg.word64 [x24 + 0], x26
    arm64.storeBaseDispReg.word64 [x24 + 8], x23
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x24 + 24], x0
    arm64.movRegReg x0, x24
    arm64.bl print
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movImm x20, 0
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.movRegReg x9, x20
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

func @main {
  entry:
    arm64.prologue 176
    arm64.storeSlotReg slot19, x28
    arm64.storeSlotReg slot18, x27
    arm64.storeSlotReg slot17, x26
    arm64.storeSlotReg slot16, x25
    arm64.storeSlotReg slot15, x24
    arm64.storeSlotReg slot14, x23
    arm64.storeSlotReg slot13, x22
    arm64.storeSlotReg slot12, x21
    arm64.storeSlotReg slot11, x20
    arm64.storeSlotReg slot10, x19
    arm64.movImm x0, 3
    arm64.bl pair
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x9
    arm64.movImm x0, 0
    arm64.bl pair
    arm64.movRegReg x21, x0
    arm64.movRegReg x22, x9
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.storeSlotReg slot0, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_5  ; " "
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot9, x1
    arm64.movRegReg x0, x20
    arm64.bl __int_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_5  ; " "
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot8, x1
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot7, x1
    arm64.movRegReg x0, x21
    arm64.bl __int_to_string
    arm64.movRegReg x21, x0
    arm64.leaRdata x0, __str_rec_5  ; " "
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot6, x1
    arm64.loadRegBaseDisp.word64 x27, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x28, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x28
    arm64.bl __int_to_string
    arm64.storeSlotReg slot5, x0
    arm64.leaRdata x1, __str_rec_4  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.storeSlotReg slot4, x2
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.storeSlotReg slot3, x1
    arm64.add x2, x19, x25
    arm64.add x2, x2, x20
    arm64.add x2, x2, x26
    arm64.add x2, x2, x21
    arm64.add x2, x2, x27
    arm64.add x0, x2, x0
    arm64.add x0, x0, x1
    arm64.storeSlotReg slot1, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot2, x0
    arm64.add x22, x0, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x22, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x19, x19, x25
    arm64.loadRegSlot x1, slot9
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x19, x20
    arm64.loadRegSlot x1, slot8
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x19, x19, x26
    arm64.loadRegSlot x1, slot7
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x19, x19, x21
    arm64.loadRegSlot x1, slot6
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x27
    arm64.bl __str_copy
    arm64.add x19, x19, x27
    arm64.loadRegSlot x2, slot5
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x28
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot5
    arm64.add x19, x19, x0
    arm64.loadRegSlot x2, slot3
    arm64.loadRegSlot x1, slot4
    arm64.movRegReg x0, x19
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot3
    arm64.add x0, x19, x0
    arm64.loadRegSlot x19, slot2
    arm64.storeBaseDispReg.word64 [x19 + 0], x22
    arm64.loadRegSlot x0, slot1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.loadRegSlot x0, slot0
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot9
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot7
    arm64.bl __mm_decref
    arm64.movRegReg x0, x28
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.bl print
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot10
    arm64.loadRegSlot x20, slot11
    arm64.loadRegSlot x21, slot12
    arm64.loadRegSlot x22, slot13
    arm64.loadRegSlot x23, slot14
    arm64.loadRegSlot x24, slot15
    arm64.loadRegSlot x25, slot16
    arm64.loadRegSlot x26, slot17
    arm64.loadRegSlot x27, slot18
    arm64.loadRegSlot x28, slot19
    arm64.epilogue 176
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

func @pair {
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
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_3  ; "n="
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.add x24, x21, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x20, x26, x21
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
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.cmp x19, 0
    arm64.b.le ifcont
  positive:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.movRegReg x9, x20
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
  ifcont:
    arm64.loadRegBaseDisp.word64 x19, [x25 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x25 + 8]
    arm64.leaRdata x0, __str_rec_4  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.add x23, x20, x22
    arm64.add x0, x23, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.add x26, x24, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x26, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x0, x19, x22
    arm64.storeBaseDispReg.word64 [x24 + 0], x26
    arm64.storeBaseDispReg.word64 [x24 + 8], x23
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x24 + 24], x0
    arm64.movRegReg x0, x24
    arm64.bl print
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movImm x20, 0
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.movRegReg x9, x20
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

func @main {
  entry:
    arm64.prologue 176
    arm64.storeSlotReg slot19, x28
    arm64.storeSlotReg slot18, x27
    arm64.storeSlotReg slot17, x26
    arm64.storeSlotReg slot16, x25
    arm64.storeSlotReg slot15, x24
    arm64.storeSlotReg slot14, x23
    arm64.storeSlotReg slot13, x22
    arm64.storeSlotReg slot12, x21
    arm64.storeSlotReg slot11, x20
    arm64.storeSlotReg slot10, x19
    arm64.movImm x0, 3
    arm64.bl pair
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x9
    arm64.movImm x0, 0
    arm64.bl pair
    arm64.movRegReg x21, x0
    arm64.movRegReg x22, x9
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.storeSlotReg slot0, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_5  ; " "
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot9, x1
    arm64.movRegReg x0, x20
    arm64.bl __int_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_5  ; " "
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot8, x1
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot7, x1
    arm64.movRegReg x0, x21
    arm64.bl __int_to_string
    arm64.movRegReg x21, x0
    arm64.leaRdata x0, __str_rec_5  ; " "
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot6, x1
    arm64.loadRegBaseDisp.word64 x27, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x28, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x28
    arm64.bl __int_to_string
    arm64.storeSlotReg slot5, x0
    arm64.leaRdata x1, __str_rec_4  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.storeSlotReg slot4, x2
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.storeSlotReg slot3, x1
    arm64.add x2, x19, x25
    arm64.add x2, x2, x20
    arm64.add x2, x2, x26
    arm64.add x2, x2, x21
    arm64.add x2, x2, x27
    arm64.add x0, x2, x0
    arm64.add x0, x0, x1
    arm64.storeSlotReg slot1, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot2, x0
    arm64.add x22, x0, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x22, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x19, x19, x25
    arm64.loadRegSlot x1, slot9
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x19, x20
    arm64.loadRegSlot x1, slot8
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x19, x19, x26
    arm64.loadRegSlot x1, slot7
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x19, x19, x21
    arm64.loadRegSlot x1, slot6
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x27
    arm64.bl __str_copy
    arm64.add x19, x19, x27
    arm64.loadRegSlot x2, slot5
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x28
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot5
    arm64.add x19, x19, x0
    arm64.loadRegSlot x2, slot3
    arm64.loadRegSlot x1, slot4
    arm64.movRegReg x0, x19
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot3
    arm64.add x0, x19, x0
    arm64.loadRegSlot x19, slot2
    arm64.storeBaseDispReg.word64 [x19 + 0], x22
    arm64.loadRegSlot x0, slot1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.loadRegSlot x0, slot0
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot9
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot7
    arm64.bl __mm_decref
    arm64.movRegReg x0, x28
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.bl print
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot10
    arm64.loadRegSlot x20, slot11
    arm64.loadRegSlot x21, slot12
    arm64.loadRegSlot x22, slot13
    arm64.loadRegSlot x23, slot14
    arm64.loadRegSlot x24, slot15
    arm64.loadRegSlot x25, slot16
    arm64.loadRegSlot x26, slot17
    arm64.loadRegSlot x27, slot18
    arm64.loadRegSlot x28, slot19
    arm64.epilogue 176
    arm64.ret
}
```

<!-- test: a-pair-forwarded-through-an-exit-cascade-stays-in-registers -->
A pair FORWARDED through a cascade stays a value at both ends. `wrap` owns the String `tag` and returns
`pair`'s result on two exits, one inside an `if`, so each exit carries a pair that arrived in the two
return registers into `wrap`'s cascade and hands it on in the same two registers; neither `pair` nor
`wrap` allocates a two-word record, and `main` reads both halves of each `wrap` result out of the two
return registers. `wrap(9)` takes the `if` and forwards `pair(4)`, which is `(4, 1)` and prints nothing;
`wrap(0)` prints `w=0` and forwards `pair(0)`, which prints `n=0` and is `(0, 0)`. Prints `w=0`, `n=0`,
then `4 1 0 0`.
```maxon
typealias Integer = int(i64.min to i64.max)

function pair(n Integer) returns (Integer, Integer)
	let label = "n={n}"

	if n > 0 'positive'
		return (n, 1)
	end 'positive'

	print("{label}\n")
	return (0, 0)
end 'pair'

function wrap(n Integer) returns (Integer, Integer)
	let tag = "w={n}"

	if n > 5 'big'
		return pair(n - 5)
	end 'big'

	print("{tag}\n")
	return pair(n)
end 'wrap'

function main() returns ExitCode
	let (a, b) = wrap(9)
	let (c, d) = wrap(0)
	print("{a} {b} {c} {d}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
w=0
n=0
4 1 0 0
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

func @pair {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 88
    x64.movRegReg rbx, rcx
    x64.storeSlotReg slot1, rbx
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; "n="
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __int_to_string
    x64.storeSlotReg slot5, r8
    x64.leaRegRegReg rax, r13, r8
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.leaRegRegImm32 r15, rbx, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r13
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
    x64.loadRegSlot rax, slot1
    x64.cmpRegImm32 rax, 0
    x64.jcc lessEqual, ifcont
  positive:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.loadRegSlot r8, slot1
    x64.movRegReg r10, r12
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.loadRegBaseDisp.word64 r12, [rbx + 0]
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
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
    x64.loadRegSlot rbx, slot4
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 r12, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __str_decref
    x64.movRegReg r8, rbx
    x64.movRegReg r10, r12
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @wrap {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 88
    x64.movRegReg rbx, rcx
    x64.storeSlotReg slot1, rbx
    x64.leaRegRdata rax, [rip + __str_rec_6]  ; "w="
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot6, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot4, r8
    x64.leaRegRegReg rax, r13, r8
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.storeSlotReg slot0, r14
    x64.leaRegRegImm32 r15, r14, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r13
    x64.loadRegSlot rax, slot4
    x64.loadRegSlot rdx, slot6
    x64.movRegReg rcx, r12
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot4
    x64.leaRegRegReg rax, r12, rax
    x64.storeBaseDispReg.word64 [r14 + 0], r15
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [r14 + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r14 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r14 + 24], rax
    x64.loadRegSlot rcx, slot6
    x64.callDirect __mm_decref
    x64.cmpRegImm32 rbx, 5
    x64.jcc lessEqual, ifcont
  big:
    x64.leaRegRegImm32 rcx, rbx, -5
    x64.callDirect pair
    x64.movRegReg rbx, r8
    x64.movRegReg r12, r10
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
    x64.movRegReg r8, rbx
    x64.movRegReg r10, r12
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.loadRegBaseDisp.word64 rbx, [r14 + 0]
    x64.loadRegBaseDisp.word64 r12, [r14 + 8]
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, r12, r14
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot5, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, rbx, r14
    x64.loadRegSlot rbx, slot5
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.loadRegSlot rcx, slot1
    x64.callDirect pair
    x64.movRegReg rbx, r8
    x64.movRegReg r12, r10
    x64.loadRegSlot rcx, slot0
    x64.callDirect __str_decref
    x64.movRegReg r8, rbx
    x64.movRegReg r10, r12
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 184
    x64.movRegImm32 rcx, 9
    x64.callDirect wrap
    x64.movRegReg rbx, r8
    x64.movRegReg r12, r10
    x64.movRegImm32 rcx, 0
    x64.callDirect wrap
    x64.movRegReg r13, r8
    x64.movRegReg r14, r10
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.storeSlotReg slot0, r15
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __int_to_string
    x64.storeSlotReg slot16, r8
    x64.leaRegRdata rax, [rip + __str_rec_7]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot11, rcx
    x64.loadRegBaseDisp.word64 rbx, [rax + 8]
    x64.storeSlotReg slot15, rbx
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot10, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot14, r8
    x64.leaRegRdata rax, [rip + __str_rec_7]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot9, rcx
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.storeSlotReg slot13, r12
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot8, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot12, r8
    x64.leaRegRdata rax, [rip + __str_rec_7]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.storeSlotReg slot3, r13
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot1, r8
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot5, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot17, rax
    x64.loadRegSlot r14, slot16
    x64.leaRegRegReg rax, r14, rbx
    x64.loadRegSlot rbx, slot14
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r12
    x64.loadRegSlot r12, slot12
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r8
    x64.loadRegSlot rcx, slot17
    x64.leaRegRegReg rax, rax, rcx
    x64.storeSlotReg slot6, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot7, r8
    x64.leaRegRegImm32 r13, r8, 56
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r15
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg r14, r13, r14
    x64.loadRegSlot rdx, slot11
    x64.loadRegSlot rax, slot15
    x64.movRegReg rcx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot15
    x64.leaRegRegReg r14, r14, rax
    x64.loadRegSlot rdx, slot10
    x64.movRegReg rcx, r14
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r14, rbx
    x64.loadRegSlot rdx, slot9
    x64.loadRegSlot rax, slot13
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot13
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r12
    x64.loadRegSlot rax, slot3
    x64.loadRegSlot rdx, slot4
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot3
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot rdx, slot2
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot1
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot5
    x64.loadRegSlot rax, slot17
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot17
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rbx, slot7
    x64.storeBaseDispReg.word64 [rbx + 0], r13
    x64.loadRegSlot rax, slot6
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot10
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot8
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
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

func @pair {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 88
    x64.movRegReg rbx, rcx
    x64.storeSlotReg slot1, rbx
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; "n="
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __int_to_string
    x64.storeSlotReg slot5, r8
    x64.leaRegRegReg rax, r13, r8
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.leaRegRegImm32 r15, rbx, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r13
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
    x64.loadRegSlot rax, slot1
    x64.cmpRegImm32 rax, 0
    x64.jcc lessEqual, ifcont
  positive:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.loadRegSlot r8, slot1
    x64.movRegReg r10, r12
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.loadRegBaseDisp.word64 r12, [rbx + 0]
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
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
    x64.loadRegSlot rbx, slot4
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 r12, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __str_decref
    x64.movRegReg r8, rbx
    x64.movRegReg r10, r12
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @wrap {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 88
    x64.movRegReg rbx, rcx
    x64.storeSlotReg slot1, rbx
    x64.leaRegRdata rax, [rip + __str_rec_6]  ; "w="
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot6, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot4, r8
    x64.leaRegRegReg rax, r13, r8
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.storeSlotReg slot0, r14
    x64.leaRegRegImm32 r15, r14, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r13
    x64.loadRegSlot rax, slot4
    x64.loadRegSlot rdx, slot6
    x64.movRegReg rcx, r12
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot4
    x64.leaRegRegReg rax, r12, rax
    x64.storeBaseDispReg.word64 [r14 + 0], r15
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [r14 + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r14 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r14 + 24], rax
    x64.loadRegSlot rcx, slot6
    x64.callDirect __mm_decref
    x64.cmpRegImm32 rbx, 5
    x64.jcc lessEqual, ifcont
  big:
    x64.leaRegRegImm32 rcx, rbx, -5
    x64.callDirect pair
    x64.movRegReg rbx, r8
    x64.movRegReg r12, r10
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
    x64.movRegReg r8, rbx
    x64.movRegReg r10, r12
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.loadRegBaseDisp.word64 rbx, [r14 + 0]
    x64.loadRegBaseDisp.word64 r12, [r14 + 8]
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, r12, r14
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot5, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, rbx, r14
    x64.loadRegSlot rbx, slot5
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.loadRegSlot rcx, slot1
    x64.callDirect pair
    x64.movRegReg rbx, r8
    x64.movRegReg r12, r10
    x64.loadRegSlot rcx, slot0
    x64.callDirect __str_decref
    x64.movRegReg r8, rbx
    x64.movRegReg r10, r12
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 184
    x64.movRegImm32 rcx, 9
    x64.callDirect wrap
    x64.movRegReg rbx, r8
    x64.movRegReg r12, r10
    x64.movRegImm32 rcx, 0
    x64.callDirect wrap
    x64.movRegReg r13, r8
    x64.movRegReg r14, r10
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.storeSlotReg slot0, r15
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __int_to_string
    x64.storeSlotReg slot16, r8
    x64.leaRegRdata rax, [rip + __str_rec_7]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot11, rcx
    x64.loadRegBaseDisp.word64 rbx, [rax + 8]
    x64.storeSlotReg slot15, rbx
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot10, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot14, r8
    x64.leaRegRdata rax, [rip + __str_rec_7]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot9, rcx
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.storeSlotReg slot13, r12
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot8, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot12, r8
    x64.leaRegRdata rax, [rip + __str_rec_7]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.storeSlotReg slot3, r13
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot1, r8
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot5, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot17, rax
    x64.loadRegSlot r14, slot16
    x64.leaRegRegReg rax, r14, rbx
    x64.loadRegSlot rbx, slot14
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r12
    x64.loadRegSlot r12, slot12
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r8
    x64.loadRegSlot rcx, slot17
    x64.leaRegRegReg rax, rax, rcx
    x64.storeSlotReg slot6, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot7, r8
    x64.leaRegRegImm32 r13, r8, 56
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r15
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg r14, r13, r14
    x64.loadRegSlot rdx, slot11
    x64.loadRegSlot rax, slot15
    x64.movRegReg rcx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot15
    x64.leaRegRegReg r14, r14, rax
    x64.loadRegSlot rdx, slot10
    x64.movRegReg rcx, r14
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r14, rbx
    x64.loadRegSlot rdx, slot9
    x64.loadRegSlot rax, slot13
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot13
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r12
    x64.loadRegSlot rax, slot3
    x64.loadRegSlot rdx, slot4
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot3
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot rdx, slot2
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot1
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot5
    x64.loadRegSlot rax, slot17
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot17
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rbx, slot7
    x64.storeBaseDispReg.word64 [rbx + 0], r13
    x64.loadRegSlot rax, slot6
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot10
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot8
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
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
```
```TargetIr:arm64-macos
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @pair {
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
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_4  ; "n="
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.add x24, x21, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x20, x26, x21
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
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.cmp x19, 0
    arm64.b.le ifcont
  positive:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.movRegReg x9, x20
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
  ifcont:
    arm64.loadRegBaseDisp.word64 x19, [x25 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x25 + 8]
    arm64.leaRdata x0, __str_rec_5  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.add x23, x20, x22
    arm64.add x0, x23, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.add x26, x24, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x26, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x0, x19, x22
    arm64.storeBaseDispReg.word64 [x24 + 0], x26
    arm64.storeBaseDispReg.word64 [x24 + 8], x23
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x24 + 24], x0
    arm64.movRegReg x0, x24
    arm64.bl print
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movImm x20, 0
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.movRegReg x9, x20
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

func @wrap {
  entry:
    arm64.prologue 96
    arm64.storeSlotReg slot8, x27
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_6  ; "w="
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.add x24, x21, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x20, x26, x21
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
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.cmp x19, 5
    arm64.b.le ifcont
  big:
    arm64.sub x0, x19, 5
    arm64.bl pair
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x9
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.movRegReg x9, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.loadRegSlot x27, slot8
    arm64.epilogue 96
    arm64.ret
  ifcont:
    arm64.loadRegBaseDisp.word64 x20, [x25 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x25 + 8]
    arm64.leaRdata x0, __str_rec_5  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.add x24, x21, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x26, x0
    arm64.add x27, x26, 56
    arm64.movRegReg x0, x27
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x20, x27, x21
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x0, x20, x23
    arm64.storeBaseDispReg.word64 [x26 + 0], x27
    arm64.storeBaseDispReg.word64 [x26 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x26 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x26 + 24], x0
    arm64.movRegReg x0, x26
    arm64.bl print
    arm64.movRegReg x0, x26
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.bl pair
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x9
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.movRegReg x9, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.loadRegSlot x27, slot8
    arm64.epilogue 96
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 176
    arm64.storeSlotReg slot19, x28
    arm64.storeSlotReg slot18, x27
    arm64.storeSlotReg slot17, x26
    arm64.storeSlotReg slot16, x25
    arm64.storeSlotReg slot15, x24
    arm64.storeSlotReg slot14, x23
    arm64.storeSlotReg slot13, x22
    arm64.storeSlotReg slot12, x21
    arm64.storeSlotReg slot11, x20
    arm64.storeSlotReg slot10, x19
    arm64.movImm x0, 9
    arm64.bl wrap
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x9
    arm64.movImm x0, 0
    arm64.bl wrap
    arm64.movRegReg x21, x0
    arm64.movRegReg x22, x9
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.storeSlotReg slot0, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_7  ; " "
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot9, x1
    arm64.movRegReg x0, x20
    arm64.bl __int_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_7  ; " "
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot8, x1
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot7, x1
    arm64.movRegReg x0, x21
    arm64.bl __int_to_string
    arm64.movRegReg x21, x0
    arm64.leaRdata x0, __str_rec_7  ; " "
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot6, x1
    arm64.loadRegBaseDisp.word64 x27, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x28, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x28
    arm64.bl __int_to_string
    arm64.storeSlotReg slot5, x0
    arm64.leaRdata x1, __str_rec_5  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.storeSlotReg slot4, x2
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.storeSlotReg slot3, x1
    arm64.add x2, x19, x25
    arm64.add x2, x2, x20
    arm64.add x2, x2, x26
    arm64.add x2, x2, x21
    arm64.add x2, x2, x27
    arm64.add x0, x2, x0
    arm64.add x0, x0, x1
    arm64.storeSlotReg slot1, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot2, x0
    arm64.add x22, x0, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x22, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x19, x19, x25
    arm64.loadRegSlot x1, slot9
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x19, x20
    arm64.loadRegSlot x1, slot8
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x19, x19, x26
    arm64.loadRegSlot x1, slot7
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x19, x19, x21
    arm64.loadRegSlot x1, slot6
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x27
    arm64.bl __str_copy
    arm64.add x19, x19, x27
    arm64.loadRegSlot x2, slot5
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x28
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot5
    arm64.add x19, x19, x0
    arm64.loadRegSlot x2, slot3
    arm64.loadRegSlot x1, slot4
    arm64.movRegReg x0, x19
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot3
    arm64.add x0, x19, x0
    arm64.loadRegSlot x19, slot2
    arm64.storeBaseDispReg.word64 [x19 + 0], x22
    arm64.loadRegSlot x0, slot1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.loadRegSlot x0, slot0
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot9
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot7
    arm64.bl __mm_decref
    arm64.movRegReg x0, x28
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.bl print
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot10
    arm64.loadRegSlot x20, slot11
    arm64.loadRegSlot x21, slot12
    arm64.loadRegSlot x22, slot13
    arm64.loadRegSlot x23, slot14
    arm64.loadRegSlot x24, slot15
    arm64.loadRegSlot x25, slot16
    arm64.loadRegSlot x26, slot17
    arm64.loadRegSlot x27, slot18
    arm64.loadRegSlot x28, slot19
    arm64.epilogue 176
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

func @pair {
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
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_4  ; "n="
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.add x24, x21, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x20, x26, x21
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
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.cmp x19, 0
    arm64.b.le ifcont
  positive:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.movRegReg x9, x20
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
  ifcont:
    arm64.loadRegBaseDisp.word64 x19, [x25 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x25 + 8]
    arm64.leaRdata x0, __str_rec_5  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.add x23, x20, x22
    arm64.add x0, x23, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.add x26, x24, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x26, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x0, x19, x22
    arm64.storeBaseDispReg.word64 [x24 + 0], x26
    arm64.storeBaseDispReg.word64 [x24 + 8], x23
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x24 + 24], x0
    arm64.movRegReg x0, x24
    arm64.bl print
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movImm x20, 0
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.movRegReg x9, x20
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

func @wrap {
  entry:
    arm64.prologue 96
    arm64.storeSlotReg slot8, x27
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_6  ; "w="
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.add x24, x21, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x20, x26, x21
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
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.cmp x19, 5
    arm64.b.le ifcont
  big:
    arm64.sub x0, x19, 5
    arm64.bl pair
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x9
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.movRegReg x9, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.loadRegSlot x27, slot8
    arm64.epilogue 96
    arm64.ret
  ifcont:
    arm64.loadRegBaseDisp.word64 x20, [x25 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x25 + 8]
    arm64.leaRdata x0, __str_rec_5  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.add x24, x21, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x26, x0
    arm64.add x27, x26, 56
    arm64.movRegReg x0, x27
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x20, x27, x21
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x0, x20, x23
    arm64.storeBaseDispReg.word64 [x26 + 0], x27
    arm64.storeBaseDispReg.word64 [x26 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x26 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x26 + 24], x0
    arm64.movRegReg x0, x26
    arm64.bl print
    arm64.movRegReg x0, x26
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.bl pair
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x9
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.movRegReg x9, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.loadRegSlot x27, slot8
    arm64.epilogue 96
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 176
    arm64.storeSlotReg slot19, x28
    arm64.storeSlotReg slot18, x27
    arm64.storeSlotReg slot17, x26
    arm64.storeSlotReg slot16, x25
    arm64.storeSlotReg slot15, x24
    arm64.storeSlotReg slot14, x23
    arm64.storeSlotReg slot13, x22
    arm64.storeSlotReg slot12, x21
    arm64.storeSlotReg slot11, x20
    arm64.storeSlotReg slot10, x19
    arm64.movImm x0, 9
    arm64.bl wrap
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x9
    arm64.movImm x0, 0
    arm64.bl wrap
    arm64.movRegReg x21, x0
    arm64.movRegReg x22, x9
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.storeSlotReg slot0, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_7  ; " "
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot9, x1
    arm64.movRegReg x0, x20
    arm64.bl __int_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_7  ; " "
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot8, x1
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot7, x1
    arm64.movRegReg x0, x21
    arm64.bl __int_to_string
    arm64.movRegReg x21, x0
    arm64.leaRdata x0, __str_rec_7  ; " "
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot6, x1
    arm64.loadRegBaseDisp.word64 x27, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x28, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x28
    arm64.bl __int_to_string
    arm64.storeSlotReg slot5, x0
    arm64.leaRdata x1, __str_rec_5  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.storeSlotReg slot4, x2
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.storeSlotReg slot3, x1
    arm64.add x2, x19, x25
    arm64.add x2, x2, x20
    arm64.add x2, x2, x26
    arm64.add x2, x2, x21
    arm64.add x2, x2, x27
    arm64.add x0, x2, x0
    arm64.add x0, x0, x1
    arm64.storeSlotReg slot1, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot2, x0
    arm64.add x22, x0, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x22, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x19, x19, x25
    arm64.loadRegSlot x1, slot9
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x19, x20
    arm64.loadRegSlot x1, slot8
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x19, x19, x26
    arm64.loadRegSlot x1, slot7
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x19, x19, x21
    arm64.loadRegSlot x1, slot6
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x27
    arm64.bl __str_copy
    arm64.add x19, x19, x27
    arm64.loadRegSlot x2, slot5
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x28
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot5
    arm64.add x19, x19, x0
    arm64.loadRegSlot x2, slot3
    arm64.loadRegSlot x1, slot4
    arm64.movRegReg x0, x19
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot3
    arm64.add x0, x19, x0
    arm64.loadRegSlot x19, slot2
    arm64.storeBaseDispReg.word64 [x19 + 0], x22
    arm64.loadRegSlot x0, slot1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.loadRegSlot x0, slot0
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot9
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot7
    arm64.bl __mm_decref
    arm64.movRegReg x0, x28
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.bl print
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot10
    arm64.loadRegSlot x20, slot11
    arm64.loadRegSlot x21, slot12
    arm64.loadRegSlot x22, slot13
    arm64.loadRegSlot x23, slot14
    arm64.loadRegSlot x24, slot15
    arm64.loadRegSlot x25, slot16
    arm64.loadRegSlot x26, slot17
    arm64.loadRegSlot x27, slot18
    arm64.loadRegSlot x28, slot19
    arm64.epilogue 176
    arm64.ret
}
```

<!-- test: a-spliced-pair-returned-through-an-exit-cascade-stays-in-registers -->
`pair` has one call site, so the called-once rule splices it into `wrap`, and the pair it builds is a
fresh literal in `wrap`'s own frame. `wrap` owns the String `tag` and returns on two exits, the spliced
pair inside an `if` and a literal `(0, 0)` after it, so both pairs reach `wrap`'s one terminal through
the shared cascade of drop blocks. Both come back in the two return registers, with no `__mm_alloc` for
a two-word record anywhere in `wrap`, and `main` reads both halves of each `wrap` result out of the two
return registers. `wrap(9)` takes the `if` and is `(4, 1)`; `wrap(0)` prints `w=0` and is `(0, 0)`.
Prints `w=0`, then `4 1 0 0`.
```maxon
typealias Integer = int(i64.min to i64.max)

function pair(n Integer) returns (Integer, Integer)
	return (n, 1)
end 'pair'

function wrap(n Integer) returns (Integer, Integer)
	let tag = "w={n}"

	if n > 5 'big'
		return pair(n - 5)
	end 'big'

	print("{tag}\n")
	return (0, 0)
end 'wrap'

function main() returns ExitCode
	let (a, b) = wrap(9)
	let (c, d) = wrap(0)
	print("{a} {b} {c} {d}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
w=0
4 1 0 0
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

func @wrap {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 88
    x64.movRegReg rbx, rcx
    x64.storeSlotReg slot1, rbx
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "w="
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __int_to_string
    x64.storeSlotReg slot5, r8
    x64.leaRegRegReg rax, r13, r8
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.leaRegRegImm32 r15, rbx, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r13
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
    x64.loadRegSlot rax, slot1
    x64.cmpRegImm32 rax, 5
    x64.jcc lessEqual, ifcont
  big:
    x64.loadRegSlot rax, slot1
    x64.leaRegRegImm32 r12, rax, -5
  __il_body:
    x64.movRegImm32 r13, 1
  __il_cont:
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.movRegReg r8, r12
    x64.movRegReg r10, r13
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.loadRegBaseDisp.word64 r12, [rbx + 0]
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
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
    x64.loadRegSlot rbx, slot4
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 r12, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __str_decref
    x64.movRegReg r8, rbx
    x64.movRegReg r10, r12
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 184
    x64.movRegImm32 rcx, 9
    x64.callDirect wrap
    x64.movRegReg rbx, r8
    x64.movRegReg r12, r10
    x64.movRegImm32 rcx, 0
    x64.callDirect wrap
    x64.movRegReg r13, r8
    x64.movRegReg r14, r10
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.storeSlotReg slot0, r15
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __int_to_string
    x64.storeSlotReg slot16, r8
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot11, rcx
    x64.loadRegBaseDisp.word64 rbx, [rax + 8]
    x64.storeSlotReg slot15, rbx
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot10, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot14, r8
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot9, rcx
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.storeSlotReg slot13, r12
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot8, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot12, r8
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.storeSlotReg slot3, r13
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot1, r8
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot5, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot17, rax
    x64.loadRegSlot r14, slot16
    x64.leaRegRegReg rax, r14, rbx
    x64.loadRegSlot rbx, slot14
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r12
    x64.loadRegSlot r12, slot12
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r8
    x64.loadRegSlot rcx, slot17
    x64.leaRegRegReg rax, rax, rcx
    x64.storeSlotReg slot6, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot7, r8
    x64.leaRegRegImm32 r13, r8, 56
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r15
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg r14, r13, r14
    x64.loadRegSlot rdx, slot11
    x64.loadRegSlot rax, slot15
    x64.movRegReg rcx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot15
    x64.leaRegRegReg r14, r14, rax
    x64.loadRegSlot rdx, slot10
    x64.movRegReg rcx, r14
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r14, rbx
    x64.loadRegSlot rdx, slot9
    x64.loadRegSlot rax, slot13
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot13
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r12
    x64.loadRegSlot rax, slot3
    x64.loadRegSlot rdx, slot4
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot3
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot rdx, slot2
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot1
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot5
    x64.loadRegSlot rax, slot17
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot17
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rbx, slot7
    x64.storeBaseDispReg.word64 [rbx + 0], r13
    x64.loadRegSlot rax, slot6
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot10
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot8
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
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

func @wrap {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 88
    x64.movRegReg rbx, rcx
    x64.storeSlotReg slot1, rbx
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "w="
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __int_to_string
    x64.storeSlotReg slot5, r8
    x64.leaRegRegReg rax, r13, r8
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.leaRegRegImm32 r15, rbx, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r13
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
    x64.loadRegSlot rax, slot1
    x64.cmpRegImm32 rax, 5
    x64.jcc lessEqual, ifcont
  big:
    x64.loadRegSlot rax, slot1
    x64.leaRegRegImm32 r12, rax, -5
  __il_body:
    x64.movRegImm32 r13, 1
  __il_cont:
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.movRegReg r8, r12
    x64.movRegReg r10, r13
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.loadRegBaseDisp.word64 r12, [rbx + 0]
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
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
    x64.loadRegSlot rbx, slot4
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 r12, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __str_decref
    x64.movRegReg r8, rbx
    x64.movRegReg r10, r12
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 184
    x64.movRegImm32 rcx, 9
    x64.callDirect wrap
    x64.movRegReg rbx, r8
    x64.movRegReg r12, r10
    x64.movRegImm32 rcx, 0
    x64.callDirect wrap
    x64.movRegReg r13, r8
    x64.movRegReg r14, r10
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.storeSlotReg slot0, r15
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __int_to_string
    x64.storeSlotReg slot16, r8
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot11, rcx
    x64.loadRegBaseDisp.word64 rbx, [rax + 8]
    x64.storeSlotReg slot15, rbx
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot10, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot14, r8
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot9, rcx
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.storeSlotReg slot13, r12
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot8, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot12, r8
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.storeSlotReg slot3, r13
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r8
    x64.callDirect __int_to_string
    x64.storeSlotReg slot1, r8
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot5, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot17, rax
    x64.loadRegSlot r14, slot16
    x64.leaRegRegReg rax, r14, rbx
    x64.loadRegSlot rbx, slot14
    x64.leaRegRegReg rax, rax, rbx
    x64.leaRegRegReg rax, rax, r12
    x64.loadRegSlot r12, slot12
    x64.leaRegRegReg rax, rax, r12
    x64.leaRegRegReg rax, rax, r13
    x64.leaRegRegReg rax, rax, r8
    x64.loadRegSlot rcx, slot17
    x64.leaRegRegReg rax, rax, rcx
    x64.storeSlotReg slot6, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot7, r8
    x64.leaRegRegImm32 r13, r8, 56
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r15
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg r14, r13, r14
    x64.loadRegSlot rdx, slot11
    x64.loadRegSlot rax, slot15
    x64.movRegReg rcx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot15
    x64.leaRegRegReg r14, r14, rax
    x64.loadRegSlot rdx, slot10
    x64.movRegReg rcx, r14
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r14, rbx
    x64.loadRegSlot rdx, slot9
    x64.loadRegSlot rax, slot13
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot13
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r12
    x64.loadRegSlot rax, slot3
    x64.loadRegSlot rdx, slot4
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot3
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot rdx, slot2
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot1
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rdx, slot5
    x64.loadRegSlot rax, slot17
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot17
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rbx, slot7
    x64.storeBaseDispReg.word64 [rbx + 0], r13
    x64.loadRegSlot rax, slot6
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot10
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot8
    x64.callDirect __mm_decref
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
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
```
```TargetIr:arm64-macos
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @wrap {
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
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_3  ; "w="
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.add x24, x21, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x20, x26, x21
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
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.cmp x19, 5
    arm64.b.le ifcont
  big:
    arm64.sub x19, x19, 5
  __il_body:
    arm64.movImm x20, 1
  __il_cont:
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.movRegReg x9, x20
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
  ifcont:
    arm64.loadRegBaseDisp.word64 x19, [x25 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x25 + 8]
    arm64.leaRdata x0, __str_rec_4  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.add x23, x20, x22
    arm64.add x0, x23, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.add x26, x24, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x26, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x0, x19, x22
    arm64.storeBaseDispReg.word64 [x24 + 0], x26
    arm64.storeBaseDispReg.word64 [x24 + 8], x23
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x24 + 24], x0
    arm64.movRegReg x0, x24
    arm64.bl print
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movImm x20, 0
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.movRegReg x9, x20
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

func @main {
  entry:
    arm64.prologue 176
    arm64.storeSlotReg slot19, x28
    arm64.storeSlotReg slot18, x27
    arm64.storeSlotReg slot17, x26
    arm64.storeSlotReg slot16, x25
    arm64.storeSlotReg slot15, x24
    arm64.storeSlotReg slot14, x23
    arm64.storeSlotReg slot13, x22
    arm64.storeSlotReg slot12, x21
    arm64.storeSlotReg slot11, x20
    arm64.storeSlotReg slot10, x19
    arm64.movImm x0, 9
    arm64.bl wrap
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x9
    arm64.movImm x0, 0
    arm64.bl wrap
    arm64.movRegReg x21, x0
    arm64.movRegReg x22, x9
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.storeSlotReg slot0, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_5  ; " "
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot9, x1
    arm64.movRegReg x0, x20
    arm64.bl __int_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_5  ; " "
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot8, x1
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot7, x1
    arm64.movRegReg x0, x21
    arm64.bl __int_to_string
    arm64.movRegReg x21, x0
    arm64.leaRdata x0, __str_rec_5  ; " "
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot6, x1
    arm64.loadRegBaseDisp.word64 x27, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x28, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x28
    arm64.bl __int_to_string
    arm64.storeSlotReg slot5, x0
    arm64.leaRdata x1, __str_rec_4  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.storeSlotReg slot4, x2
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.storeSlotReg slot3, x1
    arm64.add x2, x19, x25
    arm64.add x2, x2, x20
    arm64.add x2, x2, x26
    arm64.add x2, x2, x21
    arm64.add x2, x2, x27
    arm64.add x0, x2, x0
    arm64.add x0, x0, x1
    arm64.storeSlotReg slot1, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot2, x0
    arm64.add x22, x0, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x22, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x19, x19, x25
    arm64.loadRegSlot x1, slot9
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x19, x20
    arm64.loadRegSlot x1, slot8
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x19, x19, x26
    arm64.loadRegSlot x1, slot7
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x19, x19, x21
    arm64.loadRegSlot x1, slot6
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x27
    arm64.bl __str_copy
    arm64.add x19, x19, x27
    arm64.loadRegSlot x2, slot5
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x28
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot5
    arm64.add x19, x19, x0
    arm64.loadRegSlot x2, slot3
    arm64.loadRegSlot x1, slot4
    arm64.movRegReg x0, x19
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot3
    arm64.add x0, x19, x0
    arm64.loadRegSlot x19, slot2
    arm64.storeBaseDispReg.word64 [x19 + 0], x22
    arm64.loadRegSlot x0, slot1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.loadRegSlot x0, slot0
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot9
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot7
    arm64.bl __mm_decref
    arm64.movRegReg x0, x28
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.bl print
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot10
    arm64.loadRegSlot x20, slot11
    arm64.loadRegSlot x21, slot12
    arm64.loadRegSlot x22, slot13
    arm64.loadRegSlot x23, slot14
    arm64.loadRegSlot x24, slot15
    arm64.loadRegSlot x25, slot16
    arm64.loadRegSlot x26, slot17
    arm64.loadRegSlot x27, slot18
    arm64.loadRegSlot x28, slot19
    arm64.epilogue 176
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

func @wrap {
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
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_3  ; "w="
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.add x24, x21, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x20, x26, x21
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
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.cmp x19, 5
    arm64.b.le ifcont
  big:
    arm64.sub x19, x19, 5
  __il_body:
    arm64.movImm x20, 1
  __il_cont:
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.movRegReg x9, x20
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
  ifcont:
    arm64.loadRegBaseDisp.word64 x19, [x25 + 0]
    arm64.loadRegBaseDisp.word64 x20, [x25 + 8]
    arm64.leaRdata x0, __str_rec_4  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.add x23, x20, x22
    arm64.add x0, x23, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.add x26, x24, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x26, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x0, x19, x22
    arm64.storeBaseDispReg.word64 [x24 + 0], x26
    arm64.storeBaseDispReg.word64 [x24 + 8], x23
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x24 + 24], x0
    arm64.movRegReg x0, x24
    arm64.bl print
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movImm x20, 0
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movRegReg x0, x19
    arm64.movRegReg x9, x20
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

func @main {
  entry:
    arm64.prologue 176
    arm64.storeSlotReg slot19, x28
    arm64.storeSlotReg slot18, x27
    arm64.storeSlotReg slot17, x26
    arm64.storeSlotReg slot16, x25
    arm64.storeSlotReg slot15, x24
    arm64.storeSlotReg slot14, x23
    arm64.storeSlotReg slot13, x22
    arm64.storeSlotReg slot12, x21
    arm64.storeSlotReg slot11, x20
    arm64.storeSlotReg slot10, x19
    arm64.movImm x0, 9
    arm64.bl wrap
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x9
    arm64.movImm x0, 0
    arm64.bl wrap
    arm64.movRegReg x21, x0
    arm64.movRegReg x22, x9
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.storeSlotReg slot0, x23
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_5  ; " "
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot9, x1
    arm64.movRegReg x0, x20
    arm64.bl __int_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_5  ; " "
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot8, x1
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeSlotReg slot7, x1
    arm64.movRegReg x0, x21
    arm64.bl __int_to_string
    arm64.movRegReg x21, x0
    arm64.leaRdata x0, __str_rec_5  ; " "
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.storeSlotReg slot6, x1
    arm64.loadRegBaseDisp.word64 x27, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x28, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x28
    arm64.bl __int_to_string
    arm64.storeSlotReg slot5, x0
    arm64.leaRdata x1, __str_rec_4  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.storeSlotReg slot4, x2
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.storeSlotReg slot3, x1
    arm64.add x2, x19, x25
    arm64.add x2, x2, x20
    arm64.add x2, x2, x26
    arm64.add x2, x2, x21
    arm64.add x2, x2, x27
    arm64.add x0, x2, x0
    arm64.add x0, x0, x1
    arm64.storeSlotReg slot1, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot2, x0
    arm64.add x22, x0, 56
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x22, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x19, x19, x25
    arm64.loadRegSlot x1, slot9
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x19, x20
    arm64.loadRegSlot x1, slot8
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x19, x19, x26
    arm64.loadRegSlot x1, slot7
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x19, x19, x21
    arm64.loadRegSlot x1, slot6
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x27
    arm64.bl __str_copy
    arm64.add x19, x19, x27
    arm64.loadRegSlot x2, slot5
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x28
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot5
    arm64.add x19, x19, x0
    arm64.loadRegSlot x2, slot3
    arm64.loadRegSlot x1, slot4
    arm64.movRegReg x0, x19
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot3
    arm64.add x0, x19, x0
    arm64.loadRegSlot x19, slot2
    arm64.storeBaseDispReg.word64 [x19 + 0], x22
    arm64.loadRegSlot x0, slot1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 24], x0
    arm64.loadRegSlot x0, slot0
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot9
    arm64.bl __mm_decref
    arm64.loadRegSlot x0, slot7
    arm64.bl __mm_decref
    arm64.movRegReg x0, x28
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.bl print
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot10
    arm64.loadRegSlot x20, slot11
    arm64.loadRegSlot x21, slot12
    arm64.loadRegSlot x22, slot13
    arm64.loadRegSlot x23, slot14
    arm64.loadRegSlot x24, slot15
    arm64.loadRegSlot x25, slot16
    arm64.loadRegSlot x26, slot17
    arm64.loadRegSlot x27, slot18
    arm64.loadRegSlot x28, slot19
    arm64.epilogue 176
    arm64.ret
}
```

<!-- test: a-with-iterator-loop-over-an-array-allocates-nothing-per-element -->
A `for (iter, x) in values.withIterator()` loop reads each element together with its iterator, and the
pair it destructures is a value: the loop body holds no `__mm_alloc` or `__slab_alloc` call, so a step
costs the cursor advance and the two reads and no allocation. Prints `sum=35`, the four elements plus
their positions.
```maxon
typealias Tally = int(0 to u64.max)

function main() returns ExitCode
	let values = [3 as Tally, 5, 8, 13]
	var sum = 0 as Tally

	for (iter, x) in values.withIterator() 'each'
		sum = sum + x + (iter.index() as Tally)
	end 'each'

	print("sum={sum}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
sum=35
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
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot0, r8
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 4
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rbx, 0
    x64.leaRegRdata r12, [rip + __layout_Array_Tally]
  __il_body#29:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_retain
    x64.movRegReg r13, r8
  __il_body#34:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cursor_create
    x64.movRegReg r14, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#35
  tryerr#36:
    x64.movRegImm32 r14, 1
    x64.movRegImm32 r15, 0
    x64.jmp __il_cont#33
  tryok#35:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r14
    x64.callDirect __mm_incref
    x64.storeBaseDispReg.word64 [r15 + 0], r14
    x64.movRegReg rcx, r14
    x64.callDirect __managed_cursor_decref
  exitterminal#37:
    x64.movRegImm32 r14, 0
  __il_cont#33:
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, tryok#30
  tryerr#31:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
  exitterminal#32:
    x64.movRegImm32 r13, 0
    x64.movRegReg rax, r14
    x64.jmp __il_cont#28
  tryok#30:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg r13, r15
  __il_cont#28:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#22
  tryerr#23:
    x64.movRegImm32 r12, 0
    x64.jmp __il_cont#20
  tryok#22:
    x64.movRegReg rcx, r13
    x64.callDirect __mm_incref
    x64.loadRegBaseDisp.word64 rax, [r12 + 80]
    x64.loadRegBaseDisp.word64 r12, [r12 + 72]
  __il_body#26:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __retain_type_param
    x64.storeBaseDispReg.word64 [r14 + 0], r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __release_type_param
  __il_cont#25:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_ArrayIterator_T63315404e8d3fd14
  exitterminal#24:
    x64.movRegImm32 rax, 0
    x64.movRegReg r12, r14
  __il_cont#20:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, forhdr
    x64.jmp forjoin
  each:
    x64.leaRegRdata r13, [rip + __witness_ArrayIterator_Tally.Iterator]
    x64.leaRegRdata rcx, [rip + __layout_WithIterIterator_ArrayIterator_Tally_Tally]
  __il_body#10:
    x64.loadRegBaseDisp.word64 rdx, [r12 + 0]
    x64.callDirect __retain_type_param
    x64.movRegReg r14, r8
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callMem [r13 + 48]
  __il_cont#9:
    x64.leaRegRegReg rax, rbx, r8
  __il_body#11:
    x64.loadRegBaseDisp.word64 rcx, [r14 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic
  __il_cont#8:
    x64.leaRegRegReg rbx, rax, rcx
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_ArrayIterator_Tally
  forstep:
    x64.leaRegRdata rax, [rip + __witness_ArrayIterator_Tally.Iterator]
  __il_body#15:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callMem [rax + 56]
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#17
  tryok#16:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#14
  tryerr#17:
    x64.movRegReg rax, r10
  __il_cont#14:
    x64.movRegReg rcx, rax
  forhdr:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, each
  forexit:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Tally_Tally
  forjoin:
    x64.leaRegRdata rax, [rip + __str_rec_7]  ; "sum="
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_8]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot3, rax
    x64.leaRegRegReg rcx, r13, rbx
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r13
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, rbx
    x64.loadRegSlot rax, slot3
    x64.loadRegSlot rdx, slot4
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot3
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
  __il_body#19:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __write_stdout
  __il_cont#18:
    x64.loadRegSlot rcx, slot2
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
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_38]  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot0, r8
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 4
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm32 rax, 8
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rbx, 0
    x64.leaRegRdata r12, [rip + __layout_Array_Tally]
  __il_body#29:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_retain
    x64.movRegReg r13, r8
  __il_body#34:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cursor_create
    x64.movRegReg r14, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#35
  tryerr#36:
    x64.movRegImm32 r14, 1
    x64.movRegImm32 r15, 0
    x64.jmp __il_cont#33
  tryok#35:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r14
    x64.callDirect __mm_incref
    x64.storeBaseDispReg.word64 [r15 + 0], r14
    x64.movRegReg rcx, r14
    x64.callDirect __managed_cursor_decref
  exitterminal#37:
    x64.movRegImm32 r14, 0
  __il_cont#33:
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, tryok#30
  tryerr#31:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
  exitterminal#32:
    x64.movRegImm32 r13, 0
    x64.movRegReg rax, r14
    x64.jmp __il_cont#28
  tryok#30:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg r13, r15
  __il_cont#28:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#22
  tryerr#23:
    x64.movRegImm32 r12, 0
    x64.jmp __il_cont#20
  tryok#22:
    x64.movRegReg rcx, r13
    x64.callDirect __mm_incref
    x64.loadRegBaseDisp.word64 rax, [r12 + 80]
    x64.loadRegBaseDisp.word64 r12, [r12 + 72]
  __il_body#26:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __retain_type_param
    x64.storeBaseDispReg.word64 [r14 + 0], r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __release_type_param
  __il_cont#25:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_ArrayIterator_T63315404e8d3fd14
  exitterminal#24:
    x64.movRegImm32 rax, 0
    x64.movRegReg r12, r14
  __il_cont#20:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, forhdr
    x64.jmp forjoin
  each:
    x64.leaRegRdata r13, [rip + __witness_ArrayIterator_Tally.Iterator]
    x64.leaRegRdata rcx, [rip + __layout_WithIterIterator_ArrayIterator_Tally_Tally]
  __il_body#10:
    x64.loadRegBaseDisp.word64 rdx, [r12 + 0]
    x64.callDirect __retain_type_param
    x64.movRegReg r14, r8
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callMem [r13 + 48]
  __il_cont#9:
    x64.leaRegRegReg rax, rbx, r8
  __il_body#11:
    x64.loadRegBaseDisp.word64 rcx, [r14 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic
  __il_cont#8:
    x64.leaRegRegReg rbx, rax, rcx
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_ArrayIterator_Tally
  forstep:
    x64.leaRegRdata rax, [rip + __witness_ArrayIterator_Tally.Iterator]
  __il_body#15:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callMem [rax + 56]
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#17
  tryok#16:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#14
  tryerr#17:
    x64.movRegReg rax, r10
  __il_cont#14:
    x64.movRegReg rcx, rax
  forhdr:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, each
  forexit:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Tally_Tally
  forjoin:
    x64.leaRegRdata rax, [rip + __str_rec_7]  ; "sum="
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_8]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot3, rax
    x64.leaRegRegReg rcx, r13, rbx
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r13
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, rbx
    x64.loadRegSlot rax, slot3
    x64.loadRegSlot rdx, slot4
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot3
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
  __il_body#19:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __write_stdout
  __il_cont#18:
    x64.loadRegSlot rcx, slot2
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
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_38]  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
    arm64.prologue 96
    arm64.storeSlotReg slot9, x28
    arm64.storeSlotReg slot8, x27
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
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
    arm64.movImm x20, 0
    arm64.leaRdata x21, __layout_Array_Tally
  __il_body#29:
    arm64.movRegReg x0, x19
    arm64.bl __managed_retain
    arm64.movRegReg x22, x0
  __il_body#34:
    arm64.movRegReg x0, x22
    arm64.bl __managed_cursor_create
    arm64.movRegReg x23, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#35
  tryerr#36:
    arm64.movImm x23, 1
    arm64.movImm x24, 0
    arm64.b __il_cont#33
  tryok#35:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x23
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x24 + 0], x23
    arm64.movRegReg x0, x23
    arm64.bl __managed_cursor_decref
  exitterminal#37:
    arm64.movImm x23, 0
  __il_cont#33:
    arm64.cmp x23, 0
    arm64.b.eq tryok#30
  tryerr#31:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitterminal#32:
    arm64.movImm x22, 0
    arm64.movRegReg x0, x23
    arm64.b __il_cont#28
  tryok#30:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
    arm64.movImm x0, 0
    arm64.movRegReg x22, x24
  __il_cont#28:
    arm64.cmp x0, 0
    arm64.b.eq tryok#22
  tryerr#23:
    arm64.movImm x21, 0
    arm64.b __il_cont#20
  tryok#22:
    arm64.movRegReg x0, x22
    arm64.bl __mm_incref
    arm64.loadRegBaseDisp.word64 x0, [x21 + 80]
    arm64.loadRegBaseDisp.word64 x21, [x21 + 72]
  __il_body#26:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __retain_type_param
    arm64.storeBaseDispReg.word64 [x23 + 0], x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __release_type_param
  __il_cont#25:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_ArrayIterator_T63315404e8d3fd14
  exitterminal#24:
    arm64.movImm x0, 0
    arm64.movRegReg x21, x23
  __il_cont#20:
    arm64.movImm x1, 0
    arm64.cmp x0, 0
    arm64.b.eq forhdr
    arm64.b forjoin
  each:
    arm64.leaRdata x22, __witness_ArrayIterator_Tally.Iterator
    arm64.leaRdata x0, __layout_WithIterIterator_ArrayIterator_Tally_Tally
  __il_body#10:
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.bl __retain_type_param
    arm64.movRegReg x23, x0
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x16, [x22 + 48]
    arm64.blr x16
  __il_cont#9:
    arm64.add x0, x20, x0
  __il_body#11:
    arm64.loadRegBaseDisp.word64 x1, [x23 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic
  __il_cont#8:
    arm64.add x20, x0, x1
    arm64.movRegReg x0, x23
    arm64.bl __destruct_ArrayIterator_Tally
  forstep:
    arm64.leaRdata x1, __witness_ArrayIterator_Tally.Iterator
  __il_body#15:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x16, [x1 + 56]
    arm64.blr x16
    arm64.cmp x9, 0
    arm64.b.ne tryerr#17
  tryok#16:
    arm64.movImm x0, 0
    arm64.b __il_cont#14
  tryerr#17:
    arm64.movRegReg x0, x9
  __il_cont#14:
    arm64.movRegReg x1, x0
  forhdr:
    arm64.cmp x1, 0
    arm64.b.eq each
  forexit:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Tally_Tally
  forjoin:
    arm64.leaRdata x0, __str_rec_7  ; "sum="
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x23
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_8  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x0, x22, x20
    arm64.add x26, x0, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x21, x28, x22
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x20, x21, x20
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x20, x25
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
    arm64.loadRegSlot x27, slot8
    arm64.loadRegSlot x28, slot9
    arm64.epilogue 96
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_38  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
    arm64.loadRegSlot x27, slot8
    arm64.loadRegSlot x28, slot9
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
    arm64.storeSlotReg slot9, x28
    arm64.storeSlotReg slot8, x27
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
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
    arm64.movImm x20, 0
    arm64.leaRdata x21, __layout_Array_Tally
  __il_body#29:
    arm64.movRegReg x0, x19
    arm64.bl __managed_retain
    arm64.movRegReg x22, x0
  __il_body#34:
    arm64.movRegReg x0, x22
    arm64.bl __managed_cursor_create
    arm64.movRegReg x23, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#35
  tryerr#36:
    arm64.movImm x23, 1
    arm64.movImm x24, 0
    arm64.b __il_cont#33
  tryok#35:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x23
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x24 + 0], x23
    arm64.movRegReg x0, x23
    arm64.bl __managed_cursor_decref
  exitterminal#37:
    arm64.movImm x23, 0
  __il_cont#33:
    arm64.cmp x23, 0
    arm64.b.eq tryok#30
  tryerr#31:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitterminal#32:
    arm64.movImm x22, 0
    arm64.movRegReg x0, x23
    arm64.b __il_cont#28
  tryok#30:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
    arm64.movImm x0, 0
    arm64.movRegReg x22, x24
  __il_cont#28:
    arm64.cmp x0, 0
    arm64.b.eq tryok#22
  tryerr#23:
    arm64.movImm x21, 0
    arm64.b __il_cont#20
  tryok#22:
    arm64.movRegReg x0, x22
    arm64.bl __mm_incref
    arm64.loadRegBaseDisp.word64 x0, [x21 + 80]
    arm64.loadRegBaseDisp.word64 x21, [x21 + 72]
  __il_body#26:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __retain_type_param
    arm64.storeBaseDispReg.word64 [x23 + 0], x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __release_type_param
  __il_cont#25:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_ArrayIterator_T63315404e8d3fd14
  exitterminal#24:
    arm64.movImm x0, 0
    arm64.movRegReg x21, x23
  __il_cont#20:
    arm64.movImm x1, 0
    arm64.cmp x0, 0
    arm64.b.eq forhdr
    arm64.b forjoin
  each:
    arm64.leaRdata x22, __witness_ArrayIterator_Tally.Iterator
    arm64.leaRdata x0, __layout_WithIterIterator_ArrayIterator_Tally_Tally
  __il_body#10:
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.bl __retain_type_param
    arm64.movRegReg x23, x0
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x16, [x22 + 48]
    arm64.blr x16
  __il_cont#9:
    arm64.add x0, x20, x0
  __il_body#11:
    arm64.loadRegBaseDisp.word64 x1, [x23 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic
  __il_cont#8:
    arm64.add x20, x0, x1
    arm64.movRegReg x0, x23
    arm64.bl __destruct_ArrayIterator_Tally
  forstep:
    arm64.leaRdata x1, __witness_ArrayIterator_Tally.Iterator
  __il_body#15:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x16, [x1 + 56]
    arm64.blr x16
    arm64.cmp x9, 0
    arm64.b.ne tryerr#17
  tryok#16:
    arm64.movImm x0, 0
    arm64.b __il_cont#14
  tryerr#17:
    arm64.movRegReg x0, x9
  __il_cont#14:
    arm64.movRegReg x1, x0
  forhdr:
    arm64.cmp x1, 0
    arm64.b.eq each
  forexit:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Tally_Tally
  forjoin:
    arm64.leaRdata x0, __str_rec_7  ; "sum="
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x23
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_8  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x0, x22, x20
    arm64.add x26, x0, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x21, x28, x22
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x20, x21, x20
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x20, x25
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
    arm64.loadRegSlot x27, slot8
    arm64.loadRegSlot x28, slot9
    arm64.epilogue 96
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_38  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
    arm64.loadRegSlot x27, slot8
    arm64.loadRegSlot x28, slot9
    arm64.epilogue 96
    arm64.ret
}
```

<!-- test: a-with-iterator-loop-that-returns-early-allocates-nothing-per-element -->
A `for (iter, pad) in keystream.withIterator()` loop whose body can `return` drops its destructured pair
on two paths, the early return and the end of the step. Each drop becomes the pair's slot releases, so the
loop body holds no `__mm_alloc` call. Prints `count=6 sum=46`.
```maxon
typealias ByteSum = int(0 to u64.max)

function xorKeyStream(output ByteArray, data ByteArray, keystream ByteArray, off ElementIndex)
	for (iter, pad) in keystream.withIterator() 'eachByte'
		let index = off + iter.index()

		if index >= data.count() 'finished'
			return
		end 'finished'

		let plain = try data.get(index) otherwise panic("data index unreachable")
		output.push(plain xor pad)
	end 'eachByte'
end 'xorKeyStream'

function main() returns ExitCode
	var output = ByteArray.create()
	let data = [1 as Byte, 2, 3, 4]
	let keystream = [9 as Byte, 8, 7, 6, 5]

	xorKeyStream(output, data: data, keystream: keystream, off: 0)
	xorKeyStream(output, data: data, keystream: keystream, off: 2)

	var sum = 0 as ByteSum

	for slot in 0 upto output.count() 'eachOutput'
		let value = try output.get(slot) otherwise panic("output index unreachable")
		sum = sum + (value as ByteSum)
	end 'eachOutput'

	print("count={output.count()} sum={sum}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
count=6 sum=46
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

func @xorKeyStream {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 56
    x64.storeSlotReg slot0, rcx
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rbx, r9
    x64.leaRegRdata r12, [rip + __layout_Array_Byte]
  __il_body#51:
    x64.movRegReg rcx, rax
    x64.callDirect __managed_retain
    x64.movRegReg r13, r8
  __il_body#56:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cursor_create
    x64.movRegReg r14, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#57
  tryerr#58:
    x64.movRegImm32 r14, 1
    x64.movRegImm32 r15, 0
    x64.jmp __il_cont#55
  tryok#57:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r14
    x64.callDirect __mm_incref
    x64.storeBaseDispReg.word64 [r15 + 0], r14
    x64.movRegReg rcx, r14
    x64.callDirect __managed_cursor_decref
  exitterminal#59:
    x64.movRegImm32 r14, 0
  __il_cont#55:
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, tryok#52
  tryerr#53:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
  exitterminal#54:
    x64.movRegImm32 r13, 0
    x64.movRegReg rax, r14
    x64.jmp __il_cont#50
  tryok#52:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg r13, r15
  __il_cont#50:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#44
  tryerr#45:
    x64.movRegImm32 r12, 0
    x64.jmp __il_cont#42
  tryok#44:
    x64.movRegReg rcx, r13
    x64.callDirect __mm_incref
    x64.loadRegBaseDisp.word64 rax, [r12 + 80]
    x64.loadRegBaseDisp.word64 r12, [r12 + 72]
  __il_body#48:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __retain_type_param
    x64.storeBaseDispReg.word64 [r14 + 0], r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __release_type_param
  __il_cont#47:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_ArrayIterator_T63315404e8d3fd14
  exitterminal#46:
    x64.movRegImm32 rax, 0
    x64.movRegReg r12, r14
  __il_cont#42:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, forhdr
    x64.jmp forjoin
  eachByte:
    x64.leaRegRdata r13, [rip + __witness_ArrayIterator_Byte.Iterator]
    x64.leaRegRdata rcx, [rip + __layout_WithIterIterator_ArrayIterator_Byte_Byte]
  __il_body#34:
    x64.loadRegBaseDisp.word64 rdx, [r12 + 0]
    x64.callDirect __retain_type_param
    x64.movRegReg r14, r8
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callMem [r13 + 48]
    x64.movRegReg r13, r8
  __il_body#35:
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic#37
  __il_cont#32:
    x64.leaRegRegReg rdx, rbx, rax
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc below, ifcont
  finished:
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_ArrayIterator_Byte
  exitdrop:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  exitterminal#7:
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#17
  __im_load:
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride#21
  __im_word#19:
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rax + rdx*8 + 0]
    x64.jmp tryok#10
  __im_stride#21:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#17
  __im_byte#20:
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegReg rax, rax, rdx
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok#10
  __im_slow#17:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#11
  critsplit:
    x64.movRegReg rax, r8
  tryok#10:
    x64.movRegReg rdx, rax
    x64.xorRegReg rdx, rax, r13
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __rc_panic#14
  __rc_chk:
    x64.cmpRegImm32 rdx, 255
    x64.jcc greater, __rc_panic#14
  __rc_ok:
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.loadRegBaseDisp.word64 rax, [rax + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __im_slow#24
  __im_bounds:
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rax, [rax + 16]
    x64.cmpRegReg rcx, rax
    x64.jcc aboveEqual, __im_slow#24
  __im_buffer:
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_slow#24
  __im_viewed:
    x64.leaRegRegImm32 rsi, rax, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#24
  __im_store:
    x64.loadRegSlot rsi, slot0
    x64.loadRegBaseDisp.word64 rsi, [rsi + 24]
    x64.cmpRegImm32 rsi, 8
    x64.jcc notEqual, __im_stride#31
  __im_word#29:
    x64.storeBaseIndexScaleReg.word64 [rax + rcx*8 + 0], rdx
    x64.leaRegRegImm32 rax, rcx, 1
    x64.loadRegSlot rcx, slot0
    x64.storeBaseDispReg.word64 [rcx + 8], rax
    x64.jmp __im_cont
  __im_stride#31:
    x64.cmpRegImm32 rsi, 1
    x64.jcc notEqual, __im_slow#24
  __im_byte#30:
    x64.leaRegRegReg rax, rax, rcx
    x64.storeBaseDispReg.byte [rax + 0], rdx
    x64.leaRegRegImm32 rax, rcx, 1
    x64.loadRegSlot rcx, slot0
    x64.storeBaseDispReg.word64 [rcx + 8], rax
    x64.jmp __im_cont
  __im_slow#24:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_push
  __im_cont:
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_ArrayIterator_Byte
  forstep:
    x64.leaRegRdata rax, [rip + __witness_ArrayIterator_Byte.Iterator]
  __il_body#39:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callMem [rax + 56]
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#41
  tryok#40:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#38
  tryerr#41:
    x64.movRegReg rax, r10
  __il_cont#38:
    x64.movRegReg rcx, rax
  forhdr:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, eachByte
  forexit:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  forjoin:
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#37:
    x64.leaRegRdata rcx, [rip + __str_blob_44]  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#11:
    x64.leaRegRdata rcx, [rip + __str_blob_9]  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:12: data index unreachable\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#14:
    x64.leaRegRdata rcx, [rip + __str_blob_43]  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:13: Range check failed: value outside typealias 'Byte'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 136
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot2, rbx
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot0, r12
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r12 + 0], rax
    x64.movRegImm32 rax, 4
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot1, r13
    x64.leaRegRdata rax, [rip + __barr_blob_1]
    x64.storeBaseDispReg.word64 [r13 + 0], rax
    x64.movRegImm32 rax, 5
    x64.storeBaseDispReg.word64 [r13 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r13 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r13 + 24], rax
    x64.movRegImm32 r9, 0
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect xorKeyStream
    x64.movRegImm32 r9, 2
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect xorKeyStream
    x64.movRegImm32 r12, 0
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 r13, [rbx + 8]
  __us_test:
    x64.loadRegBaseDisp.word64 r14, [rbx + 24]
    x64.loadRegBaseDisp.word64 r15, [rbx + 0]
    x64.jmp forhdr
  eachOutput:
    x64.cmpRegReg rdx, r13
    x64.jcc aboveEqual, __im_slow
  __im_load:
    x64.cmpRegImm32 r14, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseIndexScale.word64 rax, [r15 + rdx*8 + 0]
    x64.jmp tryok
  __im_stride:
    x64.cmpRegImm32 r14, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.leaRegRegReg rax, r15, rdx
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok
  __im_slow:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  critsplit:
    x64.movRegReg rax, r8
  tryok:
    x64.leaRegRegReg r12, r12, rax
  forstep:
    x64.loadRegSlot rax, slot10
    x64.leaRegRegImm32 rdx, rax, 1
  forhdr:
    x64.storeSlotReg slot10, rdx
    x64.cmpRegReg rdx, r13
    x64.jcc below, eachOutput
  forexit:
    x64.leaRegRdata rax, [rip + __str_rec_10]  ; "count="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot11, rcx
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot9, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r8
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_11]  ; " sum="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot8, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __uint_to_string
    x64.storeSlotReg slot7, r8
    x64.leaRegRdata rax, [rip + __str_rec_12]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rcx, r13, rbx
    x64.leaRegRegReg rcx, rcx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.loadRegSlot rdx, slot11
    x64.movRegReg rcx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r13, r12, r13
    x64.loadRegSlot rdx, slot9
    x64.movRegReg rcx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r13, rbx
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
    x64.loadRegSlot rcx, slot9
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
  exitdrop#8:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
  exitdrop#9:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 136
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_13]  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:28: output index unreachable\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 136
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

func @xorKeyStream {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 56
    x64.storeSlotReg slot0, rcx
    x64.storeSlotReg slot1, rdx
    x64.movRegReg rbx, r9
    x64.leaRegRdata r12, [rip + __layout_Array_Byte]
  __il_body#51:
    x64.movRegReg rcx, rax
    x64.callDirect __managed_retain
    x64.movRegReg r13, r8
  __il_body#56:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cursor_create
    x64.movRegReg r14, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#57
  tryerr#58:
    x64.movRegImm32 r14, 1
    x64.movRegImm32 r15, 0
    x64.jmp __il_cont#55
  tryok#57:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r14
    x64.callDirect __mm_incref
    x64.storeBaseDispReg.word64 [r15 + 0], r14
    x64.movRegReg rcx, r14
    x64.callDirect __managed_cursor_decref
  exitterminal#59:
    x64.movRegImm32 r14, 0
  __il_cont#55:
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, tryok#52
  tryerr#53:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
  exitterminal#54:
    x64.movRegImm32 r13, 0
    x64.movRegReg rax, r14
    x64.jmp __il_cont#50
  tryok#52:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg r13, r15
  __il_cont#50:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#44
  tryerr#45:
    x64.movRegImm32 r12, 0
    x64.jmp __il_cont#42
  tryok#44:
    x64.movRegReg rcx, r13
    x64.callDirect __mm_incref
    x64.loadRegBaseDisp.word64 rax, [r12 + 80]
    x64.loadRegBaseDisp.word64 r12, [r12 + 72]
  __il_body#48:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __retain_type_param
    x64.storeBaseDispReg.word64 [r14 + 0], r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __release_type_param
  __il_cont#47:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_ArrayIterator_T63315404e8d3fd14
  exitterminal#46:
    x64.movRegImm32 rax, 0
    x64.movRegReg r12, r14
  __il_cont#42:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, forhdr
    x64.jmp forjoin
  eachByte:
    x64.leaRegRdata r13, [rip + __witness_ArrayIterator_Byte.Iterator]
    x64.leaRegRdata rcx, [rip + __layout_WithIterIterator_ArrayIterator_Byte_Byte]
  __il_body#34:
    x64.loadRegBaseDisp.word64 rdx, [r12 + 0]
    x64.callDirect __retain_type_param
    x64.movRegReg r14, r8
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callMem [r13 + 48]
    x64.movRegReg r13, r8
  __il_body#35:
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic#37
  __il_cont#32:
    x64.leaRegRegReg rdx, rbx, rax
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc below, ifcont
  finished:
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_ArrayIterator_Byte
  exitdrop:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  exitterminal#7:
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#17
  __im_load:
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride#21
  __im_word#19:
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rax + rdx*8 + 0]
    x64.jmp tryok#10
  __im_stride#21:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#17
  __im_byte#20:
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegReg rax, rax, rdx
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok#10
  __im_slow#17:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#11
  critsplit:
    x64.movRegReg rax, r8
  tryok#10:
    x64.movRegReg rdx, rax
    x64.xorRegReg rdx, rax, r13
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __rc_panic#14
  __rc_chk:
    x64.cmpRegImm32 rdx, 255
    x64.jcc greater, __rc_panic#14
  __rc_ok:
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.loadRegBaseDisp.word64 rax, [rax + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __im_slow#24
  __im_bounds:
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rax, [rax + 16]
    x64.cmpRegReg rcx, rax
    x64.jcc aboveEqual, __im_slow#24
  __im_buffer:
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_slow#24
  __im_viewed:
    x64.leaRegRegImm32 rsi, rax, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#24
  __im_store:
    x64.loadRegSlot rsi, slot0
    x64.loadRegBaseDisp.word64 rsi, [rsi + 24]
    x64.cmpRegImm32 rsi, 8
    x64.jcc notEqual, __im_stride#31
  __im_word#29:
    x64.storeBaseIndexScaleReg.word64 [rax + rcx*8 + 0], rdx
    x64.leaRegRegImm32 rax, rcx, 1
    x64.loadRegSlot rcx, slot0
    x64.storeBaseDispReg.word64 [rcx + 8], rax
    x64.jmp __im_cont
  __im_stride#31:
    x64.cmpRegImm32 rsi, 1
    x64.jcc notEqual, __im_slow#24
  __im_byte#30:
    x64.leaRegRegReg rax, rax, rcx
    x64.storeBaseDispReg.byte [rax + 0], rdx
    x64.leaRegRegImm32 rax, rcx, 1
    x64.loadRegSlot rcx, slot0
    x64.storeBaseDispReg.word64 [rcx + 8], rax
    x64.jmp __im_cont
  __im_slow#24:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_push
  __im_cont:
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_ArrayIterator_Byte
  forstep:
    x64.leaRegRdata rax, [rip + __witness_ArrayIterator_Byte.Iterator]
  __il_body#39:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callMem [rax + 56]
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#41
  tryok#40:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#38
  tryerr#41:
    x64.movRegReg rax, r10
  __il_cont#38:
    x64.movRegReg rcx, rax
  forhdr:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, eachByte
  forexit:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  forjoin:
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#37:
    x64.leaRegRdata rcx, [rip + __str_blob_44]  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#11:
    x64.leaRegRdata rcx, [rip + __str_blob_9]  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:12: data index unreachable\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#14:
    x64.leaRegRdata rcx, [rip + __str_blob_43]  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:13: Range check failed: value outside typealias 'Byte'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 136
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot2, rbx
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot0, r12
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r12 + 0], rax
    x64.movRegImm32 rax, 4
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot1, r13
    x64.leaRegRdata rax, [rip + __barr_blob_1]
    x64.storeBaseDispReg.word64 [r13 + 0], rax
    x64.movRegImm32 rax, 5
    x64.storeBaseDispReg.word64 [r13 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r13 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r13 + 24], rax
    x64.movRegImm32 r9, 0
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect xorKeyStream
    x64.movRegImm32 r9, 2
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect xorKeyStream
    x64.movRegImm32 r12, 0
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 r13, [rbx + 8]
  __us_test:
    x64.loadRegBaseDisp.word64 r14, [rbx + 24]
    x64.loadRegBaseDisp.word64 r15, [rbx + 0]
    x64.jmp forhdr
  eachOutput:
    x64.cmpRegReg rdx, r13
    x64.jcc aboveEqual, __im_slow
  __im_load:
    x64.cmpRegImm32 r14, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseIndexScale.word64 rax, [r15 + rdx*8 + 0]
    x64.jmp tryok
  __im_stride:
    x64.cmpRegImm32 r14, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.leaRegRegReg rax, r15, rdx
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok
  __im_slow:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  critsplit:
    x64.movRegReg rax, r8
  tryok:
    x64.leaRegRegReg r12, r12, rax
  forstep:
    x64.loadRegSlot rax, slot10
    x64.leaRegRegImm32 rdx, rax, 1
  forhdr:
    x64.storeSlotReg slot10, rdx
    x64.cmpRegReg rdx, r13
    x64.jcc below, eachOutput
  forexit:
    x64.leaRegRdata rax, [rip + __str_rec_10]  ; "count="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot11, rcx
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot9, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r8
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_11]  ; " sum="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot8, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __uint_to_string
    x64.storeSlotReg slot7, r8
    x64.leaRegRdata rax, [rip + __str_rec_12]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rcx, r13, rbx
    x64.leaRegRegReg rcx, rcx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.loadRegSlot rdx, slot11
    x64.movRegReg rcx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r13, r12, r13
    x64.loadRegSlot rdx, slot9
    x64.movRegReg rcx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r13, rbx
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
    x64.loadRegSlot rcx, slot9
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
  exitdrop#8:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
  exitdrop#9:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 136
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_13]  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:28: output index unreachable\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 136
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

func @xorKeyStream {
  entry:
    arm64.prologue 80
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x3
    arm64.leaRdata x22, __layout_Array_Byte
  __il_body#51:
    arm64.movRegReg x0, x2
    arm64.bl __managed_retain
    arm64.movRegReg x23, x0
  __il_body#56:
    arm64.movRegReg x0, x23
    arm64.bl __managed_cursor_create
    arm64.movRegReg x24, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#57
  tryerr#58:
    arm64.movImm x24, 1
    arm64.movImm x25, 0
    arm64.b __il_cont#55
  tryok#57:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.movRegReg x0, x24
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x25 + 0], x24
    arm64.movRegReg x0, x24
    arm64.bl __managed_cursor_decref
  exitterminal#59:
    arm64.movImm x24, 0
  __il_cont#55:
    arm64.cmp x24, 0
    arm64.b.eq tryok#52
  tryerr#53:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
  exitterminal#54:
    arm64.movImm x23, 0
    arm64.movRegReg x0, x24
    arm64.b __il_cont#50
  tryok#52:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
    arm64.movImm x0, 0
    arm64.movRegReg x23, x25
  __il_cont#50:
    arm64.cmp x0, 0
    arm64.b.eq tryok#44
  tryerr#45:
    arm64.movImm x22, 0
    arm64.b __il_cont#42
  tryok#44:
    arm64.movRegReg x0, x23
    arm64.bl __mm_incref
    arm64.loadRegBaseDisp.word64 x0, [x22 + 80]
    arm64.loadRegBaseDisp.word64 x22, [x22 + 72]
  __il_body#48:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.bl __retain_type_param
    arm64.storeBaseDispReg.word64 [x24 + 0], x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.bl __release_type_param
  __il_cont#47:
    arm64.movRegReg x0, x23
    arm64.bl __destruct_ArrayIterator_T63315404e8d3fd14
  exitterminal#46:
    arm64.movImm x0, 0
    arm64.movRegReg x22, x24
  __il_cont#42:
    arm64.movImm x1, 0
    arm64.cmp x0, 0
    arm64.b.eq forhdr
    arm64.b forjoin
  eachByte:
    arm64.leaRdata x23, __witness_ArrayIterator_Byte.Iterator
    arm64.leaRdata x0, __layout_WithIterIterator_ArrayIterator_Byte_Byte
  __il_body#34:
    arm64.loadRegBaseDisp.word64 x1, [x22 + 0]
    arm64.bl __retain_type_param
    arm64.movRegReg x24, x0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x16, [x23 + 48]
    arm64.blr x16
    arm64.movRegReg x23, x0
  __il_body#35:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#37
  __il_cont#32:
    arm64.add x1, x21, x0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x1, x0
    arm64.b.lo ifcont
  finished:
    arm64.movRegReg x0, x24
    arm64.bl __destruct_ArrayIterator_Byte
  exitdrop:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  exitterminal#7:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  ifcont:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#17
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride#21
  __im_word#19:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
    arm64.b tryok#10
  __im_stride#21:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#17
  __im_byte#20:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.add x0, x0, x1
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok#10
  __im_slow#17:
    arm64.movRegReg x0, x20
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#11
  tryok#10:
    arm64.eor x1, x0, x23
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#14
  __rc_chk:
    arm64.cmp x1, 255
    arm64.b.gt __rc_panic#14
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow#24
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow#24
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow#24
  __im_viewed:
    arm64.sub x3, x2, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#24
  __im_store:
    arm64.loadRegBaseDisp.word64 x3, [x19 + 24]
    arm64.cmp x3, 8
    arm64.b.ne __im_stride#31
  __im_word#29:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.b __im_cont
  __im_stride#31:
    arm64.cmp x3, 1
    arm64.b.ne __im_slow#24
  __im_byte#30:
    arm64.add x2, x2, x0
    arm64.storeBaseDispReg.byte [x2 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.b __im_cont
  __im_slow#24:
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
  __im_cont:
    arm64.movRegReg x0, x24
    arm64.bl __destruct_ArrayIterator_Byte
  forstep:
    arm64.leaRdata x1, __witness_ArrayIterator_Byte.Iterator
  __il_body#39:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x16, [x1 + 56]
    arm64.blr x16
    arm64.cmp x9, 0
    arm64.b.ne tryerr#41
  tryok#40:
    arm64.movImm x0, 0
    arm64.b __il_cont#38
  tryerr#41:
    arm64.movRegReg x0, x9
  __il_cont#38:
    arm64.movRegReg x1, x0
  forhdr:
    arm64.cmp x1, 0
    arm64.b.eq eachByte
  forexit:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  forjoin:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  __rc_panic#37:
    arm64.leaRdata x0, __str_blob_44  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  tryerr#11:
    arm64.leaRdata x0, __str_blob_9  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:12: data index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  __rc_panic#14:
    arm64.leaRdata x0, __str_blob_43  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:13: Range check failed: value outside typealias 'Byte'\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 144
    arm64.storeSlotReg slot15, x28
    arm64.storeSlotReg slot14, x27
    arm64.storeSlotReg slot13, x26
    arm64.storeSlotReg slot12, x25
    arm64.storeSlotReg slot11, x24
    arm64.storeSlotReg slot10, x23
    arm64.storeSlotReg slot9, x22
    arm64.storeSlotReg slot8, x21
    arm64.storeSlotReg slot7, x20
    arm64.storeSlotReg slot6, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot1, x20
    arm64.leaRdata x0, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movImm x0, 4
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.movImm x0, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x20 + 24], x0
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot2, x21
    arm64.leaRdata x0, __barr_blob_1
    arm64.storeBaseDispReg.word64 [x21 + 0], x0
    arm64.movImm x0, 5
    arm64.storeBaseDispReg.word64 [x21 + 8], x0
    arm64.movImm x0, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x21 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x21 + 24], x0
    arm64.movImm x3, 0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl xorKeyStream
    arm64.movImm x3, 2
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl xorKeyStream
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.loadRegBaseDisp.word64 x22, [x19 + 8]
  __us_test:
    arm64.loadRegBaseDisp.word64 x23, [x19 + 24]
    arm64.loadRegBaseDisp.word64 x24, [x19 + 0]
    arm64.b forhdr
  eachOutput:
    arm64.cmp x21, x22
    arm64.b.hs __im_slow
  __im_load:
    arm64.cmp x23, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseIndexScale.word64 x0, [x24 + x21*8 + 0]
    arm64.b tryok
  __im_stride:
    arm64.cmp x23, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.add x0, x24, x21
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok
  __im_slow:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  tryok:
    arm64.add x20, x20, x0
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.cmp x21, x22
    arm64.b.lo eachOutput
  forexit:
    arm64.leaRdata x0, __str_rec_10  ; "count="
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x19, [x19 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_11  ; " sum="
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x26, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x26
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_12  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot5, x0
    arm64.add x1, x22, x19
    arm64.add x1, x1, x25
    arm64.add x1, x1, x20
    arm64.add x0, x1, x0
    arm64.storeSlotReg slot3, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot4, x0
    arm64.add x28, x0, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x21, x28, x22
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x21, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x19, x19, x25
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x26
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x19, x20
    arm64.loadRegSlot x2, slot5
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot5
    arm64.add x0, x19, x0
    arm64.loadRegSlot x0, slot4
    arm64.storeBaseDispReg.word64 [x0 + 0], x28
    arm64.loadRegSlot x1, slot3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movRegReg x0, x23
    arm64.bl __mm_decref
    arm64.movRegReg x0, x26
    arm64.bl __mm_decref
  __il_body:
    arm64.loadRegSlot x0, slot4
    arm64.bl __write_stdout
  __il_cont:
    arm64.loadRegSlot x0, slot4
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.loadRegSlot x0, slot2
    arm64.bl __managed_decref
  exitdrop#8:
    arm64.loadRegSlot x0, slot1
    arm64.bl __managed_decref
  exitdrop#9:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_13  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:28: output index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
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

func @xorKeyStream {
  entry:
    arm64.prologue 80
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x3
    arm64.leaRdata x22, __layout_Array_Byte
  __il_body#51:
    arm64.movRegReg x0, x2
    arm64.bl __managed_retain
    arm64.movRegReg x23, x0
  __il_body#56:
    arm64.movRegReg x0, x23
    arm64.bl __managed_cursor_create
    arm64.movRegReg x24, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#57
  tryerr#58:
    arm64.movImm x24, 1
    arm64.movImm x25, 0
    arm64.b __il_cont#55
  tryok#57:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.movRegReg x0, x24
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x25 + 0], x24
    arm64.movRegReg x0, x24
    arm64.bl __managed_cursor_decref
  exitterminal#59:
    arm64.movImm x24, 0
  __il_cont#55:
    arm64.cmp x24, 0
    arm64.b.eq tryok#52
  tryerr#53:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
  exitterminal#54:
    arm64.movImm x23, 0
    arm64.movRegReg x0, x24
    arm64.b __il_cont#50
  tryok#52:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
    arm64.movImm x0, 0
    arm64.movRegReg x23, x25
  __il_cont#50:
    arm64.cmp x0, 0
    arm64.b.eq tryok#44
  tryerr#45:
    arm64.movImm x22, 0
    arm64.b __il_cont#42
  tryok#44:
    arm64.movRegReg x0, x23
    arm64.bl __mm_incref
    arm64.loadRegBaseDisp.word64 x0, [x22 + 80]
    arm64.loadRegBaseDisp.word64 x22, [x22 + 72]
  __il_body#48:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.bl __retain_type_param
    arm64.storeBaseDispReg.word64 [x24 + 0], x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.bl __release_type_param
  __il_cont#47:
    arm64.movRegReg x0, x23
    arm64.bl __destruct_ArrayIterator_T63315404e8d3fd14
  exitterminal#46:
    arm64.movImm x0, 0
    arm64.movRegReg x22, x24
  __il_cont#42:
    arm64.movImm x1, 0
    arm64.cmp x0, 0
    arm64.b.eq forhdr
    arm64.b forjoin
  eachByte:
    arm64.leaRdata x23, __witness_ArrayIterator_Byte.Iterator
    arm64.leaRdata x0, __layout_WithIterIterator_ArrayIterator_Byte_Byte
  __il_body#34:
    arm64.loadRegBaseDisp.word64 x1, [x22 + 0]
    arm64.bl __retain_type_param
    arm64.movRegReg x24, x0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x16, [x23 + 48]
    arm64.blr x16
    arm64.movRegReg x23, x0
  __il_body#35:
    arm64.loadRegBaseDisp.word64 x0, [x24 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#37
  __il_cont#32:
    arm64.add x1, x21, x0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x1, x0
    arm64.b.lo ifcont
  finished:
    arm64.movRegReg x0, x24
    arm64.bl __destruct_ArrayIterator_Byte
  exitdrop:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  exitterminal#7:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  ifcont:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x1, x0
    arm64.b.hs __im_slow#17
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 24]
    arm64.cmp x0, 8
    arm64.b.ne __im_stride#21
  __im_word#19:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
    arm64.b tryok#10
  __im_stride#21:
    arm64.cmp x0, 1
    arm64.b.ne __im_slow#17
  __im_byte#20:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.add x0, x0, x1
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok#10
  __im_slow#17:
    arm64.movRegReg x0, x20
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#11
  tryok#10:
    arm64.eor x1, x0, x23
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#14
  __rc_chk:
    arm64.cmp x1, 255
    arm64.b.gt __rc_panic#14
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow#24
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow#24
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow#24
  __im_viewed:
    arm64.sub x3, x2, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#24
  __im_store:
    arm64.loadRegBaseDisp.word64 x3, [x19 + 24]
    arm64.cmp x3, 8
    arm64.b.ne __im_stride#31
  __im_word#29:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.b __im_cont
  __im_stride#31:
    arm64.cmp x3, 1
    arm64.b.ne __im_slow#24
  __im_byte#30:
    arm64.add x2, x2, x0
    arm64.storeBaseDispReg.byte [x2 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.b __im_cont
  __im_slow#24:
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
  __im_cont:
    arm64.movRegReg x0, x24
    arm64.bl __destruct_ArrayIterator_Byte
  forstep:
    arm64.leaRdata x1, __witness_ArrayIterator_Byte.Iterator
  __il_body#39:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.loadRegBaseDisp.word64 x16, [x1 + 56]
    arm64.blr x16
    arm64.cmp x9, 0
    arm64.b.ne tryerr#41
  tryok#40:
    arm64.movImm x0, 0
    arm64.b __il_cont#38
  tryerr#41:
    arm64.movRegReg x0, x9
  __il_cont#38:
    arm64.movRegReg x1, x0
  forhdr:
    arm64.cmp x1, 0
    arm64.b.eq eachByte
  forexit:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  forjoin:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  __rc_panic#37:
    arm64.leaRdata x0, __str_blob_44  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  tryerr#11:
    arm64.leaRdata x0, __str_blob_9  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:12: data index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  __rc_panic#14:
    arm64.leaRdata x0, __str_blob_43  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:13: Range check failed: value outside typealias 'Byte'\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 144
    arm64.storeSlotReg slot15, x28
    arm64.storeSlotReg slot14, x27
    arm64.storeSlotReg slot13, x26
    arm64.storeSlotReg slot12, x25
    arm64.storeSlotReg slot11, x24
    arm64.storeSlotReg slot10, x23
    arm64.storeSlotReg slot9, x22
    arm64.storeSlotReg slot8, x21
    arm64.storeSlotReg slot7, x20
    arm64.storeSlotReg slot6, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot1, x20
    arm64.leaRdata x0, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x20 + 0], x0
    arm64.movImm x0, 4
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.movImm x0, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x20 + 24], x0
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot2, x21
    arm64.leaRdata x0, __barr_blob_1
    arm64.storeBaseDispReg.word64 [x21 + 0], x0
    arm64.movImm x0, 5
    arm64.storeBaseDispReg.word64 [x21 + 8], x0
    arm64.movImm x0, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x21 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x21 + 24], x0
    arm64.movImm x3, 0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl xorKeyStream
    arm64.movImm x3, 2
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl xorKeyStream
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.loadRegBaseDisp.word64 x22, [x19 + 8]
  __us_test:
    arm64.loadRegBaseDisp.word64 x23, [x19 + 24]
    arm64.loadRegBaseDisp.word64 x24, [x19 + 0]
    arm64.b forhdr
  eachOutput:
    arm64.cmp x21, x22
    arm64.b.hs __im_slow
  __im_load:
    arm64.cmp x23, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseIndexScale.word64 x0, [x24 + x21*8 + 0]
    arm64.b tryok
  __im_stride:
    arm64.cmp x23, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.add x0, x24, x21
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.b tryok
  __im_slow:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  tryok:
    arm64.add x20, x20, x0
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.cmp x21, x22
    arm64.b.lo eachOutput
  forexit:
    arm64.leaRdata x0, __str_rec_10  ; "count="
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x19, [x19 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_11  ; " sum="
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x26, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x26
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_12  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot5, x0
    arm64.add x1, x22, x19
    arm64.add x1, x1, x25
    arm64.add x1, x1, x20
    arm64.add x0, x1, x0
    arm64.storeSlotReg slot3, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot4, x0
    arm64.add x28, x0, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x21, x28, x22
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x21, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x19, x19, x25
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x26
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x19, x19, x20
    arm64.loadRegSlot x2, slot5
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot5
    arm64.add x0, x19, x0
    arm64.loadRegSlot x0, slot4
    arm64.storeBaseDispReg.word64 [x0 + 0], x28
    arm64.loadRegSlot x1, slot3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movRegReg x0, x23
    arm64.bl __mm_decref
    arm64.movRegReg x0, x26
    arm64.bl __mm_decref
  __il_body:
    arm64.loadRegSlot x0, slot4
    arm64.bl __write_stdout
  __il_cont:
    arm64.loadRegSlot x0, slot4
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.loadRegSlot x0, slot2
    arm64.bl __managed_decref
  exitdrop#8:
    arm64.loadRegSlot x0, slot1
    arm64.bl __managed_decref
  exitdrop#9:
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_13  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:28: output index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot6
    arm64.loadRegSlot x20, slot7
    arm64.loadRegSlot x21, slot8
    arm64.loadRegSlot x22, slot9
    arm64.loadRegSlot x23, slot10
    arm64.loadRegSlot x24, slot11
    arm64.loadRegSlot x25, slot12
    arm64.loadRegSlot x26, slot13
    arm64.loadRegSlot x27, slot14
    arm64.loadRegSlot x28, slot15
    arm64.epilogue 144
    arm64.ret
}
```

<!-- test: a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element -->
When two loops read `withIterator()` pairs, `WithIterIterator.current` is not spliced into either, and each
loop receives the pair from a call. The pair arrives in registers and its drop, on the early `return` and at
the end of the step alike, becomes the releases of its slots: neither loop holds an `__mm_alloc` call or a
destructor cascade for the pair. Prints `count=6 sum=61`.
```maxon
typealias ByteSum = int(0 to u64.max)

function xorKeyStream(output ByteArray, data ByteArray, keystream ByteArray, off ElementIndex)
	for (iter, pad) in keystream.withIterator() 'eachByte'
		let index = off + iter.index()

		if index >= data.count() 'finished'
			return
		end 'finished'

		let plain = try data.get(index) otherwise panic("data index unreachable")
		output.push(plain xor pad)
	end 'eachByte'
end 'xorKeyStream'

function sumWithPositions(values ByteArray) returns ByteSum
	var sum = 0 as ByteSum

	for (iter, value) in values.withIterator() 'eachValue'
		sum = sum + (value as ByteSum) + (iter.index() as ByteSum)
	end 'eachValue'

	return sum
end 'sumWithPositions'

function main() returns ExitCode
	var output = ByteArray.create()
	let data = [1 as Byte, 2, 3, 4]
	let keystream = [9 as Byte, 8, 7, 6, 5]

	xorKeyStream(output, data: data, keystream: keystream, off: 0)
	xorKeyStream(output, data: data, keystream: keystream, off: 2)
	print("count={output.count()} sum={sumWithPositions(output)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
count=6 sum=61
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

func @xorKeyStream {
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
    x64.movRegReg r13, r9
    x64.leaRegRdata rdx, [rip + __layout_Array_Byte]
    x64.movRegReg rcx, rax
    x64.callDirect Array.withIterator
    x64.movRegReg r14, r8
    x64.movRegImm32 rax, 0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, forhdr
    x64.jmp forjoin
  eachByte:
    x64.leaRegRdata rdx, [rip + __witness_ArrayIterator_Byte.Iterator]
    x64.leaRegRdata rax, [rip + __layout_WithIterIterator_ArrayIterator_Byte_Byte]
    x64.movRegReg rcx, r14
    x64.callDirect WithIterIterator.current
    x64.storeSlotReg slot0, r8
    x64.movRegReg r15, r10
  __il_body:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic#35
  __il_cont:
    x64.leaRegRegReg rdx, r13, rax
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc below, ifcont
  finished:
    x64.movRegReg rcx, r8
    x64.callDirect __destruct_ArrayIterator_Byte
  exitdrop:
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  exitterminal:
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#17
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r12 + 24]
    x64.cmpRegImm32 rax, 8
    x64.jcc notEqual, __im_stride#21
  __im_word#19:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rax + rdx*8 + 0]
    x64.jmp tryok
  __im_stride#21:
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, __im_slow#17
  __im_byte#20:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.leaRegRegReg rax, rax, rdx
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.jmp tryok
  __im_slow#17:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  critsplit:
    x64.movRegReg rax, r8
  tryok:
    x64.movRegReg rdx, rax
    x64.xorRegReg rdx, rax, r15
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __rc_panic#14
  __rc_chk:
    x64.cmpRegImm32 rdx, 255
    x64.jcc greater, __rc_panic#14
  __rc_ok:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#24
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegReg rax, rcx
    x64.jcc aboveEqual, __im_slow#24
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#24
  __im_viewed:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#24
  __im_store:
    x64.loadRegBaseDisp.word64 rsi, [rbx + 24]
    x64.cmpRegImm32 rsi, 8
    x64.jcc notEqual, __im_stride#31
  __im_word#29:
    x64.storeBaseIndexScaleReg.word64 [rcx + rax*8 + 0], rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.jmp __im_cont
  __im_stride#31:
    x64.cmpRegImm32 rsi, 1
    x64.jcc notEqual, __im_slow#24
  __im_byte#30:
    x64.leaRegRegReg rcx, rcx, rax
    x64.storeBaseDispReg.byte [rcx + 0], rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.jmp __im_cont
  __im_slow#24:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
  __im_cont:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __destruct_ArrayIterator_Byte
  forstep:
    x64.leaRegRdata rdx, [rip + __witness_ArrayIterator_Byte.Iterator]
    x64.movRegReg rcx, r14
    x64.callDirect WithIterIterator.advance
    x64.movRegReg rax, r10
  forhdr:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, eachByte
  forexit:
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  forjoin:
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#35:
    x64.leaRegRdata rcx, [rip + __str_blob_43]  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_9]  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:12: data index unreachable\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#14:
    x64.leaRegRdata rcx, [rip + __str_blob_42]  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:13: Range check failed: value outside typealias 'Byte'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 136
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r12 + 0], rax
    x64.movRegImm32 rax, 4
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.leaRegRdata rax, [rip + __barr_blob_1]
    x64.storeBaseDispReg.word64 [r13 + 0], rax
    x64.movRegImm32 rax, 5
    x64.storeBaseDispReg.word64 [r13 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r13 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r13 + 24], rax
    x64.movRegImm32 r9, 0
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect xorKeyStream
    x64.movRegImm32 r9, 2
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect xorKeyStream
    x64.leaRegRdata rax, [rip + __str_rec_10]  ; "count="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot11, rcx
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.loadRegBaseDisp.word64 r13, [rbx + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot10, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r8
    x64.callDirect __uint_to_string
    x64.movRegReg r13, r8
    x64.leaRegRdata rax, [rip + __str_rec_11]  ; " sum="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot9, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
  __il_body#6:
    x64.movRegImm32 r15, 0
    x64.leaRegRdata rdx, [rip + __layout_Array_Byte]
    x64.movRegReg rcx, rbx
    x64.callDirect Array.withIterator
    x64.movRegReg rbx, r8
    x64.movRegImm32 rax, 0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, forhdr
    x64.jmp __il_cont#5
  eachValue:
    x64.leaRegRdata rdx, [rip + __witness_ArrayIterator_Byte.Iterator]
    x64.leaRegRdata rax, [rip + __layout_WithIterIterator_ArrayIterator_Byte_Byte]
    x64.movRegReg rcx, rbx
    x64.callDirect WithIterIterator.current
    x64.leaRegRegReg rax, r15, r10
  __il_body#15:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic
  __il_cont#14:
    x64.leaRegRegReg rax, rax, rcx
    x64.storeSlotReg slot8, rax
    x64.movRegReg rcx, r8
    x64.callDirect __destruct_ArrayIterator_Byte
  forstep:
    x64.leaRegRdata rdx, [rip + __witness_ArrayIterator_Byte.Iterator]
    x64.movRegReg rcx, rbx
    x64.callDirect WithIterIterator.advance
    x64.loadRegSlot rax, slot8
    x64.movRegReg r15, rax
    x64.movRegReg rax, r10
  forhdr:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, eachValue
  forexit:
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  __il_cont#5:
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, rbx
    x64.callDirect __uint_to_string
    x64.storeSlotReg slot7, r8
    x64.leaRegRdata rax, [rip + __str_rec_12]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rcx, r12, r13
    x64.leaRegRegReg rcx, rcx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.loadRegSlot rdx, slot11
    x64.movRegReg rcx, r15
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r12
    x64.loadRegSlot rdx, slot10
    x64.movRegReg rcx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r12, r13
    x64.loadRegSlot rdx, slot9
    x64.movRegReg rcx, r12
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r12, r14
    x64.loadRegSlot rax, slot7
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot7
    x64.leaRegRegReg r12, r12, rax
    x64.loadRegSlot rax, slot5
    x64.loadRegSlot rdx, slot6
    x64.movRegReg rcx, r12
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.loadRegSlot rax, slot4
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot3
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot10
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
  __il_body#13:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __write_stdout
  __il_cont#4:
    x64.loadRegSlot rcx, slot4
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot2
    x64.callDirect __managed_decref
  exitdrop#2:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __managed_decref
  exitdrop#3:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 136
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_43]  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 136
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
