---
feature: stdlib-loading
status: stable
keywords: [stdlib, loader, Clock, WallClock, dead-function-elimination, runtime-floor]
category: system
---

# Loading the stdlib: the cases that pin emitted code

The cases of `specs/stdlib-loading.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: stdlib-loading.a-stdlib-modules-literals-cannot-reach-the-rdata-image -->
⛔⛔ **THE BYTE-NEUTRALITY CLAIM'S ONLY GATE. ITS TWO SIBLINGS IN `specs/stdlib-loading.md` CANNOT FAIL.**
`a-stdlib-modules-literals-are-byte-neutral` shows a displaced `.rdata` payload only in its emitted
code, which nothing pins; its exit code is its only assertion. Neutralising `LowerMaxonToStd.registerProgramLiteralBlobs`'
unreachable-stdlib gate puts an orphan blob from `stdlib/Json.maxon` at `.rdata` byte 0 of every program
in the suite, and that case still PASSES. It is a real reading and a useful one, but nothing in the
battery turns it red.

⭐ **A ```RequiredRdata BLOCK IS THAT ENFORCEMENT, AND THE FIT IS EXACT.** The block is compared as a run
FROM BYTE 0, read back out of the LINKED IMAGE rather than out of the compiler's opinion of it — so it
answers precisely "did anything get in front of this program's read-only data?". This program's whole
`.rdata` is its two float constants, 16 bytes, every one of them pinned; a stdlib module that registers ANY
`.rdata` for code no path from `main` reaches lands ahead of them, because `registerProgramLiteralBlobs`
runs before the target tier mints a float. With the gate neutralised it reports
`.rdata mismatch at byte 6: expected 0x29, got 0x00` — eight zero bytes of orphan where `12.5` belongs.

⚠ **IT MUST HOLD NO STRING LITERAL OF ITS OWN, and that is not a stylistic choice.** The user's `main` is
walked before stdlib's functions, so a program literal in `main` keeps byte 0 whatever an orphan does and
the pin goes green on the broken compiler. The payload a displacement is visible against has to
be one the COMPILER composes.

⛔⛔ **AND THE LOOP IS LOAD-BEARING: WITHOUT IT THIS PROGRAM HAS NO `.rdata` AT ALL.** Straight-line
`let scale = 12.5` / `let floor = 1.5` / `if scale > floor` is folded by `foldConstants`, which folds
FLOATS — the comparison becomes a constant, `foldConstantBranches` takes the arm, and both float
`const`s are retired unread. The program still returns 8, but materialises neither float, the linked
image has no `.rdata` section, and this gate cannot read the thing it gates: `could not read the .rdata
section … has no .rdata section`.

⇒ The loop makes `scale` a HEADER PHI, which this pass reads as unknown by construction — *"it is not a
constant propagator; a value that is constant on every path into a phi is not constant to this pass"*. So
`scale + floor` and `scale > floor` both keep their instructions, `12.5` is materialised as the phi's
entering value and `1.5` as an operand (floats have no immediate form on any target, so
`foldConstOperands` cannot absorb it either), and the two islands are registered in source order. **Do not
simplify it back.** A folded version of this program passes its exit code and gates nothing — which is
precisely the failure mode the paragraph above this one is about, arriving by a second route.
```maxon
function main() returns ExitCode
	var scale = 12.5
	let floor = 1.5
	var spins = 0
	while spins < 1 'spin'
		scale = scale + floor
		spins = spins + 1
	end 'spin'
	if scale > floor 'gt'
		return 8
	end 'gt'
	return 1
end 'main'
```
```exitcode
8
```
```RequiredRdata
f64 12.5
f64 1.5
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
    x64.movsdRegRip xmm0, [rip + __fconst_12.5]
    x64.movsdRegRip xmm1, [rip + __fconst_1.5]
    x64.movRegImm32 rax, 0
    x64.jmp whilehdr
  spin:
    x64.addsdRegReg xmm0, xmm0, xmm1
    x64.leaRegRegImm32 rax, rax, 1
  whilehdr:
    x64.cmpRegImm32 rax, 1
    x64.jcc less, spin
  whileexit:
    x64.ucomisdRegReg xmm0, xmm1
    x64.jcc belowEqual, ifcont
  gt:
    x64.movRegImm32 r8, 8
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
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
    x64.movsdRegRip xmm0, [rip + __fconst_12.5]
    x64.movsdRegRip xmm1, [rip + __fconst_1.5]
    x64.movRegImm32 rax, 0
    x64.jmp whilehdr
  spin:
    x64.addsdRegReg xmm0, xmm0, xmm1
    x64.leaRegRegImm32 rax, rax, 1
  whilehdr:
    x64.cmpRegImm32 rax, 1
    x64.jcc less, spin
  whileexit:
    x64.ucomisdRegReg xmm0, xmm1
    x64.jcc belowEqual, ifcont
  gt:
    x64.movRegImm32 r8, 8
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.movRegImm32 r8, 1
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
    arm64.fpLoadRdata d0, [rdata + __fconst_12.5]
    arm64.fpLoadRdata d1, [rdata + __fconst_1.5]
    arm64.movImm x0, 0
    arm64.b whilehdr
  spin:
    arm64.fadd d0, d0, d1
    arm64.add x0, x0, 1
  whilehdr:
    arm64.cmp x0, 1
    arm64.b.lt spin
  whileexit:
    arm64.fcmp d0, d1
    arm64.b.le ifcont
  gt:
    arm64.movImm x0, 8
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
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
    arm64.fpLoadRdata d0, [rdata + __fconst_12.5]
    arm64.fpLoadRdata d1, [rdata + __fconst_1.5]
    arm64.movImm x0, 0
    arm64.b whilehdr
  spin:
    arm64.fadd d0, d0, d1
    arm64.add x0, x0, 1
  whilehdr:
    arm64.cmp x0, 1
    arm64.b.lt spin
  whileexit:
    arm64.fcmp d0, d1
    arm64.b.le ifcont
  gt:
    arm64.movImm x0, 8
    arm64.epilogue 16
    arm64.ret
  ifcont:
    arm64.movImm x0, 1
    arm64.epilogue 16
    arm64.ret
}
```
