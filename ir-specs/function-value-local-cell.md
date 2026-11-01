---
feature: function-value-local-cell
status: stable
keywords: [function value, by reference, mutable, cell, local, codegen]
category: codegen
---

# A local handed to a function value: the cases that pin emitted code

The cases of `specs/pass-by-reference.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: a-function-value-handed-a-bare-local-takes-no-heap-cell -->
`adder` is a function value of arity 3 and no free function of arity 3 marks a parameter `mutable`, so `base`
handed to it as argument 0 is read as a value and `main` allocates no heap cell for it. A `static` method of
three parameters whose first is `mutable` takes no part in the answer, because a method is never a function value
(E2078): the block below holds no `__mm_alloc` and no `__mm_decref`.
```maxon
typealias Tally = int(0 to u64.max)

function add3(a Tally, b Tally, c Tally) returns Tally
	return a + b + c
end 'add3'

function main() returns ExitCode
	var base = 4 as Tally
	let adder = add3

	base = adder(base, 5, 6)

	if base == 15 'summed'
		return 0
	end 'summed'

	return 1
end 'main'
```
```exitcode
0
```

```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @add3 {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg r8, rcx, rax
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegRdata rax, [rip + __fnrec_add3]
  summed:
    x64.movRegImm32 r8, 0
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

func @add3 {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegRegReg rcx, rcx, rdx
    x64.leaRegRegReg r8, rcx, rax
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegRdata rax, [rip + __fnrec_add3]
  summed:
    x64.movRegImm32 r8, 0
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __mrt_program_started@0 = i8 0
}

func @add3 {
  entry:
    arm64.prologue 16
    arm64.add x0, x0, x1
    arm64.add x0, x0, x2
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaRdata x0, __fnrec_add3
  summed:
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

func @add3 {
  entry:
    arm64.prologue 16
    arm64.add x0, x0, x1
    arm64.add x0, x0, x2
    arm64.epilogue 16
    arm64.ret
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaRdata x0, __fnrec_add3
  summed:
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```
