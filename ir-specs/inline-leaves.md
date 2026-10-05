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
  __rc_ok:
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
  __rc_ok:
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
  __rc_ok:
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
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 0
  __rc_ok:
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
  __rc_ok:
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
  __rc_ok:
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
  __rc_ok:
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
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 0
  __rc_ok:
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
  __rc_ok:
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
  __rc_ok:
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
  __rc_ok:
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
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 0
  __rc_ok:
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
