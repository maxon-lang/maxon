---
feature: dead-top-level-var-elim
status: experimental
keywords: [dce, dead-code-elimination, top-level, let, module-init, optimization]
category: optimizations
---

# Dead Top-Level Binding Elimination: the cases that pin emitted code

The cases of `specs/dead-top-level-var-elim.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: dead-array-let-leaves-no-data-slot -->
An array `let` is IMAGE DATA: its bytes are laid down in `.rdata` and it reserves no `.data` slot at
all, so the `data` block lists none for it and nothing builds it before `main`.
```maxon
let deadArr = [1, 2, 3, 4, 5]

function main() returns ExitCode
	return 7
end 'main'
```
```exitcode
7
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
    x64.movRegImm32 r8, 7
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
    arm64.epilogue 16
    arm64.ret
}
```
