---
feature: dead-top-level-var-elim
status: experimental
keywords: [dce, dead-code-elimination, top-level, var, module-init, optimization]
category: optimizations
---

# Dead Top-Level Variable Elimination: the cases that pin emitted code

The cases of `specs/dead-top-level-var-elim.md` whose subject is the emitted code. Each pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: dead-scalar-var-leaves-no-data-slot -->
The exit-code cases above compute the right answer whether or not anything is dropped, so they
cannot see this pass at all. `RequiredData` can: it reads the `.data` section back out of the
LINKED binary, so a slot that is still being laid down is a byte at offset 0 that the pin does not
have. The dead global is declared FIRST deliberately — the gate is a PREFIX compare, so a dead
slot has to sit ahead of a live one to be visible to it.

```maxon
var unused = 99
var used = 42

function main() returns ExitCode
	return used - 42
end 'main'
```
```exitcode
0
```
```RequiredData
i64 42
```

```TargetIr:x64-windows
data {
  __data_used@0 = i64 42
  __mrt_console_probe_stdin@8 = i8 0
  __mrt_console_probe_stdout@9 = i8 0
  __mrt_console_probe_stderr@10 = i8 0
  __mrt_program_started@11 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_used
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegImm32 r8, rax, -42
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
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at dead-scalar-var-leaves-no-data-slot.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_used@0 = i64 42
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_used
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegImm32 r8, rax, -42
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
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at dead-scalar-var-leaves-no-data-slot.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_used@0 = i64 42
  __mrt_program_started@8 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_used
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.sub x0, x0, 42
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at dead-scalar-var-leaves-no-data-slot.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_used@0 = i64 42
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_used
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.sub x0, x0, 42
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at dead-scalar-var-leaves-no-data-slot.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: dead-array-let-leaves-no-data-slot -->
An array `let` is IMAGE DATA: its bytes are laid down in `.rdata` and it reserves no `.data` slot at
all, so `live` is the only global the section may hold. The pin is a PREFIX compare, and the array is
declared FIRST, so a slot that came back would be a byte at offset 0 the pin does not have.

```maxon
let deadArr = [1, 2, 3, 4, 5]
var live = 7

function main() returns ExitCode
	return live
end 'main'
```
```exitcode
7
```
```RequiredData
i64 7
```

```TargetIr:x64-windows
data {
  __data_live@0 = i64 7
  __mrt_console_probe_stdin@8 = i8 0
  __mrt_console_probe_stdout@9 = i8 0
  __mrt_console_probe_stderr@10 = i8 0
  __mrt_program_started@11 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_live
    x64.loadRegBaseDisp.word64 r8, [rax + 0]
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
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at dead-array-let-leaves-no-data-slot.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_live@0 = i64 7
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_live
    x64.loadRegBaseDisp.word64 r8, [rax + 0]
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
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at dead-array-let-leaves-no-data-slot.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_live@0 = i64 7
  __mrt_program_started@8 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_live
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at dead-array-let-leaves-no-data-slot.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_live@0 = i64 7
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_live
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at dead-array-let-leaves-no-data-slot.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: dead-global-dropped-beside-a-live-managed-one -->
⭐ **THE PRUNE IS PER-GLOBAL.** The compiler has ONE `__module_init` for the whole program, so an
all-or-nothing prune of that function would mean never dropping anything. Here `deadArr` and `liveArr` share that one
function: the dead one's build must go while the live one's stays, and `liveArr.get(1)` still
answering 20 is what proves the surviving record was built correctly rather than merely allocated.

⚠ **BOTH ARRAYS ARE `var`s, AND THAT IS WHAT KEEPS THE CASE ABOUT THIS PASS.** A `let` array is
IMAGE DATA — no slot, no build, nothing for a prune to reach — so declaring either one `let` would
leave `probe` as the only slot and the pin would hold whether or not this pass ran at all.

```maxon
var deadArr = [1, 2, 3]
var liveArr = [10, 20, 30]
var probe = 4

function main() returns ExitCode
	return ((try liveArr.get(1) otherwise 0) - probe) as ExitCode
end 'main'
```
```exitcode
16
```
```RequiredData
i64 0
i64 4
```

```TargetIr:x64-windows
data {
  __data_liveArr@0 = i64 0
  __data_probe@8 = i64 4
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_console_probe_stdin@40 = i8 0
  __mrt_console_probe_stdout@41 = i8 0
  __mrt_console_probe_stderr@42 = i8 0
  __mrt_program_started@43 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_liveArr
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow
    x64.jmp __im_load
  tryerr:
    x64.movRegImm32 rax, 0
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.leaRegRegImm32 rax, rax, 8
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.movRegReg r8, rax
  tryok:
    x64.movRegReg rax, r8
  trycont:
    x64.leaRegGlobal rcx, __data_probe
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.movRegReg r8, rax
    x64.subRegReg r8, rax, rcx
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
  __im_slow:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at dead-global-dropped-beside-a-live-managed-one.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_liveArr@0 = i64 0
  __data_probe@8 = i64 4
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_liveArr
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow
    x64.jmp __im_load
  tryerr:
    x64.movRegImm32 rax, 0
    x64.jmp trycont
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.leaRegRegImm32 rax, rax, 8
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.movRegReg r8, rax
  tryok:
    x64.movRegReg rax, r8
  trycont:
    x64.leaRegGlobal rcx, __data_probe
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.movRegReg r8, rax
    x64.subRegReg r8, rax, rcx
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
  __im_slow:
    x64.callDirect __managed_get
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at dead-global-dropped-beside-a-live-managed-one.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_liveArr@0 = i64 0
  __data_probe@8 = i64 4
  __slab_arena_list@16 = i64 0
  __slab_arena_map_l1@24 = i64 0
  __slab_state@32 = i64 0
  __mrt_program_started@40 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_liveArr
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 1
    arm64.b.ls __im_slow
    arm64.b __im_load
  tryerr:
    arm64.movImm x0, 0
    arm64.b trycont
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x0, x0, 8
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  trycont:
    arm64.leaGlobal x1, __data_probe
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.sub x0, x0, x1
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __im_slow:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b trycont
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at dead-global-dropped-beside-a-live-managed-one.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_liveArr@0 = i64 0
  __data_probe@8 = i64 4
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __slab_arena_list@32 = i64 0
  __slab_arena_map_l1@40 = i64 0
  __slab_state@48 = i64 0
  __mrt_program_started@56 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_liveArr
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.cmp x2, 1
    arm64.b.ls __im_slow
    arm64.b __im_load
  tryerr:
    arm64.movImm x0, 0
    arm64.b trycont
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x0, x0, 8
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
  trycont:
    arm64.leaGlobal x1, __data_probe
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.sub x0, x0, x1
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __im_slow:
    arm64.bl __managed_get
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b trycont
  __rc_panic:
    arm64.leaRdata x0, __str_blob_2  ; "panic at dead-global-dropped-beside-a-live-managed-one.test:7: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: write-only-var-kept-beside-a-dead-one -->
⭐ **BOTH DIRECTIONS IN ONE PROGRAM.** `dead` is named nowhere and goes; `writeOnly` is only ever
STORED to and stays, because a store is observable. The pin fails from both sides: too eager and
`.data` is 8 bytes where 16 are claimed, too timid and byte 0 is `dead`'s 1 rather than
`writeOnly`'s 0.

```maxon
var dead = 1
var writeOnly = 0
var live = 5

function main() returns ExitCode
	writeOnly = 99
	return live
end 'main'
```
```exitcode
5
```
```RequiredData
i64 0
i64 5
```

```TargetIr:x64-windows
data {
  __data_writeOnly@0 = i64 0
  __data_live@8 = i64 5
  __mrt_console_probe_stdin@16 = i8 0
  __mrt_console_probe_stdout@17 = i8 0
  __mrt_console_probe_stderr@18 = i8 0
  __mrt_program_started@19 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rax, 99
    x64.leaRegGlobal rcx, __data_writeOnly
    x64.storeBaseDispReg.word64 [rcx + 0], rax
    x64.leaRegGlobal rax, __data_live
    x64.loadRegBaseDisp.word64 r8, [rax + 0]
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
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at write-only-var-kept-beside-a-dead-one.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_writeOnly@0 = i64 0
  __data_live@8 = i64 5
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.movRegImm32 rax, 99
    x64.leaRegGlobal rcx, __data_writeOnly
    x64.storeBaseDispReg.word64 [rcx + 0], rax
    x64.leaRegGlobal rax, __data_live
    x64.loadRegBaseDisp.word64 r8, [rax + 0]
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
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at write-only-var-kept-beside-a-dead-one.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_writeOnly@0 = i64 0
  __data_live@8 = i64 5
  __mrt_program_started@16 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 99
    arm64.leaGlobal x1, __data_writeOnly
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.leaGlobal x0, __data_live
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at write-only-var-kept-beside-a-dead-one.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_writeOnly@0 = i64 0
  __data_live@8 = i64 5
  __mrt_envp@16 = i64 0
  __mrt_signal_stack_bytes@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.movImm x0, 99
    arm64.leaGlobal x1, __data_writeOnly
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.leaGlobal x0, __data_live
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at write-only-var-kept-beside-a-dead-one.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: dead-function-value-global-leaves-no-data-slot -->
A global whose initializer only names a function runs no code before `main`, so a dead one keeps no slot. The dead
global is declared FIRST, for the prefix compare's reason.

```maxon
typealias Integer = int(i64.min to i64.max)

function countdown() returns Integer
	return 3
end 'countdown'

var deadStep = countdown
var live = 7

function main() returns ExitCode
	return live
end 'main'
```
```exitcode
7
```
```RequiredData
i64 7
```

```TargetIr:x64-windows
data {
  __data_live@0 = i64 7
  __mrt_console_probe_stdin@8 = i8 0
  __mrt_console_probe_stdout@9 = i8 0
  __mrt_console_probe_stderr@10 = i8 0
  __mrt_program_started@11 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_live
    x64.loadRegBaseDisp.word64 r8, [rax + 0]
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
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at dead-function-value-global-leaves-no-data-slot.test:12: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_live@0 = i64 7
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __data_live
    x64.loadRegBaseDisp.word64 r8, [rax + 0]
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
    x64.leaRegRdata rcx, [rip + __str_blob_0]  ; "panic at dead-function-value-global-leaves-no-data-slot.test:12: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_live@0 = i64 7
  __mrt_program_started@8 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_live
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at dead-function-value-global-leaves-no-data-slot.test:12: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_live@0 = i64 7
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __data_live
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_0  ; "panic at dead-function-value-global-leaves-no-data-slot.test:12: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```
