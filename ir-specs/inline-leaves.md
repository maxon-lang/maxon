---
feature: inline-leaves
status: experimental
keywords: [optimizer, inliner, leaf, codegen, panic, runtime]
category: codegen
---

# Inlining Tiny Leaf Functions: the cases that pin emitted code

The cases of `specs/inline-leaves.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: a-runtime-body-that-asks-to-be-spliced-is-spliced-past-the-budget -->
⭐⭐ **THE BUDGET IS A COST RULE, AND `__Raw.splicedAtEverySite()` OVERRULES IT.** `__probe_wide` is a
runtime body far past `MaxInlinedLeafOps`, so the budget alone refuses it — and it has TWO call sites, so
the called-once rule never looks at it either. The row is the whole difference: the pin below is
`__probe_wide_caller` with both copies of the body spliced in and no `callDirect` left.

⚠ **A COMPILER WITHOUT THE ROW RENDERS `call __probe_wide` TWICE HERE**, which is what this case exists
to hold. It is the same shape the `tlsSlotLoad` case above uses, and for the same reason: a pin
records what the compiler that minted it renders, so the reading that makes it evidence is the one taken from
a compiler built without the change.

⚠ **THE ROW OVERRIDES WHAT INLINING IS WORTH AND NEVER WHAT IT WOULD BREAK.** Both bodies here are tier
source, which is what keeps `InlineLeaves.splicingWouldWidenTheSafePoint` — a correctness rule the row
does not touch — out of the way; a tier callee spliced into a caller the compiler does not own is refused
by it, and the row then ends the compile rather than falling back to a call.
```maxon
// --- runtime-file: Probe.maxon
module function __probe_wide(seed MachineWord) returns MachineWord
	__Raw.splicedAtEverySite()

	var acc = seed + 1
	acc = acc + 2
	acc = acc + 3
	acc = acc + 4
	acc = acc + 5
	acc = acc + 6
	acc = acc + 7
	acc = acc + 8
	acc = acc + 9
	acc = acc + 10
	acc = acc + 11
	acc = acc + 12
	acc = acc + 13
	acc = acc + 14

	return acc
end '__probe_wide'

function __probe_wide_caller(seed ExitCode) returns ExitCode
	let base = __probe_wide(seed as MachineWord)
	let again = __probe_wide(base and 1)

	return (base + again) as ExitCode
end '__probe_wide_caller'
// --- stdlib-overlay: Builtins.maxon
export function probeWideSplice(seed ExitCode) returns ExitCode
	return __probe_wide_caller(seed)
end 'probeWideSplice'
// --- file: main.maxon
function main() returns ExitCode
	return probeWideSplice(0)
end 'main'
```
```exitcode
211
```
```RequiredRuntime
__probe_wide_caller
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
    x64.movRegImm32 rcx, 0
  __il_body:
    x64.callDirect __probe_wide_caller
  __il_cont:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @__probe_wide_caller {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#2
  __rc_chk#3:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg rcx, rax
    x64.jcc greater, __rc_panic#2
  __il_body#9:
    x64.leaRegRegImm32 rax, rcx, 1
    x64.leaRegRegImm32 rax, rax, 2
    x64.leaRegRegImm32 rax, rax, 3
    x64.leaRegRegImm32 rax, rax, 4
    x64.leaRegRegImm32 rax, rax, 5
    x64.leaRegRegImm32 rax, rax, 6
    x64.leaRegRegImm32 rax, rax, 7
    x64.leaRegRegImm32 rax, rax, 8
    x64.leaRegRegImm32 rax, rax, 9
    x64.leaRegRegImm32 rax, rax, 10
    x64.leaRegRegImm32 rax, rax, 11
    x64.leaRegRegImm32 rax, rax, 12
    x64.leaRegRegImm32 rax, rax, 13
    x64.leaRegRegImm32 rax, rax, 14
  __il_cont#8:
    x64.movRegReg rcx, rax
    x64.andRegImm32 rcx, rax, 1
  __il_body#10:
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.leaRegRegImm32 rcx, rcx, 2
    x64.leaRegRegImm32 rcx, rcx, 3
    x64.leaRegRegImm32 rcx, rcx, 4
    x64.leaRegRegImm32 rcx, rcx, 5
    x64.leaRegRegImm32 rcx, rcx, 6
    x64.leaRegRegImm32 rcx, rcx, 7
    x64.leaRegRegImm32 rcx, rcx, 8
    x64.leaRegRegImm32 rcx, rcx, 9
    x64.leaRegRegImm32 rcx, rcx, 10
    x64.leaRegRegImm32 rcx, rcx, 11
    x64.leaRegRegImm32 rcx, rcx, 12
    x64.leaRegRegImm32 rcx, rcx, 13
    x64.leaRegRegImm32 rcx, rcx, 14
  __il_cont#7:
    x64.leaRegRegReg r8, rax, rcx
  __rc_chk#6:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic#5
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#2:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at Probe.maxon:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#5:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at Probe.maxon:26: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
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
    x64.movRegImm32 rcx, 0
  __il_body:
    x64.callDirect __probe_wide_caller
  __il_cont:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @__probe_wide_caller {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#2
  __rc_chk#3:
    x64.cmpRegImm32 rcx, 255
    x64.jcc greater, __rc_panic#2
  __il_body#9:
    x64.leaRegRegImm32 rax, rcx, 1
    x64.leaRegRegImm32 rax, rax, 2
    x64.leaRegRegImm32 rax, rax, 3
    x64.leaRegRegImm32 rax, rax, 4
    x64.leaRegRegImm32 rax, rax, 5
    x64.leaRegRegImm32 rax, rax, 6
    x64.leaRegRegImm32 rax, rax, 7
    x64.leaRegRegImm32 rax, rax, 8
    x64.leaRegRegImm32 rax, rax, 9
    x64.leaRegRegImm32 rax, rax, 10
    x64.leaRegRegImm32 rax, rax, 11
    x64.leaRegRegImm32 rax, rax, 12
    x64.leaRegRegImm32 rax, rax, 13
    x64.leaRegRegImm32 rax, rax, 14
  __il_cont#8:
    x64.movRegReg rcx, rax
    x64.andRegImm32 rcx, rax, 1
  __il_body#10:
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.leaRegRegImm32 rcx, rcx, 2
    x64.leaRegRegImm32 rcx, rcx, 3
    x64.leaRegRegImm32 rcx, rcx, 4
    x64.leaRegRegImm32 rcx, rcx, 5
    x64.leaRegRegImm32 rcx, rcx, 6
    x64.leaRegRegImm32 rcx, rcx, 7
    x64.leaRegRegImm32 rcx, rcx, 8
    x64.leaRegRegImm32 rcx, rcx, 9
    x64.leaRegRegImm32 rcx, rcx, 10
    x64.leaRegRegImm32 rcx, rcx, 11
    x64.leaRegRegImm32 rcx, rcx, 12
    x64.leaRegRegImm32 rcx, rcx, 13
    x64.leaRegRegImm32 rcx, rcx, 14
  __il_cont#7:
    x64.leaRegRegReg r8, rax, rcx
  __rc_chk#6:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic#5
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#2:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at Probe.maxon:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#5:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at Probe.maxon:26: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    arm64.movImm x0, 0
  __il_body:
    arm64.bl __probe_wide_caller
  __il_cont:
    arm64.epilogue 16
    arm64.ret
}

func @__probe_wide_caller {
  entry:
    arm64.prologue 16
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#2
  __rc_chk#3:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic#2
  __il_body#9:
    arm64.add x0, x0, 1
    arm64.add x0, x0, 2
    arm64.add x0, x0, 3
    arm64.add x0, x0, 4
    arm64.add x0, x0, 5
    arm64.add x0, x0, 6
    arm64.add x0, x0, 7
    arm64.add x0, x0, 8
    arm64.add x0, x0, 9
    arm64.add x0, x0, 10
    arm64.add x0, x0, 11
    arm64.add x0, x0, 12
    arm64.add x0, x0, 13
    arm64.add x0, x0, 14
  __il_cont#8:
    arm64.and x1, x0, 1
  __il_body#10:
    arm64.add x1, x1, 1
    arm64.add x1, x1, 2
    arm64.add x1, x1, 3
    arm64.add x1, x1, 4
    arm64.add x1, x1, 5
    arm64.add x1, x1, 6
    arm64.add x1, x1, 7
    arm64.add x1, x1, 8
    arm64.add x1, x1, 9
    arm64.add x1, x1, 10
    arm64.add x1, x1, 11
    arm64.add x1, x1, 12
    arm64.add x1, x1, 13
    arm64.add x1, x1, 14
  __il_cont#7:
    arm64.add x0, x0, x1
  __rc_chk#6:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic#5
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic#2:
    arm64.leaRdata x0, __str_blob_1  ; "panic at Probe.maxon:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __rc_panic#5:
    arm64.leaRdata x0, __str_blob_2  ; "panic at Probe.maxon:26: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    arm64.movImm x0, 0
  __il_body:
    arm64.bl __probe_wide_caller
  __il_cont:
    arm64.epilogue 16
    arm64.ret
}

func @__probe_wide_caller {
  entry:
    arm64.prologue 16
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#2
  __rc_chk#3:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic#2
  __il_body#9:
    arm64.add x0, x0, 1
    arm64.add x0, x0, 2
    arm64.add x0, x0, 3
    arm64.add x0, x0, 4
    arm64.add x0, x0, 5
    arm64.add x0, x0, 6
    arm64.add x0, x0, 7
    arm64.add x0, x0, 8
    arm64.add x0, x0, 9
    arm64.add x0, x0, 10
    arm64.add x0, x0, 11
    arm64.add x0, x0, 12
    arm64.add x0, x0, 13
    arm64.add x0, x0, 14
  __il_cont#8:
    arm64.and x1, x0, 1
  __il_body#10:
    arm64.add x1, x1, 1
    arm64.add x1, x1, 2
    arm64.add x1, x1, 3
    arm64.add x1, x1, 4
    arm64.add x1, x1, 5
    arm64.add x1, x1, 6
    arm64.add x1, x1, 7
    arm64.add x1, x1, 8
    arm64.add x1, x1, 9
    arm64.add x1, x1, 10
    arm64.add x1, x1, 11
    arm64.add x1, x1, 12
    arm64.add x1, x1, 13
    arm64.add x1, x1, 14
  __il_cont#7:
    arm64.add x0, x0, x1
  __rc_chk#6:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic#5
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic#2:
    arm64.leaRdata x0, __str_blob_1  ; "panic at Probe.maxon:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __rc_panic#5:
    arm64.leaRdata x0, __str_blob_2  ; "panic at Probe.maxon:26: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: a-body-that-asks-to-be-spliced-may-call-another-that-does -->
⭐⭐ **THE ONE CASCADE THE ROW BUYS, AND WHY IT IS NOT OPTIONAL.** A builder composes its emitters freely
and pays nothing for it — `emitElementByteLen` calls `emitElementBits` and `emitBitsToBytes`, and all three
land inline at every site. A tier family that could hold no helpers would be a family written in one
function, so a body declaring the row may call another that does.

⭐ **CALLEES FIRST, AND ONCE.** `__probe_inner` is spliced out of `__probe_outer` before `__probe_outer`
is copied anywhere, so each body is copied exactly once per site and the work is bounded by the chain of
DECLARED rows rather than by anything the program controls. The pin is `__probe_chain_caller` with both
levels flattened into it and no `callDirect` left.

⚠ **`__probe_outer` IS WHAT THIS CASE DISCRIMINATES ON, NOT `__probe_inner`.** The inner body holds no
call and is an ordinary tiny leaf, which a compiler without the row inlines anyway; the outer body holds
TWO calls, so the leaf rule refuses it outright and the called-once rule never sees it. Without the row a
compiler renders `callDirect __probe_outer` twice here.
```maxon
// --- runtime-file: Probe.maxon
module function __probe_inner(seed MachineWord) returns MachineWord
	__Raw.splicedAtEverySite()

	return seed + 1
end '__probe_inner'

module function __probe_outer(seed MachineWord) returns MachineWord
	__Raw.splicedAtEverySite()

	return __probe_inner(seed) + __probe_inner(seed + 1)
end '__probe_outer'

function __probe_chain_caller(seed ExitCode) returns ExitCode
	let base = __probe_outer(seed as MachineWord)
	let again = __probe_outer(base and 1)

	return (base + again) as ExitCode
end '__probe_chain_caller'
// --- stdlib-overlay: Builtins.maxon
export function probeSpliceChain(seed ExitCode) returns ExitCode
	return __probe_chain_caller(seed)
end 'probeSpliceChain'
// --- file: main.maxon
function main() returns ExitCode
	return probeSpliceChain(0)
end 'main'
```
```exitcode
8
```
```RequiredRuntime
__probe_chain_caller
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
    x64.movRegImm32 rcx, 0
  __il_body:
    x64.callDirect __probe_chain_caller
  __il_cont:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @__probe_chain_caller {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#2
  __rc_chk#3:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg rcx, rax
    x64.jcc greater, __rc_panic#2
  __il_body#12:
    x64.leaRegRegImm32 rax, rcx, 1
  __il_body#13:
    x64.leaRegRegImm32 rcx, rax, 1
  __il_cont#10:
    x64.leaRegRegReg rax, rax, rcx
  __il_cont#8:
    x64.movRegReg rcx, rax
    x64.andRegImm32 rcx, rax, 1
  __il_body#17:
    x64.leaRegRegImm32 rcx, rcx, 1
  __il_body#18:
    x64.leaRegRegImm32 rdx, rcx, 1
  __il_cont#15:
    x64.leaRegRegReg rcx, rcx, rdx
  __il_cont#7:
    x64.leaRegRegReg r8, rax, rcx
  __rc_chk#6:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic#5
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#2:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at Probe.maxon:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#5:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at Probe.maxon:17: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
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
    x64.movRegImm32 rcx, 0
  __il_body:
    x64.callDirect __probe_chain_caller
  __il_cont:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @__probe_chain_caller {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#2
  __rc_chk#3:
    x64.cmpRegImm32 rcx, 255
    x64.jcc greater, __rc_panic#2
  __il_body#12:
    x64.leaRegRegImm32 rax, rcx, 1
  __il_body#13:
    x64.leaRegRegImm32 rcx, rax, 1
  __il_cont#10:
    x64.leaRegRegReg rax, rax, rcx
  __il_cont#8:
    x64.movRegReg rcx, rax
    x64.andRegImm32 rcx, rax, 1
  __il_body#17:
    x64.leaRegRegImm32 rcx, rcx, 1
  __il_body#18:
    x64.leaRegRegImm32 rdx, rcx, 1
  __il_cont#15:
    x64.leaRegRegReg rcx, rcx, rdx
  __il_cont#7:
    x64.leaRegRegReg r8, rax, rcx
  __rc_chk#6:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic#5
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#2:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at Probe.maxon:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#5:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at Probe.maxon:17: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    arm64.movImm x0, 0
  __il_body:
    arm64.bl __probe_chain_caller
  __il_cont:
    arm64.epilogue 16
    arm64.ret
}

func @__probe_chain_caller {
  entry:
    arm64.prologue 16
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#2
  __rc_chk#3:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic#2
  __il_body#12:
    arm64.add x0, x0, 1
  __il_body#13:
    arm64.add x1, x0, 1
  __il_cont#10:
    arm64.add x0, x0, x1
  __il_cont#8:
    arm64.and x1, x0, 1
  __il_body#17:
    arm64.add x1, x1, 1
  __il_body#18:
    arm64.add x2, x1, 1
  __il_cont#15:
    arm64.add x1, x1, x2
  __il_cont#7:
    arm64.add x0, x0, x1
  __rc_chk#6:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic#5
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic#2:
    arm64.leaRdata x0, __str_blob_1  ; "panic at Probe.maxon:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __rc_panic#5:
    arm64.leaRdata x0, __str_blob_2  ; "panic at Probe.maxon:17: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    arm64.movImm x0, 0
  __il_body:
    arm64.bl __probe_chain_caller
  __il_cont:
    arm64.epilogue 16
    arm64.ret
}

func @__probe_chain_caller {
  entry:
    arm64.prologue 16
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#2
  __rc_chk#3:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic#2
  __il_body#12:
    arm64.add x0, x0, 1
  __il_body#13:
    arm64.add x1, x0, 1
  __il_cont#10:
    arm64.add x0, x0, x1
  __il_cont#8:
    arm64.and x1, x0, 1
  __il_body#17:
    arm64.add x1, x1, 1
  __il_body#18:
    arm64.add x2, x1, 1
  __il_cont#15:
    arm64.add x1, x1, x2
  __il_cont#7:
    arm64.add x0, x0, x1
  __rc_chk#6:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic#5
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic#2:
    arm64.leaRdata x0, __str_blob_1  ; "panic at Probe.maxon:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __rc_panic#5:
    arm64.leaRdata x0, __str_blob_2  ; "panic at Probe.maxon:17: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: a-runtime-leaf-reading-its-machines-tls-slot-is-spliced -->
⭐⭐ **THE ADMISSION RULE READS `isUnsupportedInInlineBody`, AND THE `system` BAND IS NOT ONE ANSWER.**
`__probe_tls_read` is a two-op leaf whose whole body is the per-OS-thread slot read every allocation and
every current-GT read begins with. It is `isCall: false`, so the flag's tail files it a body op and both
admission rules may carry it; the pin below is `__probe_tls_caller` with the read spliced in and no
`callDirect` left. A compiler that refused the callee renders the call instead, which is the difference
this case exists to hold.

⚠ **SPLICING IS NOT HOISTING, AND ONLY THE SECOND WOULD BE WRONG.** A copy stays where it was written, so
the M whose slot is read is the M the surrounding code is running on. What forbids the move is the pair of
rosters `classifyArithOperands` and `classifyLoadOperands`, which answer `neither` and `notALoad` for this
variant — so CSE, LICM and the unswitcher's invariance test each decline it before `isPure` is reached.

⚠ **BOTH FUNCTIONS ARE TIER SOURCE, WHICH IS WHAT LETS THE SPLICE HAPPEN AT ALL.**
`InlineLeaves.splicingWouldWidenTheSafePoint` refuses the compiler's own scaffolding spliced into code
that is not, so a runtime callee reaches only a runtime caller — and the pair here is inside one
`runtime/` file.

⚠ It carries NO `unsupported-targets` marker: wasm32-wasi has no per-thread storage and answers E3104,
which the harness counts as a SKIP naming this case.
```maxon
// --- runtime-file: Probe.maxon
module function __probe_tls_read(tebOffset MachineWord) returns MachineWord
	return __Raw.tlsSlotLoad(tebOffset)
end '__probe_tls_read'

function __probe_tls_caller(tebOffset ExitCode) returns ExitCode
	if tebOffset == 0 'neverAMachine'
		return 0
	end 'neverAMachine'

	return __probe_tls_read(tebOffset as MachineWord) as ExitCode
end '__probe_tls_caller'
// --- stdlib-overlay: Builtins.maxon
export function probeTlsSlotRead(tebOffset ExitCode) returns ExitCode
	return __probe_tls_caller(tebOffset)
end 'probeTlsSlotRead'
// --- file: main.maxon
function main() returns ExitCode
	return probeTlsSlotRead(0)
end 'main'
```
```exitcode
0
```
```RequiredRuntime
__probe_tls_caller
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
    x64.movRegImm32 rcx, 0
  __il_body:
    x64.callDirect __probe_tls_caller
  __il_cont:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @__probe_tls_caller {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#4
  __rc_chk#5:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg rcx, rax
    x64.jcc greater, __rc_panic#4
  __rc_ok#3:
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, __il_body
  neverAMachine:
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __il_body:
    x64.mov r8, gs:[rcx]
  __il_cont:
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic#7
  __rc_chk#8:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic#7
  __rc_ok#6:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#4:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at Probe.maxon:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#7:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at Probe.maxon:10: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
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
    x64.movRegImm32 rcx, 0
  __il_body:
    x64.callDirect __probe_tls_caller
  __il_cont:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @__probe_tls_caller {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#4
  __rc_chk#5:
    x64.cmpRegImm32 rcx, 255
    x64.jcc greater, __rc_panic#4
  __rc_ok#3:
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, __il_body
  neverAMachine:
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __il_body:
    x64.mov r8, gs:[rcx]
  __il_cont:
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic#7
  __rc_chk#8:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic#7
  __rc_ok#6:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#4:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at Probe.maxon:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#7:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at Probe.maxon:10: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    arm64.movImm x0, 0
  __il_body:
    arm64.bl __probe_tls_caller
  __il_cont:
    arm64.epilogue 16
    arm64.ret
}

func @__probe_tls_caller {
  entry:
    arm64.prologue 16
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#4
  __rc_chk#5:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic#4
  __rc_ok#3:
    arm64.cmp x0, 0
    arm64.b.ne __il_body
  neverAMachine:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __il_body:
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x0
    arm64.cmp x0, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x0, x16, x17
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#7
  __rc_chk#8:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic#7
  __rc_ok#6:
    arm64.epilogue 16
    arm64.ret
  __rc_panic#4:
    arm64.leaRdata x0, __str_blob_1  ; "panic at Probe.maxon:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __rc_panic#7:
    arm64.leaRdata x0, __str_blob_2  ; "panic at Probe.maxon:10: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    arm64.movImm x0, 0
  __il_body:
    arm64.bl __probe_tls_caller
  __il_cont:
    arm64.epilogue 16
    arm64.ret
}

func @__probe_tls_caller {
  entry:
    arm64.prologue 16
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#4
  __rc_chk#5:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic#4
  __rc_ok#3:
    arm64.cmp x0, 0
    arm64.b.ne __il_body
  neverAMachine:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __il_body:
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x0
    arm64.cmp x0, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x0, x16, x17
  __il_cont:
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#7
  __rc_chk#8:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic#7
  __rc_ok#6:
    arm64.epilogue 16
    arm64.ret
  __rc_panic#4:
    arm64.leaRdata x0, __str_blob_1  ; "panic at Probe.maxon:5: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __rc_panic#7:
    arm64.leaRdata x0, __str_blob_2  ; "panic at Probe.maxon:10: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: a-checked-accessor-whose-slow-arm-is-cold-is-inlined -->
A checked `get` or `set` on a concrete `Array with Integer` keeps its failure path on cold blocks of
the expansion `inlineManagedPrimitives` emits: the known stride means the fast arm serves every
element. A `get` keeps the runtime call on its SLOW ARM, reached only for an index past the end. A
`set` calls `__managed_index_refused` there for that index, and detaches a shared or non-owned
buffer on a cold `__im_detach` block that calls `__managed_cow_detach` and rejoins before the one
store. A helper that wraps one access holds those blocks, and a call in a cold block does not refuse
a leaf, so `elementAt` and `putElement` are spliced at both of their sites and the pin's `main`
holds the fast arms with no `callDirect elementAt` or `callDirect putElement`; the `__im_slow` arm
of each copy still reaches `__managed_index_refused`, and the panic block still names the helper's
own line.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function elementAt(values IntArray, index ElementIndex) returns Integer
	return try values.get(index) otherwise panic("element past the end")
end 'elementAt'

function putElement(values IntArray, index ElementIndex, value Integer)
	try values.set(index, value: value) otherwise panic("element past the end")
end 'putElement'

function main() returns ExitCode
	var values = IntArray.create()
	values.push(20)
	values.push(0)
	putElement(values, index: 1, value: 1)
	putElement(values, index: 1, value: 22)
	return (elementAt(values, index: 0) + elementAt(values, index: 1)) as ExitCode
end 'main'
```
```exitcode
42
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
    x64.prologue 32
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 20
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 1
  __il_body:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegImm32 rcx, 1
    x64.jcc belowEqual, __im_slow
  __im_buffer#14:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rdx, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_detach
  __im_detached#19:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  __il_cont#8:
    x64.movRegImm32 rax, 22
  __im_buffer#25:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
  __im_detached#30:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  __im_load#36:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  __im_load#42:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
  __il_cont#5:
    x64.leaRegRegReg r12, rax, rcx
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r12, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp __il_cont#8
  __im_detach:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 1
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.jmp __im_detached#19
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-checked-accessor-whose-slow-arm-is-cold-is-inlined.test:10: element past the end\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_6]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-checked-accessor-whose-slow-arm-is-cold-is-inlined.test:19: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    x64.prologue 32
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 20
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 1
    x64.movRegImm32 rax, 1
  __il_body:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegImm32 rcx, 1
    x64.jcc belowEqual, __im_slow
  __im_buffer#14:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_missing
  __im_owned:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __im_detach
  __im_viewed:
    x64.leaRegRegImm32 rdx, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_detach
  __im_detached#19:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  __il_cont#8:
    x64.movRegImm32 rax, 22
  __im_buffer#25:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
  __im_detached#30:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  __im_load#36:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  __im_load#42:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
  __il_cont#5:
    x64.leaRegRegReg r12, rax, rcx
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r12, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_index_refused
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp __il_cont#8
  __im_detach:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.movRegImm32 rax, 1
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.jmp __im_detached#19
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-checked-accessor-whose-slow-arm-is-cold-is-inlined.test:10: element past the end\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_missing:
    x64.leaRegRdata rcx, [rip + __str_blob_6]  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-checked-accessor-whose-slow-arm-is-cold-is-inlined.test:19: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
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
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 20
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 1
    arm64.movImm x0, 1
  __il_body:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
    arm64.cmp x2, 1
    arm64.b.ls __im_slow
  __im_buffer#14:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x2, x1, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne __im_detach
  __im_detached#19:
    arm64.storeBaseDispReg.word64 [x1 + 8], x0
  __il_cont#8:
    arm64.movImm x0, 22
  __im_buffer#25:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
  __im_detached#30:
    arm64.storeBaseDispReg.word64 [x1 + 8], x0
  __im_load#36:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  __im_load#42:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
  __il_cont#5:
    arm64.add x20, x0, x1
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.movRegReg x0, x19
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b __il_cont#8
  __im_detach:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x1, x0
    arm64.movImm x0, 1
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.b __im_detached#19
  tryerr:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-checked-accessor-whose-slow-arm-is-cold-is-inlined.test:10: element past the end\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-checked-accessor-whose-slow-arm-is-cold-is-inlined.test:19: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
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
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 20
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 1
    arm64.movImm x0, 1
  __il_body:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
    arm64.cmp x2, 1
    arm64.b.ls __im_slow
  __im_buffer#14:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_missing
  __im_owned:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 16]
    arm64.cmp x2, 0
    arm64.b.lt __im_detach
  __im_viewed:
    arm64.sub x2, x1, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne __im_detach
  __im_detached#19:
    arm64.storeBaseDispReg.word64 [x1 + 8], x0
  __il_cont#8:
    arm64.movImm x0, 22
  __im_buffer#25:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
  __im_detached#30:
    arm64.storeBaseDispReg.word64 [x1 + 8], x0
  __im_load#36:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  __im_load#42:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
  __il_cont#5:
    arm64.add x20, x0, x1
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __im_slow:
    arm64.movRegReg x0, x19
    arm64.bl __managed_index_refused
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b __il_cont#8
  __im_detach:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.movRegReg x1, x0
    arm64.movImm x0, 1
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.b __im_detached#19
  tryerr:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-checked-accessor-whose-slow-arm-is-cold-is-inlined.test:10: element past the end\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __im_missing:
    arm64.leaRdata x0, __str_blob_6  ; "fatal error: an element store found no buffer behind a record that holds elements\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-checked-accessor-whose-slow-arm-is-cold-is-inlined.test:19: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}
```

<!-- test: a-byte-push-loop-reaches-the-runtime-only-on-its-slow-arm -->
A loop that pushes bytes onto a `ByteArray` holds no call on its path: the push is the guarded fast arm
`inlineManagedPrimitives` emits, and `__managed_push` is reached only from the `__im_slow` arm, which the
ownership, sharing and capacity guards send a full, shared or non-owned array to.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias ByteCount = int(0 to 1000)

function fill(output ByteArray, count ByteCount, value Byte)
	for _ in 0 upto count 'eachByte'
		output.push(value)
	end 'eachByte'
end 'fill'

function main() returns ExitCode
	var output = ByteArray.create()
	fill(output, count: 42, value: 7)
	return output.count() as ExitCode
end 'main'
```
```exitcode
42
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
    x64.prologue 56
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegImm32 rdx, 7
  __il_body:
    x64.movRegImm32 rax, 0
    x64.jmp forhdr
  eachByte:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 8]
    x64.loadRegBaseDisp.word64 rsi, [r8 + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rsi, [r8 + 16]
    x64.cmpRegReg rcx, rsi
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [r8 + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.leaRegRegReg rsi, rsi, rcx
    x64.storeBaseDispReg.byte [rsi + 0], rdx
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.storeBaseDispReg.word64 [r8 + 8], rcx
  forstep:
    x64.leaRegRegImm32 rax, rax, 1
  forhdr:
    x64.cmpRegImm32 rax, 42
    x64.jcc less, eachByte
  __il_cont:
    x64.loadRegBaseDisp.word64 rbx, [r8 + 8]
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
    x64.epilogue 56
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_push
    x64.movRegReg rcx, r8
    x64.loadRegSlot rax, slot0
    x64.movRegImm32 rdx, 7
    x64.loadRegSlot r8, slot1
    x64.jmp forstep
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-byte-push-loop-reaches-the-runtime-only-on-its-slow-arm.test:15: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
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
    x64.prologue 56
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegImm32 rdx, 7
  __il_body:
    x64.movRegImm32 rax, 0
    x64.jmp forhdr
  eachByte:
    x64.loadRegBaseDisp.word64 rcx, [r8 + 8]
    x64.loadRegBaseDisp.word64 rsi, [r8 + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rsi, [r8 + 16]
    x64.cmpRegReg rcx, rsi
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rsi, [r8 + 0]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rdi, rsi, -16
    x64.loadRegBaseDisp.word64 rdi, [rdi + 8]
    x64.cmpRegImm32 rdi, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.leaRegRegReg rsi, rsi, rcx
    x64.storeBaseDispReg.byte [rsi + 0], rdx
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.storeBaseDispReg.word64 [r8 + 8], rcx
  forstep:
    x64.leaRegRegImm32 rax, rax, 1
  forhdr:
    x64.cmpRegImm32 rax, 42
    x64.jcc less, eachByte
  __il_cont:
    x64.loadRegBaseDisp.word64 rbx, [r8 + 8]
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r8
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 56
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_push
    x64.movRegReg rcx, r8
    x64.loadRegSlot rax, slot0
    x64.movRegImm32 rdx, 7
    x64.loadRegSlot r8, slot1
    x64.jmp forstep
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-byte-push-loop-reaches-the-runtime-only-on-its-slow-arm.test:15: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 56
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
    arm64.prologue 48
    arm64.storeSlotReg slot2, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movImm x1, 7
  __il_body:
    arm64.movImm x2, 0
    arm64.b forhdr
  eachByte:
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
    arm64.add x4, x4, x3
    arm64.storeBaseDispReg.byte [x4 + 0], x1
    arm64.add x3, x3, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x3
  forstep:
    arm64.add x2, x2, 1
  forhdr:
    arm64.cmp x2, 42
    arm64.b.lt eachByte
  __il_cont:
    arm64.loadRegBaseDisp.word64 x19, [x0 + 8]
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot2
    arm64.epilogue 48
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x2
    arm64.bl __managed_push
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.movImm x1, 7
    arm64.loadRegSlot x2, slot1
    arm64.b forstep
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-byte-push-loop-reaches-the-runtime-only-on-its-slow-arm.test:15: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
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

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movImm x1, 7
  __il_body:
    arm64.movImm x2, 0
    arm64.b forhdr
  eachByte:
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
    arm64.add x4, x4, x3
    arm64.storeBaseDispReg.byte [x4 + 0], x1
    arm64.add x3, x3, 1
    arm64.storeBaseDispReg.word64 [x0 + 8], x3
  forstep:
    arm64.add x2, x2, 1
  forhdr:
    arm64.cmp x2, 42
    arm64.b.lt eachByte
  __il_cont:
    arm64.loadRegBaseDisp.word64 x19, [x0 + 8]
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot2
    arm64.epilogue 48
    arm64.ret
  __im_slow:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x2
    arm64.bl __managed_push
    arm64.movRegReg x3, x0
    arm64.loadRegSlot x0, slot0
    arm64.movImm x1, 7
    arm64.loadRegSlot x2, slot1
    arm64.b forstep
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-byte-push-loop-reaches-the-runtime-only-on-its-slow-arm.test:15: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: a-product-of-ranged-operands-shifts-without-a-guard -->
A shift count that is a product, sum or difference of ranged operands is bounded by the arithmetic of their ranges, and when that bound lies inside the counts the instruction takes as written the shift is emitted bare. `rotateRow` is the bitsliced AES row rotation: `row * rowBits` is `16 … 48`, `nibbles * nibbleBits` is `4 … 12` and `rowBits - amount` is `4 … 12`, so none of its four shifts needs the negative-count branch or the saturation ops. `row as RowIndex` and `nibbles as NibbleCount` are the counted loop's interval elided against the parameter's own cascade, so `rotateRow` is spliced into `shiftRows` with neither its entry cascades nor a single saturation op.
```maxon
typealias Word = bits(64)
typealias RowIndex = int(1 to 3)
typealias NibbleCount = int(1 to 3)

let rowBits = 16
let nibbleBits = 4
let rowCount = 4
let rowMask = 0xFFFF as Word

function rotateRow(word Word, row RowIndex, nibbles NibbleCount) returns Word
	let rowShift = row * rowBits
	let amount = nibbles * nibbleBits
	let chunk = (word shr rowShift) and rowMask
	let rotated = ((chunk shl amount) or (chunk shr (rowBits - amount))) and rowMask

	return rotated shl rowShift
end 'rotateRow'

function shiftRows(word Word, inverse bool) returns Word
	var shifted = word and rowMask

	for row in 1 upto rowCount 'eachRow'
		let nibbles = row if inverse else rowCount - row
		shifted = shifted or rotateRow(word, row: row as RowIndex, nibbles: nibbles as NibbleCount)
	end 'eachRow'

	return shifted
end 'shiftRows'

function main() returns ExitCode
	let word = 0x0123456789ABCDEF
	let forward = shiftRows(word, inverse: false)
	let backward = shiftRows(forward, inverse: true)

	if backward != word 'roundTrip'
		return 1
	end 'roundTrip'

	return 42
end 'main'
```
```exitcode
42
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @shiftRows {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.movRegReg rax, rcx
    x64.movRegReg rcx, rax
    x64.andRegImm32 rcx, rax, 65535
    x64.movRegImm32 rsi, 1
    x64.movRegReg r8, rcx
    x64.jmp forhdr
  eachRow:
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, ternarytrue
  ternaryfalse:
    x64.movRegImm32 rcx, 4
    x64.subRegReg rcx, rcx, rsi
    x64.jmp __il_body
  ternarytrue:
    x64.movRegReg rcx, rsi
  __il_body:
    x64.imulRegRegImm32 rdi, rsi, 16
    x64.imulRegRegImm32 r9, rcx, 4
    x64.movRegReg rcx, rdi
    x64.movRegReg r10, rax
    x64.shrRegCl r10, rax
    x64.andRegImm32 r10, r10, 65535
    x64.movRegReg rcx, r9
    x64.movRegReg r11, r10
    x64.shlRegCl r11, r10
    x64.movRegImm32 rbx, 16
    x64.subRegReg rbx, rbx, r9
    x64.movRegReg rcx, rbx
    x64.shrRegCl r10, r10
    x64.orRegReg r11, r11, r10
    x64.andRegImm32 r11, r11, 65535
    x64.movRegReg rcx, rdi
    x64.shlRegCl r11, r11
  __il_cont:
    x64.orRegReg r8, r8, r11
  forstep:
    x64.leaRegRegImm32 rsi, rsi, 1
  forhdr:
    x64.cmpRegImm32 rsi, 4
    x64.jcc less, eachRow
  forexit:
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegImm rbx, 81985529216486895
    x64.movRegImm32 rdx, 0
    x64.movRegReg rcx, rbx
    x64.callDirect shiftRows
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, r8
    x64.callDirect shiftRows
    x64.cmpRegReg r8, rbx
    x64.jcc equal, ifcont
  roundTrip:
    x64.movRegImm32 r8, 1
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 42
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
  __mrt_program_started@16 = i8 0
}

func @shiftRows {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.movRegReg rax, rcx
    x64.movRegReg rcx, rax
    x64.andRegImm32 rcx, rax, 65535
    x64.movRegImm32 rsi, 1
    x64.movRegReg r8, rcx
    x64.jmp forhdr
  eachRow:
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, ternarytrue
  ternaryfalse:
    x64.movRegImm32 rcx, 4
    x64.subRegReg rcx, rcx, rsi
    x64.jmp __il_body
  ternarytrue:
    x64.movRegReg rcx, rsi
  __il_body:
    x64.imulRegRegImm32 rdi, rsi, 16
    x64.imulRegRegImm32 r9, rcx, 4
    x64.movRegReg rcx, rdi
    x64.movRegReg r10, rax
    x64.shrRegCl r10, rax
    x64.andRegImm32 r10, r10, 65535
    x64.movRegReg rcx, r9
    x64.movRegReg r11, r10
    x64.shlRegCl r11, r10
    x64.movRegImm32 rbx, 16
    x64.subRegReg rbx, rbx, r9
    x64.movRegReg rcx, rbx
    x64.shrRegCl r10, r10
    x64.orRegReg r11, r11, r10
    x64.andRegImm32 r11, r11, 65535
    x64.movRegReg rcx, rdi
    x64.shlRegCl r11, r11
  __il_cont:
    x64.orRegReg r8, r8, r11
  forstep:
    x64.leaRegRegImm32 rsi, rsi, 1
  forhdr:
    x64.cmpRegImm32 rsi, 4
    x64.jcc less, eachRow
  forexit:
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegImm rbx, 81985529216486895
    x64.movRegImm32 rdx, 0
    x64.movRegReg rcx, rbx
    x64.callDirect shiftRows
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, r8
    x64.callDirect shiftRows
    x64.cmpRegReg r8, rbx
    x64.jcc equal, ifcont
  roundTrip:
    x64.movRegImm32 r8, 1
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 42
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @shiftRows {
  entry:
    arm64.prologue 16
    arm64.and x2, x0, 65535
    arm64.movImm x3, 1
    arm64.b forhdr
  eachRow:
    arm64.cbnz x1, ternarytrue
  ternaryfalse:
    arm64.movImm x4, 4
    arm64.sub x4, x4, x3
    arm64.b __il_body
  ternarytrue:
    arm64.movRegReg x4, x3
  __il_body:
    arm64.lsl x5, x3, 4
    arm64.lsl x4, x4, 2
    arm64.lsrv x6, x0, x5
    arm64.and x6, x6, 65535
    arm64.lslv x7, x6, x4
    arm64.movImm x8, 16
    arm64.sub x4, x8, x4
    arm64.lsrv x4, x6, x4
    arm64.orr x4, x7, x4
    arm64.and x4, x4, 65535
    arm64.lslv x4, x4, x5
  __il_cont:
    arm64.orr x2, x2, x4
  forstep:
    arm64.add x3, x3, 1
  forhdr:
    arm64.cmp x3, 4
    arm64.b.lt eachRow
  forexit:
    arm64.movRegReg x0, x2
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 81985529216486895
    arm64.movImm x1, 0
    arm64.movRegReg x0, x19
    arm64.bl shiftRows
    arm64.movImm x1, 1
    arm64.bl shiftRows
    arm64.cmp x0, x19
    arm64.b.eq ifcont
  roundTrip:
    arm64.movImm x0, 1
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
  ifcont:
    arm64.movImm x0, 42
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
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

func @shiftRows {
  entry:
    arm64.prologue 16
    arm64.and x2, x0, 65535
    arm64.movImm x3, 1
    arm64.b forhdr
  eachRow:
    arm64.cbnz x1, ternarytrue
  ternaryfalse:
    arm64.movImm x4, 4
    arm64.sub x4, x4, x3
    arm64.b __il_body
  ternarytrue:
    arm64.movRegReg x4, x3
  __il_body:
    arm64.lsl x5, x3, 4
    arm64.lsl x4, x4, 2
    arm64.lsrv x6, x0, x5
    arm64.and x6, x6, 65535
    arm64.lslv x7, x6, x4
    arm64.movImm x8, 16
    arm64.sub x4, x8, x4
    arm64.lsrv x4, x6, x4
    arm64.orr x4, x7, x4
    arm64.and x4, x4, 65535
    arm64.lslv x4, x4, x5
  __il_cont:
    arm64.orr x2, x2, x4
  forstep:
    arm64.add x3, x3, 1
  forhdr:
    arm64.cmp x3, 4
    arm64.b.lt eachRow
  forexit:
    arm64.movRegReg x0, x2
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 81985529216486895
    arm64.movImm x1, 0
    arm64.movRegReg x0, x19
    arm64.bl shiftRows
    arm64.movImm x1, 1
    arm64.bl shiftRows
    arm64.cmp x0, x19
    arm64.b.eq ifcont
  roundTrip:
    arm64.movImm x0, 1
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
  ifcont:
    arm64.movImm x0, 42
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}
```

<!-- test: a-leaf-whose-entry-cascades-every-site-declines-is-inlined -->
A leaf is measured WITHOUT the entry range-check cascades of its ranged parameters, and a site is spliced only where the caller proves every argument, so no cascade is copied. `blend`'s body is inside the 24-op budget and its three cascades are what push it past; both of its calls pass literals the `Stride` range contains. Both sites are spliced, and the pin holds no `callDirect blend` and no `__rc_panic` naming `Stride`.
```maxon
typealias Word = bits(64)
typealias Stride = int(1 to 4)

function blend(a Word, b Word, p Stride, q Stride, r Stride) returns Word
	let x = (a shl p) xor (b shr q)
	let y = (b shl q) xor (a shr r)
	let z = (x and y) or (x xor y)

	return (z shl r) xor ((x shr p) and (y shr q))
end 'blend'

function main() returns ExitCode
	let first = blend(0xF0, b: 0x0F, p: 1, q: 2, r: 3)
	let second = blend(first, b: 0x3C, p: 4, q: 1, r: 2)

	if first != 3864 'first'
		return 1
	end 'first'

	if second != 249825 'second'
		return 2
	end 'second'

	return 42
end 'main'
```
```exitcode
42
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
  ifcont:
    x64.movRegImm32 r8, 42
    x64.popReg rbp
    x64.ret
}
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
  ifcont:
    x64.movRegImm32 r8, 42
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
  ifcont:
    arm64.movImm x0, 42
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
  ifcont:
    arm64.movImm x0, 42
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: a-leaf-relying-on-declined-cascades-stays-a-call-at-an-unproven-site -->
`blend` fits the budget only without its cascades, so a site is spliced only where the caller proves every argument. `blend` is called twice, so the called-once rule does not admit it. The first call passes literals the `Stride` range contains and is spliced with no cascade. The second passes `s + s` over a `Stride`: the sum is `2 … 8`, outside `Stride`, so its cascade must stay and the call is left as a call. The pin holds exactly one `callDirect blend`.
```maxon
typealias Word = bits(64)
typealias Stride = int(1 to 4)
typealias Integer = int(i64.min to i64.max)

function opaque(v Integer, depth Integer) returns Integer
	if depth == 0 'done'
		return v
	end 'done'

	return opaque(v, depth: depth - 1)
end 'opaque'

function blend(a Word, b Word, p Stride, q Stride, r Stride) returns Word
	let x = (a shl p) xor (b shr q)
	let y = (b shl q) xor (a shr r)
	let z = (x and y) or (x xor y)

	return (z shl r) xor ((x shr p) and (y shr q))
end 'blend'

function main() returns ExitCode
	let s = opaque(1, depth: 3) as Stride

	if blend(0xF0, b: 0x0F, p: 1, q: 2, r: 3) != 3864 'proven'
		return 1
	end 'proven'

	if blend(0xF0, b: 0x0F, p: s + s, q: 2, r: 3) != 7960 'unproven'
		return 2
	end 'unproven'

	return 42
end 'main'
```
```exitcode
42
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @opaque {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, ifcont
  done:
    x64.movRegReg r8, rcx
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.leaRegRegImm32 rdx, rdx, -1
    x64.callDirect opaque
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @blend {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegReg rdi, rcx
    x64.cmpRegImm32 rax, 1
    x64.jcc less, __rc_panic#2
  __rc_chk#3:
    x64.cmpRegImm32 rax, 4
    x64.jcc greater, __rc_panic#2
  __rc_ok#1:
    x64.cmpRegImm32 r9, 1
    x64.jcc less, __rc_panic#5
  __rc_chk#6:
    x64.cmpRegImm32 r9, 4
    x64.jcc greater, __rc_panic#5
  __rc_ok#4:
    x64.cmpRegImm32 rsi, 1
    x64.jcc less, __rc_panic#8
  __rc_chk#9:
    x64.cmpRegImm32 rsi, 4
    x64.jcc greater, __rc_panic#8
  __rc_ok#7:
    x64.movRegReg rcx, rax
    x64.movRegReg r8, rdi
    x64.shlRegCl r8, rdi
    x64.movRegReg rcx, r9
    x64.movRegReg r10, rdx
    x64.shrRegCl r10, rdx
    x64.xorRegReg r8, r8, r10
    x64.movRegReg rcx, r9
    x64.shlRegCl rdx, rdx
    x64.movRegReg rcx, rsi
    x64.shrRegCl rdi, rdi
    x64.xorRegReg rdx, rdx, rdi
    x64.movRegReg rdi, r8
    x64.andRegReg rdi, r8, rdx
    x64.movRegReg r10, r8
    x64.xorRegReg r10, r8, rdx
    x64.orRegReg rdi, rdi, r10
    x64.movRegReg rcx, rsi
    x64.shlRegCl rdi, rdi
    x64.movRegReg rcx, rax
    x64.shrRegCl r8, r8
    x64.movRegReg rcx, r9
    x64.shrRegCl rdx, rdx
    x64.andRegReg r8, r8, rdx
    x64.xorRegReg rdi, rdi, r8
    x64.movRegReg r8, rdi
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#2:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-leaf-relying-on-declined-cascades-stays-a-call-at-an-unproven-site.test:14: Range check failed: value outside typealias 'Stride'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#5:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-leaf-relying-on-declined-cascades-stays-a-call-at-an-unproven-site.test:14: Range check failed: value outside typealias 'Stride'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#8:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-leaf-relying-on-declined-cascades-stays-a-call-at-an-unproven-site.test:14: Range check failed: value outside typealias 'Stride'\x0a"
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
    x64.prologue 32
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 3
    x64.callDirect opaque
    x64.cmpRegImm32 r8, 1
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 4
    x64.jcc greater, __rc_panic
  ifcont#2:
    x64.movRegImm32 rcx, 240
    x64.movRegImm32 rdx, 15
    x64.leaRegRegReg rax, r8, r8
    x64.movRegImm32 r9, 2
    x64.movRegImm32 rsi, 3
    x64.callDirect blend
    x64.cmpRegImm32 r8, 7960
    x64.jcc equal, ifcont#4
  unproven:
    x64.movRegImm32 r8, 2
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont#4:
    x64.movRegImm32 r8, 42
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-leaf-relying-on-declined-cascades-stays-a-call-at-an-unproven-site.test:23: Range check failed: value outside typealias 'Stride'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @opaque {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, ifcont
  done:
    x64.movRegReg r8, rcx
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.leaRegRegImm32 rdx, rdx, -1
    x64.callDirect opaque
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @blend {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegReg rdi, rcx
    x64.cmpRegImm32 rax, 1
    x64.jcc less, __rc_panic#2
  __rc_chk#3:
    x64.cmpRegImm32 rax, 4
    x64.jcc greater, __rc_panic#2
  __rc_ok#1:
    x64.cmpRegImm32 r9, 1
    x64.jcc less, __rc_panic#5
  __rc_chk#6:
    x64.cmpRegImm32 r9, 4
    x64.jcc greater, __rc_panic#5
  __rc_ok#4:
    x64.cmpRegImm32 rsi, 1
    x64.jcc less, __rc_panic#8
  __rc_chk#9:
    x64.cmpRegImm32 rsi, 4
    x64.jcc greater, __rc_panic#8
  __rc_ok#7:
    x64.movRegReg rcx, rax
    x64.movRegReg r8, rdi
    x64.shlRegCl r8, rdi
    x64.movRegReg rcx, r9
    x64.movRegReg r10, rdx
    x64.shrRegCl r10, rdx
    x64.xorRegReg r8, r8, r10
    x64.movRegReg rcx, r9
    x64.shlRegCl rdx, rdx
    x64.movRegReg rcx, rsi
    x64.shrRegCl rdi, rdi
    x64.xorRegReg rdx, rdx, rdi
    x64.movRegReg rdi, r8
    x64.andRegReg rdi, r8, rdx
    x64.movRegReg r10, r8
    x64.xorRegReg r10, r8, rdx
    x64.orRegReg rdi, rdi, r10
    x64.movRegReg rcx, rsi
    x64.shlRegCl rdi, rdi
    x64.movRegReg rcx, rax
    x64.shrRegCl r8, r8
    x64.movRegReg rcx, r9
    x64.shrRegCl rdx, rdx
    x64.andRegReg r8, r8, rdx
    x64.xorRegReg rdi, rdi, r8
    x64.movRegReg r8, rdi
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#2:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-leaf-relying-on-declined-cascades-stays-a-call-at-an-unproven-site.test:14: Range check failed: value outside typealias 'Stride'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#5:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-leaf-relying-on-declined-cascades-stays-a-call-at-an-unproven-site.test:14: Range check failed: value outside typealias 'Stride'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic#8:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-leaf-relying-on-declined-cascades-stays-a-call-at-an-unproven-site.test:14: Range check failed: value outside typealias 'Stride'\x0a"
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
    x64.prologue 32
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 3
    x64.callDirect opaque
    x64.cmpRegImm32 r8, 1
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 4
    x64.jcc greater, __rc_panic
  ifcont#2:
    x64.movRegImm32 rcx, 240
    x64.movRegImm32 rdx, 15
    x64.leaRegRegReg rax, r8, r8
    x64.movRegImm32 r9, 2
    x64.movRegImm32 rsi, 3
    x64.callDirect blend
    x64.cmpRegImm32 r8, 7960
    x64.jcc equal, ifcont#4
  unproven:
    x64.movRegImm32 r8, 2
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont#4:
    x64.movRegImm32 r8, 42
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-leaf-relying-on-declined-cascades-stays-a-call-at-an-unproven-site.test:23: Range check failed: value outside typealias 'Stride'\x0a"
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

func @opaque {
  entry:
    arm64.prologue 16
    arm64.cmp x1, 0
    arm64.b.ne ifcont
  done:
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.sub x1, x1, 1
    arm64.bl opaque
    arm64.epilogue 16
    arm64.ret
}

func @blend {
  entry:
    arm64.prologue 16
    arm64.cmp x2, 1
    arm64.b.lt __rc_panic#2
  __rc_chk#3:
    arm64.cmp x2, 4
    arm64.b.gt __rc_panic#2
  __rc_ok#1:
    arm64.cmp x3, 1
    arm64.b.lt __rc_panic#5
  __rc_chk#6:
    arm64.cmp x3, 4
    arm64.b.gt __rc_panic#5
  __rc_ok#4:
    arm64.cmp x4, 1
    arm64.b.lt __rc_panic#8
  __rc_chk#9:
    arm64.cmp x4, 4
    arm64.b.gt __rc_panic#8
  __rc_ok#7:
    arm64.lslv x5, x0, x2
    arm64.lsrv x6, x1, x3
    arm64.eor x5, x5, x6
    arm64.lslv x1, x1, x3
    arm64.lsrv x0, x0, x4
    arm64.eor x0, x1, x0
    arm64.and x1, x5, x0
    arm64.eor x6, x5, x0
    arm64.orr x1, x1, x6
    arm64.lslv x1, x1, x4
    arm64.lsrv x2, x5, x2
    arm64.lsrv x0, x0, x3
    arm64.and x0, x2, x0
    arm64.eor x0, x1, x0
    arm64.epilogue 16
    arm64.ret
  __rc_panic#2:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-leaf-relying-on-declined-cascades-stays-a-call-at-an-unproven-site.test:14: Range check failed: value outside typealias 'Stride'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __rc_panic#5:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-leaf-relying-on-declined-cascades-stays-a-call-at-an-unproven-site.test:14: Range check failed: value outside typealias 'Stride'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __rc_panic#8:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-leaf-relying-on-declined-cascades-stays-a-call-at-an-unproven-site.test:14: Range check failed: value outside typealias 'Stride'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 1
    arm64.movImm x1, 3
    arm64.bl opaque
    arm64.cmp x0, 1
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 4
    arm64.b.gt __rc_panic
  ifcont#2:
    arm64.movImm x5, 240
    arm64.movImm x1, 15
    arm64.add x2, x0, x0
    arm64.movImm x3, 2
    arm64.movImm x4, 3
    arm64.movRegReg x0, x5
    arm64.bl blend
    arm64.movImm x16, 7960
    arm64.cmp x0, x16
    arm64.b.eq ifcont#4
  unproven:
    arm64.movImm x0, 2
    arm64.epilogue 16
    arm64.ret
  ifcont#4:
    arm64.movImm x0, 42
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-leaf-relying-on-declined-cascades-stays-a-call-at-an-unproven-site.test:23: Range check failed: value outside typealias 'Stride'\x0a"
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

func @opaque {
  entry:
    arm64.prologue 16
    arm64.cmp x1, 0
    arm64.b.ne ifcont
  done:
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.sub x1, x1, 1
    arm64.bl opaque
    arm64.epilogue 16
    arm64.ret
}

func @blend {
  entry:
    arm64.prologue 16
    arm64.cmp x2, 1
    arm64.b.lt __rc_panic#2
  __rc_chk#3:
    arm64.cmp x2, 4
    arm64.b.gt __rc_panic#2
  __rc_ok#1:
    arm64.cmp x3, 1
    arm64.b.lt __rc_panic#5
  __rc_chk#6:
    arm64.cmp x3, 4
    arm64.b.gt __rc_panic#5
  __rc_ok#4:
    arm64.cmp x4, 1
    arm64.b.lt __rc_panic#8
  __rc_chk#9:
    arm64.cmp x4, 4
    arm64.b.gt __rc_panic#8
  __rc_ok#7:
    arm64.lslv x5, x0, x2
    arm64.lsrv x6, x1, x3
    arm64.eor x5, x5, x6
    arm64.lslv x1, x1, x3
    arm64.lsrv x0, x0, x4
    arm64.eor x0, x1, x0
    arm64.and x1, x5, x0
    arm64.eor x6, x5, x0
    arm64.orr x1, x1, x6
    arm64.lslv x1, x1, x4
    arm64.lsrv x2, x5, x2
    arm64.lsrv x0, x0, x3
    arm64.and x0, x2, x0
    arm64.eor x0, x1, x0
    arm64.epilogue 16
    arm64.ret
  __rc_panic#2:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-leaf-relying-on-declined-cascades-stays-a-call-at-an-unproven-site.test:14: Range check failed: value outside typealias 'Stride'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __rc_panic#5:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-leaf-relying-on-declined-cascades-stays-a-call-at-an-unproven-site.test:14: Range check failed: value outside typealias 'Stride'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  __rc_panic#8:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-leaf-relying-on-declined-cascades-stays-a-call-at-an-unproven-site.test:14: Range check failed: value outside typealias 'Stride'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 1
    arm64.movImm x1, 3
    arm64.bl opaque
    arm64.cmp x0, 1
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 4
    arm64.b.gt __rc_panic
  ifcont#2:
    arm64.movImm x5, 240
    arm64.movImm x1, 15
    arm64.add x2, x0, x0
    arm64.movImm x3, 2
    arm64.movImm x4, 3
    arm64.movRegReg x0, x5
    arm64.bl blend
    arm64.movImm x16, 7960
    arm64.cmp x0, x16
    arm64.b.eq ifcont#4
  unproven:
    arm64.movImm x0, 2
    arm64.epilogue 16
    arm64.ret
  ifcont#4:
    arm64.movImm x0, 42
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-leaf-relying-on-declined-cascades-stays-a-call-at-an-unproven-site.test:23: Range check failed: value outside typealias 'Stride'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```
<!-- test: a-cursor-loop-reaches-the-runtime-only-on-its-slow-arms -->
A loop driving an `ArrayIterator` reads its element and its index without calling the runtime's cursor entries: `current` and `index` are expanded inline, and `__managed_cursor_current` is reached only from the `__im_slow` arm that an out-of-range position, a stride the arm does not move in one operation or an empty managed slot is sent to.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Total = int(0 to 1000000)

function walk(values ByteArray) returns Total
	var total = 0 as Total
	var cursor = try values.createIterator() otherwise return total

	while true 'visit'
		total = (total as FullRangeInt + cursor.current() as FullRangeInt + cursor.index() as FullRangeInt) as Total

		try cursor.advance() otherwise break
	end 'visit'

	return total
end 'walk'

function main() returns ExitCode
	var first = ByteArray.create()
	first.push(10)
	first.push(20)
	first.push(9)

	var second = ByteArray.create()
	second.push(1)
	second.push(2)

	let tail = walk(second)

	if tail != 4 'wrong'
		return 1
	end 'wrong'

	return walk(first) as ExitCode
end 'main'
```
```exitcode
42
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

func @walk {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegImm32 rbx, 0
  __il_body#34:
    x64.callDirect __managed_retain
    x64.movRegReg r12, r8
  __il_body#39:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_cursor_create
    x64.movRegReg r13, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#40
  tryerr#41:
    x64.movRegImm32 rax, 1
    x64.movRegImm32 r13, 0
    x64.jmp __il_cont#38
  tryok#40:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r13
    x64.callDirect __mm_incref
    x64.storeBaseDispReg.word64 [r14 + 0], r13
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cursor_decref
  exitterminal:
    x64.movRegImm32 rax, 0
    x64.movRegReg r13, r14
  __il_cont#38:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#35
  tryerr#36:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#35:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  __il_body#14:
    x64.leaRegRdata rdx, [rip + __str_rec_1]  ; "panic at Array.maxon:497: "
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdi, [rsi + 8]
    x64.cmpRegReg rax, rdi
    x64.jcc aboveEqual, __im_slow#16
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
    x64.jcc notEqual, __im_slow#16
  __im_byte:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 0]
    x64.leaRegRegReg rax, rdi, rax
    x64.loadRegBaseDisp.byte rax, [rax + 0]
  __im_loaded:
    x64.loadRegBaseDisp.word64 rsi, [rsi + 40]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, critsplit#43
  __im_empty:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#44
  __im_slow#16:
    x64.callDirect __managed_cursor_current
    x64.jmp __il_cont#13
  critsplit#44:
    x64.movRegReg r8, rax
    x64.jmp __il_cont#13
  critsplit#43:
    x64.movRegReg r8, rax
  __il_cont#13:
    x64.leaRegRegReg rax, rbx, r8
  __il_body#23:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#25
  __il_cont#12:
    x64.leaRegRegReg rbx, rax, rcx
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic#10
  __rc_chk:
    x64.cmpRegImm32 rbx, 1000000
    x64.jcc greater, __rc_panic#10
  __il_body#27:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegReg rax, rdx
    x64.jcc aboveEqual, __im_slow#31
    x64.jmp __im_move
  tryerr#29:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#26
  __im_move:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  tryok#28:
    x64.movRegImm32 rax, 0
  __il_cont#26:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __il_body#14
  whileexit:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_ArrayIterator_Byte$0_255$1
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#31:
    x64.callDirect __managed_cursor_advance
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#29
    x64.jmp tryok#28
  __rc_panic#25:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#10:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-cursor-loop-reaches-the-runtime-only-on-its-slow-arms.test:11: Range check failed: value outside typealias 'Total'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
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
    x64.prologue 40
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 10
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 20
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 9
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, r12
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 2
    x64.movRegReg rcx, r12
    x64.callDirect __managed_push
    x64.movRegReg rcx, r12
    x64.callDirect walk
    x64.cmpRegImm32 r8, 4
    x64.jcc equal, ifcont
  wrong:
    x64.movRegImm32 r13, 1
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitdrop#3:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitterminal#2:
    x64.movRegReg r8, r13
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegReg rcx, rbx
    x64.callDirect walk
    x64.movRegReg r13, r8
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitdrop#6:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitterminal#5:
    x64.movRegReg r8, r13
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

func @walk {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegImm32 rbx, 0
  __il_body#34:
    x64.callDirect __managed_retain
    x64.movRegReg r12, r8
  __il_body#39:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_cursor_create
    x64.movRegReg r13, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#40
  tryerr#41:
    x64.movRegImm32 rax, 1
    x64.movRegImm32 r13, 0
    x64.jmp __il_cont#38
  tryok#40:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r13
    x64.callDirect __mm_incref
    x64.storeBaseDispReg.word64 [r14 + 0], r13
    x64.movRegReg rcx, r13
    x64.callDirect __managed_cursor_decref
  exitterminal:
    x64.movRegImm32 rax, 0
    x64.movRegReg r13, r14
  __il_cont#38:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tryok#35
  tryerr#36:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#35:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  __il_body#14:
    x64.leaRegRdata rdx, [rip + __str_rec_1]  ; "panic at Array.maxon:497: "
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdi, [rsi + 8]
    x64.cmpRegReg rax, rdi
    x64.jcc aboveEqual, __im_slow#16
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
    x64.jcc notEqual, __im_slow#16
  __im_byte:
    x64.loadRegBaseDisp.word64 rdi, [rsi + 0]
    x64.leaRegRegReg rax, rdi, rax
    x64.loadRegBaseDisp.byte rax, [rax + 0]
  __im_loaded:
    x64.loadRegBaseDisp.word64 rsi, [rsi + 40]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, critsplit#43
  __im_empty:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#44
  __im_slow#16:
    x64.callDirect __managed_cursor_current
    x64.jmp __il_cont#13
  critsplit#44:
    x64.movRegReg r8, rax
    x64.jmp __il_cont#13
  critsplit#43:
    x64.movRegReg r8, rax
  __il_cont#13:
    x64.leaRegRegReg rax, rbx, r8
  __il_body#23:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#25
  __il_cont#12:
    x64.leaRegRegReg rbx, rax, rcx
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic#10
  __rc_chk:
    x64.cmpRegImm32 rbx, 1000000
    x64.jcc greater, __rc_panic#10
  __il_body#27:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.leaRegRegImm32 rax, rax, 1
    x64.loadRegBaseDisp.word64 rdx, [rcx + 0]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegReg rax, rdx
    x64.jcc aboveEqual, __im_slow#31
    x64.jmp __im_move
  tryerr#29:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#26
  __im_move:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  tryok#28:
    x64.movRegImm32 rax, 0
  __il_cont#26:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __il_body#14
  whileexit:
    x64.movRegReg rcx, r13
    x64.callDirect __destruct_ArrayIterator_Byte$0_255$1
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow#31:
    x64.callDirect __managed_cursor_advance
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#29
    x64.jmp tryok#28
  __rc_panic#25:
    x64.leaRegRdata rcx, [rip + __str_blob_5]  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#10:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-cursor-loop-reaches-the-runtime-only-on-its-slow-arms.test:11: Range check failed: value outside typealias 'Total'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
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
    x64.prologue 40
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 10
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 20
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 9
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, r12
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 2
    x64.movRegReg rcx, r12
    x64.callDirect __managed_push
    x64.movRegReg rcx, r12
    x64.callDirect walk
    x64.cmpRegImm32 r8, 4
    x64.jcc equal, ifcont
  wrong:
    x64.movRegImm32 r13, 1
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitdrop#3:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitterminal#2:
    x64.movRegReg r8, r13
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegReg rcx, rbx
    x64.callDirect walk
    x64.movRegReg r13, r8
    x64.cmpRegImm32 r13, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r13, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitdrop#6:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
  exitterminal#5:
    x64.movRegReg r8, r13
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at a-cursor-loop-reaches-the-runtime-only-on-its-slow-arms.test:35: Range check failed: value outside typealias 'ExitCode'\x0a"
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

func @walk {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 0
  __il_body#34:
    arm64.bl __managed_retain
    arm64.movRegReg x20, x0
  __il_body#39:
    arm64.movRegReg x0, x20
    arm64.bl __managed_cursor_create
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#40
  tryerr#41:
    arm64.movImm x0, 1
    arm64.movImm x21, 0
    arm64.b __il_cont#38
  tryok#40:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x22 + 0], x21
    arm64.movRegReg x0, x21
    arm64.bl __managed_cursor_decref
  exitterminal:
    arm64.movImm x0, 0
    arm64.movRegReg x21, x22
  __il_cont#38:
    arm64.cmp x0, 0
    arm64.b.eq tryok#35
  tryerr#36:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  tryok#35:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  __il_body#14:
    arm64.leaRdata x1, __str_rec_1  ; "panic at Array.maxon:497: "
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x4, [x3 + 8]
    arm64.cmp x2, x4
    arm64.b.hs __im_slow#16
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
    arm64.b.ne __im_slow#16
  __im_byte:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.add x2, x4, x2
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
  __im_loaded:
    arm64.loadRegBaseDisp.word64 x3, [x3 + 40]
    arm64.cmp x3, 0
    arm64.b.eq critsplit#43
  __im_empty:
    arm64.cmp x2, 0
    arm64.b.ne critsplit#44
  __im_slow#16:
    arm64.bl __managed_cursor_current
    arm64.b __il_cont#13
  critsplit#44:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#13
  critsplit#43:
    arm64.movRegReg x0, x2
  __il_cont#13:
    arm64.add x0, x19, x0
  __il_body#23:
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#25
  __il_cont#12:
    arm64.add x19, x0, x1
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic#10
  __rc_chk:
    arm64.movImm x16, 1000000
    arm64.cmp x19, x16
    arm64.b.gt __rc_panic#10
  __il_body#27:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#31
    arm64.b __im_move
  tryerr#29:
    arm64.movImm x0, 1
    arm64.b __il_cont#26
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#28:
    arm64.movImm x0, 0
  __il_cont#26:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#14
  whileexit:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_ArrayIterator_Byte$0_255$1
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  __im_slow#31:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr#29
    arm64.b tryok#28
  __rc_panic#25:
    arm64.leaRdata x0, __str_blob_5  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  __rc_panic#10:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-cursor-loop-reaches-the-runtime-only-on-its-slow-arms.test:11: Range check failed: value outside typealias 'Total'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 10
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 20
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 9
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x1, 1
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
    arm64.movImm x1, 2
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
    arm64.movRegReg x0, x20
    arm64.bl walk
    arm64.cmp x0, 4
    arm64.b.eq ifcont
  wrong:
    arm64.movImm x21, 1
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitdrop#3:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal#2:
    arm64.movRegReg x0, x21
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movRegReg x0, x19
    arm64.bl walk
    arm64.movRegReg x21, x0
    arm64.cmp x21, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x21, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitdrop#6:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal#5:
    arm64.movRegReg x0, x21
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-cursor-loop-reaches-the-runtime-only-on-its-slow-arms.test:35: Range check failed: value outside typealias 'ExitCode'\x0a"
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

func @walk {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 0
  __il_body#34:
    arm64.bl __managed_retain
    arm64.movRegReg x20, x0
  __il_body#39:
    arm64.movRegReg x0, x20
    arm64.bl __managed_cursor_create
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#40
  tryerr#41:
    arm64.movImm x0, 1
    arm64.movImm x21, 0
    arm64.b __il_cont#38
  tryok#40:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.bl __mm_incref
    arm64.storeBaseDispReg.word64 [x22 + 0], x21
    arm64.movRegReg x0, x21
    arm64.bl __managed_cursor_decref
  exitterminal:
    arm64.movImm x0, 0
    arm64.movRegReg x21, x22
  __il_cont#38:
    arm64.cmp x0, 0
    arm64.b.eq tryok#35
  tryerr#36:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  tryok#35:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  __il_body#14:
    arm64.leaRdata x1, __str_rec_1  ; "panic at Array.maxon:497: "
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x4, [x3 + 8]
    arm64.cmp x2, x4
    arm64.b.hs __im_slow#16
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
    arm64.b.ne __im_slow#16
  __im_byte:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.add x2, x4, x2
    arm64.loadRegBaseDisp.byte x2, [x2 + 0]
  __im_loaded:
    arm64.loadRegBaseDisp.word64 x3, [x3 + 40]
    arm64.cmp x3, 0
    arm64.b.eq critsplit#43
  __im_empty:
    arm64.cmp x2, 0
    arm64.b.ne critsplit#44
  __im_slow#16:
    arm64.bl __managed_cursor_current
    arm64.b __il_cont#13
  critsplit#44:
    arm64.movRegReg x0, x2
    arm64.b __il_cont#13
  critsplit#43:
    arm64.movRegReg x0, x2
  __il_cont#13:
    arm64.add x0, x19, x0
  __il_body#23:
    arm64.loadRegBaseDisp.word64 x1, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.cmp x1, 0
    arm64.b.lt __rc_panic#25
  __il_cont#12:
    arm64.add x19, x0, x1
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic#10
  __rc_chk:
    arm64.movImm x16, 1000000
    arm64.cmp x19, x16
    arm64.b.gt __rc_panic#10
  __il_body#27:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 8]
    arm64.add x1, x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x1, x2
    arm64.b.hs __im_slow#31
    arm64.b __im_move
  tryerr#29:
    arm64.movImm x0, 1
    arm64.b __il_cont#26
  __im_move:
    arm64.storeBaseDispReg.word64 [x0 + 8], x1
  tryok#28:
    arm64.movImm x0, 0
  __il_cont#26:
    arm64.cmp x0, 0
    arm64.b.eq __il_body#14
  whileexit:
    arm64.movRegReg x0, x21
    arm64.bl __destruct_ArrayIterator_Byte$0_255$1
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  __im_slow#31:
    arm64.bl __managed_cursor_advance
    arm64.cmp x9, 0
    arm64.b.ne tryerr#29
    arm64.b tryok#28
  __rc_panic#25:
    arm64.leaRdata x0, __str_blob_5  ; "panic at Array.maxon:501: Range check failed: value outside typealias 'ElementIndex'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  __rc_panic#10:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-cursor-loop-reaches-the-runtime-only-on-its-slow-arms.test:11: Range check failed: value outside typealias 'Total'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 10
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 20
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 9
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x1, 1
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
    arm64.movImm x1, 2
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
    arm64.movRegReg x0, x20
    arm64.bl walk
    arm64.cmp x0, 4
    arm64.b.eq ifcont
  wrong:
    arm64.movImm x21, 1
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitdrop#3:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal#2:
    arm64.movRegReg x0, x21
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  ifcont:
    arm64.movRegReg x0, x19
    arm64.bl walk
    arm64.movRegReg x21, x0
    arm64.cmp x21, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x21, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitdrop#6:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
  exitterminal#5:
    arm64.movRegReg x0, x21
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_4  ; "panic at a-cursor-loop-reaches-the-runtime-only-on-its-slow-arms.test:35: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```
<!-- test: a-throwing-leaf-called-with-try-otherwise-in-a-loop-is-spliced -->
A small throwing function called with `try ... otherwise` from a loop and from `main` is spliced at both sites: no `callDirect checked` remains, and the caller's error-flag test is folded away because the copied body's exits carry a constant flag.
```maxon
typealias Integer = int(i64.min to i64.max)

enum CheckError implements Error
	negative
end 'CheckError'

function checked(x Integer) returns Integer throws CheckError
	if x < 0 'below'
		throw CheckError.negative
	end 'below'

	return x + 1
end 'checked'

function sum(limit Integer) returns Integer
	var total = 0 as Integer

	for i in 0 upto limit 'each'
		total = total + (try checked(i) otherwise 0)
	end 'each'

	return total
end 'sum'

function main() returns ExitCode
	let rejected = try checked(-1) otherwise 14
	return (sum(7) + rejected) as ExitCode
end 'main'
```
```exitcode
42
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
  __il_body:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 0
    x64.jmp forhdr
  ifcont:
    x64.leaRegRegImm32 rcx, rcx, 1
  trycont:
    x64.leaRegRegReg rax, rax, rcx
  forhdr:
    x64.cmpRegImm32 rcx, 7
    x64.jcc less, ifcont
  __il_cont:
    x64.leaRegRegImm32 r8, rax, 14
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
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-throwing-leaf-called-with-try-otherwise-in-a-loop-is-spliced.test:28: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
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
  __il_body:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rcx, 0
    x64.jmp forhdr
  ifcont:
    x64.leaRegRegImm32 rcx, rcx, 1
  trycont:
    x64.leaRegRegReg rax, rax, rcx
  forhdr:
    x64.cmpRegImm32 rcx, 7
    x64.jcc less, ifcont
  __il_cont:
    x64.leaRegRegImm32 r8, rax, 14
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
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-throwing-leaf-called-with-try-otherwise-in-a-loop-is-spliced.test:28: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __il_body:
    arm64.movImm x0, 0
    arm64.movImm x1, 0
    arm64.b forhdr
  ifcont:
    arm64.add x1, x1, 1
  trycont:
    arm64.add x0, x0, x1
  forhdr:
    arm64.cmp x1, 7
    arm64.b.lt ifcont
  __il_cont:
    arm64.add x0, x0, 14
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-throwing-leaf-called-with-try-otherwise-in-a-loop-is-spliced.test:28: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __il_body:
    arm64.movImm x0, 0
    arm64.movImm x1, 0
    arm64.b forhdr
  ifcont:
    arm64.add x1, x1, 1
  trycont:
    arm64.add x0, x0, x1
  forhdr:
    arm64.cmp x1, 7
    arm64.b.lt ifcont
  __il_cont:
    arm64.add x0, x0, 14
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-throwing-leaf-called-with-try-otherwise-in-a-loop-is-spliced.test:28: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: a-called-once-body-that-holds-values-across-calls-stays-a-call-off-a-lean-callers-hot-path -->
`tally` is called once, from `pick`, and holds `total`, `limit` and `step` across the `print` call in its loop. `pick`
holds nothing across any call, and its `limit` return path never reaches the call. Splicing `tally` into `pick` would
make `pick` save callee registers on that path too, so the call stays: the pin holds one `callDirect tally` and a
`pick` frame that saves no register.
```maxon
typealias Integer = int(i64.min to i64.max)

function tally(limit Integer) returns Integer
	var total = 0 as Integer

	for step in 0 upto limit 'each'
		print("{step}\n")
		total = total + step
	end 'each'

	return total
end 'tally'

function pick(heavy bool, limit Integer) returns Integer
	if heavy 'heavy'
		return tally(limit)
	end 'heavy'

	return limit + limit * 2 + limit * 3 + limit * 4 + limit * 5 + limit * 6 + limit * 7 + limit * 8
end 'pick'

function main() returns ExitCode
	let light = pick(false, limit: 3)
	let heavy = pick(true, limit: 3)
	print("{light} {heavy}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
1
2
108 3
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

func @tally {
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
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r12, 0
    x64.jmp forhdr
  each:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rax, r12, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r12
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.loadRegSlot r12, slot4
    x64.storeBaseDispReg.word64 [r12 + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect print
    x64.movRegReg rcx, r12
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rcx, rax
  forstep:
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg r12, rcx
  forhdr:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, r12
    x64.cmpRegReg r12, rbx
    x64.jcc less, each
  forexit:
    x64.loadRegSlot r8, slot1
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @pick {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, ifcont
  heavy:
    x64.movRegReg rcx, rdx
    x64.callDirect tally
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.leaRegBaseIndexScale rax, [rdx + rdx*2]
    x64.imulRegRegImm32 rcx, rdx, 3
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegBaseIndexScale rax, [rax + rdx*4]
    x64.imulRegRegImm32 rcx, rdx, 5
    x64.leaRegRegReg rax, rax, rcx
    x64.imulRegRegImm32 rcx, rdx, 6
    x64.leaRegRegReg rax, rax, rcx
    x64.imulRegRegImm32 rcx, rdx, 7
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegBaseIndexScale r8, [rax + rdx*8]
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 88
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 3
    x64.callDirect pick
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 3
    x64.callDirect pick
    x64.movRegReg r12, r8
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot0, r13
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __int_to_string
    x64.storeSlotReg slot5, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot3, rax
    x64.leaRegRegReg rcx, rbx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, rbx
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
    x64.loadRegSlot rbx, slot2
    x64.storeBaseDispReg.word64 [rbx + 0], r12
    x64.loadRegSlot rax, slot1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
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

func @tally {
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
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r12, 0
    x64.jmp forhdr
  each:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rax, r12, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r12
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.loadRegSlot r12, slot4
    x64.storeBaseDispReg.word64 [r12 + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect print
    x64.movRegReg rcx, r12
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rcx, rax
  forstep:
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg r12, rcx
  forhdr:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, r12
    x64.cmpRegReg r12, rbx
    x64.jcc less, each
  forexit:
    x64.loadRegSlot r8, slot1
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @pick {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, ifcont
  heavy:
    x64.movRegReg rcx, rdx
    x64.callDirect tally
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.leaRegBaseIndexScale rax, [rdx + rdx*2]
    x64.imulRegRegImm32 rcx, rdx, 3
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegBaseIndexScale rax, [rax + rdx*4]
    x64.imulRegRegImm32 rcx, rdx, 5
    x64.leaRegRegReg rax, rax, rcx
    x64.imulRegRegImm32 rcx, rdx, 6
    x64.leaRegRegReg rax, rax, rcx
    x64.imulRegRegImm32 rcx, rdx, 7
    x64.leaRegRegReg rax, rax, rcx
    x64.leaRegBaseIndexScale r8, [rax + rdx*8]
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 88
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 3
    x64.callDirect pick
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 3
    x64.callDirect pick
    x64.movRegReg r12, r8
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot0, r13
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __int_to_string
    x64.storeSlotReg slot5, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot3, rax
    x64.leaRegRegReg rcx, rbx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, rbx
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
    x64.loadRegSlot rbx, slot2
    x64.storeBaseDispReg.word64 [rbx + 0], r12
    x64.loadRegSlot rax, slot1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
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

func @tally {
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
    arm64.movRegReg x19, x0
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b forhdr
  each:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x23, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x23, x28, x23
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x23, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.add x20, x20, x21
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.cmp x21, x19
    arm64.b.lt each
  forexit:
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
}

func @pick {
  entry:
    arm64.prologue 16
    arm64.cbz x0, ifcont
  heavy:
    arm64.movRegReg x0, x1
    arm64.bl tally
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.arm64AddLsl x0, x1, x1, lsl #1
    arm64.arm64AddLsl x2, x1, x1, lsl #1
    arm64.add x0, x0, x2
    arm64.arm64AddLsl x0, x0, x1, lsl #2
    arm64.arm64AddLsl x2, x1, x1, lsl #2
    arm64.add x0, x0, x2
    arm64.movImm x16, 6
    arm64.mul x2, x1, x16
    arm64.add x0, x0, x2
    arm64.movImm x16, 7
    arm64.mul x2, x1, x16
    arm64.add x0, x0, x2
    arm64.arm64AddLsl x0, x0, x1, lsl #3
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot11, x28
    arm64.storeSlotReg slot10, x27
    arm64.storeSlotReg slot9, x26
    arm64.storeSlotReg slot8, x25
    arm64.storeSlotReg slot7, x24
    arm64.storeSlotReg slot6, x23
    arm64.storeSlotReg slot5, x22
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movImm x0, 0
    arm64.movImm x1, 3
    arm64.bl pick
    arm64.movRegReg x19, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 3
    arm64.bl pick
    arm64.movRegReg x20, x0
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot0, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_3  ; " "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl __int_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.add x0, x19, x23
    arm64.add x0, x0, x20
    arm64.add x0, x0, x26
    arm64.storeSlotReg slot1, x0
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
    arm64.loadRegSlot x0, slot1
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.loadRegSlot x0, slot0
    arm64.bl __mm_decref
    arm64.movRegReg x0, x24
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.loadRegSlot x23, slot6
    arm64.loadRegSlot x24, slot7
    arm64.loadRegSlot x25, slot8
    arm64.loadRegSlot x26, slot9
    arm64.loadRegSlot x27, slot10
    arm64.loadRegSlot x28, slot11
    arm64.epilogue 112
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

func @tally {
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
    arm64.movRegReg x19, x0
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b forhdr
  each:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x23, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x23, x28, x23
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x23, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.add x20, x20, x21
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.cmp x21, x19
    arm64.b.lt each
  forexit:
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
}

func @pick {
  entry:
    arm64.prologue 16
    arm64.cbz x0, ifcont
  heavy:
    arm64.movRegReg x0, x1
    arm64.bl tally
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.arm64AddLsl x0, x1, x1, lsl #1
    arm64.arm64AddLsl x2, x1, x1, lsl #1
    arm64.add x0, x0, x2
    arm64.arm64AddLsl x0, x0, x1, lsl #2
    arm64.arm64AddLsl x2, x1, x1, lsl #2
    arm64.add x0, x0, x2
    arm64.movImm x16, 6
    arm64.mul x2, x1, x16
    arm64.add x0, x0, x2
    arm64.movImm x16, 7
    arm64.mul x2, x1, x16
    arm64.add x0, x0, x2
    arm64.arm64AddLsl x0, x0, x1, lsl #3
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot11, x28
    arm64.storeSlotReg slot10, x27
    arm64.storeSlotReg slot9, x26
    arm64.storeSlotReg slot8, x25
    arm64.storeSlotReg slot7, x24
    arm64.storeSlotReg slot6, x23
    arm64.storeSlotReg slot5, x22
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movImm x0, 0
    arm64.movImm x1, 3
    arm64.bl pick
    arm64.movRegReg x19, x0
    arm64.movImm x0, 1
    arm64.movImm x1, 3
    arm64.bl pick
    arm64.movRegReg x20, x0
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot0, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_3  ; " "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl __int_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.add x0, x19, x23
    arm64.add x0, x0, x20
    arm64.add x0, x0, x26
    arm64.storeSlotReg slot1, x0
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
    arm64.loadRegSlot x0, slot1
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.loadRegSlot x0, slot0
    arm64.bl __mm_decref
    arm64.movRegReg x0, x24
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.loadRegSlot x23, slot6
    arm64.loadRegSlot x24, slot7
    arm64.loadRegSlot x25, slot8
    arm64.loadRegSlot x26, slot9
    arm64.loadRegSlot x27, slot10
    arm64.loadRegSlot x28, slot11
    arm64.epilogue 112
    arm64.ret
}
```

<!-- test: a-called-once-body-that-holds-values-across-calls-is-spliced-where-every-hot-path-reaches-it -->
The same `tally`, called once, from `run`, whose only path reaches the call. Every path of `run` pays the callee's
registers either way, so the body is spliced: the pin holds no `callDirect tally` and the loop is in `run`.
```maxon
typealias Integer = int(i64.min to i64.max)

function tally(limit Integer) returns Integer
	var total = 0 as Integer

	for step in 0 upto limit 'each'
		print("{step}\n")
		total = total + step
	end 'each'

	return total
end 'tally'

function run(limit Integer) returns Integer
	return tally(limit)
end 'run'

function main() returns ExitCode
	let first = run(3)
	let second = run(2)
	print("{first} {second}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
1
2
0
1
3 1
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

func @run {
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
  __il_body:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r12, 0
    x64.jmp forhdr
  each:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rax, r12, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r12
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.loadRegSlot r12, slot4
    x64.storeBaseDispReg.word64 [r12 + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect print
    x64.movRegReg rcx, r12
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rcx, rax
  forstep:
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg r12, rcx
  forhdr:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, r12
    x64.cmpRegReg r12, rbx
    x64.jcc less, each
  __il_cont:
    x64.loadRegSlot r8, slot1
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
    x64.prologue 88
    x64.movRegImm32 rcx, 3
    x64.callDirect run
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 2
    x64.callDirect run
    x64.movRegReg r12, r8
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot0, r13
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __int_to_string
    x64.storeSlotReg slot5, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot3, rax
    x64.leaRegRegReg rcx, rbx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, rbx
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
    x64.loadRegSlot rbx, slot2
    x64.storeBaseDispReg.word64 [rbx + 0], r12
    x64.loadRegSlot rax, slot1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
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

func @run {
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
  __il_body:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r12, 0
    x64.jmp forhdr
  each:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rax, r12, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r12
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.loadRegSlot r12, slot4
    x64.storeBaseDispReg.word64 [r12 + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect print
    x64.movRegReg rcx, r12
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rcx, rax
  forstep:
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg r12, rcx
  forhdr:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, r12
    x64.cmpRegReg r12, rbx
    x64.jcc less, each
  __il_cont:
    x64.loadRegSlot r8, slot1
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
    x64.prologue 88
    x64.movRegImm32 rcx, 3
    x64.callDirect run
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 2
    x64.callDirect run
    x64.movRegReg r12, r8
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot0, r13
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __int_to_string
    x64.storeSlotReg slot5, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot3, rax
    x64.leaRegRegReg rcx, rbx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, rbx
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
    x64.loadRegSlot rbx, slot2
    x64.storeBaseDispReg.word64 [rbx + 0], r12
    x64.loadRegSlot rax, slot1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
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

func @run {
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
    arm64.movRegReg x19, x0
  __il_body:
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b forhdr
  each:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x23, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x23, x28, x23
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x23, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.add x20, x20, x21
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.cmp x21, x19
    arm64.b.lt each
  __il_cont:
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
}

func @main {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot11, x28
    arm64.storeSlotReg slot10, x27
    arm64.storeSlotReg slot9, x26
    arm64.storeSlotReg slot8, x25
    arm64.storeSlotReg slot7, x24
    arm64.storeSlotReg slot6, x23
    arm64.storeSlotReg slot5, x22
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movImm x0, 3
    arm64.bl run
    arm64.movRegReg x19, x0
    arm64.movImm x0, 2
    arm64.bl run
    arm64.movRegReg x20, x0
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot0, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_3  ; " "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl __int_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.add x0, x19, x23
    arm64.add x0, x0, x20
    arm64.add x0, x0, x26
    arm64.storeSlotReg slot1, x0
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
    arm64.loadRegSlot x0, slot1
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.loadRegSlot x0, slot0
    arm64.bl __mm_decref
    arm64.movRegReg x0, x24
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.loadRegSlot x23, slot6
    arm64.loadRegSlot x24, slot7
    arm64.loadRegSlot x25, slot8
    arm64.loadRegSlot x26, slot9
    arm64.loadRegSlot x27, slot10
    arm64.loadRegSlot x28, slot11
    arm64.epilogue 112
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

func @run {
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
    arm64.movRegReg x19, x0
  __il_body:
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b forhdr
  each:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x23, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x23, x28, x23
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x23, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.add x20, x20, x21
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.cmp x21, x19
    arm64.b.lt each
  __il_cont:
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
}

func @main {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot11, x28
    arm64.storeSlotReg slot10, x27
    arm64.storeSlotReg slot9, x26
    arm64.storeSlotReg slot8, x25
    arm64.storeSlotReg slot7, x24
    arm64.storeSlotReg slot6, x23
    arm64.storeSlotReg slot5, x22
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movImm x0, 3
    arm64.bl run
    arm64.movRegReg x19, x0
    arm64.movImm x0, 2
    arm64.bl run
    arm64.movRegReg x20, x0
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot0, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_3  ; " "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl __int_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.add x0, x19, x23
    arm64.add x0, x0, x20
    arm64.add x0, x0, x26
    arm64.storeSlotReg slot1, x0
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
    arm64.loadRegSlot x0, slot1
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.loadRegSlot x0, slot0
    arm64.bl __mm_decref
    arm64.movRegReg x0, x24
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.loadRegSlot x23, slot6
    arm64.loadRegSlot x24, slot7
    arm64.loadRegSlot x25, slot8
    arm64.loadRegSlot x26, slot9
    arm64.loadRegSlot x27, slot10
    arm64.loadRegSlot x28, slot11
    arm64.epilogue 112
    arm64.ret
}
```

<!-- test: a-called-once-body-that-holds-nothing-across-its-calls-is-spliced-off-a-lean-callers-hot-path -->
`announce` is called once, from `pick`, on a path the other arm of `pick` avoids, and keeps no value across its call.
Splicing it gives `pick` no callee-saved register, so it is spliced: the pin holds no `callDirect announce` and a
`pick` frame that saves no register.
```maxon
function announce()
	print("picked\n")
end 'announce'

function pick(heavy bool)
	if heavy 'heavy'
		announce()
	end 'heavy'
end 'pick'

function main() returns ExitCode
	pick(false)
	pick(true)

	return 0
end 'main'
```
```exitcode
0
```
```stdout
picked
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @pick {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, ifcont
  __il_body#4:
    x64.leaRegRdata rcx, [rip + __str_rec_1]  ; "picked\x0a"
  __il_body#6:
    x64.callDirect __write_stdout
  ifcont:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rcx, 0
    x64.callDirect pick
    x64.movRegImm32 rcx, 1
    x64.callDirect pick
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_program_started@16 = i8 0
}

func @pick {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, ifcont
  __il_body#4:
    x64.leaRegRdata rcx, [rip + __str_rec_1]  ; "picked\x0a"
  __il_body#6:
    x64.callDirect __write_stdout
  ifcont:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rcx, 0
    x64.callDirect pick
    x64.movRegImm32 rcx, 1
    x64.callDirect pick
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

func @pick {
  entry:
    arm64.prologue 16
    arm64.cbz x0, ifcont
  __il_body#4:
    arm64.leaRdata x0, __str_rec_1  ; "picked\x0a"
  __il_body#6:
    arm64.bl __write_stdout
  ifcont:
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 0
    arm64.bl pick
    arm64.movImm x0, 1
    arm64.bl pick
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

func @pick {
  entry:
    arm64.prologue 16
    arm64.cbz x0, ifcont
  __il_body#4:
    arm64.leaRdata x0, __str_rec_1  ; "picked\x0a"
  __il_body#6:
    arm64.bl __write_stdout
  ifcont:
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 0
    arm64.bl pick
    arm64.movImm x0, 1
    arm64.bl pick
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: an-error-exit-before-the-site-is-not-a-hot-path-that-avoids-it -->
`guarded` propagates a refusal from `check` and then reaches the call of `tally`. The refusal exit leaves the function
by an error flag the success path never sets, so it is not a hot path that avoids the call: every path that completes
reaches `tally`, which is spliced, and no `callDirect tally` remains.
```maxon
typealias Integer = int(i64.min to i64.max)

enum CheckError implements Error
	refused
end 'CheckError'

function check(limit Integer) throws CheckError
	if limit < 0 'negative'
		throw CheckError.refused
	end 'negative'
end 'check'

function tally(limit Integer) returns Integer
	var total = 0 as Integer

	for step in 0 upto limit 'each'
		print("{step}\n")
		total = total + step
	end 'each'

	return total
end 'tally'

function guarded(limit Integer) returns Integer throws CheckError
	try check(limit)

	return tally(limit)
end 'guarded'

function main() returns ExitCode
	let first = try guarded(3) otherwise 0
	let second = try guarded(2) otherwise 0
	print("{first} {second}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
1
2
0
1
3 1
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

func @guarded {
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
  __il_body#4:
    x64.cmpRegImm32 rbx, 0
    x64.jcc greaterEqual, ifcont
  negative:
    x64.movRegImm32 r10, 1
    x64.jmp __il_cont#3
  ifcont:
    x64.movRegImm32 r10, 0
  __il_cont#3:
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, __il_body#8
  tryerr:
    x64.movRegImm32 r8, 0
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __il_body#8:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r12, 0
    x64.jmp forhdr
  each:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rax, r12, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r12
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.loadRegSlot r12, slot4
    x64.storeBaseDispReg.word64 [r12 + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect print
    x64.movRegReg rcx, r12
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rcx, rax
  forstep:
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg r12, rcx
  forhdr:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, r12
    x64.cmpRegReg r12, rbx
    x64.jcc less, each
  __il_cont#7:
    x64.movRegImm32 r10, 0
    x64.loadRegSlot r8, slot1
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
    x64.prologue 88
    x64.movRegImm32 rcx, 3
    x64.callDirect guarded
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#1
  tryerr#2:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont#3
  tryok#1:
    x64.movRegReg rbx, r8
  trycont#3:
    x64.movRegImm32 rcx, 2
    x64.callDirect guarded
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#4
  tryerr#5:
    x64.movRegImm32 r12, 0
    x64.jmp trycont#6
  tryok#4:
    x64.movRegReg r12, r8
  trycont#6:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot0, r13
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __int_to_string
    x64.storeSlotReg slot5, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot3, rax
    x64.leaRegRegReg rcx, rbx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, rbx
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
    x64.loadRegSlot rbx, slot2
    x64.storeBaseDispReg.word64 [rbx + 0], r12
    x64.loadRegSlot rax, slot1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
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

func @guarded {
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
  __il_body#4:
    x64.cmpRegImm32 rbx, 0
    x64.jcc greaterEqual, ifcont
  negative:
    x64.movRegImm32 r10, 1
    x64.jmp __il_cont#3
  ifcont:
    x64.movRegImm32 r10, 0
  __il_cont#3:
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, __il_body#8
  tryerr:
    x64.movRegImm32 r8, 0
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __il_body#8:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r12, 0
    x64.jmp forhdr
  each:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rax, r12, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r12
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.loadRegSlot r12, slot4
    x64.storeBaseDispReg.word64 [r12 + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect print
    x64.movRegReg rcx, r12
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rcx, rax
  forstep:
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg r12, rcx
  forhdr:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, r12
    x64.cmpRegReg r12, rbx
    x64.jcc less, each
  __il_cont#7:
    x64.movRegImm32 r10, 0
    x64.loadRegSlot r8, slot1
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
    x64.prologue 88
    x64.movRegImm32 rcx, 3
    x64.callDirect guarded
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#1
  tryerr#2:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont#3
  tryok#1:
    x64.movRegReg rbx, r8
  trycont#3:
    x64.movRegImm32 rcx, 2
    x64.callDirect guarded
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#4
  tryerr#5:
    x64.movRegImm32 r12, 0
    x64.jmp trycont#6
  tryok#4:
    x64.movRegReg r12, r8
  trycont#6:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot0, r13
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __int_to_string
    x64.storeSlotReg slot5, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot3, rax
    x64.leaRegRegReg rcx, rbx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, rbx
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
    x64.loadRegSlot rbx, slot2
    x64.storeBaseDispReg.word64 [rbx + 0], r12
    x64.loadRegSlot rax, slot1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
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

func @guarded {
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
    arm64.movRegReg x19, x0
  __il_body#4:
    arm64.cmp x19, 0
    arm64.b.ge ifcont
  negative:
    arm64.movImm x9, 1
    arm64.b __il_cont#3
  ifcont:
    arm64.movImm x9, 0
  __il_cont#3:
    arm64.cmp x9, 0
    arm64.b.eq __il_body#8
  tryerr:
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
  __il_body#8:
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b forhdr
  each:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x23, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x23, x28, x23
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x23, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.add x20, x20, x21
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.cmp x21, x19
    arm64.b.lt each
  __il_cont#7:
    arm64.movImm x9, 0
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
}

func @main {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot11, x28
    arm64.storeSlotReg slot10, x27
    arm64.storeSlotReg slot9, x26
    arm64.storeSlotReg slot8, x25
    arm64.storeSlotReg slot7, x24
    arm64.storeSlotReg slot6, x23
    arm64.storeSlotReg slot5, x22
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movImm x0, 3
    arm64.bl guarded
    arm64.cmp x9, 0
    arm64.b.eq tryok#1
  tryerr#2:
    arm64.movImm x19, 0
    arm64.b trycont#3
  tryok#1:
    arm64.movRegReg x19, x0
  trycont#3:
    arm64.movImm x0, 2
    arm64.bl guarded
    arm64.cmp x9, 0
    arm64.b.eq tryok#4
  tryerr#5:
    arm64.movImm x20, 0
    arm64.b trycont#6
  tryok#4:
    arm64.movRegReg x20, x0
  trycont#6:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot0, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_3  ; " "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl __int_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.add x0, x19, x23
    arm64.add x0, x0, x20
    arm64.add x0, x0, x26
    arm64.storeSlotReg slot1, x0
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
    arm64.loadRegSlot x0, slot1
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.loadRegSlot x0, slot0
    arm64.bl __mm_decref
    arm64.movRegReg x0, x24
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.loadRegSlot x23, slot6
    arm64.loadRegSlot x24, slot7
    arm64.loadRegSlot x25, slot8
    arm64.loadRegSlot x26, slot9
    arm64.loadRegSlot x27, slot10
    arm64.loadRegSlot x28, slot11
    arm64.epilogue 112
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

func @guarded {
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
    arm64.movRegReg x19, x0
  __il_body#4:
    arm64.cmp x19, 0
    arm64.b.ge ifcont
  negative:
    arm64.movImm x9, 1
    arm64.b __il_cont#3
  ifcont:
    arm64.movImm x9, 0
  __il_cont#3:
    arm64.cmp x9, 0
    arm64.b.eq __il_body#8
  tryerr:
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
  __il_body#8:
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b forhdr
  each:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x23, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x23, x28, x23
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x23, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.add x20, x20, x21
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.cmp x21, x19
    arm64.b.lt each
  __il_cont#7:
    arm64.movImm x9, 0
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
}

func @main {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot11, x28
    arm64.storeSlotReg slot10, x27
    arm64.storeSlotReg slot9, x26
    arm64.storeSlotReg slot8, x25
    arm64.storeSlotReg slot7, x24
    arm64.storeSlotReg slot6, x23
    arm64.storeSlotReg slot5, x22
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movImm x0, 3
    arm64.bl guarded
    arm64.cmp x9, 0
    arm64.b.eq tryok#1
  tryerr#2:
    arm64.movImm x19, 0
    arm64.b trycont#3
  tryok#1:
    arm64.movRegReg x19, x0
  trycont#3:
    arm64.movImm x0, 2
    arm64.bl guarded
    arm64.cmp x9, 0
    arm64.b.eq tryok#4
  tryerr#5:
    arm64.movImm x20, 0
    arm64.b trycont#6
  tryok#4:
    arm64.movRegReg x20, x0
  trycont#6:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot0, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_3  ; " "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl __int_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.add x0, x19, x23
    arm64.add x0, x0, x20
    arm64.add x0, x0, x26
    arm64.storeSlotReg slot1, x0
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
    arm64.loadRegSlot x0, slot1
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.loadRegSlot x0, slot0
    arm64.bl __mm_decref
    arm64.movRegReg x0, x24
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.loadRegSlot x23, slot6
    arm64.loadRegSlot x24, slot7
    arm64.loadRegSlot x25, slot8
    arm64.loadRegSlot x26, slot9
    arm64.loadRegSlot x27, slot10
    arm64.loadRegSlot x28, slot11
    arm64.epilogue 112
    arm64.ret
}
```

<!-- test: a-lean-caller-returning-an-interface-keeps-a-holding-body-off-its-hot-path-a-call -->
An interface result leaves a non-throwing function through `errorReturn` with the witness in its second slot, a slot
that is not an error flag there. `pick` returns a `Shape`, holds nothing across any call, and its `shapeOf(limit)`
path never reaches `tally`, which holds `total`, `limit` and `step` across `print`. The call stays: the pin holds one
`callDirect tally` and a `pick` frame that saves no register.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	let side as Integer

	static function create(side Integer) returns Square
		return Self{side: side}
	end 'create'

	function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

function tally(limit Integer) returns Integer
	var total = 0 as Integer

	for step in 0 upto limit 'each'
		print("{step}\n")
		total = total + step
	end 'each'

	return total
end 'tally'

function shapeOf(side Integer) returns Shape
	if side < 0 'mirrored'
		return shapeOf(0 - side)
	end 'mirrored'

	return Square.create(side)
end 'shapeOf'

function pick(heavy bool, limit Integer) returns Shape
	if heavy 'heavy'
		return shapeOf(tally(limit))
	end 'heavy'

	return shapeOf(limit)
end 'pick'

function main() returns ExitCode
	let light = pick(false, limit: 3)
	let heavy = pick(true, limit: 3)
	print("{light.area()} {heavy.area()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
1
2
9 9
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

func @Square.create {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @Square.area {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.loadRegBaseDisp.word64 r8, [rcx + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.imulRegReg r8, r8, rax
    x64.popReg rbp
    x64.ret
}

func @tally {
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
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r12, 0
    x64.jmp forhdr
  each:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rax, r12, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r12
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.loadRegSlot r12, slot4
    x64.storeBaseDispReg.word64 [r12 + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect print
    x64.movRegReg rcx, r12
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rcx, rax
  forstep:
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg r12, rcx
  forhdr:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, r12
    x64.cmpRegReg r12, rbx
    x64.jcc less, each
  forexit:
    x64.loadRegSlot r8, slot1
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @shapeOf {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc greaterEqual, ifcont
  mirrored:
    x64.movRegImm32 rax, 0
    x64.subRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.callDirect shapeOf
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.callDirect Square.create
    x64.leaRegRdata r10, [rip + __witness_Square.Shape]
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @pick {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, ifcont
  heavy:
    x64.movRegReg rcx, rdx
    x64.callDirect tally
    x64.movRegReg rcx, r8
    x64.callDirect shapeOf
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegReg rcx, rdx
    x64.callDirect shapeOf
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 120
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 3
    x64.callDirect pick
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.movRegReg r12, r10
    x64.storeSlotReg slot1, r12
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 3
    x64.callDirect pick
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg r14, r10
    x64.storeSlotReg slot3, r14
    x64.movRegReg rcx, rbx
    x64.callMem [r12 + 48]
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot4, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot10, rcx
    x64.loadRegBaseDisp.word64 r15, [rax + 8]
    x64.movRegReg rcx, r13
    x64.callMem [r14 + 48]
    x64.movRegReg r13, r8
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r14
    x64.callDirect __int_to_string
    x64.storeSlotReg slot9, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot8, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot7, rax
    x64.leaRegRegReg rcx, rbx, r15
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot5, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot6, r8
    x64.leaRegRegImm32 r13, r8, 56
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r13, rbx
    x64.loadRegSlot rdx, slot10
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r15
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r15
    x64.loadRegSlot rax, slot9
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot9
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot7
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot7
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rbx, slot6
    x64.storeBaseDispReg.word64 [rbx + 0], r13
    x64.loadRegSlot rax, slot5
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot4
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rcx, slot3
    x64.callDirect __drop_existential
  exitdrop:
    x64.loadRegSlot rdx, slot0
    x64.loadRegSlot rcx, slot1
    x64.callDirect __drop_existential
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 120
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

func @Square.create {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @Square.area {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.loadRegBaseDisp.word64 r8, [rcx + 0]
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.imulRegReg r8, r8, rax
    x64.popReg rbp
    x64.ret
}

func @tally {
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
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r12, 0
    x64.jmp forhdr
  each:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rax, r12, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r12
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.loadRegSlot r12, slot4
    x64.storeBaseDispReg.word64 [r12 + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect print
    x64.movRegReg rcx, r12
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rcx, rax
  forstep:
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg r12, rcx
  forhdr:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, r12
    x64.cmpRegReg r12, rbx
    x64.jcc less, each
  forexit:
    x64.loadRegSlot r8, slot1
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @shapeOf {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc greaterEqual, ifcont
  mirrored:
    x64.movRegImm32 rax, 0
    x64.subRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.callDirect shapeOf
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.callDirect Square.create
    x64.leaRegRdata r10, [rip + __witness_Square.Shape]
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @pick {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, ifcont
  heavy:
    x64.movRegReg rcx, rdx
    x64.callDirect tally
    x64.movRegReg rcx, r8
    x64.callDirect shapeOf
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegReg rcx, rdx
    x64.callDirect shapeOf
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 120
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 3
    x64.callDirect pick
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.movRegReg r12, r10
    x64.storeSlotReg slot1, r12
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 3
    x64.callDirect pick
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg r14, r10
    x64.storeSlotReg slot3, r14
    x64.movRegReg rcx, rbx
    x64.callMem [r12 + 48]
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot4, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot10, rcx
    x64.loadRegBaseDisp.word64 r15, [rax + 8]
    x64.movRegReg rcx, r13
    x64.callMem [r14 + 48]
    x64.movRegReg r13, r8
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r14
    x64.callDirect __int_to_string
    x64.storeSlotReg slot9, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot8, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot7, rax
    x64.leaRegRegReg rcx, rbx, r15
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot5, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot6, r8
    x64.leaRegRegImm32 r13, r8, 56
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r13, rbx
    x64.loadRegSlot rdx, slot10
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r15
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r15
    x64.loadRegSlot rax, slot9
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot9
    x64.leaRegRegReg rbx, rbx, rax
    x64.loadRegSlot rax, slot7
    x64.loadRegSlot rdx, slot8
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot7
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rbx, slot6
    x64.storeBaseDispReg.word64 [rbx + 0], r13
    x64.loadRegSlot rax, slot5
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot4
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rcx, slot3
    x64.callDirect __drop_existential
  exitdrop:
    x64.loadRegSlot rdx, slot0
    x64.loadRegSlot rcx, slot1
    x64.callDirect __drop_existential
  exitterminal:
    x64.movRegReg r8, rbx
    x64.epilogue 120
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

func @Square.create {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @Square.area {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.mul x0, x1, x0
    arm64.epilogue 16
    arm64.ret
}

func @tally {
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
    arm64.movRegReg x19, x0
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b forhdr
  each:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x23, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x23, x28, x23
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x23, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.add x20, x20, x21
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.cmp x21, x19
    arm64.b.lt each
  forexit:
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
}

func @shapeOf {
  entry:
    arm64.prologue 16
    arm64.cmp x0, 0
    arm64.b.ge ifcont
  mirrored:
    arm64.movImm x1, 0
    arm64.sub x0, x1, x0
    arm64.bl shapeOf
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.bl Square.create
    arm64.leaRdata x9, __witness_Square.Shape
    arm64.epilogue 16
    arm64.ret
}

func @pick {
  entry:
    arm64.prologue 16
    arm64.cbz x0, ifcont
  heavy:
    arm64.movRegReg x0, x1
    arm64.bl tally
    arm64.bl shapeOf
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movRegReg x0, x1
    arm64.bl shapeOf
    arm64.epilogue 16
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
    arm64.movImm x0, 0
    arm64.movImm x1, 3
    arm64.bl pick
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x20, x9
    arm64.storeSlotReg slot1, x20
    arm64.movImm x0, 1
    arm64.movImm x1, 3
    arm64.bl pick
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot2, x21
    arm64.movRegReg x22, x9
    arm64.storeSlotReg slot3, x22
    arm64.movRegReg x0, x19
    arm64.loadRegBaseDisp.word64 x16, [x20 + 48]
    arm64.blr x16
    arm64.movRegReg x19, x0
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot4, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_3  ; " "
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.movRegReg x0, x21
    arm64.loadRegBaseDisp.word64 x16, [x22 + 48]
    arm64.blr x16
    arm64.movRegReg x21, x0
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x21, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.add x0, x19, x24
    arm64.add x0, x0, x21
    arm64.add x0, x0, x26
    arm64.storeSlotReg slot5, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x28, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.add x19, x19, x24
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x19, x19, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x0, x19, x26
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.loadRegSlot x0, slot5
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.loadRegSlot x0, slot4
    arm64.bl __mm_decref
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.loadRegSlot x1, slot2
    arm64.loadRegSlot x0, slot3
    arm64.bl __drop_existential
  exitdrop:
    arm64.loadRegSlot x1, slot0
    arm64.loadRegSlot x0, slot1
    arm64.bl __drop_existential
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

func @Square.create {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @Square.area {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.mul x0, x1, x0
    arm64.epilogue 16
    arm64.ret
}

func @tally {
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
    arm64.movRegReg x19, x0
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b forhdr
  each:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x23, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x23, x28, x23
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x23, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.add x20, x20, x21
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.cmp x21, x19
    arm64.b.lt each
  forexit:
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
}

func @shapeOf {
  entry:
    arm64.prologue 16
    arm64.cmp x0, 0
    arm64.b.ge ifcont
  mirrored:
    arm64.movImm x1, 0
    arm64.sub x0, x1, x0
    arm64.bl shapeOf
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.bl Square.create
    arm64.leaRdata x9, __witness_Square.Shape
    arm64.epilogue 16
    arm64.ret
}

func @pick {
  entry:
    arm64.prologue 16
    arm64.cbz x0, ifcont
  heavy:
    arm64.movRegReg x0, x1
    arm64.bl tally
    arm64.bl shapeOf
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movRegReg x0, x1
    arm64.bl shapeOf
    arm64.epilogue 16
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
    arm64.movImm x0, 0
    arm64.movImm x1, 3
    arm64.bl pick
    arm64.movRegReg x19, x0
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x20, x9
    arm64.storeSlotReg slot1, x20
    arm64.movImm x0, 1
    arm64.movImm x1, 3
    arm64.bl pick
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot2, x21
    arm64.movRegReg x22, x9
    arm64.storeSlotReg slot3, x22
    arm64.movRegReg x0, x19
    arm64.loadRegBaseDisp.word64 x16, [x20 + 48]
    arm64.blr x16
    arm64.movRegReg x19, x0
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeSlotReg slot4, x20
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_3  ; " "
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.movRegReg x0, x21
    arm64.loadRegBaseDisp.word64 x16, [x22 + 48]
    arm64.blr x16
    arm64.movRegReg x21, x0
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x21, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.add x0, x19, x24
    arm64.add x0, x0, x21
    arm64.add x0, x0, x26
    arm64.storeSlotReg slot5, x0
    arm64.add x0, x0, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x28, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.add x19, x19, x24
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x19, x19, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.movRegReg x2, x26
    arm64.bl __str_copy
    arm64.add x0, x19, x26
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.loadRegSlot x0, slot5
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.loadRegSlot x0, slot4
    arm64.bl __mm_decref
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.loadRegSlot x1, slot2
    arm64.loadRegSlot x0, slot3
    arm64.bl __drop_existential
  exitdrop:
    arm64.loadRegSlot x1, slot0
    arm64.loadRegSlot x0, slot1
    arm64.bl __drop_existential
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
}
```

<!-- test: a-lean-caller-returning-a-tuple-keeps-a-holding-body-off-its-hot-path-a-call -->
The same shape with a tuple result, which the value-tuple convention later returns in two registers. The call of
`tally` stays and `pick` saves no register.
```maxon
typealias Integer = int(i64.min to i64.max)

function tally(limit Integer) returns Integer
	var total = 0 as Integer

	for step in 0 upto limit 'each'
		print("{step}\n")
		total = total + step
	end 'each'

	return total
end 'tally'

function pairOf(low Integer) returns (Integer, Integer)
	if low < 0 'mirrored'
		return pairOf(0 - low)
	end 'mirrored'

	return (low, low + 1)
end 'pairOf'

function pick(heavy bool, limit Integer) returns (Integer, Integer)
	if heavy 'heavy'
		return pairOf(tally(limit))
	end 'heavy'

	return pairOf(limit + 1 + 2 + 3 + 4 + 5 + 6 + 7 + 8 + 9 + 10)
end 'pick'

function main() returns ExitCode
	let light = pick(false, limit: 3)
	let heavy = pick(true, limit: 3)
	print("{light.0} {light.1} {heavy.0} {heavy.1}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
1
2
58 59 3 4
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

func @tally {
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
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r12, 0
    x64.jmp forhdr
  each:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rax, r12, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r12
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.loadRegSlot r12, slot4
    x64.storeBaseDispReg.word64 [r12 + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect print
    x64.movRegReg rcx, r12
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rcx, rax
  forstep:
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg r12, rcx
  forhdr:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, r12
    x64.cmpRegReg r12, rbx
    x64.jcc less, each
  forexit:
    x64.loadRegSlot r8, slot1
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @pairOf {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc greaterEqual, ifcont
  mirrored:
    x64.movRegImm32 rax, 0
    x64.subRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.callDirect pairOf
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.leaRegRegImm32 r10, rcx, 1
    x64.movRegReg r8, rcx
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @pick {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, ifcont
  heavy:
    x64.movRegReg rcx, rdx
    x64.callDirect tally
    x64.movRegReg rcx, r8
    x64.callDirect pairOf
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.leaRegRegImm32 rax, rdx, 1
    x64.leaRegRegImm32 rax, rax, 2
    x64.leaRegRegImm32 rax, rax, 3
    x64.leaRegRegImm32 rax, rax, 4
    x64.leaRegRegImm32 rax, rax, 5
    x64.leaRegRegImm32 rax, rax, 6
    x64.leaRegRegImm32 rax, rax, 7
    x64.leaRegRegImm32 rax, rax, 8
    x64.leaRegRegImm32 rax, rax, 9
    x64.leaRegRegImm32 rcx, rax, 10
    x64.callDirect pairOf
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 184
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 3
    x64.callDirect pick
    x64.movRegReg rbx, r8
    x64.movRegReg r12, r10
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 3
    x64.callDirect pick
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
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; " "
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
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; " "
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
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; " "
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
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
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

func @tally {
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
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r12, 0
    x64.jmp forhdr
  each:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rax, r12, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r12
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.loadRegSlot r12, slot4
    x64.storeBaseDispReg.word64 [r12 + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect print
    x64.movRegReg rcx, r12
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rcx, rax
  forstep:
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg r12, rcx
  forhdr:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, r12
    x64.cmpRegReg r12, rbx
    x64.jcc less, each
  forexit:
    x64.loadRegSlot r8, slot1
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @pairOf {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc greaterEqual, ifcont
  mirrored:
    x64.movRegImm32 rax, 0
    x64.subRegReg rax, rax, rcx
    x64.movRegReg rcx, rax
    x64.callDirect pairOf
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.leaRegRegImm32 r10, rcx, 1
    x64.movRegReg r8, rcx
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @pick {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, ifcont
  heavy:
    x64.movRegReg rcx, rdx
    x64.callDirect tally
    x64.movRegReg rcx, r8
    x64.callDirect pairOf
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.leaRegRegImm32 rax, rdx, 1
    x64.leaRegRegImm32 rax, rax, 2
    x64.leaRegRegImm32 rax, rax, 3
    x64.leaRegRegImm32 rax, rax, 4
    x64.leaRegRegImm32 rax, rax, 5
    x64.leaRegRegImm32 rax, rax, 6
    x64.leaRegRegImm32 rax, rax, 7
    x64.leaRegRegImm32 rax, rax, 8
    x64.leaRegRegImm32 rax, rax, 9
    x64.leaRegRegImm32 rcx, rax, 10
    x64.callDirect pairOf
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 184
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 3
    x64.callDirect pick
    x64.movRegReg rbx, r8
    x64.movRegReg r12, r10
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 3
    x64.callDirect pick
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
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; " "
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
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; " "
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
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; " "
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
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
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

func @tally {
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
    arm64.movRegReg x19, x0
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b forhdr
  each:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x23, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x23, x28, x23
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x23, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.add x20, x20, x21
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.cmp x21, x19
    arm64.b.lt each
  forexit:
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
}

func @pairOf {
  entry:
    arm64.prologue 16
    arm64.cmp x0, 0
    arm64.b.ge ifcont
  mirrored:
    arm64.movImm x1, 0
    arm64.sub x0, x1, x0
    arm64.bl pairOf
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.add x9, x0, 1
    arm64.epilogue 16
    arm64.ret
}

func @pick {
  entry:
    arm64.prologue 16
    arm64.cbz x0, ifcont
  heavy:
    arm64.movRegReg x0, x1
    arm64.bl tally
    arm64.bl pairOf
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.add x0, x1, 1
    arm64.add x0, x0, 2
    arm64.add x0, x0, 3
    arm64.add x0, x0, 4
    arm64.add x0, x0, 5
    arm64.add x0, x0, 6
    arm64.add x0, x0, 7
    arm64.add x0, x0, 8
    arm64.add x0, x0, 9
    arm64.add x0, x0, 10
    arm64.bl pairOf
    arm64.epilogue 16
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
    arm64.movImm x0, 0
    arm64.movImm x1, 3
    arm64.bl pick
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x9
    arm64.movImm x0, 1
    arm64.movImm x1, 3
    arm64.bl pick
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
    arm64.leaRdata x0, __str_rec_3  ; " "
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
    arm64.leaRdata x0, __str_rec_3  ; " "
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
    arm64.leaRdata x0, __str_rec_3  ; " "
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
    arm64.leaRdata x1, __str_rec_2  ; "\x0a"
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

func @tally {
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
    arm64.movRegReg x19, x0
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b forhdr
  each:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x23, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x23, x28, x23
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x23, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.add x20, x20, x21
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.cmp x21, x19
    arm64.b.lt each
  forexit:
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
}

func @pairOf {
  entry:
    arm64.prologue 16
    arm64.cmp x0, 0
    arm64.b.ge ifcont
  mirrored:
    arm64.movImm x1, 0
    arm64.sub x0, x1, x0
    arm64.bl pairOf
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.add x9, x0, 1
    arm64.epilogue 16
    arm64.ret
}

func @pick {
  entry:
    arm64.prologue 16
    arm64.cbz x0, ifcont
  heavy:
    arm64.movRegReg x0, x1
    arm64.bl tally
    arm64.bl pairOf
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.add x0, x1, 1
    arm64.add x0, x0, 2
    arm64.add x0, x0, 3
    arm64.add x0, x0, 4
    arm64.add x0, x0, 5
    arm64.add x0, x0, 6
    arm64.add x0, x0, 7
    arm64.add x0, x0, 8
    arm64.add x0, x0, 9
    arm64.add x0, x0, 10
    arm64.bl pairOf
    arm64.epilogue 16
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
    arm64.movImm x0, 0
    arm64.movImm x1, 3
    arm64.bl pick
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x9
    arm64.movImm x0, 1
    arm64.movImm x1, 3
    arm64.bl pick
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
    arm64.leaRdata x0, __str_rec_3  ; " "
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
    arm64.leaRdata x0, __str_rec_3  ; " "
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
    arm64.leaRdata x0, __str_rec_3  ; " "
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
    arm64.leaRdata x1, __str_rec_2  ; "\x0a"
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

<!-- test: a-second-holding-body-is-spliced-once-an-earlier-one-has-filled-the-frame -->
`route` holds nothing across its calls. `grindA` is called on every path, so splicing it costs `route` no path it
did not already pay, and it is spliced. After that `route` saves callee registers on every path, so `grindB`, called
on one arm only, widens nothing and is spliced too: neither call remains.
```maxon
typealias Integer = int(i64.min to i64.max)

function grindB()
	var total = 0 as Integer

	for step in 0 upto 3 'each'
		print("b{step}\n")
		total = total + step
	end 'each'

	print("{total}\n")
end 'grindB'

function grindA()
	var total = 0 as Integer

	for step in 0 upto 2 'each'
		print("a{step}\n")
		total = total + step
	end 'each'

	print("{total}\n")
end 'grindA'

function route(second bool)
	if second 'second'
		grindB()
	end 'second'

	grindA()
end 'route'

function main() returns ExitCode
	route(false)
	route(true)

	return 0
end 'main'
```
```exitcode
0
```
```stdout
a0
a1
1
b0
b1
b2
3
a0
a1
1
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

func @route {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 184
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __il_body#10
  __il_body#4:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rbx, 0
    x64.jmp forhdr#5
  each#6:
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "b"
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot17, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot13, rax
    x64.leaRegRegReg rcx, r13, rbx
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot5, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot7, r8
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
    x64.loadRegSlot rax, slot13
    x64.loadRegSlot rdx, slot17
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot13
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rbx, slot7
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot5
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot rcx, slot3
    x64.leaRegRegReg rax, rcx, rax
  forstep#7:
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg rbx, rcx
  forhdr#5:
    x64.storeSlotReg slot3, rax
    x64.storeSlotReg slot1, rbx
    x64.cmpRegImm32 rbx, 3
    x64.jcc less, each#6
  forexit#8:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot9, rbx
    x64.loadRegSlot rcx, slot3
    x64.movRegReg rdx, rbx
    x64.callDirect __int_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, r12, r14
    x64.storeSlotReg slot12, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot16, r8
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
    x64.loadRegSlot rbx, slot16
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot12
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot9
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
  __il_body#10:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rbx, 0
    x64.jmp forhdr#11
  each#12:
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; "a"
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot15, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot11, rax
    x64.leaRegRegReg rcx, r13, rbx
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot4, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot6, r8
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
    x64.loadRegSlot rax, slot11
    x64.loadRegSlot rdx, slot15
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot11
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rbx, slot6
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot4
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot2
    x64.leaRegRegReg rax, rcx, rax
  forstep#13:
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg rbx, rcx
  forhdr#11:
    x64.storeSlotReg slot2, rax
    x64.storeSlotReg slot0, rbx
    x64.cmpRegImm32 rbx, 2
    x64.jcc less, each#12
  forexit#14:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot8, rbx
    x64.loadRegSlot rcx, slot2
    x64.movRegReg rdx, rbx
    x64.callDirect __int_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, r12, r14
    x64.storeSlotReg slot10, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot14, r8
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
    x64.loadRegSlot rbx, slot14
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot10
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot8
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
  __il_cont:
    x64.epilogue 184
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
    x64.prologue 32
    x64.movRegImm32 rcx, 0
    x64.callDirect route
    x64.movRegImm32 rcx, 1
    x64.callDirect route
    x64.movRegImm32 r8, 0
    x64.epilogue 32
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

func @route {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 184
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __il_body#10
  __il_body#4:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rbx, 0
    x64.jmp forhdr#5
  each#6:
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "b"
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot17, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot13, rax
    x64.leaRegRegReg rcx, r13, rbx
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot5, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot7, r8
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
    x64.loadRegSlot rax, slot13
    x64.loadRegSlot rdx, slot17
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot13
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rbx, slot7
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot5
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot rcx, slot3
    x64.leaRegRegReg rax, rcx, rax
  forstep#7:
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg rbx, rcx
  forhdr#5:
    x64.storeSlotReg slot3, rax
    x64.storeSlotReg slot1, rbx
    x64.cmpRegImm32 rbx, 3
    x64.jcc less, each#6
  forexit#8:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot9, rbx
    x64.loadRegSlot rcx, slot3
    x64.movRegReg rdx, rbx
    x64.callDirect __int_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, r12, r14
    x64.storeSlotReg slot12, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot16, r8
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
    x64.loadRegSlot rbx, slot16
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot12
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot9
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
  __il_body#10:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rbx, 0
    x64.jmp forhdr#11
  each#12:
    x64.leaRegRdata rax, [rip + __str_rec_5]  ; "a"
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot15, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot11, rax
    x64.leaRegRegReg rcx, r13, rbx
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot4, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot6, r8
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
    x64.loadRegSlot rax, slot11
    x64.loadRegSlot rdx, slot15
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot11
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rbx, slot6
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot4
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot2
    x64.leaRegRegReg rax, rcx, rax
  forstep#13:
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg rbx, rcx
  forhdr#11:
    x64.storeSlotReg slot2, rax
    x64.storeSlotReg slot0, rbx
    x64.cmpRegImm32 rbx, 2
    x64.jcc less, each#12
  forexit#14:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot8, rbx
    x64.loadRegSlot rcx, slot2
    x64.movRegReg rdx, rbx
    x64.callDirect __int_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, r12, r14
    x64.storeSlotReg slot10, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot14, r8
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
    x64.loadRegSlot rbx, slot14
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot10
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot8
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
  __il_cont:
    x64.epilogue 184
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
    x64.prologue 32
    x64.movRegImm32 rcx, 0
    x64.callDirect route
    x64.movRegImm32 rcx, 1
    x64.callDirect route
    x64.movRegImm32 r8, 0
    x64.epilogue 32
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

func @route {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot11, x28
    arm64.storeSlotReg slot10, x27
    arm64.storeSlotReg slot9, x26
    arm64.storeSlotReg slot8, x25
    arm64.storeSlotReg slot7, x24
    arm64.storeSlotReg slot6, x23
    arm64.storeSlotReg slot5, x22
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.cbz x0, __il_body#10
  __il_body#4:
    arm64.movImm x19, 0
    arm64.movImm x20, 0
    arm64.b forhdr#5
  each#6:
    arm64.leaRdata x0, __str_rec_3  ; "b"
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x23
    arm64.bl __int_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_4  ; "\x0a"
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
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot1
    arm64.add x19, x19, x0
  forstep#7:
    arm64.loadRegSlot x0, slot1
    arm64.add x0, x0, 1
    arm64.movRegReg x20, x0
  forhdr#5:
    arm64.storeSlotReg slot1, x20
    arm64.cmp x20, 3
    arm64.b.lt each#6
  forexit#8:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_4  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.add x23, x19, x22
    arm64.add x0, x23, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.add x25, x24, 56
    arm64.movRegReg x0, x25
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x25, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x0, x19, x22
    arm64.storeBaseDispReg.word64 [x24 + 0], x25
    arm64.storeBaseDispReg.word64 [x24 + 8], x23
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x24 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.movRegReg x0, x24
    arm64.bl print
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
  __il_body#10:
    arm64.movImm x19, 0
    arm64.movImm x20, 0
    arm64.b forhdr#11
  each#12:
    arm64.leaRdata x0, __str_rec_5  ; "a"
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x23
    arm64.bl __int_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_4  ; "\x0a"
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
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot0
    arm64.add x19, x19, x0
  forstep#13:
    arm64.loadRegSlot x0, slot0
    arm64.add x0, x0, 1
    arm64.movRegReg x20, x0
  forhdr#11:
    arm64.storeSlotReg slot0, x20
    arm64.cmp x20, 2
    arm64.b.lt each#12
  forexit#14:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_4  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.add x23, x19, x22
    arm64.add x0, x23, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.add x25, x24, 56
    arm64.movRegReg x0, x25
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x25, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x0, x19, x22
    arm64.storeBaseDispReg.word64 [x24 + 0], x25
    arm64.storeBaseDispReg.word64 [x24 + 8], x23
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x24 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.movRegReg x0, x24
    arm64.bl print
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
  __il_cont:
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.loadRegSlot x23, slot6
    arm64.loadRegSlot x24, slot7
    arm64.loadRegSlot x25, slot8
    arm64.loadRegSlot x26, slot9
    arm64.loadRegSlot x27, slot10
    arm64.loadRegSlot x28, slot11
    arm64.epilogue 112
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 0
    arm64.bl route
    arm64.movImm x0, 1
    arm64.bl route
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
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @route {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot11, x28
    arm64.storeSlotReg slot10, x27
    arm64.storeSlotReg slot9, x26
    arm64.storeSlotReg slot8, x25
    arm64.storeSlotReg slot7, x24
    arm64.storeSlotReg slot6, x23
    arm64.storeSlotReg slot5, x22
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.cbz x0, __il_body#10
  __il_body#4:
    arm64.movImm x19, 0
    arm64.movImm x20, 0
    arm64.b forhdr#5
  each#6:
    arm64.leaRdata x0, __str_rec_3  ; "b"
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x23
    arm64.bl __int_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_4  ; "\x0a"
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
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot1
    arm64.add x19, x19, x0
  forstep#7:
    arm64.loadRegSlot x0, slot1
    arm64.add x0, x0, 1
    arm64.movRegReg x20, x0
  forhdr#5:
    arm64.storeSlotReg slot1, x20
    arm64.cmp x20, 3
    arm64.b.lt each#6
  forexit#8:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_4  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.add x23, x19, x22
    arm64.add x0, x23, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.add x25, x24, 56
    arm64.movRegReg x0, x25
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x25, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x0, x19, x22
    arm64.storeBaseDispReg.word64 [x24 + 0], x25
    arm64.storeBaseDispReg.word64 [x24 + 8], x23
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x24 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.movRegReg x0, x24
    arm64.bl print
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
  __il_body#10:
    arm64.movImm x19, 0
    arm64.movImm x20, 0
    arm64.b forhdr#11
  each#12:
    arm64.leaRdata x0, __str_rec_5  ; "a"
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x23
    arm64.bl __int_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_4  ; "\x0a"
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
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.loadRegSlot x0, slot0
    arm64.add x19, x19, x0
  forstep#13:
    arm64.loadRegSlot x0, slot0
    arm64.add x0, x0, 1
    arm64.movRegReg x20, x0
  forhdr#11:
    arm64.storeSlotReg slot0, x20
    arm64.cmp x20, 2
    arm64.b.lt each#12
  forexit#14:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_4  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x21, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x22, [x0 + 8]
    arm64.add x23, x19, x22
    arm64.add x0, x23, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.add x25, x24, 56
    arm64.movRegReg x0, x25
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x25, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x0, x19, x22
    arm64.storeBaseDispReg.word64 [x24 + 0], x25
    arm64.storeBaseDispReg.word64 [x24 + 8], x23
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x24 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x24 + 24], x0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.movRegReg x0, x24
    arm64.bl print
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
  __il_cont:
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.loadRegSlot x23, slot6
    arm64.loadRegSlot x24, slot7
    arm64.loadRegSlot x25, slot8
    arm64.loadRegSlot x26, slot9
    arm64.loadRegSlot x27, slot10
    arm64.loadRegSlot x28, slot11
    arm64.epilogue 112
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 0
    arm64.bl route
    arm64.movImm x0, 1
    arm64.bl route
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: a-panic-exit-beside-the-site-is-not-a-hot-path-that-avoids-it -->
`run` panics on a negative limit and otherwise calls `tally`. The panic block never returns, so it is no path that
reaches an exit without the call: every path that completes reaches `tally`, which is spliced, and no
`callDirect tally` remains.
```maxon
typealias Integer = int(i64.min to i64.max)

function tally(limit Integer) returns Integer
	var total = 0 as Integer

	for step in 0 upto limit 'each'
		print("{step}\n")
		total = total + step
	end 'each'

	return total
end 'tally'

function run(limit Integer) returns Integer
	if limit < 0 'negative'
		panic("negative limit")
	end 'negative'

	return tally(limit)
end 'run'

function main() returns ExitCode
	let first = run(3)
	let second = run(2)
	print("{first} {second}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
1
2
0
1
3 1
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

func @run {
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
    x64.cmpRegImm32 rbx, 0
    x64.jcc greaterEqual, __il_body
  negative:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-panic-exit-beside-the-site-is-not-a-hot-path-that-avoids-it.test:17: negative limit\x0a"
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
  __il_body:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r12, 0
    x64.jmp forhdr
  each:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rax, r12, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r12
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.loadRegSlot r12, slot4
    x64.storeBaseDispReg.word64 [r12 + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect print
    x64.movRegReg rcx, r12
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rcx, rax
  forstep:
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg r12, rcx
  forhdr:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, r12
    x64.cmpRegReg r12, rbx
    x64.jcc less, each
  __il_cont:
    x64.loadRegSlot r8, slot1
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
    x64.prologue 88
    x64.movRegImm32 rcx, 3
    x64.callDirect run
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 2
    x64.callDirect run
    x64.movRegReg r12, r8
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot0, r13
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __int_to_string
    x64.storeSlotReg slot5, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot3, rax
    x64.leaRegRegReg rcx, rbx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, rbx
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
    x64.loadRegSlot rbx, slot2
    x64.storeBaseDispReg.word64 [rbx + 0], r12
    x64.loadRegSlot rax, slot1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
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

func @run {
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
    x64.cmpRegImm32 rbx, 0
    x64.jcc greaterEqual, __il_body
  negative:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-panic-exit-beside-the-site-is-not-a-hot-path-that-avoids-it.test:17: negative limit\x0a"
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
  __il_body:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r12, 0
    x64.jmp forhdr
  each:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot2, r13
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot5, rax
    x64.leaRegRegReg rax, r12, rax
    x64.storeSlotReg slot3, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot4, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg r12, r15, r12
    x64.loadRegSlot rax, slot5
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot5
    x64.leaRegRegReg rax, r12, rax
    x64.loadRegSlot r12, slot4
    x64.storeBaseDispReg.word64 [r12 + 0], r15
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r12 + 24], rax
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect print
    x64.movRegReg rcx, r12
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rcx, rax
  forstep:
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg r12, rcx
  forhdr:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, r12
    x64.cmpRegReg r12, rbx
    x64.jcc less, each
  __il_cont:
    x64.loadRegSlot r8, slot1
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
    x64.prologue 88
    x64.movRegImm32 rcx, 3
    x64.callDirect run
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 2
    x64.callDirect run
    x64.movRegReg r12, r8
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r13, r8
    x64.storeSlotReg slot0, r13
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_4]  ; " "
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot6, rcx
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect __int_to_string
    x64.storeSlotReg slot5, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot3, rax
    x64.leaRegRegReg rcx, rbx, r14
    x64.leaRegRegReg rcx, rcx, r8
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r12, r8, 56
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r13
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r12, rbx
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
    x64.loadRegSlot rbx, slot2
    x64.storeBaseDispReg.word64 [rbx + 0], r12
    x64.loadRegSlot rax, slot1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r15
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
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

func @run {
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
    arm64.movRegReg x19, x0
    arm64.cmp x19, 0
    arm64.b.ge __il_body
  negative:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-panic-exit-beside-the-site-is-not-a-hot-path-that-avoids-it.test:17: negative limit\x0a"
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
  __il_body:
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b forhdr
  each:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x23, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x23, x28, x23
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x23, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.add x20, x20, x21
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.cmp x21, x19
    arm64.b.lt each
  __il_cont:
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
}

func @main {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot11, x28
    arm64.storeSlotReg slot10, x27
    arm64.storeSlotReg slot9, x26
    arm64.storeSlotReg slot8, x25
    arm64.storeSlotReg slot7, x24
    arm64.storeSlotReg slot6, x23
    arm64.storeSlotReg slot5, x22
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movImm x0, 3
    arm64.bl run
    arm64.movRegReg x19, x0
    arm64.movImm x0, 2
    arm64.bl run
    arm64.movRegReg x20, x0
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot0, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_4  ; " "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl __int_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.add x0, x19, x23
    arm64.add x0, x0, x20
    arm64.add x0, x0, x26
    arm64.storeSlotReg slot1, x0
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
    arm64.loadRegSlot x0, slot1
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.loadRegSlot x0, slot0
    arm64.bl __mm_decref
    arm64.movRegReg x0, x24
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.loadRegSlot x23, slot6
    arm64.loadRegSlot x24, slot7
    arm64.loadRegSlot x25, slot8
    arm64.loadRegSlot x26, slot9
    arm64.loadRegSlot x27, slot10
    arm64.loadRegSlot x28, slot11
    arm64.epilogue 112
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

func @run {
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
    arm64.movRegReg x19, x0
    arm64.cmp x19, 0
    arm64.b.ge __il_body
  negative:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-panic-exit-beside-the-site-is-not-a-hot-path-that-avoids-it.test:17: negative limit\x0a"
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
  __il_body:
    arm64.movImm x20, 0
    arm64.movImm x21, 0
    arm64.b forhdr
  each:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __int_to_string
    arm64.movRegReg x23, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x26, x23, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x23, x28, x23
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x23, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x22
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.add x20, x20, x21
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.cmp x21, x19
    arm64.b.lt each
  __il_cont:
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
}

func @main {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot11, x28
    arm64.storeSlotReg slot10, x27
    arm64.storeSlotReg slot9, x26
    arm64.storeSlotReg slot8, x25
    arm64.storeSlotReg slot7, x24
    arm64.storeSlotReg slot6, x23
    arm64.storeSlotReg slot5, x22
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movImm x0, 3
    arm64.bl run
    arm64.movRegReg x19, x0
    arm64.movImm x0, 2
    arm64.bl run
    arm64.movRegReg x20, x0
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.storeSlotReg slot0, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __int_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_4  ; " "
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl __int_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x25, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x26, [x0 + 8]
    arm64.add x0, x19, x23
    arm64.add x0, x0, x20
    arm64.add x0, x0, x26
    arm64.storeSlotReg slot1, x0
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
    arm64.loadRegSlot x0, slot1
    arm64.storeBaseDispReg.word64 [x27 + 8], x0
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.loadRegSlot x0, slot0
    arm64.bl __mm_decref
    arm64.movRegReg x0, x24
    arm64.bl __mm_decref
    arm64.movRegReg x0, x27
    arm64.bl print
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.loadRegSlot x22, slot5
    arm64.loadRegSlot x23, slot6
    arm64.loadRegSlot x24, slot7
    arm64.loadRegSlot x25, slot8
    arm64.loadRegSlot x26, slot9
    arm64.loadRegSlot x27, slot10
    arm64.loadRegSlot x28, slot11
    arm64.epilogue 112
    arm64.ret
}
```

<!-- test: a-caller-with-no-hot-exit-refuses-nothing -->
`spin` loops forever around the call of `tally`, so it has no path to an exit for the call to be off. Nothing is
refused: `tally` is spliced into `spin` and `spin` into `main`, and the pin holds `main` alone.
```maxon
typealias Integer = int(i64.min to i64.max)

function tally(limit Integer) returns Integer
	var total = 0 as Integer

	for step in 0 upto limit 'each'
		print("{step}\n")
		total = total + step
	end 'each'

	return total
end 'tally'

function spin()
	while true 'forever'
		_ = tally(2)
	end 'forever'
end 'spin'

function main() returns ExitCode
	if CommandLine.args().count() > 100 'never'
		spin()
	end 'never'

	print("idle\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
idle
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
  __il_body#15:
    x64.callDirect __cl_count
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.leaRegFunc rdx, [rip + __str_decref]
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegImm32 rcx, 0
    x64.jmp forhdr#16
  loop:
    x64.callDirect __cl_arg
    x64.storeSlotReg slot5, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_retain
    x64.movRegReg r13, r8
  __il_body#30:
    x64.loadRegBaseDisp.word64 r14, [r13 + 8]
    x64.movRegImm32 r15, 0
    x64.jmp forhdr#31
  scan:
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r15
    x64.callDirect __managed_byte_at
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  tryok:
    x64.cmpRegImm32 r8, 128
    x64.movRegImm32 rax, 1
    x64.jcc greaterEqual, scmerge
  orrhs:
    x64.cmpRegImm32 r8, 13
    x64.setccReg equal, rax
  scmerge:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, forstep#33
  notSingleByte:
    x64.movRegImm32 rdx, 0
    x64.jmp __il_cont#29
  forstep#33:
    x64.leaRegRegImm32 r15, r15, 1
  forhdr#31:
    x64.cmpRegReg r15, r14
    x64.jcc below, scan
  forexit:
    x64.movRegImm32 rdx, 1
  __il_cont#29:
    x64.movRegReg rcx, r13
    x64.callDirect __str_of_buffer
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
  __il_cont#26:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegReg rax, rcx
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rdx, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.storeBaseIndexScaleReg.word64 [rcx + rax*8 + 0], r14
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [r12 + 8], rax
  __im_cont:
    x64.loadRegSlot rcx, slot5
    x64.callDirect __managed_decref
  forstep#18:
    x64.loadRegSlot rax, slot3
    x64.leaRegRegImm32 rcx, rax, 1
  forhdr#16:
    x64.storeSlotReg slot3, rcx
    x64.cmpRegReg rcx, rbx
    x64.jcc below, loop
  __il_cont#14:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegImm32 rax, 100
    x64.setccReg above, rbx
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, ifcont
  __il_body#9:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rbx, 0
    x64.jmp forhdr#10
  each:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot2, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot4, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot6, r8
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
    x64.loadRegSlot rbx, slot6
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot4
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rcx, rax
  forstep#12:
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg rbx, rcx
  forhdr#10:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, rbx
    x64.cmpRegImm32 rbx, 2
    x64.jcc less, each
    x64.jmp __il_body#9
  ifcont:
    x64.leaRegRdata rcx, [rip + __str_rec_3]  ; "idle\x0a"
    x64.callDirect print
    x64.movRegImm32 r8, 0
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __managed_push
    x64.jmp __im_cont
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at String.maxon:1119: scanSingleByteGraphemes: byteAt OOB \xe2\x80\x94 i < len invariant\x0a"
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
  __cl_argc@0 = i64 0
  __cl_argv@8 = i64 0
  __cl_empty_arg@16 = i64 0
  __mrt_envp@24 = i64 0
  __mrt_signal_stack_bytes@32 = i64 0
  __slab_arena_list@40 = i64 0
  __slab_arena_map_l1@48 = i64 0
  __slab_state@56 = i64 0
  __mrt_program_started@64 = i8 0
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
  __il_body#15:
    x64.callDirect __cl_count
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.leaRegFunc rdx, [rip + __str_decref]
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegImm32 rcx, 0
    x64.jmp forhdr#16
  loop:
    x64.callDirect __cl_arg
    x64.storeSlotReg slot5, r8
    x64.movRegReg rcx, r8
    x64.callDirect __managed_retain
    x64.movRegReg r13, r8
  __il_body#30:
    x64.loadRegBaseDisp.word64 r14, [r13 + 8]
    x64.movRegImm32 r15, 0
    x64.jmp forhdr#31
  scan:
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r15
    x64.callDirect __managed_byte_at
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  tryok:
    x64.cmpRegImm32 r8, 128
    x64.movRegImm32 rax, 1
    x64.jcc greaterEqual, scmerge
  orrhs:
    x64.cmpRegImm32 r8, 13
    x64.setccReg equal, rax
  scmerge:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, forstep#33
  notSingleByte:
    x64.movRegImm32 rdx, 0
    x64.jmp __il_cont#29
  forstep#33:
    x64.leaRegRegImm32 r15, r15, 1
  forhdr#31:
    x64.cmpRegReg r15, r14
    x64.jcc below, scan
  forexit:
    x64.movRegImm32 rdx, 1
  __il_cont#29:
    x64.movRegReg rcx, r13
    x64.callDirect __str_of_buffer
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
  __il_cont#26:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 16]
    x64.cmpRegReg rax, rcx
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rdx, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.storeBaseIndexScaleReg.word64 [rcx + rax*8 + 0], r14
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [r12 + 8], rax
  __im_cont:
    x64.loadRegSlot rcx, slot5
    x64.callDirect __managed_decref
  forstep#18:
    x64.loadRegSlot rax, slot3
    x64.leaRegRegImm32 rcx, rax, 1
  forhdr#16:
    x64.storeSlotReg slot3, rcx
    x64.cmpRegReg rcx, rbx
    x64.jcc below, loop
  __il_cont#14:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegImm32 rax, 100
    x64.setccReg above, rbx
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, ifcont
  __il_body#9:
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rbx, 0
    x64.jmp forhdr#10
  each:
    x64.movRegImm32 rcx, 21
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot2, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __int_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_2]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot4, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot6, r8
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
    x64.loadRegSlot rbx, slot6
    x64.storeBaseDispReg.word64 [rbx + 0], r15
    x64.loadRegSlot rax, slot4
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.movRegImm rax, 18446744073709551613
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 24], rax
    x64.loadRegSlot rcx, slot2
    x64.callDirect __mm_decref
    x64.movRegReg rcx, rbx
    x64.callDirect print
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.leaRegRegReg rax, rcx, rax
  forstep#12:
    x64.loadRegSlot rcx, slot0
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg rbx, rcx
  forhdr#10:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, rbx
    x64.cmpRegImm32 rbx, 2
    x64.jcc less, each
    x64.jmp __il_body#9
  ifcont:
    x64.leaRegRdata rcx, [rip + __str_rec_3]  ; "idle\x0a"
    x64.callDirect print
    x64.movRegImm32 r8, 0
    x64.epilogue 88
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __managed_push
    x64.jmp __im_cont
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at String.maxon:1119: scanSingleByteGraphemes: byteAt OOB \xe2\x80\x94 i < len invariant\x0a"
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
  __cl_argc@0 = i64 0
  __cl_argv@8 = i64 0
  __cl_empty_arg@16 = i64 0
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
  __il_body#15:
    arm64.bl __cl_count
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x21, 0
    arm64.b forhdr#16
  loop:
    arm64.movRegReg x0, x21
    arm64.bl __cl_arg
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x22
    arm64.bl __managed_retain
    arm64.movRegReg x23, x0
  __il_body#30:
    arm64.loadRegBaseDisp.word64 x24, [x23 + 8]
    arm64.movImm x25, 0
    arm64.b forhdr#31
  scan:
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x25
    arm64.bl __managed_byte_at
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  tryok:
    arm64.cmp x0, 128
    arm64.movImm x1, 1
    arm64.b.ge critsplit
  orrhs:
    arm64.cmp x0, 13
    arm64.cset x0, eq
    arm64.b scmerge
  critsplit:
    arm64.movRegReg x0, x1
  scmerge:
    arm64.cbz x0, forstep#33
  notSingleByte:
    arm64.movImm x1, 0
    arm64.b __il_cont#29
  forstep#33:
    arm64.add x25, x25, 1
  forhdr#31:
    arm64.cmp x25, x24
    arm64.b.lo scan
  forexit:
    arm64.movImm x1, 1
  __il_cont#29:
    arm64.movRegReg x0, x23
    arm64.bl __str_of_buffer
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
  __il_cont#26:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.loadRegBaseDisp.word64 x1, [x20 + 16]
    arm64.cmp x1, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x1, [x20 + 16]
    arm64.cmp x0, x1
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x1, [x20 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x2, x1, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.storeBaseIndexScaleReg.word64 [x1 + x0*8 + 0], x24
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
  __im_cont:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  forstep#18:
    arm64.add x21, x21, 1
  forhdr#16:
    arm64.cmp x21, x19
    arm64.b.lo loop
  __il_cont#14:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x0, 100
    arm64.cset x19, hi
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.cbz x19, ifcont
  __il_body#9:
    arm64.movImm x19, 0
    arm64.movImm x20, 0
    arm64.b forhdr#10
  each:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.bl __int_to_string
    arm64.movRegReg x22, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.add x25, x22, x24
    arm64.add x0, x25, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x26, x0
    arm64.add x27, x26, 56
    arm64.movRegReg x0, x27
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x22, x27, x22
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.add x0, x22, x24
    arm64.storeBaseDispReg.word64 [x26 + 0], x27
    arm64.storeBaseDispReg.word64 [x26 + 8], x25
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x26 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x26 + 24], x0
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.movRegReg x0, x26
    arm64.bl print
    arm64.movRegReg x0, x26
    arm64.bl __str_decref
    arm64.add x19, x19, x20
  forstep#12:
    arm64.add x20, x20, 1
  forhdr#10:
    arm64.cmp x20, 2
    arm64.b.lt each
    arm64.b __il_body#9
  ifcont:
    arm64.leaRdata x0, __str_rec_3  ; "idle\x0a"
    arm64.bl print
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
  __im_slow:
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl __managed_push
    arm64.b __im_cont
  tryerr:
    arm64.leaRdata x0, __str_blob_4  ; "panic at String.maxon:1119: scanSingleByteGraphemes: byteAt OOB \xe2\x80\x94 i < len invariant\x0a"
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
  __cl_argc@0 = i64 0
  __cl_argv@8 = i64 0
  __cl_empty_arg@16 = i64 0
  __mrt_envp@24 = i64 0
  __mrt_signal_stack_bytes@32 = i64 0
  __mrt_hwcap_atomics@40 = i64 0
  __slab_arena_list@48 = i64 0
  __slab_arena_map_l1@56 = i64 0
  __slab_state@64 = i64 0
  __mrt_program_started@72 = i8 0
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
  __il_body#15:
    arm64.bl __cl_count
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movImm x21, 0
    arm64.b forhdr#16
  loop:
    arm64.movRegReg x0, x21
    arm64.bl __cl_arg
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x22
    arm64.bl __managed_retain
    arm64.movRegReg x23, x0
  __il_body#30:
    arm64.loadRegBaseDisp.word64 x24, [x23 + 8]
    arm64.movImm x25, 0
    arm64.b forhdr#31
  scan:
    arm64.movRegReg x0, x23
    arm64.movRegReg x1, x25
    arm64.bl __managed_byte_at
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  tryok:
    arm64.cmp x0, 128
    arm64.movImm x1, 1
    arm64.b.ge critsplit
  orrhs:
    arm64.cmp x0, 13
    arm64.cset x0, eq
    arm64.b scmerge
  critsplit:
    arm64.movRegReg x0, x1
  scmerge:
    arm64.cbz x0, forstep#33
  notSingleByte:
    arm64.movImm x1, 0
    arm64.b __il_cont#29
  forstep#33:
    arm64.add x25, x25, 1
  forhdr#31:
    arm64.cmp x25, x24
    arm64.b.lo scan
  forexit:
    arm64.movImm x1, 1
  __il_cont#29:
    arm64.movRegReg x0, x23
    arm64.bl __str_of_buffer
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x23
    arm64.bl __managed_decref
  __il_cont#26:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.loadRegBaseDisp.word64 x1, [x20 + 16]
    arm64.cmp x1, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x1, [x20 + 16]
    arm64.cmp x0, x1
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x1, [x20 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x2, x1, 16
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.storeBaseIndexScaleReg.word64 [x1 + x0*8 + 0], x24
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
  __im_cont:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  forstep#18:
    arm64.add x21, x21, 1
  forhdr#16:
    arm64.cmp x21, x19
    arm64.b.lo loop
  __il_cont#14:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x0, 100
    arm64.cset x19, hi
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.cbz x19, ifcont
  __il_body#9:
    arm64.movImm x19, 0
    arm64.movImm x20, 0
    arm64.b forhdr#10
  each:
    arm64.movImm x0, 21
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.bl __int_to_string
    arm64.movRegReg x22, x0
    arm64.leaRdata x0, __str_rec_2  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x23, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x24, [x0 + 8]
    arm64.add x25, x22, x24
    arm64.add x0, x25, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x26, x0
    arm64.add x27, x26, 56
    arm64.movRegReg x0, x27
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x22, x27, x22
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x24
    arm64.bl __str_copy
    arm64.add x0, x22, x24
    arm64.storeBaseDispReg.word64 [x26 + 0], x27
    arm64.storeBaseDispReg.word64 [x26 + 8], x25
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x26 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x26 + 24], x0
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
    arm64.movRegReg x0, x26
    arm64.bl print
    arm64.movRegReg x0, x26
    arm64.bl __str_decref
    arm64.add x19, x19, x20
  forstep#12:
    arm64.add x20, x20, 1
  forhdr#10:
    arm64.cmp x20, 2
    arm64.b.lt each
    arm64.b __il_body#9
  ifcont:
    arm64.leaRdata x0, __str_rec_3  ; "idle\x0a"
    arm64.bl print
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
  __im_slow:
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl __managed_push
    arm64.b __im_cont
  tryerr:
    arm64.leaRdata x0, __str_blob_4  ; "panic at String.maxon:1119: scanSingleByteGraphemes: byteAt OOB \xe2\x80\x94 i < len invariant\x0a"
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
<!-- test: a-small-pair-returning-leaf-is-spliced-into-a-loop -->
`stir` builds a pair of words, has two call sites, and holds no call but the allocation of its two-word box. That box is the pair the value-tuple pass keeps out of memory, so it does not make `stir` a non-leaf: both sites are spliced, the pair flows as two plain values, and the pin holds no `callDirect stir` and no `__mm_alloc` in `main`.
```maxon
typealias Word = bits(64)
typealias WordPair = (Word, Word)

function stir(a Word, b Word) returns WordPair
	return ((a shl 3) xor (b shr 5), (b shl 7) xor (a shr 2))
end 'stir'

function main() returns ExitCode
	var lo = 1 as Word
	var hi = 2 as Word

	for _ in 0 upto 1000 'rounds'
		let (x, y) = stir(lo, b: hi)
		lo = y
		hi = x
	end 'rounds'

	let (p, q) = stir(hi, b: lo)

	return ((p xor q) and 127) as ExitCode
end 'main'
```
```exitcode
93
```
```TargetIr:x64-windows
data {
  __slab_state@0 = i64 0
  __mrt_console_probe_stdin@8 = i8 0
  __mrt_console_probe_stdout@9 = i8 0
  __mrt_console_probe_stderr@10 = i8 0
  __mrt_program_started@11 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rcx, 2
    x64.movRegImm32 rdx, 0
    x64.jmp forhdr
  __il_body#7:
    x64.movRegReg rsi, rax
    x64.shlRegImm8 rsi, rax, 3
    x64.movRegReg rdi, rcx
    x64.shrRegImm8 rdi, rcx, 5
    x64.xorRegReg rsi, rsi, rdi
    x64.shlRegImm8 rcx, rcx, 7
    x64.shrRegImm8 rax, rax, 2
    x64.xorRegReg rcx, rcx, rax
  forstep:
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.movRegReg rax, rcx
    x64.movRegReg rcx, rsi
  forhdr:
    x64.cmpRegImm32 rdx, 1000
    x64.jcc less, __il_body#7
  __il_body#9:
    x64.movRegReg rdx, rcx
    x64.shlRegImm8 rdx, rcx, 3
    x64.movRegReg rsi, rax
    x64.shrRegImm8 rsi, rax, 5
    x64.xorRegReg rdx, rdx, rsi
    x64.shlRegImm8 rax, rax, 7
    x64.shrRegImm8 rcx, rcx, 2
    x64.xorRegReg rax, rax, rcx
  __il_cont:
    x64.xorRegReg rdx, rdx, rax
    x64.movRegReg r8, rdx
    x64.andRegImm32 r8, rdx, 127
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
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rcx, 2
    x64.movRegImm32 rdx, 0
    x64.jmp forhdr
  __il_body#7:
    x64.movRegReg rsi, rax
    x64.shlRegImm8 rsi, rax, 3
    x64.movRegReg rdi, rcx
    x64.shrRegImm8 rdi, rcx, 5
    x64.xorRegReg rsi, rsi, rdi
    x64.shlRegImm8 rcx, rcx, 7
    x64.shrRegImm8 rax, rax, 2
    x64.xorRegReg rcx, rcx, rax
  forstep:
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.movRegReg rax, rcx
    x64.movRegReg rcx, rsi
  forhdr:
    x64.cmpRegImm32 rdx, 1000
    x64.jcc less, __il_body#7
  __il_body#9:
    x64.movRegReg rdx, rcx
    x64.shlRegImm8 rdx, rcx, 3
    x64.movRegReg rsi, rax
    x64.shrRegImm8 rsi, rax, 5
    x64.xorRegReg rdx, rdx, rsi
    x64.shlRegImm8 rax, rax, 7
    x64.shrRegImm8 rcx, rcx, 2
    x64.xorRegReg rax, rax, rcx
  __il_cont:
    x64.xorRegReg rdx, rdx, rax
    x64.movRegReg r8, rdx
    x64.andRegImm32 r8, rdx, 127
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
    arm64.movImm x0, 1
    arm64.movImm x1, 2
    arm64.movImm x2, 0
    arm64.b forhdr
  __il_body#7:
    arm64.lsl x3, x0, 3
    arm64.lsr x4, x1, 5
    arm64.eor x3, x3, x4
    arm64.lsl x1, x1, 7
    arm64.lsr x0, x0, 2
    arm64.eor x0, x1, x0
  forstep:
    arm64.add x2, x2, 1
    arm64.movRegReg x1, x3
  forhdr:
    arm64.cmp x2, 1000
    arm64.b.lt __il_body#7
  __il_body#9:
    arm64.lsl x2, x1, 3
    arm64.lsr x3, x0, 5
    arm64.eor x2, x2, x3
    arm64.lsl x0, x0, 7
    arm64.lsr x1, x1, 2
    arm64.eor x0, x0, x1
  __il_cont:
    arm64.eor x0, x2, x0
    arm64.and x0, x0, 127
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
    arm64.movImm x0, 1
    arm64.movImm x1, 2
    arm64.movImm x2, 0
    arm64.b forhdr
  __il_body#7:
    arm64.lsl x3, x0, 3
    arm64.lsr x4, x1, 5
    arm64.eor x3, x3, x4
    arm64.lsl x1, x1, 7
    arm64.lsr x0, x0, 2
    arm64.eor x0, x1, x0
  forstep:
    arm64.add x2, x2, 1
    arm64.movRegReg x1, x3
  forhdr:
    arm64.cmp x2, 1000
    arm64.b.lt __il_body#7
  __il_body#9:
    arm64.lsl x2, x1, 3
    arm64.lsr x3, x0, 5
    arm64.eor x2, x2, x3
    arm64.lsl x0, x0, 7
    arm64.lsr x1, x1, 2
    arm64.eor x0, x0, x1
  __il_cont:
    arm64.eor x0, x2, x0
    arm64.and x0, x0, 127
    arm64.epilogue 16
    arm64.ret
}
```
<!-- test: a-leaf-whose-body-only-calls-other-leaves-is-spliced -->
`shiftRows` only wraps two leaves and `shiftTwice` only wraps `shiftRows`. Splicing the innermost leaves into `shiftRows` leaves it call-free, so it is a leaf in the next follow-up round and is spliced into `shiftTwice`, which is a leaf one round after. The pin holds no `callDirect` in `main`.
```maxon
typealias Word = bits(64)

function rotateDown(x Word) returns Word
	return (x shr 4) or (x shl 60)
end 'rotateDown'

function swapHalves(x Word) returns Word
	return (x shl 32) or (x shr 32)
end 'swapHalves'

function shiftRows(x Word) returns Word
	return swapHalves(rotateDown(x))
end 'shiftRows'

function shiftTwice(x Word) returns Word
	return shiftRows(shiftRows(x))
end 'shiftTwice'

function main() returns ExitCode
	var acc = 1 as Word

	for _ in 0 upto 1000 'rounds'
		acc = shiftTwice(acc + 3) xor rotateDown(acc) xor swapHalves(acc + 5) xor shiftRows(acc)
	end 'rounds'

	return (acc and 127) as ExitCode
end 'main'
```
```exitcode
74
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
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rcx, 0
    x64.jmp forhdr
  rounds:
    x64.leaRegRegImm32 rdx, rax, 3
  __il_body#16:
    x64.rolRegImm8 rdx, rdx, 60
  __il_body#17:
    x64.rolRegImm8 rdx, rdx, 32
  __il_body#21:
    x64.rolRegImm8 rdx, rdx, 60
  __il_body#22:
    x64.rolRegImm8 rdx, rdx, 32
  __il_body#9:
    x64.movRegReg rsi, rax
    x64.rolRegImm8 rsi, rax, 60
  __il_cont#8:
    x64.xorRegReg rdx, rdx, rsi
    x64.leaRegRegImm32 rax, rax, 5
  __il_body#10:
    x64.rolRegImm8 rax, rax, 32
  __il_cont#7:
    x64.xorRegReg rdx, rdx, rax
  __il_body#28:
    x64.rolRegImm8 rsi, rsi, 32
  __il_cont#23:
    x64.xorRegReg rdx, rdx, rsi
  forstep:
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg rax, rdx
  forhdr:
    x64.cmpRegImm32 rcx, 1000
    x64.jcc less, rounds
  forexit:
    x64.movRegReg r8, rax
    x64.andRegImm32 r8, rax, 127
    x64.popReg rbp
    x64.ret
}
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
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rcx, 0
    x64.jmp forhdr
  rounds:
    x64.leaRegRegImm32 rdx, rax, 3
  __il_body#16:
    x64.rolRegImm8 rdx, rdx, 60
  __il_body#17:
    x64.rolRegImm8 rdx, rdx, 32
  __il_body#21:
    x64.rolRegImm8 rdx, rdx, 60
  __il_body#22:
    x64.rolRegImm8 rdx, rdx, 32
  __il_body#9:
    x64.movRegReg rsi, rax
    x64.rolRegImm8 rsi, rax, 60
  __il_cont#8:
    x64.xorRegReg rdx, rdx, rsi
    x64.leaRegRegImm32 rax, rax, 5
  __il_body#10:
    x64.rolRegImm8 rax, rax, 32
  __il_cont#7:
    x64.xorRegReg rdx, rdx, rax
  __il_body#28:
    x64.rolRegImm8 rsi, rsi, 32
  __il_cont#23:
    x64.xorRegReg rdx, rdx, rsi
  forstep:
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg rax, rdx
  forhdr:
    x64.cmpRegImm32 rcx, 1000
    x64.jcc less, rounds
  forexit:
    x64.movRegReg r8, rax
    x64.andRegImm32 r8, rax, 127
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
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.b forhdr
  rounds:
    arm64.add x2, x0, 3
  __il_body#16:
    arm64.ror x2, x2, 4
  __il_body#17:
    arm64.ror x2, x2, 32
  __il_body#21:
    arm64.ror x2, x2, 4
  __il_body#22:
    arm64.ror x2, x2, 32
  __il_body#9:
    arm64.ror x3, x0, 4
  __il_cont#8:
    arm64.eor x2, x2, x3
    arm64.add x0, x0, 5
  __il_body#10:
    arm64.ror x0, x0, 32
  __il_cont#7:
    arm64.eor x0, x2, x0
  __il_body#28:
    arm64.ror x2, x3, 32
  __il_cont#23:
    arm64.eor x0, x0, x2
  forstep:
    arm64.add x1, x1, 1
  forhdr:
    arm64.cmp x1, 1000
    arm64.b.lt rounds
  forexit:
    arm64.and x0, x0, 127
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
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.b forhdr
  rounds:
    arm64.add x2, x0, 3
  __il_body#16:
    arm64.ror x2, x2, 4
  __il_body#17:
    arm64.ror x2, x2, 32
  __il_body#21:
    arm64.ror x2, x2, 4
  __il_body#22:
    arm64.ror x2, x2, 32
  __il_body#9:
    arm64.ror x3, x0, 4
  __il_cont#8:
    arm64.eor x2, x2, x3
    arm64.add x0, x0, 5
  __il_body#10:
    arm64.ror x0, x0, 32
  __il_cont#7:
    arm64.eor x0, x2, x0
  __il_body#28:
    arm64.ror x2, x3, 32
  __il_cont#23:
    arm64.eor x0, x0, x2
  forstep:
    arm64.add x1, x1, 1
  forhdr:
    arm64.cmp x1, 1000
    arm64.b.lt rounds
  forexit:
    arm64.and x0, x0, 127
    arm64.epilogue 16
    arm64.ret
}
```
<!-- test: a-pair-leaf-with-two-returns-keeps-its-register-convention -->
`pick` builds its pair on two exits. Spliced, the two boxes would meet at the continuation's block argument, which `ValueTupleReturn` cannot take, and `main` would be left with an allocation per site. The leaf rule admits a pair's box only from a leaf that returns at one exit, so `pick` stays a call, returns its pair in the two return registers, and the pin holds no `__mm_alloc` in `main`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntegerPair = (Integer, Integer)

function pick(flag Integer, a Integer, b Integer) returns IntegerPair
	if flag > 0 'first'
		return (a + 1, b)
	end 'first'

	return (b, a + 2)
end 'pick'

function main() returns ExitCode
	var lo = 1 as Integer
	var hi = 2 as Integer

	for step in 0 upto 100 'rounds'
		let (x, y) = pick(step - 50, a: lo, b: hi)
		lo = y
		hi = x
	end 'rounds'

	let (p, q) = pick(lo, a: hi, b: lo)

	return ((p + q) and 127) as ExitCode
end 'main'
```
```exitcode
27
```
```TargetIr:x64-windows
data {
  __slab_state@0 = i64 0
  __mrt_console_probe_stdin@8 = i8 0
  __mrt_console_probe_stdout@9 = i8 0
  __mrt_console_probe_stderr@10 = i8 0
  __mrt_program_started@11 = i8 0
}

func @pick {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.cmpRegImm32 rcx, 0
    x64.jcc lessEqual, ifcont
  first:
    x64.leaRegRegImm32 r8, rdx, 1
    x64.movRegReg r10, rax
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.leaRegRegImm32 r10, rdx, 2
    x64.movRegReg r8, rax
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rsi, 2
    x64.movRegImm32 rbx, 0
    x64.jmp forhdr
  rounds:
    x64.leaRegRegImm32 rcx, rbx, -50
    x64.movRegReg rdx, rax
    x64.movRegReg rax, rsi
    x64.callDirect pick
  forstep:
    x64.leaRegRegImm32 rbx, rbx, 1
    x64.movRegReg rax, r10
    x64.movRegReg rsi, r8
  forhdr:
    x64.cmpRegImm32 rbx, 100
    x64.jcc less, rounds
  forexit:
    x64.movRegReg rcx, rax
    x64.movRegReg rdx, rsi
    x64.callDirect pick
    x64.leaRegRegReg r8, r8, r10
    x64.andRegImm32 r8, r8, 127
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
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @pick {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.cmpRegImm32 rcx, 0
    x64.jcc lessEqual, ifcont
  first:
    x64.leaRegRegImm32 r8, rdx, 1
    x64.movRegReg r10, rax
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.leaRegRegImm32 r10, rdx, 2
    x64.movRegReg r8, rax
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rsi, 2
    x64.movRegImm32 rbx, 0
    x64.jmp forhdr
  rounds:
    x64.leaRegRegImm32 rcx, rbx, -50
    x64.movRegReg rdx, rax
    x64.movRegReg rax, rsi
    x64.callDirect pick
  forstep:
    x64.leaRegRegImm32 rbx, rbx, 1
    x64.movRegReg rax, r10
    x64.movRegReg rsi, r8
  forhdr:
    x64.cmpRegImm32 rbx, 100
    x64.jcc less, rounds
  forexit:
    x64.movRegReg rcx, rax
    x64.movRegReg rdx, rsi
    x64.callDirect pick
    x64.leaRegRegReg r8, r8, r10
    x64.andRegImm32 r8, r8, 127
    x64.epilogue 40
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

func @pick {
  entry:
    arm64.prologue 16
    arm64.cmp x0, 0
    arm64.b.le ifcont
  first:
    arm64.add x0, x1, 1
    arm64.movRegReg x9, x2
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.add x9, x1, 2
    arm64.movRegReg x0, x2
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 1
    arm64.movImm x3, 2
    arm64.movImm x19, 0
    arm64.b forhdr
  rounds:
    arm64.sub x0, x19, 50
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.bl pick
  forstep:
    arm64.add x19, x19, 1
    arm64.movRegReg x2, x9
    arm64.movRegReg x3, x0
  forhdr:
    arm64.cmp x19, 100
    arm64.b.lt rounds
  forexit:
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x3
    arm64.bl pick
    arm64.add x0, x0, x9
    arm64.and x0, x0, 127
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
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

func @pick {
  entry:
    arm64.prologue 16
    arm64.cmp x0, 0
    arm64.b.le ifcont
  first:
    arm64.add x0, x1, 1
    arm64.movRegReg x9, x2
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.add x9, x1, 2
    arm64.movRegReg x0, x2
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movImm x2, 1
    arm64.movImm x3, 2
    arm64.movImm x19, 0
    arm64.b forhdr
  rounds:
    arm64.sub x0, x19, 50
    arm64.movRegReg x1, x2
    arm64.movRegReg x2, x3
    arm64.bl pick
  forstep:
    arm64.add x19, x19, 1
    arm64.movRegReg x2, x9
    arm64.movRegReg x3, x0
  forhdr:
    arm64.cmp x19, 100
    arm64.b.lt rounds
  forexit:
    arm64.movRegReg x0, x2
    arm64.movRegReg x1, x3
    arm64.bl pick
    arm64.add x0, x0, x9
    arm64.and x0, x0, 127
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}
```

<!-- test: a-leaf-composed-of-two-spliced-leaves-is-spliced-into-a-loop -->
`shiftRows` is `swapTopBytes(rotateNibblesDown(w))`, and each of the two helpers is a leaf well inside the budget. Splicing both into `shiftRows` leaves it call-free, and the body it then holds measures what the same arithmetic written flat in one function measures: the jumps between the three spliced pieces are fall-throughs, not code. So the follow-up round takes `shiftRows` for a leaf and splices it into both sites in the loop, and the pin holds no `callDirect` and no `shiftRows` function.
```maxon
typealias Word = bits(64)

let nibbleKeep = 0x0000FFFF0000FFFF
let nibbleDown = 0xFFF00000FFF00000 as Word
let nibbleWrap = 0x000F0000000F0000
let topBytes = 0x00FF00FF00000000

function rotateNibblesDown(w Word) returns Word
	return (w and nibbleKeep) or ((w and nibbleDown) shr 4) or ((w and nibbleWrap) shl 12)
end 'rotateNibblesDown'

function swapTopBytes(w Word) returns Word
	let moved = (w xor (w shr 8)) and topBytes

	return w xor moved xor (moved shl 8)
end 'swapTopBytes'

function shiftRows(w Word) returns Word
	return swapTopBytes(rotateNibblesDown(w))
end 'shiftRows'

function main() returns ExitCode
	var acc = 1 as Word

	for _ in 0 upto 1000 'rounds'
		acc = shiftRows(acc) xor shiftRows(acc + 1)
	end 'rounds'

	return (acc and 127) as ExitCode
end 'main'
```
```exitcode
127
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
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rcx, 0
    x64.jmp forhdr
  __il_body#10:
    x64.movRegImm rdx, 281470681808895
    x64.movRegReg rsi, rax
    x64.andRegReg rsi, rax, rdx
    x64.movRegImm rdx, 18442240478376099840
    x64.movRegReg rdi, rax
    x64.andRegReg rdi, rax, rdx
    x64.shrRegImm8 rdi, rdi, 4
    x64.orRegReg rsi, rsi, rdi
    x64.movRegImm rdx, 4222124651642880
    x64.movRegReg rdi, rax
    x64.andRegReg rdi, rax, rdx
    x64.shlRegImm8 rdi, rdi, 12
    x64.orRegReg rsi, rsi, rdi
  __il_body#11:
    x64.movRegReg rdx, rsi
    x64.shrRegImm8 rdx, rsi, 8
    x64.xorRegReg rdx, rdx, rsi
    x64.movRegImm rdi, 71777214277877760
    x64.andRegReg rdx, rdx, rdi
    x64.xorRegReg rsi, rsi, rdx
    x64.shlRegImm8 rdx, rdx, 8
    x64.xorRegReg rsi, rsi, rdx
  __il_cont#6:
    x64.leaRegRegImm32 rax, rax, 1
  __il_body#15:
    x64.movRegImm rdx, 281470681808895
    x64.movRegReg rdi, rax
    x64.andRegReg rdi, rax, rdx
    x64.movRegImm rdx, 18442240478376099840
    x64.movRegReg r8, rax
    x64.andRegReg r8, rax, rdx
    x64.shrRegImm8 r8, r8, 4
    x64.orRegReg rdi, rdi, r8
    x64.movRegImm rdx, 4222124651642880
    x64.andRegReg rax, rax, rdx
    x64.shlRegImm8 rax, rax, 12
    x64.orRegReg rdi, rdi, rax
  __il_body#16:
    x64.movRegReg rax, rdi
    x64.shrRegImm8 rax, rdi, 8
    x64.xorRegReg rax, rax, rdi
    x64.movRegImm rdx, 71777214277877760
    x64.andRegReg rax, rax, rdx
    x64.xorRegReg rdi, rdi, rax
    x64.shlRegImm8 rax, rax, 8
    x64.xorRegReg rdi, rdi, rax
  __il_cont#5:
    x64.xorRegReg rsi, rsi, rdi
  forstep:
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg rax, rsi
  forhdr:
    x64.cmpRegImm32 rcx, 1000
    x64.jcc less, __il_body#10
  forexit:
    x64.movRegReg r8, rax
    x64.andRegImm32 r8, rax, 127
    x64.popReg rbp
    x64.ret
}
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
    x64.movRegImm32 rax, 1
    x64.movRegImm32 rcx, 0
    x64.jmp forhdr
  __il_body#10:
    x64.movRegImm rdx, 281470681808895
    x64.movRegReg rsi, rax
    x64.andRegReg rsi, rax, rdx
    x64.movRegImm rdx, 18442240478376099840
    x64.movRegReg rdi, rax
    x64.andRegReg rdi, rax, rdx
    x64.shrRegImm8 rdi, rdi, 4
    x64.orRegReg rsi, rsi, rdi
    x64.movRegImm rdx, 4222124651642880
    x64.movRegReg rdi, rax
    x64.andRegReg rdi, rax, rdx
    x64.shlRegImm8 rdi, rdi, 12
    x64.orRegReg rsi, rsi, rdi
  __il_body#11:
    x64.movRegReg rdx, rsi
    x64.shrRegImm8 rdx, rsi, 8
    x64.xorRegReg rdx, rdx, rsi
    x64.movRegImm rdi, 71777214277877760
    x64.andRegReg rdx, rdx, rdi
    x64.xorRegReg rsi, rsi, rdx
    x64.shlRegImm8 rdx, rdx, 8
    x64.xorRegReg rsi, rsi, rdx
  __il_cont#6:
    x64.leaRegRegImm32 rax, rax, 1
  __il_body#15:
    x64.movRegImm rdx, 281470681808895
    x64.movRegReg rdi, rax
    x64.andRegReg rdi, rax, rdx
    x64.movRegImm rdx, 18442240478376099840
    x64.movRegReg r8, rax
    x64.andRegReg r8, rax, rdx
    x64.shrRegImm8 r8, r8, 4
    x64.orRegReg rdi, rdi, r8
    x64.movRegImm rdx, 4222124651642880
    x64.andRegReg rax, rax, rdx
    x64.shlRegImm8 rax, rax, 12
    x64.orRegReg rdi, rdi, rax
  __il_body#16:
    x64.movRegReg rax, rdi
    x64.shrRegImm8 rax, rdi, 8
    x64.xorRegReg rax, rax, rdi
    x64.movRegImm rdx, 71777214277877760
    x64.andRegReg rax, rax, rdx
    x64.xorRegReg rdi, rdi, rax
    x64.shlRegImm8 rax, rax, 8
    x64.xorRegReg rdi, rdi, rax
  __il_cont#5:
    x64.xorRegReg rsi, rsi, rdi
  forstep:
    x64.leaRegRegImm32 rcx, rcx, 1
    x64.movRegReg rax, rsi
  forhdr:
    x64.cmpRegImm32 rcx, 1000
    x64.jcc less, __il_body#10
  forexit:
    x64.movRegReg r8, rax
    x64.andRegImm32 r8, rax, 127
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
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.b forhdr
  __il_body#10:
    arm64.movImm x2, 281470681808895
    arm64.and x2, x0, x2
    arm64.movImm x3, 18442240478376099840
    arm64.and x3, x0, x3
    arm64.lsr x3, x3, 4
    arm64.orr x2, x2, x3
    arm64.movImm x3, 4222124651642880
    arm64.and x3, x0, x3
    arm64.lsl x3, x3, 12
    arm64.orr x2, x2, x3
  __il_body#11:
    arm64.lsr x3, x2, 8
    arm64.eor x3, x2, x3
    arm64.movImm x4, 71777214277877760
    arm64.and x3, x3, x4
    arm64.eor x2, x2, x3
    arm64.lsl x3, x3, 8
    arm64.eor x2, x2, x3
  __il_cont#6:
    arm64.add x0, x0, 1
  __il_body#15:
    arm64.movImm x3, 281470681808895
    arm64.and x3, x0, x3
    arm64.movImm x4, 18442240478376099840
    arm64.and x4, x0, x4
    arm64.lsr x4, x4, 4
    arm64.orr x3, x3, x4
    arm64.movImm x4, 4222124651642880
    arm64.and x0, x0, x4
    arm64.lsl x0, x0, 12
    arm64.orr x0, x3, x0
  __il_body#16:
    arm64.lsr x3, x0, 8
    arm64.eor x3, x0, x3
    arm64.movImm x4, 71777214277877760
    arm64.and x3, x3, x4
    arm64.eor x0, x0, x3
    arm64.lsl x3, x3, 8
    arm64.eor x0, x0, x3
  __il_cont#5:
    arm64.eor x0, x2, x0
  forstep:
    arm64.add x1, x1, 1
  forhdr:
    arm64.cmp x1, 1000
    arm64.b.lt __il_body#10
  forexit:
    arm64.and x0, x0, 127
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
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.b forhdr
  __il_body#10:
    arm64.movImm x2, 281470681808895
    arm64.and x2, x0, x2
    arm64.movImm x3, 18442240478376099840
    arm64.and x3, x0, x3
    arm64.lsr x3, x3, 4
    arm64.orr x2, x2, x3
    arm64.movImm x3, 4222124651642880
    arm64.and x3, x0, x3
    arm64.lsl x3, x3, 12
    arm64.orr x2, x2, x3
  __il_body#11:
    arm64.lsr x3, x2, 8
    arm64.eor x3, x2, x3
    arm64.movImm x4, 71777214277877760
    arm64.and x3, x3, x4
    arm64.eor x2, x2, x3
    arm64.lsl x3, x3, 8
    arm64.eor x2, x2, x3
  __il_cont#6:
    arm64.add x0, x0, 1
  __il_body#15:
    arm64.movImm x3, 281470681808895
    arm64.and x3, x0, x3
    arm64.movImm x4, 18442240478376099840
    arm64.and x4, x0, x4
    arm64.lsr x4, x4, 4
    arm64.orr x3, x3, x4
    arm64.movImm x4, 4222124651642880
    arm64.and x0, x0, x4
    arm64.lsl x0, x0, 12
    arm64.orr x0, x3, x0
  __il_body#16:
    arm64.lsr x3, x0, 8
    arm64.eor x3, x0, x3
    arm64.movImm x4, 71777214277877760
    arm64.and x3, x3, x4
    arm64.eor x0, x0, x3
    arm64.lsl x3, x3, 8
    arm64.eor x0, x0, x3
  __il_cont#5:
    arm64.eor x0, x2, x0
  forstep:
    arm64.add x1, x1, 1
  forhdr:
    arm64.cmp x1, 1000
    arm64.b.lt __il_body#10
  forexit:
    arm64.and x0, x0, 127
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: a-wrapper-over-a-standing-call-is-spliced-so-the-calls-land-in-the-caller -->
`twin` is two calls to `churn` and a pair, and `churn` is far past the leaf budget, so `churn` stays a call and `twin` is no leaf. `twin` holds nothing but those calls: splicing it replaces the one call to `twin` with the two calls to `churn` it makes, and removes a frame. Both sites of `twin` are spliced and the pair never becomes a record. The pin holds four `callDirect churn` in `main`, no `callDirect twin` and no `twin` function.
```maxon
typealias Word = bits(64)
typealias WordPair = (Word, Word)

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
	return (churn(a), churn(b shr 8))
end 'twin'

function main() returns ExitCode
	var lo = 1 as Word
	var hi = 2 as Word

	for _ in 0 upto 1000 'rounds'
		let (x, y) = twin(lo, b: hi)
		lo = y
		hi = x
	end 'rounds'

	let (p, q) = twin(hi, b: lo)

	return ((p xor q) and 127) as ExitCode
end 'main'
```
```exitcode
72
```
```TargetIr:x64-windows
data {
  __slab_state@0 = i64 0
  __mrt_console_probe_stdin@8 = i8 0
  __mrt_console_probe_stdout@9 = i8 0
  __mrt_console_probe_stderr@10 = i8 0
  __mrt_program_started@11 = i8 0
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
  __il_body#7:
    x64.movRegReg rcx, rbx
    x64.callDirect churn
    x64.movRegReg rbx, r8
    x64.movRegReg rcx, r12
    x64.shrRegImm8 rcx, r12, 8
    x64.callDirect churn
  forstep:
    x64.leaRegRegImm32 r13, r13, 1
    x64.movRegReg r12, rbx
    x64.movRegReg rbx, r8
  forhdr:
    x64.cmpRegImm32 r13, 1000
    x64.jcc less, __il_body#7
  __il_body#9:
    x64.movRegReg rcx, r12
    x64.callDirect churn
    x64.movRegReg r12, r8
    x64.movRegReg rcx, rbx
    x64.shrRegImm8 rcx, rbx, 8
    x64.callDirect churn
  __il_cont:
    x64.xorRegReg r12, r12, r8
    x64.movRegReg r8, r12
    x64.andRegImm32 r8, r12, 127
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
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
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
  __il_body#7:
    x64.movRegReg rcx, rbx
    x64.callDirect churn
    x64.movRegReg rbx, r8
    x64.movRegReg rcx, r12
    x64.shrRegImm8 rcx, r12, 8
    x64.callDirect churn
  forstep:
    x64.leaRegRegImm32 r13, r13, 1
    x64.movRegReg r12, rbx
    x64.movRegReg rbx, r8
  forhdr:
    x64.cmpRegImm32 r13, 1000
    x64.jcc less, __il_body#7
  __il_body#9:
    x64.movRegReg rcx, r12
    x64.callDirect churn
    x64.movRegReg r12, r8
    x64.movRegReg rcx, rbx
    x64.shrRegImm8 rcx, rbx, 8
    x64.callDirect churn
  __il_cont:
    x64.xorRegReg r12, r12, r8
    x64.movRegReg r8, r12
    x64.andRegImm32 r8, r12, 127
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
  __slab_state@0 = i64 0
  __mrt_program_started@8 = i8 0
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
  __il_body#7:
    arm64.movRegReg x0, x19
    arm64.bl churn
    arm64.movRegReg x19, x0
    arm64.lsr x0, x20, 8
    arm64.bl churn
  forstep:
    arm64.add x21, x21, 1
    arm64.movRegReg x20, x19
    arm64.movRegReg x19, x0
  forhdr:
    arm64.cmp x21, 1000
    arm64.b.lt __il_body#7
  __il_body#9:
    arm64.movRegReg x0, x20
    arm64.bl churn
    arm64.movRegReg x20, x0
    arm64.lsr x0, x19, 8
    arm64.bl churn
  __il_cont:
    arm64.eor x0, x20, x0
    arm64.and x0, x0, 127
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
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
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
  __il_body#7:
    arm64.movRegReg x0, x19
    arm64.bl churn
    arm64.movRegReg x19, x0
    arm64.lsr x0, x20, 8
    arm64.bl churn
  forstep:
    arm64.add x21, x21, 1
    arm64.movRegReg x20, x19
    arm64.movRegReg x19, x0
  forhdr:
    arm64.cmp x21, 1000
    arm64.b.lt __il_body#7
  __il_body#9:
    arm64.movRegReg x0, x20
    arm64.bl churn
    arm64.movRegReg x20, x0
    arm64.lsr x0, x19, 8
    arm64.bl churn
  __il_cont:
    arm64.eor x0, x20, x0
    arm64.and x0, x0, 127
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: a-wrapper-stays-a-call-where-splicing-would-widen-a-lean-callers-frame -->
`mix` is a wrapper of the case above: two calls to `churn`, the first result kept in a register across the second. `pickA` and `pickB` keep nothing across their one call and reach it only on the path that does not return early. Splicing `mix` there would make each caller save callee registers on every path, including the quick one, so on the lanes that allocate registers the site stays a call (`widensCallerFrame`). The pin holds `callDirect mix` in `pickA` and in `pickB`.
```maxon
typealias Word = bits(64)

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

function mix(a Word, b Word) returns Word
	return churn(a) xor churn(b shr 8)
end 'mix'

function pickA(flag Word, a Word, b Word) returns Word
	if flag == 0 'quick'
		return a xor b
	end 'quick'

	return mix(a, b: b) + 1
end 'pickA'

function pickB(flag Word, a Word, b Word) returns Word
	if flag == 0 'quick'
		return a + b
	end 'quick'

	return mix(b, b: a) + 3
end 'pickB'

function main() returns ExitCode
	var acc = 1 as Word
	var flag = 0 as Word

	for _ in 0 upto 100 'rounds'
		flag = (flag + 1) and 3
		acc = pickA(flag, a: acc, b: 7) + pickB(flag, a: acc, b: 9)
		acc = pickA(flag, a: acc, b: 11) + pickB(flag, a: acc, b: 13)
	end 'rounds'

	return (acc and 127) as ExitCode
end 'main'
```
```exitcode
88
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
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

func @mix {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.movRegReg rbx, rdx
    x64.callDirect churn
    x64.movRegReg r12, r8
    x64.movRegReg rcx, rbx
    x64.shrRegImm8 rcx, rbx, 8
    x64.callDirect churn
    x64.xorRegReg r12, r12, r8
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @pickA {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, ifcont
  quick:
    x64.movRegReg r8, rdx
    x64.xorRegReg r8, rdx, rax
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegReg rcx, rdx
    x64.movRegReg rdx, rax
    x64.callDirect mix
    x64.leaRegRegImm32 r8, r8, 1
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @pickB {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, ifcont
  quick:
    x64.leaRegRegReg r8, rdx, rax
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegReg rcx, rax
    x64.callDirect mix
    x64.leaRegRegImm32 r8, r8, 3
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
    x64.pushReg r14
    x64.prologue 32
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r12, 0
    x64.jmp forhdr
  rounds:
    x64.leaRegRegImm32 r13, rax, 1
    x64.andRegImm32 r13, r13, 3
    x64.movRegImm32 rax, 7
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, rbx
    x64.callDirect pickA
    x64.movRegReg r14, r8
    x64.movRegImm32 rax, 9
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, rbx
    x64.callDirect pickB
    x64.leaRegRegReg rbx, r14, r8
    x64.movRegImm32 rax, 11
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, rbx
    x64.callDirect pickA
    x64.movRegReg r14, r8
    x64.movRegImm32 rax, 13
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, rbx
    x64.callDirect pickB
    x64.leaRegRegReg rax, r14, r8
  forstep:
    x64.leaRegRegImm32 r12, r12, 1
    x64.movRegReg rbx, rax
    x64.movRegReg rax, r13
  forhdr:
    x64.cmpRegImm32 r12, 100
    x64.jcc less, rounds
  forexit:
    x64.movRegReg r8, rbx
    x64.andRegImm32 r8, rbx, 127
    x64.epilogue 32
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
  __mrt_program_started@16 = i8 0
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

func @mix {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.movRegReg rbx, rdx
    x64.callDirect churn
    x64.movRegReg r12, r8
    x64.movRegReg rcx, rbx
    x64.shrRegImm8 rcx, rbx, 8
    x64.callDirect churn
    x64.xorRegReg r12, r12, r8
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @pickA {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, ifcont
  quick:
    x64.movRegReg r8, rdx
    x64.xorRegReg r8, rdx, rax
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegReg rcx, rdx
    x64.movRegReg rdx, rax
    x64.callDirect mix
    x64.leaRegRegImm32 r8, r8, 1
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @pickB {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, ifcont
  quick:
    x64.leaRegRegReg r8, rdx, rax
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegReg rcx, rax
    x64.callDirect mix
    x64.leaRegRegImm32 r8, r8, 3
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
    x64.pushReg r14
    x64.prologue 32
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r12, 0
    x64.jmp forhdr
  rounds:
    x64.leaRegRegImm32 r13, rax, 1
    x64.andRegImm32 r13, r13, 3
    x64.movRegImm32 rax, 7
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, rbx
    x64.callDirect pickA
    x64.movRegReg r14, r8
    x64.movRegImm32 rax, 9
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, rbx
    x64.callDirect pickB
    x64.leaRegRegReg rbx, r14, r8
    x64.movRegImm32 rax, 11
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, rbx
    x64.callDirect pickA
    x64.movRegReg r14, r8
    x64.movRegImm32 rax, 13
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, rbx
    x64.callDirect pickB
    x64.leaRegRegReg rax, r14, r8
  forstep:
    x64.leaRegRegImm32 r12, r12, 1
    x64.movRegReg rbx, rax
    x64.movRegReg rax, r13
  forhdr:
    x64.cmpRegImm32 r12, 100
    x64.jcc less, rounds
  forexit:
    x64.movRegReg r8, rbx
    x64.andRegImm32 r8, rbx, 127
    x64.epilogue 32
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
  __mrt_program_started@0 = i8 0
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

func @mix {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x1
    arm64.bl churn
    arm64.movRegReg x20, x0
    arm64.lsr x0, x19, 8
    arm64.bl churn
    arm64.eor x0, x20, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

func @pickA {
  entry:
    arm64.prologue 16
    arm64.cmp x0, 0
    arm64.b.ne ifcont
  quick:
    arm64.eor x0, x1, x2
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x2
    arm64.bl mix
    arm64.add x0, x0, 1
    arm64.epilogue 16
    arm64.ret
}

func @pickB {
  entry:
    arm64.prologue 16
    arm64.cmp x0, 0
    arm64.b.ne ifcont
  quick:
    arm64.add x0, x1, x2
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movRegReg x0, x2
    arm64.bl mix
    arm64.add x0, x0, 3
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 1
    arm64.movImm x0, 0
    arm64.movImm x20, 0
    arm64.b forhdr
  rounds:
    arm64.add x0, x0, 1
    arm64.and x21, x0, 3
    arm64.movImm x2, 7
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x19
    arm64.bl pickA
    arm64.movRegReg x22, x0
    arm64.movImm x2, 9
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x19
    arm64.bl pickB
    arm64.add x19, x22, x0
    arm64.movImm x2, 11
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x19
    arm64.bl pickA
    arm64.movRegReg x22, x0
    arm64.movImm x2, 13
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x19
    arm64.bl pickB
    arm64.add x0, x22, x0
  forstep:
    arm64.add x20, x20, 1
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x21
  forhdr:
    arm64.cmp x20, 100
    arm64.b.lt rounds
  forexit:
    arm64.and x0, x19, 127
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
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

func @mix {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x1
    arm64.bl churn
    arm64.movRegReg x20, x0
    arm64.lsr x0, x19, 8
    arm64.bl churn
    arm64.eor x0, x20, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

func @pickA {
  entry:
    arm64.prologue 16
    arm64.cmp x0, 0
    arm64.b.ne ifcont
  quick:
    arm64.eor x0, x1, x2
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movRegReg x0, x1
    arm64.movRegReg x1, x2
    arm64.bl mix
    arm64.add x0, x0, 1
    arm64.epilogue 16
    arm64.ret
}

func @pickB {
  entry:
    arm64.prologue 16
    arm64.cmp x0, 0
    arm64.b.ne ifcont
  quick:
    arm64.add x0, x1, x2
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movRegReg x0, x2
    arm64.bl mix
    arm64.add x0, x0, 3
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 1
    arm64.movImm x0, 0
    arm64.movImm x20, 0
    arm64.b forhdr
  rounds:
    arm64.add x0, x0, 1
    arm64.and x21, x0, 3
    arm64.movImm x2, 7
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x19
    arm64.bl pickA
    arm64.movRegReg x22, x0
    arm64.movImm x2, 9
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x19
    arm64.bl pickB
    arm64.add x19, x22, x0
    arm64.movImm x2, 11
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x19
    arm64.bl pickA
    arm64.movRegReg x22, x0
    arm64.movImm x2, 13
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x19
    arm64.bl pickB
    arm64.add x0, x22, x0
  forstep:
    arm64.add x20, x20, 1
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x21
  forhdr:
    arm64.cmp x20, 100
    arm64.b.lt rounds
  forexit:
    arm64.and x0, x19, 127
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: a-panic-in-the-standing-callee-of-a-spliced-wrapper-names-every-frame -->
`churn` is far past the leaf budget and stays a call; `both` is two calls to it and is a wrapper the compiler splices into `main`'s two sites. The pin shows `main` calling `churn` directly and no `callDirect both`; the panic fires inside `churn`, called from the copied wrapper body, and the trace still names `churn`, `both` and `main`, exactly as the calls did.
```maxon
typealias Word = bits(64)

function churn(x Word) returns Word
	if x == 0 'zero'
		panic("zero")
	end 'zero'

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

function both(a Word, b Word) returns Word
	return churn(a) xor churn(b)
end 'both'

function main() returns ExitCode
	var seed = 3 as Word
	var low = 1 as Word

	for _ in 0 upto 3 'rounds'
		seed = both(seed, b: low)
		low = low - 1
	end 'rounds'

	let last = both(seed, b: low)

	return (last and 127) as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at a-panic-in-the-standing-callee-of-a-spliced-wrapper-names-every-frame.test:6: zero
Stack trace:
  in churn
  in both
  in main
  in mrt_start
```
```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @churn {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, ifcont
  zero:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-panic-in-the-standing-callee-of-a-spliced-wrapper-names-every-frame.test:6: zero\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
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
    x64.movRegImm32 rcx, 3
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 r12, 0
    x64.jmp forhdr
  __il_body#6:
    x64.callDirect churn
    x64.movRegReg r13, r8
    x64.movRegReg rcx, rbx
    x64.callDirect churn
    x64.xorRegReg r13, r13, r8
  __il_cont#5:
    x64.leaRegRegImm32 rbx, rbx, -1
  forstep:
    x64.leaRegRegImm32 r12, r12, 1
    x64.movRegReg rcx, r13
  forhdr:
    x64.cmpRegImm32 r12, 3
    x64.jcc less, __il_body#6
  __il_body#8:
    x64.callDirect churn
    x64.movRegReg r12, r8
    x64.movRegReg rcx, rbx
    x64.callDirect churn
    x64.xorRegReg r12, r12, r8
  __il_cont#7:
    x64.movRegReg r8, r12
    x64.andRegImm32 r8, r12, 127
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
  __mrt_program_started@16 = i8 0
}

func @churn {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, ifcont
  zero:
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at a-panic-in-the-standing-callee-of-a-spliced-wrapper-names-every-frame.test:6: zero\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  ifcont:
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
    x64.movRegImm32 rcx, 3
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 r12, 0
    x64.jmp forhdr
  __il_body#6:
    x64.callDirect churn
    x64.movRegReg r13, r8
    x64.movRegReg rcx, rbx
    x64.callDirect churn
    x64.xorRegReg r13, r13, r8
  __il_cont#5:
    x64.leaRegRegImm32 rbx, rbx, -1
  forstep:
    x64.leaRegRegImm32 r12, r12, 1
    x64.movRegReg rcx, r13
  forhdr:
    x64.cmpRegImm32 r12, 3
    x64.jcc less, __il_body#6
  __il_body#8:
    x64.callDirect churn
    x64.movRegReg r12, r8
    x64.movRegReg rcx, rbx
    x64.callDirect churn
    x64.xorRegReg r12, r12, r8
  __il_cont#7:
    x64.movRegReg r8, r12
    x64.andRegImm32 r8, r12, 127
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
  __mrt_program_started@0 = i8 0
}

func @churn {
  entry:
    arm64.prologue 16
    arm64.cmp x0, 0
    arm64.b.ne ifcont
  zero:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-panic-in-the-standing-callee-of-a-spliced-wrapper-names-every-frame.test:6: zero\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  ifcont:
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

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 3
    arm64.movImm x19, 1
    arm64.movImm x20, 0
    arm64.b forhdr
  __il_body#6:
    arm64.bl churn
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x19
    arm64.bl churn
    arm64.eor x0, x21, x0
  __il_cont#5:
    arm64.sub x19, x19, 1
  forstep:
    arm64.add x20, x20, 1
  forhdr:
    arm64.cmp x20, 3
    arm64.b.lt __il_body#6
  __il_body#8:
    arm64.bl churn
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl churn
    arm64.eor x0, x20, x0
  __il_cont#7:
    arm64.and x0, x0, 127
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
  __mrt_program_started@24 = i8 0
}

func @churn {
  entry:
    arm64.prologue 16
    arm64.cmp x0, 0
    arm64.b.ne ifcont
  zero:
    arm64.leaRdata x0, __str_blob_0  ; "panic at a-panic-in-the-standing-callee-of-a-spliced-wrapper-names-every-frame.test:6: zero\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
  ifcont:
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

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 3
    arm64.movImm x19, 1
    arm64.movImm x20, 0
    arm64.b forhdr
  __il_body#6:
    arm64.bl churn
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x19
    arm64.bl churn
    arm64.eor x0, x21, x0
  __il_cont#5:
    arm64.sub x19, x19, 1
  forstep:
    arm64.add x20, x20, 1
  forhdr:
    arm64.cmp x20, 3
    arm64.b.lt __il_body#6
  __il_body#8:
    arm64.bl churn
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl churn
    arm64.eor x0, x20, x0
  __il_cont#7:
    arm64.and x0, x0, 127
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```
