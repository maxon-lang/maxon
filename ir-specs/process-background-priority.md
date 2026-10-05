---
feature: process-background-priority
status: stable
keywords: [process, priority, background, scheduling, enterBackgroundPriority, __Builtins, intrinsics]
category: system
---

# `__Builtins.enterBackgroundPriority()` — run this process out of the way: the cases that pin emitted code

The cases of `specs/process-background-priority.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: process-background-priority.priority-body-is-runtime-source -->
⭐⭐ **THE PRIORITY WRITE'S BODY IS MAXON SOURCE THE COMPILER READS OUT OF THE TREE, AND THIS IS THE CASE
THAT SEES IT.** `runtime/Process.maxon` writes `__proc_bg_priority` against the `__Raw` floor, and the block
below renders what the back end made of that source. It is the one place in this file where the SET is
visible at all: every case above reads the answer back, and the section on the suite's own priority explains
why none of them can tell a working set from a missing one. The rendered body carries both halves of the op
— the write and the read — so a lowering that lost the write changes this block even where it changes no
exit code.

⚠ The unit of the answer is platform-defined, so the assertions here are the two properties both scales
share: the reading is live rather than zero, and asking twice does not drift.

⚠ **WHAT THIS CASE CANNOT SAY.** A `RequiredRuntime` block ALSO marks the function it names
never-inline for that one compile (`InlineLeaves.goldenRequestedFunctions`), so a rendered body here is
no evidence about what survives inlining; `StdOp.osEnterBackgroundPriority.isUnsupportedInInlineBody` is
what keeps the call standing in every other case's emitted code.
```maxon
function main() returns ExitCode
	let first = __Builtins.enterBackgroundPriority()
	let second = __Builtins.enterBackgroundPriority()
	var score = 0
	if first > 0 'live'
		score = score + 1
	end 'live'
	if second == first 'stable'
		score = score + 1
	end 'stable'
	return score as ExitCode
end 'main'
```
```exitcode
2
```
```RequiredRuntime
__proc_bg_priority
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
    x64.pushReg rbx
    x64.prologue 40
    x64.callDirect __proc_bg_priority
    x64.movRegReg rbx, r8
    x64.callDirect __proc_bg_priority
    x64.movRegImm32 rax, 0
    x64.cmpRegImm32 rbx, 0
    x64.jcc lessEqual, ifcont
  live:
    x64.movRegImm32 rax, 1
  ifcont:
    x64.cmpRegReg r8, rbx
    x64.jcc notEqual, critsplit
  stable:
    x64.leaRegRegImm32 r8, rax, 1
    x64.jmp __rc_ok
  critsplit:
    x64.movRegReg r8, rax
  __rc_ok:
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__proc_bg_priority {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm rcx, 18446744073709551615
    x64.movRegImm32 rdx, 16384
    x64.iatCall 53
    x64.movRegImm rcx, 18446744073709551615
    x64.iatCall 54
    x64.movReg32Reg32 r8, rax
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
    x64.pushReg rbx
    x64.prologue 40
    x64.callDirect __proc_bg_priority
    x64.movRegReg rbx, r8
    x64.callDirect __proc_bg_priority
    x64.movRegImm32 rax, 0
    x64.cmpRegImm32 rbx, 0
    x64.jcc lessEqual, ifcont
  live:
    x64.movRegImm32 rax, 1
  ifcont:
    x64.cmpRegReg r8, rbx
    x64.jcc notEqual, critsplit
  stable:
    x64.leaRegRegImm32 r8, rax, 1
    x64.jmp __rc_ok
  critsplit:
    x64.movRegReg r8, rax
  __rc_ok:
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__proc_bg_priority {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rdi, 0
    x64.movRegImm32 rsi, 0
    x64.movRegImm32 rdx, 10
    x64.x64Syscall 141
    x64.movRegImm32 rdi, 0
    x64.movRegImm32 rsi, 0
    x64.x64Syscall 140
    x64.movRegImm32 r8, 20
    x64.subRegReg r8, r8, rax
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
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.bl __proc_bg_priority
    arm64.movRegReg x19, x0
    arm64.bl __proc_bg_priority
    arm64.movImm x1, 0
    arm64.cmp x19, 0
    arm64.b.le ifcont
  live:
    arm64.movImm x1, 1
  ifcont:
    arm64.cmp x0, x19
    arm64.b.ne critsplit
  stable:
    arm64.add x0, x1, 1
    arm64.b __rc_ok
  critsplit:
    arm64.movRegReg x0, x1
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__proc_bg_priority {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 0
    arm64.movImm x1, 0
    arm64.movImm x2, 10
    arm64.importCall 12
    arm64.movImm x0, 0
    arm64.movImm x1, 0
    arm64.importCall 13
    arm64.lsl x0, x0, 32
    arm64.asr x0, x0, 32
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
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.bl __proc_bg_priority
    arm64.movRegReg x19, x0
    arm64.bl __proc_bg_priority
    arm64.movImm x1, 0
    arm64.cmp x19, 0
    arm64.b.le ifcont
  live:
    arm64.movImm x1, 1
  ifcont:
    arm64.cmp x0, x19
    arm64.b.ne critsplit
  stable:
    arm64.add x0, x1, 1
    arm64.b __rc_ok
  critsplit:
    arm64.movRegReg x0, x1
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__proc_bg_priority {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 0
    arm64.movImm x1, 0
    arm64.movImm x2, 10
    arm64.syscall 140
    arm64.movImm x0, 0
    arm64.movImm x1, 0
    arm64.syscall 141
    arm64.movImm x1, 20
    arm64.sub x0, x1, x0
    arm64.epilogue 16
    arm64.ret
}
```
