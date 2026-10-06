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
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_program_started@40 = i8 0
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
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_program_started@40 = i8 0
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
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_program_started@40 = i8 0
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
