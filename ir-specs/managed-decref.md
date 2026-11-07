---
feature: managed-decref
status: stable
keywords: [refcount, decref, release, managed, runtime, hot-path, codegen]
category: memory
---

# The array release: the cases that pin emitted code

Each case pins the Target IR its program compiles to, on every native lane that runs it.

## Tests

<!-- test: the-array-decref-decrements-and-leaves-the-free-to-a-guarded-release -->
Dropping a reference to a buffer record that other owners still hold is a decrement and a return; the last
owner's work, which walks the elements, releases the buffer and frees the record, is `__managed_release`.
The pin holds both bodies: `__managed_decref` has no stack guard and saves no register the decrement does
not use, and `__managed_release` is the function that carries the guard and the element walk. `main` drops
its one reference to the record on its way out, which reaches the last owner, and the record holds a buffer
of its own, so the last owner goes to `__managed_release`. Prints `2`.
```maxon
function main() returns ExitCode
	var bytes = ByteArray.create(0)
	bytes.push(7)
	bytes.push(9)
	let alias = bytes

	print("{alias.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```
```RequiredRuntime
__managed_decref
__managed_release
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 72
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.movRegImm32 rdx, 7
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 9
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot3, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, rbx
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, rbx, r14
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot2
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot1
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__managed_decref {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.loadRegBaseDisp.word64 rax, [rcx + 16]
    x64.cmpRegImm32 rax, -4
    x64.jcc notEqual, countable
  immortal:
    x64.popReg rbp
    x64.ret
  countable:
    x64.leaRegRegImm32 rax, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rax + 8]
    x64.movRegImm rsi, 2305843009213693952
    x64.cmpRegReg rdx, rsi
    x64.jcc greaterEqual, uncounted
  counted:
    x64.loadRegBaseDisp.word64 rdx, [rax + 8]
    x64.leaRegRegImm32 rsi, rdx, -1
    x64.storeBaseDispReg.word64 [rax + 8], rsi
    x64.jmp stepped
  uncounted:
    x64.movRegImm rsi, 4611686018427387904
    x64.cmpRegReg rdx, rsi
    x64.jcc equal, lower
  sharedcount:
    x64.movRegImm rdx, 18446744073709551615
    x64.leaRegRegImm32 rax, rax, 8
    x64.lock xadd [rax], rdx <- rdx
    x64.movRegImm rax, 2305843009213693952
    x64.subRegReg rdx, rdx, rax
  stepped:
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, release
  lower:
    x64.popReg rbp
    x64.ret
  relowned:
    x64.loadRegBaseDisp.word64 rax, [rcx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc greaterEqual, obase
  vbase:
    x64.loadRegBaseDisp.word64 rax, [rcx + 32]
    x64.jmp relbase
  obase:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, relbase
  obufprobe:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 32]
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, relbase
  obaseco:
    x64.movRegImm32 rax, 0
  relbase:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, relfree
  relwalk:
    x64.popReg rbp
    x64.tailJmp __managed_release
  relfree:
    x64.popReg rbp
    x64.tailJmp __mm_free
  release:
    x64.loadRegBaseDisp.word64 rax, [rcx + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, relwalk
    x64.jmp relowned
}

func @__managed_release {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [rbx + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, relbuf
  mwalk:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_destroy_range
  relbuf:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc greaterEqual, obase
  vbase:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 32]
    x64.jmp basejoin
  obase:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, basejoin
  obufprobe:
    x64.loadRegBaseDisp.word64 rax, [rbx + 32]
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, basejoin
  obaseco:
    x64.movRegImm32 rcx, 0
  basejoin:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, freerec
  rcstep:
    x64.callDirect __mm_decref
  freerec:
    x64.movRegReg rcx, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.tailJmp __mm_free
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 72
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.storeSlotReg slot0, rbx
    x64.movRegImm32 rdx, 7
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.movRegImm32 rdx, 9
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.loadRegBaseDisp.word64 rbx, [rbx + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot3, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, rbx
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, rbx, r14
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot2
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot1
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__managed_decref {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.loadRegBaseDisp.word64 rax, [rcx + 16]
    x64.cmpRegImm32 rax, -4
    x64.jcc notEqual, countable
  immortal:
    x64.popReg rbp
    x64.ret
  countable:
    x64.leaRegRegImm32 rax, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rax + 8]
    x64.movRegImm rsi, 2305843009213693952
    x64.cmpRegReg rdx, rsi
    x64.jcc greaterEqual, uncounted
  counted:
    x64.loadRegBaseDisp.word64 rdx, [rax + 8]
    x64.leaRegRegImm32 rsi, rdx, -1
    x64.storeBaseDispReg.word64 [rax + 8], rsi
    x64.jmp stepped
  uncounted:
    x64.movRegImm rsi, 4611686018427387904
    x64.cmpRegReg rdx, rsi
    x64.jcc equal, lower
  sharedcount:
    x64.movRegImm rdx, 18446744073709551615
    x64.leaRegRegImm32 rax, rax, 8
    x64.lock xadd [rax], rdx <- rdx
    x64.movRegImm rax, 2305843009213693952
    x64.subRegReg rdx, rdx, rax
  stepped:
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, release
  lower:
    x64.popReg rbp
    x64.ret
  relowned:
    x64.loadRegBaseDisp.word64 rax, [rcx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc greaterEqual, obase
  vbase:
    x64.loadRegBaseDisp.word64 rax, [rcx + 32]
    x64.jmp relbase
  obase:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, relbase
  obufprobe:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 32]
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, relbase
  obaseco:
    x64.movRegImm32 rax, 0
  relbase:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, relfree
  relwalk:
    x64.popReg rbp
    x64.tailJmp __managed_release
  relfree:
    x64.popReg rbp
    x64.tailJmp __mm_free
  release:
    x64.loadRegBaseDisp.word64 rax, [rcx + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, relwalk
    x64.jmp relowned
}

func @__managed_release {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.movRegImm32 rdx, 0
    x64.loadRegBaseDisp.word64 rax, [rbx + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, relbuf
  mwalk:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_destroy_range
  relbuf:
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc greaterEqual, obase
  vbase:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 32]
    x64.jmp basejoin
  obase:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, basejoin
  obufprobe:
    x64.loadRegBaseDisp.word64 rax, [rbx + 32]
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, basejoin
  obaseco:
    x64.movRegImm32 rcx, 0
  basejoin:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, freerec
  rcstep:
    x64.callDirect __mm_decref
  freerec:
    x64.movRegReg rcx, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.tailJmp __mm_free
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
    arm64.prologue 80
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 7
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 9
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_1  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.add x24, x20, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x20, x26, x20
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x0, x20, x23
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
  __il_body:
    arm64.movRegReg x0, x25
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.epilogue 80
    arm64.ret
}

func @__managed_decref {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.cmp x1, -4
    arm64.b.ne countable
  immortal:
    arm64.epilogue 16
    arm64.ret
  countable:
    arm64.sub x1, x0, 16
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.movImm x3, 2305843009213693952
    arm64.cmp x2, x3
    arm64.b.ge uncounted
  counted:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x3, x2, -1
    arm64.storeBaseDispReg.word64 [x1 + 8], x3
    arm64.b stepped
  uncounted:
    arm64.movImm x3, 4611686018427387904
    arm64.cmp x2, x3
    arm64.b.eq lower
  sharedcount:
    arm64.movImm x2, 18446744073709551615
    arm64.add x1, x1, 8
    arm64.arm64AtomicRmw.add x1, [x1], x2
    arm64.movImm x2, 2305843009213693952
    arm64.sub x1, x1, x2
    arm64.movRegReg x2, x1
  stepped:
    arm64.cmp x2, 0
    arm64.b.eq release
  lower:
    arm64.epilogue 16
    arm64.ret
  relowned:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.cmp x1, 0
    arm64.b.ge obase
  vbase:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 32]
    arm64.b relbase
  obase:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 0
    arm64.b.eq relbase
  obufprobe:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 32]
    arm64.cmp x2, 1
    arm64.b.ne relbase
  obaseco:
    arm64.movImm x1, 0
  relbase:
    arm64.cmp x1, 0
    arm64.b.eq relfree
  relwalk:
    arm64.epilogue 16
    arm64.tailBranch __managed_release
  relfree:
    arm64.epilogue 16
    arm64.tailBranch __mm_free
  release:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 40]
    arm64.cmp x1, 0
    arm64.b.ne relwalk
    arm64.b relowned
}

func @__managed_release {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 40]
    arm64.cmp x0, 0
    arm64.b.eq relbuf
  mwalk:
    arm64.movRegReg x0, x19
    arm64.bl __managed_destroy_range
  relbuf:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, 0
    arm64.b.ge obase
  vbase:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 32]
    arm64.b basejoin
  obase:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq basejoin
  obufprobe:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 32]
    arm64.cmp x1, 1
    arm64.b.ne basejoin
  obaseco:
    arm64.movImm x0, 0
  basejoin:
    arm64.cmp x0, 0
    arm64.b.eq freerec
  rcstep:
    arm64.bl __mm_decref
  freerec:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.tailBranch __mm_free
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
    arm64.prologue 80
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 7
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.movImm x1, 9
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_1  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.add x24, x20, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x20, x26, x20
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x0, x20, x23
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
  __il_body:
    arm64.movRegReg x0, x25
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.epilogue 80
    arm64.ret
}

func @__managed_decref {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.cmp x1, -4
    arm64.b.ne countable
  immortal:
    arm64.epilogue 16
    arm64.ret
  countable:
    arm64.sub x1, x0, 16
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.movImm x3, 2305843009213693952
    arm64.cmp x2, x3
    arm64.b.ge uncounted
  counted:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x3, x2, -1
    arm64.storeBaseDispReg.word64 [x1 + 8], x3
    arm64.b stepped
  uncounted:
    arm64.movImm x3, 4611686018427387904
    arm64.cmp x2, x3
    arm64.b.eq lower
  sharedcount:
    arm64.movImm x2, 18446744073709551615
    arm64.add x1, x1, 8
    arm64.arm64AtomicRmw.add x1, [x1], x2
    arm64.movImm x2, 2305843009213693952
    arm64.sub x1, x1, x2
    arm64.movRegReg x2, x1
  stepped:
    arm64.cmp x2, 0
    arm64.b.eq release
  lower:
    arm64.epilogue 16
    arm64.ret
  relowned:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.cmp x1, 0
    arm64.b.ge obase
  vbase:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 32]
    arm64.b relbase
  obase:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 0
    arm64.b.eq relbase
  obufprobe:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 32]
    arm64.cmp x2, 1
    arm64.b.ne relbase
  obaseco:
    arm64.movImm x1, 0
  relbase:
    arm64.cmp x1, 0
    arm64.b.eq relfree
  relwalk:
    arm64.epilogue 16
    arm64.tailBranch __managed_release
  relfree:
    arm64.epilogue 16
    arm64.tailBranch __mm_free
  release:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 40]
    arm64.cmp x1, 0
    arm64.b.ne relwalk
    arm64.b relowned
}

func @__managed_release {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movImm x1, 0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 40]
    arm64.cmp x0, 0
    arm64.b.eq relbuf
  mwalk:
    arm64.movRegReg x0, x19
    arm64.bl __managed_destroy_range
  relbuf:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, 0
    arm64.b.ge obase
  vbase:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 32]
    arm64.b basejoin
  obase:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.cmp x0, 0
    arm64.b.eq basejoin
  obufprobe:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 32]
    arm64.cmp x1, 1
    arm64.b.ne basejoin
  obaseco:
    arm64.movImm x0, 0
  basejoin:
    arm64.cmp x0, 0
    arm64.b.eq freerec
  rcstep:
    arm64.bl __mm_decref
  freerec:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.tailBranch __mm_free
}
```

<!-- test: the-box-decref-decrements-and-leaves-the-free-to-a-guarded-release -->
Dropping a reference to a plain box that another name still holds is a decrement and a return; the last
owner's work, which runs the box's destructor and frees it, is `__mm_release`. The pin holds both bodies:
`__mm_decref` has no stack guard and saves no register the decrement does not use, and `__mm_release` is the
function that carries the guard. Prints `1`.
```maxon
typealias Hits = int(0 to 100)

type Plain
	export var hits as Hits

	static function create() returns Plain
		return Self{hits: 1}
	end 'create'
end 'Plain'

function main() returns ExitCode
	let first = Plain.create()
	let second = first

	print("{second.hits}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```
```RequiredRuntime
__mm_decref
__mm_release
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 72
  __il_body#4:
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot0, r8
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
  __il_cont#3:
    x64.loadRegBaseDisp.word64 rbx, [r8 + 0]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot3, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, rbx
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, rbx, r14
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot2
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot1
    x64.callDirect __mm_decref
  __il_body#5:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __write_stdout
  __il_cont#2:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
    x64.movRegReg r8, rbx
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__mm_decref {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegRegImm32 rax, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rax + 8]
    x64.movRegImm rsi, 2305843009213693952
    x64.cmpRegReg rdx, rsi
    x64.jcc greaterEqual, uncounted
  counted:
    x64.loadRegBaseDisp.word64 rdx, [rax + 8]
    x64.leaRegRegImm32 rsi, rdx, -1
    x64.storeBaseDispReg.word64 [rax + 8], rsi
    x64.jmp stepped
  uncounted:
    x64.movRegImm rsi, 4611686018427387904
    x64.cmpRegReg rdx, rsi
    x64.jcc equal, lower
  sharedcount:
    x64.movRegImm rdx, 18446744073709551615
    x64.leaRegRegImm32 rsi, rax, 8
    x64.lock xadd [rsi], rdx <- rdx
    x64.movRegImm rsi, 2305843009213693952
    x64.subRegReg rdx, rdx, rsi
  stepped:
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, release
  lower:
    x64.popReg rbp
    x64.ret
  free:
    x64.popReg rbp
    x64.tailJmp __mm_free
  release:
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, free
  destroy:
    x64.popReg rbp
    x64.tailJmp __mm_release
}

func @__mm_release {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.leaRegRegImm32 rax, rbx, -16
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.movRegReg rcx, rbx
    x64.callReg rax
    x64.movRegReg rcx, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.tailJmp __mm_free
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 72
  __il_body#4:
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot0, r8
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
  __il_cont#3:
    x64.loadRegBaseDisp.word64 rbx, [r8 + 0]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot3, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, rbx
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, rbx, r14
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot2
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot1
    x64.callDirect __mm_decref
  __il_body#5:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __write_stdout
  __il_cont#2:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __mm_decref
    x64.movRegReg r8, rbx
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__mm_decref {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.leaRegRegImm32 rax, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rax + 8]
    x64.movRegImm rsi, 2305843009213693952
    x64.cmpRegReg rdx, rsi
    x64.jcc greaterEqual, uncounted
  counted:
    x64.loadRegBaseDisp.word64 rdx, [rax + 8]
    x64.leaRegRegImm32 rsi, rdx, -1
    x64.storeBaseDispReg.word64 [rax + 8], rsi
    x64.jmp stepped
  uncounted:
    x64.movRegImm rsi, 4611686018427387904
    x64.cmpRegReg rdx, rsi
    x64.jcc equal, lower
  sharedcount:
    x64.movRegImm rdx, 18446744073709551615
    x64.leaRegRegImm32 rsi, rax, 8
    x64.lock xadd [rsi], rdx <- rdx
    x64.movRegImm rsi, 2305843009213693952
    x64.subRegReg rdx, rdx, rsi
  stepped:
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, release
  lower:
    x64.popReg rbp
    x64.ret
  free:
    x64.popReg rbp
    x64.tailJmp __mm_free
  release:
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, free
  destroy:
    x64.popReg rbp
    x64.tailJmp __mm_release
}

func @__mm_release {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.leaRegRegImm32 rax, rbx, -16
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.movRegReg rcx, rbx
    x64.callReg rax
    x64.movRegReg rcx, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.tailJmp __mm_free
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
    arm64.prologue 80
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#4:
    arm64.movImm x19, 1
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
  __il_cont#3:
    arm64.loadRegBaseDisp.word64 x19, [x20 + 0]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_1  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.add x24, x19, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x26, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x0, x19, x23
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
  __il_body#5:
    arm64.movRegReg x0, x25
    arm64.bl __write_stdout
  __il_cont#2:
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.epilogue 80
    arm64.ret
}

func @__mm_decref {
  entry:
    arm64.prologue 16
    arm64.sub x1, x0, 16
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.movImm x3, 2305843009213693952
    arm64.cmp x2, x3
    arm64.b.ge uncounted
  counted:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x3, x2, -1
    arm64.storeBaseDispReg.word64 [x1 + 8], x3
    arm64.b stepped
  uncounted:
    arm64.movImm x3, 4611686018427387904
    arm64.cmp x2, x3
    arm64.b.eq lower
  sharedcount:
    arm64.movImm x2, 18446744073709551615
    arm64.add x3, x1, 8
    arm64.arm64AtomicRmw.add x2, [x3], x2
    arm64.movImm x3, 2305843009213693952
    arm64.sub x2, x2, x3
  stepped:
    arm64.cmp x2, 0
    arm64.b.eq release
  lower:
    arm64.epilogue 16
    arm64.ret
  free:
    arm64.epilogue 16
    arm64.tailBranch __mm_free
  release:
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.cmp x1, 0
    arm64.b.eq free
  destroy:
    arm64.epilogue 16
    arm64.tailBranch __mm_release
}

func @__mm_release {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.sub x0, x19, 16
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.movRegReg x0, x19
    arm64.blr x1
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.tailBranch __mm_free
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
    arm64.prologue 80
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body#4:
    arm64.movImm x19, 1
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
  __il_cont#3:
    arm64.loadRegBaseDisp.word64 x19, [x20 + 0]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x19, x0
    arm64.leaRdata x0, __str_rec_1  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.add x24, x19, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x19
    arm64.bl __str_copy
    arm64.add x19, x26, x19
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x0, x19, x23
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
  __il_body#5:
    arm64.movRegReg x0, x25
    arm64.bl __write_stdout
  __il_cont#2:
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.epilogue 80
    arm64.ret
}

func @__mm_decref {
  entry:
    arm64.prologue 16
    arm64.sub x1, x0, 16
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.movImm x3, 2305843009213693952
    arm64.cmp x2, x3
    arm64.b.ge uncounted
  counted:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x3, x2, -1
    arm64.storeBaseDispReg.word64 [x1 + 8], x3
    arm64.b stepped
  uncounted:
    arm64.movImm x3, 4611686018427387904
    arm64.cmp x2, x3
    arm64.b.eq lower
  sharedcount:
    arm64.movImm x2, 18446744073709551615
    arm64.add x3, x1, 8
    arm64.arm64AtomicRmw.add x2, [x3], x2
    arm64.movImm x3, 2305843009213693952
    arm64.sub x2, x2, x3
  stepped:
    arm64.cmp x2, 0
    arm64.b.eq release
  lower:
    arm64.epilogue 16
    arm64.ret
  free:
    arm64.epilogue 16
    arm64.tailBranch __mm_free
  release:
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.cmp x1, 0
    arm64.b.eq free
  destroy:
    arm64.epilogue 16
    arm64.tailBranch __mm_release
}

func @__mm_release {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.sub x0, x19, 16
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.movRegReg x0, x19
    arm64.blr x1
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.tailBranch __mm_free
}
```

<!-- test: the-last-owner-of-a-record-with-no-buffer-of-its-own-is-freed-by-the-decref -->
When the last owner of a buffer record holds trivial elements and no separate buffer, `__managed_decref` frees the
record itself with a tail jump to `__mm_free`: it does not enter `__managed_release`, which carries the stack guard
and the frame the element walk and the buffer release need. `main` creates a record at a constant capacity, so its
bytes are in the record's own allocation, and drops it on its way out. Prints `16`.
```maxon
function main() returns ExitCode
	let bytes = ByteArray.create(16)

	print("{bytes.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
16
```
```RequiredRuntime
__managed_decref
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 72
    x64.movRegImm32 rsi, 16
    x64.movRegImm32 r8, 1
    x64.movRegImm32 r9, 0
    x64.movRegImm32 r10, 0
  cls#6: [async-unsafe]
    x64.leaRegRdata rax, [rip + __slab_size_to_class8]
    x64.loadRegBaseDisp.byte rax, [rax + 12]
  cls#8: [async-unsafe]
    x64.leaRegGlobal rcx, __slab_state
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __ia_slow
  state: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [rcx + 24]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, walk#17
  walk#10: [async-unsafe]
    x64.loadRegBaseIndexScale.word64 r11, [rcx + rax*8 + 139328]
    x64.cmpRegImm32 r11, 0
    x64.jcc equal, __ia_slow
  probe#20: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [r11 + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __ia_slow
  probe#21: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [r11 + 48]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __ia_slow
  probe#19: [async-unsafe]
    x64.aluBaseDispImm32.add [rcx + 152120], 1
    x64.aluBaseDispImm32.add [rcx + 152128], 80
    x64.jmp pop#18
  walk#17: [async-unsafe]
    x64.mov rdx, gs:[rdx]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __ia_slow
  walk#11: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdi, [rdx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, __ia_slow
  walk#14: [async-unsafe]
    x64.loadRegBaseDisp.word64 r11, [rdi + 32]
    x64.cmpRegImm32 r11, 255
    x64.jcc greaterEqual, __ia_slow
  walk#16: [async-unsafe]
    x64.imulRegRegImm32 rbx, r11, 544
    x64.leaRegRegReg rbx, rcx, rbx
    x64.loadRegBaseIndexScale.word64 rbx, [rbx + rax*8 + 608]
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, __ia_slow
  ownprobe#23: [async-unsafe]
    x64.loadRegBaseDisp.word64 r12, [rbx + 16]
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, __ia_slow
  ownprobe#24: [async-unsafe]
    x64.loadRegBaseDisp.word64 r12, [rbx + 48]
    x64.cmpRegReg r12, rdi
    x64.jcc notEqual, __ia_slow
  ownprobe#22: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, ownthread#26
  ownthread#25: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 112]
    x64.aluBaseDispImm32.add [rdx + 512], 1
    x64.aluBaseDispImm32.add [rdx + 528], 96
  ownthread#26: [async-unsafe]
    x64.imulRegRegImm32 rdx, r11, 48
    x64.leaRegRegImm32 rcx, rcx, 139872
    x64.leaRegRegReg rcx, rcx, rdx
    x64.aluBaseDispImm32.add [rcx + 8], 1
    x64.aluBaseDispImm32.add [rcx + 16], 80
    x64.movRegReg r11, rbx
  pop#18: [async-unsafe]
    x64.leaRegRdata rcx, [rip + __slab_class_geom]
    x64.loadRegBaseIndexScale.word32 rcx, [rcx + rax*8 + 0]
    x64.loadRegBaseDisp.word64 rbx, [r11 + 8]
    x64.cmpRegImm32 rbx, 0
    x64.jcc notEqual, pop#27
  pop#28: [async-unsafe]
    x64.loadRegBaseDisp.word64 rax, [r11 + 40]
    x64.loadRegBaseDisp.word64 rdx, [r11 + 32]
    x64.leaRegRdata rdi, [rip + __slab_class_geom]
    x64.loadRegBaseIndexScale.word32 rdx, [rdi + rdx*8 + 4]
    x64.movRegReg rdi, rcx
    x64.imulRegReg rdi, rcx, rdx
    x64.loadRegBaseDisp.word64 rdx, [r11 + 0]
    x64.leaRegRegReg rdx, rdx, rdi
    x64.cmpRegReg rax, rdx
    x64.jcc greaterEqual, pop#30
  pop#29: [async-unsafe]
    x64.leaRegRegReg rcx, rax, rcx
    x64.storeBaseDispReg.word64 [r11 + 40], rcx
    x64.jmp pop#31
  pop#27: [async-unsafe]
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.storeBaseDispReg.word64 [r11 + 8], rax
    x64.movRegReg rdi, rbx
    x64.x64MemFill 0x00
    x64.movRegReg rax, rbx
  pop#31: [async-unsafe]
    x64.loadRegBaseDisp.word64 rcx, [r11 + 16]
    x64.leaRegRegImm32 rcx, rcx, -1
    x64.storeBaseDispReg.word64 [r11 + 16], rcx
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, pop#33
  pop#34: [async-unsafe]
    x64.loadRegBaseDisp.word64 rcx, [r11 + 48]
    x64.loadRegBaseDisp.word64 rdx, [r11 + 32]
    x64.leaRegGlobal rdi, __slab_state
    x64.loadRegBaseDisp.word64 rdi, [rdi + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, pop#35
  pop#36: [async-unsafe]
    x64.leaRegRegImm32 rcx, rdi, 138720
    x64.leaRegRegImm32 rcx, rcx, 608
    x64.leaRegBaseIndexScale rcx, [rcx + rdx*8]
    x64.jmp pop#32
  pop#35: [async-unsafe]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, pop#38
  pop#37: [async-unsafe]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 32]
    x64.imulRegRegImm32 rcx, rcx, 544
    x64.leaRegRegReg rcx, rdi, rcx
    x64.leaRegRegImm32 rcx, rcx, 608
    x64.leaRegBaseIndexScale rcx, [rcx + rdx*8]
  pop#32: [async-unsafe]
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rcx + 0], rdx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [r11 + 48], rcx
  pop#33: [async-unsafe]
    x64.storeBaseDispReg.word64 [rax + 0], r10
    x64.leaRegRegImm32 rax, rax, 16
  __ia_done:
    x64.storeSlotReg slot0, rax
    x64.storeBaseDispReg.word64 [rax + 24], r8
    x64.storeBaseDispReg.word64 [rax + 40], r9
    x64.movRegImm32 rcx, 16
    x64.leaRegRegImm32 rdx, rax, 64
    x64.storeBaseDispReg.word64 [rax + 0], rdx
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 32], rcx
    x64.storeBaseDispReg.word64 [rax + 8], rsi
  __im_cont:
    x64.loadRegBaseDisp.word64 rbx, [rax + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot3, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, rbx
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, rbx, r14
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot2
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot1
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __ia_slow:
    x64.movRegImm32 rcx, 80
    x64.movRegReg rdx, r10
    x64.callDirect __mm_alloc_slow
    x64.movRegReg rax, r8
    x64.movRegImm32 rsi, 16
    x64.movRegImm32 r8, 1
    x64.movRegImm32 r9, 0
    x64.jmp __ia_done
  pop#30: [async-unsafe]
    x64.callDirect __slab_past_end
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  pop#38: [async-unsafe]
    x64.callDirect __slab_evict_unowned
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__managed_decref {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.loadRegBaseDisp.word64 rax, [rcx + 16]
    x64.cmpRegImm32 rax, -4
    x64.jcc notEqual, countable
  immortal:
    x64.popReg rbp
    x64.ret
  countable:
    x64.leaRegRegImm32 rax, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rax + 8]
    x64.movRegImm rsi, 2305843009213693952
    x64.cmpRegReg rdx, rsi
    x64.jcc greaterEqual, uncounted
  counted:
    x64.loadRegBaseDisp.word64 rdx, [rax + 8]
    x64.leaRegRegImm32 rsi, rdx, -1
    x64.storeBaseDispReg.word64 [rax + 8], rsi
    x64.jmp stepped
  uncounted:
    x64.movRegImm rsi, 4611686018427387904
    x64.cmpRegReg rdx, rsi
    x64.jcc equal, lower
  sharedcount:
    x64.movRegImm rdx, 18446744073709551615
    x64.leaRegRegImm32 rax, rax, 8
    x64.lock xadd [rax], rdx <- rdx
    x64.movRegImm rax, 2305843009213693952
    x64.subRegReg rdx, rdx, rax
  stepped:
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, release
  lower:
    x64.popReg rbp
    x64.ret
  relowned:
    x64.loadRegBaseDisp.word64 rax, [rcx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc greaterEqual, obase
  vbase:
    x64.loadRegBaseDisp.word64 rax, [rcx + 32]
    x64.jmp relbase
  obase:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, relbase
  obufprobe:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 32]
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, relbase
  obaseco:
    x64.movRegImm32 rax, 0
  relbase:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, relfree
  relwalk:
    x64.popReg rbp
    x64.tailJmp __managed_release
  relfree:
    x64.popReg rbp
    x64.tailJmp __mm_free
  release:
    x64.loadRegBaseDisp.word64 rax, [rcx + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, relwalk
    x64.jmp relowned
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
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 72
    x64.movRegImm32 rsi, 16
    x64.movRegImm32 r8, 1
    x64.movRegImm32 r9, 0
    x64.movRegImm32 r10, 0
  cls#6: [async-unsafe]
    x64.leaRegRdata rax, [rip + __slab_size_to_class8]
    x64.loadRegBaseDisp.byte rax, [rax + 12]
  cls#8: [async-unsafe]
    x64.leaRegGlobal rcx, __slab_state
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __ia_slow
  state: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [rcx + 24]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, walk#17
  walk#10: [async-unsafe]
    x64.loadRegBaseIndexScale.word64 r11, [rcx + rax*8 + 139328]
    x64.cmpRegImm32 r11, 0
    x64.jcc equal, __ia_slow
  probe#20: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [r11 + 16]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __ia_slow
  probe#21: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [r11 + 48]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, __ia_slow
  probe#19: [async-unsafe]
    x64.aluBaseDispImm32.add [rcx + 152120], 1
    x64.aluBaseDispImm32.add [rcx + 152128], 80
    x64.jmp pop#18
  walk#17: [async-unsafe]
    x64.mov rdx, gs:[rdx]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, __ia_slow
  walk#11: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdi, [rdx + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, __ia_slow
  walk#14: [async-unsafe]
    x64.loadRegBaseDisp.word64 r11, [rdi + 32]
    x64.cmpRegImm32 r11, 255
    x64.jcc greaterEqual, __ia_slow
  walk#16: [async-unsafe]
    x64.imulRegRegImm32 rbx, r11, 544
    x64.leaRegRegReg rbx, rcx, rbx
    x64.loadRegBaseIndexScale.word64 rbx, [rbx + rax*8 + 608]
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, __ia_slow
  ownprobe#23: [async-unsafe]
    x64.loadRegBaseDisp.word64 r12, [rbx + 16]
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, __ia_slow
  ownprobe#24: [async-unsafe]
    x64.loadRegBaseDisp.word64 r12, [rbx + 48]
    x64.cmpRegReg r12, rdi
    x64.jcc notEqual, __ia_slow
  ownprobe#22: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 8]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, ownthread#26
  ownthread#25: [async-unsafe]
    x64.loadRegBaseDisp.word64 rdx, [rdx + 112]
    x64.aluBaseDispImm32.add [rdx + 512], 1
    x64.aluBaseDispImm32.add [rdx + 528], 96
  ownthread#26: [async-unsafe]
    x64.imulRegRegImm32 rdx, r11, 48
    x64.leaRegRegImm32 rcx, rcx, 139872
    x64.leaRegRegReg rcx, rcx, rdx
    x64.aluBaseDispImm32.add [rcx + 8], 1
    x64.aluBaseDispImm32.add [rcx + 16], 80
    x64.movRegReg r11, rbx
  pop#18: [async-unsafe]
    x64.leaRegRdata rcx, [rip + __slab_class_geom]
    x64.loadRegBaseIndexScale.word32 rcx, [rcx + rax*8 + 0]
    x64.loadRegBaseDisp.word64 rbx, [r11 + 8]
    x64.cmpRegImm32 rbx, 0
    x64.jcc notEqual, pop#27
  pop#28: [async-unsafe]
    x64.loadRegBaseDisp.word64 rax, [r11 + 40]
    x64.loadRegBaseDisp.word64 rdx, [r11 + 32]
    x64.leaRegRdata rdi, [rip + __slab_class_geom]
    x64.loadRegBaseIndexScale.word32 rdx, [rdi + rdx*8 + 4]
    x64.movRegReg rdi, rcx
    x64.imulRegReg rdi, rcx, rdx
    x64.loadRegBaseDisp.word64 rdx, [r11 + 0]
    x64.leaRegRegReg rdx, rdx, rdi
    x64.cmpRegReg rax, rdx
    x64.jcc greaterEqual, pop#30
  pop#29: [async-unsafe]
    x64.leaRegRegReg rcx, rax, rcx
    x64.storeBaseDispReg.word64 [r11 + 40], rcx
    x64.jmp pop#31
  pop#27: [async-unsafe]
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.storeBaseDispReg.word64 [r11 + 8], rax
    x64.movRegReg rdi, rbx
    x64.x64MemFill 0x00
    x64.movRegReg rax, rbx
  pop#31: [async-unsafe]
    x64.loadRegBaseDisp.word64 rcx, [r11 + 16]
    x64.leaRegRegImm32 rcx, rcx, -1
    x64.storeBaseDispReg.word64 [r11 + 16], rcx
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, pop#33
  pop#34: [async-unsafe]
    x64.loadRegBaseDisp.word64 rcx, [r11 + 48]
    x64.loadRegBaseDisp.word64 rdx, [r11 + 32]
    x64.leaRegGlobal rdi, __slab_state
    x64.loadRegBaseDisp.word64 rdi, [rdi + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, pop#35
  pop#36: [async-unsafe]
    x64.leaRegRegImm32 rcx, rdi, 138720
    x64.leaRegRegImm32 rcx, rcx, 608
    x64.leaRegBaseIndexScale rcx, [rcx + rdx*8]
    x64.jmp pop#32
  pop#35: [async-unsafe]
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, pop#38
  pop#37: [async-unsafe]
    x64.loadRegBaseDisp.word64 rcx, [rcx + 32]
    x64.imulRegRegImm32 rcx, rcx, 544
    x64.leaRegRegReg rcx, rdi, rcx
    x64.leaRegRegImm32 rcx, rcx, 608
    x64.leaRegBaseIndexScale rcx, [rcx + rdx*8]
  pop#32: [async-unsafe]
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rcx + 0], rdx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [r11 + 48], rcx
  pop#33: [async-unsafe]
    x64.storeBaseDispReg.word64 [rax + 0], r10
    x64.leaRegRegImm32 rax, rax, 16
  __ia_done:
    x64.storeSlotReg slot0, rax
    x64.storeBaseDispReg.word64 [rax + 24], r8
    x64.storeBaseDispReg.word64 [rax + 40], r9
    x64.movRegImm32 rcx, 16
    x64.leaRegRegImm32 rdx, rax, 64
    x64.storeBaseDispReg.word64 [rax + 0], rdx
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 32], rcx
    x64.storeBaseDispReg.word64 [rax + 8], rsi
  __im_cont:
    x64.loadRegBaseDisp.word64 rbx, [rax + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeSlotReg slot1, r12
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __uint_to_string
    x64.movRegReg rbx, r8
    x64.leaRegRdata rax, [rip + __str_rec_1]  ; "\x0a"
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.loadRegBaseDisp.word64 r14, [rax + 8]
    x64.leaRegRegReg rax, rbx, r14
    x64.storeSlotReg slot2, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot3, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, r12
    x64.movRegReg rax, rbx
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, rbx
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r14
    x64.callDirect __str_copy
    x64.leaRegRegReg rax, rbx, r14
    x64.loadRegSlot rax, slot3
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot2
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.loadRegSlot rcx, slot1
    x64.callDirect __mm_decref
  __il_body:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __write_stdout
  __il_cont:
    x64.loadRegSlot rcx, slot3
    x64.callDirect __str_decref
    x64.movRegImm32 rbx, 0
    x64.loadRegSlot rcx, slot0
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __ia_slow:
    x64.movRegImm32 rcx, 80
    x64.movRegReg rdx, r10
    x64.callDirect __mm_alloc_slow
    x64.movRegReg rax, r8
    x64.movRegImm32 rsi, 16
    x64.movRegImm32 r8, 1
    x64.movRegImm32 r9, 0
    x64.jmp __ia_done
  pop#30: [async-unsafe]
    x64.callDirect __slab_past_end
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  pop#38: [async-unsafe]
    x64.callDirect __slab_evict_unowned
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__managed_decref {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.loadRegBaseDisp.word64 rax, [rcx + 16]
    x64.cmpRegImm32 rax, -4
    x64.jcc notEqual, countable
  immortal:
    x64.popReg rbp
    x64.ret
  countable:
    x64.leaRegRegImm32 rax, rcx, -16
    x64.loadRegBaseDisp.word64 rdx, [rax + 8]
    x64.movRegImm rsi, 2305843009213693952
    x64.cmpRegReg rdx, rsi
    x64.jcc greaterEqual, uncounted
  counted:
    x64.loadRegBaseDisp.word64 rdx, [rax + 8]
    x64.leaRegRegImm32 rsi, rdx, -1
    x64.storeBaseDispReg.word64 [rax + 8], rsi
    x64.jmp stepped
  uncounted:
    x64.movRegImm rsi, 4611686018427387904
    x64.cmpRegReg rdx, rsi
    x64.jcc equal, lower
  sharedcount:
    x64.movRegImm rdx, 18446744073709551615
    x64.leaRegRegImm32 rax, rax, 8
    x64.lock xadd [rax], rdx <- rdx
    x64.movRegImm rax, 2305843009213693952
    x64.subRegReg rdx, rdx, rax
  stepped:
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, release
  lower:
    x64.popReg rbp
    x64.ret
  relowned:
    x64.loadRegBaseDisp.word64 rax, [rcx + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc greaterEqual, obase
  vbase:
    x64.loadRegBaseDisp.word64 rax, [rcx + 32]
    x64.jmp relbase
  obase:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, relbase
  obufprobe:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 32]
    x64.cmpRegImm32 rdx, 1
    x64.jcc notEqual, relbase
  obaseco:
    x64.movRegImm32 rax, 0
  relbase:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, relfree
  relwalk:
    x64.popReg rbp
    x64.tailJmp __managed_release
  relfree:
    x64.popReg rbp
    x64.tailJmp __mm_free
  release:
    x64.loadRegBaseDisp.word64 rax, [rcx + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, relwalk
    x64.jmp relowned
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
    arm64.prologue 80
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x4, 16
    arm64.movImm x5, 1
    arm64.movImm x6, 0
    arm64.movImm x7, 0
  cls#6: [async-unsafe]
    arm64.leaRdata x0, __slab_size_to_class8
    arm64.loadRegBaseDisp.byte x0, [x0 + 12]
  cls#8: [async-unsafe]
    arm64.leaGlobal x1, __slab_state
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __ia_slow
  state: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x2, [x1 + 24]
    arm64.cmp x2, 0
    arm64.b.ne walk#17
  walk#10: [async-unsafe]
    arm64.arm64AddLsl x2, x1, x0, lsl #3
    arm64.add x16, x2, 139328
    arm64.loadRegBaseDisp.word64 x8, [x16 + 0]
    arm64.cmp x8, 0
    arm64.b.eq __ia_slow
  probe#20: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x2, [x8 + 16]
    arm64.cmp x2, 0
    arm64.b.eq __ia_slow
  probe#21: [async-unsafe]
    arm64.add x16, x8, 48
    arm64.ldar.word64 x2, [x16]
    arm64.cmp x2, 0
    arm64.b.ne __ia_slow
  probe#19: [async-unsafe]
    arm64.add x1, x1, 152112
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 8], x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 16]
    arm64.add x2, x2, 80
    arm64.storeBaseDispReg.word64 [x1 + 16], x2
    arm64.b pop#18
  walk#17: [async-unsafe]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x2, 0
    arm64.b.eq __ia_slow
  walk#11: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x3, [x2 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __ia_slow
  walk#14: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x8, [x3 + 32]
    arm64.cmp x8, 255
    arm64.b.ge __ia_slow
  walk#16: [async-unsafe]
    arm64.movImm x16, 544
    arm64.mul x9, x8, x16
    arm64.add x9, x1, x9
    arm64.arm64AddLsl x9, x9, x0, lsl #3
    arm64.loadRegBaseDisp.word64 x9, [x9 + 608]
    arm64.cmp x9, 0
    arm64.b.eq __ia_slow
  ownprobe#23: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x10, [x9 + 16]
    arm64.cmp x10, 0
    arm64.b.eq __ia_slow
  ownprobe#24: [async-unsafe]
    arm64.add x16, x9, 48
    arm64.ldar.word64 x10, [x16]
    arm64.cmp x10, x3
    arm64.b.ne __ia_slow
  ownprobe#22: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.eq ownthread#26
  ownthread#25: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 112]
    arm64.loadRegBaseDisp.word64 x3, [x2 + 512]
    arm64.add x3, x3, 1
    arm64.storeBaseDispReg.word64 [x2 + 512], x3
    arm64.loadRegBaseDisp.word64 x3, [x2 + 528]
    arm64.add x3, x3, 96
    arm64.storeBaseDispReg.word64 [x2 + 528], x3
  ownthread#26: [async-unsafe]
    arm64.movImm x16, 48
    arm64.mul x2, x8, x16
    arm64.add x1, x1, 139872
    arm64.add x1, x1, x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 8], x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 16]
    arm64.add x2, x2, 80
    arm64.storeBaseDispReg.word64 [x1 + 16], x2
    arm64.movRegReg x8, x9
  pop#18: [async-unsafe]
    arm64.leaRdata x1, __slab_class_geom
    arm64.arm64AddLsl x0, x1, x0, lsl #3
    arm64.loadRegBaseDisp.word32 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x9, [x8 + 8]
    arm64.cmp x9, 0
    arm64.b.ne pop#27
  pop#28: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x0, [x8 + 40]
    arm64.loadRegBaseDisp.word64 x2, [x8 + 32]
    arm64.leaRdata x3, __slab_class_geom
    arm64.arm64AddLsl x2, x3, x2, lsl #3
    arm64.loadRegBaseDisp.word32 x2, [x2 + 4]
    arm64.mul x2, x1, x2
    arm64.loadRegBaseDisp.word64 x3, [x8 + 0]
    arm64.add x2, x3, x2
    arm64.cmp x0, x2
    arm64.b.ge pop#30
  pop#29: [async-unsafe]
    arm64.add x1, x0, x1
    arm64.storeBaseDispReg.word64 [x8 + 40], x1
    arm64.b pop#31
  pop#27: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x0, [x9 + 0]
    arm64.storeBaseDispReg.word64 [x8 + 8], x0
    arm64.movRegReg x0, x9
    arm64.arm64MemFill 0x00
    arm64.movRegReg x0, x9
  pop#31: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x1, [x8 + 16]
    arm64.sub x1, x1, 1
    arm64.storeBaseDispReg.word64 [x8 + 16], x1
    arm64.cmp x1, 0
    arm64.b.ne pop#33
  pop#34: [async-unsafe]
    arm64.add x16, x8, 48
    arm64.ldar.word64 x1, [x16]
    arm64.loadRegBaseDisp.word64 x2, [x8 + 32]
    arm64.leaGlobal x3, __slab_state
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.cmp x1, 0
    arm64.b.ne pop#35
  pop#36: [async-unsafe]
    arm64.add x1, x3, 138720
    arm64.add x1, x1, 608
    arm64.arm64AddLsl x1, x1, x2, lsl #3
    arm64.b pop#32
  pop#35: [async-unsafe]
    arm64.cmp x1, 0
    arm64.b.lt pop#38
  pop#37: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 32]
    arm64.movImm x16, 544
    arm64.mul x1, x1, x16
    arm64.add x1, x3, x1
    arm64.add x1, x1, 608
    arm64.arm64AddLsl x1, x1, x2, lsl #3
  pop#32: [async-unsafe]
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
    arm64.movImm x1, 18446744073709551613
    arm64.add x16, x8, 48
    arm64.stlr.word64 [x16], x1
  pop#33: [async-unsafe]
    arm64.storeBaseDispReg.word64 [x0 + 0], x7
    arm64.add x19, x0, 16
  __ia_done:
    arm64.storeBaseDispReg.word64 [x19 + 24], x5
    arm64.storeBaseDispReg.word64 [x19 + 40], x6
    arm64.movImm x0, 16
    arm64.add x1, x19, 64
    arm64.storeBaseDispReg.word64 [x19 + 0], x1
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 32], x0
    arm64.storeBaseDispReg.word64 [x19 + 8], x4
  __im_cont:
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_1  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.add x24, x20, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x20, x26, x20
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x0, x20, x23
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
  __il_body:
    arm64.movRegReg x0, x25
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.epilogue 80
    arm64.ret
  __ia_slow:
    arm64.movImm x0, 80
    arm64.movRegReg x1, x7
    arm64.bl __mm_alloc_slow
    arm64.movImm x4, 16
    arm64.movImm x5, 1
    arm64.movImm x6, 0
    arm64.movRegReg x19, x0
    arm64.b __ia_done
  pop#30: [async-unsafe]
    arm64.movRegReg x0, x1
    arm64.bl __slab_past_end
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.epilogue 80
    arm64.ret
  pop#38: [async-unsafe]
    arm64.movRegReg x0, x1
    arm64.bl __slab_evict_unowned
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.epilogue 80
    arm64.ret
}

func @__managed_decref {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.cmp x1, -4
    arm64.b.ne countable
  immortal:
    arm64.epilogue 16
    arm64.ret
  countable:
    arm64.sub x1, x0, 16
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.movImm x3, 2305843009213693952
    arm64.cmp x2, x3
    arm64.b.ge uncounted
  counted:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x3, x2, -1
    arm64.storeBaseDispReg.word64 [x1 + 8], x3
    arm64.b stepped
  uncounted:
    arm64.movImm x3, 4611686018427387904
    arm64.cmp x2, x3
    arm64.b.eq lower
  sharedcount:
    arm64.movImm x2, 18446744073709551615
    arm64.add x1, x1, 8
    arm64.arm64AtomicRmw.add x1, [x1], x2
    arm64.movImm x2, 2305843009213693952
    arm64.sub x1, x1, x2
    arm64.movRegReg x2, x1
  stepped:
    arm64.cmp x2, 0
    arm64.b.eq release
  lower:
    arm64.epilogue 16
    arm64.ret
  relowned:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.cmp x1, 0
    arm64.b.ge obase
  vbase:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 32]
    arm64.b relbase
  obase:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 0
    arm64.b.eq relbase
  obufprobe:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 32]
    arm64.cmp x2, 1
    arm64.b.ne relbase
  obaseco:
    arm64.movImm x1, 0
  relbase:
    arm64.cmp x1, 0
    arm64.b.eq relfree
  relwalk:
    arm64.epilogue 16
    arm64.tailBranch __managed_release
  relfree:
    arm64.epilogue 16
    arm64.tailBranch __mm_free
  release:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 40]
    arm64.cmp x1, 0
    arm64.b.ne relwalk
    arm64.b relowned
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
    arm64.prologue 80
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x4, 16
    arm64.movImm x5, 1
    arm64.movImm x6, 0
    arm64.movImm x7, 0
  cls#6: [async-unsafe]
    arm64.leaRdata x0, __slab_size_to_class8
    arm64.loadRegBaseDisp.byte x0, [x0 + 12]
  cls#8: [async-unsafe]
    arm64.leaGlobal x1, __slab_state
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.cmp x1, 0
    arm64.b.eq __ia_slow
  state: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x2, [x1 + 24]
    arm64.cmp x2, 0
    arm64.b.ne walk#17
  walk#10: [async-unsafe]
    arm64.arm64AddLsl x2, x1, x0, lsl #3
    arm64.add x16, x2, 139328
    arm64.loadRegBaseDisp.word64 x8, [x16 + 0]
    arm64.cmp x8, 0
    arm64.b.eq __ia_slow
  probe#20: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x2, [x8 + 16]
    arm64.cmp x2, 0
    arm64.b.eq __ia_slow
  probe#21: [async-unsafe]
    arm64.add x16, x8, 48
    arm64.ldar.word64 x2, [x16]
    arm64.cmp x2, 0
    arm64.b.ne __ia_slow
  probe#19: [async-unsafe]
    arm64.add x1, x1, 152112
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 8], x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 16]
    arm64.add x2, x2, 80
    arm64.storeBaseDispReg.word64 [x1 + 16], x2
    arm64.b pop#18
  walk#17: [async-unsafe]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x2, 0
    arm64.b.eq __ia_slow
  walk#11: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x3, [x2 + 0]
    arm64.cmp x3, 0
    arm64.b.eq __ia_slow
  walk#14: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x8, [x3 + 32]
    arm64.cmp x8, 255
    arm64.b.ge __ia_slow
  walk#16: [async-unsafe]
    arm64.movImm x16, 544
    arm64.mul x9, x8, x16
    arm64.add x9, x1, x9
    arm64.arm64AddLsl x9, x9, x0, lsl #3
    arm64.loadRegBaseDisp.word64 x9, [x9 + 608]
    arm64.cmp x9, 0
    arm64.b.eq __ia_slow
  ownprobe#23: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x10, [x9 + 16]
    arm64.cmp x10, 0
    arm64.b.eq __ia_slow
  ownprobe#24: [async-unsafe]
    arm64.add x16, x9, 48
    arm64.ldar.word64 x10, [x16]
    arm64.cmp x10, x3
    arm64.b.ne __ia_slow
  ownprobe#22: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 8]
    arm64.cmp x2, 0
    arm64.b.eq ownthread#26
  ownthread#25: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x2, [x2 + 112]
    arm64.loadRegBaseDisp.word64 x3, [x2 + 512]
    arm64.add x3, x3, 1
    arm64.storeBaseDispReg.word64 [x2 + 512], x3
    arm64.loadRegBaseDisp.word64 x3, [x2 + 528]
    arm64.add x3, x3, 96
    arm64.storeBaseDispReg.word64 [x2 + 528], x3
  ownthread#26: [async-unsafe]
    arm64.movImm x16, 48
    arm64.mul x2, x8, x16
    arm64.add x1, x1, 139872
    arm64.add x1, x1, x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 8], x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 16]
    arm64.add x2, x2, 80
    arm64.storeBaseDispReg.word64 [x1 + 16], x2
    arm64.movRegReg x8, x9
  pop#18: [async-unsafe]
    arm64.leaRdata x1, __slab_class_geom
    arm64.arm64AddLsl x0, x1, x0, lsl #3
    arm64.loadRegBaseDisp.word32 x1, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x9, [x8 + 8]
    arm64.cmp x9, 0
    arm64.b.ne pop#27
  pop#28: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x0, [x8 + 40]
    arm64.loadRegBaseDisp.word64 x2, [x8 + 32]
    arm64.leaRdata x3, __slab_class_geom
    arm64.arm64AddLsl x2, x3, x2, lsl #3
    arm64.loadRegBaseDisp.word32 x2, [x2 + 4]
    arm64.mul x2, x1, x2
    arm64.loadRegBaseDisp.word64 x3, [x8 + 0]
    arm64.add x2, x3, x2
    arm64.cmp x0, x2
    arm64.b.ge pop#30
  pop#29: [async-unsafe]
    arm64.add x1, x0, x1
    arm64.storeBaseDispReg.word64 [x8 + 40], x1
    arm64.b pop#31
  pop#27: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x0, [x9 + 0]
    arm64.storeBaseDispReg.word64 [x8 + 8], x0
    arm64.movRegReg x0, x9
    arm64.arm64MemFill 0x00
    arm64.movRegReg x0, x9
  pop#31: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x1, [x8 + 16]
    arm64.sub x1, x1, 1
    arm64.storeBaseDispReg.word64 [x8 + 16], x1
    arm64.cmp x1, 0
    arm64.b.ne pop#33
  pop#34: [async-unsafe]
    arm64.add x16, x8, 48
    arm64.ldar.word64 x1, [x16]
    arm64.loadRegBaseDisp.word64 x2, [x8 + 32]
    arm64.leaGlobal x3, __slab_state
    arm64.loadRegBaseDisp.word64 x3, [x3 + 0]
    arm64.cmp x1, 0
    arm64.b.ne pop#35
  pop#36: [async-unsafe]
    arm64.add x1, x3, 138720
    arm64.add x1, x1, 608
    arm64.arm64AddLsl x1, x1, x2, lsl #3
    arm64.b pop#32
  pop#35: [async-unsafe]
    arm64.cmp x1, 0
    arm64.b.lt pop#38
  pop#37: [async-unsafe]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 32]
    arm64.movImm x16, 544
    arm64.mul x1, x1, x16
    arm64.add x1, x3, x1
    arm64.add x1, x1, 608
    arm64.arm64AddLsl x1, x1, x2, lsl #3
  pop#32: [async-unsafe]
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
    arm64.movImm x1, 18446744073709551613
    arm64.add x16, x8, 48
    arm64.stlr.word64 [x16], x1
  pop#33: [async-unsafe]
    arm64.storeBaseDispReg.word64 [x0 + 0], x7
    arm64.add x19, x0, 16
  __ia_done:
    arm64.storeBaseDispReg.word64 [x19 + 24], x5
    arm64.storeBaseDispReg.word64 [x19 + 40], x6
    arm64.movImm x0, 16
    arm64.add x1, x19, 64
    arm64.storeBaseDispReg.word64 [x19 + 0], x1
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x19 + 32], x0
    arm64.storeBaseDispReg.word64 [x19 + 8], x4
  __im_cont:
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x21
    arm64.bl __uint_to_string
    arm64.movRegReg x20, x0
    arm64.leaRdata x0, __str_rec_1  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x22, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x0 + 8]
    arm64.add x24, x20, x23
    arm64.add x0, x24, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x25, x0
    arm64.add x26, x25, 56
    arm64.movRegReg x0, x26
    arm64.movRegReg x1, x21
    arm64.movRegReg x2, x20
    arm64.bl __str_copy
    arm64.add x20, x26, x20
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x23
    arm64.bl __str_copy
    arm64.add x0, x20, x23
    arm64.storeBaseDispReg.word64 [x25 + 0], x26
    arm64.storeBaseDispReg.word64 [x25 + 8], x24
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x25 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x25 + 24], x0
    arm64.movRegReg x0, x21
    arm64.bl __mm_decref
  __il_body:
    arm64.movRegReg x0, x25
    arm64.bl __write_stdout
  __il_cont:
    arm64.movRegReg x0, x25
    arm64.bl __str_decref
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.epilogue 80
    arm64.ret
  __ia_slow:
    arm64.movImm x0, 80
    arm64.movRegReg x1, x7
    arm64.bl __mm_alloc_slow
    arm64.movImm x4, 16
    arm64.movImm x5, 1
    arm64.movImm x6, 0
    arm64.movRegReg x19, x0
    arm64.b __ia_done
  pop#30: [async-unsafe]
    arm64.movRegReg x0, x1
    arm64.bl __slab_past_end
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.epilogue 80
    arm64.ret
  pop#38: [async-unsafe]
    arm64.movRegReg x0, x1
    arm64.bl __slab_evict_unowned
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.epilogue 80
    arm64.ret
}

func @__managed_decref {
  entry:
    arm64.prologue 16
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.cmp x1, -4
    arm64.b.ne countable
  immortal:
    arm64.epilogue 16
    arm64.ret
  countable:
    arm64.sub x1, x0, 16
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.movImm x3, 2305843009213693952
    arm64.cmp x2, x3
    arm64.b.ge uncounted
  counted:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x3, x2, -1
    arm64.storeBaseDispReg.word64 [x1 + 8], x3
    arm64.b stepped
  uncounted:
    arm64.movImm x3, 4611686018427387904
    arm64.cmp x2, x3
    arm64.b.eq lower
  sharedcount:
    arm64.movImm x2, 18446744073709551615
    arm64.add x1, x1, 8
    arm64.arm64AtomicRmw.add x1, [x1], x2
    arm64.movImm x2, 2305843009213693952
    arm64.sub x1, x1, x2
    arm64.movRegReg x2, x1
  stepped:
    arm64.cmp x2, 0
    arm64.b.eq release
  lower:
    arm64.epilogue 16
    arm64.ret
  relowned:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.cmp x1, 0
    arm64.b.ge obase
  vbase:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 32]
    arm64.b relbase
  obase:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 0
    arm64.b.eq relbase
  obufprobe:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 32]
    arm64.cmp x2, 1
    arm64.b.ne relbase
  obaseco:
    arm64.movImm x1, 0
  relbase:
    arm64.cmp x1, 0
    arm64.b.eq relfree
  relwalk:
    arm64.epilogue 16
    arm64.tailBranch __managed_release
  relfree:
    arm64.epilogue 16
    arm64.tailBranch __mm_free
  release:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 40]
    arm64.cmp x1, 0
    arm64.b.ne relwalk
    arm64.b relowned
}
```
