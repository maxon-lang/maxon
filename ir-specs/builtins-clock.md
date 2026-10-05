---
feature: builtins-clock
status: stable
keywords: [builtins, __Builtins, clock, time, monotonic, wall-clock, cpu-time, intrinsics]
category: system
---

# `__Builtins` clock intrinsics: the cases that pin emitted code

The cases of `specs/builtins-clock.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: builtins-clock.wall-clock-body-is-runtime-source -->
⭐⭐ **THE CALENDAR'S BODY IS MAXON SOURCE THE COMPILER READS OUT OF THE TREE, AND THIS IS THE CASE THAT
SEES IT.** `runtime/Clock.maxon` writes `__clock_now_unix_s` as a call to `clockTicksSince1970` — the
helper both wall clocks share, which reads the host's FILETIME through a `__Raw` frame word and applies
the 1601→1970 shift — followed by the ticks→seconds divide, and the block below renders what the back end
made of that source. Every OTHER case in this file reads the clock's ANSWER, and each would pass just as
happily against a body the compiler built itself; only a rendered body says which tier it came from.

⚠ **THE DIVIDE IS THE HALF THAT NEEDS WATCHING.** It is inline in this body. The source states both its
types `int(0 to u64.max)`, so the quotient is emitted UNSIGNED and reduced to an unsigned magic
multiply — and this case's `TargetIr` pin is where a range guard would show up, as an `__rc_panic` on the helper's
answer or on the quotient. The helper's answer carries a proven whole-word interval, so neither has
anything to reject. The divisor is a literal constant, which is what keeps the divide BARE: a
`__checked_div` call appearing here is a divisor that stopped being provably non-zero.

⚠ `main` reads the clock TWICE so that the rendered body is the one that runs: a called-once function is
moved into its caller, which would leave this pin holding an emitted leftover.
```maxon
function main() returns ExitCode
	let first = __Builtins.currentUnixTimeSeconds()
	let second = __Builtins.currentUnixTimeSeconds()
	var score = 0
	if second >= first 'nondecreasing'
		score = score + 1
	end 'nondecreasing'
	if first > 1735689600 'afterKnownPast'
		score = score + 1
	end 'afterKnownPast'
	return score as ExitCode
end 'main'
```
```exitcode
2
```
```RequiredRuntime
__clock_now_unix_s
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
    x64.callDirect __clock_now_unix_s
    x64.movRegReg rbx, r8
    x64.callDirect __clock_now_unix_s
    x64.movRegImm32 rax, 0
    x64.cmpRegReg r8, rbx
    x64.jcc less, ifcont
  nondecreasing:
    x64.movRegImm32 rax, 1
  ifcont:
    x64.cmpRegImm32 rbx, 1735689600
    x64.jcc lessEqual, critsplit
  afterKnownPast:
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

func @__clock_now_unix_s {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
  __il_body:
    x64.callDirect __clock_now_filetime
    x64.movRegImm rax, 116444736000000000
    x64.movRegReg rcx, r8
    x64.subRegReg rcx, r8, rax
    x64.movRegReg rdx, r8
    x64.xorRegImm32 rdx, r8, -1
    x64.andRegReg rdx, rdx, rax
    x64.xorRegReg r8, r8, rax
    x64.xorRegImm32 r8, r8, -1
    x64.andRegReg r8, r8, rcx
    x64.orRegReg rdx, rdx, r8
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __rc_panic
  __il_cont:
    x64.movRegImm rax, 15474250491067253437
    x64.mulHighReg rcx
    x64.movRegReg r8, rdx
    x64.shrRegImm8 r8, rdx, 23
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at Clock.maxon:73: Range check failed: value outside typealias 'FileTimeTicks'\x0a"
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
    x64.pushReg rbx
    x64.prologue 40
    x64.callDirect __clock_now_unix_s
    x64.movRegReg rbx, r8
    x64.callDirect __clock_now_unix_s
    x64.movRegImm32 rax, 0
    x64.cmpRegReg r8, rbx
    x64.jcc less, ifcont
  nondecreasing:
    x64.movRegImm32 rax, 1
  ifcont:
    x64.cmpRegImm32 rbx, 1735689600
    x64.jcc lessEqual, critsplit
  afterKnownPast:
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

func @__clock_now_unix_s {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
  __il_body:
    x64.callDirect __clock_now_filetime
    x64.movRegImm rax, 116444736000000000
    x64.movRegReg rcx, r8
    x64.subRegReg rcx, r8, rax
    x64.movRegReg rdx, r8
    x64.xorRegImm32 rdx, r8, -1
    x64.andRegReg rdx, rdx, rax
    x64.xorRegReg r8, r8, rax
    x64.xorRegImm32 r8, r8, -1
    x64.andRegReg r8, r8, rcx
    x64.orRegReg rdx, rdx, r8
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, __rc_panic
  __il_cont:
    x64.movRegImm rax, 15474250491067253437
    x64.mulHighReg rcx
    x64.movRegReg r8, rdx
    x64.shrRegImm8 r8, rdx, 23
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at Clock.maxon:73: Range check failed: value outside typealias 'FileTimeTicks'\x0a"
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
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.bl __clock_now_unix_s
    arm64.movRegReg x19, x0
    arm64.bl __clock_now_unix_s
    arm64.movImm x1, 0
    arm64.cmp x0, x19
    arm64.b.lt critsplit
  nondecreasing:
    arm64.movImm x0, 1
    arm64.b ifcont
  critsplit:
    arm64.movRegReg x0, x1
  ifcont:
    arm64.movImm x16, 1735689600
    arm64.cmp x19, x16
    arm64.b.le __rc_ok
  afterKnownPast:
    arm64.add x0, x0, 1
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__clock_now_unix_s {
  entry:
    arm64.prologue 16
  __il_body:
    arm64.bl __clock_now_filetime
    arm64.movImm x1, 116444736000000000
    arm64.sub x2, x0, x1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x3, x0, x16
    arm64.and x3, x3, x1
    arm64.eor x0, x0, x1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x0, x0, x16
    arm64.and x0, x0, x2
    arm64.orr x0, x3, x0
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __il_cont:
    arm64.movImm x0, 15474250491067253437
    arm64.umulh x0, x0, x2
    arm64.lsr x0, x0, 23
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at Clock.maxon:73: Range check failed: value outside typealias 'FileTimeTicks'\x0a"
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
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.bl __clock_now_unix_s
    arm64.movRegReg x19, x0
    arm64.bl __clock_now_unix_s
    arm64.movImm x1, 0
    arm64.cmp x0, x19
    arm64.b.lt critsplit
  nondecreasing:
    arm64.movImm x0, 1
    arm64.b ifcont
  critsplit:
    arm64.movRegReg x0, x1
  ifcont:
    arm64.movImm x16, 1735689600
    arm64.cmp x19, x16
    arm64.b.le __rc_ok
  afterKnownPast:
    arm64.add x0, x0, 1
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__clock_now_unix_s {
  entry:
    arm64.prologue 16
  __il_body:
    arm64.bl __clock_now_filetime
    arm64.movImm x1, 116444736000000000
    arm64.sub x2, x0, x1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x3, x0, x16
    arm64.and x3, x3, x1
    arm64.eor x0, x0, x1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x0, x0, x16
    arm64.and x0, x0, x2
    arm64.orr x0, x3, x0
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __il_cont:
    arm64.movImm x0, 15474250491067253437
    arm64.umulh x0, x0, x2
    arm64.lsr x0, x0, 23
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at Clock.maxon:73: Range check failed: value outside typealias 'FileTimeTicks'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```
