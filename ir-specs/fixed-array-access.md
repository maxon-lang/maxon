---
feature: fixed-array-access
status: experimental
keywords: [fixed array, get, set, index, bounds, proven index, codegen]
category: codegen
---

# FixedArray access at a proven index: the cases that pin emitted code

A `get` or `set` on a `FixedArray with N` at an index proven inside `0 to N-1` cannot fail
(`specs/fixed-array.md`, Element Access), so it compiles to what C compiles `planes[at]` to:
the element's address — the fixed array's base plus the index times the element size, folded to one
displacement when the index is a constant — and ONE load for a `get` or ONE store for a `set`. There is no
compare of the index against the count `N`, no branch to an `ArrayError.indexOutOfBounds` path, and no call
to `__managed_get`/`__managed_set` or the `FixedArray.get`/`FixedArray.set` bodies.

Each accessor is also stored in a module `var` of function type and called through it, so the call is
indirect and the accessor's body stays in the program. That body, compiled with no caller's array to fold
against, is what the pin holds.

## Tests

<!-- test: a-get-at-a-constant-index-is-one-load -->
`readThird` reads element 2 of an 8-word fixed array. The pin's `readThird` body is the C `planes[2]`: one
load from the array's address at displacement `2 * 8`, and the return. No compare, no branch, no call.
`main` stores distinct words around element 2, so a load from the wrong slot fails the exit code.
```maxon
typealias Word = bits(64)
typealias Planes = FixedArray with 8 Word

typealias PlaneReader = function(Planes) returns Word

function readThird(planes Planes) returns Word
	return planes.get(2)
end 'readThird'

var reader = readThird as PlaneReader

function main() returns ExitCode
	var planes = Planes.create()
	planes.set(1, value: 20)
	planes.set(2, value: 30)
	planes.set(3, value: 40)

	if reader(planes) == 30 'third'
		return 0
	end 'third'

	return 1
end 'main'
```
```exitcode
0
```

```TargetIr:x64-windows
data {
  __data_reader@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_console_probe_stdin@32 = i8 0
  __mrt_console_probe_stdout@33 = i8 0
  __mrt_console_probe_stderr@34 = i8 0
  __mrt_program_started@35 = i8 0
}

func @readThird {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseDisp.word64 r8, [rax + 16]
  __im_cont:
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
    x64.movRegImm32 rax, 8
    x64.leaRegRdata rdx, [rip + __layout_FixedArray_N8_Word]
  __il_body:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 72]
    x64.loadRegBaseDisp.word64 r9, [rdx + 40]
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
    x64.movRegReg rbx, r8
  __il_cont:
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rcx, 20
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __im_missing
  __im_owned#7:
    x64.loadRegBaseDisp.word64 rsi, [rbx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach#6
  __im_viewed#8:
    x64.leaRegRegImm32 rsi, rdx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach#6
  __im_detached#9:
    x64.storeBaseIndexScaleReg.word64 [rdx + rax*8 + 0], rcx
  __im_cont#4:
    x64.movRegImm32 rax, 30
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_missing
  __im_owned#12:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __im_detach#11
  __im_viewed#13:
    x64.leaRegRegImm32 rdx, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_detach#11
  __im_detached#14:
    x64.storeBaseDispReg.word64 [rcx + 16], rax
  __im_cont#10:
    x64.movRegImm32 rax, 40
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
  __im_detached#19:
    x64.storeBaseDispReg.word64 [rcx + 24], rax
  __im_cont#15:
    x64.leaRegGlobal rax, __data_reader
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callReg rax
    x64.cmpRegImm32 r8, 30
    x64.setccReg equal, r12
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, ifcont
  third:
    x64.movRegImm32 r12, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_detach#6:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rcx, 20
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.jmp __im_detached#9
  __im_detach#11:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 30
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.jmp __im_detached#14
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_6]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
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
  __data_reader@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @readThird {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseDisp.word64 r8, [rax + 16]
  __im_cont:
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
    x64.movRegImm32 rax, 8
    x64.leaRegRdata rdx, [rip + __layout_FixedArray_N8_Word]
  __il_body:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 72]
    x64.loadRegBaseDisp.word64 r9, [rdx + 40]
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
    x64.movRegReg rbx, r8
  __il_cont:
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rcx, 20
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __im_missing
  __im_owned#7:
    x64.loadRegBaseDisp.word64 rsi, [rbx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach#6
  __im_viewed#8:
    x64.leaRegRegImm32 rsi, rdx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach#6
  __im_detached#9:
    x64.storeBaseIndexScaleReg.word64 [rdx + rax*8 + 0], rcx
  __im_cont#4:
    x64.movRegImm32 rax, 30
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_missing
  __im_owned#12:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __im_detach#11
  __im_viewed#13:
    x64.leaRegRegImm32 rdx, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_detach#11
  __im_detached#14:
    x64.storeBaseDispReg.word64 [rcx + 16], rax
  __im_cont#10:
    x64.movRegImm32 rax, 40
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
  __im_detached#19:
    x64.storeBaseDispReg.word64 [rcx + 24], rax
  __im_cont#15:
    x64.leaRegGlobal rax, __data_reader
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callReg rax
    x64.cmpRegImm32 r8, 30
    x64.setccReg equal, r12
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, ifcont
  third:
    x64.movRegImm32 r12, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_detach#6:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rcx, 20
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.jmp __im_detached#9
  __im_detach#11:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 30
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.jmp __im_detached#14
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_6]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
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
  __data_reader@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @readThird {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 16]
  __im_cont:
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 8
    arm64.leaRdata x1, __layout_FixedArray_N8_Word
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 72]
    arm64.loadRegBaseDisp.word64 x3, [x1 + 40]
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
    arm64.movRegReg x19, x0
  __il_cont:
    arm64.movImm x0, 1
    arm64.movImm x1, 20
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_missing
  __im_owned#7:
    arm64.loadRegBaseDisp.word64 x3, [x19 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach#6
  __im_viewed#8:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach#6
  __im_detached#9:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
  __im_cont#4:
    arm64.movImm x0, 30
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_missing
  __im_owned#12:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_detach#11
  __im_viewed#13:
    arm64.sub x2, x1, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne __im_detach#11
  __im_detached#14:
    arm64.storeBaseDispReg.word64 [x1 + 16], x0
  __im_cont#10:
    arm64.movImm x0, 40
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
  __im_detached#19:
    arm64.storeBaseDispReg.word64 [x1 + 24], x0
  __im_cont#15:
    arm64.leaGlobal x0, __data_reader
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x2, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.blr x2
    arm64.cmp x0, 30
    arm64.cset x20, eq
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.cbz x20, ifcont
  third:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __im_detach#6:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 20
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.b __im_detached#9
  __im_detach#11:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x1, x0
    arm64.movImm x0, 30
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.b __im_detached#14
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
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
  __data_reader@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_hwcap_atomics@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @readThird {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 16]
  __im_cont:
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 8
    arm64.leaRdata x1, __layout_FixedArray_N8_Word
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 72]
    arm64.loadRegBaseDisp.word64 x3, [x1 + 40]
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
    arm64.movRegReg x19, x0
  __il_cont:
    arm64.movImm x0, 1
    arm64.movImm x1, 20
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_missing
  __im_owned#7:
    arm64.loadRegBaseDisp.word64 x3, [x19 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach#6
  __im_viewed#8:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach#6
  __im_detached#9:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
  __im_cont#4:
    arm64.movImm x0, 30
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_missing
  __im_owned#12:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_detach#11
  __im_viewed#13:
    arm64.sub x2, x1, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne __im_detach#11
  __im_detached#14:
    arm64.storeBaseDispReg.word64 [x1 + 16], x0
  __im_cont#10:
    arm64.movImm x0, 40
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
  __im_detached#19:
    arm64.storeBaseDispReg.word64 [x1 + 24], x0
  __im_cont#15:
    arm64.leaGlobal x0, __data_reader
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x2, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.blr x2
    arm64.cmp x0, 30
    arm64.cset x20, eq
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.cbz x20, ifcont
  third:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __im_detach#6:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 20
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.b __im_detached#9
  __im_detach#11:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x1, x0
    arm64.movImm x0, 30
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.b __im_detached#14
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: a-get-at-a-ranged-index-is-one-load -->
`readAt` reads the element at a `PlaneIndex`, whose range `0 to 7` is the fixed array's own indices. The
access is the C `planes[at]`: the address `base + at * 8` — a scaled-index operand, or one shift and add — and
one load. No compare of `at` against the count 8 follows the parameter's entry, and no call. What the body
does hold ahead of the access is the `PlaneIndex` parameter's own entry cascade, the `__rc_` blocks every
function with a ranged parameter carries: that is where `at` gets the range the access then relies on, it is
paid once per call and never at the access, and its cold panic block's `mrt_panic` is the only call in the
body.
```maxon
typealias Word = bits(64)
typealias Planes = FixedArray with 8 Word
typealias PlaneIndex = int(0 to 7) implements ElementIndex

typealias PlaneReader = function(Planes, PlaneIndex) returns Word

function readAt(planes Planes, at PlaneIndex) returns Word
	return planes.get(at)
end 'readAt'

var reader = readAt as PlaneReader

function main() returns ExitCode
	var planes = Planes.create()
	planes.set(4, value: 50)
	planes.set(5, value: 60)
	planes.set(6, value: 70)

	if reader(planes, 5) == 60 'sixth'
		return 0
	end 'sixth'

	return 1
end 'main'
```
```exitcode
0
```

```TargetIr:x64-windows
data {
  __data_reader@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_console_probe_stdin@32 = i8 0
  __mrt_console_probe_stdout@33 = i8 0
  __mrt_console_probe_stderr@34 = i8 0
  __mrt_program_started@35 = i8 0
}

func @readAt {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rdx, 7
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseIndexScale.word64 r8, [rax + rdx*8 + 0]
  __im_cont:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-get-at-a-ranged-index-is-one-load.test:8: Range check failed: value outside typealias 'PlaneIndex'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
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
    x64.movRegImm32 rax, 8
    x64.leaRegRdata rdx, [rip + __layout_FixedArray_N8_Word]
  __il_body:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 72]
    x64.loadRegBaseDisp.word64 r9, [rdx + 40]
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
    x64.movRegReg rbx, r8
  __il_cont:
    x64.movRegImm32 rax, 4
    x64.movRegImm32 rcx, 50
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __im_missing
  __im_owned#7:
    x64.loadRegBaseDisp.word64 rsi, [rbx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach#6
  __im_viewed#8:
    x64.leaRegRegImm32 rsi, rdx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach#6
  __im_detached#9:
    x64.storeBaseIndexScaleReg.word64 [rdx + rax*8 + 0], rcx
  __im_cont#4:
    x64.movRegImm32 rax, 60
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_missing
  __im_owned#12:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __im_detach#11
  __im_viewed#13:
    x64.leaRegRegImm32 rdx, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_detach#11
  __im_detached#14:
    x64.storeBaseDispReg.word64 [rcx + 40], rax
  __im_cont#10:
    x64.movRegImm32 rax, 70
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
  __im_detached#19:
    x64.storeBaseDispReg.word64 [rcx + 48], rax
  __im_cont#15:
    x64.leaRegGlobal rax, __data_reader
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 5
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callReg rsi
    x64.cmpRegImm32 r8, 60
    x64.setccReg equal, r12
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, ifcont
  sixth:
    x64.movRegImm32 r12, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_detach#6:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 4
    x64.movRegImm32 rcx, 50
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.jmp __im_detached#9
  __im_detach#11:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 60
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.jmp __im_detached#14
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_7]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
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
  __data_reader@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @readAt {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rdx, 7
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseIndexScale.word64 r8, [rax + rdx*8 + 0]
  __im_cont:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-get-at-a-ranged-index-is-one-load.test:8: Range check failed: value outside typealias 'PlaneIndex'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
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
    x64.movRegImm32 rax, 8
    x64.leaRegRdata rdx, [rip + __layout_FixedArray_N8_Word]
  __il_body:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 72]
    x64.loadRegBaseDisp.word64 r9, [rdx + 40]
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
    x64.movRegReg rbx, r8
  __il_cont:
    x64.movRegImm32 rax, 4
    x64.movRegImm32 rcx, 50
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __im_missing
  __im_owned#7:
    x64.loadRegBaseDisp.word64 rsi, [rbx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_detach#6
  __im_viewed#8:
    x64.leaRegRegImm32 rsi, rdx, -16
    x64.loadRegBaseDisp.word64 rsi, [rsi + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_detach#6
  __im_detached#9:
    x64.storeBaseIndexScaleReg.word64 [rdx + rax*8 + 0], rcx
  __im_cont#4:
    x64.movRegImm32 rax, 60
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_missing
  __im_owned#12:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __im_detach#11
  __im_viewed#13:
    x64.leaRegRegImm32 rdx, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_detach#11
  __im_detached#14:
    x64.storeBaseDispReg.word64 [rcx + 40], rax
  __im_cont#10:
    x64.movRegImm32 rax, 70
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
  __im_detached#19:
    x64.storeBaseDispReg.word64 [rcx + 48], rax
  __im_cont#15:
    x64.leaRegGlobal rax, __data_reader
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 5
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callReg rsi
    x64.cmpRegImm32 r8, 60
    x64.setccReg equal, r12
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, ifcont
  sixth:
    x64.movRegImm32 r12, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_detach#6:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 4
    x64.movRegImm32 rcx, 50
    x64.loadRegBaseDisp.word64 rdx, [rbx + 0]
    x64.jmp __im_detached#9
  __im_detach#11:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 60
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.jmp __im_detached#14
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_7]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
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
  __data_reader@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @readAt {
  entry:
    arm64.prologue 16
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x1, 7
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
  __im_cont:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-get-at-a-ranged-index-is-one-load.test:8: Range check failed: value outside typealias 'PlaneIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 8
    arm64.leaRdata x1, __layout_FixedArray_N8_Word
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 72]
    arm64.loadRegBaseDisp.word64 x3, [x1 + 40]
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
    arm64.movRegReg x19, x0
  __il_cont:
    arm64.movImm x0, 4
    arm64.movImm x1, 50
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_missing
  __im_owned#7:
    arm64.loadRegBaseDisp.word64 x3, [x19 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach#6
  __im_viewed#8:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach#6
  __im_detached#9:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
  __im_cont#4:
    arm64.movImm x0, 60
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_missing
  __im_owned#12:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_detach#11
  __im_viewed#13:
    arm64.sub x2, x1, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne __im_detach#11
  __im_detached#14:
    arm64.storeBaseDispReg.word64 [x1 + 40], x0
  __im_cont#10:
    arm64.movImm x0, 70
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
  __im_detached#19:
    arm64.storeBaseDispReg.word64 [x1 + 48], x0
  __im_cont#15:
    arm64.leaGlobal x0, __data_reader
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 5
    arm64.loadRegBaseDisp.word64 x3, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x20
    arm64.blr x3
    arm64.cmp x0, 60
    arm64.cset x20, eq
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.cbz x20, ifcont
  sixth:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __im_detach#6:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.movImm x0, 4
    arm64.movImm x1, 50
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.b __im_detached#9
  __im_detach#11:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x1, x0
    arm64.movImm x0, 60
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.b __im_detached#14
  __im_missing:
    arm64.leaRdata x0, __str_blob_7  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
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
  __data_reader@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_hwcap_atomics@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @readAt {
  entry:
    arm64.prologue 16
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x1, 7
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
  __im_cont:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-get-at-a-ranged-index-is-one-load.test:8: Range check failed: value outside typealias 'PlaneIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 8
    arm64.leaRdata x1, __layout_FixedArray_N8_Word
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 72]
    arm64.loadRegBaseDisp.word64 x3, [x1 + 40]
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
    arm64.movRegReg x19, x0
  __il_cont:
    arm64.movImm x0, 4
    arm64.movImm x1, 50
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.cmp x2, 0
    arm64.b.eq __im_missing
  __im_owned#7:
    arm64.loadRegBaseDisp.word64 x3, [x19 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_detach#6
  __im_viewed#8:
    arm64.sub x3, x2, 16
    arm64.loadRegBaseDisp.word64 x3, [x3 + 8]
    arm64.cmp x3, 0
    arm64.b.ne __im_detach#6
  __im_detached#9:
    arm64.storeBaseIndexScaleReg.word64 [x2 + x0*8 + 0], x1
  __im_cont#4:
    arm64.movImm x0, 60
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_missing
  __im_owned#12:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_detach#11
  __im_viewed#13:
    arm64.sub x2, x1, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne __im_detach#11
  __im_detached#14:
    arm64.storeBaseDispReg.word64 [x1 + 40], x0
  __im_cont#10:
    arm64.movImm x0, 70
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
  __im_detached#19:
    arm64.storeBaseDispReg.word64 [x1 + 48], x0
  __im_cont#15:
    arm64.leaGlobal x0, __data_reader
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 5
    arm64.loadRegBaseDisp.word64 x3, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x2, x20
    arm64.blr x3
    arm64.cmp x0, 60
    arm64.cset x20, eq
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.cbz x20, ifcont
  sixth:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __im_detach#6:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x2, x0
    arm64.movImm x0, 4
    arm64.movImm x1, 50
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.b __im_detached#9
  __im_detach#11:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x1, x0
    arm64.movImm x0, 60
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.b __im_detached#14
  __im_missing:
    arm64.leaRdata x0, __str_blob_7  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: a-set-at-a-ranged-index-is-one-store -->
`writeAt` stores a word at a `PlaneIndex`: the address `base + at * 8` and one store, with no compare of
`at` against the count 8 and no branch to a throw path. A write is the one access a value-semantics buffer
cannot make blind: the buffer may be shared with a copy or be an immortal image built from constants, so the
store is preceded by the same ownership tests `Array.set` carries, and a cold arm detaches the buffer
through `__managed_cow_detach` before storing. As in `readAt`, the `PlaneIndex` parameter's entry cascade
precedes it. `main` checks the written slot and its neighbour, so a store to the wrong slot fails the exit
code.
```maxon
typealias Word = bits(64)
typealias Planes = FixedArray with 8 Word
typealias PlaneIndex = int(0 to 7) implements ElementIndex

typealias PlaneWriter = function(Planes, PlaneIndex, Word)

function writeAt(planes Planes, at PlaneIndex, value Word)
	planes.set(at, value: value)
end 'writeAt'

var writer = writeAt as PlaneWriter

function main() returns ExitCode
	var planes = Planes.create()

	writer(planes, 6, 77)

	if planes.get(6) == 77 and planes.get(5) == 0 'written'
		return 0
	end 'written'

	return 1
end 'main'
```
```exitcode
0
```

```TargetIr:x64-windows
data {
  __data_writer@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_console_probe_stdin@32 = i8 0
  __mrt_console_probe_stdout@33 = i8 0
  __mrt_console_probe_stderr@34 = i8 0
  __mrt_program_started@35 = i8 0
}

func @writeAt {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rdx, 7
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_detach
  critsplit:
    x64.movRegReg rcx, rsi
  __im_detached:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rax
  __im_cont:
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_detach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.jmp __im_detached
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-set-at-a-ranged-index-is-one-store.test:8: Range check failed: value outside typealias 'PlaneIndex'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_7]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
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
    x64.movRegImm32 rax, 8
    x64.leaRegRdata rdx, [rip + __layout_FixedArray_N8_Word]
  __il_body:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 72]
    x64.loadRegBaseDisp.word64 r9, [rdx + 40]
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
    x64.movRegReg rbx, r8
  __il_cont:
    x64.leaRegGlobal rax, __data_writer
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 6
    x64.movRegImm32 rax, 77
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg r9, r12
    x64.callReg rsi
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 48]
  __im_cont#6:
    x64.cmpRegImm32 rax, 77
    x64.movRegImm32 rax, 0
    x64.jcc notEqual, scmerge
  andrhs:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 40]
  __im_cont#7:
    x64.cmpRegImm32 rax, 0
    x64.setccReg equal, rax
  scmerge:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  written:
    x64.movRegImm32 r12, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
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
  __data_writer@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @writeAt {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 64
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rdx, 7
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rdi, [rcx + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_detach
  critsplit:
    x64.movRegReg rcx, rsi
  __im_detached:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rax
  __im_cont:
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_detach:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.callDirect __managed_cow_detach
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.jmp __im_detached
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-set-at-a-ranged-index-is-one-store.test:8: Range check failed: value outside typealias 'PlaneIndex'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
    x64.popReg rbp
    x64.ret
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_7]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 64
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
    x64.movRegImm32 rax, 8
    x64.leaRegRdata rdx, [rip + __layout_FixedArray_N8_Word]
  __il_body:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 72]
    x64.loadRegBaseDisp.word64 r9, [rdx + 40]
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
    x64.movRegReg rbx, r8
  __il_cont:
    x64.leaRegGlobal rax, __data_writer
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.movRegImm32 rdx, 6
    x64.movRegImm32 rax, 77
    x64.loadRegBaseDisp.word64 rsi, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg r9, r12
    x64.callReg rsi
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 48]
  __im_cont#6:
    x64.cmpRegImm32 rax, 77
    x64.movRegImm32 rax, 0
    x64.jcc notEqual, scmerge
  andrhs:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 40]
  __im_cont#7:
    x64.cmpRegImm32 rax, 0
    x64.setccReg equal, rax
  scmerge:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  written:
    x64.movRegImm32 r12, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
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
  __data_writer@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @writeAt {
  entry:
    arm64.prologue 48
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x1, 7
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_detach
  critsplit:
    arm64.movRegReg x0, x3
  __im_detached:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x1*8 + 0], x2
  __im_cont:
    arm64.epilogue 48
    arm64.ret
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.bl __managed_cow_detach
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b __im_detached
  __rc_panic:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-set-at-a-ranged-index-is-one-store.test:8: Range check failed: value outside typealias 'PlaneIndex'\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  __im_missing:
    arm64.leaRdata x0, __str_blob_7  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 8
    arm64.leaRdata x1, __layout_FixedArray_N8_Word
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 72]
    arm64.loadRegBaseDisp.word64 x3, [x1 + 40]
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
    arm64.movRegReg x19, x0
  __il_cont:
    arm64.leaGlobal x0, __data_writer
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 6
    arm64.movImm x2, 77
    arm64.loadRegBaseDisp.word64 x4, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x3, x20
    arm64.blr x4
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 48]
  __im_cont#6:
    arm64.cmp x0, 77
    arm64.movImm x0, 0
    arm64.b.ne scmerge
  andrhs:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 40]
  __im_cont#7:
    arm64.cmp x0, 0
    arm64.cset x0, eq
  scmerge:
    arm64.cbz x0, ifcont
  written:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_writer@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_hwcap_atomics@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @writeAt {
  entry:
    arm64.prologue 48
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x1, 7
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x4, [x0 + 16]
    arm64.cmp x4, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x4, x3, 16
    arm64.loadRegBaseDisp.word64 x4, [x4 + 8]
    arm64.cmp x4, 0
    arm64.b.ne __im_detach
  critsplit:
    arm64.movRegReg x0, x3
  __im_detached:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x1*8 + 0], x2
  __im_cont:
    arm64.epilogue 48
    arm64.ret
  __im_detach:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.bl __managed_cow_detach
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.b __im_detached
  __rc_panic:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-set-at-a-ranged-index-is-one-store.test:8: Range check failed: value outside typealias 'PlaneIndex'\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
  __im_missing:
    arm64.leaRdata x0, __str_blob_7  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 8
    arm64.leaRdata x1, __layout_FixedArray_N8_Word
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x1 + 72]
    arm64.loadRegBaseDisp.word64 x3, [x1 + 40]
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
    arm64.movRegReg x19, x0
  __il_cont:
    arm64.leaGlobal x0, __data_writer
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.movImm x1, 6
    arm64.movImm x2, 77
    arm64.loadRegBaseDisp.word64 x4, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x3, x20
    arm64.blr x4
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 48]
  __im_cont#6:
    arm64.cmp x0, 77
    arm64.movImm x0, 0
    arm64.b.ne scmerge
  andrhs:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 40]
  __im_cont#7:
    arm64.cmp x0, 0
    arm64.cset x0, eq
  scmerge:
    arm64.cbz x0, ifcont
  written:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: a-runtime-sized-count-is-one-load-of-the-record-length -->
A runtime-sized fixed array carries its length in the record, so `lengthOf` reads it with one load of `length@8` and returns it. The function is stored in a module `var` and called through it so its body, compiled with no caller's array to fold against, stays in the program.
```maxon
typealias Word = bits(64)
typealias Cells = FixedArray with Word

typealias CellsMeasure = function(Cells) returns Count

function lengthOf(cells Cells) returns Count
	return cells.count()
end 'lengthOf'

var measure = lengthOf as CellsMeasure

function make(n ElementIndex) returns Cells
	return Cells.create(n)
end 'make'

function main() returns ExitCode
	let cells = make(6)

	return measure(cells) as ExitCode
end 'main'
```
```exitcode
6
```
```TargetIr:x64-windows
data {
  __data_measure@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_console_probe_stdin@32 = i8 0
  __mrt_console_probe_stdout@33 = i8 0
  __mrt_console_probe_stderr@34 = i8 0
  __mrt_program_started@35 = i8 0
}

func @lengthOf {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.loadRegBaseDisp.word64 r8, [rcx + 8]
  __il_cont:
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
    x64.movRegImm32 rax, 6
  __il_body:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 r9, 0
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
    x64.movRegReg rbx, r8
  __il_cont:
    x64.leaRegGlobal rax, __data_measure
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callReg rax
    x64.movRegReg r12, r8
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r12, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-runtime-sized-count-is-one-load-of-the-record-length.test:20: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __data_measure@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @lengthOf {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.loadRegBaseDisp.word64 r8, [rcx + 8]
  __il_cont:
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
    x64.movRegImm32 rax, 6
  __il_body:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 r9, 0
    x64.movRegReg rdx, rax
    x64.callDirect __managed_create_fixed
    x64.movRegReg rbx, r8
  __il_cont:
    x64.leaRegGlobal rax, __data_measure
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, r12
    x64.callDirect __mm_retain
    x64.movRegReg r13, r8
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callReg rax
    x64.movRegReg r12, r8
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r12, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r13
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-runtime-sized-count-is-one-load-of-the-record-length.test:20: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __data_measure@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @lengthOf {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_cont:
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 6
  __il_body:
    arm64.movImm x0, 8
    arm64.movImm x3, 0
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
    arm64.movRegReg x19, x0
  __il_cont:
    arm64.leaGlobal x0, __data_measure
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x2, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.blr x2
    arm64.movRegReg x20, x0
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-runtime-sized-count-is-one-load-of-the-record-length.test:20: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __data_measure@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_hwcap_atomics@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @lengthOf {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_cont:
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 6
  __il_body:
    arm64.movImm x0, 8
    arm64.movImm x3, 0
    arm64.movRegReg x1, x2
    arm64.bl __managed_create_fixed
    arm64.movRegReg x19, x0
  __il_cont:
    arm64.leaGlobal x0, __data_measure
    arm64.loadRegBaseDisp.word64 x20, [x0 + 0]
    arm64.movRegReg x0, x20
    arm64.bl __mm_retain
    arm64.movRegReg x21, x0
    arm64.loadRegBaseDisp.word64 x2, [x20 + 0]
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.blr x2
    arm64.movRegReg x20, x0
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-runtime-sized-count-is-one-load-of-the-record-length.test:20: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: a-constant-length-count-folds-to-the-constant -->
`Cells.create(8)` is a `FixedArray with 8 Word`, so `count()` on it is the constant 8: there is no load of the record's length in `lengthOfEight`.
```maxon
typealias Word = bits(64)
typealias Cells = FixedArray with Word

typealias EightMeasure = function() returns Count

function lengthOfEight() returns Count
	let cells = Cells.create(8)

	return cells.count()
end 'lengthOfEight'

var measureEight = lengthOfEight as EightMeasure

function main() returns ExitCode
	return measureEight() as ExitCode
end 'main'
```
```exitcode
8
```
```TargetIr:x64-windows
data {
  __data_measureEight@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_console_probe_stdin@32 = i8 0
  __mrt_console_probe_stdout@33 = i8 0
  __mrt_console_probe_stderr@34 = i8 0
  __mrt_program_started@35 = i8 0
}

func @lengthOfEight {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.movRegImm32 rsi, 8
    x64.movRegImm32 r8, 8
    x64.movRegImm32 r9, 0
    x64.movRegImm32 r10, 0
  cls#6:
    x64.leaRegRdata rax, [rip + __slab_size_to_class8]
    x64.loadRegBaseDisp.byte rax, [rax + 18]
  cls#8:
    x64.leaRegGlobal rcx, __slab_state
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __ia_slow
  state:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 24]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, walk#17
  walk#10:
    x64.loadRegBaseIndexScale.word64 r11, [rcx + rax*8 + 139328]
    x64.cmpRegImm32 r11, 0
    x64.jcc equal, __ia_slow
  probe#20:
    x64.loadRegBaseDisp.word64 rdx, [r11 + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __ia_slow
  probe#21:
    x64.loadRegBaseDisp.word64 rdx, [r11 + 48]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __ia_slow
  probe#19:
    x64.aluBaseDispImm32.add [rcx + 152120], 1
    x64.aluBaseDispImm32.add [rcx + 152128], 128
    x64.jmp pop#18
  walk#17:
    x64.mov rdx, gs:[rdx]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __ia_slow
  walk#11:
    x64.loadRegBaseDisp.word64 rdi, [rdx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, __ia_slow
  walk#14:
    x64.loadRegBaseDisp.word64 r11, [rdi + 32]
    x64.cmpRegImm32 r11, 255
    x64.jcc greaterEqual, __ia_slow
  walk#16:
    x64.imulRegRegImm32 rbx, r11, 544
    x64.leaRegRegReg rbx, rcx, rbx
    x64.loadRegBaseIndexScale.word64 rbx, [rbx + rax*8 + 608]
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, __ia_slow
  ownprobe#23:
    x64.loadRegBaseDisp.word64 r12, [rbx + 16]
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, __ia_slow
  ownprobe#24:
    x64.loadRegBaseDisp.word64 r12, [rbx + 48]
    x64.cmpRegReg r12, rdi
    x64.jcc notEqual, __ia_slow
  ownprobe#22:
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, ownthread#26
  ownthread#25:
    x64.loadRegBaseDisp.word64 rdx, [rdx + 112]
    x64.aluBaseDispImm32.add [rdx + 512], 1
    x64.aluBaseDispImm32.add [rdx + 528], 144
  ownthread#26:
    x64.imulRegRegImm32 rdx, r11, 48
    x64.leaRegRegImm32 rcx, rcx, 139872
    x64.leaRegRegReg rcx, rcx, rdx
    x64.aluBaseDispImm32.add [rcx + 8], 1
    x64.aluBaseDispImm32.add [rcx + 16], 128
    x64.movRegReg r11, rbx
  pop#18:
    x64.leaRegRdata rcx, [rip + __slab_class_geom]
    x64.loadRegBaseIndexScale.word32 rcx, [rcx + rax*8 + 0]
    x64.loadRegBaseDisp.word64 rbx, [r11 + 8]
    x64.cmpRegImm32 rbx, 0
    x64.jcc notEqual, pop#27
  pop#28:
    x64.loadRegBaseDisp.word64 rax, [r11 + 40]
    x64.loadRegBaseDisp.word64 rdx, [r11 + 32]
    x64.leaRegRdata rdi, [rip + __slab_class_geom]
    x64.loadRegBaseIndexScale.word32 rdx, [rdi + rdx*8 + 4]
    x64.movRegReg rdi, rcx
    x64.imulRegReg rdi, rcx, rdx
    x64.loadRegBaseDisp.word64 rdx, [r11 + 0]
    x64.leaRegRegReg rdx, rdx, rdi
    x64.cmpRegReg rax, rdx
    x64.jcc greaterEqual, pop#30
  pop#29:
    x64.leaRegRegReg rcx, rax, rcx
    x64.storeBaseDispReg.word64 [r11 + 40], rcx
    x64.jmp pop#31
  pop#27:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.storeBaseDispReg.word64 [r11 + 8], rax
    x64.movRegReg rdi, rbx
    x64.x64MemFill 0x00
    x64.movRegReg rax, rbx
  pop#31:
    x64.loadRegBaseDisp.word64 rcx, [r11 + 16]
    x64.leaRegRegImm32 rcx, rcx, -1
    x64.storeBaseDispReg.word64 [r11 + 16], rcx
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, pop#33
  pop#34:
    x64.loadRegBaseDisp.word64 rcx, [r11 + 48]
    x64.loadRegBaseDisp.word64 rdx, [r11 + 32]
    x64.leaRegGlobal rdi, __slab_state
    x64.loadRegBaseDisp.word64 rdi, [rdi + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, pop#35
  pop#36:
    x64.leaRegRegImm32 rcx, rdi, 138720
    x64.leaRegRegImm32 rcx, rcx, 608
    x64.leaRegBaseIndexScale rcx, [rcx + rdx*8]
    x64.jmp pop#32
  pop#35:
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, pop#38
  pop#37:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 32]
    x64.imulRegRegImm32 rcx, rcx, 544
    x64.leaRegRegReg rcx, rdi, rcx
    x64.leaRegRegImm32 rcx, rcx, 608
    x64.leaRegBaseIndexScale rcx, [rcx + rdx*8]
  pop#32:
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rcx + 0], rdx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [r11 + 48], rcx
  pop#33:
    x64.storeBaseDispReg.word64 [rax + 0], r10
    x64.leaRegRegImm32 rcx, rax, 16
  __ia_done:
    x64.storeBaseDispReg.word64 [rcx + 24], r8
    x64.storeBaseDispReg.word64 [rcx + 40], r9
    x64.movRegImm32 rax, 8
    x64.leaRegRegImm32 rdx, rcx, 64
    x64.storeBaseDispReg.word64 [rcx + 0], rdx
    x64.storeBaseDispReg.word64 [rcx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rcx + 32], rax
    x64.storeBaseDispReg.word64 [rcx + 8], rsi
  __il_body:
    x64.movRegImm32 rbx, 8
  __il_cont:
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __ia_slow:
    x64.movRegImm32 rcx, 128
    x64.movRegReg rdx, r10
    x64.callDirect __mm_alloc_slow
    x64.movRegReg rcx, r8
    x64.movRegImm32 rsi, 8
    x64.movRegImm32 r8, 8
    x64.movRegImm32 r9, 0
    x64.jmp __ia_done
  pop#30:
    x64.callDirect __slab_past_end
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  pop#38:
    x64.callDirect __slab_evict_unowned
    x64.movRegImm32 r8, 0
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
    x64.prologue 32
    x64.leaRegGlobal rax, __data_measureEight
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_retain
    x64.movRegReg r12, r8
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.movRegReg rcx, rbx
    x64.callReg rax
    x64.movRegReg rbx, r8
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg rbx, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __mm_decref
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-constant-length-count-folds-to-the-constant.test:16: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:x64-linux
data {
  __data_measureEight@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @lengthOfEight {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.movRegImm32 rsi, 8
    x64.movRegImm32 r8, 8
    x64.movRegImm32 r9, 0
    x64.movRegImm32 r10, 0
  cls#6:
    x64.leaRegRdata rax, [rip + __slab_size_to_class8]
    x64.loadRegBaseDisp.byte rax, [rax + 18]
  cls#8:
    x64.leaRegGlobal rcx, __slab_state
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __ia_slow
  state:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 24]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, walk#17
  walk#10:
    x64.loadRegBaseIndexScale.word64 r11, [rcx + rax*8 + 139328]
    x64.cmpRegImm32 r11, 0
    x64.jcc equal, __ia_slow
  probe#20:
    x64.loadRegBaseDisp.word64 rdx, [r11 + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __ia_slow
  probe#21:
    x64.loadRegBaseDisp.word64 rdx, [r11 + 48]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __ia_slow
  probe#19:
    x64.aluBaseDispImm32.add [rcx + 152120], 1
    x64.aluBaseDispImm32.add [rcx + 152128], 128
    x64.jmp pop#18
  walk#17:
    x64.mov rdx, gs:[rdx]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __ia_slow
  walk#11:
    x64.loadRegBaseDisp.word64 rdi, [rdx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, __ia_slow
  walk#14:
    x64.loadRegBaseDisp.word64 r11, [rdi + 32]
    x64.cmpRegImm32 r11, 255
    x64.jcc greaterEqual, __ia_slow
  walk#16:
    x64.imulRegRegImm32 rbx, r11, 544
    x64.leaRegRegReg rbx, rcx, rbx
    x64.loadRegBaseIndexScale.word64 rbx, [rbx + rax*8 + 608]
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, __ia_slow
  ownprobe#23:
    x64.loadRegBaseDisp.word64 r12, [rbx + 16]
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, __ia_slow
  ownprobe#24:
    x64.loadRegBaseDisp.word64 r12, [rbx + 48]
    x64.cmpRegReg r12, rdi
    x64.jcc notEqual, __ia_slow
  ownprobe#22:
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, ownthread#26
  ownthread#25:
    x64.loadRegBaseDisp.word64 rdx, [rdx + 112]
    x64.aluBaseDispImm32.add [rdx + 512], 1
    x64.aluBaseDispImm32.add [rdx + 528], 144
  ownthread#26:
    x64.imulRegRegImm32 rdx, r11, 48
    x64.leaRegRegImm32 rcx, rcx, 139872
    x64.leaRegRegReg rcx, rcx, rdx
    x64.aluBaseDispImm32.add [rcx + 8], 1
    x64.aluBaseDispImm32.add [rcx + 16], 128
    x64.movRegReg r11, rbx
  pop#18:
    x64.leaRegRdata rcx, [rip + __slab_class_geom]
    x64.loadRegBaseIndexScale.word32 rcx, [rcx + rax*8 + 0]
    x64.loadRegBaseDisp.word64 rbx, [r11 + 8]
    x64.cmpRegImm32 rbx, 0
    x64.jcc notEqual, pop#27
  pop#28:
    x64.loadRegBaseDisp.word64 rax, [r11 + 40]
    x64.loadRegBaseDisp.word64 rdx, [r11 + 32]
    x64.leaRegRdata rdi, [rip + __slab_class_geom]
    x64.loadRegBaseIndexScale.word32 rdx, [rdi + rdx*8 + 4]
    x64.movRegReg rdi, rcx
    x64.imulRegReg rdi, rcx, rdx
    x64.loadRegBaseDisp.word64 rdx, [r11 + 0]
    x64.leaRegRegReg rdx, rdx, rdi
    x64.cmpRegReg rax, rdx
    x64.jcc greaterEqual, pop#30
  pop#29:
    x64.leaRegRegReg rcx, rax, rcx
    x64.storeBaseDispReg.word64 [r11 + 40], rcx
    x64.jmp pop#31
  pop#27:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.storeBaseDispReg.word64 [r11 + 8], rax
    x64.movRegReg rdi, rbx
    x64.x64MemFill 0x00
    x64.movRegReg rax, rbx
  pop#31:
    x64.loadRegBaseDisp.word64 rcx, [r11 + 16]
    x64.leaRegRegImm32 rcx, rcx, -1
    x64.storeBaseDispReg.word64 [r11 + 16], rcx
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, pop#33
  pop#34:
    x64.loadRegBaseDisp.word64 rcx, [r11 + 48]
    x64.loadRegBaseDisp.word64 rdx, [r11 + 32]
    x64.leaRegGlobal rdi, __slab_state
    x64.loadRegBaseDisp.word64 rdi, [rdi + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, pop#35
  pop#36:
    x64.leaRegRegImm32 rcx, rdi, 138720
    x64.leaRegRegImm32 rcx, rcx, 608
    x64.leaRegBaseIndexScale rcx, [rcx + rdx*8]
    x64.jmp pop#32
  pop#35:
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, pop#38
  pop#37:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 32]
    x64.imulRegRegImm32 rcx, rcx, 544
    x64.leaRegRegReg rcx, rdi, rcx
    x64.leaRegRegImm32 rcx, rcx, 608
    x64.leaRegBaseIndexScale rcx, [rcx + rdx*8]
  pop#32:
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rcx + 0], rdx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [r11 + 48], rcx
  pop#33:
    x64.storeBaseDispReg.word64 [rax + 0], r10
    x64.leaRegRegImm32 rcx, rax, 16
  __ia_done:
    x64.storeBaseDispReg.word64 [rcx + 24], r8
    x64.storeBaseDispReg.word64 [rcx + 40], r9
    x64.movRegImm32 rax, 8
    x64.leaRegRegImm32 rdx, rcx, 64
    x64.storeBaseDispReg.word64 [rcx + 0], rdx
    x64.storeBaseDispReg.word64 [rcx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rcx + 32], rax
    x64.storeBaseDispReg.word64 [rcx + 8], rsi
  __il_body:
    x64.movRegImm32 rbx, 8
  __il_cont:
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __ia_slow:
    x64.movRegImm32 rcx, 128
    x64.movRegReg rdx, r10
    x64.callDirect __mm_alloc_slow
    x64.movRegReg rcx, r8
    x64.movRegImm32 rsi, 8
    x64.movRegImm32 r8, 8
    x64.movRegImm32 r9, 0
    x64.jmp __ia_done
  pop#30:
    x64.callDirect __slab_past_end
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  pop#38:
    x64.callDirect __slab_evict_unowned
    x64.movRegImm32 r8, 0
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
    x64.prologue 32
    x64.leaRegGlobal rax, __data_measureEight
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_retain
    x64.movRegReg r12, r8
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.movRegReg rcx, rbx
    x64.callReg rax
    x64.movRegReg rbx, r8
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __mm_decref
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-constant-length-count-folds-to-the-constant.test:16: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-macos
data {
  __data_measureEight@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @lengthOfEight {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movImm x4, 8
    arm64.movImm x5, 8
    arm64.movImm x6, 0
    arm64.movImm x7, 0
  cls#6:
    arm64.leaRdata x0, __slab_size_to_class8
    arm64.loadRegBaseDisp.byte x0, [x0 + 18]
  cls#8:
    arm64.leaGlobal x1, __slab_state
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __ia_slow
  state:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 24]
    arm64.cmp x2, 0
    arm64.b.ne walk#17
  walk#10:
    arm64.arm64AddLsl x2, x1, x0, lsl #3
    arm64.add x16, x2, 139328
    arm64.loadRegBaseDisp.word64 x8, [x16 + 0]
    arm64.cmp x8, 0
    arm64.b.eq __ia_slow
  probe#20:
    arm64.loadRegBaseDisp.word64 x2, [x8 + 16]
    arm64.cmp x2, 0
    arm64.b.eq __ia_slow
  probe#21:
    arm64.add x16, x8, 48
    arm64.ldar.word64 x2, [x16]
    arm64.cmp x2, 0
    arm64.b.ne __ia_slow
  probe#19:
    arm64.add x1, x1, 152112
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 8], x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 16]
    arm64.add x2, x2, 128
    arm64.storeBaseDispReg.word64 [x1 + 16], x2
    arm64.b pop#18
  walk#17:
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x2, 0
    arm64.b.eq __ia_slow
  walk#11:
    arm64.loadRegBaseDisp.word64 x3, [x2 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __ia_slow
  walk#14:
    arm64.loadRegBaseDisp.word64 x8, [x3 + 32]
    arm64.cmp x8, 255
    arm64.b.ge __ia_slow
  walk#16:
    arm64.movImm x16, 544
    arm64.mul x9, x8, x16
    arm64.add x9, x1, x9
    arm64.arm64AddLsl x9, x9, x0, lsl #3
    arm64.loadRegBaseDisp.word64 x9, [x9 + 608]
    arm64.cmp x9, 0
    arm64.b.eq __ia_slow
  ownprobe#23:
    arm64.loadRegBaseDisp.word64 x10, [x9 + 16]
    arm64.cmp x10, 0
    arm64.b.eq __ia_slow
  ownprobe#24:
    arm64.add x16, x9, 48
    arm64.ldar.word64 x10, [x16]
    arm64.cmp x10, x3
    arm64.b.ne __ia_slow
  ownprobe#22:
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.eq ownthread#26
  ownthread#25:
    arm64.loadRegBaseDisp.word64 x2, [x2 + 112]
    arm64.loadRegBaseDisp.word64 x3, [x2 + 512]
    arm64.add x3, x3, 1
    arm64.storeBaseDispReg.word64 [x2 + 512], x3
    arm64.loadRegBaseDisp.word64 x3, [x2 + 528]
    arm64.add x3, x3, 144
    arm64.storeBaseDispReg.word64 [x2 + 528], x3
  ownthread#26:
    arm64.movImm x16, 48
    arm64.mul x2, x8, x16
    arm64.add x1, x1, 139872
    arm64.add x1, x1, x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 8], x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 16]
    arm64.add x2, x2, 128
    arm64.storeBaseDispReg.word64 [x1 + 16], x2
    arm64.movRegReg x8, x9
  pop#18:
    arm64.leaRdata x1, __slab_class_geom
    arm64.arm64AddLsl x0, x1, x0, lsl #3
    arm64.loadRegBaseDisp.word32 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x9, [x8 + 8]
    arm64.cmp x9, 0
    arm64.b.ne pop#27
  pop#28:
    arm64.loadRegBaseDisp.word64 x0, [x8 + 40]
    arm64.loadRegBaseDisp.word64 x2, [x8 + 32]
    arm64.leaRdata x3, __slab_class_geom
    arm64.arm64AddLsl x2, x3, x2, lsl #3
    arm64.loadRegBaseDisp.word32 x2, [x2 + 4]
    arm64.mul x2, x1, x2
    arm64.loadRegBaseDisp.word64 x3, [x8 + 0]
    arm64.add x2, x3, x2
    arm64.cmp x0, x2
    arm64.b.ge pop#30
  pop#29:
    arm64.add x1, x0, x1
    arm64.storeBaseDispReg.word64 [x8 + 40], x1
    arm64.b pop#31
  pop#27:
    arm64.loadRegBaseDisp.word64 x0, [x9 + 0]
    arm64.storeBaseDispReg.word64 [x8 + 8], x0
    arm64.movRegReg x0, x9
    arm64.arm64MemFill 0x00
    arm64.movRegReg x0, x9
  pop#31:
    arm64.loadRegBaseDisp.word64 x1, [x8 + 16]
    arm64.sub x1, x1, 1
    arm64.storeBaseDispReg.word64 [x8 + 16], x1
    arm64.cmp x1, 0
    arm64.b.ne pop#33
  pop#34:
    arm64.add x16, x8, 48
    arm64.ldar.word64 x1, [x16]
    arm64.loadRegBaseDisp.word64 x2, [x8 + 32]
    arm64.leaGlobal x3, __slab_state
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.cmp x1, 0
    arm64.b.ne pop#35
  pop#36:
    arm64.add x1, x3, 138720
    arm64.add x1, x1, 608
    arm64.arm64AddLsl x1, x1, x2, lsl #3
    arm64.b pop#32
  pop#35:
    arm64.cmp x1, 0
    arm64.b.lt pop#38
  pop#37:
    arm64.loadRegBaseDisp.word64 x1, [x1 + 32]
    arm64.movImm x16, 544
    arm64.mul x1, x1, x16
    arm64.add x1, x3, x1
    arm64.add x1, x1, 608
    arm64.arm64AddLsl x1, x1, x2, lsl #3
  pop#32:
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
    arm64.movImm x1, 18446744073709551613
    arm64.add x16, x8, 48
    arm64.stlr.word64 [x16], x1
  pop#33:
    arm64.storeBaseDispReg.word64 [x0 + 0], x7
    arm64.add x0, x0, 16
  __ia_done:
    arm64.storeBaseDispReg.word64 [x0 + 24], x5
    arm64.storeBaseDispReg.word64 [x0 + 40], x6
    arm64.movImm x1, 8
    arm64.add x2, x0, 64
    arm64.storeBaseDispReg.word64 [x0 + 0], x2
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 32], x1
    arm64.storeBaseDispReg.word64 [x0 + 8], x4
  __il_body:
    arm64.movImm x19, 8
  __il_cont:
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
  __ia_slow:
    arm64.movImm x0, 128
    arm64.movRegReg x1, x7
    arm64.bl __mm_alloc_slow
    arm64.movImm x4, 8
    arm64.movImm x5, 8
    arm64.movImm x6, 0
    arm64.b __ia_done
  pop#30:
    arm64.movRegReg x0, x1
    arm64.bl __slab_past_end
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
  pop#38:
    arm64.movRegReg x0, x1
    arm64.bl __slab_evict_unowned
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaGlobal x0, __data_measureEight
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.movRegReg x0, x19
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.movRegReg x0, x19
    arm64.blr x1
    arm64.movRegReg x19, x0
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-constant-length-count-folds-to-the-constant.test:16: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __data_measureEight@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_hwcap_atomics@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @lengthOfEight {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movImm x4, 8
    arm64.movImm x5, 8
    arm64.movImm x6, 0
    arm64.movImm x7, 0
  cls#6:
    arm64.leaRdata x0, __slab_size_to_class8
    arm64.loadRegBaseDisp.byte x0, [x0 + 18]
  cls#8:
    arm64.leaGlobal x1, __slab_state
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __ia_slow
  state:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 24]
    arm64.cmp x2, 0
    arm64.b.ne walk#17
  walk#10:
    arm64.arm64AddLsl x2, x1, x0, lsl #3
    arm64.add x16, x2, 139328
    arm64.loadRegBaseDisp.word64 x8, [x16 + 0]
    arm64.cmp x8, 0
    arm64.b.eq __ia_slow
  probe#20:
    arm64.loadRegBaseDisp.word64 x2, [x8 + 16]
    arm64.cmp x2, 0
    arm64.b.eq __ia_slow
  probe#21:
    arm64.add x16, x8, 48
    arm64.ldar.word64 x2, [x16]
    arm64.cmp x2, 0
    arm64.b.ne __ia_slow
  probe#19:
    arm64.add x1, x1, 152112
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 8], x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 16]
    arm64.add x2, x2, 128
    arm64.storeBaseDispReg.word64 [x1 + 16], x2
    arm64.b pop#18
  walk#17:
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x2, 0
    arm64.b.eq __ia_slow
  walk#11:
    arm64.loadRegBaseDisp.word64 x3, [x2 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __ia_slow
  walk#14:
    arm64.loadRegBaseDisp.word64 x8, [x3 + 32]
    arm64.cmp x8, 255
    arm64.b.ge __ia_slow
  walk#16:
    arm64.movImm x16, 544
    arm64.mul x9, x8, x16
    arm64.add x9, x1, x9
    arm64.arm64AddLsl x9, x9, x0, lsl #3
    arm64.loadRegBaseDisp.word64 x9, [x9 + 608]
    arm64.cmp x9, 0
    arm64.b.eq __ia_slow
  ownprobe#23:
    arm64.loadRegBaseDisp.word64 x10, [x9 + 16]
    arm64.cmp x10, 0
    arm64.b.eq __ia_slow
  ownprobe#24:
    arm64.add x16, x9, 48
    arm64.ldar.word64 x10, [x16]
    arm64.cmp x10, x3
    arm64.b.ne __ia_slow
  ownprobe#22:
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.eq ownthread#26
  ownthread#25:
    arm64.loadRegBaseDisp.word64 x2, [x2 + 112]
    arm64.loadRegBaseDisp.word64 x3, [x2 + 512]
    arm64.add x3, x3, 1
    arm64.storeBaseDispReg.word64 [x2 + 512], x3
    arm64.loadRegBaseDisp.word64 x3, [x2 + 528]
    arm64.add x3, x3, 144
    arm64.storeBaseDispReg.word64 [x2 + 528], x3
  ownthread#26:
    arm64.movImm x16, 48
    arm64.mul x2, x8, x16
    arm64.add x1, x1, 139872
    arm64.add x1, x1, x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 8], x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 16]
    arm64.add x2, x2, 128
    arm64.storeBaseDispReg.word64 [x1 + 16], x2
    arm64.movRegReg x8, x9
  pop#18:
    arm64.leaRdata x1, __slab_class_geom
    arm64.arm64AddLsl x0, x1, x0, lsl #3
    arm64.loadRegBaseDisp.word32 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x9, [x8 + 8]
    arm64.cmp x9, 0
    arm64.b.ne pop#27
  pop#28:
    arm64.loadRegBaseDisp.word64 x0, [x8 + 40]
    arm64.loadRegBaseDisp.word64 x2, [x8 + 32]
    arm64.leaRdata x3, __slab_class_geom
    arm64.arm64AddLsl x2, x3, x2, lsl #3
    arm64.loadRegBaseDisp.word32 x2, [x2 + 4]
    arm64.mul x2, x1, x2
    arm64.loadRegBaseDisp.word64 x3, [x8 + 0]
    arm64.add x2, x3, x2
    arm64.cmp x0, x2
    arm64.b.ge pop#30
  pop#29:
    arm64.add x1, x0, x1
    arm64.storeBaseDispReg.word64 [x8 + 40], x1
    arm64.b pop#31
  pop#27:
    arm64.loadRegBaseDisp.word64 x0, [x9 + 0]
    arm64.storeBaseDispReg.word64 [x8 + 8], x0
    arm64.movRegReg x0, x9
    arm64.arm64MemFill 0x00
    arm64.movRegReg x0, x9
  pop#31:
    arm64.loadRegBaseDisp.word64 x1, [x8 + 16]
    arm64.sub x1, x1, 1
    arm64.storeBaseDispReg.word64 [x8 + 16], x1
    arm64.cmp x1, 0
    arm64.b.ne pop#33
  pop#34:
    arm64.add x16, x8, 48
    arm64.ldar.word64 x1, [x16]
    arm64.loadRegBaseDisp.word64 x2, [x8 + 32]
    arm64.leaGlobal x3, __slab_state
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.cmp x1, 0
    arm64.b.ne pop#35
  pop#36:
    arm64.add x1, x3, 138720
    arm64.add x1, x1, 608
    arm64.arm64AddLsl x1, x1, x2, lsl #3
    arm64.b pop#32
  pop#35:
    arm64.cmp x1, 0
    arm64.b.lt pop#38
  pop#37:
    arm64.loadRegBaseDisp.word64 x1, [x1 + 32]
    arm64.movImm x16, 544
    arm64.mul x1, x1, x16
    arm64.add x1, x3, x1
    arm64.add x1, x1, 608
    arm64.arm64AddLsl x1, x1, x2, lsl #3
  pop#32:
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
    arm64.movImm x1, 18446744073709551613
    arm64.add x16, x8, 48
    arm64.stlr.word64 [x16], x1
  pop#33:
    arm64.storeBaseDispReg.word64 [x0 + 0], x7
    arm64.add x0, x0, 16
  __ia_done:
    arm64.storeBaseDispReg.word64 [x0 + 24], x5
    arm64.storeBaseDispReg.word64 [x0 + 40], x6
    arm64.movImm x1, 8
    arm64.add x2, x0, 64
    arm64.storeBaseDispReg.word64 [x0 + 0], x2
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 32], x1
    arm64.storeBaseDispReg.word64 [x0 + 8], x4
  __il_body:
    arm64.movImm x19, 8
  __il_cont:
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
  __ia_slow:
    arm64.movImm x0, 128
    arm64.movRegReg x1, x7
    arm64.bl __mm_alloc_slow
    arm64.movImm x4, 8
    arm64.movImm x5, 8
    arm64.movImm x6, 0
    arm64.b __ia_done
  pop#30:
    arm64.movRegReg x0, x1
    arm64.bl __slab_past_end
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
  pop#38:
    arm64.movRegReg x0, x1
    arm64.bl __slab_evict_unowned
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaGlobal x0, __data_measureEight
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.movRegReg x0, x19
    arm64.bl __mm_retain
    arm64.movRegReg x20, x0
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.movRegReg x0, x19
    arm64.blr x1
    arm64.movRegReg x19, x0
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-constant-length-count-folds-to-the-constant.test:16: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}
```
