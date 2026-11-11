---
feature: static-variables
status: experimental
keywords: [static, let, global, module, type]
category: language
---

# Static Members and Top-Level Bindings: the cases that pin emitted code

The cases of `specs/static-variables.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: an-image-let-element-held-across-a-call-takes-no-retain -->
An element an accessor reads out of an image `static let` is immortal: the image owns it for the whole run, so
`main` holds `q` across the call to `churn` with no `__mm_retain` and drops it with no `__mm_decref`.

```maxon
typealias Num = int(i64.min to i64.max)
typealias Names = Array with String

type Point
	export var n as Num

	static let origins = [Point{n: 1}]
end 'Point'

function churn(xs Names) returns Num
	return xs.count() as Num
end 'churn'

function main() returns ExitCode
	let q = try Point.origins.get(0) otherwise panic("one point")
	let words = Names.create(0)
	words.push("alpha")
	let spent = churn(words)
	return (q.n + spent) as ExitCode
end 'main'
```
```exitcode
2
```
```TargetIr:x64-windows
data {
  __data_Point.origins@0 = i64 0
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
    x64.prologue 32
    x64.movRegImm32 rdx, 0
    x64.leaRegGlobal rax, __data_Point.origins
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.movRegImm32 rax, 0
    x64.loadRegBaseDisp.word64 rsi, [rcx + 40]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, critsplit#13
  __im_empty:
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, __im_slow
  critsplit#14:
    x64.movRegReg r10, rax
    x64.jmp __im_cont
  critsplit#13:
    x64.movRegReg r10, rax
  __im_cont:
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  tryok:
    x64.movRegImm32 rcx, 8
    x64.leaRegFunc rdx, [rip + __str_decref]
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.leaRegRdata rcx, [rip + __str_rec_1]  ; "alpha"
    x64.callDirect __str_retain
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __managed_push
  __il_body:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
  __il_cont:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.leaRegRegReg rbx, rcx, rax
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg rbx, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_get
    x64.movRegReg rbx, r8
    x64.jmp __im_cont
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at an-image-let-element-held-across-a-call-takes-no-retain.test:16: one point\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at an-image-let-element-held-across-a-call-takes-no-retain.test:20: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __data_Point.origins@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
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
    x64.prologue 32
    x64.movRegImm32 rdx, 0
    x64.leaRegGlobal rax, __data_Point.origins
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.movRegImm32 rax, 0
    x64.loadRegBaseDisp.word64 rsi, [rcx + 40]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, critsplit#13
  __im_empty:
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, __im_slow
  critsplit#14:
    x64.movRegReg r10, rax
    x64.jmp __im_cont
  critsplit#13:
    x64.movRegReg r10, rax
  __im_cont:
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  tryok:
    x64.movRegImm32 rcx, 8
    x64.leaRegFunc rdx, [rip + __str_decref]
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.leaRegRdata rcx, [rip + __str_rec_1]  ; "alpha"
    x64.callDirect __str_retain
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __managed_push
  __il_body:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
  __il_cont:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.leaRegRegReg rbx, rcx, rax
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_get
    x64.movRegReg rbx, r8
    x64.jmp __im_cont
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at an-image-let-element-held-across-a-call-takes-no-retain.test:16: one point\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at an-image-let-element-held-across-a-call-takes-no-retain.test:20: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __data_Point.origins@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x1, 0
    arm64.leaGlobal x0, __data_Point.origins
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 0
    arm64.b.ls __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x19, [x2 + 0]
    arm64.movImm x2, 0
    arm64.loadRegBaseDisp.word64 x3, [x0 + 40]
    arm64.cmp x3, 0
    arm64.b.eq critsplit#13
  __im_empty:
    arm64.cmp x19, 0
    arm64.b.eq __im_slow
  critsplit#14:
    arm64.movRegReg x9, x2
    arm64.b __im_cont
  critsplit#13:
    arm64.movRegReg x9, x2
  __im_cont:
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  tryok:
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_1  ; "alpha"
    arm64.bl __str_retain
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
  __il_cont:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.add x19, x1, x0
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.bl __managed_get
    arm64.movRegReg x19, x0
    arm64.b __im_cont
  tryerr:
    arm64.leaRdata x0, __str_blob_2  ; "panic at an-image-let-element-held-across-a-call-takes-no-retain.test:16: one point\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_4  ; "panic at an-image-let-element-held-across-a-call-takes-no-retain.test:20: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __data_Point.origins@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_hwcap_atomics@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x1, 0
    arm64.leaGlobal x0, __data_Point.origins
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 0
    arm64.b.ls __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x19, [x2 + 0]
    arm64.movImm x2, 0
    arm64.loadRegBaseDisp.word64 x3, [x0 + 40]
    arm64.cmp x3, 0
    arm64.b.eq critsplit#13
  __im_empty:
    arm64.cmp x19, 0
    arm64.b.eq __im_slow
  critsplit#14:
    arm64.movRegReg x9, x2
    arm64.b __im_cont
  critsplit#13:
    arm64.movRegReg x9, x2
  __im_cont:
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  tryok:
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_1  ; "alpha"
    arm64.bl __str_retain
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
  __il_cont:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.add x19, x1, x0
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.bl __managed_get
    arm64.movRegReg x19, x0
    arm64.b __im_cont
  tryerr:
    arm64.leaRdata x0, __str_blob_2  ; "panic at an-image-let-element-held-across-a-call-takes-no-retain.test:16: one point\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_4  ; "panic at an-image-let-element-held-across-a-call-takes-no-retain.test:20: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}
```

<!-- test: an-inline-record-static-let-over-constants-is-image-data -->
An inline record is a word, so a `static let` of one written as a struct literal over constants is that word laid down in
the image: nothing builds it before `main`, and each use is folded to the constant word. The Target IR's `data` block
lists every `.data` slot, so a slot the `static let` was given would show there.
```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	static let origin = Pair{lo: 1, hi: 2}
end 'Pair'

function main() returns ExitCode
	return (Pair.origin.lo + Pair.origin.hi) as ExitCode
end 'main'
```
```exitcode
3
```
```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.movRegImm32 r8, 3
  __rc_ok:
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 3
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 3
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
}
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.movRegImm32 r8, 3
  __rc_ok:
    x64.popReg rbp
    x64.ret
}
```

<!-- test: an-inline-record-field-of-a-heap-record-static-let-over-constants-is-image-data -->
A heap record holding an inline record is image data when every field is a constant, the inline record's
word included: nothing builds either before `main`, and `main` reads the record by its `.rdata` address, so the `static
let` owns no `.data` slot and the `data` block lists none for it.
```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	static let seven = Pair{lo: 7, hi: 8}
end 'Pair'

type Holder
	export let pair as Pair
	export let tag as Half

	static let holder = Holder{pair: Pair.seven, tag: 9}
end 'Holder'

function main() returns ExitCode
	return (Holder.holder.pair.lo + Holder.holder.pair.hi + Holder.holder.tag) as ExitCode
end 'main'
```
```exitcode
24
```
```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegRdata rax, [rip + __struct_rec_0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.movRegImm32 rcx, 4294967295
    x64.andRegReg rax, rax, rcx
    x64.leaRegRdata rcx, [rip + __struct_rec_0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.shrRegImm8 rcx, rcx, 32
    x64.movRegImm32 rdx, 4294967295
    x64.andRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRdata rcx, [rip + __struct_rec_0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.leaRegRegReg r8, rax, rcx
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at an-inline-record-field-of-a-heap-record-static-let-over-constants-is-image-data.test:19: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaRdata x0, __struct_rec_0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.movImm x1, 4294967295
    arm64.and x0, x0, x1
    arm64.leaRdata x1, __struct_rec_0
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.lsr x1, x1, 32
    arm64.movImm x2, 4294967295
    arm64.and x1, x1, x2
    arm64.add x0, x0, x1
    arm64.leaRdata x1, __struct_rec_0
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.add x0, x0, x1
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at an-inline-record-field-of-a-heap-record-static-let-over-constants-is-image-data.test:19: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaRdata x0, __struct_rec_0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.movImm x1, 4294967295
    arm64.and x0, x0, x1
    arm64.leaRdata x1, __struct_rec_0
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.lsr x1, x1, 32
    arm64.movImm x2, 4294967295
    arm64.and x1, x1, x2
    arm64.add x0, x0, x1
    arm64.leaRdata x1, __struct_rec_0
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.add x0, x0, x1
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at an-inline-record-field-of-a-heap-record-static-let-over-constants-is-image-data.test:19: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegRdata rax, [rip + __struct_rec_0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.movRegImm32 rcx, 4294967295
    x64.andRegReg rax, rax, rcx
    x64.leaRegRdata rcx, [rip + __struct_rec_0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.shrRegImm8 rcx, rcx, 32
    x64.movRegImm32 rdx, 4294967295
    x64.andRegReg rcx, rcx, rdx
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegRdata rcx, [rip + __struct_rec_0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.leaRegRegReg r8, rax, rcx
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at an-inline-record-field-of-a-heap-record-static-let-over-constants-is-image-data.test:19: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

<!-- test: an-inline-record-static-let-naming-another-inline-record-static-let-is-image-data -->
A `static let` bound to another inline-record `static let` is the same word, so it owns no `.data` slot either and each
use is folded to that word.
```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	static let first = Pair{lo: 3, hi: 4}
	static let second = Pair.first
end 'Pair'

function main() returns ExitCode
	return (Pair.second.lo + Pair.second.hi) as ExitCode
end 'main'
```
```exitcode
7
```
```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.movRegImm32 r8, 7
  __rc_ok:
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 7
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 7
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
}
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.movRegImm32 r8, 7
  __rc_ok:
    x64.popReg rbp
    x64.ret
}
```
