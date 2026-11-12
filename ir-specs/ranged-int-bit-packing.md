---
feature: ranged-int-bit-packing
status: selfhosted
keywords: [array, bit-packing, ranged-int, sub-byte, element-size, packed, bool]
category: memory
---

# Ranged-Int Sub-Byte Bit-Packing: the cases that pin emitted code

The cases of `specs/ranged-int-bit-packing.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it. Each reads the record's packed width through `.managed`, which only a source file under `stdlib/` or `runtime/` may read (E3185), so each compiles in the library tier (`<!-- tier: stdlib -->`).

## Tests

<!-- test: static-2bit-cast-packed -->
<!-- tier: stdlib -->
```maxon
typealias Q = int(0 to 3)

function main() returns ExitCode
	let a = [0 as Q, 1 as Q, 2 as Q, 3 as Q]
	if a.managed.elementSize() != -2 'notPacked'
		return 99
	end 'notPacked'
	var sum = 0 as FullRangeInt
	var i = 0 as ElementIndex
	while i < 4 'read'
		sum = sum + ((try a.get(i) otherwise 0) as FullRangeInt)
		i = i + 1
	end 'read'
	return sum
end 'main'
```
```exitcode
6
```
```RequiredRdata
u8[] 228
```

```TargetIr:x64-windows
data {
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
    x64.prologue 72
    x64.leaRegRdata rbx, [rip + __barr_rec_1]
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_element_size
    x64.cmpRegImm32 r8, -2
    x64.jcc equal, ifcont
  notPacked:
    x64.movRegImm32 r8, 99
    x64.epilogue 72
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 rdx, 0
  __us_test:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rbx + 0]
    x64.jmp whilehdr
  read:
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow
    x64.jmp __im_load
  tryerr:
    x64.movRegImm32 rcx, 0
    x64.jmp trycont
  __im_load:
    x64.imulRegRegImm32 rdi, rdx, 2
    x64.movRegReg r9, rdi
    x64.shrRegImm8 r9, rdi, 3
    x64.movRegReg rcx, rdi
    x64.andRegImm32 rcx, rdi, 7
    x64.leaRegRegReg rdi, rsi, r9
    x64.loadRegBaseDisp.byte rdi, [rdi + 0]
    x64.shrRegCl rdi, rdi
    x64.andRegImm32 rdi, rdi, 3
  tryok:
    x64.movRegReg rcx, rdi
  trycont:
    x64.leaRegRegReg r8, r8, rcx
    x64.leaRegRegImm32 rdx, rdx, 1
  whilehdr:
    x64.cmpRegImm32 rdx, 4
    x64.jcc below, read
  whileexit:
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 72
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rdx
    x64.storeSlotReg slot2, rsi
    x64.storeSlotReg slot3, r8
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.movRegReg rcx, r8
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rdx, slot1
    x64.loadRegSlot rsi, slot2
    x64.loadRegSlot r8, slot3
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  critsplit:
    x64.movRegReg rdi, rcx
    x64.jmp tryok
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at static-2bit-cast-packed.test:15: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 72
    x64.leaRegRdata rbx, [rip + __barr_rec_1]
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_element_size
    x64.cmpRegImm32 r8, -2
    x64.jcc equal, ifcont
  notPacked:
    x64.movRegImm32 r8, 99
    x64.epilogue 72
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 rdx, 0
  __us_test:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rbx + 0]
    x64.jmp whilehdr
  read:
    x64.cmpRegReg rdx, rax
    x64.jcc aboveEqual, __im_slow
    x64.jmp __im_load
  tryerr:
    x64.movRegImm32 rcx, 0
    x64.jmp trycont
  __im_load:
    x64.imulRegRegImm32 rdi, rdx, 2
    x64.movRegReg r9, rdi
    x64.shrRegImm8 r9, rdi, 3
    x64.movRegReg rcx, rdi
    x64.andRegImm32 rcx, rdi, 7
    x64.leaRegRegReg rdi, rsi, r9
    x64.loadRegBaseDisp.byte rdi, [rdi + 0]
    x64.shrRegCl rdi, rdi
    x64.andRegImm32 rdi, rdi, 3
  tryok:
    x64.movRegReg rcx, rdi
  trycont:
    x64.leaRegRegReg r8, r8, rcx
    x64.leaRegRegImm32 rdx, rdx, 1
  whilehdr:
    x64.cmpRegImm32 rdx, 4
    x64.jcc below, read
  whileexit:
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 72
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rdx
    x64.storeSlotReg slot2, rsi
    x64.storeSlotReg slot3, r8
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.movRegReg rcx, r8
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rdx, slot1
    x64.loadRegSlot rsi, slot2
    x64.loadRegSlot r8, slot3
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  critsplit:
    x64.movRegReg rdi, rcx
    x64.jmp tryok
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at static-2bit-cast-packed.test:15: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __slab_state@0 = i64 0
  __mrt_program_started@8 = i8 0
}

func @main {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x19
    arm64.leaRdata x19, __barr_rec_1
    arm64.movRegReg x0, x19
    arm64.bl __managed_element_size
    arm64.cmp x0, -2
    arm64.b.eq ifcont
  notPacked:
    arm64.movImm x0, 99
    arm64.loadRegSlot x19, slot4
    arm64.epilogue 64
    arm64.ret
  ifcont:
    arm64.movImm x0, 0
    arm64.movImm x1, 0
  __us_test:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.b whilehdr
  read:
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
    arm64.b __im_load
  tryerr:
    arm64.movImm x4, 0
    arm64.b trycont
  __im_load:
    arm64.lsl x4, x1, 1
    arm64.lsr x5, x4, 3
    arm64.and x4, x4, 7
    arm64.add x5, x3, x5
    arm64.loadRegBaseDisp.byte x5, [x5 + 0]
    arm64.lsrv x4, x5, x4
    arm64.and x4, x4, 3
  trycont:
    arm64.add x0, x0, x4
    arm64.add x1, x1, 1
  whilehdr:
    arm64.cmp x1, 4
    arm64.b.lo read
  whileexit:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegSlot x19, slot4
    arm64.epilogue 64
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.movRegReg x4, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b trycont
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at static-2bit-cast-packed.test:15: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.epilogue 64
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x19
    arm64.leaRdata x19, __barr_rec_1
    arm64.movRegReg x0, x19
    arm64.bl __managed_element_size
    arm64.cmp x0, -2
    arm64.b.eq ifcont
  notPacked:
    arm64.movImm x0, 99
    arm64.loadRegSlot x19, slot4
    arm64.epilogue 64
    arm64.ret
  ifcont:
    arm64.movImm x0, 0
    arm64.movImm x1, 0
  __us_test:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x19 + 0]
    arm64.b whilehdr
  read:
    arm64.cmp x1, x2
    arm64.b.hs __im_slow
    arm64.b __im_load
  tryerr:
    arm64.movImm x4, 0
    arm64.b trycont
  __im_load:
    arm64.lsl x4, x1, 1
    arm64.lsr x5, x4, 3
    arm64.and x4, x4, 7
    arm64.add x5, x3, x5
    arm64.loadRegBaseDisp.byte x5, [x5 + 0]
    arm64.lsrv x4, x5, x4
    arm64.and x4, x4, 3
  trycont:
    arm64.add x0, x0, x4
    arm64.add x1, x1, 1
  whilehdr:
    arm64.cmp x1, 4
    arm64.b.lo read
  whileexit:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegSlot x19, slot4
    arm64.epilogue 64
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.movRegReg x4, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b trycont
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at static-2bit-cast-packed.test:15: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.epilogue 64
    arm64.ret
}
```

<!-- test: static-4bit-cast-packed-cross-byte -->
<!-- tier: stdlib -->
```maxon
typealias N = int(0 to 15)

function main() returns ExitCode
	let a = [1 as N, 2 as N, 3 as N, 15 as N, 8 as N]
	if a.managed.elementSize() != -4 'notPacked'
		return 99
	end 'notPacked'
	return (try a.get(0) otherwise 0) + (try a.get(3) otherwise 0) + (try a.get(4) otherwise 0)
end 'main'
```
```exitcode
24
```
```RequiredRdata
u8[] 33, 243, 8
```

```TargetIr:x64-windows
data {
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
    x64.prologue 40
    x64.leaRegRdata rbx, [rip + __barr_rec_1]
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_element_size
    x64.cmpRegImm32 r8, -4
    x64.jcc equal, ifcont
  notPacked:
    x64.movRegImm32 r8, 99
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#13
    x64.jmp __im_load#14
  tryerr#4:
    x64.movRegImm32 rax, 0
    x64.jmp trycont#5
  __im_load#14:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.andRegImm32 rax, rax, 15
  trycont#5:
    x64.movRegImm32 rdx, 3
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegImm32 rcx, 3
    x64.jcc belowEqual, __im_slow#16
    x64.jmp __im_load#17
  tryerr#7:
    x64.movRegImm32 rcx, 0
    x64.jmp trycont#8
  __im_load#17:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.loadRegBaseDisp.byte rcx, [rcx + 1]
    x64.shrRegImm8 rcx, rcx, 4
    x64.andRegImm32 rcx, rcx, 15
  trycont#8:
    x64.leaRegRegReg rax, rax, rcx
    x64.movRegImm32 rdx, 4
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegImm32 rcx, 4
    x64.jcc belowEqual, __im_slow#19
    x64.jmp __im_load#20
  tryerr#10:
    x64.movRegImm32 rcx, 0
    x64.jmp trycont#11
  __im_load#20:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.loadRegBaseDisp.byte rcx, [rcx + 2]
    x64.andRegImm32 rcx, rcx, 15
  trycont#11:
    x64.leaRegRegReg r8, rax, rcx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#13:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#4
  critsplit#21:
    x64.movRegReg rax, r8
    x64.jmp trycont#5
  __im_slow#16:
    x64.storeSlotReg slot0, rax
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.loadRegSlot rax, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#7
  critsplit#22:
    x64.movRegReg rcx, r8
    x64.jmp trycont#8
  __im_slow#19:
    x64.storeSlotReg slot0, rax
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.loadRegSlot rax, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#10
  critsplit#23:
    x64.movRegReg rcx, r8
    x64.jmp trycont#11
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.leaRegRdata rbx, [rip + __barr_rec_1]
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_element_size
    x64.cmpRegImm32 r8, -4
    x64.jcc equal, ifcont
  notPacked:
    x64.movRegImm32 r8, 99
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc belowEqual, __im_slow#13
    x64.jmp __im_load#14
  tryerr#4:
    x64.movRegImm32 rax, 0
    x64.jmp trycont#5
  __im_load#14:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.byte rax, [rax + 0]
    x64.andRegImm32 rax, rax, 15
  trycont#5:
    x64.movRegImm32 rdx, 3
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegImm32 rcx, 3
    x64.jcc belowEqual, __im_slow#16
    x64.jmp __im_load#17
  tryerr#7:
    x64.movRegImm32 rcx, 0
    x64.jmp trycont#8
  __im_load#17:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.loadRegBaseDisp.byte rcx, [rcx + 1]
    x64.shrRegImm8 rcx, rcx, 4
    x64.andRegImm32 rcx, rcx, 15
  trycont#8:
    x64.leaRegRegReg rax, rax, rcx
    x64.movRegImm32 rdx, 4
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegImm32 rcx, 4
    x64.jcc belowEqual, __im_slow#19
    x64.jmp __im_load#20
  tryerr#10:
    x64.movRegImm32 rcx, 0
    x64.jmp trycont#11
  __im_load#20:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.loadRegBaseDisp.byte rcx, [rcx + 2]
    x64.andRegImm32 rcx, rcx, 15
  trycont#11:
    x64.leaRegRegReg r8, rax, rcx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#13:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#4
  critsplit#21:
    x64.movRegReg rax, r8
    x64.jmp trycont#5
  __im_slow#16:
    x64.storeSlotReg slot0, rax
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.loadRegSlot rax, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#7
  critsplit#22:
    x64.movRegReg rcx, r8
    x64.jmp trycont#8
  __im_slow#19:
    x64.storeSlotReg slot0, rax
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_get
    x64.loadRegSlot rax, slot0
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#10
  critsplit#23:
    x64.movRegReg rcx, r8
    x64.jmp trycont#11
}
```

```TargetIr:arm64-macos
data {
  __slab_state@0 = i64 0
  __mrt_program_started@8 = i8 0
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.leaRdata x19, __barr_rec_1
    arm64.movRegReg x0, x19
    arm64.bl __managed_element_size
    arm64.cmp x0, -4
    arm64.b.eq ifcont
  notPacked:
    arm64.movImm x0, 99
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  ifcont:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow#13
    arm64.b __im_load#14
  tryerr#4:
    arm64.movImm x0, 0
    arm64.b trycont#5
  __im_load#14:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.and x0, x0, 15
  trycont#5:
    arm64.movImm x1, 3
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
    arm64.cmp x2, 3
    arm64.b.ls __im_slow#16
    arm64.b __im_load#17
  tryerr#7:
    arm64.movImm x1, 0
    arm64.b trycont#8
  __im_load#17:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.loadRegBaseDisp.byte x1, [x1 + 1]
    arm64.lsr x1, x1, 4
    arm64.and x1, x1, 15
  trycont#8:
    arm64.add x0, x0, x1
    arm64.movImm x1, 4
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
    arm64.cmp x2, 4
    arm64.b.ls __im_slow#19
    arm64.b __im_load#20
  tryerr#10:
    arm64.movImm x1, 0
    arm64.b trycont#11
  __im_load#20:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.loadRegBaseDisp.byte x1, [x1 + 2]
    arm64.and x1, x1, 15
  trycont#11:
    arm64.add x0, x0, x1
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow#13:
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#4
    arm64.b trycont#5
  __im_slow#16:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#7
    arm64.b trycont#8
  __im_slow#19:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#10
    arm64.b trycont#11
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x19
    arm64.leaRdata x19, __barr_rec_1
    arm64.movRegReg x0, x19
    arm64.bl __managed_element_size
    arm64.cmp x0, -4
    arm64.b.eq ifcont
  notPacked:
    arm64.movImm x0, 99
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  ifcont:
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 0
    arm64.b.ls __im_slow#13
    arm64.b __im_load#14
  tryerr#4:
    arm64.movImm x0, 0
    arm64.b trycont#5
  __im_load#14:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.and x0, x0, 15
  trycont#5:
    arm64.movImm x1, 3
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
    arm64.cmp x2, 3
    arm64.b.ls __im_slow#16
    arm64.b __im_load#17
  tryerr#7:
    arm64.movImm x1, 0
    arm64.b trycont#8
  __im_load#17:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.loadRegBaseDisp.byte x1, [x1 + 1]
    arm64.lsr x1, x1, 4
    arm64.and x1, x1, 15
  trycont#8:
    arm64.add x0, x0, x1
    arm64.movImm x1, 4
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
    arm64.cmp x2, 4
    arm64.b.ls __im_slow#19
    arm64.b __im_load#20
  tryerr#10:
    arm64.movImm x1, 0
    arm64.b trycont#11
  __im_load#20:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.loadRegBaseDisp.byte x1, [x1 + 2]
    arm64.and x1, x1, 15
  trycont#11:
    arm64.add x0, x0, x1
    arm64.loadRegSlot x19, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow#13:
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr#4
    arm64.b trycont#5
  __im_slow#16:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#7
    arm64.b trycont#8
  __im_slow#19:
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x19
    arm64.bl __managed_get
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#10
    arm64.b trycont#11
}
```

<!-- test: static-bool-packed -->
<!-- tier: stdlib -->
```maxon
function main() returns ExitCode
	let a = [true, false, true, false]
	if a.managed.elementSize() != -1 'notPacked'
		return 99
	end 'notPacked'
	return 7
end 'main'
```
```exitcode
7
```
```RequiredRdata
u8[] 5
```

```TargetIr:x64-windows
data {
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
    x64.prologue 32
    x64.leaRegRdata rcx, [rip + __barr_rec_1]
    x64.callDirect __managed_element_size
    x64.cmpRegImm32 r8, -1
    x64.jcc equal, ifcont
  notPacked:
    x64.movRegImm32 r8, 99
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 7
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegRdata rcx, [rip + __barr_rec_1]
    x64.callDirect __managed_element_size
    x64.cmpRegImm32 r8, -1
    x64.jcc equal, ifcont
  notPacked:
    x64.movRegImm32 r8, 99
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 7
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __slab_state@0 = i64 0
  __mrt_program_started@8 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaRdata x0, __barr_rec_1
    arm64.bl __managed_element_size
    arm64.cmp x0, -1
    arm64.b.eq ifcont
  notPacked:
    arm64.movImm x0, 99
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 7
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaRdata x0, __barr_rec_1
    arm64.bl __managed_element_size
    arm64.cmp x0, -1
    arm64.b.eq ifcont
  notPacked:
    arm64.movImm x0, 99
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 7
    arm64.epilogue 16
    arm64.ret
}
```
