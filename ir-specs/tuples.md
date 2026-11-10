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
    x64.leaRegRdata rcx, [rip + __barr_rec_10]
    x64.movRegImm32 rbx, 0
    x64.leaRegRdata r12, [rip + __layout_Array_Tally]
  __il_body#32:
    x64.callDirect __managed_retain
    x64.movRegReg r13, r8
  __il_body#37:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cursor_create
    x64.movRegReg r14, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#38
  tryerr#39:
    x64.movRegImm32 r14, 1
    x64.movRegImm32 r15, 0
    x64.jmp __il_cont#36
  tryok#38:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r14
    x64.callDirect __mm_incref
    x64.storeBaseDispReg.word64 [r15 + 0], r14
    x64.movRegReg rcx, r14
    x64.callDirect __managed_cursor_decref
  exitterminal#40:
    x64.movRegImm32 r14, 0
  __il_cont#36:
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, tryok#33
  tryerr#34:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
  exitterminal#35:
    x64.movRegImm32 r13, 0
    x64.movRegReg rax, r14
    x64.jmp __il_cont#31
  tryok#33:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg r13, r15
  __il_cont#31:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#25
  tryerr#26:
    x64.movRegImm32 r12, 0
    x64.jmp __il_cont#23
  tryok#25:
    x64.movRegReg rcx, r13
    x64.callDirect __mm_incref
    x64.loadRegBaseDisp.word64 rax, [r12 + 88]
    x64.loadRegBaseDisp.word64 r12, [r12 + 80]
  __il_body#29:
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
  __il_cont#28:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_ArrayIterator_T63315404e8d3fd14
  exitterminal#27:
    x64.movRegImm32 rax, 0
    x64.movRegReg r12, r14
  __il_cont#23:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, forhdr
    x64.jmp forjoin
  __il_body#12:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#44:
    x64.leaRegRdata rdx, [rip + __str_rec_13]  ; "panic at Array.maxon:500: "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdi, [rsi + 8]
    x64.cmpRegReg rax, rdi
    x64.jcc aboveEqual, __im_slow#46
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
    x64.jcc notEqual, __im_slow#46
  __im_byte:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 0]
    x64.leaRegRegReg rax, rdi, rax
    x64.loadRegBaseDisp.byte rax, [rax + 0]
  __im_loaded:
    x64.loadRegBaseDisp.word64 rsi, [rsi + 40]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, critsplit#61
  __im_empty:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#62
  __im_slow#46:
    x64.callDirect __managed_cursor_current
    x64.jmp __il_cont#11
  critsplit#62:
    x64.movRegReg r8, rax
    x64.jmp __il_cont#11
  critsplit#61:
    x64.movRegReg r8, rax
  __il_cont#11:
    x64.leaRegRegReg rax, rbx, r8
    x64.cmpRegReg rax, rbx
    x64.jcc below, __rc_panic#8
  __il_body#20:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#22
  __il_cont#19:
    x64.leaRegRegReg rbx, rax, rcx
    x64.cmpRegReg rbx, rax
    x64.jcc below, __rc_panic#10
  __rc_ok:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_ArrayIterator_Tally
  __il_body#14:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#54:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegReg rax, rdx
    x64.jcc aboveEqual, __im_slow#58
    x64.jmp __im_move
  tryerr#56:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#53
  __im_move:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  tryok#55:
    x64.movRegImm32 rax, 0
  __il_cont#53:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __il_cont#13
  tryok#15:
    x64.movRegImm32 rax, 0
  __il_cont#13:
    x64.movRegReg rcx, rax
  forhdr:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __il_body#12
  forexit:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Tally_Tally
  forjoin:
    x64.leaRegRdata rax, [rip + __str_rec_11]  ; "sum="
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
    x64.leaRegRdata rax, [rip + __str_rec_12]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot3, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot2, rax
    x64.leaRegRegReg rcx, r13, rbx
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot0, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot1, r8
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
    x64.leaRegRegReg rcx, r12, rbx
    x64.loadRegSlot rax, slot2
    x64.loadRegSlot rdx, slot3
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot1
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot0
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
  __il_body#18:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __write_stdout
  __il_cont#17:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __str_decref
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#58:
    x64.callDirect __managed_cursor_advance
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#56
    x64.jmp tryok#55
  __rc_panic#8:
    x64.leaRegRdata rcx, [rip + __str_blob_45]  ; "panic at a-with-iterator-loop-over-an-array-allocates-nothing-per-element.test:9: Range check failed: value outside typealias 'Tally'\x0a"
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
  __rc_panic#22:
    x64.leaRegRdata rcx, [rip + __str_blob_46]  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
  __rc_panic#10:
    x64.leaRegRdata rcx, [rip + __str_blob_45]  ; "panic at a-with-iterator-loop-over-an-array-allocates-nothing-per-element.test:9: Range check failed: value outside typealias 'Tally'\x0a"
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
    x64.leaRegRdata rcx, [rip + __barr_rec_10]
    x64.movRegImm32 rbx, 0
    x64.leaRegRdata r12, [rip + __layout_Array_Tally]
  __il_body#32:
    x64.callDirect __managed_retain
    x64.movRegReg r13, r8
  __il_body#37:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cursor_create
    x64.movRegReg r14, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#38
  tryerr#39:
    x64.movRegImm32 r14, 1
    x64.movRegImm32 r15, 0
    x64.jmp __il_cont#36
  tryok#38:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r14
    x64.callDirect __mm_incref
    x64.storeBaseDispReg.word64 [r15 + 0], r14
    x64.movRegReg rcx, r14
    x64.callDirect __managed_cursor_decref
  exitterminal#40:
    x64.movRegImm32 r14, 0
  __il_cont#36:
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, tryok#33
  tryerr#34:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
  exitterminal#35:
    x64.movRegImm32 r13, 0
    x64.movRegReg rax, r14
    x64.jmp __il_cont#31
  tryok#33:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg r13, r15
  __il_cont#31:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#25
  tryerr#26:
    x64.movRegImm32 r12, 0
    x64.jmp __il_cont#23
  tryok#25:
    x64.movRegReg rcx, r13
    x64.callDirect __mm_incref
    x64.loadRegBaseDisp.word64 rax, [r12 + 88]
    x64.loadRegBaseDisp.word64 r12, [r12 + 80]
  __il_body#29:
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
  __il_cont#28:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_ArrayIterator_T63315404e8d3fd14
  exitterminal#27:
    x64.movRegImm32 rax, 0
    x64.movRegReg r12, r14
  __il_cont#23:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, forhdr
    x64.jmp forjoin
  __il_body#12:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#44:
    x64.leaRegRdata rdx, [rip + __str_rec_13]  ; "panic at Array.maxon:500: "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdi, [rsi + 8]
    x64.cmpRegReg rax, rdi
    x64.jcc aboveEqual, __im_slow#46
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
    x64.jcc notEqual, __im_slow#46
  __im_byte:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 0]
    x64.leaRegRegReg rax, rdi, rax
    x64.loadRegBaseDisp.byte rax, [rax + 0]
  __im_loaded:
    x64.loadRegBaseDisp.word64 rsi, [rsi + 40]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, critsplit#61
  __im_empty:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#62
  __im_slow#46:
    x64.callDirect __managed_cursor_current
    x64.jmp __il_cont#11
  critsplit#62:
    x64.movRegReg r8, rax
    x64.jmp __il_cont#11
  critsplit#61:
    x64.movRegReg r8, rax
  __il_cont#11:
    x64.leaRegRegReg rax, rbx, r8
    x64.cmpRegReg rax, rbx
    x64.jcc below, __rc_panic#8
  __il_body#20:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#22
  __il_cont#19:
    x64.leaRegRegReg rbx, rax, rcx
    x64.cmpRegReg rbx, rax
    x64.jcc below, __rc_panic#10
  __rc_ok:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_ArrayIterator_Tally
  __il_body#14:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#54:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegReg rax, rdx
    x64.jcc aboveEqual, __im_slow#58
    x64.jmp __im_move
  tryerr#56:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#53
  __im_move:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  tryok#55:
    x64.movRegImm32 rax, 0
  __il_cont#53:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __il_cont#13
  tryok#15:
    x64.movRegImm32 rax, 0
  __il_cont#13:
    x64.movRegReg rcx, rax
  forhdr:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __il_body#12
  forexit:
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Tally_Tally
  forjoin:
    x64.leaRegRdata rax, [rip + __str_rec_11]  ; "sum="
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
    x64.leaRegRdata rax, [rip + __str_rec_12]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot3, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot2, rax
    x64.leaRegRegReg rcx, r13, rbx
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot0, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot1, r8
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
    x64.leaRegRegReg rcx, r12, rbx
    x64.loadRegSlot rax, slot2
    x64.loadRegSlot rdx, slot3
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot1
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot0
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
  __il_body#18:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __write_stdout
  __il_cont#17:
    x64.loadRegSlot rcx, slot1
    x64.callDirect __str_decref
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#58:
    x64.callDirect __managed_cursor_advance
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#56
    x64.jmp tryok#55
  __rc_panic#8:
    x64.leaRegRdata rcx, [rip + __str_blob_45]  ; "panic at a-with-iterator-loop-over-an-array-allocates-nothing-per-element.test:9: Range check failed: value outside typealias 'Tally'\x0a"
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
  __rc_panic#22:
    x64.leaRegRdata rcx, [rip + __str_blob_46]  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
  __rc_panic#10:
    x64.leaRegRdata rcx, [rip + __str_blob_45]  ; "panic at a-with-iterator-loop-over-an-array-allocates-nothing-per-element.test:9: Range check failed: value outside typealias 'Tally'\x0a"
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
    arm64.storeSlotReg slot8, x27
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x0, __barr_rec_10
    arm64.movImm x19, 0
    arm64.leaRdata x20, __layout_Array_Tally
  __il_body#32:
    arm64.bl __managed_retain
    arm64.movRegReg x21, x0
  __il_body#37:
    arm64.movRegReg x0, x21
    arm64.bl __managed_cursor_create
    arm64.movRegReg x22, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#38
  tryerr#39:
    arm64.movImm x22, 1
    arm64.movImm x23, 0
    arm64.b __il_cont#36
  tryok#38:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x23 + 0], x22
    arm64.movRegReg x0, x22
    arm64.bl __managed_cursor_decref
  exitterminal#40:
    arm64.movImm x22, 0
  __il_cont#36:
    arm64.cmp x22, 0
    arm64.b.eq tryok#33
  tryerr#34:
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitterminal#35:
    arm64.movImm x21, 0
    arm64.movRegReg x0, x22
    arm64.b __il_cont#31
  tryok#33:
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.movImm x0, 0
    arm64.movRegReg x21, x23
  __il_cont#31:
    arm64.cmp x0, 0
    arm64.b.eq tryok#25
  tryerr#26:
    arm64.movImm x20, 0
    arm64.b __il_cont#23
  tryok#25:
    arm64.movRegReg x0, x21
    arm64.bl __mm_incref
    arm64.loadRegBaseDisp.word64 x0, [x20 + 88]
    arm64.loadRegBaseDisp.word64 x20, [x20 + 80]
  __il_body#29:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.bl __retain_type_param
    arm64.storeBaseDispReg.word64 [x22 + 0], x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.bl __release_type_param
  __il_cont#28:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_ArrayIterator_T63315404e8d3fd14
  exitterminal#27:
    arm64.movImm x0, 0
    arm64.movRegReg x20, x22
  __il_cont#23:
    arm64.movImm x1, 0
    arm64.cmp x0, 0
    arm64.b.eq forhdr
    arm64.b forjoin
  __il_body#12:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#44:
    arm64.leaRdata x1, __str_rec_13  ; "panic at Array.maxon:500: "
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x4, [x3 + 8]
    arm64.cmp x2, x4
    arm64.b.hs __im_slow#46
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
    arm64.b.ne __im_slow#46
  __im_byte:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.add x2, x4, x2
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
  __im_loaded:
    arm64.loadRegBaseDisp.word64 x3, [x3 + 40]
    arm64.cmp x3, 0
    arm64.b.eq critsplit#61
  __im_empty:
    arm64.cmp x2, 0
    arm64.b.ne critsplit#62
  __im_slow#46:
    arm64.bl __managed_cursor_current
    arm64.b __il_cont#11
  critsplit#62:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#11
  critsplit#61:
    arm64.movRegReg x0, x2
  __il_cont#11:
    arm64.add x0, x19, x0
    arm64.cmp x0, x19
    arm64.b.lo __rc_panic#8
  __il_body#20:
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#22
  __il_cont#19:
    arm64.add x19, x0, x1
    arm64.cmp x19, x0
    arm64.b.lo __rc_panic#10
  __rc_ok:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_ArrayIterator_Tally
  __il_body#14:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#54:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#58
    arm64.b __im_move
  tryerr#56:
    arm64.movImm x0, 1
    arm64.b __il_cont#53
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#55:
    arm64.movImm x0, 0
  __il_cont#53:
    arm64.cmp x0, 0
    arm64.b.ne __il_cont#13
  tryok#15:
    arm64.movImm x0, 0
  __il_cont#13:
    arm64.movRegReg x1, x0
  forhdr:
    arm64.cmp x1, 0
    arm64.b.eq __il_body#12
  forexit:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Tally_Tally
  forjoin:
    arm64.leaRdata x0, __str_rec_11  ; "sum="
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_12  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.add x0, x21, x19
    arm64.add x25, x0, x24
    arm64.add x0, x25, 57
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
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x0, x20, x19
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.storeBaseDispReg.word64 [x26 + 0], x27
    arm64.storeBaseDispReg.word64 [x26 + 8], x25
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x26 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x26 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
  __il_body#18:
    arm64.movRegReg x0, x26
    arm64.bl __write_stdout
  __il_cont#17:
    arm64.movRegReg x0, x26
    arm64.bl __str_decref
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
    arm64.epilogue 96
    arm64.ret
  __im_slow#58:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr#56
    arm64.b tryok#55
  __rc_panic#8:
    arm64.leaRdata x0, __str_blob_45  ; "panic at a-with-iterator-loop-over-an-array-allocates-nothing-per-element.test:9: Range check failed: value outside typealias 'Tally'\x0a"
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
    arm64.epilogue 96
    arm64.ret
  __rc_panic#22:
    arm64.leaRdata x0, __str_blob_46  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
    arm64.epilogue 96
    arm64.ret
  __rc_panic#10:
    arm64.leaRdata x0, __str_blob_45  ; "panic at a-with-iterator-loop-over-an-array-allocates-nothing-per-element.test:9: Range check failed: value outside typealias 'Tally'\x0a"
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
    arm64.storeSlotReg slot8, x27
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x0, __barr_rec_10
    arm64.movImm x19, 0
    arm64.leaRdata x20, __layout_Array_Tally
  __il_body#32:
    arm64.bl __managed_retain
    arm64.movRegReg x21, x0
  __il_body#37:
    arm64.movRegReg x0, x21
    arm64.bl __managed_cursor_create
    arm64.movRegReg x22, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#38
  tryerr#39:
    arm64.movImm x22, 1
    arm64.movImm x23, 0
    arm64.b __il_cont#36
  tryok#38:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x23 + 0], x22
    arm64.movRegReg x0, x22
    arm64.bl __managed_cursor_decref
  exitterminal#40:
    arm64.movImm x22, 0
  __il_cont#36:
    arm64.cmp x22, 0
    arm64.b.eq tryok#33
  tryerr#34:
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitterminal#35:
    arm64.movImm x21, 0
    arm64.movRegReg x0, x22
    arm64.b __il_cont#31
  tryok#33:
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
    arm64.movImm x0, 0
    arm64.movRegReg x21, x23
  __il_cont#31:
    arm64.cmp x0, 0
    arm64.b.eq tryok#25
  tryerr#26:
    arm64.movImm x20, 0
    arm64.b __il_cont#23
  tryok#25:
    arm64.movRegReg x0, x21
    arm64.bl __mm_incref
    arm64.loadRegBaseDisp.word64 x0, [x20 + 88]
    arm64.loadRegBaseDisp.word64 x20, [x20 + 80]
  __il_body#29:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.bl __retain_type_param
    arm64.storeBaseDispReg.word64 [x22 + 0], x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.bl __release_type_param
  __il_cont#28:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_ArrayIterator_T63315404e8d3fd14
  exitterminal#27:
    arm64.movImm x0, 0
    arm64.movRegReg x20, x22
  __il_cont#23:
    arm64.movImm x1, 0
    arm64.cmp x0, 0
    arm64.b.eq forhdr
    arm64.b forjoin
  __il_body#12:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#44:
    arm64.leaRdata x1, __str_rec_13  ; "panic at Array.maxon:500: "
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x4, [x3 + 8]
    arm64.cmp x2, x4
    arm64.b.hs __im_slow#46
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
    arm64.b.ne __im_slow#46
  __im_byte:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.add x2, x4, x2
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
  __im_loaded:
    arm64.loadRegBaseDisp.word64 x3, [x3 + 40]
    arm64.cmp x3, 0
    arm64.b.eq critsplit#61
  __im_empty:
    arm64.cmp x2, 0
    arm64.b.ne critsplit#62
  __im_slow#46:
    arm64.bl __managed_cursor_current
    arm64.b __il_cont#11
  critsplit#62:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#11
  critsplit#61:
    arm64.movRegReg x0, x2
  __il_cont#11:
    arm64.add x0, x19, x0
    arm64.cmp x0, x19
    arm64.b.lo __rc_panic#8
  __il_body#20:
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#22
  __il_cont#19:
    arm64.add x19, x0, x1
    arm64.cmp x19, x0
    arm64.b.lo __rc_panic#10
  __rc_ok:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_ArrayIterator_Tally
  __il_body#14:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
  __il_body#54:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#58
    arm64.b __im_move
  tryerr#56:
    arm64.movImm x0, 1
    arm64.b __il_cont#53
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#55:
    arm64.movImm x0, 0
  __il_cont#53:
    arm64.cmp x0, 0
    arm64.b.ne __il_cont#13
  tryok#15:
    arm64.movImm x0, 0
  __il_cont#13:
    arm64.movRegReg x1, x0
  forhdr:
    arm64.cmp x1, 0
    arm64.b.eq __il_body#12
  forexit:
    arm64.movRegReg x0, x20
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Tally_Tally
  forjoin:
    arm64.leaRdata x0, __str_rec_11  ; "sum="
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_12  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.add x0, x21, x19
    arm64.add x25, x0, x24
    arm64.add x0, x25, 57
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
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x0, x20, x19
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.storeBaseDispReg.word64 [x26 + 0], x27
    arm64.storeBaseDispReg.word64 [x26 + 8], x25
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x26 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x26 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
  __il_body#18:
    arm64.movRegReg x0, x26
    arm64.bl __write_stdout
  __il_cont#17:
    arm64.movRegReg x0, x26
    arm64.bl __str_decref
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
    arm64.epilogue 96
    arm64.ret
  __im_slow#58:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr#56
    arm64.b tryok#55
  __rc_panic#8:
    arm64.leaRdata x0, __str_blob_45  ; "panic at a-with-iterator-loop-over-an-array-allocates-nothing-per-element.test:9: Range check failed: value outside typealias 'Tally'\x0a"
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
    arm64.epilogue 96
    arm64.ret
  __rc_panic#22:
    arm64.leaRdata x0, __str_blob_46  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
    arm64.epilogue 96
    arm64.ret
  __rc_panic#10:
    arm64.leaRdata x0, __str_blob_45  ; "panic at a-with-iterator-loop-over-an-array-allocates-nothing-per-element.test:9: Range check failed: value outside typealias 'Tally'\x0a"
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
	var output = ByteArray.create(0)
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
  __il_body#46:
    x64.movRegReg rcx, rax
    x64.callDirect __managed_retain
    x64.movRegReg r13, r8
  __il_body#51:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cursor_create
    x64.movRegReg r14, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#52
  tryerr#53:
    x64.movRegImm32 r14, 1
    x64.movRegImm32 r15, 0
    x64.jmp __il_cont#50
  tryok#52:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r14
    x64.callDirect __mm_incref
    x64.storeBaseDispReg.word64 [r15 + 0], r14
    x64.movRegReg rcx, r14
    x64.callDirect __managed_cursor_decref
  exitterminal#54:
    x64.movRegImm32 r14, 0
  __il_cont#50:
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, tryok#47
  tryerr#48:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
  exitterminal#49:
    x64.movRegImm32 r13, 0
    x64.movRegReg rax, r14
    x64.jmp __il_cont#45
  tryok#47:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg r13, r15
  __il_cont#45:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#39
  tryerr#40:
    x64.movRegImm32 r12, 0
    x64.jmp __il_cont#37
  tryok#39:
    x64.movRegReg rcx, r13
    x64.callDirect __mm_incref
    x64.loadRegBaseDisp.word64 rax, [r12 + 88]
    x64.loadRegBaseDisp.word64 r12, [r12 + 80]
  __il_body#43:
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
  __il_cont#42:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_ArrayIterator_T63315404e8d3fd14
  exitterminal#41:
    x64.movRegImm32 rax, 0
    x64.movRegReg r12, r14
  __il_cont#37:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, forhdr
    x64.jmp forjoin
  __il_body#29:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#58:
    x64.leaRegRdata rdx, [rip + __str_rec_19]  ; "panic at Array.maxon:500: "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdi, [rsi + 8]
    x64.cmpRegReg rax, rdi
    x64.jcc aboveEqual, __im_slow#60
  __im_load#61:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 24]
    x64.cmpRegImm32 rdi, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rdi + rax*8 + 0]
    x64.jmp __im_loaded
  __im_stride:
    x64.cmpRegImm32 rdi, 1
    x64.jcc notEqual, __im_slow#60
  __im_byte:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 0]
    x64.leaRegRegReg rax, rdi, rax
    x64.loadRegBaseDisp.byte rax, [rax + 0]
  __im_loaded:
    x64.loadRegBaseDisp.word64 rsi, [rsi + 40]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, critsplit#76
  __im_empty:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#77
  __im_slow#60:
    x64.callDirect __managed_cursor_current
    x64.jmp __il_body#30
  critsplit#77:
    x64.movRegReg r8, rax
    x64.jmp __il_body#30
  critsplit#76:
    x64.movRegReg r8, rax
  __il_body#30:
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic#32
  __il_cont#27:
    x64.leaRegRegReg rdx, rbx, rax
    x64.cmpRegReg rdx, rbx
    x64.jcc below, __rc_panic#14
  __rc_ok#13:
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc below, ifcont
  finished:
    x64.movRegReg rcx, r13
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
    x64.jcc aboveEqual, __im_slow#19
  __im_load#20:
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegReg rax, rax, rdx
    x64.loadRegBaseDisp.byte rax, [rax + 0]
  tryok#10:
    x64.movRegReg rdx, rax
    x64.xorRegReg rdx, rax, r8
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __rc_panic#16
  __rc_chk:
    x64.cmpRegImm32 rdx, 255
    x64.jcc greater, __rc_panic#16
  __rc_ok#15:
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.loadRegBaseDisp.word64 rax, [rax + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __im_slow#22
  __im_bounds:
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rax, [rax + 16]
    x64.cmpRegReg rcx, rax
    x64.jcc aboveEqual, __im_slow#22
  __im_buffer:
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_slow#22
  __im_viewed:
    x64.leaRegRegImm32 rsi, rax, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#22
  __im_store:
    x64.leaRegRegReg rax, rax, rcx
    x64.storeBaseDispReg.byte [rax + 0], rdx
    x64.leaRegRegImm32 rax, rcx, 1
    x64.loadRegSlot rcx, slot0
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  __im_cont:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_ArrayIterator_Byte
  __il_body#34:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#68:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegReg rax, rdx
    x64.jcc aboveEqual, __im_slow#72
    x64.jmp __im_move
  tryerr#70:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#67
  __im_move:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  tryok#69:
    x64.movRegImm32 rax, 0
  __il_cont#67:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __il_cont#33
  tryok#35:
    x64.movRegImm32 rax, 0
  __il_cont#33:
    x64.movRegReg rcx, rax
  forhdr:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __il_body#29
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
  __im_slow#19:
    x64.loadRegSlot rcx, slot1
    x64.storeSlotReg slot2, r8
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot2
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#11
    x64.jmp tryok#10
  __im_slow#22:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_push
    x64.jmp __im_cont
  __im_slow#72:
    x64.callDirect __managed_cursor_advance
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#70
    x64.jmp tryok#69
  __rc_panic#32:
    x64.leaRegRdata rcx, [rip + __str_blob_54]  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
    x64.leaRegRdata rcx, [rip + __str_blob_51]  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:6: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
    x64.leaRegRdata rcx, [rip + __str_blob_12]  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:12: data index unreachable\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#16:
    x64.leaRegRdata rcx, [rip + __str_blob_52]  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:13: Range check failed: value outside typealias 'Byte'\x0a"
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
    x64.storeSlotReg slot0, rbx
    x64.leaRegRdata r12, [rip + __barr_rec_13]
    x64.leaRegRdata r13, [rip + __barr_rec_14]
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
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
  __us_test:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.jmp forhdr
  eachOutput:
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow
  __im_load:
    x64.leaRegRegReg rsi, rcx, rdx
    x64.loadRegBaseDisp.byte rsi, [rsi + 0]
  tryok:
    x64.leaRegRegReg rsi, r12, rsi
    x64.cmpRegReg rsi, r12
    x64.jcc below, __rc_panic
  forstep:
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.movRegReg r12, rsi
  forhdr:
    x64.cmpRegReg rdx, rax
    x64.jcc below, eachOutput
  forexit:
    x64.leaRegRdata rax, [rip + __str_rec_15]  ; "count="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot8, rcx
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot7, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r8
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_16]  ; " sum="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __uint_to_string
    x64.storeSlotReg slot5, r8
    x64.leaRegRdata rax, [rip + __str_rec_17]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot3, rax
    x64.leaRegRegReg rcx, r13, rbx
    x64.leaRegRegReg rcx, rcx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r13, r12, r13
    x64.loadRegSlot rdx, slot7
    x64.movRegReg rcx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r13, rbx
    x64.loadRegSlot rdx, slot6
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r14
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot3
    x64.loadRegSlot rdx, slot4
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot3
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rax + 0], r12
    x64.loadRegSlot rcx, slot1
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
    x64.loadRegSlot rcx, slot2
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 136
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot9, rax
    x64.storeSlotReg slot10, rcx
    x64.storeSlotReg slot11, rdx
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.loadRegSlot rax, slot9
    x64.loadRegSlot rcx, slot10
    x64.loadRegSlot rdx, slot11
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  critsplit:
    x64.movRegReg rsi, r8
    x64.jmp tryok
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_18]  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:28: output index unreachable\x0a"
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
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_53]  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:29: Range check failed: value outside typealias 'ByteSum'\x0a"
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
  __il_body#46:
    x64.movRegReg rcx, rax
    x64.callDirect __managed_retain
    x64.movRegReg r13, r8
  __il_body#51:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cursor_create
    x64.movRegReg r14, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#52
  tryerr#53:
    x64.movRegImm32 r14, 1
    x64.movRegImm32 r15, 0
    x64.jmp __il_cont#50
  tryok#52:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r14
    x64.callDirect __mm_incref
    x64.storeBaseDispReg.word64 [r15 + 0], r14
    x64.movRegReg rcx, r14
    x64.callDirect __managed_cursor_decref
  exitterminal#54:
    x64.movRegImm32 r14, 0
  __il_cont#50:
    x64.cmpRegImm32 r14, 0
    x64.jcc equal, tryok#47
  tryerr#48:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
  exitterminal#49:
    x64.movRegImm32 r13, 0
    x64.movRegReg rax, r14
    x64.jmp __il_cont#45
  tryok#47:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg r13, r15
  __il_cont#45:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#39
  tryerr#40:
    x64.movRegImm32 r12, 0
    x64.jmp __il_cont#37
  tryok#39:
    x64.movRegReg rcx, r13
    x64.callDirect __mm_incref
    x64.loadRegBaseDisp.word64 rax, [r12 + 88]
    x64.loadRegBaseDisp.word64 r12, [r12 + 80]
  __il_body#43:
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
  __il_cont#42:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_ArrayIterator_T63315404e8d3fd14
  exitterminal#41:
    x64.movRegImm32 rax, 0
    x64.movRegReg r12, r14
  __il_cont#37:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, forhdr
    x64.jmp forjoin
  __il_body#29:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#58:
    x64.leaRegRdata rdx, [rip + __str_rec_19]  ; "panic at Array.maxon:500: "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdi, [rsi + 8]
    x64.cmpRegReg rax, rdi
    x64.jcc aboveEqual, __im_slow#60
  __im_load#61:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 24]
    x64.cmpRegImm32 rdi, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 0]
    x64.loadRegBaseIndexScale.word64 rax, [rdi + rax*8 + 0]
    x64.jmp __im_loaded
  __im_stride:
    x64.cmpRegImm32 rdi, 1
    x64.jcc notEqual, __im_slow#60
  __im_byte:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 0]
    x64.leaRegRegReg rax, rdi, rax
    x64.loadRegBaseDisp.byte rax, [rax + 0]
  __im_loaded:
    x64.loadRegBaseDisp.word64 rsi, [rsi + 40]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, critsplit#76
  __im_empty:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#77
  __im_slow#60:
    x64.callDirect __managed_cursor_current
    x64.jmp __il_body#30
  critsplit#77:
    x64.movRegReg r8, rax
    x64.jmp __il_body#30
  critsplit#76:
    x64.movRegReg r8, rax
  __il_body#30:
    x64.loadRegBaseDisp.word64 rax, [r13 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic#32
  __il_cont#27:
    x64.leaRegRegReg rdx, rbx, rax
    x64.cmpRegReg rdx, rbx
    x64.jcc below, __rc_panic#14
  __rc_ok#13:
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc below, ifcont
  finished:
    x64.movRegReg rcx, r13
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
    x64.jcc aboveEqual, __im_slow#19
  __im_load#20:
    x64.loadRegSlot rax, slot1
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegReg rax, rax, rdx
    x64.loadRegBaseDisp.byte rax, [rax + 0]
  tryok#10:
    x64.movRegReg rdx, rax
    x64.xorRegReg rdx, rax, r8
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __rc_panic#16
  __rc_chk:
    x64.cmpRegImm32 rdx, 255
    x64.jcc greater, __rc_panic#16
  __rc_ok#15:
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rcx, [rax + 8]
    x64.loadRegBaseDisp.word64 rax, [rax + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __im_slow#22
  __im_bounds:
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rax, [rax + 16]
    x64.cmpRegReg rcx, rax
    x64.jcc aboveEqual, __im_slow#22
  __im_buffer:
    x64.loadRegSlot rax, slot0
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_slow#22
  __im_viewed:
    x64.leaRegRegImm32 rsi, rax, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#22
  __im_store:
    x64.leaRegRegReg rax, rax, rcx
    x64.storeBaseDispReg.byte [rax + 0], rdx
    x64.leaRegRegImm32 rax, rcx, 1
    x64.loadRegSlot rcx, slot0
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  __im_cont:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_ArrayIterator_Byte
  __il_body#34:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
  __il_body#68:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegReg rax, rdx
    x64.jcc aboveEqual, __im_slow#72
    x64.jmp __im_move
  tryerr#70:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#67
  __im_move:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  tryok#69:
    x64.movRegImm32 rax, 0
  __il_cont#67:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __il_cont#33
  tryok#35:
    x64.movRegImm32 rax, 0
  __il_cont#33:
    x64.movRegReg rcx, rax
  forhdr:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __il_body#29
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
  __im_slow#19:
    x64.loadRegSlot rcx, slot1
    x64.storeSlotReg slot2, r8
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot2
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#11
    x64.jmp tryok#10
  __im_slow#22:
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_push
    x64.jmp __im_cont
  __im_slow#72:
    x64.callDirect __managed_cursor_advance
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#70
    x64.jmp tryok#69
  __rc_panic#32:
    x64.leaRegRdata rcx, [rip + __str_blob_54]  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
    x64.leaRegRdata rcx, [rip + __str_blob_51]  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:6: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
    x64.leaRegRdata rcx, [rip + __str_blob_12]  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:12: data index unreachable\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#16:
    x64.leaRegRdata rcx, [rip + __str_blob_52]  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:13: Range check failed: value outside typealias 'Byte'\x0a"
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
    x64.storeSlotReg slot0, rbx
    x64.leaRegRdata r12, [rip + __barr_rec_13]
    x64.leaRegRdata r13, [rip + __barr_rec_14]
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
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
  __us_test:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.jmp forhdr
  eachOutput:
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow
  __im_load:
    x64.leaRegRegReg rsi, rcx, rdx
    x64.loadRegBaseDisp.byte rsi, [rsi + 0]
  tryok:
    x64.leaRegRegReg rsi, r12, rsi
    x64.cmpRegReg rsi, r12
    x64.jcc below, __rc_panic
  forstep:
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.movRegReg r12, rsi
  forhdr:
    x64.cmpRegReg rdx, rax
    x64.jcc below, eachOutput
  forexit:
    x64.leaRegRdata rax, [rip + __str_rec_15]  ; "count="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot8, rcx
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot7, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r8
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_16]  ; " sum="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __uint_to_string
    x64.storeSlotReg slot5, r8
    x64.leaRegRdata rax, [rip + __str_rec_17]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot3, rax
    x64.leaRegRegReg rcx, r13, rbx
    x64.leaRegRegReg rcx, rcx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r13, r12, r13
    x64.loadRegSlot rdx, slot7
    x64.movRegReg rcx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r13, rbx
    x64.loadRegSlot rdx, slot6
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r14
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r15
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot3
    x64.loadRegSlot rdx, slot4
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot3
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rax + 0], r12
    x64.loadRegSlot rcx, slot1
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
    x64.loadRegSlot rcx, slot2
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 136
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot9, rax
    x64.storeSlotReg slot10, rcx
    x64.storeSlotReg slot11, rdx
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.loadRegSlot rax, slot9
    x64.loadRegSlot rcx, slot10
    x64.loadRegSlot rdx, slot11
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  critsplit:
    x64.movRegReg rsi, r8
    x64.jmp tryok
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_18]  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:28: output index unreachable\x0a"
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
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_53]  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:29: Range check failed: value outside typealias 'ByteSum'\x0a"
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
    arm64.storeSlotReg slot7, x25
    arm64.storeSlotReg slot6, x24
    arm64.storeSlotReg slot5, x23
    arm64.storeSlotReg slot4, x22
    arm64.storeSlotReg slot3, x21
    arm64.storeSlotReg slot2, x20
    arm64.storeSlotReg slot1, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x3
    arm64.leaRdata x22, __layout_Array_Byte
  __il_body#46:
    arm64.movRegReg x0, x2
    arm64.bl __managed_retain
    arm64.movRegReg x23, x0
  __il_body#51:
    arm64.movRegReg x0, x23
    arm64.bl __managed_cursor_create
    arm64.movRegReg x24, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#52
  tryerr#53:
    arm64.movImm x24, 1
    arm64.movImm x25, 0
    arm64.b __il_cont#50
  tryok#52:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.movRegReg x0, x24
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x25 + 0], x24
    arm64.movRegReg x0, x24
    arm64.bl __managed_cursor_decref
  exitterminal#54:
    arm64.movImm x24, 0
  __il_cont#50:
    arm64.cmp x24, 0
    arm64.b.eq tryok#47
  tryerr#48:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
  exitterminal#49:
    arm64.movImm x23, 0
    arm64.movRegReg x0, x24
    arm64.b __il_cont#45
  tryok#47:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
    arm64.movImm x0, 0
    arm64.movRegReg x23, x25
  __il_cont#45:
    arm64.cmp x0, 0
    arm64.b.eq tryok#39
  tryerr#40:
    arm64.movImm x22, 0
    arm64.b __il_cont#37
  tryok#39:
    arm64.movRegReg x0, x23
    arm64.bl __mm_incref
    arm64.loadRegBaseDisp.word64 x0, [x22 + 88]
    arm64.loadRegBaseDisp.word64 x22, [x22 + 80]
  __il_body#43:
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
  __il_cont#42:
    arm64.movRegReg x0, x23
    arm64.bl __destruct_ArrayIterator_T63315404e8d3fd14
  exitterminal#41:
    arm64.movImm x0, 0
    arm64.movRegReg x22, x24
  __il_cont#37:
    arm64.movImm x1, 0
    arm64.cmp x0, 0
    arm64.b.eq forhdr
    arm64.b forjoin
  __il_body#29:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.bl __mm_retain
    arm64.movRegReg x23, x0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
  __il_body#58:
    arm64.leaRdata x1, __str_rec_19  ; "panic at Array.maxon:500: "
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x4, [x3 + 8]
    arm64.cmp x2, x4
    arm64.b.hs __im_slow#60
  __im_load#61:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 24]
    arm64.cmp x4, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.loadRegBaseIndexScale.word64 x2, [x4 + x2*8 + 0]
    arm64.b __im_loaded
  __im_stride:
    arm64.cmp x4, 1
    arm64.b.ne __im_slow#60
  __im_byte:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.add x2, x4, x2
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
  __im_loaded:
    arm64.loadRegBaseDisp.word64 x3, [x3 + 40]
    arm64.cmp x3, 0
    arm64.b.eq critsplit#76
  __im_empty:
    arm64.cmp x2, 0
    arm64.b.ne critsplit#77
  __im_slow#60:
    arm64.bl __managed_cursor_current
    arm64.b __il_body#30
  critsplit#77:
    arm64.movRegReg x0, x2
    arm64.b __il_body#30
  critsplit#76:
    arm64.movRegReg x0, x2
  __il_body#30:
    arm64.loadRegBaseDisp.word64 x1, [x23 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#32
  __il_cont#27:
    arm64.add x1, x21, x1
    arm64.cmp x1, x21
    arm64.b.lo __rc_panic#14
  __rc_ok#13:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 8]
    arm64.cmp x1, x2
    arm64.b.lo ifcont
  finished:
    arm64.movRegReg x0, x23
    arm64.bl __destruct_ArrayIterator_Byte
  exitdrop:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  exitterminal#7:
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.epilogue 80
    arm64.ret
  ifcont:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#19
  __im_load#20:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 0]
    arm64.add x1, x2, x1
    arm64.loadRegBaseDisp.byte x1, [x1 + 0]
  tryok#10:
    arm64.eor x1, x1, x0
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#16
  __rc_chk:
    arm64.cmp x1, 255
    arm64.b.gt __rc_panic#16
  __rc_ok#15:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow#22
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow#22
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow#22
  __im_viewed:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#22
  __im_store:
    arm64.add x2, x2, x0
    arm64.storeBaseDispReg.byte [x2 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
  __im_cont:
    arm64.movRegReg x0, x23
    arm64.bl __destruct_ArrayIterator_Byte
  __il_body#34:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
  __il_body#68:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#72
    arm64.b __im_move
  tryerr#70:
    arm64.movImm x0, 1
    arm64.b __il_cont#67
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#69:
    arm64.movImm x0, 0
  __il_cont#67:
    arm64.cmp x0, 0
    arm64.b.ne __il_cont#33
  tryok#35:
    arm64.movImm x0, 0
  __il_cont#33:
    arm64.movRegReg x1, x0
  forhdr:
    arm64.cmp x1, 0
    arm64.b.eq __il_body#29
  forexit:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  forjoin:
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.epilogue 80
    arm64.ret
  __im_slow#19:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#11
    arm64.b tryok#10
  __im_slow#22:
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.b __im_cont
  __im_slow#72:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr#70
    arm64.b tryok#69
  __rc_panic#32:
    arm64.leaRdata x0, __str_blob_54  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.epilogue 80
    arm64.ret
  __rc_panic#14:
    arm64.leaRdata x0, __str_blob_51  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:6: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.epilogue 80
    arm64.ret
  tryerr#11:
    arm64.leaRdata x0, __str_blob_12  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:12: data index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.epilogue 80
    arm64.ret
  __rc_panic#16:
    arm64.leaRdata x0, __str_blob_52  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:13: Range check failed: value outside typealias 'Byte'\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.epilogue 80
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 160
    arm64.storeSlotReg slot16, x28
    arm64.storeSlotReg slot15, x27
    arm64.storeSlotReg slot14, x26
    arm64.storeSlotReg slot13, x25
    arm64.storeSlotReg slot12, x24
    arm64.storeSlotReg slot11, x23
    arm64.storeSlotReg slot10, x22
    arm64.storeSlotReg slot9, x21
    arm64.storeSlotReg slot8, x20
    arm64.storeSlotReg slot7, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x20, __barr_rec_13
    arm64.leaRdata x21, __barr_rec_14
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
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
  __us_test:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.b forhdr
  eachOutput:
    arm64.cmp x1, x0
    arm64.b.hs __im_slow
  __im_load:
    arm64.add x3, x2, x1
    arm64.loadRegBaseDisp.byte x3, [x3 + 0]
  tryok:
    arm64.add x3, x20, x3
    arm64.cmp x3, x20
    arm64.b.lo __rc_panic
  forstep:
    arm64.add x1, x1, 1
    arm64.movRegReg x20, x3
  forhdr:
    arm64.cmp x1, x0
    arm64.b.lo eachOutput
  forexit:
    arm64.leaRdata x0, __str_rec_15  ; "count="
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
    arm64.leaRdata x0, __str_rec_16  ; " sum="
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
    arm64.leaRdata x0, __str_rec_17  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot3, x0
    arm64.add x1, x22, x19
    arm64.add x1, x1, x25
    arm64.add x1, x1, x20
    arm64.add x0, x1, x0
    arm64.storeSlotReg slot1, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot2, x0
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
    arm64.loadRegSlot x2, slot3
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot3
    arm64.add x0, x19, x0
    arm64.loadRegSlot x0, slot2
    arm64.storeBaseDispReg.word64 [x0 + 0], x28
    arm64.loadRegSlot x1, slot1
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
    arm64.loadRegSlot x0, slot2
    arm64.bl __write_stdout
  __il_cont:
    arm64.loadRegSlot x0, slot2
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot7
    arm64.loadRegSlot x20, slot8
    arm64.loadRegSlot x21, slot9
    arm64.loadRegSlot x22, slot10
    arm64.loadRegSlot x23, slot11
    arm64.loadRegSlot x24, slot12
    arm64.loadRegSlot x25, slot13
    arm64.loadRegSlot x26, slot14
    arm64.loadRegSlot x27, slot15
    arm64.loadRegSlot x28, slot16
    arm64.epilogue 160
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot4, x0
    arm64.storeSlotReg slot5, x1
    arm64.storeSlotReg slot6, x2
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot4
    arm64.loadRegSlot x1, slot5
    arm64.loadRegSlot x2, slot6
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.leaRdata x0, __str_blob_18  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:28: output index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot7
    arm64.loadRegSlot x20, slot8
    arm64.loadRegSlot x21, slot9
    arm64.loadRegSlot x22, slot10
    arm64.loadRegSlot x23, slot11
    arm64.loadRegSlot x24, slot12
    arm64.loadRegSlot x25, slot13
    arm64.loadRegSlot x26, slot14
    arm64.loadRegSlot x27, slot15
    arm64.loadRegSlot x28, slot16
    arm64.epilogue 160
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_53  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:29: Range check failed: value outside typealias 'ByteSum'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot7
    arm64.loadRegSlot x20, slot8
    arm64.loadRegSlot x21, slot9
    arm64.loadRegSlot x22, slot10
    arm64.loadRegSlot x23, slot11
    arm64.loadRegSlot x24, slot12
    arm64.loadRegSlot x25, slot13
    arm64.loadRegSlot x26, slot14
    arm64.loadRegSlot x27, slot15
    arm64.loadRegSlot x28, slot16
    arm64.epilogue 160
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
    arm64.storeSlotReg slot7, x25
    arm64.storeSlotReg slot6, x24
    arm64.storeSlotReg slot5, x23
    arm64.storeSlotReg slot4, x22
    arm64.storeSlotReg slot3, x21
    arm64.storeSlotReg slot2, x20
    arm64.storeSlotReg slot1, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x3
    arm64.leaRdata x22, __layout_Array_Byte
  __il_body#46:
    arm64.movRegReg x0, x2
    arm64.bl __managed_retain
    arm64.movRegReg x23, x0
  __il_body#51:
    arm64.movRegReg x0, x23
    arm64.bl __managed_cursor_create
    arm64.movRegReg x24, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#52
  tryerr#53:
    arm64.movImm x24, 1
    arm64.movImm x25, 0
    arm64.b __il_cont#50
  tryok#52:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.movRegReg x0, x24
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x25 + 0], x24
    arm64.movRegReg x0, x24
    arm64.bl __managed_cursor_decref
  exitterminal#54:
    arm64.movImm x24, 0
  __il_cont#50:
    arm64.cmp x24, 0
    arm64.b.eq tryok#47
  tryerr#48:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
  exitterminal#49:
    arm64.movImm x23, 0
    arm64.movRegReg x0, x24
    arm64.b __il_cont#45
  tryok#47:
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
    arm64.movImm x0, 0
    arm64.movRegReg x23, x25
  __il_cont#45:
    arm64.cmp x0, 0
    arm64.b.eq tryok#39
  tryerr#40:
    arm64.movImm x22, 0
    arm64.b __il_cont#37
  tryok#39:
    arm64.movRegReg x0, x23
    arm64.bl __mm_incref
    arm64.loadRegBaseDisp.word64 x0, [x22 + 88]
    arm64.loadRegBaseDisp.word64 x22, [x22 + 80]
  __il_body#43:
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
  __il_cont#42:
    arm64.movRegReg x0, x23
    arm64.bl __destruct_ArrayIterator_T63315404e8d3fd14
  exitterminal#41:
    arm64.movImm x0, 0
    arm64.movRegReg x22, x24
  __il_cont#37:
    arm64.movImm x1, 0
    arm64.cmp x0, 0
    arm64.b.eq forhdr
    arm64.b forjoin
  __il_body#29:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.bl __mm_retain
    arm64.movRegReg x23, x0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
  __il_body#58:
    arm64.leaRdata x1, __str_rec_19  ; "panic at Array.maxon:500: "
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x4, [x3 + 8]
    arm64.cmp x2, x4
    arm64.b.hs __im_slow#60
  __im_load#61:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 24]
    arm64.cmp x4, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.loadRegBaseIndexScale.word64 x2, [x4 + x2*8 + 0]
    arm64.b __im_loaded
  __im_stride:
    arm64.cmp x4, 1
    arm64.b.ne __im_slow#60
  __im_byte:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.add x2, x4, x2
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
  __im_loaded:
    arm64.loadRegBaseDisp.word64 x3, [x3 + 40]
    arm64.cmp x3, 0
    arm64.b.eq critsplit#76
  __im_empty:
    arm64.cmp x2, 0
    arm64.b.ne critsplit#77
  __im_slow#60:
    arm64.bl __managed_cursor_current
    arm64.b __il_body#30
  critsplit#77:
    arm64.movRegReg x0, x2
    arm64.b __il_body#30
  critsplit#76:
    arm64.movRegReg x0, x2
  __il_body#30:
    arm64.loadRegBaseDisp.word64 x1, [x23 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#32
  __il_cont#27:
    arm64.add x1, x21, x1
    arm64.cmp x1, x21
    arm64.b.lo __rc_panic#14
  __rc_ok#13:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 8]
    arm64.cmp x1, x2
    arm64.b.lo ifcont
  finished:
    arm64.movRegReg x0, x23
    arm64.bl __destruct_ArrayIterator_Byte
  exitdrop:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  exitterminal#7:
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.epilogue 80
    arm64.ret
  ifcont:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#19
  __im_load#20:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 0]
    arm64.add x1, x2, x1
    arm64.loadRegBaseDisp.byte x1, [x1 + 0]
  tryok#10:
    arm64.eor x1, x1, x0
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#16
  __rc_chk:
    arm64.cmp x1, 255
    arm64.b.gt __rc_panic#16
  __rc_ok#15:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_slow#22
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x0, x2
    arm64.b.hs __im_slow#22
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_slow#22
  __im_viewed:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#22
  __im_store:
    arm64.add x2, x2, x0
    arm64.storeBaseDispReg.byte [x2 + 0], x1
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
  __im_cont:
    arm64.movRegReg x0, x23
    arm64.bl __destruct_ArrayIterator_Byte
  __il_body#34:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
  __il_body#68:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#72
    arm64.b __im_move
  tryerr#70:
    arm64.movImm x0, 1
    arm64.b __il_cont#67
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#69:
    arm64.movImm x0, 0
  __il_cont#67:
    arm64.cmp x0, 0
    arm64.b.ne __il_cont#33
  tryok#35:
    arm64.movImm x0, 0
  __il_cont#33:
    arm64.movRegReg x1, x0
  forhdr:
    arm64.cmp x1, 0
    arm64.b.eq __il_body#29
  forexit:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  forjoin:
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.epilogue 80
    arm64.ret
  __im_slow#19:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#11
    arm64.b tryok#10
  __im_slow#22:
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.b __im_cont
  __im_slow#72:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr#70
    arm64.b tryok#69
  __rc_panic#32:
    arm64.leaRdata x0, __str_blob_54  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.epilogue 80
    arm64.ret
  __rc_panic#14:
    arm64.leaRdata x0, __str_blob_51  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:6: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.epilogue 80
    arm64.ret
  tryerr#11:
    arm64.leaRdata x0, __str_blob_12  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:12: data index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.epilogue 80
    arm64.ret
  __rc_panic#16:
    arm64.leaRdata x0, __str_blob_52  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:13: Range check failed: value outside typealias 'Byte'\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.epilogue 80
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 160
    arm64.storeSlotReg slot16, x28
    arm64.storeSlotReg slot15, x27
    arm64.storeSlotReg slot14, x26
    arm64.storeSlotReg slot13, x25
    arm64.storeSlotReg slot12, x24
    arm64.storeSlotReg slot11, x23
    arm64.storeSlotReg slot10, x22
    arm64.storeSlotReg slot9, x21
    arm64.storeSlotReg slot8, x20
    arm64.storeSlotReg slot7, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x20, __barr_rec_13
    arm64.leaRdata x21, __barr_rec_14
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
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
  __us_test:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.b forhdr
  eachOutput:
    arm64.cmp x1, x0
    arm64.b.hs __im_slow
  __im_load:
    arm64.add x3, x2, x1
    arm64.loadRegBaseDisp.byte x3, [x3 + 0]
  tryok:
    arm64.add x3, x20, x3
    arm64.cmp x3, x20
    arm64.b.lo __rc_panic
  forstep:
    arm64.add x1, x1, 1
    arm64.movRegReg x20, x3
  forhdr:
    arm64.cmp x1, x0
    arm64.b.lo eachOutput
  forexit:
    arm64.leaRdata x0, __str_rec_15  ; "count="
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
    arm64.leaRdata x0, __str_rec_16  ; " sum="
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
    arm64.leaRdata x0, __str_rec_17  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot3, x0
    arm64.add x1, x22, x19
    arm64.add x1, x1, x25
    arm64.add x1, x1, x20
    arm64.add x0, x1, x0
    arm64.storeSlotReg slot1, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot2, x0
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
    arm64.loadRegSlot x2, slot3
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot3
    arm64.add x0, x19, x0
    arm64.loadRegSlot x0, slot2
    arm64.storeBaseDispReg.word64 [x0 + 0], x28
    arm64.loadRegSlot x1, slot1
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
    arm64.loadRegSlot x0, slot2
    arm64.bl __write_stdout
  __il_cont:
    arm64.loadRegSlot x0, slot2
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot7
    arm64.loadRegSlot x20, slot8
    arm64.loadRegSlot x21, slot9
    arm64.loadRegSlot x22, slot10
    arm64.loadRegSlot x23, slot11
    arm64.loadRegSlot x24, slot12
    arm64.loadRegSlot x25, slot13
    arm64.loadRegSlot x26, slot14
    arm64.loadRegSlot x27, slot15
    arm64.loadRegSlot x28, slot16
    arm64.epilogue 160
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot4, x0
    arm64.storeSlotReg slot5, x1
    arm64.storeSlotReg slot6, x2
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot4
    arm64.loadRegSlot x1, slot5
    arm64.loadRegSlot x2, slot6
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.leaRdata x0, __str_blob_18  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:28: output index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot7
    arm64.loadRegSlot x20, slot8
    arm64.loadRegSlot x21, slot9
    arm64.loadRegSlot x22, slot10
    arm64.loadRegSlot x23, slot11
    arm64.loadRegSlot x24, slot12
    arm64.loadRegSlot x25, slot13
    arm64.loadRegSlot x26, slot14
    arm64.loadRegSlot x27, slot15
    arm64.loadRegSlot x28, slot16
    arm64.epilogue 160
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_53  ; "panic at a-with-iterator-loop-that-returns-early-allocates-nothing-per-element.test:29: Range check failed: value outside typealias 'ByteSum'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot7
    arm64.loadRegSlot x20, slot8
    arm64.loadRegSlot x21, slot9
    arm64.loadRegSlot x22, slot10
    arm64.loadRegSlot x23, slot11
    arm64.loadRegSlot x24, slot12
    arm64.loadRegSlot x25, slot13
    arm64.loadRegSlot x26, slot14
    arm64.loadRegSlot x27, slot15
    arm64.loadRegSlot x28, slot16
    arm64.epilogue 160
    arm64.ret
}
```

<!-- test: a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element -->
The ```RequiredRuntime block asks for `WithIterIterator.current`'s body, and a body a request names is spliced
by neither inlining rule, so each of the two loops reading `withIterator()` pairs receives its pair from a call
to the one shared `current`. That body returns the pair in registers, and the pair's drop, on the early
`return` and at the end of the step alike, becomes the releases of its slots: neither loop nor `current` holds
an `__mm_alloc` call or a destructor cascade for the pair. Prints `count=6 sum=61`.
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
	var output = ByteArray.create(0)
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
```RequiredRuntime
WithIterIterator.current
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
    x64.prologue 48
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
  __il_body#28:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic#30
  __il_cont#27:
    x64.leaRegRegReg rdx, r13, rax
    x64.cmpRegReg rdx, r13
    x64.jcc below, __rc_panic#14
  __rc_ok#13:
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
    x64.epilogue 48
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#19
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.leaRegRegReg rax, rax, rdx
    x64.loadRegBaseDisp.byte rax, [rax + 0]
  tryok#10:
    x64.movRegReg rdx, rax
    x64.xorRegReg rdx, rax, r10
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __rc_panic#16
  __rc_chk:
    x64.cmpRegImm32 rdx, 255
    x64.jcc greater, __rc_panic#16
  __rc_ok#15:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#22
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegReg rax, rcx
    x64.jcc aboveEqual, __im_slow#22
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#22
  __im_viewed:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#22
  __im_store:
    x64.leaRegRegReg rcx, rcx, rax
    x64.storeBaseDispReg.byte [rcx + 0], rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
  __im_cont:
    x64.movRegReg rcx, r8
    x64.callDirect __destruct_ArrayIterator_Byte
  __il_body#32:
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
  __il_body#36:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegReg rax, rdx
    x64.jcc aboveEqual, __im_slow#40
    x64.jmp __im_move
  tryerr#38:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#35
  __im_move:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  tryok#37:
    x64.movRegImm32 rax, 0
  __il_cont#35:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, forhdr
  tryok#33:
    x64.movRegImm32 rax, 0
  forhdr:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, eachByte
  forexit:
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  forjoin:
    x64.epilogue 48
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#19:
    x64.storeSlotReg slot0, r8
    x64.storeSlotReg slot1, r10
    x64.movRegReg rcx, r12
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.movRegReg rcx, r10
    x64.loadRegSlot r8, slot0
    x64.loadRegSlot r10, slot1
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, tryerr#11
    x64.jmp tryok#10
  __im_slow#22:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.jmp __im_cont
  __im_slow#40:
    x64.callDirect __managed_cursor_advance
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#38
    x64.jmp tryok#37
  __rc_panic#30:
    x64.leaRegRdata rcx, [rip + __str_blob_53]  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 48
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#14:
    x64.leaRegRdata rcx, [rip + __str_blob_50]  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:6: Range check failed: value outside typealias 'ElementIndex'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 48
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#11:
    x64.leaRegRdata rcx, [rip + __str_blob_12]  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:12: data index unreachable\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 48
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#16:
    x64.leaRegRdata rcx, [rip + __str_blob_51]  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:13: Range check failed: value outside typealias 'Byte'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 48
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
    x64.prologue 120
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.leaRegRdata r12, [rip + __barr_rec_13]
    x64.leaRegRdata r13, [rip + __barr_rec_14]
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
    x64.leaRegRdata rax, [rip + __str_rec_15]  ; "count="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot9, rcx
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.loadRegBaseDisp.word64 r13, [rbx + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot8, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r8
    x64.callDirect __uint_to_string
    x64.movRegReg r13, r8
    x64.leaRegRdata rax, [rip + __str_rec_16]  ; " sum="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot7, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
  __il_body#4:
    x64.movRegImm32 r15, 0
    x64.leaRegRdata rdx, [rip + __layout_Array_Byte]
    x64.movRegReg rcx, rbx
    x64.callDirect Array.withIterator
    x64.movRegReg rbx, r8
    x64.movRegImm32 rax, 0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, forhdr
    x64.jmp __il_cont#3
  eachValue:
    x64.leaRegRdata rdx, [rip + __witness_ArrayIterator_Byte.Iterator]
    x64.leaRegRdata rax, [rip + __layout_WithIterIterator_ArrayIterator_Byte_Byte]
    x64.movRegReg rcx, rbx
    x64.callDirect WithIterIterator.current
    x64.leaRegRegReg rax, r15, r10
    x64.cmpRegReg rax, r15
    x64.jcc below, __rc_panic#12
  __il_body#21:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#23
  __il_cont#20:
    x64.leaRegRegReg rcx, rax, rcx
    x64.storeSlotReg slot6, rcx
    x64.cmpRegReg rcx, rax
    x64.jcc below, __rc_panic#14
  __rc_ok:
    x64.movRegReg rcx, r8
    x64.callDirect __destruct_ArrayIterator_Byte
  __il_body#17:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
  __il_body#25:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegReg rax, rdx
    x64.jcc aboveEqual, __im_slow
    x64.jmp __im_move
  tryerr:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#24
  __im_move:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  tryok#26:
    x64.movRegImm32 rax, 0
  __il_cont#24:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __il_cont#16
  tryok#18:
    x64.movRegImm32 rax, 0
  __il_cont#16:
    x64.loadRegSlot rcx, slot6
    x64.movRegReg r15, rcx
  forhdr:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, eachValue
  forexit:
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  __il_cont#3:
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, rbx
    x64.callDirect __uint_to_string
    x64.storeSlotReg slot5, r8
    x64.leaRegRdata rax, [rip + __str_rec_17]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot3, rax
    x64.leaRegRegReg rcx, r12, r13
    x64.leaRegRegReg rcx, rcx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.loadRegSlot rdx, slot9
    x64.movRegReg rcx, r15
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r12
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r12, r13
    x64.loadRegSlot rdx, slot7
    x64.movRegReg rcx, r12
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r12, r14
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rcx, r12, rax
    x64.loadRegSlot rax, slot3
    x64.loadRegSlot rdx, slot4
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot8
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
  __il_body#15:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __write_stdout
  __il_cont#2:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_cursor_advance
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok#26
  __rc_panic#12:
    x64.leaRegRdata rcx, [rip + __str_blob_52]  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:21: Range check failed: value outside typealias 'ByteSum'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#23:
    x64.leaRegRdata rcx, [rip + __str_blob_53]  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#14:
    x64.leaRegRdata rcx, [rip + __str_blob_52]  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:21: Range check failed: value outside typealias 'ByteSum'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @WithIterIterator.current {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.movRegReg rcx, rax
    x64.callDirect __retain_type_param
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callMem [r12 + 48]
    x64.movRegReg rax, r8
    x64.movRegReg r8, r13
    x64.movRegReg r10, rax
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

func @xorKeyStream {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 48
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
  __il_body#28:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __rc_panic#30
  __il_cont#27:
    x64.leaRegRegReg rdx, r13, rax
    x64.cmpRegReg rdx, r13
    x64.jcc below, __rc_panic#14
  __rc_ok#13:
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
    x64.epilogue 48
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow#19
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.leaRegRegReg rax, rax, rdx
    x64.loadRegBaseDisp.byte rax, [rax + 0]
  tryok#10:
    x64.movRegReg rdx, rax
    x64.xorRegReg rdx, rax, r10
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __rc_panic#16
  __rc_chk:
    x64.cmpRegImm32 rdx, 255
    x64.jcc greater, __rc_panic#16
  __rc_ok#15:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#22
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegReg rax, rcx
    x64.jcc aboveEqual, __im_slow#22
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#22
  __im_viewed:
    x64.leaRegRegImm32 rsi, rcx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#22
  __im_store:
    x64.leaRegRegReg rcx, rcx, rax
    x64.storeBaseDispReg.byte [rcx + 0], rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
  __im_cont:
    x64.movRegReg rcx, r8
    x64.callDirect __destruct_ArrayIterator_Byte
  __il_body#32:
    x64.loadRegBaseDisp.word64 rax, [r14 + 0]
  __il_body#36:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegReg rax, rdx
    x64.jcc aboveEqual, __im_slow#40
    x64.jmp __im_move
  tryerr#38:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#35
  __im_move:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  tryok#37:
    x64.movRegImm32 rax, 0
  __il_cont#35:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, forhdr
  tryok#33:
    x64.movRegImm32 rax, 0
  forhdr:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, eachByte
  forexit:
    x64.movRegReg rcx, r14
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  forjoin:
    x64.epilogue 48
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#19:
    x64.storeSlotReg slot0, r8
    x64.storeSlotReg slot1, r10
    x64.movRegReg rcx, r12
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.movRegReg rcx, r10
    x64.loadRegSlot r8, slot0
    x64.loadRegSlot r10, slot1
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, tryerr#11
    x64.jmp tryok#10
  __im_slow#22:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.jmp __im_cont
  __im_slow#40:
    x64.callDirect __managed_cursor_advance
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#38
    x64.jmp tryok#37
  __rc_panic#30:
    x64.leaRegRdata rcx, [rip + __str_blob_53]  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 48
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#14:
    x64.leaRegRdata rcx, [rip + __str_blob_50]  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:6: Range check failed: value outside typealias 'ElementIndex'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 48
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#11:
    x64.leaRegRdata rcx, [rip + __str_blob_12]  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:12: data index unreachable\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 48
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#16:
    x64.leaRegRdata rcx, [rip + __str_blob_51]  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:13: Range check failed: value outside typealias 'Byte'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 48
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
    x64.prologue 120
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.leaRegRdata r12, [rip + __barr_rec_13]
    x64.leaRegRdata r13, [rip + __barr_rec_14]
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
    x64.leaRegRdata rax, [rip + __str_rec_15]  ; "count="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot9, rcx
    x64.loadRegBaseDisp.word64 r12, [rax + 8]
    x64.loadRegBaseDisp.word64 r13, [rbx + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot8, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r8
    x64.callDirect __uint_to_string
    x64.movRegReg r13, r8
    x64.leaRegRdata rax, [rip + __str_rec_16]  ; " sum="
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot7, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
  __il_body#4:
    x64.movRegImm32 r15, 0
    x64.leaRegRdata rdx, [rip + __layout_Array_Byte]
    x64.movRegReg rcx, rbx
    x64.callDirect Array.withIterator
    x64.movRegReg rbx, r8
    x64.movRegImm32 rax, 0
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, forhdr
    x64.jmp __il_cont#3
  eachValue:
    x64.leaRegRdata rdx, [rip + __witness_ArrayIterator_Byte.Iterator]
    x64.leaRegRdata rax, [rip + __layout_WithIterIterator_ArrayIterator_Byte_Byte]
    x64.movRegReg rcx, rbx
    x64.callDirect WithIterIterator.current
    x64.leaRegRegReg rax, r15, r10
    x64.cmpRegReg rax, r15
    x64.jcc below, __rc_panic#12
  __il_body#21:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#23
  __il_cont#20:
    x64.leaRegRegReg rcx, rax, rcx
    x64.storeSlotReg slot6, rcx
    x64.cmpRegReg rcx, rax
    x64.jcc below, __rc_panic#14
  __rc_ok:
    x64.movRegReg rcx, r8
    x64.callDirect __destruct_ArrayIterator_Byte
  __il_body#17:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
  __il_body#25:
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegReg rax, rdx
    x64.jcc aboveEqual, __im_slow
    x64.jmp __im_move
  tryerr:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#24
  __im_move:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  tryok#26:
    x64.movRegImm32 rax, 0
  __il_cont#24:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __il_cont#16
  tryok#18:
    x64.movRegImm32 rax, 0
  __il_cont#16:
    x64.loadRegSlot rcx, slot6
    x64.movRegReg r15, rcx
  forhdr:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, eachValue
  forexit:
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  __il_cont#3:
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, rbx
    x64.callDirect __uint_to_string
    x64.storeSlotReg slot5, r8
    x64.leaRegRdata rax, [rip + __str_rec_17]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot3, rax
    x64.leaRegRegReg rcx, r12, r13
    x64.leaRegRegReg rcx, rcx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.loadRegSlot rdx, slot9
    x64.movRegReg rcx, r15
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r12
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, r12
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r12, r13
    x64.loadRegSlot rdx, slot7
    x64.movRegReg rcx, r12
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r12, r14
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rcx, r12, rax
    x64.loadRegSlot rax, slot3
    x64.loadRegSlot rdx, slot4
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot8
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
  __il_body#15:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __write_stdout
  __il_cont#2:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_cursor_advance
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok#26
  __rc_panic#12:
    x64.leaRegRdata rcx, [rip + __str_blob_52]  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:21: Range check failed: value outside typealias 'ByteSum'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#23:
    x64.leaRegRdata rcx, [rip + __str_blob_53]  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#14:
    x64.leaRegRdata rcx, [rip + __str_blob_52]  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:21: Range check failed: value outside typealias 'ByteSum'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 120
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @WithIterIterator.current {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.movRegReg rcx, rax
    x64.callDirect __retain_type_param
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callMem [r12 + 48]
    x64.movRegReg rax, r8
    x64.movRegReg r8, r13
    x64.movRegReg r10, rax
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

func @xorKeyStream {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot5, x22
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x3
    arm64.leaRdata x1, __layout_Array_Byte
    arm64.movRegReg x0, x2
    arm64.bl Array.withIterator
    arm64.movRegReg x22, x0
    arm64.movImm x0, 0
    arm64.cmp x9, 0
    arm64.b.eq forhdr
    arm64.b forjoin
  eachByte:
    arm64.leaRdata x1, __witness_ArrayIterator_Byte.Iterator
    arm64.leaRdata x2, __layout_WithIterIterator_ArrayIterator_Byte_Byte
    arm64.movRegReg x0, x22
    arm64.bl WithIterIterator.current
  __il_body#28:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#30
  __il_cont#27:
    arm64.add x1, x21, x1
    arm64.cmp x1, x21
    arm64.b.lo __rc_panic#14
  __rc_ok#13:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 8]
    arm64.cmp x1, x2
    arm64.b.lo ifcont
  finished:
    arm64.bl __destruct_ArrayIterator_Byte
  exitdrop:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  exitterminal:
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.epilogue 64
    arm64.ret
  ifcont:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#19
  __im_load:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 0]
    arm64.add x1, x2, x1
    arm64.loadRegBaseDisp.byte x1, [x1 + 0]
  tryok#10:
    arm64.eor x1, x1, x9
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#16
  __rc_chk:
    arm64.cmp x1, 255
    arm64.b.gt __rc_panic#16
  __rc_ok#15:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow#22
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x3, [x19 + 16]
    arm64.cmp x2, x3
    arm64.b.hs __im_slow#22
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
    arm64.add x3, x3, x2
    arm64.storeBaseDispReg.byte [x3 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x1
  __im_cont:
    arm64.bl __destruct_ArrayIterator_Byte
  __il_body#32:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
  __il_body#36:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#40
    arm64.b __im_move
  tryerr#38:
    arm64.movImm x0, 1
    arm64.b __il_cont#35
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#37:
    arm64.movImm x0, 0
  __il_cont#35:
    arm64.cmp x0, 0
    arm64.b.ne forhdr
  tryok#33:
    arm64.movImm x0, 0
  forhdr:
    arm64.cmp x0, 0
    arm64.b.eq eachByte
  forexit:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  forjoin:
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.epilogue 64
    arm64.ret
  __im_slow#19:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x9
    arm64.movRegReg x0, x20
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.movRegReg x2, x9
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x9, slot1
    arm64.cmp x2, 0
    arm64.b.ne tryerr#11
    arm64.b tryok#10
  __im_slow#22:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b __im_cont
  __im_slow#40:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr#38
    arm64.b tryok#37
  __rc_panic#30:
    arm64.leaRdata x0, __str_blob_53  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.epilogue 64
    arm64.ret
  __rc_panic#14:
    arm64.leaRdata x0, __str_blob_50  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:6: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.epilogue 64
    arm64.ret
  tryerr#11:
    arm64.leaRdata x0, __str_blob_12  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:12: data index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.epilogue 64
    arm64.ret
  __rc_panic#16:
    arm64.leaRdata x0, __str_blob_51  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:13: Range check failed: value outside typealias 'Byte'\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.epilogue 64
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
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x20, __barr_rec_13
    arm64.leaRdata x21, __barr_rec_14
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
    arm64.leaRdata x0, __str_rec_15  ; "count="
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x22, [x19 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.bl __uint_to_string
    arm64.movRegReg x22, x0
    arm64.leaRdata x0, __str_rec_16  ; " sum="
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
  __il_body#4:
    arm64.movImm x26, 0
    arm64.leaRdata x1, __layout_Array_Byte
    arm64.movRegReg x0, x19
    arm64.bl Array.withIterator
    arm64.movRegReg x19, x0
    arm64.movImm x0, 0
    arm64.cmp x9, 0
    arm64.b.eq forhdr
    arm64.b __il_cont#3
  eachValue:
    arm64.leaRdata x1, __witness_ArrayIterator_Byte.Iterator
    arm64.leaRdata x2, __layout_WithIterIterator_ArrayIterator_Byte_Byte
    arm64.movRegReg x0, x19
    arm64.bl WithIterIterator.current
    arm64.add x1, x26, x9
    arm64.cmp x1, x26
    arm64.b.lo __rc_panic#12
  __il_body#21:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.lt __rc_panic#23
  __il_cont#20:
    arm64.add x26, x1, x2
    arm64.cmp x26, x1
    arm64.b.lo __rc_panic#14
  __rc_ok:
    arm64.bl __destruct_ArrayIterator_Byte
  __il_body#17:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
  __il_body#25:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
    arm64.b __im_move
  tryerr:
    arm64.movImm x0, 1
    arm64.b __il_cont#24
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#26:
    arm64.movImm x0, 0
  __il_cont#24:
    arm64.cmp x0, 0
    arm64.b.ne forhdr
  tryok#18:
    arm64.movImm x0, 0
  forhdr:
    arm64.cmp x0, 0
    arm64.b.eq eachValue
  forexit:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  __il_cont#3:
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x19
    arm64.bl __uint_to_string
    arm64.movRegReg x26, x0
    arm64.leaRdata x0, __str_rec_17  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot3, x0
    arm64.add x1, x21, x22
    arm64.add x1, x1, x25
    arm64.add x1, x1, x26
    arm64.add x0, x1, x0
    arm64.storeSlotReg slot1, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot2, x0
    arm64.add x28, x0, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x20, x28, x21
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x20, x20, x22
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x20, x20, x25
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x0, x20, x26
    arm64.loadRegSlot x2, slot3
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot2
    arm64.storeBaseDispReg.word64 [x0 + 0], x28
    arm64.loadRegSlot x1, slot1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movRegReg x0, x23
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
  __il_body#15:
    arm64.loadRegSlot x0, slot2
    arm64.bl __write_stdout
  __il_cont#2:
    arm64.loadRegSlot x0, slot2
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
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
  __im_slow:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok#26
  __rc_panic#12:
    arm64.leaRdata x0, __str_blob_52  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:21: Range check failed: value outside typealias 'ByteSum'\x0a"
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
  __rc_panic#23:
    arm64.leaRdata x0, __str_blob_53  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
  __rc_panic#14:
    arm64.leaRdata x0, __str_blob_52  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:21: Range check failed: value outside typealias 'ByteSum'\x0a"
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

func @WithIterIterator.current {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.movRegReg x0, x2
    arm64.bl __retain_type_param
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x16, [x20 + 48]
    arm64.blr x16
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x9, x1
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

func @xorKeyStream {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot5, x22
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x3
    arm64.leaRdata x1, __layout_Array_Byte
    arm64.movRegReg x0, x2
    arm64.bl Array.withIterator
    arm64.movRegReg x22, x0
    arm64.movImm x0, 0
    arm64.cmp x9, 0
    arm64.b.eq forhdr
    arm64.b forjoin
  eachByte:
    arm64.leaRdata x1, __witness_ArrayIterator_Byte.Iterator
    arm64.leaRdata x2, __layout_WithIterIterator_ArrayIterator_Byte_Byte
    arm64.movRegReg x0, x22
    arm64.bl WithIterIterator.current
  __il_body#28:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#30
  __il_cont#27:
    arm64.add x1, x21, x1
    arm64.cmp x1, x21
    arm64.b.lo __rc_panic#14
  __rc_ok#13:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 8]
    arm64.cmp x1, x2
    arm64.b.lo ifcont
  finished:
    arm64.bl __destruct_ArrayIterator_Byte
  exitdrop:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  exitterminal:
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.epilogue 64
    arm64.ret
  ifcont:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#19
  __im_load:
    arm64.loadRegBaseDisp.word64 x2, [x20 + 0]
    arm64.add x1, x2, x1
    arm64.loadRegBaseDisp.byte x1, [x1 + 0]
  tryok#10:
    arm64.eor x1, x1, x9
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#16
  __rc_chk:
    arm64.cmp x1, 255
    arm64.b.gt __rc_panic#16
  __rc_ok#15:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow#22
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x3, [x19 + 16]
    arm64.cmp x2, x3
    arm64.b.hs __im_slow#22
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
    arm64.add x3, x3, x2
    arm64.storeBaseDispReg.byte [x3 + 0], x1
    arm64.add x1, x2, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x1
  __im_cont:
    arm64.bl __destruct_ArrayIterator_Byte
  __il_body#32:
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
  __il_body#36:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#40
    arm64.b __im_move
  tryerr#38:
    arm64.movImm x0, 1
    arm64.b __il_cont#35
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#37:
    arm64.movImm x0, 0
  __il_cont#35:
    arm64.cmp x0, 0
    arm64.b.ne forhdr
  tryok#33:
    arm64.movImm x0, 0
  forhdr:
    arm64.cmp x0, 0
    arm64.b.eq eachByte
  forexit:
    arm64.movRegReg x0, x22
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  forjoin:
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.epilogue 64
    arm64.ret
  __im_slow#19:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x9
    arm64.movRegReg x0, x20
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.movRegReg x2, x9
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x9, slot1
    arm64.cmp x2, 0
    arm64.b.ne tryerr#11
    arm64.b tryok#10
  __im_slow#22:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b __im_cont
  __im_slow#40:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr#38
    arm64.b tryok#37
  __rc_panic#30:
    arm64.leaRdata x0, __str_blob_53  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.epilogue 64
    arm64.ret
  __rc_panic#14:
    arm64.leaRdata x0, __str_blob_50  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:6: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.epilogue 64
    arm64.ret
  tryerr#11:
    arm64.leaRdata x0, __str_blob_12  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:12: data index unreachable\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.epilogue 64
    arm64.ret
  __rc_panic#16:
    arm64.leaRdata x0, __str_blob_51  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:13: Range check failed: value outside typealias 'Byte'\x0a"
    arm64.bl mrt_panic
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.epilogue 64
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
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x20, __barr_rec_13
    arm64.leaRdata x21, __barr_rec_14
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
    arm64.leaRdata x0, __str_rec_15  ; "count="
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x22, [x19 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.bl __uint_to_string
    arm64.movRegReg x22, x0
    arm64.leaRdata x0, __str_rec_16  ; " sum="
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
  __il_body#4:
    arm64.movImm x26, 0
    arm64.leaRdata x1, __layout_Array_Byte
    arm64.movRegReg x0, x19
    arm64.bl Array.withIterator
    arm64.movRegReg x19, x0
    arm64.movImm x0, 0
    arm64.cmp x9, 0
    arm64.b.eq forhdr
    arm64.b __il_cont#3
  eachValue:
    arm64.leaRdata x1, __witness_ArrayIterator_Byte.Iterator
    arm64.leaRdata x2, __layout_WithIterIterator_ArrayIterator_Byte_Byte
    arm64.movRegReg x0, x19
    arm64.bl WithIterIterator.current
    arm64.add x1, x26, x9
    arm64.cmp x1, x26
    arm64.b.lo __rc_panic#12
  __il_body#21:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.lt __rc_panic#23
  __il_cont#20:
    arm64.add x26, x1, x2
    arm64.cmp x26, x1
    arm64.b.lo __rc_panic#14
  __rc_ok:
    arm64.bl __destruct_ArrayIterator_Byte
  __il_body#17:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
  __il_body#25:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
    arm64.b __im_move
  tryerr:
    arm64.movImm x0, 1
    arm64.b __il_cont#24
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#26:
    arm64.movImm x0, 0
  __il_cont#24:
    arm64.cmp x0, 0
    arm64.b.ne forhdr
  tryok#18:
    arm64.movImm x0, 0
  forhdr:
    arm64.cmp x0, 0
    arm64.b.eq eachValue
  forexit:
    arm64.movRegReg x0, x19
    arm64.bl __destruct_WithIterIterator_ArrayIterator_Byte_Byte
  __il_cont#3:
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x19
    arm64.bl __uint_to_string
    arm64.movRegReg x26, x0
    arm64.leaRdata x0, __str_rec_17  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x27, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.storeSlotReg slot3, x0
    arm64.add x1, x21, x22
    arm64.add x1, x1, x25
    arm64.add x1, x1, x26
    arm64.add x0, x1, x0
    arm64.storeSlotReg slot1, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeSlotReg slot2, x0
    arm64.add x28, x0, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x20, x28, x21
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x20, x20, x22
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x20, x20, x25
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x0, x20, x26
    arm64.loadRegSlot x2, slot3
    arm64.movRegReg x1, x27
    arm64.bl __str_copy
    arm64.loadRegSlot x0, slot2
    arm64.storeBaseDispReg.word64 [x0 + 0], x28
    arm64.loadRegSlot x1, slot1
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movRegReg x0, x23
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
  __il_body#15:
    arm64.loadRegSlot x0, slot2
    arm64.bl __write_stdout
  __il_cont#2:
    arm64.loadRegSlot x0, slot2
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.loadRegSlot x0, slot0
    arm64.bl __managed_decref
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
  __im_slow:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok#26
  __rc_panic#12:
    arm64.leaRdata x0, __str_blob_52  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:21: Range check failed: value outside typealias 'ByteSum'\x0a"
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
  __rc_panic#23:
    arm64.leaRdata x0, __str_blob_53  ; "panic at Array.maxon:504: Range check failed: value outside typealias 'ElementIndex'\x0a"
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
  __rc_panic#14:
    arm64.leaRdata x0, __str_blob_52  ; "panic at a-with-iterator-loop-calling-the-shared-current-allocates-nothing-per-element.test:21: Range check failed: value outside typealias 'ByteSum'\x0a"
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

func @WithIterIterator.current {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.movRegReg x0, x2
    arm64.bl __retain_type_param
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x16, [x20 + 48]
    arm64.blr x16
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x9, x1
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: a-pair-from-a-function-holding-standing-calls-returns-in-registers -->
`twin` returns a pair built from two calls to `churn`, which is far past the leaf budget and stays a call, so `twin` is no leaf and is not spliced. It still returns its pair in the two return registers: the pin holds no `__mm_alloc` in `twin`, and the loop in `main` reads both halves out of the registers. `checkedTwin` forwards `twin`'s pair out of a throwing return, which cannot ride registers, so the record is built there from the two registers it receives and `twin` is not held to the heap convention for it.
```maxon
typealias Word = bits(64)
typealias WordPair = (Word, Word)

enum PairError implements Error
	tooBig
end 'PairError'

function churn(x Word) returns Word
	var acc = x
	acc = acc xor (acc shl 13)
	acc = acc xor (acc shr 7)
	acc = acc xor (acc shl 17)
	acc = acc + 0x9E3779B97F4A7C15
	acc = acc xor (acc shr 11)
	acc = acc xor (acc shl 5)
	acc = acc + 0x7F4A7C159E3779B9
	acc = acc xor (acc shr 3)
	acc = acc xor (acc shl 29)
	acc = acc + 0x1234567890ABCDEF
	acc = acc xor (acc shr 19)
	return acc
end 'churn'

function twin(a Word, b Word) returns WordPair
	let first = churn(a) xor ((a shl 3) + (b shr 5)) xor ((a shr 7) + (b shl 11)) xor ((a shl 13) + (b shr 17))
	let second = churn(b shr 8) xor ((b shl 3) + (a shr 5)) xor ((b shr 7) + (a shl 11)) xor ((b shl 13) + (a shr 17))

	return (first, second)
end 'twin'

function checkedTwin(a Word, b Word) returns WordPair throws PairError
	if a == 77 or b == 78 or a == 79 or b == 80 or a == 81 or b == 82 'tooBig'
		throw PairError.tooBig
	end 'tooBig'

	return twin(a, b: b)
end 'checkedTwin'

function main() returns ExitCode
	var lo = 1 as Word
	var hi = 2 as Word

	for _ in 0 upto 1000 'rounds'
		let (x, y) = twin(lo, b: hi)
		lo = y
		hi = x
	end 'rounds'

	let (p, q) = try checkedTwin(hi, b: lo) otherwise panic("unreachable")
	let (r, s) = try checkedTwin(lo, b: hi) otherwise panic("unreachable")

	return ((p xor q xor r xor s) and 127) as ExitCode
end 'main'
```
```exitcode
63
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

func @churn {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.movRegReg rax, rcx
    x64.shlRegImm8 rax, rcx, 13
    x64.xorRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.shrRegImm8 rcx, rax, 7
    x64.xorRegReg rcx, rcx, rax
    x64.movRegReg rax, rcx
    x64.shlRegImm8 rax, rcx, 17
    x64.xorRegReg rax, rax, rcx
    x64.movRegImm rcx, 11400714819323198485
    x64.leaRegRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.shrRegImm8 rcx, rax, 11
    x64.xorRegReg rcx, rcx, rax
    x64.movRegReg rax, rcx
    x64.shlRegImm8 rax, rcx, 5
    x64.xorRegReg rax, rax, rcx
    x64.movRegImm rcx, 9172280023384029625
    x64.leaRegRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.shrRegImm8 rcx, rax, 3
    x64.xorRegReg rcx, rcx, rax
    x64.movRegReg rax, rcx
    x64.shlRegImm8 rax, rcx, 29
    x64.xorRegReg rax, rax, rcx
    x64.movRegImm rcx, 1311768467294899695
    x64.leaRegRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.shrRegImm8 rcx, rax, 19
    x64.movRegReg r8, rcx
    x64.xorRegReg r8, rcx, rax
    x64.popReg rbp
    x64.ret
}

func @twin {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.movRegReg rcx, rbx
    x64.callDirect churn
    x64.movRegReg rax, rbx
    x64.shlRegImm8 rax, rbx, 3
    x64.movRegReg rcx, r12
    x64.shrRegImm8 rcx, r12, 5
    x64.leaRegRegReg rax, rax, rcx
    x64.xorRegReg r8, r8, rax
    x64.movRegReg rax, rbx
    x64.shrRegImm8 rax, rbx, 7
    x64.movRegReg rcx, r12
    x64.shlRegImm8 rcx, r12, 11
    x64.leaRegRegReg rax, rax, rcx
    x64.xorRegReg r8, r8, rax
    x64.movRegReg rax, rbx
    x64.shlRegImm8 rax, rbx, 13
    x64.movRegReg rcx, r12
    x64.shrRegImm8 rcx, r12, 17
    x64.leaRegRegReg rax, rax, rcx
    x64.movRegReg r13, r8
    x64.xorRegReg r13, r8, rax
    x64.movRegReg rcx, r12
    x64.shrRegImm8 rcx, r12, 8
    x64.callDirect churn
    x64.movRegReg rax, r12
    x64.shlRegImm8 rax, r12, 3
    x64.movRegReg rcx, rbx
    x64.shrRegImm8 rcx, rbx, 5
    x64.leaRegRegReg rax, rax, rcx
    x64.xorRegReg r8, r8, rax
    x64.movRegReg rax, r12
    x64.shrRegImm8 rax, r12, 7
    x64.movRegReg rcx, rbx
    x64.shlRegImm8 rcx, rbx, 11
    x64.leaRegRegReg rax, rax, rcx
    x64.xorRegReg r8, r8, rax
    x64.shlRegImm8 r12, r12, 13
    x64.shrRegImm8 rbx, rbx, 17
    x64.leaRegRegReg rax, r12, rbx
    x64.movRegReg r10, r8
    x64.xorRegReg r10, r8, rax
    x64.movRegReg r8, r13
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @checkedTwin {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.cmpRegImm32 rcx, 77
    x64.movRegImm32 rax, 1
    x64.jcc equal, scmerge#2
  orrhs#1:
    x64.cmpRegImm32 rdx, 78
    x64.setccReg equal, rax
  scmerge#2:
    x64.movRegImm32 rsi, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#14
  orrhs#3:
    x64.cmpRegImm32 rcx, 79
    x64.setccReg equal, rax
    x64.jmp scmerge#4
  critsplit#14:
    x64.movRegReg rax, rsi
  scmerge#4:
    x64.movRegImm32 rsi, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#15
  orrhs#5:
    x64.cmpRegImm32 rdx, 80
    x64.setccReg equal, rax
    x64.jmp scmerge#6
  critsplit#15:
    x64.movRegReg rax, rsi
  scmerge#6:
    x64.movRegImm32 rsi, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#16
  orrhs#7:
    x64.cmpRegImm32 rcx, 81
    x64.setccReg equal, rax
    x64.jmp scmerge#8
  critsplit#16:
    x64.movRegReg rax, rsi
  scmerge#8:
    x64.movRegImm32 rsi, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#17
  orrhs#9:
    x64.cmpRegImm32 rdx, 82
    x64.setccReg equal, rax
    x64.jmp scmerge#10
  critsplit#17:
    x64.movRegReg rax, rsi
  scmerge#10:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  tooBig:
    x64.movRegImm32 r10, 1
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.callDirect twin
    x64.movRegReg rbx, r8
    x64.movRegReg r12, r10
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
    x64.storeBaseDispReg.word64 [r8 + 8], r12
    x64.movRegImm32 r10, 0
    x64.epilogue 32
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
    x64.prologue 40
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 r12, 2
    x64.movRegImm32 r13, 0
    x64.jmp forhdr
  rounds:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect twin
  forstep:
    x64.leaRegRegImm32 r13, r13, 1
    x64.movRegReg rbx, r10
    x64.movRegReg r12, r8
  forhdr:
    x64.cmpRegImm32 r13, 1000
    x64.jcc less, rounds
  forexit:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect checkedTwin
    x64.movRegReg r13, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#6
  tryok#5:
    x64.loadRegBaseDisp.word64 r14, [r13 + 0]
    x64.loadRegBaseDisp.word64 r15, [r13 + 8]
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect checkedTwin
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#8
  tryok#7:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word64 rcx, [r8 + 8]
    x64.xorRegReg r14, r14, r15
    x64.xorRegReg r14, r14, rax
    x64.xorRegReg r14, r14, rcx
    x64.andRegImm32 r14, r14, 127
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
  exitdrop:
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
  exitterminal:
    x64.movRegReg r8, r14
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#6:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-pair-from-a-function-holding-standing-calls-returns-in-registers.test:50: unreachable\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#8:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-pair-from-a-function-holding-standing-calls-returns-in-registers.test:51: unreachable\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
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

func @churn {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.movRegReg rax, rcx
    x64.shlRegImm8 rax, rcx, 13
    x64.xorRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.shrRegImm8 rcx, rax, 7
    x64.xorRegReg rcx, rcx, rax
    x64.movRegReg rax, rcx
    x64.shlRegImm8 rax, rcx, 17
    x64.xorRegReg rax, rax, rcx
    x64.movRegImm rcx, 11400714819323198485
    x64.leaRegRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.shrRegImm8 rcx, rax, 11
    x64.xorRegReg rcx, rcx, rax
    x64.movRegReg rax, rcx
    x64.shlRegImm8 rax, rcx, 5
    x64.xorRegReg rax, rax, rcx
    x64.movRegImm rcx, 9172280023384029625
    x64.leaRegRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.shrRegImm8 rcx, rax, 3
    x64.xorRegReg rcx, rcx, rax
    x64.movRegReg rax, rcx
    x64.shlRegImm8 rax, rcx, 29
    x64.xorRegReg rax, rax, rcx
    x64.movRegImm rcx, 1311768467294899695
    x64.leaRegRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.shrRegImm8 rcx, rax, 19
    x64.movRegReg r8, rcx
    x64.xorRegReg r8, rcx, rax
    x64.popReg rbp
    x64.ret
}

func @twin {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.movRegReg rcx, rbx
    x64.callDirect churn
    x64.movRegReg rax, rbx
    x64.shlRegImm8 rax, rbx, 3
    x64.movRegReg rcx, r12
    x64.shrRegImm8 rcx, r12, 5
    x64.leaRegRegReg rax, rax, rcx
    x64.xorRegReg r8, r8, rax
    x64.movRegReg rax, rbx
    x64.shrRegImm8 rax, rbx, 7
    x64.movRegReg rcx, r12
    x64.shlRegImm8 rcx, r12, 11
    x64.leaRegRegReg rax, rax, rcx
    x64.xorRegReg r8, r8, rax
    x64.movRegReg rax, rbx
    x64.shlRegImm8 rax, rbx, 13
    x64.movRegReg rcx, r12
    x64.shrRegImm8 rcx, r12, 17
    x64.leaRegRegReg rax, rax, rcx
    x64.movRegReg r13, r8
    x64.xorRegReg r13, r8, rax
    x64.movRegReg rcx, r12
    x64.shrRegImm8 rcx, r12, 8
    x64.callDirect churn
    x64.movRegReg rax, r12
    x64.shlRegImm8 rax, r12, 3
    x64.movRegReg rcx, rbx
    x64.shrRegImm8 rcx, rbx, 5
    x64.leaRegRegReg rax, rax, rcx
    x64.xorRegReg r8, r8, rax
    x64.movRegReg rax, r12
    x64.shrRegImm8 rax, r12, 7
    x64.movRegReg rcx, rbx
    x64.shlRegImm8 rcx, rbx, 11
    x64.leaRegRegReg rax, rax, rcx
    x64.xorRegReg r8, r8, rax
    x64.shlRegImm8 r12, r12, 13
    x64.shrRegImm8 rbx, rbx, 17
    x64.leaRegRegReg rax, r12, rbx
    x64.movRegReg r10, r8
    x64.xorRegReg r10, r8, rax
    x64.movRegReg r8, r13
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @checkedTwin {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.cmpRegImm32 rcx, 77
    x64.movRegImm32 rax, 1
    x64.jcc equal, scmerge#2
  orrhs#1:
    x64.cmpRegImm32 rdx, 78
    x64.setccReg equal, rax
  scmerge#2:
    x64.movRegImm32 rsi, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#14
  orrhs#3:
    x64.cmpRegImm32 rcx, 79
    x64.setccReg equal, rax
    x64.jmp scmerge#4
  critsplit#14:
    x64.movRegReg rax, rsi
  scmerge#4:
    x64.movRegImm32 rsi, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#15
  orrhs#5:
    x64.cmpRegImm32 rdx, 80
    x64.setccReg equal, rax
    x64.jmp scmerge#6
  critsplit#15:
    x64.movRegReg rax, rsi
  scmerge#6:
    x64.movRegImm32 rsi, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#16
  orrhs#7:
    x64.cmpRegImm32 rcx, 81
    x64.setccReg equal, rax
    x64.jmp scmerge#8
  critsplit#16:
    x64.movRegReg rax, rsi
  scmerge#8:
    x64.movRegImm32 rsi, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#17
  orrhs#9:
    x64.cmpRegImm32 rdx, 82
    x64.setccReg equal, rax
    x64.jmp scmerge#10
  critsplit#17:
    x64.movRegReg rax, rsi
  scmerge#10:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  tooBig:
    x64.movRegImm32 r10, 1
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.callDirect twin
    x64.movRegReg rbx, r8
    x64.movRegReg r12, r10
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
    x64.storeBaseDispReg.word64 [r8 + 8], r12
    x64.movRegImm32 r10, 0
    x64.epilogue 32
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
    x64.prologue 40
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 r12, 2
    x64.movRegImm32 r13, 0
    x64.jmp forhdr
  rounds:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect twin
  forstep:
    x64.leaRegRegImm32 r13, r13, 1
    x64.movRegReg rbx, r10
    x64.movRegReg r12, r8
  forhdr:
    x64.cmpRegImm32 r13, 1000
    x64.jcc less, rounds
  forexit:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect checkedTwin
    x64.movRegReg r13, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#6
  tryok#5:
    x64.loadRegBaseDisp.word64 r14, [r13 + 0]
    x64.loadRegBaseDisp.word64 r15, [r13 + 8]
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect checkedTwin
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#8
  tryok#7:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word64 rcx, [r8 + 8]
    x64.xorRegReg r14, r14, r15
    x64.xorRegReg r14, r14, rax
    x64.xorRegReg r14, r14, rcx
    x64.andRegImm32 r14, r14, 127
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
  exitdrop:
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
  exitterminal:
    x64.movRegReg r8, r14
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#6:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-pair-from-a-function-holding-standing-calls-returns-in-registers.test:50: unreachable\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#8:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-pair-from-a-function-holding-standing-calls-returns-in-registers.test:51: unreachable\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
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

func @churn {
  entry:
    arm64.prologue 16
    arm64.lsl x1, x0, 13
    arm64.eor x0, x0, x1
    arm64.lsr x1, x0, 7
    arm64.eor x0, x0, x1
    arm64.lsl x1, x0, 17
    arm64.eor x0, x0, x1
    arm64.movImm x1, 11400714819323198485
    arm64.add x0, x0, x1
    arm64.lsr x1, x0, 11
    arm64.eor x0, x0, x1
    arm64.lsl x1, x0, 5
    arm64.eor x0, x0, x1
    arm64.movImm x1, 9172280023384029625
    arm64.add x0, x0, x1
    arm64.lsr x1, x0, 3
    arm64.eor x0, x0, x1
    arm64.lsl x1, x0, 29
    arm64.eor x0, x0, x1
    arm64.movImm x1, 1311768467294899695
    arm64.add x0, x0, x1
    arm64.lsr x1, x0, 19
    arm64.eor x0, x0, x1
    arm64.epilogue 16
    arm64.ret
}

func @twin {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x0, x19
    arm64.bl churn
    arm64.lsl x1, x19, 3
    arm64.lsr x2, x20, 5
    arm64.add x1, x1, x2
    arm64.eor x0, x0, x1
    arm64.lsr x1, x19, 7
    arm64.lsl x2, x20, 11
    arm64.add x1, x1, x2
    arm64.eor x0, x0, x1
    arm64.lsl x1, x19, 13
    arm64.lsr x2, x20, 17
    arm64.add x1, x1, x2
    arm64.eor x21, x0, x1
    arm64.lsr x0, x20, 8
    arm64.bl churn
    arm64.lsl x1, x20, 3
    arm64.lsr x2, x19, 5
    arm64.add x1, x1, x2
    arm64.eor x0, x0, x1
    arm64.lsr x1, x20, 7
    arm64.lsl x2, x19, 11
    arm64.add x1, x1, x2
    arm64.eor x0, x0, x1
    arm64.lsl x1, x20, 13
    arm64.lsr x2, x19, 17
    arm64.add x1, x1, x2
    arm64.eor x9, x0, x1
    arm64.movRegReg x0, x21
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @checkedTwin {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.cmp x0, 77
    arm64.movImm x2, 1
    arm64.b.eq scmerge#2
  orrhs#1:
    arm64.cmp x1, 78
    arm64.cset x2, eq
  scmerge#2:
    arm64.movImm x3, 1
    arm64.cbnz x2, critsplit#14
  orrhs#3:
    arm64.cmp x0, 79
    arm64.cset x2, eq
    arm64.b scmerge#4
  critsplit#14:
    arm64.movRegReg x2, x3
  scmerge#4:
    arm64.movImm x3, 1
    arm64.cbnz x2, critsplit#15
  orrhs#5:
    arm64.cmp x1, 80
    arm64.cset x2, eq
    arm64.b scmerge#6
  critsplit#15:
    arm64.movRegReg x2, x3
  scmerge#6:
    arm64.movImm x3, 1
    arm64.cbnz x2, critsplit#16
  orrhs#7:
    arm64.cmp x0, 81
    arm64.cset x2, eq
    arm64.b scmerge#8
  critsplit#16:
    arm64.movRegReg x2, x3
  scmerge#8:
    arm64.movImm x3, 1
    arm64.cbnz x2, critsplit#17
  orrhs#9:
    arm64.cmp x1, 82
    arm64.cset x2, eq
    arm64.b scmerge#10
  critsplit#17:
    arm64.movRegReg x2, x3
  scmerge#10:
    arm64.cbz x2, ifcont
  tooBig:
    arm64.movImm x9, 1
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  ifcont:
    arm64.bl twin
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x9
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x19
    arm64.storeBaseDispReg.word64 [x0 + 8], x20
    arm64.movImm x9, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 1
    arm64.movImm x20, 2
    arm64.movImm x21, 0
    arm64.b forhdr
  rounds:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl twin
  forstep:
    arm64.add x21, x21, 1
    arm64.movRegReg x19, x9
    arm64.movRegReg x20, x0
  forhdr:
    arm64.cmp x21, 1000
    arm64.b.lt rounds
  forexit:
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl checkedTwin
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#6
  tryok#5:
    arm64.loadRegBaseDisp.word64 x22, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x21 + 8]
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl checkedTwin
    arm64.cmp x9, 0
    arm64.b.ne tryerr#8
  tryok#7:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.eor x3, x22, x23
    arm64.eor x1, x3, x1
    arm64.eor x1, x1, x2
    arm64.and x19, x1, 127
    arm64.bl __mm_decref
  exitdrop:
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
  exitterminal:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  tryerr#6:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-pair-from-a-function-holding-standing-calls-returns-in-registers.test:50: unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  tryerr#8:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-pair-from-a-function-holding-standing-calls-returns-in-registers.test:51: unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
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

func @churn {
  entry:
    arm64.prologue 16
    arm64.lsl x1, x0, 13
    arm64.eor x0, x0, x1
    arm64.lsr x1, x0, 7
    arm64.eor x0, x0, x1
    arm64.lsl x1, x0, 17
    arm64.eor x0, x0, x1
    arm64.movImm x1, 11400714819323198485
    arm64.add x0, x0, x1
    arm64.lsr x1, x0, 11
    arm64.eor x0, x0, x1
    arm64.lsl x1, x0, 5
    arm64.eor x0, x0, x1
    arm64.movImm x1, 9172280023384029625
    arm64.add x0, x0, x1
    arm64.lsr x1, x0, 3
    arm64.eor x0, x0, x1
    arm64.lsl x1, x0, 29
    arm64.eor x0, x0, x1
    arm64.movImm x1, 1311768467294899695
    arm64.add x0, x0, x1
    arm64.lsr x1, x0, 19
    arm64.eor x0, x0, x1
    arm64.epilogue 16
    arm64.ret
}

func @twin {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x0, x19
    arm64.bl churn
    arm64.lsl x1, x19, 3
    arm64.lsr x2, x20, 5
    arm64.add x1, x1, x2
    arm64.eor x0, x0, x1
    arm64.lsr x1, x19, 7
    arm64.lsl x2, x20, 11
    arm64.add x1, x1, x2
    arm64.eor x0, x0, x1
    arm64.lsl x1, x19, 13
    arm64.lsr x2, x20, 17
    arm64.add x1, x1, x2
    arm64.eor x21, x0, x1
    arm64.lsr x0, x20, 8
    arm64.bl churn
    arm64.lsl x1, x20, 3
    arm64.lsr x2, x19, 5
    arm64.add x1, x1, x2
    arm64.eor x0, x0, x1
    arm64.lsr x1, x20, 7
    arm64.lsl x2, x19, 11
    arm64.add x1, x1, x2
    arm64.eor x0, x0, x1
    arm64.lsl x1, x20, 13
    arm64.lsr x2, x19, 17
    arm64.add x1, x1, x2
    arm64.eor x9, x0, x1
    arm64.movRegReg x0, x21
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @checkedTwin {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.cmp x0, 77
    arm64.movImm x2, 1
    arm64.b.eq scmerge#2
  orrhs#1:
    arm64.cmp x1, 78
    arm64.cset x2, eq
  scmerge#2:
    arm64.movImm x3, 1
    arm64.cbnz x2, critsplit#14
  orrhs#3:
    arm64.cmp x0, 79
    arm64.cset x2, eq
    arm64.b scmerge#4
  critsplit#14:
    arm64.movRegReg x2, x3
  scmerge#4:
    arm64.movImm x3, 1
    arm64.cbnz x2, critsplit#15
  orrhs#5:
    arm64.cmp x1, 80
    arm64.cset x2, eq
    arm64.b scmerge#6
  critsplit#15:
    arm64.movRegReg x2, x3
  scmerge#6:
    arm64.movImm x3, 1
    arm64.cbnz x2, critsplit#16
  orrhs#7:
    arm64.cmp x0, 81
    arm64.cset x2, eq
    arm64.b scmerge#8
  critsplit#16:
    arm64.movRegReg x2, x3
  scmerge#8:
    arm64.movImm x3, 1
    arm64.cbnz x2, critsplit#17
  orrhs#9:
    arm64.cmp x1, 82
    arm64.cset x2, eq
    arm64.b scmerge#10
  critsplit#17:
    arm64.movRegReg x2, x3
  scmerge#10:
    arm64.cbz x2, ifcont
  tooBig:
    arm64.movImm x9, 1
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  ifcont:
    arm64.bl twin
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x9
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x19
    arm64.storeBaseDispReg.word64 [x0 + 8], x20
    arm64.movImm x9, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 1
    arm64.movImm x20, 2
    arm64.movImm x21, 0
    arm64.b forhdr
  rounds:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl twin
  forstep:
    arm64.add x21, x21, 1
    arm64.movRegReg x19, x9
    arm64.movRegReg x20, x0
  forhdr:
    arm64.cmp x21, 1000
    arm64.b.lt rounds
  forexit:
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl checkedTwin
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#6
  tryok#5:
    arm64.loadRegBaseDisp.word64 x22, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x21 + 8]
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl checkedTwin
    arm64.cmp x9, 0
    arm64.b.ne tryerr#8
  tryok#7:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.eor x3, x22, x23
    arm64.eor x1, x3, x1
    arm64.eor x1, x1, x2
    arm64.and x19, x1, 127
    arm64.bl __mm_decref
  exitdrop:
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
  exitterminal:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  tryerr#6:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-pair-from-a-function-holding-standing-calls-returns-in-registers.test:50: unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  tryerr#8:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-pair-from-a-function-holding-standing-calls-returns-in-registers.test:51: unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
}
```

<!-- test: a-pair-whose-only-caller-is-a-throwing-forwarder-keeps-its-heap-record -->
`twin` is called from `checkedTwin` alone, and `checkedTwin` forwards the pair out of a throwing return, which cannot ride registers. The one site of `twin` would build a record from the two registers it receives, so nothing is gained by taking registers out of `twin`: the pin holds the `__mm_alloc` and the two half stores in `twin`, and none in `checkedTwin`.
```maxon
typealias Word = bits(64)
typealias WordPair = (Word, Word)

enum PairError implements Error
	tooBig
end 'PairError'

function churn(x Word) returns Word
	var acc = x
	acc = acc xor (acc shl 13)
	acc = acc xor (acc shr 7)
	acc = acc xor (acc shl 17)
	acc = acc + 0x9E3779B97F4A7C15
	acc = acc xor (acc shr 11)
	acc = acc xor (acc shl 5)
	acc = acc + 0x7F4A7C159E3779B9
	acc = acc xor (acc shr 3)
	acc = acc xor (acc shl 29)
	acc = acc + 0x1234567890ABCDEF
	acc = acc xor (acc shr 19)
	return acc
end 'churn'

function twin(a Word, b Word) returns WordPair
	let first = churn(a) xor ((a shl 3) + (b shr 5)) xor ((a shr 7) + (b shl 11)) xor ((a shl 13) + (b shr 17))
	let second = churn(b shr 8) xor ((b shl 3) + (a shr 5)) xor ((b shr 7) + (a shl 11)) xor ((b shl 13) + (a shr 17))

	return (first, second)
end 'twin'

function checkedTwin(a Word, b Word) returns WordPair throws PairError
	if a == 77 or b == 78 or a == 79 or b == 80 or a == 81 or b == 82 'tooBig'
		throw PairError.tooBig
	end 'tooBig'

	if a == 5 'swapped'
		return twin(b, b: a)
	end 'swapped'

	return twin(a, b: b)
end 'checkedTwin'

function main() returns ExitCode
	var lo = 1 as Word
	var hi = 2 as Word

	for _ in 0 upto 1000 'rounds'
		let (x, y) = try checkedTwin(lo, b: hi) otherwise panic("unreachable")
		lo = y
		hi = x
	end 'rounds'

	let (p, q) = try checkedTwin(hi, b: lo) otherwise panic("unreachable")

	return ((lo xor hi xor p xor q) and 127) as ExitCode
end 'main'
```
```exitcode
102
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

func @churn {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.movRegReg rax, rcx
    x64.shlRegImm8 rax, rcx, 13
    x64.xorRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.shrRegImm8 rcx, rax, 7
    x64.xorRegReg rcx, rcx, rax
    x64.movRegReg rax, rcx
    x64.shlRegImm8 rax, rcx, 17
    x64.xorRegReg rax, rax, rcx
    x64.movRegImm rcx, 11400714819323198485
    x64.leaRegRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.shrRegImm8 rcx, rax, 11
    x64.xorRegReg rcx, rcx, rax
    x64.movRegReg rax, rcx
    x64.shlRegImm8 rax, rcx, 5
    x64.xorRegReg rax, rax, rcx
    x64.movRegImm rcx, 9172280023384029625
    x64.leaRegRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.shrRegImm8 rcx, rax, 3
    x64.xorRegReg rcx, rcx, rax
    x64.movRegReg rax, rcx
    x64.shlRegImm8 rax, rcx, 29
    x64.xorRegReg rax, rax, rcx
    x64.movRegImm rcx, 1311768467294899695
    x64.leaRegRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.shrRegImm8 rcx, rax, 19
    x64.movRegReg r8, rcx
    x64.xorRegReg r8, rcx, rax
    x64.popReg rbp
    x64.ret
}

func @twin {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.movRegReg rcx, rbx
    x64.callDirect churn
    x64.movRegReg rax, rbx
    x64.shlRegImm8 rax, rbx, 3
    x64.movRegReg rcx, r12
    x64.shrRegImm8 rcx, r12, 5
    x64.leaRegRegReg rax, rax, rcx
    x64.xorRegReg r8, r8, rax
    x64.movRegReg rax, rbx
    x64.shrRegImm8 rax, rbx, 7
    x64.movRegReg rcx, r12
    x64.shlRegImm8 rcx, r12, 11
    x64.leaRegRegReg rax, rax, rcx
    x64.xorRegReg r8, r8, rax
    x64.movRegReg rax, rbx
    x64.shlRegImm8 rax, rbx, 13
    x64.movRegReg rcx, r12
    x64.shrRegImm8 rcx, r12, 17
    x64.leaRegRegReg rax, rax, rcx
    x64.movRegReg r13, r8
    x64.xorRegReg r13, r8, rax
    x64.movRegReg rcx, r12
    x64.shrRegImm8 rcx, r12, 8
    x64.callDirect churn
    x64.movRegReg rax, r12
    x64.shlRegImm8 rax, r12, 3
    x64.movRegReg rcx, rbx
    x64.shrRegImm8 rcx, rbx, 5
    x64.leaRegRegReg rax, rax, rcx
    x64.xorRegReg r8, r8, rax
    x64.movRegReg rax, r12
    x64.shrRegImm8 rax, r12, 7
    x64.movRegReg rcx, rbx
    x64.shlRegImm8 rcx, rbx, 11
    x64.leaRegRegReg rax, rax, rcx
    x64.xorRegReg r8, r8, rax
    x64.shlRegImm8 r12, r12, 13
    x64.shrRegImm8 rbx, rbx, 17
    x64.leaRegRegReg rax, r12, rbx
    x64.movRegReg rbx, r8
    x64.xorRegReg rbx, r8, rax
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], r13
    x64.storeBaseDispReg.word64 [r8 + 8], rbx
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @checkedTwin {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegReg rax, rcx
    x64.cmpRegImm32 rax, 77
    x64.movRegImm32 rcx, 1
    x64.jcc equal, scmerge#2
  orrhs#1:
    x64.cmpRegImm32 rdx, 78
    x64.setccReg equal, rcx
  scmerge#2:
    x64.movRegImm32 rsi, 1
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, critsplit#16
  orrhs#3:
    x64.cmpRegImm32 rax, 79
    x64.setccReg equal, rcx
    x64.jmp scmerge#4
  critsplit#16:
    x64.movRegReg rcx, rsi
  scmerge#4:
    x64.movRegImm32 rsi, 1
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, critsplit#17
  orrhs#5:
    x64.cmpRegImm32 rdx, 80
    x64.setccReg equal, rcx
    x64.jmp scmerge#6
  critsplit#17:
    x64.movRegReg rcx, rsi
  scmerge#6:
    x64.movRegImm32 rsi, 1
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, critsplit#18
  orrhs#7:
    x64.cmpRegImm32 rax, 81
    x64.setccReg equal, rcx
    x64.jmp scmerge#8
  critsplit#18:
    x64.movRegReg rcx, rsi
  scmerge#8:
    x64.movRegImm32 rsi, 1
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, critsplit#19
  orrhs#9:
    x64.cmpRegImm32 rdx, 82
    x64.setccReg equal, rcx
    x64.jmp scmerge#10
  critsplit#19:
    x64.movRegReg rcx, rsi
  scmerge#10:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, ifcont#12
  tooBig:
    x64.movRegImm32 r10, 1
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont#12:
    x64.cmpRegImm32 rax, 5
    x64.jcc notEqual, ifcont#14
  swapped:
    x64.movRegReg rcx, rdx
    x64.movRegReg rdx, rax
    x64.callDirect twin
    x64.movRegImm32 r10, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont#14:
    x64.movRegReg rcx, rax
    x64.callDirect twin
    x64.movRegImm32 r10, 0
    x64.epilogue 32
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
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 r12, 2
    x64.movRegImm32 r13, 0
    x64.jmp forhdr
  rounds:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect checkedTwin
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#6
  tryok#5:
    x64.loadRegBaseDisp.word64 r12, [r8 + 0]
    x64.loadRegBaseDisp.word64 rbx, [r8 + 8]
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
  forstep:
    x64.leaRegRegImm32 r13, r13, 1
  forhdr:
    x64.cmpRegImm32 r13, 1000
    x64.jcc less, rounds
  forexit:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect checkedTwin
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#8
  tryok#7:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word64 rcx, [r8 + 8]
    x64.xorRegReg rbx, rbx, r12
    x64.xorRegReg rbx, rbx, rax
    x64.xorRegReg rbx, rbx, rcx
    x64.andRegImm32 rbx, rbx, 127
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#6:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-pair-whose-only-caller-is-a-throwing-forwarder-keeps-its-heap-record.test:49: unreachable\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#8:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-pair-whose-only-caller-is-a-throwing-forwarder-keeps-its-heap-record.test:54: unreachable\x0a"
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

func @churn {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.movRegReg rax, rcx
    x64.shlRegImm8 rax, rcx, 13
    x64.xorRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.shrRegImm8 rcx, rax, 7
    x64.xorRegReg rcx, rcx, rax
    x64.movRegReg rax, rcx
    x64.shlRegImm8 rax, rcx, 17
    x64.xorRegReg rax, rax, rcx
    x64.movRegImm rcx, 11400714819323198485
    x64.leaRegRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.shrRegImm8 rcx, rax, 11
    x64.xorRegReg rcx, rcx, rax
    x64.movRegReg rax, rcx
    x64.shlRegImm8 rax, rcx, 5
    x64.xorRegReg rax, rax, rcx
    x64.movRegImm rcx, 9172280023384029625
    x64.leaRegRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.shrRegImm8 rcx, rax, 3
    x64.xorRegReg rcx, rcx, rax
    x64.movRegReg rax, rcx
    x64.shlRegImm8 rax, rcx, 29
    x64.xorRegReg rax, rax, rcx
    x64.movRegImm rcx, 1311768467294899695
    x64.leaRegRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.shrRegImm8 rcx, rax, 19
    x64.movRegReg r8, rcx
    x64.xorRegReg r8, rcx, rax
    x64.popReg rbp
    x64.ret
}

func @twin {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.movRegReg rcx, rbx
    x64.callDirect churn
    x64.movRegReg rax, rbx
    x64.shlRegImm8 rax, rbx, 3
    x64.movRegReg rcx, r12
    x64.shrRegImm8 rcx, r12, 5
    x64.leaRegRegReg rax, rax, rcx
    x64.xorRegReg r8, r8, rax
    x64.movRegReg rax, rbx
    x64.shrRegImm8 rax, rbx, 7
    x64.movRegReg rcx, r12
    x64.shlRegImm8 rcx, r12, 11
    x64.leaRegRegReg rax, rax, rcx
    x64.xorRegReg r8, r8, rax
    x64.movRegReg rax, rbx
    x64.shlRegImm8 rax, rbx, 13
    x64.movRegReg rcx, r12
    x64.shrRegImm8 rcx, r12, 17
    x64.leaRegRegReg rax, rax, rcx
    x64.movRegReg r13, r8
    x64.xorRegReg r13, r8, rax
    x64.movRegReg rcx, r12
    x64.shrRegImm8 rcx, r12, 8
    x64.callDirect churn
    x64.movRegReg rax, r12
    x64.shlRegImm8 rax, r12, 3
    x64.movRegReg rcx, rbx
    x64.shrRegImm8 rcx, rbx, 5
    x64.leaRegRegReg rax, rax, rcx
    x64.xorRegReg r8, r8, rax
    x64.movRegReg rax, r12
    x64.shrRegImm8 rax, r12, 7
    x64.movRegReg rcx, rbx
    x64.shlRegImm8 rcx, rbx, 11
    x64.leaRegRegReg rax, rax, rcx
    x64.xorRegReg r8, r8, rax
    x64.shlRegImm8 r12, r12, 13
    x64.shrRegImm8 rbx, rbx, 17
    x64.leaRegRegReg rax, r12, rbx
    x64.movRegReg rbx, r8
    x64.xorRegReg rbx, r8, rax
    x64.movRegImm32 rcx, 16
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], r13
    x64.storeBaseDispReg.word64 [r8 + 8], rbx
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @checkedTwin {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegReg rax, rcx
    x64.cmpRegImm32 rax, 77
    x64.movRegImm32 rcx, 1
    x64.jcc equal, scmerge#2
  orrhs#1:
    x64.cmpRegImm32 rdx, 78
    x64.setccReg equal, rcx
  scmerge#2:
    x64.movRegImm32 rsi, 1
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, critsplit#16
  orrhs#3:
    x64.cmpRegImm32 rax, 79
    x64.setccReg equal, rcx
    x64.jmp scmerge#4
  critsplit#16:
    x64.movRegReg rcx, rsi
  scmerge#4:
    x64.movRegImm32 rsi, 1
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, critsplit#17
  orrhs#5:
    x64.cmpRegImm32 rdx, 80
    x64.setccReg equal, rcx
    x64.jmp scmerge#6
  critsplit#17:
    x64.movRegReg rcx, rsi
  scmerge#6:
    x64.movRegImm32 rsi, 1
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, critsplit#18
  orrhs#7:
    x64.cmpRegImm32 rax, 81
    x64.setccReg equal, rcx
    x64.jmp scmerge#8
  critsplit#18:
    x64.movRegReg rcx, rsi
  scmerge#8:
    x64.movRegImm32 rsi, 1
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, critsplit#19
  orrhs#9:
    x64.cmpRegImm32 rdx, 82
    x64.setccReg equal, rcx
    x64.jmp scmerge#10
  critsplit#19:
    x64.movRegReg rcx, rsi
  scmerge#10:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, ifcont#12
  tooBig:
    x64.movRegImm32 r10, 1
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont#12:
    x64.cmpRegImm32 rax, 5
    x64.jcc notEqual, ifcont#14
  swapped:
    x64.movRegReg rcx, rdx
    x64.movRegReg rdx, rax
    x64.callDirect twin
    x64.movRegImm32 r10, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont#14:
    x64.movRegReg rcx, rax
    x64.callDirect twin
    x64.movRegImm32 r10, 0
    x64.epilogue 32
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
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 r12, 2
    x64.movRegImm32 r13, 0
    x64.jmp forhdr
  rounds:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect checkedTwin
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#6
  tryok#5:
    x64.loadRegBaseDisp.word64 r12, [r8 + 0]
    x64.loadRegBaseDisp.word64 rbx, [r8 + 8]
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
  forstep:
    x64.leaRegRegImm32 r13, r13, 1
  forhdr:
    x64.cmpRegImm32 r13, 1000
    x64.jcc less, rounds
  forexit:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect checkedTwin
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#8
  tryok#7:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word64 rcx, [r8 + 8]
    x64.xorRegReg rbx, rbx, r12
    x64.xorRegReg rbx, rbx, rax
    x64.xorRegReg rbx, rbx, rcx
    x64.andRegImm32 rbx, rbx, 127
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#6:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-pair-whose-only-caller-is-a-throwing-forwarder-keeps-its-heap-record.test:49: unreachable\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#8:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-pair-whose-only-caller-is-a-throwing-forwarder-keeps-its-heap-record.test:54: unreachable\x0a"
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

func @churn {
  entry:
    arm64.prologue 16
    arm64.lsl x1, x0, 13
    arm64.eor x0, x0, x1
    arm64.lsr x1, x0, 7
    arm64.eor x0, x0, x1
    arm64.lsl x1, x0, 17
    arm64.eor x0, x0, x1
    arm64.movImm x1, 11400714819323198485
    arm64.add x0, x0, x1
    arm64.lsr x1, x0, 11
    arm64.eor x0, x0, x1
    arm64.lsl x1, x0, 5
    arm64.eor x0, x0, x1
    arm64.movImm x1, 9172280023384029625
    arm64.add x0, x0, x1
    arm64.lsr x1, x0, 3
    arm64.eor x0, x0, x1
    arm64.lsl x1, x0, 29
    arm64.eor x0, x0, x1
    arm64.movImm x1, 1311768467294899695
    arm64.add x0, x0, x1
    arm64.lsr x1, x0, 19
    arm64.eor x0, x0, x1
    arm64.epilogue 16
    arm64.ret
}

func @twin {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x0, x19
    arm64.bl churn
    arm64.lsl x1, x19, 3
    arm64.lsr x2, x20, 5
    arm64.add x1, x1, x2
    arm64.eor x0, x0, x1
    arm64.lsr x1, x19, 7
    arm64.lsl x2, x20, 11
    arm64.add x1, x1, x2
    arm64.eor x0, x0, x1
    arm64.lsl x1, x19, 13
    arm64.lsr x2, x20, 17
    arm64.add x1, x1, x2
    arm64.eor x21, x0, x1
    arm64.lsr x0, x20, 8
    arm64.bl churn
    arm64.lsl x1, x20, 3
    arm64.lsr x2, x19, 5
    arm64.add x1, x1, x2
    arm64.eor x0, x0, x1
    arm64.lsr x1, x20, 7
    arm64.lsl x2, x19, 11
    arm64.add x1, x1, x2
    arm64.eor x0, x0, x1
    arm64.lsl x1, x20, 13
    arm64.lsr x2, x19, 17
    arm64.add x1, x1, x2
    arm64.eor x19, x0, x1
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x21
    arm64.storeBaseDispReg.word64 [x0 + 8], x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @checkedTwin {
  entry:
    arm64.prologue 16
    arm64.movRegReg x2, x0
    arm64.cmp x2, 77
    arm64.movImm x0, 1
    arm64.b.eq scmerge#2
  orrhs#1:
    arm64.cmp x1, 78
    arm64.cset x0, eq
  scmerge#2:
    arm64.movImm x3, 1
    arm64.cbnz x0, critsplit#16
  orrhs#3:
    arm64.cmp x2, 79
    arm64.cset x0, eq
    arm64.b scmerge#4
  critsplit#16:
    arm64.movRegReg x0, x3
  scmerge#4:
    arm64.movImm x3, 1
    arm64.cbnz x0, critsplit#17
  orrhs#5:
    arm64.cmp x1, 80
    arm64.cset x0, eq
    arm64.b scmerge#6
  critsplit#17:
    arm64.movRegReg x0, x3
  scmerge#6:
    arm64.movImm x3, 1
    arm64.cbnz x0, critsplit#18
  orrhs#7:
    arm64.cmp x2, 81
    arm64.cset x0, eq
    arm64.b scmerge#8
  critsplit#18:
    arm64.movRegReg x0, x3
  scmerge#8:
    arm64.movImm x3, 1
    arm64.cbnz x0, critsplit#19
  orrhs#9:
    arm64.cmp x1, 82
    arm64.cset x0, eq
    arm64.b scmerge#10
  critsplit#19:
    arm64.movRegReg x0, x3
  scmerge#10:
    arm64.cbz x0, ifcont#12
  tooBig:
    arm64.movImm x9, 1
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  ifcont#12:
    arm64.cmp x2, 5
    arm64.b.ne ifcont#14
  swapped:
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x2
    arm64.bl twin
    arm64.movImm x9, 0
    arm64.epilogue 16
    arm64.ret
  ifcont#14:
    arm64.movRegReg x0, x2
    arm64.bl twin
    arm64.movImm x9, 0
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 1
    arm64.movImm x20, 2
    arm64.movImm x21, 0
    arm64.b forhdr
  rounds:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl checkedTwin
    arm64.cmp x9, 0
    arm64.b.ne tryerr#6
  tryok#5:
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x19, [x0 + 8]
    arm64.bl __mm_decref
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.cmp x21, 1000
    arm64.b.lt rounds
  forexit:
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl checkedTwin
    arm64.cmp x9, 0
    arm64.b.ne tryerr#8
  tryok#7:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.eor x3, x19, x20
    arm64.eor x1, x3, x1
    arm64.eor x1, x1, x2
    arm64.and x19, x1, 127
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  tryerr#6:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-pair-whose-only-caller-is-a-throwing-forwarder-keeps-its-heap-record.test:49: unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  tryerr#8:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-pair-whose-only-caller-is-a-throwing-forwarder-keeps-its-heap-record.test:54: unreachable\x0a"
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

func @churn {
  entry:
    arm64.prologue 16
    arm64.lsl x1, x0, 13
    arm64.eor x0, x0, x1
    arm64.lsr x1, x0, 7
    arm64.eor x0, x0, x1
    arm64.lsl x1, x0, 17
    arm64.eor x0, x0, x1
    arm64.movImm x1, 11400714819323198485
    arm64.add x0, x0, x1
    arm64.lsr x1, x0, 11
    arm64.eor x0, x0, x1
    arm64.lsl x1, x0, 5
    arm64.eor x0, x0, x1
    arm64.movImm x1, 9172280023384029625
    arm64.add x0, x0, x1
    arm64.lsr x1, x0, 3
    arm64.eor x0, x0, x1
    arm64.lsl x1, x0, 29
    arm64.eor x0, x0, x1
    arm64.movImm x1, 1311768467294899695
    arm64.add x0, x0, x1
    arm64.lsr x1, x0, 19
    arm64.eor x0, x0, x1
    arm64.epilogue 16
    arm64.ret
}

func @twin {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x0, x19
    arm64.bl churn
    arm64.lsl x1, x19, 3
    arm64.lsr x2, x20, 5
    arm64.add x1, x1, x2
    arm64.eor x0, x0, x1
    arm64.lsr x1, x19, 7
    arm64.lsl x2, x20, 11
    arm64.add x1, x1, x2
    arm64.eor x0, x0, x1
    arm64.lsl x1, x19, 13
    arm64.lsr x2, x20, 17
    arm64.add x1, x1, x2
    arm64.eor x21, x0, x1
    arm64.lsr x0, x20, 8
    arm64.bl churn
    arm64.lsl x1, x20, 3
    arm64.lsr x2, x19, 5
    arm64.add x1, x1, x2
    arm64.eor x0, x0, x1
    arm64.lsr x1, x20, 7
    arm64.lsl x2, x19, 11
    arm64.add x1, x1, x2
    arm64.eor x0, x0, x1
    arm64.lsl x1, x20, 13
    arm64.lsr x2, x19, 17
    arm64.add x1, x1, x2
    arm64.eor x19, x0, x1
    arm64.movImm x0, 16
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x21
    arm64.storeBaseDispReg.word64 [x0 + 8], x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @checkedTwin {
  entry:
    arm64.prologue 16
    arm64.movRegReg x2, x0
    arm64.cmp x2, 77
    arm64.movImm x0, 1
    arm64.b.eq scmerge#2
  orrhs#1:
    arm64.cmp x1, 78
    arm64.cset x0, eq
  scmerge#2:
    arm64.movImm x3, 1
    arm64.cbnz x0, critsplit#16
  orrhs#3:
    arm64.cmp x2, 79
    arm64.cset x0, eq
    arm64.b scmerge#4
  critsplit#16:
    arm64.movRegReg x0, x3
  scmerge#4:
    arm64.movImm x3, 1
    arm64.cbnz x0, critsplit#17
  orrhs#5:
    arm64.cmp x1, 80
    arm64.cset x0, eq
    arm64.b scmerge#6
  critsplit#17:
    arm64.movRegReg x0, x3
  scmerge#6:
    arm64.movImm x3, 1
    arm64.cbnz x0, critsplit#18
  orrhs#7:
    arm64.cmp x2, 81
    arm64.cset x0, eq
    arm64.b scmerge#8
  critsplit#18:
    arm64.movRegReg x0, x3
  scmerge#8:
    arm64.movImm x3, 1
    arm64.cbnz x0, critsplit#19
  orrhs#9:
    arm64.cmp x1, 82
    arm64.cset x0, eq
    arm64.b scmerge#10
  critsplit#19:
    arm64.movRegReg x0, x3
  scmerge#10:
    arm64.cbz x0, ifcont#12
  tooBig:
    arm64.movImm x9, 1
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  ifcont#12:
    arm64.cmp x2, 5
    arm64.b.ne ifcont#14
  swapped:
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x2
    arm64.bl twin
    arm64.movImm x9, 0
    arm64.epilogue 16
    arm64.ret
  ifcont#14:
    arm64.movRegReg x0, x2
    arm64.bl twin
    arm64.movImm x9, 0
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 1
    arm64.movImm x20, 2
    arm64.movImm x21, 0
    arm64.b forhdr
  rounds:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl checkedTwin
    arm64.cmp x9, 0
    arm64.b.ne tryerr#6
  tryok#5:
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x19, [x0 + 8]
    arm64.bl __mm_decref
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.cmp x21, 1000
    arm64.b.lt rounds
  forexit:
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl checkedTwin
    arm64.cmp x9, 0
    arm64.b.ne tryerr#8
  tryok#7:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.eor x3, x19, x20
    arm64.eor x1, x3, x1
    arm64.eor x1, x1, x2
    arm64.and x19, x1, 127
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  tryerr#6:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-pair-whose-only-caller-is-a-throwing-forwarder-keeps-its-heap-record.test:49: unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  tryerr#8:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-pair-whose-only-caller-is-a-throwing-forwarder-keeps-its-heap-record.test:54: unreachable\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```
