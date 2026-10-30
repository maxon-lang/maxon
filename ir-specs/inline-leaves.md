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
A checked `get` or `set` on a concrete `Array with Integer` keeps the runtime call as the SLOW ARM of the
expansion `inlineManagedPrimitives` emits, and that arm is a cold block: the known stride means the fast arm
serves every element, so the call is reached only for an index past the end, a shared buffer or a
non-owned one. A helper that wraps one access holds that call, and a call in a cold block does not refuse a
leaf, so `elementAt` and `putElement` are spliced at both of their sites
and the pin's `main` holds the fast arms with no `callDirect elementAt` or `callDirect putElement`; the
`__im_slow` arm of each copy still reaches `__managed_get` and `__managed_set`, and the panic block still
names the helper's own line.
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
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegImm32 rcx, 1
    x64.jcc belowEqual, __im_slow
  __im_buffer#15:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow
  __im_store#17:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  __il_cont#8:
    x64.movRegImm32 rax, 22
  __im_buffer#24:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
  __im_store#26:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  __im_load#32:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  __im_load#38:
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
    x64.callDirect __managed_set
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, __il_cont#8
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-checked-accessor-whose-slow-arm-is-cold-is-inlined.test:10: element past the end\x0a"
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
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegImm32 rcx, 1
    x64.jcc belowEqual, __im_slow
  __im_buffer#15:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rsi, [rsi + 16]
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, __im_slow
  __im_store#17:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  __il_cont#8:
    x64.movRegImm32 rax, 22
  __im_buffer#24:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
  __im_store#26:
    x64.storeBaseDispReg.word64 [rcx + 8], rax
  __im_load#32:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
  __im_load#38:
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
    x64.callDirect __managed_set
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, __il_cont#8
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-checked-accessor-whose-slow-arm-is-cold-is-inlined.test:10: element past the end\x0a"
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
    arm64.movImm x2, 1
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 1
    arm64.b.ls __im_slow
  __im_buffer#15:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow
  __im_store#17:
    arm64.storeBaseDispReg.word64 [x0 + 8], x2
  __il_cont#8:
    arm64.movImm x0, 22
  __im_buffer#24:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
  __im_store#26:
    arm64.storeBaseDispReg.word64 [x1 + 8], x0
  __im_load#32:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  __im_load#38:
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
    arm64.bl __managed_set
    arm64.cmp x9, 0
    arm64.b.eq __il_cont#8
  tryerr:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-checked-accessor-whose-slow-arm-is-cold-is-inlined.test:10: element past the end\x0a"
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
    arm64.movImm x2, 1
  __il_body:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 1
    arm64.b.ls __im_slow
  __im_buffer#15:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x3, x0, 24
    arm64.loadRegBaseDisp.word64 x3, [x3 + 16]
    arm64.cmp x3, 0
    arm64.b.ne __im_slow
  __im_store#17:
    arm64.storeBaseDispReg.word64 [x0 + 8], x2
  __il_cont#8:
    arm64.movImm x0, 22
  __im_buffer#24:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
  __im_store#26:
    arm64.storeBaseDispReg.word64 [x1 + 8], x0
  __im_load#32:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  __im_load#38:
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
    arm64.bl __managed_set
    arm64.cmp x9, 0
    arm64.b.eq __il_cont#8
  tryerr:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-checked-accessor-whose-slow-arm-is-cold-is-inlined.test:10: element past the end\x0a"
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
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 r12, 7
  __il_body:
    x64.movRegImm32 r13, 0
    x64.jmp forhdr
  eachByte:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegReg rax, rcx
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rdx, rcx, -24
    x64.loadRegBaseDisp.word64 rdx, [rdx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 24]
    x64.cmpRegImm32 rdx, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.storeBaseIndexScaleReg.word64 [rcx + rax*8 + 0], r12
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.jmp forstep
  __im_stride:
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.leaRegRegReg rcx, rcx, rax
    x64.storeBaseDispReg.byte [rcx + 0], r12
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.jmp forstep
  __im_slow:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __managed_push
  forstep:
    x64.leaRegRegImm32 r13, r13, 1
  forhdr:
    x64.cmpRegImm32 r13, 42
    x64.jcc less, eachByte
  __il_cont:
    x64.loadRegBaseDisp.word64 r12, [rbx + 8]
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
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-byte-push-loop-reaches-the-runtime-only-on-its-slow-arm.test:15: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    x64.movRegImm32 r12, 7
  __il_body:
    x64.movRegImm32 r13, 0
    x64.jmp forhdr
  eachByte:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.cmpRegReg rax, rcx
    x64.jcc aboveEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rdx, rcx, -24
    x64.loadRegBaseDisp.word64 rdx, [rdx + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __im_slow
  __im_store:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 24]
    x64.cmpRegImm32 rdx, 8
    x64.jcc notEqual, __im_stride
  __im_word:
    x64.storeBaseIndexScaleReg.word64 [rcx + rax*8 + 0], r12
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.jmp forstep
  __im_stride:
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, __im_slow
  __im_byte:
    x64.leaRegRegReg rcx, rcx, rax
    x64.storeBaseDispReg.byte [rcx + 0], r12
    x64.leaRegRegImm32 rax, rax, 1
    x64.storeBaseDispReg.word64 [rbx + 8], rax
    x64.jmp forstep
  __im_slow:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __managed_push
  forstep:
    x64.leaRegRegImm32 r13, r13, 1
  forhdr:
    x64.cmpRegImm32 r13, 42
    x64.jcc less, eachByte
  __il_cont:
    x64.loadRegBaseDisp.word64 r12, [rbx + 8]
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r12, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
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
    x64.leaRegRdata rcx, [rip + __str_blob_3]  ; "panic at a-byte-push-loop-reaches-the-runtime-only-on-its-slow-arm.test:15: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    arm64.movImm x20, 7
  __il_body:
    arm64.movImm x21, 0
    arm64.b forhdr
  eachByte:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x1, [x19 + 16]
    arm64.cmp x1, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 16]
    arm64.cmp x0, x1
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x2, x1, 24
    arm64.loadRegBaseDisp.word64 x2, [x2 + 16]
    arm64.cmp x2, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 24]
    arm64.cmp x2, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.storeBaseIndexScaleReg.word64 [x1 + x0*8 + 0], x20
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.b forstep
  __im_stride:
    arm64.cmp x2, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.add x1, x1, x0
    arm64.storeBaseDispReg.byte [x1 + 0], x20
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.b forstep
  __im_slow:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __managed_push
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.cmp x21, 42
    arm64.b.lt eachByte
  __il_cont:
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
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
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-byte-push-loop-reaches-the-runtime-only-on-its-slow-arm.test:15: Range check failed: value outside typealias 'ExitCode'\x0a"
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
    arm64.movImm x20, 7
  __il_body:
    arm64.movImm x21, 0
    arm64.b forhdr
  eachByte:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.loadRegBaseDisp.word64 x1, [x19 + 16]
    arm64.cmp x1, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 16]
    arm64.cmp x0, x1
    arm64.b.hs __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x2, x1, 24
    arm64.loadRegBaseDisp.word64 x2, [x2 + 16]
    arm64.cmp x2, 0
    arm64.b.ne __im_slow
  __im_store:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 24]
    arm64.cmp x2, 8
    arm64.b.ne __im_stride
  __im_word:
    arm64.storeBaseIndexScaleReg.word64 [x1 + x0*8 + 0], x20
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.b forstep
  __im_stride:
    arm64.cmp x2, 1
    arm64.b.ne __im_slow
  __im_byte:
    arm64.add x1, x1, x0
    arm64.storeBaseDispReg.byte [x1 + 0], x20
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 8], x0
    arm64.b forstep
  __im_slow:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __managed_push
  forstep:
    arm64.add x21, x21, 1
  forhdr:
    arm64.cmp x21, 42
    arm64.b.lt eachByte
  __il_cont:
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
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
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_3  ; "panic at a-byte-push-loop-reaches-the-runtime-only-on-its-slow-arm.test:15: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
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
  visit:
    x64.leaRegRdata r12, [rip + __layout_ArrayIterator_Byte$0_255$1]
  __il_body#14:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.leaRegRdata rdx, [rip + __str_rec_1]  ; "panic at Array.maxon:497: "
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
    x64.movRegReg rdx, r8
    x64.jmp __im_cont
  critsplit#44:
    x64.movRegReg rdx, rax
    x64.jmp __im_cont
  critsplit#43:
    x64.movRegReg rdx, rax
  __im_cont:
    x64.movRegReg rcx, r12
    x64.callDirect __retain_type_param
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
    x64.jcc equal, visit
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
  visit:
    x64.leaRegRdata r12, [rip + __layout_ArrayIterator_Byte$0_255$1]
  __il_body#14:
    x64.loadRegBaseDisp.word64 rcx, [r13 + 0]
    x64.leaRegRdata rdx, [rip + __str_rec_1]  ; "panic at Array.maxon:497: "
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
    x64.movRegReg rdx, r8
    x64.jmp __im_cont
  critsplit#44:
    x64.movRegReg rdx, rax
    x64.jmp __im_cont
  critsplit#43:
    x64.movRegReg rdx, rax
  __im_cont:
    x64.movRegReg rcx, r12
    x64.callDirect __retain_type_param
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
    x64.jcc equal, visit
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
  visit:
    arm64.leaRdata x20, __layout_ArrayIterator_Byte$0_255$1
  __il_body#14:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.leaRdata x1, __str_rec_1  ; "panic at Array.maxon:497: "
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
    arm64.movRegReg x1, x0
    arm64.b __im_cont
  critsplit#44:
    arm64.movRegReg x1, x2
    arm64.b __im_cont
  critsplit#43:
    arm64.movRegReg x1, x2
  __im_cont:
    arm64.movRegReg x0, x20
    arm64.bl __retain_type_param
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
    arm64.b.eq visit
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
  visit:
    arm64.leaRdata x20, __layout_ArrayIterator_Byte$0_255$1
  __il_body#14:
    arm64.loadRegBaseDisp.word64 x0, [x21 + 0]
    arm64.leaRdata x1, __str_rec_1  ; "panic at Array.maxon:497: "
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
    arm64.movRegReg x1, x0
    arm64.b __im_cont
  critsplit#44:
    arm64.movRegReg x1, x2
    arm64.b __im_cont
  critsplit#43:
    arm64.movRegReg x1, x2
  __im_cont:
    arm64.movRegReg x0, x20
    arm64.bl __retain_type_param
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
    arm64.b.eq visit
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
