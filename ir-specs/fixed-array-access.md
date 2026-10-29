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

Each accessor is also taken as a function value, which keeps its body in the program after the inliner
splices `main`'s call to it. That body, compiled with no caller's array to fold against, is what
the pin holds.

## Tests

<!-- test: a-get-at-a-constant-index-is-one-load -->
`readThird` reads element 2 of an 8-word fixed array. The pin's `readThird` body is the C `planes[2]`: one
load from the array's address at displacement `2 * 8`, and the return. No compare, no branch, no call.
`main` stores distinct words around element 2, so a load from the wrong slot fails the exit code.
```maxon
typealias Word = bits(64)
typealias Planes = FixedArray with 8 Word

function readThird(planes Planes) returns Word
	return planes.get(2)
end 'readThird'

function main() returns ExitCode
	var planes = Planes.create()
	planes.set(1, value: 20)
	planes.set(2, value: 30)
	planes.set(3, value: 40)

	let reader = readThird

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
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
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
    x64.prologue 32
    x64.movRegImm32 rbx, 8
    x64.leaRegRdata rax, [rip + __layout_FixedArray_N8_Word]
  __il_body#21:
    x64.movRegImm32 rcx, 8
    x64.loadRegBaseDisp.word64 rdx, [rax + 40]
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __managed_resize
  __il_cont#20:
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 20
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#6
  __im_bounds#7:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#6
  __im_viewed#8:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#6
  __im_store#9:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rax
  __im_cont#5:
    x64.movRegImm32 rdx, 2
    x64.movRegImm32 rax, 30
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#11
  __im_bounds#12:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#11
  __im_viewed#13:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#11
  __im_store#14:
    x64.storeBaseDispReg.word64 [rcx + 16], rax
  __im_cont#10:
    x64.movRegImm32 rdx, 3
    x64.movRegImm32 rax, 40
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#16
  __im_bounds#17:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#16
  __im_viewed#18:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#16
  __im_store#19:
    x64.storeBaseDispReg.word64 [rcx + 24], rax
  __im_cont#15:
    x64.leaRegRdata rax, [rip + __fnrec_readThird]
  __il_body#23:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 16]
  __il_cont#22:
    x64.cmpRegImm32 rax, 30
    x64.jcc notEqual, ifcont
  third:
    x64.movRegImm32 rbx, 0
    x64.jmp exitdrop
  ifcont:
    x64.movRegImm32 rbx, 1
  exitdrop:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#6:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_set_unchecked
    x64.jmp __im_cont#5
  __im_slow#11:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_set_unchecked
    x64.jmp __im_cont#10
  __im_slow#16:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_set_unchecked
    x64.jmp __im_cont#15
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
    x64.prologue 32
    x64.movRegImm32 rbx, 8
    x64.leaRegRdata rax, [rip + __layout_FixedArray_N8_Word]
  __il_body#21:
    x64.movRegImm32 rcx, 8
    x64.loadRegBaseDisp.word64 rdx, [rax + 40]
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __managed_resize
  __il_cont#20:
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 20
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#6
  __im_bounds#7:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#6
  __im_viewed#8:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#6
  __im_store#9:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rax
  __im_cont#5:
    x64.movRegImm32 rdx, 2
    x64.movRegImm32 rax, 30
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#11
  __im_bounds#12:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#11
  __im_viewed#13:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#11
  __im_store#14:
    x64.storeBaseDispReg.word64 [rcx + 16], rax
  __im_cont#10:
    x64.movRegImm32 rdx, 3
    x64.movRegImm32 rax, 40
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#16
  __im_bounds#17:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#16
  __im_viewed#18:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#16
  __im_store#19:
    x64.storeBaseDispReg.word64 [rcx + 24], rax
  __im_cont#15:
    x64.leaRegRdata rax, [rip + __fnrec_readThird]
  __il_body#23:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 16]
  __il_cont#22:
    x64.cmpRegImm32 rax, 30
    x64.jcc notEqual, ifcont
  third:
    x64.movRegImm32 rbx, 0
    x64.jmp exitdrop
  ifcont:
    x64.movRegImm32 rbx, 1
  exitdrop:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#6:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_set_unchecked
    x64.jmp __im_cont#5
  __im_slow#11:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_set_unchecked
    x64.jmp __im_cont#10
  __im_slow#16:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_set_unchecked
    x64.jmp __im_cont#15
}
```

```TargetIr:arm64-macos
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 8
    arm64.leaRdata x0, __layout_FixedArray_N8_Word
  __il_body#21:
    arm64.movImm x1, 8
    arm64.loadRegBaseDisp.word64 x2, [x0 + 40]
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x2
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __managed_resize
  __il_cont#20:
    arm64.movImm x1, 1
    arm64.movImm x2, 20
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#6
  __im_bounds#7:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#6
  __im_viewed#8:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#6
  __im_store#9:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x1*8 + 0], x2
  __im_cont#5:
    arm64.movImm x1, 2
    arm64.movImm x2, 30
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#11
  __im_bounds#12:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#11
  __im_viewed#13:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#11
  __im_store#14:
    arm64.storeBaseDispReg.word64 [x0 + 16], x2
  __im_cont#10:
    arm64.movImm x1, 3
    arm64.movImm x2, 40
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#16
  __im_bounds#17:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#16
  __im_viewed#18:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#16
  __im_store#19:
    arm64.storeBaseDispReg.word64 [x0 + 24], x2
  __im_cont#15:
    arm64.leaRdata x0, __fnrec_readThird
  __il_body#23:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 16]
  __il_cont#22:
    arm64.cmp x0, 30
    arm64.b.ne ifcont
  third:
    arm64.movImm x19, 0
    arm64.b exitdrop
  ifcont:
    arm64.movImm x19, 1
  exitdrop:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow#6:
    arm64.movRegReg x0, x20
    arm64.bl __managed_set_unchecked
    arm64.b __im_cont#5
  __im_slow#11:
    arm64.movRegReg x0, x20
    arm64.bl __managed_set_unchecked
    arm64.b __im_cont#10
  __im_slow#16:
    arm64.movRegReg x0, x20
    arm64.bl __managed_set_unchecked
    arm64.b __im_cont#15
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 8
    arm64.leaRdata x0, __layout_FixedArray_N8_Word
  __il_body#21:
    arm64.movImm x1, 8
    arm64.loadRegBaseDisp.word64 x2, [x0 + 40]
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x2
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __managed_resize
  __il_cont#20:
    arm64.movImm x1, 1
    arm64.movImm x2, 20
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#6
  __im_bounds#7:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#6
  __im_viewed#8:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#6
  __im_store#9:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x1*8 + 0], x2
  __im_cont#5:
    arm64.movImm x1, 2
    arm64.movImm x2, 30
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#11
  __im_bounds#12:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#11
  __im_viewed#13:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#11
  __im_store#14:
    arm64.storeBaseDispReg.word64 [x0 + 16], x2
  __im_cont#10:
    arm64.movImm x1, 3
    arm64.movImm x2, 40
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#16
  __im_bounds#17:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#16
  __im_viewed#18:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#16
  __im_store#19:
    arm64.storeBaseDispReg.word64 [x0 + 24], x2
  __im_cont#15:
    arm64.leaRdata x0, __fnrec_readThird
  __il_body#23:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 16]
  __il_cont#22:
    arm64.cmp x0, 30
    arm64.b.ne ifcont
  third:
    arm64.movImm x19, 0
    arm64.b exitdrop
  ifcont:
    arm64.movImm x19, 1
  exitdrop:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow#6:
    arm64.movRegReg x0, x20
    arm64.bl __managed_set_unchecked
    arm64.b __im_cont#5
  __im_slow#11:
    arm64.movRegReg x0, x20
    arm64.bl __managed_set_unchecked
    arm64.b __im_cont#10
  __im_slow#16:
    arm64.movRegReg x0, x20
    arm64.bl __managed_set_unchecked
    arm64.b __im_cont#15
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

function readAt(planes Planes, at PlaneIndex) returns Word
	return planes.get(at)
end 'readAt'

function main() returns ExitCode
	var planes = Planes.create()
	planes.set(4, value: 50)
	planes.set(5, value: 60)
	planes.set(6, value: 70)

	let reader = readAt

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
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
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
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-get-at-a-ranged-index-is-one-load.test:6: Range check failed: value outside typealias 'PlaneIndex'\x0a"
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
    x64.prologue 32
    x64.movRegImm32 rbx, 8
    x64.leaRegRdata rax, [rip + __layout_FixedArray_N8_Word]
  __il_body:
    x64.movRegImm32 rcx, 8
    x64.loadRegBaseDisp.word64 rdx, [rax + 40]
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __managed_resize
  __il_cont#20:
    x64.movRegImm32 rdx, 4
    x64.movRegImm32 rax, 50
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#6
  __im_bounds#7:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#6
  __im_viewed#8:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#6
  __im_store#9:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rax
  __im_cont#5:
    x64.movRegImm32 rdx, 5
    x64.movRegImm32 rax, 60
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#11
  __im_bounds#12:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#11
  __im_viewed#13:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#11
  __im_store#14:
    x64.storeBaseDispReg.word64 [rcx + 40], rax
  __im_cont#10:
    x64.movRegImm32 rdx, 6
    x64.movRegImm32 rax, 70
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#16
  __im_bounds#17:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#16
  __im_viewed#18:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#16
  __im_store#19:
    x64.storeBaseDispReg.word64 [rcx + 48], rax
  __im_cont#15:
    x64.leaRegRdata rax, [rip + __fnrec_readAt]
  __rc_ok:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 40]
  __il_cont#22:
    x64.cmpRegImm32 rax, 60
    x64.jcc notEqual, ifcont
  sixth:
    x64.movRegImm32 rbx, 0
    x64.jmp exitdrop
  ifcont:
    x64.movRegImm32 rbx, 1
  exitdrop:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#6:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_set_unchecked
    x64.jmp __im_cont#5
  __im_slow#11:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_set_unchecked
    x64.jmp __im_cont#10
  __im_slow#16:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_set_unchecked
    x64.jmp __im_cont#15
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
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-get-at-a-ranged-index-is-one-load.test:6: Range check failed: value outside typealias 'PlaneIndex'\x0a"
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
    x64.prologue 32
    x64.movRegImm32 rbx, 8
    x64.leaRegRdata rax, [rip + __layout_FixedArray_N8_Word]
  __il_body:
    x64.movRegImm32 rcx, 8
    x64.loadRegBaseDisp.word64 rdx, [rax + 40]
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __managed_resize
  __il_cont#20:
    x64.movRegImm32 rdx, 4
    x64.movRegImm32 rax, 50
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#6
  __im_bounds#7:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#6
  __im_viewed#8:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#6
  __im_store#9:
    x64.storeBaseIndexScaleReg.word64 [rcx + rdx*8 + 0], rax
  __im_cont#5:
    x64.movRegImm32 rdx, 5
    x64.movRegImm32 rax, 60
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#11
  __im_bounds#12:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#11
  __im_viewed#13:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#11
  __im_store#14:
    x64.storeBaseDispReg.word64 [rcx + 40], rax
  __im_cont#10:
    x64.movRegImm32 rdx, 6
    x64.movRegImm32 rax, 70
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow#16
  __im_bounds#17:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow#16
  __im_viewed#18:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow#16
  __im_store#19:
    x64.storeBaseDispReg.word64 [rcx + 48], rax
  __im_cont#15:
    x64.leaRegRdata rax, [rip + __fnrec_readAt]
  __rc_ok:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 40]
  __il_cont#22:
    x64.cmpRegImm32 rax, 60
    x64.jcc notEqual, ifcont
  sixth:
    x64.movRegImm32 rbx, 0
    x64.jmp exitdrop
  ifcont:
    x64.movRegImm32 rbx, 1
  exitdrop:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#6:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_set_unchecked
    x64.jmp __im_cont#5
  __im_slow#11:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_set_unchecked
    x64.jmp __im_cont#10
  __im_slow#16:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_set_unchecked
    x64.jmp __im_cont#15
}
```

```TargetIr:arm64-macos
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
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
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-get-at-a-ranged-index-is-one-load.test:6: Range check failed: value outside typealias 'PlaneIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 8
    arm64.leaRdata x0, __layout_FixedArray_N8_Word
  __il_body:
    arm64.movImm x1, 8
    arm64.loadRegBaseDisp.word64 x2, [x0 + 40]
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x2
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __managed_resize
  __il_cont#20:
    arm64.movImm x1, 4
    arm64.movImm x2, 50
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#6
  __im_bounds#7:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#6
  __im_viewed#8:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#6
  __im_store#9:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x1*8 + 0], x2
  __im_cont#5:
    arm64.movImm x1, 5
    arm64.movImm x2, 60
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#11
  __im_bounds#12:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#11
  __im_viewed#13:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#11
  __im_store#14:
    arm64.storeBaseDispReg.word64 [x0 + 40], x2
  __im_cont#10:
    arm64.movImm x1, 6
    arm64.movImm x2, 70
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#16
  __im_bounds#17:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#16
  __im_viewed#18:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#16
  __im_store#19:
    arm64.storeBaseDispReg.word64 [x0 + 48], x2
  __im_cont#15:
    arm64.leaRdata x0, __fnrec_readAt
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 40]
  __il_cont#22:
    arm64.cmp x0, 60
    arm64.b.ne ifcont
  sixth:
    arm64.movImm x19, 0
    arm64.b exitdrop
  ifcont:
    arm64.movImm x19, 1
  exitdrop:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow#6:
    arm64.movRegReg x0, x20
    arm64.bl __managed_set_unchecked
    arm64.b __im_cont#5
  __im_slow#11:
    arm64.movRegReg x0, x20
    arm64.bl __managed_set_unchecked
    arm64.b __im_cont#10
  __im_slow#16:
    arm64.movRegReg x0, x20
    arm64.bl __managed_set_unchecked
    arm64.b __im_cont#15
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
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-get-at-a-ranged-index-is-one-load.test:6: Range check failed: value outside typealias 'PlaneIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 8
    arm64.leaRdata x0, __layout_FixedArray_N8_Word
  __il_body:
    arm64.movImm x1, 8
    arm64.loadRegBaseDisp.word64 x2, [x0 + 40]
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x2
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __managed_resize
  __il_cont#20:
    arm64.movImm x1, 4
    arm64.movImm x2, 50
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#6
  __im_bounds#7:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#6
  __im_viewed#8:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#6
  __im_store#9:
    arm64.storeBaseIndexScaleReg.word64 [x0 + x1*8 + 0], x2
  __im_cont#5:
    arm64.movImm x1, 5
    arm64.movImm x2, 60
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#11
  __im_bounds#12:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#11
  __im_viewed#13:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#11
  __im_store#14:
    arm64.storeBaseDispReg.word64 [x0 + 40], x2
  __im_cont#10:
    arm64.movImm x1, 6
    arm64.movImm x2, 70
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow#16
  __im_bounds#17:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow#16
  __im_viewed#18:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow#16
  __im_store#19:
    arm64.storeBaseDispReg.word64 [x0 + 48], x2
  __im_cont#15:
    arm64.leaRdata x0, __fnrec_readAt
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 40]
  __il_cont#22:
    arm64.cmp x0, 60
    arm64.b.ne ifcont
  sixth:
    arm64.movImm x19, 0
    arm64.b exitdrop
  ifcont:
    arm64.movImm x19, 1
  exitdrop:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow#6:
    arm64.movRegReg x0, x20
    arm64.bl __managed_set_unchecked
    arm64.b __im_cont#5
  __im_slow#11:
    arm64.movRegReg x0, x20
    arm64.bl __managed_set_unchecked
    arm64.b __im_cont#10
  __im_slow#16:
    arm64.movRegReg x0, x20
    arm64.bl __managed_set_unchecked
    arm64.b __im_cont#15
}
```

<!-- test: a-set-at-a-ranged-index-is-one-store -->
`writeAt` stores a word at a `PlaneIndex`: the address `base + at * 8` and one store, with no compare of
`at` against the count 8 and no branch to a throw path. A write is the one access a value-semantics buffer
cannot make blind: the buffer may be shared with a copy or be an immortal image built from constants, so the
store is preceded by the same ownership tests `Array.set` carries, and a cold arm detaches the buffer
through `__managed_set_unchecked` before storing. As in `readAt`, the `PlaneIndex` parameter's entry cascade
precedes it. `main` checks the written slot and its neighbour, so a store to the wrong slot fails the exit
code.
```maxon
typealias Word = bits(64)
typealias Planes = FixedArray with 8 Word
typealias PlaneIndex = int(0 to 7) implements ElementIndex

function writeAt(planes Planes, at PlaneIndex, value Word)
	planes.set(at, value: value)
end 'writeAt'

function main() returns ExitCode
	var planes = Planes.create()
	let writer = writeAt

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
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_console_probe_stdin@24 = i8 0
  __mrt_console_probe_stdout@25 = i8 0
  __mrt_console_probe_stderr@26 = i8 0
  __mrt_program_started@27 = i8 0
}

func @writeAt {
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
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -24
    x64.loadRegBaseDisp.word64 rdi, [rdi + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.storeBaseIndexScaleReg.word64 [rsi + rdx*8 + 0], rax
  __im_cont:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_set_unchecked
    x64.jmp __im_cont
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-set-at-a-ranged-index-is-one-store.test:6: Range check failed: value outside typealias 'PlaneIndex'\x0a"
    x64.callDirect mrt_panic
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
    x64.prologue 32
    x64.movRegImm32 rbx, 8
    x64.leaRegRdata rax, [rip + __layout_FixedArray_N8_Word]
  __il_body:
    x64.movRegImm32 rcx, 8
    x64.loadRegBaseDisp.word64 rdx, [rax + 40]
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __managed_resize
  __il_cont#9:
    x64.leaRegRdata rax, [rip + __fnrec_writeAt]
    x64.movRegImm32 rdx, 6
    x64.movRegImm32 rax, 77
  __rc_ok:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.storeBaseDispReg.word64 [rcx + 48], rax
  __il_cont#11:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 48]
  __im_cont#7:
    x64.cmpRegImm32 rax, 77
    x64.movRegImm32 rax, 0
    x64.jcc notEqual, scmerge
  andrhs:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 40]
  __im_cont#8:
    x64.cmpRegImm32 rax, 0
    x64.setccReg equal, rax
  scmerge:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  written:
    x64.movRegImm32 rbx, 0
    x64.jmp exitdrop
  ifcont:
    x64.movRegImm32 rbx, 1
  exitdrop:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_set_unchecked
    x64.jmp __il_cont#11
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

func @writeAt {
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
    x64.loadRegBaseDisp.word64 rsi, [rcx + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -24
    x64.loadRegBaseDisp.word64 rdi, [rdi + 16]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.storeBaseIndexScaleReg.word64 [rsi + rdx*8 + 0], rax
  __im_cont:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_set_unchecked
    x64.jmp __im_cont
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-set-at-a-ranged-index-is-one-store.test:6: Range check failed: value outside typealias 'PlaneIndex'\x0a"
    x64.callDirect mrt_panic
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
    x64.prologue 32
    x64.movRegImm32 rbx, 8
    x64.leaRegRdata rax, [rip + __layout_FixedArray_N8_Word]
  __il_body:
    x64.movRegImm32 rcx, 8
    x64.loadRegBaseDisp.word64 rdx, [rax + 40]
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __managed_resize
  __il_cont#9:
    x64.leaRegRdata rax, [rip + __fnrec_writeAt]
    x64.movRegImm32 rdx, 6
    x64.movRegImm32 rax, 77
  __rc_ok:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.storeBaseDispReg.word64 [rcx + 48], rax
  __il_cont#11:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 48]
  __im_cont#7:
    x64.cmpRegImm32 rax, 77
    x64.movRegImm32 rax, 0
    x64.jcc notEqual, scmerge
  andrhs:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 40]
  __im_cont#8:
    x64.cmpRegImm32 rax, 0
    x64.setccReg equal, rax
  scmerge:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  written:
    x64.movRegImm32 rbx, 0
    x64.jmp exitdrop
  ifcont:
    x64.movRegImm32 rbx, 1
  exitdrop:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_set_unchecked
    x64.jmp __il_cont#11
}
```

```TargetIr:arm64-macos
data {
  __slab_arena_list@0 = i64 0
  __slab_arena_map_l1@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @writeAt {
  entry:
    arm64.prologue 16
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x1, 7
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x4, x3, 24
    arm64.loadRegBaseDisp.word64 x4, [x4 + 16]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.storeBaseIndexScaleReg.word64 [x3 + x1*8 + 0], x2
  __im_cont:
    arm64.epilogue 16
    arm64.ret
  __im_slow:
    arm64.bl __managed_set_unchecked
    arm64.b __im_cont
  __rc_panic:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-set-at-a-ranged-index-is-one-store.test:6: Range check failed: value outside typealias 'PlaneIndex'\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 8
    arm64.leaRdata x0, __layout_FixedArray_N8_Word
  __il_body:
    arm64.movImm x1, 8
    arm64.loadRegBaseDisp.word64 x2, [x0 + 40]
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x2
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __managed_resize
  __il_cont#9:
    arm64.leaRdata x0, __fnrec_writeAt
    arm64.movImm x1, 6
    arm64.movImm x2, 77
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.storeBaseDispReg.word64 [x0 + 48], x2
  __il_cont#11:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 48]
  __im_cont#7:
    arm64.cmp x0, 77
    arm64.movImm x0, 0
    arm64.b.ne scmerge
  andrhs:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 40]
  __im_cont#8:
    arm64.cmp x0, 0
    arm64.cset x0, eq
  scmerge:
    arm64.cbz x0, ifcont
  written:
    arm64.movImm x19, 0
    arm64.b exitdrop
  ifcont:
    arm64.movImm x19, 1
  exitdrop:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.movRegReg x0, x20
    arm64.bl __managed_set_unchecked
    arm64.b __il_cont#11
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

func @writeAt {
  entry:
    arm64.prologue 16
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x1, 7
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 16]
    arm64.cmp x3, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x4, x3, 24
    arm64.loadRegBaseDisp.word64 x4, [x4 + 16]
    arm64.cmp x4, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.storeBaseIndexScaleReg.word64 [x3 + x1*8 + 0], x2
  __im_cont:
    arm64.epilogue 16
    arm64.ret
  __im_slow:
    arm64.bl __managed_set_unchecked
    arm64.b __im_cont
  __rc_panic:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-set-at-a-ranged-index-is-one-store.test:6: Range check failed: value outside typealias 'PlaneIndex'\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 8
    arm64.leaRdata x0, __layout_FixedArray_N8_Word
  __il_body:
    arm64.movImm x1, 8
    arm64.loadRegBaseDisp.word64 x2, [x0 + 40]
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x2
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __managed_resize
  __il_cont#9:
    arm64.leaRdata x0, __fnrec_writeAt
    arm64.movImm x1, 6
    arm64.movImm x2, 77
  __rc_ok:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.storeBaseDispReg.word64 [x0 + 48], x2
  __il_cont#11:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 48]
  __im_cont#7:
    arm64.cmp x0, 77
    arm64.movImm x0, 0
    arm64.b.ne scmerge
  andrhs:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 40]
  __im_cont#8:
    arm64.cmp x0, 0
    arm64.cset x0, eq
  scmerge:
    arm64.cbz x0, ifcont
  written:
    arm64.movImm x19, 0
    arm64.b exitdrop
  ifcont:
    arm64.movImm x19, 1
  exitdrop:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.movRegReg x0, x20
    arm64.bl __managed_set_unchecked
    arm64.b __il_cont#11
}
```
