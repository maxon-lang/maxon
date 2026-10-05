---
feature: process-id
status: stable
keywords: [process, currentProcessId, pid, introspection, __Builtins, intrinsics]
category: system
---

# `__Builtins.currentProcessId()` — the running process's own id: the cases that pin emitted code

The cases of `specs/process-id.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: process-id.pid-body-is-runtime-source -->
⭐⭐ **THE PID'S BODY IS MAXON SOURCE THE COMPILER READS OUT OF THE TREE, AND THIS IS THE CASE THAT SEES
IT.** `runtime/Process.maxon` writes `__proc_pid` against the `__Raw` floor — one host read answered
straight back — and the block below renders what the back end made of that source. Every other case here
reads the ANSWER, and each would pass just as happily against a body the compiler built itself.

⛔ **AN ENTRY POINT THIS SMALL IS EXACTLY WHERE A BODY CAN GO MISSING UNSEEN.** There is no retry arm, no
buffer and no failure path, so the only thing the shape can say is that the call is made and its result is
what the function answers; a lowering that dropped the call would leave a plausible number in the result
register and every property case above would still pass.

⚠ **WHAT THIS CASE CANNOT SAY.** A `RequiredRuntime` block ALSO marks the function it names
never-inline for that one compile (`InlineLeaves.goldenRequestedFunctions`), so a rendered body here is
no evidence about what survives inlining; `main` reads the id twice, and
`StdOp.osGetPid.isUnsupportedInInlineBody` is what keeps the call standing in every other case's emitted code.
```maxon
function main() returns ExitCode
	let first = __Builtins.currentProcessId()
	let second = __Builtins.currentProcessId()
	var score = 0
	if first > 0 'positive'
		score = score + 1
	end 'positive'
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
__proc_pid
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
    x64.callDirect __proc_pid
    x64.movRegReg rbx, r8
    x64.callDirect __proc_pid
    x64.movRegImm32 rax, 0
    x64.cmpRegImm32 rbx, 0
    x64.jcc lessEqual, ifcont
  positive:
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

func @__proc_pid {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.iatCall 25
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
    x64.callDirect __proc_pid
    x64.movRegReg rbx, r8
    x64.callDirect __proc_pid
    x64.movRegImm32 rax, 0
    x64.cmpRegImm32 rbx, 0
    x64.jcc lessEqual, ifcont
  positive:
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

func @__proc_pid {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.x64Syscall 39
    x64.movRegReg r8, rax
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
    arm64.bl __proc_pid
    arm64.movRegReg x19, x0
    arm64.bl __proc_pid
    arm64.movImm x1, 0
    arm64.cmp x19, 0
    arm64.b.le ifcont
  positive:
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

func @__proc_pid {
  entry:
    arm64.prologue 16
    arm64.importCall 11
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
    arm64.bl __proc_pid
    arm64.movRegReg x19, x0
    arm64.bl __proc_pid
    arm64.movImm x1, 0
    arm64.cmp x19, 0
    arm64.b.le ifcont
  positive:
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

func @__proc_pid {
  entry:
    arm64.prologue 16
    arm64.syscall 172
    arm64.epilogue 16
    arm64.ret
}
```
