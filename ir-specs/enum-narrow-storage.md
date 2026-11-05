---
feature: enum-narrow-storage
status: selfhosted
keywords: [enum, storage, rdata, width, narrow, sign-extension]
category: codegen
---
# Enum Narrow Storage in .rdata

## Documentation

Enum values occupy the narrowest integer width that fits all their raw values:

- `u8` when raw values are 0..255
- `u16` when raw values are 0..65535
- `u32` when raw values are 0..4294967295
- `i8` when raw values fit −128..127 (any negative case forces signed)
- `i16` when raw values fit −32768..32767 with at least one negative
- `i32` when raw values fit −2147483648..2147483647 with at least one negative
- `i64`/`u64` otherwise

This narrowing applies to memory representation everywhere — struct fields, array elements, `.rdata` constants — not just the typealias range printed at the type-system level. `Array with Color` where `Color` is a three-case enum uses `element_size = 1`, and `[Color.red, Color.green, Color.blue]` lays out three contiguous bytes in `.rdata`.

Loads from narrow signed slots (`iN`) sign-extend into the 64-bit register that holds the SSA value; loads from narrow unsigned slots (`uN`) zero-extend. Stores write only the low N bytes of the i64 source operand.

## Tests

Each test below declares an enum whose raw-value range forces a specific storage width, builds a 3-element array literal, and asserts the exact bytes in `.rdata`. The chosen raw values fall outside the next-narrower type's range so a wrong-width emission would either fail the `RequiredRdata` byte-compare or wrap the value seen at runtime through `arr.get(...)`.

<!-- test: enum-rdata-u8-array -->
### u8-backed enum array packs as 1 byte per element
```maxon
enum Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	let palette = [Color.red, Color.green, Color.blue]
	let v = try palette.get(2) otherwise Color.red
	return v.ordinal
end 'main'
```
```exitcode
2
```
```RequiredRdata
u8[] 0, 1, 2
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
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 2
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.cmpRegImm32 rax, 2
    x64.jcc belowEqual, __im_slow
    x64.jmp __im_load
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 2]
  tryok:
    x64.movRegReg rbx, rax
  trycont:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
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
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 2
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.cmpRegImm32 rax, 2
    x64.jcc belowEqual, __im_slow
    x64.jmp __im_load
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 2]
  tryok:
    x64.movRegReg rbx, rax
  trycont:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 2
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 2
    arm64.b.ls __im_slow
    arm64.b __im_load
  tryerr:
    arm64.movImm x19, 0
    arm64.b trycont
  __im_load:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.byte x1, [x1 + 2]
  tryok:
    arm64.movRegReg x19, x1
  trycont:
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 2
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 2
    arm64.b.ls __im_slow
    arm64.b __im_load
  tryerr:
    arm64.movImm x19, 0
    arm64.b trycont
  __im_load:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.byte x1, [x1 + 2]
  tryok:
    arm64.movRegReg x19, x1
  trycont:
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok
}
```

<!-- test: enum-rdata-u16-array -->
### u16-backed enum array packs as 2 bytes per element
Raw values exceed u8.max so the backing must be at least u16. A u8 emission would either fail the rdata byte-compare or wrap `10000` to `16`.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Code
	a = 100
	b = 1000
	c = 10000
end 'Code'

function main() returns ExitCode
	let arr = [Code.a, Code.b, Code.c]
	let v = try arr.get(1) otherwise Code.a
	return ((v.rawValue as Integer) - 1000) as ExitCode
end 'main'
```
```exitcode
0
```
```RequiredRdata
u16[] 100, 1000, 10000
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
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm32 rax, 2
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow
    x64.jmp __im_load
  tryerr:
    x64.movRegImm32 rax, 100
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word16 rax, [rax + 2]
  trycont:
    x64.leaRegRegImm32 rbx, rax, -1000
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg rbx, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp trycont
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at enum-rdata-u16-array.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
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
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm32 rax, 2
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow
    x64.jmp __im_load
  tryerr:
    x64.movRegImm32 rax, 100
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word16 rax, [rax + 2]
  trycont:
    x64.leaRegRegImm32 rbx, rax, -1000
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp trycont
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at enum-rdata-u16-array.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 2
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 1
    arm64.b.ls __im_slow
    arm64.b __im_load
  tryerr:
    arm64.movImm x1, 100
    arm64.b trycont
  __im_load:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word16 x1, [x1 + 2]
  trycont:
    arm64.sub x19, x1, 1000
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b trycont
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at enum-rdata-u16-array.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 2
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 1
    arm64.b.ls __im_slow
    arm64.b __im_load
  tryerr:
    arm64.movImm x1, 100
    arm64.b trycont
  __im_load:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word16 x1, [x1 + 2]
  trycont:
    arm64.sub x19, x1, 1000
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b trycont
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at enum-rdata-u16-array.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
}
```

<!-- test: enum-rdata-u32-array -->
### u32-backed enum array packs as 4 bytes per element
Raw values exceed u16.max so the backing must be at least u32.
```maxon
enum Big
	small = 100
	medium = 100000
	large = 1000000
end 'Big'

function main() returns ExitCode
	let arr = [Big.small, Big.medium, Big.large]
	let v = try arr.get(0) otherwise Big.small
	return v.rawValue as ExitCode
end 'main'
```
```exitcode
100
```
```RequiredRdata
u32[] 100, 100000, 1000000
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
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm32 rax, 4
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow
    x64.jmp __im_load
  tryerr:
    x64.movRegImm32 rbx, 100
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word32 rax, [rax + 0]
  tryok:
    x64.movRegReg rbx, rax
  trycont:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
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
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm32 rax, 4
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow
    x64.jmp __im_load
  tryerr:
    x64.movRegImm32 rbx, 100
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word32 rax, [rax + 0]
  tryok:
    x64.movRegReg rbx, rax
  trycont:
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at enum-rdata-u32-array.test:11: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 4
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 0
    arm64.b.ls __im_slow
    arm64.b __im_load
  tryerr:
    arm64.movImm x19, 100
    arm64.b trycont
  __im_load:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word32 x1, [x1 + 0]
  tryok:
    arm64.movRegReg x19, x1
  trycont:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at enum-rdata-u32-array.test:11: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 4
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 0
    arm64.b.ls __im_slow
    arm64.b __im_load
  tryerr:
    arm64.movImm x19, 100
    arm64.b trycont
  __im_load:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word32 x1, [x1 + 0]
  tryok:
    arm64.movRegReg x19, x1
  trycont:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at enum-rdata-u32-array.test:11: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
}
```

<!-- test: enum-rdata-i8-array -->
### i8-backed enum array packs as 1 signed byte per element
Negative raw values force signed backing. This test exercises the sign-extending load — a buggy backend that emits `movzx` (zero-extending) on i8 would read `-1` back as `255`, making `arr.get(0).rawValue + 2 == 257`, which truncates and fails the exit-code check before the `RequiredRdata` byte-compare even runs. Two layers of detection.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Signal
	minus = -1
	zero = 0
	plus = 1
end 'Signal'

function main() returns ExitCode
	let arr = [Signal.minus, Signal.zero, Signal.plus]
	let v = try arr.get(0) otherwise Signal.zero
	return ((v.rawValue as Integer) + 2) as ExitCode
end 'main'
```
```exitcode
1
```
```RequiredRdata
i8[] -1, 0, 1
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
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm rax, 4294967297
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.movRegImm32 rcx, 0
    x64.movRegReg r10, rcx
  __im_cont:
    x64.shlRegImm8 rax, rax, 56
    x64.sarRegImm8 rax, rax, 56
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, trycont
  tryerr:
    x64.movRegImm32 rax, 0
  trycont:
    x64.leaRegRegImm32 rbx, rax, 2
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.jmp __im_cont
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
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm rax, 4294967297
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.movRegImm32 rcx, 0
    x64.movRegReg r10, rcx
  __im_cont:
    x64.shlRegImm8 rax, rax, 56
    x64.sarRegImm8 rax, rax, 56
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, trycont
  tryerr:
    x64.movRegImm32 rax, 0
  trycont:
    x64.leaRegRegImm32 rbx, rax, 2
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.jmp __im_cont
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 4294967297
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 0
    arm64.b.ls __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.byte x1, [x1 + 0]
    arm64.movImm x2, 0
    arm64.movRegReg x9, x2
  __im_cont:
    arm64.lsl x1, x1, 56
    arm64.asr x1, x1, 56
    arm64.cmp x9, 0
    arm64.b.eq trycont
  tryerr:
    arm64.movImm x1, 0
  trycont:
    arm64.add x19, x1, 2
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b __im_cont
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 4294967297
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 0
    arm64.b.ls __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.byte x1, [x1 + 0]
    arm64.movImm x2, 0
    arm64.movRegReg x9, x2
  __im_cont:
    arm64.lsl x1, x1, 56
    arm64.asr x1, x1, 56
    arm64.cmp x9, 0
    arm64.b.eq trycont
  tryerr:
    arm64.movImm x1, 0
  trycont:
    arm64.add x19, x1, 2
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b __im_cont
}
```

<!-- test: enum-rdata-i16-array -->
### i16-backed enum array packs as 2 signed bytes per element
Absolute raw values exceed i8.max so the backing must be at least i16 signed.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Offset
	behind = -200
	here = 0
	ahead = 200
end 'Offset'

function main() returns ExitCode
	let arr = [Offset.behind, Offset.here, Offset.ahead]
	let v = try arr.get(2) otherwise Offset.here
	return ((v.rawValue as Integer) - 100) as ExitCode
end 'main'
```
```exitcode
100
```
```RequiredRdata
i16[] -200, 0, 200
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
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm rax, 4294967298
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 2
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.cmpRegImm32 rax, 2
    x64.jcc belowEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word16 rax, [rax + 4]
    x64.movRegImm32 rcx, 0
    x64.movRegReg r10, rcx
  __im_cont:
    x64.shlRegImm8 rax, rax, 48
    x64.sarRegImm8 rax, rax, 48
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, trycont
  tryerr:
    x64.movRegImm32 rax, 0
  trycont:
    x64.leaRegRegImm32 rbx, rax, -100
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.jmp __im_cont
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at enum-rdata-i16-array.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
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
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm rax, 4294967298
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 2
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.cmpRegImm32 rax, 2
    x64.jcc belowEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word16 rax, [rax + 4]
    x64.movRegImm32 rcx, 0
    x64.movRegReg r10, rcx
  __im_cont:
    x64.shlRegImm8 rax, rax, 48
    x64.sarRegImm8 rax, rax, 48
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, trycont
  tryerr:
    x64.movRegImm32 rax, 0
  trycont:
    x64.leaRegRegImm32 rbx, rax, -100
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.jmp __im_cont
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at enum-rdata-i16-array.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 4294967298
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 2
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 2
    arm64.b.ls __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word16 x1, [x1 + 4]
    arm64.movImm x2, 0
    arm64.movRegReg x9, x2
  __im_cont:
    arm64.lsl x1, x1, 48
    arm64.asr x1, x1, 48
    arm64.cmp x9, 0
    arm64.b.eq trycont
  tryerr:
    arm64.movImm x1, 0
  trycont:
    arm64.sub x19, x1, 100
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b __im_cont
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at enum-rdata-i16-array.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 4294967298
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 2
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 2
    arm64.b.ls __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word16 x1, [x1 + 4]
    arm64.movImm x2, 0
    arm64.movRegReg x9, x2
  __im_cont:
    arm64.lsl x1, x1, 48
    arm64.asr x1, x1, 48
    arm64.cmp x9, 0
    arm64.b.eq trycont
  tryerr:
    arm64.movImm x1, 0
  trycont:
    arm64.sub x19, x1, 100
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b __im_cont
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at enum-rdata-i16-array.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
}
```

<!-- test: enum-rdata-i32-array -->
### i32-backed enum array packs as 4 signed bytes per element
Absolute raw values exceed i16.max so the backing must be at least i32 signed.
```maxon
typealias Integer = int(i64.min to i64.max)

enum BigOffset
	farBehind = -100000
	here = 0
	farAhead = 100000
end 'BigOffset'

function main() returns ExitCode
	let arr = [BigOffset.farBehind, BigOffset.here, BigOffset.farAhead]
	let v = try arr.get(0) otherwise BigOffset.here
	return ((v.rawValue as Integer) + 100100) as ExitCode
end 'main'
```
```exitcode
100
```
```RequiredRdata
i32[] -100000, 0, 100000
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
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm rax, 4294967300
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word32 rax, [rax + 0]
    x64.movRegImm32 rcx, 0
    x64.movRegReg r10, rcx
  __im_cont:
    x64.shlRegImm8 rax, rax, 32
    x64.sarRegImm8 rax, rax, 32
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, trycont
  tryerr:
    x64.movRegImm32 rax, 0
  trycont:
    x64.leaRegRegImm32 rbx, rax, 100100
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.jmp __im_cont
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
    x64.prologue 40
    x64.movRegImm32 rcx, 48
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.leaRegRdata rax, [rip + __barr_blob_0]
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 8], rax
    x64.movRegImm rax, 18446744073709551614
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.movRegImm rax, 4294967300
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [r8 + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.loadRegBaseDisp.word32 rax, [rax + 0]
    x64.movRegImm32 rcx, 0
    x64.movRegReg r10, rcx
  __im_cont:
    x64.shlRegImm8 rax, rax, 32
    x64.sarRegImm8 rax, rax, 32
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, trycont
  tryerr:
    x64.movRegImm32 rax, 0
  trycont:
    x64.leaRegRegImm32 rbx, rax, 100100
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_get
    x64.movRegReg rax, r8
    x64.loadRegSlot r8, slot0
    x64.jmp __im_cont
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at enum-rdata-i32-array.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 4294967300
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 0
    arm64.b.ls __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word32 x1, [x1 + 0]
    arm64.movImm x2, 0
    arm64.movRegReg x9, x2
  __im_cont:
    arm64.lsl x1, x1, 32
    arm64.asr x1, x1, 32
    arm64.cmp x9, 0
    arm64.b.eq trycont
  tryerr:
    arm64.movImm x1, 0
  trycont:
    arm64.add x19, x1, 100100
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b __im_cont
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at enum-rdata-i32-array.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.movImm x0, 48
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.leaRdata x1, __barr_blob_0
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
    arm64.movImm x1, 18446744073709551614
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.movImm x1, 4294967300
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 0
    arm64.b.ls __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word32 x1, [x1 + 0]
    arm64.movImm x2, 0
    arm64.movRegReg x9, x2
  __im_cont:
    arm64.lsl x1, x1, 32
    arm64.asr x1, x1, 32
    arm64.cmp x9, 0
    arm64.b.eq trycont
  tryerr:
    arm64.movImm x1, 0
  trycont:
    arm64.add x19, x1, 100100
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b __im_cont
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at enum-rdata-i32-array.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
}
```
