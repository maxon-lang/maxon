---
feature: function-value-local-cell
status: stable
keywords: [function value, by reference, mutable, cell, local, codegen]
category: codegen
---

# A local handed to a function value: the cases that pin emitted code

The cases of `specs/pass-by-reference.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: a-function-value-handed-a-bare-local-takes-no-cell -->
`adder` is a function value of arity 3 and no free function of arity 3 marks a parameter `mutable`, so `base`
handed to it as argument 0 is read as a value and `main` allocates no cell for it. `Ledger.addInto` is a `static`
method of three parameters whose first is `mutable`: it makes the program one that takes a parameter by
reference, so `main`'s locals are scanned for a cell, and it takes no part in the answer for `base`, because a
method is never a function value (E2078). `add3` is kept out of line by the `RequiredFunctions` block, so the call
through `adder` stays a call: `base` reaches `add3` as the value 4 in its first argument register. `spare`,
handed to the method's `mutable` position, is the cell in the same `main`: a `leaRegSlot` of a stack slot that is
initialised, passed to `Ledger.addInto` by address and read back. No `__mm_alloc` appears for either.
```maxon
typealias Tally = int(0 to u64.max)

type Ledger
	static function addInto(total mutable Tally, b Tally, c Tally)
		total = total + b + c
	end 'addInto'
end 'Ledger'

function add3(a Tally, b Tally, c Tally) returns Tally
	return a + b + c
end 'add3'

function main() returns ExitCode
	var base = 4 as Tally
	var spare = 1 as Tally
	let adder = add3

	base = adder(base, 5, 6)
	Ledger.addInto(spare, b: 2, c: 3)

	if base == 15 and spare == 6 'summed'
		return 0
	end 'summed'

	return 1
end 'main'
```
```exitcode
0
```

```RequiredFunctions
add3
```
```TargetIr:x64-windows
data {
  __slab_state@24 = i64 0
  __mrt_console_probe_stdin@32 = i8 0
  __mrt_console_probe_stdout@33 = i8 0
  __mrt_console_probe_stderr@34 = i8 0
  __mrt_program_started@35 = i8 0
}

func @Ledger.addInto {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.leaRegRegReg rdx, rsi, rdx
    x64.cmpRegReg rdx, rsi
    x64.jcc below, __rc_panic#2
  __rc_ok#1:
    x64.leaRegRegReg rax, rdx, rax
    x64.cmpRegReg rax, rdx
    x64.jcc below, __rc_panic#4
  __rc_ok#3:
    x64.storeBaseDispReg.word64 [rcx + 0], rax
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#2:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-function-value-handed-a-bare-local-takes-no-cell.test:6: Range check failed: value outside typealias 'Tally'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#4:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-function-value-handed-a-bare-local-takes-no-cell.test:6: Range check failed: value outside typealias 'Tally'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @add3 {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegRegReg rdx, rcx, rdx
    x64.cmpRegReg rdx, rcx
    x64.jcc below, __rc_panic#2
  __rc_ok#1:
    x64.leaRegRegReg r8, rdx, rax
    x64.cmpRegReg r8, rdx
    x64.jcc below, __rc_panic#4
  __rc_ok#3:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#2:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at a-function-value-handed-a-bare-local-takes-no-cell.test:11: Range check failed: value outside typealias 'Tally'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#4:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at a-function-value-handed-a-bare-local-takes-no-cell.test:11: Range check failed: value outside typealias 'Tally'\x0a"
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
    x64.prologue 48
    x64.movRegImm32 rcx, 4
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rdx, 16
    x64.movRegImm32 rdx, 0
    x64.leaRegSlot rbx, slot0
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rbx + 0], rdx
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rbx + 8], rdx
    x64.storeBaseDispReg.word64 [rbx + 0], rax
    x64.movRegImm32 rdx, 5
    x64.movRegImm32 rax, 6
    x64.callDirect add3
    x64.movRegReg r12, r8
    x64.movRegImm32 rdx, 2
    x64.movRegImm32 rax, 3
    x64.movRegReg rcx, rbx
    x64.callDirect Ledger.addInto
    x64.cmpRegImm32 r12, 15
    x64.movRegImm32 rax, 0
    x64.jcc notEqual, scmerge
  andrhs:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.cmpRegImm32 rax, 6
    x64.setccReg equal, rax
  scmerge:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  summed:
    x64.movRegImm32 r8, 0
    x64.epilogue 48
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
    x64.epilogue 48
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
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @Ledger.addInto {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.leaRegRegReg rdx, rsi, rdx
    x64.cmpRegReg rdx, rsi
    x64.jcc below, __rc_panic#2
  __rc_ok#1:
    x64.leaRegRegReg rax, rdx, rax
    x64.cmpRegReg rax, rdx
    x64.jcc below, __rc_panic#4
  __rc_ok#3:
    x64.storeBaseDispReg.word64 [rcx + 0], rax
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#2:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-function-value-handed-a-bare-local-takes-no-cell.test:6: Range check failed: value outside typealias 'Tally'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#4:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-function-value-handed-a-bare-local-takes-no-cell.test:6: Range check failed: value outside typealias 'Tally'\x0a"
    x64.callDirect mrt_panic
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @add3 {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegRegReg rdx, rcx, rdx
    x64.cmpRegReg rdx, rcx
    x64.jcc below, __rc_panic#2
  __rc_ok#1:
    x64.leaRegRegReg r8, rdx, rax
    x64.cmpRegReg r8, rdx
    x64.jcc below, __rc_panic#4
  __rc_ok#3:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#2:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at a-function-value-handed-a-bare-local-takes-no-cell.test:11: Range check failed: value outside typealias 'Tally'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#4:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at a-function-value-handed-a-bare-local-takes-no-cell.test:11: Range check failed: value outside typealias 'Tally'\x0a"
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
    x64.prologue 48
    x64.movRegImm32 rcx, 4
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rdx, 16
    x64.movRegImm32 rdx, 0
    x64.leaRegSlot rbx, slot0
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rbx + 0], rdx
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rbx + 8], rdx
    x64.storeBaseDispReg.word64 [rbx + 0], rax
    x64.movRegImm32 rdx, 5
    x64.movRegImm32 rax, 6
    x64.callDirect add3
    x64.movRegReg r12, r8
    x64.movRegImm32 rdx, 2
    x64.movRegImm32 rax, 3
    x64.movRegReg rcx, rbx
    x64.callDirect Ledger.addInto
    x64.cmpRegImm32 r12, 15
    x64.movRegImm32 rax, 0
    x64.jcc notEqual, scmerge
  andrhs:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.cmpRegImm32 rax, 6
    x64.setccReg equal, rax
  scmerge:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  summed:
    x64.movRegImm32 r8, 0
    x64.epilogue 48
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
    x64.epilogue 48
    x64.popReg r12
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

func @Ledger.addInto {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.add x1, x3, x1
    arm64.cmp x1, x3
    arm64.b.lo __rc_panic#2
  __rc_ok#1:
    arm64.add x2, x1, x2
    arm64.cmp x2, x1
    arm64.b.lo __rc_panic#4
  __rc_ok#3:
    arm64.storeBaseDispReg.word64 [x0 + 0], x2
    arm64.epilogue 16
    arm64.ret
  __rc_panic#2:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-function-value-handed-a-bare-local-takes-no-cell.test:6: Range check failed: value outside typealias 'Tally'\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 16
    arm64.ret
  __rc_panic#4:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-function-value-handed-a-bare-local-takes-no-cell.test:6: Range check failed: value outside typealias 'Tally'\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 16
    arm64.ret
}

func @add3 {
  entry:
    arm64.prologue 16
    arm64.add x1, x0, x1
    arm64.cmp x1, x0
    arm64.b.lo __rc_panic#2
  __rc_ok#1:
    arm64.add x0, x1, x2
    arm64.cmp x0, x1
    arm64.b.lo __rc_panic#4
  __rc_ok#3:
    arm64.epilogue 16
    arm64.ret
  __rc_panic#2:
    arm64.leaRdata x0, __str_blob_2  ; "panic at a-function-value-handed-a-bare-local-takes-no-cell.test:11: Range check failed: value outside typealias 'Tally'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __rc_panic#4:
    arm64.leaRdata x0, __str_blob_2  ; "panic at a-function-value-handed-a-bare-local-takes-no-cell.test:11: Range check failed: value outside typealias 'Tally'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movImm x0, 4
    arm64.movImm x1, 1
    arm64.movImm x2, 16
    arm64.movImm x2, 0
    arm64.leaRegSlot x19, slot0
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x19 + 0], x2
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x19 + 8], x2
    arm64.storeBaseDispReg.word64 [x19 + 0], x1
    arm64.movImm x1, 5
    arm64.movImm x2, 6
    arm64.bl add3
    arm64.movRegReg x20, x0
    arm64.movImm x1, 2
    arm64.movImm x2, 3
    arm64.movRegReg x0, x19
    arm64.bl Ledger.addInto
    arm64.cmp x20, 15
    arm64.movImm x0, 0
    arm64.b.ne scmerge
  andrhs:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 6
    arm64.cset x0, eq
  scmerge:
    arm64.cbz x0, ifcont
  summed:
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.epilogue 48
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

func @Ledger.addInto {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.add x1, x3, x1
    arm64.cmp x1, x3
    arm64.b.lo __rc_panic#2
  __rc_ok#1:
    arm64.add x2, x1, x2
    arm64.cmp x2, x1
    arm64.b.lo __rc_panic#4
  __rc_ok#3:
    arm64.storeBaseDispReg.word64 [x0 + 0], x2
    arm64.epilogue 16
    arm64.ret
  __rc_panic#2:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-function-value-handed-a-bare-local-takes-no-cell.test:6: Range check failed: value outside typealias 'Tally'\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 16
    arm64.ret
  __rc_panic#4:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-function-value-handed-a-bare-local-takes-no-cell.test:6: Range check failed: value outside typealias 'Tally'\x0a"
    arm64.bl mrt_panic
    arm64.epilogue 16
    arm64.ret
}

func @add3 {
  entry:
    arm64.prologue 16
    arm64.add x1, x0, x1
    arm64.cmp x1, x0
    arm64.b.lo __rc_panic#2
  __rc_ok#1:
    arm64.add x0, x1, x2
    arm64.cmp x0, x1
    arm64.b.lo __rc_panic#4
  __rc_ok#3:
    arm64.epilogue 16
    arm64.ret
  __rc_panic#2:
    arm64.leaRdata x0, __str_blob_2  ; "panic at a-function-value-handed-a-bare-local-takes-no-cell.test:11: Range check failed: value outside typealias 'Tally'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __rc_panic#4:
    arm64.leaRdata x0, __str_blob_2  ; "panic at a-function-value-handed-a-bare-local-takes-no-cell.test:11: Range check failed: value outside typealias 'Tally'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movImm x0, 4
    arm64.movImm x1, 1
    arm64.movImm x2, 16
    arm64.movImm x2, 0
    arm64.leaRegSlot x19, slot0
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x19 + 0], x2
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x19 + 8], x2
    arm64.storeBaseDispReg.word64 [x19 + 0], x1
    arm64.movImm x1, 5
    arm64.movImm x2, 6
    arm64.bl add3
    arm64.movRegReg x20, x0
    arm64.movImm x1, 2
    arm64.movImm x2, 3
    arm64.movRegReg x0, x19
    arm64.bl Ledger.addInto
    arm64.cmp x20, 15
    arm64.movImm x0, 0
    arm64.b.ne scmerge
  andrhs:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 6
    arm64.cset x0, eq
  scmerge:
    arm64.cbz x0, ifcont
  summed:
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.epilogue 48
    arm64.ret
}
```
