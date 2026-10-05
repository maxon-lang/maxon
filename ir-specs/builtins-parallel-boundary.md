---
feature: builtins-parallel-boundary
status: stable
keywords: [builtins, __Builtins, parallelBoundary, intrinsics, async, green-threads, E3073]
category: system
---

# The `__Builtins.parallelBoundary` intrinsic: the cases that pin emitted code

The cases of `specs/builtins-parallel-boundary.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: builtins-parallel-boundary.checkpoint-body-is-runtime-source -->
⭐⭐ **THE CHECKPOINT'S BODY IS MAXON SOURCE THE COMPILER READS OUT OF THE TREE, AND THIS IS THE CASE THAT
SEES IT.** `runtime/ParallelBoundary.maxon` writes `__parallel_boundary` as `__Raw.ownFrame()` and nothing
else, and the block below renders what the back end made of that: a prologue, an epilogue, a `ret`. Every
other case in this file watches the CALL; only a rendered body says the callee exists and what it costs. An
op appearing here is a row that stopped being a directive, and a body with a frame reservation is a
checkpoint that has started paying for storage it does not use.

⚠ **AND IT CANNOT, BY ITSELF, CATCH A REGRESSION IN `ownFrame` — READ THIS BEFORE TRUSTING IT.** A
`RequiredRuntime` block ALSO marks the function it names never-inline for that one compile
(`InlineLeaves.goldenRequestedFunctions`), so this body would be rendered here even by a compiler that had
stopped honouring `IrFunction.keepsItsOwnFrame` and spliced the checkpoint away everywhere else. What
shows the rule holds is the `call __parallel_boundary` standing in the emitted code of every OTHER case that
reaches one — `specs/builtins-parallel-boundary.md`'s `statement-position`, and the `sched-*`,
`builtins-mm-counters`, `builtins-cpu-parallel`, `debugstream-log-events` and `runtime-scratch-reclaim`
families. This case pins the far end of that call; those show that the call is still there.

⚠ `main` calls the checkpoint TWICE, through a function that is itself called twice, so neither the leaf
rule nor the called-once rule has a single site to move: the pin holds the body that runs rather than an
emitted leftover.
```maxon
function checkpoint()
	__Builtins.parallelBoundary()
	__Builtins.parallelBoundary()
end 'checkpoint'

function main() returns ExitCode
	checkpoint()
	checkpoint()
	return 4
end 'main'
```
```exitcode
4
```
```RequiredRuntime
__parallel_boundary
```

```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @checkpoint {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.callDirect __parallel_boundary
    x64.callDirect __parallel_boundary
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.callDirect checkpoint
    x64.callDirect checkpoint
    x64.movRegImm32 r8, 4
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @__parallel_boundary {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
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

func @checkpoint {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.callDirect __parallel_boundary
    x64.callDirect __parallel_boundary
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.callDirect checkpoint
    x64.callDirect checkpoint
    x64.movRegImm32 r8, 4
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @__parallel_boundary {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @checkpoint {
  entry:
    arm64.prologue 16
    arm64.bl __parallel_boundary
    arm64.bl __parallel_boundary
    arm64.movRegReg x1, x0
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.bl checkpoint
    arm64.bl checkpoint
    arm64.movImm x0, 4
    arm64.epilogue 16
    arm64.ret
}

func @__parallel_boundary {
  entry:
    arm64.prologue 16
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

func @checkpoint {
  entry:
    arm64.prologue 16
    arm64.bl __parallel_boundary
    arm64.bl __parallel_boundary
    arm64.movRegReg x1, x0
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.bl checkpoint
    arm64.bl checkpoint
    arm64.movImm x0, 4
    arm64.epilogue 16
    arm64.ret
}

func @__parallel_boundary {
  entry:
    arm64.prologue 16
    arm64.epilogue 16
    arm64.ret
}
```
